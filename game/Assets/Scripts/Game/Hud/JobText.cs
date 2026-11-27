using System.Collections.Generic;
using SpaceTrade.Game.Map;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Ships;
using SpaceTrade.Sim.Voyages;

namespace SpaceTrade.Game.Hud
{
    /// <summary>
    /// The words of the job lists, the planner's quote, and the receipt. Every figure is read from the simulation (the
    /// market, a job board, a quote, a receipt) and written with the simulation's own number formats; nothing is worked
    /// out here.
    /// </summary>
    public static class JobText
    {
        public static string Cargo(Simulation sim, string commodityId) => commodityId == null ? "Passengers" : ShipText.CommodityName(sim, commodityId);

        public static string SupplyName(SupplyKind kind) => kind.ToString();

        /// <summary>A whole number with thousands separators, the way the simulation writes them.</summary>
        public static string N(long n) => Format.Credits(n);

        /// <summary>The jobs a port offers, for the system view: no ship, so no profit, only the port's own figures.</summary>
        public static List<JobRow> PortRows(Simulation sim, int systemId)
        {
            var rows = new List<JobRow>();
            var market = sim.Market;
            foreach (var offer in market.OffersFrom(systemId))
            {
                rows.Add(new JobRow
                {
                    Title = $"{Cargo(sim, offer.CommodityId)} to {ShipText.SystemName(sim, offer.DestinationId)}",
                    Detail = $"{Format.LightYears(offer.DistanceMilliLy)} · {N(market.TonnesAvailable(offer))} t waiting",
                    Figure = $"{Format.Credits(market.RatePerTonne(offer))} per tonne",
                    DestinationId = offer.DestinationId,
                    CommodityId = offer.CommodityId,
                    DistanceMilliLy = offer.DistanceMilliLy,
                    Rate = market.RatePerTonne(offer),
                });
            }
            foreach (var pool in market.PoolsFrom(systemId))
            {
                rows.Add(new JobRow
                {
                    Title = $"Passengers to {ShipText.SystemName(sim, pool.DestinationId)}",
                    Detail = $"{Format.LightYears(pool.DistanceMilliLy)} · {N(pool.Waiting)} waiting",
                    Figure = $"{Format.Credits(market.FarePerPassenger(pool))} each",
                    DestinationId = pool.DestinationId,
                    IsPassengers = true,
                    DistanceMilliLy = pool.DistanceMilliLy,
                    Rate = market.FarePerPassenger(pool),
                });
            }
            return rows;
        }

        /// <summary>A ship's job board as rows: each job with the quote for the largest load the ship can take, or why it cannot fly it.</summary>
        public static List<JobRow> BoardRows(Simulation sim, JobBoard board)
        {
            var rows = new List<JobRow>();
            foreach (var e in board.Entries)
            {
                string unit = e.IsPassengerJob ? "passengers" : "t";
                string rate = e.IsPassengerJob ? $"{Format.Credits(e.Rate)} each" : $"{Format.Credits(e.Rate)} per tonne";
                string distance = Format.LightYears(e.DistanceMilliLy);
                rows.Add(new JobRow
                {
                    Title = $"{Cargo(sim, e.CommodityId)} to {ShipText.SystemName(sim, e.DestinationId)}",
                    Detail = e.CanFly
                        ? $"{distance} · {N(e.LargestLoad)} of {N(e.Available)} {unit} at {rate} · {Format.GameDuration(e.TransitTicks)}"
                        : $"{distance} · Cannot fly: {e.Reason}",
                    Figure = e.CanFly ? $"Net {Format.Credits(e.NetProfit)}" : "Cannot fly",
                    FigureIsBad = !e.CanFly || e.NetProfit < 0,
                    CanFly = e.CanFly,
                    DestinationId = e.DestinationId,
                    CommodityId = e.CommodityId,
                    IsPassengers = e.IsPassengerJob,
                    DistanceMilliLy = e.DistanceMilliLy,
                    NetProfit = e.NetProfit,
                    TransitTicks = e.TransitTicks,
                    Rate = e.Rate,
                    Entry = e,
                });
            }
            return rows;
        }

        /// <summary>Every other system as a destination for an empty voyage, with its distance.</summary>
        public static List<JobRow> DestinationRows(Simulation sim, int originId)
        {
            var rows = new List<JobRow>();
            var galaxy = sim.Market.Galaxy;
            foreach (var s in galaxy.Systems)
            {
                if (s.Id == originId) continue;
                long distance = galaxy.DistanceMilliLy(originId, s.Id);
                rows.Add(new JobRow
                {
                    Title = s.Name,
                    Detail = $"{Format.LightYears(distance)} · empty voyage, earns nothing",
                    Figure = "",
                    DestinationId = s.Id,
                    DistanceMilliLy = distance,
                });
            }
            return rows;
        }

        public static string SupplyLine(SupplyQuote s) =>
            $"{SupplyName(s.Kind)}: {Format.Tonnes(s.AboardKg)} aboard · needs {Format.Tonnes(s.RequiredKg)} · short {Format.Tonnes(s.ShortfallKg)} · top-up {Format.Credits(s.TopUpCost)} · uses {Format.Credits(s.UsedCost)}";

        public static string TopUpLine(VoyageQuote q) =>
            $"Top-up to buy: {Format.Credits(q.TopUpCost)} credits (you have {Format.Credits(q.CreditsAvailable)})";

        public static string RevenueLine(long freightRevenue, long fareRevenue, long grossRevenue) =>
            $"Revenue: freight {Format.Credits(freightRevenue)} + fares {Format.Credits(fareRevenue)} = {Format.Credits(grossRevenue)}";

        public static string SuppliesCostLine(long cost) => $"Supplies used, at cost: {Format.Credits(cost)}";

        public static string NetLine(VoyageQuote q) =>
            $"Net profit: {Format.Credits(q.NetProfit)} · {Format.Credits(q.ProfitPerGameYear)} per game year";

        public static string TripLine(Simulation sim, VoyageQuote q) =>
            $"{ShipText.SystemName(sim, q.OriginId)} to {ShipText.SystemName(sim, q.DestinationId)} · {Format.LightYears(q.DistanceMilliLy)} · {Format.GameDuration(q.TransitTicks)} in transit";

        public static string ConfirmQuestion(VoyageQuote q) =>
            $"Buy supplies for {Format.Credits(q.TopUpCost)} credits and launch?";

        /// <summary>
        /// Every term of a quote in one fixed line of text, for telling whether two quotes are the same deal. The clock
        /// (departure and arrival ticks) and the player's balance are left out: time passing and money arriving do not
        /// change a deal, and if the balance stops covering the top-up the quote's problems say so.
        /// </summary>
        public static string Terms(VoyageQuote q)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(q.IsPlanValid).Append('|').Append(q.Plan.ShipId).Append('>').Append(q.Plan.DestinationId).Append('|').Append(q.Plan.Passengers);
            foreach (var p in q.Problems) sb.Append("|problem:").Append(p);
            if (!q.IsPlanValid) return sb.ToString();
            sb.Append('|').Append(q.OriginId).Append('>').Append(q.DestinationId).Append('|').Append(q.DistanceMilliLy).Append('|').Append(q.TransitTicks);
            foreach (var f in q.Freight) sb.Append("|freight:").Append(f.CommodityId).Append(' ').Append(f.Tonnes).Append('@').Append(f.RatePerTonne);
            sb.Append("|fare:").Append(q.FarePerPassenger).Append("|revenue:").Append(q.FreightRevenue).Append('+').Append(q.FareRevenue);
            foreach (var s in q.Supplies)
                sb.Append('|').Append(s.Kind).Append(':').Append(s.AboardKg).Append('/').Append(s.AboardCost).Append(" needs ").Append(s.RequiredKg)
                  .Append(" short ").Append(s.ShortfallKg).Append('@').Append(s.PricePerTonne).Append('=').Append(s.TopUpCost).Append(" uses ").Append(s.UsedCost);
            sb.Append("|topup:").Append(q.TopUpCost).Append("|cost:").Append(q.OperatingCost).Append("|net:").Append(q.NetProfit).Append("|perYear:").Append(q.ProfitPerGameYear);
            return sb.ToString();
        }

        public static string ReceiptRoute(Simulation sim, VoyageReceipt r) =>
            $"{r.ShipName} · {ShipText.SystemName(sim, r.OriginId)} to {ShipText.SystemName(sim, r.DestinationId)}";

        public static string ReceiptDates(VoyageReceipt r) =>
            $"Departed {SpaceTrade.Sim.Timing.GameDate.FromTick(r.DepartureTick)} · arrived {SpaceTrade.Sim.Timing.GameDate.FromTick(r.ArrivalTick)} · {Format.GameDuration(r.ElapsedTicks)} in transit";

        public static string FreightLine(Simulation sim, string commodityId, long tonnes, long ratePerTonne, long revenue) =>
            $"{Cargo(sim, commodityId)}: {N(tonnes)} t at {Format.Credits(ratePerTonne)} per tonne = {Format.Credits(revenue)}";

        public static string FaresLine(long passengers, long fare, long revenue) =>
            $"Passengers: {N(passengers)} at {Format.Credits(fare)} each = {Format.Credits(revenue)}";

        public static string UsedLine(SupplyKind kind, long kg, long cost) =>
            $"{SupplyName(kind)}: {Format.Tonnes(kg)} used, {Format.Credits(cost)} at cost";

        public static string ReceiptNet(VoyageReceipt r) => $"Net profit: {Format.Credits(r.NetProfit)}";
    }
}
