using System;

namespace SpaceTrade.Sim
{
    /// <summary>
    /// Deterministic random generator (xoshiro256**, seeded through SplitMix64). The algorithm is
    /// fixed here so the same seed gives the same numbers on every machine and .NET version.
    /// Its four state words are all that need saving.
    /// </summary>
    public sealed class SeededRandom
    {
        private ulong _s0, _s1, _s2, _s3;

        public SeededRandom(long seed)
        {
            ulong x = unchecked((ulong)seed);
            _s0 = SplitMix64(ref x);
            _s1 = SplitMix64(ref x);
            _s2 = SplitMix64(ref x);
            _s3 = SplitMix64(ref x);
        }

        public SeededRandom(ulong s0, ulong s1, ulong s2, ulong s3)
        {
            if ((s0 | s1 | s2 | s3) == 0) throw new ArgumentException("Random generator state cannot be all zero.");
            _s0 = s0;
            _s1 = s1;
            _s2 = s2;
            _s3 = s3;
        }

        public ulong S0 => _s0;
        public ulong S1 => _s1;
        public ulong S2 => _s2;
        public ulong S3 => _s3;

        private static ulong SplitMix64(ref ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15UL;
                ulong z = x;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        private static ulong RotateLeft(ulong v, int k) => (v << k) | (v >> (64 - k));

        public ulong NextULong()
        {
            unchecked
            {
                ulong result = RotateLeft(_s1 * 5, 7) * 9;
                ulong t = _s1 << 17;
                _s2 ^= _s0;
                _s3 ^= _s1;
                _s1 ^= _s2;
                _s0 ^= _s3;
                _s2 ^= t;
                _s3 = RotateLeft(_s3, 45);
                return result;
            }
        }

        /// <summary>Uniform value in [0, bound), without modulo bias.</summary>
        public ulong NextBelow(ulong bound)
        {
            if (bound == 0) throw new ArgumentOutOfRangeException(nameof(bound), "Bound must be positive.");
            ulong threshold = unchecked(0UL - bound) % bound;
            while (true)
            {
                ulong r = NextULong();
                if (r >= threshold) return r % bound;
            }
        }

        /// <summary>Uniform value from <paramref name="minInclusive"/> to <paramref name="maxInclusive"/>, both included.</summary>
        public long Range(long minInclusive, long maxInclusive)
        {
            if (maxInclusive < minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxInclusive), $"Range {minInclusive}..{maxInclusive} is empty.");
            ulong span = unchecked((ulong)(maxInclusive - minInclusive) + 1UL);
            ulong offset = span == 0 ? NextULong() : NextBelow(span);
            return unchecked(minInclusive + (long)offset);
        }

        public int Range(int minInclusive, int maxInclusive) => (int)Range((long)minInclusive, (long)maxInclusive);

        /// <summary>Fisher-Yates shuffle in place.</summary>
        public void Shuffle<T>(T[] items)
        {
            for (int i = items.Length - 1; i > 0; i--)
            {
                int j = (int)NextBelow((ulong)(i + 1));
                T tmp = items[i];
                items[i] = items[j];
                items[j] = tmp;
            }
        }
    }
}
