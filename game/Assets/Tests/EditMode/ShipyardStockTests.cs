using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.World;
using SpaceTrade.Sim.Yards;
using static SpaceTrade.Sim.Tests.YardTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>Yard stock, stock limits, and restocking (spec 7).</summary>
    public class ShipyardStockTests
    {
        private const long RestockTicks = 180 * GameTime.TicksPerDay;

        [Test]
        public void EveryShipyardSystem_GetsAYard_StockingEveryClassAndDrive_StartingFull_On200Seeds()
        {
            var t = TestTuning.Load();
            var y = t.Shipyards;
            int yards = 0, items = 0;
            var limitsSeen = new System.Collections.Generic.HashSet<int>();
            for (long seed = 1; seed <= 200; seed++)
            {
                var galaxy = GalaxyGenerator.Generate(seed, t);
                var market = Market.Create(galaxy, t, 0);
                CollectionAssert.AreEqual(galaxy.Systems.Where(s => s.HasShipyard).Select(s => s.Id).ToArray(),
                    market.Shipyards.Select(x => x.SystemId).ToArray(), $"seed {seed}");
                foreach (var yard in market.Shipyards)
                {
                    yards++;
                    Assert.That(yard.PriceRollPercent, Is.InRange(y.PriceRollMinPercent, y.PriceRollMaxPercent));
                    CollectionAssert.AreEqual(t.Ships.Select(c => c.Id).ToArray(), yard.Ships.Select(s => s.ItemId).ToArray());
                    CollectionAssert.AreEqual(t.Drives.Select(d => d.Id).ToArray(), yard.Drives.Select(s => s.ItemId).ToArray());
                    foreach (var s in yard.Ships.Concat(yard.Drives))
                    {
                        items++;
                        Assert.That(s.LimitRollPercent, Is.InRange(0, 100));
                        int limit = market.StockLimit(s);
                        if (s.Kind == YardItemKind.Ship) Assert.That(limit, Is.InRange(y.ShipStockPerClassMin, y.ShipStockPerClassMax));
                        else Assert.That(limit, Is.InRange(y.DriveStockPerTierMin, y.DriveStockPerTierMax));
                        Assert.AreEqual(limit, s.Count, "A new yard starts full");
                        Assert.IsTrue(market.Stocks(s), "With the fixture minimum of 1 every yard stocks everything");
                        limitsSeen.Add(limit);
                    }
                }
            }
            CollectionAssert.IsSubsetOf(new[] { 1, 2, 3 }, limitsSeen.ToArray());
            TestContext.WriteLine($"{yards} yards, {items} stock entries over 200 seeds; limits seen: {string.Join(", ", limitsSeen.OrderBy(x => x))}");
        }

        [Test]
        public void YardRolls_UseTheirOwnGenerator_SoMarketRollsDoNotChange()
        {
            var t = YardTuning();
            t.Shipyards.PriceRollMinPercent = 80;
            t.Shipyards.PriceRollMaxPercent = 120;
            var withYards = Market.Create(ThreeSystems(yards: true), t, 0);
            var without = Market.Create(ThreeSystems(yards: false), t, 0);
            Assert.AreEqual(2, withYards.Shipyards.Count);
            Assert.AreEqual(0, without.Shipyards.Count);
            Assert.AreEqual(without.Random.S0, withYards.Random.S0);
            Assert.AreEqual(without.Random.S1, withYards.Random.S1);
            Assert.AreEqual(without.Random.S2, withYards.Random.S2);
            Assert.AreEqual(without.Random.S3, withYards.Random.S3);
            string MarketLines(Market m) => string.Join("\n", m.DescribeState().Split('\n').Where(l => !l.StartsWith("yard")));
            Assert.AreEqual(MarketLines(without), MarketLines(withYards));
        }

        [TestCase(1, 2, 0, 1)]
        [TestCase(1, 2, 49, 1)]
        [TestCase(1, 2, 50, 2)] // 1.5 rounds away from zero
        [TestCase(1, 2, 100, 2)]
        [TestCase(1, 3, 24, 1)] // 1.48
        [TestCase(1, 3, 25, 2)] // 1.5
        [TestCase(0, 10, 0, 0)]
        [TestCase(0, 10, 37, 4)] // 3.7
        [TestCase(2, 2, 77, 2)]
        public void StockLimit_IsMinPlusRollPercentOfTheRange_RoundedOnce(int min, int max, int roll, int expected)
        {
            Assert.AreEqual(expected, YardMath.StockLimit(min, max, roll));
        }

        [Test]
        public void TuningEdit_ToMinAndMax_ChangesAnExistingYardsLimit()
        {
            var sim = NewSim(0);
            var ship = ShipStock(sim, Beta, Courier);
            var drive = DriveStock(sim, Beta, Fast);
            ship.LimitRollPercent = 50;
            drive.LimitRollPercent = 25;
            ship.Count = 0;
            drive.Count = 0;
            Assert.AreEqual(2, sim.Market.StockLimit(ship), "1 + (2 - 1) x 50% = 1.5, rounds to 2");
            Assert.AreEqual(2, sim.Market.StockLimit(drive), "1 + (3 - 1) x 25% = 1.5, rounds to 2");

            var edited = YardTuning();
            edited.Shipyards.ShipStockPerClassMin = 3;
            edited.Shipyards.ShipStockPerClassMax = 7;
            edited.Shipyards.DriveStockPerTierMin = 4;
            edited.Shipyards.DriveStockPerTierMax = 12;
            sim.Market.ReplaceTuning(edited);
            Assert.AreEqual(5, sim.Market.StockLimit(ship), "3 + (7 - 3) x 50% = 5");
            Assert.AreEqual(6, sim.Market.StockLimit(drive), "4 + (12 - 4) x 25% = 6");
            Assert.AreEqual(50, ship.LimitRollPercent, "The stored roll does not change");

            for (int i = 1; i <= 7; i++)
            {
                sim.Advance(i * RestockTicks);
                Assert.AreEqual(System.Math.Min(i, 5), ship.Count, $"restock {i}");
                Assert.AreEqual(System.Math.Min(i, 6), drive.Count, $"restock {i}");
            }
        }

        [Test]
        public void Restock_AddsTheRestockAmount_EachInterval_UpToTheLimit_AndNeverBefore()
        {
            var sim = NewSim(0);
            var ship = ShipStock(sim, Beta, Courier);
            var drive = DriveStock(sim, Gamma, Fast);
            ship.Count = 0;
            drive.Count = 0;
            sim.Advance(RestockTicks - 1);
            Assert.AreEqual(0, ship.Count, "Nothing before the first interval");
            Assert.AreEqual(0, drive.Count);
            int[] expectedShip = { 1, 2, 2, 2 };
            int[] expectedDrive = { 1, 2, 3, 3 };
            for (int i = 0; i < 4; i++)
            {
                sim.Advance((i + 1) * RestockTicks);
                Assert.AreEqual(expectedShip[i], ship.Count, $"courier after restock {i + 1}");
                Assert.AreEqual(expectedDrive[i], drive.Count, $"3.75c after restock {i + 1}");
                if (i < 3)
                {
                    sim.Advance((i + 2) * RestockTicks - 1);
                    Assert.AreEqual(expectedShip[i], ship.Count, "No growth between restocks");
                }
            }
        }

        [Test]
        public void Restock_UsesTheRestockAmountField()
        {
            var t = YardTuning();
            t.Shipyards.RestockAmount = 2;
            t.Shipyards.ShipStockPerClassMax = 5;
            var sim = NewSim(0, tuning: t);
            var ship = ShipStock(sim, Beta, Bulk);
            Assert.AreEqual(5, sim.Market.StockLimit(ship));
            ship.Count = 0;
            int[] expected = { 2, 4, 5, 5 };
            for (int i = 0; i < expected.Length; i++)
            {
                sim.Advance((i + 1) * RestockTicks);
                Assert.AreEqual(expected[i], ship.Count, $"after restock {i + 1}");
            }
        }

        [Test]
        public void StockAboveALoweredLimit_IsKept_AndStopsGrowing()
        {
            var sim = NewSim(10_000_000);
            var ship = ShipStock(sim, Beta, Courier);
            Assert.AreEqual(2, ship.Count);
            var lowered = YardTuning();
            lowered.Shipyards.ShipStockPerClassMin = 0;
            lowered.Shipyards.ShipStockPerClassMax = 0;
            sim.Market.ReplaceTuning(lowered);
            Assert.AreEqual(0, sim.Market.StockLimit(ship));
            sim.Advance(2 * RestockTicks);
            Assert.AreEqual(2, ship.Count, "Stock above a lowered limit is never removed");
            Assert.IsTrue(sim.Market.Stocks(ship), "Units left over can still be sold");

            Assert.IsTrue(sim.BuyShip(Beta, Courier, out _).Succeeded);
            Assert.IsTrue(sim.BuyShip(Beta, Courier, out _).Succeeded);
            Assert.AreEqual(0, ship.Count);
            sim.Advance(3 * RestockTicks);
            Assert.AreEqual(0, ship.Count, "A limit of 0 never restocks");
            Assert.IsFalse(sim.Market.Stocks(ship));
            var refused = sim.BuyShip(Beta, Courier, out _);
            Assert.IsFalse(refused.Succeeded);
            StringAssert.StartsWith("The shipyard at Beta does not sell the courier. Shipyards that sell the courier: Gamma (2 in stock, 1,000,000 credits).", refused.Reason);
        }

        [Test]
        public void RandomBuysAndRestocks_NeverTakeStockBelowZeroOrAboveItsLimit()
        {
            var sim = NewSim(1_000_000_000_000, dockedAt: Beta);
            foreach (var s in AllStock(sim)) s.Count = 0;
            var rng = new SeededRandom(2026);
            var classes = new[] { Courier, Bulk, Liner };
            var drives = new[] { Slow, Mid, Fast };
            int yardSystem = Beta;
            int shipsBought = 0, drivesFitted = 0, refusals = 0, restockSteps = 0;
            for (int i = 0; i < 4_000; i++)
            {
                int op = rng.Range(0, 2);
                if (op == 0)
                {
                    var stock = ShipStock(sim, yardSystem = rng.Range(Beta, Gamma), classes[rng.Range(0, 2)]);
                    int before = stock.Count;
                    var r = sim.BuyShip(yardSystem, stock.ItemId, out _);
                    if (r.Succeeded) { shipsBought++; Assert.AreEqual(before - 1, stock.Count); }
                    else { refusals++; Assert.AreEqual(before, stock.Count); StringAssert.Contains("has 0 of the", r.Reason); }
                }
                else if (op == 1)
                {
                    var courier = sim.FindShip(CourierId);
                    var options = drives.Where(d => d != courier.DriveId).ToArray();
                    var stock = DriveStock(sim, Beta, options[rng.Range(0, options.Length - 1)]);
                    int before = stock.Count;
                    var r = sim.BuyDrive(CourierId, stock.ItemId);
                    if (r.Succeeded) { drivesFitted++; Assert.AreEqual(before - 1, stock.Count); }
                    else { refusals++; Assert.AreEqual(before, stock.Count); StringAssert.Contains("has 0 of the", r.Reason); }
                }
                else
                {
                    restockSteps++;
                    sim.Advance(sim.CurrentTick + rng.Range(1, 400) * GameTime.TicksPerDay);
                }
                foreach (var s in AllStock(sim))
                    Assert.That(s.Count, Is.InRange(0, sim.Market.StockLimit(s)), $"step {i}: {s.ItemId} at a yard");
            }
            TestContext.WriteLine($"ships bought {shipsBought}, drives fitted {drivesFitted}, refusals {refusals}, time steps {restockSteps}");
            Assert.Greater(shipsBought, 0);
            Assert.Greater(drivesFitted, 0);
            Assert.Greater(refusals, 0);
        }

        [Test]
        public void TwoPurchasesOfTheLastShip_GiveOneSuccessAndOneRefusal()
        {
            var sim = NewSim(10_000_000);
            ShipStock(sim, Beta, Liner).Count = 1;
            var first = sim.BuyShip(Beta, Liner, out int id1);
            var before = Bytes(sim);
            var second = sim.BuyShip(Beta, Liner, out int id2);
            Assert.IsTrue(first.Succeeded, first.Reason);
            Assert.IsFalse(second.Succeeded);
            Assert.AreEqual(2, id1);
            Assert.AreEqual(0, id2);
            Assert.AreEqual(0, ShipStock(sim, Beta, Liner).Count);
            StringAssert.Contains("has 0 of the passenger liner in stock (stock limit 2)", second.Reason);
            CollectionAssert.AreEqual(before, Bytes(sim));
        }

        [Test]
        public void TwoFittingsOfTheLastDrive_GiveOneSuccessAndOneRefusal()
        {
            var sim = NewSim(10_000_000, dockedAt: Beta);
            sim.AddShip("Second Courier", Courier, Slow, Beta);
            DriveStock(sim, Beta, Fast).Count = 1;
            var first = sim.BuyDrive(1, Fast);
            var before = Bytes(sim);
            var second = sim.BuyDrive(2, Fast);
            Assert.IsTrue(first.Succeeded, first.Reason);
            Assert.IsFalse(second.Succeeded);
            Assert.AreEqual(0, DriveStock(sim, Beta, Fast).Count);
            StringAssert.Contains("has 0 of the Alcubierre 3.75c in stock (stock limit 3)", second.Reason);
            CollectionAssert.AreEqual(before, Bytes(sim));
        }

        [Test]
        public void RestockAmount_Missing_IsANamedError()
        {
            var text = TestTuning.With(TestTuning.Text(), "\"restockAmount\": 1,", "");
            var result = TuningLoader.LoadText(text);
            Assert.IsTrue(result.UsedDefaults);
            Assert.IsTrue(result.Errors.Any(e => e.StartsWith("shipyards.restockAmount:")), string.Join("\n", result.Errors));
        }

        [Test]
        public void RestockAmount_Zero_IsANamedError()
        {
            var text = TestTuning.With(TestTuning.Text(), "\"restockAmount\": 1,", "\"restockAmount\": 0,");
            var result = TuningLoader.LoadText(text);
            Assert.IsTrue(result.UsedDefaults);
            Assert.IsTrue(result.Errors.Any(e => e.StartsWith("shipyards.restockAmount:")), string.Join("\n", result.Errors));
        }

        [Test]
        public void ReplaceTuning_ThatDropsADriveAYardStocks_IsRefused()
        {
            var sim = NewSim(0);
            var t = YardTuning();
            t.Drives.RemoveAll(d => d.Id == Fast);
            var e = Assert.Throws<System.ArgumentException>(() => sim.Market.ReplaceTuning(t));
            StringAssert.Contains("stocks drive \"alcubierre_3_75\"", e.Message);
        }
    }
}
