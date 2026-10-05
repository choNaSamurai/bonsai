using PocketArbor.Domain.Tree;

namespace PocketArbor.Domain.Growth
{
    /// <summary>Per-species balance (BALANCE §3, §1.2). Plain C#; ScriptableObject wrapper is PA-007.</summary>
    public sealed class SpeciesConfig
    {
        public SpeciesId Id;

        /// <summary>Base evaporation per day (used by SoilSimulation, PA-202).</summary>
        public double KBase;

        /// <summary>Secondary lateral angle relative to the parent (BALANCE §3, §1.2.6), triangular mean ± spread.</summary>
        public double BranchAngleMeanDeg;
        public double BranchAngleSpreadDeg;

        public double CurvatureSpread;

        /// <summary>Primary (tier) branch world angle from vertical, triangular mean ± spread (BALANCE §1.2.6).</summary>
        public double PrimaryAngleMeanDeg;
        public double PrimaryAngleSpreadDeg;

        /// <summary>Moyogi S-curve amplitude of the trunk chain (BALANCE §1.3.1: 32/30/26°; ⚑R §1.5.4 Momiji 30°). 0 = straight trunk (chokkan).</summary>
        public double TrunkSwayDeg;

        /// <summary>⚑R BALANCE §1.5.4(а): amplitude profile of chain segments k1…k4 (replaces TrunkSwayDecay^(k−1)); beyond its length —
        /// K4 × TrunkSwayDecay^(k − 4); unset (NaN) component — TrunkSwayDecay^(k−1). Value type (ADR-0010 rev.2 §6).</summary>
        public TrunkSwayProfile4 TrunkSwayProfile = TrunkSwayProfile4.Unset;

        /// <summary>⚑R BALANCE §1.5.2(а): c of the pad length decay exponent (d − 1) − c·k_t of a primary's subtree (Kuromatsu 0.8, others 0.6).</summary>
        public double PadTierDecayCompensation;

        /// <summary>§1.4.3: × on the TargetLength of every ord-1 node of a primary axis (an axis starting at a trunk-chain node).
        /// ⚑R §1.5.7: per species (was GrowthConfig.PrimaryLengthFactor 0.85): Kuromatsu 0.78, Momiji / Yamazakura 0.85.</summary>
        public double PrimaryLengthFactor;

        /// <summary>How much each branch continuation leans towards horizontal, 0..1 (BALANCE §1.2.6).</summary>
        public double BranchDroop;

        /// <summary>How many levels from a branch tip carry foliage (BALANCE §1.2.5, MODEL-derived).</summary>
        public int PadDepth;

        /// <summary>World angle from vertical of laterals of the crown axis (and of the chain top k4 before the crown axis grows),
        /// triangular mean ± spread (BALANCE §1.3.2, apex dome).</summary>
        public double CrownLateralAngleMeanDeg;
        public double CrownLateralAngleSpreadDeg;

        /// <summary>
        /// ⚑Q BALANCE §1.4.7 [STYLE-PROFILE]: shape knobs of this species (σ, λ, jitter floor + overrides of the shape groups).
        /// Null = Moyogi with the plain config fields (bitwise the same tree as <see cref="ShapeStyleProfile.Moyogi"/>, AC19).
        /// </summary>
        public ShapeStyleProfile Style;

        public static SpeciesConfig Kuromatsu() => new SpeciesConfig
        {
            Id = SpeciesId.Kuromatsu, KBase = 0.15, BranchAngleMeanDeg = 30.0, BranchAngleSpreadDeg = 10.0, CurvatureSpread = 0.45,
            PrimaryAngleMeanDeg = 80.0, PrimaryAngleSpreadDeg = 6.0, TrunkSwayDeg = 32.0, BranchDroop = 0.50, PadDepth = 2, // ⚑Q §1.4.5: 1 → 2
            CrownLateralAngleMeanDeg = 70.0, CrownLateralAngleSpreadDeg = 8.0,
            // ⚑R §1.5.4 / §1.5.7.
            // PA-102R tuning (TD-047): PrimaryLengthFactor 0.78 → 0.86 (range 0.70–0.90; upper tiers xc, W/P ≤ 1.35).
            TrunkSwayProfile = new TrunkSwayProfile4(1.00, 1.25, 0.95, 0.65), PadTierDecayCompensation = 0.8, PrimaryLengthFactor = 0.86,
        };

        public static SpeciesConfig Momiji() => new SpeciesConfig
        {
            Id = SpeciesId.Momiji, KBase = 0.28, BranchAngleMeanDeg = 30.0, BranchAngleSpreadDeg = 10.0, CurvatureSpread = 0.20,
            // ⚑Q §1.4.3 / §1.4.4: primaries 62° → 66° (PA-102Q tuning → 72°, range 60–72°), dome laterals 55° → 60°;
            // sway 30° → 26° (PA-102Q, TD-042) → ⚑R §1.5.4 30° (range 26–34°).
            PrimaryAngleMeanDeg = 72.0, PrimaryAngleSpreadDeg = 10.0, TrunkSwayDeg = 30.0, BranchDroop = 0.20, PadDepth = 2,
            CrownLateralAngleMeanDeg = 60.0, CrownLateralAngleSpreadDeg = 10.0,
            // PA-102R tuning (TD-047): c 0.6 → 1.0 (range 0.3–1.0), PrimaryLengthFactor 0.85 → 0.92 (range 0.75–0.95).
            TrunkSwayProfile = new TrunkSwayProfile4(1.00, 1.30, 0.90, 0.60), PadTierDecayCompensation = 1.0, PrimaryLengthFactor = 0.92,
        };

        public static SpeciesConfig Yamazakura() => new SpeciesConfig
        {
            Id = SpeciesId.Yamazakura, KBase = 0.22, BranchAngleMeanDeg = 35.0, BranchAngleSpreadDeg = 20.0, CurvatureSpread = 0.30,
            PrimaryAngleMeanDeg = 70.0, PrimaryAngleSpreadDeg = 10.0, TrunkSwayDeg = 26.0, BranchDroop = 0.30, PadDepth = 2,
            CrownLateralAngleMeanDeg = 60.0, CrownLateralAngleSpreadDeg = 10.0,
            // PA-102R tuning (TD-047): profile 1.00/1.20/0.85/0.60 → 1.15/1.35/1.00/0.60 (±0.15; AC13 «≥ 2 distinct bends» in 100%).
            TrunkSwayProfile = new TrunkSwayProfile4(1.15, 1.35, 1.00, 0.60), PadTierDecayCompensation = 0.6, PrimaryLengthFactor = 0.85,
        };

        public static SpeciesConfig CreateDefault(SpeciesId id)
        {
            switch (id)
            {
                case SpeciesId.Momiji: return Momiji();
                case SpeciesId.Yamazakura: return Yamazakura();
                default: return Kuromatsu();
            }
        }
    }
}
