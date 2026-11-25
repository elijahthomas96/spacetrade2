using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using SpaceTrade.Sim.Events;
using SpaceTrade.Sim.Ships;

namespace SpaceTrade.Sim.Voyages
{
    /// <summary>One commodity in a voyage's locked manifest, at the rate taken as fixed credits at dispatch.</summary>
    public sealed class LockedFreight
    {
        public string CommodityId { get; }
        public long Tonnes { get; }
        public long RatePerTonne { get; }
        public long Revenue => Tonnes * RatePerTonne;

        internal LockedFreight(string commodityId, long tonnes, long ratePerTonne)
        {
            CommodityId = commodityId;
            Tonnes = tonnes;
            RatePerTonne = ratePerTonne;
        }
    }

    /// <summary>
    /// The terms locked at dispatch (spec 6.6): manifest, bookings, drive, destination, arrival tick,
    /// the rates and fare as fixed credit amounts, and the supplies the voyage will use. Nothing here
    /// is read from the tuning after dispatch, so tuning edits never change a voyage in flight.
    /// </summary>
    public sealed class Voyage
    {
        private readonly List<LockedFreight> _freight;

        public int ShipId { get; }
        public int OriginId { get; }
        public int DestinationId { get; }
        public long DistanceMilliLy { get; }
        public string DriveId { get; }
        public long DepartureTick { get; }
        public long ArrivalTick { get; }
        public long TransitTicks => ArrivalTick - DepartureTick;

        /// <summary>Merged per commodity, in tuning order at dispatch.</summary>
        public IReadOnlyList<LockedFreight> Freight => _freight;
        public int Passengers { get; }
        public long FarePerPassenger { get; }

        /// <summary>Kilograms of each supply this voyage uses. They are taken out of the ship's stock once, at arrival.</summary>
        public long FuelUseKg { get; }
        public long FoodUseKg { get; }
        public long WaterUseKg { get; }

        public long FreightRevenue
        {
            get
            {
                long total = 0;
                foreach (var f in _freight) total = checked(total + f.Revenue);
                return total;
            }
        }

        public long FareRevenue => checked(Passengers * FarePerPassenger);

        /// <summary>What arrival pays: freight plus fares at the locked amounts.</summary>
        public long Payout => checked(FreightRevenue + FareRevenue);

        internal Voyage(int shipId, int originId, int destinationId, long distanceMilliLy, string driveId, long departureTick, long arrivalTick,
            IEnumerable<LockedFreight> freight, int passengers, long farePerPassenger, long fuelUseKg, long foodUseKg, long waterUseKg)
        {
            if (arrivalTick < departureTick) throw new ArgumentOutOfRangeException(nameof(arrivalTick), arrivalTick, "Arrival cannot be before departure.");
            ShipId = shipId;
            OriginId = originId;
            DestinationId = destinationId;
            DistanceMilliLy = distanceMilliLy;
            DriveId = driveId ?? throw new ArgumentNullException(nameof(driveId));
            DepartureTick = departureTick;
            ArrivalTick = arrivalTick;
            _freight = new List<LockedFreight>(freight);
            Passengers = passengers;
            FarePerPassenger = farePerPassenger;
            FuelUseKg = fuelUseKg;
            FoodUseKg = foodUseKg;
            WaterUseKg = waterUseKg;
        }

        public long UseKg(SupplyKind kind)
        {
            switch (kind)
            {
                case SupplyKind.Fuel: return FuelUseKg;
                case SupplyKind.Food: return FoodUseKg;
                case SupplyKind.Water: return WaterUseKg;
                default: throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown supply.");
            }
        }

        /// <summary>
        /// How much of <paramref name="kind"/> counts as used by <paramref name="tick"/>, for display only:
        /// the locked use x elapsed ticks / transit ticks, rounded once by the one rounding rule.
        /// Zero at departure, the whole locked use at arrival.
        /// </summary>
        public long UsedSoFarKg(SupplyKind kind, long tick)
        {
            long use = UseKg(kind);
            long elapsed = Math.Min(Math.Max(0, tick - DepartureTick), TransitTicks);
            if (elapsed == TransitTicks) return use;
            return IntMath.DivideRounded((BigInteger)use * elapsed, TransitTicks);
        }

        /// <summary>Every stored value in a fixed text layout, for comparing states exactly.</summary>
        public string DescribeState()
        {
            var sb = new StringBuilder();
            sb.Append("voyage ship=").Append(ShipId).Append(' ').Append(OriginId).Append("->").Append(DestinationId)
              .Append(" dist=").Append(DistanceMilliLy).Append(" drive=").Append(DriveId)
              .Append(" depart=").Append(DepartureTick).Append(" arrive=").Append(ArrivalTick)
              .Append(" pax=").Append(Passengers).Append('@').Append(FarePerPassenger)
              .Append(" use fuel=").Append(FuelUseKg).Append(" food=").Append(FoodUseKg).Append(" water=").Append(WaterUseKg);
            foreach (var f in _freight) sb.Append(" freight ").Append(f.CommodityId).Append(' ').Append(f.Tonnes).Append('@').Append(f.RatePerTonne);
            return sb.ToString();
        }
    }

    /// <summary>A ship reaching its destination: settles the voyage at its locked terms (spec 6.7).</summary>
    internal sealed class ArrivalEvent : SimEvent
    {
        private readonly int _shipId;

        public ArrivalEvent(int shipId)
        {
            _shipId = shipId;
        }

        public override EventPhase Phase => EventPhase.Arrival;
        public override int EntityId => _shipId;

        public override void Apply(Simulation sim) => sim.SettleArrival(_shipId);
    }
}
