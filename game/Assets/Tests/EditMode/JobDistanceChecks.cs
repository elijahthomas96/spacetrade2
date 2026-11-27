using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.World;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>
    /// Session 10c: the galaxy guarantees (spec 4) and the job distance limit (spec 5), checked on a game's market
    /// separately from <see cref="GalaxyRules"/>.
    /// </summary>
    internal static class JobDistanceChecks
    {
        public static void AssertGuaranteesWithin(Simulation sim, TuningData t, long limit, string context)
        {
            var m = sim.Market;
            var g = m.Galaxy;
            int n = g.Systems.Count;
            Assert.IsEmpty(GalaxyRules.FindProblems(g, t), context);

            HashSet<string> Made(int s) => new HashSet<string>(g.Systems[s].IndustryIds.SelectMany(id => t.FindIndustry(id).Produces));
            HashSet<string> Used(int s) => new HashSet<string>(g.Systems[s].IndustryIds.SelectMany(id => t.FindIndustry(id).Consumes));
            bool Near(int a, int b) => a != b && g.DistanceMilliLy(a, b) <= limit;

            // No job beyond the limit, and every pair within it has its job.
            foreach (var o in m.Offers)
                Assert.IsTrue(Near(o.OriginId, o.DestinationId), $"{context}: offer {o.CommodityId} {o.OriginId}->{o.DestinationId} is {g.DistanceMilliLy(o.OriginId, o.DestinationId)} milli-ly away");
            foreach (var p in m.Pools)
                Assert.IsTrue(Near(p.OriginId, p.DestinationId), $"{context}: pool {p.OriginId}->{p.DestinationId} is {g.DistanceMilliLy(p.OriginId, p.DestinationId)} milli-ly away");
            int offers = 0, pools = 0;
            for (int a = 0; a < n; a++)
            for (int b = 0; b < n; b++)
            {
                if (!Near(a, b)) continue;
                pools++;
                Assert.IsNotNull(m.FindPool(a, b), $"{context}: no pool {a}->{b}");
                foreach (var c in Made(a).Intersect(Used(b)))
                {
                    offers++;
                    Assert.IsNotNull(m.FindOffer(a, c, b), $"{context}: no {c} offer {a}->{b}");
                }
            }
            Assert.AreEqual(offers, m.Offers.Count, context);
            Assert.AreEqual(pools, m.Pools.Count, context);

            // An export and an import for every system, within the limit.
            bool Ships(int a, int b) => Near(a, b) && Made(a).Overlaps(Used(b));
            for (int i = 0; i < n; i++)
            {
                Assert.IsTrue(Enumerable.Range(0, n).Any(j => Ships(i, j)), $"{context}: system {i} has no export within {limit}");
                Assert.IsTrue(Enumerable.Range(0, n).Any(j => Ships(j, i)), $"{context}: system {i} has no import within {limit}");
            }

            // A closed three-stop freight circuit, every leg within the limit.
            bool circuit = false;
            for (int a = 0; a < n && !circuit; a++)
            for (int b = 0; b < n && !circuit; b++)
            for (int c = 0; c < n && !circuit; c++)
                if (a != b && b != c && a != c && Ships(a, b) && Ships(b, c) && Ships(c, a)) circuit = true;
            Assert.IsTrue(circuit, $"{context}: no closed three-stop circuit within {limit}");

            // Reachability from the start in hops no longer than maxHop, each of which has a passenger job.
            var reached = new HashSet<int> { g.StartSystemId };
            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int i = 0; i < n; i++)
                {
                    if (reached.Contains(i)) continue;
                    if (reached.Any(r => g.DistanceMilliLy(r, i) <= t.Galaxy.MaxHopMilliLy && m.FindPool(r, i) != null && m.FindPool(i, r) != null))
                    {
                        reached.Add(i);
                        grew = true;
                    }
                }
            }
            Assert.AreEqual(n, reached.Count, $"{context}: only {reached.Count} of {n} systems reachable by hops that have jobs");
        }
    }
}
