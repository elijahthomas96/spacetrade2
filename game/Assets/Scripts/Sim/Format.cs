using System.Globalization;

namespace SpaceTrade.Sim
{
    /// <summary>Number formats for messages the player reads. Whole numbers only.</summary>
    public static class Format
    {
        /// <summary>Kilograms shown as tonnes with up to three decimals and no trailing zeros: 3360 gives "3.36 t".</summary>
        public static string Tonnes(long kg) => Thousandths(kg, "t");

        /// <summary>Milli-light-years shown as light-years with up to three decimals: 4500 gives "4.5 ly".</summary>
        public static string LightYears(long milliLy) => Thousandths(milliLy, "ly");

        private static string Thousandths(long value, string unit)
        {
            string sign = value < 0 ? "-" : "";
            long abs = value < 0 ? -value : value;
            string whole = (abs / 1000).ToString("N0", CultureInfo.InvariantCulture);
            long frac = abs % 1000;
            if (frac == 0) return $"{sign}{whole} {unit}";
            return $"{sign}{whole}.{frac.ToString("000", CultureInfo.InvariantCulture).TrimEnd('0')} {unit}";
        }

        /// <summary>Whole real seconds as "2 h 05 min 07 s", "5 min 07 s", or "7 s".</summary>
        public static string RealDuration(long seconds)
        {
            if (seconds < 0) seconds = 0;
            long h = seconds / 3600, m = seconds / 60 % 60, s = seconds % 60;
            if (h > 0) return string.Format(CultureInfo.InvariantCulture, "{0} h {1:00} min {2:00} s", h, m, s);
            if (m > 0) return string.Format(CultureInfo.InvariantCulture, "{0} min {1:00} s", m, s);
            return string.Format(CultureInfo.InvariantCulture, "{0} s", s);
        }

        /// <summary>
        /// Game ticks as game years and days, exactly, with no rounding: "3 y 0 d", "1 y 72 d", "144 d", and a part day
        /// written out in full, as in "8 y 113.28 d" (a game day is 10,000 ticks, so at most four decimals).
        /// </summary>
        public static string GameDuration(long ticks)
        {
            if (ticks < 0) ticks = 0;
            long years = ticks / Timing.GameTime.TicksPerYear;
            long rest = ticks % Timing.GameTime.TicksPerYear;
            long days = rest / Timing.GameTime.TicksPerDay;
            long part = rest % Timing.GameTime.TicksPerDay;
            string d = days.ToString(CultureInfo.InvariantCulture);
            if (part != 0) d += "." + part.ToString("0000", CultureInfo.InvariantCulture).TrimEnd('0');
            return years > 0 ? $"{years.ToString("N0", CultureInfo.InvariantCulture)} y {d} d" : $"{d} d";
        }

        /// <summary>Credits with thousands separators: 43733 gives "43,733".</summary>
        public static string Credits(long credits) => credits.ToString("N0", CultureInfo.InvariantCulture);
    }
}
