using System.Globalization;
using SpaceTrade.Sim;
using SpaceTrade.Sim.Ships;
using SpaceTrade.Sim.Voyages;

namespace SpaceTrade.Game.Map
{
    /// <summary>Text for a ship's panel and close-up, built from the simulation's own figures. No figure is worked out here.</summary>
    public static class ShipText
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        public static string ClassName(Simulation sim, Ship ship) => sim.Market.Tuning.FindShip(ship.ClassId)?.Name ?? ship.ClassId;

        public static string SystemName(Simulation sim, int systemId) => sim.Market.Galaxy.Systems[systemId].Name;

        public static string Status(Simulation sim, Ship ship, ShipPosition p) =>
            p.InTransit
                ? $"In transit from {SystemName(sim, p.OriginId.Value)} to {SystemName(sim, p.DestinationId.Value)}"
                : $"Docked at {SystemName(sim, p.DockedSystemId.Value)}";

        /// <summary>Hundredths of a percent as "37.21%". The simulation rounds progress down, so a ship in flight never shows 100.00%.</summary>
        public static string Percent(long hundredths) => $"{hundredths / 100}.{hundredths % 100:00}%";

        public static string Progress(Simulation sim, ShipPosition p) =>
            p.InTransit
                ? $"{Percent(p.ProgressHundredthsOfPercent)} · {Format.LightYears(p.TravelledMilliLy)} of {Format.LightYears(p.DistanceMilliLy)}"
                : $"{Percent(p.ProgressHundredthsOfPercent)} (docked at {SystemName(sim, p.DockedSystemId.Value)})";

        /// <summary>The in-game arrival date and the real time left at the current speed, or "Paused" (spec 3.4).</summary>
        /// The simulation works both out (<see cref="Simulation.EtaOf"/>); the screen only shows its words.
        public static string Eta(Simulation sim, int shipId) => sim.EtaOf(shipId).Describe();

        public static string Kg(long kg) => kg.ToString("N0", Invariant) + " kg";

        /// <summary>A drive rating in milli-c as "1.5c".</summary>
        public static string Speed(long milliC)
        {
            string fraction = (milliC % 1000).ToString("000", Invariant).TrimEnd('0');
            return fraction.Length == 0 ? $"{milliC / 1000}c" : $"{milliC / 1000}.{fraction}c";
        }

        /// <summary>The drive's name, with its rating added when the name does not already say it.</summary>
        public static string Drive(Simulation sim, Ship ship)
        {
            var d = sim.Market.Tuning.FindDrive(ship.DriveId);
            if (d == null) return ship.DriveId;
            string speed = Speed(d.SpeedMilliC);
            return d.Name.Contains(speed) ? d.Name : $"{d.Name} ({speed})";
        }

        public static string CommodityName(Simulation sim, string id)
        {
            foreach (var c in sim.Market.Tuning.Commodities) if (c.Id == id) return c.Name;
            return id;
        }
    }
}
