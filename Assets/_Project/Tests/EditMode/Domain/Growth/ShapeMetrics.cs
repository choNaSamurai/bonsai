using System;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Soil;
using PocketArbor.Domain.Tree;

namespace PocketArbor.Domain.Tests.Growth
{
    /// <summary>
    /// Measurement harness of the PA-102S acceptance criteria (no NUnit dependency, so the same numbers can be
    /// reproduced outside Unity). Geometry uses straight chords: base(i) = base(parent) + AttachT·chord(parent).
    /// </summary>
    internal static class ShapeMetrics
    {
        public const int MonteCarloSeeds = 30;

        public static GrowthSimulator Simulator(GrowthConfig config = null) =>
            new GrowthSimulator(config ?? GrowthConfig.CreateDefault(), SoilConfig.CreateDefault(), SpeciesCatalog.CreateDefault());

        /// <summary>Post-onboarding (twig cut, N = 2), G = 1, then <paramref name="days"/> growth days.</summary>
        public static TreeGraph Grow(GrowthSimulator sim, uint seed, SpeciesId species, int days, double vigor = 1.0, double bendBias = 0.0)
        {
            GrowthConfig cfg = sim.Config;
            var tree = TreeFactory.PlantSapling(new TreeGenome(seed, species, vigor, bendBias), cfg, SpeciesConfig.CreateDefault(species));
            tree.SetGrowthFrozen(false);
            TreePruning.Prune(tree, tree.Nodes[2].Id, cfg);
            RunDays(sim, tree, days);
            return tree;
        }

        public static void RunDays(GrowthSimulator sim, TreeGraph tree, int days, double moisture = 0.5, double temperatureC = 25.0)
        {
            var input = new GrowthHourInput(moisture, temperatureC);
            int hours = sim.Config.HoursPerGrowthDay;
            for (int d = 0; d < days; d++)
            {
                for (int h = 0; h < hours; h++) sim.SimulateHour(tree, in input);
            }
        }

        public static TreeShapeAnalysis Analyze(TreeGraph tree, GrowthConfig cfg)
        {
            var shape = new TreeShapeAnalysis();
            shape.Analyze(tree, cfg, SpeciesConfig.CreateDefault(tree.Genome.Species));
            return shape;
        }

        /// <summary>Base and tip heights of every node (chord geometry, pot lip at y = 0).</summary>
        public static void Heights(TreeGraph tree, out double[] baseY, out double[] tipY)
        {
            var nodes = tree.Nodes;
            baseY = new double[nodes.Length];
            tipY = new double[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                int p = nodes[i].ParentIndex;
                baseY[i] = p < 0 ? 0.0 : baseY[p] + nodes[i].AttachT * (tipY[p] - baseY[p]);
                tipY[i] = baseY[i] + nodes[i].Length * Math.Cos(tree.WorldAngle(i));
            }
        }

        public struct PrimaryStats
        {
            public int Count;
            public int Left;
            public int Right;
            public int DistinctSegments;
            public int BareBase;
        }

        /// <summary>Primary branches (lateral children of the trunk chain) with Status ≠ Bud.</summary>
        public static PrimaryStats Primaries(TreeGraph tree, TreeShapeAnalysis shape)
        {
            var nodes = tree.Nodes;
            var stats = new PrimaryStats();
            var segmentUsed = new bool[64];
            for (int i = 1; i < nodes.Length; i++)
            {
                if (!shape.IsPrimary(i) || nodes[i].Status == NodeStatus.Bud) continue;
                stats.Count++;
                if (nodes[i].BranchSide < 0) stats.Right++;
                else stats.Left++;
                int k = nodes[nodes[i].ParentIndex].Depth;
                if (!segmentUsed[k])
                {
                    segmentUsed[k] = true;
                    stats.DistinctSegments++;
                }
                if (!shape.IsLeafy(i)) stats.BareBase++;
            }
            return stats;
        }

        /// <summary>Median |world angle| (degrees) of non-bud primaries; NaN if none.</summary>
        public static double MedianPrimaryAngleDeg(TreeGraph tree, TreeShapeAnalysis shape)
        {
            var nodes = tree.Nodes;
            var angles = new double[nodes.Length];
            int n = 0;
            for (int i = 1; i < nodes.Length; i++)
            {
                if (shape.IsPrimary(i) && nodes[i].Status != NodeStatus.Bud) angles[n++] = Math.Abs(tree.WorldAngle(i)) / GrowthFormulas.DegToRad;
            }
            return Median(angles, n);
        }

        public static double Median(double[] values, int count)
        {
            if (count == 0) return double.NaN;
            Array.Sort(values, 0, count);
            return (count & 1) == 1 ? values[count / 2] : 0.5 * (values[count / 2 - 1] + values[count / 2]);
        }

        /// <summary>
        /// Thickness(#0) / th_at(point at 2/3 of the trunk-chain height) (BALANCE §1.2.2, AC6).
        /// Height = tip of the topmost trunk-chain node.
        /// </summary>
        public static double TaperRatio(TreeGraph tree, TreeShapeAnalysis shape, GrowthConfig cfg)
        {
            var nodes = tree.Nodes;
            Heights(tree, out double[] baseY, out double[] tipY);
            int top = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (shape.IsTrunkChain(i) && nodes[i].Depth > nodes[top].Depth) top = i;
            }
            double target = tipY[top] * 2.0 / 3.0;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (!shape.IsTrunkChain(i)) continue;
                if (target < baseY[i] || target > tipY[i]) continue;
                double span = tipY[i] - baseY[i];
                double t = span > 0.0 ? (target - baseY[i]) / span : 0.0;
                return nodes[0].Thickness / GrowthFormulas.ThicknessAt(nodes[i].Thickness, t, cfg.SegmentTipTaper);
            }
            return double.NaN;
        }

        /// <summary>Fraction of leafy nodes that sit in pads with ordEff ≥ 1 (AC10).</summary>
        public static double LeafyInBranchPadsFraction(TreeShapeAnalysis shape)
        {
            int leafy = 0, inBranchPads = 0;
            for (int i = 0; i < shape.Count; i++)
            {
                if (!shape.IsLeafy(i)) continue;
                leafy++;
                if (shape.EffectiveOrder(shape.PadId(i)) >= 1) inBranchPads++;
            }
            return leafy == 0 ? 0.0 : inBranchPads / (double)leafy;
        }

        /// <summary>
        /// AC12 harness: number of lateral shoots born during <paramref name="days"/> whose parent was an interior node
        /// (analysis taken before each day).
        /// </summary>
        public static int InteriorLateralShoots(GrowthSimulator sim, TreeGraph tree, int days)
        {
            GrowthConfig cfg = sim.Config;
            var shape = new TreeShapeAnalysis();
            int found = 0;
            for (int d = 0; d < days; d++)
            {
                shape.Analyze(tree, cfg, SpeciesConfig.CreateDefault(tree.Genome.Species));
                int nextId = tree.NextId;
                int before = tree.Count;
                RunDays(sim, tree, 1);
                var nodes = tree.Nodes;
                for (int i = 0; i < nodes.Length; i++)
                {
                    if (nodes[i].Id < nextId || nodes[i].BranchSide == 0) continue;
                    int p = nodes[i].ParentIndex;
                    if (p < before && shape.IsInterior(p)) found++;
                }
            }
            return found;
        }

        /// <summary>
        /// AC11 harness: cuts a terminal non-bud node (deepest, parent has exactly one child) of the largest primary
        /// branch, then grows until 3 new shoots appear in that branch (≤ 30 days). Returns how many of them are
        /// ≤ 2 edges from the cut parent; <paramref name="counted"/> = shoots observed. −1 if no suitable tip.
        /// </summary>
        public static int BackBuddingNearCut(GrowthSimulator sim, TreeGraph tree, out int counted, out int cutParentId)
        {
            GrowthConfig cfg = sim.Config;
            counted = 0;
            cutParentId = -1;
            var shape = Analyze(tree, cfg);
            var nodes = tree.Nodes;

            // Largest primary by subtree size.
            var size = new int[nodes.Length];
            for (int i = nodes.Length - 1; i >= 0; i--)
            {
                size[i]++;
                if (nodes[i].ParentIndex >= 0) size[nodes[i].ParentIndex] += size[i];
            }
            int primary = -1;
            for (int i = 1; i < nodes.Length; i++)
            {
                if (shape.IsPrimary(i) && (primary < 0 || size[i] > size[primary])) primary = i;
            }
            if (primary < 0) return -1;
            int primaryId = nodes[primary].Id;

            int tip = -1;
            for (int i = primary + 1; i < nodes.Length; i++)
            {
                if (!IsInSubtree(nodes, i, primary) || shape.ChildCount(i) != 0 || nodes[i].Status == NodeStatus.Bud) continue;
                if (shape.ChildCount(nodes[i].ParentIndex) != 1) continue;
                if (tip < 0 || nodes[i].Depth > nodes[tip].Depth) tip = i;
            }
            if (tip < 0) return -1;
            cutParentId = nodes[nodes[tip].ParentIndex].Id;
            TreePruning.Prune(tree, nodes[tip].Id, cfg);

            int near = 0;
            for (int day = 0; day < 30 && counted < 3; day++)
            {
                int nextId = tree.NextId;
                RunDays(sim, tree, 1);
                var now = tree.Nodes;
                int primaryIndex = tree.IndexOf(primaryId);
                int cutParent = tree.IndexOf(cutParentId);
                for (int i = 0; i < now.Length && counted < 3; i++)
                {
                    if (now[i].Id < nextId || !IsInSubtree(now, i, primaryIndex)) continue;
                    counted++;
                    if (Distance(now, i, cutParent) <= 2) near++;
                }
            }
            return near;
        }

        public static bool IsInSubtree(ReadOnlySpan<NodeData> nodes, int index, int root)
        {
            while (index > root) index = nodes[index].ParentIndex;
            return index == root;
        }

        /// <summary>Edge distance between two nodes of the tree.</summary>
        public static int Distance(ReadOnlySpan<NodeData> nodes, int a, int b)
        {
            int da = 0, db = 0;
            while (a != b)
            {
                if (a > b)
                {
                    a = nodes[a].ParentIndex;
                    da++;
                }
                else
                {
                    b = nodes[b].ParentIndex;
                    db++;
                }
            }
            return da + db;
        }
    }
}
