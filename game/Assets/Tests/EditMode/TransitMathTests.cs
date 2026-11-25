using System;
using NUnit.Framework;
using SpaceTrade.Sim.Timing;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>The required timing table in spec 3.3.</summary>
    public class TransitMathTests
    {
        private const long Minute = 60_000;
        private const long Hour = 60 * Minute;

        [Test]
        public void Table_4_5ly_at_1_5c()
        {
            long ticks = TransitMath.TransitTicks(4_500, 1_500);
            Assert.AreEqual(3 * GameTime.TicksPerYear, ticks, "3 game years");
            Assert.AreEqual(3 * Hour, GameTime.RealMillisecondsFor(ticks, ClockSpeed.X1), "3 hours at 1x");
            Assert.AreEqual(1 * Hour, GameTime.RealMillisecondsFor(ticks, ClockSpeed.X3), "1 hour at 3x");
            Assert.AreEqual(36 * Minute, GameTime.RealMillisecondsFor(ticks, ClockSpeed.X5), "36 minutes at 5x");
        }

        [Test]
        public void Table_4_5ly_at_3_75c()
        {
            long ticks = TransitMath.TransitTicks(4_500, 3_750);
            Assert.AreEqual(12 * GameTime.TicksPerYear / 10, ticks, "1.2 game years");
            Assert.AreEqual(72 * Minute, GameTime.RealMillisecondsFor(ticks, ClockSpeed.X1), "72 minutes at 1x");
            Assert.AreEqual(24 * Minute, GameTime.RealMillisecondsFor(ticks, ClockSpeed.X3), "24 minutes at 3x");
            Assert.AreEqual(14 * Minute + 24_000, GameTime.RealMillisecondsFor(ticks, ClockSpeed.X5), "14 minutes 24 seconds at 5x");
        }

        [Test]
        public void TransitTicks_RoundsUp()
        {
            // 1 mly at 7 mc = 3,600,000 / 7 = 514,285.71... ticks
            Assert.AreEqual(514_286, TransitMath.TransitTicks(1, 7));
            Assert.AreEqual(0, TransitMath.TransitTicks(0, 1_500));
        }

        [Test]
        public void RealTime_RoundsUp()
        {
            Assert.AreEqual(1, GameTime.RealMillisecondsFor(1, ClockSpeed.X5));
            Assert.AreEqual(3, GameTime.RealMillisecondsFor(11, ClockSpeed.X5));
            Assert.AreEqual(0, GameTime.RealMillisecondsFor(0, ClockSpeed.X3));
        }

        [Test]
        public void TransitTicks_RejectsBadInput()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => TransitMath.TransitTicks(-1, 1_500));
            Assert.Throws<ArgumentOutOfRangeException>(() => TransitMath.TransitTicks(4_500, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => TransitMath.TransitTicks(4_500, -1_500));
        }
    }
}
