using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Game.Art;
using SpaceTrade.Game.Boot;
using SpaceTrade.Game.Map;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.Voyages;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace SpaceTrade.Game.EditorTests
{
    /// <summary>
    /// The project enters Play mode without reloading code (AGENTS.md), so this enters Play twice in a row, in an empty
    /// scene with the test tuning fixture, and checks the map and its ships work the same in the second session: every
    /// ship image is a live file, not a stand-in, and a generated stand-in is made fresh rather than reused from the
    /// session before, whose objects Unity destroyed.
    /// </summary>
    public class PlaySessionTwiceTests
    {
        private static string FixturePath => Path.Combine(Application.dataPath, "Tests", "EditMode", "Fixtures", "test-tuning.json");

        private SceneSetup[] _scenes;
        private float? _listenerVolume;

        [UnitySetUp]
        public IEnumerator KeepTheOpenScenes()
        {
            var active = EditorSceneManager.GetActiveScene();
            if (active.isDirty) Assert.Inconclusive("The open scene has unsaved changes; save it before running this test.");
            _scenes = EditorSceneManager.GetSceneManagerSetup();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator RestoreTheOpenScenes()
        {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            if (_listenerVolume.HasValue) AudioListener.volume = _listenerVolume.Value;
            _listenerVolume = null;
            if (_scenes != null && _scenes.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(_scenes);
            _scenes = null;
        }

        private static Simulation NewFixtureGame()
        {
            var load = TuningLoader.LoadText(File.ReadAllText(FixturePath));
            Assert.IsFalse(load.UsedDefaults, string.Join("; ", load.Errors));
            var t = load.Tuning;
            DebugFleet.PrepareTuning(t);
            for (int seed = 1; seed < 100; seed++)
            {
                var game = NewGame.Create(seed, t);
                if (!game.UsedDefaults) return game.Simulation;
            }
            Assert.Fail("No fixture game");
            return null;
        }

        private static bool Alive(Sprite s) => s != null && s.texture != null;

        [UnityTest]
        public IEnumerator TheMapAndItsShips_WorkTheSame_InASecondPlaySession()
        {
            var sessions = new List<string>();
            Sprite firstStandIn = null;
            for (int session = 1; session <= 2; session++)
            {
                yield return new EnterPlayMode();
                Assert.IsTrue(EditorApplication.isPlaying);

                var cameraObject = new GameObject("Session Camera");
                var cam = cameraObject.AddComponent<Camera>();
                cam.orthographic = true;
                cam.transform.position = new Vector3(0, 0, -10);
                var sim = NewFixtureGame();
                var classes = sim.Market.Tuning.Ships.Select(c => c.Id).ToList();
                int yard = sim.Market.Shipyards[0].SystemId;
                foreach (var c in classes) Assert.IsTrue(sim.BuyShip(yard, c, out _).Succeeded, c);
                var starter = sim.Ships[0];
                int to = sim.Market.Galaxy.Systems.Where(s => s.Id != starter.DockedSystemId).OrderBy(s => sim.Market.Galaxy.DistanceMilliLy(starter.DockedSystemId.Value, s.Id)).First().Id;
                Assert.IsTrue(sim.Dispatch(new VoyagePlan(starter.Id, to), true).Succeeded);
                var runnerObject = new GameObject("Session Runner");
                var runner = runnerObject.AddComponent<SimulationRunner>();
                runner.Run(sim);
                var map = MapScreen.Create(cam);
                map.Show(sim, new string[0], null);
                runner.Driver.Play();
                for (int i = 0; i < 5; i++) yield return null;

                Assert.AreEqual(starter.Id, map.SelectedShipId, $"session {session}: the starter ship is selected");
                foreach (var ship in sim.Ships)
                {
                    var icon = map.Ships.IconOf(ship.Id);
                    Assert.IsTrue(Alive(icon), $"session {session}: {ship.ClassId} map icon is alive");
                    Assert.IsFalse(ArtLibrary.IsStandIn(icon), $"session {session}: {ship.ClassId} map icon is its file");
                }
                Assert.Greater(sim.CurrentTick, 0, $"session {session}: game time moves");
                Assert.IsTrue(map.Routes.Segments.Any(s => s.From == starter.Voyage.OriginId && s.To == to), $"session {session}: the current leg is drawn");

                map.OpenCloseUp();
                yield return null;
                yield return null;
                Assert.IsTrue(map.IsCloseUpOpen);
                Assert.IsTrue(Alive(map.CloseUp.ShipImage), $"session {session}: close-up image is alive");
                Assert.IsFalse(ArtLibrary.IsStandIn(map.CloseUp.ShipImage));
                map.CloseCloseUp();

                var standIn = ArtLibrary.ShipMapIcon("tug");
                Assert.IsTrue(ArtLibrary.IsStandIn(standIn));
                Assert.IsTrue(Alive(standIn), $"session {session}: a stand-in is usable");
                if (session == 1) firstStandIn = standIn;
                else Assert.AreNotSame(firstStandIn, standIn, "the second session made its own stand-in");

                sessions.Add($"session {session}: {sim.Ships.Count} ships, tick {sim.CurrentTick}, {map.Routes.Segments.Count} legs drawn, close-up opened and closed");
                LogAssert.NoUnexpectedReceived();
                yield return new ExitPlayMode();
                Assert.IsFalse(EditorApplication.isPlaying);
            }
            TestContext.WriteLine(string.Join("\n", sessions));
        }

        /// <summary>
        /// Session 13: the theme, the sound player, and the bar keep nothing from the Play session before. The first
        /// session uses a changed theme and plays sounds; the second must start on the built-in theme with no sound player
        /// and fresh counts.
        /// </summary>
        [UnityTest]
        public IEnumerator TheThemeTheSoundPlayerAndTheBar_StartFresh_InASecondPlaySession()
        {
            var sessions = new List<string>();
            float defaultBarHeight = new SpaceTrade.Game.Theming.GameTheme().TopBar.Height;
            for (int session = 1; session <= 2; session++)
            {
                yield return new EnterPlayMode();
                Assert.IsTrue(EditorApplication.isPlaying);
                if (!_listenerVolume.HasValue) _listenerVolume = AudioListener.volume;
                AudioListener.volume = 0f; // the run stays silent at the speakers; the game's own volumes are untouched

                Assert.IsNull(SpaceTrade.Game.Audio.GameAudio.Current, $"session {session}: no sound player is left over");
                Assert.AreEqual(defaultBarHeight, SpaceTrade.Game.Theming.Theme.TopBar.Height, $"session {session}: the theme starts as the built-in one");
                CollectionAssert.IsEmpty(SpaceTrade.Game.Theming.Theme.Errors);
                if (session == 1)
                {
                    var changed = new SpaceTrade.Game.Theming.GameTheme();
                    changed.TopBar.Height = defaultBarHeight + 21;
                    SpaceTrade.Game.Theming.Theme.Use(changed);
                }
                float expectedHeight = session == 1 ? defaultBarHeight + 21 : defaultBarHeight;

                var cameraObject = new GameObject("Session Camera");
                var cam = cameraObject.AddComponent<Camera>();
                cam.orthographic = true;
                cam.transform.position = new Vector3(0, 0, -10);
                var settings = new SpaceTrade.Game.Settings.GameSettings { MasterVolume = 100, EffectsVolume = 100, MusicVolume = 100 };
                var audio = SpaceTrade.Game.Audio.GameAudio.Create(null, settings);
                var sim = NewFixtureGame();
                var starter = sim.Ships[0];
                int to = sim.Market.Galaxy.Systems.Where(s => s.Id != starter.DockedSystemId).OrderBy(s => sim.Market.Galaxy.DistanceMilliLy(starter.DockedSystemId.Value, s.Id)).First().Id;
                Assert.IsTrue(sim.Play().Succeeded, "a new game starts paused; this one runs");
                var runnerObject = new GameObject("Session Runner");
                var runner = runnerObject.AddComponent<SimulationRunner>();
                runner.Run(sim);
                var map = MapScreen.Create(cam);
                map.Runner = runner;
                map.Show(sim, new string[0], null);
                for (int i = 0; i < 3; i++) yield return null;

                Assert.AreEqual(expectedHeight, map.Bar.BarRect.height, 0.5f, $"session {session}: the bar is built from this session's theme");
                Assert.AreEqual(sim.CurrentDate.ToString(), map.Bar.DateText, $"session {session}: the bar shows the date");
                Assert.AreEqual(SpaceTrade.Game.Audio.SoundId.MusicMap, audio.Music, $"session {session}: the map music loop is on");
                Assert.IsNotNull(audio.ClipOf(SpaceTrade.Game.Audio.SoundId.Arrival), $"session {session}: the arrival clip is loaded");
                Assert.AreEqual(0, audio.StartedTotal, $"session {session}: no sound counts carried over");

                Assert.IsTrue(runner.Command(s => s.Dispatch(new VoyagePlan(starter.Id, to), true)).Succeeded);
                for (int i = 0; i < 2; i++) yield return null;
                map.Bar.TogglePause();
                for (int i = 0; i < 2; i++) yield return null;
                Assert.IsTrue(sim.Clock.IsPaused);
                Assert.IsTrue(map.Bar.ShowsPaused);
                Assert.AreEqual(1, audio.StartedCount(SpaceTrade.Game.Audio.SoundId.Dispatch), $"session {session}: one dispatch sound");
                Assert.AreEqual(1, audio.StartedCount(SpaceTrade.Game.Audio.SoundId.Pause), $"session {session}: one pause sound");
                Assert.AreEqual(2, audio.StartedTotal, $"session {session}: and no others");
                map.Bar.ToggleNotifications();
                for (int i = 0; i < 2; i++) yield return null;
                Assert.IsTrue(map.Bar.Notifications.IsOpen);

                sessions.Add($"session {session}: bar height {map.Bar.BarRect.height}, date {map.Bar.DateText}, {audio.StartedTotal} sounds started, music {audio.Music}");
                LogAssert.NoUnexpectedReceived();
                yield return new ExitPlayMode();
                Assert.IsFalse(EditorApplication.isPlaying);
            }
            TestContext.WriteLine(string.Join("\n", sessions));
        }
    }
}
