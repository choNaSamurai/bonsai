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

        public ShapeAnalysisParams(int trunkChainSegments, int padDepth, int growthDayIndex = 0, int backBudDays = 0, int backBudRadiusEdges = 0)
        {
            TrunkChainSegments = trunkChainSegments;
            PadDepth = padDepth;
            GrowthDayIndex = growthDayIndex;
            BackBudDays = backBudDays;
            BackBudRadiusEdges = backBudRadiusEdges;
        }
    }
}
