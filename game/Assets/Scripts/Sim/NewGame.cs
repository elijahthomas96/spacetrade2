using System;
using System.Collections.Generic;
using System.Linq;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.World;

namespace SpaceTrade.Sim
{
    public sealed class NewGameResult
    {
        public Simulation Simulation { get; }

        /// <summary>The seed asked for.</summary>
        public long RequestedSeed { get; }

        /// <summary>The seed the game was actually built from. It is also the galaxy's seed, kept in the game state.</summary>
        public long SeedUsed { get; }

        /// <summary>True when the given tuning could not meet the new-game guarantees and the built-in defaults were used instead.</summary>
        public bool UsedDefaults { get; }

        /// <summary>Why the given tuning was set aside, naming the fields to look at. Empty when it was used.</summary>
        public IReadOnlyList<string> Errors { get; }

        internal NewGameResult(Simulation simulation, long requestedSeed, long seedUsed, bool usedDefaults, IReadOnlyList<string> errors)
        {
            Simulation = simulation;
            RequestedSeed = requestedSeed;
            SeedUsed = seedUsed;
            UsedDefaults = usedDefaults;
            Errors = errors;
        }
    }

    /// <summary>
    /// Creates a fresh game (spec 6.4): the galaxy and markets from the seed, starting credits, one
    /// starter ship docked at the start system with empty supplies and a name picked by the seed, and
    /// the clock paused at 1x on Y1 M01 D01. The start port's opening offers are checked with the real
    /// quote function and their rate and fare rolls raised, one percent at a time and never above the
    /// tuning maximum, until the guarantee holds.
    /// </summary>
    public static class NewGame
    {
        private const long ShipNameSalt = 0x5348495031;
        private const int MaxSeedsTried = 10_000;

        /// <summary>
        /// Builds a new game. Tuning that cannot meet the guarantees for this seed is set aside with a message
        /// naming the fields to look at, and the built-in defaults are used. With the built-in defaults a new
        /// game never fails: a seed they cannot meet the guarantees on is skipped for the next one.
        /// </summary>
        public static NewGameResult Create(long seed, TuningData tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            bool isDefaults = TuningWriter.Write(tuning) == TuningWriter.Write(TuningDefaults.Create());
            if (!isDefaults)
            {
                var sim = TryCreate(seed, tuning, out var problems);
                if (sim != null) return new NewGameResult(sim, seed, seed, usedDefaults: false, new List<string>());
                var fallback = CreateTryingSeeds(seed, TuningDefaults.Create(), out long fallbackSeed);
                return new NewGameResult(fallback, seed, fallbackSeed, usedDefaults: true, problems);
            }
            var game = CreateTryingSeeds(seed, tuning, out long used);
            return new NewGameResult(game, seed, used, usedDefaults: false, new List<string>());
        }

        /// <summary>Tries <paramref name="seed"/>, then each following seed, until the guarantees hold.</summary>
        internal static Simulation CreateTryingSeeds(long seed, TuningData tuning, out long seedUsed)
        {
            long s = seed;
            for (int i = 0; i < MaxSeedsTried; i++, s = unchecked(s + 1))
            {
                var sim = TryCreate(s, tuning, out _);
                if (sim != null) { seedUsed = s; return sim; }
            }
            throw new InvalidOperationException($"No new game met the guarantees for any of the {MaxSeedsTried} seeds from {seed}.");
        }

        /// <summary>The game for exactly this seed, or null with the reasons, each naming the fields to look at.</summary>
        internal static Simulation TryCreate(long seed, TuningData tuning, out List<string> problems)
        {
            problems = new List<string>();
            if (tuning.ShipNames.Count == 0)
            {
                problems.Add("shipNames: must list at least one name for new ships");
                return null;
            }

            Galaxy galaxy;
            try
            {
                galaxy = GalaxyGenerator.Generate(seed, tuning);
            }
            catch (InvalidOperationException e)
            {
                problems.Add("galaxy, industries: " + e.Message);
                return null;
            }

            var sim = new Simulation(0, ClockSpeed.X1, paused: true);
            var market = Market.Create(galaxy, tuning, 0);
            sim.AttachMarket(market);
            sim.SetCredits(tuning.Start.StartingCredits);
            var ship = sim.AddShip(PickShipName(seed, tuning), tuning.Start.StarterShipClass, tuning.Start.StarterDrive, galaxy.StartSystemId);
            market.HoldOpeningRates();

            while (true)
            {
                var check = OpeningOffers.Check(sim, ship.Id);
                if (check.Holds) return sim;
                if (!check.EnoughFeasible || !market.RaiseOpeningRolls())
                {
                    problems.AddRange(Explain(seed, sim, check));
                    return null;
                }
            }
        }

        /// <summary>The starter ship's name: picked from the tuning's ship names by the seed, with its own generator so galaxy and market rolls are unaffected.</summary>
        internal static string PickShipName(long seed, TuningData tuning)
        {
            var names = tuning.ShipNames;
            return names[new SeededRandom(seed ^ ShipNameSalt).Range(0, names.Count - 1)];
        }

        private static IEnumerable<string> Explain(long seed, Simulation sim, OpeningOfferCheck check)
        {
            var t = sim.Market.Tuning;
            string ship = $"ships[{t.Start.StarterShipClass}]";
            string drive = $"drives[{t.Start.StarterDrive}]";
            string start = sim.Market.Galaxy.StartSystem.Name;
            var rateFields = sim.Market.OffersFrom(sim.Market.Galaxy.StartSystemId).Select(o => o.CommodityId).Distinct()
                .Select(c => $"commodities[{c}].baseRatePerTonne and rateRollMaxPercent")
                .Concat(new[] { "passengers.baseFare and fareRollMaxPercent", "rates.distanceFactorBasePercent and distanceFactorPerLyPercent" });
            string costFields = $"provisions.fuelBasePricePerTonne, foodBasePricePerTonne, waterBasePricePerTonne, {drive}.fuelKgPerTonneLy";
            string prefix = $"New game with seed {seed} (start port {start}): ";

            if (!check.EnoughFeasible)
            {
                yield return prefix + $"start.openingOffers.minFeasibleJobs: the start port offers {check.FeasibleJobs.Count} job(s) the starter ship can fly and afford, needs {check.MinFeasibleJobs}. " +
                    $"Look at start.startingCredits, start.marketStartFill, {ship}.holdTonnes, tankTonnes, and berths, and {costFields}.";
                yield break;
            }
            if (!check.EnoughProfitable)
                yield return prefix + $"start.openingOffers.minProfitableJobs: even with every start-port rate and fare at its roll maximum, {check.ProfitableJobCount} job(s) show a profit, needs {check.MinProfitableJobs}. " +
                    $"Look at {string.Join(", ", rateFields)}, and {costFields}.";
            if (!check.HasTradeoffPair)
                yield return prefix + "start.openingOffers.minFeasibleJobs: no two of the start port's jobs go to different destinations with one quicker and the other earning more net profit, even with every start-port rate and fare at its roll maximum. " +
                    $"Look at {string.Join(", ", rateFields)}, {costFields}, and galaxy.nearestNeighbourMinMilliLy and nearestNeighbourMaxMilliLy.";
        }
    }
}
