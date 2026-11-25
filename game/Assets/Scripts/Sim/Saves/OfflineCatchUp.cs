using System.Collections.Generic;
using System.Linq;
using SpaceTrade.Sim.Routes;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Voyages;

namespace SpaceTrade.Sim.Saves
{
    /// <summary>A ship the player should look at after time away: blocked on its route, or idle with no orders.</summary>
    public sealed class ShipAttention
    {
        public int ShipId { get; }
        public string ShipName { get; }
        public string Reason { get; }

        internal ShipAttention(int shipId, string shipName, string reason)
        {
            ShipId = shipId;
            ShipName = shipName;
            Reason = reason;
        }
    }

    /// <summary>
    /// What happened while the player was away (spec 11.3). Voyage payouts + in-system income - supplies bought always
    /// equals <see cref="NetCreditChange"/>, the change in credits.
    /// </summary>
    public sealed class ReturnSummary
    {
        public long FromUtcMs { get; }
        public long ToUtcMs { get; }

        /// <summary>Real time away in milliseconds; zero when the clock moved backward.</summary>
        public long RealMillisecondsAway { get; }
        public bool ClockWentBackward { get; }
        public bool WasPaused { get; }
        public long FromTick { get; }
        public long ToTick { get; }
        public long GameTicksPassed => ToTick - FromTick;

        /// <summary>Every delivery made while away, in arrival order.</summary>
        public IReadOnlyList<VoyageReceipt> Deliveries { get; }
        public long VoyagePayouts { get; }
        public long InSystemIncome { get; }
        public long SuppliesBought { get; }
        public long NetCreditChange { get; }
        public IReadOnlyList<ShipAttention> ShipsNeedingAttention { get; }

        /// <summary>True when the time away is long enough for the summary panel (<see cref="OfflineCatchUp.SummaryMinRealMs"/> or more).</summary>
        public bool ShouldShow => RealMillisecondsAway >= OfflineCatchUp.SummaryMinRealMs;

        internal ReturnSummary(long fromUtcMs, long toUtcMs, long realMsAway, bool backward, bool paused, long fromTick, long toTick,
            IReadOnlyList<VoyageReceipt> deliveries, long payouts, long inSystemIncome, long suppliesBought, long netCreditChange, IReadOnlyList<ShipAttention> attention)
        {
            FromUtcMs = fromUtcMs;
            ToUtcMs = toUtcMs;
            RealMillisecondsAway = realMsAway;
            ClockWentBackward = backward;
            WasPaused = paused;
            FromTick = fromTick;
            ToTick = toTick;
            Deliveries = deliveries;
            VoyagePayouts = payouts;
            InSystemIncome = inSystemIncome;
            SuppliesBought = suppliesBought;
            NetCreditChange = netCreditChange;
            ShipsNeedingAttention = attention;
        }
    }

    /// <summary>
    /// The one offline catch-up rule (spec 11.3), used when a closed game is opened and when the game wakes from sleep
    /// (spec 3.2). Game time passed = real milliseconds since <c>fromUtcMs</c> x the selected speed (1 tick is 1 real
    /// millisecond at 1x), run through the normal <see cref="Simulation.Advance"/>. A paused game advances nothing. If the
    /// clock moved backward, nothing advances.
    /// </summary>
    public static class OfflineCatchUp
    {
        /// <summary>A gap of this many real milliseconds or more between two frames counts as waking from sleep (30 seconds).</summary>
        public const long WakeGapRealMs = 30_000;

        /// <summary>Time away shorter than this many real milliseconds skips the summary panel (5 minutes).</summary>
        public const long SummaryMinRealMs = 300_000;

        public static ReturnSummary Run(Simulation sim, long fromUtcMs, long nowUtcMs)
        {
            bool backward = nowUtcMs < fromUtcMs;
            long away = backward ? 0 : nowUtcMs - fromUtcMs;
            bool paused = sim.Clock.IsPaused;
            long ticks = paused ? 0 : checked(away * ClockSpeeds.Multiplier(sim.Clock.Speed));

            long fromTick = sim.CurrentTick;
            int receiptsBefore = sim.Receipts.Count;
            long creditsBefore = sim.Credits, incomeBefore = sim.InSystemIncomeTotal, boughtBefore = sim.SuppliesBoughtTotal;
            if (ticks > 0) sim.Advance(checked(fromTick + ticks));

            var deliveries = sim.Receipts.Skip(receiptsBefore).ToList();
            long payouts = 0;
            foreach (var r in deliveries) payouts = checked(payouts + r.GrossRevenue);
            return new ReturnSummary(fromUtcMs, nowUtcMs, away, backward, paused, fromTick, sim.CurrentTick, deliveries, payouts,
                sim.InSystemIncomeTotal - incomeBefore, sim.SuppliesBoughtTotal - boughtBefore, sim.Credits - creditsBefore, ShipsNeedingAttention(sim));
        }

        /// <summary>Ships blocked on their route (with the reason), and docked ships with no active route and no in-system transport.</summary>
        public static IReadOnlyList<ShipAttention> ShipsNeedingAttention(Simulation sim)
        {
            var list = new List<ShipAttention>();
            foreach (var ship in sim.Ships)
            {
                if (ship.IsInTransit || ship.IsOnInSystemTransport) continue;
                string at = sim.Market.Galaxy.Systems[ship.DockedSystemId.Value].Name;
                if (ship.Route != null && ship.Route.IsActive)
                {
                    if (ship.Route.Status == RouteStatus.Blocked) list.Add(new ShipAttention(ship.Id, ship.Name, ship.Route.StatusText));
                }
                else if (ship.Route != null)
                    list.Add(new ShipAttention(ship.Id, ship.Name, $"{ship.Name} is docked at {at} and its route is paused."));
                else
                    list.Add(new ShipAttention(ship.Id, ship.Name, $"{ship.Name} is docked at {at} with no orders."));
            }
            return list;
        }
    }
}
