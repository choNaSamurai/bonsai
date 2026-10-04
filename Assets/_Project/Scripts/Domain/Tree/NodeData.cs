namespace PocketArbor.Domain.Tree
{
    /// <summary>
    /// One row of the flat node table (ADR-0003, ARCHITECTURE §4). Angles are radians; lengths and
    /// thicknesses are abstract biological units.
    /// </summary>
    public struct NodeData
    {
        /// <summary>Stable, monotonically assigned id (not the index).</summary>
        public int Id;

        /// <summary>Index of the parent in the topologically ordered table; -1 for the trunk.</summary>
        public int ParentIndex;

        public byte Depth;
        public double Length;

        /// <summary>Length the shoot elongates towards (BALANCE §1.1; proposal to ADR-0003).</summary>
        public double TargetLength;

        public double Thickness;

        /// <summary>Angle relative to the parent axis, radians.</summary>
        public double BaseAngle;

        /// <summary>Angle including wire (PA-105B). Equals <see cref="BaseAngle"/> without wire.</summary>
        public double CurrentAngle;

        /// <summary>Bezier bend in [-1, 1] (BALANCE §1: control-point offset = Curvature × 0.25 × Length).</summary>
        public double Curvature;

        /// <summary>Growth days lived (calendar growth days, independent of G).</summary>
        public int AgeDays;

        public NodeStatus Status;

        /// <summary>0..1.</summary>
        public float LeafScale;

        /// <summary>0..1.</summary>
        public float FlowerStage;

        /// <summary>0 = no wire (PA-105B).</summary>
        public long WiredAtUtcMs;

        /// <summary>0..14 (PA-105B).</summary>
        public byte WireProgressDays;

        /// <summary>
        /// 0 = trunk or apical shoot; -1 / +1 = lateral shoot on that side of the parent axis.
        /// Needed for the alternating-sign rule of BALANCE §1.1 step 6.4 (proposal to ADR-0003).
        /// </summary>
        public sbyte BranchSide;

        /// <summary>BALANCE §1.1: HasLeaves ≡ Depth ≥ 1 ∧ Status ∈ {Growing, Mature}. Not persisted.</summary>
        public bool HasLeaves => Depth >= 1 && (Status == NodeStatus.Growing || Status == NodeStatus.Mature);

        public bool IsLateral => BranchSide != 0;
    }
}
