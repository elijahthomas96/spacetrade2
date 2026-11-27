using System;
using System.Collections.Generic;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Voyages;

namespace SpaceTrade.Sim
{
    /// <summary>
    /// One job on a ship's job board: a single freight offer or a single passenger booking from the port the ship is
    /// docked at, with the port's own figures and, for this ship, the real quote for the largest load it can feasibly
    /// take, or the quote's own reason why it cannot fly the job at all.
    /// </summary>
    public sealed class JobBoardEntry
    {
        /// <summary>The commodity id for a freight job, or null for a passenger job.</summary>
        public string CommodityId { get; }
        public bool IsPassengerJob => CommodityId == null;
        public int DestinationId { get; }
        public long DistanceMilliLy { get; }

        /// <summary>What the port can load now: tonnes (limited by supply here and demand there), or passengers waiting.</summary>
        public long Available { get; }

        /// <summary>The published rate now: credits per tonne, or per passenger.</summary>
        public long Rate { get; }

        /// <summary>How long this ship takes to get there, in game ticks.</summary>
        public long TransitTicks { get; }

        /// <summary>The real quote for the largest load this ship can feasibly take, or null when it cannot fly the job.</summary>
        public VoyageQuote BestQuote { get; }

        public bool CanFly => BestQuote != null;

        /// <summary>The largest feasible load: tonnes or passengers. Zero when the ship cannot fly the job.</summary>
        public long LargestLoad { get; }

        /// <summary>Net profit on <see cref="BestQuote"/>. Zero when the ship cannot fly the job.</summary>
        public long NetProfit => BestQuote == null ? 0 : BestQuote.NetProfit;

        /// <summary>Why the ship cannot fly the job even with the smallest load: the quote's own problems. Null when it can.</summary>
        public string Reason { get; }

        internal JobBoardEntry(string commodityId, int destinationId, long distanceMilliLy, long available, long rate, long transitTicks,
            VoyageQuote bestQuote, long largestLoad, string reason)
        {
            CommodityId = commodityId;
            DestinationId = destinationId;
            DistanceMilliLy = distanceMilliLy;
            Available = available;
            Rate = rate;
            TransitTicks = transitTicks;
            BestQuote = bestQuote;
            LargestLoad = largestLoad;
            Reason = reason;
        }
    }

    /// <summary>
    /// Every job at the port a ship is docked at, priced for that ship by <see cref="Simulation.Quote"/> (spec 6.5, 9.2).
    /// A port lists only jobs within the game's job distance limit (spec 5.1). Read-only: making one changes nothing.
    /// </summary>
    public sealed class JobBoard
    {
        public int ShipId { get; }

        /// <summary>The port the jobs leave from, or null when the ship is not docked.</summary>
        public int? OriginId { get; }

        /// <summary>Why there is no board (the ship is in transit), in the quote's own words. Null when there is one.</summary>
        public string Problem { get; }

        /// <summary>Freight offers first, then passenger bookings, each in the market's own order.</summary>
        public IReadOnlyList<JobBoardEntry> Entries { get; }

        internal JobBoard(int shipId, int? originId, string problem, IReadOnlyList<JobBoardEntry> entries)
        {
            ShipId = shipId;
            OriginId = originId;
            Problem = problem;
            Entries = entries;
        }
    }

    public sealed partial class Simulation
    {
        /// <summary>
        /// The job board for a ship: every freight offer and passenger booking from the port it is docked at, each with
        /// the quote for the largest load the ship can feasibly take, found with <see cref="Quote"/> exactly as the
        /// opening-offer check does (spec 6.4), or the quote's reason when it cannot fly the job. For a ship in transit
        /// the board is empty and <see cref="JobBoard.Problem"/> says why. Read-only: it changes nothing.
        /// </summary>
        public JobBoard JobBoardFor(int shipId)
        {
            var ship = FindShip(shipId) ?? throw new ArgumentException($"There is no ship with id {shipId}.", nameof(shipId));
            if (!ship.DockedSystemId.HasValue)
                return new JobBoard(shipId, null, string.Join(" ", Quote(new VoyagePlan(shipId, 0)).Problems), new List<JobBoardEntry>());

            int origin = ship.DockedSystemId.Value;
            int berths = Market.Tuning.FindShip(ship.ClassId).Berths;
            var entries = new List<JobBoardEntry>();
            foreach (var offer in Market.OffersFrom(origin))
            {
                VoyagePlan Plan(long t) => new VoyagePlan(shipId, offer.DestinationId, new[] { new FreightLine(offer.CommodityId, (int)t) });
                long available = Market.TonnesAvailable(offer);
                entries.Add(Entry(offer.CommodityId, offer.DestinationId, offer.DistanceMilliLy, available, Market.RatePerTonne(offer), available, Plan));
            }
            foreach (var pool in Market.PoolsFrom(origin))
            {
                VoyagePlan Plan(long p) => new VoyagePlan(shipId, pool.DestinationId, null, (int)p);
                entries.Add(Entry(null, pool.DestinationId, pool.DistanceMilliLy, pool.Waiting, Market.FarePerPassenger(pool), Math.Min(pool.Waiting, berths), Plan));
            }
            return new JobBoard(shipId, origin, null, entries);
        }

        /// <summary>
        /// One entry: the quote for the largest load from 1 to <paramref name="max"/> that is feasible, by binary search.
        /// Every limit (hold, tank, berths, stock, credits) only gets harder as the load grows, so if a load of 1 is not
        /// feasible none is, and its quote's problems are the reason.
        /// </summary>
        private JobBoardEntry Entry(string commodityId, int destinationId, long distanceMilliLy, long available, long rate, long max, Func<long, VoyagePlan> plan)
        {
            var smallest = Quote(plan(1));
            if (!smallest.IsFeasible)
                return new JobBoardEntry(commodityId, destinationId, distanceMilliLy, available, rate, smallest.TransitTicks, null, 0, string.Join(" ", smallest.Problems));
            long lo = 1, hi = Math.Max(1, max);
            var best = smallest; // always the quote for lo
            while (lo < hi)
            {
                long mid = hi - (hi - lo) / 2;
                var q = Quote(plan(mid));
                if (q.IsFeasible) { lo = mid; best = q; } else hi = mid - 1;
            }
            return new JobBoardEntry(commodityId, destinationId, distanceMilliLy, available, rate, best.TransitTicks, best, lo, null);
        }
    }
}
