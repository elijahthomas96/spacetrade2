using System;

namespace SpaceTrade.Sim.Timing
{
    /// <summary>
    /// Fixed time units (spec 3.1). One tick is one real millisecond at 1x.
    /// </summary>
    public static class GameTime
    {
        public const long TicksPerDay = 10_000;
        public const int DaysPerMonth = 30;
        public const int MonthsPerYear = 12;
        public const int DaysPerYear = DaysPerMonth * MonthsPerYear;
        public const long TicksPerYear = TicksPerDay * DaysPerYear;

        public const long RealMillisecondsPerTickAt1x = 1;
        public const long RealMicrosecondsPerTickAt1x = 1_000;

        /// <summary>
        /// Real milliseconds needed for the given number of game ticks at the given speed,
        /// rounded up so a countdown never reaches zero before the event.
        /// </summary>
        public static long RealMillisecondsFor(long ticks, ClockSpeed speed)
        {
            if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "Ticks cannot be negative.");
            long multiplier = ClockSpeeds.Multiplier(speed);
            long scaled = checked(ticks * RealMillisecondsPerTickAt1x);
            return (scaled + multiplier - 1) / multiplier;
        }
    }
}
