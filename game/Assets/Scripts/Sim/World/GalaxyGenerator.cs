using System;
using System.Collections.Generic;
using System.Linq;
using SpaceTrade.Sim.Tuning;

namespace SpaceTrade.Sim.World
{
    /// <summary>
    /// Builds a galaxy from a seed (spec 4). Candidates that miss any guarantee are discarded and
    /// the next is drawn from the same random stream, so a seed always yields the same galaxy.
    /// </summary>
    public static class GalaxyGenerator
    {
        private const int MaxCandidates = 10_000;
        private const int PlacementTriesPerSystem = 2_000;

        public static Galaxy Generate(long seed, TuningData tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            var rng = new SeededRandom(seed);
            for (int attempt = 0; attempt < MaxCandidates; attempt++)
            {
                var candidate = TryBuild(seed, tuning, rng);
                if (candidate != null && GalaxyRules.FindProblems(candidate, tuning).Count == 0)
                    return candidate;
            }
            throw new InvalidOperationException($"Could not generate a galaxy meeting every guarantee for seed {seed} after {MaxCandidates} attempts. Check the galaxy and industries sections of the tuning data.");
        }

        private static Galaxy TryBuild(long seed, TuningData tuning, SeededRandom rng)
        {
            var g = tuning.Galaxy;
            int count = rng.Range(g.SystemCountMin, g.SystemCountMax);

            var positions = PlacePositions(count, g.NearestNeighbourMinMilliLy, g.NearestNeighbourMaxMilliLy, rng);
            if (positions == null) return null;

            var names = g.Names.ToArray();
            rng.Shuffle(names);

            var yardTypes = tuning.Industries.Where(i => i.EnablesShipyard).Select(i => i.Id).ToArray();
            var tradeTypes = tuning.Industries.Where(i => !i.EnablesShipyard).Select(i => i.Id).ToArray();

            int yardCount = rng.Range(g.ShipyardCountMin, Math.Min(g.ShipyardCountMax, count));
            var order = Enumerable.Range(0, count).ToArray();
            rng.Shuffle(order);
            var isYard = new bool[count];
            for (int i = 0; i < yardCount; i++) isYard[order[i]] = true;

            var systems = new List<StarSystem>(count);
            for (int id = 0; id < count; id++)
            {
                var industries = new List<string>();
                int total;
                if (isYard[id])
                {
                    // A yard system keeps one slot for the yard and at least one for trade.
                    total = rng.Range(Math.Max(g.IndustriesPerSystemMin, 2), g.IndustriesPerSystemMax);
                    industries.Add(yardTypes[rng.Range(0, yardTypes.Length - 1)]);
                }
                else
                {
                    total = rng.Range(g.IndustriesPerSystemMin, g.IndustriesPerSystemMax);
                }
                var pool = (string[])tradeTypes.Clone();
                rng.Shuffle(pool);
                industries.AddRange(pool.Take(total - industries.Count));

                int population = rng.Range(g.PopulationMinMillions, g.PopulationMaxMillions);
                systems.Add(new StarSystem(id, names[id], positions[id].x, positions[id].y, population, industries, isYard[id]));
            }

            var draft = new Galaxy(seed, 0, systems);
            var startCandidates = Enumerable.Range(0, count)
                .Where(id => GalaxyRules.NearestShipyardDistance(draft, id) <= g.MaxHopMilliLy)
                .ToArray();
            if (startCandidates.Length == 0) return null;
            int start = startCandidates[rng.Range(0, startCandidates.Length - 1)];
            return new Galaxy(seed, start, systems);
        }

        /// <summary>
        /// Places each new system between the nearest-neighbour minimum and maximum from a random
        /// existing system, and at least the minimum from every other. Every system then has a
        /// neighbour within range, and the whole map is linked by hops no longer than the maximum.
        /// Coordinates are shifted so the smallest x and y are 0.
        /// </summary>
        private static (long x, long y)[] PlacePositions(int count, long minDist, long maxDist, SeededRandom rng)
        {
            var pts = new List<(long x, long y)> { (0, 0) };
            while (pts.Count < count)
            {
                bool placed = false;
                for (int t = 0; t < PlacementTriesPerSystem && !placed; t++)
                {
                    var anchor = pts[rng.Range(0, pts.Count - 1)];
                    long x = anchor.x + rng.Range(-maxDist, maxDist);
                    long y = anchor.y + rng.Range(-maxDist, maxDist);
                    long d = Galaxy.Distance(anchor.x, anchor.y, x, y);
                    if (d < minDist || d > maxDist) continue;
                    if (pts.Any(p => Galaxy.Distance(p.x, p.y, x, y) < minDist)) continue;
                    pts.Add((x, y));
                    placed = true;
                }
                if (!placed) return null;
            }
            long minX = pts.Min(p => p.x);
            long minY = pts.Min(p => p.y);
            return pts.Select(p => (p.x - minX, p.y - minY)).ToArray();
        }
    }
}
