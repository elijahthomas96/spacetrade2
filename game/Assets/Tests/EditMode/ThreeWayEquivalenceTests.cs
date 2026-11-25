using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Timing;
using static SpaceTrade.Sim.Tests.RouteTestSupport;
using static SpaceTrade.Sim.Tests.SaveTestSupport;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>
    /// Spec 14.3: ordinary play, offline catch-up, and debug stepping over the same span end in the same state:
    /// balances, fleet positions, stock, and route state, checked as the whole state dump.
    /// </summary>
    public class ThreeWayEquivalenceTests
    {
        private TempSaveFolder _folder;

        [SetUp] public void SetUp() => _folder = new TempSaveFolder();
        [TearDown] public void TearDown() => _folder.Dispose();

        private static void AssertSameWorld(Simulation expected, Simulation actual, string path)
        {
            Assert.AreEqual(expected.Credits, actual.Credits, $"{path}: credits.");
            for (int i = 0; i < expected.Ships.Count; i++)
            {
                var e = expected.Ships[i];
                var a = actual.Ships[i];
                Assert.AreEqual(e.DockedSystemId, a.DockedSystemId, $"{path}: ship {e.Id} position.");
                Assert.AreEqual(e.Voyage?.DescribeState(), a.Voyage?.DescribeState(), $"{path}: ship {e.Id} voyage.");
                Assert.AreEqual(e.Route?.Status, a.Route?.Status, $"{path}: ship {e.Id} route status.");
                Assert.AreEqual(e.Route?.CurrentStopIndex, a.Route?.CurrentStopIndex, $"{path}: ship {e.Id} route stop.");
            }
            CollectionAssert.AreEqual(expected.Market.Supplies.Select(s => s.Tonnes).ToList(), actual.Market.Supplies.Select(s => s.Tonnes).ToList(), $"{path}: supply stock.");
            CollectionAssert.AreEqual(expected.Market.Demands.Select(s => s.Tonnes).ToList(), actual.Market.Demands.Select(s => s.Tonnes).ToList(), $"{path}: demand stock.");
            Assert.AreEqual(State(expected), State(actual), $"{path}: the whole state dump.");
        }

        private void Run(Simulation start, Tuning.TuningData tuning, long realMs)
        {
            Assert.IsFalse(start.Clock.IsPaused);
            Assert.AreEqual(ClockSpeed.X5, start.Clock.Speed);
            SaveAt(_folder, "base", start, T0);
            long target = start.CurrentTick + 5 * realMs;

            var ordinary = Load(_folder, "base", tuning);
            var stepped = Load(_folder, "base", tuning, DebugCapability.Grant());
            var mono = new FakeRealTimeSource();
            var driver = new RealTimeDriver(ordinary, mono);
            var chunks = new SeededRandom(99);
            long left = realMs * 1_000;
            int frames = 0;
            while (left > 0)
            {
                long step = System.Math.Min(left, chunks.Range(1L, 20_000_000L));
                mono.AdvanceMicroseconds(step);
                driver.Pump();
                left -= step;
                frames++;
            }
            Assert.AreEqual(target, ordinary.CurrentTick);

            var caughtUp = Open(_folder, "base", T0 + realMs, tuning).Session.Simulation;

            int skips = 0;
            while (stepped.Ships.Any(s => s.IsInTransit && s.Voyage.ArrivalTick <= target))
            {
                Assert.IsTrue(stepped.DebugSkipToNextArrival().Succeeded);
                skips++;
            }
            stepped.Advance(target);

            Assert.Greater(frames, 100, "Setup: many ordinary frames.");
            Assert.Greater(skips, 3, "Setup: several debug skips.");
            Assert.Greater(ordinary.Receipts.Count, 3, "Setup: deliveries during the span.");
            AssertSameWorld(ordinary, caughtUp, "Catch-up");
            AssertSameWorld(ordinary, stepped, "Debug stepping");
        }

        [Test]
        public void RouteFleet_OrdinaryPlay_CatchUp_AndDebugStepping_AreIdentical()
        {
            var sim = RouteFleet(runTicks: Year);
            Ok(sim.SetSpeed(ClockSpeed.X5));
            Run(sim, FlatTuning(), 2 * Hour + 3 * Minute + 7);
        }

        [Test]
        public void YardGame_OrdinaryPlay_CatchUp_AndDebugStepping_AreIdentical()
        {
            var sim = YardGame();
            Ok(sim.SetSpeed(ClockSpeed.X5));
            var offer = sim.Market.Offers.OrderBy(o => o.DistanceMilliLy).First();
            int extra = sim.AddShip("Extra", "courier", sim.FindShip(1).DriveId, offer.OriginId).Id;
            Ok(sim.SetRoute(extra, new System.Collections.Generic.List<Routes.RouteStop>
            {
                new Routes.RouteStop(offer.OriginId, new[] { offer.CommodityId }, carryPassengers: true),
                new Routes.RouteStop(offer.DestinationId, carryPassengers: true),
            }));
            Run(sim, TestTuning.Load(), 3 * Hour);
        }
    }
}
