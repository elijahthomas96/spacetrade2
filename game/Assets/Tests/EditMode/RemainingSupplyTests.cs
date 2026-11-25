using NUnit.Framework;
using SpaceTrade.Sim.Ships;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>
    /// The in-flight remaining-supplies figure (spec 6.7, 9.1): read-only, worked out from the locked
    /// terms and the current tick, never stored. Supplies are only really used at arrival.
    /// </summary>
    public class RemainingSupplyTests
    {
        private const long HandArrivalTick = 10_800_000;
        private static readonly SupplyKind[] Kinds = { SupplyKind.Fuel, SupplyKind.Food, SupplyKind.Water };

        /// <summary>The leftover-supplies hand example: uses fuel 8,514, food 2,592, water 1,728; aboard after top-up 8,514 / 2,592 / 3,000.</summary>
        private static Simulation DispatchedWithLeftovers()
        {
            var sim = NewSim(100_000);
            var ship = Courier(sim);
            ship.Water.Add(3_000, 1_500);
            ship.Food.Add(1_000, 5_000);
            Assert.IsTrue(sim.Dispatch(Plan(20, 2), true).Succeeded);
            return sim;
        }

        private static long Remaining(Simulation sim, SupplyKind k) => sim.RemainingSupplyKg(CourierId, k);

        [Test]
        public void AtDispatch_EqualsWhatIsAboard()
        {
            var sim = DispatchedWithLeftovers();
            var ship = Courier(sim);
            Assert.AreEqual(8_514, Remaining(sim, SupplyKind.Fuel));
            Assert.AreEqual(2_592, Remaining(sim, SupplyKind.Food));
            Assert.AreEqual(3_000, Remaining(sim, SupplyKind.Water));
            foreach (var k in Kinds) Assert.AreEqual(ship.Supply(k).Kg, Remaining(sim, k), k.ToString());
        }

        [Test]
        public void MidTrip_IsAboardLessTheShareForTimeElapsed_RoundedByTheOneRule()
        {
            var sim = DispatchedWithLeftovers();

            // A third of the way: fuel 8,514 / 3 = 2,838; food 864; water 576 used.
            sim.Advance(HandArrivalTick / 3);
            Assert.AreEqual(8_514 - 2_838, Remaining(sim, SupplyKind.Fuel));
            Assert.AreEqual(2_592 - 864, Remaining(sim, SupplyKind.Food));
            Assert.AreEqual(3_000 - 576, Remaining(sim, SupplyKind.Water));

            // Halfway: 4,257 / 1,296 / 864 used.
            sim.Advance(HandArrivalTick / 2);
            Assert.AreEqual(8_514 - 4_257, Remaining(sim, SupplyKind.Fuel));
            Assert.AreEqual(2_592 - 1_296, Remaining(sim, SupplyKind.Food));
            Assert.AreEqual(3_000 - 864, Remaining(sim, SupplyKind.Water));

            // The stored stock has not moved: supplies are used only at arrival.
            Assert.AreEqual(8_514, Courier(sim).Fuel.Kg);
            Assert.AreEqual(2_592, Courier(sim).Food.Kg);
            Assert.AreEqual(3_000, Courier(sim).Water.Kg);
        }

        [Test]
        public void MidTrip_ExactHalfKilogram_RoundsAwayFromZero()
        {
            // Water: 1,728 x 3,125 / 10,800,000 = 0.5 exactly -> 1 kg used.
            var sim = DispatchedWithLeftovers();
            sim.Advance(3_125);
            Assert.AreEqual(3_000 - 1, Remaining(sim, SupplyKind.Water));
            // Food: 2,592 x 3,125 / 10,800,000 = 0.75 -> 1. Fuel: 8,514 x 3,125 / 10,800,000 = 2.46 -> 2.
            Assert.AreEqual(2_592 - 1, Remaining(sim, SupplyKind.Food));
            Assert.AreEqual(8_514 - 2, Remaining(sim, SupplyKind.Fuel));
        }

        [Test]
        public void AtArrival_EqualsExactlyWhatSettlementLeavesAboard()
        {
            var sim = DispatchedWithLeftovers();
            var ship = Courier(sim);
            sim.Advance(HandArrivalTick - 1);
            var voyage = ship.Voyage;

            var expected = new long[3];
            for (int i = 0; i < 3; i++)
            {
                var k = Kinds[i];
                expected[i] = ship.Supply(k).Kg - voyage.UsedSoFarKg(k, voyage.ArrivalTick); // the formula at the arrival tick
                Assert.AreEqual(voyage.UseKg(k), voyage.UsedSoFarKg(k, voyage.ArrivalTick), k + ": the whole locked use by arrival");
            }
            CollectionAssert.AreEqual(new long[] { 0, 0, 1_272 }, expected);

            sim.Advance(HandArrivalTick);
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(expected[i], ship.Supply(Kinds[i]).Kg, Kinds[i] + " left by settlement");
                Assert.AreEqual(expected[i], Remaining(sim, Kinds[i]), Kinds[i] + " shown after docking");
            }
        }

        [Test]
        public void NeverIncreases_AndOnlyEverShowsWholeKilograms()
        {
            var sim = DispatchedWithLeftovers();
            var last = new long[] { long.MaxValue, long.MaxValue, long.MaxValue };
            for (long tick = 0; tick <= HandArrivalTick; tick += 10_800)
            {
                sim.Advance(tick);
                for (int i = 0; i < 3; i++)
                {
                    long now = Remaining(sim, Kinds[i]);
                    Assert.LessOrEqual(now, last[i], $"{Kinds[i]} at tick {tick}");
                    last[i] = now;
                }
            }
            CollectionAssert.AreEqual(new long[] { 0, 0, 1_272 }, last);
        }

        [Test]
        public void CallingIt_LeavesTheStateIdentical()
        {
            var sim = DispatchedWithLeftovers();
            foreach (long tick in new long[] { 0, 3_125, HandArrivalTick / 2, HandArrivalTick - 1 })
            {
                sim.Advance(tick);
                byte[] before = StateBytes(sim);
                for (int n = 0; n < 100; n++)
                    foreach (var k in Kinds) Remaining(sim, k);
                CollectionAssert.AreEqual(before, StateBytes(sim), $"tick {tick}");
            }
        }

        [Test]
        public void WhileDocked_IsWhatIsAboard()
        {
            var sim = NewSim(100_000);
            Courier(sim).Water.Add(500, 100);
            Assert.AreEqual(500, Remaining(sim, SupplyKind.Water));
            Assert.AreEqual(0, Remaining(sim, SupplyKind.Fuel));
        }
    }
}
