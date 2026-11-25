using System;
using System.Numerics;

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

        /// <summary>
        /// The one rounding rule for prices, rates, fares, and market amounts: divide exactly and
        /// round to the nearest whole number, with exact halves rounding up. Callers build the whole
        /// numerator first so the result is rounded once, at the end.
        /// </summary>
        public static long DivideRounded(BigInteger numerator, BigInteger denominator)
        {
            if (numerator < 0) throw new ArgumentOutOfRangeException(nameof(numerator), "Numerator cannot be negative.");
            if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator), "Denominator must be positive.");
            return (long)((numerator * 2 + denominator) / (denominator * 2));
        }
    }
}
