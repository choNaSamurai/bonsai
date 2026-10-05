namespace PocketArbor.Domain.Growth
{
    /// <summary>
    /// ⚑R BALANCE §1.5.4(а) amplitude profile of the trunk-chain segments k1…k4 (ADR-0010 rev.2 §6): a value type — a profile copied with
    /// <see cref="ShapeStyleProfile.Clone"/> or assigned from a species never aliases mutable storage. <c>NaN</c> component = unset
    /// (a style profile inherits it from the species; a species falls back to TrunkSwayDecay^(k−1)).
    /// </summary>
    public readonly struct TrunkSwayProfile4
    {
        public readonly double K1;
        public readonly double K2;
        public readonly double K3;
        public readonly double K4;

        public TrunkSwayProfile4(double k1, double k2, double k3, double k4)
        {
            K1 = k1;
            K2 = k2;
            K3 = k3;
            K4 = k4;
        }

        /// <summary>All components unset (NaN).</summary>
        public static TrunkSwayProfile4 Unset => new TrunkSwayProfile4(double.NaN, double.NaN, double.NaN, double.NaN);

        public const int Length = 4;

        /// <summary>Component of segment <paramref name="k"/> ∈ [1, 4]; NaN outside.</summary>
        public double this[int k]
        {
            get
            {
                switch (k)
                {
                    case 1: return K1;
                    case 2: return K2;
                    case 3: return K3;
                    case 4: return K4;
                    default: return double.NaN;
                }
            }
        }

        /// <summary>Per component: this value where it is finite, else <paramref name="fallback"/>'s (style override over the species).</summary>
        public TrunkSwayProfile4 Over(in TrunkSwayProfile4 fallback) => new TrunkSwayProfile4(
            Pick(K1, fallback.K1), Pick(K2, fallback.K2), Pick(K3, fallback.K3), Pick(K4, fallback.K4));

        static double Pick(double value, double fallback) => double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;
    }
}
