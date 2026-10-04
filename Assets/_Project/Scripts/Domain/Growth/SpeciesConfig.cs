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

        /// <summary>Moyogi S-curve amplitude of the trunk chain (BALANCE §1.2.3). 0 = straight trunk (chokkan).</summary>
        public double TrunkSwayDeg;

        /// <summary>How much each branch continuation leans towards horizontal, 0..1 (BALANCE §1.2.6).</summary>
        public double BranchDroop;

        /// <summary>How many levels from a branch tip carry foliage (BALANCE §1.2.5, MODEL-derived).</summary>
        public int PadDepth;

        public static SpeciesConfig Kuromatsu() => new SpeciesConfig
        {
            Id = SpeciesId.Kuromatsu, KBase = 0.15, BranchAngleMeanDeg = 30.0, BranchAngleSpreadDeg = 10.0, CurvatureSpread = 0.45,
            PrimaryAngleMeanDeg = 80.0, PrimaryAngleSpreadDeg = 6.0, TrunkSwayDeg = 14.0, BranchDroop = 0.50, PadDepth = 1,
        };

        public static SpeciesConfig Momiji() => new SpeciesConfig
        {
            Id = SpeciesId.Momiji, KBase = 0.28, BranchAngleMeanDeg = 30.0, BranchAngleSpreadDeg = 10.0, CurvatureSpread = 0.20,
            PrimaryAngleMeanDeg = 62.0, PrimaryAngleSpreadDeg = 10.0, TrunkSwayDeg = 9.0, BranchDroop = 0.20, PadDepth = 2,
        };

        public static SpeciesConfig Yamazakura() => new SpeciesConfig
        {
            Id = SpeciesId.Yamazakura, KBase = 0.22, BranchAngleMeanDeg = 35.0, BranchAngleSpreadDeg = 20.0, CurvatureSpread = 0.30,
            PrimaryAngleMeanDeg = 70.0, PrimaryAngleSpreadDeg = 10.0, TrunkSwayDeg = 12.0, BranchDroop = 0.30, PadDepth = 2,
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
