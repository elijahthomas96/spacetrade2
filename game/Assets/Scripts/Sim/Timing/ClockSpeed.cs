using System;

namespace SpaceTrade.Sim.Timing
{
    /// <summary>
    /// The only three clock speeds the game allows (spec 3.2). Do not add others.
    /// </summary>
    public enum ClockSpeed
    {
        X1 = 1,
        X3 = 3,
        X5 = 5,
    }

    public static class ClockSpeeds
    {
        public static bool IsValid(ClockSpeed speed) =>
            speed == ClockSpeed.X1 || speed == ClockSpeed.X3 || speed == ClockSpeed.X5;

        public static int Multiplier(ClockSpeed speed)
        {
            if (!IsValid(speed)) throw new ArgumentOutOfRangeException(nameof(speed), (int)speed, "Speed must be 1x, 3x, or 5x.");
            return (int)speed;
        }
    }
}
