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

        /// <summary>
        /// Fraction of the parent length where this shoot starts, 0..1 (BALANCE §1.2.1, proposal to ADR-0003 / save
        /// ADR-0008). Apical shoots and the trunk: 1.0; laterals: [AttachTMin, AttachTMax]. Relative, so the attachment
        /// point moves with the parent's elongation. Missing in old data → 1.0 (deserializer's responsibility, PA-205).
        /// </summary>
        public double AttachT;

        /// <summary>No cut recorded (<see cref="LastCutDay"/> value; also the value of <c>default(NodeData)</c>).</summary>
        public const int NoCut = -1;

        // Stored as LastCutDay + 1 so that default(NodeData) and rows created without the field mean "no cut" (−1),
        // not "cut on day 0" (TD-030).
        int _lastCutDayPlusOne;

        /// <summary>
        /// GrowthDayIndex of the last cut of a child of this node (BALANCE §1.3.5; proposal to ADR-0003 rev.3 / save ADR-0008):
        /// drives back-budding and the callus age. −1 (<see cref="NoCut"/>) = never cut; missing in old data → −1.
        /// </summary>
        public int LastCutDay
        {
            readonly get => _lastCutDayPlusOne - 1;
            set => _lastCutDayPlusOne = value < NoCut ? 0 : unchecked(value + 1);
        }

        /// <summary>BALANCE §1.1: HasLeaves ≡ Depth ≥ 1 ∧ Status ∈ {Growing, Mature}. Not persisted.</summary>
        public bool HasLeaves => Depth >= 1 && (Status == NodeStatus.Growing || Status == NodeStatus.Mature);

        public bool IsLateral => BranchSide != 0;

        /// <summary>Depth ≥ 1 and not lateral.</summary>
        public bool IsApical => Depth >= 1 && BranchSide == 0;
    }
}
