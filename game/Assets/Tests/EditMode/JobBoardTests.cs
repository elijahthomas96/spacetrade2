using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Routes;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.Voyages;
using static SpaceTrade.Sim.Tests.RouteTestSupport;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>
    /// Session 14: the two read-only queries the planner uses, <see cref="Simulation.JobBoardFor"/> and
    /// <see cref="Simulation.ManualDispatchRefusal"/>. Neither changes anything, both give the simulation's own figures
    /// and words, and the job board stays inside one frame at 50 systems.
    /// </summary>
    public class JobBoardTests
    {
        private static TuningData Sized(int systems, long limitMilliLy = 0)
        {
            var t = TestTuning.Load();
            if (limitMilliLy > 0) t.Galaxy.MaxJobDistanceMilliLy = limitMilliLy;
            t.Galaxy.SystemCountMin = systems;
            t.Galaxy.SystemCountMax = systems;
            var names = new List<string>(t.Galaxy.Names);
            for (int i = names.Count; i < systems; i++) names.Add($"System {i + 1:000}");
            t.Galaxy.Names = names;
            return t;
        }

        private static Simulation Game(int systems, long seed, long limitMilliLy = 0)
        {
            var t = Sized(systems, limitMilliLy);
            for (int i = 0; i < 100; i++)
            {
                var game = NewGame.Create(seed + i, t);
                if (!game.UsedDefaults) return game.Simulation;
            }
            Assert.Fail($"No fixture game with {systems} systems from seed {seed}.");
            return null;
        }

        private static byte[] Bytes(Simulation sim) => Encoding.UTF8.GetBytes(sim.DescribeState());

        [Test]
        public void TheJobBoard_ChangesNothing_TheStateIsByteIdenticalBeforeAndAfter([Values(12, 50)] int systems)
        {
            var sim = Game(systems, 1);
            var before = Bytes(sim);
            var board = sim.JobBoardFor(sim.Ships[0].Id);
            Assert.Greater(board.Entries.Count, 0);
            CollectionAssert.AreEqual(before, Bytes(sim), "asking for the job board changed the game");

            // Again in the middle of a game: a second ship bought and in flight, time passed, the clock running.
            int yard = sim.Market.Shipyards[0].SystemId;
            sim.SetCredits(50_000_000);
            Assert.IsTrue(sim.BuyShip(yard, sim.Market.Tuning.Ships[0].Id, out int second).Succeeded);
            int to = sim.Market.Galaxy.Systems.Where(s => s.Id != yard).OrderBy(s => sim.Market.Galaxy.DistanceMilliLy(yard, s.Id)).First().Id;
            Assert.IsTrue(sim.Dispatch(new VoyagePlan(second, to), true).Succeeded);
            sim.Play();
            sim.Advance(Year / 2);
            before = Bytes(sim);
            sim.JobBoardFor(sim.Ships[0].Id);
            var inFlight = sim.JobBoardFor(second);
            CollectionAssert.AreEqual(before, Bytes(sim), "asking for the job board mid-game changed the game");
            Assert.AreEqual(0, inFlight.Entries.Count);
        }

        [Test]
        public void TheLaunchRefusalQuery_ChangesNothing_AndSaysExactlyWhatDispatchSays()
        {
            // An ordinary docked ship: nothing in the way.
            var sim = Triangle(1_000_000);
            var before = Bytes(sim);
            Assert.IsNull(sim.ManualDispatchRefusal(CourierId));
            CollectionAssert.AreEqual(before, Bytes(sim));

            // On in-system transport.
            Ok(sim.SetInSystemTransport(CourierId, true));
            before = Bytes(sim);
            string reason = sim.ManualDispatchRefusal(CourierId);
            CollectionAssert.AreEqual(before, Bytes(sim), "asking why the ship cannot launch changed the game");
            Assert.AreEqual("Test Courier is on in-system transport at Alpha. Turn off in-system transport to dispatch it.", reason);
            var refused = sim.Dispatch(new VoyagePlan(CourierId, Beta), true);
            Assert.IsFalse(refused.Succeeded);
            Assert.AreEqual(refused.Reason, reason, "the query gives Dispatch's own words");
            CollectionAssert.AreEqual(before, Bytes(sim));

            // On an active automated route, waiting for a load it cannot get yet.
            var routed = Triangle(1_000_000);
            SetSupply(routed, Alpha, "ore", 0);
            Ok(routed.SetRoute(CourierId, new[] { new RouteStop(Alpha, new[] { "ore" }, minFreightTonnes: 5), RouteStop.Empty(Beta) }));
            Assert.IsFalse(Courier(routed).IsInTransit, "the ship is waiting, not flying");
            before = Bytes(routed);
            reason = routed.ManualDispatchRefusal(CourierId);
            CollectionAssert.AreEqual(before, Bytes(routed));
            Assert.AreEqual("Test Courier is on an automated route. Pause or clear its route to dispatch it by hand.", reason);
            refused = routed.Dispatch(new VoyagePlan(CourierId, Beta), true);
            Assert.AreEqual(refused.Reason, reason);

            // Paused, the route no longer stands in the way.
            Ok(routed.PauseRoute(CourierId));
            Assert.IsNull(routed.ManualDispatchRefusal(CourierId));

            Assert.Throws<System.ArgumentException>(() => sim.ManualDispatchRefusal(99));
        }

        [Test]
        public void TheBoard_ListsEveryJobFromThePortOnce_WithThePortsOwnFigures()
        {
            for (long seed = 1; seed <= 20; seed++)
            {
                var sim = Game(12, seed);
                var ship = sim.Ships[0];
                int origin = ship.DockedSystemId.Value;
                var board = sim.JobBoardFor(ship.Id);
                Assert.AreEqual(origin, board.OriginId);
                Assert.IsNull(board.Problem);
                var offers = sim.Market.OffersFrom(origin);
                var pools = sim.Market.PoolsFrom(origin);
                Assert.AreEqual(offers.Count + pools.Count, board.Entries.Count, $"seed {seed}");
                for (int i = 0; i < offers.Count; i++)
                {
                    var e = board.Entries[i];
                    Assert.AreEqual(offers[i].CommodityId, e.CommodityId);
                    Assert.AreEqual(offers[i].DestinationId, e.DestinationId);
                    Assert.AreEqual(offers[i].DistanceMilliLy, e.DistanceMilliLy);
                    Assert.AreEqual(sim.Market.TonnesAvailable(offers[i]), e.Available);
                    Assert.AreEqual(sim.Market.RatePerTonne(offers[i]), e.Rate);
                }
                for (int i = 0; i < pools.Count; i++)
                {
                    var e = board.Entries[offers.Count + i];
                    Assert.IsTrue(e.IsPassengerJob);
                    Assert.AreEqual(pools[i].DestinationId, e.DestinationId);
                    Assert.AreEqual(pools[i].Waiting, e.Available);
                    Assert.AreEqual(sim.Market.FarePerPassenger(pools[i]), e.Rate);
                }
            }
        }

        [Test]
        public void EveryFlyableJob_IsTheLargestFeasibleLoad_AndMatchesTheOpeningOfferCheck_AndTheRestCarryTheQuotesReason()
        {
            int flyable = 0, unflyable = 0;
            for (long seed = 1; seed <= 20; seed++)
            {
                var sim = Game(12, seed);
                var ship = sim.Ships[0];
                var board = sim.JobBoardFor(ship.Id);
                var opening = OpeningOffers.FindJobs(sim, ship.Id);
                var canFly = board.Entries.Where(e => e.CanFly).ToList();
                Assert.AreEqual(opening.Count, canFly.Count, $"seed {seed}: the same jobs the opening-offer check finds");
                for (int i = 0; i < opening.Count; i++)
                {
                    Assert.AreEqual(opening[i].CommodityId, canFly[i].CommodityId);
                    Assert.AreEqual(opening[i].DestinationId, canFly[i].DestinationId);
                    Assert.AreEqual(opening[i].NetProfit, canFly[i].NetProfit);
                    Assert.AreEqual(opening[i].TransitTicks, canFly[i].TransitTicks);
                }
                foreach (var e in board.Entries)
                {
                    VoyagePlan Plan(long n) => e.IsPassengerJob
                        ? new VoyagePlan(ship.Id, e.DestinationId, null, (int)n)
                        : new VoyagePlan(ship.Id, e.DestinationId, new[] { new FreightLine(e.CommodityId, (int)n) });
                    if (e.CanFly)
                    {
                        flyable++;
                        Assert.IsNull(e.Reason);
                        Assert.GreaterOrEqual(e.LargestLoad, 1);
                        Assert.IsTrue(sim.Quote(Plan(e.LargestLoad)).IsFeasible, "the largest load is feasible");
                        Assert.IsFalse(sim.Quote(Plan(e.LargestLoad + 1)).IsFeasible, "and one more is not");
                        Assert.AreEqual(sim.Quote(Plan(e.LargestLoad)).NetProfit, e.NetProfit, "the profit is the real quote's");
                        Assert.AreEqual(e.LargestLoad, e.IsPassengerJob ? e.BestQuote.Plan.Passengers : e.BestQuote.Freight[0].Tonnes);
                    }
                    else
                    {
                        unflyable++;
                        Assert.AreEqual(0, e.LargestLoad);
                        Assert.AreEqual(0, e.NetProfit);
                        var smallest = sim.Quote(Plan(1));
                        Assert.IsFalse(smallest.IsFeasible);
                        Assert.AreEqual(string.Join(" ", smallest.Problems), e.Reason, "the reason is the quote's own words for the smallest load");
                    }
                }
            }
            Assert.Greater(flyable, 50);
            Assert.Greater(unflyable, 0, "the 20 seeds include jobs the starter cannot fly");
            TestContext.WriteLine($"{flyable} flyable and {unflyable} unflyable jobs checked over 20 seeds.");
        }

        [Test]
        public void AHandWorkedBoard_OnTheTwoSystemMap()
        {
            // Alpha sells ore to Beta, 4.5 ly: the fixture's hand example. 60 t of ore waits; the courier's 30 t hold
            // must also take the food and water for the trip.
            var sim = NewSim(1_000_000);
            var board = sim.JobBoardFor(CourierId);
            var ore = board.Entries.Single(e => e.CommodityId == "ore");
            Assert.AreEqual(Beta, ore.DestinationId);
            Assert.AreEqual(4_500, ore.DistanceMilliLy);
            Assert.AreEqual(3 * Year, ore.TransitTicks, "4.5 ly at 1.5c is 3 game years");
            Assert.IsTrue(ore.CanFly);
            var quote = sim.Quote(Plan(ore.LargestLoad, 0));
            Assert.IsTrue(quote.IsFeasible);
            Assert.AreEqual(quote.NetProfit, ore.NetProfit);
            Assert.IsFalse(sim.Quote(Plan(ore.LargestLoad + 1, 0)).IsFeasible);
            Assert.Less(ore.LargestLoad, 30, "food and water take part of the 30 t hold");

            // With no credits the same job cannot be flown, and the reason is the quote's.
            var broke = NewSim(0);
            var entry = broke.JobBoardFor(CourierId).Entries.Single(e => e.CommodityId == "ore");
            Assert.IsFalse(entry.CanFly);
            StringAssert.Contains("Topping up supplies costs", entry.Reason);
            StringAssert.Contains("you have 0", entry.Reason);
        }

        [Test]
        public void TheBoard_RespectsTheJobDistanceLimit()
        {
            int beyond = 0;
            for (long seed = 1; seed <= 10; seed++)
            {
                var sim = Game(25, seed, 9_000);
                long limit = sim.Market.MaxJobDistanceMilliLy;
                Assert.AreEqual(9_000, limit);
                var ship = sim.Ships[0];
                int origin = ship.DockedSystemId.Value;
                var board = sim.JobBoardFor(ship.Id);
                foreach (var e in board.Entries)
                    Assert.LessOrEqual(sim.Market.Galaxy.DistanceMilliLy(origin, e.DestinationId), limit, $"seed {seed}: a job beyond the limit");
                beyond += sim.Market.Galaxy.Systems.Count(s => s.Id != origin && sim.Market.Galaxy.DistanceMilliLy(origin, s.Id) > limit);
                Assert.Greater(board.Entries.Count, 0);
            }
            Assert.Greater(beyond, 0, "the galaxies had systems beyond the limit, and none was listed");
        }

        [Test]
        public void AShipInTransit_HasNoBoard_AndTheQuotesReason()
        {
            var sim = NewSim(1_000_000);
            Ok(sim.Dispatch(Plan(10, 0), true));
            var board = sim.JobBoardFor(CourierId);
            Assert.IsNull(board.OriginId);
            Assert.AreEqual(0, board.Entries.Count);
            Assert.AreEqual("Test Courier is in transit to Beta, arriving Y4 M01 D01. It can take a new voyage once it docks.", board.Problem);
            Assert.AreEqual(string.Join(" ", sim.Quote(Plan(1, 0)).Problems), board.Problem);
            Assert.Throws<System.ArgumentException>(() => sim.JobBoardFor(99));
        }

        /// <summary>
        /// The 50 ms frame rule: one whole job board at 50 systems must fit well inside a frame, even at the busiest port
        /// in the galaxy. The fixture has no job distance limit, so these are the longest lists the game can make (the
        /// live limit of 14 ly makes them shorter).
        /// </summary>
        [Test]
        public void TheBoardAt50Systems_StaysInsideOneFrame()
        {
            var lines = new List<string>();
            double slowest = 0;
            for (long seed = 1; seed <= 5; seed++)
            {
                var sim = Game(50, seed);
                // The worst case: a courier docked at the port with the most jobs in the galaxy, with money to fly them.
                var market = sim.Market;
                int busiest = market.Galaxy.Systems.OrderByDescending(s => market.OffersFrom(s.Id).Count + market.PoolsFrom(s.Id).Count).First().Id;
                sim.SetCredits(10_000_000);
                int id = sim.AddShip("Busy Port Courier", market.Tuning.Start.StarterShipClass, market.Tuning.Start.StarterDrive, busiest).Id;
                sim.JobBoardFor(id); // the first call also pays for code being compiled on first use
                double worst = 0;
                int jobs = 0, flyable = 0;
                for (int i = 0; i < 5; i++)
                {
                    var watch = Stopwatch.StartNew();
                    var board = sim.JobBoardFor(id);
                    double ms = watch.Elapsed.TotalMilliseconds;
                    worst = System.Math.Max(worst, ms);
                    jobs = board.Entries.Count;
                    flyable = board.Entries.Count(e => e.CanFly);
                }
                slowest = System.Math.Max(slowest, worst);
                lines.Add($"seed {seed}: {jobs} jobs ({flyable} flyable), slowest of 5 boards {worst:F2} ms");
            }
            TestContext.WriteLine(string.Join("\n", lines));
            Assert.Less(slowest, 50.0, "a job board at 50 systems took a frame's worth of time or more:\n" + string.Join("\n", lines));
        }

        [TestCase(0L, "0 d")]
        [TestCase(10_000L, "1 d")]
        [TestCase(1_440_000L, "144 d")]
        [TestCase(3_600_000L, "1 y 0 d")]
        [TestCase(10_800_000L, "3 y 0 d")]
        [TestCase(4_320_000L, "1 y 72 d")]
        [TestCase(29_932_800L, "8 y 113.28 d")]
        [TestCase(31_764_707L, "8 y 296.4707 d")]
        [TestCase(5_000L, "0.5 d")]
        public void GameDurations_AreWrittenExactly_WithNoRounding(long ticks, string expected)
        {
            Assert.AreEqual(expected, Format.GameDuration(ticks));
        }

        [Test]
        public void TheQuotesArrivalLine_UsesTheSameWordsAsAShipInFlight()
        {
            var sim = NewSim(1_000_000);
            var quote = sim.Quote(Plan(10, 0));
            Assert.AreEqual("Arrives Y4 M01 D01 · 3 h 00 min 00 s at 1x", quote.DescribeArrival());
            sim.SetSpeed(Timing.ClockSpeed.X5);
            quote = sim.Quote(Plan(10, 0));
            Assert.AreEqual(5, quote.SpeedMultiplier);
            Assert.AreEqual(2_160, quote.RealSecondsAtCurrentSpeed);
            Assert.AreEqual("Arrives Y4 M01 D01 · 36 min 00 s at 5x", quote.DescribeArrival());

            // Once launched, the ship's own ETA line says the same.
            sim.Play();
            Ok(sim.Dispatch(Plan(10, 0), true));
            Assert.AreEqual(quote.DescribeArrival(), sim.EtaOf(CourierId).Describe());
        }
    }
}
