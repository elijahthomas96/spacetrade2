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

        [TestCase(0, 7, 0)]
        [TestCase(1, 7, 1)]
        [TestCase(7, 7, 1)]
        [TestCase(8, 7, 2)]
        [TestCase(8_387_280_000, 1_000_000, 8_388)]
        public void DivideCeiling(long num, long den, long expected)
        {
            Assert.AreEqual(expected, IntMath.DivideCeiling(num, den));
        }

        [TestCase(3_360, "3.36 t")]
        [TestCase(20_000, "20 t")]
        [TestCase(8_388, "8.388 t")]
        [TestCase(1_500, "1.5 t")]
        [TestCase(5, "0.005 t")]
        [TestCase(1_234_567, "1,234.567 t")]
        public void FormatTonnes(long kg, string expected)
        {
            Assert.AreEqual(expected, Format.Tonnes(kg));
        }

        [Test]
        public void FormatCredits()
        {
            Assert.AreEqual("43,733", Format.Credits(43_733));
            Assert.AreEqual("-5,665", Format.Credits(-5_665));
            Assert.AreEqual("0", Format.Credits(0));
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
