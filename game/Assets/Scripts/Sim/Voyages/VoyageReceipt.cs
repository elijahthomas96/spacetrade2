using System.Collections.Generic;
using System.Text;
using SpaceTrade.Sim.Ships;

namespace SpaceTrade.Sim.Voyages
{
    /// <summary>One supply a voyage used up at arrival, at its acquisition cost.</summary>
    public sealed class ConsumedSupply
    {
        public SupplyKind Kind { get; }
        public long Kg { get; }
        public long Cost { get; }

        internal ConsumedSupply(SupplyKind kind, long kg, long cost)
        {
            Kind = kind;
            Kg = kg;
            Cost = cost;
        }
    }

    /// <summary>
    /// The delivery receipt (spec 6.7): fares, freight revenue, supplies consumed at acquisition cost,
    /// elapsed game time, and net profit. The simulation keeps every receipt, in arrival order.
    /// </summary>
    public sealed class VoyageReceipt
    {
        private readonly List<LockedFreight> _freight;
        private readonly List<ConsumedSupply> _supplies;

        public int ShipId { get; }
        public string ShipName { get; }
        public int OriginId { get; }
        public int DestinationId { get; }
        public long DepartureTick { get; }
        public long ArrivalTick { get; }
        public long ElapsedTicks => ArrivalTick - DepartureTick;

        public IReadOnlyList<LockedFreight> Freight => _freight;
        public int Passengers { get; }
        public long FarePerPassenger { get; }
        public long FreightRevenue { get; }
        public long FareRevenue { get; }
        public long GrossRevenue => FreightRevenue + FareRevenue;

        /// <summary>Fuel, food, water, in that order.</summary>
        public IReadOnlyList<ConsumedSupply> Supplies => _supplies;
        public long SuppliesCost { get; }
        public long NetProfit => GrossRevenue - SuppliesCost;

        internal VoyageReceipt(Ship ship, Voyage voyage, IEnumerable<ConsumedSupply> supplies)
        {
            ShipId = ship.Id;
            ShipName = ship.Name;
            OriginId = voyage.OriginId;
            DestinationId = voyage.DestinationId;
            DepartureTick = voyage.DepartureTick;
            ArrivalTick = voyage.ArrivalTick;
            _freight = new List<LockedFreight>(voyage.Freight);
            Passengers = voyage.Passengers;
            FarePerPassenger = voyage.FarePerPassenger;
            FreightRevenue = voyage.FreightRevenue;
            FareRevenue = voyage.FareRevenue;
            _supplies = new List<ConsumedSupply>(supplies);
            long cost = 0;
            foreach (var s in _supplies) cost = checked(cost + s.Cost);
            SuppliesCost = cost;
        }

        /// <summary>Every stored value in a fixed text layout, for comparing states exactly.</summary>
        public string DescribeState()
        {
            var sb = new StringBuilder();
            sb.Append("receipt ship=").Append(ShipId).Append(" \"").Append(ShipName).Append("\" ").Append(OriginId).Append("->").Append(DestinationId)
              .Append(" depart=").Append(DepartureTick).Append(" arrive=").Append(ArrivalTick)
              .Append(" pax=").Append(Passengers).Append('@').Append(FarePerPassenger)
              .Append(" freightRevenue=").Append(FreightRevenue).Append(" fareRevenue=").Append(FareRevenue);
            foreach (var f in _freight) sb.Append(" freight ").Append(f.CommodityId).Append(' ').Append(f.Tonnes).Append('@').Append(f.RatePerTonne);
            foreach (var s in _supplies) sb.Append(' ').Append(s.Kind).Append('=').Append(s.Kg).Append("kg/").Append(s.Cost).Append("cr");
            return sb.ToString();
        }
    }
}
