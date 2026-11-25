using System.Collections.Generic;
using System.Numerics;
using SpaceTrade.Sim.Tuning;

namespace SpaceTrade.Sim.Markets
{
    /// <summary>
    /// Market formulas (spec 5). Each builds an exact fraction from whole numbers and rounds once
    /// with <see cref="IntMath.DivideRounded"/>.
    /// </summary>
    public static class MarketMath
    {
        /// <summary>
        /// A published freight rate or passenger fare: base x distance factor x roll percent, where
        /// distance factor percent = distanceFactorBasePercent + distanceFactorPerLyPercent x light-years.
        /// </summary>
        public static long DistancePrice(long basePrice, long distanceMilliLy, int rollPercent, RateTuning rates)
        {
            // Distance factor in thousandths of a percent, so milli-ly need no division.
            BigInteger factor = (BigInteger)rates.DistanceFactorBasePercent * 1000 + (BigInteger)rates.DistanceFactorPerLyPercent * distanceMilliLy;
            return IntMath.DivideRounded(basePrice * factor * rollPercent, 100 * 1000 * 100);
        }

        /// <summary>
        /// Waiting-passenger room on one route: (poolCapBasePassengers + poolCapExtraPerTenMillionPopulation
        /// for every 10 million people at both ends, counted proportionally), raised by hubPoolBonusPercent
        /// when the origin has a passenger-producing industry.
        /// </summary>
        public static int PoolCap(int originPopulationMillions, int destinationPopulationMillions, bool originIsHub, PassengerTuning p)
        {
            BigInteger room = (BigInteger)p.PoolCapBasePassengers * 10 + (BigInteger)p.PoolCapExtraPerTenMillionPopulation * (originPopulationMillions + destinationPopulationMillions);
            int bonus = originIsHub ? p.HubPoolBonusPercent : 0;
            return (int)IntMath.DivideRounded(room * (100 + bonus), 10 * 100);
        }

        /// <summary>Starting stock: cap x fill percent.</summary>
        public static int StartingAmount(int cap, int fillPercent) => (int)IntMath.DivideRounded((BigInteger)cap * fillPercent, 100);

        /// <summary>
        /// The combined industry modifier as an exact fraction: every listed percent multiplied
        /// together, then clamped to combinedModifierMinPercent..combinedModifierMaxPercent.
        /// No modifiers means 100 percent.
        /// </summary>
        public static void CombinedModifier(IEnumerable<int> percents, ProvisionTuning p, out BigInteger numerator, out BigInteger denominator)
        {
            numerator = 1;
            denominator = 1;
            foreach (int pct in percents)
            {
                numerator *= pct;
                denominator *= 100;
            }
            if (numerator * 100 < (BigInteger)p.CombinedModifierMinPercent * denominator)
            {
                numerator = p.CombinedModifierMinPercent;
                denominator = 100;
            }
            else if (numerator * 100 > (BigInteger)p.CombinedModifierMaxPercent * denominator)
            {
                numerator = p.CombinedModifierMaxPercent;
                denominator = 100;
            }
        }

        /// <summary>Provision price per tonne: base price x combined industry modifier x the system's stored roll percent.</summary>
        public static long ProvisionPrice(long basePricePerTonne, IEnumerable<int> modifierPercents, int rollPercent, ProvisionTuning p)
        {
            CombinedModifier(modifierPercents, p, out var num, out var den);
            return IntMath.DivideRounded(basePricePerTonne * num * rollPercent, den * 100);
        }
    }
}
