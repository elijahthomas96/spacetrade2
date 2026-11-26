using System;
using System.Collections.Generic;
using System.Linq;
using SpaceTrade.Sim.Events;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Routes;
using SpaceTrade.Sim.Ships;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Voyages;

namespace SpaceTrade.Sim
{
    /// <summary>Putting a saved game back (spec 11.1). Only the save reader calls this, on a brand-new simulation.</summary>
    public sealed partial class Simulation
    {
        /// <summary>The id the next new ship will get.</summary>
        internal int NextShipId => _nextShipId;

        /// <summary>
        /// Fills a brand-new simulation with saved state, including every pending event with its exact ordering number,
        /// then checks that the events match the state. Throws <see cref="ArgumentException"/> when anything does not fit.
        /// After the checks, a system whose next market event the current tuning's intervals put at a different tick is
        /// rescheduled to the first due tick after now; nothing is granted for time that has already passed.
        /// </summary>
        internal void RestoreState(Market market, long credits, long suppliesBought, long inSystemIncome, int nextShipId,
            IEnumerable<Ship> ships, DeliveryTotals deliveryTotals, IEnumerable<VoyageReceipt> receipts, IEnumerable<Notification> notifications,
            IEnumerable<EventQueue.ScheduledEvent> events, long nextSequence)
        {
            if (Market != null || _ships.Count > 0 || Events.Count > 0) throw new InvalidOperationException("Saved state can only go into a brand-new simulation.");
            if (credits < 0) throw new ArgumentException($"Credits are {credits}; they cannot be negative.");
            if (suppliesBought < 0 || inSystemIncome < 0) throw new ArgumentException("The supplies-bought and in-system income totals cannot be negative.");
            Market = market;
            Credits = credits;
            SuppliesBoughtTotal = suppliesBought;
            InSystemIncomeTotal = inSystemIncome;
            foreach (var ship in ships)
            {
                if (_ships.Count > 0 && ship.Id <= _ships[_ships.Count - 1].Id) throw new ArgumentException($"Ship ids must rise in order; ship {ship.Id} comes after ship {_ships[_ships.Count - 1].Id}.");
                if (ship.Id < 1 || ship.Id >= nextShipId) throw new ArgumentException($"Ship id {ship.Id} is outside 1 to {nextShipId - 1}.");
                _ships.Add(ship);
            }
            _nextShipId = nextShipId;
            _receipts.AddRange(receipts);
            try
            {
                deliveryTotals.Since(DeliveryTotals.Of(_receipts));
            }
            catch (ArgumentException)
            {
                throw new ArgumentException($"The delivery totals ({deliveryTotals.DescribeState()}) are smaller than the {_receipts.Count} receipts kept add up to; they must cover every delivery.");
            }
            _deliveryTotals = deliveryTotals.Copy();
            _notifications.AddRange(notifications);
            Events.Restore(events, nextSequence);
            CheckRestoredEvents();
            RescheduleMarketEventsForTuning();
            TrimNotifications();
            TrimReceipts();
        }

        private void CheckRestoredEvents()
        {
            var arrivals = new Dictionary<int, int>();
            var pays = new Dictionary<int, int>();
            var checks = new Dictionary<int, int>();
            var markets = new Dictionary<int, int>();
            void Count(Dictionary<int, int> d, int id) => d[id] = d.TryGetValue(id, out int n) ? n + 1 : 1;

            foreach (var e in Events.Pending)
            {
                if (e.Tick < CurrentTick) throw new ArgumentException($"An event is due at tick {e.Tick}, before the saved tick {CurrentTick}.");
                switch (e.Event)
                {
                    case ArrivalEvent a:
                    {
                        var ship = FindShip(a.EntityId);
                        if (ship?.Voyage == null || ship.Voyage.ArrivalTick != e.Tick)
                            throw new ArgumentException($"An arrival event at tick {e.Tick} for ship {a.EntityId} does not match any voyage in flight.");
                        Count(arrivals, ship.Id);
                        break;
                    }
                    case InSystemIncomeEvent p:
                    {
                        var ship = FindShip(p.ShipId);
                        long? since = ship?.InSystemSinceTick;
                        if (!since.HasValue || e.Tick <= since.Value || (e.Tick - since.Value) % GameTime.TicksPerDay != 0 || e.Tick - GameTime.TicksPerDay > CurrentTick)
                            throw new ArgumentException($"An in-system pay event at tick {e.Tick} for ship {p.ShipId} does not match its next full day on in-system transport.");
                        Count(pays, ship.Id);
                        break;
                    }
                    case RouteCheckEvent c:
                    {
                        var ship = FindShip(c.ShipId);
                        if (ship?.Route?.PendingCheckTick != e.Tick)
                            throw new ArgumentException($"A route check at tick {e.Tick} for ship {c.ShipId} does not match its route's pending check.");
                        Count(checks, ship.Id);
                        break;
                    }
                    case MarketEvent m:
                        if (m.EntityId < 0 || m.EntityId >= Market.Galaxy.Systems.Count) throw new ArgumentException($"A market event names system {m.EntityId}, which does not exist.");
                        Count(markets, m.EntityId);
                        break;
                    default:
                        throw new ArgumentException($"A saved event of type {e.Event.GetType().Name} is not one the game knows.");
                }
            }

            foreach (var ship in _ships)
            {
                int Expect(Dictionary<int, int> d) => d.TryGetValue(ship.Id, out int n) ? n : 0;
                if (Expect(arrivals) != (ship.Voyage != null ? 1 : 0))
                    throw new ArgumentException($"Ship {ship.Id} has {Expect(arrivals)} arrival events; a ship in flight needs exactly one and a docked ship none.");
                if (Expect(pays) != (ship.InSystemSinceTick.HasValue ? 1 : 0))
                    throw new ArgumentException($"Ship {ship.Id} has {Expect(pays)} in-system pay events; a ship on in-system transport needs exactly one and any other ship none.");
                if (Expect(checks) != (ship.Route?.PendingCheckTick != null ? 1 : 0))
                    throw new ArgumentException($"Ship {ship.Id} has {Expect(checks)} route check events, which does not match its route's pending check.");
            }
            foreach (var s in Market.Galaxy.Systems)
                if (!markets.TryGetValue(s.Id, out int n) || n != 1)
                    throw new ArgumentException($"System {s.Id} needs exactly one market event; the save has {(markets.TryGetValue(s.Id, out int k) ? k : 0)}.");
        }

        /// <summary>
        /// For each system, the next market event under the current tuning's intervals is the first due tick after now.
        /// With unchanged tuning that is exactly the saved event, which is kept with its ordering number. A system whose
        /// intervals changed gets its event moved to that tick. Due ticks that have already passed are not run.
        /// </summary>
        private void RescheduleMarketEventsForTuning()
        {
            var saved = Events.Pending.Where(e => e.Event is MarketEvent).ToDictionary(e => e.Event.EntityId, e => e.Tick);
            foreach (var s in Market.Galaxy.Systems)
            {
                long due = Market.NextEventTick(s.Id, CurrentTick);
                if (due == saved[s.Id]) continue;
                int id = s.Id;
                Events.RemoveWhere(e => e is MarketEvent m && m.EntityId == id);
                Schedule(due, new MarketEvent(id));
            }
        }
    }
}
