using SpaceTrade.Sim.Timing;

namespace SpaceTrade.Sim.Voyages
{
    /// <summary>
    /// When a ship arrives, for display only (spec 3.4): the in-game arrival date, and the real time left at the current
    /// speed. Worked out from the locked voyage terms, the current tick, and the clock; never stored, never saved, and it
    /// changes nothing. Whole numbers only. The screen shows these figures and works out none of its own.
    /// </summary>
    public sealed class VoyageEta
    {
        public int ShipId { get; }

        /// <summary>False for a docked ship, which has no arrival; every other figure is then zero.</summary>
        public bool InTransit { get; }

        public long ArrivalTick { get; }

        /// <summary>The in-game arrival date. It is fixed at dispatch: speed changes and pausing never move it.</summary>
        public GameDate ArrivalDate { get; }

        /// <summary>Game ticks still to fly.</summary>
        public long TicksRemaining { get; }

        /// <summary>True while the clock is paused: the countdown is frozen and the screen says "Paused".</summary>
        public bool IsPaused { get; }

        public ClockSpeed Speed { get; }

        /// <summary>1, 3, or 5.</summary>
        public int SpeedMultiplier { get; }

        /// <summary>
        /// Real seconds until arrival at the selected speed, rounded up (spec 5.5), so a countdown never reaches zero early.
        /// While paused this is the figure the countdown is frozen at.
        /// </summary>
        public long RealSecondsRemaining { get; }

        internal VoyageEta(int shipId, SimClock clock)
        {
            ShipId = shipId;
            IsPaused = clock.IsPaused;
            Speed = clock.Speed;
            SpeedMultiplier = ClockSpeeds.Multiplier(clock.Speed);
        }

        internal VoyageEta(int shipId, Voyage voyage, SimClock clock) : this(shipId, clock)
        {
            InTransit = true;
            ArrivalTick = voyage.ArrivalTick;
            ArrivalDate = GameDate.FromTick(voyage.ArrivalTick);
            TicksRemaining = voyage.ArrivalTick - clock.Tick;
            RealSecondsRemaining = IntMath.DivideCeiling(GameTime.RealMillisecondsFor(TicksRemaining, clock.Speed), 1000);
        }

        /// <summary>
        /// The line every ETA display shows: "Arrives Y3 M04 D12 · 2 h 05 min 07 s at 3x", ending "· Paused" while the clock
        /// is paused, or "Docked" for a docked ship.
        /// </summary>
        public string Describe()
        {
            if (!InTransit) return "Docked";
            string when = "Arrives " + ArrivalDate;
            return IsPaused ? when + " · Paused" : $"{when} · {Format.RealDuration(RealSecondsRemaining)} at {SpeedMultiplier}x";
        }
    }
}
