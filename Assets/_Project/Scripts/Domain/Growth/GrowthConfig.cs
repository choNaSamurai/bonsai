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
        public double TrunkTargetLength = 48.0;
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
        public double TrunkBaseSlotT0 = 0.70;
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
        public double TrunkSwayDecay = 0.85;
        public double TrunkSwayJitterDeg = 3.0;
        public double TrunkCurvatureJitter = 0.10;

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
            if (!(ApicalDominanceWeight >= 0.0) || !(TierWeight >= 0.0) || !(PadWeight >= 0.0) || !(InteriorWeight >= 0.0))
                throw new ArgumentException("Role weights must be >= 0");
        }
    }
}
