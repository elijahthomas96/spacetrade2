using System.Collections.Generic;

namespace SpaceTrade.Sim.Tuning
{
    /// <summary>
    /// Built-in tuning, used when tuning.json is missing or invalid. The shipped tuning.json was
    /// generated from these values.
    /// </summary>
    public static class TuningDefaults
    {
        public static TuningData Create()
        {
            return new TuningData
            {
                Start = new StartTuning
                {
                    StartingCredits = 200_000,
                    StarterShipClass = "courier",
                    StarterDrive = "alcubierre_1_50",
                    OpeningOffers = new OpeningOfferTuning { MinFeasibleJobs = 2, MinProfitableJobs = 1 },
                    MarketStartFill = new MarketStartFillTuning { SupplyPercent = 100, DemandPercent = 100, PassengerPercent = 100 },
                },
                Galaxy = new GalaxyTuning
                {
                    SystemCountMin = 8,
                    SystemCountMax = 12,
                    NearestNeighbourMinMilliLy = 2_500,
                    NearestNeighbourMaxMilliLy = 7_000,
                    MaxHopMilliLy = 7_000,
                    MaxJobDistanceMilliLy = 14_000,
                    IndustriesPerSystemMin = 1,
                    IndustriesPerSystemMax = 3,
                    ShipyardCountMin = 2,
                    ShipyardCountMax = 3,
                    PopulationMinMillions = 1,
                    PopulationMaxMillions = 60,
                    Names = new List<string>
                    {
                        "Achernar", "Brightwater", "Cassia", "Dunmore", "Elara", "Fenwick",
                        "Galen's Rest", "Halcyon", "Iona", "Juno", "Kestrel", "Lumen",
                        "Marrow", "Nyx", "Orison", "Perdita", "Quill", "Rhea",
                        "Solace", "Tamsin", "Umber", "Vesper", "Wren", "Zephyr",
                    },
                },
                Industries = new List<IndustryTuning>
                {
                    Industry("mining_colony", "Mining colony", new[] { "food", "machinery" }, new[] { "ore" }),
                    Industry("refinery", "Refinery", new[] { "ore" }, new[] { "metals", "chemicals" }),
                    Industry("factory", "Factory", new[] { "metals", "chemicals" }, new[] { "machinery", "consumer_goods" }),
                    Industry("agricultural_system", "Agricultural system", new[] { "machinery", "chemicals" }, new[] { "food" }),
                    Industry("population_hub", "Population hub", new[] { "food", "consumer_goods" }, new string[0], producesPassengers: true),
                    Industry("shipyard_works", "Shipyard works", new[] { "metals", "machinery" }, new string[0], enablesShipyard: true),
                },
                Commodities = new List<CommodityTuning>
                {
                    Commodity("ore", "Ore", 1_500),
                    Commodity("metals", "Metals", 2_500),
                    Commodity("chemicals", "Chemicals", 2_800),
                    Commodity("machinery", "Machinery", 3_500),
                    Commodity("consumer_goods", "Consumer goods", 3_200),
                    Commodity("food", "Food", 2_000),
                },
                Passengers = new PassengerTuning
                {
                    BaseFare = 10_000,
                    FareRollMinPercent = 80,
                    FareRollMaxPercent = 120,
                    PoolCapBasePassengers = 4,
                    PoolCapExtraPerTenMillionPopulation = 1,
                    HubPoolBonusPercent = 100,
                    ReplenishPassengers = 2,
                    ReplenishIntervalDays = 30,
                },
                Rates = new RateTuning
                {
                    DistanceFactorBasePercent = 100,
                    DistanceFactorPerLyPercent = 40,
                    RerollIntervalDays = 90,
                },
                Provisions = new ProvisionTuning
                {
                    FuelBasePricePerTonne = 2_000,
                    FoodBasePricePerTonne = 8_000,
                    WaterBasePricePerTonne = 1_000,
                    PriceRollMinPercent = 90,
                    PriceRollMaxPercent = 110,
                    IndustryModifiers = new List<ProvisionModifierTuning>
                    {
                        new ProvisionModifierTuning { Industry = "refinery", FuelPercent = 70, FoodPercent = 100, WaterPercent = 100 },
                        new ProvisionModifierTuning { Industry = "agricultural_system", FuelPercent = 100, FoodPercent = 70, WaterPercent = 80 },
                        new ProvisionModifierTuning { Industry = "mining_colony", FuelPercent = 100, FoodPercent = 120, WaterPercent = 120 },
                    },
                    CombinedModifierMinPercent = 50,
                    CombinedModifierMaxPercent = 200,
                    FoodGramsPerPersonPerDay = 600,
                    WaterGramsPerPersonPerDay = 400,
                    PersonMassKg = 100,
                },
                Ships = new List<ShipClassTuning>
                {
                    new ShipClassTuning { Id = "courier", Name = "Courier", DryMassTonnes = 60, HoldTonnes = 30, TankTonnes = 20, Berths = 4, Crew = 2, Price = 1_000_000, InSystemDailyIncome = 17 },
                    new ShipClassTuning { Id = "bulk_freighter", Name = "Bulk freighter", DryMassTonnes = 200, HoldTonnes = 300, TankTonnes = 120, Berths = 2, Crew = 4, Price = 3_500_000, InSystemDailyIncome = 50 },
                    new ShipClassTuning { Id = "passenger_liner", Name = "Passenger liner", DryMassTonnes = 120, HoldTonnes = 40, TankTonnes = 50, Berths = 24, Crew = 5, Price = 2_500_000, InSystemDailyIncome = 40 },
                },
                ShipNames = new List<string>
                {
                    "Brass Tack", "Fair Wind", "Far Errand", "Good Hope", "Halfway There", "Kingfisher",
                    "Lantern", "Ledger", "Little Dipper", "Long Haul", "Meridian", "Northern Star",
                    "Old Reliable", "Ostinato", "Patience", "Persistence", "Quiet Margin", "Saltmarsh",
                    "Second Wind", "Steady Hand", "Swallow", "Tern", "Wayfarer", "Windfall",
                },
                Drives = new List<DriveTuning>
                {
                    new DriveTuning { Id = "alcubierre_1_50", Name = "Alcubierre 1.5c", SpeedMilliC = 1_500, Price = 150_000, FuelKgPerTonneLy = 22 },
                    new DriveTuning { Id = "alcubierre_2_25", Name = "Alcubierre 2.25c", SpeedMilliC = 2_250, Price = 600_000, FuelKgPerTonneLy = 26 },
                    new DriveTuning { Id = "alcubierre_3_75", Name = "Alcubierre 3.75c", SpeedMilliC = 3_750, Price = 1_500_000, FuelKgPerTonneLy = 32 },
                },
                Shipyards = new ShipyardTuning
                {
                    ShipStockPerClassMin = 1,
                    ShipStockPerClassMax = 2,
                    DriveStockPerTierMin = 1,
                    DriveStockPerTierMax = 3,
                    RestockIntervalDays = 180,
                    RestockAmount = 1,
                    PriceRollMinPercent = 90,
                    PriceRollMaxPercent = 110,
                },
                Automation = new AutomationTuning
                {
                    RecheckMinIntervalDays = 1,
                },
                Notifications = new NotificationTuning
                {
                    KeepNewest = 200,
                },
                Receipts = new ReceiptTuning
                {
                    KeepNewest = 500,
                },
            };
        }

        private static IndustryTuning Industry(string id, string name, string[] consumes, string[] produces, bool producesPassengers = false, bool enablesShipyard = false)
        {
            return new IndustryTuning
            {
                Id = id,
                Name = name,
                Consumes = new List<string>(consumes),
                Produces = new List<string>(produces),
                ProducesPassengers = producesPassengers,
                EnablesShipyard = enablesShipyard,
            };
        }

        private static CommodityTuning Commodity(string id, string name, long baseRatePerTonne)
        {
            return new CommodityTuning
            {
                Id = id,
                Name = name,
                BaseRatePerTonne = baseRatePerTonne,
                RateRollMinPercent = 80,
                RateRollMaxPercent = 120,
                SupplyCapTonnes = 60,
                DemandCapTonnes = 60,
                SupplyReplenishTonnes = 10,
                DemandReplenishTonnes = 10,
                ReplenishIntervalDays = 30,
            };
        }
    }
}
