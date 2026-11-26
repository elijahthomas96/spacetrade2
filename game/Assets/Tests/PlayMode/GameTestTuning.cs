using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Tuning;
using UnityEngine;

namespace SpaceTrade.Game.Tests
{
    /// <summary>The fixed test tuning fixture (the same file the EditMode tests use), never the live tuning.json.</summary>
    internal static class GameTestTuning
    {
        public static string FixturePath => Path.Combine(Application.dataPath, "Tests", "EditMode", "Fixtures", "test-tuning.json");

        public static TuningData Load()
        {
            var result = TuningLoader.LoadText(File.ReadAllText(FixturePath));
            Assert.IsFalse(result.UsedDefaults, "Test fixture failed to load: " + string.Join("; ", result.Errors));
            return result.Tuning;
        }

        /// <summary>A new game from the fixture with exactly <paramref name="systems"/> systems, adding "System 013" and so on as names.</summary>
        public static Simulation NewGameWithSystems(int systems, long seed = 1)
        {
            var t = Load();
            t.Galaxy.SystemCountMin = systems;
            t.Galaxy.SystemCountMax = systems;
            var names = new List<string>(t.Galaxy.Names);
            for (int i = names.Count; i < systems; i++) names.Add($"System {i + 1:000}");
            t.Galaxy.Names = names;
            for (int i = 0; i < 100; i++)
            {
                var game = NewGame.Create(seed + i, t);
                if (!game.UsedDefaults) return game.Simulation;
            }
            Assert.Fail($"No seed from {seed} gave a {systems}-system game from the fixture.");
            return null;
        }
    }
}
