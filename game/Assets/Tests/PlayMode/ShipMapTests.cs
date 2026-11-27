using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Game.Art;
using SpaceTrade.Game.Boot;
using SpaceTrade.Game.Map;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Routes;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Voyages;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace SpaceTrade.Game.Tests
{
    /// <summary>Ships on the galaxy map (spec 9.1, session 12), at 12 and 50 systems, on a fake real-time clock.</summary>
    public class ShipMapTests
    {
        private MapTestRig _rig;

        [SetUp]
        public void SetUp() => _rig = new MapTestRig();

        [TearDown]
        public void TearDown() => _rig.Close();

        private MapScreen Map => _rig.Map;
        private Simulation Sim => _rig.Sim;

        private Vector2 Star(int systemId) => GalaxyMapView.WorldPosition(Sim.Market.Galaxy.Systems[systemId]);

        private static void AreExactly(Vector2 expected, Vector2 actual, string what)
        {
            Assert.AreEqual(expected.x, actual.x, 0f, what + " x");
            Assert.AreEqual(expected.y, actual.y, 0f, what + " y");
        }

        private float PixelsBetween(Vector2 a, Vector2 b) => (_rig.ScreenOf(a) - _rig.ScreenOf(b)).magnitude;

        [UnityTest]
        public IEnumerator StarterShip_IsSelectedAtStart_DockedBesideItsStar_WithItsOwnIconFile([Values(12, 50)] int systems)
        {
            yield return _rig.Open(GameTestTuning.NewGameWithSystems(systems));
            var ship = Sim.Ships[0];
            Assert.AreEqual(ship.Id, Map.SelectedShipId, "the starter ship is selected at start");
            Assert.AreEqual(ship.Name, Map.Hud.InfoTitle);
            Assert.AreEqual(Sim.Ships.Count, Map.Ships.MarkerCount);
            AreExactly(Star(ship.DockedSystemId.Value), Map.Ships.AnchorOf(ship.Id), "a docked ship's position is its star");
            Assert.IsTrue(Map.Ships.TryGetDrawn(ship.Id, out var drawn));
            Assert.AreEqual(ShipMapView.FanRadiusPixels, PixelsBetween(drawn, Star(ship.DockedSystemId.Value)), 0.5f, "drawn beside the star, not on it");
            var icon = Map.Ships.IconOf(ship.Id);
            Assert.AreSame(ArtLibrary.ShipMapIcon(ship.ClassId), icon);
            Assert.IsTrue(ArtLibrary.HasFile(ArtLibrary.ShipMapIconKey(ship.ClassId)), "the class has its own map icon file");
            Assert.IsFalse(ArtLibrary.IsStandIn(icon));
            Assert.IsTrue(Map.Hud.InfoLines.Any(l => l.StartsWith("Docked at ")), string.Join(" | ", Map.Hud.InfoLines));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Position_IsTheSimulations_AtDepartureMidTripAndArrival_AndHoldsStillWhilePaused([Values(12, 50)] int systems)
        {
            yield return _rig.Open(GameTestTuning.NewGameWithSystems(systems));
            int id = Sim.Ships[0].Id;
            var v = MapTestRig.SendEmpty(Sim, id);
            long transit = v.TransitTicks, arrival = v.ArrivalTick;
            Vector2 origin = Star(v.OriginId), destination = Star(v.DestinationId);
            yield return MapTestRig.Frames();

            AreExactly(origin, Map.Ships.AnchorOf(id), "at departure");
            Assert.IsTrue(Map.Ships.TryGetDrawn(id, out var drawn));
            AreExactly(origin, drawn, "a ship in flight is drawn exactly at its position");
            Vector2 d = destination - origin;
            Assert.AreEqual(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, Mathf.DeltaAngle(0f, Map.Ships.RootOf(id).eulerAngles.z), 0.01f, "points along its leg");

            Assert.IsTrue(_rig.Runner.Driver.Play().Succeeded);
            yield return _rig.RealMs(transit / 2);
            Assert.AreEqual(v.DepartureTick + transit / 2, Sim.CurrentTick, "1x: one tick per real millisecond");
            var mid = Sim.PositionOf(id);
            AreExactly(ShipMapView.WorldPosition(mid), Map.Ships.AnchorOf(id), "mid-trip: the simulation's position");
            Vector2 halfway = origin + d * ((float)(transit / 2) / transit);
            Assert.Less((Map.Ships.AnchorOf(id) - halfway).magnitude, 0.0015f, "on the straight line, in proportion to time flown");
            Assert.That(mid.ProgressHundredthsOfPercent, Is.InRange(4999, 5000));

            Assert.IsTrue(_rig.Runner.Driver.Pause().Succeeded);
            Vector2 pausedAt = Map.Ships.AnchorOf(id);
            long pausedTick = Sim.CurrentTick;
            yield return _rig.RealMs(60_000);
            yield return MapTestRig.Frames(10);
            Assert.AreEqual(pausedTick, Sim.CurrentTick, "paused: no game time passes");
            AreExactly(pausedAt, Map.Ships.AnchorOf(id), "paused: the ship holds still");

            Assert.IsTrue(_rig.Runner.Driver.Play().Succeeded);
            yield return _rig.RealMs(arrival - 1 - Sim.CurrentTick);
            Assert.AreEqual(arrival - 1, Sim.CurrentTick);
            Assert.IsTrue(Sim.FindShip(id).IsInTransit, "still flying one tick before arrival");
            Assert.Less(Sim.PositionOf(id).ProgressHundredthsOfPercent, ShipPosition.FullProgress);

            yield return _rig.RealMs(1);
            Assert.AreEqual(arrival, Sim.CurrentTick);
            Assert.AreEqual(v.DestinationId, Sim.FindShip(id).DockedSystemId, "docked at the arrival tick");
            AreExactly(destination, Map.Ships.AnchorOf(id), "at arrival: its destination star");
            Assert.IsTrue(Map.Ships.TryGetDrawn(id, out drawn));
            Assert.AreEqual(ShipMapView.FanRadiusPixels, PixelsBetween(drawn, destination), 0.5f, "now drawn beside the star");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Speed_3xAnd5x_MoveTheShipFasterOnScreen_WithoutChangingItsArrival([Values(12, 50)] int systems)
        {
            yield return _rig.Open(GameTestTuning.NewGameWithSystems(systems));
            int id = Sim.Ships[0].Id;
            var v = MapTestRig.SendEmpty(Sim, id);
            long arrival = v.ArrivalTick;
            long window = v.TransitTicks / 20;
            var driver = _rig.Runner.Driver;
            Assert.IsTrue(driver.Play().Succeeded);

            var moved = new Dictionary<ClockSpeed, float>();
            foreach (var speed in new[] { ClockSpeed.X1, ClockSpeed.X3, ClockSpeed.X5 })
            {
                Assert.IsTrue(driver.SetSpeed(speed).Succeeded);
                long tickBefore = Sim.CurrentTick;
                Vector2 before = Map.Ships.AnchorOf(id);
                yield return _rig.RealMs(window);
                Assert.AreEqual(window * ClockSpeeds.Multiplier(speed), Sim.CurrentTick - tickBefore, $"{speed}: game ticks per real millisecond");
                moved[speed] = (Map.Ships.AnchorOf(id) - before).magnitude;
                Assert.AreEqual(arrival, Sim.FindShip(id).Voyage.ArrivalTick, $"{speed}: the arrival tick never changes");
            }
            TestContext.WriteLine($"{systems} systems: in {window} real ms the ship moved {moved[ClockSpeed.X1]:0.000} ly at 1x, {moved[ClockSpeed.X3]:0.000} at 3x, {moved[ClockSpeed.X5]:0.000} at 5x");
            Assert.That(moved[ClockSpeed.X3] / moved[ClockSpeed.X1], Is.InRange(2.95f, 3.05f));
            Assert.That(moved[ClockSpeed.X5] / moved[ClockSpeed.X1], Is.InRange(4.95f, 5.05f));

            long left = arrival - Sim.CurrentTick;
            yield return _rig.RealMs((left - 1) / 5);
            Assert.Less(Sim.CurrentTick, arrival);
            Assert.IsTrue(Sim.FindShip(id).IsInTransit);
            yield return _rig.RealMs(1);
            Assert.GreaterOrEqual(Sim.CurrentTick, arrival);
            Assert.AreEqual(v.DestinationId, Sim.FindShip(id).DockedSystemId, "docks at the same arrival tick at 5x");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Follow_KeepsTheShipCentred_ThroughSpeedChanges_AndEndsWhenPannedByHand([Values(12, 50)] int systems)
        {
            yield return _rig.Open(GameTestTuning.NewGameWithSystems(systems));
            int id = Sim.Ships[0].Id;
            var v = MapTestRig.SendEmpty(Sim, id);
            long window = v.TransitTicks / 40;
            var driver = _rig.Runner.Driver;
            var cam = Map.Camera;
            Vector2 middle = new Vector2(_rig.Camera.pixelWidth / 2f, _rig.Camera.pixelHeight / 2f);
            yield return _rig.MouseTo(middle);

            yield return _rig.Keys(1, Key.F);
            Assert.IsTrue(Map.IsFollowing, "F follows the selected ship");
            Assert.IsTrue(driver.Play().Succeeded);
            foreach (var speed in new[] { ClockSpeed.X1, ClockSpeed.X3, ClockSpeed.X5, ClockSpeed.X1 })
            {
                Assert.IsTrue(driver.SetSpeed(speed).Succeeded);
                Vector2 before = Map.Ships.AnchorOf(id);
                yield return _rig.RealMs(window);
                Assert.Greater((Map.Ships.AnchorOf(id) - before).magnitude, 0f, $"{speed}: the ship moved");
                Assert.IsTrue(Map.IsFollowing);
                AreExactly(Map.Ships.AnchorOf(id), cam.Centre, $"{speed}: the view stays centred on the ship");
            }

            yield return _rig.MouseTo(middle, scroll: 120f);
            yield return _rig.MouseTo(middle);
            yield return _rig.RealMs(window);
            Assert.IsTrue(Map.IsFollowing, "zooming does not end follow");
            AreExactly(Map.Ships.AnchorOf(id), cam.Centre, "zoomed: still centred");

            yield return _rig.MouseTo(middle, right: true);
            yield return _rig.MouseTo(middle + new Vector2(40f, 0f), right: true);
            yield return _rig.MouseTo(middle + new Vector2(40f, 0f));
            Assert.IsFalse(Map.IsFollowing, "dragging the map by hand ends follow");
            Vector2 left = cam.Centre;
            yield return _rig.RealMs(window);
            AreExactly(left, cam.Centre, "the view stays where it was put");
            Assert.AreNotEqual(Map.Ships.AnchorOf(id), cam.Centre);

            yield return _rig.Keys(1, Key.F);
            yield return _rig.RealMs(window);
            Assert.IsTrue(Map.IsFollowing);
            AreExactly(Map.Ships.AnchorOf(id), cam.Centre, "following again");
            yield return _rig.Keys(3, Key.D);
            Assert.IsFalse(Map.IsFollowing, "panning with the keys ends follow");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Clicking_AShipSelectsIt_ClickingItsStarSelectsTheStar_AndEmptySpaceClears([Values(12, 50)] int systems)
        {
            yield return _rig.Open(GameTestTuning.NewGameWithSystems(systems));
            var ship = Sim.Ships[0];
            int star = ship.DockedSystemId.Value;
            Map.Camera.SetCentre(Star(star));
            yield return MapTestRig.Frames();
            Assert.IsTrue(Map.Ships.TryGetDrawn(ship.Id, out var drawn));

            yield return _rig.Click(_rig.ScreenOf(Star(star)));
            Assert.AreEqual(star, Map.SelectedSystemId, "clicking the star selects the system");
            Assert.AreEqual(-1, Map.SelectedShipId);
            Assert.AreEqual(Sim.Market.Galaxy.Systems[star].Name, Map.Hud.InfoTitle);
            CollectionAssert.Contains(Map.Hud.InfoLines, "Docked here (1)");
            CollectionAssert.Contains(Map.Hud.InfoLines, $"{ship.Name} · {ShipText.ClassName(Sim, ship)}");

            yield return _rig.Click(_rig.ScreenOf(drawn));
            Assert.AreEqual(ship.Id, Map.SelectedShipId, "clicking the icon beside the star selects the ship");
            Assert.AreEqual(-1, Map.SelectedSystemId);
            Assert.AreEqual(ship.Name, Map.Hud.InfoTitle);

            yield return _rig.Click(new Vector2(2f, 2f));
            Assert.AreEqual(-1, Map.SelectedShipId, "a click on empty space clears");
            Assert.IsFalse(Map.Hud.InfoPanelVisible);

            var v = MapTestRig.SendEmpty(Sim, ship.Id);
            Assert.IsTrue(_rig.Runner.Driver.Play().Succeeded);
            yield return _rig.RealMs(v.TransitTicks / 2);
            Assert.IsTrue(_rig.Runner.Driver.Pause().Succeeded);
            Map.Camera.SetCentre(Map.Ships.AnchorOf(ship.Id));
            yield return MapTestRig.Frames();
            yield return _rig.Click(_rig.ScreenOf(Map.Ships.AnchorOf(ship.Id)));
            Assert.AreEqual(ship.Id, Map.SelectedShipId, "clicking a ship in flight selects it");
            Assert.IsTrue(Map.Hud.InfoLines.Any(l => l.StartsWith("In transit from ")), string.Join(" | ", Map.Hud.InfoLines));
            Assert.IsTrue(Map.Hud.InfoLines.Any(l => l.EndsWith(" · Paused")), "the arrival line says Paused while paused");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator SevenDocked_ShowFiveIconsAndPlusTwo_AndTheSystemPanelListsAllSeven([Values(12, 50)] int systems)
        {
            var sim = GameTestTuning.NewGameWithSystems(systems, 1, DebugFleet.PrepareTuning);
            int yard = sim.Market.Shipyards[0].SystemId;
            var classes = sim.Market.Tuning.Ships.Select(c => c.Id).ToList();
            for (int i = 0; sim.Ships.Count(s => s.DockedSystemId == yard) < 7; i++)
                Assert.IsTrue(sim.BuyShip(yard, classes[i % classes.Count], out _).Succeeded);
            var docked = sim.Ships.Where(s => s.DockedSystemId == yard).ToList();
            Assert.AreEqual(7, docked.Count);
            yield return _rig.Open(sim);
            Map.Select(yard);
            yield return MapTestRig.Frames();

            Assert.AreEqual(ShipMapView.MaxFannedPerStar, Map.Ships.DrawnDockedAt(yard), "at most five icons beside a star");
            CollectionAssert.Contains(Map.Ships.Overflow, (yard, 2));
            Assert.IsTrue(Map.Hud.DockedCounts.Any(c => c.SystemId == yard && c.Text == "+2"), "the rest are shown as a count");
            CollectionAssert.Contains(Map.Hud.InfoLines, "Docked here (7)");
            foreach (var s in docked) CollectionAssert.Contains(Map.Hud.InfoLines, $"{s.Name} · {ShipText.ClassName(Sim, s)}", "the panel lists every docked ship");

            var hidden = docked.First(s => !Map.Ships.IsDrawn(s.Id));
            Map.SelectShip(hidden.Id);
            yield return MapTestRig.Frames();
            Assert.IsTrue(Map.Ships.IsDrawn(hidden.Id), "a selected ship is always drawn");
            Assert.AreEqual(ShipMapView.MaxFannedPerStar, Map.Ships.DrawnDockedAt(yard));
            CollectionAssert.Contains(Map.Ships.Overflow, (yard, 2));

            var drawnAt = docked.Where(s => Map.Ships.IsDrawn(s.Id)).Select(s => { Map.Ships.TryGetDrawn(s.Id, out var p); return _rig.ScreenOf(p); }).ToList();
            for (int i = 0; i < drawnAt.Count; i++)
            for (int j = i + 1; j < drawnAt.Count; j++)
                Assert.Greater((drawnAt[i] - drawnAt[j]).magnitude, ShipMapView.DockedIconPixels, "fanned icons do not sit on each other");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator RouteLines_ShowCurrentLegsAndActiveRoutes_HidePausedOnes_UnlessTheShipIsSelected([Values(12, 50)] int systems)
        {
            var sim = GameTestTuning.NewGameWithSystems(systems, 1, DebugFleet.PrepareTuning);
            var galaxy = sim.Market.Galaxy;
            int yard = sim.Market.Shipyards[0].SystemId;
            var near = galaxy.Systems.Where(s => s.Id != yard).OrderBy(s => galaxy.DistanceMilliLy(yard, s.Id)).Select(s => s.Id).ToList();
            string courier = sim.Market.Tuning.Start.StarterShipClass;
            Assert.IsTrue(sim.BuyShip(yard, courier, out int active).Succeeded);
            Assert.IsTrue(sim.BuyShip(yard, courier, out int paused).Succeeded);
            Assert.IsTrue(sim.SetRoute(active, new[] { RouteStop.Empty(yard), RouteStop.Empty(near[0]) }).Succeeded);
            Assert.IsTrue(sim.SetRoute(paused, new[] { RouteStop.Empty(yard), RouteStop.Empty(near[1]) }).Succeeded);
            Assert.IsTrue(sim.PauseRoute(paused).Succeeded);
            Assert.IsTrue(sim.FindShip(active).IsInTransit && sim.FindShip(paused).IsInTransit, "both routes departed at once");
            yield return _rig.Open(sim);
            Map.Select(-1);
            yield return MapTestRig.Frames();

            var segments = Map.Routes.Segments;
            CollectionAssert.Contains(segments, (yard, near[0], RouteLineStyle.CurrentLeg));
            CollectionAssert.Contains(segments, (near[0], yard, RouteLineStyle.Route), "an active route's whole loop is drawn");
            CollectionAssert.Contains(segments, (yard, near[1], RouteLineStyle.CurrentLeg), "a paused route's ship still flies its leg");
            Assert.IsFalse(segments.Any(s => s.From == near[1] && s.To == yard), "an unselected paused route is not drawn");

            Map.SelectShip(paused);
            yield return MapTestRig.Frames();
            segments = Map.Routes.Segments;
            CollectionAssert.Contains(segments, (yard, near[1], RouteLineStyle.Selected));
            CollectionAssert.Contains(segments, (near[1], yard, RouteLineStyle.PausedRoute), "the selected ship's paused route is drawn dimmed");
            Assert.Less(RouteLinesView.Alpha(RouteLineStyle.PausedRoute), RouteLinesView.Alpha(RouteLineStyle.Route));

            Assert.IsTrue(_rig.Runner.Driver.Play().Succeeded);
            yield return _rig.RealMs(sim.FindShip(paused).Voyage.ArrivalTick - sim.CurrentTick);
            Assert.IsFalse(sim.FindShip(paused).IsInTransit, "a paused route's ship stays docked after its leg");
            Assert.IsTrue(_rig.Runner.Driver.Pause().Succeeded);
            yield return MapTestRig.Frames();
            segments = Map.Routes.Segments;
            CollectionAssert.Contains(segments, (yard, near[1], RouteLineStyle.PausedRoute), "docked and paused: still drawn, dimmed, while selected");
            CollectionAssert.Contains(segments, (near[1], yard, RouteLineStyle.PausedRoute));

            Map.Select(-1);
            yield return MapTestRig.Frames();
            Assert.IsFalse(Map.Routes.Segments.Any(s => (s.From == near[1] && s.To == yard) || (s.From == yard && s.To == near[1])), "unselected again: hidden");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
