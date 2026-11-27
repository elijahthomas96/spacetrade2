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
    }
}
