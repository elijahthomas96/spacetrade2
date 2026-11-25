namespace SpaceTrade.Sim.Markets
{
    /// <summary>Freight waiting to ship (supply) or wanted (demand) for one commodity at one system, in whole tonnes.</summary>
    public sealed class CommodityStock
    {
        public int SystemId { get; }
        public string CommodityId { get; }
        public int Tonnes { get; internal set; }

        internal CommodityStock(int systemId, string commodityId, int tonnes)
        {
            SystemId = systemId;
            CommodityId = commodityId;
            Tonnes = tonnes;
        }
    }

    /// <summary>
    /// A producing system paired with a different system that consumes the same commodity (spec 5.1).
    /// Tonnes available and the rate are worked out by <see cref="Market"/> from current stock and tuning.
    /// </summary>
    public sealed class FreightOffer
    {
        public int OriginId { get; }
        public string CommodityId { get; }
        public int DestinationId { get; }
        public long DistanceMilliLy { get; }

        /// <summary>The stored random roll, as a percent of base rate x distance factor.</summary>
        public int RateRollPercent { get; internal set; }

        internal FreightOffer(int originId, string commodityId, int destinationId, long distanceMilliLy, int rateRollPercent)
        {
            OriginId = originId;
            CommodityId = commodityId;
            DestinationId = destinationId;
            DistanceMilliLy = distanceMilliLy;
            RateRollPercent = rateRollPercent;
        }
    }

    /// <summary>Passengers waiting to travel from one system to another (spec 5.2).</summary>
    public sealed class PassengerPool
    {
        public int OriginId { get; }
        public int DestinationId { get; }
        public long DistanceMilliLy { get; }
        public int Waiting { get; internal set; }

        /// <summary>The stored random roll, as a percent of base fare x distance factor.</summary>
        public int FareRollPercent { get; internal set; }

        internal PassengerPool(int originId, int destinationId, long distanceMilliLy, int waiting, int fareRollPercent)
        {
            OriginId = originId;
            DestinationId = destinationId;
            DistanceMilliLy = distanceMilliLy;
            Waiting = waiting;
            FareRollPercent = fareRollPercent;
        }
    }

    /// <summary>
    /// A system's provision price rolls, made once when the market is created and never re-rolled.
    /// Prices are worked out from these and the current tuning, so tuning edits reach existing saves.
    /// </summary>
    public sealed class ProvisionRoll
    {
        public int SystemId { get; }
        public int FuelRollPercent { get; }
        public int FoodRollPercent { get; }
        public int WaterRollPercent { get; }

        internal ProvisionRoll(int systemId, int fuelRollPercent, int foodRollPercent, int waterRollPercent)
        {
            SystemId = systemId;
            FuelRollPercent = fuelRollPercent;
            FoodRollPercent = foodRollPercent;
            WaterRollPercent = waterRollPercent;
        }
    }

    /// <summary>Provision prices at one system, in credits per tonne.</summary>
    public readonly struct ProvisionPrices
    {
        public long FuelPerTonne { get; }
        public long FoodPerTonne { get; }
        public long WaterPerTonne { get; }

        public ProvisionPrices(long fuelPerTonne, long foodPerTonne, long waterPerTonne)
        {
            FuelPerTonne = fuelPerTonne;
            FoodPerTonne = foodPerTonne;
            WaterPerTonne = waterPerTonne;
        }
    }
}
