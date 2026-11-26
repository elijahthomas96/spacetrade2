using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Routes;
using SpaceTrade.Sim.Saves;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.World;
using static SpaceTrade.Sim.Tests.SaveTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>
    /// Session 10b: how the current rules scale to bigger galaxies. Nothing here changes a rule: the fixture tuning is used
    /// with only the system count and enough extra system names changed. These runs take minutes, so they are left out of
    /// the normal suite and run by name. Each reports its figures to the console.
    /// </summary>
    [Explicit("Measurement for session 10b. Takes minutes; run by name.")]
    [Category("Measurement")]
    [Timeout(3 * 60 * 60 * 1000)]
    public class ScalingMeasurementTests
    {
        private static readonly string ProgressFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "spacetrade2-10b-measurements.txt");
        /// <summary>The fixture tuning with exactly <paramref name="systems"/> systems, or the fixture as it is for 0.</summary>
        private static TuningData Sized(int systems)
        {
            var t = TestTuning.Load();
            if (systems == 0) return t;
            t.Galaxy.SystemCountMin = systems;
            t.Galaxy.SystemCountMax = systems;
            var names = new List<string>(t.Galaxy.Names);
            for (int i = names.Count; i < systems; i++) names.Add($"System {i + 1:000}");
            t.Galaxy.Names = names;
            return t;
        }

        private static string Label(int systems) => systems == 0 ? "8-12" : systems.ToString();

        private static void Report(string text)
        {
            UnityEngine.Debug.Log(text);
            TestContext.WriteLine(text);
            Progress(text);
        }

        private static void Progress(string text) =>
            System.IO.File.AppendAllText(ProgressFile, $"{System.DateTime.Now:HH:mm:ss} {text}{System.Environment.NewLine}");

        private static int Median(List<int> xs)
        {
            var s = xs.OrderBy(x => x).ToList();
            return s[s.Count / 2];
        }

        [TestCase(0)]
        [TestCase(25)]
        [TestCase(50)]
        [TestCase(100)]
        public void Generation_200Seeds(int systems)
        {
            var tuning = Sized(systems);
            var load = TuningLoader.LoadText(TuningWriter.Write(tuning));
            Assert.IsFalse(load.UsedDefaults, "The sized tuning must pass validation: " + string.Join("; ", load.Errors));

            int galaxyOk = 0, newGameOk = 0;
            var failures = new List<string>();
            var offers = new List<int>();
            var pools = new List<int>();
            var jobsPerPort = new List<int>();
            var jobsInHop = new List<int>();
            var startJobs = new List<int>();
            var feasibleAtStart = new List<int>();
            var saveKb = new List<int>();

            const int sampled = 10;
            var part = Stopwatch.StartNew();
            for (long seed = 1; seed <= sampled; seed++) GalaxyGenerator.Generate(seed, tuning);
            double galaxyMs = part.Elapsed.TotalMilliseconds / sampled;
            var sampleGalaxies = Enumerable.Range(1, sampled).Select(s => GalaxyGenerator.Generate(s, tuning)).ToList();
            part.Restart();
            foreach (var g in sampleGalaxies) Markets.Market.Create(g, tuning, 0);
            double marketMs = part.Elapsed.TotalMilliseconds / sampled;

            var clock = Stopwatch.StartNew();
            double slowest = 0;
            using (var folder = new TempSaveFolder())
            {
                for (long seed = 1; seed <= 200; seed++)
                {
                    if (seed % 20 == 0) Progress($"generation {Label(systems)}: seed {seed}, {clock.Elapsed.TotalSeconds:0} s");
                    var one = Stopwatch.StartNew();
                    var sim = NewGame.TryCreate(seed, tuning, out var problems);
                    slowest = System.Math.Max(slowest, one.Elapsed.TotalSeconds);
                    if (sim == null)
                    {
                        failures.Add($"seed {seed}: {string.Join(" ", problems)}");
                        continue;
                    }
                    newGameOk++;
                    var galaxy = sim.Market.Galaxy;
                    if (GalaxyRules.FindProblems(galaxy, tuning).Count == 0) galaxyOk++;
                    else failures.Add($"seed {seed}: galaxy rules: {string.Join(" ", GalaxyRules.FindProblems(galaxy, tuning))}");

                    var m = sim.Market;
                    offers.Add(m.Offers.Count);
                    pools.Add(m.Pools.Count);
                    long hop = tuning.Galaxy.MaxHopMilliLy;
                    foreach (var s in galaxy.Systems)
                    {
                        jobsPerPort.Add(m.OffersFrom(s.Id).Count() + m.PoolsFrom(s.Id).Count());
                        jobsInHop.Add(m.OffersFrom(s.Id).Count(o => o.DistanceMilliLy <= hop) + m.PoolsFrom(s.Id).Count(p => p.DistanceMilliLy <= hop));
                    }
                    startJobs.Add(m.OffersFrom(galaxy.StartSystemId).Count() + m.PoolsFrom(galaxy.StartSystemId).Count());
                    feasibleAtStart.Add(OpeningOffers.FindJobs(sim, 1).Count);
                    if (seed <= 10)
                    {
                        Ok(folder.Store.Write("g" + seed, sim, T0));
                        saveKb.Add((int)(new System.IO.FileInfo(folder.File("g" + seed + ".json")).Length / 1024));
                    }
                }
            }
            double seconds = clock.Elapsed.TotalSeconds;

            Report($"[10b generation {Label(systems)} systems] 200 seeds: new-game guarantee met on {newGameOk}, galaxy guarantees met on {galaxyOk}. " +
                   $"Time {seconds:0.0} s ({seconds / 200 * 1000:0} ms per new game, slowest {slowest * 1000:0} ms; " +
                   $"of which, averaged over seeds 1-{sampled}, galaxy {galaxyMs:0} ms and markets {marketMs:0} ms, the rest is the opening-offer check). " +
                   $"Freight offers min/median/max {offers.Min()}/{Median(offers)}/{offers.Max()}; passenger pools {pools.Min()}/{Median(pools)}/{pools.Max()}. " +
                   $"Jobs listed per port (offers + pools) median {Median(jobsPerPort)}, max {jobsPerPort.Max()}; within {tuning.Galaxy.MaxHopMilliLy / 1000} ly median {Median(jobsInHop)}, max {jobsInHop.Max()}. " +
                   $"Start port lists median {Median(startJobs)} jobs, of which the starter can fly median {Median(feasibleAtStart)} (min {feasibleAtStart.Min()}). " +
                   $"New-game save size median {Median(saveKb)} KB (seeds 1-10)." +
                   (failures.Count > 0 ? " Failures: " + string.Join(" | ", failures.Take(5)) : ""));
            Assert.AreEqual(200, newGameOk, string.Join("\n", failures));
            Assert.AreEqual(200, galaxyOk);
        }

        [TestCase(0, 30)]
        [TestCase(25, 30)]
        [TestCase(50, 30)]
        [TestCase(100, 30)]
        [TestCase(100, 1)]
        public void CatchUp_TenRouteShips_AtFiveTimes(int systems, int realDays)
        {
            var tuning = Sized(systems);
            using (var folder = new TempSaveFolder())
            {
                var sim = NewGame.TryCreate(1, tuning, out var problems);
                Assert.IsNotNull(sim, string.Join(" ", problems));
                sim.SetCredits(1_000_000_000);
                RouteTestSupport.Ok(sim.Play());
                RouteTestSupport.Ok(sim.SetSpeed(ClockSpeed.X5));

                var used = new HashSet<(int, int)>();
                int routed = 0;
                foreach (var offer in sim.Market.Offers.OrderBy(o => o.DistanceMilliLy))
                {
                    if (routed == 10) break;
                    if (!used.Add((System.Math.Min(offer.OriginId, offer.DestinationId), System.Math.Max(offer.OriginId, offer.DestinationId)))) continue;
                    var ship = sim.AddShip($"Hauler {routed + 1}", tuning.Start.StarterShipClass, tuning.Start.StarterDrive, offer.OriginId);
                    RouteTestSupport.Ok(sim.SetRoute(ship.Id, new List<RouteStop>
                    {
                        new RouteStop(offer.OriginId, new[] { offer.CommodityId }, carryPassengers: true),
                        new RouteStop(offer.DestinationId, carryPassengers: true),
                    }));
                    routed++;
                }
                Assert.AreEqual(10, routed);
                sim.Advance(3 * RouteTestSupport.Year);

                var clock = Stopwatch.StartNew();
                Ok(folder.Store.Write("measure", sim, T0));
                double firstSave = clock.Elapsed.TotalSeconds;
                long firstBytes = new System.IO.FileInfo(folder.File("measure.json")).Length;

                long now = T0 + realDays * RealDay;
                clock.Restart();
                var load = folder.Store.Load("measure", tuning, now);
                double loadSeconds = clock.Elapsed.TotalSeconds;
                Assert.IsTrue(load.Succeeded, string.Join(" ", load.Messages));

                long creditsBefore = load.Simulation.Credits;
                clock.Restart();
                var run = OfflineCatchUp.Begin(load.Simulation, load.SavedUtcMs, now);
                long tenth = System.Math.Max(1, run.TicksTotal / 10);
                while (!run.IsDone)
                {
                    run.Step(tenth);
                    Progress($"catch-up {Label(systems)}/{realDays}d: {run.ProgressPerMille / 10}% at {clock.Elapsed.TotalSeconds:0} s");
                }
                var summary = run.Finish();
                double catchUpSeconds = clock.Elapsed.TotalSeconds;

                clock.Restart();
                Ok(folder.Store.Write("measure", load.Simulation, now));
                double saveSeconds = clock.Elapsed.TotalSeconds;
                long bytes = new System.IO.FileInfo(folder.File("measure.json")).Length;

                var after = load.Simulation;
                Assert.AreEqual(5L * realDays * RealDay, summary.GameTicksPassed);
                Assert.AreEqual(after.Credits - creditsBefore, summary.NetCreditChange);
                Assert.AreEqual(summary.NetCreditChange, summary.VoyagePayouts + summary.InSystemIncome - summary.SuppliesBought);
                int waiting = after.Ships.Count(s => s.Route?.Status == RouteStatus.Waiting);
                int blocked = after.Ships.Count(s => s.Route?.Status == RouteStatus.Blocked);

                Report($"[10b catch-up {Label(systems)} systems, {realDays} real days at 5x = {summary.GameTicksPassed / (double)GameTime.TicksPerYear:0} game years] " +
                       $"{after.Market.Galaxy.Systems.Count} systems, {after.Market.Offers.Count} offers, {after.Market.Pools.Count} pools, 10 route ships + starter. " +
                       $"Load {loadSeconds:0.000} s, catch-up {catchUpSeconds:0.000} s, save {saveSeconds:0.000} s. " +
                       $"Save before {firstBytes / 1024.0:0.0} KB ({firstSave:0.000} s), after {bytes / 1024.0:0.0} KB with {after.Receipts.Count} receipts kept. " +
                       $"Deliveries {summary.DeliveryCount:N0} ({summary.DeliveriesNotListed:N0} not listed), payouts {summary.VoyagePayouts:N0}, supplies {summary.SuppliesBought:N0}, net {summary.NetCreditChange:N0}. " +
                       $"At the end: {waiting} waiting, {blocked} blocked.");
            }
        }
    }
}
