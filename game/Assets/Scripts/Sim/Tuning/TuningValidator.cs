using System.Collections.Generic;
using System.Linq;
using SpaceTrade.Sim.Ships;

namespace SpaceTrade.Sim.Tuning
{
    /// <summary>
    /// Checks that compare tuning fields with each other: ranges in the right order, unique ids,
    /// references to ids that exist, and settings that galaxy generation needs to always succeed.
    /// </summary>
    public static class TuningValidator
    {
        public static List<string> Validate(TuningData t)
        {
            var errors = new List<string>();
            Validate(t, errors);
            return errors;
        }

        internal static void Validate(TuningData t, List<string> errors)
        {
            void Order(string section, string minName, long min, string maxName, long max)
            {
                if (min > max) errors.Add($"{section}.{maxName}: must be at least {section}.{minName} ({min}), got {max}");
            }

            void UniqueIds(string section, IEnumerable<string> ids)
            {
                foreach (var dup in ids.Where(id => id.Length > 0).GroupBy(id => id).Where(g => g.Count() > 1))
                    errors.Add($"{section}[{dup.Key}]: the id \"{dup.Key}\" is used more than once");
            }

            // start
            if (t.Start.StarterShipClass.Length > 0 && t.FindShip(t.Start.StarterShipClass) == null)
                errors.Add($"start.starterShipClass: \"{t.Start.StarterShipClass}\" is not the id of any entry in ships");
            if (t.Start.StarterDrive.Length > 0 && t.FindDrive(t.Start.StarterDrive) == null)
                errors.Add($"start.starterDrive: \"{t.Start.StarterDrive}\" is not the id of any entry in drives");
            Order("start.openingOffers", "minProfitableJobs", t.Start.OpeningOffers.MinProfitableJobs, "minFeasibleJobs", t.Start.OpeningOffers.MinFeasibleJobs);

            // galaxy
            var g = t.Galaxy;
            Order("galaxy", "systemCountMin", g.SystemCountMin, "systemCountMax", g.SystemCountMax);
            Order("galaxy", "nearestNeighbourMinMilliLy", g.NearestNeighbourMinMilliLy, "nearestNeighbourMaxMilliLy", g.NearestNeighbourMaxMilliLy);
            if (g.MaxHopMilliLy < g.NearestNeighbourMaxMilliLy)
                errors.Add($"galaxy.maxHopMilliLy: must be at least galaxy.nearestNeighbourMaxMilliLy ({g.NearestNeighbourMaxMilliLy}), got {g.MaxHopMilliLy}");
            Order("galaxy", "industriesPerSystemMin", g.IndustriesPerSystemMin, "industriesPerSystemMax", g.IndustriesPerSystemMax);
            if (g.IndustriesPerSystemMax < 2)
                errors.Add($"galaxy.industriesPerSystemMax: must be at least 2, so a shipyard system can also have an industry that trades, got {g.IndustriesPerSystemMax}");
            int tradingIndustries = t.Industries.Count(i => !i.EnablesShipyard);
            if (t.Industries.Count > 0 && g.IndustriesPerSystemMax > tradingIndustries)
                errors.Add($"galaxy.industriesPerSystemMax: cannot be more than the {tradingIndustries} industries that do not enable a shipyard, got {g.IndustriesPerSystemMax}");
            Order("galaxy", "shipyardCountMin", g.ShipyardCountMin, "shipyardCountMax", g.ShipyardCountMax);
            if (g.ShipyardCountMax > g.SystemCountMin)
                errors.Add($"galaxy.shipyardCountMax: cannot be more than galaxy.systemCountMin ({g.SystemCountMin}), got {g.ShipyardCountMax}");
            Order("galaxy", "populationMinMillions", g.PopulationMinMillions, "populationMaxMillions", g.PopulationMaxMillions);
            if (g.Names.Count < g.SystemCountMax)
                errors.Add($"galaxy.names: needs at least galaxy.systemCountMax ({g.SystemCountMax}) names, has {g.Names.Count}");
            foreach (var dup in g.Names.GroupBy(n => n).Where(x => x.Count() > 1))
                errors.Add($"galaxy.names: \"{dup.Key}\" appears more than once");

            // industries
            if (t.Industries.Count == 0) errors.Add("industries: must list at least one industry");
            UniqueIds("industries", t.Industries.Select(i => i.Id));
            foreach (var ind in t.Industries)
            {
                foreach (var c in ind.Consumes)
                    if (t.FindCommodity(c) == null) errors.Add($"industries[{ind.Id}].consumes: \"{c}\" is not the id of any entry in commodities");
                foreach (var c in ind.Produces)
                    if (t.FindCommodity(c) == null) errors.Add($"industries[{ind.Id}].produces: \"{c}\" is not the id of any entry in commodities");
            }
            if (t.Industries.Count > 0 && !t.Industries.Any(i => i.EnablesShipyard))
                errors.Add("industries: at least one industry must have enablesShipyard set to true");
            if (t.Industries.Count > 0 && !HasThreeIndustryCircuit(t.Industries))
                errors.Add("industries: no three industries form a closed freight circuit (A makes something B uses, B makes something C uses, C makes something A uses)");

            // commodities
            if (t.Commodities.Count == 0) errors.Add("commodities: must list at least one commodity");
            UniqueIds("commodities", t.Commodities.Select(c => c.Id));
            foreach (var c in t.Commodities)
                Order($"commodities[{c.Id}]", "rateRollMinPercent", c.RateRollMinPercent, "rateRollMaxPercent", c.RateRollMaxPercent);

            // passengers, rates
            Order("passengers", "fareRollMinPercent", t.Passengers.FareRollMinPercent, "fareRollMaxPercent", t.Passengers.FareRollMaxPercent);
            if (t.Rates.DistanceFactorBasePercent + t.Rates.DistanceFactorPerLyPercent == 0)
                errors.Add("rates.distanceFactorPerLyPercent: distanceFactorBasePercent and distanceFactorPerLyPercent cannot both be 0, or every job would pay nothing");

            // provisions
            Order("provisions", "priceRollMinPercent", t.Provisions.PriceRollMinPercent, "priceRollMaxPercent", t.Provisions.PriceRollMaxPercent);
            Order("provisions", "combinedModifierMinPercent", t.Provisions.CombinedModifierMinPercent, "combinedModifierMaxPercent", t.Provisions.CombinedModifierMaxPercent);
            foreach (var m in t.Provisions.IndustryModifiers)
                if (m.Industry.Length > 0 && t.FindIndustry(m.Industry) == null)
                    errors.Add($"provisions.industryModifiers[{m.Industry}].industry: \"{m.Industry}\" is not the id of any entry in industries");
            foreach (var dup in t.Provisions.IndustryModifiers.Where(m => m.Industry.Length > 0).GroupBy(m => m.Industry).Where(x => x.Count() > 1))
                errors.Add($"provisions.industryModifiers[{dup.Key}]: industry \"{dup.Key}\" has more than one entry");

            // ships, drives
            if (t.Ships.Count == 0) errors.Add("ships: must list at least one ship class");
            UniqueIds("ships", t.Ships.Select(s => s.Id));
            if (t.Drives.Count == 0) errors.Add("drives: must list at least one drive");
            UniqueIds("drives", t.Drives.Select(d => d.Id));

            // shipyards
            var y = t.Shipyards;
            Order("shipyards", "shipStockPerClassMin", y.ShipStockPerClassMin, "shipStockPerClassMax", y.ShipStockPerClassMax);
            Order("shipyards", "driveStockPerTierMin", y.DriveStockPerTierMin, "driveStockPerTierMax", y.DriveStockPerTierMax);
            Order("shipyards", "priceRollMinPercent", y.PriceRollMinPercent, "priceRollMaxPercent", y.PriceRollMaxPercent);

            StarterRange(t, errors);
        }

        /// <summary>
        /// The starter ship with the starter drive, a full tank, crew only, and the food and water the
        /// crew needs, must be able to fly galaxy.maxHopMilliLy (spec 12).
        /// </summary>
        private static void StarterRange(TuningData t, List<string> errors)
        {
            var shipClass = t.FindShip(t.Start.StarterShipClass);
            var drive = t.FindDrive(t.Start.StarterDrive);
            if (shipClass == null || drive == null) return;
            long hop = t.Galaxy.MaxHopMilliLy;
            var needs = ProvisioningMath.Compute(t, shipClass, drive, hop, 0, 0, 0, 0);
            string who = $"the starter ship ({shipClass.Id} with {drive.Id}, crew only)";
            if (needs.FuelRequiredKg > (long)shipClass.TankTonnes * 1000)
                errors.Add($"ships[{shipClass.Id}].tankTonnes: {who} needs {Format.Tonnes(needs.FuelRequiredKg)} of fuel to fly galaxy.maxHopMilliLy ({hop}), but its tank holds {shipClass.TankTonnes} t");
            if (needs.HoldUsedKg > (long)shipClass.HoldTonnes * 1000)
                errors.Add($"ships[{shipClass.Id}].holdTonnes: {who} needs {Format.Tonnes(needs.HoldUsedKg)} of food and water to fly galaxy.maxHopMilliLy ({hop}), but its hold holds {shipClass.HoldTonnes} t");
        }

        private static bool Feeds(IndustryTuning from, IndustryTuning to) => from.Produces.Any(to.Consumes.Contains);

        private static bool HasThreeIndustryCircuit(List<IndustryTuning> industries)
        {
            int n = industries.Count;
            for (int a = 0; a < n; a++)
            for (int b = 0; b < n; b++)
            for (int c = 0; c < n; c++)
            {
                if (a == b || b == c || a == c) continue;
                if (Feeds(industries[a], industries[b]) && Feeds(industries[b], industries[c]) && Feeds(industries[c], industries[a]))
                    return true;
            }
            return false;
        }
    }
}
