using System;
using System.Collections.Generic;
using System.Linq;
using SpaceTrade.Sim.Events;
using SpaceTrade.Sim.Json;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Routes;
using SpaceTrade.Sim.Ships;
using SpaceTrade.Sim.Voyages;
using SpaceTrade.Sim.Yards;

namespace SpaceTrade.Sim.Saves
{
    /// <summary>
    /// Writes the whole game state as save text (spec 11.1): the generated galaxy, clock, credits and running totals,
    /// every ship (id, name, class, drive, docked system, supplies at cost, in-system start tick, voyage with locked terms,
    /// route with its status), receipts, notifications, the market with every yard, and every pending event with its exact
    /// ordering number. Every number is a whole number.
    /// </summary>
    internal static class SaveWriter
    {
        public static string Write(Simulation sim, long savedUtcMs)
        {
            if (sim.Market == null) throw new InvalidOperationException("A game with no market cannot be saved.");
            var root = new JsonObjectBuilder()
                .Add("format", SaveFormat.Marker)
                .Add("version", SaveFormat.CurrentVersion)
                .Add("savedUtcMs", savedUtcMs)
                .Add("checksum", SaveFormat.ChecksumPlaceholder)
                .Add("game", Game(sim));
            return SaveFormat.Seal(root.ToString());
        }

        private static JsonObjectBuilder Game(Simulation sim) => new JsonObjectBuilder()
            .Add("clock", new JsonObjectBuilder()
                .Add("tick", sim.Clock.Tick)
                .Add("speed", (int)sim.Clock.Speed)
                .Add("paused", sim.Clock.IsPaused))
            .Add("credits", sim.Credits)
            .Add("suppliesBought", sim.SuppliesBoughtTotal)
            .Add("inSystemIncome", sim.InSystemIncomeTotal)
            .Add("nextShipId", sim.NextShipId)
            .Add("galaxy", Galaxy(sim.Market))
            .Add("market", Market(sim.Market))
            .Add("ships", sim.Ships.Select(Ship).ToList())
            .Add("deliveryTotals", Totals(sim.DeliveryTotals))
            .Add("receipts", sim.Receipts.Select(Receipt).ToList())
            .Add("notifications", sim.Notifications.Select(n => new JsonObjectBuilder()
                .Add("tick", n.Tick)
                .Add("kind", n.Kind.ToString())
                .Add("ship", n.ShipId)
                .Add("text", n.Text)).ToList())
            .Add("events", Events(sim.Events));

        private static JsonObjectBuilder Galaxy(Market m) => new JsonObjectBuilder()
            .Add("seed", m.Galaxy.Seed)
            .Add("startSystem", m.Galaxy.StartSystemId)
            .Add("systems", m.Galaxy.Systems.Select(s => new JsonObjectBuilder()
                .Add("id", s.Id)
                .Add("name", s.Name)
                .Add("xMilliLy", s.XMilliLy)
                .Add("yMilliLy", s.YMilliLy)
                .Add("populationMillions", s.PopulationMillions)
                .Add("industries", s.IndustryIds)
                .Add("shipyard", s.HasShipyard)).ToList());

        private static List<string> RandomState(SeededRandom r) => new List<string> { r.S0.ToString("x16"), r.S1.ToString("x16"), r.S2.ToString("x16"), r.S3.ToString("x16") };

        private static JsonObjectBuilder Market(Market m) => new JsonObjectBuilder()
            .Add("createdTick", m.CreatedTick)
            .Add("maxJobDistanceMilliLy", m.MaxJobDistanceMilliLy)
            .Add("random", RandomState(m.Random))
            .Add("yardRandom", RandomState(m.YardRandom))
            .Add("holdsOpeningRates", m.HoldsOpeningRates)
            .Add("supplies", m.Supplies.Select(Stock).ToList())
            .Add("demands", m.Demands.Select(Stock).ToList())
            .Add("offers", m.Offers.Select(o => new JsonObjectBuilder()
                .Add("origin", o.OriginId)
                .Add("commodity", o.CommodityId)
                .Add("destination", o.DestinationId)
                .Add("rollPercent", o.RateRollPercent)).ToList())
            .Add("pools", m.Pools.Select(p => new JsonObjectBuilder()
                .Add("origin", p.OriginId)
                .Add("destination", p.DestinationId)
                .Add("waiting", p.Waiting)
                .Add("rollPercent", p.FareRollPercent)).ToList())
            .Add("provisionRolls", m.ProvisionRolls.Select(r => new JsonObjectBuilder()
                .Add("system", r.SystemId)
                .Add("fuelPercent", r.FuelRollPercent)
                .Add("foodPercent", r.FoodRollPercent)
                .Add("waterPercent", r.WaterRollPercent)).ToList())
            .Add("shipyards", m.Shipyards.Select(y => new JsonObjectBuilder()
                .Add("system", y.SystemId)
                .Add("priceRollPercent", y.PriceRollPercent)
                .Add("ships", y.Ships.Select(YardItem).ToList())
                .Add("drives", y.Drives.Select(YardItem).ToList())).ToList());

        private static JsonObjectBuilder Stock(CommodityStock s) => new JsonObjectBuilder()
            .Add("system", s.SystemId)
            .Add("commodity", s.CommodityId)
            .Add("tonnes", s.Tonnes);

        private static JsonObjectBuilder YardItem(YardStock s) => new JsonObjectBuilder()
            .Add("id", s.ItemId)
            .Add("count", s.Count)
            .Add("limitRollPercent", s.LimitRollPercent);

        private static JsonObjectBuilder Supply(SupplyStock s) => new JsonObjectBuilder()
            .Add("kg", s.Kg)
            .Add("cost", s.CostCredits);

        private static JsonObjectBuilder Ship(Ship s)
        {
            var b = new JsonObjectBuilder()
                .Add("id", s.Id)
                .Add("name", s.Name)
                .Add("class", s.ClassId)
                .Add("drive", s.DriveId);
            if (s.DockedSystemId.HasValue) b.Add("dockedSystem", s.DockedSystemId.Value);
            b.Add("fuel", Supply(s.Fuel)).Add("food", Supply(s.Food)).Add("water", Supply(s.Water));
            if (s.InSystemSinceTick.HasValue) b.Add("inSystemSinceTick", s.InSystemSinceTick.Value);
            if (s.Voyage != null) b.Add("voyage", Voyage(s.Voyage));
            if (s.Route != null) b.Add("route", Route(s.Route));
            return b;
        }

        private static List<JsonObjectBuilder> Freight(IEnumerable<LockedFreight> freight) => freight.Select(f => new JsonObjectBuilder()
            .Add("commodity", f.CommodityId)
            .Add("tonnes", f.Tonnes)
            .Add("ratePerTonne", f.RatePerTonne)).ToList();

        private static JsonObjectBuilder Voyage(Voyage v) => new JsonObjectBuilder()
            .Add("origin", v.OriginId)
            .Add("destination", v.DestinationId)
            .Add("distanceMilliLy", v.DistanceMilliLy)
            .Add("drive", v.DriveId)
            .Add("departureTick", v.DepartureTick)
            .Add("arrivalTick", v.ArrivalTick)
            .Add("freight", Freight(v.Freight))
            .Add("passengers", v.Passengers)
            .Add("farePerPassenger", v.FarePerPassenger)
            .Add("fuelUseKg", v.FuelUseKg)
            .Add("foodUseKg", v.FoodUseKg)
            .Add("waterUseKg", v.WaterUseKg);

        private static JsonObjectBuilder Route(ShipRoute r)
        {
            var b = new JsonObjectBuilder()
                .Add("paused", r.IsPaused)
                .Add("currentStop", r.CurrentStopIndex)
                .Add("status", r.Status.ToString());
            if (r.StatusText != null) b.Add("statusText", r.StatusText);
            b.Add("blockedForTopUp", r.BlockedForTopUp);
            if (r.LastCheckTick.HasValue) b.Add("lastCheckTick", r.LastCheckTick.Value);
            if (r.PendingCheckTick.HasValue) b.Add("pendingCheckTick", r.PendingCheckTick.Value);
            b.Add("stops", r.Stops.Select(s => new JsonObjectBuilder()
                .Add("system", s.SystemId)
                .Add("freight", s.Freight)
                .Add("carryPassengers", s.CarryPassengers)
                .Add("autoProvision", s.AutoProvision)
                .Add("minFreightTonnes", s.MinFreightTonnes)
                .Add("minPassengers", s.MinPassengers)
                .Add("emptyLeg", s.EmptyLeg)).ToList());
            return b;
        }

        internal static JsonObjectBuilder Totals(DeliveryTotals t) => new JsonObjectBuilder()
            .Add("deliveries", t.Deliveries)
            .Add("freightTonnes", t.FreightTonnes)
            .Add("passengers", t.Passengers)
            .Add("freightRevenue", t.FreightRevenue)
            .Add("fareRevenue", t.FareRevenue)
            .Add("fuelKg", t.FuelKg)
            .Add("fuelCost", t.FuelCost)
            .Add("foodKg", t.FoodKg)
            .Add("foodCost", t.FoodCost)
            .Add("waterKg", t.WaterKg)
            .Add("waterCost", t.WaterCost);

        private static JsonObjectBuilder Receipt(VoyageReceipt r) => new JsonObjectBuilder()
            .Add("ship", r.ShipId)
            .Add("shipName", r.ShipName)
            .Add("origin", r.OriginId)
            .Add("destination", r.DestinationId)
            .Add("departureTick", r.DepartureTick)
            .Add("arrivalTick", r.ArrivalTick)
            .Add("freight", Freight(r.Freight))
            .Add("passengers", r.Passengers)
            .Add("farePerPassenger", r.FarePerPassenger)
            .Add("freightRevenue", r.FreightRevenue)
            .Add("fareRevenue", r.FareRevenue)
            .Add("supplies", r.Supplies.Select(s => new JsonObjectBuilder()
                .Add("kind", s.Kind.ToString())
                .Add("kg", s.Kg)
                .Add("cost", s.Cost)).ToList());

        private static JsonObjectBuilder Events(EventQueue q) => new JsonObjectBuilder()
            .Add("nextSequence", q.NextSequence)
            .Add("pending", q.Pending.Select(e => new JsonObjectBuilder()
                .Add("tick", e.Tick)
                .Add("type", SaveEvents.TypeName(e.Event))
                .Add("id", e.Event.EntityId)
                .Add("sequence", e.Sequence)).ToList());
    }
}
