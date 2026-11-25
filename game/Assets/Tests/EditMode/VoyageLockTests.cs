using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Ships;
using SpaceTrade.Sim.Tuning;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>Tuning edits reach existing saves, but never a voyage already in flight (spec 6.6, 12).</summary>
    public class VoyageLockTests
    {
        private const long HandArrivalTick = 10_800_000;
        private const string Drive = "alcubierre_1_50";

        [Test]
        public void Payout_StaysLocked_WhenRatesFaresAndDistanceFactorChangeMidFlight()
        {
            var sim = NewSim(100_000);
            var q = sim.Quote(Plan(20, 2));
            Assert.IsTrue(sim.Dispatch(Plan(20, 2), true).Succeeded);
            sim.Advance(HandArrivalTick / 2);

            var t = FlatTuning();
            t.FindCommodity("ore").BaseRatePerTonne *= 3;
            t.Passengers.BaseFare *= 3;
            t.Rates.DistanceFactorBasePercent += 50;
            t.Rates.DistanceFactorPerLyPercent *= 2;
            sim.Market.ReplaceTuning(t);

            long newRate = sim.Market.RatePerTonne(sim.Market.FindOffer(Alpha, "ore", Beta));
            long newFare = sim.Market.FarePerPassenger(sim.Market.FindPool(Alpha, Beta));
            TestContext.WriteLine($"published now: ore {newRate} per tonne (was 4,200), fare {newFare} (was 28,000)");
            Assert.AreNotEqual(4_200, newRate);
            Assert.AreNotEqual(28_000, newFare);

            long before = sim.Credits;
            sim.Advance(HandArrivalTick);
            var r = sim.Receipts.Single();
            Assert.AreEqual(140_000, sim.Credits - before, "Paid at the locked rate and fare.");
            Assert.AreEqual(q.GrossRevenue, r.GrossRevenue);
            Assert.AreEqual(4_200, r.Freight.Single().RatePerTonne);
            Assert.AreEqual(28_000, r.FarePerPassenger);
            Assert.AreEqual(q.NetProfit, r.NetProfit);
        }

        [Test]
        public void ArrivalTick_StaysLocked_WhenTheDriveSpeedChangesMidFlight()
        {
            var sim = NewSim(100_000);
            Assert.IsTrue(sim.Dispatch(Plan(20, 2), true).Succeeded);
            sim.Advance(1_000_000);

            var t = FlatTuning();
            t.FindDrive(Drive).SpeedMilliC = 3_750; // the same trip would now take 1.2 years
            sim.Market.ReplaceTuning(t);
            Assert.AreEqual(4_320_000, Timing.TransitMath.TransitTicks(4_500, 3_750));

            sim.Advance(4_320_000);
            Assert.IsTrue(Courier(sim).IsInTransit, "Not at the new, shorter time.");
            Assert.AreEqual(HandArrivalTick, Courier(sim).Voyage.ArrivalTick);
            // The display share also uses the locked transit time: 1,728 x 4.32 / 10.8 = 691.2 -> 691 used, 1,037 left.
            Assert.AreEqual(1_037, sim.RemainingSupplyKg(CourierId, SupplyKind.Water));

            sim.Advance(HandArrivalTick - 1);
            Assert.IsTrue(Courier(sim).IsInTransit);
            sim.Advance(HandArrivalTick);
            Assert.IsFalse(Courier(sim).IsInTransit);
            Assert.AreEqual(Beta, Courier(sim).DockedSystemId);
            Assert.AreEqual(HandArrivalTick, sim.Receipts.Single().ElapsedTicks);
        }

        [Test]
        public void SuppliesUsedAndTheirCost_StayLocked_WhenFoodWaterAndFuelRatesChangeMidFlight()
        {
            var sim = NewSim(100_000);
            var ship = Courier(sim);
            ship.Water.Add(3_000, 1_500);
            ship.Food.Add(1_000, 5_000);
            var q = sim.Quote(Plan(20, 2));
            Assert.IsTrue(sim.Dispatch(Plan(20, 2), true).Succeeded);
            sim.Advance(HandArrivalTick / 3);

            var t = FlatTuning();
            t.Provisions.FoodGramsPerPersonPerDay *= 2;
            t.Provisions.WaterGramsPerPersonPerDay *= 2;
            t.FindDrive(Drive).FuelKgPerTonneLy *= 2;
            t.Provisions.FuelBasePricePerTonne *= 5;
            t.Provisions.FoodBasePricePerTonne *= 5;
            t.Provisions.WaterBasePricePerTonne *= 5;
            sim.Market.ReplaceTuning(t);

            sim.Advance(HandArrivalTick);
            var r = sim.Receipts.Single();
            for (int i = 0; i < 3; i++)
            {
                TestContext.WriteLine($"{r.Supplies[i].Kind}: used {r.Supplies[i].Kg} kg costing {r.Supplies[i].Cost} (quoted {q.Supplies[i].RequiredKg} kg, {q.Supplies[i].UsedCost})");
                Assert.AreEqual(q.Supplies[i].RequiredKg, r.Supplies[i].Kg, r.Supplies[i].Kind + " kg");
                Assert.AreEqual(q.Supplies[i].UsedCost, r.Supplies[i].Cost, r.Supplies[i].Kind + " cost");
            }
            Assert.AreEqual(38_175, r.SuppliesCost);
            Assert.AreEqual(1_272, ship.Water.Kg);
            Assert.AreEqual(636, ship.Water.CostCredits);
            Assert.AreEqual(0, ship.Fuel.Kg);
            Assert.AreEqual(0, ship.Food.Kg);
        }

        [Test]
        public void NewVoyages_DoUseTheNewTuning()
        {
            var sim = NewSim(100_000);
            var t = FlatTuning();
            t.FindDrive(Drive).SpeedMilliC = 3_750;
            sim.Market.ReplaceTuning(t);
            Assert.AreEqual(4_320_000, sim.Quote(Plan(20, 2)).TransitTicks, "Only voyages in flight are locked.");
        }
    }
}
