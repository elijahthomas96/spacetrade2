using NUnit.Framework;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Tuning;

namespace SpaceTrade.Sim.Tests
{
    public class MarketMathTests
    {
        [TestCase(5, 2, 3)]    // 2.5 rounds away from zero
        [TestCase(3, 2, 2)]    // 1.5 rounds away from zero
        [TestCase(7, 3, 2)]    // 2.33 rounds down
        [TestCase(8, 3, 3)]    // 2.67 rounds up
        [TestCase(1, 4, 0)]    // 0.25 rounds down
        [TestCase(0, 9, 0)]
        [TestCase(6, 3, 2)]
        [TestCase(-5, 2, -3)]  // -2.5 rounds away from zero
        [TestCase(-3, 2, -2)]  // -1.5 rounds away from zero
        [TestCase(-7, 3, -2)]  // -2.33
        [TestCase(-8, 3, -3)]  // -2.67
        [TestCase(-1, 4, 0)]   // -0.25
        public void DivideRounded_NearestWithHalvesAwayFromZero(long num, long den, long expected)
        {
            Assert.AreEqual(expected, IntMath.DivideRounded(num, den));
        }

        [Test]
        public void DistancePrice_FiveLightYears_IsThreeTimesBase_WithDefaultRates()
        {
            var rates = TestTuning.Load().Rates; // 100 percent + 40 percent per ly
            Assert.AreEqual(3_000, MarketMath.DistancePrice(1_000, 5_000, 100, rates));
            Assert.AreEqual(3_600, MarketMath.DistancePrice(1_000, 5_000, 120, rates));
            Assert.AreEqual(2_400, MarketMath.DistancePrice(1_000, 5_000, 80, rates));
        }

        [Test]
        public void DistancePrice_RoundsOnceAtTheEnd()
        {
            var rates = TestTuning.Load().Rates;
            // 1,500 x (100 + 40 x 4.321) percent x 97 percent = 1,500 x 2.7284 x 0.97 = 3,969.822
            Assert.AreEqual(3_970, MarketMath.DistancePrice(1_500, 4_321, 97, rates));
        }

        [Test]
        public void PoolCap_ScalesWithPopulation_AndHubBonus()
        {
            var p = TestTuning.Load().Passengers; // base 4, +1 per 10 million, hub +100 percent
            Assert.AreEqual(10, MarketMath.PoolCap(30, 30, false, p));
            Assert.AreEqual(20, MarketMath.PoolCap(30, 30, true, p));
            Assert.AreEqual(4, MarketMath.PoolCap(1, 2, false, p));   // 4.3
            Assert.AreEqual(5, MarketMath.PoolCap(3, 2, false, p));   // 4.5 rounds up
            Assert.AreEqual(9, MarketMath.PoolCap(1, 2, true, p));    // 8.6
        }

        [Test]
        public void StartingAmount_IsPercentOfCap()
        {
            Assert.AreEqual(60, MarketMath.StartingAmount(60, 100));
            Assert.AreEqual(30, MarketMath.StartingAmount(60, 50));
            Assert.AreEqual(0, MarketMath.StartingAmount(60, 0));
            Assert.AreEqual(3, MarketMath.StartingAmount(5, 50)); // 2.5 rounds up
        }

        private static ProvisionTuning Provisions(int floor, int ceiling)
        {
            var p = TestTuning.Load().Provisions;
            p.CombinedModifierMinPercent = floor;
            p.CombinedModifierMaxPercent = ceiling;
            return p;
        }

        [Test]
        public void ProvisionPrice_NoModifiers_IsBaseTimesRoll()
        {
            Assert.AreEqual(2_000, MarketMath.ProvisionPrice(2_000, new int[0], 100, Provisions(50, 200)));
            Assert.AreEqual(2_200, MarketMath.ProvisionPrice(2_000, new int[0], 110, Provisions(50, 200)));
        }

        [Test]
        public void ProvisionPrice_TwoRefineryModifiers_Multiply()
        {
            // 70 percent x 70 percent = 49 percent, with the floor lowered so it does not apply.
            Assert.AreEqual(980, MarketMath.ProvisionPrice(2_000, new[] { 70, 70 }, 100, Provisions(10, 200)));
            // 2,000 x 0.49 x 1.07 = 1,048.6
            Assert.AreEqual(1_049, MarketMath.ProvisionPrice(2_000, new[] { 70, 70 }, 107, Provisions(10, 200)));
        }

        [Test]
        public void ProvisionPrice_CombinedModifierBelowFloor_IsClampedToFloor()
        {
            // 49 percent is below the default 50 percent floor.
            Assert.AreEqual(1_000, MarketMath.ProvisionPrice(2_000, new[] { 70, 70 }, 100, Provisions(50, 200)));
            Assert.AreEqual(500, MarketMath.ProvisionPrice(2_000, new[] { 25 }, 100, Provisions(25, 200)));
            Assert.AreEqual(1_000, MarketMath.ProvisionPrice(2_000, new[] { 25 }, 100, Provisions(50, 200)));
        }

        [Test]
        public void ProvisionPrice_CombinedModifierAboveCeiling_IsClampedToCeiling()
        {
            // 150 percent x 150 percent = 225 percent, above the default 200 percent ceiling.
            Assert.AreEqual(4_000, MarketMath.ProvisionPrice(2_000, new[] { 150, 150 }, 100, Provisions(50, 200)));
        }

        [Test]
        public void ProvisionPrice_RoundsOnceAtTheEnd()
        {
            // 1,999 x 0.70 x 1.01 = 1,413.293
            Assert.AreEqual(1_413, MarketMath.ProvisionPrice(1_999, new[] { 70 }, 101, Provisions(50, 200)));
            // 1,999 x 0.70 x 1.20 x 1.03 = 1,729.535 (three factors, one rounding)
            Assert.AreEqual(1_730, MarketMath.ProvisionPrice(1_999, new[] { 70, 120 }, 103, Provisions(50, 200)));
        }
    }
}
