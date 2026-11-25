using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Routes;
using SpaceTrade.Sim.Ships;
using SpaceTrade.Sim.Voyages;
using SpaceTrade.Sim.World;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;
using static SpaceTrade.Sim.Tests.RouteTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>Route loading, waiting, blocking, retries, pause/resume/clear, and refusals (spec 8.2 to 8.5).</summary>
    public class RouteBehaviourTests
    {
        // ---- Loading (owner's answer 1, decision 7a) ----

        [Test]
        public void PassengersBoardFirst_ThenFreightInListedOrder()
        {
            var sim = RefineryToFactory(1_000_000);
            SetSupply(sim, Rho, "metals", 10);
            Ok(sim.SetRoute(CourierId, new[] { new RouteStop(Rho, new[] { "metals", "chemicals" }, carryPassengers: true), RouteStop.Empty(Phi) }));
            var ship = Courier(sim);
            Assert.AreEqual(4, ship.Voyage.Passengers, "Berths 4, 6 waiting");
            Assert.AreEqual(10, Tonnes(ship, "metals"), "Metals first: all 10 t available");
            Assert.AreEqual(15, Tonnes(ship, "chemicals"), "Then chemicals: the 15 t still fitting beside 6 people's food and water");
        }

        [Test]
        public void ListedOrderDecides_WhichCommodityFillsTheHold()
        {
            var sim = RefineryToFactory(1_000_000);
            SetSupply(sim, Rho, "metals", 10);
            Ok(sim.SetRoute(CourierId, new[] { new RouteStop(Rho, new[] { "chemicals", "metals" }, carryPassengers: true), RouteStop.Empty(Phi) }));
            Assert.AreEqual(25, Tonnes(Courier(sim), "chemicals"));
            Assert.AreEqual(0, Tonnes(Courier(sim), "metals"));
        }

        [Test]
        public void PassengersNeverCrowdOutTheFreightMinimum()
        {
            var plain = NewSim(1_000_000);
            Ok(plain.SetRoute(CourierId, OreShuttle(passengers: true)));
            Assert.AreEqual(4, Courier(plain).Voyage.Passengers, "No minimum: every berth filled");
            Assert.AreEqual(23, Tonnes(Courier(plain), "ore"));

            var sim = NewSim(1_000_000);
            Ok(sim.SetRoute(CourierId, OreShuttle(minFreight: 25, passengers: true)));
            Assert.AreEqual(2, Courier(sim).Voyage.Passengers, "3 passengers would leave room for only 24.6 t");
            Assert.AreEqual(25, Tonnes(Courier(sim), "ore"));
        }

        // ---- Same-tick departure (owner's answer 3) ----

        [Test]
        public void RouteShip_LeavesInTheTickItArrives()
        {
            var sim = NewSim(1_000_000);
            Ok(sim.SetRoute(CourierId, OreShuttle()));
            sim.Advance(Leg45);
            Assert.AreEqual(Leg45, sim.Receipts[0].ArrivalTick);
            Assert.AreEqual(Leg45, Courier(sim).Voyage.DepartureTick, "Arrived in phase 1, left in phase 4 of the same tick");
            Assert.AreEqual(Alpha, Courier(sim).Voyage.DestinationId);
            Assert.AreEqual(0, Courier(sim).Voyage.Freight.Count, "Beta's leg is an empty repositioning leg");
        }

        // ---- Minimums and waiting (owner's answer 2) ----

        [Test]
        public void WaitsForItsMinimum_AndLeavesWhenSupplyArrives()
        {
            var sim = NewSim(1_000_000);
            SetSupply(sim, Alpha, "ore", 5);
            Ok(sim.SetRoute(CourierId, OreShuttle(minFreight: 20)));
            Assert.AreEqual(RouteStatus.Waiting, Route(sim).Status);
            Assert.AreEqual("Waiting for 15 more tonnes of ore", Route(sim).StatusText);

            sim.Advance(30 * Day - 1);
            Assert.AreEqual("Waiting for 15 more tonnes of ore", Route(sim).StatusText);
            sim.Advance(30 * Day);
            Assert.AreEqual("Waiting for 5 more tonnes of ore", Route(sim).StatusText, "Rechecked at the day-30 replenishment");
            sim.Advance(60 * Day - 1);
            Assert.IsFalse(Courier(sim).IsInTransit);
            sim.Advance(60 * Day);
            Assert.IsTrue(Courier(sim).IsInTransit);
            Assert.AreEqual(60 * Day, Courier(sim).Voyage.DepartureTick);
            Assert.AreEqual(25, Tonnes(Courier(sim), "ore"));
            Assert.AreEqual(RouteStatus.Running, Route(sim).Status);
            Assert.IsNull(Route(sim).StatusText);
        }

        [Test]
        public void WaitingText_ListsEveryUnmetMinimum()
        {
            var sim = NewSim(1_000_000);
            SetSupply(sim, Alpha, "ore", 5);
            SetWaiting(sim, Alpha, Beta, 0);
            Ok(sim.SetRoute(CourierId, OreShuttle(minFreight: 20, passengers: true, minPassengers: 3)));
            Assert.AreEqual("Waiting for 15 more tonnes of ore and 3 more passengers", Route(sim).StatusText);

            var one = NewSim(1_000_000);
            SetSupply(one, Alpha, "ore", 19);
            SetWaiting(one, Alpha, Beta, 2);
            Ok(one.SetRoute(CourierId, OreShuttle(minFreight: 20, passengers: true, minPassengers: 3)));
            Assert.AreEqual("Waiting for 1 more tonne of ore and 1 more passenger", Route(one).StatusText);

            var pax = NewSim(1_000_000);
            SetWaiting(pax, Alpha, Beta, 1);
            Ok(pax.SetRoute(CourierId, OreShuttle(minFreight: 20, passengers: true, minPassengers: 3)));
            Assert.AreEqual("Waiting for 2 more passengers", Route(pax).StatusText, "A met minimum is not listed");

            var two = RefineryToFactory(1_000_000);
            SetSupply(two, Rho, "metals", 3);
            SetSupply(two, Rho, "chemicals", 5);
            Ok(two.SetRoute(CourierId, new[] { new RouteStop(Rho, new[] { "metals", "chemicals" }, minFreightTonnes: 20), RouteStop.Empty(Phi) }));
            Assert.AreEqual("Waiting for 12 more tonnes of freight (metals or chemicals)", Route(two).StatusText);
        }

        [TestCase(45, 90)]
        [TestCase(0, 60)]
        [TestCase(1, 60)]
        public void WaitingRechecks_HappenNoMoreOftenThanTheMinimumInterval(int intervalDays, int departureDay)
        {
            var tuning = FlatTuning();
            tuning.Automation.RecheckMinIntervalDays = intervalDays;
            var sim = NewSim(1_000_000, tuning);
            SetSupply(sim, Alpha, "ore", 5);
            Ok(sim.SetRoute(CourierId, OreShuttle(minFreight: 20)));
            sim.Advance(departureDay * Day - 1);
            Assert.IsFalse(Courier(sim).IsInTransit);
            sim.Advance(departureDay * Day);
            Assert.AreEqual(departureDay * Day, Courier(sim).Voyage?.DepartureTick);
            if (intervalDays == 45) Assert.AreEqual(27, Tonnes(Courier(sim), "ore"), "35 t by day 90; 27 t fit");
        }

        [Test]
        public void TooSoonReplenishment_IsCheckedAtTheEarliestAllowedTick()
        {
            var tuning = FlatTuning();
            tuning.Automation.RecheckMinIntervalDays = 45;
            var sim = NewSim(1_000_000, tuning);
            SetSupply(sim, Alpha, "ore", 5);
            Ok(sim.SetRoute(CourierId, OreShuttle(minFreight: 20)));
            sim.Advance(30 * Day);
            Assert.AreEqual(45 * Day, Route(sim).PendingCheckTick, "Day-30 replenishment came too soon; checked on day 45");
            Assert.AreEqual("Waiting for 15 more tonnes of ore", Route(sim).StatusText);
            sim.Advance(45 * Day);
            Assert.AreEqual("Waiting for 5 more tonnes of ore", Route(sim).StatusText);
            Assert.AreEqual(45 * Day, Route(sim).LastCheckTick);
        }

        // ---- Blocked (spec 8.4, decision 2b) ----

        [Test]
        public void OutOfRangeLeg_IsBlocked_WithOneNotification_AndWaitsForThePlayer()
        {
            var sim = NewSim(1_000_000, distanceMilliLy: 20_000);
            Ok(sim.SetRoute(CourierId, OreShuttle()));
            Assert.AreEqual(RouteStatus.Blocked, Route(sim).Status);
            StringAssert.Contains("out of range", Route(sim).StatusText);
            Assert.IsFalse(Route(sim).BlockedForTopUp);
            Assert.AreEqual(1, BlockedNotifications(sim));
            StringAssert.StartsWith("Test Courier is blocked at Alpha. Test Courier cannot fly from Alpha to Beta (20 ly)", sim.Notifications.Last().Text);

            sim.Advance(2 * Year);
            Assert.AreEqual(0L, Route(sim).LastCheckTick, "Never rechecked on its own");
            Assert.IsNull(Route(sim).PendingCheckTick);
            Ok(sim.ResumeRoute(CourierId));
            Assert.AreEqual(RouteStatus.Blocked, Route(sim).Status);
            Assert.AreEqual(2 * Year, Route(sim).LastCheckTick, "Resume retried at once");
            Assert.AreEqual(1, BlockedNotifications(sim), "A failed retry raises no new notification");
        }

        [Test]
        public void MinimumThatCanNeverBeMet_IsBlocked()
        {
            var berths = NewSim(1_000_000);
            Ok(berths.SetRoute(CourierId, OreShuttle(passengers: true, minPassengers: 5)));
            Assert.AreEqual("Stop at Alpha needs at least 5 passengers to leave, but Test Courier has 4 berths.", Route(berths).StatusText);

            var caps = NewSim(1_000_000);
            Ok(caps.SetRoute(CourierId, OreShuttle(minFreight: 61)));
            Assert.AreEqual("Stop at Alpha needs at least 61 t of freight to leave, but supply and demand can never hold more than 60 t of ore from Alpha to Beta.", Route(caps).StatusText);

            var hold = NewSim(1_000_000);
            Ok(hold.SetRoute(CourierId, OreShuttle(minFreight: 28)));
            Assert.AreEqual(RouteStatus.Blocked, Route(hold).Status);
            StringAssert.Contains("does not fit Test Courier's hold and tank", Route(hold).StatusText);
            foreach (var s in new[] { berths, caps, hold }) Assert.AreEqual(1, BlockedNotifications(s));
        }

        /// <summary>
        /// Ships 1 and 2 at Alpha. Ship 2 is sent to Beta with 25 t of ore (paid 105,000 on arrival at 3 years); then
        /// credits are set to <paramref name="creditsAfter"/> and ship 1 is put on the ore shuttle.
        /// </summary>
        private static Simulation BlockedBesideAPayingShip(long creditsAfter, bool autoProvision)
        {
            var sim = NewSim(1_000_000);
            sim.AddShip("Payer", "courier", sim.Market.Tuning.Start.StarterDrive, Alpha);
            Ok(sim.Dispatch(new VoyagePlan(2, Beta, new[] { new FreightLine("ore", 25) }), true));
            sim.SetCredits(creditsAfter);
            Ok(sim.SetRoute(CourierId, OreShuttle(autoProvision: autoProvision)));
            Assert.AreEqual(RouteStatus.Blocked, Route(sim).Status);
            return sim;
        }

        [Test]
        public void BlockedForTheTopUp_LeavesByItself_WhenAnotherShipsPayoutArrives()
        {
            var sim = BlockedBesideAPayingShip(1_000, autoProvision: true);
            Assert.IsTrue(Route(sim).BlockedForTopUp);
            StringAssert.StartsWith("Topping up supplies costs", Route(sim).StatusText);
            Assert.AreEqual(1, BlockedNotifications(sim));

            sim.Advance(Leg45 - 1);
            Assert.IsFalse(Courier(sim).IsInTransit);
            Assert.AreEqual(0L, Route(sim).LastCheckTick, "Market replenishments do not recheck a blocked ship");
            long boughtBefore = sim.SuppliesBoughtTotal;
            sim.Advance(Leg45);
            long topUp = sim.SuppliesBoughtTotal - boughtBefore;
            Assert.Greater(topUp, 1_000);
            Assert.AreEqual(105_000 + 1_000 - topUp, sim.Credits, "Paid 105,000, then bought the top-up");
            Assert.AreEqual(Leg45, Courier(sim).Voyage.DepartureTick, "Rechecked in phase 4 of the payout's tick");
            Assert.AreEqual(RouteStatus.Running, Route(sim).Status);
            Assert.AreEqual(1, BlockedNotifications(sim));
        }

        [Test]
        public void BlockedForAnyOtherReason_DoesNotLeave_WhenCreditsArrive_AndResumeAfterProvisionIsTheRetry()
        {
            var sim = BlockedBesideAPayingShip(1_000_000, autoProvision: false);
            Assert.IsFalse(Route(sim).BlockedForTopUp);
            StringAssert.StartsWith("Test Courier is short of", Route(sim).StatusText);

            sim.Advance(Leg45 + 30 * Day);
            Assert.IsFalse(Courier(sim).IsInTransit, "The payout at 3 years did not release it");
            Assert.AreEqual(0L, Route(sim).LastCheckTick);

            Ok(sim.ResumeRoute(CourierId));
            Assert.AreEqual(RouteStatus.Blocked, Route(sim).Status, "Still short of supplies");
            Assert.AreEqual(1, BlockedNotifications(sim));

            Ok(sim.Provision(new VoyagePlan(CourierId, Beta, new[] { new FreightLine("ore", 27) })));
            long bought = sim.SuppliesBoughtTotal;
            Ok(sim.ResumeRoute(CourierId));
            Assert.IsTrue(Courier(sim).IsInTransit, "Provision, then the plain retry");
            Assert.AreEqual(27, Tonnes(Courier(sim), "ore"));
            Assert.AreEqual(bought, sim.SuppliesBoughtTotal, "Automatic provisioning off: nothing bought at departure");
        }

        [TestCase(10, 30)]
        [TestCase(1, 35)]
        public void TopUpRechecks_FromDailyIncome_AreLimitedByTheInterval(int intervalDays, int lastCheckDay)
        {
            var tuning = FlatTuning();
            tuning.Automation.RecheckMinIntervalDays = intervalDays;
            var sim = NewSim(1_000_000, tuning);
            sim.AddShip("Earner", "courier", tuning.Start.StarterDrive, Alpha);
            sim.SetCredits(1_000);
            Ok(sim.SetRoute(CourierId, OreShuttle()));
            Assert.IsTrue(Route(sim).BlockedForTopUp);
            Ok(sim.SetInSystemTransport(2, true));

            sim.Advance(35 * Day);
            Assert.AreEqual(1_000 + 35 * 17, sim.Credits);
            Assert.AreEqual(lastCheckDay * Day, Route(sim).LastCheckTick);
            Assert.AreEqual(RouteStatus.Blocked, Route(sim).Status);
            Assert.AreEqual(1, BlockedNotifications(sim), "One notification, not one per failed retry");
        }

        // ---- A failed check changes nothing else ----

        [Test]
        public void FailedWaitingCheck_ChangesOnlyTheStatusLine()
        {
            var sim = NewSim(1_000_000);
            SetSupply(sim, Alpha, "ore", 5);
            Ok(sim.SetRoute(CourierId, OreShuttle(minFreight: 20)));
            sim.Advance(10 * Day);
            SetSupply(sim, Alpha, "ore", 8);
            var before = Lines(sim);
            sim.RunDepartureCheck(CourierId);
            var (added, removed) = Diff(before, Lines(sim));
            Assert.AreEqual(1, added.Count);
            Assert.AreEqual(1, removed.Count);
            StringAssert.StartsWith("routestatus ship=1 status=Waiting", added[0]);
            StringAssert.Contains("text=Waiting for 12 more tonnes of ore", added[0]);
            StringAssert.StartsWith("routestatus ship=1", removed[0]);
        }

        [TestCase(1_000, true)]
        [TestCase(1_000_000, false)]
        public void FailedBlockedCheck_ChangesOnlyTheStatusLine_AndAddsOneNotification(long credits, bool lowCredits)
        {
            var sim = lowCredits ? NewSim(credits) : NewSim(credits, distanceMilliLy: 20_000);
            Ok(sim.SetRoute(CourierId, OreShuttle()));
            sim.Advance(10 * Day);
            Route(sim).ClearStatus();
            var before = Lines(sim);
            sim.RunDepartureCheck(CourierId);
            var (added, removed) = Diff(before, Lines(sim));
            Assert.AreEqual(2, added.Count, string.Join("\n", added));
            StringAssert.StartsWith("routestatus ship=1 status=Blocked", added[0]);
            StringAssert.StartsWith($"notification {10 * Day} RouteBlocked ship=1", added[1]);
            Assert.AreEqual(1, removed.Count);
            StringAssert.StartsWith("routestatus ship=1 status=Running", removed[0]);

            before = Lines(sim);
            sim.RunDepartureCheck(CourierId);
            Assert.AreEqual(string.Join("\n", before), string.Join("\n", Lines(sim)), "A second failed check at the same tick changes nothing");
        }

        // ---- Flights are never altered ----

        private static string VoyageLine(Simulation sim) => Courier(sim).Voyage.DescribeState();

        [Test]
        public void ChangingPausingOrClearing_NeverAltersAVoyageInFlight()
        {
            var twin = NewSim(1_000_000);
            var sim = NewSim(1_000_000);
            var plan = new VoyagePlan(CourierId, Beta, new[] { new FreightLine("ore", 20) });
            Ok(twin.Dispatch(plan, true));
            Ok(sim.Dispatch(plan, true));
            string line = VoyageLine(sim);

            var actions = new System.Action[]
            {
                () => Ok(sim.SetRoute(CourierId, OreShuttle())),
                () => Ok(sim.SetRoute(CourierId, OreShuttle(minFreight: 10, passengers: true))),
                () => Ok(sim.PauseRoute(CourierId)),
                () => Ok(sim.ResumeRoute(CourierId)),
                () => Ok(sim.SetRoute(CourierId, new[] { RouteStop.Empty(Beta), RouteStop.Empty(Alpha) })),
                () => Ok(sim.ClearRoute(CourierId)),
            };
            for (int i = 0; i < actions.Length; i++)
            {
                sim.Advance((i + 1) * 100 * Day);
                actions[i]();
                Assert.AreEqual(line, VoyageLine(sim), $"after action {i + 1}");
            }
            sim.Advance(Leg45);
            twin.Advance(Leg45);
            Assert.AreEqual(twin.Receipts[0].DescribeState(), sim.Receipts[0].DescribeState());
            Assert.AreEqual(twin.Credits, sim.Credits);
        }

        [Test]
        public void ChangingPausingOrClearing_AnAutomatedVoyage_LeavesItUnchanged()
        {
            foreach (var change in new System.Func<Simulation, CommandResult>[]
                     {
                         s => s.SetRoute(CourierId, new[] { RouteStop.Empty(Beta), RouteStop.Empty(Alpha) }),
                         s => s.PauseRoute(CourierId),
                         s => s.ClearRoute(CourierId),
                     })
            {
                var sim = NewSim(1_000_000);
                Ok(sim.SetRoute(CourierId, OreShuttle(passengers: true)));
                string line = VoyageLine(sim);
                sim.Advance(Year);
                Ok(change(sim));
                Assert.AreEqual(line, VoyageLine(sim));
                sim.Advance(Leg45 - 1);
                Assert.AreEqual(line, VoyageLine(sim));
                sim.Advance(Leg45);
                var r = sim.Receipts.Single();
                Assert.AreEqual(0, r.DepartureTick);
                Assert.AreEqual(Leg45, r.ArrivalTick);
                Assert.AreEqual(23 * 4_200 + 4 * 28_000, r.GrossRevenue, "Paid at the terms locked at departure");
            }
        }

        // ---- In-system transport and manual dispatch (decisions 5a, 6a) ----

        [Test]
        public void ShipOnAnActiveRoute_CannotGoOnInSystemTransport_AndTheRefusalChangesNothing()
        {
            var docked = NewSim(1_000_000);
            SetSupply(docked, Alpha, "ore", 5);
            Ok(docked.SetRoute(CourierId, OreShuttle(minFreight: 20)));
            var before = StateBytes(docked);
            var r = docked.SetInSystemTransport(CourierId, true);
            Assert.IsFalse(r.Succeeded);
            Assert.AreEqual("Test Courier is on an automated route. Pause or clear its route to use in-system transport.", r.Reason);
            CollectionAssert.AreEqual(before, StateBytes(docked));

            var flying = NewSim(1_000_000);
            Ok(flying.SetRoute(CourierId, OreShuttle()));
            before = StateBytes(flying);
            Assert.IsFalse(flying.SetInSystemTransport(CourierId, true).Succeeded);
            CollectionAssert.AreEqual(before, StateBytes(flying));

            Ok(docked.PauseRoute(CourierId));
            Ok(docked.SetInSystemTransport(CourierId, true));
            before = StateBytes(docked);
            r = docked.ResumeRoute(CourierId);
            Assert.AreEqual("Test Courier is on in-system transport at Alpha. Turn off in-system transport before resuming its route.", r.Reason);
            CollectionAssert.AreEqual(before, StateBytes(docked));
            r = docked.SetRoute(CourierId, OreShuttle());
            StringAssert.StartsWith("Test Courier is on in-system transport at Alpha. Turn off in-system transport before giving it a route.", r.Reason);
            CollectionAssert.AreEqual(before, StateBytes(docked));
            Ok(docked.SetInSystemTransport(CourierId, false));
            Ok(docked.ResumeRoute(CourierId));
        }

        [Test]
        public void ManualDispatch_IsRefusedOnAnActiveRoute_AndAllowedWhilePaused()
        {
            var sim = NewSim(1_000_000);
            SetSupply(sim, Alpha, "ore", 5);
            Ok(sim.SetRoute(CourierId, OreShuttle(minFreight: 20)));
            var before = StateBytes(sim);
            var r = sim.Dispatch(Plan(5, 0), true);
            Assert.AreEqual("Test Courier is on an automated route. Pause or clear its route to dispatch it by hand.", r.Reason);
            CollectionAssert.AreEqual(before, StateBytes(sim));
            Assert.IsTrue(sim.Quote(Plan(5, 0)).IsFeasible, "Quote still works");
            Ok(sim.Provision(Plan(5, 0)));
            Ok(sim.PauseRoute(CourierId));
            Ok(sim.Dispatch(Plan(5, 0), true));
        }

        // ---- Pause, resume, clear (decision 1a) ----

        [Test]
        public void PauseInFlight_FinishesTheVoyage_ThenStaysDocked_ResumeContinues()
        {
            var sim = NewSim(1_000_000);
            Ok(sim.SetRoute(CourierId, OreShuttle()));
            sim.Advance(Year);
            Ok(sim.PauseRoute(CourierId));
            sim.Advance(Leg45 + Year);
            Assert.IsFalse(Courier(sim).IsInTransit);
            Assert.AreEqual(Beta, Courier(sim).DockedSystemId);
            Assert.AreEqual(1, sim.Receipts.Count);
            Assert.AreEqual(1, Route(sim).CurrentStopIndex, "Remembers it is at stop 2, Beta");
            StringAssert.DoesNotContain("RouteCheckEvent", sim.DescribeState());

            Ok(sim.ResumeRoute(CourierId));
            Assert.AreEqual(Alpha, Courier(sim).Voyage.DestinationId);
            Assert.AreEqual(Leg45 + Year, Courier(sim).Voyage.DepartureTick, "Resume checks at once");
        }

        [Test]
        public void PauseClearsWaiting_AndNoRechecksHappenWhilePaused()
        {
            var sim = NewSim(1_000_000);
            SetSupply(sim, Alpha, "ore", 5);
            Ok(sim.SetRoute(CourierId, OreShuttle(minFreight: 20)));
            Ok(sim.PauseRoute(CourierId));
            Assert.AreEqual(RouteStatus.Running, Route(sim).Status);
            Assert.IsNull(Route(sim).StatusText);
            sim.Advance(Year);
            Assert.IsFalse(Courier(sim).IsInTransit);
            Ok(sim.ResumeRoute(CourierId));
            Assert.AreEqual(Year, Courier(sim).Voyage.DepartureTick);
        }

        [Test]
        public void ClearInFlight_FinishesTheVoyage_AndTheShipStaysDocked()
        {
            var sim = NewSim(1_000_000);
            Ok(sim.SetRoute(CourierId, OreShuttle()));
            sim.Advance(Year);
            Ok(sim.ClearRoute(CourierId));
            Assert.IsNull(Courier(sim).Route);
            sim.Advance(2 * Leg45);
            Assert.AreEqual(Beta, Courier(sim).DockedSystemId);
            Assert.AreEqual(1, sim.Receipts.Count);
            Ok(sim.ClearRoute(CourierId));
            Assert.IsFalse(sim.PauseRoute(CourierId).Succeeded);
            Assert.IsFalse(sim.ResumeRoute(CourierId).Succeeded);
        }

        /// <summary>Alpha, Beta, Alpha, Gamma on the line map: Alpha appears twice.</summary>
        private static List<RouteStop> AlphaTwice() => new List<RouteStop>
        {
            new RouteStop(Alpha, new[] { "ore" }),
            RouteStop.Empty(Beta),
            RouteStop.Empty(Alpha),
            new RouteStop(Gamma, new[] { "food" }),
        };

        [Test]
        public void Resume_PicksTheNextMatchingStopAfterTheRememberedPlace()
        {
            var sim = NewThreeSystemSim(1_000_000);
            Ok(sim.SetRoute(CourierId, AlphaTwice()));
            Assert.AreEqual(Beta, Courier(sim).Voyage.DestinationId);
            Ok(sim.PauseRoute(CourierId));
            sim.Advance(Leg45);
            Assert.AreEqual(1, Route(sim).CurrentStopIndex);

            Ok(sim.Dispatch(new VoyagePlan(CourierId, Alpha), true));
            sim.Advance(2 * Leg45);
            Assert.AreEqual(Alpha, Courier(sim).DockedSystemId);
            Ok(sim.ResumeRoute(CourierId));
            Assert.AreEqual(Gamma, Courier(sim).Voyage.DestinationId, "Remembered stop 2 (Beta); the next Alpha after it is stop 3, which goes to Gamma");
            Assert.AreEqual(3, Route(sim).CurrentStopIndex);
        }

        [Test]
        public void Resume_WrapsRoundTheLoop_WhenTheNextMatchIsBeforeTheRememberedPlace()
        {
            var sim = NewThreeSystemSim(1_000_000);
            Ok(sim.SetRoute(CourierId, AlphaTwice()));
            sim.Advance(2 * Leg45);
            Assert.AreEqual(Gamma, Courier(sim).Voyage.DestinationId, "Alpha to Beta, Beta to Alpha, then Alpha to Gamma");
            Ok(sim.PauseRoute(CourierId));
            sim.Advance(sim.FindShip(CourierId).Voyage.ArrivalTick);
            Assert.AreEqual(3, Route(sim).CurrentStopIndex);

            Ok(sim.Dispatch(new VoyagePlan(CourierId, Alpha), true));
            sim.Advance(Courier(sim).Voyage.ArrivalTick);
            Ok(sim.ResumeRoute(CourierId));
            Assert.AreEqual(Beta, Courier(sim).Voyage.DestinationId, "Remembered stop 4 (Gamma); the next Alpha after it, round the loop, is stop 1");
        }

        [Test]
        public void Resume_AtTheRememberedStop_ContinuesFromIt()
        {
            var sim = NewThreeSystemSim(1_000_000);
            SetSupply(sim, Alpha, "ore", 0);
            Ok(sim.SetRoute(CourierId, new[] { new RouteStop(Alpha, new[] { "ore" }, minFreightTonnes: 5), RouteStop.Empty(Beta), RouteStop.Empty(Alpha), new RouteStop(Gamma, new[] { "food" }) }));
            Ok(sim.PauseRoute(CourierId));
            SetSupply(sim, Alpha, "ore", 60);
            Ok(sim.ResumeRoute(CourierId));
            Assert.AreEqual(Beta, Courier(sim).Voyage.DestinationId);
        }

        // ---- Orders that do not match the ship's location (decision 4a) ----

        [Test]
        public void DockedAtASystemNotOnTheRoute_IsBlocked()
        {
            var sim = NewThreeSystemSim(1_000_000);
            Ok(sim.SetRoute(CourierId, new[] { RouteStop.Empty(Beta), RouteStop.Empty(Gamma) }));
            Assert.AreEqual(RouteStatus.Blocked, Route(sim).Status);
            Assert.AreEqual("Test Courier is docked at Alpha, which is not on its route. Change its orders, or pause the route and fly Test Courier to one of its stops.", Route(sim).StatusText);
            Assert.AreEqual(1, BlockedNotifications(sim));
        }

        [Test]
        public void DestinationRemovedFromTheRoute_InFlight_BlocksOnArrival()
        {
            var sim = NewThreeSystemSim(1_000_000);
            Ok(sim.SetRoute(CourierId, OreShuttle()));
            string line = VoyageLine(sim);
            sim.Advance(Year);
            Ok(sim.SetRoute(CourierId, new[] { RouteStop.Empty(Alpha), new RouteStop(Gamma, new[] { "food" }) }));
            Assert.AreEqual(line, VoyageLine(sim));
            Assert.AreEqual(0, BlockedNotifications(sim));
            sim.Advance(Leg45);
            Assert.AreEqual(Beta, Courier(sim).DockedSystemId);
            Assert.AreEqual(RouteStatus.Blocked, Route(sim).Status);
            StringAssert.StartsWith("Test Courier is docked at Beta, which is not on its route.", Route(sim).StatusText);
            Assert.AreEqual(Leg45, sim.Notifications.Last().Tick);
        }

        // ---- Drives (owner's answer 4) ----

        [Test]
        public void DriveFittedToAWaitingRouteShip_IsUsedByItsNextDeparture()
        {
            var tuning = FlatTuning();
            var galaxy = new Galaxy(1, Alpha, new List<StarSystem>
            {
                new StarSystem(Alpha, "Alpha", 0, 0, 10, new[] { "mining_colony", "shipyard_works" }, true),
                new StarSystem(Beta, "Beta", 4_500, 0, 10, new[] { "refinery" }, false),
            });
            var sim = new Simulation();
            sim.AttachMarket(Market.Create(galaxy, tuning, 0));
            sim.SetCredits(10_000_000);
            sim.AddShip("Test Courier", "courier", tuning.Start.StarterDrive, Alpha);
            SetSupply(sim, Alpha, "ore", 5);
            Ok(sim.SetRoute(CourierId, OreShuttle(minFreight: 20)));
            Assert.AreEqual(RouteStatus.Waiting, Route(sim).Status);

            Ok(sim.BuyDrive(CourierId, "alcubierre_3_75"));
            sim.Advance(60 * Day);
            Assert.AreEqual("alcubierre_3_75", Courier(sim).Voyage.DriveId);
            Assert.AreEqual(4_320_000, Courier(sim).Voyage.TransitTicks, "4.5 ly at 3.75c = 1.2 game years");
        }

        // ---- Validation (decision 9a, small readings) ----

        [Test]
        public void InvalidOrders_AreRefused_ListingEveryProblem_AndChangeNothing()
        {
            var sim = NewThreeSystemSim(1_000_000);
            var cases = new (IEnumerable<RouteStop> stops, string expected)[]
            {
                (new[] { RouteStop.Empty(Alpha) }, "A route needs at least 2 stops; this one has 1."),
                (new[] { RouteStop.Empty(Alpha), RouteStop.Empty(Alpha) }, "Stops 1 and 2 are both Alpha; each leg must go to a different system."),
                (new[] { RouteStop.Empty(Alpha), RouteStop.Empty(9) }, "Stop 2: there is no system with id 9."),
                (new[] { new RouteStop(Alpha, new[] { "food" }), RouteStop.Empty(Beta) }, "Stop 1 (Alpha): Alpha does not produce food."),
                (new[] { new RouteStop(Alpha, new[] { "ore" }), new RouteStop(Gamma, new[] { "food" }) }, "Stop 1 (Alpha): Gamma does not buy ore."),
                (new[] { new RouteStop(Alpha, new[] { "ore", "ore" }), RouteStop.Empty(Beta) }, "Stop 1 (Alpha) lists ore more than once."),
                (new[] { new RouteStop(Alpha, new[] { "gold" }), RouteStop.Empty(Beta) }, "Stop 1 (Alpha): \"gold\" is not a commodity."),
                (new[] { new RouteStop(Alpha), RouteStop.Empty(Beta) }, "Stop 1 (Alpha) lists no freight and does not carry passengers. Mark the leg to Beta as an empty repositioning leg, or choose freight or passengers."),
                (new[] { new RouteStop(Alpha, new[] { "ore" }, emptyLeg: true), RouteStop.Empty(Beta) }, "Stop 1 (Alpha) is marked as an empty leg to Beta, so it cannot list freight, carry passengers, or have a minimum."),
                (new[] { new RouteStop(Alpha, carryPassengers: true, minFreightTonnes: 5), RouteStop.Empty(Beta) }, "Stop 1 (Alpha) has a freight minimum of 5 t but lists no freight."),
                (new[] { new RouteStop(Alpha, new[] { "ore" }, minPassengers: 2), RouteStop.Empty(Beta) }, "Stop 1 (Alpha) has a passenger minimum of 2 but does not carry passengers."),
                (new[] { new RouteStop(Alpha, new[] { "ore" }, minFreightTonnes: -1), RouteStop.Empty(Beta) }, "Stop 1 (Alpha): the freight minimum cannot be negative, got -1 t."),
            };
            var before = StateBytes(sim);
            foreach (var (stops, expected) in cases)
            {
                var r = sim.SetRoute(CourierId, stops);
                Assert.IsFalse(r.Succeeded, expected);
                StringAssert.Contains(expected, r.Reason);
                CollectionAssert.AreEqual(before, StateBytes(sim), expected);
            }
            var many = sim.SetRoute(CourierId, new[] { new RouteStop(Alpha, new[] { "food", "gold" }), new RouteStop(Gamma, new[] { "food" }) });
            StringAssert.Contains("Alpha does not produce food.", many.Reason);
            StringAssert.Contains("\"gold\" is not a commodity.", many.Reason);
            Assert.IsFalse(sim.SetRoute(42, OreShuttle()).Succeeded);
            CollectionAssert.AreEqual(before, StateBytes(sim));
        }

        // ---- Notifications (decision 8a) ----

        [Test]
        public void ArrivalsRaiseANotification_WaitingDoesNot()
        {
            var sim = NewSim(1_000_000);
            SetSupply(sim, Alpha, "ore", 5);
            Ok(sim.SetRoute(CourierId, OreShuttle(minFreight: 20)));
            Assert.AreEqual(0, sim.Notifications.Count, "Waiting raises no notification");
            sim.Advance(60 * Day + Leg45);
            var n = sim.Notifications.Single();
            Assert.AreEqual(NotificationKind.Arrival, n.Kind);
            Assert.AreEqual($"Test Courier arrived at Beta. Net profit {Format.Credits(sim.Receipts[0].NetProfit)} credits.", n.Text);
            Assert.AreEqual(60 * Day + Leg45, n.Tick);
        }
    }
}
