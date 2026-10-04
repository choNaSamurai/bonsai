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
        public double BaseGrowthProbability = 0.20;
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
        }
    }
}
