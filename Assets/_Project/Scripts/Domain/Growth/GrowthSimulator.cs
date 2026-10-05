using System;
using PocketArbor.Domain.Random;
using PocketArbor.Domain.Soil;
using PocketArbor.Domain.Tree;

namespace PocketArbor.Domain.Growth
{
    /// <summary>
    /// Hourly growth accumulation and the daily growth grammar (BALANCE §1.1), implemented step by step.
    /// Stateless with respect to trees (all state lives in <see cref="TreeGraph"/>); owns preallocated scratch
    /// buffers and a reusable PCG32, so a DailyStep allocates nothing. Not thread-safe.
    /// </summary>
    public sealed class GrowthSimulator
    {
        readonly GrowthConfig _config;
        readonly SoilConfig _soil;
        readonly SpeciesCatalog _species;
        readonly Pcg32 _rng = new Pcg32(0UL, 0UL);

        readonly int[] _childCount = new int[TreeGraph.Capacity];
        readonly int[] _candidates = new int[TreeGraph.Capacity];
        readonly double[] _weights = new double[TreeGraph.Capacity];
        readonly double[] _selectionWeights = new double[TreeGraph.Capacity];
        readonly int[] _order = new int[TreeGraph.Capacity];
        readonly TreeShapeAnalysis _shape = new TreeShapeAnalysis();

        public GrowthSimulator(GrowthConfig config, SoilConfig soil, SpeciesCatalog species)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _soil = soil ?? throw new ArgumentNullException(nameof(soil));
            _species = species ?? throw new ArgumentNullException(nameof(species));
            _config.Validate();
        }

        public GrowthConfig Config => _config;

        /// <summary>Generator of the current day (tests verify the 8-draws-per-shoot stream layout via its state).</summary>
        internal Pcg32 Rng => _rng;

        /// <summary>M_soil for a moisture value with the configured thresholds.</summary>
        public double MoistureFactor(double moisture) =>
            GrowthFormulas.MoistureFactor(moisture, _soil.DryThreshold, _config.MoistureOptimal);

        /// <summary>
        /// One simulated hour (online tick and offline catch-up are identical). The caller runs
        /// SoilSimulation.SimulateOneHour first and passes the resulting W.
        /// </summary>
        public GrowthHourResult SimulateHour(TreeGraph tree, in GrowthHourInput input)
        {
            if (tree == null) throw new ArgumentNullException(nameof(tree));
            ref GrowthState state = ref tree.GrowthRef;
            if (state.Frozen) return GrowthHourResult.Frozen;

            double m = MoistureFactor(input.Moisture);
            double g = m * GrowthFormulas.TemperatureFactor(input.TemperatureC, _config);
            state.SumGrowthFactor += g;
            state.SumMoistureFactor += m;
            state.GrowthHourCounter++;

            // BALANCE §1.1: interpretation — hourly accumulation does not bump Version (node table untouched).
            if (state.GrowthHourCounter < _config.HoursPerGrowthDay) return GrowthHourResult.Accumulated;

            DailyStep(tree);
            state.GrowthHourCounter = 0;
            state.SumGrowthFactor = 0.0;
            state.SumMoistureFactor = 0.0;
            return GrowthHourResult.DayCompleted;
        }

        /// <summary>Batch form (offline catch-up). Equivalent to calling <see cref="SimulateHour"/> per element.</summary>
        /// <returns>Number of DailySteps run.</returns>
        public int SimulateHours(TreeGraph tree, ReadOnlySpan<GrowthHourInput> hours)
        {
            int days = 0;
            for (int i = 0; i < hours.Length; i++)
            {
                if (SimulateHour(tree, in hours[i]) == GrowthHourResult.DayCompleted) days++;
            }
            return days;
        }

        /// <summary>
        /// DailyStep of BALANCE §1.1, steps 1–7 in order. Reads the day sums; resetting them is done by
        /// <see cref="SimulateHour"/>. Internal: production code drives it only through hours.
        /// </summary>
        internal void DailyStep(TreeGraph tree)
        {
            GrowthConfig cfg = _config;
            ref GrowthState state = ref tree.GrowthRef;
            SpeciesConfig species = _species.Get(tree.Genome.Species);
            ShapeParams shape = ShapeParams.Resolve(cfg, species);
            int maxNodes = Math.Min(cfg.MaxNodes, TreeGraph.Capacity);

            // 1. Day factors, day index, Nebari flag (fixed before spawning).
            double hours = cfg.HoursPerGrowthDay;
            double g = GrowthFormulas.Clamp01(state.SumGrowthFactor / hours);
            double meanMoisture = GrowthFormulas.Clamp01(state.SumMoistureFactor / hours);
            state.GrowthDayIndex++;
            bool nebari = tree.Count >= maxNodes;

            double elongationFraction = 1.0 - Math.Exp(-GrowthFormulas.Clamp(cfg.ElongationRate, 0.0, double.MaxValue) * g);
            double leafStep = g / cfg.LeafUnfoldDays;

            int count = tree.Count;
            for (int i = 0; i < count; i++)
            {
                ref NodeData node = ref tree.NodeAt(i);

                // 2. Development.
                if (node.AgeDays < int.MaxValue) node.AgeDays++;
                if (node.Depth == 0)
                {
                    node.Status = NodeStatus.Mature;
                }
                else
                {
                    // BALANCE §1.1: interpretation — transitions are evaluated sequentially in one step.
                    if (node.Status == NodeStatus.Bud && node.AgeDays >= cfg.BudDays) node.Status = NodeStatus.Growing;
                    if (node.Status == NodeStatus.Growing && node.AgeDays >= cfg.MatureDays) node.Status = NodeStatus.Mature;
                }

                // 3. Elongation (Bud keeps its length).
                if (node.Status != NodeStatus.Bud)
                {
                    double length = node.Length + (node.TargetLength - node.Length) * elongationFraction;
                    if (!double.IsNaN(length) && !double.IsInfinity(length)) node.Length = length;
                }

                // 4. Leaves.
                if (node.HasLeaves)
                {
                    double leaf = node.LeafScale + leafStep;
                    node.LeafScale = leaf >= 1.0 ? 1f : (float)leaf;
                }

                // 5. Thickening (M_soil = mean moisture factor of the day, Δt = 1 day).
                // ⚑Q BALANCE §1.4.1(б): ord-0 nodes (chain, crown axis) take depthScale(Depth·TrunkChainDepthScaleFactor);
                // r = K·M·depthScale/th_max = K·M/MaxThickness is unchanged.
                int parentIndex = node.ParentIndex;
                int order = parentIndex < 0 ? 0 : _order[parentIndex] + (node.BranchSide != 0 ? 1 : 0);
                _order[i] = order;
                double depthScale = GrowthFormulas.AxisDepthScale(node.Depth, order == 0, shape.TrunkChainDepthScaleFactor, cfg);
                double thMax = cfg.MaxThickness * depthScale;
                double k = node.Depth == 0 && nebari ? cfg.ThickeningRate * cfg.NebariThickeningMultiplier : cfg.ThickeningRate;
                double thickness = node.Thickness;
                double thickened = GrowthFormulas.Thicken(thickness, thMax, k, meanMoisture, depthScale, 1.0);
                if (parentIndex >= 0)
                {
                    // ⚑ BALANCE §1.2.2: taper ceiling th_cap from the parent, already updated (index order);
                    // clamp to [th, max(th, min(th_max, th_cap))] — never shrinks. ⚑Q §1.4.1(б)/§1.4.3: children of ord-0 parents.
                    double cap = GrowthFormulas.ThicknessCap(tree.NodeAt(parentIndex).Thickness, node.AttachT, node.BranchSide == 0,
                        _order[parentIndex] == 0, in shape, cfg);
                    double upper = cap > thickness ? cap : thickness;
                    if (thickened > upper) thickened = upper;
                }
                node.Thickness = thickened;
            }

            // 6. Shoot spawning.
            if (tree.Count < maxNodes && g > 0.0) SpawnShoots(tree, ref state, species, in shape, g, maxNodes);

            // 7. Ages always change, so the node table always mutates.
            tree.MarkChanged();
        }

        void SpawnShoots(TreeGraph tree, ref GrowthState state, SpeciesConfig species, in ShapeParams shape, double g, int maxNodes)
        {
            GrowthConfig cfg = _config;

            // 6.1
            int candidateCount = CollectCandidates(tree, out double weightSum);
            if (candidateCount == 0) return;

            // 6.2
            double rate = GrowthFormulas.Clamp(cfg.BaseGrowthProbability * tree.Genome.Vigor * weightSum,
                cfg.ShootsPerDayMin, cfg.ShootsPerDayMax) * g;
            state.ShootCredit += rate;

            // 6.3
            int shoots = Math.Min((int)Math.Floor(state.ShootCredit), cfg.ShootsPerDayMax);
            if (shoots < 0) shoots = 0;
            state.ShootCredit -= shoots;

            // 6.4 — RNG of day k: Pcg32(seed, stream = GrowthDayIndex).
            _rng.Reseed(tree.Genome.Seed, (ulong)state.GrowthDayIndex);
            for (int s = 0; s < shoots; s++)
            {
                if (tree.Count >= maxNodes) break;
                candidateCount = CollectCandidates(tree, out weightSum);
                if (candidateCount == 0) break;

                ShootDraws u = ShootDraws.Next(_rng);
                double selectionSum = ComputeSelectionWeights(tree, species, shape.DomeWeightMultiplier, candidateCount);
                int parentIndex = PickParent(candidateCount, u.U1 * selectionSum);
                if (parentIndex < 0) continue; // Σs = 0: the shoot burns, its 8 draws are spent (stream layout kept).
                NodeData shoot = ShootBuilder.Build(tree, parentIndex, in u, cfg, species, in shape);
                tree.Append(shoot);
            }

            // 6.5
            if (tree.Count >= maxNodes) state.ShootCredit = 0.0;
        }

        /// <summary>Mature, Depth &lt; MaxDepth, children &lt; MaxChildrenPerNode; in index order with weights 1 − d/D_max.</summary>
        int CollectCandidates(TreeGraph tree, out double weightSum)
        {
            ReadOnlySpan<NodeData> nodes = tree.Nodes;
            int count = nodes.Length;
            for (int i = 0; i < count; i++) _childCount[i] = 0;
            for (int i = 1; i < count; i++) _childCount[nodes[i].ParentIndex]++;

            int candidates = 0;
            weightSum = 0.0;
            for (int i = 0; i < count; i++)
            {
                if (nodes[i].Status != NodeStatus.Mature) continue;
                if (nodes[i].Depth >= _config.MaxDepth) continue;
                if (_childCount[i] >= _config.MaxChildrenPerNode) continue;

                double w = GrowthFormulas.CandidateWeight(nodes[i].Depth, _config.MaxDepth);
                _candidates[candidates] = i;
                _weights[candidates] = w;
                weightSum += w;
                candidates++;
            }
            return candidates;
        }

        /// <summary>
        /// ⚑ BALANCE §1.2.7 / ⚑P §1.3.1, §1.3.4, §1.3.5: selection weights s_i = w_i · RoleWeight_i · BackBud_i, roles recomputed
        /// before every shoot. Returns Σs. The rate (step 6.2) still uses Σw.
        /// Roles: trunk chain without an apical child — ApicalDominanceWeight once the apical delay has passed, else TierWeight if a
        /// lateral slot is free, else 0; trunk chain with an apical child — TierWeight; otherwise InPad ? PadWeight : InteriorWeight
        /// (the crown axis too, but 0 while its apical delay lasts and both lateral slots are taken).
        /// BackBud_i = BackBudWeightMultiplier when BackBudDist ≤ BackBudRadiusEdges ∧ ord ≥ 1, else 1.
        /// ⚑Q §1.4.4: × DomeWeightMultiplier for the dome subtree (crown-axis nodes and all their descendants).
        /// </summary>
        double ComputeSelectionWeights(TreeGraph tree, SpeciesConfig species, double domeWeight, int candidateCount)
        {
            GrowthConfig cfg = _config;
            ReadOnlySpan<NodeData> nodes = tree.Nodes;
            _shape.Analyze(nodes, TreeShapeAnalysis.Params(tree, cfg, species));
            int lateralSlots = cfg.MaxChildrenPerNode - 1;
            double sum = 0.0;
            for (int c = 0; c < candidateCount; c++)
            {
                int i = _candidates[c];
                bool axis = _shape.Order(i) == 0;
                // An ord-0 node without an apical child whose delay still lasts can only take a lateral (BALANCE §1.3.1).
                bool lateralOnly = axis && _shape.ApicalChild(i) < 0 && ShootBuilder.IsApicalDelayed(in nodes[i], cfg);
                bool slotFree = _shape.ChildCount(i) - (_shape.ApicalChild(i) >= 0 ? 1 : 0) < lateralSlots;
                double role;
                if (lateralOnly && !slotFree) role = 0.0;
                else if (_shape.IsTrunkChain(i)) role = _shape.ApicalChild(i) < 0 && !lateralOnly ? cfg.ApicalDominanceWeight : cfg.TierWeight;
                else role = _shape.InPad(i) ? cfg.PadWeight : cfg.InteriorWeight;
                if (_shape.InDome(i)) role *= domeWeight;
                if (!axis && _shape.BackBudDist(i) <= cfg.BackBudRadiusEdges) role *= cfg.BackBudWeightMultiplier;
                double s = _weights[c] * role;
                _selectionWeights[c] = s;
                sum += s;
            }
            return sum;
        }

        /// <summary>
        /// First candidate whose cumulative selection weight exceeds <paramref name="threshold"/> = u1·Σs; −1 when Σs = 0
        /// (the shoot burns, BALANCE §1.3.1).
        /// </summary>
        int PickParent(int candidateCount, double threshold)
        {
            double cumulative = 0.0;
            int lastPositive = -1;
            for (int i = 0; i < candidateCount; i++)
            {
                if (!(_selectionWeights[i] > 0.0)) continue;
                lastPositive = i;
                cumulative += _selectionWeights[i];
                if (cumulative > threshold) return _candidates[i];
            }
            // BALANCE §1.1: interpretation — floating-point rounding fallback: the last candidate (with a positive weight).
            return lastPositive >= 0 ? _candidates[lastPositive] : -1;
        }
    }
}
