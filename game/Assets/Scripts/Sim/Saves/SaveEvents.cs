using System;
using SpaceTrade.Sim.Events;
using SpaceTrade.Sim.Markets;
using SpaceTrade.Sim.Routes;
using SpaceTrade.Sim.Ships;
using SpaceTrade.Sim.Voyages;

namespace SpaceTrade.Sim.Saves
{
    /// <summary>The four kinds of event a save can hold, by the name written in the file. Each is rebuilt from its ship or system id.</summary>
    internal static class SaveEvents
    {
        public static string TypeName(SimEvent ev)
        {
            switch (ev)
            {
                case ArrivalEvent _: return "arrival";
                case MarketEvent _: return "market";
                case InSystemIncomeEvent _: return "inSystemPay";
                case RouteCheckEvent _: return "routeCheck";
                default: throw new InvalidOperationException($"An event of type {ev.GetType().Name} cannot be saved.");
            }
        }

        /// <summary>The event for this name and id, or null for a name the game does not know.</summary>
        public static SimEvent Create(string typeName, int id)
        {
            switch (typeName)
            {
                case "arrival": return new ArrivalEvent(id);
                case "market": return new MarketEvent(id);
                case "inSystemPay": return new InSystemIncomeEvent(id);
                case "routeCheck": return new RouteCheckEvent(id);
                default: return null;
            }
        }
    }
}
