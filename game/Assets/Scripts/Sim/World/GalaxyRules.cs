using System.Collections.Generic;
using System.Linq;
using SpaceTrade.Sim.Tuning;

namespace SpaceTrade.Sim.World
{
    /// <summary>
    /// The guarantees in spec 4. Generation only returns a galaxy for which
    /// <see cref="FindProblems"/> is empty.
    /// </summary>
    public static class GalaxyRules
    {
        public static List<string> FindProblems(Galaxy galaxy, TuningData tuning)
        {
            var problems = new List<string>();
            var g = tuning.Galaxy;
            var systems = galaxy.Systems;
            int n = systems.Count;

            if (n < g.SystemCountMin || n > g.SystemCountMax)
                problems.Add($"Has {n} systems; allowed range is {g.SystemCountMin} to {g.SystemCountMax}.");
            if (n < 2) return problems;

            foreach (var dup in systems.GroupBy(s => s.Name).Where(x => x.Count() > 1))
                problems.Add($"Name \"{dup.Key}\" is used by more than one system.");

            for (int i = 0; i < n; i++)
            {
                long nearest = long.MaxValue;
                for (int j = 0; j < n; j++)
                    if (j != i) nearest = System.Math.Min(nearest, galaxy.DistanceMilliLy(i, j));
                if (nearest < g.NearestNeighbourMinMilliLy || nearest > g.NearestNeighbourMaxMilliLy)
                    problems.Add($"{systems[i].Name}: nearest neighbour is {nearest} milli-ly away; allowed range is {g.NearestNeighbourMinMilliLy} to {g.NearestNeighbourMaxMilliLy}.");
            }

            foreach (var s in systems)
            {
                int count = s.IndustryIds.Count;
                if (count < g.IndustriesPerSystemMin || count > g.IndustriesPerSystemMax)
                    problems.Add($"{s.Name}: has {count} industries; allowed range is {g.IndustriesPerSystemMin} to {g.IndustriesPerSystemMax}.");
                if (s.IndustryIds.Distinct().Count() != count)
                    problems.Add($"{s.Name}: lists the same industry twice.");
                foreach (var id in s.IndustryIds)
                    if (tuning.FindIndustry(id) == null) problems.Add($"{s.Name}: industry \"{id}\" is not in the tuning data.");
                bool yardIndustry = s.IndustryIds.Any(id => tuning.FindIndustry(id)?.EnablesShipyard == true);
                if (yardIndustry != s.HasShipyard)
                    problems.Add($"{s.Name}: has a shipyard = {s.HasShipyard}, but a shipyard-enabling industry = {yardIndustry}.");
                if (s.PopulationMillions < g.PopulationMinMillions || s.PopulationMillions > g.PopulationMaxMillions)
                    problems.Add($"{s.Name}: population {s.PopulationMillions} million is outside {g.PopulationMinMillions} to {g.PopulationMaxMillions}.");
            }

            var feeds = FreightLinks(galaxy, tuning);
            for (int i = 0; i < n; i++)
            {
                bool exports = Enumerable.Range(0, n).Any(j => feeds[i, j]);
                bool imports = Enumerable.Range(0, n).Any(j => feeds[j, i]);
                if (!exports) problems.Add($"{systems[i].Name}: produces nothing that another system uses (no export).");
                if (!imports) problems.Add($"{systems[i].Name}: uses nothing that another system produces (no import).");
            }

            if (!HasClosedThreeStopCircuit(feeds))
                problems.Add("No closed three-stop freight circuit exists.");

            var reached = ReachableFrom(galaxy, galaxy.StartSystemId, g.MaxHopMilliLy);
            foreach (var s in systems)
                if (!reached[s.Id]) problems.Add($"{s.Name}: cannot be reached from the start system in hops of {g.MaxHopMilliLy} milli-ly or less.");

            int yards = systems.Count(s => s.HasShipyard);
            if (yards < 2 || yards < g.ShipyardCountMin || yards > g.ShipyardCountMax)
                problems.Add($"Has {yards} shipyards; needs at least 2 and the allowed range is {g.ShipyardCountMin} to {g.ShipyardCountMax}.");

            long toYard = NearestShipyardDistance(galaxy, galaxy.StartSystemId);
            if (toYard > g.MaxHopMilliLy)
                problems.Add($"Start system {galaxy.StartSystem.Name} is {toYard} milli-ly from the nearest shipyard; must be within {g.MaxHopMilliLy}.");

            return problems;
        }

        /// <summary>
        /// feeds[a, b] is true when system a produces a commodity that system b consumes (a != b).
        /// </summary>
        public static bool[,] FreightLinks(Galaxy galaxy, TuningData tuning)
        {
            int n = galaxy.Systems.Count;
            var produces = new HashSet<string>[n];
            var consumes = new HashSet<string>[n];
            for (int i = 0; i < n; i++)
            {
                produces[i] = new HashSet<string>();
                consumes[i] = new HashSet<string>();
                foreach (var id in galaxy.Systems[i].IndustryIds)
                {
                    var ind = tuning.FindIndustry(id);
                    if (ind == null) continue;
                    produces[i].UnionWith(ind.Produces);
                    consumes[i].UnionWith(ind.Consumes);
                }
            }
            var feeds = new bool[n, n];
            for (int a = 0; a < n; a++)
            for (int b = 0; b < n; b++)
                feeds[a, b] = a != b && produces[a].Overlaps(consumes[b]);
            return feeds;
        }

        public static bool HasClosedThreeStopCircuit(bool[,] feeds)
        {
            int n = feeds.GetLength(0);
            for (int a = 0; a < n; a++)
            for (int b = 0; b < n; b++)
            {
                if (b == a || !feeds[a, b]) continue;
                for (int c = 0; c < n; c++)
                    if (c != a && c != b && feeds[b, c] && feeds[c, a]) return true;
            }
            return false;
        }

        /// <summary>Systems connected to <paramref name="fromId"/> by a chain of hops each no longer than <paramref name="maxHopMilliLy"/>.</summary>
        public static bool[] ReachableFrom(Galaxy galaxy, int fromId, long maxHopMilliLy)
        {
            int n = galaxy.Systems.Count;
            var seen = new bool[n];
            var queue = new Queue<int>();
            seen[fromId] = true;
            queue.Enqueue(fromId);
            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                for (int next = 0; next < n; next++)
                {
                    if (seen[next] || galaxy.DistanceMilliLy(cur, next) > maxHopMilliLy) continue;
                    seen[next] = true;
                    queue.Enqueue(next);
                }
            }
            return seen;
        }

        /// <summary>Distance to the closest shipyard, 0 if the system has one, long.MaxValue if none exist.</summary>
        public static long NearestShipyardDistance(Galaxy galaxy, int fromId)
        {
            long best = long.MaxValue;
            foreach (var s in galaxy.Systems)
                if (s.HasShipyard) best = System.Math.Min(best, galaxy.DistanceMilliLy(fromId, s.Id));
            return best;
        }
    }
}
