using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Routes;
using SpaceTrade.Sim.Ships;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Voyages;

namespace SpaceTrade.Sim
{
    /// <summary>Automated routes (spec 8): orders, departure checks, rechecks, and the whole-circuit estimate.</summary>
    public sealed partial class Simulation
    {
        private readonly List<Notification> _notifications = new List<Notification>();

        /// <summary>Every notification raised, oldest first.</summary>
        public IReadOnlyList<Notification> Notifications => _notifications;

        private string SystemName(int id) => Market.Galaxy.Systems[id].Name;

        /// <summary>
        /// Gives a ship route orders, replacing any it had (spec 8.2). Works for a docked ship or one in flight; the
        /// voyage in progress never changes. The ship starts at the first stop where it is docked or heading; if
        /// there is none it is flagged blocked once docked. A docked ship checks for departure at once. Refused,
        /// changing nothing, for invalid orders or a ship on in-system transport.
        /// </summary>
        public CommandResult SetRoute(int shipId, IEnumerable<RouteStop> stops)
        {
            if (Market == null) return CommandResult.Fail("There is no market yet.");
            var ship = FindShip(shipId);
            if (ship == null) return CommandResult.Fail($"There is no ship with id {shipId}.");
            var list = stops?.ToList();
            var problems = RouteLoader.Validate(Market, list);
            if (ship.IsOnInSystemTransport)
                problems.Insert(0, $"{ship.Name} is on in-system transport at {SystemName(ship.DockedSystemId.Value)}. Turn off in-system transport before giving it a route.");
            if (problems.Count > 0) return CommandResult.Fail(string.Join(" ", problems));

            int location = ship.IsInTransit ? ship.Voyage.DestinationId : ship.DockedSystemId.Value;
            int start = list.FindIndex(s => s.SystemId == location);
            RemovePendingRouteCheck(ship);
            ship.Route = new ShipRoute(list, start < 0 ? 0 : start);
            if (!ship.IsInTransit) RunDepartureCheck(ship.Id);
            return CommandResult.Ok();
        }

        /// <summary>
        /// Pauses a ship's route (spec 8.5): the orders and the ship's place are kept, no automatic departures are
        /// made, and any waiting or blocked status is cleared. A ship in flight finishes its voyage and stays docked.
        /// Pausing a paused route succeeds and changes nothing.
        /// </summary>
        public CommandResult PauseRoute(int shipId)
        {
            var ship = FindShip(shipId);
            if (ship == null) return CommandResult.Fail($"There is no ship with id {shipId}.");
            if (ship.Route == null) return CommandResult.Fail($"{ship.Name} has no route to pause.");
            if (ship.Route.IsPaused) return CommandResult.Ok();
            RemovePendingRouteCheck(ship);
            ship.Route.IsPaused = true;
            ship.Route.ClearStatus();
            return CommandResult.Ok();
        }

        /// <summary>
        /// Resumes a paused route, or retries a blocked or waiting ship at once with no change to its orders (spec 8.4,
        /// 8.5). A docked ship continues from its remembered stop if it is docked there, otherwise from the next stop
        /// after it whose system is where it is docked, and checks at once. A ship in flight continues when it docks.
        /// Refused, changing nothing, for a ship on in-system transport.
        /// </summary>
        public CommandResult ResumeRoute(int shipId)
        {
            var ship = FindShip(shipId);
            if (ship == null) return CommandResult.Fail($"There is no ship with id {shipId}.");
            var route = ship.Route;
            if (route == null) return CommandResult.Fail($"{ship.Name} has no route to resume.");
            if (ship.IsOnInSystemTransport)
                return CommandResult.Fail($"{ship.Name} is on in-system transport at {SystemName(ship.DockedSystemId.Value)}. Turn off in-system transport before resuming its route.");
            if (ship.IsInTransit)
            {
                route.IsPaused = false;
                return CommandResult.Ok();
            }
            int at = StopFor(route, ship.DockedSystemId.Value);
            if (at >= 0) route.CurrentStopIndex = at;
            route.IsPaused = false;
            RemovePendingRouteCheck(ship);
            RunDepartureCheck(ship.Id);
            return CommandResult.Ok();
        }

        /// <summary>
        /// Deletes a ship's route orders (spec 8.5). The ship becomes an ordinary manually flown ship; a ship in flight
        /// finishes its voyage and stays docked. Clearing a ship with no route succeeds and changes nothing.
        /// </summary>
        public CommandResult ClearRoute(int shipId)
        {
            var ship = FindShip(shipId);
            if (ship == null) return CommandResult.Fail($"There is no ship with id {shipId}.");
            if (ship.Route == null) return CommandResult.Ok();
            RemovePendingRouteCheck(ship);
            ship.Route = null;
            return CommandResult.Ok();
        }

        /// <summary>
        /// The stop matching where the ship is docked: its remembered stop if it is docked there, otherwise the next
        /// stop after it, going round the loop, at that system. -1 when the system is not on the route.
        /// </summary>
        private static int StopFor(ShipRoute route, int systemId)
        {
            int n = route.Stops.Count;
            for (int k = 0; k < n; k++)
            {
                int i = (route.CurrentStopIndex + k) % n;
                if (route.Stops[i].SystemId == systemId) return i;
            }
            return -1;
        }

        private void RemovePendingRouteCheck(Ship ship)
        {
            if (ship.Route == null) return;
            Events.RemoveWhere(e => e is RouteCheckEvent c && c.ShipId == ship.Id);
            ship.Route.PendingCheckTick = null;
        }

        private void ScheduleRouteCheck(Ship ship, long tick)
        {
            Schedule(tick, new RouteCheckEvent(ship.Id));
            ship.Route.PendingCheckTick = tick;
        }

        /// <summary>
        /// Asks for an automatic recheck now, but no sooner than recheckMinIntervalDays after the ship's last check:
        /// one that comes too soon is scheduled for the earliest allowed tick, so nothing is missed.
        /// </summary>
        private void RequestRouteRecheck(Ship ship)
        {
            var route = ship.Route;
            long at = CurrentTick;
            if (route.LastCheckTick.HasValue)
                at = Math.Max(at, checked(route.LastCheckTick.Value + (long)Market.Tuning.Automation.RecheckMinIntervalDays * GameTime.TicksPerDay));
            if (route.PendingCheckTick.HasValue && route.PendingCheckTick.Value <= at) return;
            RemovePendingRouteCheck(ship);
            ScheduleRouteCheck(ship, at);
        }

        private static bool IsDockedOnActiveRoute(Ship ship) => ship.Route != null && ship.Route.IsActive && !ship.IsInTransit;

        /// <summary>A market event ran at this system: recheck every waiting ship whose stop or next stop is here.</summary>
        internal void OnMarketEvent(int systemId)
        {
            foreach (var ship in _ships)
            {
                if (!IsDockedOnActiveRoute(ship) || ship.Route.Status != RouteStatus.Waiting) continue;
                if (ship.Route.CurrentStop.SystemId == systemId || ship.Route.NextStop.SystemId == systemId) RequestRouteRecheck(ship);
            }
        }

        /// <summary>Credits rose: recheck every ship blocked only because its top-up could not be paid.</summary>
        private void OnCreditsRose()
        {
            foreach (var ship in _ships)
                if (IsDockedOnActiveRoute(ship) && ship.Route.Status == RouteStatus.Blocked && ship.Route.BlockedForTopUp) RequestRouteRecheck(ship);
        }

        /// <summary>A route ship docked: its departure check runs in phase 4 of this tick.</summary>
        private void OnRouteShipDocked(Ship ship)
        {
            if (ship.Route == null || ship.Route.IsPaused) return;
            RemovePendingRouteCheck(ship);
            ScheduleRouteCheck(ship, CurrentTick);
        }

        /// <summary>The phase-4 event. Blocks other than for the top-up wait for the player.</summary>
        internal void RunScheduledRouteCheck(int shipId)
        {
            var ship = FindShip(shipId);
            if (ship?.Route == null) return;
            ship.Route.PendingCheckTick = null;
            if (ship.Route.Status == RouteStatus.Blocked && !ship.Route.BlockedForTopUp) return;
            RunDepartureCheck(shipId);
        }

        /// <summary>
        /// The departure check (spec 8.4): build the plan by the stop's loading rules and dispatch it through the
        /// same code as a manual voyage, or set the ship waiting or blocked. A check that does not depart changes
        /// nothing except the ship's status and, when it becomes blocked, its notification.
        /// </summary>
        internal void RunDepartureCheck(int shipId)
        {
            var ship = FindShip(shipId);
            var route = ship?.Route;
            if (route == null || route.IsPaused || ship.IsInTransit) return;

            int at = ship.DockedSystemId.Value;
            int stopIndex = StopFor(route, at);
            if (stopIndex < 0)
            {
                Block(ship, $"{ship.Name} is docked at {SystemName(at)}, which is not on its route. Change its orders, or pause the route and fly {ship.Name} to one of its stops.", false);
                return;
            }
            route.CurrentStopIndex = stopIndex;

            var t = Market.Tuning;
            var shipClass = t.FindShip(ship.ClassId);
            var drive = t.FindDrive(ship.DriveId);
            if (shipClass == null || drive == null)
            {
                Block(ship, $"{ship.Name}'s class \"{ship.ClassId}\" or drive \"{ship.DriveId}\" is not in the tuning file.", false);
                return;
            }

            var stop = route.CurrentStop;
            int destination = route.NextStop.SystemId;
            var load = RouteLoader.Load(Market, ship.Name, shipClass, drive, stop, destination, ship.Food.Kg, ship.Water.Kg);
            if (load.CannotFlyReason != null) { Block(ship, load.CannotFlyReason, false); return; }
            if (load.NeverReason != null) { Block(ship, load.NeverReason, false); return; }
            if (!load.MeetsMinimums) { Wait(ship, RouteLoader.WaitingText(t, stop, load.UnmetFreightTonnes, load.UnmetPassengers)); return; }

            var plan = new VoyagePlan(ship.Id, destination, load.Freight, load.Passengers);
            var quote = Quote(plan);
            if (!stop.AutoProvision && quote.Supplies.Any(s => s.ShortfallKg > 0)) { Block(ship, ShortageReason(ship, quote), false); return; }
            if (!quote.IsFeasible)
            {
                bool topUpOnly = !quote.CanAffordTopUp && quote.Problems.Count == 1;
                Block(ship, string.Join(" ", quote.Problems), topUpOnly);
                return;
            }
            var result = DispatchCore(plan, stop.AutoProvision);
            if (!result.Succeeded) { Block(ship, result.Reason, false); return; }

            route.CurrentStopIndex = route.NextStopIndex;
            route.ClearStatus();
        }

        private void Wait(Ship ship, string text)
        {
            var route = ship.Route;
            route.Status = RouteStatus.Waiting;
            route.StatusText = text;
            route.BlockedForTopUp = false;
            route.LastCheckTick = CurrentTick;
        }

        /// <summary>Flags the ship blocked. A notification is raised only when it becomes blocked, not on each failed retry.</summary>
        private void Block(Ship ship, string reason, bool forTopUp)
        {
            var route = ship.Route;
            bool wasBlocked = route.Status == RouteStatus.Blocked;
            route.Status = RouteStatus.Blocked;
            route.StatusText = reason;
            route.BlockedForTopUp = forTopUp;
            route.LastCheckTick = CurrentTick;
            if (!wasBlocked)
                _notifications.Add(new Notification(CurrentTick, NotificationKind.RouteBlocked, ship.Id, $"{ship.Name} is blocked at {SystemName(ship.DockedSystemId.Value)}. {reason}"));
        }

        /// <summary>
        /// The whole-circuit estimate for these orders flown by this ship's class and drive (spec 8.6). Works for any
        /// ship, from anywhere. Changes nothing.
        /// </summary>
        public CircuitEstimate EstimateCircuit(int shipId, IEnumerable<RouteStop> stops)
        {
            var e = new CircuitEstimate(shipId);
            if (Market == null) { e.AddProblem("There is no market yet."); return e; }
            var ship = FindShip(shipId);
            if (ship == null) { e.AddProblem($"There is no ship with id {shipId}."); return e; }
            var t = Market.Tuning;
            var shipClass = t.FindShip(ship.ClassId);
            var drive = t.FindDrive(ship.DriveId);
            if (shipClass == null || drive == null) { e.AddProblem($"{ship.Name}'s class \"{ship.ClassId}\" or drive \"{ship.DriveId}\" is not in the tuning file."); return e; }
            var list = stops?.ToList();
            e.AddProblems(RouteLoader.Validate(Market, list));
            if (!e.IsValid) return e;

            long totalTicks = 0, revenue = 0, cost = 0;
            bool allFly = true;
            for (int i = 0; i < list.Count; i++)
            {
                var stop = list[i];
                int to = list[(i + 1) % list.Count].SystemId;
                long distance = Market.Galaxy.DistanceMilliLy(stop.SystemId, to);
                string label = $"Stop {i + 1} ({SystemName(stop.SystemId)})";
                var load = RouteLoader.Load(Market, ship.Name, shipClass, drive, stop, to, 0, 0);
                bool canFly = load.CannotFlyReason == null;
                if (!canFly) { allFly = false; e.AddNote($"{label}: {load.CannotFlyReason}"); }
                else if (load.NeverReason != null) e.AddNote($"{label}: {load.NeverReason}");
                else if (!load.MeetsMinimums)
                    e.AddNote($"{label}: today's stock gives {load.FreightTonnes} t of freight and {load.Passengers} passengers, below its minimum of {stop.MinFreightTonnes} t and {stop.MinPassengers} passengers; the ship would wait there ({RouteLoader.WaitingText(t, stop, load.UnmetFreightTonnes, load.UnmetPassengers)}). Waiting time is not counted.");

                var freight = new List<CircuitFreight>();
                long legRevenue = 0;
                foreach (var line in load.Freight)
                {
                    var f = new CircuitFreight(line.CommodityId, line.Tonnes, Market.RatePerTonne(Market.FindOffer(stop.SystemId, line.CommodityId, to)));
                    freight.Add(f);
                    legRevenue = checked(legRevenue + f.Revenue);
                }
                legRevenue = checked(legRevenue + load.Passengers * Market.FarePerPassenger(Market.FindPool(stop.SystemId, to)));

                var needs = ProvisioningMath.Compute(t, shipClass, drive, distance, load.FreightTonnes, load.Passengers, 0, 0);
                var prices = Market.ProvisionPricesAt(stop.SystemId);
                long legCost = checked(ProvisioningMath.PurchaseCost(needs.FuelRequiredKg, prices.FuelPerTonne)
                    + ProvisioningMath.PurchaseCost(needs.FoodRequiredKg, prices.FoodPerTonne)
                    + ProvisioningMath.PurchaseCost(needs.WaterRequiredKg, prices.WaterPerTonne));

                e.AddLeg(new CircuitLeg(stop.SystemId, to, distance, needs.TransitTicks, stop.EmptyLeg, canFly, load.Passengers, freight, load.FreightTonnes,
                    needs.FuelRequiredKg, needs.FoodRequiredKg, needs.WaterRequiredKg, legRevenue, legCost));
                totalTicks = checked(totalTicks + needs.TransitTicks);
                revenue = checked(revenue + legRevenue);
                cost = checked(cost + legCost);
            }
            e.AllLegsCanBeFlown = allFly;
            e.TotalTransitTicks = totalTicks;
            e.EstimatedRevenue = revenue;
            e.EstimatedSupplyCost = cost;
            e.EstimatedProfitPerGameYear = IntMath.DivideRounded((BigInteger)(revenue - cost) * GameTime.TicksPerYear, totalTicks);
            return e;
        }
    }
}
