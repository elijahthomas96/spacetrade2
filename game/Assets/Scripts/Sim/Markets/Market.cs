using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Tuning;
using SpaceTrade.Sim.World;
using SpaceTrade.Sim.Yards;

namespace SpaceTrade.Sim.Markets
{
    /// <summary>
    /// Freight supply and demand, freight offers, passenger pools, provision prices, and shipyard stock
    /// for every system (spec 5, 7). Stored state is stock levels and random rolls; caps, rates, fares,
    /// stock limits, and prices are worked out from the current tuning so tuning edits reach existing saves (spec 12).
    /// </summary>
    public sealed class Market
    {
        private const long RandomSalt = 0x4D41524B4554;
        private const long YardRandomSalt = 0x5941524453;

        private readonly List<Shipyard> _yards = new List<Shipyard>();

        private readonly List<CommodityStock> _supplies = new List<CommodityStock>();
        private readonly List<CommodityStock> _demands = new List<CommodityStock>();
        private readonly List<FreightOffer> _offers = new List<FreightOffer>();
        private readonly List<PassengerPool> _pools = new List<PassengerPool>();
        private readonly List<ProvisionRoll> _provisionRolls = new List<ProvisionRoll>();
        private readonly Dictionary<(int, string), CommodityStock> _supplyIndex = new Dictionary<(int, string), CommodityStock>();
        private readonly Dictionary<(int, string), CommodityStock> _demandIndex = new Dictionary<(int, string), CommodityStock>();
        private readonly Dictionary<(int, string, int), FreightOffer> _offerIndex = new Dictionary<(int, string, int), FreightOffer>();
        private readonly Dictionary<(int, int), PassengerPool> _poolIndex = new Dictionary<(int, int), PassengerPool>();
        private readonly Dictionary<int, List<FreightOffer>> _offersByOrigin = new Dictionary<int, List<FreightOffer>>();
        private readonly Dictionary<int, List<PassengerPool>> _poolsByOrigin = new Dictionary<int, List<PassengerPool>>();

        public Galaxy Galaxy { get; }
        public TuningData Tuning { get; private set; }

        /// <summary>Replenishment and re-roll schedules count whole intervals from this tick.</summary>
        public long CreatedTick { get; }

        /// <summary>The market's own seeded generator. Its state is part of the save.</summary>
        public SeededRandom Random { get; }

        /// <summary>
        /// The shipyards' own seeded generator, separate from <see cref="Random"/> so yards never change a
        /// seed's market rolls. Used for yard rolls at creation and for bought ships' names. Part of the save.
        /// </summary>
        public SeededRandom YardRandom { get; }

        /// <summary>One per shipyard system, ordered by system id.</summary>
        public IReadOnlyList<Shipyard> Shipyards => _yards;

        /// <summary>Ordered by system id, then commodity in tuning order.</summary>
        public IReadOnlyList<CommodityStock> Supplies => _supplies;
        public IReadOnlyList<CommodityStock> Demands => _demands;

        /// <summary>Ordered by origin id, commodity in tuning order, then destination id.</summary>
        public IReadOnlyList<FreightOffer> Offers => _offers;

        /// <summary>Ordered by origin id, then destination id.</summary>
        public IReadOnlyList<PassengerPool> Pools => _pools;

        public IReadOnlyList<ProvisionRoll> ProvisionRolls => _provisionRolls;

        /// <summary>
        /// True from new-game creation until the first dispatch: the start system's freight rates and
        /// passenger fares are not re-rolled, so the opening-offer guarantee (spec 6.4) holds until
        /// the first voyage leaves. Every other system re-rolls as normal.
        /// </summary>
        public bool HoldsOpeningRates { get; private set; }

        /// <summary>
        /// Ports offer freight and passenger jobs only to systems within this distance (spec 5). Taken from
        /// galaxy.maxJobDistanceMilliLy when the market is created and kept in the save; later tuning edits do not change it.
        /// </summary>
        public long MaxJobDistanceMilliLy { get; }

        private Market(Galaxy galaxy, TuningData tuning, long createdTick, SeededRandom random, SeededRandom yardRandom, long maxJobDistanceMilliLy)
        {
            Galaxy = galaxy;
            Tuning = tuning;
            CreatedTick = createdTick;
            Random = random;
            YardRandom = yardRandom;
            MaxJobDistanceMilliLy = maxJobDistanceMilliLy;
        }

        /// <summary>True when ports offer jobs between these two systems: different systems no farther apart than <see cref="MaxJobDistanceMilliLy"/>.</summary>
        public bool WithinJobDistance(int fromId, int toId) => fromId != toId && Galaxy.DistanceMilliLy(fromId, toId) <= MaxJobDistanceMilliLy;

        /// <summary>
        /// Builds a fresh market: stocks at the tuning start-fill percent of their caps, and every
        /// rate, fare, and provision price rolled from a generator seeded by the galaxy seed.
        /// </summary>
        public static Market Create(Galaxy galaxy, TuningData tuning, long createdTick)
        {
            if (galaxy == null) throw new ArgumentNullException(nameof(galaxy));
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            if (createdTick < 0) throw new ArgumentOutOfRangeException(nameof(createdTick), createdTick, "Tick cannot be negative.");

            var m = new Market(galaxy, tuning, createdTick, new SeededRandom(galaxy.Seed ^ RandomSalt), new SeededRandom(galaxy.Seed ^ YardRandomSalt), tuning.Galaxy.MaxJobDistanceMilliLy);
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
                    if (!m.WithinJobDistance(origin.Id, dest.Id) || m.FindDemand(dest.Id, c.Id) == null) continue;
                    m.AddOffer(new FreightOffer(origin.Id, c.Id, dest.Id, galaxy.DistanceMilliLy(origin.Id, dest.Id), m.Random.Range(c.RateRollMinPercent, c.RateRollMaxPercent)));
                }
            }

            var p = tuning.Passengers;
            foreach (var origin in systems)
            foreach (var dest in systems)
            {
                if (!m.WithinJobDistance(origin.Id, dest.Id)) continue;
                int cap = MarketMath.PoolCap(origin.PopulationMillions, dest.PopulationMillions, m.IsHub(origin.Id), p);
                m.AddPool(new PassengerPool(origin.Id, dest.Id, galaxy.DistanceMilliLy(origin.Id, dest.Id), MarketMath.StartingAmount(cap, fill.PassengerPercent), m.Random.Range(p.FareRollMinPercent, p.FareRollMaxPercent)));
            }

            var pr = tuning.Provisions;
            foreach (var s in systems)
            {
                int fuel = m.Random.Range(pr.PriceRollMinPercent, pr.PriceRollMaxPercent);
                int food = m.Random.Range(pr.PriceRollMinPercent, pr.PriceRollMaxPercent);
                int water = m.Random.Range(pr.PriceRollMinPercent, pr.PriceRollMaxPercent);
                m._provisionRolls.Add(new ProvisionRoll(s.Id, fuel, food, water));
            }

            var y = tuning.Shipyards;
            foreach (var s in systems)
            {
                if (!s.HasShipyard) continue;
                var yard = new Shipyard(s.Id, m.YardRandom.Range(y.PriceRollMinPercent, y.PriceRollMaxPercent));
                foreach (var c in tuning.Ships) yard.Add(m.NewYardStock(YardItemKind.Ship, c.Id));
                foreach (var d in tuning.Drives) yard.Add(m.NewYardStock(YardItemKind.Drive, d.Id));
                m._yards.Add(yard);
            }

            return m;
        }

        /// <summary>A yard's stock of one item: its limit roll, starting full at the limit.</summary>
        private YardStock NewYardStock(YardItemKind kind, string itemId)
        {
            var stock = new YardStock(kind, itemId, 0, YardRandom.Range(0, 100));
            stock.Count = StockLimit(stock);
            return stock;
        }

        /// <summary>
        /// Switches to newly loaded tuning. Stored stock and rolls are kept; caps, rates, fares, and
        /// prices follow the new values from now on. Every commodity this market trades must still exist.
        /// </summary>
        public void ReplaceTuning(TuningData tuning)
        {
            if (tuning == null) throw new ArgumentNullException(nameof(tuning));
            var missing = MissingFromTuning(tuning);
            if (missing.Count > 0) throw new ArgumentException(missing[0], nameof(tuning));
            Tuning = tuning;
        }

        /// <summary>Every commodity, ship class, and drive this market uses that <paramref name="tuning"/> does not define, one message each.</summary>
        internal List<string> MissingFromTuning(TuningData tuning)
        {
            var missing = new List<string>();
            foreach (var id in _supplies.Concat(_demands).Select(x => x.CommodityId).Distinct())
                if (tuning.FindCommodity(id) == null)
                    missing.Add($"The market trades commodity \"{id}\", which the new tuning does not define.");
            foreach (var yard in _yards)
            {
                foreach (var s in yard.Ships)
                    if (tuning.FindShip(s.ItemId) == null)
                        missing.Add($"The shipyard at {SystemName(yard.SystemId)} stocks ship class \"{s.ItemId}\", which the new tuning does not define.");
                foreach (var s in yard.Drives)
                    if (tuning.FindDrive(s.ItemId) == null)
                        missing.Add($"The shipyard at {SystemName(yard.SystemId)} stocks drive \"{s.ItemId}\", which the new tuning does not define.");
            }
            return missing;
        }

        /// <summary>
        /// Rebuilds a saved market exactly: stock levels, rolls, both generators, the opening-rate hold, and every yard.
        /// Distances come from the stored galaxy. Throws <see cref="ArgumentException"/> when the parts do not fit together.
        /// </summary>
        internal static Market Restore(Galaxy galaxy, TuningData tuning, long createdTick, SeededRandom random, SeededRandom yardRandom, bool holdsOpeningRates,
            IEnumerable<CommodityStock> supplies, IEnumerable<CommodityStock> demands, IEnumerable<(int origin, string commodity, int destination, int roll)> offers,
            IEnumerable<(int origin, int destination, int waiting, int roll)> pools, IEnumerable<ProvisionRoll> provisionRolls, IEnumerable<Shipyard> yards,
            long maxJobDistanceMilliLy)
        {
            if (createdTick < 0) throw new ArgumentException($"The market's creation tick {createdTick} is negative.");
            if (maxJobDistanceMilliLy < 1) throw new ArgumentException($"The market's job distance limit {maxJobDistanceMilliLy} milli-ly is below 1.");
            var m = new Market(galaxy, tuning, createdTick, random, yardRandom, maxJobDistanceMilliLy) { HoldsOpeningRates = holdsOpeningRates };
            int count = galaxy.Systems.Count;
            void CheckSystem(int id, string what)
            {
                if (id < 0 || id >= count) throw new ArgumentException($"{what} names system {id}, but the galaxy has systems 0 to {count - 1}.");
            }
            void CheckTonnes(int tonnes, string what)
            {
                if (tonnes < 0) throw new ArgumentException($"{what} has {tonnes} t.");
            }

            foreach (var s in supplies) { CheckSystem(s.SystemId, "A supply"); CheckTonnes(s.Tonnes, $"Supply of {s.CommodityId} at system {s.SystemId}"); m.AddStock(m._supplies, m._supplyIndex, s); }
            foreach (var d in demands) { CheckSystem(d.SystemId, "A demand"); CheckTonnes(d.Tonnes, $"Demand for {d.CommodityId} at system {d.SystemId}"); m.AddStock(m._demands, m._demandIndex, d); }
            foreach (var o in offers)
            {
                CheckSystem(o.origin, "A freight offer");
                CheckSystem(o.destination, "A freight offer");
                if (m.FindSupply(o.origin, o.commodity) == null || m.FindDemand(o.destination, o.commodity) == null || o.origin == o.destination)
                    throw new ArgumentException($"The freight offer for {o.commodity} from system {o.origin} to {o.destination} has no matching supply and demand.");
                if (!m.WithinJobDistance(o.origin, o.destination))
                    throw new ArgumentException($"The freight offer for {o.commodity} from system {o.origin} to {o.destination} is beyond the game's job distance limit of {maxJobDistanceMilliLy} milli-ly.");
                m.AddOffer(new FreightOffer(o.origin, o.commodity, o.destination, galaxy.DistanceMilliLy(o.origin, o.destination), o.roll));
            }
            foreach (var s in m._supplies)
            foreach (var d in m._demands)
                if (d.CommodityId == s.CommodityId && m.WithinJobDistance(s.SystemId, d.SystemId) && m.FindOffer(s.SystemId, s.CommodityId, d.SystemId) == null)
                    throw new ArgumentException($"The freight offer for {s.CommodityId} from system {s.SystemId} to {d.SystemId} is missing, though it is within the game's job distance limit of {maxJobDistanceMilliLy} milli-ly.");
            foreach (var p in pools)
            {
                CheckSystem(p.origin, "A passenger pool");
                CheckSystem(p.destination, "A passenger pool");
                if (p.origin == p.destination) throw new ArgumentException($"A passenger pool goes from system {p.origin} to itself.");
                if (p.waiting < 0) throw new ArgumentException($"The passenger pool from system {p.origin} to {p.destination} has {p.waiting} waiting.");
                if (!m.WithinJobDistance(p.origin, p.destination))
                    throw new ArgumentException($"The passenger pool from system {p.origin} to {p.destination} is beyond the game's job distance limit of {maxJobDistanceMilliLy} milli-ly.");
                m.AddPool(new PassengerPool(p.origin, p.destination, galaxy.DistanceMilliLy(p.origin, p.destination), p.waiting, p.roll));
            }
            int pairs = 0;
            for (int a = 0; a < count; a++)
            for (int b = 0; b < count; b++)
                if (m.WithinJobDistance(a, b)) pairs++;
            if (m._pools.Count != pairs)
                throw new ArgumentException($"The market has {m._pools.Count} passenger pools; a galaxy of {count} systems with a job distance limit of {maxJobDistanceMilliLy} milli-ly needs {pairs}.");
            foreach (var r in provisionRolls)
            {
                if (r.SystemId != m._provisionRolls.Count) throw new ArgumentException($"Provision rolls must list systems 0 to {count - 1} in order; found system {r.SystemId} at position {m._provisionRolls.Count}.");
                m._provisionRolls.Add(r);
            }
            if (m._provisionRolls.Count != count) throw new ArgumentException($"The market has provision rolls for {m._provisionRolls.Count} systems; the galaxy has {count}.");
            foreach (var yard in yards)
            {
                CheckSystem(yard.SystemId, "A shipyard");
                if (!galaxy.Systems[yard.SystemId].HasShipyard) throw new ArgumentException($"System {yard.SystemId} has a shipyard entry but no shipyard.");
                if (m.FindShipyard(yard.SystemId) != null) throw new ArgumentException($"System {yard.SystemId} has two shipyard entries.");
                foreach (var s in yard.Ships.Concat(yard.Drives))
                    if (s.Count < 0 || s.LimitRollPercent < 0 || s.LimitRollPercent > 100)
                        throw new ArgumentException($"The shipyard at system {yard.SystemId} has {s.Count} of {s.ItemId} with limit roll {s.LimitRollPercent}.");
                m._yards.Add(yard);
            }
            foreach (var s in galaxy.Systems)
                if (s.HasShipyard && m.FindShipyard(s.Id) == null) throw new ArgumentException($"System {s.Id} has a shipyard but no shipyard entry.");
            return m;
        }

        // ---- Shipyards ----

        /// <summary>The shipyard at this system, or null if it has none.</summary>
        public Shipyard FindShipyard(int systemId) => _yards.Find(y => y.SystemId == systemId);

        /// <summary>The stock limit for this item, from its stored roll and the current tuning minimum and maximum.</summary>
        public int StockLimit(YardStock stock)
        {
            var y = Tuning.Shipyards;
            return stock.Kind == YardItemKind.Ship
                ? YardMath.StockLimit(y.ShipStockPerClassMin, y.ShipStockPerClassMax, stock.LimitRollPercent)
                : YardMath.StockLimit(y.DriveStockPerTierMin, y.DriveStockPerTierMax, stock.LimitRollPercent);
        }

        /// <summary>
        /// True when the yard sells this item: its stock limit is at least 1, or units are left over from
        /// before a lowered limit. Out of stock (none left now) still counts as stocking it.
        /// </summary>
        public bool Stocks(YardStock stock) => stock != null && (stock.Count > 0 || StockLimit(stock) >= 1);

        /// <summary>The price of a ship class or drive at this yard: the current tuning base price x the yard's price roll.</summary>
        public long Price(Shipyard yard, YardItemKind kind, string itemId)
        {
            if (yard == null) throw new ArgumentNullException(nameof(yard));
            long basePrice = kind == YardItemKind.Ship
                ? (Tuning.FindShip(itemId) ?? throw new ArgumentException($"\"{itemId}\" is not a ship class in the tuning.", nameof(itemId))).Price
                : (Tuning.FindDrive(itemId) ?? throw new ArgumentException($"\"{itemId}\" is not a drive in the tuning.", nameof(itemId))).Price;
            return YardMath.Price(basePrice, yard.PriceRollPercent);
        }

        /// <summary>What a ship of this class costs at this system's yard, or null if it has no yard or the yard does not sell the class.</summary>
        public long? ShipPriceAt(int systemId, string classId) => PriceAt(systemId, YardItemKind.Ship, classId);

        /// <summary>What this drive costs at this system's yard, or null if it has no yard or the yard does not stock the drive.</summary>
        public long? DrivePriceAt(int systemId, string driveId) => PriceAt(systemId, YardItemKind.Drive, driveId);

        private long? PriceAt(int systemId, YardItemKind kind, string itemId)
        {
            var yard = FindShipyard(systemId);
            if (yard == null || !Stocks(yard.Find(kind, itemId))) return null;
            return Price(yard, kind, itemId);
        }

        /// <summary>The first restock after <paramref name="afterTick"/>. Every yard restocks on the same schedule, counted from market creation.</summary>
        public long NextRestockTick(long afterTick) => NextBoundary(Tuning.Shipyards.RestockIntervalDays, afterTick);

        /// <summary>Takes one unit of an item the yard has in stock. The caller has checked it.</summary>
        internal void TakeOne(YardStock stock)
        {
            if (stock.Count <= 0) throw new InvalidOperationException($"No {stock.ItemId} left to take.");
            stock.Count--;
        }

        /// <summary>
        /// Every restock adds restockAmount of each item the yard stocks, never above its current limit.
        /// Stock already above a lowered limit is kept and simply stops growing.
        /// </summary>
        private void Restock(Shipyard yard)
        {
            int amount = Tuning.Shipyards.RestockAmount;
            foreach (var s in yard.Ships.Concat(yard.Drives))
                s.Count = Refill(s.Count, StockLimit(s), amount);
        }

        private void AddStock(List<CommodityStock> list, Dictionary<(int, string), CommodityStock> index, CommodityStock stock)
        {
            list.Add(stock);
            index.Add((stock.SystemId, stock.CommodityId), stock);
        }

        private void AddOffer(FreightOffer offer)
        {
            _offerIndex.Add((offer.OriginId, offer.CommodityId, offer.DestinationId), offer);
            _offers.Add(offer);
            if (!_offersByOrigin.TryGetValue(offer.OriginId, out var list)) _offersByOrigin.Add(offer.OriginId, list = new List<FreightOffer>());
            list.Add(offer);
        }

        private void AddPool(PassengerPool pool)
        {
            _poolIndex.Add((pool.OriginId, pool.DestinationId), pool);
            _pools.Add(pool);
            if (!_poolsByOrigin.TryGetValue(pool.OriginId, out var list)) _poolsByOrigin.Add(pool.OriginId, list = new List<PassengerPool>());
            list.Add(pool);
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

        /// <summary>Every freight offer from this system, in the same order as <see cref="Offers"/>.</summary>
        public IReadOnlyList<FreightOffer> OffersFrom(int originId) => _offersByOrigin.TryGetValue(originId, out var list) ? list : (IReadOnlyList<FreightOffer>)Array.Empty<FreightOffer>();

        /// <summary>Every passenger pool from this system, in the same order as <see cref="Pools"/>.</summary>
        public IReadOnlyList<PassengerPool> PoolsFrom(int originId) => _poolsByOrigin.TryGetValue(originId, out var list) ? list : (IReadOnlyList<PassengerPool>)Array.Empty<PassengerPool>();

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
            if (FindShipyard(systemId) != null) yield return Tuning.Shipyards.RestockIntervalDays;
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
        /// commodity (tuning order), then passenger pools, then the rate and fare re-roll, then the
        /// shipyard restock.
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

            if (IsBoundary(Tuning.Rates.RerollIntervalDays, tick) && !(HoldsOpeningRates && systemId == Galaxy.StartSystemId))
            {
                foreach (var offer in OffersFrom(systemId))
                {
                    var c = Commodity(offer.CommodityId);
                    offer.RateRollPercent = Random.Range(c.RateRollMinPercent, c.RateRollMaxPercent);
                }
                foreach (var pool in PoolsFrom(systemId))
                    pool.FareRollPercent = Random.Range(p.FareRollMinPercent, p.FareRollMaxPercent);
            }

            var yard = FindShipyard(systemId);
            if (yard != null && IsBoundary(Tuning.Shipyards.RestockIntervalDays, tick)) Restock(yard);
        }

        // ---- Opening offers ----

        internal void HoldOpeningRates() => HoldsOpeningRates = true;

        internal void ReleaseOpeningRates() => HoldsOpeningRates = false;

        /// <summary>
        /// Raises the roll of every freight offer and passenger pool from the start system by one
        /// percent, never above its tuning maximum. Returns false when every roll was already at its maximum.
        /// </summary>
        internal bool RaiseOpeningRolls()
        {
            int start = Galaxy.StartSystemId;
            bool raised = false;
            foreach (var offer in OffersFrom(start))
            {
                int max = Commodity(offer.CommodityId).RateRollMaxPercent;
                if (offer.RateRollPercent < max) { offer.RateRollPercent++; raised = true; }
            }
            foreach (var pool in PoolsFrom(start))
            {
                if (pool.FareRollPercent < Tuning.Passengers.FareRollMaxPercent) { pool.FareRollPercent++; raised = true; }
            }
            return raised;
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
            var problems = Check(request, out var lines, out var pool);
            if (problems.Count > 0) return CommandResult.Fail(string.Join(" ", problems));

            foreach (var line in lines)
            {
                FindSupply(request.OriginId, line.CommodityId).Tonnes -= line.Tonnes;
                FindDemand(request.DestinationId, line.CommodityId).Tonnes -= line.Tonnes;
            }
            if (pool != null) pool.Waiting -= request.Passengers;
            reservation = new MarketReservation(this, request.OriginId, request.DestinationId, lines, request.Passengers, pool == null ? 0 : FarePerPassenger(pool));
            return CommandResult.Ok();
        }

        /// <summary>
        /// Every reason <see cref="TryReserve"/> would refuse this request right now, with numbers.
        /// Empty when it would succeed. Changes nothing.
        /// </summary>
        public IReadOnlyList<string> CheckReservation(ReservationRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return Check(request, out _, out _);
        }

        private List<string> Check(ReservationRequest request, out List<ReservedFreight> lines, out PassengerPool pool)
        {
            lines = new List<ReservedFreight>();
            pool = null;
            int count = Galaxy.Systems.Count;
            if (request.OriginId < 0 || request.OriginId >= count) return new List<string> { $"There is no system with id {request.OriginId}." };
            if (request.DestinationId < 0 || request.DestinationId >= count) return new List<string> { $"There is no system with id {request.DestinationId}." };
            if (request.OriginId == request.DestinationId) return new List<string> { $"The origin and destination are both {SystemName(request.OriginId)}." };

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

            foreach (var c in Tuning.Commodities)
            {
                if (!wanted.TryGetValue(c.Id, out long tonnes)) continue;
                string name = c.Name.ToLowerInvariant();
                var supply = FindSupply(request.OriginId, c.Id);
                var demand = FindDemand(request.DestinationId, c.Id);
                if (supply == null) problems.Add($"{from} does not produce {name}.");
                if (demand == null) problems.Add($"{to} does not buy {name}.");
                if (supply == null || demand == null) continue;
                var offer = FindOffer(request.OriginId, c.Id, request.DestinationId);
                if (offer == null) { problems.Add($"{from} offers no {name} jobs to {to}: {BeyondJobDistance(request.OriginId, request.DestinationId)}"); continue; }
                int available = Math.Min(supply.Tonnes, demand.Tonnes);
                if (tonnes > available)
                    problems.Add($"Only {available} t of {name} available from {from} to {to} (supply {supply.Tonnes} t at {from}, demand {demand.Tonnes} t at {to}); requested {tonnes} t.");
                else
                    lines.Add(new ReservedFreight(c.Id, (int)tonnes, RatePerTonne(offer)));
            }

            pool = FindPool(request.OriginId, request.DestinationId);
            if (request.Passengers < 0) problems.Add($"The passenger count cannot be negative, got {request.Passengers}.");
            else if (pool == null && request.Passengers > 0) problems.Add($"{from} has no passenger bookings to {to}: {BeyondJobDistance(request.OriginId, request.DestinationId)}");
            else if (pool != null && request.Passengers > pool.Waiting) problems.Add($"Only {pool.Waiting} passengers waiting from {from} to {to}; requested {request.Passengers}.");

            return problems;
        }

        /// <summary>"it is 23.4 ly away, beyond the 21 ly job distance limit. An empty voyage can still go there."</summary>
        private string BeyondJobDistance(int fromId, int toId) =>
            $"it is {Format.LightYears(Galaxy.DistanceMilliLy(fromId, toId))} away, beyond the {Format.LightYears(MaxJobDistanceMilliLy)} job distance limit. An empty voyage can still go there.";

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
            var pool = FindPool(reservation.OriginId, reservation.DestinationId);
            if (pool != null) pool.Waiting += reservation.Passengers;
            reservation.IsReleased = true;
            return CommandResult.Ok();
        }

        /// <summary>Every stored value in a fixed text layout, for comparing market states exactly.</summary>
        public string DescribeState()
        {
            var sb = new StringBuilder();
            sb.Append("seed=").Append(Galaxy.Seed).Append(" start=").Append(Galaxy.StartSystemId)
              .Append(" created=").Append(CreatedTick).Append(" openingHeld=").Append(HoldsOpeningRates)
              .Append(" rng=").Append(Random.S0).Append(',').Append(Random.S1).Append(',').Append(Random.S2).Append(',').Append(Random.S3).Append('\n');
            sb.Append("joblimit ").Append(MaxJobDistanceMilliLy).Append('\n');
            foreach (var sys in Galaxy.Systems)
                sb.Append("system ").Append(sys.Id).Append(" \"").Append(sys.Name).Append("\" x=").Append(sys.XMilliLy).Append(" y=").Append(sys.YMilliLy)
                  .Append(" pop=").Append(sys.PopulationMillions).Append(" industries=[").Append(string.Join(",", sys.IndustryIds)).Append("] yard=").Append(sys.HasShipyard).Append('\n');
            foreach (var s in _supplies) sb.Append("supply ").Append(s.SystemId).Append(' ').Append(s.CommodityId).Append(' ').Append(s.Tonnes).Append('\n');
            foreach (var d in _demands) sb.Append("demand ").Append(d.SystemId).Append(' ').Append(d.CommodityId).Append(' ').Append(d.Tonnes).Append('\n');
            foreach (var o in _offers) sb.Append("offer ").Append(o.OriginId).Append(' ').Append(o.CommodityId).Append(' ').Append(o.DestinationId).Append(" roll=").Append(o.RateRollPercent).Append('\n');
            foreach (var p in _pools) sb.Append("pool ").Append(p.OriginId).Append(' ').Append(p.DestinationId).Append(" waiting=").Append(p.Waiting).Append(" roll=").Append(p.FareRollPercent).Append('\n');
            foreach (var r in _provisionRolls) sb.Append("provisions ").Append(r.SystemId).Append(' ').Append(r.FuelRollPercent).Append(' ').Append(r.FoodRollPercent).Append(' ').Append(r.WaterRollPercent).Append('\n');
            sb.Append("yards rng=").Append(YardRandom.S0).Append(',').Append(YardRandom.S1).Append(',').Append(YardRandom.S2).Append(',').Append(YardRandom.S3).Append('\n');
            foreach (var yard in _yards)
            {
                sb.Append("yard ").Append(yard.SystemId).Append(" priceRoll=").Append(yard.PriceRollPercent).Append('\n');
                foreach (var s in yard.Ships.Concat(yard.Drives))
                    sb.Append("yardstock ").Append(yard.SystemId).Append(' ').Append(s.Kind == YardItemKind.Ship ? "ship " : "drive ").Append(s.ItemId)
                      .Append(" count=").Append(s.Count).Append(" limitRoll=").Append(s.LimitRollPercent).Append('\n');
            }
            return sb.ToString();
        }
    }
}
