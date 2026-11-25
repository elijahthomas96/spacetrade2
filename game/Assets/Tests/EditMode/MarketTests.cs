using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.World;
using static SpaceTrade.Sim.Tests.MarketTestSupport;

namespace SpaceTrade.Sim.Tests
{
    public class MarketTests
    {
        private const int SeedCount = 200;

        private static TuningData EmptyStartTuning()
        {
            var t = TestTuning.Load();
            t.Start.MarketStartFill.SupplyPercent = 0;
            t.Start.MarketStartFill.DemandPercent = 0;
            t.Start.MarketStartFill.PassengerPercent = 0;
            return t;
        }

        // ---- Creation ----

        [Test]
        public void NewMarket_HasStockForEveryProducerAndConsumer_AndAnOfferForEveryPairing()
        {
            var tuning = TestTuning.Load();
            for (long seed = 1; seed <= 20; seed++)
            {
                var m = NewSim(seed, tuning).Market;
                var g = m.Galaxy;
                foreach (var s in g.Systems)
                foreach (var c in tuning.Commodities)
                {
                    var inds = s.IndustryIds.Select(tuning.FindIndustry).ToList();
                    Assert.AreEqual(inds.Any(i => i.Produces.Contains(c.Id)), m.FindSupply(s.Id, c.Id) != null, $"seed {seed} {s.Name} supply {c.Id}");
                    Assert.AreEqual(inds.Any(i => i.Consumes.Contains(c.Id)), m.FindDemand(s.Id, c.Id) != null, $"seed {seed} {s.Name} demand {c.Id}");
                }
                int expectedOffers = 0;
                foreach (var supply in m.Supplies)
                    expectedOffers += m.Demands.Count(d => d.CommodityId == supply.CommodityId && d.SystemId != supply.SystemId);
                Assert.AreEqual(expectedOffers, m.Offers.Count, $"seed {seed}");
                foreach (var o in m.Offers)
                {
                    Assert.AreNotEqual(o.OriginId, o.DestinationId);
                    Assert.AreEqual(g.DistanceMilliLy(o.OriginId, o.DestinationId), o.DistanceMilliLy);
                }
                int n = g.Systems.Count;
                Assert.AreEqual(n * (n - 1), m.Pools.Count, $"seed {seed}: one pool per ordered pair of systems");
                Assert.AreEqual(n, m.ProvisionRolls.Count);
            }
        }

        [Test]
        public void NewMarket_DefaultFill_StartsAtCaps()
        {
            var tuning = TestTuning.Load();
            Assert.AreEqual(100, tuning.Start.MarketStartFill.SupplyPercent);
            var m = NewSim(5, tuning).Market;
            foreach (var s in m.Supplies) Assert.AreEqual(m.SupplyCap(s), s.Tonnes);
            foreach (var d in m.Demands) Assert.AreEqual(m.DemandCap(d), d.Tonnes);
            foreach (var p in m.Pools) Assert.AreEqual(m.PoolCap(p), p.Waiting);
        }

        [Test]
        public void NewMarket_StartFillPercents_AreAppliedSeparately()
        {
            var tuning = TestTuning.Load();
            tuning.Start.MarketStartFill.SupplyPercent = 50;
            tuning.Start.MarketStartFill.DemandPercent = 25;
            tuning.Start.MarketStartFill.PassengerPercent = 0;
            var m = NewSim(5, tuning).Market;
            foreach (var s in m.Supplies) Assert.AreEqual(30, s.Tonnes);  // 60 x 50 percent
            foreach (var d in m.Demands) Assert.AreEqual(15, d.Tonnes);   // 60 x 25 percent
            foreach (var p in m.Pools) Assert.AreEqual(0, p.Waiting);
        }

        [Test]
        public void TonnesAvailable_IsSmallerOfSupplyAndDemand()
        {
            var tuning = TestTuning.Load();
            tuning.FindCommodity("ore").SupplyCapTonnes = 45;
            tuning.FindCommodity("ore").DemandCapTonnes = 70;
            var m = FindSim(tuning, x => x.Offers.Any(o => o.CommodityId == "ore"), "an ore offer").Market;
            foreach (var o in m.Offers.Where(o => o.CommodityId == "ore"))
                Assert.AreEqual(45, m.TonnesAvailable(o));
        }

        // ---- Replenishment ----

        [Test]
        public void Replenishment_AddsExactAmountEachInterval_UpToCaps()
        {
            var tuning = EmptyStartTuning(); // caps 60 t, +10 t every 30 days; passengers +2 every 30 days
            var sim = NewSim(11, tuning);
            var m = sim.Market;
            for (int k = 1; k <= 8; k++)
            {
                sim.Advance(Days(30 * k) - 1);
                foreach (var s in m.Supplies) Assert.AreEqual(System.Math.Min(60, 10 * (k - 1)), s.Tonnes, $"supply one tick before interval {k}");
                foreach (var d in m.Demands) Assert.AreEqual(System.Math.Min(60, 10 * (k - 1)), d.Tonnes, $"demand one tick before interval {k}");
                foreach (var p in m.Pools) Assert.AreEqual(System.Math.Min(m.PoolCap(p), 2 * (k - 1)), p.Waiting, $"pool one tick before interval {k}");

                sim.Advance(Days(30 * k));
                foreach (var s in m.Supplies) Assert.AreEqual(System.Math.Min(60, 10 * k), s.Tonnes, $"supply at interval {k}");
                foreach (var d in m.Demands) Assert.AreEqual(System.Math.Min(60, 10 * k), d.Tonnes, $"demand at interval {k}");
                foreach (var p in m.Pools) Assert.AreEqual(System.Math.Min(m.PoolCap(p), 2 * k), p.Waiting, $"pool at interval {k}");
            }
        }

        [Test]
        public void Replenishment_StopsExactlyAtCap_WhenStepDoesNotDivideCap()
        {
            var tuning = EmptyStartTuning();
            foreach (var c in tuning.Commodities) c.SupplyReplenishTonnes = 25;
            var sim = NewSim(12, tuning);
            var expected = new[] { 25, 50, 60, 60, 60 };
            for (int k = 1; k <= expected.Length; k++)
            {
                sim.Advance(Days(30 * k));
                foreach (var s in sim.Market.Supplies) Assert.AreEqual(expected[k - 1], s.Tonnes, $"interval {k}");
            }
        }

        [Test]
        public void Replenishment_EachCommodityUsesItsOwnInterval()
        {
            var tuning = EmptyStartTuning();
            tuning.FindCommodity("ore").ReplenishIntervalDays = 7;
            var sim = FindSim(tuning, x => x.Supplies.Any(s => s.CommodityId == "ore") && x.Supplies.Any(s => s.CommodityId != "ore"), "ore and another supply");
            var m = sim.Market;
            sim.Advance(Days(7));
            foreach (var s in m.Supplies) Assert.AreEqual(s.CommodityId == "ore" ? 10 : 0, s.Tonnes, s.CommodityId);
            sim.Advance(Days(28));
            foreach (var s in m.Supplies) Assert.AreEqual(s.CommodityId == "ore" ? 40 : 0, s.Tonnes, s.CommodityId);
            sim.Advance(Days(30));
            foreach (var s in m.Supplies) Assert.AreEqual(s.CommodityId == "ore" ? 40 : 10, s.Tonnes, s.CommodityId);
        }

        [Test]
        public void Replenishment_NeverRemovesStockAboveALoweredCap()
        {
            var sim = NewSim(13, TestTuning.Load());
            var lowered = TestTuning.Load();
            foreach (var c in lowered.Commodities) c.SupplyCapTonnes = 40;
            sim.Market.ReplaceTuning(lowered);
            sim.Advance(Days(90));
            foreach (var s in sim.Market.Supplies) Assert.AreEqual(60, s.Tonnes);
        }

        [Test]
        public void OneBigAdvance_AndManySmallAdvances_GiveTheSameMarket()
        {
            var tuning = EmptyStartTuning();
            var a = NewSim(21, tuning);
            var b = NewSim(21, tuning);
            long end = Days(720) + 1234;
            a.Advance(end);
            for (long t = 0; t < end; t += 9_973) b.Advance(System.Math.Min(end, t + 9_973));
            Assert.AreEqual(a.Market.DescribeState(), b.Market.DescribeState());
        }

        [Test]
        public void AttachMarket_SchedulesOneEventPerSystem_AndKeepsItThatWay()
        {
            var sim = NewSim(3, TestTuning.Load());
            int n = sim.Market.Galaxy.Systems.Count;
            Assert.AreEqual(n, sim.Events.Count);
            sim.Advance(Days(1000));
            Assert.AreEqual(n, sim.Events.Count);
            Assert.Throws<System.InvalidOperationException>(() => sim.AttachMarket(sim.Market));
        }

        // ---- Rate rolls ----

        private static void AssertRatesInRange(Market m, string context)
        {
            var t = m.Tuning;
            foreach (var o in m.Offers)
            {
                var c = t.FindCommodity(o.CommodityId);
                Assert.That(o.RateRollPercent, Is.InRange(c.RateRollMinPercent, c.RateRollMaxPercent), context);
                long low = MarketMath.DistancePrice(c.BaseRatePerTonne, o.DistanceMilliLy, c.RateRollMinPercent, t.Rates);
                long high = MarketMath.DistancePrice(c.BaseRatePerTonne, o.DistanceMilliLy, c.RateRollMaxPercent, t.Rates);
                Assert.That(m.RatePerTonne(o), Is.InRange(low, high), context);
            }
            foreach (var p in m.Pools)
            {
                Assert.That(p.FareRollPercent, Is.InRange(t.Passengers.FareRollMinPercent, t.Passengers.FareRollMaxPercent), context);
                long low = MarketMath.DistancePrice(t.Passengers.BaseFare, p.DistanceMilliLy, t.Passengers.FareRollMinPercent, t.Rates);
                long high = MarketMath.DistancePrice(t.Passengers.BaseFare, p.DistanceMilliLy, t.Passengers.FareRollMaxPercent, t.Rates);
                Assert.That(m.FarePerPassenger(p), Is.InRange(low, high), context);
            }
            foreach (var r in m.ProvisionRolls)
            {
                Assert.That(r.FuelRollPercent, Is.InRange(t.Provisions.PriceRollMinPercent, t.Provisions.PriceRollMaxPercent), context);
                Assert.That(r.FoodRollPercent, Is.InRange(t.Provisions.PriceRollMinPercent, t.Provisions.PriceRollMaxPercent), context);
                Assert.That(r.WaterRollPercent, Is.InRange(t.Provisions.PriceRollMinPercent, t.Provisions.PriceRollMaxPercent), context);
            }
        }

        [Test]
        public void RateRolls_TwoHundredSeeds_TenRerolls_AllStayInRange_AndActuallyChange()
        {
            var tuning = TestTuning.Load();
            int reroll = tuning.Rates.RerollIntervalDays;
            int rollsChecked = 0, rateChanges = 0, fareChanges = 0;
            for (long seed = 1; seed <= SeedCount; seed++)
            {
                var sim = NewSim(seed, tuning);
                var m = sim.Market;
                AssertRatesInRange(m, $"seed {seed} at creation");
                for (int k = 1; k <= 10; k++)
                {
                    var rates = m.Offers.Select(m.RatePerTonne).ToArray();
                    var fares = m.Pools.Select(m.FarePerPassenger).ToArray();
                    sim.Advance(Days(reroll * k));
                    AssertRatesInRange(m, $"seed {seed} after re-roll {k}");
                    rateChanges += m.Offers.Select(m.RatePerTonne).Zip(rates, (x, y) => x != y ? 1 : 0).Sum();
                    fareChanges += m.Pools.Select(m.FarePerPassenger).Zip(fares, (x, y) => x != y ? 1 : 0).Sum();
                    rollsChecked += m.Offers.Count + m.Pools.Count;
                }
            }
            Assert.Greater(rateChanges, 0);
            Assert.Greater(fareChanges, 0);
            TestContext.WriteLine($"Checked {rollsChecked} re-rolled rates and fares; {rateChanges} rates and {fareChanges} fares changed.");
        }

        [Test]
        public void RateRolls_RepeatExactly_ForTheSameSeed()
        {
            var tuning = TestTuning.Load();
            int reroll = tuning.Rates.RerollIntervalDays;
            foreach (long seed in new long[] { 1, 77, 2026, -5 })
            {
                var a = NewSim(seed, tuning);
                var b = NewSim(seed, TestTuning.Load());
                Assert.AreEqual(a.Market.DescribeState(), b.Market.DescribeState(), $"seed {seed} at creation");
                for (int k = 1; k <= 10; k++)
                {
                    a.Advance(Days(reroll * k));
                    b.Advance(Days(reroll * k));
                    Assert.AreEqual(a.Market.DescribeState(), b.Market.DescribeState(), $"seed {seed} after re-roll {k}");
                    CollectionAssert.AreEqual(a.Market.Offers.Select(a.Market.RatePerTonne), b.Market.Offers.Select(b.Market.RatePerTonne));
                    CollectionAssert.AreEqual(a.Market.Pools.Select(a.Market.FarePerPassenger), b.Market.Pools.Select(b.Market.FarePerPassenger));
                }
            }
        }

        [Test]
        public void RateRolls_DifferBetweenSeeds()
        {
            var tuning = TestTuning.Load();
            var states = new HashSet<string>();
            for (long seed = 1; seed <= 30; seed++) states.Add(NewSim(seed, tuning).Market.DescribeState());
            Assert.AreEqual(30, states.Count);
        }

        [Test]
        public void Reroll_HappensExactlyAtTheInterval_AndNotBefore()
        {
            var tuning = TestTuning.Load();
            var sim = NewSim(8, tuning);
            var m = sim.Market;
            var initial = m.Offers.Select(o => o.RateRollPercent).Concat(m.Pools.Select(p => p.FareRollPercent)).ToArray();
            sim.Advance(Days(tuning.Rates.RerollIntervalDays) - 1);
            CollectionAssert.AreEqual(initial, m.Offers.Select(o => o.RateRollPercent).Concat(m.Pools.Select(p => p.FareRollPercent)).ToArray());
            sim.Advance(Days(tuning.Rates.RerollIntervalDays));
            CollectionAssert.AreNotEqual(initial, m.Offers.Select(o => o.RateRollPercent).Concat(m.Pools.Select(p => p.FareRollPercent)).ToArray());
        }

        // ---- Provision prices ----

        [Test]
        public void ProvisionRolls_AreNeverReRolled()
        {
            var sim = NewSim(4, TestTuning.Load());
            string before = string.Join(";", sim.Market.ProvisionRolls.Select(r => $"{r.FuelRollPercent},{r.FoodRollPercent},{r.WaterRollPercent}"));
            sim.Advance(Days(360 * 5));
            string after = string.Join(";", sim.Market.ProvisionRolls.Select(r => $"{r.FuelRollPercent},{r.FoodRollPercent},{r.WaterRollPercent}"));
            Assert.AreEqual(before, after);
        }

        [Test]
        public void ProvisionPrice_ChangingBasePriceInTuning_ChangesPriceInExistingMarket()
        {
            bool NoModifiers(StarSystem s) => s.IndustryIds.All(i => i != "refinery" && i != "agricultural_system" && i != "mining_colony");
            var sim = FindSim(TestTuning.Load(), x => x.Galaxy.Systems.Any(NoModifiers), "a system with no provision modifiers");
            var m = sim.Market;
            int id = m.Galaxy.Systems.First(NoModifiers).Id;
            var roll = m.ProvisionRolls[id];
            Assert.AreEqual((2_000L * roll.FuelRollPercent + 50) / 100, m.ProvisionPricesAt(id).FuelPerTonne);
            string stateBefore = m.DescribeState();

            var edited = TestTuning.Load();
            edited.Provisions.FuelBasePricePerTonne = 3_000;
            m.ReplaceTuning(edited);

            Assert.AreEqual((3_000L * roll.FuelRollPercent + 50) / 100, m.ProvisionPricesAt(id).FuelPerTonne);
            Assert.AreEqual(stateBefore, m.DescribeState(), "Only tuning changed; stored rolls must not.");
        }

        private static TuningData TwoFuelModifierTuning(int floorPercent)
        {
            var t = TestTuning.Load();
            t.Provisions.CombinedModifierMinPercent = floorPercent;
            var other = t.Provisions.IndustryModifiers.Find(x => x.Industry == "agricultural_system");
            other.FuelPercent = 70; // same as the refinery
            return t;
        }

        private static Simulation SystemWithRefineryAndFarm(TuningData t, out int systemId)
        {
            var sim = FindSim(t, m => m.Galaxy.Systems.Any(s => s.IndustryIds.Contains("refinery") && s.IndustryIds.Contains("agricultural_system")), "a system with a refinery and an agricultural system");
            systemId = sim.Market.Galaxy.Systems.First(s => s.IndustryIds.Contains("refinery") && s.IndustryIds.Contains("agricultural_system")).Id;
            Assert.IsFalse(sim.Market.Galaxy.Systems[systemId].IndustryIds.Contains("mining_colony"), "Test setup: a third fuel modifier would change the expected value.");
            return sim;
        }

        [Test]
        public void ProvisionPrice_TwoRefineryStyleModifiersInOneSystem_Multiply()
        {
            // A system cannot list the same industry twice, so the agricultural system is given the
            // refinery's 70 percent fuel modifier: 70 x 70 = 49 percent.
            var t = TwoFuelModifierTuning(floorPercent: 10);
            var sim = SystemWithRefineryAndFarm(t, out int id);
            int roll = sim.Market.ProvisionRolls[id].FuelRollPercent;
            Assert.AreEqual((2_000L * 49 * roll + 5_000) / 10_000, sim.Market.ProvisionPricesAt(id).FuelPerTonne);
        }

        [Test]
        public void ProvisionPrice_TwoRefineryStyleModifiersInOneSystem_HitTheDefaultFloor()
        {
            var t = TwoFuelModifierTuning(floorPercent: 50);
            var sim = SystemWithRefineryAndFarm(t, out int id);
            int roll = sim.Market.ProvisionRolls[id].FuelRollPercent;
            Assert.AreEqual((2_000L * 50 * roll + 5_000) / 10_000, sim.Market.ProvisionPricesAt(id).FuelPerTonne);
        }

        [Test]
        public void ProvisionPrices_TwoHundredSeeds_StayWithinModifierAndRollRange()
        {
            var tuning = TestTuning.Load();
            var p = tuning.Provisions;
            for (long seed = 1; seed <= SeedCount; seed++)
            {
                var m = Market.Create(GalaxyGenerator.Generate(seed, tuning), tuning, 0);
                foreach (var s in m.Galaxy.Systems)
                {
                    var prices = m.ProvisionPricesAt(s.Id);
                    // Combined modifier is clamped to 50..200 percent; roll is 90..110 percent.
                    Assert.That(prices.FuelPerTonne, Is.InRange(p.FuelBasePricePerTonne * 50 * 90 / 10_000, p.FuelBasePricePerTonne * 200 * 110 / 10_000), $"seed {seed} {s.Name}");
                    Assert.That(prices.FoodPerTonne, Is.InRange(p.FoodBasePricePerTonne * 50 * 90 / 10_000, p.FoodBasePricePerTonne * 200 * 110 / 10_000), $"seed {seed} {s.Name}");
                    Assert.That(prices.WaterPerTonne, Is.InRange(p.WaterBasePricePerTonne * 50 * 90 / 10_000, p.WaterBasePricePerTonne * 200 * 110 / 10_000), $"seed {seed} {s.Name}");
                }
            }
        }
    }
}
