using System.Collections;
using System.Diagnostics;
using NUnit.Framework;
using SpaceTrade.Game.Boot;
using UnityEngine;
using UnityEngine.TestTools;

namespace SpaceTrade.Game.Tests
{
    /// <summary>
    /// The 50 ms frame rule on the map (session 12) at 12 and 50 systems with 1 and 50 ships in flight, on the real clock,
    /// at 1x, 3x, and 5x. The test asserts on our own scripts' work in each frame, which does not depend on whether the
    /// Editor window has focus; the whole-frame times are reported, and the live check in the Editor measures them.
    /// Building a new game and its map is a one-off frame: reported, and held to 250 ms.
    /// </summary>
    public class MapFrameTimeTests
    {
        public const double OrdinaryFrameLimitMs = 50.0;
        public const double OneOffFrameLimitMs = 250.0;

        private MapTestRig _rig;
        private GameObject _probeObject;

        [SetUp]
        public void SetUp() => _rig = new MapTestRig();

        [TearDown]
        public void TearDown()
        {
            _rig.Close();
            if (_probeObject != null) Object.Destroy(_probeObject);
        }

        [UnityTest]
        public IEnumerator OurWorkInEveryFrame_StaysUnder50ms_AndBuildingTheGameUnder250ms([Values(12, 50)] int systems, [Values(1, 50)] int shipsInFlight)
        {
            _probeObject = new GameObject("Probe");
            var probe = _probeObject.AddComponent<FrameTimeProbe>();
            yield return MapTestRig.Frames();

            var watch = Stopwatch.StartNew();
            var sim = MapTestRig.FleetGame(systems, shipsInFlight);
            double newGameMs = watch.Elapsed.TotalMilliseconds;
            _rig.Build(sim, fakeTime: false);
            probe.MarkOneOff($"new game and fleet, {systems} systems, {shipsInFlight} ships", newGameMs);
            probe.MarkOneOff($"map build, {systems} systems, {shipsInFlight} ships", _rig.MapBuildMs);
            yield return MapTestRig.Frames(3);
            int inFlight = 0;
            foreach (var s in sim.Ships) if (s.IsInTransit) inFlight++;
            Assert.AreEqual(shipsInFlight, inFlight);

            yield return MapFrameCheck.Run(_rig.Map, _rig.Runner.Driver, probe, 1.5f, $"{systems} systems x {shipsInFlight} ships");
            TestContext.WriteLine(probe.Report());

            Assert.AreEqual(1, probe.OneOffFrames.Count, "the build is one frame");
            foreach (var o in probe.OneOffFrames) Assert.Less(o.Ms, OneOffFrameLimitMs, o.What);
            Assert.AreEqual(3, probe.Segments.Count);
            foreach (var segment in probe.Segments)
            {
                Assert.Greater(segment.Frames, 10, segment.Label);
                Assert.Less(segment.SlowestWorkMs, OrdinaryFrameLimitMs, segment.Label + ": our work in the slowest frame");
            }
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// Session 13: the same rule with the bar on screen and the notification list full and open, at 50 systems with 50
        /// ships in flight. Sounds are logged too, so the cost of the debug log line is inside the measurement.
        /// </summary>
        [UnityTest]
        public IEnumerator WithAFullNotificationListOpen_At50SystemsAnd50Ships_OurWorkInEveryFrame_StaysUnder50ms()
        {
            _probeObject = new GameObject("Probe");
            var probe = _probeObject.AddComponent<FrameTimeProbe>();
            yield return MapTestRig.Frames();

            // Test setup, in its own frame and not part of the game: flying 50 ships through enough voyages to fill the list.
            var watch = Stopwatch.StartNew();
            var sim = MapTestRig.FleetGame(50, 50);
            double newGameMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            int keep = sim.Market.Tuning.Notifications.KeepNewest;
            Assert.AreEqual(keep, DebugFleet.FillNotifications(sim), "the notification list is full");
            const string setup = "test setup only: filling the notification list";
            probe.MarkOneOff(setup, watch.Elapsed.TotalMilliseconds);
            yield return MapTestRig.Frames(3);

            _rig.Build(sim, fakeTime: false);
            probe.MarkOneOff("new game and fleet, 50 systems, 50 ships", newGameMs);
            probe.MarkOneOff("map build, 50 systems, 50 ships, full notification list", _rig.MapBuildMs);
            _rig.Map.Bar.ToggleNotifications();
            yield return MapTestRig.Frames(3);
            int inFlight = 0;
            foreach (var s in sim.Ships) if (s.IsInTransit) inFlight++;
            Assert.AreEqual(50, inFlight, "all 50 ships are in flight");
            Assert.AreEqual(50, sim.Market.Galaxy.Systems.Count);
            Assert.IsTrue(_rig.Map.Bar.Notifications.IsOpen);
            Assert.AreEqual(keep, _rig.Map.Bar.Notifications.Count, "the open list holds every notification");

            yield return MapFrameCheck.Run(_rig.Map, _rig.Runner.Driver, probe, 1.5f, $"50 systems x 50 ships, {keep} notifications, list open");
            TestContext.WriteLine(probe.Report());
            TestContext.WriteLine($"Notification rows on screen: {_rig.Map.Bar.Notifications.RowElementCount} of {keep}; list rebuilt {_rig.Map.Bar.Notifications.Rebuilds} time(s).");

            Assert.IsTrue(_rig.Map.Bar.Notifications.IsOpen, "the list stayed open throughout");
            Assert.AreEqual(2, probe.OneOffFrames.Count, "the setup frame and the build frame");
            foreach (var o in probe.OneOffFrames)
                if (o.What != setup) Assert.Less(o.Ms, OneOffFrameLimitMs, o.What);
            Assert.AreEqual(3, probe.Segments.Count);
            foreach (var segment in probe.Segments)
            {
                Assert.Greater(segment.Frames, 10, segment.Label);
                Assert.Less(segment.SlowestWorkMs, OrdinaryFrameLimitMs, segment.Label + ": our work in the slowest frame");
            }
            LogAssert.NoUnexpectedReceived();
        }
    }
}
