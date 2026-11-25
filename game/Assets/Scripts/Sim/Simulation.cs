using System;
using System.Collections.Generic;
using System.Text;
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
    public sealed partial class Simulation
    {
        private bool _advancing;
        private readonly List<Ship> _ships = new List<Ship>();
        private readonly List<VoyageReceipt> _receipts = new List<VoyageReceipt>();
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

        /// <summary>Every delivery receipt, in the order the ships arrived.</summary>
        public IReadOnlyList<VoyageReceipt> Receipts => _receipts;

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
            BuyShortfall(FindShip(plan.ShipId), quote);
            return CommandResult.Ok();
        }

        /// <summary>Buys exactly the quoted shortfall, moving the credits into the ship's supplies at cost. The quote must be feasible.</summary>
        private void BuyShortfall(Ship ship, VoyageQuote quote)
        {
            foreach (var s in quote.Supplies)
                if (s.ShortfallKg > 0) ship.Supply(s.Kind).Add(s.ShortfallKg, s.TopUpCost);
            Credits -= quote.TopUpCost;
        }

        /// <summary>
        /// Sends a docked ship on a voyage (spec 6.6), as one transaction: quote, reserve freight, demand,
        /// and seats, buy the top-up if <paramref name="buyTopUp"/> allows it, lock the terms, and depart.
        /// With <paramref name="buyTopUp"/> false a ship short of any supply is refused. A refused
        /// dispatch changes nothing.
        /// </summary>
        public CommandResult Dispatch(VoyagePlan plan, bool buyTopUp)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var ship = FindShip(plan.ShipId);
            if (ship != null && ship.IsOnInSystemTransport)
                return CommandResult.Fail($"{ship.Name} is on in-system transport at {Market.Galaxy.Systems[ship.DockedSystemId.Value].Name}. Turn off in-system transport to dispatch it.");
            var quote = Quote(plan);
            if (!quote.IsFeasible) return CommandResult.Fail(string.Join(" ", quote.Problems));

            if (!buyTopUp)
            {
                var shortages = new List<string>();
                foreach (var s in quote.Supplies)
                    if (s.ShortfallKg > 0) shortages.Add($"{Format.Tonnes(s.ShortfallKg)} of {s.Kind.ToString().ToLowerInvariant()}");
                if (shortages.Count > 0)
                    return CommandResult.Fail($"{ship.Name} is short of {string.Join(", ", shortages)} for this voyage (top-up {Format.Credits(quote.TopUpCost)} credits). Press Provision first, or dispatch with the top-up.");
            }

            var reserved = Market.TryReserve(new ReservationRequest(quote.OriginId, quote.DestinationId, plan.Freight, plan.Passengers), out var reservation);
            if (!reserved.Succeeded) return reserved;
            if (reservation.FreightRevenue != quote.FreightRevenue || reservation.FarePerPassenger != quote.FarePerPassenger)
            {
                Market.Release(reservation);
                return CommandResult.Fail($"The market's rates ({Format.Credits(reservation.FreightRevenue)} freight, {Format.Credits(reservation.FarePerPassenger)} per passenger) differ from the quote's ({Format.Credits(quote.FreightRevenue)}, {Format.Credits(quote.FarePerPassenger)}). Nothing was dispatched.");
            }

            var freight = new List<LockedFreight>();
            foreach (var f in reservation.Freight) freight.Add(new LockedFreight(f.CommodityId, f.Tonnes, f.RatePerTonne));
            var voyage = new Voyage(ship.Id, quote.OriginId, quote.DestinationId, quote.DistanceMilliLy, ship.DriveId,
                quote.DepartureTick, checked(quote.DepartureTick + quote.TransitTicks), freight, reservation.Passengers, reservation.FarePerPassenger,
                quote.Fuel.RequiredKg, quote.Food.RequiredKg, quote.Water.RequiredKg);

            // Nothing below can fail.
            BuyShortfall(ship, quote);
            ship.DockedSystemId = null;
            ship.Voyage = voyage;
            Schedule(voyage.ArrivalTick, new ArrivalEvent(ship.Id));
            Market.ReleaseOpeningRates();
            return CommandResult.Ok();
        }

        /// <summary>
        /// Switches in-system transport on or off for a ship (spec 10). On is refused for a ship in transit.
        /// While on, the ship earns its class's daily income after each full game day since it was switched
        /// on; switching off is instant, pays nothing for a partial day, and switching on again starts a
        /// new count. Asking for the state the ship is already in succeeds and changes nothing.
        /// </summary>
        public CommandResult SetInSystemTransport(int shipId, bool on)
        {
            var ship = FindShip(shipId);
            if (ship == null) return CommandResult.Fail($"There is no ship with id {shipId}.");
            if (on == ship.IsOnInSystemTransport) return CommandResult.Ok();

            if (on)
            {
                if (ship.IsInTransit)
                    return CommandResult.Fail($"{ship.Name} is in transit to {Market.Galaxy.Systems[ship.Voyage.DestinationId].Name}, arriving {GameDate.FromTick(ship.Voyage.ArrivalTick)}. In-system transport can only be switched on for a docked ship.");
                long firstPay = checked(CurrentTick + GameTime.TicksPerDay);
                ship.InSystemSinceTick = CurrentTick;
                Schedule(firstPay, new InSystemIncomeEvent(ship.Id));
            }
            else
            {
                ship.InSystemSinceTick = null;
                Events.RemoveWhere(e => e is InSystemIncomeEvent pay && pay.ShipId == ship.Id);
            }
            return CommandResult.Ok();
        }

        /// <summary>One full game day of in-system transport, run by the ship's income event: pay the class's daily income and schedule the next day.</summary>
        internal void PayInSystemDay(int shipId)
        {
            var ship = FindShip(shipId);
            long since = ship?.InSystemSinceTick ?? throw new InvalidOperationException($"Ship {shipId} is not on in-system transport.");
            if (CurrentTick <= since || (CurrentTick - since) % GameTime.TicksPerDay != 0)
                throw new InvalidOperationException($"Ship {shipId}'s in-system pay is due on whole days from tick {since}, not at tick {CurrentTick}.");
            var shipClass = Market.Tuning.FindShip(ship.ClassId) ?? throw new InvalidOperationException($"Ship {shipId}'s class \"{ship.ClassId}\" is not in the tuning.");
            Credits = checked(Credits + shipClass.InSystemDailyIncome);
            Schedule(checked(CurrentTick + GameTime.TicksPerDay), new InSystemIncomeEvent(shipId));
        }

        /// <summary>
        /// Arrival settlement (spec 6.7), run by the ship's arrival event: dock at the destination, use up the
        /// locked supplies once, pay the locked freight and fares once, and keep the receipt.
        /// </summary>
        internal void SettleArrival(int shipId)
        {
            var ship = FindShip(shipId);
            var voyage = ship?.Voyage ?? throw new InvalidOperationException($"Ship {shipId} has no voyage to settle.");
            if (voyage.ArrivalTick != CurrentTick)
                throw new InvalidOperationException($"Ship {shipId} is due at tick {voyage.ArrivalTick}, not {CurrentTick}.");

            var consumed = new List<ConsumedSupply>();
            foreach (var kind in new[] { SupplyKind.Fuel, SupplyKind.Food, SupplyKind.Water })
            {
                long kg = voyage.UseKg(kind);
                consumed.Add(new ConsumedSupply(kind, kg, ship.Supply(kind).Use(kg)));
            }
            Credits = checked(Credits + voyage.Payout);
            ship.Voyage = null;
            ship.DockedSystemId = voyage.DestinationId;
            _receipts.Add(new VoyageReceipt(ship, voyage, consumed));
        }

        /// <summary>
        /// The kilograms of a supply to show as aboard, for display only (spec 6.7, 9.1). Docked: what is
        /// aboard. In transit: what is aboard less the share of the voyage's locked use for the time
        /// elapsed so far. Never stored; changes nothing.
        /// </summary>
        public long RemainingSupplyKg(int shipId, SupplyKind kind)
        {
            var ship = FindShip(shipId) ?? throw new ArgumentException($"There is no ship with id {shipId}.", nameof(shipId));
            long aboard = ship.Supply(kind).Kg;
            return ship.Voyage == null ? aboard : aboard - ship.Voyage.UsedSoFarKg(kind, CurrentTick);
        }

        /// <summary>Every stored value in a fixed text layout: clock, credits, ships and voyages, receipts, events, and market.</summary>
        public string DescribeState()
        {
            var sb = new StringBuilder();
            sb.Append("tick=").Append(Clock.Tick).Append(" speed=").Append(Clock.Speed).Append(" paused=").Append(Clock.IsPaused)
              .Append(" credits=").Append(Credits).Append(" nextShip=").Append(_nextShipId).Append('\n');
            foreach (var s in _ships) sb.Append(s.DescribeState()).Append('\n');
            foreach (var r in _receipts) sb.Append(r.DescribeState()).Append('\n');
            sb.Append(Events.DescribeState());
            if (Market != null) sb.Append(Market.DescribeState());
            return sb.ToString();
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
