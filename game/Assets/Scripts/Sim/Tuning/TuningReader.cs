using System.Collections.Generic;
using SpaceTrade.Sim.Json;

namespace SpaceTrade.Sim.Tuning
{
    /// <summary>
    /// Turns parsed JSON into <see cref="TuningData"/>, checking each field's type and allowed
    /// range. Checks that compare fields with each other live in <see cref="TuningValidator"/>.
    /// </summary>
    internal static class TuningReader
    {
        private const int MaxPercent = 100_000;
        private const long MaxCredits = 1_000_000_000_000L;
        private const int MaxTonnes = 10_000_000;
        private const int MaxDays = 360_000;

        public static TuningData Read(JsonValue root, List<string> errors)
        {
            var r = ObjectReader.Root(root, errors);
            var t = new TuningData
            {
                Start = ReadStart(r.Child("start")),
                Galaxy = ReadGalaxy(r.Child("galaxy")),
                Industries = ReadList(r, "industries", ReadIndustry),
                Commodities = ReadList(r, "commodities", ReadCommodity),
                Passengers = ReadPassengers(r.Child("passengers")),
                Rates = ReadRates(r.Child("rates")),
                Provisions = ReadProvisions(r.Child("provisions")),
                Ships = ReadList(r, "ships", ReadShip),
                Drives = ReadList(r, "drives", ReadDrive),
                Shipyards = ReadShipyards(r.Child("shipyards")),
                Automation = ReadAutomation(r.Child("automation")),
            };
            r.RejectUnknownFields();
            return t;
        }

        private static List<T> ReadList<T>(ObjectReader parent, string name, System.Func<ObjectReader, T> readItem, string labelField = "id")
        {
            var list = new List<T>();
            foreach (var item in parent.ObjectList(name, labelField))
            {
                list.Add(readItem(item));
                item.RejectUnknownFields();
            }
            return list;
        }

        private static StartTuning ReadStart(ObjectReader r)
        {
            var s = new StartTuning
            {
                StartingCredits = r.Long("startingCredits", 0, MaxCredits),
                StarterShipClass = r.Text("starterShipClass"),
                StarterDrive = r.Text("starterDrive"),
            };
            var o = r.Child("openingOffers");
            s.OpeningOffers.MinFeasibleJobs = o.Int("minFeasibleJobs", 1, 100);
            s.OpeningOffers.MinProfitableJobs = o.Int("minProfitableJobs", 0, 100);
            o.RejectUnknownFields();
            r.RejectUnknownFields();
            return s;
        }

        private static GalaxyTuning ReadGalaxy(ObjectReader r)
        {
            var g = new GalaxyTuning
            {
                SystemCountMin = r.Int("systemCountMin", 3, 1000),
                SystemCountMax = r.Int("systemCountMax", 3, 1000),
                NearestNeighbourMinMilliLy = r.Long("nearestNeighbourMinMilliLy", 1, 1_000_000),
                NearestNeighbourMaxMilliLy = r.Long("nearestNeighbourMaxMilliLy", 1, 1_000_000),
                MaxHopMilliLy = r.Long("maxHopMilliLy", 1, 1_000_000),
                IndustriesPerSystemMin = r.Int("industriesPerSystemMin", 1, 100),
                IndustriesPerSystemMax = r.Int("industriesPerSystemMax", 1, 100),
                ShipyardCountMin = r.Int("shipyardCountMin", 2, 1000),
                ShipyardCountMax = r.Int("shipyardCountMax", 2, 1000),
                PopulationMinMillions = r.Int("populationMinMillions", 1, 1_000_000),
                PopulationMaxMillions = r.Int("populationMaxMillions", 1, 1_000_000),
                Names = r.TextList("names"),
            };
            r.RejectUnknownFields();
            return g;
        }

        private static IndustryTuning ReadIndustry(ObjectReader r) => new IndustryTuning
        {
            Id = r.Text("id"),
            Name = r.Text("name"),
            Consumes = r.TextList("consumes"),
            Produces = r.TextList("produces"),
            ProducesPassengers = r.Bool("producesPassengers"),
            EnablesShipyard = r.Bool("enablesShipyard"),
        };

        private static CommodityTuning ReadCommodity(ObjectReader r) => new CommodityTuning
        {
            Id = r.Text("id"),
            Name = r.Text("name"),
            BaseRatePerTonne = r.Long("baseRatePerTonne", 1, MaxCredits),
            RateRollMinPercent = r.Int("rateRollMinPercent", 1, MaxPercent),
            RateRollMaxPercent = r.Int("rateRollMaxPercent", 1, MaxPercent),
            SupplyCapTonnes = r.Int("supplyCapTonnes", 1, MaxTonnes),
            DemandCapTonnes = r.Int("demandCapTonnes", 1, MaxTonnes),
            SupplyReplenishTonnes = r.Int("supplyReplenishTonnes", 1, MaxTonnes),
            DemandReplenishTonnes = r.Int("demandReplenishTonnes", 1, MaxTonnes),
            ReplenishIntervalDays = r.Int("replenishIntervalDays", 1, MaxDays),
        };

        private static PassengerTuning ReadPassengers(ObjectReader r)
        {
            var p = new PassengerTuning
            {
                BaseFare = r.Long("baseFare", 1, MaxCredits),
                FareRollMinPercent = r.Int("fareRollMinPercent", 1, MaxPercent),
                FareRollMaxPercent = r.Int("fareRollMaxPercent", 1, MaxPercent),
                PoolCapBasePassengers = r.Int("poolCapBasePassengers", 0, 1_000_000),
                PoolCapExtraPerTenMillionPopulation = r.Int("poolCapExtraPerTenMillionPopulation", 0, 1_000_000),
                HubPoolBonusPercent = r.Int("hubPoolBonusPercent", 0, MaxPercent),
                ReplenishPassengers = r.Int("replenishPassengers", 1, 1_000_000),
                ReplenishIntervalDays = r.Int("replenishIntervalDays", 1, MaxDays),
            };
            r.RejectUnknownFields();
            return p;
        }

        private static RateTuning ReadRates(ObjectReader r)
        {
            var x = new RateTuning
            {
                DistanceFactorBasePercent = r.Int("distanceFactorBasePercent", 0, MaxPercent),
                DistanceFactorPerLyPercent = r.Int("distanceFactorPerLyPercent", 0, MaxPercent),
                RerollIntervalDays = r.Int("rerollIntervalDays", 1, MaxDays),
            };
            r.RejectUnknownFields();
            return x;
        }

        private static ProvisionTuning ReadProvisions(ObjectReader r)
        {
            var p = new ProvisionTuning
            {
                FuelBasePricePerTonne = r.Long("fuelBasePricePerTonne", 1, MaxCredits),
                FoodBasePricePerTonne = r.Long("foodBasePricePerTonne", 1, MaxCredits),
                WaterBasePricePerTonne = r.Long("waterBasePricePerTonne", 1, MaxCredits),
                PriceRollMinPercent = r.Int("priceRollMinPercent", 1, MaxPercent),
                PriceRollMaxPercent = r.Int("priceRollMaxPercent", 1, MaxPercent),
                IndustryModifiers = ReadList(r, "industryModifiers", ReadModifier, "industry"),
                FoodGramsPerPersonPerDay = r.Int("foodGramsPerPersonPerDay", 0, 1_000_000),
                WaterGramsPerPersonPerDay = r.Int("waterGramsPerPersonPerDay", 0, 1_000_000),
                PersonMassKg = r.Int("personMassKg", 1, 100_000),
            };
            r.RejectUnknownFields();
            return p;
        }

        private static ProvisionModifierTuning ReadModifier(ObjectReader r) => new ProvisionModifierTuning
        {
            Industry = r.Text("industry"),
            FuelPercent = r.Int("fuelPercent", 1, MaxPercent),
            FoodPercent = r.Int("foodPercent", 1, MaxPercent),
            WaterPercent = r.Int("waterPercent", 1, MaxPercent),
        };

        private static ShipClassTuning ReadShip(ObjectReader r) => new ShipClassTuning
        {
            Id = r.Text("id"),
            Name = r.Text("name"),
            DryMassTonnes = r.Int("dryMassTonnes", 1, MaxTonnes),
            HoldTonnes = r.Int("holdTonnes", 0, MaxTonnes),
            TankTonnes = r.Int("tankTonnes", 1, MaxTonnes),
            Berths = r.Int("berths", 0, 100_000),
            Crew = r.Int("crew", 1, 100_000),
            Price = r.Long("price", 0, MaxCredits),
            InSystemDailyIncome = r.Long("inSystemDailyIncome", 0, MaxCredits),
        };

        private static DriveTuning ReadDrive(ObjectReader r) => new DriveTuning
        {
            Id = r.Text("id"),
            Name = r.Text("name"),
            SpeedMilliC = r.Long("speedMilliC", 1, 1_000_000),
            Price = r.Long("price", 0, MaxCredits),
            FuelKgPerTonneLy = r.Int("fuelKgPerTonneLy", 0, 1_000_000),
        };

        private static ShipyardTuning ReadShipyards(ObjectReader r)
        {
            var s = new ShipyardTuning
            {
                ShipStockPerClassMin = r.Int("shipStockPerClassMin", 0, 1000),
                ShipStockPerClassMax = r.Int("shipStockPerClassMax", 0, 1000),
                DriveStockPerTierMin = r.Int("driveStockPerTierMin", 0, 1000),
                DriveStockPerTierMax = r.Int("driveStockPerTierMax", 0, 1000),
                RestockIntervalDays = r.Int("restockIntervalDays", 1, MaxDays),
                PriceRollMinPercent = r.Int("priceRollMinPercent", 1, MaxPercent),
                PriceRollMaxPercent = r.Int("priceRollMaxPercent", 1, MaxPercent),
            };
            r.RejectUnknownFields();
            return s;
        }

        private static AutomationTuning ReadAutomation(ObjectReader r)
        {
            var a = new AutomationTuning
            {
                RecheckMinIntervalDays = r.Int("recheckMinIntervalDays", 0, MaxDays),
            };
            r.RejectUnknownFields();
            return a;
        }
    }
}
