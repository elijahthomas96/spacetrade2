using System;
using System.Collections.Generic;
using SpaceTrade.Sim.Events;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Ships;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Voyages;

namespace SpaceTrade.Sim
{
    /// <summary>
    /// The whole game state and the only place it changes. Ordinary play, offline catch-up,
    /// and debug skip all move time forward through <see cref="Advance"/>.
    /// </summary>
    public sealed class Simulation
    {
        private bool _advancing;
        private readonly List<Ship> _ships = new List<Ship>();
        private int _nextShipId = 1;

        public SimClock Clock { get; }
        public EventQueue Events { get; } = new EventQueue();

        public long CurrentTick => Clock.Tick;

        /// <summary>Null until <see cref="AttachMarket"/> is called.</summary>
        public Market Market { get; private set; }

        public long Credits { get; private set; }

        /// <summary>The player's fleet, in ship-id order. Ids start at 1.</summary>
        public IReadOnlyList<Ship> Ships => _ships;

        public Ship FindShip(int id) => _ships.Find(s => s.Id == id);

        public Simulation(long startTick = 0, ClockSpeed speed = ClockSpeed.X1, bool paused = false)
        {
            if (startTick < 0) throw new ArgumentOutOfRangeException(nameof(startTick), startTick, "Start tick cannot be negative.");
            if (!ClockSpeeds.IsValid(speed)) throw new ArgumentOutOfRangeException(nameof(speed), (int)speed, "Speed must be 1x, 3x, or 5x.");
            Clock = new SimClock(startTick, speed, paused);
        }

        /// <summary>Installs the market and schedules each system's first replenishment event.</summary>
        public void AttachMarket(Market market)
        {
            if (market == null) throw new ArgumentNullException(nameof(market));
            if (Market != null) throw new InvalidOperationException("A market is already attached.");
            if (_advancing) throw new InvalidOperationException("A market cannot be attached from inside an event.");
            if (market.CreatedTick > CurrentTick)
                throw new ArgumentException($"The market was created at tick {market.CreatedTick}, after the current tick {CurrentTick}.", nameof(market));
            Market = market;
            foreach (var s in market.Galaxy.Systems)
                Schedule(market.NextEventTick(s.Id, CurrentTick), new MarketEvent(s.Id));
        }

        /// <summary>
        /// Adds a docked ship with empty supplies. For building game state (new game, ship purchase,
        /// tests); not a player command.
        /// </summary>
        internal Ship AddShip(string name, string classId, string driveId, int dockedSystemId)
        {
            if (Market == null) throw new InvalidOperationException("Attach a market before adding ships.");
            if (Market.Tuning.FindShip(classId) == null) throw new ArgumentException($"\"{classId}\" is not a ship class in the tuning.", nameof(classId));
            if (Market.Tuning.FindDrive(driveId) == null) throw new ArgumentException($"\"{driveId}\" is not a drive in the tuning.", nameof(driveId));
            if (dockedSystemId < 0 || dockedSystemId >= Market.Galaxy.Systems.Count)
                throw new ArgumentOutOfRangeException(nameof(dockedSystemId), dockedSystemId, "No such system.");
            var ship = new Ship(_nextShipId++, name, classId, driveId, dockedSystemId);
            _ships.Add(ship);
            return ship;
        }

        /// <summary>Sets the credit balance. For building game state (new game, tests); not a player command.</summary>
        internal void SetCredits(long credits)
        {
            if (credits < 0) throw new ArgumentOutOfRangeException(nameof(credits), credits, "Credits cannot be negative.");
            Credits = credits;
        }

        /// <summary>
        /// Prices a voyage (spec 6.5). The one quote function: the voyage preview and dispatch both
        /// call this. Changes nothing.
        /// </summary>
        public VoyageQuote Quote(VoyagePlan plan) => VoyageQuoter.Quote(this, plan);

        /// <summary>
        /// The one-click Provision button: buys exactly the quoted shortfall of fuel, food, and water at
        /// local prices, moving the credits into the ship's supplies at cost. Refused, changing nothing,
        /// unless the quote is feasible.
        /// </summary>
        public CommandResult Provision(VoyagePlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var quote = Quote(plan);
            if (!quote.IsFeasible) return CommandResult.Fail(string.Join(" ", quote.Problems));
            var ship = FindShip(plan.ShipId);
            foreach (var s in quote.Supplies)
                if (s.ShortfallKg > 0) ship.Supply(s.Kind).Add(s.ShortfallKg, s.TopUpCost);
            Credits -= quote.TopUpCost;
            return CommandResult.Ok();
        }

        public void Schedule(long tick, SimEvent ev)
        {
            if (tick < CurrentTick)
                throw new ArgumentOutOfRangeException(nameof(tick), tick, $"Cannot schedule an event at tick {tick}, before the current tick {CurrentTick}.");
            Events.Enqueue(tick, ev);
        }

        /// <summary>
        /// Processes every event due at or before <paramref name="toTick"/> in order, then leaves
        /// the clock at <paramref name="toTick"/>. Events scheduled during processing are included
        /// if they fall in range.
        /// </summary>
        public void Advance(long toTick)
        {
            if (toTick < CurrentTick)
                throw new ArgumentOutOfRangeException(nameof(toTick), toTick, $"Time cannot run backward: current tick is {CurrentTick}.");
            if (_advancing)
                throw new InvalidOperationException("Advance cannot be called from inside an event.");

            _advancing = true;
            try
            {
                while (Events.TryDequeueDueBy(toTick, out var next))
                {
                    Clock.Tick = next.Tick;
                    next.Event.Apply(this);
                }
                Clock.Tick = toTick;
            }
            finally
            {
                _advancing = false;
            }
        }

        public CommandResult Pause()
        {
            Clock.IsPaused = true;
            return CommandResult.Ok();
        }

        /// <summary>Resumes at the last selected speed.</summary>
        public CommandResult Play()
        {
            Clock.IsPaused = false;
            return CommandResult.Ok();
        }

        /// <summary>Selects 1x, 3x, or 5x. Does not change the pause state.</summary>
        public CommandResult SetSpeed(ClockSpeed speed)
        {
            if (!ClockSpeeds.IsValid(speed))
                return CommandResult.Fail($"Speed {(int)speed}x is not allowed. Choose 1x, 3x, or 5x.");
            Clock.Speed = speed;
            return CommandResult.Ok();
        }
    }
}
