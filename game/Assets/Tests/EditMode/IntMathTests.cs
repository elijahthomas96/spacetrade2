using NUnit.Framework;
using SpaceTrade.Sim.World;

namespace SpaceTrade.Sim.Tests
{
    public class IntMathTests
    {
        [TestCase(0, 0)]
        [TestCase(1, 1)]
        [TestCase(2, 1)]
        [TestCase(3, 2)]
        [TestCase(15, 4)]
        [TestCase(16, 4)]
        [TestCase(20, 4)]
        [TestCase(21, 5)]
        [TestCase(9_000_000_000_000_000_000, 3_000_000_000)]
        public void RoundedSqrt(long n, long expected)
        {
            Assert.AreEqual(expected, IntMath.RoundedSqrt(n));
        }

        [Test]
        public void FloorSqrt_IsExactAcrossRange()
        {
            for (long n = 0; n < 100_000; n++)
            {
                long r = IntMath.FloorSqrt(n);
                Assert.IsTrue(r * r <= n && (r + 1) * (r + 1) > n, $"n={n} r={r}");
            }
        }

        [Test]
        public void Distance_ThreeFourFive()
        {
            Assert.AreEqual(5_000, Galaxy.Distance(0, 0, 3_000, 4_000));
            Assert.AreEqual(5_000, Galaxy.Distance(3_000, 4_000, 0, 0));
            Assert.AreEqual(1_414, Galaxy.Distance(0, 0, 1_000, 1_000));
        }
    }
}
