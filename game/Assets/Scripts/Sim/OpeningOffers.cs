using System;
using System.Collections.Generic;
using System.Linq;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Voyages;

namespace SpaceTrade.Sim
{
    /// <summary>
    /// One job at a ship's port: a single freight offer or a single passenger booking, loaded with as
    /// much as the ship can feasibly take, priced by the real quote function.
    /// </summary>
    public sealed class OpeningJob
    {
        /// <summary>The commodity id for a freight job, or null for a passenger job.</summary>
        public string CommodityId { get; }
        public bool IsPassengerJob => CommodityId == null;
        public VoyageQuote Quote { get; }

        public int DestinationId => Quote.DestinationId;
        public long TransitTicks => Quote.TransitTicks;
        public long NetProfit => Quote.NetProfit;

        internal OpeningJob(string commodityId, VoyageQuote quote)
        {
            CommodityId = commodityId;
            Quote = quote;
        }
    }

    /// <summary>The opening-offer guarantee (spec 6.4) checked against a ship at its port right now.</summary>
    public sealed class OpeningOfferCheck
    {
        /// <summary>Every job the ship can fly and the player can afford, freight offers first, then passenger bookings.</summary>
        public IReadOnlyList<OpeningJob> FeasibleJobs { get; }
        public int ProfitableJobCount { get; }

        /// <summary>
        /// Two feasible jobs to different destinations, one with strictly fewer transit ticks and the other
        /// with strictly more net profit on its quote, so neither is better on both. Always true when
        /// fewer than two jobs are required.
        /// </summary>
        public bool HasTradeoffPair { get; }

        public int MinFeasibleJobs { get; }
        public int MinProfitableJobs { get; }

        public bool EnoughFeasible => FeasibleJobs.Count >= MinFeasibleJobs;
        public bool EnoughProfitable => ProfitableJobCount >= MinProfitableJobs;
        public bool Holds => EnoughFeasible && EnoughProfitable && HasTradeoffPair;

        internal OpeningOfferCheck(IReadOnlyList<OpeningJob> jobs, int minFeasible, int minProfitable)
        {
            FeasibleJobs = jobs;
            MinFeasibleJobs = minFeasible;
            MinProfitableJobs = minProfitable;
            ProfitableJobCount = jobs.Count(j => j.NetProfit > 0);
            HasTradeoffPair = minFeasible < 2 || jobs.Any(a => jobs.Any(b =>
                a.DestinationId != b.DestinationId && a.TransitTicks < b.TransitTicks && a.NetProfit < b.NetProfit));
        }
    }

    /// <summary>Finds a docked ship's jobs with <see cref="Simulation.Quote"/> and checks the opening-offer guarantee. Changes nothing.</summary>
    public static class OpeningOffers
    {
        public static IReadOnlyList<OpeningJob> FindJobs(Simulation sim, int shipId)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            var ship = sim.FindShip(shipId) ?? throw new ArgumentException($"There is no ship with id {shipId}.", nameof(shipId));
            if (!ship.DockedSystemId.HasValue) throw new ArgumentException($"{ship.Name} is not docked.", nameof(shipId));
            var market = sim.Market;
            int origin = ship.DockedSystemId.Value;
            int berths = market.Tuning.FindShip(ship.ClassId).Berths;

            var jobs = new List<OpeningJob>();
            foreach (var offer in market.OffersFrom(origin))
            {
                VoyagePlan Plan(long t) => new VoyagePlan(shipId, offer.DestinationId, new[] { new FreightLine(offer.CommodityId, (int)t) });
                var quote = LargestFeasible(sim, market.TonnesAvailable(offer), Plan);
                if (quote != null) jobs.Add(new OpeningJob(offer.CommodityId, quote));
            }
            foreach (var pool in market.PoolsFrom(origin))
            {
                VoyagePlan Plan(long p) => new VoyagePlan(shipId, pool.DestinationId, null, (int)p);
                var quote = LargestFeasible(sim, Math.Min(pool.Waiting, berths), Plan);
                if (quote != null) jobs.Add(new OpeningJob(null, quote));
            }
            return jobs;
        }

        public static OpeningOfferCheck Check(Simulation sim, int shipId)
        {
            var rules = sim.Market.Tuning.Start.OpeningOffers;
            return new OpeningOfferCheck(FindJobs(sim, shipId), rules.MinFeasibleJobs, rules.MinProfitableJobs);
        }

        /// <summary>
        /// The quote for the largest load from 1 to <paramref name="max"/> that is feasible, or null if none is.
        /// Every limit (hold, tank, berths, stock, credits) only gets harder as the load grows.
        /// </summary>
        private static VoyageQuote LargestFeasible(Simulation sim, long max, Func<long, VoyagePlan> plan)
        {
            if (max < 1) return null;
            var smallest = sim.Quote(plan(1));
            if (!smallest.IsFeasible) return null;
            long lo = 1, hi = max;
            var best = smallest; // always the quote for lo
            while (lo < hi)
            {
                long mid = hi - (hi - lo) / 2;
                var q = sim.Quote(plan(mid));
                if (q.IsFeasible) { lo = mid; best = q; } else hi = mid - 1;
            }
            return best;
        }
    }
}
