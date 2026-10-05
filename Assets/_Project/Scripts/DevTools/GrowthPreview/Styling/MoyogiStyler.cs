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
            /// <summary>⚑R BALANCE §1.5.3(в): «air between tiers» of one side (StylePadGapUnits, StylePadGapCutsPerPass).</summary>
            S8PadGap = 8,
        }

        readonly TreeShapeAnalysis _shape = new TreeShapeAnalysis();
        readonly List<int> _targets = new List<int>();
        int[] _axisPos = new int[TreeGraph.Capacity];
        int[] _axisStart = new int[TreeGraph.Capacity];

        /// <summary>Cuts made per rule by the last <see cref="PruneToStyle"/> (index = (int)Rule).</summary>
        public readonly int[] LastCuts = new int[9];

        /// <summary>AC19 statistics of the last S8 pass: same-side tier pairs, pairs with gap ≥ StylePadGapUnits or the cut limit used,
        /// cuts that hit the first node of a primary (must stay 0).</summary>
        public int LastGapPairs { get; private set; }
        public int LastGapPairsOk { get; private set; }
        public int LastGapFirstNodeCuts { get; private set; }

        /// <summary>Pairs below the gap whose lower pad has nothing S8 may cut (only the first node of the primary — e.g. a one-node k4 tier).</summary>
        public int LastGapPairsStuck { get; private set; }

        /// <summary>Dev diagnostics: when set, every pair that stays below the gap without using the limit is described here.</summary>
        public System.Text.StringBuilder GapLog;

        readonly StyleGeometry _geometry = new StyleGeometry();
        readonly List<int> _tiers = new List<int>();
        readonly List<int> _sideTiers = new List<int>();
        readonly Dictionary<int, int> _pairCuts = new Dictionary<int, int>();

        /// <summary>Runs S1–S8. Returns the number of successful cuts.</summary>
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
            int s8 = PadGap(tree, config, species, style);
            LastCuts[(int)Rule.S8PadGap] = s8;
            return cuts + s8;
        }

        /// <summary>
        /// ⚑R BALANCE §1.5.3(в) S8: for each pair of same-side tiers adjacent by attachment height (#0 slot 0, bend tiers; the top tier of a
        /// side pairs with the dome) gap = min tip Y of the upper pad's InPad nodes − max tip Y of the lower pad's InPad nodes (C1 preview
        /// geometry). While gap &lt; StylePadGapUnits, the lower pad loses its node with the highest tip — ord ≥ 2 first, then the end of the
        /// primary axis; the first node of a primary is never cut; ≤ StylePadGapCutsPerPass cuts per pair.
        /// </summary>
        int PadGap(TreeGraph tree, GrowthConfig config, SpeciesConfig species, StyleConfig style)
        {
            _pairCuts.Clear();
            LastGapPairs = LastGapPairsOk = LastGapFirstNodeCuts = LastGapPairsStuck = 0;
            if (style.StylePadGapCutsPerPass <= 0) return 0;
            int total = 0;
            for (int guard = 0; guard < 64; guard++)
            {
                int target = -1, pairKey = 0;
                _shape.Analyze(tree, config, species);
                _geometry.Build(tree.Nodes, _shape, style.CurvatureOffsetRatio);
                CollectTiers(tree.Nodes);
                for (int side = -1; side <= 1 && target < 0; side += 2)
                {
                    SideTiers(tree.Nodes, side);
                    for (int k = 0; k < _sideTiers.Count && target < 0; k++)
                    {
                        int lower = _sideTiers[k];
                        int upper = k + 1 < _sideTiers.Count ? _sideTiers[k + 1] : -1;
                        double top = upper >= 0 ? PadTipExtreme(tree.Nodes, upper, false) : DomeTipMin(tree.Nodes);
                        double low = PadTipExtreme(tree.Nodes, lower, true);
                        if (double.IsNaN(top) || double.IsNaN(low) || top - low >= style.StylePadGapUnits) continue;
                        int key = tree.Nodes[lower].Id;
                        _pairCuts.TryGetValue(key, out int used);
                        if (used >= style.StylePadGapCutsPerPass) continue;
                        target = GapCutTarget(tree.Nodes, lower);
                        pairKey = key;
                    }
                }
                if (target < 0) break;
                NodeData cut = tree.Nodes[target];
                if (_shape.IsPrimary(target)) LastGapFirstNodeCuts++;
                if (!TreePruning.Prune(tree, cut.Id, config).Succeeded) break;
                _pairCuts.TryGetValue(pairKey, out int n);
                _pairCuts[pairKey] = n + 1;
                total++;
            }

            // Statistics after the pass (AC19).
            _shape.Analyze(tree, config, species);
            _geometry.Build(tree.Nodes, _shape, style.CurvatureOffsetRatio);
            CollectTiers(tree.Nodes);
            for (int side = -1; side <= 1; side += 2)
            {
                SideTiers(tree.Nodes, side);
                for (int k = 0; k < _sideTiers.Count; k++)
                {
                    int lower = _sideTiers[k];
                    int upper = k + 1 < _sideTiers.Count ? _sideTiers[k + 1] : -1;
                    double top = upper >= 0 ? PadTipExtreme(tree.Nodes, upper, false) : DomeTipMin(tree.Nodes);
                    double low = PadTipExtreme(tree.Nodes, lower, true);
                    if (double.IsNaN(top) || double.IsNaN(low)) continue;
                    LastGapPairs++;
                    _pairCuts.TryGetValue(tree.Nodes[lower].Id, out int used);
                    if (top - low >= style.StylePadGapUnits || used >= style.StylePadGapCutsPerPass) LastGapPairsOk++;
                    else if (GapCutTarget(tree.Nodes, lower) < 0) LastGapPairsStuck++;
                    if (!(top - low >= style.StylePadGapUnits || used >= style.StylePadGapCutsPerPass)) GapLog?.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
                        "side {0} lower k{1} (id {2}) upper {3} gap {4:0.0} cuts {5} target {6} lowerMaxTip node {7} ord {8} upperMinTip node {9}\n",
                        side, tree.Nodes[tree.Nodes[lower].ParentIndex].Depth, tree.Nodes[lower].Id, upper >= 0 ? "k" + tree.Nodes[tree.Nodes[upper].ParentIndex].Depth : "dome",
                        top - low, used, GapCutTarget(tree.Nodes, lower), ArgTip(tree.Nodes, lower, true), _shape.Order(ArgTip(tree.Nodes, lower, true)),
                        upper >= 0 ? ArgTip(tree.Nodes, upper, false) : -1);
                }
            }
            return total;
        }

        void CollectTiers(System.ReadOnlySpan<NodeData> nodes)
        {
            _tiers.Clear();
            for (int i = 1; i < nodes.Length; i++)
            {
                if (_shape.IsPrimary(i) && nodes[i].Status != NodeStatus.Bud) _tiers.Add(i);
            }
        }

        /// <summary>Tiers of one side (BranchSide = <paramref name="side"/>) sorted by attachment height.</summary>
        void SideTiers(System.ReadOnlySpan<NodeData> nodes, int side)
        {
            _sideTiers.Clear();
            foreach (int i in _tiers) if (nodes[i].BranchSide == side) _sideTiers.Add(i);
            _sideTiers.Sort((a, b) => _geometry.BaseY(a).CompareTo(_geometry.BaseY(b)));
        }

        bool InSubtree(System.ReadOnlySpan<NodeData> nodes, int node, int root)
        {
            while (node > root) node = nodes[node].ParentIndex;
            return node == root;
        }

        /// <summary>Max (<paramref name="max"/>) or min tip Y of the InPad nodes of the subtree of primary <paramref name="primary"/>; NaN if none.</summary>
        double PadTipExtreme(System.ReadOnlySpan<NodeData> nodes, int primary, bool max)
        {
            double best = double.NaN;
            for (int i = primary; i < nodes.Length; i++)
            {
                if (!_shape.InPad(i) || !InSubtree(nodes, i, primary)) continue;
                double y = _geometry.TipY(i);
                if (double.IsNaN(best) || (max ? y > best : y < best)) best = y;
            }
            return best;
        }

        int ArgTip(System.ReadOnlySpan<NodeData> nodes, int primary, bool max)
        {
            int best = -1;
            for (int i = primary; i < nodes.Length; i++)
            {
                if (!_shape.InPad(i) || !InSubtree(nodes, i, primary)) continue;
                if (best < 0 || (max ? _geometry.TipY(i) > _geometry.TipY(best) : _geometry.TipY(i) < _geometry.TipY(best))) best = i;
            }
            return best;
        }

        double DomeTipMin(System.ReadOnlySpan<NodeData> nodes)
        {
            double best = double.NaN;
            for (int i = 0; i < nodes.Length; i++)
            {
                if (!_shape.InDome(i) || !_shape.InPad(i)) continue;
                double y = _geometry.TipY(i);
                if (double.IsNaN(best) || y < best) best = y;
            }
            return best;
        }

        /// <summary>The InPad node of the lower pad with the highest tip among ord ≥ 2; else the end of the primary axis (never its first node).</summary>
        int GapCutTarget(System.ReadOnlySpan<NodeData> nodes, int primary)
        {
            int best = -1;
            for (int i = primary + 1; i < nodes.Length; i++)
            {
                if (!_shape.InPad(i) || _shape.Order(i) < 2 || !InSubtree(nodes, i, primary)) continue;
                if (best < 0 || _geometry.TipY(i) > _geometry.TipY(best)) best = i;
            }
            if (best >= 0) return best;
            int end = primary;
            while (_shape.ApicalChild(end) >= 0) end = _shape.ApicalChild(end);
            return end != primary ? end : -1;
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

    /// <summary>
    /// Plain-C# copy of the preview's C1 axis geometry (TreePreviewMeshBuilder.ComputeGeometry / Eval, double precision) for S8: cubic
    /// Bezier per node with bisector tangents at the joints and the Curvature bump 12t²(1−t)² along the left normal; a lateral starts on its
    /// parent's curve at AttachT. Tips do not depend on the bump. PlayMode test: matches the builder within 1e-3.
    /// </summary>
    public sealed class StyleGeometry
    {
        double[] _dx = new double[TreeGraph.Capacity], _dy = new double[TreeGraph.Capacity];
        double[] _bx = new double[TreeGraph.Capacity], _by = new double[TreeGraph.Capacity];
        double[] _tx = new double[TreeGraph.Capacity], _ty = new double[TreeGraph.Capacity];
        double[] _tbx = new double[TreeGraph.Capacity], _tby = new double[TreeGraph.Capacity];
        double[] _ttx = new double[TreeGraph.Capacity], _tty = new double[TreeGraph.Capacity];
        double[] _world = new double[TreeGraph.Capacity];
        NodeData[] _nodes = new NodeData[TreeGraph.Capacity];
        double _ratio;
        int _count;

        public void Build(System.ReadOnlySpan<NodeData> nodes, TreeShapeAnalysis shape, double curvatureOffsetRatio)
        {
            _count = nodes.Length;
            _ratio = curvatureOffsetRatio;
            nodes.CopyTo(_nodes);
            for (int i = 0; i < _count; i++)
            {
                int p = nodes[i].ParentIndex;
                _world[i] = (p >= 0 ? _world[p] : 0.0) + nodes[i].CurrentAngle;
                _dx[i] = -System.Math.Sin(_world[i]);
                _dy[i] = System.Math.Cos(_world[i]);
            }
            for (int i = 0; i < _count; i++)
            {
                int p = nodes[i].ParentIndex;
                bool axisRoot = p < 0 || nodes[i].BranchSide != 0;
                if (axisRoot) { _tbx[i] = _dx[i]; _tby[i] = _dy[i]; }
                else Bisector(_dx[p], _dy[p], _dx[i], _dy[i], out _tbx[i], out _tby[i]);
                int a = shape.ApicalChild(i);
                if (a >= 0) Bisector(_dx[i], _dy[i], _dx[a], _dy[a], out _ttx[i], out _tty[i]);
                else { _ttx[i] = _dx[i]; _tty[i] = _dy[i]; }
            }
            for (int i = 0; i < _count; i++)
            {
                int p = nodes[i].ParentIndex;
                if (p < 0) { _bx[i] = 0.0; _by[i] = 0.0; }
                else if (nodes[i].BranchSide == 0) { _bx[i] = _tx[p]; _by[i] = _ty[p]; }
                else Eval(p, nodes[i].AttachT, out _bx[i], out _by[i]);
                _tx[i] = _bx[i] + _dx[i] * nodes[i].Length;
                _ty[i] = _by[i] + _dy[i] * nodes[i].Length;
            }
        }

        public double BaseY(int i) => _by[i];
        public double BaseX(int i) => _bx[i];
        public double TipY(int i) => _ty[i];
        public double TipX(int i) => _tx[i];

        public void Eval(int i, double t, out double x, out double y)
        {
            double length = _nodes[i].Length;
            double p0x = _bx[i], p0y = _by[i], p3x = _tx[i], p3y = _ty[i];
            double p1x = p0x + _tbx[i] * length / 3.0, p1y = p0y + _tby[i] * length / 3.0;
            double p2x = p3x - _ttx[i] * length / 3.0, p2y = p3y - _tty[i] * length / 3.0;
            double u = 1.0 - t;
            x = u * u * u * p0x + 3.0 * u * u * t * p1x + 3.0 * u * t * t * p2x + t * t * t * p3x;
            y = u * u * u * p0y + 3.0 * u * u * t * p1y + 3.0 * u * t * t * p2y + t * t * t * p3y;
            double bump = 12.0 * t * t * u * u * _nodes[i].Curvature * _ratio * length;
            x += -_dy[i] * bump;
            y += _dx[i] * bump;
        }

        static void Bisector(double ax, double ay, double bx, double by, out double x, out double y)
        {
            double sx = ax + bx, sy = ay + by;
            double m = System.Math.Sqrt(sx * sx + sy * sy);
            if (m * m > 1e-8) { x = sx / m; y = sy / m; }
            else { x = bx; y = by; }
        }
    }
}
