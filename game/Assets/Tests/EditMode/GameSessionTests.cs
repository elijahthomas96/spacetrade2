using System.IO;
using NUnit.Framework;
using SpaceTrade.Sim.Saves;
using SpaceTrade.Sim.Timing;
using static SpaceTrade.Sim.Tests.RouteTestSupport;
using static SpaceTrade.Sim.Tests.SaveTestSupport;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>Spec 3.2, 11.2, 11.3: a running game's autosaves, waking from sleep, and save slots.</summary>
    public class GameSessionTests
    {
        private TempSaveFolder _folder;
        private FakeRealTimeSource _mono;
        private FakeUtcClock _utc;

        [SetUp]
        public void SetUp()
        {
            _folder = new TempSaveFolder();
            _mono = new FakeRealTimeSource();
            _utc = new FakeUtcClock(T0);
        }

        [TearDown] public void TearDown() => _folder.Dispose();

        private GameSession Start(Simulation sim, string slot = "a")
        {
            var r = GameSession.Start(_folder.Store, slot, sim, _mono, _utc);
            Assert.IsNotNull(r.Session, string.Join(" ", r.Messages));
            return r.Session;
        }

        /// <summary>One ordinary frame: both clocks move together.</summary>
        private ReturnSummary Frame(GameSession session, long ms)
        {
            _mono.AdvanceMilliseconds(ms);
            _utc.AdvanceMilliseconds(ms);
            return session.Pump();
        }

        // ---- Autosave ----

        [Test]
        public void Autosave_EverySixtyRealSeconds_NotBefore()
        {
            var session = Start(NewSim(1_000));
            Assert.AreEqual(1, session.SavesWritten, "Starting a game saves it at once.");
            for (int i = 0; i < 59; i++) Frame(session, Second);
            Frame(session, Second - 1);
            Assert.AreEqual(1, session.SavesWritten, "No autosave at 59.999 seconds.");
            Frame(session, 1);
            Assert.AreEqual(2, session.SavesWritten, "Autosave at 60 seconds.");
            Assert.AreEqual(T0 + Minute, SavedUtcMs(_folder, "a"));
            for (int i = 0; i < 60; i++) Frame(session, Second);
            Assert.AreEqual(3, session.SavesWritten);
        }

        [Test]
        public void Autosave_OnEveryDispatch_AndEveryArrival()
        {
            var sim = NewSim(1_000_000);
            var session = Start(sim);
            Assert.IsTrue(session.Command(s => s.Dispatch(Plan(20, 2), buyTopUp: true)).Succeeded);
            Assert.AreEqual(2, session.SavesWritten, "The dispatch is saved at once.");
            Assert.IsTrue(Load(_folder, "a").FindShip(CourierId).IsInTransit);

            Assert.IsFalse(session.Command(s => s.Dispatch(Plan(20, 2), buyTopUp: true)).Succeeded);
            Assert.AreEqual(2, session.SavesWritten, "A refused command changes nothing and saves nothing.");

            long arrival = Courier(sim).Voyage.ArrivalTick;
            _mono.AdvanceMicroseconds((arrival - sim.CurrentTick) * GameTime.RealMicrosecondsPerTickAt1x - 1);
            session.Pump();
            Assert.IsTrue(Courier(sim).IsInTransit);
            Assert.AreEqual(3, session.SavesWritten, "Only the 60-second autosave so far.");
            _mono.AdvanceMicroseconds(1);
            session.Pump();
            Assert.IsFalse(Courier(sim).IsInTransit);
            Assert.AreEqual(4, session.SavesWritten, "The arrival is saved at once.");
            Assert.AreEqual(1, Load(_folder, "a").Receipts.Count);
        }

        [Test]
        public void Quit_Saves_AndCountsTimeUpToTheQuit()
        {
            var sim = NewSim(1_000);
            var session = Start(sim);
            Frame(session, 10 * Second);
            _mono.AdvanceMilliseconds(500);
            _utc.AdvanceMilliseconds(500);
            Assert.IsTrue(session.Quit().Succeeded);
            Assert.AreEqual(10_500, sim.CurrentTick);
            Assert.AreEqual(T0 + 10_500, SavedUtcMs(_folder, "a"));
            Assert.AreEqual(10_500, Load(_folder, "a").CurrentTick);
        }

        [Test]
        public void SaveAs_WritesANewSlotThatBecomesCurrent_AndTheOldSlotIsLeftAsItWas()
        {
            var session = Start(NewSim(1_000));
            byte[] first = Bytes(_folder, "a.json");
            Frame(session, 5 * Second);
            Assert.IsTrue(session.SaveAs("b").Succeeded);
            Assert.AreEqual("b", session.Slot);
            CollectionAssert.AreEqual(first, Bytes(_folder, "a.json"));
            Frame(session, 60 * Second);
            Assert.AreEqual(T0 + 65 * Second, SavedUtcMs(_folder, "b"), "Autosaves now go to the new slot.");
            CollectionAssert.AreEqual(first, Bytes(_folder, "a.json"));

            var bad = session.SaveAs("no/slashes");
            Assert.IsFalse(bad.Succeeded);
            Assert.AreEqual("b", session.Slot);
            Directory.CreateDirectory(_folder.File("c.json.tmp"));
            Assert.IsFalse(session.SaveAs("c").Succeeded);
            Assert.AreEqual("b", session.Slot, "A failed Save as keeps the current slot.");
        }

        [Test]
        public void StartingANewGame_UnderAnExistingName_IsRefused_AndTheSaveIsUntouched()
        {
            SaveAt(_folder, "a", NewSim(5));
            byte[] before = Bytes(_folder, "a.json");
            var r = GameSession.Start(_folder.Store, "a", NewSim(1), _mono, _utc);
            Assert.IsNull(r.Session);
            Assert.AreEqual("A save named \"a\" already exists. Choose another name.", r.Messages[0]);
            CollectionAssert.AreEqual(before, Bytes(_folder, "a.json"));
        }

        [Test]
        public void SaveWarning_StaysThroughFailedSaves_AndClearsOnTheNextSuccess()
        {
            var session = Start(NewSim(1_000));
            Directory.CreateDirectory(_folder.File("a.json.tmp"));
            Frame(session, 60 * Second);
            Assert.IsNotNull(session.SaveWarning);
            Frame(session, Second);
            Assert.IsNotNull(session.SaveWarning, "Frames without a save leave the warning showing.");
            Frame(session, 60 * Second);
            Assert.IsNotNull(session.SaveWarning);
            Directory.Delete(_folder.File("a.json.tmp"));
            Frame(session, 60 * Second);
            Assert.IsNull(session.SaveWarning);
        }

        // ---- Sleep ----

        [Test]
        public void ShortGap_IsAnOrdinaryFrame_NotAWake()
        {
            var sim = NewSim(1_000);
            Ok(sim.SetSpeed(ClockSpeed.X5));
            var session = Start(sim);
            Assert.IsNull(Frame(session, OfflineCatchUp.WakeGapRealMs - 1));
            Assert.AreEqual(5 * (OfflineCatchUp.WakeGapRealMs - 1), sim.CurrentTick);
            Assert.AreEqual(1, session.SavesWritten, "No save for an ordinary frame.");
            Assert.IsNull(session.LastSummary);
        }

        [TestCase(true, TestName = "Wake_MonotonicClockStoppedDuringSleep_GapCountedOnceFromTheRealClock")]
        [TestCase(false, TestName = "Wake_MonotonicClockKeptRunningDuringSleep_GapStillCountedOnlyOnce")]
        public void Wake(bool monotonicStopped)
        {
            var sim = NewSim(1_000_000);
            Ok(sim.SetSpeed(ClockSpeed.X5));
            Ok(sim.Dispatch(Plan(20, 2), buyTopUp: true));
            var session = Start(sim);
            Frame(session, 3 * Second);
            long tick = sim.CurrentTick;

            long gap = 2 * Hour;
            _utc.AdvanceMilliseconds(gap);
            if (!monotonicStopped) _mono.AdvanceMilliseconds(gap);
            var summary = session.Pump();
            Assert.IsNotNull(summary);
            Assert.AreEqual(tick + 5 * gap, sim.CurrentTick);
            Assert.AreEqual(gap, summary.RealMillisecondsAway);
            Assert.IsTrue(summary.ShouldShow);
            Assert.AreSame(summary, session.LastSummary);
            Assert.AreEqual(T0 + 3 * Second + gap, SavedUtcMs(_folder, "a"), "The wake is saved at once.");
            Assert.AreEqual(sim.CurrentTick, Load(_folder, "a").CurrentTick);

            Frame(session, Second);
            Assert.AreEqual(tick + 5 * gap + 5 * Second, sim.CurrentTick, "Afterwards frames count normally, and the sleep is never counted twice.");
        }

        [Test]
        public void Wake_AtExactlyThirtySeconds_ShortSummaryNotShown()
        {
            var session = Start(NewSim(1_000));
            _utc.AdvanceMilliseconds(OfflineCatchUp.WakeGapRealMs);
            var summary = session.Pump();
            Assert.IsNotNull(summary, "A 30-second gap counts as a wake.");
            Assert.IsFalse(summary.ShouldShow, "Under 5 minutes away: no summary panel.");
            Assert.AreEqual(OfflineCatchUp.WakeGapRealMs, session.Simulation.CurrentTick);
        }

        [Test]
        public void Wake_WhilePaused_AdvancesNothing()
        {
            var sim = NewSim(1_000);
            var session = Start(sim);
            Ok(session.Pause());
            string state = State(sim);
            _utc.AdvanceMilliseconds(10 * Hour);
            var summary = session.Pump();
            Assert.IsTrue(summary.WasPaused);
            Assert.AreEqual(state, State(sim));
        }

        [Test]
        public void RealClockGoesBackwardWhileRunning_IsAnOrdinaryFrame_AndTheMonotonicClockKeepsTime()
        {
            var sim = NewSim(1_000);
            var session = Start(sim);
            _utc.AdvanceMilliseconds(-2 * Hour);
            _mono.AdvanceMilliseconds(Second);
            Assert.IsNull(session.Pump());
            Assert.AreEqual(Second, sim.CurrentTick);
            Frame(session, Second);
            Assert.AreEqual(2 * Second, sim.CurrentTick, "Frames after the change count normally.");
        }

        [Test]
        public void SleepCatchUp_AndClosedGameCatchUp_OverTheSameGap_EndByteIdentical()
        {
            var sim = RouteFleet(runTicks: Year);
            Ok(sim.SetSpeed(ClockSpeed.X5));
            SaveAt(_folder, "sleep", sim, T0);
            SaveAt(_folder, "closed", sim, T0);
            long gap = 77 * Minute + 123;

            var sleeping = Open(_folder, "sleep", T0, mono: _mono, utc: _utc).Session;
            _utc.AdvanceMilliseconds(gap);
            var wake = sleeping.Pump();
            Assert.IsNotNull(wake);

            var closed = Open(_folder, "closed", T0 + gap);
            Assert.Greater(wake.Deliveries.Count, 0, "Setup: deliveries during the gap.");
            Assert.AreEqual(State(closed.Session.Simulation), State(sleeping.Simulation));
            CollectionAssert.AreEqual(StateBytes(closed.Session.Simulation), StateBytes(sleeping.Simulation));
            CollectionAssert.AreEqual(Bytes(_folder, "closed.json"), Bytes(_folder, "sleep.json"), "The two save files are byte-identical.");
            Assert.AreEqual(closed.Summary.NetCreditChange, wake.NetCreditChange);
        }
    }
}
