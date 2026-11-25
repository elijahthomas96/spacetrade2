using System;

namespace SpaceTrade.Sim.Timing
{
    /// <summary>
    /// Transit time (spec 3.3): game years = light-years / multiples of c.
    /// </summary>
    public static class TransitMath
    {
        public static long TransitTicks(long distanceMilliLy, long speedMilliC)
        {
            if (distanceMilliLy < 0) throw new ArgumentOutOfRangeException(nameof(distanceMilliLy), distanceMilliLy, "Distance cannot be negative.");
            if (speedMilliC <= 0) throw new ArgumentOutOfRangeException(nameof(speedMilliC), speedMilliC, "Drive speed must be positive.");
            long numerator = checked(distanceMilliLy * GameTime.TicksPerYear);
            return numerator / speedMilliC + (numerator % speedMilliC == 0 ? 0 : 1);
        }
    }
}
