using System;

namespace PocketArbor.Domain.Random
{
    /// <summary>
    /// PCG32 (XSH-RR, 64-bit state, 32-bit output) after M. O'Neill, pcg-random.org.
    /// Bit-exact with the reference C implementation <c>pcg32_srandom_r / pcg32_random_r</c>.
    /// Allocation-free; safe to use in steady-state frames.
    /// </summary>
    public sealed class Pcg32 : IRandomSource
    {
        const ulong Multiplier = 6364136223846793005UL;

        ulong _state;
        ulong _increment;

        /// <param name="seed">Initial state.</param>
        /// <param name="sequence">Stream selector; different sequences give independent streams.</param>
        public Pcg32(ulong seed, ulong sequence = 0xDA3E39CB94B95BDBUL)
        {
            Reseed(seed, sequence);
        }

        public ulong State => _state;
        public ulong Increment => _increment;

        public void Reseed(ulong seed, ulong sequence)
        {
            _state = 0UL;
            _increment = (sequence << 1) | 1UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        public uint NextUInt()
        {
            ulong old = _state;
            _state = unchecked(old * Multiplier + _increment);
            uint xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            int rotation = (int)(old >> 59);
            return (xorShifted >> rotation) | (xorShifted << ((-rotation) & 31));
        }

        public uint NextUInt(uint bound)
        {
            if (bound == 0) throw new ArgumentOutOfRangeException(nameof(bound), "bound must be > 0");

            // Rejection sampling as in pcg32_boundedrand_r: threshold = 2^32 mod bound.
            uint threshold = unchecked((uint)-bound) % bound;
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return r % bound;
            }
        }

        public double NextDouble()
        {
            // 27 + 26 bits = 53-bit mantissa, same construction as genrand_res53.
            ulong a = NextUInt() >> 5;
            ulong b = NextUInt() >> 6;
            return (a * 67108864.0 + b) * (1.0 / 9007199254740992.0);
        }
    }
}
