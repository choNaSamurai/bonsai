using System.Collections.Generic;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Tree;

namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>
    /// <c>PruneToStyle</c> of BALANCE §1.3.7 (PA-102P): rules S1–S7 in order, each after re-analysing the tree, cuts through the
    /// domain <see cref="TreePruning.Prune"/> (so LastCutDay / back-budding behave exactly as for a player's cut).
    /// Axis numbering: an axis starts at the root or at a lateral and continues through apical children; its first node is 1.
    /// Dev-only: allocations are fine. Plain C# (no UnityEngine) so the measurement harness can run it outside Unity.
    /// </summary>
    public sealed class MoyogiStyler
    {
        public enum Rule
        {
            S1WildTwig = 1,
            S2CrownHeight = 2,
            S3IntermediateTiers = 3,
            S4BranchLegs = 4,
            S5LongSecondaries = 5,
            S6InteriorShoots = 6,
            S7Dome = 7,
        }

        readonly TreeShapeAnalysis _shape = new TreeShapeAnalysis();
        readonly List<int> _targets = new List<int>();
        int[] _axisPos = new int[TreeGraph.Capacity];
        int[] _axisStart = new int[TreeGraph.Capacity];

        /// <summary>Cuts made per rule by the last <see cref="PruneToStyle"/> (index = (int)Rule).</summary>
        public readonly int[] LastCuts = new int[8];

        /// <summary>Runs S1–S7. Returns the number of successful cuts.</summary>
        public int PruneToStyle(TreeGraph tree, GrowthConfig config, SpeciesConfig species, StyleConfig style)
        {
            for (int r = 0; r < LastCuts.Length; r++) LastCuts[r] = 0;
            int cuts = 0;
            for (int r = (int)Rule.S1WildTwig; r <= (int)Rule.S7Dome; r++)
            {
                Collect(tree, config, species, style, (Rule)r, _targets);
                int n = PruneAll(tree, config);
                LastCuts[r] = n;
                cuts += n;
            }
            return cuts;
        }

        /// <summary>Ids of the nodes rule <paramref name="rule"/> would cut on the current tree (tests: S2–S7 violations after styling).</summary>
        public void Collect(TreeGraph tree, GrowthConfig config, SpeciesConfig species, StyleConfig style, Rule rule, List<int> ids)
        {
            ids.Clear();
            _shape.Analyze(tree, config, species);
            var nodes = tree.Nodes;
            int count = nodes.Length;
            for (int i = 0; i < count; i++)
            {
                int parent = nodes[i].ParentIndex;
                bool axisRoot = parent < 0 || nodes[i].BranchSide != 0;
                _axisStart[i] = axisRoot ? i : _axisStart[parent];
                _axisPos[i] = axisRoot ? 1 : _axisPos[parent] + 1;
            }

            for (int i = 1; i < count; i++)
            {
                NodeData n = nodes[i];
                int parent = n.ParentIndex;
                int start = _axisStart[i];
                int startParent = nodes[start].ParentIndex;
                bool cut = false;
                switch (rule)
                {
                    case Rule.S1WildTwig:
                        cut = parent == 0 && n.BranchSide != 0 && n.AttachT < config.TrunkBaseSlotT0 - config.AttachTJitter - 1e-9;
                        break;
                    case Rule.S2CrownHeight:
                        // Crown-axis node number above k4 = Depth − TrunkChainSegments + 1.
                        cut = _shape.IsCrownAxis(i) && n.Depth - config.TrunkChainSegments + 1 > style.StyleCrownAxisMax;
                        break;
                    case Rule.S3IntermediateTiers:
                        cut = !style.StyleKeepIntermediateTiers && _shape.IsPrimary(i) && nodes[parent].Depth >= 1
                            && IsSlot0(n, parent, config) && HasGrownBendTier(nodes, parent, config);
                        break;
                    case Rule.S4BranchLegs:
                        cut = startParent >= 0 && _shape.IsPrimary(start) && _axisPos[i] > species.PadDepth + style.StyleBranchAxisExtra;
                        break;
                    case Rule.S5LongSecondaries:
                    {
                        int ord = _shape.Order(start);
                        cut = ord == 2 ? _axisPos[i] > style.StyleSecondaryAxisMax : ord >= 3 && _axisPos[i] > style.StyleTertiaryAxisMax;
                        break;
                    }
                    case Rule.S6InteriorShoots:
                        cut = style.StyleCutInteriorShoots && n.BranchSide != 0 && _shape.IsInterior(parent);
                        break;
                    case Rule.S7Dome:
                        cut = startParent >= 0 && _shape.IsCrownAxis(startParent) && _axisPos[i] > style.StyleDomeLateralMax;
                        break;
                }
                // Cut only the first offending node of a subtree (its descendants go with it).
                if (cut && !AncestorListed(nodes, parent, ids)) ids.Add(n.Id);
            }
        }

        static bool IsSlot0(in NodeData lateral, int parent, GrowthConfig config)
        {
            double boundary = parent == 0 ? 0.5 * (config.TrunkBaseSlotT0 + config.TrunkBaseSlotT1) : 0.5 * (config.LateralSlotT0 + config.LateralSlotT1);
            return lateral.AttachT < boundary;
        }

        static bool HasGrownBendTier(System.ReadOnlySpan<NodeData> nodes, int parent, GrowthConfig config)
        {
            for (int j = parent + 1; j < nodes.Length; j++)
            {
                if (nodes[j].ParentIndex != parent || nodes[j].BranchSide == 0) continue;
                if (!IsSlot0(nodes[j], parent, config) && nodes[j].Status != NodeStatus.Bud) return true;
            }
            return false;
        }

        bool AncestorListed(System.ReadOnlySpan<NodeData> nodes, int index, List<int> ids)
        {
            while (index > 0)
            {
                if (ids.Contains(nodes[index].Id)) return true;
                index = nodes[index].ParentIndex;
            }
            return false;
        }

        int PruneAll(TreeGraph tree, GrowthConfig config)
        {
            int cuts = 0;
            for (int k = 0; k < _targets.Count; k++)
            {
                if (tree.IndexOf(_targets[k]) < 0) continue; // already removed with an ancestor
                if (TreePruning.Prune(tree, _targets[k], config).Succeeded) cuts++;
            }
            _targets.Clear();
            return cuts;
        }
    }
}
