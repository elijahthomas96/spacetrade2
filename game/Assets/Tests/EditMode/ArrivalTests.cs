using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Ships;
using SpaceTrade.Sim.Voyages;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;

namespace SpaceTrade.Sim.Tests
{
    public class ArrivalTests
    {
        private const long HandArrivalTick = 10_800_000;
        private static readonly SupplyKind[] Kinds = { SupplyKind.Fuel, SupplyKind.Food, SupplyKind.Water };

        private static void Log(VoyageReceipt r)
        {
            TestContext.WriteLine($"receipt ship {r.ShipId} {r.OriginId}->{r.DestinationId} elapsed={r.ElapsedTicks} freight={r.FreightRevenue} fares={r.FareRevenue} " +
                $"gross={r.GrossRevenue} supplies={r.SuppliesCost} net={r.NetProfit} " + string.Join(" ", r.Supplies.Select(s => $"{s.Kind}={s.Kg}kg/{s.Cost}cr")));
        }

        private static void AssertReceiptMatchesQuote(VoyageQuote q, VoyageReceipt r)
        {
            Assert.AreEqual(q.FreightRevenue, r.FreightRevenue, "freight revenue");
            Assert.AreEqual(q.FareRevenue, r.FareRevenue, "fares");
            Assert.AreEqual(q.GrossRevenue, r.GrossRevenue, "gross");
            Assert.AreEqual(q.OperatingCost, r.SuppliesCost, "supplies consumed");
            Assert.AreEqual(q.NetProfit, r.NetProfit, "net profit");
            Assert.AreEqual(q.TransitTicks, r.ElapsedTicks, "elapsed game time");
            Assert.AreEqual(q.DepartureTick, r.DepartureTick);
            Assert.AreEqual(q.ArrivalTick, r.ArrivalTick);
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(q.Supplies[i].Kind, r.Supplies[i].Kind);
                Assert.AreEqual(q.Supplies[i].RequiredKg, r.Supplies[i].Kg, q.Supplies[i].Kind + " kg");
                Assert.AreEqual(q.Supplies[i].UsedCost, r.Supplies[i].Cost, q.Supplies[i].Kind + " cost");
            }
        }

        // ---- Receipt matches the quote ----

        [Test]
        public void Receipt_HandExample_MatchesTheQuote()
        {
            var sim = NewSim(100_000);
            var q = sim.Quote(Plan(20, 2));
            Assert.IsTrue(sim.Dispatch(Plan(20, 2), true).Succeeded);
            sim.Advance(HandArrivalTick);

            var r = sim.Receipts.Single();
            Log(r);
            AssertReceiptMatchesQuote(q, r);
            Assert.AreEqual(84_000, r.FreightRevenue);
            Assert.AreEqual(56_000, r.FareRevenue);
            Assert.AreEqual(43_733, r.SuppliesCost);
            Assert.AreEqual(96_267, r.NetProfit);
            Assert.AreEqual(10_800_000, r.ElapsedTicks, "3 game years");
            Assert.AreEqual(Beta, Courier(sim).DockedSystemId);
            Assert.IsNull(Courier(sim).Voyage);
        }

        [Test]
        public void Receipt_WithLeftoverSupplies_MatchesTheQuote()
        {
            var sim = NewSim(100_000);
            var ship = Courier(sim);
            ship.Water.Add(3_000, 1_500);
            ship.Food.Add(1_000, 5_000);
            var q = sim.Quote(Plan(20, 2));
            Assert.IsTrue(sim.Dispatch(Plan(20, 2), true).Succeeded);
            sim.Advance(HandArrivalTick);

            var r = sim.Receipts.Single();
            Log(r);
            AssertReceiptMatchesQuote(q, r);
            Assert.AreEqual(38_175, r.SuppliesCost);
            Assert.AreEqual(101_825, r.NetProfit);
            Assert.AreEqual(864, r.Supplies[2].Cost, "Water: its share of the 1,500 it cost.");
            Assert.AreEqual(1_272, ship.Water.Kg, "Leftover water stays aboard...");
            Assert.AreEqual(636, ship.Water.CostCredits, "...with the rest of its cost.");
        }

        // ---- Exactly once ----

        [Test]
        public void Payout_HappensExactlyOnce_HoweverTimeIsStepped()
        {
            var oneStep = NewSim(100_000);
            Assert.IsTrue(oneStep.Dispatch(Plan(20, 2), true).Succeeded);
            oneStep.Advance(HandArrivalTick);

            var manySteps = NewSim(100_000);
            Assert.IsTrue(manySteps.Dispatch(Plan(20, 2), true).Succeeded);
            for (long tick = 10_800; tick <= HandArrivalTick; tick += 10_800) manySteps.Advance(tick);

            CollectionAssert.AreEqual(StateBytes(oneStep), StateBytes(manySteps), "1 step and 1,000 steps give the same state.");
            Assert.AreEqual(100_000 - 43_733 + 140_000, oneStep.Credits);

            // Ten more game years, in one jump and then repeatedly: nothing more is paid.
            oneStep.Advance(HandArrivalTick + 36_000_000);
            for (int i = 0; i < 5; i++) oneStep.Advance(oneStep.CurrentTick);
            oneStep.Advance(oneStep.CurrentTick + 1);
            Assert.AreEqual(196_267, oneStep.Credits);
            Assert.AreEqual(1, oneStep.Receipts.Count);
            StringAssert.DoesNotContain("ArrivalEvent", State(oneStep));
        }

        [Test]
        public void Arrival_OneTickEarly_HasNotHappened()
        {
            var sim = NewSim(100_000);
            Assert.IsTrue(sim.Dispatch(Plan(20, 2), true).Succeeded);
            sim.Advance(HandArrivalTick - 1);
            Assert.IsTrue(Courier(sim).IsInTransit);
            Assert.AreEqual(56_267, sim.Credits);
            Assert.AreEqual(0, sim.Receipts.Count);
            sim.Advance(HandArrivalTick);
            Assert.IsFalse(Courier(sim).IsInTransit);
            Assert.AreEqual(196_267, sim.Credits);
        }

        // ---- Money is conserved ----

        [Test]
        public void Money_IsConservedAcrossAFullVoyage()
        {
            var sim = NewSim(100_000);
            var ship = Courier(sim);
            ship.Water.Add(3_000, 1_500);
            ship.Food.Add(1_000, 5_000);
            long creditsBefore = sim.Credits;
            long worthBefore = CreditsPlusSuppliesAtCost(sim);
            var q = sim.Quote(Plan(20, 2));

            Assert.IsTrue(sim.Dispatch(Plan(20, 2), true).Succeeded);
            long payout = Courier(sim).Voyage.Payout;
            Assert.AreEqual(worthBefore, CreditsPlusSuppliesAtCost(sim), "Dispatch only moves credits into supplies at cost.");
            sim.Advance(HandArrivalTick);
            var r = sim.Receipts.Single();

            TestContext.WriteLine($"before={creditsBefore} topUp={q.TopUpCost} payout={payout} after={sim.Credits}");
            Assert.AreEqual(creditsBefore - q.TopUpCost + payout, sim.Credits);
            Assert.AreEqual(140_000, payout);

            // spent = expensed + cost aboard, for each supply. "Spent" includes the 1,500 and 5,000 bought before.
            var spentBefore = new Dictionary<SupplyKind, long> { { SupplyKind.Fuel, 0 }, { SupplyKind.Food, 5_000 }, { SupplyKind.Water, 1_500 } };
            for (int i = 0; i < 3; i++)
            {
                var kind = Kinds[i];
                long spent = spentBefore[kind] + q.Supplies[i].TopUpCost;
                Assert.AreEqual(spent, r.Supplies[i].Cost + ship.Supply(kind).CostCredits, kind.ToString());
            }
            Assert.AreEqual(worthBefore + payout - r.SuppliesCost, CreditsPlusSuppliesAtCost(sim));
        }

        // ---- Supplies bought two stops earlier ----

        [Test]
        public void SuppliesBoughtTwoStopsEarlier_AreExpensedOnce_OnTheVoyagesThatUseThem()
        {
            // At Alpha, provision for 4 passengers to Beta, then fly with none: food is left over.
            // Alpha -> Beta (4.5 ly, 1,080 days), Beta -> Gamma (3 ly, 720 days), Gamma -> Alpha (7.5 ly, 1,800 days).
            var sim = NewThreeSystemSim(1_000_000);
            var ship = Courier(sim);
            var spent = new Dictionary<SupplyKind, long> { { SupplyKind.Fuel, 0 }, { SupplyKind.Food, 0 }, { SupplyKind.Water, 0 } };
            var expensed = new Dictionary<SupplyKind, long> { { SupplyKind.Fuel, 0 }, { SupplyKind.Food, 0 }, { SupplyKind.Water, 0 } };

            void Spend(VoyageQuote q) { foreach (var s in q.Supplies) spent[s.Kind] += s.TopUpCost; }
            void Check(string when)
            {
                foreach (var k in Kinds)
                    Assert.AreEqual(spent[k], expensed[k] + ship.Supply(k).CostCredits, $"{k} {when}: spent = expensed + cost aboard");
            }
            VoyageReceipt Fly(VoyagePlan plan)
            {
                var q = sim.Quote(plan);
                Assert.IsTrue(q.IsFeasible, string.Join("\n", q.Problems));
                Spend(q);
                Assert.IsTrue(sim.Dispatch(plan, true).Succeeded);
                Check("after dispatch");
                sim.Advance(q.ArrivalTick);
                var r = sim.Receipts.Last();
                Log(r);
                AssertReceiptMatchesQuote(q, r);
                foreach (var s in r.Supplies) expensed[s.Kind] += s.Cost;
                Check("after arrival");
                return r;
            }

            var big = new VoyagePlan(CourierId, Beta, null, 4);
            var provisionQuote = sim.Quote(big);
            Spend(provisionQuote);
            Assert.IsTrue(sim.Provision(big).Succeeded);
            // 6 people x 600 g x 1,080 days = 3,888 kg of food at 9,600 per tonne = 37,324.8 -> 37,325.
            Assert.AreEqual(3_888, ship.Food.Kg);
            Assert.AreEqual(37_325, ship.Food.CostCredits);
            long foodBoughtAtAlpha = ship.Food.CostCredits;

            // Leg 1: crew only uses 1,296 kg: 37,325 x 1,296 / 3,888 = 12,441.67 -> 12,442. 2,592 kg (24,883) left.
            var leg1 = Fly(new VoyagePlan(CourierId, Beta));
            Assert.AreEqual(12_442, leg1.Supplies[1].Cost);
            Assert.AreEqual(2_592, ship.Food.Kg);
            Assert.AreEqual(24_883, ship.Food.CostCredits);

            // Leg 2 from Beta buys no food: it uses 864 kg of the Alpha food: 24,883 x 864 / 2,592 = 8,294.33 -> 8,294.
            var leg2Quote = sim.Quote(new VoyagePlan(CourierId, Gamma));
            Assert.AreEqual(0, leg2Quote.Food.ShortfallKg, "Enough Alpha food is still aboard.");
            var leg2 = Fly(new VoyagePlan(CourierId, Gamma));
            Assert.AreEqual(8_294, leg2.Supplies[1].Cost);
            Assert.AreEqual(1_728, ship.Food.Kg);
            Assert.AreEqual(16_589, ship.Food.CostCredits);

            // Leg 3 from Gamma needs 2,160 kg: tops up 432 kg there and uses everything, so it is charged everything left.
            var leg3Quote = sim.Quote(new VoyagePlan(CourierId, Alpha));
            Assert.AreEqual(432, leg3Quote.Food.ShortfallKg);
            var leg3 = Fly(new VoyagePlan(CourierId, Alpha));
            Assert.AreEqual(16_589 + leg3Quote.Food.TopUpCost, leg3.Supplies[1].Cost);
            Assert.AreEqual(0, ship.Food.Kg);
            Assert.AreEqual(0, ship.Food.CostCredits);

            // The Alpha food was expensed once in total, split over the three voyages that used it.
            Assert.AreEqual(foodBoughtAtAlpha + leg3Quote.Food.TopUpCost, leg1.Supplies[1].Cost + leg2.Supplies[1].Cost + leg3.Supplies[1].Cost);
            Assert.AreEqual(3, sim.Receipts.Count);
            Assert.AreEqual(Alpha, ship.DockedSystemId);
        }

        // ---- Empty voyages (owner's decision 4a) ----

        [Test]
        public void EmptyVoyage_ReservesNothing_PaysZero_ExpensesItsSuppliesOnce_AndShowsTheQuotedLoss()
        {
            var sim = NewSim(100_000);
            var q = sim.Quote(Plan(0, 0));
            Assert.IsTrue(q.IsFeasible, string.Join("\n", q.Problems));
            string marketBefore = sim.Market.DescribeState();

            Assert.IsTrue(sim.Dispatch(Plan(0, 0), true).Succeeded);
            Assert.AreEqual(marketBefore, sim.Market.DescribeState(), "An empty voyage reserves nothing.");
            Assert.AreEqual(0, Courier(sim).Voyage.Freight.Count);
            Assert.AreEqual(0, Courier(sim).Voyage.Passengers);
            Assert.AreEqual(0, Courier(sim).Voyage.Payout);
            long creditsAfterDispatch = sim.Credits;
            Assert.AreEqual(100_000 - 25_827, creditsAfterDispatch);

            sim.Advance(HandArrivalTick);
            var r = sim.Receipts.Single();
            Log(r);
            Assert.AreEqual(creditsAfterDispatch, sim.Credits, "Pays zero.");
            Assert.AreEqual(0, r.GrossRevenue);
            Assert.AreEqual(25_827, r.SuppliesCost);
            Assert.AreEqual(-25_827, r.NetProfit);
            Assert.AreEqual(q.NetProfit, r.NetProfit, "The receipt shows the loss the quote showed.");
            AssertReceiptMatchesQuote(q, r);
            foreach (var k in Kinds)
            {
                Assert.AreEqual(0, Courier(sim).Supply(k).Kg, k.ToString());
                Assert.AreEqual(0, Courier(sim).Supply(k).CostCredits, k + ": expensed once, nothing left to expense again");
            }

            sim.Advance(HandArrivalTick + 36_000_000);
            Assert.AreEqual(1, sim.Receipts.Count);
            Assert.AreEqual(creditsAfterDispatch, sim.Credits);
        }

        // ---- Two ships ----

        [Test]
        public void TwoShips_TravelIndependently()
        {
            var sim = NewThreeSystemSim(1_000_000);
            var first = Courier(sim);
            var second = sim.AddShip("Second Courier", "courier", "alcubierre_1_50", Alpha);

            var q1 = sim.Quote(Plan(20, 2));
            Assert.IsTrue(sim.Dispatch(Plan(20, 2), true).Succeeded);
            sim.Advance(1_000_000);

            // The docked ship can be quoted and dispatched while the other flies.
            var plan2 = new VoyagePlan(second.Id, Gamma, null, 2);
            var q2 = sim.Quote(plan2);
            Assert.IsTrue(q2.IsFeasible, string.Join("\n", q2.Problems));
            Assert.AreEqual(1_000_000, q2.DepartureTick);
            Assert.AreEqual(1_000_000 + 18_000_000, q2.ArrivalTick, "7.5 ly at 1.5c = 5 game years");
            Assert.IsTrue(sim.Dispatch(plan2, true).Succeeded);
            long afterBoth = sim.Credits;
            Assert.AreEqual(1_000_000 - q1.TopUpCost - q2.TopUpCost, afterBoth);

            // The first arrives on its own; the second is untouched.
            string secondBefore = second.DescribeState();
            sim.Advance(q1.ArrivalTick);
            Assert.AreEqual(Beta, first.DockedSystemId);
            Assert.IsTrue(second.IsInTransit);
            Assert.AreEqual(secondBefore, second.DescribeState());
            Assert.AreEqual(afterBoth + q1.GrossRevenue, sim.Credits);
            Assert.AreEqual(first.Id, sim.Receipts.Single().ShipId);

            // The first sets off again from Beta (3 ly, empty) while the second is still out, and overtakes it.
            var again = sim.Quote(new VoyagePlan(first.Id, Gamma));
            Assert.IsTrue(sim.Dispatch(new VoyagePlan(first.Id, Gamma), true).Succeeded);
            long afterAgain = sim.Credits;
            Assert.AreEqual(HandArrivalTick + 7_200_000, again.ArrivalTick);
            Assert.Less(again.ArrivalTick, q2.ArrivalTick);

            sim.Advance(again.ArrivalTick);
            Assert.AreEqual(Gamma, first.DockedSystemId);
            Assert.IsTrue(second.IsInTransit);
            Assert.AreEqual(afterAgain, sim.Credits, "The empty leg pays nothing.");

            sim.Advance(q2.ArrivalTick);
            Assert.AreEqual(Gamma, second.DockedSystemId);
            Assert.AreEqual(afterAgain + q2.GrossRevenue, sim.Credits);
            CollectionAssert.AreEqual(new[] { first.Id, first.Id, second.Id }, sim.Receipts.Select(r => r.ShipId).ToArray());
            AssertReceiptMatchesQuote(q1, sim.Receipts[0]);
            AssertReceiptMatchesQuote(again, sim.Receipts[1]);
            AssertReceiptMatchesQuote(q2, sim.Receipts[2]);
            foreach (var r in sim.Receipts) Log(r);
        }

        [Test]
        public void SameTickArrivals_SettleInShipIdOrder()
        {
            var sim = NewSim(1_000_000);
            var second = sim.AddShip("Second Courier", "courier", "alcubierre_1_50", Alpha);
            Assert.IsTrue(sim.Dispatch(new VoyagePlan(second.Id, Beta, null, 1), true).Succeeded, "Ship 2 dispatched first");
            Assert.IsTrue(sim.Dispatch(Plan(5, 1), true).Succeeded);
            sim.Advance(HandArrivalTick);
            CollectionAssert.AreEqual(new[] { 1, 2 }, sim.Receipts.Select(r => r.ShipId).ToArray());
        }
    }
}
