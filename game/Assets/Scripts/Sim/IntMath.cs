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
        /// The one rounding rule (spec 5.5): divide exactly and round to the nearest whole number,
        /// with exact halves rounding away from zero (2.5 gives 3, -2.5 gives -3). Callers build the
        /// whole numerator first so the result is rounded once, at the end.
        /// </summary>
        public static long DivideRounded(BigInteger numerator, BigInteger denominator)
        {
            if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator), "Denominator must be positive.");
            BigInteger magnitude = (BigInteger.Abs(numerator) * 2 + denominator) / (denominator * 2);
            return (long)(numerator < 0 ? -magnitude : magnitude);
        }

        /// <summary>
        /// Exact division rounded up, for amounts that must never come out short: supplies a voyage
        /// needs (spec 6.3). Not a rounding to nearest, so it is not covered by <see cref="DivideRounded"/>.
        /// </summary>
        public static long DivideCeiling(BigInteger numerator, BigInteger denominator)
        {
            if (numerator < 0) throw new ArgumentOutOfRangeException(nameof(numerator), "Numerator cannot be negative.");
            if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator), "Denominator must be positive.");
            return (long)((numerator + denominator - 1) / denominator);
        }

        /// <summary>
        /// Exact division rounded down, for figures shown on screen that must never come out ahead: a voyage's progress
        /// and distance flown (spec 5.5), so 100 percent appears only once the ship has docked. Display only.
        /// </summary>
        public static long DivideFloor(BigInteger numerator, BigInteger denominator)
        {
            if (numerator < 0) throw new ArgumentOutOfRangeException(nameof(numerator), "Numerator cannot be negative.");
            if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator), "Denominator must be positive.");
            return (long)(numerator / denominator);
        }
    }
}
