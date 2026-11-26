using System;
using System.Collections.Generic;
using System.IO;
using SpaceTrade.Game.Map;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Tuning;
using UnityEngine;

namespace SpaceTrade.Game.Boot
{
    /// <summary>
    /// The startup scene's one component. It loads tuning.json, creates a new game in memory, drives it from real time,
    /// and opens the galaxy map. Nothing is saved yet: saves, autosave, and offline catch-up are wired in session 17.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [Tooltip("Editor and Development Builds only. 0 uses tuning.json; otherwise the galaxy gets exactly this many systems.")]
        [SerializeField] private int debugSystemCount;

        [Tooltip("Editor and Development Builds only. 0 picks a seed from the clock.")]
        [SerializeField] private long debugSeed;

        public SimulationRunner Runner { get; private set; }
        public MapScreen Map { get; private set; }
        public long SeedUsed { get; private set; }

        private void Start()
        {
            Runner = gameObject.AddComponent<SimulationRunner>();
            Map = MapScreen.Create(Camera.main);
            int count = 0;
            long seed = 0;
#if DEBUG
            count = debugSystemCount;
            seed = debugSeed;
#endif
            NewGameNow(count, seed);
        }

#if DEBUG
        /// <summary>Debug only: throws away the current game and starts a new one with exactly this many systems (0 = tuning.json).</summary>
        public void DebugRestart(int systemCount, long seed = 0) => NewGameNow(systemCount, seed);
#endif

        private void NewGameNow(int systemCount, long seed)
        {
            var messages = new List<string>();
            var load = TuningLoader.LoadFile(Path.Combine(Application.streamingAssetsPath, "tuning.json"));
            if (load.UsedDefaults)
            {
                messages.Add("tuning.json has problems, so the built-in defaults are used:");
                messages.AddRange(load.Errors);
            }
            var tuning = load.Tuning;
            if (seed == 0) seed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 1_000_000_000L;

            NewGameResult game;
            if (systemCount > 0) game = CreateSized(tuning, systemCount, seed);
            else
            {
                game = NewGame.Create(seed, tuning);
                if (game.UsedDefaults)
                {
                    messages.Add("This new game could not be built from tuning.json, so the built-in defaults are used:");
                    messages.AddRange(game.Errors);
                }
            }

            SeedUsed = game.SeedUsed;
            Runner.Run(game.Simulation);
            string debugLine = null;
#if DEBUG
            debugLine = $"Debug: seed {game.SeedUsed}, {game.Simulation.Market.Galaxy.Systems.Count} systems";
#endif
            Map.Show(game.Simulation, messages, debugLine);
        }

        /// <summary>
        /// Debug only: tuning.json with exactly <paramref name="systemCount"/> systems, adding "System 013" and so on when
        /// the name list is too short. Seeds that cannot meet the new-game guarantees are skipped.
        /// </summary>
        private static NewGameResult CreateSized(TuningData tuning, int systemCount, long seed)
        {
            tuning.Galaxy.SystemCountMin = systemCount;
            tuning.Galaxy.SystemCountMax = systemCount;
            var names = new List<string>(tuning.Galaxy.Names);
            for (int i = names.Count; i < systemCount; i++) names.Add($"System {i + 1:000}");
            tuning.Galaxy.Names = names;
            NewGameResult game = null;
            for (int i = 0; i < 100; i++)
            {
                game = NewGame.Create(seed + i, tuning);
                if (!game.UsedDefaults) return game;
            }
            return game;
        }
    }
}
