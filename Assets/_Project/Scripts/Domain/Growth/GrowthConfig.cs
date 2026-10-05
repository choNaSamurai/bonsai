using System;
using PocketArbor.Domain.Tree;

namespace PocketArbor.Domain.Growth
{
    /// <summary>
    /// Growth balance (BALANCE §1, §1.1). Plain C#: the ScriptableObject wrapper and mapping are PA-007.
    /// Angles are degrees here (designer-facing) and converted to radians by the simulation.
    /// Field defaults are the BALANCE values.
    /// </summary>
    public sealed class GrowthConfig
    {
        public int MaxNodes = 200;
        public int MaxDepth = 8;
        /// <summary>
        /// BALANCE §1: 0.20; PA-102S AC15 / §1.2.10 compensation → 0.23 (fixture N(D31) ≥ 50, N(D47) ≥ 100 with the role-based
        /// parent selection). Pending Game Designer sync of the BALANCE table (TD-PA102S-01).
        /// </summary>
        public double BaseGrowthProbability = 0.23;
        public int ShootsPerDayMin = 1;
        public int ShootsPerDayMax = 3;

        // Thickening / depth scale
        public double ThickeningRate = 0.25;
        public double MaxThickness = 24.0;
        public double DepthScaleExponent = 1.8;
        public double DepthScaleMin = 0.1;
        public double NebariThickeningMultiplier = 3.0;

        // Temperature factor
        public double TempLow = 5.0;
        public double TempLowRange = 18.0;
        public double TempHigh = 38.0;
        public double TempHighRange = 12.0;
        public double TempHighMaxSuppression = 0.8;

        /// <summary>Winter floor: T_factor = max(TempFactorMin, T_factor_TZ). Owner decision 2026-10-04 (0 = exact TZ).</summary>
        public double TempFactorMin = 0.25;

        // Moisture factor (W_dry lives in SoilConfig.DryThreshold)
        public double MoistureOptimal = 0.40;

        // Time
        public int HoursPerGrowthDay = 24;

        // Development
        public int MaxChildrenPerNode = 3;
        public int BudDays = 1;
        public int MatureDays = 5;
        public double ElongationRate = 0.5;
        public double LeafUnfoldDays = 3.0;

        // New shoots
        public double ShootInitialLength = 3.0;
        public double ShootInitialThickness = 1.0;
        public double ShootBaseLength = 30.0;
        public double ShootLengthDecay = 0.85;
        public double ShootLengthJitter = 0.20;
        public double ApicalLengthMultiplier = 1.2;
        public double ApicalAngleJitterDeg = 6.0;
        public double ApicalBendScaleDeg = 12.0;
        public double TrunkBendScaleDeg = 8.0;
        public double MaxShootWorldAngleDeg = 90.0;

        // Phoenix bud (depth is always 1: apical child of the trunk)
        public double PhoenixBudLength = 6.0;
        public double PhoenixBudThickness = 2.5;

        // Sapling (PlantSeed)
        public double TrunkInitialLength = 20.0;
        /// <summary>⚑P BALANCE §1.3.1: 48 → 38 (target length of trunk node #0); PA-102P tuning → 36.</summary>
        public double TrunkTargetLength = 36.0; // PA-102P tuning (range 34–42, Proposed 38): TD-031
        public double TrunkInitialThickness = 4.0;
        public double SaplingLeaderLength = 10.0;
        public double SaplingLeaderThickness = 1.6;
        public int SaplingLeaderAgeDays = 4;
        public double SaplingTwigLength = 8.0;
        public double SaplingTwigThickness = 1.2;
        public int SaplingTwigAgeDays = 2;

        // ---- Bonsai shape (BALANCE §1.2, PA-102S) -------------------------------------------------

        // §1.2.1 Attachment point of lateral shoots (fraction of the parent length).
        public double LateralSlotT0 = 0.45;
        public double LateralSlotT1 = 0.85;
        /// <summary>⚑Q BALANCE §1.4.1(в): 0.70 → 0.78 Proposed; PA-102Q tuning → 0.74 (range 0.74–0.82, TD-042).</summary>
        public double TrunkBaseSlotT0 = 0.74;
        public double TrunkBaseSlotT1 = 0.95;
        public double AttachTJitter = 0.04;
        public double AttachTMin = 0.05;
        public double AttachTMax = 0.97;

        // §1.2.2 Taper: th_at(p, t) = th_p·(1 − (1 − SegmentTipTaper)·t); th_cap = R·th_at(parent, AttachT).
        public double SegmentTipTaper = 0.70;
        public double ApicalThicknessRatio = 0.90;
        public double LateralThicknessRatio = 0.50;
        public double MinShootThickness = 0.30;

        // §1.2.3 Trunk chain (moyogi S-curve). Amplitude TrunkSwayDeg is per species.
        public int TrunkChainSegments = 5;
        /// <summary>⚑P BALANCE §1.3.1: 0.85 → 0.88.</summary>
        public double TrunkSwayDecay = 0.88;
        public double TrunkSwayJitterDeg = 3.0;
        public double TrunkCurvatureJitter = 0.10;

        // ---- Reference shape (BALANCE §1.3, PA-102P) ----------------------------------------------

        // §1.3.1 Trunk chain segment k ≥ 1: TargetLength = TrunkSegmentLength·TrunkSegmentDecay^(k−1)·(1 + TrunkSegmentJitter·(2·u5 − 1)).
        public double TrunkSegmentLength = 22.5;
        /// <summary>⚑Q BALANCE §1.4.1(в): 0.90 → 0.78 Proposed; PA-102Q tuning → 0.84 (range 0.72–0.84, TD-042).</summary>
        public double TrunkSegmentDecay = 0.84;
        public double TrunkSegmentJitter = 0.08;

        /// <summary>§1.3.1 Crown axis (ord 0, Depth ≥ TrunkChainSegments) length factor (replaces OrderLengthFactor; ×ApicalLengthMultiplier stays).</summary>
        public double CrownAxisLengthFactor = 0.70; // PA-102P tuning (range 0.40–0.70, Proposed 0.50): TD-031

        /// <summary>§1.3.1 S-curve amplitude: A_k = TrunkSwayDeg·(1 + TrunkSwayBendGain·|bendBias|)·TrunkSwayDecay^(k−1).</summary>
        public double TrunkSwayBendGain = 0.5;

        /// <summary>§1.3.1 Base lean of #0: BaseAngle(#0) = lean + TierPhase·TrunkBaseSwayRatio·A_1.</summary>
        public double TrunkBaseSwayRatio = 0.20; // PA-102Q tuning (PA-102P range 0.2–0.5, Proposed 0.35): TD-042

        // §1.3.1 Apical delay of the trunk: an ord-0 node of index k = Depth takes an apical child only when AgeDays ≥ Base + Step·k.
        public int TrunkApicalDelayBaseDays = 4; // PA-102P tuning (range 4–8, Proposed 6): TD-031
        public int TrunkApicalDelayStepDays = 2; // PA-102P tuning (range 2–4, Proposed 3): TD-031

        /// <summary>§1.3.4 Apical continuation of a branch (ordEff ≥ 1, not the crown axis): × this instead of ApicalLengthMultiplier.</summary>
        public double BranchApicalLengthMultiplier = 0.45; // PA-102P tuning (range 0.45–0.80, Proposed 0.60): TD-031

        // §1.3.5 Back-budding after a cut: s_i ×= BackBudWeightMultiplier when BackBudDist(i) ≤ BackBudRadiusEdges ∧ ord(i) ≥ 1;
        // a cut is fresh while GrowthDayIndex − LastCutDay < BackBudDays.
        public int BackBudDays = 14;
        public int BackBudRadiusEdges = 2;
        public double BackBudWeightMultiplier = 8.0; // PA-102P tuning (range 4–10, Proposed 6): TD-031

        // §1.2.6 Length by branch order: × OrderLengthFactor^min(ordEff, 2).
        public double OrderLengthFactor = 0.70;

        /// <summary>§1.2.6: continuations / secondary laterals with |world angle| ≥ this arc upwards (like primaries).</summary>
        public double ArcUpMinWorldAngleDeg = 45.0;

        // §1.2.7 Parent selection role weights (s_i = w_i · RoleWeight_i; Σw for the rate is unchanged).
        public double ApicalDominanceWeight = 2.5;
        public double TierWeight = 1.5;
        public double PadWeight = 1.0;
        public double InteriorWeight = 0.25;

        // §1.2.5 [MODEL-derived] A pad is "full" from this many nodes.
        public int PadFullNodes = 4;

        // ---- Ideal, iteration 2 (BALANCE §1.4, PA-102Q) --------------------------------------------

        /// <summary>§1.4.1(б): ceiling of an apical child of an ord-0 node (chain and crown axis) = ratio × Thickness(parent)
        /// (replaces ApicalThicknessRatio·th_at(parent, 1) for these nodes).</summary>
        public double TrunkChainTaperRatio = 0.75;

        /// <summary>§1.4.1(б): natural ceiling of ord-0 nodes th_max = MaxThickness·depthScale(Depth × this); ord ≥ 1 — TZ.</summary>
        public double TrunkChainDepthScaleFactor = 0.5;

        /// <summary>§1.4.3: ceiling of a lateral child of an ord-0 node (primary, crown-axis lateral) = ratio × th_at(parent, AttachT).</summary>
        public double PrimaryThicknessRatio = 0.38;

        /// <summary>§1.4.3: × on the TargetLength of every ord-1 node of a primary axis (an axis starting at a trunk-chain node).</summary>
        public double PrimaryLengthFactor = 0.85;

        /// <summary>§1.4.4: × on the TargetLength of crown-axis laterals and their apical continuations (apex dome).</summary>
        public double CrownLateralLengthFactor = 1.5; // PA-102Q tuning (range 1.0–1.5, Proposed 1.25): TD-042

        /// <summary>§1.4.4: × on the selection weight of the dome subtree (crown-axis nodes and all descendants); Σw (rate) unchanged.</summary>
        public double DomeWeightMultiplier = 3.0; // PA-102Q tuning (range 1.5–3.0, Proposed 2.0): TD-042

        /// <summary>§1.4.4 lever (range 0.45–0.70): slot 0 of crown-axis laterals (other non-#0 parents keep LateralSlotT0).</summary>
        public double CrownLateralSlotT0 = 0.45;

        /// <summary>§1.4.7: |TrunkLeanOffsetDeg| λ of a ShapeStyleProfile is clamped to this (owner 2026-10-05: Shakan ≤ 30°).</summary>
        public double TrunkLeanOffsetMaxDeg = 30.0;

        // §1.2.8 Sapling wild twig.
        public double SaplingTwigAttachT = 0.30;
        public double SaplingTwigWorldAngleDeg = 30.0;

        public static GrowthConfig CreateDefault() => new GrowthConfig();

        /// <summary>Composition-time check of structural invariants. Throws on programmer/config error.</summary>
        public void Validate()
        {
            if (MaxNodes < 1 || MaxNodes > TreeGraph.Capacity) throw new ArgumentException("MaxNodes must be in [1, " + TreeGraph.Capacity + "]");
            if (MaxDepth < 1 || MaxDepth > byte.MaxValue - 1) throw new ArgumentException("MaxDepth must be in [1, 254]");
            if (HoursPerGrowthDay < 1) throw new ArgumentException("HoursPerGrowthDay must be >= 1");
            if (ShootsPerDayMin < 0 || ShootsPerDayMax < ShootsPerDayMin) throw new ArgumentException("ShootsPerDay range is invalid");
            if (MaxChildrenPerNode < 1) throw new ArgumentException("MaxChildrenPerNode must be >= 1");
            if (!(LeafUnfoldDays > 0)) throw new ArgumentException("LeafUnfoldDays must be > 0");
            if (!(MaxThickness > 0)) throw new ArgumentException("MaxThickness must be > 0");
            if (TrunkChainSegments < 1) throw new ArgumentException("TrunkChainSegments must be >= 1");
            if (!(AttachTMin >= 0.0) || !(AttachTMax <= 1.0) || !(AttachTMin <= AttachTMax)) throw new ArgumentException("AttachT clamp must be within [0, 1]");
            if (!(SegmentTipTaper >= 0.0) || !(SegmentTipTaper <= 1.0)) throw new ArgumentException("SegmentTipTaper must be in [0, 1]");
            if (TrunkApicalDelayBaseDays < 0 || TrunkApicalDelayStepDays < 0) throw new ArgumentException("Trunk apical delay must be >= 0");
            if (BackBudDays < 0 || BackBudRadiusEdges < 0 || !(BackBudWeightMultiplier >= 0.0)) throw new ArgumentException("Back-budding parameters must be >= 0");
            if (!(TrunkChainTaperRatio > 0.0) || !(TrunkChainDepthScaleFactor >= 0.0) || !(PrimaryThicknessRatio > 0.0))
                throw new ArgumentException("Trunk-chain taper parameters must be positive");
            if (!(PrimaryLengthFactor > 0.0) || !(CrownLateralLengthFactor > 0.0) || !(DomeWeightMultiplier >= 0.0))
                throw new ArgumentException("Primary / dome factors must be positive");
            if (!(CrownLateralSlotT0 >= 0.0) || !(CrownLateralSlotT0 <= 1.0)) throw new ArgumentException("CrownLateralSlotT0 must be in [0, 1]");
            if (!(TrunkLeanOffsetMaxDeg >= 0.0) || !(TrunkLeanOffsetMaxDeg <= MaxShootWorldAngleDeg)) throw new ArgumentException("TrunkLeanOffsetMaxDeg must be in [0, MaxShootWorldAngleDeg]");
            if (!(ApicalDominanceWeight >= 0.0) || !(TierWeight >= 0.0) || !(PadWeight >= 0.0) || !(InteriorWeight >= 0.0))
                throw new ArgumentException("Role weights must be >= 0");
        }
    }
}
