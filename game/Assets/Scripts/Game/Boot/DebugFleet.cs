#if DEBUG
using System.Collections.Generic;
using System.Linq;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Routes;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.Voyages;

namespace SpaceTrade.Game.Boot
{
    /// <summary>
    /// Editor and Development Builds only: puts ships in flight for checking the map, through the normal commands
    /// (BuyShip, BuyDrive, Dispatch, SetRoute, PauseRoute). No time passes and no rule is bypassed. Buying many ships needs
    /// money and yard stock, so <see cref="PrepareTuning"/> raises the starting credits and stock limits in an in-memory
    /// copy of the tuning only; tuning.json is never written.
    /// </summary>
    public static class DebugFleet
    {
        public const long StartingCredits = 1_000_000_000_000;
        public const int YardStock = 200;

        /// <summary>True when the fleet needs more ships than the starter.</summary>
        public static bool NeedsPurchases(int inFlight, int dockedAtYard, bool pausedRoute) => inFlight > 1 || dockedAtYard > 0 || pausedRoute;

        public static void PrepareTuning(TuningData t)
        {
            t.Start.StartingCredits = StartingCredits;
            t.Shipyards.ShipStockPerClassMin = YardStock;
            t.Shipyards.ShipStockPerClassMax = YardStock;
            t.Shipyards.DriveStockPerTierMin = YardStock;
            t.Shipyards.DriveStockPerTierMax = YardStock;
        }

        /// <summary>
        /// The starter ship flies its first opening job; then <paramref name="inFlight"/> - 1 bought ships (every class, every
        /// drive, from every yard, to as many destinations as possible) fly empty voyages. <paramref name="dockedAtYard"/>
        /// more stay docked at the first yard, and with <paramref name="pausedRoute"/> one more courier is put on a three-stop
        /// route that departs at once and is then paused. Returns any command that was refused.
        /// </summary>
        public static List<string> Launch(Simulation sim, int inFlight, int dockedAtYard, bool pausedRoute)
        {
            var problems = new List<string>();
            var tuning = sim.Market.Tuning;
            var galaxy = sim.Market.Galaxy;
            var starter = sim.Ships[0];
            if (inFlight >= 1)
            {
                var jobs = OpeningOffers.FindJobs(sim, starter.Id);
                var job = jobs.FirstOrDefault(j => !j.IsPassengerJob) ?? jobs.FirstOrDefault();
                if (job == null) problems.Add("The starter ship has no job to fly.");
                else Check(sim.Dispatch(job.Quote.Plan, true), "starter dispatch", problems);
            }

            var yards = sim.Market.Shipyards.Select(y => y.SystemId).ToList();
            var classes = tuning.Ships.Select(c => c.Id).ToList();
            var drives = tuning.Drives.Select(d => d.Id).ToList();
            for (int i = 0; i < inFlight - 1; i++)
            {
                int yard = yards[i % yards.Count];
                if (!Check(sim.BuyShip(yard, classes[i % classes.Count], out int id), "buy", problems)) continue;
                string drive = drives[i / classes.Count % drives.Count];
                if (sim.FindShip(id).DriveId != drive) Check(sim.BuyDrive(id, drive), "drive", problems);
                var targets = galaxy.Systems.Where(s => s.Id != yard).OrderBy(s => galaxy.DistanceMilliLy(yard, s.Id)).Select(s => s.Id).ToList();
                int first = i / yards.Count;
                bool sent = false;
                for (int k = 0; k < targets.Count && !sent; k++)
                    sent = sim.Dispatch(new VoyagePlan(id, targets[(first + k) % targets.Count]), true).Succeeded;
                if (!sent) problems.Add($"Ship {id} at system {yard} could not be sent anywhere.");
            }

            for (int j = 0; j < dockedAtYard; j++)
                Check(sim.BuyShip(yards[0], classes[j % classes.Count], out _), "buy docked", problems);

            if (pausedRoute)
            {
                int yard = yards[0];
                var near = galaxy.Systems.Where(s => s.Id != yard).OrderBy(s => galaxy.DistanceMilliLy(yard, s.Id)).Select(s => s.Id).ToList();
                var stops = new[] { RouteStop.Empty(yard), RouteStop.Empty(near[0]), RouteStop.Empty(near[1]) };
                if (Check(sim.BuyShip(yard, tuning.Start.StarterShipClass, out int id), "buy route ship", problems)
                    && Check(sim.SetRoute(id, stops), "set route", problems))
                    Check(sim.PauseRoute(id), "pause route", problems);
            }
            return problems;
        }

        private static bool Check(CommandResult result, string what, List<string> problems)
        {
            if (!result.Succeeded) problems.Add(what + ": " + result.Reason);
            return result.Succeeded;
        }
    }
}
#endif
