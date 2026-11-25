using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.Voyages;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>Checks shared by the fixture tests and the one live-file test.</summary>
    internal static class NewGameChecks
    {
        public const int StarterShipId = 1;

        /// <summary>
        /// The opening-offer guarantee (spec 6.4) on a fresh game, checked independently of
        /// <see cref="OpeningOfferCheck.Holds"/>: enough feasible, affordable jobs, enough profitable ones,
        /// and two to different destinations where one has fewer transit ticks and the other more net profit.
        /// Returns the number of feasible jobs.
        /// </summary>
        public static int AssertGuarantee(Simulation sim, TuningData t, string context)
        {
            var jobs = OpeningOffers.FindJobs(sim, StarterShipId);
            foreach (var j in jobs)
            {
                var again = sim.Quote(j.Quote.Plan);
                Assert.IsTrue(again.IsFeasible, $"{context}: job is not feasible: {string.Join(" ", again.Problems)}");
                Assert.LessOrEqual(again.TopUpCost, t.Start.StartingCredits, $"{context}: job is not affordable with starting credits");
                Assert.AreEqual(j.NetProfit, again.NetProfit, context);
                Assert.AreEqual(j.TransitTicks, again.TransitTicks, context);
            }
            Assert.GreaterOrEqual(jobs.Count, t.Start.OpeningOffers.MinFeasibleJobs, $"{context}: too few feasible jobs");
            Assert.GreaterOrEqual(jobs.Count(j => j.NetProfit > 0), t.Start.OpeningOffers.MinProfitableJobs, $"{context}: too few profitable jobs");
            bool pair = jobs.Any(a => jobs.Any(b => a.DestinationId != b.DestinationId && a.TransitTicks < b.TransitTicks && a.NetProfit < b.NetProfit));
            Assert.IsTrue(pair, $"{context}: no two jobs where one is quicker and the other earns more");
            Assert.IsTrue(OpeningOffers.Check(sim, StarterShipId).Holds, context);
            return jobs.Count;
        }

        /// <summary>
        /// The starter ship, with empty supplies and the starting credits, can buy what it needs and fly to its
        /// nearest shipyard. Dispatches it there and lands it. Returns false when the start system has a yard itself.
        /// </summary>
        public static bool AssertStarterReachesNearestShipyard(Simulation sim, TuningData t, string context)
        {
            var galaxy = sim.Market.Galaxy;
            if (galaxy.StartSystem.HasShipyard) return false;
            var yard = galaxy.Systems.Where(s => s.HasShipyard)
                .OrderBy(s => galaxy.DistanceMilliLy(galaxy.StartSystemId, s.Id)).ThenBy(s => s.Id).First();

            var plan = new VoyagePlan(StarterShipId, yard.Id);
            var quote = sim.Quote(plan);
            Assert.IsTrue(quote.IsFeasible, $"{context}: cannot reach {yard.Name}: {string.Join(" ", quote.Problems)}");
            Assert.Greater(quote.TopUpCost, 0, $"{context}: the starter ship should have to buy its supplies");
            Assert.LessOrEqual(quote.TopUpCost, t.Start.StartingCredits, context);

            Assert.IsTrue(sim.Dispatch(plan, buyTopUp: true).Succeeded, context);
            Assert.AreEqual(t.Start.StartingCredits - quote.TopUpCost, sim.Credits, $"{context}: supplies are paid from the starting credits");
            sim.Advance(quote.ArrivalTick);
            Assert.AreEqual(yard.Id, sim.FindShip(StarterShipId).DockedSystemId, $"{context}: did not dock at {yard.Name}");
            return true;
        }
    }
}
