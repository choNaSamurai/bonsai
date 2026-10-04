using System;

namespace PocketArbor.Domain.Tree
{
    /// <summary>
    /// Flat, preallocated, topologically ordered node table (ADR-0003). Parents always precede children.
    /// Read access is allocation-free via <see cref="Nodes"/>. Mutations go only through domain operations
    /// (<see cref="TreeFactory"/>, <see cref="TreePruning"/>, <c>GrowthSimulator</c>), which bump <see cref="Version"/>.
    /// </summary>
    public sealed class TreeGraph
    {
        /// <summary>Hard storage ceiling (BALANCE §1 MaxNodes = 200). GrowthConfig.MaxNodes must not exceed it.</summary>
        public const int Capacity = 200;

        readonly NodeData[] _nodes = new NodeData[Capacity];
        readonly bool[] _removedScratch = new bool[Capacity];
        readonly int[] _remapScratch = new int[Capacity];

        GrowthState _growth;

        internal TreeGraph(TreeGenome genome)
        {
            Genome = genome;
        }

        public TreeGenome Genome { get; }

        public int Count { get; private set; }

        /// <summary>Incremented on every node-table mutation (render rebuilds only when it changes).</summary>
        public int Version { get; private set; }

        /// <summary>Id the next created node receives.</summary>
        public int NextId { get; private set; }

        public ReadOnlySpan<NodeData> Nodes => new ReadOnlySpan<NodeData>(_nodes, 0, Count);

        public GrowthState Growth => _growth;

        public bool IsGrowthFrozen => _growth.Frozen;

        /// <summary>
        /// Freeze guard entry point (onboarding / Dormancy). While frozen, hourly simulation accumulates nothing.
        /// Does not touch nodes, so <see cref="Version"/> is unchanged.
        /// </summary>
        public void SetGrowthFrozen(bool frozen)
        {
            _growth.Frozen = frozen;
        }

        /// <summary>Index of the node with <paramref name="id"/>, or -1.</summary>
        public int IndexOf(int id)
        {
            for (int i = 0; i < Count; i++)
            {
                if (_nodes[i].Id == id) return i;
            }
            return -1;
        }

        public int ChildCount(int index)
        {
            int children = 0;
            for (int i = index + 1; i < Count; i++)
            {
                if (_nodes[i].ParentIndex == index) children++;
            }
            return children;
        }

        /// <summary>Sum of CurrentAngle from the trunk down to <paramref name="index"/> (radians from vertical).</summary>
        public double WorldAngle(int index)
        {
            double angle = 0.0;
            while (index >= 0)
            {
                angle += _nodes[index].CurrentAngle;
                index = _nodes[index].ParentIndex;
            }
            return angle;
        }

        // ---- Domain-internal mutation API --------------------------------------------------------

        internal ref NodeData NodeAt(int index)
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            return ref _nodes[index];
        }

        internal ref GrowthState GrowthRef => ref _growth;

        internal void MarkChanged()
        {
            Version = unchecked(Version + 1);
        }

        /// <summary>Appends a node (keeps topological order: the parent already exists). Assigns the id.</summary>
        internal int Append(NodeData node)
        {
            if (Count >= Capacity) throw new InvalidOperationException("TreeGraph is full");
            if (Count == 0 ? node.ParentIndex != -1 : (node.ParentIndex < 0 || node.ParentIndex >= Count))
            {
                throw new ArgumentException("Parent must precede the node (topological order)", nameof(node));
            }

            node.Id = NextId;
            NextId = unchecked(NextId + 1);
            int index = Count;
            _nodes[index] = node;
            Count = index + 1;
            return index;
        }

        /// <summary>
        /// Removes the node at <paramref name="index"/> with its whole subtree, compacting the table in O(n)
        /// while preserving the relative (topological) order and remapping ParentIndex. Returns removed count.
        /// </summary>
        internal int RemoveSubtreeAt(int index)
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));

            int oldCount = Count;
            for (int i = 0; i < oldCount; i++) _removedScratch[i] = false;
            _removedScratch[index] = true;
            for (int i = index + 1; i < oldCount; i++)
            {
                int parent = _nodes[i].ParentIndex;
                _removedScratch[i] = parent >= 0 && _removedScratch[parent];
            }

            int write = 0;
            for (int read = 0; read < oldCount; read++)
            {
                if (_removedScratch[read])
                {
                    _remapScratch[read] = -1;
                    continue;
                }

                _remapScratch[read] = write;
                NodeData node = _nodes[read];
                if (node.ParentIndex >= 0) node.ParentIndex = _remapScratch[node.ParentIndex];
                _nodes[write++] = node;
            }

            for (int i = write; i < oldCount; i++) _nodes[i] = default;
            Count = write;
            return oldCount - write;
        }

        // ---- Snapshot boundary -------------------------------------------------------------------

        public TreeSnapshot ToSnapshot()
        {
            var nodes = new NodeData[Count];
            Array.Copy(_nodes, nodes, Count);
            return new TreeSnapshot(Genome, _growth, Version, NextId, nodes);
        }

        /// <summary>Restores a graph. Throws <see cref="ArgumentException"/> if the snapshot violates invariants.</summary>
        public static TreeGraph FromSnapshot(TreeSnapshot snapshot)
        {
            if (!TryFromSnapshot(snapshot, out var graph, out var error)) throw new ArgumentException(error, nameof(snapshot));
            return graph;
        }

        public static bool TryFromSnapshot(TreeSnapshot snapshot, out TreeGraph graph, out string error)
        {
            graph = null;
            if (snapshot == null)
            {
                error = "snapshot is null";
                return false;
            }
            if (!snapshot.Validate(out error)) return false;

            graph = new TreeGraph(snapshot.Genome)
            {
                Version = snapshot.Version,
                NextId = snapshot.NextId,
            };
            graph._growth = snapshot.Growth;
            ReadOnlySpan<NodeData> nodes = snapshot.Nodes;
            for (int i = 0; i < nodes.Length; i++) graph._nodes[i] = nodes[i];
            graph.Count = nodes.Length;
            return true;
        }
    }
}
