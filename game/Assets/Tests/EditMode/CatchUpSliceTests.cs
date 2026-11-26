using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Saves;
using SpaceTrade.Sim.Timing;
using static SpaceTrade.Sim.Tests.RouteTestSupport;
using static SpaceTrade.Sim.Tests.SaveTestSupport;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>A monotonic clock that moves on by a fixed step every time it is read, like real time passing while work is done.</summary>
    internal sealed class SteppingRealTimeSource : IRealTimeSource
    {
        private long _now;

        public long StepMicroseconds { get; set; }

        public SteppingRealTimeSource(long stepMicroseconds) { StepMicroseconds = stepMicroseconds; }

        public long NowMicroseconds => _now += StepMicroseconds;
    }

    /// <summary>
    /// Spec 11.3: a catch-up can run in slices through the same Advance, with a progress figure, and ends in exactly the
    /// same state as one big step.
    /// </summary>
    public class CatchUpSliceTests
    {
        private const long Away = 2 * Hour;

        private TempSaveFolder _folder;

        [SetUp] public void SetUp() => _folder = new TempSaveFolder();
        [TearDown] public void TearDown() => _folder.Dispose();

        /// <summary>The five-ship route fleet after a game year, at 5x, saved to slot "a" at T0 and copied to "b".</summary>
        private Simulation SavedFleet()
        {
            var sim = RouteFleet(runTicks: Year);
            Ok(sim.SetSpeed(ClockSpeed.X5));
            SaveAt(_folder, "a", sim, T0);
            SaveAt(_folder, "b", sim, T0);
            return sim;
        }

        private static string SummaryText(ReturnSummary s) =>
            $"{s.FromUtcMs} {s.ToUtcMs} {s.RealMillisecondsAway} {s.ClockWentBackward} {s.WasPaused} {s.FromTick} {s.ToTick} {s.Delivered.DescribeState()} " +
            $"listed={string.Join("|", s.Deliveries.Select(d => d.DescribeState()))} {s.InSystemIncome} {s.SuppliesBought} {s.NetCreditChange} " +
            $"attention={string.Join("|", s.ShipsNeedingAttention.Select(a => a.ShipId + ":" + a.Reason))}";

        [TestCase(CatchUpRun.SliceTicks, TestName = "Slices_OfTenGameDays_EndByteIdenticalToOneBigStep")]
        [TestCase(7_919L, TestName = "Slices_Of7919Ticks_EndByteIdenticalToOneBigStep")]
        [TestCase(0L, TestName = "Slices_OfRandomSizes_EndByteIdenticalToOneBigStep")]
        public void SlicesEndLikeOneBigStep(long slice)
        {
            SavedFleet();
            var one = Load(_folder, "a");
            var sliced = Load(_folder, "b");

            var oneSummary = OfflineCatchUp.Run(one, T0, T0 + Away);

            var run = OfflineCatchUp.Begin(sliced, T0, T0 + Away);
            Assert.AreEqual(5 * Away, run.TicksTotal);
            Assert.AreEqual(0, run.ProgressPerMille);
            var random = new SeededRandom(99);
            int steps = 0, last = 0;
            while (!run.IsDone)
            {
                run.Step(slice > 0 ? slice : random.Range(1, 3 * Day));
                steps++;
                Assert.GreaterOrEqual(run.ProgressPerMille, last, "Progress never goes back.");
                if (!run.IsDone) Assert.Less(run.ProgressPerMille, 1000, "1,000 only when done.");
                last = run.ProgressPerMille;
            }
            Assert.AreEqual(1000, run.ProgressPerMille);
            Assert.AreEqual(run.TicksTotal, run.TicksDone);
            var slicedSummary = run.Finish();

            Assert.Greater(steps, 100, "Setup: many slices.");
            Assert.Greater(oneSummary.DeliveryCount, 3, "Setup: deliveries while away.");
            Assert.AreEqual(State(one), State(sliced));
            CollectionAssert.AreEqual(StateBytes(one), StateBytes(sliced));
            Assert.AreEqual(SummaryText(oneSummary), SummaryText(slicedSummary));
            Ok(_folder.Store.Write("a", one, T0 + Away));
            Ok(_folder.Store.Write("b", sliced, T0 + Away));
            CollectionAssert.AreEqual(Bytes(_folder, "a.json"), Bytes(_folder, "b.json"), "The two save files are byte-identical.");
            TestContext.WriteLine($"{steps} slices; {oneSummary.DeliveryCount} deliveries; net {oneSummary.NetCreditChange}.");
        }

        [Test]
        public void BeginOpen_CatchesUpOverManyFrames_WithProgress_RefusesCommands_AndEndsLikeOpen()
        {
            SavedFleet();
            var whole = Open(_folder, "a", T0 + Away);

            var mono = new SteppingRealTimeSource(30_000);
            var utc = new FakeUtcClock(T0 + Away);
            var r = GameSession.BeginOpen(_folder.Store, "b", FlatTuning(), mono, utc);
            var session = r.Session;
            Assert.IsNotNull(session, string.Join(" ", r.Messages));
            Assert.IsNull(r.Summary, "The summary comes when the catch-up ends.");
            Assert.IsTrue(session.IsCatchingUp);
            Assert.AreEqual(0, session.CatchUp.ProgressPerMille);
            Assert.AreEqual(5 * Away, session.CatchUp.TicksTotal);

            ReturnSummary summary = null;
            var progress = new List<int>();
            while (summary == null)
            {
                summary = session.Pump();
                if (summary != null) break;
                progress.Add(session.CatchUp.ProgressPerMille);
                Assert.AreEqual(0, session.SavesWritten, "Nothing is saved until the catch-up ends.");
                if (progress.Count == 50)
                {
                    string state = State(session.Simulation);
                    var refused = session.Command(s => s.SetInSystemTransport(1, true));
                    Assert.IsFalse(refused.Succeeded);
                    StringAssert.StartsWith("The game is still catching up on the time it was away (", refused.Reason);
                    Assert.IsFalse(session.Pause().Succeeded);
                    Assert.IsFalse(session.SetSpeed(ClockSpeed.X1).Succeeded);
                    Assert.AreEqual(state, State(session.Simulation), "A refused command changes nothing.");
                }
            }

            Assert.Greater(progress.Count, 100, "Setup: the catch-up took many frames.");
            for (int i = 1; i < progress.Count; i++) Assert.GreaterOrEqual(progress[i], progress[i - 1]);
            Assert.Less(progress.Last(), 1000);
            Assert.IsFalse(session.IsCatchingUp);
            Assert.AreSame(summary, session.LastSummary);
            Assert.AreEqual(1, session.SavesWritten, "One save when it ends.");
            Assert.AreEqual(State(whole.Session.Simulation), State(session.Simulation));
            Assert.AreEqual(SummaryText(whole.Summary), SummaryText(summary));
            CollectionAssert.AreEqual(Bytes(_folder, "a.json"), Bytes(_folder, "b.json"), "Same save file as opening in one go.");
            Assert.IsTrue(session.Command(s => s.SetInSystemTransport(1, false)).Succeeded, "Commands work once it ends.");
            TestContext.WriteLine($"{progress.Count + 1} frames; progress after frame 1: {progress[0]} per mille, frame {progress.Count / 2}: {progress[progress.Count / 2]}, last before the end: {progress.Last()}.");
        }

        [Test]
        public void RealTimeSpentCatchingUp_IsCounted_AndEndsLikeOneStepToTheLaterTime()
        {
            SavedFleet();
            var mono = new SteppingRealTimeSource(30_000);
            var utc = new FakeUtcClock(T0 + Away);
            var session = GameSession.BeginOpen(_folder.Store, "b", FlatTuning(), mono, utc).Session;
            ReturnSummary summary = null;
            int frames = 0;
            while (summary == null)
            {
                utc.AdvanceMilliseconds(40);
                summary = session.Pump();
                frames++;
            }
            long end = T0 + Away + 40L * frames;
            Assert.AreEqual(end, summary.ToUtcMs);
            Assert.AreEqual(5 * (Away + 40L * frames), summary.GameTicksPassed, "The time spent catching up is in the catch-up too.");
            Assert.AreEqual(end, SavedUtcMs(_folder, "b"));

            var whole = Open(_folder, "a", end);
            Assert.AreEqual(State(whole.Session.Simulation), State(session.Simulation), "Same as one step to the later time.");
            CollectionAssert.AreEqual(Bytes(_folder, "a.json"), Bytes(_folder, "b.json"));

            long tick = session.Simulation.CurrentTick;
            mono.StepMicroseconds = 0;
            utc.AdvanceMilliseconds(1_000);
            session.Pump();
            long advanced = session.Simulation.CurrentTick - tick;
            Assert.Greater(frames * 30_000L / 1_000, 5_000, "Setup: the catch-up took more than 5 real seconds on the frame clock.");
            Assert.Less(advanced, 5 * 1_000, "The next frame counts only the moments after the catch-up last counted, not the seconds spent catching up.");
            TestContext.WriteLine($"{frames} frames; {40L * frames} real ms counted while catching up; the next frame advanced {advanced} ticks.");
        }

        [Test]
        public void Quit_DuringACatchUp_FinishesItFirst_ThenSaves()
        {
            SavedFleet();
            var whole = Open(_folder, "a", T0 + Away);
            var mono = new SteppingRealTimeSource(30_000);
            var utc = new FakeUtcClock(T0 + Away);
            var session = GameSession.BeginOpen(_folder.Store, "b", FlatTuning(), mono, utc).Session;
            for (int i = 0; i < 10; i++) Assert.IsNull(session.Pump());
            Assert.IsTrue(session.IsCatchingUp);
            Assert.Less(session.CatchUp.ProgressPerMille, 100);

            mono.StepMicroseconds = 0;
            Assert.IsTrue(session.Quit().Succeeded);
            Assert.IsFalse(session.IsCatchingUp);
            Assert.IsNotNull(session.LastSummary);
            Assert.AreEqual(State(whole.Session.Simulation), State(session.Simulation));
            CollectionAssert.AreEqual(Bytes(_folder, "a.json"), Bytes(_folder, "b.json"));
        }

        [Test]
        public void Wake_CatchUpRunsInSlices_AndEndsLikeTheClosedGameCatchUp()
        {
            var sim = SavedFleet();
            var mono = new SteppingRealTimeSource(0);
            var utc = new FakeUtcClock(T0);
            var sleeping = GameSession.BeginOpen(_folder.Store, "b", FlatTuning(), mono, utc).Session;
            Assert.IsNotNull(sleeping.Pump(), "Opening at the saved moment ends at once.");

            utc.AdvanceMilliseconds(Away);
            mono.StepMicroseconds = 30_000;
            int frames = 0;
            ReturnSummary wake = null;
            while (wake == null) { wake = sleeping.Pump(); frames++; }
            Assert.Greater(frames, 100);
            Assert.AreEqual(Away, wake.RealMillisecondsAway);

            var closed = Open(_folder, "a", T0 + Away);
            Assert.AreEqual(State(closed.Session.Simulation), State(sleeping.Simulation));
            Assert.AreEqual(SummaryText(closed.Summary), SummaryText(wake));
            CollectionAssert.AreEqual(Bytes(_folder, "a.json"), Bytes(_folder, "b.json"));
        }

        [TestCase(true, TestName = "PausedSave_CatchUpEndsOnTheFirstFrame_AdvancingNothing")]
        [TestCase(false, TestName = "BackwardClock_CatchUpEndsOnTheFirstFrame_AdvancingNothing")]
        public void NothingToCatchUp(bool paused)
        {
            var sim = RouteFleet(runTicks: Year);
            if (paused) Ok(sim.Pause());
            SaveAt(_folder, "a", sim, T0);
            var mono = new SteppingRealTimeSource(30_000);
            var utc = new FakeUtcClock(paused ? T0 + RealDay : T0 - Hour);
            var session = GameSession.BeginOpen(_folder.Store, "a", FlatTuning(), mono, utc).Session;
            Assert.AreEqual(0, session.CatchUp.TicksTotal);
            Assert.AreEqual(1000, session.CatchUp.ProgressPerMille);
            var summary = session.Pump();
            Assert.IsNotNull(summary);
            Assert.AreEqual(0, summary.GameTicksPassed);
            Assert.AreEqual(State(sim), State(session.Simulation));
            Assert.AreEqual(utc.NowUtcMilliseconds, SavedUtcMs(_folder, "a"));
        }
    }
}
