using SpaceTrade.Sim.Events;

namespace SpaceTrade.Sim.Routes
{
    /// <summary>A route ship's departure check (spec 8.4), in phase 4 by ship id.</summary>
    internal sealed class RouteCheckEvent : SimEvent
    {
        public int ShipId { get; }

        public RouteCheckEvent(int shipId)
        {
            ShipId = shipId;
        }

        public override EventPhase Phase => EventPhase.AutomatedDeparture;
        public override int EntityId => ShipId;

        public override void Apply(Simulation sim) => sim.RunScheduledRouteCheck(ShipId);
    }
}
