using System;
using NUnit.Framework;
using SpaceTrade.Sim.Ships;

namespace SpaceTrade.Sim.Tests
{
    public class SupplyStockTests
    {
        private static SupplyStock NewStock() => new Ship(1, "S", "courier", "alcubierre_1_50", 0).Water;

        [Test]
        public void UsingEverythingLeft_ChargesAllRemainingCost_AndLeavesZero()
        {
            var s = NewStock();
            s.Add(1, 1);
            s.Add(1, 2);
            s.Add(1, 4); // 3 kg costing 7
            Assert.AreEqual(2, s.Use(1)); // 7 / 3 = 2.33 -> 2
            Assert.AreEqual(3, s.Use(1)); // 5 / 2 = 2.5 -> 3
            Assert.AreEqual(2, s.Use(1)); // the last kilogram takes whatever cost is left
            Assert.AreEqual(0, s.Kg);
            Assert.AreEqual(0, s.CostCredits);
        }

        [Test]
        public void UsingWholeStockAtOnce_ChargesWholeCost()
        {
            var s = NewStock();
            s.Add(9_999, 12_345);
            Assert.AreEqual(12_345, s.Use(9_999));
            Assert.AreEqual(0, s.Kg);
            Assert.AreEqual(0, s.CostCredits);
            Assert.AreEqual(12_345, SupplyStock.CostOfUsing(9_999, 12_345, 9_999));
        }

        [Test]
        public void CannotUseMoreThanAboard()
        {
            var s = NewStock();
            s.Add(10, 10);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.Use(11));
            Assert.AreEqual(10, s.Kg);
            Assert.AreEqual(10, s.CostCredits);
        }

        [Test]
        public void CostWithNoQuantity_IsRefused()
        {
            var s = NewStock();
            Assert.Throws<ArgumentException>(() => s.Add(0, 5));
            Assert.AreEqual(0, s.Kg);
            Assert.AreEqual(0, s.CostCredits);
            s.Add(0, 0);
            Assert.AreEqual(0, s.CostCredits);
        }

        [Test]
        public void TotalSpent_AlwaysEquals_Expensed_PlusCostAboard()
        {
            var rng = new SeededRandom(20261124);
            var s = NewStock();
            long spent = 0, expensed = 0;
            int emptied = 0;
            for (int step = 0; step < 20_000; step++)
            {
                if (s.Kg == 0 || rng.Range(0, 1) == 0)
                {
                    long kg = rng.Range(0, 5_000);
                    long cost = kg == 0 ? 0 : rng.Range(0, 20_000);
                    s.Add(kg, cost);
                    spent += cost;
                }
                else
                {
                    long kg = rng.Range(0, 3) == 0 ? s.Kg : rng.Range(0, (int)s.Kg);
                    expensed += s.Use(kg);
                    if (s.Kg == 0) emptied++;
                }
                Assert.AreEqual(spent, expensed + s.CostCredits, $"step {step}");
                Assert.GreaterOrEqual(s.CostCredits, 0, $"step {step}");
                if (s.Kg == 0) Assert.AreEqual(0, s.CostCredits, $"step {step}: an empty stock must have zero cost");
            }
            TestContext.WriteLine($"20,000 steps: spent {spent}, expensed {expensed}, aboard {s.CostCredits}, emptied {emptied} times.");
            Assert.Greater(emptied, 100);
        }
    }
}
