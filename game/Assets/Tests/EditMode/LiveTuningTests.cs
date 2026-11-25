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
    }
}
