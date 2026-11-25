using System.Collections.Generic;

namespace SpaceTrade.Sim.Tuning
{
    /// <summary>
    /// Every economy value in the game (spec 12). Loaded from tuning.json; see docs/tuning-guide.md
    /// for the meaning and unit of each field. Field names carry their unit.
    /// </summary>
    public sealed class TuningData
    {
        public StartTuning Start = new StartTuning();
        public GalaxyTuning Galaxy = new GalaxyTuning();
        public List<IndustryTuning> Industries = new List<IndustryTuning>();
        public List<CommodityTuning> Commodities = new List<CommodityTuning>();
        public PassengerTuning Passengers = new PassengerTuning();
        public RateTuning Rates = new RateTuning();
        public ProvisionTuning Provisions = new ProvisionTuning();
        public List<ShipClassTuning> Ships = new List<ShipClassTuning>();
        /// <summary>Names given to new ships. A ship keeps the name it was given, whatever later happens to this list.</summary>
        public List<string> ShipNames = new List<string>();
        public List<DriveTuning> Drives = new List<DriveTuning>();
        public ShipyardTuning Shipyards = new ShipyardTuning();
        public AutomationTuning Automation = new AutomationTuning();
        public NotificationTuning Notifications = new NotificationTuning();

        public IndustryTuning FindIndustry(string id) => Industries.Find(x => x.Id == id);
        public CommodityTuning FindCommodity(string id) => Commodities.Find(x => x.Id == id);
        public ShipClassTuning FindShip(string id) => Ships.Find(x => x.Id == id);
        public DriveTuning FindDrive(string id) => Drives.Find(x => x.Id == id);
    }

    public sealed class StartTuning
    {
        public long StartingCredits;
        public string StarterShipClass;
        public string StarterDrive;
        public OpeningOfferTuning OpeningOffers = new OpeningOfferTuning();
        public MarketStartFillTuning MarketStartFill = new MarketStartFillTuning();
    }

    public sealed class OpeningOfferTuning
    {
        public int MinFeasibleJobs;
        public int MinProfitableJobs;
    }

    /// <summary>How full each market stock is when a market is created, as a percent of its cap (0 to 100).</summary>
    public sealed class MarketStartFillTuning
    {
        public int SupplyPercent;
        public int DemandPercent;
        public int PassengerPercent;
    }

    public sealed class GalaxyTuning
    {
        public int SystemCountMin;
        public int SystemCountMax;
        public long NearestNeighbourMinMilliLy;
        public long NearestNeighbourMaxMilliLy;
        public long MaxHopMilliLy;
        public int IndustriesPerSystemMin;
        public int IndustriesPerSystemMax;
        public int ShipyardCountMin;
        public int ShipyardCountMax;
        public int PopulationMinMillions;
        public int PopulationMaxMillions;
        public List<string> Names = new List<string>();
    }

    public sealed class IndustryTuning
    {
        public string Id;
        public string Name;
        public List<string> Consumes = new List<string>();
        public List<string> Produces = new List<string>();
        public bool ProducesPassengers;
        public bool EnablesShipyard;
    }

    public sealed class CommodityTuning
    {
        public string Id;
        public string Name;
        public long BaseRatePerTonne;
        public int RateRollMinPercent;
        public int RateRollMaxPercent;
        public int SupplyCapTonnes;
        public int DemandCapTonnes;
        public int SupplyReplenishTonnes;
        public int DemandReplenishTonnes;
        public int ReplenishIntervalDays;
    }

    public sealed class PassengerTuning
    {
        public long BaseFare;
        public int FareRollMinPercent;
        public int FareRollMaxPercent;
        public int PoolCapBasePassengers;
        public int PoolCapExtraPerTenMillionPopulation;
        public int HubPoolBonusPercent;
        public int ReplenishPassengers;
        public int ReplenishIntervalDays;
    }

    public sealed class RateTuning
    {
        public int DistanceFactorBasePercent;
        public int DistanceFactorPerLyPercent;
        public int RerollIntervalDays;
    }

    public sealed class ProvisionTuning
    {
        public long FuelBasePricePerTonne;
        public long FoodBasePricePerTonne;
        public long WaterBasePricePerTonne;
        public int PriceRollMinPercent;
        public int PriceRollMaxPercent;
        public List<ProvisionModifierTuning> IndustryModifiers = new List<ProvisionModifierTuning>();
        public int CombinedModifierMinPercent;
        public int CombinedModifierMaxPercent;
        public int FoodGramsPerPersonPerDay;
        public int WaterGramsPerPersonPerDay;
        public int PersonMassKg;
    }

    public sealed class ProvisionModifierTuning
    {
        public string Industry;
        public int FuelPercent;
        public int FoodPercent;
        public int WaterPercent;
    }

    public sealed class ShipClassTuning
    {
        public string Id;
        public string Name;
        public int DryMassTonnes;
        public int HoldTonnes;
        public int TankTonnes;
        public int Berths;
        public int Crew;
        public long Price;
        public long InSystemDailyIncome;
    }

    public sealed class DriveTuning
    {
        public string Id;
        public string Name;
        public long SpeedMilliC;
        public long Price;
        public int FuelKgPerTonneLy;
    }

    public sealed class ShipyardTuning
    {
        public int ShipStockPerClassMin;
        public int ShipStockPerClassMax;
        public int DriveStockPerTierMin;
        public int DriveStockPerTierMax;
        public int RestockIntervalDays;
        /// <summary>Units of each ship class and drive a yard stocks that are added every restock interval, up to its stock limit.</summary>
        public int RestockAmount;
        public int PriceRollMinPercent;
        public int PriceRollMaxPercent;
    }

    public sealed class AutomationTuning
    {
        public int RecheckMinIntervalDays;
    }

    public sealed class NotificationTuning
    {
        /// <summary>How many notifications are kept, newest first; older ones are dropped. Receipts are never dropped.</summary>
        public int KeepNewest;
    }
}
