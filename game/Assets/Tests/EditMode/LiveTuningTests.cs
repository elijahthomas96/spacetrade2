using System.IO;
using NUnit.Framework;
using SpaceTrade.Sim.Tuning;
using UnityEngine;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>
    /// The one test that reads the live tuning.json (spec 12). Every other test uses the fixed fixture,
    /// so editing the live file can only break this one.
    /// </summary>
    public class LiveTuningTests
    {
        [Test]
        public void LiveTuningFile_Loads_PassesValidation_AndMeetsTheNewGameGuarantees()
        {
            string path = Path.Combine(Application.streamingAssetsPath, "tuning.json");
            var load = TuningLoader.LoadFile(path);
            Assert.IsFalse(load.UsedDefaults, "Live tuning.json did not load: " + string.Join("\n", load.Errors));
            Assert.IsEmpty(load.Errors);
            var t = load.Tuning;
            Assert.IsEmpty(TuningValidator.Validate(t), "Includes the starter-range check");

            int flown = 0;
            for (long seed = 1; seed <= 200; seed++)
            {
                var result = NewGame.Create(seed, t);
                Assert.IsFalse(result.UsedDefaults, $"seed {seed}: " + string.Join("\n", result.Errors));
                Assert.AreEqual(seed, result.SeedUsed, $"seed {seed}: the live file must meet the guarantee on the seed asked for");
                NewGameChecks.AssertGuarantee(result.Simulation, t, $"live, seed {seed}");
                if (NewGameChecks.AssertStarterReachesNearestShipyard(result.Simulation, t, $"live, seed {seed}")) flown++;
            }
            TestContext.WriteLine($"Live tuning: 200 seeds meet the guarantee; starter flown to its nearest yard on {flown}, the rest start at a yard");
        }

        /// <summary>
        /// Session 10c: the live file's job distance limit at 12, 25, and 50 systems (only the system count changed, with
        /// extra names). On 200 seeds each, every galaxy guarantee and the opening offers hold on the seed asked for, and no
        /// port offers a job beyond the limit.
        /// </summary>
        [TestCase(12)]
        [TestCase(25)]
        [TestCase(50)]
        public void LiveJobDistanceLimit_MeetsEveryGuarantee_On200Seeds(int systems)
        {
            var load = TuningLoader.LoadFile(Path.Combine(Application.streamingAssetsPath, "tuning.json"));
            Assert.IsFalse(load.UsedDefaults, string.Join("\n", load.Errors));
            var t = load.Tuning;
            t.Galaxy.SystemCountMin = systems;
            t.Galaxy.SystemCountMax = systems;
            var names = new System.Collections.Generic.List<string>(t.Galaxy.Names);
            for (int i = names.Count; i < systems; i++) names.Add($"System {i + 1:000}");
            t.Galaxy.Names = names;
            Assert.IsEmpty(TuningValidator.Validate(t));
            long limit = t.Galaxy.MaxJobDistanceMilliLy;

            for (long seed = 1; seed <= 200; seed++)
            {
                string context = $"live at {systems} systems, limit {limit}, seed {seed}";
                var sim = NewGame.TryCreate(seed, t, out var problems);
                Assert.IsNotNull(sim, $"{context}: " + string.Join("\n", problems));
                Assert.AreEqual(systems, sim.Market.Galaxy.Systems.Count, context);
                Assert.AreEqual(limit, sim.Market.MaxJobDistanceMilliLy, context);
                JobDistanceChecks.AssertGuaranteesWithin(sim, t, limit, context);
                NewGameChecks.AssertGuarantee(sim, t, context);
            }
        }
    }
}
