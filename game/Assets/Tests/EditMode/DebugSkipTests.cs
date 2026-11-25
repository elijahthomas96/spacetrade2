using NUnit.Framework;
using SpaceTrade.Sim.Timing;
using static SpaceTrade.Sim.Tests.RouteTestSupport;
using static SpaceTrade.Sim.Tests.SaveTestSupport;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>Spec 13: Skip to next arrival, refused without debug permission.</summary>
    public class DebugSkipTests
    {
        private static Simulation Flying(DebugCapability debug)
        {
            var sim = new Simulation(0, ClockSpeed.X1, false, debug);
            var tuning = FlatTuning();
            sim.AttachMarket(Markets.Market.Create(TwoSystems(4_500), tuning, 0));
            sim.SetCredits(1_000_000);
            sim.AddShip("Test Courier", tuning.Start.StarterShipClass, tuning.Start.StarterDrive, Alpha);
            Ok(sim.Dispatch(Plan(20, 2), buyTopUp: true));
            return sim;
        }

        [Test]
        public void WithoutDebugPermission_SkipIsRefused_AndNothingChanges()
        {
            var sim = Flying(null);
            Assert.IsFalse(sim.HasDebugCapability);
            byte[] before = StateBytes(sim);
            var r = sim.DebugSkipToNextArrival();
            Assert.IsFalse(r.Succeeded);
            Assert.AreEqual("Skip to next arrival is a debug command. Debug mode was not switched on when this game was started.", r.Reason);
            CollectionAssert.AreEqual(before, StateBytes(sim));
        }

        [Test]
        public void DebugPermission_IsNeverSaved_ALoadedGameHasOnlyWhatTheLoaderGrants()
        {
            using (var folder = new TempSaveFolder())
            {
                var sim = Flying(DebugCapability.Grant());
                SaveAt(folder, "a", sim);
                StringAssert.DoesNotContain("debug", System.IO.File.ReadAllText(folder.File("a.json")).ToLowerInvariant());
                var plain = Load(folder, "a");
                Assert.IsFalse(plain.HasDebugCapability);
                Assert.IsFalse(plain.DebugSkipToNextArrival().Succeeded);
                Assert.IsTrue(Load(folder, "a", debug: DebugCapability.Grant()).HasDebugCapability);
            }
        }

        [Test]
        public void WithDebugPermission_SkipLandsOnTheArrivalTick_AndProcessesTheArrival()
        {
            var sim = Flying(DebugCapability.Grant());
            var voyage = Courier(sim).Voyage;
            long credits = sim.Credits;
            Assert.IsTrue(sim.DebugSkipToNextArrival().Succeeded);
            Assert.AreEqual(voyage.ArrivalTick, sim.CurrentTick);
            Assert.AreEqual(credits + voyage.Payout, sim.Credits);
            Assert.AreEqual(1, sim.Receipts.Count);
        }

        [Test]
        public void Skip_WorksWhilePaused_AndDoesNotUnpause()
        {
            var sim = Flying(DebugCapability.Grant());
            Ok(sim.Pause());
            Assert.IsTrue(sim.DebugSkipToNextArrival().Succeeded);
            Assert.IsTrue(sim.Clock.IsPaused);
            Assert.AreEqual(1, sim.Receipts.Count);
        }

        [Test]
        public void Skip_WithNothingInFlight_IsRefused_AndNothingChanges()
        {
            var sim = Flying(DebugCapability.Grant());
            sim.DebugSkipToNextArrival();
            byte[] before = StateBytes(sim);
            var r = sim.DebugSkipToNextArrival();
            Assert.IsFalse(r.Succeeded);
            Assert.AreEqual("No ship is in flight, so there is no arrival to skip to.", r.Reason);
            CollectionAssert.AreEqual(before, StateBytes(sim));
        }

        [Test]
        public void Skip_EndsInTheSameStateAsAdvancingToTheArrival()
        {
            var skipped = Flying(DebugCapability.Grant());
            var advanced = Flying(null);
            skipped.DebugSkipToNextArrival();
            advanced.Advance(Courier(advanced).Voyage.ArrivalTick);
            Assert.AreEqual(State(advanced), State(skipped));
        }
    }
}
