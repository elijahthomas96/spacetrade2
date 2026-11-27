using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Game.Hud;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Voyages;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace SpaceTrade.Game.Tests
{
    /// <summary>
    /// The one-panel rule in two tiers (AGENTS.md, owner's wording of session 14).
    ///
    /// Work panels (system view, ship panel, planner, receipt): only one shows at a time; opening another replaces it,
    /// and closing it brings back the one it replaced.
    /// Utility panels (notification list, settings): only one is open at a time, and it sits over the work panel; they
    /// replace each other and never bring each other back; closing one returns to the work panel underneath.
    /// Esc closes a utility panel first, then the top work panel, then the close-up, then clears the selection.
    /// </summary>
    public class PanelRuleTests : PlannerTestBase
    {
        private const string SystemView = "system view", ShipPanel = "ship panel", PlannerPanel = "planner", ReceiptPanel = "receipt", List = "notification list", Settings = "settings";

        /// <summary>The side panels on screen right now. The rule is that there is never more than one.</summary>
        private List<string> Showing()
        {
            var showing = new List<string>();
            if (Map.Hud.InfoPanelShowing) showing.Add(Map.Hud.SelectedSystemId >= 0 ? SystemView : ShipPanel);
            if (OnScreen(Planner.Panel)) showing.Add(PlannerPanel);
            if (OnScreen(Receipt.Panel)) showing.Add(ReceiptPanel);
            if (Bar.Notifications.IsOpen && OnScreen(Bar.Notifications.Panel)) showing.Add(List);
            if (Bar.Settings.IsOpen && OnScreen(Bar.Settings.Panel)) showing.Add(Settings);
            return showing;
        }

        private void AssertOnly(string panel, string when)
        {
            var showing = Showing();
            Assert.AreEqual(panel ?? "", string.Join(" + ", showing), when);
        }

        /// <summary>A game with one finished voyage (so there is a receipt) and the starter ship docked again, selected.</summary>
        private IEnumerator OpenWithAReceipt()
        {
            var sim = GameTestTuning.NewGameWithSystems(12);
            var job = OpeningOffers.FindJobs(sim, sim.Ships[0].Id).OrderBy(j => j.TransitTicks).First();
            Assert.IsTrue(sim.Dispatch(job.Quote.Plan, true).Succeeded);
            sim.Play();
            sim.Advance(sim.Ships[0].Voyage.ArrivalTick);
            sim.Pause();
            Assert.AreEqual(1, sim.Receipts.Count);
            yield return _rig.Open(sim);
            yield return MapTestRig.Frames(3);
        }

        private void OpenWork(string panel)
        {
            switch (panel)
            {
                case SystemView: Map.Select(Sim.Ships[0].DockedSystemId.Value); break;
                case ShipPanel: Map.SelectShip(Sim.Ships[0].Id); break;
                case PlannerPanel: Map.SelectShip(Sim.Ships[0].Id); Assert.IsTrue(Map.OpenPlanner()); break;
                case ReceiptPanel: Map.SelectShip(Sim.Ships[0].Id); Assert.IsTrue(Map.OpenLastReceipt()); break;
            }
        }

        private void ToggleUtility(string panel)
        {
            if (panel == List) Bar.ToggleNotifications(); else Bar.ToggleSettings();
        }

        [UnityTest]
        public IEnumerator WorkPanels_OnlyOneShows_OpeningAnotherReplacesIt_AndClosingItBringsBackTheOneItReplaced()
        {
            yield return OpenWithAReceipt();
            AssertOnly(ShipPanel, "a game opens on the starter ship's panel");

            // Ship panel -> planner -> back.
            Press(ShipPanelButton("plan-voyage"));
            yield return MapTestRig.Frames(3);
            AssertOnly(PlannerPanel, "the planner replaces the ship panel");
            Press(PlannerButton("planner-close"));
            yield return MapTestRig.Frames(3);
            AssertOnly(ShipPanel, "closing the planner brings the ship panel back");

            // Ship panel -> receipt -> back.
            Press(ShipPanelButton("last-receipt"));
            yield return MapTestRig.Frames(3);
            AssertOnly(ReceiptPanel, "the receipt replaces the ship panel");
            Press(Receipt.Panel.Q<Button>("receipt-close"));
            yield return MapTestRig.Frames(3);
            AssertOnly(ShipPanel, "closing the receipt brings the ship panel back");

            // Ship panel -> planner -> receipt -> back to the planner -> back to the ship panel.
            Assert.IsTrue(Map.OpenPlanner());
            yield return MapTestRig.Frames(3);
            Planner.ChooseJob(FirstFlyable());
            int destination = Planner.DestinationId;
            Assert.IsTrue(Map.OpenLastReceipt());
            yield return MapTestRig.Frames(3);
            AssertOnly(ReceiptPanel, "the receipt replaces the planner");
            Press(Receipt.Panel.Q<Button>("receipt-close"));
            yield return MapTestRig.Frames(3);
            AssertOnly(PlannerPanel, "closing the receipt brings the planner back");
            Assert.AreEqual(destination, Planner.DestinationId, "with its plan as it was left");
            Press(PlannerButton("planner-close"));
            yield return MapTestRig.Frames(3);
            AssertOnly(ShipPanel, "and closing the planner brings the ship panel back");

            // Receipt -> planner -> back to the receipt -> back to the ship panel (the other way round).
            Assert.IsTrue(Map.OpenLastReceipt());
            Assert.IsTrue(Map.OpenPlanner());
            yield return MapTestRig.Frames(3);
            AssertOnly(PlannerPanel, "the planner replaces the receipt");
            Map.Panels.Close(Planner);
            yield return MapTestRig.Frames(3);
            AssertOnly(ReceiptPanel, "closing the planner brings the receipt back");
            Map.Panels.Close(Receipt);
            yield return MapTestRig.Frames(3);
            AssertOnly(ShipPanel, "and then the ship panel");

            // Ship panel <-> system view: each replaces the other, and neither is ever shown with the other.
            Map.Select(Sim.Ships[0].DockedSystemId.Value);
            yield return MapTestRig.Frames(3);
            AssertOnly(SystemView, "selecting the star replaces the ship panel with the system view");
            Map.SelectShip(Sim.Ships[0].Id);
            yield return MapTestRig.Frames(3);
            AssertOnly(ShipPanel, "selecting the ship replaces the system view with the ship panel");

            // The planner and a receipt belong to the ship: selecting something else takes them away with it.
            Assert.IsTrue(Map.OpenPlanner());
            yield return MapTestRig.Frames(3);
            Map.Select(Sim.Ships[0].DockedSystemId.Value);
            yield return MapTestRig.Frames(3);
            AssertOnly(SystemView, "selecting a star while planning shows only the system view");
            Assert.IsFalse(Map.Panels.IsOpen(Planner));
            Map.Select(-1);
            yield return MapTestRig.Frames(3);
            AssertOnly(null, "nothing selected, no panel");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>Every pair of a utility panel over a work panel: the utility is the only panel showing, and closing it returns to that work panel.</summary>
        [UnityTest]
        public IEnumerator AUtilityPanel_SitsOverTheWorkPanel_AndClosingItReturnsToIt(
            [Values(SystemView, ShipPanel, PlannerPanel, ReceiptPanel)] string work, [Values(List, Settings)] string utility)
        {
            yield return OpenWithAReceipt();
            OpenWork(work);
            yield return MapTestRig.Frames(3);
            AssertOnly(work, "before");

            ToggleUtility(utility);
            yield return MapTestRig.Frames(3);
            AssertOnly(utility, $"the {utility} is the only panel on screen: the {work} under it is hidden, not showing beside it");

            ToggleUtility(utility);
            yield return MapTestRig.Frames(3);
            AssertOnly(work, $"closing the {utility} returns to the {work}");

            // The same with Esc.
            ToggleUtility(utility);
            yield return MapTestRig.Frames(3);
            yield return _rig.Keys(1, Key.Escape);
            AssertOnly(work, $"Esc closes the {utility} and returns to the {work}");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>The two utility panels replace each other and never bring each other back, over every work panel.</summary>
        [UnityTest]
        public IEnumerator UtilityPanels_ReplaceEachOther_AndNeverBringEachOtherBack(
            [Values(SystemView, ShipPanel, PlannerPanel, ReceiptPanel)] string work, [Values(List, Settings)] string first)
        {
            string second = first == List ? Settings : List;
            yield return OpenWithAReceipt();
            OpenWork(work);
            yield return MapTestRig.Frames(3);

            ToggleUtility(first);
            yield return MapTestRig.Frames(3);
            AssertOnly(first, "the first utility panel");
            ToggleUtility(second);
            yield return MapTestRig.Frames(3);
            AssertOnly(second, $"the {second} replaces the {first}");
            Assert.IsFalse(first == List ? Bar.Notifications.IsOpen : Bar.Settings.IsOpen, $"the {first} is closed, not waiting underneath");

            ToggleUtility(second);
            yield return MapTestRig.Frames(3);
            AssertOnly(work, $"closing the {second} returns to the {work}; the {first} does not come back");
            Assert.IsFalse(Bar.IsUtilityOpen);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>A work panel opened while a utility panel is open takes its place underneath; it shows when the utility closes.</summary>
        [UnityTest]
        public IEnumerator SelectingSomething_WhileAUtilityPanelIsOpen_ChangesTheWorkPanelUnderneath([Values(List, Settings)] string utility)
        {
            yield return OpenWithAReceipt();
            ToggleUtility(utility);
            yield return MapTestRig.Frames(3);
            AssertOnly(utility, "over the ship panel");
            Map.Select(Sim.Ships[0].DockedSystemId.Value);
            yield return MapTestRig.Frames(3);
            AssertOnly(utility, "still the only panel on screen");
            ToggleUtility(utility);
            yield return MapTestRig.Frames(3);
            AssertOnly(SystemView, "the work panel underneath is now the system view");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>The fault this rule fixes: the settings panel used to sit over the ship panel, whose buttons showed under the mute button.</summary>
        [UnityTest]
        public IEnumerator Settings_NoLongerSitsOverTheShipPanel_WhoseButtonsAreNeitherSeenNorClickable()
        {
            yield return _rig.Open(GameTestTuning.NewGameWithSystems(12));
            yield return MapTestRig.Frames(3);
            var follow = ShipPanelButton("follow");
            var closeUp = ShipPanelButton("close-up");
            Assert.IsTrue(OnScreen(follow));
            var followAt = follow.worldBound.center;
            var panel = Map.Hud.Root.panel;
            Assert.IsTrue(follow == panel.Pick(followAt) || follow.Contains(panel.Pick(followAt)), "before: the Follow button is what is under that point");

            Bar.ToggleSettings();
            yield return MapTestRig.Frames(3);
            AssertOnly(Settings, "settings is the only panel on screen");
            Assert.IsFalse(OnScreen(follow), "the ship panel's Follow button is hidden");
            Assert.IsFalse(OnScreen(closeUp), "and so is Close-up");
            var picked = panel.Pick(followAt);
            Assert.IsFalse(picked != null && (picked == follow || follow.Contains(picked) || Map.Hud.Root.Q("system-info").Contains(picked)), "a click where Follow was does not reach the ship panel");

            Bar.ToggleSettings();
            yield return MapTestRig.Frames(3);
            AssertOnly(ShipPanel, "closing settings brings the ship panel back");
            Assert.IsTrue(OnScreen(follow));
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>Esc closes a utility panel first, then the top work panel, then the close-up, then clears the selection.</summary>
        [UnityTest]
        public IEnumerator Esc_ClosesTheUtilityPanel_ThenTheTopWorkPanel_ThenClearsTheSelection()
        {
            yield return OpenWithAReceipt();
            int ship = Sim.Ships[0].Id;
            Assert.IsTrue(Map.OpenPlanner());
            Assert.IsTrue(Map.OpenLastReceipt());
            Bar.ToggleSettings();
            Bar.ToggleNotifications();
            yield return MapTestRig.Frames(3);
            AssertOnly(List, "the list, over the receipt, over the planner, over the ship panel");

            yield return _rig.Keys(1, Key.Escape);
            AssertOnly(ReceiptPanel, "the first Esc closes the utility panel (settings does not come back)");
            yield return _rig.Keys(1, Key.Escape);
            AssertOnly(PlannerPanel, "the next closes the top work panel, the receipt, and the planner it replaced comes back");
            yield return _rig.Keys(1, Key.Escape);
            AssertOnly(ShipPanel, "the next closes the planner");
            Assert.AreEqual(ship, Map.SelectedShipId);
            yield return _rig.Keys(1, Key.Escape);
            Assert.AreEqual(-1, Map.SelectedShipId, "the last clears the selection");
            AssertOnly(null, "and no panel is left");

            // With the close-up: the utility, then the close-up, then the selection (the planner and receipt cannot be open in the close-up).
            Map.SelectShip(ship);
            Assert.IsTrue(Map.OpenPlanner());
            Map.OpenCloseUp();
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(Map.IsCloseUpOpen);
            Assert.IsFalse(Map.Panels.IsOpen(Planner), "opening the close-up puts the planner away");
            Bar.ToggleSettings();
            yield return MapTestRig.Frames(3);
            yield return _rig.Keys(1, Key.Escape);
            Assert.IsFalse(Bar.Settings.IsOpen);
            Assert.IsTrue(Map.IsCloseUpOpen, "the first Esc closes only settings");
            yield return _rig.Keys(1, Key.Escape);
            Assert.IsFalse(Map.IsCloseUpOpen, "the next closes the close-up");
            Assert.AreEqual(ship, Map.SelectedShipId);
            yield return _rig.Keys(1, Key.Escape);
            Assert.AreEqual(-1, Map.SelectedShipId);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>Opening and closing work panels plays the panel sounds once each; a panel that comes back is silent.</summary>
        [UnityTest]
        public IEnumerator WorkPanels_PlayThePanelSoundsOnce_AndAPanelComingBackIsSilent()
        {
            yield return OpenWithAReceipt();
            int total = Audio.StartedTotal;
            Press(ShipPanelButton("plan-voyage"));
            yield return MapTestRig.Frames(3);
            Assert.AreEqual("PanelOpen", Names(Since(total)), "the planner opening");
            total = Audio.StartedTotal;
            Assert.IsTrue(Map.OpenLastReceipt());
            yield return MapTestRig.Frames(3);
            Assert.AreEqual("PanelOpen", Names(Since(total)), "the receipt opening over the planner");
            total = Audio.StartedTotal;
            Press(Receipt.Panel.Q<Button>("receipt-close"));
            yield return MapTestRig.Frames(3);
            Assert.AreEqual("PanelClose", Names(Since(total)), "the receipt closing; the planner comes back without a sound");
            total = Audio.StartedTotal;
            Press(PlannerButton("planner-close"));
            yield return MapTestRig.Frames(3);
            Assert.AreEqual("PanelClose", Names(Since(total)), "the planner closing");
            yield return NothingMorePlays("the panels");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
