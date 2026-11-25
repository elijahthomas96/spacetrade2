using System;
using NUnit.Framework;
using SpaceTrade.Sim.Timing;

namespace SpaceTrade.Sim.Tests
{
    public class TimeUnitTests
    {
        [Test]
        public void Units_MatchSpec()
        {
            Assert.AreEqual(10_000, GameTime.TicksPerDay);
            Assert.AreEqual(360, GameTime.DaysPerYear);
            Assert.AreEqual(3_600_000, GameTime.TicksPerYear);
            Assert.AreEqual(60L * 60 * 1000, GameTime.RealMillisecondsFor(GameTime.TicksPerYear, ClockSpeed.X1), "One game year is one real hour at 1x.");
            Assert.AreEqual(10_000, GameTime.RealMillisecondsFor(GameTime.TicksPerDay, ClockSpeed.X1), "One game day is ten real seconds at 1x.");
        }

        [TestCase(0L, "Y1 M01 D01")]
        [TestCase(9_999L, "Y1 M01 D01")]
        [TestCase(10_000L, "Y1 M01 D02")]
        [TestCase(299_999L, "Y1 M01 D30")]
        [TestCase(300_000L, "Y1 M02 D01")]
        [TestCase(3_599_999L, "Y1 M12 D30")]
        [TestCase(3_600_000L, "Y2 M01 D01")]
        [TestCase(2L * 3_600_000 + 3 * 300_000 + 11 * 10_000, "Y3 M04 D12")]
        public void Calendar_FormatsTick(long tick, string expected)
        {
            Assert.AreEqual(expected, GameDate.FromTick(tick).ToString());
        }

        [Test]
        public void Calendar_RejectsNegativeTick()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => GameDate.FromTick(-1));
        }
    }
}
