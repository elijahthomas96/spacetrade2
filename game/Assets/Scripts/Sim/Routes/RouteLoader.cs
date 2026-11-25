using System;
using System.Collections.Generic;
using System.Linq;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Ships;
using SpaceTrade.Sim.Tuning;

namespace SpaceTrade.Sim.Routes
{
    /// <summary>What a stop would load now, and why it cannot leave if it cannot.</summary>
    internal sealed class RouteLoad
    {
        /// <summary>Set when the leg cannot be flown even with no freight or passengers.</summary>
        public string CannotFlyReason;

        /// <summary>Set when a minimum can never be met.</summary>
        public string NeverReason;

        public int Passengers;
        public readonly List<FreightLine> Freight = new List<FreightLine>();
        public long FreightTonnes;
        public long UnmetFreightTonnes;
        public int UnmetPassengers;

        public bool MeetsMinimums => UnmetFreightTonnes == 0 && UnmetPassengers == 0;
    }

    /// <summary>
    /// Route validation and the loading rules (spec 8.2, 8.3), shared by the departure check and the
    /// whole-circuit estimate. Fitting uses the same provisioning math as the quote.
    /// </summary>
    internal static class RouteLoader
    {
        public static List<string> Validate(Market market, IReadOnlyList<RouteStop> stops)
        {
            var problems = new List<string>();
            if (stops == null || stops.Count < 2)
            {
                problems.Add($"A route needs at least 2 stops; this one has {stops?.Count ?? 0}.");
                return problems;
            }
            var systems = market.Galaxy.Systems;
            for (int i = 0; i < stops.Count; i++)
            {
                if (stops[i] == null) problems.Add($"Stop {i + 1} is missing.");
                else if (stops[i].SystemId < 0 || stops[i].SystemId >= systems.Count) problems.Add($"Stop {i + 1}: there is no system with id {stops[i].SystemId}.");
            }
            if (problems.Count > 0) return problems;

            for (int i = 0; i < stops.Count; i++)
            {
                int n = (i + 1) % stops.Count;
                if (stops[i].SystemId == stops[n].SystemId)
                    problems.Add($"Stops {i + 1} and {n + 1} are both {systems[stops[i].SystemId].Name}; each leg must go to a different system.");
            }

            var t = market.Tuning;
            for (int i = 0; i < stops.Count; i++)
            {
                var stop = stops[i];
                var next = stops[(i + 1) % stops.Count];
                string here = systems[stop.SystemId].Name;
                string there = systems[next.SystemId].Name;
                string label = $"Stop {i + 1} ({here})";
                if (stop.MinFreightTonnes < 0) problems.Add($"{label}: the freight minimum cannot be negative, got {stop.MinFreightTonnes} t.");
                if (stop.MinPassengers < 0) problems.Add($"{label}: the passenger minimum cannot be negative, got {stop.MinPassengers}.");

                if (stop.EmptyLeg)
                {
                    if (stop.Freight.Count > 0 || stop.CarryPassengers || stop.MinFreightTonnes > 0 || stop.MinPassengers > 0)
                        problems.Add($"{label} is marked as an empty leg to {there}, so it cannot list freight, carry passengers, or have a minimum.");
                    continue;
                }
                if (stop.Freight.Count == 0 && !stop.CarryPassengers)
                    problems.Add($"{label} lists no freight and does not carry passengers. Mark the leg to {there} as an empty repositioning leg, or choose freight or passengers.");

                var seen = new HashSet<string>();
                foreach (var id in stop.Freight)
                {
                    var c = id == null ? null : t.FindCommodity(id);
                    if (c == null) { problems.Add($"{label}: \"{id}\" is not a commodity."); continue; }
                    string name = c.Name.ToLowerInvariant();
                    if (!seen.Add(id)) { problems.Add($"{label} lists {name} more than once."); continue; }
                    if (market.FindSupply(stop.SystemId, id) == null) problems.Add($"{label}: {here} does not produce {name}.");
                    if (market.FindDemand(next.SystemId, id) == null) problems.Add($"{label}: {there} does not buy {name}.");
                }
                if (stop.MinFreightTonnes > 0 && stop.Freight.Count == 0)
                    problems.Add($"{label} has a freight minimum of {stop.MinFreightTonnes} t but lists no freight.");
                if (stop.MinPassengers > 0 && !stop.CarryPassengers)
                    problems.Add($"{label} has a passenger minimum of {stop.MinPassengers} but does not carry passengers.");
            }
            return problems;
        }

        /// <summary>
        /// What <paramref name="stop"/> loads now for the leg to <paramref name="nextSystemId"/>: passengers first, up
        /// to berths and bookings but never so many that the freight minimum could no longer fit; then each eligible
        /// commodity in listed order, as many tonnes as fit and are available. Fitting is the quote's hold and tank
        /// check, with the food and water already aboard. The stop must be valid.
        /// </summary>
        public static RouteLoad Load(Market market, string shipName, ShipClassTuning shipClass, DriveTuning drive, RouteStop stop, int nextSystemId,
            long foodAboardKg, long waterAboardKg)
        {
            var t = market.Tuning;
            var systems = market.Galaxy.Systems;
            int origin = stop.SystemId;
            string from = systems[origin].Name, to = systems[nextSystemId].Name;
            long distance = market.Galaxy.DistanceMilliLy(origin, nextSystemId);
            long holdKg = (long)shipClass.HoldTonnes * 1000, tankKg = (long)shipClass.TankTonnes * 1000;
            VoyageNeeds Needs(long freight, int passengers) => ProvisioningMath.Compute(t, shipClass, drive, distance, freight, passengers, foodAboardKg, waterAboardKg);
            bool Fits(long freight, int passengers)
            {
                var n = Needs(freight, passengers);
                return n.HoldUsedKg <= holdKg && n.FuelRequiredKg <= tankKg;
            }

            var load = new RouteLoad();
            if (!Fits(0, 0))
            {
                var empty = Needs(0, 0);
                load.CannotFlyReason = $"{shipName} cannot fly from {from} to {to} ({Format.LightYears(distance)}) even with no freight or passengers: it needs {Format.Tonnes(empty.FuelRequiredKg)} of fuel " +
                    $"(tank {Format.Tonnes(tankKg)}) and {Format.Tonnes(empty.FoodAboardAfterKg + empty.WaterAboardAfterKg)} of food and water (hold {Format.Tonnes(holdKg)}). This leg is out of range with its current drive.";
                return load;
            }
            if (stop.EmptyLeg) return load;

            var pool = market.FindPool(origin, nextSystemId);
            int berths = shipClass.Berths;
            long freightCap = 0;
            foreach (var id in stop.Freight)
                freightCap += Math.Min(market.SupplyCap(market.FindSupply(origin, id)), market.DemandCap(market.FindDemand(nextSystemId, id)));
            if (stop.MinPassengers > berths)
                load.NeverReason = $"Stop at {from} needs at least {stop.MinPassengers} passengers to leave, but {shipName} has {berths} berths.";
            else if (stop.MinPassengers > market.PoolCap(pool))
                load.NeverReason = $"Stop at {from} needs at least {stop.MinPassengers} passengers to leave, but at most {market.PoolCap(pool)} can ever be waiting from {from} to {to}.";
            else if (stop.MinFreightTonnes > freightCap)
                load.NeverReason = $"Stop at {from} needs at least {stop.MinFreightTonnes} t of freight to leave, but supply and demand can never hold more than {freightCap} t of {FreightNames(t, stop.Freight)} from {from} to {to}.";
            else if (!Fits(stop.MinFreightTonnes, stop.MinPassengers))
                load.NeverReason = $"Stop at {from} needs at least {stop.MinFreightTonnes} t of freight and {stop.MinPassengers} passengers to leave, but that does not fit {shipName}'s hold and tank for the leg to {to}.";

            long reserved = load.NeverReason == null ? stop.MinFreightTonnes : 0;
            int maxPassengers = stop.CarryPassengers ? Math.Min(berths, pool.Waiting) : 0;
            load.Passengers = (int)Largest(maxPassengers, p => Fits(reserved, (int)p));

            foreach (var id in stop.Freight)
            {
                long available = market.TonnesAvailable(market.FindOffer(origin, id, nextSystemId));
                long loaded = load.FreightTonnes;
                long tonnes = Largest(available, x => Fits(loaded + x, load.Passengers));
                if (tonnes <= 0) continue;
                load.Freight.Add(new FreightLine(id, (int)tonnes));
                load.FreightTonnes += tonnes;
            }

            load.UnmetFreightTonnes = Math.Max(0, stop.MinFreightTonnes - load.FreightTonnes);
            load.UnmetPassengers = Math.Max(0, stop.MinPassengers - load.Passengers);
            return load;
        }

        /// <summary>Largest n in 0..max for which <paramref name="fits"/> is true. <paramref name="fits"/>(0) must be true and must never turn true again once false.</summary>
        private static long Largest(long max, Func<long, bool> fits)
        {
            if (max <= 0) return 0;
            if (fits(max)) return max;
            long lo = 0, hi = max;
            while (hi - lo > 1)
            {
                long mid = lo + (hi - lo) / 2;
                if (fits(mid)) lo = mid; else hi = mid;
            }
            return lo;
        }

        /// <summary>"Waiting for 12 more tonnes of ore and 3 more passengers" (spec 8.3).</summary>
        public static string WaitingText(TuningData t, RouteStop stop, long unmetFreightTonnes, int unmetPassengers)
        {
            var parts = new List<string>();
            if (unmetFreightTonnes > 0)
            {
                string names = stop.Freight.Count == 1 ? FreightNames(t, stop.Freight) : $"freight ({FreightNames(t, stop.Freight)})";
                parts.Add($"{unmetFreightTonnes} more {(unmetFreightTonnes == 1 ? "tonne" : "tonnes")} of {names}");
            }
            if (unmetPassengers > 0) parts.Add($"{unmetPassengers} more {(unmetPassengers == 1 ? "passenger" : "passengers")}");
            return "Waiting for " + string.Join(" and ", parts);
        }

        /// <summary>"ore", "ore or metals", "ore, metals, or chemicals".</summary>
        private static string FreightNames(TuningData t, IReadOnlyList<string> ids)
        {
            var names = ids.Select(id => t.FindCommodity(id).Name.ToLowerInvariant()).ToList();
            if (names.Count <= 1) return string.Join("", names);
            if (names.Count == 2) return names[0] + " or " + names[1];
            return string.Join(", ", names.Take(names.Count - 1)) + ", or " + names[names.Count - 1];
        }
    }
}
