using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SpaceTrade.Game.Audio;
using SpaceTrade.Game.Map;
using SpaceTrade.Game.Settings;
using SpaceTrade.Game.Theming;
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

        [Tooltip("Editor and Development Builds only. Starts the game with debug permission, so the debug skip (spec 13) is allowed.")]
        [SerializeField] private bool debugMode;

        [Tooltip("Editor and Development Builds only. Writes one console line for every sound event as it fires.")]
        [SerializeField] private bool debugLogSounds;

        public SimulationRunner Runner { get; private set; }
        public MapScreen Map { get; private set; }
        public GameAudio Audio { get; private set; }
        public SettingsStore SettingsStore { get; private set; }
        public long SeedUsed { get; private set; }

        /// <summary>Problems found in theme.json at startup. Empty when the file was used.</summary>
        public IReadOnlyList<string> ThemeProblems { get; private set; } = new string[0];

        /// <summary>The last new game's one-off work: creating the game, and building the map for it (real milliseconds).</summary>
        public double LastNewGameMs { get; private set; }
        public double LastMapBuildMs { get; private set; }

        /// <summary>The player's settings file: beside the saves folder, never inside a save.</summary>
        public static string SettingsPath => Path.Combine(Application.persistentDataPath, SettingsStore.FileName);

        private void Start()
        {
            // The look and the controls are read before any screen is built.
            var theme = Theme.LoadFile(Path.Combine(Application.streamingAssetsPath, Theme.FileName));
            if (theme.UsedDefaults)
            {
                var problems = new List<string> { "theme.json has problems, so the built-in look and controls are used:" };
                problems.AddRange(theme.Errors);
                ThemeProblems = problems;
            }

            SettingsStore = new SettingsStore(SettingsPath);
            var settings = SettingsStore.Load();
            if (SettingsStore.LastProblem != null) _startupMessages.Add(SettingsStore.LastProblem);
            Audio = GameAudio.Create(transform, settings);

            Runner = gameObject.AddComponent<SimulationRunner>();
            Map = MapScreen.Create(Camera.main);
            Map.Runner = Runner;
            Map.SettingsStore = SettingsStore;
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
            Audio.LogEvents = debugLogSounds;
#endif
            NewGameNow(count, seed, inFlight, docked, pausedRoute);
        }

        private readonly List<string> _startupMessages = new List<string>();

#if DEBUG
        public FrameTimeProbe Probe { get; private set; }

        /// <summary>Debug only: the next new game is started with debug permission, so the debug skip is allowed.</summary>
        public bool DebugMode { get => debugMode; set => debugMode = value; }

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
        public void StartFrameCheck(int systemCount, int shipsInFlight, float secondsPerSpeed, bool fullNotificationList = false, long seed = 0)
        {
            FrameCheckRunning = true;
            FrameCheckReport = null;
            StartCoroutine(FrameCheck(systemCount, shipsInFlight, secondsPerSpeed, fullNotificationList, seed));
        }

        private System.Collections.IEnumerator FrameCheck(int systemCount, int shipsInFlight, float secondsPerSpeed, bool fullNotificationList, long seed)
        {
            Probe.ClearAll();
            yield return null;
            NewGameNow(systemCount, seed, shipsInFlight, 0, false);
            yield return null;
            yield return null;
            if (fullNotificationList)
            {
                // Filling the list is debug setup, not something a player's game ever does in one frame, so it has a frame
                // of its own and is reported apart from the one-off frames the 250 ms limit is about. It moves the
                // simulation's time, so it is declared as a time jump like every other skip.
                var watch = Stopwatch.StartNew();
                Runner.BeginTimeJump();
                DebugFleet.FillNotifications(Runner.Simulation);
                Runner.EndTimeJump();
                Probe.MarkOneOff("debug setup only: filling the notification list", watch.Elapsed.TotalMilliseconds);
                yield return null;
                yield return null;
            }
            string prefix = $"{systemCount} systems, {Runner.Simulation.Ships.Count} ships ({InFlight()} in flight)";
            if (fullNotificationList)
            {
                prefix += $", {Runner.Simulation.Notifications.Count} notifications with the list open";
                if (!Map.Bar.Notifications.IsOpen) Map.Bar.ToggleNotifications();
                yield return null;
            }
            yield return MapFrameCheck.Run(Map, Runner.Driver, Probe, secondsPerSpeed, prefix);
            FrameCheckReport = Probe.Report();
            FrameCheckRunning = false;
        }

        /// <summary>
        /// Debug only: a new game with this many systems, ships in flight, and one ship docked at a yard, then the job-list
        /// frame-time scenario (planner and system view opened and sorted at 5x).
        /// </summary>
        public void StartPlannerFrameCheck(int systemCount, int shipsInFlight, float secondsEach, long seed = 0)
        {
            FrameCheckRunning = true;
            FrameCheckReport = null;
            StartCoroutine(PlannerCheck(systemCount, shipsInFlight, secondsEach, seed));
        }

        private System.Collections.IEnumerator PlannerCheck(int systemCount, int shipsInFlight, float secondsEach, long seed)
        {
            Probe.ClearAll();
            yield return null;
            NewGameNow(systemCount, seed, shipsInFlight, 1, false);
            yield return null;
            yield return null;
            string prefix = $"{systemCount} systems, {Runner.Simulation.Ships.Count} ships ({InFlight()} in flight)";
            yield return PlannerFrameCheck.Run(Map, Runner.Driver, Probe, secondsEach, prefix);
            FrameCheckReport = Probe.Report() + PlannerFrameCheck.LastSummary + "\n";
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
            var messages = new List<string>(ThemeProblems);
            messages.AddRange(_startupMessages);
            SpaceTrade.Sim.DebugCapability debug = null;
#if DEBUG
            if (debugMode) debug = SpaceTrade.Sim.DebugCapability.Grant();
#endif
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
            if (systemCount > 0) game = CreateSized(tuning, systemCount, seed, debug);
            else
            {
                game = NewGame.Create(seed, tuning, debug);
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
        private static NewGameResult CreateSized(TuningData tuning, int systemCount, long seed, SpaceTrade.Sim.DebugCapability debug)
        {
            tuning.Galaxy.SystemCountMin = systemCount;
            tuning.Galaxy.SystemCountMax = systemCount;
            var names = new List<string>(tuning.Galaxy.Names);
            for (int i = names.Count; i < systemCount; i++) names.Add($"System {i + 1:000}");
            tuning.Galaxy.Names = names;
            NewGameResult game = null;
            for (int i = 0; i < 100; i++)
            {
                game = NewGame.Create(seed + i, tuning, debug);
                if (!game.UsedDefaults) return game;
            }
            return game;
        }
    }
}
