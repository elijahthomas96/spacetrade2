using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.Voyages;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;

namespace SpaceTrade.Sim.Tests
{
    public class VoyageQuoteTests
    {
        private static void Log(VoyageQuote q)
        {
            TestContext.WriteLine($"feasible={q.IsFeasible} transit={q.TransitTicks} arrival={q.ArrivalDate} fuel={q.Fuel.RequiredKg}kg food={q.Food.RequiredKg}kg water={q.Water.RequiredKg}kg " +
                $"hold={q.Needs.HoldUsedKg}/{q.HoldCapacityKg}kg topUp={q.TopUpCost} gross={q.GrossRevenue} cost={q.OperatingCost} net={q.NetProfit} perYear={q.ProfitPerGameYear}");
            foreach (var p in q.Problems) TestContext.WriteLine("  problem: " + p);
        }

        // ---- Hand-calculated examples ----

        [Test]
        public void HandExample_FreshCourier_20TonnesOre_2Passengers_4Point5Ly()
        {
            var sim = NewSim(100_000);
            var q = sim.Quote(Plan(20, 2));
            Log(q);
            Assert.IsTrue(q.IsFeasible, string.Join("\n", q.Problems));

            // 4.5 ly at 1.5c = 3 game years = 1,080 days = 10,800,000 ticks; departs tick 0, arrives Y4 M01 D01.
            Assert.AreEqual(4_500, q.DistanceMilliLy);
            Assert.AreEqual(10_800_000, q.TransitTicks);
            Assert.AreEqual(new GameDate(4, 1, 1), q.ArrivalDate);
            Assert.AreEqual(10_800_000, q.RealMillisecondsAtCurrentSpeed, "3 real hours at 1x");

            // People = 2 crew + 2 passengers. Food 4 x 600 g x 1,080 days = 2,592 kg; water 4 x 400 g x 1,080 = 1,728 kg.
            Assert.AreEqual(4, q.Needs.People);
            Assert.AreEqual(2_592, q.Food.RequiredKg);
            Assert.AreEqual(1_728, q.Water.RequiredKg);
            // Hold: 20,000 freight + 2,592 + 1,728 = 24,320 kg of 30,000.
            Assert.AreEqual(24_320, q.Needs.HoldUsedKg);
            // Loaded mass: 60,000 dry + 24,320 + 4 x 100 people = 84,720 kg.
            Assert.AreEqual(84_720, q.Needs.LoadedMassKg);
            // Fuel: 4.5 ly x 84.72 t x 22 kg = 8,387.28 kg, rounded up to 8,388.
            Assert.AreEqual(8_388, q.Fuel.RequiredKg);

            // Prices at Alpha (mining colony: fuel 100%, food 120%, water 120%; roll 100%): 2,000 / 9,600 / 1,200 per tonne.
            Assert.AreEqual(2_000, q.Fuel.PricePerTonne);
            Assert.AreEqual(9_600, q.Food.PricePerTonne);
            Assert.AreEqual(1_200, q.Water.PricePerTonne);
            // Nothing aboard: buy it all. 8,388 x 2 = 16,776; 2,592 x 9.6 = 24,883.2 -> 24,883; 1,728 x 1.2 = 2,073.6 -> 2,074.
            Assert.AreEqual(8_388, q.Fuel.ShortfallKg);
            Assert.AreEqual(16_776, q.Fuel.TopUpCost);
            Assert.AreEqual(24_883, q.Food.TopUpCost);
            Assert.AreEqual(2_074, q.Water.TopUpCost);
            Assert.AreEqual(43_733, q.TopUpCost);

            // Ore: 1,500 x (100% + 40% x 4.5) = 4,200 per tonne; 20 t = 84,000. Fare: 10,000 x 280% = 28,000; 2 = 56,000.
            Assert.AreEqual(4_200, q.Freight.Single().RatePerTonne);
            Assert.AreEqual(84_000, q.FreightRevenue);
            Assert.AreEqual(28_000, q.FarePerPassenger);
            Assert.AreEqual(56_000, q.FareRevenue);
            Assert.AreEqual(140_000, q.GrossRevenue);

            // Everything bought is used: operating cost = 43,733. Net 96,267; per year 96,267 / 3 = 32,089.
            Assert.AreEqual(43_733, q.OperatingCost);
            Assert.AreEqual(96_267, q.NetProfit);
            Assert.AreEqual(32_089, q.ProfitPerGameYear);
        }

        [Test]
        public void HandExample_LeftoverSupplies_AreUsedAtTheirOwnCost()
        {
            var sim = NewSim(100_000);
            var ship = Courier(sim);
            ship.Water.Add(3_000, 1_500); // bought earlier at 500 per tonne
            ship.Food.Add(1_000, 5_000);  // bought earlier at 5,000 per tonne
            var q = sim.Quote(Plan(20, 2));
            Log(q);
            Assert.IsTrue(q.IsFeasible, string.Join("\n", q.Problems));

            // Water: 1,728 needed, 3,000 aboard: nothing to buy; all 3,000 kg stay aboard and count.
            Assert.AreEqual(0, q.Water.ShortfallKg);
            Assert.AreEqual(0, q.Water.TopUpCost);
            Assert.AreEqual(3_000, q.Needs.WaterAboardAfterKg);
            // Water used: 1,500 x 1,728 / 3,000 = 864.
            Assert.AreEqual(864, q.Water.UsedCost);

            // Food: 2,592 needed, 1,000 aboard: buy 1,592 kg x 9.6 = 15,283.2 -> 15,283. All 2,592 kg used: 5,000 + 15,283 = 20,283.
            Assert.AreEqual(1_592, q.Food.ShortfallKg);
            Assert.AreEqual(15_283, q.Food.TopUpCost);
            Assert.AreEqual(20_283, q.Food.UsedCost);

            // Hold: 20,000 + 2,592 + 3,000 = 25,592. Loaded: 60,000 + 25,592 + 400 = 85,992.
            Assert.AreEqual(25_592, q.Needs.HoldUsedKg);
            Assert.AreEqual(85_992, q.Needs.LoadedMassKg);
            // Fuel: 4.5 x 85.992 x 22 = 8,513.208 -> 8,514 kg; 8,514 x 2 = 17,028.
            Assert.AreEqual(8_514, q.Fuel.RequiredKg);
            Assert.AreEqual(17_028, q.Fuel.TopUpCost);
            Assert.AreEqual(17_028, q.Fuel.UsedCost);

            // Top-up 17,028 + 15,283 = 32,311. Operating 17,028 + 20,283 + 864 = 38,175. Net 101,825; per year 33,941.67 -> 33,942.
            Assert.AreEqual(32_311, q.TopUpCost);
            Assert.AreEqual(38_175, q.OperatingCost);
            Assert.AreEqual(101_825, q.NetProfit);
            Assert.AreEqual(33_942, q.ProfitPerGameYear);
        }

        [Test]
        public void HandExample_EmptyRun_IsALoss_AndProfitPerYearIsNegative()
        {
            var sim = NewSim(100_000);
            var q = sim.Quote(Plan(0, 0));
            Log(q);
            Assert.IsTrue(q.IsFeasible, string.Join("\n", q.Problems));
            // Crew only: food 1,296 kg, water 864 kg. Mass 60,000 + 2,160 + 200 = 62,360. Fuel 4.5 x 62.36 x 22 = 6,173.64 -> 6,174.
            Assert.AreEqual(6_174, q.Fuel.RequiredKg);
            // 6,174 x 2 = 12,348; 1,296 x 9.6 = 12,441.6 -> 12,442; 864 x 1.2 = 1,036.8 -> 1,037. Total 25,827.
            Assert.AreEqual(25_827, q.OperatingCost);
            Assert.AreEqual(0, q.GrossRevenue);
            Assert.AreEqual(-25_827, q.NetProfit);
            Assert.AreEqual(-8_609, q.ProfitPerGameYear);
        }

        [Test]
        public void HandExample_ProfitPerYear_NegativeHalf_RoundsAwayFromZero()
        {
            // 1 ly at 1.5c = 2,400,000 ticks (240 days), so per year = net x 1.5.
            var sim = NewSim(100_000, distanceMilliLy: 1_000);
            var q = sim.Quote(Plan(0, 0));
            Log(q);
            Assert.AreEqual(2_400_000, q.TransitTicks);
            // Crew only: food 288 kg, water 192 kg; mass 60,680 kg; fuel 1 x 60.68 x 22 = 1,334.96 -> 1,335 kg.
            // 1,335 x 2 = 2,670; 288 x 9.6 = 2,764.8 -> 2,765; 192 x 1.2 = 230.4 -> 230. Loss 5,665.
            Assert.AreEqual(-5_665, q.NetProfit);
            // -5,665 x 1.5 = -8,497.5, an exact half: rounds away from zero to -8,498.
            Assert.AreEqual(-8_498, q.ProfitPerGameYear);
        }

        [Test]
        public void SpeedChange_ChangesRealTimeOnly()
        {
            var sim = NewSim(100_000);
            var at1x = sim.Quote(Plan(20, 2));
            sim.SetSpeed(ClockSpeed.X5);
            var at5x = sim.Quote(Plan(20, 2));
            Assert.AreEqual(at1x.ArrivalTick, at5x.ArrivalTick);
            Assert.AreEqual(at1x.NetProfit, at5x.NetProfit);
            Assert.AreEqual(2_160_000, at5x.RealMillisecondsAtCurrentSpeed, "36 real minutes at 5x");
        }

        [Test]
        public void Quote_ChangesNothing()
        {
            var sim = NewSim(10_000);
            Courier(sim).Water.Add(500, 400);
            string before = State(sim);
            sim.Quote(Plan(20, 2));
            sim.Quote(Plan(60, 9));
            sim.Quote(new VoyagePlan(CourierId, Beta, new[] { new FreightLine("metals", 5) }, 1));
            Assert.AreEqual(before, State(sim));
        }

        // ---- Hold ----

        [Test]
        public void Hold_ExactlyFull_Fits_OneKilogramOver_Fails()
        {
            // 25 t freight + 2,592 kg food + 2,408 kg water aboard (more than the 1,728 needed) = 30,000 kg exactly.
            var sim = NewSim(100_000);
            Courier(sim).Water.Add(2_408, 0);
            var full = sim.Quote(Plan(25, 2));
            Assert.AreEqual(30_000, full.Needs.HoldUsedKg);
            Assert.IsTrue(full.HoldFits);
            Assert.IsNull(HoldProblem(full));

            Courier(sim).Water.Add(1, 0);
            var over = sim.Quote(Plan(25, 2));
            Log(over);
            Assert.AreEqual(30_001, over.Needs.HoldUsedKg);
            Assert.IsFalse(over.HoldFits);
            Assert.IsFalse(over.IsFeasible);
            StringAssert.Contains("Unload 1 t of freight to make room for food and water.", HoldProblem(over));
        }

        [Test]
        public void Hold_SuppliesOwnMassCounts_FullHoldOfFreightDoesNotFit()
        {
            // 30 t of freight fills the hold, but the crew's 1,296 kg food and 864 kg water need room too.
            var sim = NewSim(100_000);
            var q = sim.Quote(Plan(30, 0));
            Log(q);
            Assert.IsFalse(q.HoldFits);
            Assert.AreEqual(
                "The hold holds 30 t; this voyage needs 30 t of freight plus 2.16 t of food and water (32.16 t). Unload 3 t of freight to make room for food and water.",
                HoldProblem(q));
            Assert.IsTrue(sim.Quote(Plan(27, 0)).HoldFits);
            Assert.IsFalse(sim.Quote(Plan(28, 0)).HoldFits);
        }

        [Test]
        public void Hold_UnloadN_IsAlwaysTheSmallestAmountThatFits()
        {
            int checkedCases = 0;
            foreach (int extraWater in new[] { 0, 999, 2_500 })
            for (int passengers = 0; passengers <= 4; passengers++)
            {
                var sim = NewSim(1_000_000);
                Courier(sim).Water.Add(extraWater, 0);
                for (int freight = 0; freight <= 60; freight++)
                {
                    var q = sim.Quote(Plan(freight, passengers));
                    string problem = HoldProblem(q);
                    Assert.AreEqual(q.HoldFits, problem == null);
                    if (q.HoldFits) continue;

                    int smallest = Enumerable.Range(1, freight).First(n => sim.Quote(Plan(freight - n, passengers)).HoldFits);
                    var m = Regex.Match(problem, @"Unload (\d+) t of freight to make room for food and water\.");
                    Assert.IsTrue(m.Success, problem);
                    Assert.AreEqual(smallest, int.Parse(m.Groups[1].Value), $"freight {freight}, passengers {passengers}, extra water {extraWater}: {problem}");
                    Assert.IsFalse(sim.Quote(Plan(freight - smallest + 1, passengers)).HoldFits, "One tonne less unloaded must still not fit.");
                    checkedCases++;
                }
            }
            TestContext.WriteLine($"Checked {checkedCases} over-full holds.");
            Assert.Greater(checkedCases, 100);
        }

        [Test]
        public void Hold_WhenFreightIsNotEnough_FixCarriesFewerPassengers()
        {
            // Hold 6 t. 2 crew + 4 passengers at 1 kg/day for 1,080 days = 6,480 kg, over even with no freight.
            var t = FlatTuning();
            t.FindShip("courier").HoldTonnes = 6;
            var sim = NewSim(1_000_000, t);
            var q = sim.Quote(Plan(2, 4));
            Log(q);
            // 1 fewer passenger: 5 people = 5,400 kg, leaving 600 kg, so both freight tonnes must go.
            StringAssert.EndsWith("Carry 1 fewer passenger and unload 2 t of freight to make room for food and water.", HoldProblem(q));
            Assert.IsTrue(sim.Quote(Plan(0, 3)).HoldFits);
            Assert.IsFalse(sim.Quote(Plan(1, 3)).HoldFits);
            Assert.IsFalse(sim.Quote(Plan(0, 4)).HoldFits);
        }

        [Test]
        public void Hold_TooFarEvenForCrewAlone_SaysSo()
        {
            var t = FlatTuning();
            t.FindShip("courier").HoldTonnes = 2; // crew alone need 2.16 t
            var sim = NewSim(1_000_000, t);
            string problem = HoldProblem(sim.Quote(Plan(5, 1)));
            TestContext.WriteLine(problem);
            StringAssert.Contains("Even with no freight or passengers, food and water need 2.16 t, more than the hold holds", problem);
        }

        // ---- Tank ----

        [Test]
        public void Tank_OverCapacity_FailsWithNumbersAndSmallestUnload()
        {
            // Tank 8 t. 20 t ore + 2 passengers needs 8,388 kg. 16 t needs 7,992 kg (fits), 17 t needs 8,091 kg.
            var t = FlatTuning();
            t.FindShip("courier").TankTonnes = 8;
            var sim = NewSim(1_000_000, t);
            var q = sim.Quote(Plan(20, 2));
            Log(q);
            Assert.IsFalse(q.TankFits);
            Assert.IsFalse(q.IsFeasible);
            Assert.AreEqual("The fuel tank holds 8 t; this voyage needs 8.388 t of fuel. Unload 4 t of freight to bring the fuel needed within the tank.", TankProblem(q));
            Assert.AreEqual(7_992, sim.Quote(Plan(16, 2)).Fuel.RequiredKg);
            Assert.IsTrue(sim.Quote(Plan(16, 2)).TankFits);
            Assert.IsFalse(sim.Quote(Plan(17, 2)).TankFits);
        }

        [Test]
        public void Tank_FixIsAlwaysTheSmallestUnload()
        {
            var t = FlatTuning();
            t.FindShip("courier").TankTonnes = 8;
            int checkedCases = 0;
            for (int passengers = 0; passengers <= 4; passengers++)
            {
                var sim = NewSim(1_000_000, t);
                for (int freight = 0; freight <= 30; freight++)
                {
                    var q = sim.Quote(Plan(freight, passengers));
                    if (q.TankFits) { Assert.IsNull(TankProblem(q)); continue; }
                    int smallest = Enumerable.Range(1, freight).First(n => sim.Quote(Plan(freight - n, passengers)).TankFits);
                    StringAssert.Contains($"Unload {smallest} t of freight to bring the fuel needed within the tank.", TankProblem(q));
                    checkedCases++;
                }
            }
            TestContext.WriteLine($"Checked {checkedCases} over-full tanks.");
            Assert.Greater(checkedCases, 20);
        }

        [Test]
        public void Tank_OutOfRangeEvenEmpty_SaysSo()
        {
            var t = FlatTuning();
            t.FindShip("courier").TankTonnes = 5; // crew only needs 6,174 kg
            var sim = NewSim(1_000_000, t);
            string problem = TankProblem(sim.Quote(Plan(10, 2)));
            TestContext.WriteLine(problem);
            StringAssert.Contains("Even with no freight or passengers it needs 6.174 t; this destination is out of range for Test Courier with its current drive.", problem);
        }

        // ---- Berths ----

        [Test]
        public void Berths_FullFits_OneMoreFails()
        {
            var sim = NewSim(1_000_000);
            Assert.IsTrue(sim.Quote(Plan(0, 4)).IsFeasible, string.Join("\n", sim.Quote(Plan(0, 4)).Problems));
            var q = sim.Quote(Plan(0, 5)); // the pool has 6 waiting, so only the berths limit applies
            Log(q);
            Assert.IsFalse(q.BerthsFit);
            Assert.IsFalse(q.IsFeasible);
            CollectionAssert.Contains(q.Problems, "Test Courier has 4 berths; 5 passengers requested. Book 1 fewer passenger.");
        }

        // ---- Credits ----

        [Test]
        public void Credits_ExactlyEnough_Fits_OneShort_Fails()
        {
            Assert.IsTrue(NewSim(43_733).Quote(Plan(20, 2)).IsFeasible);
            var q = NewSim(43_732).Quote(Plan(20, 2));
            Assert.IsFalse(q.CanAffordTopUp);
            CollectionAssert.AreEqual(new[] { "Topping up supplies costs 43,733 credits; you have 43,732. You need 1 more credit." }, q.Problems);
        }

        // ---- Market and plan problems ----

        [Test]
        public void MarketShortage_IsReportedByTheQuote()
        {
            var sim = NewSim(1_000_000);
            var q = sim.Quote(new VoyagePlan(CourierId, Beta, new[] { new FreightLine("ore", 10), new FreightLine("metals", 5) }, 0));
            Log(q);
            Assert.IsFalse(q.IsFeasible);
            CollectionAssert.Contains(q.Problems, "Alpha does not produce metals.");
        }

        [Test]
        public void MarketSupplyLimit_IsReportedByTheQuote()
        {
            var t = FlatTuning();
            t.FindCommodity("ore").SupplyCapTonnes = 12;
            var q = NewSim(1_000_000, t).Quote(Plan(15, 0));
            Assert.IsFalse(q.IsFeasible);
            CollectionAssert.Contains(q.Problems, "Only 12 t of ore available from Alpha to Beta (supply 12 t at Alpha, demand 60 t at Beta); requested 15 t.");
        }

        [Test]
        public void InvalidPlans_AreRefusedWithAReason()
        {
            var sim = NewSim(1_000_000);
            var same = sim.Quote(new VoyagePlan(CourierId, Alpha));
            Assert.IsFalse(same.IsPlanValid);
            CollectionAssert.AreEqual(new[] { "Test Courier is already at Alpha. Choose another destination." }, same.Problems);

            var noShip = sim.Quote(new VoyagePlan(99, Beta));
            Assert.IsFalse(noShip.IsFeasible);
            CollectionAssert.AreEqual(new[] { "There is no ship with id 99." }, noShip.Problems);

            var noSystem = sim.Quote(new VoyagePlan(CourierId, 7));
            CollectionAssert.AreEqual(new[] { "There is no system with id 7." }, noSystem.Problems);

            var negative = sim.Quote(new VoyagePlan(CourierId, Beta, null, -1));
            Assert.IsFalse(negative.IsPlanValid);
        }

        [Test]
        public void Quote_UsesCurrentTuning_ForShipStats()
        {
            var t = FlatTuning();
            var sim = NewSim(1_000_000, t);
            Assert.IsTrue(sim.Quote(Plan(25, 2)).HoldFits);
            t.FindShip("courier").HoldTonnes = 20;
            Assert.IsFalse(sim.Quote(Plan(25, 2)).HoldFits);
        }
    }
}
