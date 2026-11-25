using System;

namespace SpaceTrade.Sim
{
    public static class IntMath
    {
        /// <summary>Largest r with r * r &lt;= n.</summary>
        public static long FloorSqrt(long n)
        {
            if (n < 0) throw new ArgumentOutOfRangeException(nameof(n), n, "Cannot take the square root of a negative number.");
            if (n < 2) return n;
            // Newton's method on integers, starting above the root.
            long x = n;
            long y = (x + 1) / 2;
            while (y < x)
            {
                x = y;
                y = (x + n / x) / 2;
            }
            return x;
        }

        /// <summary>Square root rounded to the nearest whole number (halves round up).</summary>
        public static long RoundedSqrt(long n)
        {
            long r = FloorSqrt(n);
            // (r + 0.5)^2 = r^2 + r + 0.25, so round up when n - r^2 > r.
            return n - r * r > r ? r + 1 : r;
        }
    }
}
