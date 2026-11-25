using System;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.World;

namespace SpaceTrade.Sim.Tests
{
    internal static class MarketTestSupport
    {
        public static long Days(long days) => days * GameTime.TicksPerDay;

        /// <summary>A simulation at tick 0 with a freshly generated galaxy and market attached.</summary>
        public static Simulation NewSim(long seed, TuningData tuning)
        {
            var galaxy = GalaxyGenerator.Generate(seed, tuning);
            var sim = new Simulation();
            sim.AttachMarket(Market.Create(galaxy, tuning, 0));
            return sim;
        }

        /// <summary>The first seed from 1 upward whose fresh market satisfies <paramref name="ok"/>.</summary>
        public static Simulation FindSim(TuningData tuning, Func<Market, bool> ok, string what)
        {
            for (long seed = 1; seed <= 500; seed++)
            {
                var sim = NewSim(seed, tuning);
                if (ok(sim.Market)) return sim;
            }
            Assert.Fail("Test setup: no seed from 1 to 500 gives " + what);
            return null;
        }

        public static int[] SystemsWith(Market m, Func<string, bool> industryTest) =>
            m.Galaxy.Systems.Where(s => s.IndustryIds.Any(industryTest)).Select(s => s.Id).ToArray();
    }
}
