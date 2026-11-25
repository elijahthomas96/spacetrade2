using NUnit.Framework;
using SpaceTrade.Sim.Ships;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;

namespace SpaceTrade.Sim.Tests
{
    public class ProvisionTests
    {
        private static long ValueAboard(Ship s) => s.Fuel.CostCredits + s.Food.CostCredits + s.Water.CostCredits;

        [Test]
        public void Provision_BuysExactlyTheShortfall()
        {
            var sim = NewSim(100_000);
            var ship = Courier(sim);
            var before = sim.Quote(Plan(20, 2));

            var result = sim.Provision(Plan(20, 2));
            Assert.IsTrue(result.Succeeded, result.Reason);

            Assert.AreEqual(8_388, ship.Fuel.Kg);
            Assert.AreEqual(2_592, ship.Food.Kg);
            Assert.AreEqual(1_728, ship.Water.Kg);
            Assert.AreEqual(16_776, ship.Fuel.CostCredits);
            Assert.AreEqual(24_883, ship.Food.CostCredits);
            Assert.AreEqual(2_074, ship.Water.CostCredits);
            Assert.AreEqual(100_000 - 43_733, sim.Credits);
            Assert.AreEqual(100_000, sim.Credits + ValueAboard(ship), "Buying moves credits into supplies at cost.");

            var after = sim.Quote(Plan(20, 2));
            Assert.IsTrue(after.IsFeasible, string.Join("\n", after.Problems));
            foreach (var s in after.Supplies) Assert.AreEqual(0, s.ShortfallKg, s.Kind.ToString());
            Assert.AreEqual(0, after.TopUpCost);
            Assert.AreEqual(before.OperatingCost, after.OperatingCost);
            Assert.AreEqual(before.NetProfit, after.NetProfit);
            Assert.AreEqual(before.Fuel.RequiredKg, after.Fuel.RequiredKg);
        }

        [Test]
        public void Provision_WithLeftovers_BuysOnlyWhatIsMissing()
        {
            var sim = NewSim(100_000);
            var ship = Courier(sim);
            ship.Water.Add(3_000, 1_500);
            ship.Food.Add(1_000, 5_000);

            Assert.IsTrue(sim.Provision(Plan(20, 2)).Succeeded);
            Assert.AreEqual(3_000, ship.Water.Kg, "Enough water was aboard; none bought.");
            Assert.AreEqual(1_500, ship.Water.CostCredits);
            Assert.AreEqual(2_592, ship.Food.Kg);
            Assert.AreEqual(5_000 + 15_283, ship.Food.CostCredits);
            Assert.AreEqual(8_514, ship.Fuel.Kg);
            Assert.AreEqual(100_000 - 32_311, sim.Credits);
        }

        [Test]
        public void Provision_Twice_SecondBuysNothing()
        {
            var sim = NewSim(100_000);
            Assert.IsTrue(sim.Provision(Plan(20, 2)).Succeeded);
            string state = State(sim);
            Assert.IsTrue(sim.Provision(Plan(20, 2)).Succeeded);
            Assert.AreEqual(state, State(sim));
        }

        [Test]
        public void Provision_RefusedWhenInfeasible_ChangesNothing()
        {
            var sim = NewSim(100_000);
            string before = State(sim);
            var result = sim.Provision(Plan(20, 5)); // 5 passengers, 4 berths
            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains("Book 1 fewer passenger", result.Reason);
            Assert.AreEqual(before, State(sim));

            var hold = sim.Provision(Plan(30, 0));
            Assert.IsFalse(hold.Succeeded);
            StringAssert.Contains("Unload 3 t of freight", hold.Reason);
            Assert.AreEqual(before, State(sim));
        }

        [Test]
        public void Provision_RefusedWhenUnaffordable_ChangesNothing()
        {
            var sim = NewSim(43_732);
            string before = State(sim);
            var result = sim.Provision(Plan(20, 2));
            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains("You need 1 more credit.", result.Reason);
            Assert.AreEqual(before, State(sim));
        }

        [Test]
        public void UsingTheQuotedSupplies_ExpensesExactlyTheQuotedCost()
        {
            var sim = NewSim(100_000);
            var ship = Courier(sim);
            ship.Water.Add(3_000, 1_500);
            ship.Food.Add(1_000, 5_000);
            var q = sim.Quote(Plan(20, 2));
            Assert.IsTrue(sim.Provision(Plan(20, 2)).Succeeded);

            long expensed = 0;
            foreach (var s in q.Supplies)
            {
                long cost = ship.Supply(s.Kind).Use(s.RequiredKg);
                Assert.AreEqual(s.UsedCost, cost, s.Kind.ToString());
                expensed += cost;
            }
            Assert.AreEqual(q.OperatingCost, expensed);
            Assert.AreEqual(1_272, ship.Water.Kg);
            Assert.AreEqual(636, ship.Water.CostCredits, "Leftover water keeps the rest of its cost.");
            Assert.AreEqual(0, ship.Fuel.Kg);
            Assert.AreEqual(0, ship.Fuel.CostCredits);
            Assert.AreEqual(0, ship.Food.Kg);
            Assert.AreEqual(0, ship.Food.CostCredits);
        }
    }
}
