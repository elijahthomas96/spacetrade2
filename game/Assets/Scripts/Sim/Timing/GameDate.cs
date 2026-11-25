using System;

namespace SpaceTrade.Sim.Timing
{
    /// <summary>
    /// Calendar date for a tick (spec 3.1). Tick 0 is Y1 M01 D01.
    /// </summary>
    public readonly struct GameDate : IEquatable<GameDate>
    {
        public int Year { get; }
        public int Month { get; }
        public int Day { get; }

        public GameDate(int year, int month, int day)
        {
            Year = year;
            Month = month;
            Day = day;
        }

        public static GameDate FromTick(long tick)
        {
            if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick), tick, "Tick cannot be negative.");
            long dayIndex = tick / GameTime.TicksPerDay;
            long yearIndex = dayIndex / GameTime.DaysPerYear;
            long dayOfYear = dayIndex % GameTime.DaysPerYear;
            int month = (int)(dayOfYear / GameTime.DaysPerMonth) + 1;
            int day = (int)(dayOfYear % GameTime.DaysPerMonth) + 1;
            return new GameDate(checked((int)(yearIndex + 1)), month, day);
        }

        public override string ToString() => $"Y{Year} M{Month:00} D{Day:00}";

        public bool Equals(GameDate other) => Year == other.Year && Month == other.Month && Day == other.Day;
        public override bool Equals(object obj) => obj is GameDate other && Equals(other);
        public override int GetHashCode() => (Year * 397 + Month) * 397 + Day;
    }
}
