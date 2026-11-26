using System;
using System.Collections.Generic;
using System.Text;
using SpaceTrade.Sim.Ships;

namespace SpaceTrade.Sim.Voyages
{
    /// <summary>
    /// Running totals of deliveries (spec 6.7). The simulation keeps one for every delivery since the game began, including
    /// deliveries whose receipts were dropped under <c>receipts.keepNewest</c>, so the totals always cover everything.
    /// </summary>
    public sealed class DeliveryTotals
    {
        public long Deliveries { get; private set; }
        public long FreightTonnes { get; private set; }
        public long Passengers { get; private set; }
        public long FreightRevenue { get; private set; }
        public long FareRevenue { get; private set; }
        public long GrossRevenue => FreightRevenue + FareRevenue;
        public long FuelKg { get; private set; }
        public long FuelCost { get; private set; }
        public long FoodKg { get; private set; }
        public long FoodCost { get; private set; }
        public long WaterKg { get; private set; }
        public long WaterCost { get; private set; }
        public long SuppliesCost => FuelCost + FoodCost + WaterCost;
        public long NetProfit => GrossRevenue - SuppliesCost;

        internal DeliveryTotals()
        {
        }

        /// <summary>Totals read back from a save. Every figure must be zero or more.</summary>
        internal DeliveryTotals(long deliveries, long freightTonnes, long passengers, long freightRevenue, long fareRevenue,
            long fuelKg, long fuelCost, long foodKg, long foodCost, long waterKg, long waterCost)
        {
            Deliveries = deliveries;
            FreightTonnes = freightTonnes;
            Passengers = passengers;
            FreightRevenue = freightRevenue;
            FareRevenue = fareRevenue;
            FuelKg = fuelKg;
            FuelCost = fuelCost;
            FoodKg = foodKg;
            FoodCost = foodCost;
            WaterKg = waterKg;
            WaterCost = waterCost;
            foreach (long x in Figures())
                if (x < 0) throw new ArgumentException("Delivery totals cannot be negative.");
        }

        /// <summary>The totals of the given receipts.</summary>
        public static DeliveryTotals Of(IEnumerable<VoyageReceipt> receipts)
        {
            var t = new DeliveryTotals();
            foreach (var r in receipts) t.Add(r);
            return t;
        }

        internal void Add(VoyageReceipt r)
        {
            Deliveries = checked(Deliveries + 1);
            foreach (var f in r.Freight) FreightTonnes = checked(FreightTonnes + f.Tonnes);
            Passengers = checked(Passengers + r.Passengers);
            FreightRevenue = checked(FreightRevenue + r.FreightRevenue);
            FareRevenue = checked(FareRevenue + r.FareRevenue);
            foreach (var s in r.Supplies)
            {
                switch (s.Kind)
                {
                    case SupplyKind.Fuel: FuelKg = checked(FuelKg + s.Kg); FuelCost = checked(FuelCost + s.Cost); break;
                    case SupplyKind.Food: FoodKg = checked(FoodKg + s.Kg); FoodCost = checked(FoodCost + s.Cost); break;
                    case SupplyKind.Water: WaterKg = checked(WaterKg + s.Kg); WaterCost = checked(WaterCost + s.Cost); break;
                }
            }
        }

        /// <summary>What was added after <paramref name="earlier"/>, a copy of these totals taken before. Throws if any figure would go below zero.</summary>
        public DeliveryTotals Since(DeliveryTotals earlier)
        {
            if (earlier == null) throw new ArgumentNullException(nameof(earlier));
            return new DeliveryTotals(Deliveries - earlier.Deliveries, FreightTonnes - earlier.FreightTonnes, Passengers - earlier.Passengers,
                FreightRevenue - earlier.FreightRevenue, FareRevenue - earlier.FareRevenue, FuelKg - earlier.FuelKg, FuelCost - earlier.FuelCost,
                FoodKg - earlier.FoodKg, FoodCost - earlier.FoodCost, WaterKg - earlier.WaterKg, WaterCost - earlier.WaterCost);
        }

        internal DeliveryTotals Copy() => Since(new DeliveryTotals());

        private IEnumerable<long> Figures()
        {
            yield return Deliveries;
            yield return FreightTonnes;
            yield return Passengers;
            yield return FreightRevenue;
            yield return FareRevenue;
            yield return FuelKg;
            yield return FuelCost;
            yield return FoodKg;
            yield return FoodCost;
            yield return WaterKg;
            yield return WaterCost;
        }

        /// <summary>Every figure in a fixed text layout, for comparing states exactly.</summary>
        public string DescribeState()
        {
            var sb = new StringBuilder();
            sb.Append("deliveries count=").Append(Deliveries).Append(" tonnes=").Append(FreightTonnes).Append(" pax=").Append(Passengers)
              .Append(" freightRevenue=").Append(FreightRevenue).Append(" fareRevenue=").Append(FareRevenue)
              .Append(" Fuel=").Append(FuelKg).Append("kg/").Append(FuelCost).Append("cr")
              .Append(" Food=").Append(FoodKg).Append("kg/").Append(FoodCost).Append("cr")
              .Append(" Water=").Append(WaterKg).Append("kg/").Append(WaterCost).Append("cr");
            return sb.ToString();
        }
    }
}
