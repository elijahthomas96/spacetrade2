using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.Voyages;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;

namespace SpaceTrade.Sim.Tests
{
    public class DispatchTests
    {
        private const long HandArrivalTick = 10_800_000; // 4.5 ly at 1.5c = 3 game years

        [Test]
        public void Dispatch_HandExample_LocksTheTermsAndDeparts()
        {
            var sim = NewSim(100_000);
            var ship = Courier(sim);
            var q = sim.Quote(Plan(20, 2));

            var result = sim.Dispatch(Plan(20, 2), buyTopUp: true);
            Assert.IsTrue(result.Succeeded, result.Reason);

            Assert.AreEqual(100_000 - 43_733, sim.Credits, "Only the top-up is paid at dispatch.");
            Assert.IsTrue(ship.IsInTransit);
            Assert.IsNull(ship.DockedSystemId);
            var v = ship.Voyage;
            Assert.AreEqual(Alpha, v.OriginId);
            Assert.AreEqual(Beta, v.DestinationId);
            Assert.AreEqual(4_500, v.DistanceMilliLy);
            Assert.AreEqual("alcubierre_1_50", v.DriveId);
            Assert.AreEqual(0, v.DepartureTick);
            Assert.AreEqual(HandArrivalTick, v.ArrivalTick);
            Assert.AreEqual(q.ArrivalTick, v.ArrivalTick);
            Assert.AreEqual("ore", v.Freight.Single().CommodityId);
            Assert.AreEqual(20, v.Freight.Single().Tonnes);
            Assert.AreEqual(4_200, v.Freight.Single().RatePerTonne);
            Assert.AreEqual(2, v.Passengers);
            Assert.AreEqual(28_000, v.FarePerPassenger);
            Assert.AreEqual(140_000, v.Payout);
            Assert.AreEqual(8_388, v.FuelUseKg);
            Assert.AreEqual(2_592, v.FoodUseKg);
            Assert.AreEqual(1_728, v.WaterUseKg);

            // Supplies are bought now but used only at arrival.
            Assert.AreEqual(8_388, ship.Fuel.Kg);
            Assert.AreEqual(2_592, ship.Food.Kg);
            Assert.AreEqual(1_728, ship.Water.Kg);

            // Freight from supply at Alpha and demand at Beta, and the seats, are all reserved.
            Assert.AreEqual(40, sim.Market.FindSupply(Alpha, "ore").Tonnes);
            Assert.AreEqual(40, sim.Market.FindDemand(Beta, "ore").Tonnes);
            Assert.AreEqual(4, sim.Market.FindPool(Alpha, Beta).Waiting);

            StringAssert.Contains($"event {HandArrivalTick} Arrival 1 ", State(sim));
            TestContext.WriteLine(State(sim));
        }

        private static readonly (string name, long credits, int tankTonnes, VoyagePlan plan, string reason)[] Failures =
        {
            ("too many passengers", 100_000, 20, Plan(20, 5), "Book 1 fewer passenger"),
            ("hold too full", 100_000, 20, Plan(30, 0), "Unload 3 t of freight"),
            ("tank too small", 1_000_000, 8, Plan(20, 2), "Unload 4 t of freight to bring the fuel needed within the tank."),
            ("market cannot supply", 1_000_000, 20, new VoyagePlan(CourierId, Beta, new[] { new FreightLine("ore", 10), new FreightLine("metals", 5) }), "Alpha does not produce metals."),
            ("one credit short", 43_732, 20, Plan(20, 2), "You need 1 more credit."),
            ("unknown ship", 100_000, 20, new VoyagePlan(99, Beta), "There is no ship with id 99."),
            ("unknown destination", 100_000, 20, new VoyagePlan(CourierId, 7), "There is no system with id 7."),
            ("already there", 100_000, 20, new VoyagePlan(CourierId, Alpha), "Test Courier is already at Alpha."),
        };

        [Test]
        public void FailedDispatch_LeavesTheStateByteIdentical([Values(0, 1, 2, 3, 4, 5, 6, 7)] int index)
        {
            var c = Failures[index];
            var t = FlatTuning();
            t.FindShip("courier").TankTonnes = c.tankTonnes;
            var sim = NewSim(c.credits, t);
            sim.Advance(1_234_567); // past the first market events, so the queue has history
            byte[] before = StateBytes(sim);

            foreach (bool buyTopUp in new[] { true, false })
            {
                var result = sim.Dispatch(c.plan, buyTopUp);
                TestContext.WriteLine($"{c.name} (buyTopUp={buyTopUp}): {result.Reason}");
                Assert.IsFalse(result.Succeeded, c.name);
                StringAssert.Contains(c.reason, result.Reason, c.name);
                CollectionAssert.AreEqual(before, StateBytes(sim), $"{c.name}: a refused dispatch must change nothing");
            }
        }

        [Test]
        public void FailedDispatch_ShipInTransit_LeavesTheStateByteIdentical()
        {
            var sim = NewSim(1_000_000);
            Assert.IsTrue(sim.Dispatch(Plan(20, 2), true).Succeeded);
            sim.Advance(5_000_000);
            byte[] before = StateBytes(sim);
            var result = sim.Dispatch(Plan(5, 0), true);
            TestContext.WriteLine(result.Reason);
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual("Test Courier is in transit to Beta, arriving Y4 M01 D01. It can take a new voyage once it docks.", result.Reason);
            CollectionAssert.AreEqual(before, StateBytes(sim));
        }

        // ---- The top-up yes/no (owner's decision 1a) ----

        [Test]
        public void WithoutTopUp_AShortShipIsRefusedWithAReason_AndNothingChanges()
        {
            var sim = NewSim(100_000);
            Courier(sim).Water.Add(3_000, 1_500); // enough water; fuel and food short
            byte[] before = StateBytes(sim);

            var result = sim.Dispatch(Plan(20, 2), buyTopUp: false);
            TestContext.WriteLine(result.Reason);
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual("Test Courier is short of 8.514 t of fuel, 2.592 t of food for this voyage (top-up 41,911 credits). Press Provision first, or dispatch with the top-up.", result.Reason);
            CollectionAssert.AreEqual(before, StateBytes(sim));
        }

        [Test]
        public void WithTopUp_EndsInExactlyTheSameStateAsProvisionThenDispatch()
        {
            var a = NewSim(100_000);
            Assert.IsTrue(a.Dispatch(Plan(20, 2), buyTopUp: true).Succeeded);

            var b = NewSim(100_000);
            Assert.IsTrue(b.Provision(Plan(20, 2)).Succeeded);
            var afterProvision = b.Dispatch(Plan(20, 2), buyTopUp: false);
            Assert.IsTrue(afterProvision.Succeeded, afterProvision.Reason);

            var c = NewSim(100_000);
            Assert.IsTrue(c.Provision(Plan(20, 2)).Succeeded);
            Assert.IsTrue(c.Dispatch(Plan(20, 2), buyTopUp: true).Succeeded, "With nothing short, yes buys nothing more.");

            TestContext.WriteLine(State(a));
            CollectionAssert.AreEqual(StateBytes(a), StateBytes(b));
            CollectionAssert.AreEqual(StateBytes(a), StateBytes(c));

            a.Advance(HandArrivalTick);
            b.Advance(HandArrivalTick);
            CollectionAssert.AreEqual(StateBytes(a), StateBytes(b), "And they stay identical through arrival.");
        }

        [Test]
        public void WithoutTopUp_NothingShort_Departs()
        {
            var sim = NewSim(100_000);
            Assert.IsTrue(sim.Provision(Plan(20, 2)).Succeeded);
            long credits = sim.Credits;
            Assert.IsTrue(sim.Dispatch(Plan(20, 2), buyTopUp: false).Succeeded);
            Assert.AreEqual(credits, sim.Credits);
            Assert.IsTrue(Courier(sim).IsInTransit);
        }

        // ---- A ship in flight is refused by every voyage command ----

        [Test]
        public void QuoteProvisionAndDispatch_EachRefuseAShipInFlight_AndChangeNothing()
        {
            var sim = NewSim(1_000_000);
            Assert.IsTrue(sim.Dispatch(Plan(20, 2), true).Succeeded);
            sim.Advance(3_000_000);
            const string reason = "Test Courier is in transit to Beta, arriving Y4 M01 D01. It can take a new voyage once it docks.";

            foreach (var plan in new[] { Plan(20, 2), Plan(0, 0), new VoyagePlan(CourierId, Alpha) })
            {
                byte[] before = StateBytes(sim);

                var q = sim.Quote(plan);
                Assert.IsFalse(q.IsPlanValid);
                Assert.IsFalse(q.IsFeasible);
                CollectionAssert.AreEqual(new[] { reason }, q.Problems);
                CollectionAssert.AreEqual(before, StateBytes(sim), "Quote");

                var p = sim.Provision(plan);
                Assert.IsFalse(p.Succeeded);
                Assert.AreEqual(reason, p.Reason);
                CollectionAssert.AreEqual(before, StateBytes(sim), "Provision");

                foreach (bool buy in new[] { true, false })
                {
                    var d = sim.Dispatch(plan, buy);
                    Assert.IsFalse(d.Succeeded);
                    Assert.AreEqual(reason, d.Reason);
                    CollectionAssert.AreEqual(before, StateBytes(sim), $"Dispatch buyTopUp={buy}");
                }
            }
        }

        [Test]
        public void AfterDocking_TheShipCanBeQuotedFromItsNewPort()
        {
            var sim = NewSim(1_000_000);
            Assert.IsTrue(sim.Dispatch(Plan(20, 2), true).Succeeded);
            sim.Advance(HandArrivalTick);
            var back = sim.Quote(new VoyagePlan(CourierId, Alpha));
            Assert.IsTrue(back.IsFeasible, string.Join("\n", back.Problems));
            Assert.AreEqual(Beta, back.OriginId);
            Assert.AreEqual(HandArrivalTick, back.DepartureTick);
        }
    }
}
