using System.Collections.Generic;
using System.Linq;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.World;
using SpaceTrade.Sim.Yards;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>
    /// A hand-built map for shipyard tests: Alpha (mining colony, no yard) at 0, Beta (refinery and yard)
    /// 4.5 ly east, Gamma (agricultural system and yard) 3 ly further east. Every yard price roll is
    /// 100 percent and every stock is at its maximum limit and full, unless a test changes them.
    /// </summary>
    internal static class YardTestSupport
    {
        public const int Alpha = 0;
        public const int Beta = 1;
        public const int Gamma = 2;
        public const int CourierId = 1;

        public const string Courier = "courier";
        public const string Bulk = "bulk_freighter";
        public const string Liner = "passenger_liner";
        public const string Slow = "alcubierre_1_50";
        public const string Mid = "alcubierre_2_25";
        public const string Fast = "alcubierre_3_75";

        public static TuningData YardTuning()
        {
            var t = VoyageTestSupport.FlatTuning();
            t.Shipyards.PriceRollMinPercent = 100;
            t.Shipyards.PriceRollMaxPercent = 100;
            return t;
        }

        public static Galaxy ThreeSystems(bool yards = true, long seed = 7)
        {
            return new Galaxy(seed, Alpha, new List<StarSystem>
            {
                new StarSystem(Alpha, "Alpha", 0, 0, 10, new[] { "mining_colony" }, false),
                new StarSystem(Beta, "Beta", 4_500, 0, 10, new[] { "refinery", "shipyard_works" }, yards),
                new StarSystem(Gamma, "Gamma", 7_500, 0, 10, new[] { "agricultural_system", "shipyard_works" }, yards),
            });
        }

        /// <summary>One courier, "Test Courier" (id 1), with the 1.5c drive and empty supplies, docked at <paramref name="dockedAt"/>.</summary>
        public static Simulation NewSim(long credits, int dockedAt = Alpha, TuningData tuning = null, long seed = 7)
        {
            tuning = tuning ?? YardTuning();
            var sim = new Simulation();
            sim.AttachMarket(Market.Create(ThreeSystems(true, seed), tuning, 0));
            foreach (var yard in sim.Market.Shipyards)
            foreach (var s in yard.Ships.Concat(yard.Drives))
            {
                s.LimitRollPercent = 100;
                s.Count = sim.Market.StockLimit(s);
            }
            sim.SetCredits(credits);
            sim.AddShip("Test Courier", Courier, Slow, dockedAt);
            return sim;
        }

        public static YardStock Stock(Simulation sim, int yardSystemId, YardItemKind kind, string itemId) =>
            sim.Market.FindShipyard(yardSystemId).Find(kind, itemId);

        public static YardStock ShipStock(Simulation sim, int yard, string classId) => Stock(sim, yard, YardItemKind.Ship, classId);
        public static YardStock DriveStock(Simulation sim, int yard, string driveId) => Stock(sim, yard, YardItemKind.Drive, driveId);

        public static byte[] Bytes(Simulation sim) => VoyageTestSupport.StateBytes(sim);

        /// <summary>Every stock at every yard: 0 to its limit, unless it was above a lowered limit.</summary>
        public static IEnumerable<YardStock> AllStock(Simulation sim) => sim.Market.Shipyards.SelectMany(y => y.Ships.Concat(y.Drives));

        /// <summary>The state-dump lines only in <paramref name="before"/> and only in <paramref name="after"/>, counting repeats.</summary>
        public static (List<string> Removed, List<string> Added) Diff(string before, string after)
        {
            var counts = new Dictionary<string, int>();
            foreach (var l in before.Split('\n')) counts[l] = (counts.TryGetValue(l, out int c) ? c : 0) + 1;
            var added = new List<string>();
            foreach (var l in after.Split('\n'))
            {
                if (counts.TryGetValue(l, out int c) && c > 0) counts[l] = c - 1;
                else added.Add(l);
            }
            var removed = new List<string>();
            foreach (var kv in counts) for (int i = 0; i < kv.Value; i++) removed.Add(kv.Key);
            return (removed, added);
        }
    }
}
