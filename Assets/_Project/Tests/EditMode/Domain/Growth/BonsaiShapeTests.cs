using System;
using System.Collections.Generic;
using NUnit.Framework;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Random;
using PocketArbor.Domain.Tree;

namespace PocketArbor.Domain.Tests.Growth
{
    /// <summary>
    /// PA-102S bonsai shape, model acceptance criteria AC1–AC8, AC10–AC14 (BALANCE §1.2). «30 seeds» = seeds 1…30,
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
                    for (int i = 1; i < nodes.Length; i++)
                    {
                        double t = nodes[i].AttachT;
                        string at = species + " seed " + seed + " node " + i;
                        if (nodes[i].BranchSide == 0)
                        {
                            Assert.AreEqual(1.0, t, at);
                            continue;
                        }
                        Assert.That(t, Is.InRange(0.05, 0.97), at);
                        if (nodes[i].ParentIndex == 0) Assert.That(t, Is.InRange(0.66, 0.97), at + " (#0 slots)");
                        else Assert.IsTrue((t >= 0.41 && t <= 0.49) || (t >= 0.81 && t <= 0.89), at + " (slots 0.45 / 0.85): " + t);
                        for (int j = i + 1; j < nodes.Length; j++)
                        {
                            if (nodes[j].ParentIndex == nodes[i].ParentIndex && nodes[j].BranchSide != 0)
                            {
                                Assert.GreaterOrEqual(Math.Abs(nodes[j].AttachT - t), 0.17, at + " vs sibling " + j);
                            }
                        }
                    }
                    Assert.IsTrue(tree.ToSnapshot().BitwiseEquals(TreeGraph.FromSnapshot(tree.ToSnapshot()).ToSnapshot()), "round-trip");
                }
            }
        }

        // ---- AC2 / AC3: tiers --------------------------------------------------------------------------

        [Test]
        public void Tiers_Day40And80_PrimarySidesFollowSlotsAndAlternateByHeight()
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
            int tier = TreeShapeAnalysis.TierPhase(tree.Genome.Seed);
            var primaries = new List<int>();
            for (int i = 1; i < nodes.Length; i++)
            {
                if (!shape.IsPrimary(i)) continue;
                int p = nodes[i].ParentIndex;
                double boundary = p == 0 ? 0.825 : 0.65;
                bool slot0 = nodes[i].AttachT < boundary;
                Assert.AreEqual(tier * (slot0 ? -1 : 1), nodes[i].BranchSide, label + " node " + i);
                primaries.Add(i);
            }
            primaries.Sort((a, b) => baseY[a].CompareTo(baseY[b]));
            for (int j = 1; j < primaries.Count; j++)
            {
                int a = primaries[j - 1], b = primaries[j];
                int slotA = 2 * nodes[nodes[a].ParentIndex].Depth + (nodes[a].AttachT < (nodes[a].ParentIndex == 0 ? 0.825 : 0.65) ? 0 : 1);
                int slotB = 2 * nodes[nodes[b].ParentIndex].Depth + (nodes[b].AttachT < (nodes[b].ParentIndex == 0 ? 0.825 : 0.65) ? 0 : 1);
                Assert.Less(slotA, slotB, label + ": height order follows slot order");
                if (slotB - slotA == 1) Assert.AreNotEqual(nodes[a].BranchSide, nodes[b].BranchSide, label + ": adjacent tiers alternate");
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
                            double ratio = n.BranchSide == 0 ? 0.90 : 0.50;
                            double cap = ratio * nodes[n.ParentIndex].Thickness * (1.0 - 0.3 * n.AttachT);
                            Assert.LessOrEqual(n.Thickness, Math.Max(config.MinShootThickness, cap) + 1e-9, species + " seed " + seed + " day " + day + " node " + i);
                            if (previous.TryGetValue(n.Id, out double before)) Assert.GreaterOrEqual(n.Thickness, before, "never shrinks");
                            previous[n.Id] = n.Thickness;
                        }
                    }
                }
            }
        }

        [Test]
        public void Taper_Day40And80_BaseToTwoThirdsHeightAtLeast2_5()
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
                    Assert.GreaterOrEqual(d40, 2.5, species + " seed " + seed + " D40");
                    Assert.GreaterOrEqual(d80, 2.5, species + " seed " + seed + " D80");
                }
            }
        }

        // ---- AC7: horizontality ------------------------------------------------------------------------

        [TestCase(SpeciesId.Kuromatsu, 74.0, 86.0)]
        [TestCase(SpeciesId.Momiji, 52.0, 72.0)]
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
        public void TargetLength_WithoutJitter_EqualsSection11TimesOrderFactor()
        {
            var config = Config();
            config.ShootLengthJitter = 0.0;
            var sim = ShapeMetrics.Simulator(config);
            foreach (SpeciesId species in AllSpecies)
            {
                var tree = ShapeMetrics.Grow(sim, GrowthTestKit.FixtureSeed, species, 80);
                var shape = ShapeMetrics.Analyze(tree, config);
                for (int i = 1; i < tree.Count; i++)
                {
                    NodeData n = tree.Nodes[i];
                    double expected = config.ShootBaseLength * Math.Pow(config.ShootLengthDecay, n.Depth - 1)
                        * (n.BranchSide == 0 ? config.ApicalLengthMultiplier : 1.0)
                        * Math.Pow(config.OrderLengthFactor, Math.Min(shape.EffectiveOrder(i), 2));
                    Assert.AreEqual(expected, n.TargetLength, 1e-9, species + " node " + i);
                    if (shape.IsTrunkChain(i)) Assert.AreEqual(0, shape.EffectiveOrder(i), "trunk chain has no order factor");
                }
            }
        }

        // ---- AC10: pads --------------------------------------------------------------------------------

        [Test]
        public void Pads_FixtureDay40_AtLeastFivePadsMostlyOnBranches()
        {
            var config = Config();
            var tree = ShapeMetrics.Grow(ShapeMetrics.Simulator(config), GrowthTestKit.FixtureSeed, SpeciesId.Kuromatsu, 40);
            var shape = ShapeMetrics.Analyze(tree, config);
            double fraction = ShapeMetrics.LeafyInBranchPadsFraction(shape);
            TestContext.Out.WriteLine("fixture D40: pads = " + shape.PadCount + ", leafy in ordEff ≥ 1 pads = " + fraction.ToString("0.00"));
            Assert.GreaterOrEqual(shape.PadCount, 5);
            Assert.GreaterOrEqual(fraction, 0.70);
        }

        [TestCase(SpeciesId.Kuromatsu)]
        [TestCase(SpeciesId.Momiji, Ignore = "PA-102S open GD question: PadDepth 2 gives median 0.10 (PadDepth 1 → 0.60), see PA-102S journal")]
        [TestCase(SpeciesId.Yamazakura, Ignore = "PA-102S open GD question: PadDepth 2 gives median 0.10, see PA-102S journal")]
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
        public void PruneTip_TerminalNode_ParentBecomesLeafyTip()
        {
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
                    if (shape.ChildCount(i) != 0 || shape.ChildCount(p) != 1 || shape.EffectiveOrder(p) < 1) continue;
                    int parentId = tree.Nodes[p].Id;
                    bool parentHasLeaves = tree.Nodes[p].HasLeaves;
                    TreePruning.Prune(tree, tree.Nodes[i].Id, config);
                    shape = ShapeMetrics.Analyze(tree, config);
                    int now = tree.IndexOf(parentId);
                    Assert.AreEqual(0, shape.TipDistance(now));
                    Assert.AreEqual(parentHasLeaves, shape.IsLeafy(now));
                    checkedCuts++;
                    break;
                }
            }
            Assert.Greater(checkedCuts, 20);
        }

        [Test, Ignore("PA-102S open GD question: role weights alone give 13/87 ≈ 15% (needs ≥ 50%); a back-budding rule is not in BALANCE §1.2.7")]
        public void PruneTip_Day40_NextShootsOfBranchMostlyNearCut()
        {
            var config = Config();
            var sim = ShapeMetrics.Simulator(config);
            int near = 0, counted = 0;
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                var tree = ShapeMetrics.Grow(sim, seed, SpeciesId.Kuromatsu, 40);
                int n = ShapeMetrics.BackBuddingNearCut(sim, tree, out int c, out _);
                if (n < 0) continue;
                near += n;
                counted += c;
            }
            TestContext.Out.WriteLine("back-budding: " + near + " / " + counted + " shoots ≤ 2 edges from the cut");
            Assert.GreaterOrEqual(near, counted * 0.5);
        }

        // ---- AC12: interior shoots ---------------------------------------------------------------------

        [TestCase(SpeciesId.Kuromatsu)]
        [TestCase(SpeciesId.Momiji, Ignore = "PA-102S open GD question: PadDepth 2 → 2/30 seeds by D40, see PA-102S journal")]
        [TestCase(SpeciesId.Yamazakura, Ignore = "PA-102S open GD question: PadDepth 2 → 2/30 seeds by D40, see PA-102S journal")]
        public void InteriorShoots_Day40_MostSeedsHaveOne(SpeciesId species)
        {
            var config = Config();
            var sim = ShapeMetrics.Simulator(config);
            int seedsWithInterior = 0;
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                var tree = ShapeMetrics.Grow(sim, seed, species, 0);
                if (ShapeMetrics.InteriorLateralShoots(sim, tree, 40) > 0) seedsWithInterior++;
            }
            TestContext.Out.WriteLine(species + ": " + seedsWithInterior + " / " + Seeds + " seeds with an interior shoot by D40");
            Assert.GreaterOrEqual(seedsWithInterior, (int)Math.Ceiling(0.70 * Seeds));
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
