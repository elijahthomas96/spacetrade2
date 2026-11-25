using SpaceTrade.Sim.Events;

namespace SpaceTrade.Sim.Ships
{
    /// <summary>
    /// One full game day of in-system transport for one ship (spec 10). Pays the day's income, then
    /// schedules the next day. Switching the toggle off removes it from the queue.
    /// </summary>
    internal sealed class InSystemIncomeEvent : SimEvent
    {
        public int ShipId { get; }

        public InSystemIncomeEvent(int shipId)
        {
            ShipId = shipId;
        }

        public override EventPhase Phase => EventPhase.InSystemTransport;
        public override int EntityId => ShipId;

        public override void Apply(Simulation sim) => sim.PayInSystemDay(ShipId);
    }
}
