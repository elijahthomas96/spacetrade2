using System.Globalization;

namespace SpaceTrade.Sim
{
    /// <summary>Number formats for messages the player reads. Whole numbers only.</summary>
    public static class Format
    {
        /// <summary>Kilograms shown as tonnes with up to three decimals and no trailing zeros: 3360 gives "3.36 t".</summary>
        public static string Tonnes(long kg)
        {
            string sign = kg < 0 ? "-" : "";
            long abs = kg < 0 ? -kg : kg;
            string whole = (abs / 1000).ToString("N0", CultureInfo.InvariantCulture);
            long frac = abs % 1000;
            if (frac == 0) return $"{sign}{whole} t";
            return $"{sign}{whole}.{frac.ToString("000", CultureInfo.InvariantCulture).TrimEnd('0')} t";
        }

        /// <summary>Credits with thousands separators: 43733 gives "43,733".</summary>
        public static string Credits(long credits) => credits.ToString("N0", CultureInfo.InvariantCulture);
    }
}
