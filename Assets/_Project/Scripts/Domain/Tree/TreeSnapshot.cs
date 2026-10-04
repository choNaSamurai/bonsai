using System;

namespace PocketArbor.Domain.Tree
{
    /// <summary>
    /// Immutable copy of a <see cref="TreeGraph"/> for save, chronicle and tests (ADR-0003).
    /// The node array is private; <see cref="Nodes"/> exposes read-only elements.
    /// </summary>
    public sealed class TreeSnapshot
    {
        readonly NodeData[] _nodes;

        public TreeSnapshot(TreeGenome genome, GrowthState growth, int version, int nextId, NodeData[] nodes)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            Genome = genome;
            Growth = growth;
            Version = version;
            NextId = nextId;
            _nodes = (NodeData[])nodes.Clone();
        }

        public TreeGenome Genome { get; }
        public GrowthState Growth { get; }
        public int Version { get; }
        public int NextId { get; }
        public int NodeCount => _nodes.Length;
        public ReadOnlySpan<NodeData> Nodes => _nodes;

        /// <summary>Structural invariants of ADR-0003: trunk at 0, parents precede children, depth = parent + 1, unique ids.</summary>
        public bool Validate(out string error)
        {
            if (_nodes.Length == 0 || _nodes.Length > TreeGraph.Capacity)
            {
                error = "node count out of range: " + _nodes.Length;
                return false;
            }
            if (_nodes[0].ParentIndex != -1 || _nodes[0].Depth != 0)
            {
                error = "node 0 must be the trunk";
                return false;
            }
            for (int i = 1; i < _nodes.Length; i++)
            {
                int parent = _nodes[i].ParentIndex;
                if (parent < 0 || parent >= i)
                {
                    error = "node " + i + " breaks topological order";
                    return false;
                }
                if (_nodes[i].Depth != _nodes[parent].Depth + 1)
                {
                    error = "node " + i + " has inconsistent depth";
                    return false;
                }
            }
            for (int i = 0; i < _nodes.Length; i++)
            {
                if (_nodes[i].Id < 0 || _nodes[i].Id >= NextId)
                {
                    error = "node " + i + " has id outside [0, NextId)";
                    return false;
                }
                for (int j = i + 1; j < _nodes.Length; j++)
                {
                    if (_nodes[i].Id == _nodes[j].Id)
                    {
                        error = "duplicate id " + _nodes[i].Id;
                        return false;
                    }
                }
            }
            error = null;
            return true;
        }

        /// <summary>Bit-exact equality of every field (doubles compared by bit pattern).</summary>
        public bool BitwiseEquals(TreeSnapshot other)
        {
            if (other == null) return false;
            if (!Genome.Equals(other.Genome) || Version != other.Version || NextId != other.NextId) return false;
            if (!GrowthEquals(Growth, other.Growth)) return false;
            if (_nodes.Length != other._nodes.Length) return false;
            for (int i = 0; i < _nodes.Length; i++)
            {
                if (!NodeEquals(in _nodes[i], in other._nodes[i])) return false;
            }
            return true;
        }

        static bool GrowthEquals(GrowthState a, GrowthState b)
        {
            return a.GrowthHourCounter == b.GrowthHourCounter && a.GrowthDayIndex == b.GrowthDayIndex
                && Same(a.ShootCredit, b.ShootCredit) && Same(a.SumGrowthFactor, b.SumGrowthFactor)
                && Same(a.SumMoistureFactor, b.SumMoistureFactor) && a.Frozen == b.Frozen;
        }

        static bool NodeEquals(in NodeData a, in NodeData b)
        {
            return a.Id == b.Id && a.ParentIndex == b.ParentIndex && a.Depth == b.Depth
                && Same(a.Length, b.Length) && Same(a.TargetLength, b.TargetLength) && Same(a.Thickness, b.Thickness)
                && Same(a.BaseAngle, b.BaseAngle) && Same(a.CurrentAngle, b.CurrentAngle) && Same(a.Curvature, b.Curvature)
                && a.AgeDays == b.AgeDays && a.Status == b.Status
                && Same(a.LeafScale, b.LeafScale) && Same(a.FlowerStage, b.FlowerStage)
                && a.WiredAtUtcMs == b.WiredAtUtcMs && a.WireProgressDays == b.WireProgressDays
                && a.BranchSide == b.BranchSide;
        }

        static bool Same(double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);

        static bool Same(float a, float b) => BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
    }
}
