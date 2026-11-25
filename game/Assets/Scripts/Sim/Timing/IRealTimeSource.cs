namespace SpaceTrade.Sim.Timing
{
    /// <summary>
    /// A monotonic real-world clock in whole microseconds. Unaffected by game speed or pause.
    /// </summary>
    public interface IRealTimeSource
    {
        long NowMicroseconds { get; }
    }
}
