using NUnit.Framework;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.Voyages;
using static SpaceTrade.Sim.Tests.YardTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>The drive upgrade preview on a route (spec 7, owner's decision 4c for session 8).</summary>
    public class DriveUpgradePreviewTests
    {
        private static VoyagePlan EmptyTo(int shipId, int destination) => new VoyagePlan(shipId, destination, null, 0);

        [Test]
        public void TimeSaved_MatchesTheTransitFormula_OnEveryLeg()
        {
            var sim = NewSim(0);
            var p = sim.PreviewDriveUpgrade(CourierId, Beta, Fast, new[] { Alpha, Beta, Gamma });
            Assert.IsTrue(p.IsValid, string.Join(" ", p.Problems));
            Assert.AreEqual(3, p.Legs.Count);
            int[,] legs = { { Alpha, Beta }, { Beta, Gamma }, { Gamma, Alpha } };
            long[] distances = { 4_500, 3_000, 7_500 };
            long currentTotal = 0, newTotal = 0;
            for (int i = 0; i < 3; i++)
            {
                var leg = p.Legs[i];
                Assert.AreEqual(legs[i, 0], leg.FromId);
                Assert.AreEqual(legs[i, 1], leg.ToId);
                Assert.AreEqual(distances[i], leg.DistanceMilliLy);
                Assert.AreEqual(TransitMath.TransitTicks(distances[i], 1_500), leg.CurrentTicks);
                Assert.AreEqual(TransitMath.TransitTicks(distances[i], 3_750), leg.NewTicks);
                Assert.AreEqual(leg.CurrentTicks - leg.NewTicks, leg.SavedTicks);
                currentTotal += leg.CurrentTicks;
                newTotal += leg.NewTicks;
            }
            // By hand: 4.5 ly is 3 years at 1.5c and 1.2 years at 3.75c (spec 3.3); 3 ly is 2 and 0.8; 7.5 ly is 5 and 2.
            Assert.AreEqual(10_800_000, p.Legs[0].CurrentTicks);
            Assert.AreEqual(4_320_000, p.Legs[0].NewTicks);
            Assert.AreEqual(36_000_000, p.CurrentTotalTicks);
            Assert.AreEqual(14_400_000, p.NewTotalTicks);
            Assert.AreEqual(21_600_000, p.TimeSavedTicks, "6 game years saved per loop");
            Assert.AreEqual(currentTotal - newTotal, p.TimeSavedTicks);
        }

        [Test]
        public void EachLegIsRoundedUpSeparately_AsRealVoyagesAre()
        {
            var t = YardTuning();
            t.Drives.Add(new DriveTuning { Id = "odd_1_70", Name = "Odd 1.7c", SpeedMilliC = 1_700, Price = 1, FuelKgPerTonneLy = 22 });
            var sim = NewSim(0, tuning: t);
            var p = sim.PreviewDriveUpgrade(CourierId, Beta, "odd_1_70", new[] { Alpha, Beta, Gamma });
            // 4.5 ly: 9,529,411.76 -> 9,529,412; 3 ly: 6,352,941.18 -> 6,352,942; 7.5 ly: 15,882,352.94 -> 15,882,353.
            Assert.AreEqual(9_529_412, p.Legs[0].NewTicks);
            Assert.AreEqual(6_352_942, p.Legs[1].NewTicks);
            Assert.AreEqual(15_882_353, p.Legs[2].NewTicks);
            Assert.AreEqual(31_764_707, p.NewTotalTicks, "The sum of each leg rounded up, not 15 ly rounded up once (31,764,706)");
            Assert.AreEqual(TransitMath.TransitTicks(15_000, 1_700) + 1, p.NewTotalTicks);
        }

        [Test]
        public void Price_IsTheSameFigureBuyDriveCharges()
        {
            var sim = NewSim(5_000_000);
            sim.Market.FindShipyard(Gamma).PriceRollPercent = 93;
            Assert.IsTrue(sim.BuyShip(Gamma, Courier, out int id).Succeeded);
            long afterShip = sim.Credits;
            var p = sim.PreviewDriveUpgrade(id, Gamma, Fast, new[] { Gamma, Beta });
            Assert.AreEqual(1_395_000, p.Price);
            Assert.AreEqual(3, p.InStock);
            Assert.IsEmpty(p.Notes);
            Assert.IsTrue(sim.BuyDrive(id, Fast).Succeeded);
            Assert.AreEqual(afterShip - p.Price.Value, sim.Credits);
        }

        [Test]
        public void CrewOnlyFuel_MatchesTheQuoteForAnEmptyVoyageOnTheSameLeg()
        {
            var sim = NewSim(5_000_000, dockedAt: Beta);
            var p = sim.PreviewDriveUpgrade(CourierId, Beta, Fast, new[] { Beta, Gamma });
            var before = sim.Quote(EmptyTo(CourierId, Gamma));
            Assert.AreEqual(before.Fuel.RequiredKg, p.Legs[0].CurrentFuelKgEmptyCrewOnly);
            Assert.AreEqual(before.TransitTicks, p.Legs[0].CurrentTicks);
            Assert.AreEqual(4_069, p.Legs[0].CurrentFuelKgEmptyCrewOnly);
            Assert.AreEqual(4_069, p.Legs[1].CurrentFuelKgEmptyCrewOnly, "Same distance back");

            Assert.IsTrue(sim.BuyDrive(CourierId, Fast).Succeeded);
            var after = sim.Quote(EmptyTo(CourierId, Gamma));
            Assert.AreEqual(after.Fuel.RequiredKg, p.Legs[0].NewFuelKgEmptyCrewOnly);
            Assert.AreEqual(after.TransitTicks, p.Legs[0].NewTicks);
            Assert.AreEqual(5_835, p.Legs[0].NewFuelKgEmptyCrewOnly);
        }

        [Test]
        public void Preview_ChangesNothing_AndWorksForAShipInTransit_FromAnywhere()
        {
            var sim = NewSim(5_000_000, dockedAt: Beta);
            Assert.IsTrue(sim.Dispatch(EmptyTo(CourierId, Gamma), buyTopUp: true).Succeeded);
            sim.Advance(50_000);
            var bytes = Bytes(sim);
            var p = sim.PreviewDriveUpgrade(CourierId, Gamma, Fast, new[] { Gamma, Beta, Alpha });
            CollectionAssert.AreEqual(bytes, Bytes(sim));
            Assert.IsTrue(p.IsValid);
            Assert.AreEqual(3, p.Legs.Count);
            Assert.AreEqual(1_500_000, p.Price);
            Assert.AreEqual(Slow, p.CurrentDriveId);
        }

        [Test]
        public void SlowerDrive_ShowsNegativeTimeSaved()
        {
            var sim = NewSim(0);
            sim.AddShip("Quick One", Courier, Fast, Beta);
            var p = sim.PreviewDriveUpgrade(2, Beta, Slow, new[] { Alpha, Beta });
            Assert.AreEqual(-2 * (10_800_000 - 4_320_000), p.TimeSavedTicks);
            Assert.Less(p.Legs[0].NewFuelKgEmptyCrewOnly, p.Legs[0].CurrentFuelKgEmptyCrewOnly, "The slower drive burns less per tonne-ly");
        }

        [Test]
        public void Notes_WhenTheYardCannotSellTheDrive_OrItIsAlreadyFitted()
        {
            var sim = NewSim(0);
            var p = sim.PreviewDriveUpgrade(CourierId, Alpha, Fast, new[] { Alpha, Beta });
            Assert.IsTrue(p.IsValid);
            Assert.IsNull(p.Price);
            CollectionAssert.AreEqual(new[] { "Alpha has no shipyard, so the Alcubierre 3.75c cannot be bought there." }, p.Notes);
            Assert.AreEqual(2, p.Legs.Count, "The times are still shown");

            DriveStock(sim, Beta, Fast).Count = 0;
            p = sim.PreviewDriveUpgrade(CourierId, Beta, Fast, new[] { Alpha, Beta });
            Assert.AreEqual(1_500_000, p.Price);
            Assert.AreEqual(0, p.InStock);
            StringAssert.StartsWith("The shipyard at Beta has 0 of the Alcubierre 3.75c in stock (stock limit 3).", p.Notes[0]);

            p = sim.PreviewDriveUpgrade(CourierId, Beta, Slow, new[] { Alpha, Beta });
            CollectionAssert.Contains(p.Notes, "Test Courier already has the Alcubierre 1.5c installed.");
            Assert.AreEqual(0, p.TimeSavedTicks);
        }

        [Test]
        public void BadRoutesAndIds_AreProblems_WithNoLegs()
        {
            var sim = NewSim(0);
            void AssertProblem(DriveUpgradePreviewArgs a, string expected)
            {
                var p = sim.PreviewDriveUpgrade(a.Ship, a.Yard, a.Drive, a.Stops);
                Assert.IsFalse(p.IsValid);
                CollectionAssert.Contains(p.Problems, expected);
                Assert.IsEmpty(p.Legs);
            }
            AssertProblem(new DriveUpgradePreviewArgs(CourierId, Beta, Fast, new[] { Beta }), "A route needs at least 2 stops; this one has 1.");
            AssertProblem(new DriveUpgradePreviewArgs(CourierId, Beta, Fast, null), "A route needs at least 2 stops; this one has 0.");
            AssertProblem(new DriveUpgradePreviewArgs(CourierId, Beta, Fast, new[] { Beta, Gamma, Beta }), "Stops 3 and 1 are both Beta; each leg must go to a different system.");
            AssertProblem(new DriveUpgradePreviewArgs(CourierId, Beta, Fast, new[] { Beta, 7 }), "Stop 2: there is no system with id 7.");
            AssertProblem(new DriveUpgradePreviewArgs(CourierId, Beta, "warp_9", new[] { Alpha, Beta }), "\"warp_9\" is not a drive in the tuning.");
            AssertProblem(new DriveUpgradePreviewArgs(5, Beta, Fast, new[] { Alpha, Beta }), "There is no ship with id 5.");
            AssertProblem(new DriveUpgradePreviewArgs(CourierId, 11, Fast, new[] { Alpha, Beta }), "There is no system with id 11.");
        }

        private readonly struct DriveUpgradePreviewArgs
        {
            public readonly int Ship, Yard;
            public readonly string Drive;
            public readonly int[] Stops;

            public DriveUpgradePreviewArgs(int ship, int yard, string drive, int[] stops)
            {
                Ship = ship;
                Yard = yard;
                Drive = drive;
                Stops = stops;
            }
        }
    }
}
