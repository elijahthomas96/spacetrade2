using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Routes;
using SpaceTrade.Sim.Voyages;
using SpaceTrade.Sim.World;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;
using static SpaceTrade.Sim.Tests.RouteTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>Automated circuits over many years, money, determinism, and same-tick ordering (spec 8, 8.1, 14.2).</summary>
    public class RouteCircuitTests
    {
        private const long Leg3 = 2 * Year;
        private const long StartCredits = 200_000;

        private static Simulation TwoShipCircuit(Tuning.TuningData tuning = null)
        {
            var sim = Triangle(StartCredits, tuning);
            sim.AddShip("Second", sim.Market.Tuning.Start.StarterShipClass, sim.Market.Tuning.Start.StarterDrive, Beta);
            Ok(sim.SetRoute(1, Circuit()));
            Ok(sim.SetRoute(2, Circuit()));
            return sim;
        }

        [Test]
        public void ThreeStopCircuit_Runs20Years_WithHandCheckedMoney()
        {
            var sim = TwoShipCircuit();
            Assert.AreEqual(Beta, sim.FindShip(1).Voyage.DestinationId);
            Assert.AreEqual(Gamma, sim.FindShip(2).Voyage.DestinationId);

            sim.Advance(20 * Year);

            Assert.AreEqual(20, sim.Receipts.Count, "Each ship flies ten 2-year legs in 20 years");
            var expectedGross = new Dictionary<(int, int), long>
            {
                { (Alpha, Beta), 25 * 3_300 + 4 * 22_000 },     // ore at 1,500 x 220%, fares at 10,000 x 220%
                { (Beta, Gamma), 25 * 6_160 + 4 * 22_000 },     // chemicals at 2,800 x 220%
                { (Gamma, Alpha), 25 * 4_400 + 4 * 22_000 },    // food at 2,000 x 220%
            };
            foreach (int shipId in new[] { 1, 2 })
            {
                var mine = sim.Receipts.Where(r => r.ShipId == shipId).ToList();
                Assert.AreEqual(10, mine.Count);
                for (int i = 0; i < mine.Count; i++)
                {
                    var r = mine[i];
                    Assert.AreEqual(i * Leg3, r.DepartureTick, "Each leg leaves in the tick the last one arrived");
                    Assert.AreEqual(Leg3, r.ElapsedTicks);
                    Assert.AreEqual(4, r.Passengers);
                    Assert.AreEqual(25, r.Freight.Sum(f => f.Tonnes));
                    Assert.AreEqual(expectedGross[(r.OriginId, r.DestinationId)], r.GrossRevenue, $"ship {shipId} leg {i + 1}");
                }
                Assert.IsTrue(sim.FindShip(shipId).IsInTransit, "Both ships left again at the 20-year arrival");
            }
            Assert.AreEqual(170_500, sim.Receipts[0].GrossRevenue);
            Assert.AreEqual(0, sim.Notifications.Count(n => n.Kind == NotificationKind.RouteBlocked));
            Assert.AreEqual(StartCredits + sim.Receipts.Sum(r => r.GrossRevenue) - SuppliesBought(sim), sim.Credits);
            TestContext.WriteLine($"20 years, 2 ships: {sim.Receipts.Count} receipts, gross {sim.Receipts.Sum(r => r.GrossRevenue):N0}, supplies bought {SuppliesBought(sim):N0}, final credits {sim.Credits:N0}");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void MoneyIsConserved_Over20Years(bool flatRolls)
        {
            var sim = TwoShipCircuit(flatRolls ? null : TestTuning.Load());
            long bought = 0, expensed = 0, aboard = 0;
            for (int year = 1; year <= 20; year++)
            {
                sim.Advance(year * Year);
                long revenue = sim.Receipts.Sum(r => r.GrossRevenue);
                bought = SuppliesBought(sim);
                expensed = sim.Receipts.Sum(r => r.SuppliesCost);
                aboard = CostAboard(sim);
                Assert.AreEqual(StartCredits + revenue - bought, sim.Credits, $"year {year}: credits = start + revenue - supplies bought");
                Assert.AreEqual(bought, expensed + aboard, $"year {year}: spent = expensed + cost aboard");
            }
            Assert.AreEqual(20, sim.Receipts.Count);
            TestContext.WriteLine($"flat={flatRolls}: credits {sim.Credits:N0} = {StartCredits:N0} + revenue {sim.Receipts.Sum(r => r.GrossRevenue):N0} - bought {bought:N0}; bought = expensed {expensed:N0} + aboard {aboard:N0}");
        }

        [Test]
        public void TwentyYears_InOneStep_EqualsDailySteps()
        {
            var one = TwoShipCircuit(TestTuning.Load());
            var daily = TwoShipCircuit(TestTuning.Load());
            one.Advance(20 * Year);
            for (long t = Day; t <= 20 * Year; t += Day) daily.Advance(t);
            Assert.AreEqual(one.DescribeState(), daily.DescribeState());
        }

        [Test]
        public void AutomatedDeparture_AndManualDispatchOfTheSamePlan_AreByteIdentical()
        {
            var auto = NewSim(1_000_000);
            var manual = NewSim(1_000_000);
            auto.Advance(10 * Day);
            manual.Advance(10 * Day);

            Ok(auto.SetRoute(CourierId, OreShuttle(passengers: true)));
            var v = Courier(auto).Voyage;
            Assert.IsNotNull(v);
            Assert.AreEqual(23, Tonnes(Courier(auto), "ore"));
            Assert.AreEqual(4, v.Passengers);

            var plan = new VoyagePlan(CourierId, Beta, v.Freight.Select(f => new FreightLine(f.CommodityId, (int)f.Tonnes)), v.Passengers);
            Ok(manual.Dispatch(plan, buyTopUp: true));
            Ok(manual.SetRoute(CourierId, OreShuttle(passengers: true)));

            CollectionAssert.AreEqual(StateBytes(manual), StateBytes(auto));
            auto.Advance(10 * Year);
            manual.Advance(10 * Year);
            CollectionAssert.AreEqual(StateBytes(manual), StateBytes(auto), "Still identical 10 years later");
        }

        /// <summary>The event queue's line with its scheduling numbers removed.</summary>
        private static string WithoutSchedulingNumbers(string state)
        {
            var sb = new StringBuilder();
            foreach (var line in state.Split('\n'))
            {
                if (line.StartsWith("events next=")) sb.Append(line.Substring(line.IndexOf(" pending=")));
                else if (line.StartsWith("event "))
                {
                    var parts = line.Split(' ');
                    sb.Append(string.Join(" ", parts.Take(4).Concat(parts.Skip(5))));
                }
                else sb.Append(line);
                sb.Append('\n');
            }
            return sb.ToString();
        }

        [Test]
        public void DepartureAfterArrival_MatchesAManualDispatch_ExceptTheQueuesSchedulingNumbers()
        {
            var auto = NewSim(1_000_000);
            var manual = NewSim(1_000_000);
            auto.Advance(10 * Day);
            manual.Advance(10 * Day);
            Ok(auto.SetRoute(CourierId, OreShuttle()));
            var v = Courier(auto).Voyage;
            Ok(manual.Dispatch(new VoyagePlan(CourierId, Beta, v.Freight.Select(f => new FreightLine(f.CommodityId, (int)f.Tonnes))), true));
            Ok(manual.SetRoute(CourierId, OreShuttle()));
            Ok(manual.PauseRoute(CourierId));

            long arrival = v.ArrivalTick;
            auto.Advance(arrival);
            manual.Advance(arrival);
            Assert.AreEqual(arrival, Courier(auto).Voyage.DepartureTick, "The route ship left Beta in its arrival tick");
            Assert.IsFalse(Courier(manual).IsInTransit, "The paused twin stayed docked");

            Ok(manual.Dispatch(new VoyagePlan(CourierId, Alpha), true));
            Ok(manual.SetRoute(CourierId, OreShuttle()));

            var (added, removed) = Diff(Lines(auto), Lines(manual));
            Assert.IsTrue(added.Concat(removed).All(l => l.StartsWith("events next=") || l.StartsWith("event ")), "Only the queue lines differ:\n" + string.Join("\n", added.Concat(removed)));
            Assert.AreEqual(WithoutSchedulingNumbers(auto.DescribeState()), WithoutSchedulingNumbers(manual.DescribeState()));
            TestContext.WriteLine("Differing lines (auto first):\n" + string.Join("\n", removed) + "\n---\n" + string.Join("\n", added));
        }

        /// <summary>Two couriers at Beta, sent empty to Alpha (ship 2 first), then put on the ore shuttle in flight. Both reach Alpha at 3 years.</summary>
        private static Simulation TwoArrivingAtAlpha()
        {
            var tuning = FlatTuning();
            var sim = new Simulation();
            sim.AttachMarket(Market.Create(TwoSystems(4_500), tuning, 0));
            sim.SetCredits(1_000_000);
            sim.AddShip("One", "courier", tuning.Start.StarterDrive, Beta);
            sim.AddShip("Two", "courier", tuning.Start.StarterDrive, Beta);
            Ok(sim.Dispatch(new VoyagePlan(2, Alpha), true));
            Ok(sim.Dispatch(new VoyagePlan(1, Alpha), true));
            Ok(sim.SetRoute(2, OreShuttle()));
            Ok(sim.SetRoute(1, OreShuttle()));
            return sim;
        }

        [Test]
        public void SimultaneousArrivals_SettleAndLoadInShipIdOrder_WithinSupply()
        {
            var sim = TwoArrivingAtAlpha();
            sim.Advance(Leg45 - 1);
            SetSupply(sim, Alpha, "ore", 40);
            sim.Advance(Leg45);

            Assert.AreEqual(1, sim.Receipts[0].ShipId, "Ship 2 was dispatched first, but ship 1 arrives first");
            Assert.AreEqual(2, sim.Receipts[1].ShipId);
            long one = Tonnes(sim.FindShip(1), "ore"), two = Tonnes(sim.FindShip(2), "ore");
            Assert.AreEqual(27, one, "Ship 1 loads first: as much as fits");
            Assert.AreEqual(23, two, "Ship 2 gets the remainder of 40 + 10 replenished");
            Assert.AreEqual(50, one + two);
            Assert.AreEqual(0, sim.Market.FindSupply(Alpha, "ore").Tonnes);
            Assert.AreEqual(Leg45, sim.FindShip(2).Voyage.DepartureTick);
        }

        [Test]
        public void TwoShipsWantingTheSameFreight_NeverLoadMoreThanDemand()
        {
            var sim = TwoArrivingAtAlpha();
            sim.Advance(Leg45 - 1);
            SetDemand(sim, Beta, "ore", 25);
            long supplyBefore = sim.Market.FindSupply(Alpha, "ore").Tonnes;
            sim.Advance(Leg45);

            long one = Tonnes(sim.FindShip(1), "ore"), two = Tonnes(sim.FindShip(2), "ore");
            Assert.AreEqual(27, one);
            Assert.AreEqual(8, two, "Demand was 25 + 10 replenished at Beta in phase 2");
            Assert.AreEqual(0, sim.Market.FindDemand(Beta, "ore").Tonnes);
            Assert.LessOrEqual(one + two, supplyBefore);
        }
    }
}
