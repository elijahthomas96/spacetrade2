using System.Collections;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Game.Boot;
using SpaceTrade.Game.Hud;
using SpaceTrade.Game.Map;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Voyages;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace SpaceTrade.Game.Tests
{
    /// <summary>
    /// The notification list (spec 9.4): it shows the simulation's own notifications, newest first, and clicking one
    /// selects and centres its ship wherever that ship is now.
    /// </summary>
    public class NotificationListTests
    {
        private MapTestRig _rig;

        [SetUp]
        public void SetUp() => _rig = new MapTestRig();

        [TearDown]
        public void TearDown() => _rig.Close();

        private MapScreen Map => _rig.Map;
        private Simulation Sim => _rig.Sim;
        private TopBar Bar => Map.Bar;
        private NotificationList List => Bar.Notifications;

        /// <summary>A new game starts paused; these tests start with the clock running.</summary>
        private IEnumerator Open(Simulation sim)
        {
            Assert.IsTrue(sim.Play().Succeeded);
            yield return _rig.Open(sim);
        }

        private Vector2 CameraCentre => Map.Camera.Camera.transform.position;

        private void AssertCentredOnShip(int shipId, string what)
        {
            Assert.AreEqual(shipId, Map.SelectedShipId, what + ": the ship is selected");
            Assert.AreEqual(-1, Map.SelectedSystemId, what + ": no system is selected");
            var at = ShipMapView.WorldPosition(Sim.PositionOf(shipId));
            Assert.Less(Vector2.Distance(at, CameraCentre), 0.02f, $"{what}: the map is centred on where the simulation says the ship is now ({at}), camera at {CameraCentre}");
        }

        /// <summary>Moves the view somewhere else, so a later centring is really the click's doing.</summary>
        private IEnumerator LookElsewhere()
        {
            Map.Select(-1);
            Map.Camera.Camera.orthographicSize = Map.Camera.MinSize * 2f;
            var bounds = Map.View.WorldBounds;
            Map.Camera.SetCentre(new Vector2(bounds.xMin, bounds.yMin));
            yield return MapTestRig.Frames();
        }

        [UnityTest]
        public IEnumerator TheList_ShowsTheSimulationsNotifications_NewestFirst_WithDateIconAndText_AndCountsTheUnseen()
        {
            yield return Open(MapTestRig.FleetGame(12, 6));
            Assert.AreEqual(0, Sim.Notifications.Count);
            Assert.AreEqual("", Bar.BadgeText, "nothing new: no number on the bell");
            Bar.ToggleNotifications();
            yield return MapTestRig.Frames();
            Assert.IsTrue(List.IsOpen);
            Assert.AreEqual(0, List.Count);
            Assert.AreEqual(0, List.VisibleRowTexts().Count, "an empty list has no rows");
            Bar.ToggleNotifications();
            Assert.IsFalse(List.IsOpen);

            // Three ships arrive, one after another.
            var arrivals = Sim.Ships.Where(s => s.Voyage != null).OrderBy(s => s.Voyage.ArrivalTick).Take(3).ToList();
            long third = arrivals[2].Voyage.ArrivalTick;
            yield return _rig.RealMs(third - Sim.CurrentTick);
            int count = Sim.Notifications.Count;
            Assert.GreaterOrEqual(count, 3);
            Assert.AreEqual(count, Bar.UnseenCount, "every one is new");
            Assert.AreEqual(count.ToString(), Bar.BadgeText, "the bell shows how many");

            Bar.ToggleNotifications();
            yield return MapTestRig.Frames(3);
            Assert.AreEqual(count, List.Count);
            for (int i = 0; i < count; i++)
                Assert.AreSame(Sim.Notifications[count - 1 - i], List.Items[i], "row " + i + " is the simulation's own notification, newest first");
            var rows = List.VisibleRowTexts();
            Assert.Greater(rows.Count, 0);
            for (int i = 0; i < rows.Count; i++)
            {
                var n = List.Items[i];
                Assert.AreEqual(n.Date + " " + n.Text, rows[i], "row " + i + " shows the simulation's date and text");
                Assert.AreEqual(SpaceTrade.Sim.Timing.GameDate.FromTick(n.Tick).ToString(), n.Date.ToString());
            }
            foreach (var row in List.Panel.Query(className: "notification-row").ToList().Where(r => r.userData is Notification))
                Assert.IsNotNull(row.Q<Image>("kind").sprite, "each row has an icon for its kind");
            Assert.AreEqual(0, Bar.UnseenCount, "opening the list marks them seen");
            Assert.AreEqual("", Bar.BadgeText);
            TestContext.WriteLine(string.Join("\n", rows));

            Bar.ToggleNotifications();
            yield return MapTestRig.Frames();
            Assert.AreEqual("", Bar.BadgeText, "still nothing new after closing");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ClickingANotification_SelectsAndCentresItsShip_DockedOrInFlight_IncludingOneThatHasSinceMoved()
        {
            yield return Open(MapTestRig.FleetGame(12, 6));
            var first = Sim.Ships.Where(s => s.Voyage != null).OrderBy(s => s.Voyage.ArrivalTick).First();
            int shipId = first.Id;
            int arrivedAt = first.Voyage.DestinationId;
            yield return _rig.RealMs(first.Voyage.ArrivalTick - Sim.CurrentTick);
            var note = Sim.Notifications.First(n => n.ShipId == shipId);
            Assert.AreEqual(arrivedAt, Sim.FindShip(shipId).DockedSystemId);

            // 1. The ship is still docked where it arrived.
            yield return LookElsewhere();
            Bar.ToggleNotifications();
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(List.Click(note));
            yield return MapTestRig.Frames();
            AssertCentredOnShip(shipId, "docked ship");
            var star = GalaxyMapView.WorldPosition(Sim.Market.Galaxy.Systems[arrivedAt]);
            Assert.Less(Vector2.Distance(star, CameraCentre), 0.02f, "which is at the system it arrived at");
            Assert.IsFalse(Map.IsFollowing, "jumping does not start following");

            // 2. The ship has since left: the same old notification now leads to where the ship is in flight.
            var voyage = MapTestRig.SendEmpty(Sim, shipId);
            yield return _rig.RealMs((voyage.ArrivalTick - Sim.CurrentTick) / 2);
            Assert.IsNull(Sim.FindShip(shipId).DockedSystemId, "the ship is in flight");
            yield return LookElsewhere();
            Assert.IsTrue(List.Click(note));
            yield return MapTestRig.Frames();
            AssertCentredOnShip(shipId, "ship that has since departed");
            Assert.Greater(Vector2.Distance(star, CameraCentre), 0.05f, "the map did not go to the system named in the old notification");

            // 3. The ship has since arrived somewhere else.
            yield return _rig.RealMs(voyage.ArrivalTick - Sim.CurrentTick);
            Assert.AreEqual(voyage.DestinationId, Sim.FindShip(shipId).DockedSystemId);
            Assert.AreNotEqual(arrivedAt, voyage.DestinationId);
            yield return LookElsewhere();
            Assert.IsTrue(List.Click(note));
            yield return MapTestRig.Frames();
            AssertCentredOnShip(shipId, "ship that has since arrived somewhere else");
            var newStar = GalaxyMapView.WorldPosition(Sim.Market.Galaxy.Systems[voyage.DestinationId]);
            Assert.Less(Vector2.Distance(newStar, CameraCentre), 0.02f);

            // 4. From the close-up of another ship: the close-up closes and the map goes to the notification's ship.
            int other = Sim.Ships.First(s => s.Id != shipId).Id;
            Map.SelectShip(other);
            Map.OpenCloseUp();
            yield return MapTestRig.Frames();
            Assert.IsTrue(Map.IsCloseUpOpen);
            Assert.IsTrue(List.ClickAt(List.Items.ToList().IndexOf(note)));
            yield return MapTestRig.Frames();
            Assert.IsFalse(Map.IsCloseUpOpen, "the close-up closed");
            AssertCentredOnShip(shipId, "from the close-up");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ARealClickOnARow_JumpsToItsShip()
        {
            yield return Open(MapTestRig.FleetGame(12, 6));
            var first = Sim.Ships.Where(s => s.Voyage != null).OrderBy(s => s.Voyage.ArrivalTick).First();
            int shipId = first.Id;
            yield return _rig.RealMs(first.Voyage.ArrivalTick - Sim.CurrentTick);
            yield return LookElsewhere();
            Bar.ToggleNotifications();
            yield return MapTestRig.Frames(3);
            var row = List.Panel.Query(className: "notification-row").ToList().First(r => r.userData is Notification n && n.ShipId == shipId);
            using (var click = ClickEvent.GetPooled())
            {
                click.target = row;
                row.SendEvent(click);
            }
            yield return MapTestRig.Frames();
            AssertCentredOnShip(shipId, "clicked row");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator JumpingToASystem_SelectsAndCentresIt()
        {
            yield return Open(GameTestTuning.NewGameWithSystems(50));
            Map.OpenCloseUp();
            yield return MapTestRig.Frames();
            Assert.IsTrue(Map.IsCloseUpOpen);
            int target = Sim.Market.Galaxy.Systems.Count - 1;
            Assert.IsTrue(Map.JumpToSystem(target));
            yield return MapTestRig.Frames();
            Assert.IsFalse(Map.IsCloseUpOpen);
            Assert.AreEqual(target, Map.SelectedSystemId);
            Assert.AreEqual(-1, Map.SelectedShipId);
            Assert.Less(Vector2.Distance(GalaxyMapView.WorldPosition(Sim.Market.Galaxy.Systems[target]), CameraCentre), 0.02f);
            Assert.IsFalse(Map.JumpToSystem(999), "a system that does not exist is refused");
            Assert.IsFalse(Map.JumpToShip(999), "and so is a ship");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator AFullList_KeepsOnlyTheRowsInView_AndIsRebuiltOnlyWhenTheNotificationsChange()
        {
            var sim = MapTestRig.FleetGame(50, 50);
            int keep = sim.Market.Tuning.Notifications.KeepNewest;
            Assert.AreEqual(keep, DebugFleet.FillNotifications(sim), "the list is full");
            yield return Open(sim);
            Bar.ToggleNotifications();
            yield return MapTestRig.Frames(4);
            Assert.AreEqual(keep, List.Count, "every notification is in the list");
            Assert.Less(List.RowElementCount, 30, "but only the rows in view exist");
            Assert.Greater(List.RowElementCount, 3);

            int rebuilds = List.Rebuilds;
            for (int i = 0; i < 30; i++) yield return _rig.RealMs(16);
            Assert.AreEqual(rebuilds, List.Rebuilds, "thirty frames with nothing new: the rows were not rebuilt");

            long next = Sim.Ships.Where(s => s.Voyage != null).Min(s => s.Voyage.ArrivalTick);
            var oldNewest = List.Items[0];
            yield return _rig.RealMs(next - Sim.CurrentTick);
            Assert.AreEqual(rebuilds + 1, List.Rebuilds, "one frame brought new notifications: one rebuild");
            Assert.AreEqual(keep, List.Count, "the list stays at its limit");
            Assert.AreNotSame(oldNewest, List.Items[0]);
            Assert.AreSame(Sim.Notifications[Sim.Notifications.Count - 1], List.Items[0], "the newest is on top");
            Assert.AreEqual(0, Bar.UnseenCount, "an open list has nothing unseen");
            TestContext.WriteLine($"{List.Count} notifications, {List.RowElementCount} row elements, {List.Rebuilds} rebuilds.");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
