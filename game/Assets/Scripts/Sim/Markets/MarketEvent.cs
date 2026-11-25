using SpaceTrade.Sim.Events;

namespace SpaceTrade.Sim.Markets
{
    /// <summary>
    /// One system's market replenishment and re-roll. Runs whatever is due at its tick, then
    /// schedules itself for that system's next due tick.
    /// </summary>
    internal sealed class MarketEvent : SimEvent
    {
        private readonly int _systemId;

        public MarketEvent(int systemId)
        {
            _systemId = systemId;
        }

        public override EventPhase Phase => EventPhase.MarketReplenishment;
        public override int EntityId => _systemId;

        public override void Apply(Simulation sim)
        {
            sim.Market.RunDue(_systemId, sim.CurrentTick);
            sim.Schedule(sim.Market.NextEventTick(_systemId, sim.CurrentTick), new MarketEvent(_systemId));
        }
    }
}
