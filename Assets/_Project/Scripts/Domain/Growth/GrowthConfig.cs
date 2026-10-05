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
        public double TrunkTargetLength = 38.0; // PA-102P tuning 36 (TD-031) → PA-102R 38 (range 34–42, AC10 lowest tier from y_rim): TD-047
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
        /// <summary>⚑Q BALANCE §1.4.1(в): 0.70 → 0.78 Proposed; PA-102Q tuning → 0.74 (TD-042); ⚑R §1.5.3(а) → 0.70 (range 0.66–0.76).</summary>
        public double TrunkBaseSlotT0 = 0.72; // PA-102R tuning (Proposed 0.70, range 0.66–0.76): TD-047
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
        /// <summary>⚑P BALANCE §1.3.1: 0.85 → 0.88. ⚑R §1.5.4(а): replaced by SpeciesConfig.TrunkSwayProfile for k ≤ 4; used only for
        /// chain segments beyond the profile (TrunkChainSegments > 5) and for a species without a profile.</summary>
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
        public double CrownAxisLengthFactor = 0.70; // ⚑T BALANCE §1.6.2(г) Proposed 0.60; PA-102T tuning → 0.70 (range 0.50–0.70; dome above #6): TD-075

        /// <summary>§1.3.1 S-curve amplitude gain of |bendBias| (⚑R §1.5.4(а): A_k = TrunkSwayDeg·(1 + gain·|bb|)·Profile[k]·(1 + Irregularity·h_k)).</summary>
        public double TrunkSwayBendGain = 0.5;

        /// <summary>§1.3.1 Base lean of #0: BaseAngle(#0) = lean + TierPhase·TrunkBaseSwayRatio·A_1.</summary>
        public double TrunkBaseSwayRatio = 0.40; // ⚑R BALANCE §1.5.4(а): 0.20 → 0.40 (range 0.30–0.50)

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

        /// <summary>§1.4.4: × on the TargetLength of crown-axis laterals and their apical continuations (apex dome).</summary>
        public double CrownLateralLengthFactor = 1.5; // PA-102Q tuning (range 1.0–1.5, Proposed 1.25): TD-042

        /// <summary>§1.4.4: × on the selection weight of the dome subtree (crown-axis nodes and all descendants); Σw (rate) unchanged.</summary>
        public double DomeWeightMultiplier = 2.0; // ⚑R BALANCE §1.5.6: 3.0 → 2.0 (range 1.5–3.0)

        /// <summary>§1.4.4 lever: slot 0 of crown-axis laterals (other non-#0 parents keep LateralSlotT0). ⚑R §1.5.3(г): 0.45 → 0.65 (range 0.45–0.70).</summary>
        public double CrownLateralSlotT0 = 0.70; // PA-102R tuning (Proposed 0.65, range 0.45–0.70): TD-047

        /// <summary>§1.4.7: |TrunkLeanOffsetDeg| λ of a ShapeStyleProfile is clamped to this (owner 2026-10-05: Shakan ≤ 30°).</summary>
        public double TrunkLeanOffsetMaxDeg = 30.0;

        // ---- Ideal, iteration 3 (BALANCE §1.5, PA-102R) --------------------------------------------

        /// <summary>⚑R §1.5.2(а): × on the TargetLength of every node of a primary's subtree except the primary's first node
        /// (together with the decay exponent (d − 1) − PadTierDecayCompensation·k_t). Range 0.80–1.00.</summary>
        public double PadLengthFactor = 0.85; // PA-102R tuning (Proposed 0.92, range 0.80–1.00): TD-047

        /// <summary>⚑R §1.5.4(а): h_k ∈ [−1, 1) (hash, not PCG) scales A_k by (1 + this·h_k). Range 0–0.30.</summary>
        public double TrunkSwayIrregularity = 0.20;

        /// <summary>⚑R §1.5.4(а): Curvature of chain segment k = TierPhase·(−1)^k·this·σ + tri(0, TrunkCurvatureJitter)·max(σ, min)
        /// (bulge towards the side segment k leans to — the outer side of its top bend). Range 0–0.25.</summary>
        public double TrunkBowCurvature = 0.15;

        /// <summary>⚑R §1.5.4(б): g of the reach compensation of a primary's first node
        /// L' = clamp(L − g·o / max(|sin θ_p|, PrimaryReachSinMin), PrimaryReachMinRatio·L, PrimaryReachMaxRatio·L). 0 = off; range 0–1.2.</summary>
        public double PrimaryReachCompensation = 1.0; // PA-102R tuning (Proposed 0.8, range 0–1.2): TD-047
        public double PrimaryReachSinMin = 0.5;
        public double PrimaryReachMinRatio = 0.6;
        public double PrimaryReachMaxRatio = 1.4;

        /// <summary>⚑R §1.5.6: role weight of a crown-axis node with a free lateral slot (replaces Pad / Interior for it); × DomeWeightMultiplier.
        /// Range 1.0–2.5.</summary>
        public double DomeLateralWeight = 1.5;

        // ---- Ideal, iteration 4 (BALANCE §1.6, PA-102T) --------------------------------------------

        /// <summary>⚑T §1.6.1(б): a hungry tier group (N_g &lt; T_g) gets at least TierHungryShare·d_g of the whole selection mass
        /// (d_g = min(1, TierDeficitGain·(T_g − N_g)/T_g)). 0 = off. Range 0.20–0.40.</summary>
        public double TierHungryShare = 0.30;

        /// <summary>⚑T §1.6.1(б): full support while N_g &lt; T_g / gain. Range 1.5–3.0.</summary>
        public double TierDeficitGain = 2.0;

        /// <summary>⚑T §1.6.1(б): × on a fed group (T_g ≤ N_g &lt; TierCapRatio·T_g). 1 = off. Range 0.20–0.50.</summary>
        public double TierFedFactor = 0.30;

        /// <summary>⚑T §1.6.1(б): a group with N_g ≥ TierCapRatio·T_g is over its ceiling. Range 1.20–1.50.</summary>
        public double TierCapRatio = 1.30;

        /// <summary>⚑T §1.6.1(б): × on a group over its ceiling (not 0: Wild still reaches MaxNodes). 1 = off. Range 0–0.15.</summary>
        public double TierCapFactor = 0.05;

        /// <summary>⚑T §1.6.1(в) «pinch»: an ord ≥ 1 node without an apical child and with a fresh cut (GrowthDayIndex − LastCutDay &lt; BackBudDays)
        /// takes only a lateral shoot (RoleWeight 0 while no lateral slot is free). false = PA-102R.</summary>
        public bool PinchLateralOnly = true;

        /// <summary>⚑T §1.6.2(а) «laterals first»: a crown-axis node takes an apical child only once it has this many live laterals (and its apical
        /// delay has passed). 0 = PA-102R. Range 1–2.</summary>
        public int CrownApicalNeedsLaterals = 2;

        /// <summary>⚑T §1.6.2(б) crown shoulder (#7): slot 0 of the chain top is a dome lateral at this AttachT (± AttachTJitter), world angle
        /// CrownLateralAngle*, side −s_k, length × CrownLateralLengthFactor, in the dome subtree. ≤ 0 = off (PA-102R: an intermediate tier).
        /// Range 0.90–0.98.</summary>
        public double CrownShoulderSlotT = 0.95;

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
            if (!(CrownLateralLengthFactor > 0.0) || !(DomeWeightMultiplier >= 0.0) || !(PadLengthFactor > 0.0) || !(DomeLateralWeight >= 0.0))
                throw new ArgumentException("Primary / dome factors must be positive");
            if (!(CrownLateralSlotT0 >= 0.0) || !(CrownLateralSlotT0 <= 1.0)) throw new ArgumentException("CrownLateralSlotT0 must be in [0, 1]");
            if (!(TrunkLeanOffsetMaxDeg >= 0.0) || !(TrunkLeanOffsetMaxDeg <= MaxShootWorldAngleDeg)) throw new ArgumentException("TrunkLeanOffsetMaxDeg must be in [0, MaxShootWorldAngleDeg]");
            if (!(TrunkSwayIrregularity >= 0.0) || !(TrunkSwayIrregularity < 1.0)) throw new ArgumentException("TrunkSwayIrregularity must be in [0, 1)");
            if (!(PrimaryReachCompensation >= 0.0) || !(PrimaryReachSinMin > 0.0) || !(PrimaryReachMinRatio > 0.0) || !(PrimaryReachMaxRatio >= PrimaryReachMinRatio))
                throw new ArgumentException("Primary reach compensation parameters are invalid");
            if (double.IsNaN(TrunkBowCurvature) || double.IsInfinity(TrunkBowCurvature)) throw new ArgumentException("TrunkBowCurvature must be finite");
            if (!(TierHungryShare >= 0.0) || !(TierHungryShare <= 1.0) || !(TierDeficitGain >= 0.0) || !(TierFedFactor >= 0.0) || !(TierCapRatio >= 1.0)
                || !(TierCapFactor >= 0.0))
                throw new ArgumentException("Tier balance parameters are invalid");
            if (CrownApicalNeedsLaterals < 0) throw new ArgumentException("CrownApicalNeedsLaterals must be >= 0");
            if (double.IsNaN(CrownShoulderSlotT) || CrownShoulderSlotT > 1.0) throw new ArgumentException("CrownShoulderSlotT must be <= 1 (<= 0 = off)");
            if (!(ApicalDominanceWeight >= 0.0) || !(TierWeight >= 0.0) || !(PadWeight >= 0.0) || !(InteriorWeight >= 0.0))
                throw new ArgumentException("Role weights must be >= 0");
        }
    }
}
