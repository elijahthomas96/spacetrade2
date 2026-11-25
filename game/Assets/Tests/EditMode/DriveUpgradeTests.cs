using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Ships;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Voyages;
using static SpaceTrade.Sim.Tests.YardTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>Fitting drives (spec 7, owner's decision 4 for session 8).</summary>
    public class DriveUpgradeTests
    {
        private static VoyagePlan EmptyTo(int shipId, int destination) => new VoyagePlan(shipId, destination, null, 0);

        private static void AssertRefusedUnchanged(Simulation sim, int shipId, string driveId, string expectedReason)
        {
            var before = Bytes(sim);
            var r = sim.BuyDrive(shipId, driveId);
            Assert.IsFalse(r.Succeeded);
            Assert.AreEqual(expectedReason, r.Reason);
            CollectionAssert.AreEqual(before, Bytes(sim), "A refused upgrade changes nothing");
        }

        [Test]
        public void LocalUpgrade_FitsTheDrive_ChargesExactlyTheShownPrice_AndNothingElseChangesValue()
        {
            var sim = NewSim(5_000_000, dockedAt: Beta);
            var courier = sim.FindShip(CourierId);
            courier.Fuel.Add(5_000, 10_000);
            courier.Food.Add(800, 6_400);
            sim.Market.FindShipyard(Beta).PriceRollPercent = 93;
            long shown = sim.Market.DrivePriceAt(Beta, Fast).Value;
            Assert.AreEqual(1_395_000, shown);
            long creditsAndSupplies = VoyageTestSupport.CreditsPlusSuppliesAtCost(sim);
            string before = sim.DescribeState();

            var r = sim.BuyDrive(CourierId, Fast);
            Assert.IsTrue(r.Succeeded, r.Reason);
            Assert.AreEqual(Fast, courier.DriveId);
            Assert.AreEqual(5_000_000 - shown, sim.Credits);
            Assert.AreEqual(creditsAndSupplies - shown, VoyageTestSupport.CreditsPlusSuppliesAtCost(sim));

            var (removed, added) = Diff(before, sim.DescribeState());
            Assert.AreEqual(3, removed.Count, string.Join("\n", removed));
            Assert.AreEqual(3, added.Count, string.Join("\n", added));
            Assert.AreEqual("tick=0 speed=X1 paused=False credits=3605000 suppliesBought=0 inSystemIncome=0 nextShip=2", added.Single(l => l.StartsWith("tick=")));
            Assert.AreEqual("yardstock 1 drive alcubierre_3_75 count=3 limitRoll=100", removed.Single(l => l.StartsWith("yardstock")));
            Assert.AreEqual("yardstock 1 drive alcubierre_3_75 count=2 limitRoll=100", added.Single(l => l.StartsWith("yardstock")));
            Assert.AreEqual("ship 1 \"Test Courier\" class=courier drive=alcubierre_1_50 docked=1 fuel=5000kg/10000cr food=800kg/6400cr water=0kg/0cr insystem=-",
                removed.Single(l => l.StartsWith("ship ")));
            Assert.AreEqual("ship 1 \"Test Courier\" class=courier drive=alcubierre_3_75 docked=1 fuel=5000kg/10000cr food=800kg/6400cr water=0kg/0cr insystem=-",
                added.Single(l => l.StartsWith("ship ")));
        }

        [Test]
        public void RemoteUpgrade_IsRefused_WithTheReason()
        {
            var sim = NewSim(5_000_000, dockedAt: Alpha);
            AssertRefusedUnchanged(sim, CourierId, Fast,
                "Test Courier is docked at Alpha, which has no shipyard. The nearest shipyard that stocks the Alcubierre 3.75c is Beta, 4.5 ly away (3 in stock, 1,500,000 credits).");
        }

        [Test]
        public void UpgradeAtAYardThatDoesNotStockTheDrive_IsRefused_NamingTheNearestThatDoes()
        {
            var t = YardTuning();
            t.Shipyards.DriveStockPerTierMin = 0;
            var sim = NewSim(5_000_000, dockedAt: Beta, tuning: t);
            var stock = DriveStock(sim, Beta, Fast);
            stock.LimitRollPercent = 0;
            stock.Count = 0;
            AssertRefusedUnchanged(sim, CourierId, Fast,
                "The shipyard at Beta, where Test Courier is docked, does not stock the Alcubierre 3.75c. The nearest shipyard that stocks the Alcubierre 3.75c is Gamma, 3 ly away (3 in stock, 1,500,000 credits).");
        }

        [Test]
        public void ShipInFlight_IsRefused_WithOnlyTheInTransitReason()
        {
            var sim = NewSim(5_000_000, dockedAt: Beta);
            Assert.IsTrue(sim.Dispatch(EmptyTo(CourierId, Gamma), buyTopUp: true).Succeeded);
            sim.Advance(1_000);
            string reason = "Test Courier is in transit to Gamma, arriving Y3 M01 D01. A drive can only be fitted while the ship is docked at a shipyard that stocks it.";
            AssertRefusedUnchanged(sim, CourierId, Fast, reason);
            AssertRefusedUnchanged(sim, CourierId, Slow, reason);
        }

        [Test]
        public void SameDrive_IsRefused()
        {
            var sim = NewSim(5_000_000, dockedAt: Beta);
            AssertRefusedUnchanged(sim, CourierId, Slow, "Test Courier already has the Alcubierre 1.5c installed. Choose a different drive.");
        }

        [Test]
        public void OutOfStock_IsRefused_WithTheNumbers()
        {
            var sim = NewSim(5_000_000, dockedAt: Beta);
            DriveStock(sim, Beta, Fast).Count = 0;
            sim.Advance(200 * GameTime.TicksPerDay - 1);
            DriveStock(sim, Beta, Fast).Count = 0;
            AssertRefusedUnchanged(sim, CourierId, Fast,
                "The shipyard at Beta has 0 of the Alcubierre 3.75c in stock (stock limit 3). It restocks 1 every 180 game days; the next restock is Y2 M01 D01.");
        }

        [Test]
        public void NotEnoughCredits_IsRefused_WithTheNumbers()
        {
            var sim = NewSim(1_499_999, dockedAt: Beta);
            AssertRefusedUnchanged(sim, CourierId, Fast, "The Alcubierre 3.75c costs 1,500,000 credits at Beta; you have 1,499,999. You need 1 more credit.");
        }

        [Test]
        public void UnknownShipOrDrive_IsRefused()
        {
            var sim = NewSim(5_000_000, dockedAt: Beta);
            AssertRefusedUnchanged(sim, 9, Fast, "There is no ship with id 9.");
            AssertRefusedUnchanged(sim, CourierId, "warp_9", "\"warp_9\" is not a drive in the tuning.");
        }

        [Test]
        public void SlowerDrive_CanBeFitted_AtFullPrice_WithNoRefund()
        {
            var sim = NewSim(5_000_000, dockedAt: Beta);
            sim.AddShip("Quick One", Courier, Fast, Beta);
            var r = sim.BuyDrive(2, Mid);
            Assert.IsTrue(r.Succeeded, r.Reason);
            Assert.AreEqual(Mid, sim.FindShip(2).DriveId);
            Assert.AreEqual(5_000_000 - 600_000, sim.Credits, "Full price, nothing back for the 3.75c");
            Assert.AreEqual(3, DriveStock(sim, Beta, Fast).Count, "The old drive does not go back into stock");
            Assert.AreEqual(2, DriveStock(sim, Beta, Mid).Count);
        }

        [Test]
        public void ShipOnInSystemTransport_CanBeUpgraded_AndKeepsEarning_WithoutRestartingTheCount()
        {
            var sim = NewSim(5_000_000, dockedAt: Beta);
            Assert.IsTrue(sim.SetInSystemTransport(CourierId, true).Succeeded);
            sim.Advance(6_000);
            Assert.IsTrue(sim.BuyDrive(CourierId, Fast).Succeeded);
            Assert.AreEqual(0, sim.FindShip(CourierId).InSystemSinceTick, "The count is not restarted");
            Assert.IsTrue(sim.FindShip(CourierId).IsOnInSystemTransport);
            sim.Advance(GameTime.TicksPerDay);
            Assert.AreEqual(5_000_000 - 1_500_000 + 17, sim.Credits, "The first full day since switching on is paid");
            sim.Advance(3 * GameTime.TicksPerDay);
            Assert.AreEqual(5_000_000 - 1_500_000 + 3 * 17, sim.Credits);
        }

        [Test]
        public void NextQuote_UsesTheNewDrivesSpeedAndFuelBurn()
        {
            var sim = NewSim(5_000_000, dockedAt: Beta);
            var q = sim.Quote(EmptyTo(CourierId, Gamma));
            // 3 ly at 1.5c: 720 days; food 2 x 720 x 0.6 = 864 kg, water 576 kg; loaded 60 + 0.864 + 0.576 + 0.2 = 61.64 t; fuel 3 x 61.64 x 22 = 4,068.24, rounds up to 4,069 kg.
            Assert.AreEqual(7_200_000, q.TransitTicks);
            Assert.AreEqual(864, q.Food.RequiredKg);
            Assert.AreEqual(576, q.Water.RequiredKg);
            Assert.AreEqual(4_069, q.Fuel.RequiredKg);

            Assert.IsTrue(sim.BuyDrive(CourierId, Mid).Succeeded);
            q = sim.Quote(EmptyTo(CourierId, Gamma));
            // 3 ly at 2.25c: 480 days; food 576 kg, water 384 kg; loaded 61.16 t; fuel 3 x 61.16 x 26 = 4,770.48, rounds up to 4,771 kg.
            Assert.AreEqual(4_800_000, q.TransitTicks);
            Assert.AreEqual(576, q.Food.RequiredKg);
            Assert.AreEqual(384, q.Water.RequiredKg);
            Assert.AreEqual(4_771, q.Fuel.RequiredKg);

            Assert.IsTrue(sim.BuyDrive(CourierId, Fast).Succeeded);
            q = sim.Quote(EmptyTo(CourierId, Gamma));
            // 3 ly at 3.75c: 288 days; food 345.6 -> 346 kg, water 230.4 -> 231 kg; loaded 60.777 t; fuel 3 x 60.777 x 32 = 5,834.592, rounds up to 5,835 kg.
            Assert.AreEqual(2_880_000, q.TransitTicks);
            Assert.AreEqual(346, q.Food.RequiredKg);
            Assert.AreEqual(231, q.Water.RequiredKg);
            Assert.AreEqual(5_835, q.Fuel.RequiredKg);

            Assert.IsTrue(sim.Dispatch(EmptyTo(CourierId, Gamma), buyTopUp: true).Succeeded);
            Assert.AreEqual(2_880_000, sim.FindShip(CourierId).Voyage.ArrivalTick, "Dispatch uses the same quote");
            Assert.AreEqual(Fast, sim.FindShip(CourierId).Voyage.DriveId);
        }
    }
}
