using System.Numerics;
using SpaceTrade.Sim.World;

namespace SpaceTrade.Sim.Voyages
{
    /// <summary>
    /// Where a ship is at the current tick, for display only (spec 9.1). Worked out from the locked voyage terms and the
    /// tick; never stored, never saved, and it changes nothing. Positions are galaxy coordinates in milli-light-years.
    /// </summary>
    public sealed class ShipPosition
    {
        /// <summary>Progress is in hundredths of a percent: 10,000 is 100 percent.</summary>
        public const long FullProgress = 10_000;

        public int ShipId { get; }
        public long XMilliLy { get; }
        public long YMilliLy { get; }

        /// <summary>The system the ship is docked at, or null while it is in transit.</summary>
        public int? DockedSystemId { get; }
        public bool InTransit => !DockedSystemId.HasValue;

        /// <summary>Where the voyage in progress started and is going; null while docked.</summary>
        public int? OriginId { get; }
        public int? DestinationId { get; }
        public long DepartureTick { get; }
        public long ArrivalTick { get; }

        /// <summary>Ticks flown so far and the voyage's locked transit ticks; both 0 while docked.</summary>
        public long ElapsedTicks { get; }
        public long TransitTicks { get; }

        /// <summary>The voyage's distance and how far the ship has flown, rounded down (spec 5.5); both 0 while docked.</summary>
        public long DistanceMilliLy { get; }
        public long TravelledMilliLy { get; }

        /// <summary>
        /// Hundredths of a percent, rounded down (spec 5.5), so in transit it is always below 10,000. A docked ship is
        /// where it was going and counts as 10,000.
        /// </summary>
        public long ProgressHundredthsOfPercent { get; }

        internal ShipPosition(int shipId, StarSystem dockedAt)
        {
            ShipId = shipId;
            XMilliLy = dockedAt.XMilliLy;
            YMilliLy = dockedAt.YMilliLy;
            DockedSystemId = dockedAt.Id;
            ProgressHundredthsOfPercent = FullProgress;
        }

        /// <summary>
        /// In transit at <paramref name="tick"/>: origin + (destination - origin) x elapsed / transit, each coordinate rounded
        /// once by the one rounding rule. Progress and distance flown are rounded down.
        /// </summary>
        internal ShipPosition(int shipId, Voyage voyage, StarSystem origin, StarSystem destination, long tick)
        {
            ShipId = shipId;
            OriginId = voyage.OriginId;
            DestinationId = voyage.DestinationId;
            DepartureTick = voyage.DepartureTick;
            ArrivalTick = voyage.ArrivalTick;
            TransitTicks = voyage.TransitTicks;
            ElapsedTicks = voyage.ElapsedTicks(tick);
            DistanceMilliLy = voyage.DistanceMilliLy;
            if (TransitTicks == 0)
            {
                XMilliLy = destination.XMilliLy;
                YMilliLy = destination.YMilliLy;
                return;
            }
            XMilliLy = origin.XMilliLy + IntMath.DivideRounded((BigInteger)(destination.XMilliLy - origin.XMilliLy) * ElapsedTicks, TransitTicks);
            YMilliLy = origin.YMilliLy + IntMath.DivideRounded((BigInteger)(destination.YMilliLy - origin.YMilliLy) * ElapsedTicks, TransitTicks);
            TravelledMilliLy = IntMath.DivideFloor((BigInteger)DistanceMilliLy * ElapsedTicks, TransitTicks);
            ProgressHundredthsOfPercent = IntMath.DivideFloor((BigInteger)FullProgress * ElapsedTicks, TransitTicks);
        }
    }
}
