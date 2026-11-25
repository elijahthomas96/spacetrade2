using System.Collections.Generic;
using System.Text;

namespace SpaceTrade.Sim.Routes
{
    public enum RouteStatus
    {
        /// <summary>In flight on the route, or nothing to report.</summary>
        Running = 0,
        Waiting = 1,
        Blocked = 2,
    }

    /// <summary>
    /// A ship's route orders and where it stands on them (spec 8): the stops, whether the route is paused,
    /// the stop the ship is at or heading to, and its waiting or blocked status.
    /// </summary>
    public sealed class ShipRoute
    {
        private readonly List<RouteStop> _stops;

        public IReadOnlyList<RouteStop> Stops => _stops;
        public bool IsPaused { get; internal set; }
        public bool IsActive => !IsPaused;

        /// <summary>The stop the ship is docked at or heading to.</summary>
        public int CurrentStopIndex { get; internal set; }

        public RouteStatus Status { get; internal set; }

        /// <summary>The waiting text or the blocked reason; null while running.</summary>
        public string StatusText { get; internal set; }

        /// <summary>True when the ship is blocked only because the top-up cannot be paid; it is rechecked when credits rise (spec 8.4).</summary>
        public bool BlockedForTopUp { get; internal set; }

        /// <summary>The tick of the last departure check that did not depart; null once the ship leaves. Limits automatic rechecks.</summary>
        public long? LastCheckTick { get; internal set; }

        /// <summary>The tick of this ship's pending departure check event, or null.</summary>
        public long? PendingCheckTick { get; internal set; }

        internal ShipRoute(IEnumerable<RouteStop> stops, int currentStopIndex)
        {
            _stops = new List<RouteStop>(stops);
            CurrentStopIndex = currentStopIndex;
        }

        public RouteStop CurrentStop => _stops[CurrentStopIndex];
        public int NextStopIndex => (CurrentStopIndex + 1) % _stops.Count;
        public RouteStop NextStop => _stops[NextStopIndex];

        internal void ClearStatus()
        {
            Status = RouteStatus.Running;
            StatusText = null;
            BlockedForTopUp = false;
            LastCheckTick = null;
        }

        /// <summary>The orders: one line per stop, in order.</summary>
        public string DescribeOrders(int shipId)
        {
            var sb = new StringBuilder();
            sb.Append("route ship=").Append(shipId).Append(" paused=").Append(IsPaused).Append(" stop=").Append(CurrentStopIndex);
            for (int i = 0; i < _stops.Count; i++) sb.Append("\nroutestop ").Append(shipId).Append(' ').Append(i).Append(' ').Append(_stops[i].DescribeState());
            return sb.ToString();
        }

        /// <summary>The waiting or blocked status on one line, kept apart from the orders so status changes can be told apart in state comparisons.</summary>
        public string DescribeStatus(int shipId) =>
            $"routestatus ship={shipId} status={Status} topUp={BlockedForTopUp} lastCheck={(LastCheckTick.HasValue ? LastCheckTick.Value.ToString() : "-")}" +
            $" pending={(PendingCheckTick.HasValue ? PendingCheckTick.Value.ToString() : "-")} text={StatusText ?? "-"}";
    }
}
