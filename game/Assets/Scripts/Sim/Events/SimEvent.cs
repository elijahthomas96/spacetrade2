namespace SpaceTrade.Sim.Events
{
    /// <summary>
    /// Something that happens at a scheduled tick. <see cref="EntityId"/> is the ship id for
    /// arrivals, in-system transport, and departures, and the system id for market events.
    /// </summary>
    public abstract class SimEvent
    {
        public abstract EventPhase Phase { get; }
        public abstract int EntityId { get; }

        /// <summary>Runs with the simulation clock set to this event's tick.</summary>
        public abstract void Apply(Simulation sim);
    }
}
