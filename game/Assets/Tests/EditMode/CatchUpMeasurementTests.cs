using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using SpaceTrade.Sim.Routes;
using SpaceTrade.Sim.Saves;
using SpaceTrade.Sim.Timing;
using static SpaceTrade.Sim.Tests.SaveTestSupport;

namespace SpaceTrade.Sim.Tests
{
    /// <summary>
    /// Measures the catch-up the owner asked about: 30 real days away at 5x (3,600 game years) with several ships on routes,
    /// through the same load, catch-up, and save steps as opening a game. It reports the real seconds; there is no cap on
    /// offline time.
    /// </summary>
    public class CatchUpMeasurementTests
    {
        [TestCase(30, TestName = "ThirtyRealDaysAwayAtFiveTimes_WithShipsOnRoutes_Measured")]
        public void AwayAtFiveTimes(int realDays)
        {
            using (var folder = new TempSaveFolder())
            {
                var tuning = TestTuning.Load();
                var sim = NewGame.Create(1, tuning).Simulation;
                sim.SetCredits(100_000_000);
                RouteTestSupport.Ok(sim.Play());
                RouteTestSupport.Ok(sim.SetSpeed(ClockSpeed.X5));
                RouteTestSupport.Ok(sim.SetInSystemTransport(1, true));

                var offers = sim.Market.Offers.OrderBy(o => o.DistanceMilliLy).ToList();
                var used = new HashSet<(int, int)>();
                int routed = 0;
                foreach (var offer in offers)
                {
                    if (routed == 4) break;
                    if (!used.Add((System.Math.Min(offer.OriginId, offer.DestinationId), System.Math.Max(offer.OriginId, offer.DestinationId)))) continue;
                    var ship = sim.AddShip($"Hauler {routed + 1}", tuning.Start.StarterShipClass, tuning.Start.StarterDrive, offer.OriginId);
                    RouteTestSupport.Ok(sim.SetRoute(ship.Id, new List<RouteStop>
                    {
                        new RouteStop(offer.OriginId, new[] { offer.CommodityId }, carryPassengers: true),
                        new RouteStop(offer.DestinationId, carryPassengers: true),
                    }));
                    routed++;
                }
                Assert.AreEqual(4, routed);
                sim.Advance(3 * RouteTestSupport.Year);
                long credits = sim.Credits;
                Ok(folder.Store.Write("measure", sim, T0));

                long now = T0 + realDays * RealDay;
                var clock = Stopwatch.StartNew();
                var load = folder.Store.Load("measure", tuning, now);
                double loadSeconds = clock.Elapsed.TotalSeconds;
                Assert.IsTrue(load.Succeeded, string.Join(" ", load.Messages));

                clock.Restart();
                var summary = OfflineCatchUp.Run(load.Simulation, load.SavedUtcMs, now);
                double catchUpSeconds = clock.Elapsed.TotalSeconds;

                clock.Restart();
                Ok(folder.Store.Write("measure", load.Simulation, now));
                double saveSeconds = clock.Elapsed.TotalSeconds;

                var after = load.Simulation;
                long fileBytes = new System.IO.FileInfo(folder.File("measure.json")).Length;
                Assert.AreEqual(5L * realDays * RealDay, summary.GameTicksPassed);
                Assert.AreEqual(after.Credits - credits, summary.NetCreditChange);
                Assert.AreEqual(summary.NetCreditChange, summary.VoyagePayouts + summary.InSystemIncome - summary.SuppliesBought);
                Assert.Greater(summary.Deliveries.Count, 0);

                string report =
                    $"[{realDays}-day catch-up] {realDays} real days at 5x = {summary.GameTicksPassed / (double)GameTime.TicksPerYear:0.00} game years, " +
                    $"{after.Ships.Count} ships (4 on routes, 1 on in-system transport). " +
                    $"Load {loadSeconds:0.000} s, catch-up {catchUpSeconds:0.000} s, save {saveSeconds:0.000} s, total {loadSeconds + catchUpSeconds + saveSeconds:0.000} s. " +
                    $"Deliveries {summary.Deliveries.Count}, payouts {summary.VoyagePayouts:N0} cr, in-system {summary.InSystemIncome:N0} cr, " +
                    $"supplies {summary.SuppliesBought:N0} cr, net {summary.NetCreditChange:N0} cr. " +
                    $"Save file {fileBytes / 1024.0:0.0} KB, notifications kept {after.Notifications.Count}, receipts {after.Receipts.Count}.";
                UnityEngine.Debug.Log(report);
                TestContext.WriteLine(report);
            }
        }
    }
}
