namespace SpaceTrade.Sim.Events
{
    /// <summary>
    /// Processing order for events at the same tick (spec 8.1). Lower values run first;
    /// within a phase, the lower entity id runs first.
    /// </summary>
    public enum EventPhase
    {
        Arrival = 1,
        MarketReplenishment = 2,
        InSystemTransport = 3,
        AutomatedDeparture = 4,
    }
}
