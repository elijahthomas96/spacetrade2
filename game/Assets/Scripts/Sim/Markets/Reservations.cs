using System.Collections.Generic;

namespace SpaceTrade.Sim.Markets
{
    public readonly struct FreightLine
    {
        public string CommodityId { get; }
        public int Tonnes { get; }

        public FreightLine(string commodityId, int tonnes)
        {
            CommodityId = commodityId;
            Tonnes = tonnes;
        }
    }

    /// <summary>Everything one voyage wants to take from the market: freight from origin to destination, and passenger seats.</summary>
    public sealed class ReservationRequest
    {
        public int OriginId { get; }
        public int DestinationId { get; }
        public IReadOnlyList<FreightLine> Freight { get; }
        public int Passengers { get; }

        public ReservationRequest(int originId, int destinationId, IEnumerable<FreightLine> freight = null, int passengers = 0)
        {
            OriginId = originId;
            DestinationId = destinationId;
            Freight = freight == null ? new List<FreightLine>() : new List<FreightLine>(freight);
            Passengers = passengers;
        }
    }

    public sealed class ReservedFreight
    {
        public string CommodityId { get; }
        public int Tonnes { get; }
        public long RatePerTonne { get; }
        public long Revenue => Tonnes * RatePerTonne;

        internal ReservedFreight(string commodityId, int tonnes, long ratePerTonne)
        {
            CommodityId = commodityId;
            Tonnes = tonnes;
            RatePerTonne = ratePerTonne;
        }
    }

    /// <summary>
    /// What a successful <see cref="Market.TryReserve"/> took, with the rates and fare locked at
    /// that moment. Freight lines are merged per commodity, in tuning order.
    /// </summary>
    public sealed class MarketReservation
    {
        internal Market Owner { get; }
        public int OriginId { get; }
        public int DestinationId { get; }
        public IReadOnlyList<ReservedFreight> Freight { get; }
        public int Passengers { get; }
        public long FarePerPassenger { get; }
        public bool IsReleased { get; internal set; }

        public long FreightRevenue
        {
            get
            {
                long total = 0;
                foreach (var f in Freight) total += f.Revenue;
                return total;
            }
        }

        public long FareRevenue => Passengers * FarePerPassenger;

        internal MarketReservation(Market owner, int originId, int destinationId, IReadOnlyList<ReservedFreight> freight, int passengers, long farePerPassenger)
        {
            Owner = owner;
            OriginId = originId;
            DestinationId = destinationId;
            Freight = freight;
            Passengers = passengers;
            FarePerPassenger = farePerPassenger;
        }
    }
}
