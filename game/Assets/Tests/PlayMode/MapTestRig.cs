using System.Collections;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Game.Art;
using SpaceTrade.Game.Audio;
using SpaceTrade.Game.Settings;
using SpaceTrade.Game.Boot;
using SpaceTrade.Game.Map;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Voyages;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace SpaceTrade.Game.Tests
{
    /// <summary>
    /// The map screen with a simulation runner on a fake real-time clock, a test camera, and a test mouse and keyboard.
    /// Time moves only when a test says so. A coroutine resumes after Update and before LateUpdate, so
    /// <see cref="Frames"/> waits two frames for the map to have drawn the latest tick.
    /// </summary>
    internal sealed class MapTestRig
    {
        public sealed class FakeTime : IRealTimeSource
        {
            public long NowMicroseconds { get; set; } = 1_000_000;
        }

        private GameObject _cameraObject, _runnerObject, _audioObject;
        private bool _settingsChanged, _listenerChanged;
        private float _oldListenerVolume;

        /// <summary>The sound player, with every volume at 100 unless <see cref="StartSettings"/> says otherwise.</summary>
        public GameAudio Audio { get; private set; }
        public GameSettings Settings { get; private set; }

        /// <summary>A settings file in a temporary test folder, never the player's real one.</summary>
        public SettingsStore SettingsStore { get; private set; }

        /// <summary>Set before opening to start with other settings.</summary>
        public GameSettings StartSettings { get; set; }

        /// <summary>Set before opening to keep whatever theme the test has put in place instead of loading the fixture.</summary>
        public bool KeepTheme { get; set; }
        private InputSettings.BackgroundBehavior _oldBackground;
        private InputSettings.EditorInputBehaviorInPlayMode _oldEditorBehavior;

        public MapScreen Map { get; private set; }
        public Simulation Sim { get; private set; }
        public SimulationRunner Runner { get; private set; }
        public FakeTime Time { get; } = new FakeTime();
        public Mouse Mouse { get; private set; }
        public Keyboard Keyboard { get; private set; }

        /// <summary>Real milliseconds to create the map screen and show the game on it.</summary>
        public double MapBuildMs { get; private set; }

        public IEnumerator Open(Simulation sim, bool fakeTime = true)
        {
            Build(sim, fakeTime);
            yield return Frames();
        }

        /// <summary>Sets everything up within the current frame.</summary>
        public void Build(Simulation sim, bool fakeTime = true)
        {
            ArtLibrary.Load(Resources.Load<ArtCatalog>(ArtCatalog.ResourceName));
            if (!KeepTheme) GameTestTheme.Use();
            Sim = sim;
            // The real sounds are started exactly as in the game, but the test run itself is kept silent at the speakers.
            _oldListenerVolume = AudioListener.volume;
            AudioListener.volume = 0f;
            _listenerChanged = true;
            SettingsStore = new SettingsStore(GameTestSettings.NewPath());
            Settings = StartSettings ?? new GameSettings { MasterVolume = 100, EffectsVolume = 100, MusicVolume = 100 };
            _audioObject = new GameObject("Test Audio Root");
            Audio = GameAudio.Create(_audioObject.transform, Settings);
            _cameraObject = new GameObject("Test Camera");
            var cam = _cameraObject.AddComponent<Camera>();
            cam.orthographic = true;
            cam.transform.position = new Vector3(0, 0, -10);
            _runnerObject = new GameObject("Test Runner");
            Runner = _runnerObject.AddComponent<SimulationRunner>();
            Runner.Run(sim, fakeTime ? Time : null);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Map = MapScreen.Create(cam);
            Map.Runner = Runner;
            Map.SettingsStore = SettingsStore;
            Map.Show(sim, new string[0], null);
            MapBuildMs = watch.Elapsed.TotalMilliseconds;

            // Changed in place: assigning a new InputSystem.settings makes the Input System destroy its default settings object.
            var settings = InputSystem.settings;
            _oldBackground = settings.backgroundBehavior;
            _oldEditorBehavior = settings.editorInputBehaviorInPlayMode;
            _settingsChanged = true;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Mouse = InputSystem.AddDevice<Mouse>("Test Mouse");
            Keyboard = InputSystem.AddDevice<Keyboard>("Test Keyboard");
            Map.Input.MouseDevice = Mouse;
            Map.Input.KeyboardDevice = Keyboard;
        }

        public void Close()
        {
            if (Mouse != null) InputSystem.RemoveDevice(Mouse);
            if (Keyboard != null) InputSystem.RemoveDevice(Keyboard);
            if (_settingsChanged)
            {
                InputSystem.settings.backgroundBehavior = _oldBackground;
                InputSystem.settings.editorInputBehaviorInPlayMode = _oldEditorBehavior;
            }
            if (Map != null) Object.Destroy(Map.gameObject);
            if (_cameraObject != null) Object.Destroy(_cameraObject);
            if (_runnerObject != null) Object.Destroy(_runnerObject);
            if (_audioObject != null) Object.Destroy(_audioObject);
            if (_listenerChanged) AudioListener.volume = _oldListenerVolume;
            if (SettingsStore != null) GameTestSettings.Remove(SettingsStore.Path);
            Mouse = null; Keyboard = null; _settingsChanged = false; Map = null; _cameraObject = null; _runnerObject = null;
            _audioObject = null; _listenerChanged = false; Audio = null; SettingsStore = null;
        }

        /// <summary>Two frames, so the map has placed the ships for the latest tick.</summary>
        public static IEnumerator Frames(int count = 2)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        /// <summary>Real time passes on the fake clock, then the map draws it.</summary>
        public IEnumerator RealMs(long ms)
        {
            Time.NowMicroseconds += ms * 1000;
            yield return Frames();
        }

        public Camera Camera => Map.Camera.Camera;

        public Vector2 ScreenOf(Vector2 world) => Camera.WorldToScreenPoint(world);

        public IEnumerator MouseTo(Vector2 position, bool left = false, bool right = false, float scroll = 0f)
        {
            var state = new MouseState { position = position, scroll = new Vector2(0f, scroll) };
            if (left) state = state.WithButton(MouseButton.Left);
            if (right) state = state.WithButton(MouseButton.Right);
            InputSystem.QueueStateEvent(Mouse, state);
            yield return null;
        }

        public IEnumerator Click(Vector2 position)
        {
            yield return MouseTo(position, left: true);
            yield return MouseTo(position);
            yield return null;
        }

        /// <summary>Holds the keys for <paramref name="frames"/> frames, then lets go.</summary>
        public IEnumerator Keys(int frames, params Key[] keys)
        {
            InputSystem.QueueStateEvent(Keyboard, new KeyboardState(keys));
            for (int i = 0; i < frames; i++) yield return null;
            InputSystem.QueueStateEvent(Keyboard, new KeyboardState());
            yield return Frames();
        }

        /// <summary>Sends the ship on an empty voyage to the nearest other system and returns that voyage.</summary>
        public static Voyage SendEmpty(Simulation sim, int shipId)
        {
            var ship = sim.FindShip(shipId);
            var galaxy = sim.Market.Galaxy;
            int from = ship.DockedSystemId.Value;
            int to = galaxy.Systems.Where(s => s.Id != from).OrderBy(s => galaxy.DistanceMilliLy(from, s.Id)).First().Id;
            var result = sim.Dispatch(new VoyagePlan(shipId, to), true);
            Assert.IsTrue(result.Succeeded, result.ToString());
            return sim.FindShip(shipId).Voyage;
        }

        /// <summary>A fixture game with a big enough purse and yard stock for the debug fleet (in memory only).</summary>
        public static Simulation FleetGame(int systems, int inFlight, int dockedAtYard = 0, bool pausedRoute = false)
        {
            var sim = GameTestTuning.NewGameWithSystems(systems, 1, DebugFleet.PrepareTuning);
            var problems = DebugFleet.Launch(sim, inFlight, dockedAtYard, pausedRoute);
            CollectionAssert.IsEmpty(problems, "debug fleet");
            return sim;
        }
    }
}
