using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.World;

namespace SpaceTrade.Sim.Tests
{
    public class GalaxyGeneratorTests
    {
        private const int SeedCount = 200;

        private static string Describe(Galaxy g)
        {
            var sb = new StringBuilder();
            sb.Append("seed=").Append(g.Seed).Append(" start=").Append(g.StartSystemId).Append('\n');
            foreach (var s in g.Systems)
                sb.Append(s.Id).Append('|').Append(s.Name).Append('|').Append(s.XMilliLy).Append(',').Append(s.YMilliLy)
                  .Append("|pop=").Append(s.PopulationMillions).Append("|yard=").Append(s.HasShipyard)
                  .Append('|').Append(string.Join("+", s.IndustryIds)).Append('\n');
            return sb.ToString();
        }

        [Test]
        public void SameSeed_GivesSameGalaxy()
        {
            var tuning = TestTuning.Load();
            foreach (long seed in new long[] { 0, 1, 42, -7, long.MaxValue, 123456789 })
                Assert.AreEqual(Describe(GalaxyGenerator.Generate(seed, tuning)), Describe(GalaxyGenerator.Generate(seed, tuning)), $"Seed {seed}");
        }

        [Test]
        public void SameSeed_SameGalaxy_EvenWithFreshlyLoadedTuning()
        {
            var a = GalaxyGenerator.Generate(2026, TestTuning.Load());
            var b = GalaxyGenerator.Generate(2026, TestTuning.Load());
            Assert.AreEqual(Describe(a), Describe(b));
        }

        [Test]
        public void DifferentSeeds_GiveDifferentGalaxies()
        {
            var tuning = TestTuning.Load();
            var layouts = new HashSet<string>();
            for (long seed = 1; seed <= 50; seed++)
            {
                var g = GalaxyGenerator.Generate(seed, tuning);
                layouts.Add(Describe(g).Substring(Describe(g).IndexOf('\n') + 1));
            }
            Assert.AreEqual(50, layouts.Count, "Every one of 50 seeds should give a different map.");
        }

        [Test]
        public void TwoHundredSeeds_AllMeetEveryGuarantee()
        {
            var tuning = TestTuning.Load();
            var failures = new List<string>();
            var systemCounts = new HashSet<int>();
            for (long seed = 1; seed <= SeedCount; seed++)
            {
                var g = GalaxyGenerator.Generate(seed, tuning);
                systemCounts.Add(g.Systems.Count);
                failures.AddRange(IndependentCheck(g, tuning).Select(p => $"seed {seed}: {p}"));
                failures.AddRange(GalaxyRules.FindProblems(g, tuning).Select(p => $"seed {seed} (generator rules): {p}"));
            }
            Assert.IsEmpty(failures, string.Join("\n", failures.Take(50)));
            TestContext.WriteLine($"{SeedCount} seeds checked; system counts seen: {string.Join(", ", systemCounts.OrderBy(x => x))}");
        }

        [Test]
        public void FixtureUsesSpecGalaxyRanges()
        {
            var g = TestTuning.Load().Galaxy;
            Assert.AreEqual(8, g.SystemCountMin);
            Assert.AreEqual(12, g.SystemCountMax);
            Assert.AreEqual(2500, g.NearestNeighbourMinMilliLy);
            Assert.AreEqual(7000, g.NearestNeighbourMaxMilliLy);
            Assert.AreEqual(7000, g.MaxHopMilliLy);
            Assert.AreEqual(1, g.IndustriesPerSystemMin);
            Assert.AreEqual(3, g.IndustriesPerSystemMax);
        }

        [Test]
        public void GeneratedSystemsAreOnPositiveCoordinates()
        {
            var tuning = TestTuning.Load();
            for (long seed = 1; seed <= 20; seed++)
            {
                var g = GalaxyGenerator.Generate(seed, tuning);
                Assert.AreEqual(0, g.Systems.Min(s => s.XMilliLy));
                Assert.AreEqual(0, g.Systems.Min(s => s.YMilliLy));
            }
        }

        [Test]
        public void Rules_DetectBrokenGalaxies()
        {
            var tuning = TestTuning.Load();
            var good = GalaxyGenerator.Generate(5, tuning);
            Assert.IsEmpty(GalaxyRules.FindProblems(good, tuning));

            // Remove every shipyard but one.
            int keptYard = good.Systems.First(s => s.HasShipyard).Id;
            var oneYard = Rebuild(good, good.StartSystemId, s => s.HasShipyard && s.Id != keptYard
                ? new StarSystem(s.Id, s.Name, s.XMilliLy, s.YMilliLy, s.PopulationMillions, s.IndustryIds.Where(i => i != "shipyard_works").Append("factory").Distinct().Take(3).ToList(), false)
                : s);
            Assert.IsTrue(GalaxyRules.FindProblems(oneYard, tuning).Any(p => p.Contains("shipyards")));

            // Move one system far away.
            var stranded = Rebuild(good, good.StartSystemId, s => s.Id == (good.StartSystemId + 1) % good.Systems.Count
                ? new StarSystem(s.Id, s.Name, s.XMilliLy + 500_000, s.YMilliLy, s.PopulationMillions, s.IndustryIds, s.HasShipyard)
                : s);
            var strandedProblems = GalaxyRules.FindProblems(stranded, tuning);
            Assert.IsTrue(strandedProblems.Any(p => p.Contains("cannot be reached")), string.Join("\n", strandedProblems));
            Assert.IsTrue(strandedProblems.Any(p => p.Contains("nearest neighbour")), string.Join("\n", strandedProblems));

            // Give every system only population hubs: no exports, no circuit.
            var noTrade = Rebuild(good, good.StartSystemId, s =>
                new StarSystem(s.Id, s.Name, s.XMilliLy, s.YMilliLy, s.PopulationMillions, new List<string> { "population_hub" }, false));
            var noTradeProblems = GalaxyRules.FindProblems(noTrade, tuning);
            Assert.IsTrue(noTradeProblems.Any(p => p.Contains("No closed three-stop freight circuit")));
            Assert.IsTrue(noTradeProblems.Any(p => p.Contains("no export")));
        }

        private static Galaxy Rebuild(Galaxy g, int start, System.Func<StarSystem, StarSystem> change)
            => new Galaxy(g.Seed, start, g.Systems.Select(change).ToList());

        /// <summary>
        /// The spec 4 guarantees, written separately from the generator's own checks.
        /// </summary>
        private static List<string> IndependentCheck(Galaxy g, TuningData t)
        {
            var p = new List<string>();
            int n = g.Systems.Count;
            var gt = t.Galaxy;

            if (n < gt.SystemCountMin || n > gt.SystemCountMax) p.Add($"system count {n}");
            if (n < 8 || n > 12) p.Add($"system count {n} outside spec 8 to 12");
            if (g.Systems.Select(s => s.Name).Distinct().Count() != n) p.Add("duplicate names");
            if (g.Systems.Any(s => !gt.Names.Contains(s.Name))) p.Add("name not from tuning list");

            var d = new long[n, n];
            for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
                d[i, j] = g.DistanceMilliLy(i, j);

            for (int i = 0; i < n; i++)
            {
                long nn = Enumerable.Range(0, n).Where(j => j != i).Min(j => d[i, j]);
                if (nn < gt.NearestNeighbourMinMilliLy || nn > gt.NearestNeighbourMaxMilliLy) p.Add($"system {i} nearest neighbour {nn}");
            }

            // Reachability: flood fill from the start along hops <= maxHopMilliLy.
            var reached = new HashSet<int> { g.StartSystemId };
            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int i = 0; i < n; i++)
                {
                    if (reached.Contains(i)) continue;
                    if (reached.Any(r => d[r, i] <= gt.MaxHopMilliLy)) { reached.Add(i); grew = true; }
                }
            }
            if (reached.Count != n) p.Add($"only {reached.Count} of {n} systems reachable from start");

            // Exports and imports.
            HashSet<string> Made(StarSystem s) => new HashSet<string>(s.IndustryIds.SelectMany(id => t.FindIndustry(id).Produces));
            HashSet<string> Used(StarSystem s) => new HashSet<string>(s.IndustryIds.SelectMany(id => t.FindIndustry(id).Consumes));
            bool Ships(int a, int b) => a != b && Made(g.Systems[a]).Overlaps(Used(g.Systems[b]));
            for (int i = 0; i < n; i++)
            {
                if (!Enumerable.Range(0, n).Any(j => Ships(i, j))) p.Add($"system {i} has no export");
                if (!Enumerable.Range(0, n).Any(j => Ships(j, i))) p.Add($"system {i} has no import");
            }

            // Closed three-stop freight circuit.
            bool circuit = false;
            for (int a = 0; a < n && !circuit; a++)
            for (int b = 0; b < n && !circuit; b++)
            for (int c = 0; c < n && !circuit; c++)
                if (a != b && b != c && a != c && Ships(a, b) && Ships(b, c) && Ships(c, a)) circuit = true;
            if (!circuit) p.Add("no closed three-stop circuit");

            // Shipyards.
            var yards = g.Systems.Where(s => s.HasShipyard).ToList();
            if (yards.Count < 2) p.Add($"only {yards.Count} shipyards");
            if (g.Systems.Any(s => s.HasShipyard != s.IndustryIds.Contains("shipyard_works"))) p.Add("shipyard flag does not match industries");
            if (yards.Count > 0 && yards.Min(y => d[g.StartSystemId, y.Id]) > gt.MaxHopMilliLy) p.Add("start too far from a shipyard");

            // Industries per system.
            foreach (var s in g.Systems)
                if (s.IndustryIds.Count < 1 || s.IndustryIds.Count > 3 || s.IndustryIds.Distinct().Count() != s.IndustryIds.Count)
                    p.Add($"system {s.Id} industries: {string.Join("+", s.IndustryIds)}");

            return p;
        }
    }
}
