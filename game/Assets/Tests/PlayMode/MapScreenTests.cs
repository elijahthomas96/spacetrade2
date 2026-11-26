using System.Collections;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Game.Art;
using SpaceTrade.Game.Map;
using SpaceTrade.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace SpaceTrade.Game.Tests
{
    /// <summary>The galaxy map at 12 and 50 systems, driven by simulated mouse and keyboard input through the Input System.</summary>
    public class MapScreenTests
    {
        private GameObject _cameraObject;
        private MapScreen _map;
        private Simulation _sim;
        private Mouse _mouse;
        private Keyboard _keyboard;
        private bool _settingsChanged;
        private InputSettings.BackgroundBehavior _oldBackground;
        private InputSettings.EditorInputBehaviorInPlayMode _oldEditorBehavior;

        private IEnumerator Open(int systems)
        {
            ArtLibrary.Load(Resources.Load<ArtCatalog>(ArtCatalog.ResourceName));
            _cameraObject = new GameObject("Test Camera");
            var cam = _cameraObject.AddComponent<Camera>();
            cam.orthographic = true;
            cam.transform.position = new Vector3(0, 0, -10);
            _map = MapScreen.Create(cam);
            _sim = GameTestTuning.NewGameWithSystems(systems);
            _map.Show(_sim, new string[0], null);

            // Changed in place: assigning a new InputSystem.settings makes the Input System destroy its default settings object.
            var settings = InputSystem.settings;
            _oldBackground = settings.backgroundBehavior;
            _oldEditorBehavior = settings.editorInputBehaviorInPlayMode;
            _settingsChanged = true;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _mouse = InputSystem.AddDevice<Mouse>("Test Mouse");
            _keyboard = InputSystem.AddDevice<Keyboard>("Test Keyboard");
            _map.Input.MouseDevice = _mouse;
            _map.Input.KeyboardDevice = _keyboard;
            yield return null;
            yield return null;
        }

        [TearDown]
        public void Close()
        {
            if (_mouse != null) InputSystem.RemoveDevice(_mouse);
            if (_keyboard != null) InputSystem.RemoveDevice(_keyboard);
            if (_settingsChanged)
            {
                InputSystem.settings.backgroundBehavior = _oldBackground;
                InputSystem.settings.editorInputBehaviorInPlayMode = _oldEditorBehavior;
            }
            if (_map != null) Object.Destroy(_map.gameObject);
            if (_cameraObject != null) Object.Destroy(_cameraObject);
            _mouse = null; _keyboard = null; _settingsChanged = false; _map = null; _cameraObject = null;
        }

        private Vector2 ScreenOf(int systemId) =>
            _map.Camera.Camera.WorldToScreenPoint(GalaxyMapView.WorldPosition(_sim.Market.Galaxy.Systems[systemId]));

        private IEnumerator MouseTo(Vector2 position, bool left = false, bool right = false, float scroll = 0f)
        {
            var state = new MouseState { position = position, scroll = new Vector2(0f, scroll) };
            if (left) state = state.WithButton(MouseButton.Left);
            if (right) state = state.WithButton(MouseButton.Right);
            InputSystem.QueueStateEvent(_mouse, state);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EverySystem_HasAStarAndAName_AndAllAreOnScreenAtTheWidestZoom([Values(12, 50)] int systems)
        {
            yield return Open(systems);
            Assert.AreEqual(systems, _sim.Market.Galaxy.Systems.Count);
            Assert.AreEqual(systems, _map.View.MarkerCount);
            Assert.AreEqual(systems, _map.Hud.LabelCount);
            Assert.AreEqual(_map.Camera.MaxSize, _map.Camera.Camera.orthographicSize, 1e-4f, "opens at the widest zoom");
            foreach (var s in _sim.Market.Galaxy.Systems)
            {
                Vector3 v = _map.Camera.Camera.WorldToViewportPoint(GalaxyMapView.WorldPosition(s));
                Assert.That(v.x, Is.InRange(0f, 1f), s.Name + " x on screen");
                Assert.That(v.y, Is.InRange(0f, 1f), s.Name + " y on screen");
            }
            Assert.Greater(_map.Hud.VisibleLabelCount, 0);
            TestContext.WriteLine($"{systems} systems: galaxy {_map.View.WorldBounds.width:0.0} x {_map.View.WorldBounds.height:0.0} ly, " +
                                  $"zoom {_map.Camera.MinSize}..{_map.Camera.MaxSize:0.00} ly half-height, " +
                                  $"{_map.Hud.VisibleLabelCount} of {systems} names shown at the widest zoom in a {Screen.width}x{Screen.height} view");
        }

        [UnityTest]
        public IEnumerator ShownNames_NeverOverlapAStarANameOrThePanel_OrLeaveTheScreen([Values(12, 50)] int systems)
        {
            yield return Open(systems);
            var galaxy = _sim.Market.Galaxy;
            Vector2 mid = _map.View.WorldBounds.center;
            int selected = galaxy.Systems.OrderBy(s => (GalaxyMapView.WorldPosition(s) - mid).sqrMagnitude).First().Id;
            _map.Select(selected);
            Vector2 centre = ScreenOf(selected);
            for (int step = 0; step < 4; step++)
            {
                yield return null;
                yield return null;
                var hud = _map.Hud;
                var shown = hud.ShownLabels;
                Assert.IsTrue(hud.InfoPanelVisible);
                Assert.Greater(shown.Count, 0);
                Assert.IsTrue(shown.Any(l => l.SystemId == selected), "the selected system keeps its name");
                foreach (var (id, rect) in shown)
                {
                    if (id == selected) continue;
                    for (int s = 0; s < hud.StarRects.Count; s++)
                        if (s != id) Assert.IsFalse(rect.Overlaps(hud.StarRects[s]), $"name of system {id} covers star {s}");
                    foreach (var (other, otherRect) in shown)
                        if (other != id) Assert.IsFalse(rect.Overlaps(otherRect), $"names of systems {id} and {other} overlap");
                    Assert.IsFalse(rect.Overlaps(hud.InfoPanelRect), $"name of system {id} is under the info panel");
                    Assert.IsTrue(rect.xMin >= 0 && rect.yMin >= 0 && rect.xMax <= hud.PanelSize.x && rect.yMax <= hud.PanelSize.y, $"name of system {id} runs off the screen");
                }
                TestContext.WriteLine($"{systems} systems, half-height {_map.Camera.Camera.orthographicSize:0.00} ly: {shown.Count} of {systems} names shown, none overlapping");
                _map.Camera.ZoomAt(0.5f, centre);
            }
        }

        [UnityTest]
        public IEnumerator Zoom_StopsAtItsLimits_AndPan_StopsPastTheEdge([Values(12, 50)] int systems)
        {
            yield return Open(systems);
            var cam = _map.Camera;
            var centre = new Vector2(Screen.width / 2f, Screen.height / 2f);
            for (int i = 0; i < 60; i++) cam.ZoomAt(0.5f, centre);
            Assert.AreEqual(cam.MinSize, cam.Camera.orthographicSize, 1e-4f);
            for (int i = 0; i < 60; i++) cam.ZoomAt(2f, centre);
            Assert.AreEqual(cam.MaxSize, cam.Camera.orthographicSize, 1e-4f);

            var b = _map.View.WorldBounds;
            cam.PanWorld(new Vector2(10_000f, 10_000f));
            Assert.AreEqual(b.xMax + MapCamera.PanMarginLy, cam.Centre.x, 1e-3f);
            Assert.AreEqual(b.yMax + MapCamera.PanMarginLy, cam.Centre.y, 1e-3f);
            cam.PanWorld(new Vector2(-20_000f, -20_000f));
            Assert.AreEqual(b.xMin - MapCamera.PanMarginLy, cam.Centre.x, 1e-3f);
            Assert.AreEqual(b.yMin - MapCamera.PanMarginLy, cam.Centre.y, 1e-3f);
        }

        [UnityTest]
        public IEnumerator Mouse_SelectsASystem_ShowsItsPanel_AndClicksOnEmptySpaceAndEscClearIt([Values(12, 50)] int systems)
        {
            yield return Open(systems);
            var galaxy = _sim.Market.Galaxy;
            int target = galaxy.Systems.Select(s => s.Id).First(id => !_map.Hud.IsOverPanel(ScreenOf(id)));
            Vector2 p = ScreenOf(target);
            yield return MouseTo(p);
            Assert.AreEqual(target, _map.Hud.HoveredSystemId, "hovering a star");
            yield return MouseTo(p, left: true);
            yield return MouseTo(p);
            Assert.AreEqual(target, _map.SelectedSystemId);
            Assert.IsTrue(_map.Hud.InfoPanelVisible);
            Assert.AreEqual(galaxy.Systems[target].Name, _map.Hud.InfoTitle);

            yield return null;
            Vector2 empty = new Vector2(2f, 2f);
            Assume.That(_map.View.PickSystem(empty, MapInput.PickPixels), Is.EqualTo(-1), "the corner is empty");
            yield return MouseTo(empty, left: true);
            yield return MouseTo(empty);
            Assert.AreEqual(-1, _map.SelectedSystemId, "click on empty space clears");
            Assert.IsFalse(_map.Hud.InfoPanelVisible);

            yield return MouseTo(p, left: true);
            yield return MouseTo(p);
            Assert.AreEqual(target, _map.SelectedSystemId);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Escape));
            yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            Assert.AreEqual(-1, _map.SelectedSystemId, "Esc clears");
        }

        [UnityTest]
        public IEnumerator MouseWheel_Zooms_RightDrag_Pans_AndWasd_Pans([Values(12, 50)] int systems)
        {
            yield return Open(systems);
            var cam = _map.Camera;
            var centre = new Vector2(Screen.width / 2f, Screen.height / 2f);
            yield return MouseTo(centre);

            float before = cam.Camera.orthographicSize;
            yield return MouseTo(centre, scroll: 120f);
            yield return MouseTo(centre);
            float ratio = cam.Camera.orthographicSize / before;
            Assert.That(ratio, Is.InRange(0.84f, 0.86f), "one wheel notch zooms in by 0.85");

            Vector2 start = cam.Centre;
            float upp = cam.UnitsPerPixel;
            yield return MouseTo(centre, right: true);
            yield return MouseTo(centre + new Vector2(40f, 0f), right: true);
            yield return MouseTo(centre + new Vector2(40f, 0f));
            Assert.AreEqual(start.x - 40f * upp, cam.Centre.x, 1e-3f, "right drag moves the map with the cursor");
            Assert.AreEqual(start.y, cam.Centre.y, 1e-3f);

            Vector2 beforeKeys = cam.Centre;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.D, Key.W));
            for (int i = 0; i < 5; i++) yield return null;
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            yield return null;
            Assert.Greater(cam.Centre.x, beforeKeys.x, "D pans right");
            Assert.Greater(cam.Centre.y, beforeKeys.y, "W pans up");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
