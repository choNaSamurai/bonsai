using PocketArbor.Domain.Tree;

namespace PocketArbor.Domain.Growth
{
    /// <summary>Per-species balance (BALANCE §3). Plain C#; ScriptableObject wrapper is PA-007.</summary>
    public sealed class SpeciesConfig
    {
        public SpeciesId Id;

        /// <summary>Base evaporation per day (used by SoilSimulation, PA-202).</summary>
        public double KBase;

        public double BranchAngleMeanDeg;
        public double BranchAngleSpreadDeg;
        public double CurvatureSpread;

        public static SpeciesConfig Kuromatsu() => new SpeciesConfig
        {
            Id = SpeciesId.Kuromatsu, KBase = 0.15, BranchAngleMeanDeg = 45.0, BranchAngleSpreadDeg = 15.0, CurvatureSpread = 0.45,
        };

        public static SpeciesConfig Momiji() => new SpeciesConfig
        {
            Id = SpeciesId.Momiji, KBase = 0.28, BranchAngleMeanDeg = 30.0, BranchAngleSpreadDeg = 10.0, CurvatureSpread = 0.20,
        };

        public static SpeciesConfig Yamazakura() => new SpeciesConfig
        {
            Id = SpeciesId.Yamazakura, KBase = 0.22, BranchAngleMeanDeg = 35.0, BranchAngleSpreadDeg = 20.0, CurvatureSpread = 0.30,
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
