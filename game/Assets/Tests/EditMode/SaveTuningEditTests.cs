using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Ships;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.Yards;
using static SpaceTrade.Sim.Tests.RouteTestSupport;
using static SpaceTrade.Sim.Tests.SaveTestSupport;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>
    /// Spec 11.2: a save loads with the current tuning file. Prices, rates, and limits follow it; voyages in flight keep
    /// their locked terms and arrival ticks; a changed interval reschedules the next event but grants nothing for time
    /// already passed; a tuning file that no longer defines something the game uses is refused.
    /// </summary>
    public class SaveTuningEditTests
    {
        private TempSaveFolder _folder;

        [SetUp] public void SetUp() => _folder = new TempSaveFolder();
        [TearDown] public void TearDown() => _folder.Dispose();

        private static string[] NonEventLines(Simulation sim) => Lines(sim).Where(l => !l.StartsWith("event")).ToArray();

        private static TuningData EditedPricesAndLimits()
        {
            var t = TestTuning.Load();
            t.Provisions.FuelBasePricePerTonne *= 2;
            t.Provisions.FoodBasePricePerTonne *= 3;
            t.Provisions.FoodGramsPerPersonPerDay += 500;
            foreach (var c in t.Commodities) c.BaseRatePerTonne *= 2;
            t.Passengers.BaseFare *= 2;
            foreach (var s in t.Ships) s.Price *= 2;
            foreach (var d in t.Drives) { d.Price += 1_000; d.SpeedMilliC *= 2; d.FuelKgPerTonneLy += 1; }
            t.Shipyards.ShipStockPerClassMin += 1;
            t.Shipyards.ShipStockPerClassMax += 3;
            return t;
        }

        [Test]
        public void EditedTuning_PricesRatesAndLimitsFollowIt_WhileTheVoyageInFlightKeepsItsLockedTermsAndArrival()
        {
            var sim = YardGame();
            var starter = sim.FindShip(1);
            Assert.IsTrue(starter.IsInTransit);
            var voyage = starter.Voyage;
            string lockedTerms = voyage.DescribeState();
            var remaining = new[] { SupplyKind.Fuel, SupplyKind.Food, SupplyKind.Water }.Select(k => sim.RemainingSupplyKg(1, k)).ToArray();
            var yard = sim.Market.Shipyards[0];
            long oldFuel = sim.Market.ProvisionPricesAt(yard.SystemId).FuelPerTonne;
            long oldShipPrice = sim.Market.Price(yard, YardItemKind.Ship, yard.Ships[0].ItemId);
            int oldLimit = sim.Market.StockLimit(yard.Ships[0]);
            var offer = sim.Market.Offers[0];
            long oldRate = sim.Market.RatePerTonne(offer);
            SaveAt(_folder, "a", sim);

            var edited = EditedPricesAndLimits();
            var loaded = Load(_folder, "a", edited);
            sim.Market.ReplaceTuning(edited);
            var m = loaded.Market;

            foreach (var s in m.Galaxy.Systems)
            {
                var p = m.ProvisionPricesAt(s.Id);
                var expected = sim.Market.ProvisionPricesAt(s.Id);
                Assert.AreEqual(expected.FuelPerTonne, p.FuelPerTonne);
                Assert.AreEqual(expected.FoodPerTonne, p.FoodPerTonne);
                Assert.AreEqual(expected.WaterPerTonne, p.WaterPerTonne);
            }
            foreach (var y in m.Shipyards)
                foreach (var item in y.Ships.Concat(y.Drives))
                {
                    var original = sim.Market.FindShipyard(y.SystemId).Find(item.Kind, item.ItemId);
                    Assert.AreEqual(sim.Market.Price(sim.Market.FindShipyard(y.SystemId), item.Kind, item.ItemId), m.Price(y, item.Kind, item.ItemId));
                    Assert.AreEqual(sim.Market.StockLimit(original), m.StockLimit(item));
                }
            for (int i = 0; i < m.Offers.Count; i++) Assert.AreEqual(sim.Market.RatePerTonne(sim.Market.Offers[i]), m.RatePerTonne(m.Offers[i]));
            for (int i = 0; i < m.Pools.Count; i++) Assert.AreEqual(sim.Market.FarePerPassenger(sim.Market.Pools[i]), m.FarePerPassenger(m.Pools[i]));

            Assert.AreEqual(oldFuel * 2, m.ProvisionPricesAt(yard.SystemId).FuelPerTonne, 1, "Fuel follows the doubled base price.");
            Assert.AreEqual(oldShipPrice * 2, m.Price(m.FindShipyard(yard.SystemId), YardItemKind.Ship, yard.Ships[0].ItemId), 1, "Ship prices follow the doubled base price.");
            Assert.Greater(m.StockLimit(m.FindShipyard(yard.SystemId).Ships[0]), oldLimit, "Stock limits follow the raised minimum and maximum.");
            Assert.AreEqual(oldRate * 2, m.RatePerTonne(m.Offers[0]), 1, "Freight rates follow the doubled base rate.");

            var flying = loaded.FindShip(1);
            Assert.AreEqual(lockedTerms, flying.Voyage.DescribeState(), "Locked terms, drive, supplies used, and arrival tick are unchanged.");
            CollectionAssert.AreEqual(remaining, new[] { SupplyKind.Fuel, SupplyKind.Food, SupplyKind.Water }.Select(k => loaded.RemainingSupplyKg(1, k)).ToArray());

            loaded.Advance(voyage.ArrivalTick);
            var receipt = loaded.Receipts.Last(r => r.ShipId == 1);
            Assert.AreEqual(voyage.ArrivalTick, receipt.ArrivalTick, "It arrives at the locked tick, though the drive is now twice as fast.");
            Assert.AreEqual(voyage.Payout, receipt.GrossRevenue, "It pays the locked amount, though rates have doubled.");
        }

        [TestCase(25, new[] { 50, 75, 100 }, TestName = "ShorterReplenishInterval_NextReplenishmentAtTheNewBoundary_NothingForTimeAlreadyPassed")]
        [TestCase(35, new[] { 70 }, TestName = "LongerReplenishInterval_NextReplenishmentAtTheNewBoundary_NothingForTimeAlreadyPassed")]
        public void ChangedReplenishInterval(int newDays, int[] growthDays)
        {
            var tuning = FlatTuning();
            var sim = Triangle(1_000, tuning);
            SetSupply(sim, Alpha, "ore", 0);
            sim.Advance(40 * Day);
            int replenish = tuning.FindCommodity("ore").SupplyReplenishTonnes;
            Assert.AreEqual(30, tuning.FindCommodity("ore").ReplenishIntervalDays, "Setup: the fixture replenishes every 30 days.");
            Assert.AreEqual(replenish, sim.Market.FindSupply(Alpha, "ore").Tonnes, "Setup: one replenishment, at day 30.");
            SaveAt(_folder, "a", sim);

            var edited = FlatTuning();
            foreach (var c in edited.Commodities) c.ReplenishIntervalDays = newDays;
            var loaded = Load(_folder, "a", edited);
            CollectionAssert.AreEqual(NonEventLines(sim), NonEventLines(loaded), "Loading grants nothing: every stock, roll, and random state is as saved.");

            int expected = replenish;
            for (int day = 41; day <= 100; day++)
            {
                loaded.Advance(day * Day);
                if (growthDays.Contains(day)) expected += replenish;
                Assert.AreEqual(expected, loaded.Market.FindSupply(Alpha, "ore").Tonnes, $"Ore supply on day {day}.");
            }
        }

        [TestCase(70, 140, TestName = "ShorterRerollInterval_NextRerollAtTheNewBoundary_NothingForTimeAlreadyPassed")]
        [TestCase(150, 150, TestName = "LongerRerollInterval_NextRerollAtTheNewBoundary_NothingForTimeAlreadyPassed")]
        public void ChangedRerollInterval(int newDays, int firstRerollDay)
        {
            var tuning = TestTuning.Load();
            var sim = Triangle(1_000, tuning);
            sim.Advance(100 * Day);
            Assert.AreEqual(90, tuning.Rates.RerollIntervalDays, "Setup: the fixture re-rolls every 90 days.");
            SaveAt(_folder, "a", sim);

            var edited = TestTuning.Load();
            edited.Rates.RerollIntervalDays = newDays;
            var loaded = Load(_folder, "a", edited);
            CollectionAssert.AreEqual(NonEventLines(sim), NonEventLines(loaded), "Loading grants nothing: every stock, roll, and random state is as saved.");

            ulong state = loaded.Market.Random.S0;
            loaded.Advance(firstRerollDay * Day - 1);
            Assert.AreEqual(state, loaded.Market.Random.S0, "No re-roll before the new boundary.");
            loaded.Advance(firstRerollDay * Day);
            Assert.AreNotEqual(state, loaded.Market.Random.S0, $"The re-roll comes at day {firstRerollDay}.");
        }

        [TestCase(150, 300, TestName = "ShorterRestockInterval_NextRestockAtTheNewBoundary_NothingForTimeAlreadyPassed")]
        [TestCase(250, 250, TestName = "LongerRestockInterval_NextRestockAtTheNewBoundary_NothingForTimeAlreadyPassed")]
        public void ChangedRestockInterval(int newDays, int restockDay)
        {
            var tuning = TestTuning.Load();
            var sim = NewGame.Create(1, tuning).Simulation;
            sim.SetCredits(50_000_000);
            Ok(sim.Play());
            sim.Advance(200 * Day);
            Assert.AreEqual(180, tuning.Shipyards.RestockIntervalDays, "Setup: the fixture restocks every 180 days.");
            var yard = sim.Market.Shipyards[0];
            var item = yard.Ships.First(s => s.Count > 0);
            Ok(sim.BuyShip(yard.SystemId, item.ItemId, out _));
            int count = item.Count;
            SaveAt(_folder, "a", sim);

            var edited = TestTuning.Load();
            edited.Shipyards.RestockIntervalDays = newDays;
            var loaded = Load(_folder, "a", edited);
            CollectionAssert.AreEqual(NonEventLines(sim), NonEventLines(loaded), "Loading grants nothing: every stock, roll, and random state is as saved.");

            YardStock Stock() => loaded.Market.FindShipyard(yard.SystemId).Find(YardItemKind.Ship, item.ItemId);
            loaded.Advance(restockDay * Day - 1);
            Assert.AreEqual(count, Stock().Count, "No restock before the new boundary.");
            loaded.Advance(restockDay * Day);
            int expected = System.Math.Min(loaded.Market.StockLimit(Stock()), count + edited.Shipyards.RestockAmount);
            Assert.Greater(expected, count, "Setup: the restock has room to add stock.");
            Assert.AreEqual(expected, Stock().Count, $"The restock comes at day {restockDay}.");
        }

        [Test]
        public void TuningWithoutACommodityTheGameUses_IsRefusedClearly_AndNothingChanges()
        {
            var sim = RouteFleet();
            SaveAt(_folder, "a", sim);
            byte[] before = Bytes(_folder, "a.json");
            var edited = FlatTuning();
            edited.Commodities.RemoveAll(c => c.Id == "ore");

            var r = _folder.Store.Load("a", edited, T0);
            Assert.IsFalse(r.Succeeded);
            StringAssert.StartsWith("The save \"a\" was not loaded: the tuning file no longer defines something this game uses.", r.Messages.Single());
            StringAssert.Contains("ore", r.Messages.Single());
            StringAssert.EndsWith("Put it back in tuning.json to load this save. Nothing was changed.", r.Messages.Single());
            CollectionAssert.AreEqual(new[] { "a.json" }, _folder.FileNames());
            CollectionAssert.AreEqual(before, Bytes(_folder, "a.json"));
        }

        [Test]
        public void TuningWithoutAShipsClassOrDrive_IsRefusedNamingTheShip()
        {
            var sim = RouteFleet();
            SaveAt(_folder, "a", sim);
            var tuning = FlatTuning();
            var noClass = FlatTuning();
            noClass.Ships.RemoveAll(c => c.Id == tuning.Start.StarterShipClass);
            var noDrive = FlatTuning();
            noDrive.Drives.RemoveAll(d => d.Id == tuning.Start.StarterDrive);

            var a = _folder.Store.Load("a", noClass, T0);
            Assert.IsFalse(a.Succeeded);
            StringAssert.Contains($"Ship 1 (Test Courier) is a \"{tuning.Start.StarterShipClass}\", a ship class the tuning file does not define.", a.Messages.Single());
            var b = _folder.Store.Load("a", noDrive, T0);
            Assert.IsFalse(b.Succeeded);
            StringAssert.Contains($"Ship 1 (Test Courier) has drive \"{tuning.Start.StarterDrive}\", which the tuning file does not define.", b.Messages.Single());
        }
    }
}
