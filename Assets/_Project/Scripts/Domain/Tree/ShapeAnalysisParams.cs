namespace PocketArbor.Domain.Tree
{
    /// <summary>
    /// Inputs of <see cref="TreeShapeAnalysis"/> besides the node table (ADR-0003 rev.3 §4). <see cref="GrowthDayIndex"/> is explicit
    /// because <c>BackBudDist</c> changes with the day even without node mutations: a result is valid for the pair (Version, GrowthDayIndex).
    /// </summary>
    public readonly struct ShapeAnalysisParams
    {
        public readonly int TrunkChainSegments;
        public readonly int PadDepth;
        public readonly int GrowthDayIndex;

        /// <summary>A cut is fresh while GrowthDayIndex − LastCutDay &lt; BackBudDays (0 = no back-budding context).</summary>
        public readonly int BackBudDays;

        /// <summary>Radius of the back-budding zone, edges (consumers compare <c>BackBudDist ≤ BackBudRadiusEdges</c>).</summary>
        public readonly int BackBudRadiusEdges;

        /// <summary>⚑T BALANCE §1.6.1(а): genome seed (TierPhase → the slot of a chain lateral by its side) and whether slot 0 of the chain top is the
        /// crown shoulder (GrowthConfig.CrownShoulderSlotT &gt; 0). Defaults: seed 0, no shoulder.</summary>
        public readonly uint Seed;
        public readonly bool CrownShoulder;

        public ShapeAnalysisParams(int trunkChainSegments, int padDepth, int growthDayIndex = 0, int backBudDays = 0, int backBudRadiusEdges = 0,
            uint seed = 0u, bool crownShoulder = false)
        {
            TrunkChainSegments = trunkChainSegments;
            PadDepth = padDepth;
            GrowthDayIndex = growthDayIndex;
            BackBudDays = backBudDays;
            BackBudRadiusEdges = backBudRadiusEdges;
            Seed = seed;
            CrownShoulder = crownShoulder;
        }
    }
}
