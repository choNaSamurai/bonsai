using System;
using System.Collections.Generic;
using NUnit.Framework;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Random;
using PocketArbor.Domain.Tree;

namespace PocketArbor.Domain.Tests.Growth
{
    /// <summary>
    /// PA-102S bonsai shape, model acceptance criteria AC1–AC8, AC10–AC14 (BALANCE §1.2), updated to PA-102P (§1.3: AC9, AC13–AC15).
    /// «30 seeds» = seeds 1…30,
    /// G = 1, vigor 1, bendBias 0, no pruning, from the post-onboarding state; «fixture» = seed 20261004.
    /// </summary>
    public sealed class BonsaiShapeTests
    {
        const int Seeds = ShapeMetrics.MonteCarloSeeds;
        static readonly SpeciesId[] AllSpecies = { SpeciesId.Kuromatsu, SpeciesId.Momiji, SpeciesId.Yamazakura };

        static GrowthConfig Config() => GrowthConfig.CreateDefault();

        // ---- AC1: AttachT --------------------------------------------------------------------------------

        [Test]
        public void AttachT_GrownTrees_ApicalOneLateralsInSlotRanges()
        {
            var config = Config();
            var sim = ShapeMetrics.Simulator(config);
            foreach (SpeciesId species in AllSpecies)
            {
                for (uint seed = 1; seed <= Seeds; seed++)
                {
                    var tree = ShapeMetrics.Grow(sim, seed, species, 80);
                    var nodes = tree.Nodes;
                    var shape = ShapeMetrics.Analyze(tree, config);
                    for (int i = 1; i < nodes.Length; i++)
                    {
                        double t = nodes[i].AttachT;
                        // ⚑R §1.5.3(г) (conscious update): crown-axis laterals take slot 0 at CrownLateralSlotT0 (0.65).
                        bool crownParent = nodes[i].BranchSide != 0 && shape.IsCrownAxis(nodes[i].ParentIndex);
                        double s0 = crownParent ? config.CrownLateralSlotT0 : config.LateralSlotT0;
                        string at = species + " seed " + seed + " node " + i;
                        if (nodes[i].BranchSide == 0)
                        {
                            Assert.AreEqual(1.0, t, at);
                            continue;
                        }
                        Assert.That(t, Is.InRange(0.05, 0.97), at);
                        // ⚑Q BALANCE §1.4.1(в): #0 slots 0.78 / 0.95 ± 0.04 (clamped to 0.97).
                        double slot0Min = config.TrunkBaseSlotT0 - config.AttachTJitter - 1e-9;
                        double slotBoundary = 0.5 * (config.TrunkBaseSlotT0 + config.TrunkBaseSlotT1);
                        if (nodes[i].ParentIndex == 0) Assert.That(t, Is.InRange(slot0Min, 0.97), at + " (#0 slots)");
                        else Assert.IsTrue((t >= s0 - 0.04 - 1e-9 && t <= s0 + 0.04 + 1e-9) || (t >= 0.81 && t <= 0.89), at + " (slots " + s0 + " / 0.85): " + t);
                        for (int j = i + 1; j < nodes.Length; j++)
                        {
                            if (nodes[j].ParentIndex == nodes[i].ParentIndex && nodes[j].BranchSide != 0)
                            {
                                // #0: slot gap 0.17 − 2·0.04 jitter = 0.09 (§1.4.1(в): «±0.04 does not cross the boundary 0.865»).
                                double gap = nodes[i].ParentIndex == 0 ? 0.09 - 1e-9 : config.LateralSlotT1 - s0 - 2.0 * config.AttachTJitter - 1e-9;
                                Assert.GreaterOrEqual(Math.Abs(nodes[j].AttachT - t), gap, at + " vs sibling " + j);
                                if (nodes[i].ParentIndex == 0) Assert.AreNotEqual(t < slotBoundary, nodes[j].AttachT < slotBoundary, at + " one per #0 slot");
                            }
                        }
                    }
                    Assert.IsTrue(tree.ToSnapshot().BitwiseEquals(TreeGraph.FromSnapshot(tree.ToSnapshot()).ToSnapshot()), "round-trip");
                }
            }
        }

        // ---- AC2 / AC3: tiers --------------------------------------------------------------------------

        [Test]
        public void Tiers_Day40And80_SidesFollowBendSlotsAndBendTiersAlternate()
        {
            var config = Config();
            var sim = ShapeMetrics.Simulator(config);
            foreach (SpeciesId species in AllSpecies)
            {
                for (uint seed = 1; seed <= Seeds; seed++)
                {
                    var tree = ShapeMetrics.Grow(sim, seed, species, 40);
                    AssertTierAlternation(tree, config, species + " seed " + seed + " D40");
                    ShapeMetrics.RunDays(sim, tree, 40);
                    AssertTierAlternation(tree, config, species + " seed " + seed + " D80");
                }
            }
        }

        static void AssertTierAlternation(TreeGraph tree, GrowthConfig config, string label)
        {
            var nodes = tree.Nodes;
            var shape = ShapeMetrics.Analyze(tree, config);
            ShapeMetrics.Heights(tree, out double[] baseY, out _);
            var bendTiers = new List<int>();
            for (int i = 1; i < nodes.Length; i++)
            {
                if (!shape.IsPrimary(i)) continue;
                int p = nodes[i].ParentIndex;
                int k = nodes[p].Depth;
                bool slot0 = nodes[i].AttachT < (p == 0 ? 0.825 : 0.65);
                // ⚑P BALANCE §1.3.2: slot 1 on s_k = TierPhase·(−1)^k, slot 0 on −s_k.
                int sk = ShootBuilder.TierSide(k, tree.Genome.Seed);
                Assert.AreEqual(slot0 ? -sk : sk, nodes[i].BranchSide, label + " node " + i);
                if (!slot0 || k == 0) bendTiers.Add(i);
            }
            // Bend tiers (slot 1 of every chain node) + the bottom tier (#0 slot 0) alternate strictly by height.
            bendTiers.Sort((a, b) => baseY[a].CompareTo(baseY[b]));
            for (int j = 1; j < bendTiers.Count; j++)
            {
                Assert.AreNotEqual(nodes[bendTiers[j - 1]].BranchSide, nodes[bendTiers[j]].BranchSide, label + ": bend tiers alternate");
            }
        }

        [Test]
        public void Tiers_Day40And80_EnoughPrimariesOnSeveralSegmentsAndBalanced()
        {
            var config = Config();
            var sim = ShapeMetrics.Simulator(config);
            foreach (SpeciesId species in AllSpecies)
            {
                for (uint s = 0; s <= Seeds; s++)
                {
                    uint seed = s == 0 ? GrowthTestKit.FixtureSeed : s;
                    var tree = ShapeMetrics.Grow(sim, seed, species, 40);
                    var d40 = ShapeMetrics.Primaries(tree, ShapeMetrics.Analyze(tree, config));
                    string label = species + " seed " + seed;
                    Assert.GreaterOrEqual(d40.Count, 4, label + " D40 primaries");
                    Assert.GreaterOrEqual(d40.DistinctSegments, 3, label + " D40 segments");
                    ShapeMetrics.RunDays(sim, tree, 40);
                    var d80 = ShapeMetrics.Primaries(tree, ShapeMetrics.Analyze(tree, config));
                    Assert.GreaterOrEqual(d80.Count, 6, label + " D80 primaries");
                    Assert.LessOrEqual(Math.Abs(d80.Left - d80.Right), 2, label + " D80 |left − right|");
                }
            }
        }

        // ---- AC4: S-trunk ------------------------------------------------------------------------------

        [Test]
        public void TrunkChain_AllSeedsSpeciesAndBends_SegmentsAlternateAroundLean()
        {
            var config = Config();
            var sim = ShapeMetrics.Simulator(config);
            foreach (SpeciesId species in AllSpecies)
            {
                foreach (double bend in new[] { -0.6, 0.0, 0.6 })
                {
                    for (uint seed = 1; seed <= Seeds; seed++)
                    {
                        var tree = ShapeMetrics.Grow(sim, seed, species, 40, 1.0, bend);
                        var shape = ShapeMetrics.Analyze(tree, config);
                        double lean = tree.Genome.BendBias * config.TrunkBendScaleDeg * GrowthFormulas.DegToRad;
                        int tier = TreeShapeAnalysis.TierPhase(seed);
                        int found = 0;
                        for (int i = 1; i < tree.Count; i++)
                        {
                            if (!shape.IsTrunkChain(i)) continue;
                            int k = tree.Nodes[i].Depth;
                            int expected = tier * ((k & 1) == 0 ? 1 : -1);
                            Assert.AreEqual(expected, Math.Sign(tree.WorldAngle(i) - lean), species + " bend " + bend + " seed " + seed + " k=" + k);
                            found++;
                        }
                        Assert.AreEqual(config.TrunkChainSegments - 1, found, "segments k = 1…4 exist by D40");
                    }
                }
            }
        }

        // ---- AC5 / AC6: taper --------------------------------------------------------------------------

        [Test]
        public void Taper_EveryDailyStep_ThicknessWithinCapNeverShrinksTrunkFollowsFormula()
        {
            var config = Config();
            var sim = ShapeMetrics.Simulator(config);
            foreach (SpeciesId species in AllSpecies)
            {
                for (uint seed = 1; seed <= 6; seed++)
                {
                    var tree = ShapeMetrics.Grow(sim, seed, species, 0);
                    var previous = new Dictionary<int, double>();
                    double trunk = tree.Nodes[0].Thickness;
                    for (int day = 1; day <= 120; day++)
                    {
                        bool nebari = tree.Count >= config.MaxNodes;
                        double k = nebari ? config.ThickeningRate * config.NebariThickeningMultiplier : config.ThickeningRate;
                        trunk = GrowthFormulas.Thicken(trunk, config.MaxThickness, k, 1.0, 1.0, 1.0);
                        ShapeMetrics.RunDays(sim, tree, 1);

                        var nodes = tree.Nodes;
                        Assert.AreEqual(trunk, nodes[0].Thickness, 1e-12, "trunk = TZ formula (Nebari ×3) day " + day);
                        for (int i = 1; i < nodes.Length; i++)
                        {
                            NodeData n = nodes[i];
                            // ⚑Q BALANCE §1.4.1(б) / §1.4.3: children of ord-0 parents — apical 0.75·th_p, lateral 0.38·th_at; else §1.2.2.
                            bool parentAxis = TreeShapeAnalysis.OrderOf(nodes, n.ParentIndex) == 0;
                            double cap = parentAxis
                                ? (n.BranchSide == 0 ? config.TrunkChainTaperRatio * nodes[n.ParentIndex].Thickness : config.PrimaryThicknessRatio * nodes[n.ParentIndex].Thickness * (1.0 - 0.3 * n.AttachT))
                                : (n.BranchSide == 0 ? 0.90 : 0.50) * nodes[n.ParentIndex].Thickness * (1.0 - 0.3 * n.AttachT);
                            Assert.LessOrEqual(n.Thickness, Math.Max(config.MinShootThickness, cap) + 1e-9, species + " seed " + seed + " day " + day + " node " + i);
                            if (previous.TryGetValue(n.Id, out double before)) Assert.GreaterOrEqual(n.Thickness, before, "never shrinks");
                            previous[n.Id] = n.Thickness;
                        }
                    }
                }
            }
        }

        /// <remarks>
        /// PA-102S AC20 «base / width at 2/3 of the chain ≥ 2.5» is superseded by ⚑Q BALANCE §1.4.1(б) + §1.4.0 (the upper trunk must
        /// stay thick: w(0.60H)/w(0.10H) ≈ 0.48, i.e. th0 / w(2/3 chain) ≈ 2.0–2.8); the visible profile is measured on the mesh
        /// (PreviewMetricsHarness, AC6). Here: the model side — the trunk still tapers (ratio &gt; 1.5) and never more than ×3.5.
        /// </remarks>
        [Test]
        public void Taper_Day40And80_TrunkTapersButUpperTrunkStaysThick()
        {
            var config = Config();
            var sim = ShapeMetrics.Simulator(config);
            foreach (SpeciesId species in AllSpecies)
            {
                for (uint seed = 1; seed <= Seeds; seed++)
                {
                    var tree = ShapeMetrics.Grow(sim, seed, species, 40);
                    double d40 = ShapeMetrics.TaperRatio(tree, ShapeMetrics.Analyze(tree, config), config);
                    ShapeMetrics.RunDays(sim, tree, 40);
                    double d80 = ShapeMetrics.TaperRatio(tree, ShapeMetrics.Analyze(tree, config), config);
                    Assert.That(d40, Is.InRange(1.5, 3.5), species + " seed " + seed + " D40");
                    Assert.That(d80, Is.InRange(1.5, 3.5), species + " seed " + seed + " D80");
                }
            }
        }

        // ---- AC7: horizontality ------------------------------------------------------------------------

        [TestCase(SpeciesId.Kuromatsu, 74.0, 86.0)]
        [TestCase(SpeciesId.Momiji, 62.0, 80.0)] // ⚑Q §1.4.3: mean 62° → 66° Proposed → 72° tuned (range 60–72°)
        [TestCase(SpeciesId.Yamazakura, 60.0, 80.0)]
        public void Primaries_Day80_MedianWorldAngleInSpeciesRange(SpeciesId species, double min, double max)
        {
            var config = Config();
            var sim = ShapeMetrics.Simulator(config);
            var medians = new double[Seeds];
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                var tree = ShapeMetrics.Grow(sim, seed, species, 80);
                medians[seed - 1] = ShapeMetrics.MedianPrimaryAngleDeg(tree, ShapeMetrics.Analyze(tree, config));
            }
            double median = ShapeMetrics.Median(medians, Seeds);
            TestContext.Out.WriteLine(species + " D80 median |primary angle| = " + median.ToString("0.0") + "°");
            Assert.That(median, Is.InRange(min, max));
        }

        [Test]
        public void Continuations_AllTrees_LeanTowardsHorizontalAndRespectPotLip()
        {
            var config = Config();
            var sim = ShapeMetrics.Simulator(config);
            double jitter = config.ApicalAngleJitterDeg * GrowthFormulas.DegToRad + 1e-9;
            double maxWorld = config.MaxShootWorldAngleDeg * GrowthFormulas.DegToRad + 1e-9;
            foreach (SpeciesId species in AllSpecies)
            {
                for (uint seed = 1; seed <= Seeds; seed++)
                {
                    var tree = ShapeMetrics.Grow(sim, seed, species, 80);
                    var shape = ShapeMetrics.Analyze(tree, config);
                    ShapeAsserts.AssertShootAngles(tree, config, SpeciesConfig.CreateDefault(species));
                    for (int i = 1; i < tree.Count; i++)
                    {
                        Assert.LessOrEqual(Math.Abs(tree.WorldAngle(i)), maxWorld);
                        int p = tree.Nodes[i].ParentIndex;
                        bool continuation = tree.Nodes[i].BranchSide == 0 && !shape.IsTrunkChain(p) && !shape.IsCrownAxis(p);
                        if (continuation) Assert.GreaterOrEqual(Math.Abs(tree.WorldAngle(i)), Math.Abs(tree.WorldAngle(p)) - jitter, species + " seed " + seed + " node " + i);
                    }
                }
            }
        }

        // ---- AC8: lengths ------------------------------------------------------------------------------

        [Test]
        public void TargetLength_WithoutJitter_MatchesSection13FormulasByRole()
        {
            // PA-102P AC9: chain k ≥ 1 = TrunkSegmentLength·Decay^(k−1); crown axis ×1.2×CrownAxisLengthFactor;
            // branch continuations ×BranchApicalLengthMultiplier×0.7^min(ordEff,2); laterals ×0.7^min(ordEff,2). Exact (1e-12).
            // ⚑R PA-102R (conscious update): nodes of a primary's subtree except its first node — exponent (d − 1) − c·k_t and × PadLengthFactor
            // (§1.5.2 а); the first node of a primary — reach compensation (§1.5.4 б), recomputed here independently of ShootBuilder.
            var config = Config();
            config.ShootLengthJitter = 0.0;
            config.TrunkSegmentJitter = 0.0;
            var sim = ShapeMetrics.Simulator(config);
            foreach (SpeciesId species in AllSpecies)
            {
                var speciesConfig = SpeciesConfig.CreateDefault(species);
                var tree = ShapeMetrics.Grow(sim, GrowthTestKit.FixtureSeed, species, 120);
                var shape = ShapeMetrics.Analyze(tree, config);
                int crown = 0, continuations = 0, padNodes = 0, primaries = 0;
                for (int i = 1; i < tree.Count; i++)
                {
                    NodeData n = tree.Nodes[i];
                    int tier = PadTier(tree, shape, i, config);
                    double exponent = tier >= 0 ? n.Depth - 1 - speciesConfig.PadTierDecayCompensation * tier : n.Depth - 1;
                    double baseLength = config.ShootBaseLength * Math.Pow(config.ShootLengthDecay, exponent) * (tier >= 0 ? config.PadLengthFactor : 1.0);
                    double orderFactor = Math.Pow(config.OrderLengthFactor, Math.Min(shape.EffectiveOrder(i), 2));
                    double expected;
                    if (shape.IsTrunkChain(i)) expected = config.TrunkSegmentLength * Math.Pow(config.TrunkSegmentDecay, n.Depth - 1);
                    else if (shape.IsCrownAxis(i))
                    {
                        expected = baseLength * config.ApicalLengthMultiplier * config.CrownAxisLengthFactor;
                        crown++;
                    }
                    else if (n.BranchSide == 0)
                    {
                        expected = baseLength * config.BranchApicalLengthMultiplier * orderFactor * AxisLengthFactor(tree, shape, i, config, speciesConfig);
                        continuations++;
                    }
                    else
                    {
                        expected = baseLength * orderFactor * AxisLengthFactor(tree, shape, i, config, speciesConfig);
                        if (shape.IsPrimary(i))
                        {
                            expected = ReachCompensated(tree, i, expected, config);
                            primaries++;
                        }
                    }
                    if (tier >= 0) padNodes++;
                    Assert.AreEqual(expected, n.TargetLength, 1e-9, species + " node " + i);
                }
                Assert.AreEqual(config.TrunkTargetLength, tree.Nodes[0].TargetLength);
                Assert.Greater(crown, 0, species + ": crown axis exists by D120");
                Assert.Greater(continuations, 10, species.ToString());
                Assert.Greater(padNodes, 20, species + ": primary subtrees exist");
                Assert.Greater(primaries, 4, species.ToString());
            }
        }

        /// <summary>⚑Q BALANCE §1.4.3 / §1.4.4 (AC14): PrimaryLengthFactor (⚑R §1.5.7: per species) on every ord-1 node of an axis rooted on the chain,
        /// CrownLateralLengthFactor on every ord-1 node of an axis rooted on the crown axis, 1 otherwise (ord ≥ 2 included).</summary>
        static double AxisLengthFactor(TreeGraph tree, TreeShapeAnalysis shape, int i, GrowthConfig config, SpeciesConfig species)
        {
            if (shape.Order(i) != 1) return 1.0;
            int root = i;
            while (tree.Nodes[root].BranchSide == 0) root = tree.Nodes[root].ParentIndex;
            int p = tree.Nodes[root].ParentIndex;
            return shape.IsTrunkChain(p) ? species.PrimaryLengthFactor : config.CrownLateralLengthFactor;
        }

        /// <summary>⚑R §1.5.2(а): k_t = Depth of the chain node a primary sits on, for nodes of its subtree except the primary itself; −1 otherwise.</summary>
        static int PadTier(TreeGraph tree, TreeShapeAnalysis shape, int i, GrowthConfig config)
        {
            if (shape.Order(i) == 0 || shape.IsPrimary(i)) return -1;
            int top = -1;
            for (int j = i; j > 0; j = tree.Nodes[j].ParentIndex) if (tree.Nodes[j].BranchSide != 0) top = j;
            int p = tree.Nodes[top].ParentIndex;
            return shape.IsTrunkChain(p) ? tree.Nodes[p].Depth : -1;
        }

        /// <summary>⚑R §1.5.4(б), independent: L' = clamp(L − g·o / max(|sin θ_p|, 0.5), 0.6L, 1.4L), o = −side·(x_attach − x_lean(y_attach)).</summary>
        static double ReachCompensated(TreeGraph tree, int i, double length, GrowthConfig config)
        {
            NodeData n = tree.Nodes[i];
            double x = 0.0, y = 0.0;
            // Chain from #0 to the parent (TargetLength, world angles, no curvature), then AttachT along the parent.
            var chain = new List<int>();
            for (int j = n.ParentIndex; j >= 0; j = tree.Nodes[j].ParentIndex) chain.Insert(0, j);
            for (int c = 0; c < chain.Count; c++)
            {
                int j = chain[c];
                double f = c == chain.Count - 1 ? n.AttachT : 1.0;
                double w = tree.WorldAngle(j);
                x += -f * tree.Nodes[j].TargetLength * Math.Sin(w);
                y += f * tree.Nodes[j].TargetLength * Math.Cos(w);
            }
            double lean = tree.Genome.BendBias * config.TrunkBendScaleDeg * GrowthFormulas.DegToRad;
            double o = -n.BranchSide * (x - (-y * Math.Tan(lean)));
            double sin = Math.Max(Math.Abs(Math.Sin(tree.WorldAngle(i))), 0.5);
            return Math.Min(Math.Max(length - config.PrimaryReachCompensation * o / sin, 0.6 * length), 1.4 * length);
        }

        [Test]
        public void TargetLength_TrunkSegmentJitter_UsesU5WithinEightPercent()
        {
            var config = Config();
            var sim = ShapeMetrics.Simulator(config);
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                var tree = ShapeMetrics.Grow(sim, seed, SpeciesId.Momiji, 60);
                var shape = ShapeMetrics.Analyze(tree, config);
                for (int i = 1; i < tree.Count; i++)
                {
                    if (!shape.IsTrunkChain(i)) continue;
                    double center = config.TrunkSegmentLength * Math.Pow(config.TrunkSegmentDecay, tree.Nodes[i].Depth - 1);
                    Assert.That(tree.Nodes[i].TargetLength, Is.InRange(center * 0.92 - 1e-9, center * 1.08 + 1e-9), "seed " + seed + " k=" + tree.Nodes[i].Depth);
                }
            }
        }

        // ---- AC10: pads --------------------------------------------------------------------------------

        [Test]
        public void Pads_FixtureDay40_AtLeastFivePadsMostlyOnBranches()
        {
            var config = Config();
            foreach (SpeciesId species in AllSpecies)
            {
                var tree = ShapeMetrics.Grow(ShapeMetrics.Simulator(config), GrowthTestKit.FixtureSeed, species, 40);
                var shape = ShapeMetrics.Analyze(tree, config);
                double fraction = ShapeMetrics.LeafyInBranchPadsFraction(shape);
                TestContext.Out.WriteLine(species + " fixture D40: pads = " + shape.PadCount + ", leafy in ordEff ≥ 1 pads = " + fraction.ToString("0.00"));
                Assert.GreaterOrEqual(shape.PadCount, 5, species.ToString());
                Assert.GreaterOrEqual(fraction, 0.70, species.ToString());
            }
        }

        // PA-102P AC13 (was PA-102S AC10): with InPad (BALANCE §1.3.4) the primary's own base is bare in every species.
        [TestCase(SpeciesId.Kuromatsu)]
        [TestCase(SpeciesId.Momiji)]
        [TestCase(SpeciesId.Yamazakura)]
        public void Pads_Day80_MostPrimariesHaveBareBase(SpeciesId species)
        {
            var config = Config();
            var sim = ShapeMetrics.Simulator(config);
            var fractions = new double[Seeds];
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                var tree = ShapeMetrics.Grow(sim, seed, species, 80);
                var stats = ShapeMetrics.Primaries(tree, ShapeMetrics.Analyze(tree, config));
                fractions[seed - 1] = stats.Count == 0 ? 0.0 : stats.BareBase / (double)stats.Count;
            }
            double median = ShapeMetrics.Median(fractions, Seeds);
            TestContext.Out.WriteLine(species + " D80 median bare-base fraction = " + median.ToString("0.00"));
            Assert.GreaterOrEqual(median, 0.60);
        }

        // ---- AC11: back-budding ------------------------------------------------------------------------

        [Test]
        public void PruneTip_TerminalApicalNode_ParentBecomesLeafyAxisTipAndRecordsCutDay()
        {
            // PA-102P AC15: Prune writes LastCutDay = GrowthDayIndex on the parent; the parent becomes the axis tip (AxisRemain 0, InPad).
            var config = Config();
            var sim = ShapeMetrics.Simulator(config);
            int checkedCuts = 0;
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                var tree = ShapeMetrics.Grow(sim, seed, SpeciesId.Momiji, 40);
                var shape = ShapeMetrics.Analyze(tree, config);
                for (int i = tree.Count - 1; i > 0; i--)
                {
                    int p = tree.Nodes[i].ParentIndex;
                    if (shape.ChildCount(i) != 0 || tree.Nodes[i].BranchSide != 0 || shape.Order(p) < 1) continue;
                    int parentId = tree.Nodes[p].Id;
                    bool parentHasLeaves = tree.Nodes[p].HasLeaves;
                    Assert.AreEqual(NodeData.NoCut, tree.Nodes[p].LastCutDay);
                    TreePruning.Prune(tree, tree.Nodes[i].Id, config);
                    shape = ShapeMetrics.Analyze(tree, config);
                    int now = tree.IndexOf(parentId);
                    Assert.AreEqual(tree.Growth.GrowthDayIndex, tree.Nodes[now].LastCutDay, "LastCutDay = GrowthDayIndex");
                    Assert.AreEqual(0, shape.AxisRemain(now));
                    Assert.IsTrue(shape.InPad(now));
                    Assert.AreEqual(0, shape.BackBudDist(now), "fresh cut");
                    Assert.AreEqual(parentHasLeaves, shape.IsLeafy(now));
                    checkedCuts++;
                    break;
                }
            }
            Assert.Greater(checkedCuts, 20);
        }

        // PA-102P AC15 (was PA-102S AC11): back-budding — BALANCE §1.3.5 (×BackBudWeightMultiplier within 2 edges for 14 days).
        [TestCase(SpeciesId.Kuromatsu)]
        [TestCase(SpeciesId.Momiji)]
        [TestCase(SpeciesId.Yamazakura)]
        public void PruneTip_Day40_NextShootsOfBranchMostlyNearCut(SpeciesId species)
        {
            var config = Config();
            var sim = ShapeMetrics.Simulator(config);
            var fractions = new List<double>();
            int near = 0, counted = 0;
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                var tree = ShapeMetrics.Grow(sim, seed, species, 40);
                int n = ShapeMetrics.BackBuddingNearCut(sim, tree, out int c, out _);
                if (n < 0 || c == 0) continue;
                near += n;
                counted += c;
                fractions.Add(n / (double)c);
            }
            double median = ShapeMetrics.Median(fractions.ToArray(), fractions.Count);
            TestContext.Out.WriteLine(species + " back-budding: " + near + " / " + counted + " shoots ≤ 2 edges from the cut, median per seed " + median.ToString("0.00"));
            Assert.GreaterOrEqual(fractions.Count, 25);
            Assert.GreaterOrEqual(median, 0.5);
        }

        // ---- AC12: interior shoots ---------------------------------------------------------------------

        // PA-102P AC14 (was PA-102S AC12): ≥ 1 interior shoot (lateral of a ¬InPad node) — D40 in ≥ 50% of seeds, D60 in ≥ 70%.
        [TestCase(SpeciesId.Kuromatsu)]
        [TestCase(SpeciesId.Momiji)]
        [TestCase(SpeciesId.Yamazakura)]
        public void InteriorShoots_Day40And60_MostSeedsHaveOne(SpeciesId species)
        {
            var config = Config();
            var sim = ShapeMetrics.Simulator(config);
            int by40 = 0, by60 = 0;
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                var tree = ShapeMetrics.Grow(sim, seed, species, 0);
                int first = ShapeMetrics.InteriorLateralShoots(sim, tree, 40);
                int second = ShapeMetrics.InteriorLateralShoots(sim, tree, 20);
                if (first > 0) by40++;
                if (first + second > 0) by60++;
            }
            TestContext.Out.WriteLine(species + ": interior shoot by D40 in " + by40 + " / " + Seeds + ", by D60 in " + by60 + " / " + Seeds);
            Assert.GreaterOrEqual(by40, (int)Math.Ceiling(0.50 * Seeds));
            Assert.GreaterOrEqual(by60, (int)Math.Ceiling(0.70 * Seeds));
        }

        // ---- AC13: planting ----------------------------------------------------------------------------

        sealed class CountingRandom : IRandomSource
        {
            readonly Pcg32 _inner;
            public int UIntCalls;
            public int OtherCalls;

            public CountingRandom(ulong seed) => _inner = new Pcg32(seed, 0UL);

            public uint NextUInt()
            {
                UIntCalls++;
                return _inner.NextUInt();
            }

            public uint NextUInt(uint bound)
            {
                OtherCalls++;
                return _inner.NextUInt(bound);
            }

            public double NextDouble()
            {
                OtherCalls++;
                return _inner.NextDouble();
            }
        }

        [Test]
        public void PlantSapling_AnySeedAndSpecies_Exactly18DrawsAndIdenticalToDefaultStream()
        {
            var config = Config();
            foreach (SpeciesId species in AllSpecies)
            {
                for (uint seed = 1; seed <= Seeds; seed++)
                {
                    var genome = new TreeGenome(seed, species);
                    var counting = new CountingRandom(seed);
                    var tree = TreeFactory.PlantSapling(genome, config, SpeciesConfig.CreateDefault(species), counting);
                    Assert.AreEqual(18, counting.UIntCalls);
                    Assert.AreEqual(18, TreeFactory.PlantingDraws);
                    Assert.AreEqual(0, counting.OtherCalls);
                    Assert.AreEqual(3, tree.Count);
                    Assert.IsTrue(tree.ToSnapshot().BitwiseEquals(TreeFactory.PlantSapling(genome, config, SpeciesConfig.CreateDefault(species)).ToSnapshot()));
                }
            }
        }

        [Test]
        public void PlantSapling_TwigCut_NextTrunkLateralStartsAtUpperSlots()
        {
            var config = Config();
            var sim = ShapeMetrics.Simulator(config);
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                var tree = ShapeMetrics.Grow(sim, seed, SpeciesId.Kuromatsu, 0);
                int nextId = tree.NextId;
                int found = -1;
                for (int day = 0; day < 40 && found < 0; day++)
                {
                    ShapeMetrics.RunDays(sim, tree, 1);
                    for (int i = 1; i < tree.Count; i++)
                    {
                        if (tree.Nodes[i].Id >= nextId && tree.Nodes[i].ParentIndex == 0 && tree.Nodes[i].BranchSide != 0)
                        {
                            found = i;
                            break;
                        }
                    }
                }
                Assert.GreaterOrEqual(found, 0, "seed " + seed);
                Assert.GreaterOrEqual(tree.Nodes[found].AttachT, 0.66, "seed " + seed);
            }
        }

        // ---- AC14: determinism of a shaped tree (stream layout: GrowthSimulatorTests) ---------------------

        [Test]
        public void Shape_SameGenome_BitwiseIdenticalAcrossSnapshotsAndAnalyses()
        {
            var config = Config();
            var sim = ShapeMetrics.Simulator(config);
            var a = ShapeMetrics.Grow(sim, 77u, SpeciesId.Momiji, 60, 1.2, -0.4);
            var b = ShapeMetrics.Grow(sim, 77u, SpeciesId.Momiji, 30, 1.2, -0.4);
            b = TreeGraph.FromSnapshot(b.ToSnapshot());
            ShapeMetrics.RunDays(sim, b, 30);
            Assert.IsTrue(a.ToSnapshot().BitwiseEquals(b.ToSnapshot()));
        }
    }
}
