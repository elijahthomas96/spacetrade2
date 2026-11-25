using System;

namespace SpaceTrade.Sim.Timing
{
    /// <summary>
    /// Turns real elapsed time into whole game ticks (spec 3.2). Real elapsed microseconds are
    /// multiplied by the speed once and added to a carry; whole ticks are passed to
    /// <see cref="Simulation.Advance"/> and the remainder is kept, so the result does not depend
    /// on how the elapsed time is split into frames. Real time that passes while paused is discarded.
    /// </summary>
    public sealed class RealTimeDriver
    {
        private readonly Simulation _sim;
        private readonly IRealTimeSource _source;
        private long _lastReading;
        private long _carryGameMicroseconds;

        public RealTimeDriver(Simulation sim, IRealTimeSource source)
        {
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _lastReading = source.NowMicroseconds;
        }

        /// <summary>Reads the time source and advances the simulation. Returns the ticks advanced.</summary>
        public long Pump()
        {
            long now = _source.NowMicroseconds;
            long elapsed = now - _lastReading;
            _lastReading = now;
            if (elapsed <= 0 || _sim.Clock.IsPaused) return 0;

            _carryGameMicroseconds = checked(_carryGameMicroseconds + elapsed * ClockSpeeds.Multiplier(_sim.Clock.Speed));
            long ticks = _carryGameMicroseconds / GameTime.RealMicrosecondsPerTickAt1x;
            _carryGameMicroseconds %= GameTime.RealMicrosecondsPerTickAt1x;
            if (ticks > 0) _sim.Advance(checked(_sim.CurrentTick + ticks));
            return ticks;
        }

        // Clock commands pump first so real time already elapsed is counted at the old setting.

        public CommandResult Pause()
        {
            Pump();
            return _sim.Pause();
        }

        public CommandResult Play()
        {
            Pump();
            return _sim.Play();
        }

        public CommandResult SetSpeed(ClockSpeed speed)
        {
            Pump();
            return _sim.SetSpeed(speed);
        }
    }
}
