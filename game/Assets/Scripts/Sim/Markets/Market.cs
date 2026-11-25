using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.World;

namespace SpaceTrade.Sim.Markets
{
    /// <summary>
    /// Freight supply and demand, freight offers, passenger pools, and provision prices for every
    /// system (spec 5). Stored state is stock levels and random rolls; caps, rates, fares, and prices
    /// are worked out from the current tuning so tuning edits reach existing saves (spec 12).
    /// </summary>
    public sealed class Market
    {
        private const long RandomSalt = 0x4D41524B4554;

        private readonly List<CommodityStock> _supplies = new List<CommodityStock>();
        private readonly List<CommodityStock> _demands = new List<CommodityStock>();
        private readonly List<FreightOffer> _offers = new List<FreightOffer>();
        private readonly List<PassengerPool> _pools = new List<PassengerPool>();
        private readonly List<ProvisionRoll> _provisionRolls = new List<ProvisionRoll>();
        private readonly Dictionary<(int, string), CommodityStock> _supplyIndex = new Dictionary<(int, string), CommodityStock>();
        private readonly Dictionary<(int, string), CommodityStock> _demandIndex = new Dictionary<(int, string), CommodityStock>();
        private readonly Dictionary<(int, string, int), FreightOffer> _offerIndex = new Dictionary<(int, string, int), FreightOffer>();
        private readonly Dictionary<(int, int), PassengerPool> _poolIndex = new Dictionary<(int, int), PassengerPool>();

        public Galaxy Galaxy { get; }
        public TuningData Tuning { get; private set; }

        /// <summary>Replenishment and re-roll schedules count whole intervals from this tick.</summary>
        public long CreatedTick { get; }

        /// <summary>The market's own seeded generator. Its state is part of the save.</summary>
        public SeededRandom Random { get; }

        /// <summary>Ordered by system id, then commodity in tuning order.</summary>
        public IReadOnlyList<CommodityStock> Supplies => _supplies;
        public IReadOnlyList<CommodityStock> Demands => _demands;

        /// <summary>Ordered by origin id, commodity in tuning order, then destination id.</summary>
        public IReadOnlyList<FreightOffer> Offers => _offers;

        /// <summary>Ordered by origin id, then destination id.</summary>
        public IReadOnlyList<PassengerPool> Pools => _pools;

        public IReadOnlyList<ProvisionRoll> ProvisionRolls => _provisionRolls;

        private Market(Galaxy galaxy, TuningData tuning, long createdTick, SeededRandom random)
        {
            Galaxy = galaxy;
            Tuning = tuning;
            CreatedTick = createdTick;
            Random = random;
        }

        /// <summary>
        /// Builds a fresh market: stocks at the tuning start-fill percent of their caps, and every
        /// rate, fare, and provision price rolled from a generator seeded by the galaxy seed.
        /// </summary>
        public static Market Create(Galaxy galaxy, TuningData tuning, long createdTick)
        {
            if (galaxy == null) throw new ArgumentNullException(nameof(galaxy));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            if (createdTick < 0) throw new ArgumentOutOfRangeException(nameof(createdTick), createdTick, "Tick cannot be negative.");

            var m = new Market(galaxy, tuning, createdTick, new SeededRandom(galaxy.Seed ^ RandomSalt));
            var fill = tuning.Start.MarketStartFill;
            var systems = galaxy.Systems;

            foreach (var s in systems)
            foreach (var c in tuning.Commodities)
            {
                if (m.SystemProduces(s.Id, c.Id))
                    m.AddStock(m._supplies, m._supplyIndex, new CommodityStock(s.Id, c.Id, MarketMath.StartingAmount(c.SupplyCapTonnes, fill.SupplyPercent)));
                if (m.SystemConsumes(s.Id, c.Id))
                    m.AddStock(m._demands, m._demandIndex, new CommodityStock(s.Id, c.Id, MarketMath.StartingAmount(c.DemandCapTonnes, fill.DemandPercent)));
            }

            foreach (var origin in systems)
            foreach (var c in tuning.Commodities)
            {
                if (m.FindSupply(origin.Id, c.Id) == null) continue;
                foreach (var dest in systems)
                {
                    if (dest.Id == origin.Id || m.FindDemand(dest.Id, c.Id) == null) continue;
                    var offer = new FreightOffer(origin.Id, c.Id, dest.Id, galaxy.DistanceMilliLy(origin.Id, dest.Id), m.Random.Range(c.RateRollMinPercent, c.RateRollMaxPercent));
                    m._offers.Add(offer);
                    m._offerIndex.Add((origin.Id, c.Id, dest.Id), offer);
                }
            }

            var p = tuning.Passengers;
            foreach (var origin in systems)
            foreach (var dest in systems)
            {
                if (dest.Id == origin.Id) continue;
                int cap = MarketMath.PoolCap(origin.PopulationMillions, dest.PopulationMillions, m.IsHub(origin.Id), p);
                var pool = new PassengerPool(origin.Id, dest.Id, galaxy.DistanceMilliLy(origin.Id, dest.Id), MarketMath.StartingAmount(cap, fill.PassengerPercent), m.Random.Range(p.FareRollMinPercent, p.FareRollMaxPercent));
                m._pools.Add(pool);
                m._poolIndex.Add((origin.Id, dest.Id), pool);
            }

            var pr = tuning.Provisions;
            foreach (var s in systems)
            {
                int fuel = m.Random.Range(pr.PriceRollMinPercent, pr.PriceRollMaxPercent);
                int food = m.Random.Range(pr.PriceRollMinPercent, pr.PriceRollMaxPercent);
                int water = m.Random.Range(pr.PriceRollMinPercent, pr.PriceRollMaxPercent);
                m._provisionRolls.Add(new ProvisionRoll(s.Id, fuel, food, water));
            }

            return m;
        }

        /// <summary>
        /// Switches to newly loaded tuning. Stored stock and rolls are kept; caps, rates, fares, and
        /// prices follow the new values from now on. Every commodity this market trades must still exist.
        /// </summary>
        public void ReplaceTuning(TuningData tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            foreach (var id in _supplies.Concat(_demands).Select(x => x.CommodityId).Distinct())
                if (tuning.FindCommodity(id) == null)
                    throw new ArgumentException($"The market trades commodity \"{id}\", which the new tuning does not define.", nameof(tuning));
            Tuning = tuning;
        }

        private void AddStock(List<CommodityStock> list, Dictionary<(int, string), CommodityStock> index, CommodityStock stock)
        {
            list.Add(stock);
            index.Add((stock.SystemId, stock.CommodityId), stock);
        }

        private IEnumerable<IndustryTuning> IndustriesAt(int systemId)
        {
            foreach (var id in Galaxy.Systems[systemId].IndustryIds)
            {
                var ind = Tuning.FindIndustry(id);
                if (ind != null) yield return ind;
            }
        }

        private bool SystemProduces(int systemId, string commodityId) => IndustriesAt(systemId).Any(i => i.Produces.Contains(commodityId));
        private bool SystemConsumes(int systemId, string commodityId) => IndustriesAt(systemId).Any(i => i.Consumes.Contains(commodityId));

        /// <summary>True when the system has an industry that produces passengers (a population hub).</summary>
        public bool IsHub(int systemId) => IndustriesAt(systemId).Any(i => i.ProducesPassengers);

        public CommodityStock FindSupply(int systemId, string commodityId) => _supplyIndex.TryGetValue((systemId, commodityId), out var s) ? s : null;
        public CommodityStock FindDemand(int systemId, string commodityId) => _demandIndex.TryGetValue((systemId, commodityId), out var s) ? s : null;
        public FreightOffer FindOffer(int originId, string commodityId, int destinationId) => _offerIndex.TryGetValue((originId, commodityId, destinationId), out var o) ? o : null;
        public PassengerPool FindPool(int originId, int destinationId) => _poolIndex.TryGetValue((originId, destinationId), out var p) ? p : null;

        public IEnumerable<FreightOffer> OffersFrom(int originId) => _offers.Where(o => o.OriginId == originId);
        public IEnumerable<PassengerPool> PoolsFrom(int originId) => _pools.Where(p => p.OriginId == originId);

        private CommodityTuning Commodity(string id) => Tuning.FindCommodity(id);

        public int SupplyCap(CommodityStock supply) => Commodity(supply.CommodityId).SupplyCapTonnes;
        public int DemandCap(CommodityStock demand) => Commodity(demand.CommodityId).DemandCapTonnes;

        /// <summary>Tonnes a ship could load now: the smaller of supply at the origin and demand at the destination.</summary>
        public int TonnesAvailable(FreightOffer offer) => Math.Min(FindSupply(offer.OriginId, offer.CommodityId).Tonnes, FindDemand(offer.DestinationId, offer.CommodityId).Tonnes);

        /// <summary>The published rate in credits per tonne.</summary>
        public long RatePerTonne(FreightOffer offer) =>
            MarketMath.DistancePrice(Commodity(offer.CommodityId).BaseRatePerTonne, offer.DistanceMilliLy, offer.RateRollPercent, Tuning.Rates);

        public int PoolCap(PassengerPool pool) =>
            MarketMath.PoolCap(Galaxy.Systems[pool.OriginId].PopulationMillions, Galaxy.Systems[pool.DestinationId].PopulationMillions, IsHub(pool.OriginId), Tuning.Passengers);

        /// <summary>The published fare in credits per passenger.</summary>
        public long FarePerPassenger(PassengerPool pool) =>
            MarketMath.DistancePrice(Tuning.Passengers.BaseFare, pool.DistanceMilliLy, pool.FareRollPercent, Tuning.Rates);

        public ProvisionPrices ProvisionPricesAt(int systemId)
        {
            var p = Tuning.Provisions;
            var roll = _provisionRolls[systemId];
            var mods = IndustriesAt(systemId)
                .Select(i => p.IndustryModifiers.Find(m => m.Industry == i.Id))
                .Where(m => m != null)
                .ToList();
            return new ProvisionPrices(
                MarketMath.ProvisionPrice(p.FuelBasePricePerTonne, mods.Select(m => m.FuelPercent), roll.FuelRollPercent, p),
                MarketMath.ProvisionPrice(p.FoodBasePricePerTonne, mods.Select(m => m.FoodPercent), roll.FoodRollPercent, p),
                MarketMath.ProvisionPrice(p.WaterBasePricePerTonne, mods.Select(m => m.WaterPercent), roll.WaterRollPercent, p));
        }

        // ---- Replenishment and re-rolls ----

        private IEnumerable<int> IntervalsFor(int systemId)
        {
            foreach (var c in Tuning.Commodities)
                if (FindSupply(systemId, c.Id) != null || FindDemand(systemId, c.Id) != null)
                    yield return c.ReplenishIntervalDays;
            yield return Tuning.Passengers.ReplenishIntervalDays;
            yield return Tuning.Rates.RerollIntervalDays;
        }

        private long NextBoundary(int intervalDays, long afterTick)
        {
            long period = intervalDays * GameTime.TicksPerDay;
            long elapsed = Math.Max(0, afterTick - CreatedTick);
            return CreatedTick + (elapsed / period + 1) * period;
        }

        private bool IsBoundary(int intervalDays, long tick)
        {
            long elapsed = tick - CreatedTick;
            return elapsed > 0 && elapsed % (intervalDays * GameTime.TicksPerDay) == 0;
        }

        /// <summary>The first tick after <paramref name="afterTick"/> at which this system has any replenishment or re-roll due.</summary>
        public long NextEventTick(int systemId, long afterTick)
        {
            long best = long.MaxValue;
            foreach (int days in IntervalsFor(systemId)) best = Math.Min(best, NextBoundary(days, afterTick));
            return best;
        }

        private static int Refill(int current, int cap, int amount) => current < cap ? Math.Min(cap, current + amount) : current;

        /// <summary>
        /// Everything due for one system at this tick, in a fixed order: supply and demand by
        /// commodity (tuning order), then passenger pools, then the rate and fare re-roll.
        /// </summary>
        internal void RunDue(int systemId, long tick)
        {
            foreach (var c in Tuning.Commodities)
            {
                if (!IsBoundary(c.ReplenishIntervalDays, tick)) continue;
                var supply = FindSupply(systemId, c.Id);
                if (supply != null) supply.Tonnes = Refill(supply.Tonnes, c.SupplyCapTonnes, c.SupplyReplenishTonnes);
                var demand = FindDemand(systemId, c.Id);
                if (demand != null) demand.Tonnes = Refill(demand.Tonnes, c.DemandCapTonnes, c.DemandReplenishTonnes);
            }

            var p = Tuning.Passengers;
            if (IsBoundary(p.ReplenishIntervalDays, tick))
                foreach (var pool in PoolsFrom(systemId))
                    pool.Waiting = Refill(pool.Waiting, PoolCap(pool), p.ReplenishPassengers);

            if (IsBoundary(Tuning.Rates.RerollIntervalDays, tick))
            {
                foreach (var offer in OffersFrom(systemId))
                {
                    var c = Commodity(offer.CommodityId);
                    offer.RateRollPercent = Random.Range(c.RateRollMinPercent, c.RateRollMaxPercent);
                }
                foreach (var pool in PoolsFrom(systemId))
                    pool.FareRollPercent = Random.Range(p.FareRollMinPercent, p.FareRollMaxPercent);
            }
        }

        // ---- Reservations ----

        private string SystemName(int id) => Galaxy.Systems[id].Name;

        /// <summary>
        /// Takes the requested freight (from both supply at the origin and demand at the destination)
        /// and passenger seats, all together or not at all. On success the reservation carries the
        /// rates and fare published at this moment, locked.
        /// </summary>
        public CommandResult TryReserve(ReservationRequest request, out MarketReservation reservation)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            reservation = null;
            int count = Galaxy.Systems.Count;
            if (request.OriginId < 0 || request.OriginId >= count) return CommandResult.Fail($"There is no system with id {request.OriginId}.");
            if (request.DestinationId < 0 || request.DestinationId >= count) return CommandResult.Fail($"There is no system with id {request.DestinationId}.");
            if (request.OriginId == request.DestinationId) return CommandResult.Fail($"The origin and destination are both {SystemName(request.OriginId)}.");

            string from = SystemName(request.OriginId);
            string to = SystemName(request.DestinationId);
            var problems = new List<string>();

            var wanted = new Dictionary<string, long>();
            foreach (var line in request.Freight)
            {
                var c = line.CommodityId == null ? null : Commodity(line.CommodityId);
                if (c == null) { problems.Add($"\"{line.CommodityId}\" is not a commodity."); continue; }
                if (line.Tonnes <= 0) { problems.Add($"Each freight line must be at least 1 t; the {c.Name.ToLowerInvariant()} line asks for {line.Tonnes} t."); continue; }
                wanted.TryGetValue(c.Id, out long sofar);
                wanted[c.Id] = sofar + line.Tonnes;
            }

            var lines = new List<ReservedFreight>();
            foreach (var c in Tuning.Commodities)
            {
                if (!wanted.TryGetValue(c.Id, out long tonnes)) continue;
                string name = c.Name.ToLowerInvariant();
                var supply = FindSupply(request.OriginId, c.Id);
                var demand = FindDemand(request.DestinationId, c.Id);
                if (supply == null) problems.Add($"{from} does not produce {name}.");
                if (demand == null) problems.Add($"{to} does not buy {name}.");
                if (supply == null || demand == null) continue;
                int available = Math.Min(supply.Tonnes, demand.Tonnes);
                if (tonnes > available)
                    problems.Add($"Only {available} t of {name} available from {from} to {to} (supply {supply.Tonnes} t at {from}, demand {demand.Tonnes} t at {to}); requested {tonnes} t.");
                else
                    lines.Add(new ReservedFreight(c.Id, (int)tonnes, RatePerTonne(FindOffer(request.OriginId, c.Id, request.DestinationId))));
            }

            var pool = FindPool(request.OriginId, request.DestinationId);
            if (request.Passengers < 0) problems.Add($"The passenger count cannot be negative, got {request.Passengers}.");
            else if (request.Passengers > pool.Waiting) problems.Add($"Only {pool.Waiting} passengers waiting from {from} to {to}; requested {request.Passengers}.");

            if (problems.Count > 0) return CommandResult.Fail(string.Join(" ", problems));

            foreach (var line in lines)
            {
                FindSupply(request.OriginId, line.CommodityId).Tonnes -= line.Tonnes;
                FindDemand(request.DestinationId, line.CommodityId).Tonnes -= line.Tonnes;
            }
            pool.Waiting -= request.Passengers;
            reservation = new MarketReservation(this, request.OriginId, request.DestinationId, lines, request.Passengers, FarePerPassenger(pool));
            return CommandResult.Ok();
        }

        /// <summary>
        /// Puts back exactly what a reservation took. For undoing a reservation inside the same
        /// command (a dispatch that fails at a later step); a reservation can be released once.
        /// </summary>
        public CommandResult Release(MarketReservation reservation)
        {
            if (reservation == null) throw new ArgumentNullException(nameof(reservation));
            if (reservation.Owner != this) return CommandResult.Fail("This reservation belongs to a different market.");
            if (reservation.IsReleased) return CommandResult.Fail("This reservation has already been released.");
            foreach (var line in reservation.Freight)
            {
                FindSupply(reservation.OriginId, line.CommodityId).Tonnes += line.Tonnes;
                FindDemand(reservation.DestinationId, line.CommodityId).Tonnes += line.Tonnes;
            }
            FindPool(reservation.OriginId, reservation.DestinationId).Waiting += reservation.Passengers;
            reservation.IsReleased = true;
            return CommandResult.Ok();
        }

        /// <summary>Every stored value in a fixed text layout, for comparing market states exactly.</summary>
        public string DescribeState()
        {
            var sb = new StringBuilder();
            sb.Append("created=").Append(CreatedTick)
              .Append(" rng=").Append(Random.S0).Append(',').Append(Random.S1).Append(',').Append(Random.S2).Append(',').Append(Random.S3).Append('\n');
            foreach (var s in _supplies) sb.Append("supply ").Append(s.SystemId).Append(' ').Append(s.CommodityId).Append(' ').Append(s.Tonnes).Append('\n');
            foreach (var d in _demands) sb.Append("demand ").Append(d.SystemId).Append(' ').Append(d.CommodityId).Append(' ').Append(d.Tonnes).Append('\n');
            foreach (var o in _offers) sb.Append("offer ").Append(o.OriginId).Append(' ').Append(o.CommodityId).Append(' ').Append(o.DestinationId).Append(" roll=").Append(o.RateRollPercent).Append('\n');
            foreach (var p in _pools) sb.Append("pool ").Append(p.OriginId).Append(' ').Append(p.DestinationId).Append(" waiting=").Append(p.Waiting).Append(" roll=").Append(p.FareRollPercent).Append('\n');
            foreach (var r in _provisionRolls) sb.Append("provisions ").Append(r.SystemId).Append(' ').Append(r.FuelRollPercent).Append(' ').Append(r.FoodRollPercent).Append(' ').Append(r.WaterRollPercent).Append('\n');
            return sb.ToString();
        }
    }
}
