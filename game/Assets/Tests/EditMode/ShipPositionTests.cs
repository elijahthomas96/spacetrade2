using System.Collections.Generic;
using NUnit.Framework;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Voyages;
using SpaceTrade.Sim.World;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>
    /// The ship position the map draws (spec 9.1): worked out from the locked voyage terms and the tick, never stored.
    /// Hand example: Alpha at (3, 4) ly, Beta at the origin, so the leg runs 5 ly left and down; the courier's 1.5c drive
    /// takes 5,000 x 3,600,000 / 1,500 = 12,000,000 ticks.
    /// </summary>
    public class ShipPositionTests
    {
        private const int Alpha = 0;
        private const int Beta = 1;
        private const int ShipId = 1;
        private const long Transit = 12_000_000;

        private sealed class FakeTime : IRealTimeSource
        {
            public long NowMicroseconds { get; set; }
        }

        private static Simulation Flying()
        {
            var tuning = VoyageTestSupport.FlatTuning();
            var galaxy = new Galaxy(1, Alpha, new List<StarSystem>
            {
                new StarSystem(Alpha, "Alpha", 3_000, 4_000, 10, new[] { "mining_colony" }, false),
                new StarSystem(Beta, "Beta", 0, 0, 10, new[] { "refinery" }, false),
            });
            var sim = new Simulation();
            sim.AttachMarket(Market.Create(galaxy, tuning, 0));
            sim.SetCredits(1_000_000);
            sim.AddShip("Test Courier", tuning.Start.StarterShipClass, tuning.Start.StarterDrive, Alpha);
            Assert.IsTrue(sim.Dispatch(new VoyagePlan(ShipId, Beta), true).Succeeded);
            Assert.AreEqual(Transit, sim.FindShip(ShipId).Voyage.TransitTicks);
            return sim;
        }

        private static (long X, long Y) At(Simulation sim)
        {
            var p = sim.PositionOf(ShipId);
            return (p.XMilliLy, p.YMilliLy);
        }

        [Test]
        public void Docked_IsExactlyItsStar_At100Percent()
        {
            var tuning = VoyageTestSupport.FlatTuning();
            var sim = VoyageTestSupport.NewSim(100_000, tuning);
            var p = sim.PositionOf(VoyageTestSupport.CourierId);
            Assert.IsFalse(p.InTransit);
            Assert.AreEqual(VoyageTestSupport.Alpha, p.DockedSystemId);
            Assert.AreEqual((0L, 0L), (p.XMilliLy, p.YMilliLy));
            Assert.AreEqual(ShipPosition.FullProgress, p.ProgressHundredthsOfPercent);
            Assert.IsNull(p.OriginId);
            Assert.AreEqual(0, p.TransitTicks);
        }

        [Test]
        public void AtDeparture_IsExactlyTheOrigin()
        {
            var sim = Flying();
            var p = sim.PositionOf(ShipId);
            Assert.IsTrue(p.InTransit);
            Assert.AreEqual((3_000L, 4_000L), (p.XMilliLy, p.YMilliLy));
            Assert.AreEqual(Alpha, p.OriginId);
            Assert.AreEqual(Beta, p.DestinationId);
            Assert.AreEqual(0, p.ElapsedTicks);
            Assert.AreEqual(0, p.TravelledMilliLy);
            Assert.AreEqual(0, p.ProgressHundredthsOfPercent);
            Assert.AreEqual(5_000, p.DistanceMilliLy);
        }

        [Test]
        public void MidTrip_MatchesTheHandFigures_RoundedOnceByTheOneRule()
        {
            var sim = Flying();
            // A third of the way: x 3,000 - 1,000 = 2,000; y 4,000 - 1,333.33 = 2,667 (nearest);
            // flown 1,666.67 rounded down to 1,666; progress 33.33 percent rounded down to 3,333.
            sim.Advance(Transit / 3);
            var p = sim.PositionOf(ShipId);
            Assert.AreEqual((2_000L, 2_667L), (p.XMilliLy, p.YMilliLy));
            Assert.AreEqual(4_000_000, p.ElapsedTicks);
            Assert.AreEqual(1_666, p.TravelledMilliLy);
            Assert.AreEqual(3_333, p.ProgressHundredthsOfPercent);

            // Exact half: x moves -3,000 x 2,000 / 12,000,000 = -0.5, away from zero gives -1; y -0.667 gives -1.
            var half = Flying();
            half.Advance(2_000);
            Assert.AreEqual((2_999L, 3_999L), At(half));
            // -1.5 gives -2; y exactly -2.
            half.Advance(6_000);
            Assert.AreEqual((2_998L, 3_998L), At(half));
        }

        [Test]
        public void OneTickBeforeArrival_IsBelow100Percent_AndDocked_Is100Percent()
        {
            var sim = Flying();
            sim.Advance(Transit - 1);
            var p = sim.PositionOf(ShipId);
            Assert.IsTrue(p.InTransit);
            Assert.AreEqual(9_999, p.ProgressHundredthsOfPercent, "progress is rounded down, so it never shows 100.00 percent in flight");
            Assert.AreEqual(4_999, p.TravelledMilliLy);
            Assert.AreEqual((0L, 0L), (p.XMilliLy, p.YMilliLy), "the position itself rounds to nearest");

            sim.Advance(Transit);
            p = sim.PositionOf(ShipId);
            Assert.IsFalse(p.InTransit);
            Assert.AreEqual(Beta, p.DockedSystemId);
            Assert.AreEqual((0L, 0L), (p.XMilliLy, p.YMilliLy));
            Assert.AreEqual(ShipPosition.FullProgress, p.ProgressHundredthsOfPercent);
        }

        [Test]
        public void NeverMovesBackward_AndProgressNeverFalls()
        {
            var sim = Flying();
            long lastDistanceToGo = long.MaxValue, lastProgress = -1;
            for (long t = 0; t <= Transit; t += 7_919)
            {
                sim.Advance(t);
                var p = sim.PositionOf(ShipId);
                long toGo = p.XMilliLy * p.XMilliLy + p.YMilliLy * p.YMilliLy;
                Assert.LessOrEqual(toGo, lastDistanceToGo, $"tick {t}");
                Assert.GreaterOrEqual(p.ProgressHundredthsOfPercent, lastProgress, $"tick {t}");
                lastDistanceToGo = toGo;
                lastProgress = p.ProgressHundredthsOfPercent;
            }
        }

        [Test]
        public void WhilePaused_StaysStill()
        {
            var sim = Flying();
            var time = new FakeTime { NowMicroseconds = 1_000_000 };
            var driver = new RealTimeDriver(sim, time);
            time.NowMicroseconds += 3_000_000_000;
            driver.Pump();
            var before = At(sim);
            Assert.AreNotEqual((3_000L, 4_000L), before);
            driver.Pause();
            for (int i = 0; i < 10; i++)
            {
                time.NowMicroseconds += 60_000_000;
                driver.Pump();
                Assert.AreEqual(before, At(sim));
            }
        }

        [Test]
        public void TheSameTick_GivesTheSamePosition_AtEverySpeed_AndSpeedNeverMovesTheArrival()
        {
            var results = new List<(long, long, long)>();
            foreach (var speed in new[] { ClockSpeed.X1, ClockSpeed.X3, ClockSpeed.X5 })
            {
                var sim = Flying();
                long arrival = sim.FindShip(ShipId).Voyage.ArrivalTick;
                var time = new FakeTime { NowMicroseconds = 0 };
                var driver = new RealTimeDriver(sim, time);
                driver.SetSpeed(speed);
                time.NowMicroseconds += 4_500_000_000L / ClockSpeeds.Multiplier(speed);
                driver.Pump();
                Assert.AreEqual(4_500_000, sim.CurrentTick, speed.ToString());
                Assert.AreEqual(arrival, sim.FindShip(ShipId).Voyage.ArrivalTick, speed.ToString());
                var p = sim.PositionOf(ShipId);
                results.Add((p.XMilliLy, p.YMilliLy, p.ProgressHundredthsOfPercent));
            }
            Assert.AreEqual(results[0], results[1]);
            Assert.AreEqual(results[0], results[2]);
            Assert.AreEqual((1_875L, 2_500L, 3_750L), results[0]);
        }

        [Test]
        public void Asking_ChangesNothing()
        {
            var sim = Flying();
            sim.Advance(Transit / 2);
            byte[] before = VoyageTestSupport.StateBytes(sim);
            for (int i = 0; i < 3; i++) sim.PositionOf(ShipId);
            CollectionAssert.AreEqual(before, VoyageTestSupport.StateBytes(sim));
        }
    }
}
