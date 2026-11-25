using System.Collections.Generic;
using System.Text;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Ships;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.Voyages;
using SpaceTrade.Sim.World;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>
    /// A hand-built map for checking quote figures by hand: Alpha (mining colony, sells ore) and
    /// Beta (refinery, buys ore) on a line, 10 million people each, with every random roll fixed
    /// at 100 percent. One courier with the starter drive docked at Alpha.
    /// </summary>
    internal static class VoyageTestSupport
    {
        public const int Alpha = 0;
        public const int Beta = 1;
        public const int CourierId = 1;

        /// <summary>The test fixture with every rate, fare, and provision price roll pinned to 100 percent.</summary>
        public static TuningData FlatTuning()
        {
            var t = TestTuning.Load();
            foreach (var c in t.Commodities) { c.RateRollMinPercent = 100; c.RateRollMaxPercent = 100; }
            t.Passengers.FareRollMinPercent = 100;
            t.Passengers.FareRollMaxPercent = 100;
            t.Provisions.PriceRollMinPercent = 100;
            t.Provisions.PriceRollMaxPercent = 100;
            return t;
        }

        public static Galaxy TwoSystems(long distanceMilliLy)
        {
            return new Galaxy(1, Alpha, new List<StarSystem>
            {
                new StarSystem(Alpha, "Alpha", 0, 0, 10, new[] { "mining_colony" }, false),
                new StarSystem(Beta, "Beta", distanceMilliLy, 0, 10, new[] { "refinery" }, false),
            });
        }

        public static Simulation NewSim(long credits, TuningData tuning = null, long distanceMilliLy = 4_500)
        {
            tuning = tuning ?? FlatTuning();
            var sim = new Simulation();
            sim.AttachMarket(Market.Create(TwoSystems(distanceMilliLy), tuning, 0));
            sim.SetCredits(credits);
            sim.AddShip("Test Courier", tuning.Start.StarterShipClass, tuning.Start.StarterDrive, Alpha);
            return sim;
        }

        public static Ship Courier(Simulation sim) => sim.FindShip(CourierId);

        public static VoyagePlan Plan(long oreTonnes, int passengers)
        {
            var freight = oreTonnes > 0 ? new[] { new FreightLine("ore", (int)oreTonnes) } : null;
            return new VoyagePlan(CourierId, Beta, freight, passengers);
        }

        /// <summary>Every stored value: clock, credits, ships, voyages, receipts, pending events, and market.</summary>
        public static string State(Simulation sim) => sim.DescribeState();

        /// <summary>The whole state as bytes, for "byte-identical" checks.</summary>
        public static byte[] StateBytes(Simulation sim) => Encoding.UTF8.GetBytes(sim.DescribeState());

        public const int Gamma = 2;

        /// <summary>
        /// Alpha (mining colony), Beta (refinery) 4.5 ly east, Gamma (agricultural system) 3 ly further
        /// east (7.5 ly from Alpha). Same courier and flat rolls as <see cref="NewSim"/>.
        /// </summary>
        public static Simulation NewThreeSystemSim(long credits, TuningData tuning = null)
        {
            tuning = tuning ?? FlatTuning();
            var galaxy = new Galaxy(1, Alpha, new List<StarSystem>
            {
                new StarSystem(Alpha, "Alpha", 0, 0, 10, new[] { "mining_colony" }, false),
                new StarSystem(Beta, "Beta", 4_500, 0, 10, new[] { "refinery" }, false),
                new StarSystem(Gamma, "Gamma", 7_500, 0, 10, new[] { "agricultural_system" }, false),
            });
            var sim = new Simulation();
            sim.AttachMarket(Market.Create(galaxy, tuning, 0));
            sim.SetCredits(credits);
            sim.AddShip("Test Courier", tuning.Start.StarterShipClass, tuning.Start.StarterDrive, Alpha);
            return sim;
        }

        /// <summary>Credits plus the cost of every supply aboard every ship.</summary>
        public static long CreditsPlusSuppliesAtCost(Simulation sim)
        {
            long total = sim.Credits;
            foreach (var s in sim.Ships) total += s.Fuel.CostCredits + s.Food.CostCredits + s.Water.CostCredits;
            return total;
        }

        public static string HoldProblem(VoyageQuote q) => FindProblem(q, "The hold holds");
        public static string TankProblem(VoyageQuote q) => FindProblem(q, "The fuel tank holds");

        private static string FindProblem(VoyageQuote q, string start)
        {
            foreach (var p in q.Problems) if (p.StartsWith(start)) return p;
            return null;
        }
    }
}
