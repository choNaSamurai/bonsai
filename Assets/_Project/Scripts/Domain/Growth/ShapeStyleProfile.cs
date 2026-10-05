namespace PocketArbor.Domain.Growth
{
    /// <summary>
    /// ⚑Q BALANCE §1.4.7 [STYLE-PROFILE] — the shape "knobs" of a species / style (PA-102Q). Plain C#; the ScriptableObject wrapper
    /// is PA-007 (placement — Unity Architect). Three own parameters of the trunk line — σ <see cref="TrunkSwayScale"/>,
    /// λ <see cref="TrunkLeanOffsetDeg"/>, <see cref="TrunkSwayJitterMinScale"/> — plus optional overrides of the shape groups of
    /// §1.4.7: <c>double.NaN</c> = inherit the <see cref="GrowthConfig"/> / <see cref="SpeciesConfig"/> field. Resolved once per use
    /// into <see cref="ShapeParams"/> (no allocation). Moyogi (σ 1, λ 0, no overrides) grows bitwise the same tree as no profile (AC19).
    /// Not persisted: the style of a tree in the game comes from the player's actions (GDD §10.9, separate task).
    /// </summary>
    public sealed class ShapeStyleProfile
    {
        public string Name = "Moyogi";

        // ---- Trunk line (§1.4.7 «Профіль лінії стовбура») ----
        /// <summary>σ: × the S-curve amplitude A_k and the base sway of #0 (0 = straight, chokkan).</summary>
        public double TrunkSwayScale = 1.0;

        /// <summary>λ: lean offset of the whole chain towards TierPhase, degrees (clamped to ±GrowthConfig.TrunkLeanOffsetMaxDeg).</summary>
        public double TrunkLeanOffsetDeg;

        /// <summary>Jitter / chain Curvature are × max(σ, this) — a straight trunk keeps a trace of life.</summary>
        public double TrunkSwayJitterMinScale = 0.1;

        // ---- Overrides (NaN = inherit) ----
        public double TrunkSwayDeg = double.NaN;
        public double TrunkSwayDecay = double.NaN;
        public double TrunkBaseSwayRatio = double.NaN;
        public double TrunkSegmentLength = double.NaN;
        public double TrunkSegmentDecay = double.NaN;
        public double CrownAxisLengthFactor = double.NaN;
        public double TrunkChainTaperRatio = double.NaN;
        public double TrunkChainDepthScaleFactor = double.NaN;
        public double PrimaryThicknessRatio = double.NaN;
        public double PrimaryAngleMeanDeg = double.NaN;
        public double PrimaryAngleSpreadDeg = double.NaN;
        public double PrimaryLengthFactor = double.NaN;
        public double BranchDroop = double.NaN;
        public double CrownLateralAngleMeanDeg = double.NaN;
        public double CrownLateralAngleSpreadDeg = double.NaN;
        public double CrownLateralLengthFactor = double.NaN;
        public double DomeWeightMultiplier = double.NaN;

        // ⚑R BALANCE §1.5 (PA-102R): «Лінія стовбура» (profile, irregularity, bow — all × σ in the model), «Яруси», «Купол».
        /// <summary>Amplitude profile k1…k4; NaN component = inherit the species component (ADR-0010 rev.2 §6).</summary>
        public TrunkSwayProfile4 TrunkSwayProfile = TrunkSwayProfile4.Unset;
        public double TrunkSwayIrregularity = double.NaN;
        public double TrunkBowCurvature = double.NaN;
        public double PadTierDecayCompensation = double.NaN;
        public double PadLengthFactor = double.NaN;
        public double PrimaryReachCompensation = double.NaN;
        public double DomeLateralWeight = double.NaN;

        /// <summary>Default style (the L2 reference): σ 1, λ 0, jitter floor 0.1, no overrides.</summary>
        public static ShapeStyleProfile Moyogi() => new ShapeStyleProfile();

        public ShapeStyleProfile Clone() => (ShapeStyleProfile)MemberwiseClone();
    }

    /// <summary>
    /// Shape parameters resolved from <see cref="GrowthConfig"/>, <see cref="SpeciesConfig"/> and its optional
    /// <see cref="ShapeStyleProfile"/> (§1.4.7). A value type: resolving allocates nothing.
    /// </summary>
    public readonly struct ShapeParams
    {
        public readonly double SwayScale;
        public readonly double LeanOffsetDeg;
        /// <summary>max(σ, TrunkSwayJitterMinScale) — the multiplier of the chain angle jitter and of the chain Curvature.</summary>
        public readonly double JitterScale;
        public readonly double TrunkSwayDeg;
        public readonly double TrunkSwayDecay;
        public readonly double TrunkBaseSwayRatio;
        public readonly double TrunkSegmentLength;
        public readonly double TrunkSegmentDecay;
        public readonly double CrownAxisLengthFactor;
        public readonly double TrunkChainTaperRatio;
        public readonly double TrunkChainDepthScaleFactor;
        public readonly double PrimaryThicknessRatio;
        public readonly double PrimaryAngleMeanDeg;
        public readonly double PrimaryAngleSpreadDeg;
        public readonly double PrimaryLengthFactor;
        public readonly double BranchDroop;
        public readonly double CrownLateralAngleMeanDeg;
        public readonly double CrownLateralAngleSpreadDeg;
        public readonly double CrownLateralLengthFactor;
        public readonly double DomeWeightMultiplier;
        /// <summary>⚑R §1.5.4(а): k1…k4 amplitude profile (style over species; NaN component → TrunkSwayDecay^(k−1)).</summary>
        public readonly TrunkSwayProfile4 TrunkSwayProfile;
        public readonly double TrunkSwayIrregularity;
        public readonly double TrunkBowCurvature;
        public readonly double PadTierDecayCompensation;
        public readonly double PadLengthFactor;
        public readonly double PrimaryReachCompensation;
        public readonly double DomeLateralWeight;

        ShapeParams(GrowthConfig c, SpeciesConfig s, ShapeStyleProfile p)
        {
            double sigma = p != null ? Finite(p.TrunkSwayScale, 1.0) : 1.0;
            if (sigma < 0.0) sigma = 0.0;
            double floor = p != null ? Finite(p.TrunkSwayJitterMinScale, 0.0) : 0.0;
            SwayScale = sigma;
            JitterScale = sigma > floor ? sigma : floor;
            double maxLean = c.TrunkLeanOffsetMaxDeg;
            LeanOffsetDeg = p != null ? GrowthFormulas.Clamp(Finite(p.TrunkLeanOffsetDeg, 0.0), -maxLean, maxLean) : 0.0;
            TrunkSwayDeg = Pick(p?.TrunkSwayDeg, s.TrunkSwayDeg);
            TrunkSwayDecay = Pick(p?.TrunkSwayDecay, c.TrunkSwayDecay);
            TrunkBaseSwayRatio = Pick(p?.TrunkBaseSwayRatio, c.TrunkBaseSwayRatio);
            TrunkSegmentLength = Pick(p?.TrunkSegmentLength, c.TrunkSegmentLength);
            TrunkSegmentDecay = Pick(p?.TrunkSegmentDecay, c.TrunkSegmentDecay);
            CrownAxisLengthFactor = Pick(p?.CrownAxisLengthFactor, c.CrownAxisLengthFactor);
            TrunkChainTaperRatio = Pick(p?.TrunkChainTaperRatio, c.TrunkChainTaperRatio);
            TrunkChainDepthScaleFactor = Pick(p?.TrunkChainDepthScaleFactor, c.TrunkChainDepthScaleFactor);
            PrimaryThicknessRatio = Pick(p?.PrimaryThicknessRatio, c.PrimaryThicknessRatio);
            PrimaryAngleMeanDeg = Pick(p?.PrimaryAngleMeanDeg, s.PrimaryAngleMeanDeg);
            PrimaryAngleSpreadDeg = Pick(p?.PrimaryAngleSpreadDeg, s.PrimaryAngleSpreadDeg);
            PrimaryLengthFactor = Pick(p?.PrimaryLengthFactor, s.PrimaryLengthFactor);
            BranchDroop = Pick(p?.BranchDroop, s.BranchDroop);
            CrownLateralAngleMeanDeg = Pick(p?.CrownLateralAngleMeanDeg, s.CrownLateralAngleMeanDeg);
            CrownLateralAngleSpreadDeg = Pick(p?.CrownLateralAngleSpreadDeg, s.CrownLateralAngleSpreadDeg);
            CrownLateralLengthFactor = Pick(p?.CrownLateralLengthFactor, c.CrownLateralLengthFactor);
            DomeWeightMultiplier = Pick(p?.DomeWeightMultiplier, c.DomeWeightMultiplier);
            TrunkSwayProfile = p != null ? p.TrunkSwayProfile.Over(in s.TrunkSwayProfile) : s.TrunkSwayProfile;
            TrunkSwayIrregularity = GrowthFormulas.Clamp(Pick(p?.TrunkSwayIrregularity, c.TrunkSwayIrregularity), 0.0, MaxIrregularity);
            TrunkBowCurvature = Pick(p?.TrunkBowCurvature, c.TrunkBowCurvature);
            PadTierDecayCompensation = Pick(p?.PadTierDecayCompensation, s.PadTierDecayCompensation);
            PadLengthFactor = Pick(p?.PadLengthFactor, c.PadLengthFactor);
            PrimaryReachCompensation = Pick(p?.PrimaryReachCompensation, c.PrimaryReachCompensation);
            DomeLateralWeight = Pick(p?.DomeLateralWeight, c.DomeLateralWeight);
        }

        /// <summary>|h_k|·Irregularity &lt; 1 keeps every A_k positive (alternation of the S-curve).</summary>
        const double MaxIrregularity = 0.99;

        /// <summary>⚑R §1.5.4(а) Profile[k] (k ≥ 1): component k of the profile; k &gt; 4 — K4 × TrunkSwayDecay^(k − 4); unset (NaN) —
        /// TrunkSwayDecay^(k−1) (the PA-102Q rule). Allocation-free.</summary>
        public double SwayProfileAt(int k)
        {
            if (k < 1) k = 1;
            if (k <= TrunkSwayProfile4.Length)
            {
                double v = TrunkSwayProfile[k];
                return double.IsNaN(v) || double.IsInfinity(v) ? System.Math.Pow(TrunkSwayDecay, k - 1) : v;
            }
            double last = TrunkSwayProfile.K4;
            return double.IsNaN(last) || double.IsInfinity(last)
                ? System.Math.Pow(TrunkSwayDecay, k - 1)
                : last * System.Math.Pow(TrunkSwayDecay, k - TrunkSwayProfile4.Length);
        }

        public static ShapeParams Resolve(GrowthConfig config, SpeciesConfig species) => new ShapeParams(config, species, species.Style);

        static double Pick(double? profileValue, double fallback) =>
            profileValue.HasValue && !double.IsNaN(profileValue.Value) && !double.IsInfinity(profileValue.Value) ? profileValue.Value : fallback;

        static double Finite(double value, double fallback) => double.IsNaN(value) || double.IsInfinity(value) ? fallback : value;
    }
}
