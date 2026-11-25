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

        /// <summary>Credits with thousands separators: 43733 gives "43,733".</summary>
        public static string Credits(long credits) => credits.ToString("N0", CultureInfo.InvariantCulture);
    }
}
