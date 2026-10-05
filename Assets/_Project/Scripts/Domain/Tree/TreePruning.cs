using System;
using PocketArbor.Domain.Growth;

namespace PocketArbor.Domain.Tree
{
    public enum PruneStatus : byte
    {
        Pruned = 0,
        NotFound = 1,

        /// <summary>The trunk (depth 0) is untouchable (Phoenix protocol).</summary>
        TrunkProtected = 2,
    }

    public readonly struct PruneResult
    {
        public readonly PruneStatus Status;
        public readonly int RemovedCount;

        /// <summary>Id of the Phoenix bud spawned by this cut, or -1.</summary>
        public readonly int PhoenixNodeId;

        public PruneResult(PruneStatus status, int removedCount, int phoenixNodeId)
        {
            Status = status;
            RemovedCount = removedCount;
            PhoenixNodeId = phoenixNodeId;
        }

        public bool Succeeded => Status == PruneStatus.Pruned;
        public bool PhoenixSpawned => PhoenixNodeId >= 0;
    }

    /// <summary>
    /// Domain cut operation (subtree removal + Phoenix protocol, BALANCE §1.1). Gesture, callus and falling
    /// branch are PA-105A. Never throws for gameplay input; failures are reported in <see cref="PruneResult"/>.
    /// </summary>
    public static class TreePruning
    {
        public static PruneResult Prune(TreeGraph tree, int nodeId, GrowthConfig config)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            if (config == null) throw new ArgumentNullException(nameof(config));

            int index = tree.IndexOf(nodeId);
            if (index < 0) return new PruneResult(PruneStatus.NotFound, 0, -1);
            if (index == 0 || tree.Nodes[index].Depth == 0) return new PruneResult(PruneStatus.TrunkProtected, 0, -1);

            // BALANCE §1.3.5: the parent of the cut node remembers the day of the cut (back-budding, callus).
            // Parents precede children, so compaction does not move it.
            int parent = tree.Nodes[index].ParentIndex;
            int removed = tree.RemoveSubtreeAt(index);
            tree.NodeAt(parent).LastCutDay = tree.Growth.GrowthDayIndex;
            int phoenixId = tree.Count == 1 ? SpawnPhoenix(tree, config) : -1;
            tree.MarkChanged();
            return new PruneResult(PruneStatus.Pruned, removed, phoenixId);
        }

        /// <summary>Phoenix bud: apical child of the trunk, no RNG (BALANCE §1.1; ⚑ §1.2.8: AttachT = 1, thickness ≤ th_cap).</summary>
        static int SpawnPhoenix(TreeGraph tree, GrowthConfig config)
        {
            double angle = ShootBuilder.ClampToPotLip(tree.WorldAngle(0),
                tree.Genome.BendBias * config.ApicalBendScaleDeg * GrowthFormulas.DegToRad, config);
            const int depth = 1;
            int index = tree.Append(new NodeData
            {
                ParentIndex = 0,
                Depth = depth,
                Length = config.PhoenixBudLength,
                TargetLength = config.ShootBaseLength * config.ApicalLengthMultiplier,
                // ⚑Q BALANCE §1.4.1(б): apical child of the ord-0 trunk (species profile not known here → config values).
                Thickness = Math.Min(config.PhoenixBudThickness,
                    Math.Min(GrowthFormulas.MaxThicknessAt(depth, true, config.TrunkChainDepthScaleFactor, config),
                        GrowthFormulas.AxisChildThicknessCap(tree.Nodes[0].Thickness, 1.0, true, config.TrunkChainTaperRatio, config.PrimaryThicknessRatio, config))),
                BaseAngle = angle,
                CurrentAngle = angle,
                Curvature = 0.0,
                AgeDays = 0,
                Status = NodeStatus.Bud,
                LeafScale = 0f,
                BranchSide = 0,
                AttachT = 1.0,
                LastCutDay = NodeData.NoCut,
            });
            return tree.Nodes[index].Id;
        }
    }
}
