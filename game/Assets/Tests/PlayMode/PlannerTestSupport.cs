using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SpaceTrade.Game.Audio;
using SpaceTrade.Game.Hud;
using SpaceTrade.Game.Map;
using SpaceTrade.Sim;
using UnityEngine.UIElements;

namespace SpaceTrade.Game.Tests
{
    /// <summary>
    /// Shared by the session 14 screen tests: pressing real buttons and rows the way a click does, reading the sounds
    /// started, and comparing whole game states byte for byte.
    /// </summary>
    public abstract class PlannerTestBase
    {
        internal MapTestRig _rig;

        [SetUp]
        public void SetUp() => _rig = new MapTestRig();

        [TearDown]
        public void TearDown() => _rig.Close();

        protected MapScreen Map => _rig.Map;
        protected Simulation Sim => _rig.Sim;
        protected TopBar Bar => Map.Bar;
        protected VoyagePlanner Planner => Map.Planner;
        protected ReceiptPanel Receipt => Map.Receipt;
        protected GameAudio Audio => _rig.Audio;

        protected static byte[] Bytes(Simulation sim) => Encoding.UTF8.GetBytes(sim.DescribeState());

        /// <summary>Presses a button the way the mouse or the Enter key does. A greyed-out button takes no press.</summary>
        protected static void Press(Button button)
        {
            Assert.IsNotNull(button, "the button exists");
            if (!button.enabledInHierarchy) return;
            using (var e = NavigationSubmitEvent.GetPooled())
            {
                e.target = button;
                button.SendEvent(e);
            }
        }

        protected Button ShipPanelButton(string name) => Map.Hud.Root.Q<Button>(name);
        protected Button PlannerButton(string name) => Planner.Panel.Q<Button>(name);

        /// <summary>True when the button is on screen: itself and everything above it displayed and visible.</summary>
        protected static bool OnScreen(VisualElement e)
        {
            if (e == null || e.panel == null) return false;
            for (var p = e; p != null; p = p.parent)
                if (p.resolvedStyle.display == DisplayStyle.None || p.resolvedStyle.visibility == Visibility.Hidden) return false;
            return true;
        }

        /// <summary>Clicks the row of a job list that shows this job, which must be one of the rows on screen.</summary>
        protected static void ClickRow(JobListView list, JobRow job)
        {
            var row = list.Root.Query(className: "job-row").ToList().FirstOrDefault(r => ReferenceEquals(r.userData, job));
            Assert.IsNotNull(row, "the job's row is on screen: " + job.Title);
            using (var click = ClickEvent.GetPooled())
            {
                click.target = row;
                row.SendEvent(click);
            }
        }

        protected List<PlayedSound> Since(int total) => Audio.History.Skip(Audio.History.Count - (Audio.StartedTotal - total)).ToList();
        protected static string Names(IEnumerable<PlayedSound> sounds) => string.Join(", ", sounds.Select(s => s.Id.ToString()));

        protected IEnumerator NothingMorePlays(string after)
        {
            int total = Audio.StartedTotal;
            yield return MapTestRig.Frames(30);
            Assert.AreEqual(total, Audio.StartedTotal, $"after {after}, thirty more frames started no sound (got {Names(Since(total))})");
        }

        /// <summary>Opens the planner for the selected ship with the ship panel's own button.</summary>
        protected IEnumerator OpenPlannerFromTheShipPanel()
        {
            var plan = ShipPanelButton("plan-voyage");
            Assert.IsTrue(OnScreen(plan), "the ship panel shows a Plan voyage button for a docked ship");
            Press(plan);
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(Map.Panels.IsShowing(Planner), "the planner is showing");
        }

        /// <summary>The first job in the planner's list, as sorted, that the ship can fly.</summary>
        protected JobRow FirstFlyable() => Planner.Jobs.Shown.First(r => r.CanFly);
    }
}
