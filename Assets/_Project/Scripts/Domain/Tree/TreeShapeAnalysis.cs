using System;
using PocketArbor.Domain.Growth;

namespace PocketArbor.Domain.Tree
{
    /// <summary>
    /// [MODEL-derived] bonsai-shape terms of BALANCE §1.2.0 / §1.3.4 / §1.3.5, computed from a node table in O(n) with
    /// forward and reverse passes over the topological order (ADR-0003). Stores nothing in the tree; owns preallocated buffers,
    /// so <see cref="Analyze(ReadOnlySpan{NodeData}, in ShapeAnalysisParams)"/> allocates nothing. Results are valid until the next call.
    /// Shared by growth (parent roles, §1.2.7), the preview / PA-103 renderer (foliage pads) and PA-104 hit-testing.
    /// Not thread-safe.
    /// </summary>
    public sealed class TreeShapeAnalysis
    {
        const int NoChildren = int.MaxValue;

        /// <summary><see cref="BackBudDist"/> when no fresh cut lies below the node.</summary>
        public const int NoFreshCut = int.MaxValue;

        readonly int[] _order = new int[TreeGraph.Capacity];
        readonly int[] _tipDistance = new int[TreeGraph.Capacity];
        readonly int[] _childCount = new int[TreeGraph.Capacity];
        readonly int[] _apicalChild = new int[TreeGraph.Capacity];
        readonly int[] _padId = new int[TreeGraph.Capacity];
        readonly int[] _padSize = new int[TreeGraph.Capacity];
        readonly int[] _padSizeByRoot = new int[TreeGraph.Capacity];
        readonly bool[] _leafy = new bool[TreeGraph.Capacity];
        readonly bool[] _trunkChain = new bool[TreeGraph.Capacity];
        readonly bool[] _crownAxis = new bool[TreeGraph.Capacity];
        readonly bool[] _primary = new bool[TreeGraph.Capacity];
        readonly int[] _axisRemain = new int[TreeGraph.Capacity];
        readonly int[] _backBudDist = new int[TreeGraph.Capacity];
        readonly bool[] _inPad = new bool[TreeGraph.Capacity];

        /// <summary>Number of analysed nodes.</summary>
        public int Count { get; private set; }

        /// <summary>Number of foliage pads (connected components of leafy nodes).</summary>
        public int PadCount { get; private set; }

        /// <summary>Number of trunk-chain nodes present (k = 0 … TrunkChainLength − 1).</summary>
        public int TrunkChainLength { get; private set; }

        public int TrunkChainSegments { get; private set; }
        public int PadDepth { get; private set; }

        /// <summary>BALANCE §1.2.0: TierPhase = (randomSeed &amp; 1) == 0 ? +1 : −1 (genome bit, no RNG).</summary>
        public static int TierPhase(uint seed) => (seed & 1u) == 0u ? 1 : -1;

        /// <summary>ord(i): number of lateral branchings on the path from the root (O(depth), allocation-free).</summary>
        public static int OrderOf(ReadOnlySpan<NodeData> nodes, int index)
        {
            int order = 0;
            while (index > 0)
            {
                if (nodes[index].BranchSide != 0) order++;
                index = nodes[index].ParentIndex;
            }
            return order;
        }

        public void Analyze(TreeGraph tree, GrowthConfig config, SpeciesConfig species)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (species == null) throw new ArgumentNullException(nameof(species));
            Analyze(tree.Nodes, Params(tree, config, species));
        }

        /// <summary>Parameters of <paramref name="tree"/> on its current GrowthDayIndex.</summary>
        public static ShapeAnalysisParams Params(TreeGraph tree, GrowthConfig config, SpeciesConfig species) =>
            new ShapeAnalysisParams(config.TrunkChainSegments, species.PadDepth, tree.Growth.GrowthDayIndex, config.BackBudDays, config.BackBudRadiusEdges);

        /// <summary>Analysis without back-budding context: <see cref="BackBudDist"/> is <see cref="NoFreshCut"/> everywhere.</summary>
        public void Analyze(ReadOnlySpan<NodeData> nodes, int trunkChainSegments, int padDepth) =>
            Analyze(nodes, new ShapeAnalysisParams(trunkChainSegments, padDepth));

        public void Analyze(ReadOnlySpan<NodeData> nodes, in ShapeAnalysisParams p)
        {
            int trunkChainSegments = p.TrunkChainSegments;
            int padDepth = p.PadDepth;
            int growthDayIndex = p.GrowthDayIndex;
            int backBudDays = p.BackBudDays;
            int count = nodes.Length;
            if (count > TreeGraph.Capacity) throw new ArgumentException("node table exceeds TreeGraph.Capacity", nameof(nodes));
            Count = count;
            TrunkChainSegments = trunkChainSegments;
            PadDepth = padDepth;

            for (int i = 0; i < count; i++)
            {
                _childCount[i] = 0;
                _apicalChild[i] = -1;
                _tipDistance[i] = NoChildren;
                _backBudDist[i] = NoFreshCut;
                _padSizeByRoot[i] = 0;
            }

            // Forward pass (parents precede children): order, roles, child counts.
            int chainLength = 0;
            for (int i = 0; i < count; i++)
            {
                int parent = nodes[i].ParentIndex;
                bool lateral = nodes[i].BranchSide != 0;
                if (parent < 0)
                {
                    _order[i] = 0;
                }
                else
                {
                    _order[i] = _order[parent] + (lateral ? 1 : 0);
                    _childCount[parent]++;
                    if (!lateral && _apicalChild[parent] < 0) _apicalChild[parent] = i;
                }

                bool axis = _order[i] == 0;
                _trunkChain[i] = axis && nodes[i].Depth < trunkChainSegments;
                _crownAxis[i] = axis && !_trunkChain[i];
                _primary[i] = lateral && parent >= 0 && _trunkChain[parent];
                if (_trunkChain[i]) chainLength++;
            }
            TrunkChainLength = chainLength;

            // Reverse pass (children precede parents): TipDistance = 0 without children, else 1 + min over children;
            // §1.3.4 AxisRemain = 0 without an apical child, else 1 + AxisRemain(apical child);
            // §1.3.5 BackBudDist = 0 at a node with a fresh cut, else 1 + min over children (NoFreshCut if none below).
            for (int i = count - 1; i >= 0; i--)
            {
                if (_tipDistance[i] == NoChildren) _tipDistance[i] = 0;
                int apical = _apicalChild[i];
                _axisRemain[i] = apical < 0 ? 0 : _axisRemain[apical] + 1;
                int lastCut = nodes[i].LastCutDay;
                if (lastCut >= 0 && growthDayIndex - lastCut < backBudDays) _backBudDist[i] = 0;

                int parent = nodes[i].ParentIndex;
                if (parent < 0) continue;
                if (_tipDistance[i] + 1 < _tipDistance[parent]) _tipDistance[parent] = _tipDistance[i] + 1;
                if (_backBudDist[i] != NoFreshCut && _backBudDist[i] + 1 < _backBudDist[parent]) _backBudDist[parent] = _backBudDist[i] + 1;
            }

            // §1.3.4 InPad, IsLeafy and pads: forward pass — a pad's smallest index is its topmost node (topological order).
            int pads = 0;
            for (int i = 0; i < count; i++)
            {
                int parent = nodes[i].ParentIndex;
                bool inPad = EffectiveOrder(i) >= 1 && (_axisRemain[i] < padDepth || (parent >= 0 && _inPad[parent]));
                _inPad[i] = inPad;
                bool leafy = nodes[i].HasLeaves && (inPad || (_trunkChain[i] && _childCount[i] == 0));
                _leafy[i] = leafy;
                if (!leafy)
                {
                    _padId[i] = -1;
                    continue;
                }
                if (parent >= 0 && _leafy[parent])
                {
                    _padId[i] = _padId[parent];
                }
                else
                {
                    _padId[i] = i;
                    pads++;
                }
                _padSizeByRoot[_padId[i]]++;
            }
            PadCount = pads;
            for (int i = 0; i < count; i++) _padSize[i] = _padId[i] >= 0 ? _padSizeByRoot[_padId[i]] : 0;
        }

        /// <summary>ord(i) — lateral branchings from the root.</summary>
        public int Order(int index) => _order[index];

        /// <summary>ordEff(i): ord, but 1 for the crown axis.</summary>
        public int EffectiveOrder(int index) => _crownAxis[index] ? 1 : _order[index];

        /// <summary>ord = 0 ∧ Depth &lt; TrunkChainSegments (segment index k = Depth).</summary>
        public bool IsTrunkChain(int index) => _trunkChain[index];

        /// <summary>ord = 0 ∧ Depth ≥ TrunkChainSegments — the leader above the last trunk segment (apex dome).</summary>
        public bool IsCrownAxis(int index) => _crownAxis[index];

        /// <summary>Lateral child of a trunk-chain node (a tier).</summary>
        public bool IsPrimary(int index) => _primary[index];

        /// <summary>TipDistance (§1.2.0): 0 without children, else 1 + min over children. Not used for foliage/roles since §1.3.4.</summary>
        public int TipDistance(int index) => _tipDistance[index];

        public int ChildCount(int index) => _childCount[index];

        /// <summary>Index of the apical (BranchSide == 0) child, or −1.</summary>
        public int ApicalChild(int index) => _apicalChild[index];

        /// <summary>§1.3.4 AxisRemain: number of apical continuations left on this node's axis (0 at the axis tip).</summary>
        public int AxisRemain(int index) => _axisRemain[index];

        /// <summary>§1.3.4 InPad: ordEff ≥ 1 ∧ (AxisRemain &lt; PadDepth ∨ InPad(parent)) — the pad zone of an axis and all it bears.</summary>
        public bool InPad(int index) => _inPad[index];

        /// <summary>§1.3.5 Edges from this node down to the nearest node with a fresh cut (itself included); <see cref="NoFreshCut"/> if none.</summary>
        public int BackBudDist(int index) => _backBudDist[index];

        /// <summary>Interior node of a branch (§1.3.4): ordEff ≥ 1 ∧ ¬InPad.</summary>
        public bool IsInterior(int index) => EffectiveOrder(index) >= 1 && !_inPad[index];

        /// <summary>Pad-role node (§1.3.4): InPad.</summary>
        public bool IsPadNode(int index) => _inPad[index];

        public bool IsLeafy(int index) => _leafy[index];

        /// <summary>Smallest index of the node's pad, or −1 if the node is not leafy.</summary>
        public int PadId(int index) => _padId[index];

        /// <summary>Node count of the node's pad (0 if not leafy).</summary>
        public int PadSize(int index) => _padSize[index];
    }
}
