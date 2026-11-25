namespace SpaceTrade.Sim.Timing
{
    /// <summary>
    /// Authoritative clock state: current tick, selected speed, and pause state.
    /// Changed only through <see cref="Simulation"/>.
    /// </summary>
    public sealed class SimClock
    {
        public long Tick { get; internal set; }
        public ClockSpeed Speed { get; internal set; }
        public bool IsPaused { get; internal set; }

        public GameDate Date => GameDate.FromTick(Tick);

        internal SimClock(long tick, ClockSpeed speed, bool isPaused)
        {
            Tick = tick;
            Speed = speed;
            IsPaused = isPaused;
        }
    }
}
