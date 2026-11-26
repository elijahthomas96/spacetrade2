using System;
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
    /// equals <see cref="NetCreditChange"/>, the change in credits. The figures come from running totals, so they cover
    /// every delivery while away, even when more were made than receipts are kept.
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

        /// <summary>Totals of every delivery made while away: count, tonnes, passengers, revenue, and supplies used.</summary>
        public DeliveryTotals Delivered { get; }

        /// <summary>How many deliveries were made while away.</summary>
        public long DeliveryCount => Delivered.Deliveries;

        /// <summary>The deliveries made while away whose receipts are still kept in full (the newest ones), in arrival order.</summary>
        public IReadOnlyList<VoyageReceipt> Deliveries { get; }

        /// <summary>Deliveries made while away that are counted in the totals but not listed in <see cref="Deliveries"/>.</summary>
        public long DeliveriesNotListed => DeliveryCount - Deliveries.Count;

        public long VoyagePayouts => Delivered.GrossRevenue;
        public long InSystemIncome { get; }
        public long SuppliesBought { get; }
        public long NetCreditChange { get; }
        public IReadOnlyList<ShipAttention> ShipsNeedingAttention { get; }

        /// <summary>True when the time away is long enough for the summary panel (<see cref="OfflineCatchUp.SummaryMinRealMs"/> or more).</summary>
        public bool ShouldShow => RealMillisecondsAway >= OfflineCatchUp.SummaryMinRealMs;

        internal ReturnSummary(long fromUtcMs, long toUtcMs, long realMsAway, bool backward, bool paused, long fromTick, long toTick,
            DeliveryTotals delivered, IReadOnlyList<VoyageReceipt> deliveries, long inSystemIncome, long suppliesBought, long netCreditChange, IReadOnlyList<ShipAttention> attention)
        {
            FromUtcMs = fromUtcMs;
            ToUtcMs = toUtcMs;
            RealMillisecondsAway = realMsAway;
            ClockWentBackward = backward;
            WasPaused = paused;
            FromTick = fromTick;
            ToTick = toTick;
            Delivered = delivered;
            Deliveries = deliveries;
            InSystemIncome = inSystemIncome;
            SuppliesBought = suppliesBought;
            NetCreditChange = netCreditChange;
            ShipsNeedingAttention = attention;
        }
    }

    /// <summary>
    /// One offline catch-up in progress (spec 11.3). It moves the game toward its target in slices, each through the normal
    /// <see cref="Simulation.Advance"/>, so the screen can draw between slices and show <see cref="ProgressPerMille"/>.
    /// However the time is sliced, the game ends in exactly the same state as one step to the target.
    /// </summary>
    public sealed class CatchUpRun
    {
        /// <summary>The game time one slice covers: 10 game days.</summary>
        public const long SliceTicks = 10 * GameTime.TicksPerDay;

        private readonly Simulation _sim;
        private readonly DeliveryTotals _deliveredBefore;
        private readonly long _creditsBefore, _incomeBefore, _boughtBefore;

        public long FromUtcMs { get; }

        /// <summary>The real time the catch-up runs to. <see cref="ExtendTo"/> moves it later.</summary>
        public long ToUtcMs { get; private set; }

        public bool ClockWentBackward { get; }
        public bool WasPaused { get; }
        public long FromTick { get; }

        /// <summary>The game tick the catch-up ends at: real milliseconds from <see cref="FromUtcMs"/> to <see cref="ToUtcMs"/> x the speed.</summary>
        public long TargetTick { get; private set; }

        public long TicksDone => _sim.CurrentTick - FromTick;
        public long TicksTotal => TargetTick - FromTick;
        public bool IsDone => _sim.CurrentTick >= TargetTick;

        /// <summary>How far the catch-up has got, in thousandths (0 to 1,000). It reaches 1,000 only when it is done.</summary>
        public int ProgressPerMille => TicksTotal == 0 ? 1000 : (int)(TicksDone * 1000 / TicksTotal);

        internal CatchUpRun(Simulation sim, long fromUtcMs, long nowUtcMs)
        {
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            FromUtcMs = fromUtcMs;
            ToUtcMs = nowUtcMs;
            ClockWentBackward = nowUtcMs < fromUtcMs;
            WasPaused = sim.Clock.IsPaused;
            FromTick = sim.CurrentTick;
            TargetTick = TickFor(nowUtcMs);
            _deliveredBefore = sim.DeliveryTotals.Copy();
            _creditsBefore = sim.Credits;
            _incomeBefore = sim.InSystemIncomeTotal;
            _boughtBefore = sim.SuppliesBoughtTotal;
        }

        private long TickFor(long utcMs)
        {
            if (ClockWentBackward || WasPaused || utcMs <= FromUtcMs) return FromTick;
            return checked(FromTick + (utcMs - FromUtcMs) * ClockSpeeds.Multiplier(_sim.Clock.Speed));
        }

        /// <summary>
        /// Counts real time up to <paramref name="nowUtcMs"/> too, so time spent catching up is not lost. Never moves the
        /// target earlier. Does nothing after the clock was found to have gone backward.
        /// </summary>
        public void ExtendTo(long nowUtcMs)
        {
            if (ClockWentBackward || nowUtcMs <= ToUtcMs) return;
            ToUtcMs = nowUtcMs;
            TargetTick = TickFor(nowUtcMs);
        }

        /// <summary>Advances at most <paramref name="maxTicks"/> toward the target, through the normal Advance.</summary>
        public void Step(long maxTicks)
        {
            if (maxTicks < 1) throw new ArgumentOutOfRangeException(nameof(maxTicks), maxTicks, "A slice must be at least 1 tick.");
            if (IsDone) return;
            _sim.Advance(_sim.CurrentTick + Math.Min(maxTicks, TargetTick - _sim.CurrentTick));
        }

        /// <summary>
        /// Runs slices of <see cref="SliceTicks"/> until it is done or <paramref name="budgetMicroseconds"/> of real time on
        /// <paramref name="clock"/> have passed. Always runs at least one slice.
        /// </summary>
        public void StepFor(IRealTimeSource clock, long budgetMicroseconds)
        {
            if (clock == null) throw new ArgumentNullException(nameof(clock));
            long start = clock.NowMicroseconds;
            do Step(SliceTicks);
            while (!IsDone && clock.NowMicroseconds - start < budgetMicroseconds);
        }

        /// <summary>Runs whatever is left in one step.</summary>
        public void RunToEnd()
        {
            if (!IsDone) Step(TargetTick - _sim.CurrentTick);
        }

        /// <summary>The return summary. Only once the catch-up is done.</summary>
        public ReturnSummary Finish()
        {
            if (!IsDone) throw new InvalidOperationException($"The catch-up is not done: tick {_sim.CurrentTick} of {TargetTick}.");
            var delivered = _sim.DeliveryTotals.Since(_deliveredBefore);
            int listed = (int)Math.Min(delivered.Deliveries, _sim.Receipts.Count);
            var deliveries = _sim.Receipts.Skip(_sim.Receipts.Count - listed).ToList();
            long away = ClockWentBackward ? 0 : ToUtcMs - FromUtcMs;
            return new ReturnSummary(FromUtcMs, ToUtcMs, away, ClockWentBackward, WasPaused, FromTick, _sim.CurrentTick, delivered, deliveries,
                _sim.InSystemIncomeTotal - _incomeBefore, _sim.SuppliesBoughtTotal - _boughtBefore, _sim.Credits - _creditsBefore, OfflineCatchUp.ShipsNeedingAttention(_sim));
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

        /// <summary>Starts a catch-up from <paramref name="fromUtcMs"/> to <paramref name="nowUtcMs"/> without advancing anything yet.</summary>
        public static CatchUpRun Begin(Simulation sim, long fromUtcMs, long nowUtcMs) => new CatchUpRun(sim, fromUtcMs, nowUtcMs);

        /// <summary>The whole catch-up in one step.</summary>
        public static ReturnSummary Run(Simulation sim, long fromUtcMs, long nowUtcMs)
        {
            var run = Begin(sim, fromUtcMs, nowUtcMs);
            run.RunToEnd();
            return run.Finish();
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
