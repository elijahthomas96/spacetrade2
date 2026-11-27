#if DEBUG
using System.Collections;
using System.Linq;
using SpaceTrade.Game.Hud;
using SpaceTrade.Game.Map;
using SpaceTrade.Sim.Timing;
using UnityEngine;

namespace SpaceTrade.Game.Boot
{
    /// <summary>
    /// Editor and Development Builds only: the frame-time scenario for the job lists (session 14), run inside the game so
    /// nothing outside it disturbs the frames measured. With the clock at 5x, the voyage planner for a docked ship is
    /// opened, sorted each way, filtered, a job chosen, and closed, one step a frame, over and over; then the system view
    /// of the busiest port and of the ship's own port is opened and sorted the same way. Only the three allowed speeds
    /// are used and no time is skipped.
    /// </summary>
    public static class PlannerFrameCheck
    {
        /// <summary>What the last run saw, for the report.</summary>
        public static string LastSummary { get; private set; } = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlaySession()
        {
            LastSummary = "";
            Opens = 0;
            PlannerJobs = 0;
            RowElements = 0;
        }

        public static IEnumerator Run(MapScreen map, RealTimeDriver driver, FrameTimeProbe probe, float secondsEach, string prefix)
        {
            var sim = map.Simulation;
            var docked = sim.Ships.FirstOrDefault(s => !s.IsInTransit);
            if (docked == null) { LastSummary = "No docked ship to plan for."; yield break; }
            int port = docked.DockedSystemId.Value;
            var market = sim.Market;
            var busiest = market.Galaxy.Systems.OrderByDescending(s => market.OffersFrom(s.Id).Count + market.PoolsFrom(s.Id).Count).First();
            map.SelectShip(docked.Id);
            yield return null;
            driver.SetSpeed(ClockSpeed.X5);
            driver.Play();
            yield return null;

            var planner = map.Planner;
            var sorts = new[] { JobSort.MostProfitable, JobSort.Quickest, JobSort.Nearest };
            int builds = planner.BoardBuilds;
            probe.Begin(prefix + " 5x: planner job list opened, sorted, filtered");
            float until = Time.realtimeSinceStartup + secondsEach;
            int step = 0, opens = 0, rowElements = 0;
            while (Time.realtimeSinceStartup < until)
            {
                switch (step % 8)
                {
                    case 0: if (map.OpenPlanner()) opens++; break;
                    case 1: planner.Jobs.SetSort(sorts[0]); break;
                    case 2: planner.Jobs.SetSort(sorts[1]); break;
                    case 3: planner.Jobs.SetSort(sorts[2]); rowElements = Mathf.Max(rowElements, planner.Jobs.RowElementCount); break;
                    case 4: planner.Jobs.SetHideUnflyable(true); break;
                    case 5: planner.Jobs.SetHideUnflyable(false); break;
                    case 6: if (planner.Jobs.Shown.Count > 0) planner.ChooseJob(planner.Jobs.Shown[step % planner.Jobs.Shown.Count]); break;
                    case 7: map.ClosePlanner(); break;
                }
                step++;
                yield return null;
            }
            probe.End();
            int plannerJobs = planner.Jobs.All.Count;
            builds = planner.BoardBuilds - builds;
            map.ClosePlanner();

            probe.Begin(prefix + " 5x: system view job list opened, sorted");
            until = Time.realtimeSinceStartup + secondsEach;
            step = 0;
            while (Time.realtimeSinceStartup < until)
            {
                switch (step % 4)
                {
                    case 0: map.Select(busiest.Id); break;
                    case 1: map.Hud.SystemJobs.SetSort(JobSort.HighestRate); break;
                    case 2: map.Select(port); break;
                    case 3: map.Hud.SystemJobs.SetSort(JobSort.Nearest); break;
                }
                step++;
                yield return null;
            }
            probe.End();
            driver.Pause();

            int systemJobs = market.OffersFrom(busiest.Id).Count + market.PoolsFrom(busiest.Id).Count;
            Opens = opens;
            PlannerJobs = plannerJobs;
            RowElements = rowElements;
            LastSummary = $"Planner: {plannerJobs} jobs at the ship's port ({market.Galaxy.Systems[port].Name}), opened {opens} times, job board read {builds} times, at most {rowElements} row elements. System view: {systemJobs} jobs at the busiest port ({busiest.Name}).";
        }

        public static int Opens { get; private set; }
        public static int PlannerJobs { get; private set; }
        public static int RowElements { get; private set; }
    }
}
#endif
