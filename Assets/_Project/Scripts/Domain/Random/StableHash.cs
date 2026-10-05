namespace PocketArbor.Domain.Random
{
    /// <summary>
    /// Stable integer hash (randomSeed, id, salt) → [0, 1) for values that must not consume the PCG32 stream (BALANCE §1.2.9, TD-018):
    /// the same mixing as the render hash of the Nebari roots / Dormancy subset, in double. Pure, allocation-free, identical on every
    /// platform (unchecked uint arithmetic only).
    /// </summary>
    public static class StableHash
    {
        /// <summary>⚑R BALANCE §1.5.4(а): salt of the trunk-sway irregularity h_k = 2·Hash01(randomSeed, k, "SWAY") − 1 (ASCII "SWAY").</summary>
        public const uint SwaySalt = 0x59415753u;

        /// <summary>⚑T BALANCE §1.6.4 / TD-071: salts of the Nebari root roles / sizes ("ROOT") and forks ("FORK"), ASCII little-endian.</summary>
        public const uint RootSalt = 0x544F4F52u;
        public const uint ForkSalt = 0x4B524F46u;

        const double Inv24 = 1.0 / 16777216.0;

        /// <summary>Hash of (seed, id, salt) in [0, 1) with 24-bit resolution (same bits as the render <c>Hash01</c>).</summary>
        public static double Hash01(uint seed, int id, uint salt)
        {
            unchecked
            {
                uint h = seed ^ (0x9E3779B9u * (uint)(id + 1)) ^ (salt * 0x85EBCA6Bu);
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (h >> 8) * Inv24;
            }
        }

        /// <summary>Signed hash in [−1, 1): 2·Hash01 − 1.</summary>
        public static double Signed(uint seed, int id, uint salt) => 2.0 * Hash01(seed, id, salt) - 1.0;
    }
}
