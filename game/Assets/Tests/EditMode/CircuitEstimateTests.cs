using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SpaceTrade.Sim.Routes;
using SpaceTrade.Sim.Timing;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;
using static SpaceTrade.Sim.Tests.RouteTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>The whole-circuit estimate (spec 8.6), including empty legs.</summary>
    public class CircuitEstimateTests
    {
        [Test]
        public void EmptyLegCosts_AreInTheCircuitEstimate_HandWorked()
        {
            var sim = NewSim(1_000_000);
            var e = sim.EstimateCircuit(CourierId, OreShuttle(passengers: true));
            Assert.IsTrue(e.IsValid, string.Join(" ", e.Problems));
            Assert.AreEqual(2, e.Legs.Count);

            // Loaded leg, Alpha to Beta, 4.5 ly, 1,080 days: 4 passengers (6 people eat 6.48 t), 23 t of ore.
            var loaded = e.Legs[0];
            Assert.AreEqual(4, loaded.EstimatedPassengers);
            Assert.AreEqual(23, loaded.EstimatedFreightTonnes);
            Assert.AreEqual(23 * 4_200 + 4 * 28_000, loaded.EstimatedRevenue);
            Assert.AreEqual(8_918, loaded.FuelKg, "4.5 ly x 90.08 t x 22 kg, rounded up");
            Assert.AreEqual(17_836 + 37_325 + 3_110, loaded.EstimatedSupplyCost, "Alpha: fuel 2,000/t, food 9,600/t, water 1,200/t");
            Assert.IsTrue(loaded.SupplyKgAreEstimates);

            // Empty leg, Beta to Alpha: crew only. Food 2 x 1,080 x 0.6 kg = 1,296 kg, water 864 kg,
            // fuel 4.5 ly x 62.36 t x 22 kg = 6,173.64, rounded up to 6,174 kg.
            var empty = e.Legs[1];
            Assert.IsTrue(empty.IsEmptyLeg);
            Assert.IsFalse(empty.SupplyKgAreEstimates, "An empty leg's kilograms are exact");
            Assert.AreEqual(0, empty.EstimatedRevenue);
            Assert.AreEqual(6_174, empty.FuelKg);
            Assert.AreEqual(1_296, empty.FoodKg);
            Assert.AreEqual(864, empty.WaterKg);
            Assert.AreEqual(8_644 + 10_368 + 864, empty.EstimatedSupplyCost, "Beta (refinery): fuel 1,400/t, food 8,000/t, water 1,000/t");
            Assert.AreEqual(-19_876, empty.EstimatedProfit);

            Assert.AreEqual(2 * Leg45, e.TotalTransitTicks);
            Assert.AreEqual(208_600, e.EstimatedRevenue);
            Assert.AreEqual(58_271 + 19_876, e.EstimatedSupplyCost);
            Assert.AreEqual(130_453, e.EstimatedProfit);
            Assert.AreEqual(21_742, e.EstimatedProfitPerGameYear, "130,453 over 6 game years");
            Assert.IsTrue(e.AllLegsCanBeFlown);
            Assert.AreEqual(0, e.Notes.Count);
        }

        [Test]
        public void TransitTimes_AreExact_AndMatchTheTransitFormula()
        {
            var sim = Triangle(1_000_000);
            var e = sim.EstimateCircuit(CourierId, Circuit());
            foreach (var leg in e.Legs)
            {
                Assert.AreEqual(3_000, leg.DistanceMilliLy);
                Assert.AreEqual(TransitMath.TransitTicks(3_000, 1_500), leg.TransitTicks);
            }
            Assert.AreEqual(6 * Year, e.TotalTransitTicks);
        }

        [Test]
        public void EveryMoneyAndLoadFigure_IsNamedAsAnEstimate()
        {
            foreach (var type in new[] { typeof(CircuitEstimate), typeof(CircuitLeg) })
            foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                bool money = new[] { "Revenue", "Cost", "Profit", "Passengers", "Freight" }.Any(w => p.Name.Contains(w));
                if (money) StringAssert.StartsWith("Estimated", p.Name, $"{type.Name}.{p.Name} depends on bookings and must be labelled as an estimate");
            }
            Assert.IsNotNull(typeof(CircuitLeg).GetProperty("TransitTicks"));
            Assert.IsNotNull(typeof(CircuitEstimate).GetProperty("TotalTransitTicks"));
        }

        [Test]
        public void StopBelowItsMinimumToday_GetsANote()
        {
            var sim = NewSim(1_000_000);
            SetSupply(sim, Alpha, "ore", 5);
            var e = sim.EstimateCircuit(CourierId, OreShuttle(minFreight: 20));
            Assert.AreEqual(5, e.Legs[0].EstimatedFreightTonnes, "Today's stock");
            Assert.AreEqual(1, e.Notes.Count);
            StringAssert.Contains("Stop 1 (Alpha): today's stock gives 5 t of freight", e.Notes[0]);
            StringAssert.Contains("the ship would wait there (Waiting for 15 more tonnes of ore). Waiting time is not counted.", e.Notes[0]);
        }

        [Test]
        public void LegThatCannotBeFlown_GetsANote()
        {
            var sim = NewSim(1_000_000, distanceMilliLy: 20_000);
            var e = sim.EstimateCircuit(CourierId, OreShuttle());
            Assert.IsFalse(e.AllLegsCanBeFlown);
            Assert.IsFalse(e.Legs[0].CanBeFlown);
            StringAssert.Contains("out of range", e.Notes[0]);
        }

        [Test]
        public void Estimate_ChangesNothing_WorksInFlight_AndReportsBadOrders()
        {
            var sim = NewSim(1_000_000);
            Ok(sim.Dispatch(Plan(10, 0), true));
            var before = StateBytes(sim);
            var e = sim.EstimateCircuit(CourierId, OreShuttle(passengers: true));
            Assert.IsTrue(e.IsValid);
            Assert.AreEqual(4, e.Legs[0].EstimatedPassengers);
            CollectionAssert.AreEqual(before, StateBytes(sim));

            var bad = sim.EstimateCircuit(CourierId, new[] { RouteStop.Empty(Alpha) });
            Assert.IsFalse(bad.IsValid);
            StringAssert.Contains("at least 2 stops", bad.Problems[0]);
            Assert.IsFalse(sim.EstimateCircuit(9, OreShuttle()).IsValid);
            CollectionAssert.AreEqual(before, StateBytes(sim));
        }
    }
}
