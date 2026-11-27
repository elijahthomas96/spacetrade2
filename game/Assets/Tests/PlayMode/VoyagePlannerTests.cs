using System.Collections;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Game.Audio;
using SpaceTrade.Game.Hud;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Ships;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Voyages;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace SpaceTrade.Game.Tests
{
    /// <summary>
    /// Session 14: the voyage planner, the launch with its top-up question, and the receipt (spec 6.5 to 6.7). The screen
    /// shows the simulation's quote and failure messages exactly as given, never launches with figures other than the
    /// ones on screen, and a launch through it leaves the game exactly as the same Dispatch command would.
    /// </summary>
    public class VoyagePlannerTests : PlannerTestBase
    {
        private Ship Starter => Sim.Ships[0];

        /// <summary>
        /// The session's "Done when": a whole manual voyage through the screen only (plan, quote, launch with the top-up
        /// confirmed, arrival, receipt), and the receipt matches the quote that was shown, figure for figure. The sounds
        /// for dispatch, arrival, and payment each play once, in the frame they happen.
        /// </summary>
        [UnityTest]
        public IEnumerator AFullVoyage_ThroughTheScreenOnly_AndTheReceiptMatchesTheQuoteFigureForFigure()
        {
            yield return _rig.Open(GameTestTuning.NewGameWithSystems(12));
            int shipId = Starter.Id;
            Assert.AreEqual(shipId, Map.SelectedShipId, "a new game opens with the starter ship selected");
            Assert.IsFalse(OnScreen(ShipPanelButton("last-receipt")), "no voyage yet, so no Last receipt button");

            // Plan: the planner's own buttons and rows.
            yield return OpenPlannerFromTheShipPanel();
            Assert.IsFalse(Map.Hud.InfoPanelShowing, "the planner has taken the ship panel's place");
            Press(PlannerButton("planner-jobs-sort-MostProfitable"));
            yield return MapTestRig.Frames(3);
            Assert.AreEqual(JobSort.MostProfitable, Planner.Jobs.Sort);
            var job = Planner.Jobs.Shown[0];
            Assert.IsTrue(job.CanFly);
            Assert.Greater(job.NetProfit, 0, "the guaranteed profitable opening job is at the top");
            ClickRow(Planner.Jobs, job);
            yield return MapTestRig.Frames(3);
            Assert.AreEqual(job.DestinationId, Planner.DestinationId);

            // The quote on screen is the simulation's, exactly.
            var plan = Planner.Plan();
            var simQuote = Sim.Quote(plan);
            Assert.IsTrue(simQuote.IsFeasible);
            Assert.AreEqual(job.NetProfit, simQuote.NetProfit, "the row's profit is the quote's");
            Assert.AreEqual(JobText.Terms(simQuote), JobText.Terms(Planner.Quote));
            string quoteRevenue = Planner.LineText("quote-revenue"), quoteCost = Planner.LineText("quote-cost"), quoteNet = Planner.LineText("quote-net");
            var quoteSupplies = Enumerable.Range(0, 3).Select(i => Planner.LineText("quote-supply-" + i)).ToArray();
            Assert.AreEqual($"Revenue: freight {Format.Credits(simQuote.FreightRevenue)} + fares {Format.Credits(simQuote.FareRevenue)} = {Format.Credits(simQuote.GrossRevenue)}", quoteRevenue);
            Assert.AreEqual($"Supplies used, at cost: {Format.Credits(simQuote.OperatingCost)}", quoteCost);
            Assert.AreEqual($"Net profit: {Format.Credits(simQuote.NetProfit)} · {Format.Credits(simQuote.ProfitPerGameYear)} per game year", quoteNet);
            Assert.AreEqual($"Top-up to buy: {Format.Credits(simQuote.TopUpCost)} credits (you have {Format.Credits(Sim.Credits)})", Planner.LineText("quote-topup"));
            Assert.AreEqual(simQuote.DescribeArrival() + " if launched now", Planner.LineText("quote-arrival"));
            for (int i = 0; i < 3; i++)
            {
                var s = simQuote.Supplies[i];
                Assert.AreEqual($"{s.Kind}: {Format.Tonnes(s.AboardKg)} aboard · needs {Format.Tonnes(s.RequiredKg)} · short {Format.Tonnes(s.ShortfallKg)} · top-up {Format.Credits(s.TopUpCost)} · uses {Format.Credits(s.UsedCost)}", quoteSupplies[i]);
            }
            Assert.AreEqual("", Planner.LineText("quote-problems"));
            Assert.Greater(simQuote.TopUpCost, 0, "a new ship has empty tanks, so supplies are short");

            // Launch: the planner asks first, showing the top-up cost. Nothing has been sent yet.
            var before = Bytes(Sim);
            Press(PlannerButton("launch"));
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(Planner.IsConfirming);
            Assert.AreEqual($"Buy supplies for {Format.Credits(simQuote.TopUpCost)} credits and launch?", Planner.ConfirmText);
            Assert.IsTrue(OnScreen(PlannerButton("confirm-yes")));
            CollectionAssert.AreEqual(before, Bytes(Sim), "asking changes nothing");

            long creditsBefore = Sim.Credits;
            int total = Audio.StartedTotal;
            int pressFrame = UnityEngine.Time.frameCount;
            Press(PlannerButton("confirm-yes"));
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(Starter.IsInTransit, "the ship has left");
            Assert.AreEqual(creditsBefore - simQuote.TopUpCost, Sim.Credits, "the top-up was bought, exactly the quoted amount");
            Assert.AreEqual(JobText.Terms(simQuote), JobText.Terms(Planner.LastLaunchedQuote));
            Assert.AreEqual("Dispatch", Names(Since(total)), "a launch that buys supplies plays dispatch only, once");
            Assert.LessOrEqual(Since(total)[0].Frame - pressFrame, 1, "in the frame the ship left");
            Assert.IsFalse(Map.Panels.IsOpen(Planner), "the planner has gone");
            Assert.IsTrue(Map.Hud.InfoPanelShowing, "and the ship panel is back");
            Assert.AreEqual(Sim.EtaOf(shipId).Describe(), Map.Hud.Root.Q<Label>("ship-eta").text);
            Assert.IsFalse(OnScreen(ShipPanelButton("plan-voyage")), "a ship in flight cannot be planned for");
            yield return NothingMorePlays("the launch");

            // Arrival. The test rig's clock stands in for the hours of real time (the Editor playthrough uses debug skip).
            var voyage = Starter.Voyage;
            Bar.TogglePause();
            yield return MapTestRig.Frames();
            total = Audio.StartedTotal;
            int arrivalFrame = UnityEngine.Time.frameCount;
            yield return _rig.RealMs(voyage.ArrivalTick - Sim.CurrentTick);
            Assert.IsFalse(Starter.IsInTransit, "the ship has docked");
            Assert.AreEqual(1, Sim.Receipts.Count);
            Assert.AreEqual("Arrival, Payment", Names(Since(total)), "arrival and payment, once each");
            foreach (var sound in Since(total)) Assert.LessOrEqual(sound.Frame - arrivalFrame, 1, sound.Id + " in the frame the ship arrived");
            yield return NothingMorePlays("the arrival");

            // The receipt, opened from the arrival's notification.
            Bar.ToggleNotifications();
            yield return MapTestRig.Frames(3);
            var arrival = Bar.Notifications.Items[0];
            Assert.AreEqual(NotificationKind.Arrival, arrival.Kind);
            var row = Bar.Notifications.Panel.Query(className: "notification-row").ToList().First(r => ReferenceEquals(r.userData, arrival));
            using (var click = ClickEvent.GetPooled())
            {
                click.target = row;
                row.SendEvent(click);
            }
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(Map.Panels.IsShowing(Receipt), "the receipt is on screen");
            Assert.IsFalse(Bar.Notifications.IsOpen, "the list has given way to it");
            var receipt = Receipt.Receipt;
            Assert.AreSame(Sim.Receipts[0], receipt);

            // Figure for figure against the quote that was shown.
            var shown = Planner.LastLaunchedQuote;
            Assert.AreEqual(shown.FreightRevenue, receipt.FreightRevenue);
            Assert.AreEqual(shown.FareRevenue, receipt.FareRevenue);
            Assert.AreEqual(shown.GrossRevenue, receipt.GrossRevenue);
            Assert.AreEqual(shown.OperatingCost, receipt.SuppliesCost);
            Assert.AreEqual(shown.NetProfit, receipt.NetProfit);
            Assert.AreEqual(shown.TransitTicks, receipt.ElapsedTicks);
            Assert.AreEqual(shown.Plan.Passengers, receipt.Passengers);
            Assert.AreEqual(shown.Freight.Count, receipt.Freight.Count);
            for (int i = 0; i < shown.Freight.Count; i++)
            {
                Assert.AreEqual(shown.Freight[i].CommodityId, receipt.Freight[i].CommodityId);
                Assert.AreEqual(shown.Freight[i].Tonnes, receipt.Freight[i].Tonnes);
                Assert.AreEqual(shown.Freight[i].RatePerTonne, receipt.Freight[i].RatePerTonne);
            }
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(shown.Supplies[i].Kind, receipt.Supplies[i].Kind);
                Assert.AreEqual(shown.Supplies[i].RequiredKg, receipt.Supplies[i].Kg);
                Assert.AreEqual(shown.Supplies[i].UsedCost, receipt.Supplies[i].Cost);
            }

            // And the words on the two panels carry the same figures.
            var lines = Receipt.Lines();
            Assert.AreEqual(quoteRevenue, Receipt.LineText("receipt-revenue"), "the revenue line is the same on both");
            Assert.AreEqual(quoteCost, Receipt.LineText("receipt-cost"), "the supplies line is the same on both");
            Assert.AreEqual($"Net profit: {Format.Credits(receipt.NetProfit)}", Receipt.LineText("receipt-net"));
            StringAssert.StartsWith(Receipt.LineText("receipt-net") + " · ", quoteNet);
            for (int i = 0; i < 3; i++)
            {
                var s = receipt.Supplies[i];
                string line = $"{s.Kind}: {Format.Tonnes(s.Kg)} used, {Format.Credits(s.Cost)} at cost";
                CollectionAssert.Contains(lines, line);
                StringAssert.Contains($"needs {Format.Tonnes(s.Kg)} ", quoteSupplies[i]);
                StringAssert.EndsWith($"uses {Format.Credits(s.Cost)}", quoteSupplies[i]);
            }
            CollectionAssert.Contains(lines, $"Departed {GameDate.FromTick(receipt.DepartureTick)} · arrived {GameDate.FromTick(receipt.ArrivalTick)} · {Format.GameDuration(receipt.ElapsedTicks)} in transit");
            foreach (var f in receipt.Freight)
                CollectionAssert.Contains(lines, $"{JobText.Cargo(Sim, f.CommodityId)}: {Format.Credits(f.Tonnes)} t at {Format.Credits(f.RatePerTonne)} per tonne = {Format.Credits(f.Revenue)}");

            // Closing the receipt brings the ship panel back, which now offers the last receipt.
            Press(Receipt.Panel.Q<Button>("receipt-close"));
            yield return MapTestRig.Frames(3);
            Assert.IsFalse(Map.Panels.IsOpen(Receipt));
            Assert.IsTrue(Map.Hud.InfoPanelShowing);
            Assert.AreEqual(shipId, Map.SelectedShipId);
            Press(ShipPanelButton("last-receipt"));
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(Map.Panels.IsShowing(Receipt));
            Assert.AreSame(Sim.Receipts[0], Receipt.Receipt);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// A launch through the screen leaves the game in exactly the state the same Dispatch command leaves it in, sent
        /// directly: with the top-up bought at launch, and with Provision pressed first.
        /// </summary>
        [UnityTest]
        public IEnumerator ALaunchThroughTheScreen_EndsInExactlyTheSameState_AsTheSameDispatchCommandSentDirectly([Values(false, true)] bool provisionFirst)
        {
            yield return _rig.Open(GameTestTuning.NewGameWithSystems(12));
            var direct = GameTestTuning.NewGameWithSystems(12);
            CollectionAssert.AreEqual(Bytes(direct), Bytes(Sim), "the two games start identical");

            yield return OpenPlannerFromTheShipPanel();
            Planner.Jobs.SetSort(JobSort.MostProfitable);
            var job = Planner.Jobs.Shown[0];
            ClickRow(Planner.Jobs, job);
            yield return MapTestRig.Frames(3);
            // A mixed load where the port offers one: a passenger too.
            var passengers = Planner.Board.Entries.FirstOrDefault(e => e.IsPassengerJob && e.DestinationId == job.DestinationId && e.CanFly);
            if (passengers != null && !job.IsPassengers)
            {
                Planner.SetPassengers(1);
                // A passenger's food and water need room in the hold: unload freight until the voyage fits again.
                while (!Sim.Quote(Planner.Plan()).IsFeasible && Planner.Tonnes(job.CommodityId) > 1)
                    Planner.SetTonnes(job.CommodityId, Planner.Tonnes(job.CommodityId) - 1);
                yield return MapTestRig.Frames(3);
                Assert.AreEqual(1, Planner.Plan().Passengers);
                Assert.AreEqual(1, Planner.Plan().Freight.Count, "freight and a passenger together");
            }
            var plan = Planner.Plan();
            Assert.IsTrue(Planner.Quote.IsFeasible, string.Join(" ", Planner.Quote.Problems));

            if (provisionFirst)
            {
                Press(PlannerButton("provision"));
                yield return MapTestRig.Frames(3);
                Assert.AreEqual(0, Planner.Quote.TopUpCost, "Provision bought exactly the shortfall");
                Press(PlannerButton("launch"));
                yield return MapTestRig.Frames(3);
                Assert.IsFalse(Planner.IsConfirming, "nothing to buy, so no question");
                Assert.IsTrue(direct.Provision(plan).Succeeded);
                Assert.IsTrue(direct.Dispatch(plan, false).Succeeded);
            }
            else
            {
                Press(PlannerButton("launch"));
                yield return MapTestRig.Frames(3);
                Assert.IsTrue(Planner.IsConfirming);
                Press(PlannerButton("confirm-yes"));
                yield return MapTestRig.Frames(3);
                Assert.IsTrue(direct.Dispatch(plan, true).Succeeded);
            }
            Assert.IsTrue(Sim.Ships[0].IsInTransit, "the screen's launch went through");
            Assert.AreEqual(direct.DescribeState(), Sim.DescribeState(), "the screen's launch and the direct command differ");
            CollectionAssert.AreEqual(Bytes(direct), Bytes(Sim));
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>Provision buys exactly the shortfall and plays the provision sound; No sends nothing.</summary>
        [UnityTest]
        public IEnumerator Provision_BuysExactlyTheShortfall_AndNo_SendsNothing()
        {
            yield return _rig.Open(GameTestTuning.NewGameWithSystems(12));
            yield return OpenPlannerFromTheShipPanel();
            ClickRow(Planner.Jobs, FirstFlyable());
            yield return MapTestRig.Frames(3);
            var quote = Planner.Quote;
            Assert.Greater(quote.TopUpCost, 0);

            // No: back to the planner, the game untouched.
            var before = Bytes(Sim);
            Press(PlannerButton("launch"));
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(Planner.IsConfirming);
            Assert.IsFalse(Planner.CanLaunch, "while it asks, the other buttons wait");
            Press(PlannerButton("confirm-no"));
            yield return MapTestRig.Frames(3);
            Assert.IsFalse(Planner.IsConfirming);
            Assert.IsTrue(Map.Panels.IsShowing(Planner));
            Assert.IsFalse(Sim.Ships[0].IsInTransit);
            CollectionAssert.AreEqual(before, Bytes(Sim), "No changes nothing");

            long credits = Sim.Credits;
            int total = Audio.StartedTotal;
            Press(PlannerButton("provision"));
            yield return MapTestRig.Frames(3);
            Assert.AreEqual(credits - quote.TopUpCost, Sim.Credits);
            Assert.AreEqual(quote.TopUpCost, Sim.SuppliesBoughtTotal);
            for (int i = 0; i < 3; i++) Assert.AreEqual(quote.Supplies[i].RequiredKg, Sim.Ships[0].Supply(quote.Supplies[i].Kind).Kg);
            Assert.AreEqual("Provision", Names(Since(total)));
            Assert.AreEqual(0, Planner.Quote.TopUpCost);
            Assert.IsFalse(Planner.CanProvision, "nothing left to buy");
            Assert.AreEqual($"Top-up to buy: 0 credits (you have {Format.Credits(Sim.Credits)})", Planner.LineText("quote-topup"));

            total = Audio.StartedTotal;
            Press(PlannerButton("launch"));
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(Sim.Ships[0].IsInTransit, "with nothing to buy the launch needs no question");
            Assert.AreEqual("Dispatch", Names(Since(total)));
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// The owner's condition: if the quote changes between what the question shows and the click, the launch does not
        /// go through with different figures. The new quote is shown and the question asked again.
        /// </summary>
        [UnityTest]
        public IEnumerator IfTheQuoteChangesWhileThePlannerIsAsking_NothingIsLaunched_AndItShowsTheNewQuoteAndAsksAgain()
        {
            yield return _rig.Open(GameTestTuning.NewGameWithSystems(12));
            yield return OpenPlannerFromTheShipPanel();
            Planner.Jobs.SetSort(JobSort.MostProfitable);
            var job = Planner.Jobs.Shown.First(r => r.CanFly && !r.IsPassengers && r.Entry.LargestLoad >= 3);
            Planner.ChooseJob(job);
            yield return MapTestRig.Frames(3);
            var plan = Planner.Plan();
            Press(PlannerButton("launch"));
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(Planner.IsConfirming);
            var asked = Planner.Quote;
            string askedText = Planner.ConfirmText;

            // Behind the question, the ship is part-provisioned (for a one-tonne load), so the top-up this voyage needs is now smaller.
            var small = new VoyagePlan(plan.ShipId, plan.DestinationId, new[] { new SpaceTrade.Sim.Markets.FreightLine(job.CommodityId, 1) });
            Assert.IsTrue(Sim.Provision(small).Succeeded);
            var fresh = Sim.Quote(plan);
            Assert.AreNotEqual(JobText.Terms(asked), JobText.Terms(fresh), "the quote has changed");
            Assert.Greater(fresh.TopUpCost, 0);
            Assert.Less(fresh.TopUpCost, asked.TopUpCost);
            yield return MapTestRig.Frames(3);
            Assert.AreEqual(askedText, Planner.ConfirmText, "the question holds still until it is answered");
            Assert.AreEqual(JobText.Terms(asked), JobText.Terms(Planner.Quote), "and so does the quote above it");

            var before = Bytes(Sim);
            int total = Audio.StartedTotal;
            Press(PlannerButton("confirm-yes"));
            yield return MapTestRig.Frames(3);
            Assert.IsFalse(Sim.Ships[0].IsInTransit, "nothing was launched");
            CollectionAssert.AreEqual(before, Bytes(Sim), "and nothing was bought");
            Assert.AreEqual("", Names(Since(total)), "no dispatch sound");
            Assert.IsTrue(Planner.IsConfirming, "it asks again");
            Assert.AreEqual($"Buy supplies for {Format.Credits(fresh.TopUpCost)} credits and launch?", Planner.ConfirmText, "with the new cost");
            Assert.AreEqual(JobText.Terms(fresh), JobText.Terms(Planner.Quote), "and shows the new quote");
            StringAssert.Contains("The quote changed while you were deciding, so nothing was launched.", Planner.Message);
            Assert.AreEqual(JobText.TopUpLine(fresh), Planner.LineText("quote-topup"));

            // Answering the new question launches on the new figures.
            long credits = Sim.Credits;
            Press(PlannerButton("confirm-yes"));
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(Sim.Ships[0].IsInTransit);
            Assert.AreEqual(credits - fresh.TopUpCost, Sim.Credits);
            Assert.AreEqual(JobText.Terms(fresh), JobText.Terms(Planner.LastLaunchedQuote));
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>The same when the change leaves nothing to buy: no launch on the old figures, and no question left to ask.</summary>
        [UnityTest]
        public IEnumerator IfTheTopUpIsNoLongerNeeded_TheOldQuestionDoesNotLaunch()
        {
            yield return _rig.Open(GameTestTuning.NewGameWithSystems(12));
            yield return OpenPlannerFromTheShipPanel();
            Planner.ChooseJob(FirstFlyable());
            yield return MapTestRig.Frames(3);
            var plan = Planner.Plan();
            Press(PlannerButton("launch"));
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(Planner.IsConfirming);

            Assert.IsTrue(Sim.Provision(plan).Succeeded);
            var before = Bytes(Sim);
            Press(PlannerButton("confirm-yes"));
            yield return MapTestRig.Frames(3);
            Assert.IsFalse(Sim.Ships[0].IsInTransit, "nothing was launched on the old question");
            CollectionAssert.AreEqual(before, Bytes(Sim));
            Assert.IsFalse(Planner.IsConfirming);
            Assert.AreEqual(0, Planner.Quote.TopUpCost);
            StringAssert.Contains("The quote changed while you were deciding", Planner.Message);
            Assert.IsTrue(Planner.CanLaunch);
            Press(PlannerButton("launch"));
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(Sim.Ships[0].IsInTransit, "Launch, pressed again on the new quote, goes");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>The owner's own example: a rate re-roll between the question and the click.</summary>
        [UnityTest]
        public IEnumerator ARateReRoll_BetweenTheQuestionAndTheClick_StopsTheLaunch_AndTheNewRateIsShown()
        {
            // One ship already in flight, so the start port's opening rates are no longer held and re-roll like any other.
            yield return _rig.Open(MapTestRig.FleetGame(12, 1, 1));
            var docked = Sim.Ships.First(s => !s.IsInTransit);
            Map.SelectShip(docked.Id);
            yield return MapTestRig.Frames(3);
            yield return OpenPlannerFromTheShipPanel();
            Planner.Jobs.SetSort(JobSort.MostProfitable);
            var job = Planner.Jobs.Shown.First(r => r.CanFly && !r.IsPassengers);
            Planner.ChooseJob(job);
            yield return MapTestRig.Frames(3);
            var plan = Planner.Plan();
            Press(PlannerButton("launch"));
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(Planner.IsConfirming);
            var asked = Planner.Quote;

            // Time passes behind the question until this port's rates have re-rolled to something else.
            int rolls = 0;
            long interval = Sim.Market.Tuning.Rates.RerollIntervalDays * GameTime.TicksPerDay;
            while (JobText.Terms(Sim.Quote(plan)) == JobText.Terms(asked) && rolls < 20)
            {
                Sim.Advance(Sim.CurrentTick + interval);
                rolls++;
            }
            var fresh = Sim.Quote(plan);
            Assert.AreNotEqual(asked.Freight[0].RatePerTonne, fresh.Freight[0].RatePerTonne, $"the rate re-rolled (after {rolls} re-roll intervals)");

            long credits = Sim.Credits;
            Press(PlannerButton("confirm-yes"));
            yield return MapTestRig.Frames(3);
            Assert.IsFalse(docked.IsInTransit, "nothing was launched at the old rate");
            Assert.AreEqual(credits, Sim.Credits, "and nothing was bought");
            Assert.AreEqual(JobText.Terms(fresh), JobText.Terms(Planner.Quote), "the new quote is on screen");
            Assert.AreEqual(JobText.RevenueLine(fresh.FreightRevenue, fresh.FareRevenue, fresh.GrossRevenue), Planner.LineText("quote-revenue"));
            StringAssert.Contains("The quote changed while you were deciding", Planner.Message);
            if (fresh.IsFeasible)
            {
                Assert.IsTrue(Planner.IsConfirming, "it asks again");
                Press(PlannerButton("confirm-yes"));
                yield return MapTestRig.Frames(3);
                Assert.IsTrue(docked.IsInTransit);
                Assert.AreEqual(fresh.Freight[0].RatePerTonne, docked.Voyage.Freight[0].RatePerTonne, "the voyage is locked at the rate that was shown");
            }
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>Failure messages are the simulation's own words, and a voyage that cannot be flown cannot be launched.</summary>
        [UnityTest]
        public IEnumerator FailureMessages_AreShownExactlyAsTheSimulationGivesThem_AndSuchAVoyageCannotBeLaunched()
        {
            yield return _rig.Open(GameTestTuning.NewGameWithSystems(12));
            yield return OpenPlannerFromTheShipPanel();
            var job = Planner.Jobs.Shown.First(r => r.CanFly && !r.IsPassengers);
            ClickRow(Planner.Jobs, job);
            yield return MapTestRig.Frames(3);

            // Far more freight than the hold takes, and more passengers than there are berths: typed into the planner's own boxes.
            var field = Planner.Panel.Q("load-" + job.CommodityId).Q<IntegerField>("amount");
            field.value = 500;
            var seats = Planner.Panel.Q("load-passengers")?.Q<IntegerField>("amount");
            if (seats != null) seats.value = 99;
            yield return MapTestRig.Frames(3);
            Assert.AreEqual(500, Planner.Tonnes(job.CommodityId));
            var quote = Sim.Quote(Planner.Plan());
            Assert.IsFalse(quote.IsFeasible);
            Assert.GreaterOrEqual(quote.Problems.Count, 2);
            Assert.AreEqual(string.Join("\n", quote.Problems), Planner.LineText("quote-problems"), "every problem, word for word");
            StringAssert.Contains("The hold holds 30 t", Planner.LineText("quote-problems"));
            Assert.IsFalse(Planner.CanLaunch);
            Assert.IsFalse(Planner.CanProvision, "Provision is refused unless the voyage is otherwise feasible (spec 6.5)");

            var before = Bytes(Sim);
            Press(PlannerButton("launch"));
            Planner.PressLaunch();
            Planner.PressProvision();
            yield return MapTestRig.Frames(3);
            Assert.IsFalse(Planner.IsConfirming);
            Assert.IsFalse(Sim.Ships[0].IsInTransit);
            CollectionAssert.AreEqual(before, Bytes(Sim), "pressing Launch and Provision on a voyage that cannot be flown changes nothing");

            // The minus and Max buttons bring it back to a load the ship can fly.
            Press(Planner.Panel.Q("load-" + job.CommodityId).Q<Button>("max"));
            if (seats != null) Planner.SetPassengers(0);
            yield return MapTestRig.Frames(3);
            Assert.AreEqual((int)job.Entry.LargestLoad, Planner.Tonnes(job.CommodityId), "Max is the job board's largest feasible load");
            Assert.IsTrue(Planner.Quote.IsFeasible);
            Press(Planner.Panel.Q("load-" + job.CommodityId).Q<Button>("less"));
            yield return MapTestRig.Frames(3);
            Assert.AreEqual((int)job.Entry.LargestLoad - 1, Planner.Tonnes(job.CommodityId));
            Press(Planner.Panel.Q("load-" + job.CommodityId).Q<Button>("more"));
            Press(Planner.Panel.Q("load-" + job.CommodityId).Q<Button>("more"));
            yield return MapTestRig.Frames(3);
            Assert.AreEqual((int)job.Entry.LargestLoad + 1, Planner.Tonnes(job.CommodityId));
            Assert.IsFalse(Planner.Quote.IsFeasible, "one more than the largest load does not fit, and the quote says so");
            Assert.AreEqual(string.Join("\n", Sim.Quote(Planner.Plan()).Problems), Planner.LineText("quote-problems"));
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>A ship on in-system transport: the planner still quotes and provisions, but shows why it cannot launch.</summary>
        [UnityTest]
        public IEnumerator AShipOnInSystemTransport_ShowsTheSimulationsReason_AndCannotBeLaunched()
        {
            yield return _rig.Open(GameTestTuning.NewGameWithSystems(12));
            int id = Sim.Ships[0].Id;
            Assert.IsTrue(Sim.SetInSystemTransport(id, true).Succeeded);
            yield return OpenPlannerFromTheShipPanel();
            string reason = Sim.ManualDispatchRefusal(id);
            Assert.IsNotNull(reason);
            StringAssert.EndsWith("Turn off in-system transport to dispatch it.", reason);
            Assert.AreEqual(reason, Planner.LaunchReason);
            Assert.AreEqual(reason, Planner.LineText("launch-reason"), "the reason is on screen");

            ClickRow(Planner.Jobs, FirstFlyable());
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(Planner.Quote.IsFeasible, "the quote itself is fine (spec 6.6)");
            Assert.IsFalse(Planner.CanLaunch, "but Launch is greyed out");
            var plan = Planner.Plan();
            var before = Bytes(Sim);
            Press(PlannerButton("launch"));
            Planner.PressLaunch();
            yield return MapTestRig.Frames(3);
            Assert.IsFalse(Planner.IsConfirming);
            CollectionAssert.AreEqual(before, Bytes(Sim));

            // The words are exactly Dispatch's own.
            var refused = Sim.Dispatch(plan, true);
            Assert.IsFalse(refused.Succeeded);
            Assert.AreEqual(refused.Reason, Planner.LaunchReason);

            // Provision is still allowed.
            Assert.IsTrue(Planner.CanProvision);
            long topUp = Planner.Quote.TopUpCost, credits = Sim.Credits;
            Press(PlannerButton("provision"));
            yield return MapTestRig.Frames(3);
            Assert.AreEqual(credits - topUp, Sim.Credits);
            Assert.IsFalse(Sim.Ships[0].IsInTransit);

            // Switched off, the reason goes and the ship can launch.
            Assert.IsTrue(Sim.SetInSystemTransport(id, false).Succeeded);
            yield return MapTestRig.Frames(3);
            Assert.AreEqual("", Planner.LaunchReason);
            Assert.IsTrue(Planner.CanLaunch);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>The planner and the system view work while paused, and while other ships are in flight with the clock running.</summary>
        [UnityTest]
        public IEnumerator ThePlannerAndTheSystemView_WorkWhilePaused_AndWhileOtherShipsAreInFlight()
        {
            yield return _rig.Open(MapTestRig.FleetGame(12, 4, 2));
            Assert.IsTrue(Sim.Clock.IsPaused, "the game starts paused");
            Assert.AreEqual(4, Sim.Ships.Count(s => s.IsInTransit));
            var docked = Sim.Ships.Where(s => !s.IsInTransit).ToList();
            Assert.AreEqual(2, docked.Count);
            int port = docked[0].DockedSystemId.Value;

            // Paused: the system view.
            Map.Select(port);
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(Map.Hud.InfoPanelShowing);
            int jobs = Sim.Market.OffersFrom(port).Count + Sim.Market.PoolsFrom(port).Count;
            Assert.AreEqual(jobs, Map.Hud.SystemJobs.Shown.Count);
            CollectionAssert.Contains(Map.Hud.InfoLines, "Docked here (2)");

            // Paused: plan and launch. The launch takes no game time.
            long tick = Sim.CurrentTick;
            Map.SelectShip(docked[0].Id);
            yield return MapTestRig.Frames(3);
            yield return OpenPlannerFromTheShipPanel();
            ClickRow(Planner.Jobs, FirstFlyable());
            yield return MapTestRig.Frames(3);
            Assert.AreEqual(JobText.Terms(Sim.Quote(Planner.Plan())), JobText.Terms(Planner.Quote));
            Press(PlannerButton("launch"));
            yield return MapTestRig.Frames(3);
            if (Planner.IsConfirming) Press(PlannerButton("confirm-yes"));
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(docked[0].IsInTransit, "launched while paused");
            Assert.AreEqual(tick, Sim.CurrentTick, "and the clock did not move");
            Assert.IsTrue(Sim.Clock.IsPaused);
            StringAssert.EndsWith(" · Paused", Map.Hud.Root.Q<Label>("ship-eta").text);

            // Running at 5x with five ships in flight: the planner for the last docked ship keeps up with the game.
            Bar.ChooseSpeed(ClockSpeed.X5);
            Bar.TogglePause();
            Map.SelectShip(docked[1].Id);
            yield return MapTestRig.Frames(3);
            yield return OpenPlannerFromTheShipPanel();
            ClickRow(Planner.Jobs, FirstFlyable());
            yield return MapTestRig.Frames(3);
            var progress = Sim.Ships.Where(s => s.IsInTransit).ToDictionary(s => s.Id, s => Sim.PositionOf(s.Id).ProgressHundredthsOfPercent);
            string arrivalBefore = Planner.LineText("quote-arrival");
            int builds = Planner.BoardBuilds;
            yield return _rig.RealMs(40_000); // 200,000 ticks at 5x: 20 game days
            Assert.AreEqual(tick + 200_000, Sim.CurrentTick);
            foreach (var pair in progress)
                if (Sim.FindShip(pair.Key).IsInTransit) Assert.Greater(Sim.PositionOf(pair.Key).ProgressHundredthsOfPercent, pair.Value, "the other ships keep flying");
            Assert.Greater(Planner.BoardBuilds, builds, "a new game day: the job list was read again from the simulation");
            Assert.AreEqual(JobText.Terms(Sim.Quote(Planner.Plan())), JobText.Terms(Planner.Quote), "the quote on screen is the simulation's quote now");
            Assert.AreEqual(Sim.Quote(Planner.Plan()).DescribeArrival() + " if launched now", Planner.LineText("quote-arrival"));
            Assert.AreNotEqual(arrivalBefore, Planner.LineText("quote-arrival"), "twenty days later, the arrival date has moved on");
            StringAssert.Contains(" at 5x", Planner.LineText("quote-arrival"));

            Press(PlannerButton("launch"));
            yield return MapTestRig.Frames(3);
            if (Planner.IsConfirming) Press(PlannerButton("confirm-yes"));
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(docked[1].IsInTransit, "launched with the clock running");
            Assert.AreEqual(6, Sim.Ships.Count(s => s.IsInTransit));

            // The system view while everything flies.
            Map.Select(port);
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(Map.Hud.InfoPanelShowing);
            Assert.AreEqual(jobs, Map.Hud.SystemJobs.Shown.Count);
            Assert.IsFalse(Map.Hud.InfoLines.Any(l => l.StartsWith("Docked here")), "both ships have left");
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>While a number is being typed into a load box, the map's keys (1, 2, 3 for speed, Space) leave it alone.</summary>
        [UnityTest]
        public IEnumerator WhileTypingALoad_TheMapsKeysAreNotPressed()
        {
            yield return _rig.Open(GameTestTuning.NewGameWithSystems(12));
            yield return OpenPlannerFromTheShipPanel();
            var job = Planner.Jobs.Shown.First(r => r.CanFly && !r.IsPassengers);
            ClickRow(Planner.Jobs, job);
            yield return MapTestRig.Frames(3);
            var field = Planner.Panel.Q("load-" + job.CommodityId).Q<IntegerField>("amount");
            field.Focus();
            yield return MapTestRig.Frames(3);
            Assert.IsTrue(Planner.IsTyping);
            yield return _rig.Keys(1, UnityEngine.InputSystem.Key.Digit3);
            yield return _rig.Keys(1, UnityEngine.InputSystem.Key.Space);
            Assert.AreEqual(ClockSpeed.X1, Sim.Clock.Speed, "the 3 went to the number box, not the speed");
            Assert.IsTrue(Sim.Clock.IsPaused, "and Space did not start the clock");

            field.Blur();
            yield return MapTestRig.Frames(3);
            Assert.IsFalse(Planner.IsTyping);
            yield return _rig.Keys(1, UnityEngine.InputSystem.Key.Digit3);
            Assert.AreEqual(ClockSpeed.X5, Sim.Clock.Speed, "with the box left, 3 chooses 5x again");
            LogAssert.NoUnexpectedReceived();
        }
    }
}
