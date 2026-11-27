using System.Collections;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Game.Hud;
using SpaceTrade.Game.Map;
using SpaceTrade.Game.Theming;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Timing;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace SpaceTrade.Game.Tests
{
    /// <summary>
    /// The always-visible bar (spec 9.4) and the two ETA displays (spec 3.4): everything shown is the simulation's own
    /// figure, a speed change shows on the real-time ETA without moving the arrival date, and the countdown freezes while
    /// paused.
    /// </summary>
    public class TopBarTests
    {
        private MapTestRig _rig;

        [SetUp]
        public void SetUp() => _rig = new MapTestRig();

        [TearDown]
        public void TearDown() => _rig.Close();

        private MapScreen Map => _rig.Map;
        private Simulation Sim => _rig.Sim;
        private TopBar Bar => Map.Bar;

        /// <summary>A new game starts paused; these tests start with the clock running.</summary>
        private IEnumerator Open(Simulation sim)
        {
            Assert.IsTrue(sim.Clock.IsPaused, "a new game starts paused");
            Assert.IsTrue(sim.Play().Succeeded);
            yield return _rig.Open(sim);
        }

        internal static void Press(VisualElement button)
        {
            Assert.IsNotNull(button, "the button exists");
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = button;
                button.SendEvent(e);
            }
        }

        private static long CeilingOf(long ticks, long ticksPerRealSecond) => (ticks + ticksPerRealSecond - 1) / ticksPerRealSecond;

        private string PanelEta => Map.Hud.Root.Q<Label>("ship-eta").text;

        [UnityTest]
        public IEnumerator TheBar_ShowsTheSimulationsDateAndCredits_AndStaysOnTopOfTheCloseUp([Values(12, 50)] int systems)
        {
            yield return Open(GameTestTuning.NewGameWithSystems(systems));
            Assert.AreEqual("Y1 M01 D01", Bar.DateText);
            Assert.AreEqual(Format.Credits(Sim.Credits), Bar.CreditsText);
            Assert.AreEqual(Theme.TopBar.Height, Bar.BarRect.height, 0.5f);
            Assert.AreEqual(0f, Bar.BarRect.y, 0.5f, "the bar is at the top");
            Assert.AreEqual(Map.Hud.PanelSize.x, Bar.BarRect.width, 0.5f, "and spans the screen");

            long creditsBefore = Sim.Credits;
            var voyage = MapTestRig.SendEmpty(Sim, Sim.Ships[0].Id);
            yield return _rig.RealMs(3 * GameTime.TicksPerDay);
            Assert.AreEqual(Sim.CurrentDate.ToString(), Bar.DateText);
            Assert.AreEqual("Y1 M01 D04", Bar.DateText, "three game days on");
            TestContext.WriteLine($"Credits {Format.Credits(creditsBefore)} before the voyage, {Format.Credits(Sim.Credits)} after; the bar shows {Bar.CreditsText}.");
            Assert.AreEqual(Format.Credits(Sim.Credits), Bar.CreditsText, "credits follow the simulation");

            Map.OpenCloseUp();
            yield return MapTestRig.Frames();
            Assert.IsTrue(Map.IsCloseUpOpen);
            Assert.Greater(TopBar.SortingOrder, ShipCloseUp.SortingOrder, "the bar is drawn above the close-up");
            Assert.AreEqual(TopBar.SortingOrder, Bar.GetComponent<UIDocument>().sortingOrder, 0.01f);
            Assert.AreEqual(ShipCloseUp.SortingOrder, Map.CloseUp.GetComponent<UIDocument>().sortingOrder, 0.01f);
            Assert.AreEqual(DisplayStyle.Flex, Bar.Root.resolvedStyle.display);
            var picked = Bar.Root.panel.Pick(new Vector2(Bar.BarRect.width / 2f, Bar.BarRect.height / 2f));
            Assert.IsTrue(picked != null && Bar.Root.Contains(picked), "a click on the bar reaches the bar, not the close-up under it");
            var vesselPanel = Map.CloseUp.Root.Q("vessel-panel");
            Assert.GreaterOrEqual(vesselPanel.worldBound.yMin, Bar.BarRect.yMax - 0.5f, "the close-up's panel starts below the bar");

            Press(Bar.Root.Q<Button>("pause-play"));
            yield return MapTestRig.Frames();
            Assert.IsTrue(Sim.Clock.IsPaused, "the bar's pause button works while the close-up is open");
            Assert.IsTrue(Map.IsCloseUpOpen);
            StringAssert.EndsWith("· Paused", Map.CloseUp.Value("eta"));
            Assert.AreEqual(voyage.ArrivalTick, Sim.Ships[0].Voyage.ArrivalTick);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator SpaceAndThePauseButton_PauseAndResume_AndKeys123AndTheSpeedButtons_ChooseTheSpeed()
        {
            yield return Open(GameTestTuning.NewGameWithSystems(12));
            Assert.IsFalse(Sim.Clock.IsPaused);
            Assert.IsFalse(Bar.ShowsPaused);
            Assert.IsTrue(Bar.IsSpeedLit(ClockSpeed.X1));

            yield return _rig.Keys(1, Key.Space);
            Assert.IsTrue(Sim.Clock.IsPaused, "Space pauses");
            Assert.IsTrue(Bar.ShowsPaused, "the button now offers play");
            yield return _rig.Keys(1, Key.Space);
            Assert.IsFalse(Sim.Clock.IsPaused, "Space resumes");
            Assert.IsFalse(Bar.ShowsPaused);

            Press(Bar.Root.Q<Button>("pause-play"));
            yield return MapTestRig.Frames();
            Assert.IsTrue(Sim.Clock.IsPaused, "the button pauses");
            Press(Bar.Root.Q<Button>("pause-play"));
            yield return MapTestRig.Frames();
            Assert.IsFalse(Sim.Clock.IsPaused, "and resumes");

            yield return _rig.Keys(1, Key.Digit2);
            Assert.AreEqual(ClockSpeed.X3, Sim.Clock.Speed, "2 chooses 3x");
            Assert.IsTrue(Bar.IsSpeedLit(ClockSpeed.X3));
            Assert.IsFalse(Bar.IsSpeedLit(ClockSpeed.X1));
            yield return _rig.Keys(1, Key.Digit3);
            Assert.AreEqual(ClockSpeed.X5, Sim.Clock.Speed, "3 chooses 5x");
            Assert.IsTrue(Bar.IsSpeedLit(ClockSpeed.X5));
            yield return _rig.Keys(1, Key.Digit1);
            Assert.AreEqual(ClockSpeed.X1, Sim.Clock.Speed, "1 chooses 1x");
            Assert.IsTrue(Bar.IsSpeedLit(ClockSpeed.X1));
            Assert.IsFalse(Bar.IsSpeedLit(ClockSpeed.X5));

            Press(Bar.Root.Q<Button>("speed-5x"));
            yield return MapTestRig.Frames();
            Assert.AreEqual(ClockSpeed.X5, Sim.Clock.Speed, "the 5x button");
            Press(Bar.Root.Q<Button>("speed-3x"));
            yield return MapTestRig.Frames();
            Assert.AreEqual(ClockSpeed.X3, Sim.Clock.Speed, "the 3x button");
            Assert.AreEqual(ClockSpeed.X3, Bar.LitSpeed);
            Assert.AreEqual(1, new[] { ClockSpeed.X1, ClockSpeed.X3, ClockSpeed.X5 }.Count(Bar.IsSpeedLit), "exactly one speed is lit");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ASpeedChange_ShowsOnTheRealTimeEta_InBothDisplays_WithoutChangingTheArrivalDate()
        {
            yield return Open(GameTestTuning.NewGameWithSystems(12));
            var ship = Sim.Ships[0];
            var voyage = MapTestRig.SendEmpty(Sim, ship.Id);
            long arrival = voyage.ArrivalTick;
            string arrives = "Arrives " + GameDate.FromTick(arrival);
            yield return _rig.RealMs(1000);

            var at1 = Sim.EtaOf(ship.Id);
            Assert.AreEqual(at1.Describe(), PanelEta, "the ship panel shows the simulation's own line");
            Assert.AreEqual($"{arrives} · {Format.RealDuration(at1.RealSecondsRemaining)} at 1x", PanelEta);
            Assert.AreEqual(CeilingOf(arrival - Sim.CurrentTick, 1000), at1.RealSecondsRemaining, "at 1x one tick is one real millisecond");

            yield return _rig.Keys(1, Key.Digit2);
            var at3 = Sim.EtaOf(ship.Id);
            Assert.AreEqual(at3.Describe(), PanelEta);
            Assert.AreEqual($"{arrives} · {Format.RealDuration(at3.RealSecondsRemaining)} at 3x", PanelEta, "the same arrival date, a third of the real time");
            Assert.AreEqual(CeilingOf(arrival - Sim.CurrentTick, 3000), at3.RealSecondsRemaining);
            Assert.Less(at3.RealSecondsRemaining, at1.RealSecondsRemaining);

            Map.OpenCloseUp();
            yield return _rig.Keys(1, Key.Digit3);
            var at5 = Sim.EtaOf(ship.Id);
            Assert.AreEqual(at5.Describe(), Map.CloseUp.Value("eta"), "the close-up shows the same line");
            Assert.AreEqual(at5.Describe(), PanelEta, "and so does the ship panel under it");
            Assert.AreEqual($"{arrives} · {Format.RealDuration(at5.RealSecondsRemaining)} at 5x", Map.CloseUp.Value("eta"));
            Assert.AreEqual(CeilingOf(arrival - Sim.CurrentTick, 5000), at5.RealSecondsRemaining);

            Assert.AreEqual(arrival, ship.Voyage.ArrivalTick, "three speed changes never moved the arrival");
            Assert.AreEqual(at1.ArrivalDate, at5.ArrivalDate);
            TestContext.WriteLine($"1x: {at1.Describe()}\n3x: {at3.Describe()}\n5x: {at5.Describe()}");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator TheCountdown_FreezesWhilePaused_AndRunsAgainOnResume()
        {
            yield return Open(GameTestTuning.NewGameWithSystems(12));
            var ship = Sim.Ships[0];
            var voyage = MapTestRig.SendEmpty(Sim, ship.Id);
            Map.OpenCloseUp();
            yield return _rig.RealMs(5000);
            string running = PanelEta;
            StringAssert.EndsWith("at 1x", running);
            yield return _rig.RealMs(5000);
            Assert.AreNotEqual(running, PanelEta, "the countdown runs while the clock runs");

            yield return _rig.Keys(1, Key.Space);
            Assert.IsTrue(Sim.Clock.IsPaused);
            long tick = Sim.CurrentTick;
            long frozen = Sim.EtaOf(ship.Id).RealSecondsRemaining;
            string paused = "Arrives " + GameDate.FromTick(voyage.ArrivalTick) + " · Paused";
            for (int i = 0; i < 5; i++)
            {
                yield return _rig.RealMs(60_000);
                Assert.AreEqual(paused, PanelEta, "a real minute passed: the ship panel still says Paused");
                Assert.AreEqual(paused, Map.CloseUp.Value("eta"), "and so does the close-up");
                Assert.AreEqual(frozen, Sim.EtaOf(ship.Id).RealSecondsRemaining, "the real time left has not moved");
                Assert.AreEqual(tick, Sim.CurrentTick);
            }

            yield return _rig.Keys(1, Key.Space);
            Assert.AreEqual(frozen, Sim.EtaOf(ship.Id).RealSecondsRemaining, "resuming picks up exactly where it froze");
            yield return _rig.RealMs(10_000);
            Assert.AreEqual(frozen - 10, Sim.EtaOf(ship.Id).RealSecondsRemaining, "ten real seconds later, ten seconds less");
            Assert.AreEqual(Sim.EtaOf(ship.Id).Describe(), PanelEta);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator StarNamesAndPanels_KeepClearOfTheBar([Values(12, 50)] int systems)
        {
            yield return Open(MapTestRig.FleetGame(systems, 3));
            yield return MapTestRig.Frames(3);
            var bar = Bar.BarRect;
            Assert.Greater(Map.Hud.ShownLabels.Count, 0);
            Map.Select(Sim.Market.Shipyards[0].SystemId);
            yield return MapTestRig.Frames(3);
            int hovered = Map.Hud.HoveredSystemId, selected = Map.Hud.SelectedSystemId;
            foreach (var (id, rect) in Map.Hud.ShownLabels)
                if (id != hovered && id != selected) Assert.IsFalse(rect.Overlaps(bar), $"the name of system {id} is under the bar");
            Assert.GreaterOrEqual(Map.Hud.InfoPanelRect.yMin, bar.yMax - 0.5f, "the info panel starts below the bar");

            Bar.ToggleNotifications();
            yield return MapTestRig.Frames(3);
            var list = Bar.Notifications.Rect;
            Assert.Greater(list.width, 0f);
            Assert.GreaterOrEqual(list.yMin, bar.yMax - 0.5f, "the list hangs below the bar");
            foreach (var (id, rect) in Map.Hud.ShownLabels)
                if (id != hovered && id != selected) Assert.IsFalse(rect.Overlaps(list), $"the name of system {id} is under the open list");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator Esc_ClosesTheOpenListOrSettingsFirst_ThenTheCloseUp_ThenClearsTheSelection()
        {
            yield return Open(GameTestTuning.NewGameWithSystems(12));
            int ship = Sim.Ships[0].Id;
            Map.OpenCloseUp();
            Bar.ToggleNotifications();
            yield return MapTestRig.Frames();
            Assert.IsTrue(Bar.Notifications.IsOpen);

            yield return _rig.Keys(1, Key.Escape);
            Assert.IsFalse(Bar.Notifications.IsOpen, "the first Esc closes the list");
            Assert.IsTrue(Map.IsCloseUpOpen, "and nothing else");

            Bar.ToggleSettings();
            Assert.IsTrue(Bar.Settings.IsOpen);
            Bar.ToggleNotifications();
            Assert.IsFalse(Bar.Settings.IsOpen, "opening the list closes the settings panel");
            Bar.ToggleSettings();
            Assert.IsFalse(Bar.Notifications.IsOpen, "and the other way round");
            yield return _rig.Keys(1, Key.Escape);
            Assert.IsFalse(Bar.Settings.IsOpen, "Esc closes the settings panel");
            Assert.IsTrue(Map.IsCloseUpOpen);

            yield return _rig.Keys(1, Key.Escape);
            Assert.IsFalse(Map.IsCloseUpOpen, "the next Esc closes the close-up");
            Assert.AreEqual(ship, Map.SelectedShipId);
            yield return _rig.Keys(1, Key.Escape);
            Assert.AreEqual(-1, Map.SelectedShipId, "and the last clears the selection");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
