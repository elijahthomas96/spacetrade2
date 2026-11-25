using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Routes;
using SpaceTrade.Sim.Ships;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.World;
using static SpaceTrade.Sim.Tests.VoyageTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>Maps, orders, and state helpers for the route tests.</summary>
    internal static class RouteTestSupport
    {
        public const long Day = GameTime.TicksPerDay;
        public const long Year = GameTime.TicksPerYear;

        /// <summary>One courier leg of 4.5 ly at 1.5c: 3 game years.</summary>
        public const long Leg45 = 3 * Year;

        /// <summary>
        /// Alpha (mining colony), Beta (refinery), and Gamma (agricultural system) on a triangle with 3 ly sides,
        /// 10 million people each, every roll at 100 percent unless <paramref name="tuning"/> says otherwise. A closed
        /// freight circuit: ore Alpha to Beta, chemicals Beta to Gamma, food Gamma to Alpha. One courier at Alpha.
        /// </summary>
        public static Simulation Triangle(long credits, TuningData tuning = null)
        {
            tuning = tuning ?? FlatTuning();
            var galaxy = new Galaxy(1, Alpha, new List<StarSystem>
            {
                new StarSystem(Alpha, "Alpha", 0, 0, 10, new[] { "mining_colony" }, false),
                new StarSystem(Beta, "Beta", 3_000, 0, 10, new[] { "refinery" }, false),
                new StarSystem(Gamma, "Gamma", 1_500, 2_598, 10, new[] { "agricultural_system" }, false),
            });
            var sim = new Simulation();
            sim.AttachMarket(Market.Create(galaxy, tuning, 0));
            sim.SetCredits(credits);
            sim.AddShip("Test Courier", tuning.Start.StarterShipClass, tuning.Start.StarterDrive, Alpha);
            return sim;
        }

        public const int Rho = 0;
        public const int Phi = 1;

        /// <summary>Rho (refinery: metals and chemicals) and Phi (factory, buys both) 3 ly apart, flat rolls, a courier at Rho.</summary>
        public static Simulation RefineryToFactory(long credits)
        {
            var tuning = FlatTuning();
            var galaxy = new Galaxy(1, Rho, new List<StarSystem>
            {
                new StarSystem(Rho, "Rho", 0, 0, 10, new[] { "refinery" }, false),
                new StarSystem(Phi, "Phi", 3_000, 0, 10, new[] { "factory" }, false),
            });
            var sim = new Simulation();
            sim.AttachMarket(Market.Create(galaxy, tuning, 0));
            sim.SetCredits(credits);
            sim.AddShip("Test Courier", tuning.Start.StarterShipClass, tuning.Start.StarterDrive, Rho);
            return sim;
        }

        /// <summary>The three-stop circuit on <see cref="Triangle"/>, every stop carrying passengers and one commodity, with a 10 t freight minimum.</summary>
        public static List<RouteStop> Circuit() => new List<RouteStop>
        {
            new RouteStop(Alpha, new[] { "ore" }, carryPassengers: true, minFreightTonnes: 10),
            new RouteStop(Beta, new[] { "chemicals" }, carryPassengers: true, minFreightTonnes: 10),
            new RouteStop(Gamma, new[] { "food" }, carryPassengers: true, minFreightTonnes: 10),
        };

        /// <summary>Alpha loads ore for Beta (optional minimum); Beta goes back empty.</summary>
        public static List<RouteStop> OreShuttle(int minFreight = 0, bool passengers = false, int minPassengers = 0, bool autoProvision = true) => new List<RouteStop>
        {
            new RouteStop(Alpha, new[] { "ore" }, carryPassengers: passengers, autoProvision: autoProvision, minFreightTonnes: minFreight, minPassengers: minPassengers),
            RouteStop.Empty(Beta),
        };

        public static void SetSupply(Simulation sim, int system, string commodity, int tonnes) => sim.Market.FindSupply(system, commodity).Tonnes = tonnes;
        public static void SetDemand(Simulation sim, int system, string commodity, int tonnes) => sim.Market.FindDemand(system, commodity).Tonnes = tonnes;
        public static void SetWaiting(Simulation sim, int origin, int destination, int passengers) => sim.Market.FindPool(origin, destination).Waiting = passengers;

        public static void Ok(CommandResult r) => Assert.IsTrue(r.Succeeded, r.Reason);

        public static ShipRoute Route(Simulation sim, int shipId = CourierId) => sim.FindShip(shipId).Route;

        public static long Tonnes(Ship ship, string commodity) => ship.Voyage.Freight.Where(f => f.CommodityId == commodity).Sum(f => f.Tonnes);

        public static int BlockedNotifications(Simulation sim, int shipId = CourierId) =>
            sim.Notifications.Count(n => n.Kind == NotificationKind.RouteBlocked && n.ShipId == shipId);

        public static string[] Lines(Simulation sim) => sim.DescribeState().Split('\n');

        /// <summary>Lines in <paramref name="after"/> that are not in <paramref name="before"/>, and the other way round.</summary>
        public static (List<string> added, List<string> removed) Diff(string[] before, string[] after)
        {
            var added = after.Where(l => !before.Contains(l)).ToList();
            var removed = before.Where(l => !after.Contains(l)).ToList();
            return (added, removed);
        }

        /// <summary>Every credit spent buying supplies so far.</summary>
        public static long SuppliesBought(Simulation sim) => sim.SuppliesBoughtTotal;

        public static long CostAboard(Simulation sim) => sim.Ships.Sum(s => s.Fuel.CostCredits + s.Food.CostCredits + s.Water.CostCredits);
    }
}
