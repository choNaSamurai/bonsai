namespace PocketArbor.Domain.Growth
{
    /// <summary>
    /// ⚑T BALANCE §1.6.1(б) node targets T_g of the side tiers t0…t5 (value type, ADR-0010 rev.2 §6 — like <see cref="TrunkSwayProfile4"/>:
    /// a style profile copied with <see cref="ShapeStyleProfile.Clone"/> or assigned from a species never aliases mutable storage).
    /// A negative component = unset (a style profile inherits it from the species).
    /// </summary>
    public readonly struct PadNodeTargets
    {
        public readonly int T0;
        public readonly int T1;
        public readonly int T2;
        public readonly int T3;
        public readonly int T4;
        public readonly int T5;

        public PadNodeTargets(int t0, int t1, int t2, int t3, int t4, int t5)
        {
            T0 = t0;
            T1 = t1;
            T2 = t2;
            T3 = t3;
            T4 = t4;
            T5 = t5;
        }

        /// <summary>All components unset (−1).</summary>
        public static PadNodeTargets Unset => new PadNodeTargets(-1, -1, -1, -1, -1, -1);

        public const int Length = 6;

        /// <summary>Target of tier <paramref name="tier"/> ∈ [0, 5]; −1 outside.</summary>
        public int this[int tier]
        {
            get
            {
                switch (tier)
                {
                    case 0: return T0;
                    case 1: return T1;
                    case 2: return T2;
                    case 3: return T3;
                    case 4: return T4;
                    case 5: return T5;
                    default: return -1;
                }
            }
        }

        /// <summary>Per component: this value where it is set (≥ 0), else <paramref name="fallback"/>'s (style override over the species).</summary>
        public PadNodeTargets Over(in PadNodeTargets fallback) => new PadNodeTargets(
            Pick(T0, fallback.T0), Pick(T1, fallback.T1), Pick(T2, fallback.T2), Pick(T3, fallback.T3), Pick(T4, fallback.T4), Pick(T5, fallback.T5));

        static int Pick(int value, int fallback) => value >= 0 ? value : fallback;
    }
}
