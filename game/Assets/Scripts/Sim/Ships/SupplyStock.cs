using System;

namespace SpaceTrade.Sim.Ships
{
    public enum SupplyKind
    {
        Fuel,
        Food,
        Water,
    }

    /// <summary>
    /// One supply aboard a ship: how much, and what it cost in total (spec 6.1). Buying moves credits
    /// into this stock at cost; using it moves a share of that cost onto the voyage that uses it.
    /// </summary>
    public sealed class SupplyStock
    {
        public SupplyKind Kind { get; }
        public long Kg { get; private set; }
        public long CostCredits { get; private set; }

        internal SupplyStock(SupplyKind kind)
        {
            Kind = kind;
        }

        /// <summary>
        /// What using <paramref name="useKg"/> out of a stock of <paramref name="stockKg"/> costing
        /// <paramref name="stockCost"/> is charged: its share of the cost, rounded once. Using the whole
        /// stock charges the whole cost, so an empty stock always has zero cost.
        /// </summary>
        public static long CostOfUsing(long stockKg, long stockCost, long useKg)
        {
            if (useKg < 0) throw new ArgumentOutOfRangeException(nameof(useKg), useKg, "Cannot use a negative amount.");
            if (useKg > stockKg) throw new ArgumentOutOfRangeException(nameof(useKg), useKg, $"Cannot use more than the {stockKg} kg in stock.");
            if (useKg == stockKg) return stockCost;
            return IntMath.DivideRounded((System.Numerics.BigInteger)stockCost * useKg, stockKg);
        }

        internal void Add(long kg, long cost)
        {
            if (kg < 0) throw new ArgumentOutOfRangeException(nameof(kg), kg, "Cannot add a negative amount.");
            if (cost < 0) throw new ArgumentOutOfRangeException(nameof(cost), cost, "Cost cannot be negative.");
            if (kg == 0 && cost != 0) throw new ArgumentException($"A cost of {cost} credits needs a quantity; nothing was added.", nameof(cost));
            Kg = checked(Kg + kg);
            CostCredits = checked(CostCredits + cost);
        }

        /// <summary>Removes <paramref name="kg"/> and returns the cost it carried (see <see cref="CostOfUsing"/>).</summary>
        internal long Use(long kg)
        {
            long cost = CostOfUsing(Kg, CostCredits, kg);
            Kg -= kg;
            CostCredits -= cost;
            return cost;
        }
    }
}
