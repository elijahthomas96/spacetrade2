using System;
using System.Collections.Generic;
using System.Numerics;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Ships;
using SpaceTrade.Sim.Timing;
using SpaceTrade.Sim.Tuning;

namespace SpaceTrade.Sim.Voyages
{
    /// <summary>
    /// The single quote function (spec 2.7, 6.5). The preview and dispatch both go through
    /// <see cref="Simulation.Quote"/>, which calls this. It never changes state. A ship in transit
    /// cannot be quoted, so it cannot be provisioned or dispatched either.
    /// </summary>
    internal static class VoyageQuoter
    {
        public static VoyageQuote Quote(Simulation sim, VoyagePlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var q = new VoyageQuote(plan);
            var market = sim.Market;
            if (market == null) { q.AddProblem("There is no market yet."); return q; }

            var ship = sim.FindShip(plan.ShipId);
            if (ship == null) { q.AddProblem($"There is no ship with id {plan.ShipId}."); return q; }

            var t = market.Tuning;
            var shipClass = t.FindShip(ship.ClassId);
            var drive = t.FindDrive(ship.DriveId);
            if (shipClass == null) { q.AddProblem($"{ship.Name}'s class \"{ship.ClassId}\" is not in the tuning file."); return q; }
            if (drive == null) { q.AddProblem($"{ship.Name}'s drive \"{ship.DriveId}\" is not in the tuning file."); return q; }

            var systems = market.Galaxy.Systems;
            if (ship.Voyage != null)
            {
                q.AddProblem($"{ship.Name} is in transit to {systems[ship.Voyage.DestinationId].Name}, arriving {GameDate.FromTick(ship.Voyage.ArrivalTick)}. It can take a new voyage once it docks.");
                return q;
            }
            int origin = ship.DockedSystemId.Value;
            if (plan.DestinationId < 0 || plan.DestinationId >= systems.Count) { q.AddProblem($"There is no system with id {plan.DestinationId}."); return q; }
            if (plan.DestinationId == origin) { q.AddProblem($"{ship.Name} is already at {systems[origin].Name}. Choose another destination."); return q; }
            if (plan.Passengers < 0) { q.AddProblem($"The passenger count cannot be negative, got {plan.Passengers}."); return q; }

            q.IsPlanValid = true;
            q.OriginId = origin;
            q.DestinationId = plan.DestinationId;
            q.DistanceMilliLy = market.Galaxy.DistanceMilliLy(origin, plan.DestinationId);
            q.DepartureTick = sim.CurrentTick;

            // Freight merged per commodity in tuning order. Bad lines are reported by the market check below.
            var wanted = new Dictionary<string, long>();
            foreach (var line in plan.Freight)
            {
                if (line.CommodityId == null || t.FindCommodity(line.CommodityId) == null || line.Tonnes <= 0) continue;
                wanted.TryGetValue(line.CommodityId, out long sofar);
                wanted[line.CommodityId] = sofar + line.Tonnes;
            }
            long freightTonnes = 0;
            foreach (var c in t.Commodities)
            {
                if (!wanted.TryGetValue(c.Id, out long tonnes)) continue;
                var offer = market.FindOffer(origin, c.Id, plan.DestinationId);
                q.AddFreight(new QuotedFreight(c.Id, tonnes, offer == null ? 0 : market.RatePerTonne(offer)));
                freightTonnes += tonnes;
            }

            var needs = ProvisioningMath.Compute(t, shipClass, drive, q.DistanceMilliLy, freightTonnes, plan.Passengers, ship.Food.Kg, ship.Water.Kg);
            q.Needs = needs;
            q.TransitTicks = needs.TransitTicks;
            q.RealMillisecondsAtCurrentSpeed = GameTime.RealMillisecondsFor(needs.TransitTicks, sim.Clock.Speed);
            q.HoldCapacityKg = (long)shipClass.HoldTonnes * 1000;
            q.TankCapacityKg = (long)shipClass.TankTonnes * 1000;
            q.Berths = shipClass.Berths;

            var prices = market.ProvisionPricesAt(origin);
            q.AddSupply(PriceSupply(ship.Fuel, needs.FuelRequiredKg, prices.FuelPerTonne));
            q.AddSupply(PriceSupply(ship.Food, needs.FoodRequiredKg, prices.FoodPerTonne));
            q.AddSupply(PriceSupply(ship.Water, needs.WaterRequiredKg, prices.WaterPerTonne));
            long topUp = 0, used = 0;
            foreach (var s in q.Supplies) { topUp += s.TopUpCost; used += s.UsedCost; }
            q.TopUpCost = topUp;
            q.OperatingCost = used;
            q.CreditsAvailable = sim.Credits;

            long freightRevenue = 0;
            foreach (var f in q.Freight) freightRevenue += f.Revenue;
            q.FreightRevenue = freightRevenue;
            var pool = market.FindPool(origin, plan.DestinationId);
            q.FarePerPassenger = pool == null ? 0 : market.FarePerPassenger(pool);
            q.FareRevenue = plan.Passengers * q.FarePerPassenger;
            q.ProfitPerGameYear = IntMath.DivideRounded((BigInteger)q.NetProfit * GameTime.TicksPerYear, needs.TransitTicks);

            // Problems, each with its numbers and a fix.
            q.BerthsFit = plan.Passengers <= shipClass.Berths;
            if (!q.BerthsFit)
                q.AddProblem($"{ship.Name} has {shipClass.Berths} berths; {plan.Passengers} passengers requested. Book {FewerPassengers(plan.Passengers - shipClass.Berths)}.");

            q.HoldFits = needs.HoldUsedKg <= q.HoldCapacityKg;
            if (!q.HoldFits) q.AddProblem(HoldProblem(t, shipClass, drive, ship, q, needs));

            q.TankFits = needs.FuelRequiredKg <= q.TankCapacityKg;
            if (!q.TankFits) q.AddProblem(TankProblem(t, shipClass, drive, ship, q, needs));

            foreach (var problem in market.CheckReservation(new ReservationRequest(origin, plan.DestinationId, plan.Freight, plan.Passengers)))
                q.AddProblem(problem);

            q.CanAffordTopUp = topUp <= sim.Credits;
            if (!q.CanAffordTopUp)
            {
                long more = topUp - sim.Credits;
                q.AddProblem($"Topping up supplies costs {Format.Credits(topUp)} credits; you have {Format.Credits(sim.Credits)}. You need {Format.Credits(more)} more {(more == 1 ? "credit" : "credits")}.");
            }

            return q;
        }

        private static SupplyQuote PriceSupply(SupplyStock stock, long requiredKg, long pricePerTonne)
        {
            long shortfall = Math.Max(0, requiredKg - stock.Kg);
            long topUpCost = ProvisioningMath.PurchaseCost(shortfall, pricePerTonne);
            long usedCost = SupplyStock.CostOfUsing(stock.Kg + shortfall, stock.CostCredits + topUpCost, requiredKg);
            return new SupplyQuote(stock.Kind, stock.Kg, stock.CostCredits, requiredKg, shortfall, pricePerTonne, topUpCost, usedCost);
        }

        /// <summary>Smallest n in 1..max for which <paramref name="fits"/> is true, or -1. <paramref name="fits"/> must never turn false again as n grows.</summary>
        private static long SmallestThatFits(long max, Func<long, bool> fits)
        {
            if (max < 1 || !fits(max)) return -1;
            long lo = 1, hi = max;
            while (lo < hi)
            {
                long mid = lo + (hi - lo) / 2;
                if (fits(mid)) hi = mid; else lo = mid + 1;
            }
            return lo;
        }

        private static string FewerPassengers(long n) => n == 1 ? "1 fewer passenger" : $"{n} fewer passengers";

        /// <summary>
        /// The fix for a voyage that does not fit: unload the fewest tonnes of freight that makes it fit;
        /// if no amount of freight is enough, carry the fewest fewer passengers, then unload the fewest
        /// tonnes with that many passengers. Null when nothing the player can change makes it fit.
        /// </summary>
        private static string Fix(long freight, int passengers, Func<long, int, bool> fits, string purpose)
        {
            long unload = SmallestThatFits(freight, n => fits(freight - n, passengers));
            if (unload > 0) return $"Unload {unload} t of freight {purpose}.";
            long fewer = SmallestThatFits(passengers, m => fits(0, passengers - (int)m));
            if (fewer < 0) return null;
            int keptPassengers = passengers - (int)fewer;
            long unloadWithFewer = fits(freight, keptPassengers) ? 0 : SmallestThatFits(freight, n => fits(freight - n, keptPassengers));
            return unloadWithFewer == 0
                ? $"Carry {FewerPassengers(fewer)} {purpose}."
                : $"Carry {FewerPassengers(fewer)} and unload {unloadWithFewer} t of freight {purpose}.";
        }

        private static string HoldProblem(TuningData t, ShipClassTuning shipClass, DriveTuning drive, Ship ship, VoyageQuote q, VoyageNeeds needs)
        {
            long supplies = needs.FoodAboardAfterKg + needs.WaterAboardAfterKg;
            string text = $"The hold holds {Format.Tonnes(q.HoldCapacityKg)}; this voyage needs {Format.Tonnes(needs.FreightTonnes * 1000)} of freight plus {Format.Tonnes(supplies)} of food and water ({Format.Tonnes(needs.HoldUsedKg)}).";
            bool Fits(long freight, int passengers) =>
                ProvisioningMath.Compute(t, shipClass, drive, q.DistanceMilliLy, freight, passengers, ship.Food.Kg, ship.Water.Kg).HoldUsedKg <= q.HoldCapacityKg;
            string fix = Fix(needs.FreightTonnes, needs.Passengers, Fits, "to make room for food and water");
            if (fix != null) return text + " " + fix;
            var crewOnly = ProvisioningMath.Compute(t, shipClass, drive, q.DistanceMilliLy, 0, 0, ship.Food.Kg, ship.Water.Kg);
            return text + $" Even with no freight or passengers, food and water need {Format.Tonnes(crewOnly.HoldUsedKg)}, more than the hold holds; this destination is too far for {ship.Name}.";
        }

        private static string TankProblem(TuningData t, ShipClassTuning shipClass, DriveTuning drive, Ship ship, VoyageQuote q, VoyageNeeds needs)
        {
            string text = $"The fuel tank holds {Format.Tonnes(q.TankCapacityKg)}; this voyage needs {Format.Tonnes(needs.FuelRequiredKg)} of fuel.";
            bool Fits(long freight, int passengers) =>
                ProvisioningMath.Compute(t, shipClass, drive, q.DistanceMilliLy, freight, passengers, ship.Food.Kg, ship.Water.Kg).FuelRequiredKg <= q.TankCapacityKg;
            string fix = Fix(needs.FreightTonnes, needs.Passengers, Fits, "to bring the fuel needed within the tank");
            if (fix != null) return text + " " + fix;
            var crewOnly = ProvisioningMath.Compute(t, shipClass, drive, q.DistanceMilliLy, 0, 0, ship.Food.Kg, ship.Water.Kg);
            return text + $" Even with no freight or passengers it needs {Format.Tonnes(crewOnly.FuelRequiredKg)}; this destination is out of range for {ship.Name} with its current drive.";
        }
    }
}
