using System;
using System.Collections.Generic;
using System.Numerics;
using SpaceTrade.Sim.Tuning;

namespace SpaceTrade.Sim.Yards
{
    /// <summary>Shipyard formulas (spec 7). Each is built exactly from whole numbers and rounded once.</summary>
    public static class YardMath
    {
        /// <summary>Stock limit: min + (max - min) x roll percent, rounded by the one rounding rule.</summary>
        public static int StockLimit(int min, int max, int rollPercent) =>
            (int)IntMath.DivideRounded((BigInteger)min * 100 + (BigInteger)(max - min) * rollPercent, 100);

        /// <summary>A yard price: tuning base price x the yard's price roll percent.</summary>
        public static long Price(long basePrice, int rollPercent) => IntMath.DivideRounded((BigInteger)basePrice * rollPercent, 100);

        /// <summary>The drive a bought ship comes with: the lowest speed in the tuning, the first listed if two are equally slow.</summary>
        public static DriveTuning SlowestDrive(TuningData tuning)
        {
            DriveTuning slowest = null;
            foreach (var d in tuning.Drives)
                if (slowest == null || d.SpeedMilliC < slowest.SpeedMilliC) slowest = d;
            return slowest ?? throw new InvalidOperationException("The tuning lists no drives.");
        }

        /// <summary>
        /// The name for a bought ship. While any name on the list is not already in the fleet, one of those
        /// is drawn. Once every name is in use, a name is drawn from the whole list and given the lowest
        /// number from 2 up that no ship in the fleet already has ("Tern 2"). Always exactly one draw.
        /// </summary>
        internal static string PickPurchaseName(SeededRandom random, IReadOnlyList<string> names, ICollection<string> fleetNames)
        {
            if (names.Count == 0) throw new InvalidOperationException("The tuning lists no ship names.");
            var unused = new List<string>();
            foreach (var n in names) if (!fleetNames.Contains(n)) unused.Add(n);
            if (unused.Count > 0) return unused[random.Range(0, unused.Count - 1)];

            string name = names[random.Range(0, names.Count - 1)];
            for (int i = 2; ; i++)
            {
                string candidate = $"{name} {i}";
                if (!fleetNames.Contains(candidate)) return candidate;
            }
        }
    }
}
