using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Game.Audio;
using SpaceTrade.Game.Boot;
using SpaceTrade.Game.Map;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Routes;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Voyages;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace SpaceTrade.Game.Tests
{
    /// <summary>
    /// Sounds follow game events (spec 9.6): one sound per event, in the frame the event shows up, never once per frame.
    /// The same sound asked for twice in a frame plays once; the more specific sound wins; a declared time jump stays
    /// silent and ends with the return summary sound once; an ordinary frame, however slow, plays its events.
    /// </summary>
    public class SoundEventTests
    {
        private MapTestRig _rig;
        private readonly List<string> _log = new List<string>();
        private readonly List<string> _otherMessages = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _rig = new MapTestRig();
            _log.Clear();
            _otherMessages.Clear();
            Application.logMessageReceived += OnLog;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= OnLog;
            _rig.Close();
        }

        private void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Log && message.StartsWith("[sound]")) _log.Add(message);
            else _otherMessages.Add(type + ": " + message);
        }

        /// <summary>For the tests that switch the sound log on: the console holds the sound lines and nothing else.</summary>
        private void AssertOnlySoundLinesWereLogged() => CollectionAssert.IsEmpty(_otherMessages, "console messages other than the sound log");

        private MapScreen Map => _rig.Map;
        private Simulation Sim => _rig.Sim;
        private GameAudio Audio => _rig.Audio;
        private int N(SoundId id) => Audio.StartedCount(id);

        /// <summary>The sounds started since <paramref name="startedBefore"/> sounds had been started, oldest first.</summary>
        private List<PlayedSound> Since(int startedBefore) => Audio.History.Skip(Audio.History.Count - (Audio.StartedTotal - startedBefore)).ToList();

        private static string Names(IEnumerable<PlayedSound> sounds) => string.Join(", ", sounds.Select(s => s.Id.ToString()));

        /// <summary>Thirty more frames of ordinary play with nothing happening: no sound may start.</summary>
        private IEnumerator NothingMorePlays(string after)
        {
            int total = Audio.StartedTotal;
            for (int i = 0; i < 30; i++) yield return _rig.RealMs(1);
            Assert.AreEqual(total, Audio.StartedTotal, $"after {after}, thirty more frames started no sound (got {Names(Since(total))})");
        }

        /// <summary>A new game starts paused; these tests start with the clock already running, which makes no sound.</summary>
        private IEnumerator Open(Simulation sim)
        {
            Assert.IsTrue(sim.Play().Succeeded);
            yield return _rig.Open(sim);
        }

        private static Simulation DebugGame(int systems)
        {
            var t = GameTestTuning.Load();
            DebugFleet.PrepareTuning(t);
            t.Galaxy.SystemCountMin = systems;
            t.Galaxy.SystemCountMax = systems;
            for (int i = 0; i < 100; i++)
            {
                var game = NewGame.Create(1 + i, t, DebugCapability.Grant());
                if (!game.UsedDefaults) return game.Simulation;
            }
            Assert.Fail("no seed gave a game");
            return null;
        }

        [UnityTest]
        public IEnumerator PauseResumeAndSpeed_EachPlayTheirOwnSoundOnce_WithoutAClick()
        {
            yield return Open(GameTestTuning.NewGameWithSystems(12));
            Assert.AreEqual(0, Audio.StartedTotal, "opening the game plays no effect");

            yield return _rig.Keys(1, Key.Space);
            Assert.AreEqual(1, N(SoundId.Pause), "pause");
            Assert.AreEqual(1, Audio.StartedTotal, "and nothing else: no click with it");
            yield return NothingMorePlays("pausing");

            yield return _rig.Keys(1, Key.Space);
            Assert.AreEqual(1, N(SoundId.Resume), "resume");
            Assert.AreEqual(2, Audio.StartedTotal);

            yield return _rig.Keys(1, Key.Digit2);
            Assert.AreEqual(1, N(SoundId.SpeedChange), "speed change");
            Assert.AreEqual(3, Audio.StartedTotal);
            yield return NothingMorePlays("a speed change");

            TopBarTests.Press(Map.Bar.Root.Q<Button>("speed-3x"));
            yield return MapTestRig.Frames();
            Assert.AreEqual(1, N(SoundId.SpeedChange), "choosing the speed already chosen changes nothing");
            Assert.AreEqual(1, N(SoundId.Click), "so it only clicks");

            TopBarTests.Press(Map.Bar.Root.Q<Button>("speed-5x"));
            yield return MapTestRig.Frames();
            Assert.AreEqual(2, N(SoundId.SpeedChange));
            TopBarTests.Press(Map.Bar.Root.Q<Button>("pause-play"));
            yield return MapTestRig.Frames();
            Assert.AreEqual(2, N(SoundId.Pause));
            Assert.AreEqual(1, N(SoundId.Click), "the buttons play the more specific sound only");
            Assert.AreEqual(6, Audio.StartedTotal);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ADispatch_PlaysDispatchWithoutProvision_AndBuyingSuppliesAlone_PlaysProvision()
        {
            yield return Open(MapTestRig.FleetGame(12, 0, 1));
            int starter = Sim.Ships[0].Id, second = Sim.Ships[1].Id;
            int from = Sim.FindShip(second).DockedSystemId.Value;
            int to = Sim.Market.Galaxy.Systems.Where(s => s.Id != from).OrderBy(s => Sim.Market.Galaxy.DistanceMilliLy(from, s.Id)).First().Id;

            long bought = Sim.SuppliesBoughtTotal;
            Assert.IsTrue(_rig.Runner.Command(s => s.Provision(new VoyagePlan(second, to))).Succeeded);
            Assert.Greater(Sim.SuppliesBoughtTotal, bought, "setup: supplies were bought");
            int frame = Time.frameCount;
            yield return MapTestRig.Frames();
            Assert.AreEqual(1, N(SoundId.Provision), "supplies bought on their own: the provision sound");
            Assert.AreEqual(0, N(SoundId.Dispatch));
            Assert.AreEqual(frame + 1, Since(0).Last().Frame, "in the next frame");
            yield return NothingMorePlays("provisioning");

            bought = Sim.SuppliesBoughtTotal;
            int total = Audio.StartedTotal;
            MapTestRig.SendEmpty(Sim, starter);
            Assert.Greater(Sim.SuppliesBoughtTotal, bought, "setup: this dispatch bought its top-up");
            yield return MapTestRig.Frames();
            Assert.AreEqual("Dispatch", Names(Since(total)), "a dispatch that buys supplies plays dispatch only");
            Assert.AreEqual(1, N(SoundId.Provision));
            yield return NothingMorePlays("a dispatch");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator AnArrival_PlaysOnceInTheFrameItHappens_AndAPaidArrival_PlaysArrivalAndPaymentTogether()
        {
            yield return Open(MapTestRig.FleetGame(12, 1, 1));
            Audio.LogEvents = true;
            var paid = Sim.Ships[0];
            Assert.Greater(paid.Voyage.Payout, 0, "setup: the starter ship flies a paying job");
            // The second ship flies empty to the farthest system it can reach, so the two arrivals are well apart.
            var second = Sim.Ships[1];
            int home = second.DockedSystemId.Value;
            foreach (var far in Sim.Market.Galaxy.Systems.Where(s => s.Id != home).OrderByDescending(s => Sim.Market.Galaxy.DistanceMilliLy(home, s.Id)))
                if (Sim.Dispatch(new VoyagePlan(second.Id, far.Id), true).Succeeded) break;
            var empty = second.Voyage;
            Assert.IsNotNull(empty, "setup: the second ship was sent");
            Assert.AreEqual(0, empty.Payout);
            Assert.AreNotEqual(empty.ArrivalTick, paid.Voyage.ArrivalTick, "setup: two separate arrivals");
            yield return MapTestRig.Frames();
            Assert.AreEqual(1, N(SoundId.Dispatch));

            foreach (bool isPaid in new[] { empty.ArrivalTick > paid.Voyage.ArrivalTick, empty.ArrivalTick < paid.Voyage.ArrivalTick })
            {
                long arrival = Sim.Ships.Where(s => s.Voyage != null).Min(s => s.Voyage.ArrivalTick);
                // One real millisecond short of the arrival: nothing yet.
                int total = Audio.StartedTotal;
                yield return _rig.RealMs(arrival - Sim.CurrentTick - 1);
                Assert.AreEqual(total, Audio.StartedTotal, "one tick before the arrival nothing has played");

                int frame = Time.frameCount;
                _log.Clear();
                yield return _rig.RealMs(1);
                var played = Since(total);
                Assert.AreEqual(isPaid ? "Arrival, Payment" : "Arrival", Names(played), isPaid ? "a paid arrival plays arrival and payment together" : "an unpaid arrival plays arrival only");
                foreach (var p in played) Assert.AreEqual(frame + 1, p.Frame, p.Id + " played in the very frame the ship arrived");
                Assert.AreEqual(1f, played[0].Volume, 0.0001f);

                // Done when: a debug log line confirms the sound event fired at the right moment.
                var note = Sim.Notifications[Sim.Notifications.Count - 1];
                Assert.AreEqual(GameDate.FromTick(arrival), note.Date);
                Assert.AreEqual($"[sound] Events/arrival.wav · frame {frame + 1} · volume 1.00 · {note.Date} · {note.Text}", _log[0]);
                if (isPaid) StringAssert.StartsWith($"[sound] Events/payment.wav · frame {frame + 1} · volume 1.00 · {note.Date} · deliveries paid ", _log[1]);
                Assert.AreEqual(played.Count, _log.Count, "one log line per sound");
                TestContext.WriteLine(string.Join("\n", _log));
                yield return NothingMorePlays("an arrival");
            }
            Assert.AreEqual(2, N(SoundId.Arrival));
            Assert.AreEqual(1, N(SoundId.Payment));
            AssertOnlySoundLinesWereLogged();
        }

        [UnityTest]
        public IEnumerator ASlowOrdinaryFrame_ContainingAnArrival_StillPlaysTheArrivalSound()
        {
            yield return Open(GameTestTuning.NewGameWithSystems(12));
            var voyage = MapTestRig.SendEmpty(Sim, Sim.Ships[0].Id);
            yield return MapTestRig.Frames();
            int total = Audio.StartedTotal;

            // One ordinary frame that took as long as the whole voyage and ten real minutes more. Nobody declared a jump.
            long frameMs = voyage.ArrivalTick - Sim.CurrentTick + 600_000;
            int frame = Time.frameCount;
            yield return _rig.RealMs(frameMs);
            Assert.IsNull(Sim.Ships[0].Voyage, "the ship arrived inside that frame");
            Assert.AreEqual("Arrival", Names(Since(total)), $"a {frameMs / 1000} second frame is still an ordinary frame: the arrival is heard");
            Assert.AreEqual(frame + 1, Since(total)[0].Frame);
            Assert.AreEqual(0, N(SoundId.ReturnSummary), "and it is not treated as a time jump");
            Assert.AreEqual(0, _rig.Runner.Sounds.JumpsFinished);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator SeveralArrivalsInOneFrame_PlayOneArrivalSound()
        {
            yield return Open(MapTestRig.FleetGame(12, 8));
            int inFlight = Sim.Ships.Count(s => s.Voyage != null);
            Assert.AreEqual(8, inFlight);
            long last = Sim.Ships.Where(s => s.Voyage != null).Max(s => s.Voyage.ArrivalTick);
            int total = Audio.StartedTotal, dropped = Audio.DroppedSameFrame;
            yield return _rig.RealMs(last - Sim.CurrentTick);
            Assert.AreEqual(8, Sim.Notifications.Count(n => n.Kind == NotificationKind.Arrival), "eight ships arrived in that frame");
            Assert.AreEqual(1, N(SoundId.Arrival), "one arrival sound for the frame");
            Assert.AreEqual(7, Audio.DroppedSameFrame - dropped, "the other seven requests were dropped");
            Assert.LessOrEqual(Since(total).Count, 2, "at most arrival and payment: " + Names(Since(total)));
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>Fifty ships shuttling between the first yard and its neighbours on two-stop routes.</summary>
        private static Simulation ShuttleFleet(out long longestLeg)
        {
            var sim = MapTestRig.FleetGame(12, 0, 50);
            var galaxy = sim.Market.Galaxy;
            longestLeg = 0;
            int n = 0;
            foreach (var ship in sim.Ships.Skip(1).ToList())
            {
                int yard = ship.DockedSystemId.Value;
                var near = galaxy.Systems.Where(s => s.Id != yard).OrderBy(s => galaxy.DistanceMilliLy(yard, s.Id)).Select(s => s.Id).ToList();
                bool set = false;
                for (int k = 0; k < near.Count && !set; k++)
                {
                    int other = near[(n + k) % 3];
                    set = sim.SetRoute(ship.Id, new[] { RouteStop.Empty(yard), RouteStop.Empty(other) }).Succeeded && ship.Voyage != null;
                }
                Assert.IsTrue(set, $"{ship.Name} could not be put on a shuttle route: {ship.Route?.StatusText}");
                longestLeg = System.Math.Max(longestLeg, ship.Voyage.TransitTicks);
                n++;
            }
            return sim;
        }

        [UnityTest]
        public IEnumerator AThousandArrivals_InOneCatchUp_PlayNoBurstOfSounds_OnlyTheReturnSummaryOnce()
        {
            var sim = ShuttleFleet(out long longestLeg);
            yield return Open(sim);
            yield return MapTestRig.Frames();
            Audio.LogEvents = true;
            int total = Audio.StartedTotal;
            long deliveriesBefore = Sim.DeliveryTotals.Deliveries;
            long fromTick = Sim.CurrentTick;

            // The player was away for 21 legs of the slowest shuttle (at 1x one tick is one real millisecond).
            long awayMs = 21 * longestLeg;
            int frame = Time.frameCount;
            var summary = _rig.Runner.CatchUp(1_000_000, 1_000_000 + awayMs);
            long arrivals = Sim.DeliveryTotals.Deliveries - deliveriesBefore;
            Assert.GreaterOrEqual(arrivals, 1000, "setup: at least 1,000 arrivals were processed in the catch-up");
            Assert.AreEqual(arrivals, summary.DeliveryCount);
            Assert.AreEqual(fromTick + awayMs, Sim.CurrentTick);

            var played = Since(total);
            Assert.AreEqual("ReturnSummary", Names(played), "the whole catch-up made one sound: the return summary");
            Assert.AreEqual(frame, played[0].Frame);
            Assert.AreEqual(0, N(SoundId.Arrival), "no arrival sound for any of them");
            Assert.AreEqual(0, N(SoundId.Dispatch));
            Assert.AreEqual(0, N(SoundId.Payment));
            Assert.AreEqual(0, N(SoundId.Provision));
            Assert.AreEqual(0, Audio.DroppedSameFrame, "nothing was even asked for: the events were dropped, not squeezed into one frame");
            Assert.AreEqual(1, _log.Count, "one log line");
            StringAssert.StartsWith($"[sound] Events/return_summary.wav · frame {frame} · volume 1.00 · {Sim.CurrentDate} · time jump finished", _log[0]);

            // The frames after the catch-up do not replay anything either.
            yield return MapTestRig.Frames(5);
            Assert.AreEqual(1, Audio.StartedTotal - total, "nothing is replayed in the frames after: " + Names(Since(total)));
            Assert.AreEqual(fromTick + awayMs, Sim.CurrentTick, "and the time away is not counted twice");

            // Ordinary play then sounds normal again: the next arrival is heard.
            long next = Sim.Ships.Where(s => s.Voyage != null).Min(s => s.Voyage.ArrivalTick);
            yield return _rig.RealMs(next - Sim.CurrentTick);
            Assert.AreEqual(1, N(SoundId.Arrival), "the next arrival in ordinary play is heard");
            Assert.AreEqual(1, N(SoundId.ReturnSummary));
            TestContext.WriteLine($"{arrivals} arrivals in a catch-up of {awayMs} ms; sounds: {Names(played)}; log: {_log[0]}");
            AssertOnlySoundLinesWereLogged();
        }

        [UnityTest]
        public IEnumerator ACatchUpSpreadOverSeveralFrames_IsSilentThroughout_AndPlaysTheReturnSummaryOnceAtItsEnd()
        {
            var sim = ShuttleFleet(out long longestLeg);
            yield return Open(sim);
            yield return MapTestRig.Frames();
            int total = Audio.StartedTotal;
            long deliveriesBefore = Sim.DeliveryTotals.Deliveries;

            _rig.Runner.BeginTimeJump();
            Assert.IsTrue(_rig.Runner.Sounds.IsJumping);
            for (int slice = 0; slice < 6; slice++)
            {
                Sim.Advance(Sim.CurrentTick + longestLeg);
                long tick = Sim.CurrentTick;
                yield return _rig.RealMs(250);
                Assert.AreEqual(tick, Sim.CurrentTick, "while the jump is declared the frame clock does not move the game");
                Assert.AreEqual(total, Audio.StartedTotal, "slice " + slice + " made no sound");
            }
            Assert.Greater(Sim.DeliveryTotals.Deliveries - deliveriesBefore, 200, "setup: hundreds of arrivals happened in the slices");
            long end = Sim.CurrentTick;
            _rig.Runner.EndTimeJump();
            Assert.AreEqual("ReturnSummary", Names(Since(total)));
            yield return MapTestRig.Frames(5);
            Assert.AreEqual("ReturnSummary", Names(Since(total)), "once, and nothing after");
            Assert.AreEqual(end, Sim.CurrentTick, "the real time spent catching up is not counted again");
            Assert.AreEqual(1, _rig.Runner.Sounds.JumpsFinished);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ADebugSkip_IsSilent_AndPlaysTheReturnSummaryOnce_AndARefusedSkip_PlaysTheErrorSound()
        {
            var sim = DebugGame(12);
            CollectionAssert.IsEmpty(DebugFleet.Launch(sim, 6, 0, false));
            yield return Open(sim);
            yield return MapTestRig.Frames();
            int total = Audio.StartedTotal;
            int skip = 0;
            while (Sim.Ships.Any(s => s.Voyage != null))
            {
                skip++;
                long next = Sim.Ships.Where(s => s.Voyage != null).Min(s => s.Voyage.ArrivalTick);
                Assert.IsTrue(_rig.Runner.DebugSkipToNextArrival().Succeeded);
                Assert.AreEqual(next, Sim.CurrentTick);
                yield return MapTestRig.Frames(3);
                Assert.AreEqual(skip, N(SoundId.ReturnSummary), "one return summary per skip");
                Assert.AreEqual(skip, Audio.StartedTotal - total, "and nothing else: " + Names(Since(total)));
            }
            Assert.Greater(skip, 1);
            Assert.AreEqual(0, N(SoundId.Arrival));
            Assert.AreEqual(0, N(SoundId.Payment));
            Assert.AreEqual(6, Sim.Notifications.Count(n => n.Kind == NotificationKind.Arrival), "all six ships arrived in the skips");

            total = Audio.StartedTotal;
            var refused = _rig.Runner.DebugSkipToNextArrival();
            Assert.IsFalse(refused.Succeeded, "no ship is in flight now");
            yield return MapTestRig.Frames(3);
            Assert.AreEqual("Error", Names(Since(total)), "a refused skip plays the error sound and no return summary");
            Assert.IsFalse(_rig.Runner.Sounds.IsJumping);
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator WithoutDebugPermission_TheSkipIsRefused_AndARefusedCommand_PlaysTheErrorSound()
        {
            yield return Open(GameTestTuning.NewGameWithSystems(12));
            var voyage = MapTestRig.SendEmpty(Sim, Sim.Ships[0].Id);
            yield return MapTestRig.Frames();
            int total = Audio.StartedTotal;
            long tick = Sim.CurrentTick;
            Assert.IsFalse(_rig.Runner.DebugSkipToNextArrival().Succeeded);
            yield return MapTestRig.Frames();
            Assert.AreEqual(tick, Sim.CurrentTick, "no time jump in a game without debug permission");
            Assert.AreEqual("Error", Names(Since(total)));

            total = Audio.StartedTotal;
            var result = _rig.Runner.Command(s => s.Dispatch(new VoyagePlan(Sim.Ships[0].Id, voyage.OriginId), true));
            Assert.IsFalse(result.Succeeded, "a ship in flight cannot be dispatched");
            yield return MapTestRig.Frames();
            Assert.AreEqual("Error", Names(Since(total)), "a refused command plays the error sound");
            yield return NothingMorePlays("a refusal");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator BuyingAShip_FittingADrive_InSystemTransport_AndItsDailyIncome_EachPlayTheirSoundOnce()
        {
            yield return Open(MapTestRig.FleetGame(12, 0));
            var tuning = Sim.Market.Tuning;
            int yard = Sim.Market.Shipyards[0].SystemId;
            int total = Audio.StartedTotal;

            int id = -1;
            Assert.IsTrue(_rig.Runner.Command(s => s.BuyShip(yard, tuning.Start.StarterShipClass, out id)).Succeeded);
            yield return MapTestRig.Frames();
            Assert.AreEqual("PurchaseShip", Names(Since(total)), "a ship bought");
            yield return NothingMorePlays("buying a ship");

            total = Audio.StartedTotal;
            string drive = tuning.Drives.Select(d => d.Id).First(d => d != Sim.FindShip(id).DriveId);
            Assert.IsTrue(_rig.Runner.Command(s => s.BuyDrive(id, drive)).Succeeded);
            yield return MapTestRig.Frames();
            Assert.AreEqual("DriveInstall", Names(Since(total)), "a drive fitted");

            total = Audio.StartedTotal;
            Assert.IsTrue(_rig.Runner.Command(s => s.SetInSystemTransport(id, true)).Succeeded);
            yield return MapTestRig.Frames();
            Assert.AreEqual("ToggleOn", Names(Since(total)), "in-system transport on");

            total = Audio.StartedTotal;
            yield return _rig.RealMs(GameTime.TicksPerDay - 1);
            Assert.AreEqual(total, Audio.StartedTotal, "one tick short of a full day: no income yet");
            for (int day = 1; day <= 3; day++)
            {
                yield return _rig.RealMs(day == 1 ? 1 : GameTime.TicksPerDay);
                Assert.AreEqual(day, N(SoundId.InSystemIncome), "one income sound per paid day");
                yield return NothingMorePlays("a day's income");
            }
            Assert.AreEqual(3, Audio.StartedTotal - total);

            total = Audio.StartedTotal;
            Assert.IsTrue(_rig.Runner.Command(s => s.SetInSystemTransport(id, false)).Succeeded);
            yield return MapTestRig.Frames();
            Assert.AreEqual("ToggleOff", Names(Since(total)), "in-system transport off");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator ARouteThatBlocks_PlaysBlocked_AndAShipThatStartsWaiting_PlaysWaiting()
        {
            yield return Open(MapTestRig.FleetGame(12, 0, 12));
            var galaxy = Sim.Market.Galaxy;
            int blockedShip = Sim.Ships[1].Id;
            int yard = Sim.FindShip(blockedShip).DockedSystemId.Value;
            var near = galaxy.Systems.Where(s => s.Id != yard).OrderBy(s => galaxy.DistanceMilliLy(yard, s.Id)).Select(s => s.Id).ToList();
            int berths = Sim.Market.Tuning.Ships.First(c => c.Id == Sim.FindShip(blockedShip).ClassId).Berths;

            // A stop asking for more passengers than the ship has berths can never be met: the route is blocked at once.
            int total = Audio.StartedTotal;
            Assert.IsTrue(_rig.Runner.Command(s => s.SetRoute(blockedShip, new[] { new RouteStop(yard, carryPassengers: true, minPassengers: berths + 1), RouteStop.Empty(near[0]) })).Succeeded);
            Assert.AreEqual(RouteStatus.Blocked, Sim.FindShip(blockedShip).Route.Status, "setup: blocked");
            Assert.AreEqual(1, Sim.Notifications.Count(n => n.Kind == NotificationKind.RouteBlocked));
            yield return MapTestRig.Frames();
            Assert.AreEqual("Blocked", Names(Since(total)), "a blocked route plays the blocked sound, not the arrival sound");
            yield return NothingMorePlays("a route blocking");

            // Ships that each want every berth full before leaving: the first ones take the passengers waiting at the yard
            // and go; the first ship to find too few left starts waiting.
            string waitText = null;
            int waitingShip = -1;
            foreach (var ship in Sim.Ships.Skip(2).ToList())
            {
                int seats = Sim.Market.Tuning.Ships.First(c => c.Id == ship.ClassId).Berths;
                if (seats == 0) continue;
                yield return MapTestRig.Frames();
                total = Audio.StartedTotal;
                Assert.IsTrue(Sim.SetRoute(ship.Id, new[] { new RouteStop(yard, carryPassengers: true, minPassengers: seats), RouteStop.Empty(near[0]) }).Succeeded);
                if (ship.Route.Status != RouteStatus.Waiting) continue;
                waitText = ship.Route.StatusText;
                waitingShip = ship.Id;
                break;
            }
            Assert.IsNotNull(waitText, "setup: no ship in this fixture game had to wait");
            yield return MapTestRig.Frames();
            var played = Since(total);
            Assert.AreEqual("Waiting", Names(played), "a ship that starts waiting plays the waiting sound, once");
            Assert.IsNull(Sim.FindShip(waitingShip).Voyage);
            yield return NothingMorePlays("a ship starting to wait");
            TestContext.WriteLine(waitText + " · " + Names(played));
        }

        [UnityTest]
        public IEnumerator SelectFollowAndPanels_PlayTheirSounds_AndTheMapMusicAndCloseUpAmbienceLoop()
        {
            yield return Open(GameTestTuning.NewGameWithSystems(12));
            Assert.AreEqual(SoundId.MusicMap, Audio.Music, "the map music loop starts with the map");
            Assert.IsTrue(Audio.IsMusicAudible);
            Assert.IsNull(Audio.Ambience);
            Assert.AreEqual(0, Audio.StartedTotal);

            // The system lowest on the screen, well clear of the bar.
            int system = Sim.Market.Galaxy.Systems.Where(s => s.Id != Sim.Ships[0].DockedSystemId).OrderBy(s => s.YMilliLy).First().Id;
            int total = Audio.StartedTotal;
            yield return _rig.Click(_rig.ScreenOf(GalaxyMapView.WorldPosition(Sim.Market.Galaxy.Systems[system])));
            Assert.AreEqual(system, Map.SelectedSystemId);
            Assert.AreEqual("Select", Names(Since(total)), "selecting a system");
            yield return NothingMorePlays("selecting");

            total = Audio.StartedTotal;
            Map.SelectShip(Sim.Ships[0].Id);
            Map.ToggleFollow();
            yield return MapTestRig.Frames();
            Map.ToggleFollow();
            yield return MapTestRig.Frames();
            Assert.AreEqual("ToggleOn, ToggleOff", Names(Since(total)), "follow on, then off");

            total = Audio.StartedTotal;
            Map.OpenCloseUp();
            yield return MapTestRig.Frames();
            Assert.AreEqual("PanelOpen", Names(Since(total)), "the close-up opening");
            Assert.AreEqual(SoundId.ShipCloseUpDocked, Audio.Ambience, "a docked ship: the station ambience loop");
            Assert.IsTrue(Audio.IsAmbienceAudible);
            Assert.AreEqual(SoundId.MusicMap, Audio.Music, "the music keeps playing");

            MapTestRig.SendEmpty(Sim, Sim.Ships[0].Id);
            yield return MapTestRig.Frames();
            Assert.AreEqual(SoundId.ShipCloseUpDrive, Audio.Ambience, "in flight: the drive hum loop");
            Assert.IsTrue(Audio.IsAmbienceAudible);
            yield return NothingMorePlays("the close-up");

            total = Audio.StartedTotal;
            Map.CloseCloseUp();
            yield return MapTestRig.Frames();
            Assert.AreEqual("PanelClose", Names(Since(total)), "the close-up closing");
            Assert.IsNull(Audio.Ambience, "the ambience stops with the close-up");
            Assert.IsFalse(Audio.IsAmbienceAudible);

            total = Audio.StartedTotal;
            Map.Bar.ToggleNotifications();
            yield return MapTestRig.Frames();
            Map.Bar.ToggleSettings();
            yield return MapTestRig.Frames();
            Map.Bar.ToggleSettings();
            yield return MapTestRig.Frames();
            Assert.AreEqual("PanelOpen, PanelOpen, PanelClose", Names(Since(total)), "the list opening, the settings replacing it, the settings closing");
            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator TheSameSound_AskedForTwiceInOneFrame_PlaysOnce_AndAgainInALaterFrame()
        {
            yield return Open(GameTestTuning.NewGameWithSystems(12));
            Assert.IsTrue(Audio.PlayNow(SoundId.Click, "first"));
            Assert.IsFalse(Audio.PlayNow(SoundId.Click, "second, same frame"));
            Assert.IsFalse(Audio.PlayNow(SoundId.Click, "third, same frame"));
            Assert.IsTrue(Audio.PlayNow(SoundId.Select, "a different sound in the same frame"));
            Assert.AreEqual(1, N(SoundId.Click));
            Assert.AreEqual(2, Audio.DroppedSameFrame);
            yield return null;
            Assert.IsTrue(Audio.PlayNow(SoundId.Click, "next frame"));
            Assert.AreEqual(2, N(SoundId.Click));
            Assert.IsFalse(Audio.PlayNow(SoundId.MusicMap, "a loop is not a one-off effect"));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
