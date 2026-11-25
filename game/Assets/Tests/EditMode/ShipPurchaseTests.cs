using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.Yards;
using static SpaceTrade.Sim.Tests.YardTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>Buying ships (spec 7, owner's decisions 1 and 2 for session 8).</summary>
    public class ShipPurchaseTests
    {
        [Test]
        public void BoughtShip_IsDeliveredDockedAtTheYard_WithTheSlowestDrive_EmptySupplies_AndTheToggleOff()
        {
            var sim = NewSim(5_000_000);
            var r = sim.BuyShip(Gamma, Bulk, out int id);
            Assert.IsTrue(r.Succeeded, r.Reason);
            Assert.AreEqual(2, id);
            var ship = sim.FindShip(id);
            Assert.AreEqual(Bulk, ship.ClassId);
            Assert.AreEqual(Slow, ship.DriveId);
            Assert.AreEqual(Gamma, ship.DockedSystemId);
            Assert.IsFalse(ship.IsInTransit);
            Assert.IsFalse(ship.IsOnInSystemTransport);
            Assert.AreEqual(0, ship.Fuel.Kg + ship.Food.Kg + ship.Water.Kg);
            Assert.AreEqual(0, ship.Fuel.CostCredits + ship.Food.CostCredits + ship.Water.CostCredits);
            Assert.AreEqual(1_500_000, sim.Credits);
            Assert.AreEqual(1, ShipStock(sim, Gamma, Bulk).Count, "Stock goes down by one");
            Assert.AreEqual(3, DriveStock(sim, Gamma, Slow).Count, "The ship's own drive does not come out of the yard's drive stock");
        }

        [Test]
        public void Ship_CanBeBoughtAtAYardWhereNoneOfMyShipsIsDocked_AndAFasterDriveFittedAtOnce()
        {
            var sim = NewSim(3_000_000);
            Assert.IsFalse(sim.Ships.Any(s => s.DockedSystemId == Gamma), "No ship of mine is at Gamma");
            var bought = sim.BuyShip(Gamma, Courier, out int id);
            Assert.IsTrue(bought.Succeeded, bought.Reason);
            Assert.AreEqual(Gamma, sim.FindShip(id).DockedSystemId);
            var fitted = sim.BuyDrive(id, Fast);
            Assert.IsTrue(fitted.Succeeded, fitted.Reason);
            Assert.AreEqual(Fast, sim.FindShip(id).DriveId);
            Assert.AreEqual(3_000_000 - 1_000_000 - 1_500_000, sim.Credits);
            Assert.AreEqual(0, sim.CurrentTick, "Neither takes game time");
            Assert.AreEqual(Alpha, sim.FindShip(CourierId).DockedSystemId, "The other ship is untouched");
        }

        [Test]
        public void SlowestDrive_IsTheLowestSpeed_TheFirstListedOnATie()
        {
            var t = YardTuning();
            t.Drives.Reverse();
            Assert.AreEqual(Slow, YardMath.SlowestDrive(t).Id, "Order does not matter when speeds differ");
            t.Drives.Add(new DriveTuning { Id = "alt_1_50", Name = "Alt 1.5c", SpeedMilliC = 1_500, Price = 1, FuelKgPerTonneLy = 1 });
            Assert.AreEqual(Slow, YardMath.SlowestDrive(t).Id, "Tie: the first listed wins");
            t.Drives.Insert(0, new DriveTuning { Id = "alt_first", Name = "Alt first", SpeedMilliC = 1_500, Price = 1, FuelKgPerTonneLy = 1 });
            Assert.AreEqual("alt_first", YardMath.SlowestDrive(t).Id);

            var sim = NewSim(5_000_000);
            var edited = YardTuning();
            edited.Drives.Reverse();
            sim.Market.ReplaceTuning(edited);
            sim.BuyShip(Beta, Courier, out int id);
            Assert.AreEqual(Slow, sim.FindShip(id).DriveId);
        }

        [Test]
        public void Purchase_LowersCreditsByExactlyTheShownPrice_AndNothingElseChangesValue()
        {
            var sim = NewSim(5_000_000);
            sim.Market.FindShipyard(Beta).PriceRollPercent = 93;
            long shown = sim.Market.ShipPriceAt(Beta, Bulk).Value;
            Assert.AreEqual(3_255_000, shown, "3,500,000 x 93%");
            long creditsAndSupplies = VoyageTestSupport.CreditsPlusSuppliesAtCost(sim);
            string before = sim.DescribeState();

            Assert.IsTrue(sim.BuyShip(Beta, Bulk, out int id).Succeeded);
            Assert.AreEqual(5_000_000 - shown, sim.Credits);
            Assert.AreEqual(creditsAndSupplies - shown, VoyageTestSupport.CreditsPlusSuppliesAtCost(sim));

            var (removed, added) = Diff(before, sim.DescribeState());
            Assert.AreEqual(3, removed.Count, string.Join("\n", removed));
            Assert.AreEqual(4, added.Count, string.Join("\n", added));
            Assert.AreEqual("tick=0 speed=X1 paused=False credits=5000000 nextShip=2", removed.Single(l => l.StartsWith("tick=")));
            Assert.AreEqual("tick=0 speed=X1 paused=False credits=1745000 nextShip=3", added.Single(l => l.StartsWith("tick=")));
            Assert.AreEqual("yardstock 1 ship bulk_freighter count=2 limitRoll=100", removed.Single(l => l.StartsWith("yardstock")));
            Assert.AreEqual("yardstock 1 ship bulk_freighter count=1 limitRoll=100", added.Single(l => l.StartsWith("yardstock")));
            Assert.AreNotEqual(removed.Single(l => l.StartsWith("yards rng=")), added.Single(l => l.StartsWith("yards rng=")),
                "The name draw moves the yards' generator; nothing else changes");
            Assert.AreEqual($"ship 2 \"{sim.FindShip(id).Name}\" class=bulk_freighter drive=alcubierre_1_50 docked=1 fuel=0kg/0cr food=0kg/0cr water=0kg/0cr insystem=-",
                added.Single(l => l.StartsWith("ship ")));
        }

        [Test]
        public void ExactlyEnoughCredits_IsEnough()
        {
            var sim = NewSim(1_000_000);
            Assert.IsTrue(sim.BuyShip(Beta, Courier, out _).Succeeded);
            Assert.AreEqual(0, sim.Credits);
        }

        private static void AssertRefusedUnchanged(Simulation sim, System.Func<(CommandResult, int)> buy, string expectedReason)
        {
            var before = Bytes(sim);
            var (r, id) = buy();
            Assert.IsFalse(r.Succeeded);
            Assert.AreEqual(0, id);
            Assert.AreEqual(expectedReason, r.Reason);
            CollectionAssert.AreEqual(before, Bytes(sim), "A refused purchase changes nothing");
        }

        private static (CommandResult, int) Buy(Simulation sim, int yard, string classId)
        {
            var r = sim.BuyShip(yard, classId, out int id);
            return (r, id);
        }

        [Test]
        public void NotEnoughCredits_IsRefused_WithTheNumbers()
        {
            var sim = NewSim(999_999);
            AssertRefusedUnchanged(sim, () => Buy(sim, Beta, Courier),
                "The courier costs 1,000,000 credits at Beta; you have 999,999. You need 1 more credit.");
        }

        [Test]
        public void OutOfStock_IsRefused_WithTheNumbers()
        {
            var sim = NewSim(10_000_000);
            ShipStock(sim, Beta, Courier).Count = 0;
            AssertRefusedUnchanged(sim, () => Buy(sim, Beta, Courier),
                "The shipyard at Beta has 0 of the courier in stock (stock limit 2). It restocks 1 every 180 game days; the next restock is Y1 M07 D01.");
        }

        [Test]
        public void OutOfStockAndTooFewCredits_ListsBoth()
        {
            var sim = NewSim(100);
            ShipStock(sim, Gamma, Liner).Count = 0;
            AssertRefusedUnchanged(sim, () => Buy(sim, Gamma, Liner),
                "The shipyard at Gamma has 0 of the passenger liner in stock (stock limit 2). It restocks 1 every 180 game days; the next restock is Y1 M07 D01. " +
                "The passenger liner costs 2,500,000 credits at Gamma; you have 100. You need 2,499,900 more credits.");
        }

        [Test]
        public void SystemWithNoShipyard_IsRefused_NamingTheYardsThatSell()
        {
            var sim = NewSim(10_000_000);
            ShipStock(sim, Gamma, Courier).Count = 0;
            AssertRefusedUnchanged(sim, () => Buy(sim, Alpha, Courier),
                "Alpha has no shipyard. Shipyards that sell the courier: Beta (2 in stock, 1,000,000 credits), Gamma (0 in stock, 1,000,000 credits).");
        }

        [Test]
        public void YardThatDoesNotSellTheClass_IsRefused()
        {
            var t = YardTuning();
            t.Shipyards.ShipStockPerClassMin = 0;
            var sim = NewSim(10_000_000, tuning: t);
            var stock = ShipStock(sim, Gamma, Bulk);
            stock.LimitRollPercent = 0;
            stock.Count = 0;
            AssertRefusedUnchanged(sim, () => Buy(sim, Gamma, Bulk),
                "The shipyard at Gamma does not sell the bulk freighter. Shipyards that sell the bulk freighter: Beta (2 in stock, 3,500,000 credits).");
            Assert.IsNull(sim.Market.ShipPriceAt(Gamma, Bulk));
        }

        [Test]
        public void UnknownClassOrSystem_IsRefused()
        {
            var sim = NewSim(10_000_000);
            AssertRefusedUnchanged(sim, () => Buy(sim, Beta, "yacht"), "\"yacht\" is not a ship class in the tuning.");
            AssertRefusedUnchanged(sim, () => Buy(sim, 9, Courier), "There is no system with id 9.");
        }

        [Test]
        public void TuningEdit_ToShipOrDrivePrice_ChangesTheYardPrice_UsingTheStoredRoll()
        {
            var sim = NewSim(10_000_000, dockedAt: Beta);
            sim.Market.FindShipyard(Beta).PriceRollPercent = 93;
            Assert.AreEqual(930_000, sim.Market.ShipPriceAt(Beta, Courier));
            Assert.AreEqual(1_395_000, sim.Market.DrivePriceAt(Beta, Fast));
            Assert.AreEqual(1_000_000, sim.Market.ShipPriceAt(Gamma, Courier), "Gamma keeps its own roll");

            var edited = YardTuning();
            edited.FindShip(Courier).Price = 1_000_050;
            edited.FindDrive(Fast).Price = 1_666_667;
            sim.Market.ReplaceTuning(edited);
            Assert.AreEqual(93, sim.Market.FindShipyard(Beta).PriceRollPercent, "The stored roll does not change");
            Assert.AreEqual(930_047, sim.Market.ShipPriceAt(Beta, Courier), "1,000,050 x 93% = 930,046.5, halves round away from zero");
            Assert.AreEqual(1_550_000, sim.Market.DrivePriceAt(Beta, Fast), "1,666,667 x 93% = 1,550,000.31");

            Assert.IsTrue(sim.BuyShip(Beta, Courier, out _).Succeeded);
            Assert.AreEqual(10_000_000 - 930_047, sim.Credits, "The purchase charges the edited price");
            Assert.IsTrue(sim.BuyDrive(CourierId, Fast).Succeeded);
            Assert.AreEqual(10_000_000 - 930_047 - 1_550_000, sim.Credits);
        }

        [Test]
        public void Purchase_WorksWhilePaused_AndTakesNoGameTime()
        {
            var sim = NewSim(5_000_000);
            sim.Advance(12_345);
            sim.Pause();
            Assert.IsTrue(sim.BuyShip(Beta, Courier, out _).Succeeded);
            Assert.AreEqual(12_345, sim.CurrentTick);
            Assert.IsTrue(sim.Clock.IsPaused);
        }

        // ---- Names ----

        [Test]
        public void BoughtNames_AreNeverAlreadyInTheFleet_WhileUnusedNamesRemain()
        {
            var sim = NewSim(1_000_000_000);
            var list = sim.Market.Tuning.ShipNames;
            foreach (var s in AllStock(sim)) s.Count = 100;
            for (int i = 0; i < list.Count; i++)
            {
                var fleetNames = sim.Ships.Select(s => s.Name).ToList();
                Assert.IsTrue(sim.BuyShip(i % 2 == 0 ? Beta : Gamma, Courier, out int id).Succeeded);
                string name = sim.FindShip(id).Name;
                CollectionAssert.DoesNotContain(fleetNames, name, $"purchase {i + 1}");
                CollectionAssert.Contains(list, name);
            }
            CollectionAssert.AreEquivalent(list, sim.Ships.Skip(1).Select(s => s.Name).ToList(), "Every listed name used exactly once");
            Assert.IsTrue(sim.BuyShip(Beta, Courier, out int extra).Succeeded);
            string numbered = sim.FindShip(extra).Name;
            StringAssert.EndsWith(" 2", numbered);
            CollectionAssert.Contains(list, numbered.Substring(0, numbered.Length - 2));
            TestContext.WriteLine("25th name: " + numbered);
        }

        [Test]
        public void SameSeedAndCommands_GiveTheSameNames()
        {
            List<string> Run(long seed)
            {
                var sim = NewSim(1_000_000_000, seed: seed);
                foreach (var s in AllStock(sim)) s.Count = 100;
                for (int i = 0; i < 30; i++) sim.BuyShip(i % 3 == 0 ? Gamma : Beta, i % 2 == 0 ? Courier : Liner, out _);
                return sim.Ships.Select(s => s.Name).ToList();
            }
            var a = Run(7);
            var b = Run(7);
            CollectionAssert.AreEqual(a, b);
            CollectionAssert.AreNotEqual(a, Run(8), "A different seed draws different names");
            TestContext.WriteLine(string.Join(", ", a.Skip(1).Take(5)));
        }

        [Test]
        public void OneNameList_GivesTheLowestUnusedNumber_FromTwo()
        {
            var t = YardTuning();
            t.ShipNames = new List<string> { "Tern" };
            var sim = new Simulation();
            sim.AttachMarket(Markets.Market.Create(ThreeSystems(), t, 0));
            sim.SetCredits(100_000_000);
            foreach (var s in AllStock(sim)) s.Count = 100;
            sim.AddShip("Tern", Courier, Slow, Alpha);
            sim.AddShip("Tern 3", Courier, Slow, Alpha);
            var names = new List<string>();
            for (int i = 0; i < 3; i++)
            {
                Assert.IsTrue(sim.BuyShip(Beta, Courier, out int id).Succeeded);
                names.Add(sim.FindShip(id).Name);
            }
            CollectionAssert.AreEqual(new[] { "Tern 2", "Tern 4", "Tern 5" }, names);
        }

        [Test]
        public void NewGame_BoughtShipName_DiffersFromTheStarter_AndRepeats_On50Seeds()
        {
            var t = TestTuning.Load();
            for (long seed = 1; seed <= 50; seed++)
            {
                string Buy()
                {
                    var sim = NewGame.Create(seed, t).Simulation;
                    sim.SetCredits(10_000_000);
                    int yard = sim.Market.Shipyards[0].SystemId;
                    var r = sim.BuyShip(yard, Courier, out int id);
                    Assert.IsTrue(r.Succeeded, $"seed {seed}: {r.Reason}");
                    Assert.AreNotEqual(sim.Ships[0].Name, sim.FindShip(id).Name, $"seed {seed}");
                    CollectionAssert.Contains(t.ShipNames, sim.FindShip(id).Name);
                    return sim.FindShip(id).Name;
                }
                Assert.AreEqual(Buy(), Buy(), $"seed {seed}");
            }
        }
    }
}
