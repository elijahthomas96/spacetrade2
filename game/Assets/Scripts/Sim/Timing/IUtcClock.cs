using System;
using System.Diagnostics;

namespace SpaceTrade.Sim.Timing
{
    /// <summary>The real-world calendar clock, in whole milliseconds since 1970 UTC. Save timestamps and offline time use it (spec 11).</summary>
    public interface IUtcClock
    {
        long NowUtcMilliseconds { get; }
    }

    /// <summary>The computer's UTC clock. It can jump forward or backward when the user or Windows changes the time.</summary>
    public sealed class SystemUtcClock : IUtcClock
    {
        public long NowUtcMilliseconds => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>A monotonic clock (one that never jumps backward) in whole microseconds, for the real-time driver (spec 3.2).</summary>
    public sealed class StopwatchTimeSource : IRealTimeSource
    {
        public long NowMicroseconds
        {
            get
            {
                long ticks = Stopwatch.GetTimestamp();
                long frequency = Stopwatch.Frequency;
                return ticks / frequency * 1_000_000 + ticks % frequency * 1_000_000 / frequency;
            }
        }
    }
}
