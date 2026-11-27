using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        [Tooltip("Editor and Development Builds only. Ships put in flight at the start: 1 sends the starter ship on a job; more are bought and sent on empty voyages.")]
        [SerializeField] private int debugShipsInFlight;

        [Tooltip("Editor and Development Builds only. Extra ships bought and left docked at the first shipyard.")]
        [SerializeField] private int debugShipsDocked;

        [Tooltip("Editor and Development Builds only. Puts one more ship on a three-stop route and pauses it.")]
        [SerializeField] private bool debugPausedRoute;

        public SimulationRunner Runner { get; private set; }
        public MapScreen Map { get; private set; }
        public long SeedUsed { get; private set; }

        /// <summary>The last new game's one-off work: creating the game, and building the map for it (real milliseconds).</summary>
        public double LastNewGameMs { get; private set; }
        public double LastMapBuildMs { get; private set; }

        private void Start()
        {
            Runner = gameObject.AddComponent<SimulationRunner>();
            Map = MapScreen.Create(Camera.main);
            int count = 0, inFlight = 0, docked = 0;
            long seed = 0;
            bool pausedRoute = false;
#if DEBUG
            Probe = gameObject.AddComponent<FrameTimeProbe>();
            count = debugSystemCount;
            seed = debugSeed;
            inFlight = debugShipsInFlight;
            docked = debugShipsDocked;
            pausedRoute = debugPausedRoute;
#endif
            NewGameNow(count, seed, inFlight, docked, pausedRoute);
        }

#if DEBUG
        public FrameTimeProbe Probe { get; private set; }

        /// <summary>Debug setup work after the last new game (buying and sending the debug fleet), real milliseconds.</summary>
        public double LastDebugFleetMs { get; private set; }

        /// <summary>Commands the debug fleet had refused, if any.</summary>
        public IReadOnlyList<string> LastDebugFleetProblems { get; private set; } = new string[0];

        /// <summary>Debug only: throws away the current game and starts a new one with exactly this many systems (0 = tuning.json).</summary>
        public void DebugRestart(int systemCount, long seed = 0, int shipsInFlight = 0, int shipsDocked = 0, bool pausedRoute = false) =>
            NewGameNow(systemCount, seed, shipsInFlight, shipsDocked, pausedRoute);

        public bool FrameCheckRunning { get; private set; }
        public string FrameCheckReport { get; private set; }

        /// <summary>Debug only: a new game with this many systems and ships in flight, then the map frame-time scenario at 1x, 3x, and 5x.</summary>
        public void StartFrameCheck(int systemCount, int shipsInFlight, float secondsPerSpeed)
        {
            FrameCheckRunning = true;
            FrameCheckReport = null;
            StartCoroutine(FrameCheck(systemCount, shipsInFlight, secondsPerSpeed));
        }

        private System.Collections.IEnumerator FrameCheck(int systemCount, int shipsInFlight, float secondsPerSpeed)
        {
            Probe.ClearAll();
            yield return null;
            NewGameNow(systemCount, 0, shipsInFlight, 0, false);
            yield return null;
            yield return null;
            string prefix = $"{systemCount} systems, {Runner.Simulation.Ships.Count} ships ({InFlight()} in flight)";
            yield return MapFrameCheck.Run(Map, Runner.Driver, Probe, secondsPerSpeed, prefix);
            FrameCheckReport = Probe.Report();
            FrameCheckRunning = false;
        }

        private int InFlight()
        {
            int n = 0;
            foreach (var s in Runner.Simulation.Ships) if (s.IsInTransit) n++;
            return n;
        }
#endif

        private void NewGameNow(int systemCount, long seed, int shipsInFlight, int shipsDocked, bool pausedRoute)
        {
            var watch = Stopwatch.StartNew();
            var messages = new List<string>();
            var load = TuningLoader.LoadFile(Path.Combine(Application.streamingAssetsPath, "tuning.json"));
            if (load.UsedDefaults)
            {
                messages.Add("tuning.json has problems, so the built-in defaults are used:");
                messages.AddRange(load.Errors);
            }
            var tuning = load.Tuning;
            if (seed == 0) seed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() % 1_000_000_000L;
#if DEBUG
            bool fleet = DebugFleet.NeedsPurchases(shipsInFlight, shipsDocked, pausedRoute);
            if (fleet) DebugFleet.PrepareTuning(tuning);
#endif

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
            LastNewGameMs = watch.Elapsed.TotalMilliseconds;

            string debugLine = null;
#if DEBUG
            watch.Restart();
            LastDebugFleetProblems = shipsInFlight > 0 || fleet ? DebugFleet.Launch(game.Simulation, shipsInFlight, shipsDocked, pausedRoute) : new List<string>();
            LastDebugFleetMs = watch.Elapsed.TotalMilliseconds;
            messages.AddRange(LastDebugFleetProblems);
            debugLine = $"Debug: seed {game.SeedUsed}, {game.Simulation.Market.Galaxy.Systems.Count} systems";
#endif

            SeedUsed = game.SeedUsed;
            Runner.Run(game.Simulation);
            watch.Restart();
            Map.Show(game.Simulation, messages, debugLine);
            LastMapBuildMs = watch.Elapsed.TotalMilliseconds;
#if DEBUG
            if (Probe != null)
            {
                int systems = game.Simulation.Market.Galaxy.Systems.Count;
                Probe.MarkOneOff($"new game, {systems} systems", LastNewGameMs);
                Probe.MarkOneOff($"map build, {systems} systems, {game.Simulation.Ships.Count} ships", LastMapBuildMs);
                if (LastDebugFleetMs > 0.5) Probe.MarkOneOff($"debug fleet setup, {game.Simulation.Ships.Count} ships", LastDebugFleetMs);
            }
#endif
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
