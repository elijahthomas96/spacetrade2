using System.IO;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Routes;
using SpaceTrade.Sim.Saves;
using SpaceTrade.Sim.Timing;
using static SpaceTrade.Sim.Tests.RouteTestSupport;
using static SpaceTrade.Sim.Tests.SaveTestSupport;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>Spec 11.3: opening a closed game catches up through the one elapsed-time rule and saves once.</summary>
    public class OfflineCatchUpTests
    {
        private TempSaveFolder _folder;

        [SetUp] public void SetUp() => _folder = new TempSaveFolder();
        [TearDown] public void TearDown() => _folder.Dispose();

        /// <summary>The two-system map with the courier mid-voyage, unpaused at the given speed.</summary>
        private static Simulation Flying(ClockSpeed speed = ClockSpeed.X1)
        {
            var sim = NewSim(1_000_000);
            Ok(sim.SetSpeed(speed));
            Ok(sim.Dispatch(Plan(20, 2), buyTopUp: true));
            sim.Advance(Day);
            return sim;
        }

        [Test]
        public void ElapsedRule_RealMillisecondsTimesSpeed_ThroughTheNormalAdvance()
        {
            var sim = Flying(ClockSpeed.X3);
            long tick = sim.CurrentTick;
            SaveAt(_folder, "a", sim, T0);
            var r = Open(_folder, "a", T0 + 10 * Minute);
            Assert.AreEqual(tick + 3 * 10 * Minute, r.Session.Simulation.CurrentTick);
            Assert.AreEqual(3 * 10 * Minute, r.Summary.GameTicksPassed);
            Assert.AreEqual(10 * Minute, r.Summary.RealMillisecondsAway);
            Assert.AreEqual(T0 + 10 * Minute, SavedUtcMs(_folder, "a"), "The catch-up is saved with the new time at once.");
            Assert.AreEqual(1, r.Session.SavesWritten);
        }

        [Test]
        public void MidTransitSave_OpenedAfterTheArrival_PaysOnce_AndOpeningAgainPaysNothingMore()
        {
            var sim = Flying(ClockSpeed.X5);
            var voyage = Courier(sim).Voyage;
            long credits = sim.Credits;
            SaveAt(_folder, "a", sim, T0);
            long realToArrival = (voyage.ArrivalTick - sim.CurrentTick) / 5 + 1;

            var first = Open(_folder, "a", T0 + realToArrival);
            Assert.AreEqual(credits + voyage.Payout, first.Session.Simulation.Credits);
            Assert.AreEqual(1, first.Summary.Deliveries.Count);
            Assert.AreEqual(voyage.Payout, first.Summary.VoyagePayouts);

            var again = Open(_folder, "a", T0 + realToArrival);
            Assert.AreEqual(credits + voyage.Payout, again.Session.Simulation.Credits, "Reopening at the same moment pays nothing more.");
            Assert.AreEqual(0, again.Summary.GameTicksPassed);
            Assert.AreEqual(0, again.Summary.Deliveries.Count);

            var later = Open(_folder, "a", T0 + realToArrival + RealDay);
            Assert.AreEqual(credits + voyage.Payout, later.Session.Simulation.Credits, "A settled save never pays again.");
            Assert.AreEqual(0, later.Summary.Deliveries.Count);
            Assert.AreEqual(1, later.Session.Simulation.Receipts.Count);
        }

        [Test]
        public void PausedSave_AdvancesNothing_HoweverLongTheGameWasClosed()
        {
            var sim = Flying(ClockSpeed.X5);
            Ok(sim.Pause());
            string state = State(sim);
            SaveAt(_folder, "a", sim, T0);
            var r = Open(_folder, "a", T0 + 365 * RealDay);
            Assert.IsTrue(r.Summary.WasPaused);
            Assert.AreEqual(0, r.Summary.GameTicksPassed);
            Assert.AreEqual(state, State(r.Session.Simulation));
            Assert.AreEqual(T0 + 365 * RealDay, SavedUtcMs(_folder, "a"));
        }

        [Test]
        public void BackwardClock_GivesZeroElapsed_AndTimeCountsFromTheNewTimestampAfterwards()
        {
            var sim = Flying(ClockSpeed.X1);
            string state = State(sim);
            long tick = sim.CurrentTick;
            SaveAt(_folder, "a", sim, T0);

            var back = Open(_folder, "a", T0 - 3 * Hour);
            Assert.IsTrue(back.Summary.ClockWentBackward);
            Assert.AreEqual(0, back.Summary.RealMillisecondsAway);
            Assert.AreEqual(0, back.Summary.GameTicksPassed);
            Assert.AreEqual(state, State(back.Session.Simulation));
            Assert.AreEqual(T0 - 3 * Hour, SavedUtcMs(_folder, "a"), "The save takes the new, earlier time.");

            var next = Open(_folder, "a", T0 - 2 * Hour);
            Assert.AreEqual(tick + Hour, next.Session.Simulation.CurrentTick, "Only the hour since the earlier time counts.");
        }

        [Test]
        public void ReturnSummary_AddsUpExactlyToTheChangeInCredits()
        {
            var sim = RouteFleet(runTicks: Year);
            Ok(sim.SetSpeed(ClockSpeed.X5));
            long credits = sim.Credits;
            int receipts = sim.Receipts.Count;
            SaveAt(_folder, "a", sim, T0);

            var r = Open(_folder, "a", T0 + 2 * Hour);
            var s = r.Summary;
            var after = r.Session.Simulation;
            Assert.AreEqual(5 * 2 * Hour, s.GameTicksPassed);
            Assert.Greater(s.Deliveries.Count, 3, "Setup: several deliveries while away.");
            Assert.Greater(s.InSystemIncome, 0);
            Assert.Greater(s.SuppliesBought, 0);
            CollectionAssert.AreEqual(after.Receipts.Skip(receipts).ToList(), s.Deliveries);
            Assert.AreEqual(s.Deliveries.Sum(d => d.GrossRevenue), s.VoyagePayouts);
            Assert.AreEqual(after.Credits - credits, s.NetCreditChange);
            Assert.AreEqual(s.NetCreditChange, s.VoyagePayouts + s.InSystemIncome - s.SuppliesBought, "Payouts + in-system income - supplies bought = net change.");
        }

        [Test]
        public void ReturnSummary_ListsBlockedAndIdleShips()
        {
            var sim = RouteFleet(runTicks: Year);
            sim.AddShip("Idle", sim.Market.Tuning.Start.StarterShipClass, sim.Market.Tuning.Start.StarterDrive, Gamma);
            SaveAt(_folder, "a", sim, T0);
            var r = Open(_folder, "a", T0 + Hour);
            var after = r.Session.Simulation;

            var attention = r.Summary.ShipsNeedingAttention.ToDictionary(a => a.ShipId);
            CollectionAssert.AreEqual(new[] { 4, 5, 6 }, attention.Keys.OrderBy(k => k).ToArray(),
                "Ships flying or waiting on an active route, or on in-system transport, need nothing.");
            Assert.AreEqual(RouteStatus.Blocked, after.FindShip(4).Route.Status);
            Assert.AreEqual(after.FindShip(4).Route.StatusText, attention[4].Reason, "The blocked ship gives its route's reason.");
            string restingAt = after.Market.Galaxy.Systems[after.FindShip(5).DockedSystemId.Value].Name;
            Assert.AreEqual($"Resting is docked at {restingAt} and its route is paused.", attention[5].Reason);
            Assert.AreEqual("Idle is docked at Gamma with no orders.", attention[6].Reason);
        }

        [TestCase(5 * Minute - 1, false)]
        [TestCase(5 * Minute, true)]
        public void ReturnSummary_IsShownOnlyAfterFiveMinutesAway(long away, bool shown)
        {
            SaveAt(_folder, "a", Flying(), T0);
            Assert.AreEqual(shown, Open(_folder, "a", T0 + away).Summary.ShouldShow);
        }

        [Test]
        public void CatchUpSaveFails_TheGameStillOpensWithAWarning_ThatStaysUntilASaveSucceeds()
        {
            var sim = Flying(ClockSpeed.X5);
            var voyage = Courier(sim).Voyage;
            byte[] before = SaveAt(_folder, "a", sim, T0);
            Directory.CreateDirectory(_folder.File("a.json.tmp"));
            long arrive = (voyage.ArrivalTick - sim.CurrentTick) / 5 + 1;
            var mono = new FakeRealTimeSource();
            var utc = new FakeUtcClock(T0 + arrive);

            var r = Open(_folder, "a", T0 + arrive, mono: mono, utc: utc);
            var session = r.Session;
            Assert.AreEqual(1, session.Simulation.Receipts.Count, "The game opens caught up.");
            StringAssert.Contains("a folder named a.json.tmp is in the way", session.SaveWarning);
            CollectionAssert.AreEqual(before, Bytes(_folder, "a.json"), "The file on disk is unchanged, so reopening cannot pay twice.");

            for (int i = 0; i < 61; i++) { mono.AdvanceSeconds(1); utc.AdvanceMilliseconds(Second); session.Pump(); }
            Assert.IsNotNull(session.SaveWarning, "The 60-second autosave also failed, and the warning stays.");
            Assert.IsFalse(session.Save().Succeeded);
            Assert.IsNotNull(session.SaveWarning);

            Directory.Delete(_folder.File("a.json.tmp"));
            Assert.IsTrue(session.Save().Succeeded);
            Assert.IsNull(session.SaveWarning, "The warning clears once a save succeeds.");
            var reopened = Load(_folder, "a");
            Assert.AreEqual(1, reopened.Receipts.Count);
        }
    }
}
