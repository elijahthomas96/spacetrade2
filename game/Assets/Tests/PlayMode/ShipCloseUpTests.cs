using System.Collections;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Game.Art;
using SpaceTrade.Game.Map;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Ships;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace SpaceTrade.Game.Tests
{
    /// <summary>The ship close-up (spec 9.1): how it opens and closes, and its live values read from the simulation.</summary>
    public class ShipCloseUpTests
    {
        private MapTestRig _rig;

        [SetUp]
        public void SetUp() => _rig = new MapTestRig();

        [TearDown]
        public void TearDown() => _rig.Close();

        private MapScreen Map => _rig.Map;
        private Simulation Sim => _rig.Sim;
        private ShipCloseUp CloseUp => Map.CloseUp;

        private Vector2 Middle => new Vector2(_rig.Camera.pixelWidth / 2f, _rig.Camera.pixelHeight / 2f);

        private IEnumerator Wheel(float scroll)
        {
            yield return _rig.MouseTo(Middle, scroll: scroll);
            yield return _rig.MouseTo(Middle);
            yield return null;
        }

        [UnityTest]
        public IEnumerator WheelIn_AtTheClosestZoom_Opens_AndWheelOutEscAndTheCloseButton_ReturnToTheMap([Values(12, 50)] int systems)
        {
            yield return _rig.Open(GameTestTuning.NewGameWithSystems(systems));
            var ship = Sim.Ships[0];
            yield return _rig.MouseTo(Middle);
            yield return Wheel(120f);
            Assert.IsFalse(Map.IsCloseUpOpen, "zooming in short of the closest zoom only zooms");

            var cam = Map.Camera;
            for (int i = 0; i < 60; i++) cam.ZoomAt(0.5f, Middle);
            Assert.AreEqual(cam.MinSize, cam.Camera.orthographicSize, 1e-4f);
            yield return Wheel(120f);
            Assert.IsTrue(Map.IsCloseUpOpen, "one more notch in at the closest zoom opens the close-up");
            Assert.AreEqual(ship.Id, CloseUp.ShipId);
            Assert.AreEqual(ship.Name, CloseUp.Title);
            Assert.AreSame(ArtLibrary.ShipCloseUp(ship.ClassId), CloseUp.ShipImage);
            Assert.IsTrue(ArtLibrary.HasFile(ArtLibrary.ShipCloseUpKey(ship.ClassId)), "the class has its own close-up file");
            Assert.IsFalse(ArtLibrary.IsStandIn(CloseUp.ShipImage));

            yield return Wheel(-120f);
            Assert.IsFalse(Map.IsCloseUpOpen, "wheel out returns to the map");
            Assert.AreEqual(ship.Id, Map.SelectedShipId, "the same ship is still selected");
            Assert.IsTrue(Map.IsFollowing, "and followed, at the closest zoom");
            Assert.AreEqual(cam.MinSize, cam.Camera.orthographicSize, 1e-4f);

            yield return Wheel(120f);
            Assert.IsTrue(Map.IsCloseUpOpen);
            yield return _rig.Keys(1, Key.Escape);
            Assert.IsFalse(Map.IsCloseUpOpen, "Esc returns to the map");
            Assert.AreEqual(ship.Id, Map.SelectedShipId, "Esc in the close-up only closes it");

            Map.OpenCloseUp();
            yield return MapTestRig.Frames();
            var button = CloseUp.GetComponent<UIDocument>().rootVisualElement.Q<Button>("close-up-close");
            Assert.IsNotNull(button);
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = button;
                button.SendEvent(e);
            }
            yield return MapTestRig.Frames();
            Assert.IsFalse(Map.IsCloseUpOpen, "the close button returns to the map");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Values_AreLive_SuppliesFallAsTheSimulationReports_AndPausedSaysSo([Values(12, 50)] int systems)
        {
            yield return _rig.Open(GameTestTuning.NewGameWithSystems(systems));
            var ship = Sim.Ships[0];
            var job = OpeningOffers.FindJobs(Sim, ship.Id).First(j => !j.IsPassengerJob);
            Assert.IsTrue(Sim.Dispatch(job.Quote.Plan, true).Succeeded);
            var v = ship.Voyage;
            Map.OpenCloseUp();
            yield return MapTestRig.Frames();

            Assert.AreEqual($"In transit from {Sim.Market.Galaxy.Systems[v.OriginId].Name} to {Sim.Market.Galaxy.Systems[v.DestinationId].Name}", CloseUp.Value("status"));
            Assert.IsTrue(CloseUp.IsShown("destination"));
            Assert.AreEqual(Sim.Market.Galaxy.Systems[v.DestinationId].Name, CloseUp.Value("destination"));
            Assert.AreEqual($"{Sim.Market.Tuning.FindShip(ship.ClassId).Crew} crew", CloseUp.Value("crew"));
            Assert.AreEqual(ShipText.Drive(Sim, ship), CloseUp.Value("drive"));
            string manifest = CloseUp.Value("manifest");
            var line = v.Freight[0];
            StringAssert.Contains($"{ShipText.CommodityName(Sim, line.CommodityId)}: {line.Tonnes} t at {Format.Credits(line.RatePerTonne)} per tonne", manifest);
            StringAssert.Contains($"Payout on arrival: {Format.Credits(v.Payout)} credits", manifest);
            StringAssert.EndsWith(" · Paused", CloseUp.Value("eta"), "a new game is paused");
            StringAssert.StartsWith("0.00%", CloseUp.Value("progress"));

            float backdrop = CloseUp.BackdropOffsetPixels;
            yield return _rig.RealMs(10_000);
            Assert.AreEqual(backdrop, CloseUp.BackdropOffsetPixels, "paused: the backdrop holds still");

            long fuelBefore = Sim.RemainingSupplyKg(ship.Id, SupplyKind.Fuel);
            long foodBefore = Sim.RemainingSupplyKg(ship.Id, SupplyKind.Food);
            Assert.AreEqual(ShipText.Kg(fuelBefore), CloseUp.Value("fuel"));
            Assert.IsTrue(_rig.Runner.Driver.Play().Succeeded);
            for (int step = 1; step <= 3; step++)
            {
                yield return _rig.RealMs(v.TransitTicks / 4);
                foreach (var kind in new[] { SupplyKind.Fuel, SupplyKind.Food, SupplyKind.Water })
                    Assert.AreEqual(ShipText.Kg(Sim.RemainingSupplyKg(ship.Id, kind)), CloseUp.Value(kind.ToString().ToLowerInvariant()), $"{kind} is the simulation's figure");
                StringAssert.Contains(" at 1x", CloseUp.Value("eta"));
                Assert.AreNotEqual(backdrop, CloseUp.BackdropOffsetPixels, "flying: the backdrop drifts");
                backdrop = CloseUp.BackdropOffsetPixels;
            }
            Assert.Less(Sim.RemainingSupplyKg(ship.Id, SupplyKind.Fuel), fuelBefore, "fuel falls in flight");
            Assert.Less(Sim.RemainingSupplyKg(ship.Id, SupplyKind.Food), foodBefore, "food falls in flight");
            long progress = Sim.PositionOf(ship.Id).ProgressHundredthsOfPercent;
            Assert.That(progress, Is.InRange(7499, 7500));
            StringAssert.StartsWith(ShipText.Percent(progress) + " · ", CloseUp.Value("progress"));
            TestContext.WriteLine($"{systems} systems, three quarters of the way: fuel {CloseUp.Value("fuel")}, food {CloseUp.Value("food")}, water {CloseUp.Value("water")}, ETA {CloseUp.Value("eta")}");

            Assert.IsTrue(_rig.Runner.Driver.Pause().Succeeded);
            yield return MapTestRig.Frames();
            StringAssert.EndsWith(" · Paused", CloseUp.Value("eta"));
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Progress_Is99Point99_OneTickBeforeArrival_And100OnlyOnceDocked([Values(12, 50)] int systems)
        {
            yield return _rig.Open(GameTestTuning.NewGameWithSystems(systems));
            var ship = Sim.Ships[0];
            var v = MapTestRig.SendEmpty(Sim, ship.Id);
            string destination = Sim.Market.Galaxy.Systems[v.DestinationId].Name;
            Map.OpenCloseUp();
            Assert.IsTrue(_rig.Runner.Driver.Play().Succeeded);
            Assert.IsTrue(_rig.Runner.Driver.SetSpeed(SpaceTrade.Sim.Timing.ClockSpeed.X1).Succeeded);
            for (int step = 1; step < 10; step++)
            {
                yield return _rig.RealMs(v.TransitTicks / 10);
                StringAssert.DoesNotStartWith("100.00%", CloseUp.Value("progress"), "never 100% in flight");
            }
            yield return _rig.RealMs(v.ArrivalTick - 1 - Sim.CurrentTick);
            Assert.AreEqual(v.ArrivalTick - 1, Sim.CurrentTick);
            StringAssert.StartsWith("99.99% · ", CloseUp.Value("progress"), "one tick before arrival");
            Assert.IsTrue(CloseUp.IsShown("destination"));

            yield return _rig.RealMs(1);
            Assert.AreEqual(v.DestinationId, ship.DockedSystemId);
            Assert.AreEqual($"100.00% (docked at {destination})", CloseUp.Value("progress"), "100% only when docked");
            Assert.AreEqual($"Docked at {destination}", CloseUp.Value("status"));
            Assert.IsFalse(CloseUp.IsShown("destination"));
            Assert.IsFalse(CloseUp.IsShown("eta"));
            Assert.AreEqual("Nothing aboard (docked)", CloseUp.Value("manifest"));
            float backdrop = CloseUp.BackdropOffsetPixels;
            yield return _rig.RealMs(5_000);
            Assert.AreEqual(backdrop, CloseUp.BackdropOffsetPixels, "docked: the backdrop holds still");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
