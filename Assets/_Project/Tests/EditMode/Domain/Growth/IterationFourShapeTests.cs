using System;
using System.Collections.Generic;
using NUnit.Framework;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Random;
using PocketArbor.Domain.Soil;
using PocketArbor.Domain.Tree;

namespace PocketArbor.Domain.Tests.Growth
{
    /// <summary>
    /// PA-102T model acceptance criteria (BALANCE §1.6): T1 tier-balance formula, TierGroup table, «balance off ≡ PA-102R» bitwise, weight group = group of
    /// the built shoot (TD-065/066); T3 pinch; T4 laterals-first dome, crown shoulder, crown-axis lean; T5 RNG / rules version; defaults within §1.6.8.
    /// «30 seeds» = 1…30, G = 1, vigor 1, from the post-onboarding state.
    /// </summary>
    public sealed class IterationFourShapeTests
    {
        const int Seeds = ShapeMetrics.MonteCarloSeeds;
        const double Exact = 1e-12;
        static readonly SpeciesId[] AllSpecies = { SpeciesId.Kuromatsu, SpeciesId.Momiji, SpeciesId.Yamazakura };

        /// <summary>PA-102R (commit e2b4ace) fingerprints of 30 seeds × D120 Wild per species (FNV-1a over every node field) — recorded with the PA-102R code.</summary>
        static readonly Dictionary<SpeciesId, ulong> LegacyFingerprints = new Dictionary<SpeciesId, ulong>
        {
            { SpeciesId.Kuromatsu, 0x8FAF9002288DECA3UL },
            { SpeciesId.Momiji, 0x86F0BA6C87459312UL },
            { SpeciesId.Yamazakura, 0x68A5D091717AF47FUL },
        };

        static ulong Fingerprint(TreeGraph tree)
        {
            ulong h = 14695981039346656037UL;
            void Mix(long v)
            {
                unchecked
                {
                    for (int b = 0; b < 8; b++)
                    {
                        h ^= (ulong)((v >> (8 * b)) & 0xFF);
                        h *= 1099511628211UL;
                    }
                }
            }
            ReadOnlySpan<NodeData> nodes = tree.Nodes;
            Mix(nodes.Length);
            for (int i = 0; i < nodes.Length; i++)
            {
                NodeData n = nodes[i];
                Mix(n.Id); Mix(n.ParentIndex); Mix(n.Depth); Mix(n.AgeDays); Mix((long)n.Status); Mix(n.BranchSide); Mix(n.LastCutDay);
                Mix(BitConverter.DoubleToInt64Bits(n.Length)); Mix(BitConverter.DoubleToInt64Bits(n.TargetLength));
                Mix(BitConverter.DoubleToInt64Bits(n.Thickness)); Mix(BitConverter.DoubleToInt64Bits(n.BaseAngle));
                Mix(BitConverter.DoubleToInt64Bits(n.Curvature)); Mix(BitConverter.DoubleToInt64Bits(n.AttachT));
            }
            return h;
        }

        /// <summary>Every §1.6 model rule off: balance (share 0, fed = cap = 1), pinch, laterals first, shoulder, crown-axis lean, PA-102R crown-axis length 0.70.</summary>
        static GrowthConfig LegacyConfig()
        {
            var c = GrowthConfig.CreateDefault();
            c.TierHungryShare = 0.0;
            c.TierFedFactor = 1.0;
            c.TierCapFactor = 1.0;
            c.PinchLateralOnly = false;
            c.CrownApicalNeedsLaterals = 0;
            c.CrownShoulderSlotT = 0.0;
            c.CrownAxisLengthFactor = 0.70;
            return c;
        }

        static SpeciesCatalog LegacyCatalog()
        {
            var catalog = SpeciesCatalog.CreateDefault();
            foreach (SpeciesId id in AllSpecies) catalog.Get(id).CrownAxisLeanDeg = 0.0;
            return catalog;
        }

        // ---- Defaults (§1.6 Proposed, ranges §1.6.8) -----------------------------------------------------------

        [Test]
        public void Defaults_Section16_WithinTuningRanges()
        {
            var c = GrowthConfig.CreateDefault();
            Assert.That(c.TierHungryShare, Is.InRange(0.20, 0.40));
            Assert.That(c.TierDeficitGain, Is.InRange(1.5, 3.0));
            Assert.That(c.TierFedFactor, Is.InRange(0.20, 0.50));
            Assert.That(c.TierCapRatio, Is.InRange(1.20, 1.50));
            Assert.That(c.TierCapFactor, Is.InRange(0.0, 0.15));
            Assert.That(c.CrownApicalNeedsLaterals, Is.InRange(1, 2));
            Assert.That(c.CrownShoulderSlotT, Is.InRange(0.90, 0.98));
            Assert.That(c.CrownAxisLengthFactor, Is.InRange(0.50, 0.70));
            Assert.IsTrue(c.PinchLateralOnly);
            int[] proposed = { 13, 13, 11, 10, 9, 8 };
            foreach (SpeciesId id in AllSpecies)
            {
                SpeciesConfig s = SpeciesConfig.CreateDefault(id);
                for (int t = 0; t < 6; t++) Assert.That(s.PadNodeTarget[t], Is.InRange(proposed[t] - 2, proposed[t] + 2), id + " t" + t);
                for (int t = 2; t < 6; t++) Assert.LessOrEqual(s.PadNodeTarget[t], s.PadNodeTarget[t - 1], id + " t1…t5 non-increasing");
                Assert.LessOrEqual(Math.Abs(s.PadNodeTarget[0] - s.PadNodeTarget[1]), 1, id + " t0 = t1 ± 1");
                Assert.That(s.DomeNodeTarget, Is.InRange(12, 20));
                Assert.That(s.CrownAxisLeanDeg, Is.InRange(0.0, 12.0));
            }
            Assert.AreEqual(5, GrowthRules.Version, "T5: rules v5");
        }

        // ---- T1: B_g ------------------------------------------------------------------------------------------

        [Test]
        public void TierBalanceFactor_ThreeRegimes_MatchBalanceFormula()
        {
            var c = GrowthConfig.CreateDefault();
            c.TierHungryShare = 0.30;
            c.TierDeficitGain = 2.0;
            c.TierFedFactor = 0.30;
            c.TierCapRatio = 1.30;
            c.TierCapFactor = 0.05;
            // Hungry, deficit saturated (N < T/2): max(R_g, 0.30·1·R)/R_g.
            Assert.AreEqual(0.30 * 1.0 * 40.0 / 2.0, GrowthFormulas.TierBalanceFactor(2.0, 40.0, 3, 8, c), Exact);
            // Hungry, partial deficit: d = 2·(8 − 6)/8 = 0.5.
            Assert.AreEqual(0.30 * 0.5 * 40.0 / 2.0, GrowthFormulas.TierBalanceFactor(2.0, 40.0, 6, 8, c), Exact);
            // Hungry but already above the floor → 1.
            Assert.AreEqual(1.0, GrowthFormulas.TierBalanceFactor(20.0, 40.0, 6, 8, c), Exact);
            // Fed: T ≤ N < 1.3·T.
            Assert.AreEqual(0.30, GrowthFormulas.TierBalanceFactor(5.0, 40.0, 8, 8, c), Exact);
            Assert.AreEqual(0.30, GrowthFormulas.TierBalanceFactor(5.0, 40.0, 10, 8, c), Exact);
            // Over the ceiling: N ≥ 1.3·T (10.4 → 11).
            Assert.AreEqual(0.05, GrowthFormulas.TierBalanceFactor(5.0, 40.0, 11, 8, c), Exact);
            // No target / no mass → 1 (TD-066: R_g = 0 → B = 1, never NaN).
            Assert.AreEqual(1.0, GrowthFormulas.TierBalanceFactor(5.0, 40.0, 3, -1, c), Exact);
            Assert.AreEqual(1.0, GrowthFormulas.TierBalanceFactor(0.0, 40.0, 3, 8, c), Exact);
            Assert.AreEqual(1.0, GrowthFormulas.TierBalanceFactor(double.NaN, 40.0, 3, 8, c), Exact);
            Assert.IsFalse(double.IsNaN(GrowthFormulas.TierBalanceFactor(1.0, double.NaN, 3, 8, c)));
            // Off: share 0, fed = cap = 1 → exactly 1.
            c.TierHungryShare = 0.0;
            c.TierFedFactor = 1.0;
            c.TierCapFactor = 1.0;
            foreach (int n in new[] { 0, 4, 8, 12 }) Assert.AreEqual(1.0, GrowthFormulas.TierBalanceFactor(0.37, 40.0, n, 8, c), 0.0);
        }

        [Test]
        public void BalanceOff_30Seeds3SpeciesD120_BitwisePa102R()
        {
            var config = LegacyConfig();
            foreach (SpeciesId id in AllSpecies)
            {
                var sim = new GrowthSimulator(config, SoilConfig.CreateDefault(), LegacyCatalog());
                ulong all = 14695981039346656037UL;
                for (uint seed = 1; seed <= Seeds; seed++)
                {
                    TreeGraph tree = ShapeMetrics.Grow(sim, seed, id, 120);
                    unchecked { all = (all ^ Fingerprint(tree)) * 1099511628211UL; }
                }
                Assert.AreEqual(LegacyFingerprints[id], all, id + ": with every §1.6 rule off the growth is bitwise PA-102R");
            }
        }

        // ---- T1: TierGroup table --------------------------------------------------------------------------------

        [Test]
        public void TierGroup_EveryNodeType_FollowsTable()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = ShapeMetrics.Simulator(config);
            int shoulders = 0;
            var seen = new HashSet<TierGroup>();
            foreach (SpeciesId id in AllSpecies)
            {
                for (uint seed = 1; seed <= 10; seed++)
                {
                    TreeGraph tree = ShapeMetrics.Grow(sim, seed, id, 120);
                    var shape = ShapeMetrics.Analyze(tree, config);
                    ReadOnlySpan<NodeData> nodes = tree.Nodes;
                    var counts = new int[TierGroups.Count];
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        TierGroup g = shape.GroupOf(i);
                        counts[(int)g]++;
                        seen.Add(g);
                        int p = nodes[i].ParentIndex;
                        if (shape.IsTrunkChain(i)) Assert.AreEqual(TierGroup.Chain, g);
                        else if (shape.IsCrownAxis(i) || shape.IsCrownShoulder(i)) Assert.AreEqual(TierGroup.Dome, g);
                        else if (p >= 0 && shape.IsTrunkChain(p) && nodes[i].BranchSide != 0)
                        {
                            int k = nodes[p].Depth;
                            bool slot1 = nodes[i].BranchSide == ShootBuilder.TierSide(k, seed);
                            TierGroup expected = k == 0 ? (nodes[i].Id == TreeFactory.SaplingTwigId ? TierGroup.Other : slot1 ? TierGroup.T1 : TierGroup.T0)
                                : slot1 ? (TierGroup)(k + 1) : TierGroup.Other;
                            Assert.AreEqual(expected, g, id + " seed " + seed + " lateral of k" + k);
                        }
                        else if (p >= 0) Assert.AreEqual(shape.GroupOf(p), g, "a node inherits its parent's group");
                        if (shape.IsCrownShoulder(i))
                        {
                            shoulders++;
                            Assert.IsTrue(shape.InDome(i));
                            Assert.IsFalse(shape.IsPrimary(i));
                        }
                    }
                    for (int g = 0; g < TierGroups.Count; g++) Assert.AreEqual(counts[g], shape.GroupCount((TierGroup)g), "N_g");
                }
            }
            Assert.Greater(shoulders, 10, "crown shoulders grow");
            foreach (TierGroup g in new[] { TierGroup.T0, TierGroup.T1, TierGroup.T2, TierGroup.T3, TierGroup.T4, TierGroup.T5, TierGroup.Dome, TierGroup.Chain })
                Assert.IsTrue(seen.Contains(g), g + " seen");
        }

        [Test]
        public void TierGroup_SaplingTwig_IsOther()
        {
            var config = GrowthConfig.CreateDefault();
            TreeGraph tree = GrowthTestKit.Sapling(7u, SpeciesId.Momiji, config: config);
            Assert.AreEqual(TreeFactory.SaplingTwigId, tree.Nodes[2].Id);
            var shape = ShapeMetrics.Analyze(tree, config);
            Assert.AreEqual(TierGroup.Other, shape.GroupOf(2));
            Assert.AreEqual(TierGroup.Chain, shape.GroupOf(0));
            Assert.AreEqual(TierGroup.Chain, shape.GroupOf(1));
        }

        [Test]
        public void WeightGroup_EqualsGroupOfTheBuiltShoot()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = ShapeMetrics.Simulator(config);
            int checkedShoots = 0;
            var u = new ShootDraws { U1 = 0.5, U2 = 0.4, U3 = 0.6, U4 = 0.3, U5 = 0.5, U6 = 0.5, U7 = 0.5, U8 = 0.5 };
            foreach (SpeciesId id in AllSpecies)
            {
                SpeciesConfig species = SpeciesConfig.CreateDefault(id);
                for (uint seed = 1; seed <= 10; seed++)
                {
                    TreeGraph tree = ShapeMetrics.Grow(sim, seed, id, 0);
                    for (int day = 1; day <= 100; day++)
                    {
                        ShapeMetrics.RunDays(sim, tree, 1);
                        if (day % 7 != 0 || tree.Count >= config.MaxNodes) continue;
                        var shape = new TreeShapeAnalysis();
                        shape.Analyze(tree, config, species);
                        ReadOnlySpan<NodeData> nodes = tree.Nodes;
                        for (int i = 0; i < nodes.Length; i++)
                        {
                            if (nodes[i].Status != NodeStatus.Mature || shape.ChildCount(i) >= config.MaxChildrenPerNode) continue;
                            bool noApical = shape.ApicalChild(i) < 0;
                            int laterals = shape.ChildCount(i) - (noApical ? 0 : 1);
                            bool lateralOnly = noApical && ShootBuilder.MustBeLateral(in nodes[i], shape.Order(i), shape.IsCrownAxis(i), laterals,
                                tree.Growth.GrowthDayIndex, config);
                            if (lateralOnly && laterals >= config.MaxChildrenPerNode - 1) continue;
                            TierGroup weight = shape.WeightGroup(i, noApical && !lateralOnly);
                            TreeGraph copy = TreeGraph.FromSnapshot(tree.ToSnapshot());
                            copy.Append(ShootBuilder.Build(copy, i, in u, config, species));
                            var after = new TreeShapeAnalysis();
                            after.Analyze(copy, config, species);
                            TierGroup built = after.GroupOf(copy.Count - 1);
                            if (weight == TierGroup.Other)
                                Assert.IsTrue(built == TierGroup.Other || built == TierGroup.Chain || built == TierGroup.Dome || !shape.IsTrunkChain(i),
                                    id + " seed " + seed + " node " + i);
                            else Assert.AreEqual(weight, built, id + " seed " + seed + " day " + day + " node " + i);
                            checkedShoots++;
                        }
                    }
                }
            }
            Assert.Greater(checkedShoots, 1000);
        }

        // ---- T3: pinch ------------------------------------------------------------------------------------------

        [Test]
        public void Pinch_FreshCutTipWithoutApical_NeverTakesApicalShoot()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = ShapeMetrics.Simulator(config);
            int pinchedShoots = 0;
            foreach (SpeciesId id in AllSpecies)
            {
                for (uint seed = 1; seed <= Seeds; seed++)
                {
                    TreeGraph tree = ShapeMetrics.Grow(sim, seed, id, 0);
                    for (int day = 1; day <= 120; day++)
                    {
                        if (day % 10 == 0) CutBranchLegs(tree, config);
                        var before = tree.ToSnapshot();
                        int nextId = tree.NextId;
                        ShapeMetrics.RunDays(sim, tree, 1);
                        ReadOnlySpan<NodeData> nodes = tree.Nodes;
                        int growthDay = tree.Growth.GrowthDayIndex;
                        for (int i = 0; i < nodes.Length; i++)
                        {
                            if (nodes[i].Id < nextId) continue;
                            int p = nodes[i].ParentIndex;
                            if (TreeShapeAnalysis.OrderOf(nodes, p) < 1) continue;
                            NodeData parent = nodes[p];
                            if (parent.LastCutDay < 0 || growthDay - parent.LastCutDay >= config.BackBudDays) continue;
                            pinchedShoots++;
                            Assert.AreNotEqual(0, nodes[i].BranchSide, id + " seed " + seed + " day " + day + ": a pinched tip took an apical shoot");
                        }
                        Assert.IsNotNull(before);
                    }
                }
            }
            Assert.Greater(pinchedShoots, 100, "pinched tips do grow laterals");
        }

        /// <summary>S4-like cut: every ord ≥ 1 axis longer than 3 nodes loses its 4th node (the «leg»).</summary>
        static void CutBranchLegs(TreeGraph tree, GrowthConfig config)
        {
            var ids = new List<int>();
            ReadOnlySpan<NodeData> nodes = tree.Nodes;
            var pos = new int[nodes.Length];
            for (int i = 1; i < nodes.Length; i++)
            {
                int p = nodes[i].ParentIndex;
                pos[i] = nodes[i].BranchSide != 0 ? 1 : pos[p] + 1;
                if (pos[i] == 4 && TreeShapeAnalysis.OrderOf(nodes, i) >= 1) ids.Add(nodes[i].Id);
            }
            foreach (int id in ids) if (tree.IndexOf(id) >= 0) TreePruning.Prune(tree, id, config);
        }

        // ---- T4: dome ---------------------------------------------------------------------------------------------

        [Test]
        public void Dome_LateralsFirst_ShoulderAndLean_30Seeds3Species()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = ShapeMetrics.Simulator(config);
            int shoulders = 0, crownNodes = 0;
            foreach (SpeciesId id in AllSpecies)
            {
                SpeciesConfig species = SpeciesConfig.CreateDefault(id);
                for (uint seed = 1; seed <= Seeds; seed++)
                {
                    TreeGraph tree = ShapeMetrics.Grow(sim, seed, id, 0);
                    for (int day = 1; day <= 120; day++)
                    {
                        ShapeMetrics.RunDays(sim, tree, 1);
                        if (day % 5 != 0) continue;
                        var shape = ShapeMetrics.Analyze(tree, config);
                        ReadOnlySpan<NodeData> nodes = tree.Nodes;
                        for (int i = 0; i < nodes.Length; i++)
                        {
                            if (shape.IsCrownAxis(i) && shape.ApicalChild(i) >= 0)
                                Assert.GreaterOrEqual(shape.ChildCount(i) - 1, config.CrownApicalNeedsLaterals, id + " seed " + seed + ": apical before 2 laterals");
                        }
                    }
                    var final = ShapeMetrics.Analyze(tree, config);
                    int sTop = ShootBuilder.TierSide(config.TrunkChainSegments - 1, seed);
                    for (int i = 0; i < tree.Count; i++)
                    {
                        NodeData n = tree.Nodes[i];
                        if (final.IsCrownShoulder(i))
                        {
                            shoulders++;
                            Assert.That(n.AttachT, Is.InRange(config.CrownShoulderSlotT - config.AttachTJitter - 1e-12,
                                Math.Min(config.AttachTMax, config.CrownShoulderSlotT + config.AttachTJitter) + 1e-12));
                            Assert.AreEqual(-sTop, n.BranchSide, "shoulder on −s_top");
                            double world = tree.WorldAngle(i) / GrowthFormulas.DegToRad;
                            double raw = Math.Abs(world);
                            Assert.That(raw, Is.InRange(Math.Min(90.0, species.CrownLateralAngleMeanDeg - species.CrownLateralAngleSpreadDeg) - 1e-9,
                                Math.Min(90.0, species.CrownLateralAngleMeanDeg + species.CrownLateralAngleSpreadDeg) + 1e-9));
                            Assert.AreEqual(-sTop, Math.Sign(world), "world sign of a side −s_top lateral");
                            Assert.IsTrue(final.InDome(i));
                        }
                        if (final.IsCrownAxis(i))
                        {
                            crownNodes++;
                            double world = tree.WorldAngle(i) / GrowthFormulas.DegToRad;
                            double lean = -sTop * species.CrownAxisLeanDeg;
                            Assert.That(world - lean, Is.InRange(-config.ApicalAngleJitterDeg - 1e-9, config.ApicalAngleJitterDeg + 1e-9), "crown axis lean −s_top·8°·σ");
                        }
                    }
                }
            }
            Assert.Greater(shoulders, 60);
            Assert.Greater(crownNodes, 60);
        }

        [Test]
        public void CrownShoulder_Build_DomeLateralLengthAttachAndSide()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = ShapeMetrics.Simulator(config);
            var u = new ShootDraws { U1 = 0.5, U2 = 0.5, U3 = 0.5, U4 = 0.5, U5 = 0.5, U6 = 0.5, U7 = 0.5, U8 = 0.5 };
            int built = 0;
            foreach (SpeciesId id in AllSpecies)
            {
                SpeciesConfig species = SpeciesConfig.CreateDefault(id);
                for (uint seed = 1; seed <= Seeds; seed++)
                {
                    TreeGraph tree = ShapeMetrics.Grow(sim, seed, id, 20);
                    for (int day = 20; day < 90; day++)
                    {
                    ShapeMetrics.RunDays(sim, tree, 1);
                    var shape = ShapeMetrics.Analyze(tree, config);
                    for (int k = 0; k < tree.Count; k++)
                    {
                        if (!shape.IsTrunkChain(k) || tree.Nodes[k].Depth != config.TrunkChainSegments - 1) continue;
                        if (!shape.ChainSlot1Taken(k) || shape.ChainSlot0Taken(k) || shape.ApicalChild(k) < 0) continue;
                        NodeData n = ShootBuilder.Build(tree, k, in u, config, species);
                        Assert.AreEqual(-ShootBuilder.TierSide(tree.Nodes[k].Depth, seed), n.BranchSide);
                        Assert.AreEqual(config.CrownShoulderSlotT, n.AttachT, Exact);
                        double expected = config.ShootBaseLength * Math.Pow(config.ShootLengthDecay, tree.Nodes[k].Depth) * config.OrderLengthFactor
                            * config.CrownLateralLengthFactor;
                        Assert.AreEqual(expected, n.TargetLength, 1e-9, id + " seed " + seed);
                        built++;
                    }
                    }
                }
            }
            Assert.Greater(built, 3);
        }

        [Test]
        public void CrownAxisLeanDeg_ScalesWithSigma_ShakanHalf_ChokkanZero()
        {
            var config = GrowthConfig.CreateDefault();
            var species = SpeciesConfig.Momiji();
            ShapeParams moyogi = ShapeParams.Resolve(config, species);
            double lean = species.CrownAxisLeanDeg;
            Assert.AreEqual(-ShootBuilder.TierSide(4, 2u) * lean, ShootBuilder.CrownAxisLeanDeg(2u, in moyogi, config), Exact);
            species.Style = new ShapeStyleProfile { TrunkSwayScale = 0.5 };
            ShapeParams shakan = ShapeParams.Resolve(config, species);
            Assert.AreEqual(-ShootBuilder.TierSide(4, 3u) * 0.5 * lean, ShootBuilder.CrownAxisLeanDeg(3u, in shakan, config), Exact);
            species.Style = new ShapeStyleProfile { TrunkSwayScale = 0.0, DomeNodeTarget = 12 };
            ShapeParams chokkan = ShapeParams.Resolve(config, species);
            Assert.AreEqual(0.0, ShootBuilder.CrownAxisLeanDeg(3u, in chokkan, config), Exact);
            Assert.AreEqual(12, chokkan.DomeNodeTarget);
            Assert.AreEqual(16, moyogi.DomeNodeTarget);
            Assert.AreEqual(12, chokkan.TierTarget(TierGroup.Dome));
            Assert.AreEqual(SpeciesConfig.Momiji().PadNodeTarget[5], chokkan.TierTarget(TierGroup.T5)); // species default (TD-075), not overridden by Chokkan
            Assert.AreEqual(-1, chokkan.TierTarget(TierGroup.Other));
        }

        [Test]
        public void StyleProfile_Validate_RejectsFractionalDomeTarget()
        {
            Assert.DoesNotThrow(() => new ShapeStyleProfile().Validate());
            Assert.DoesNotThrow(() => new ShapeStyleProfile { DomeNodeTarget = 12 }.Validate());
            Assert.Throws<ArgumentException>(() => new ShapeStyleProfile { DomeNodeTarget = 12.5 }.Validate());
            Assert.Throws<ArgumentException>(() => new ShapeStyleProfile { DomeNodeTarget = -1 }.Validate());
            Assert.Throws<ArgumentException>(() => new ShapeStyleProfile { DomeNodeTarget = double.PositiveInfinity }.Validate());
            var a = new ShapeStyleProfile { PadNodeTarget = new PadNodeTargets(-1, 12, -1, -1, -1, -1) };
            var b = a.Clone();
            Assert.AreEqual(12, b.PadNodeTarget.Over(SpeciesConfig.Momiji().PadNodeTarget)[1]);
            Assert.AreEqual(13, b.PadNodeTarget.Over(SpeciesConfig.Momiji().PadNodeTarget)[0]);
        }

        [Test]
        public void ChainLateralSlot_BySide_EqualsAttachTMidpointForNonShoulder()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = ShapeMetrics.Simulator(config);
            int n = 0;
            foreach (SpeciesId id in AllSpecies)
            {
                for (uint seed = 1; seed <= 10; seed++)
                {
                    TreeGraph tree = ShapeMetrics.Grow(sim, seed, id, 120);
                    var shape = ShapeMetrics.Analyze(tree, config);
                    for (int i = 1; i < tree.Count; i++)
                    {
                        NodeData c = tree.Nodes[i];
                        int p = c.ParentIndex;
                        if (c.BranchSide == 0 || !shape.IsTrunkChain(p) || shape.IsCrownShoulder(i)) continue;
                        double t0 = p == 0 ? config.TrunkBaseSlotT0 : config.LateralSlotT0, t1 = p == 0 ? config.TrunkBaseSlotT1 : config.LateralSlotT1;
                        int byT = c.AttachT < 0.5 * (t0 + t1) ? 0 : 1;
                        Assert.AreEqual(byT, TreeShapeAnalysis.ChainLateralSlot(c.BranchSide, tree.Nodes[p].Depth, seed), id + " seed " + seed + " node " + i);
                        n++;
                    }
                }
            }
            Assert.Greater(n, 200);
        }

        // ---- T5: RNG / determinism ---------------------------------------------------------------------------------

        [Test]
        public void Section16_NoExtraDraws_SameTreeTwice_ChunkedEqualsBatch()
        {
            var config = GrowthConfig.CreateDefault();
            foreach (SpeciesId id in AllSpecies)
            {
                for (uint seed = 1; seed <= 5; seed++)
                {
                    var a = ShapeMetrics.Grow(ShapeMetrics.Simulator(config), seed, id, 120, 1.0, 0.3);
                    var b = ShapeMetrics.Grow(ShapeMetrics.Simulator(config), seed, id, 120, 1.0, 0.3);
                    Assert.IsTrue(a.ToSnapshot().BitwiseEquals(b.ToSnapshot()), id + " seed " + seed);
                    // Chunks with save/load (snapshot round trip) every 17 days.
                    var sim = ShapeMetrics.Simulator(config);
                    var c = ShapeMetrics.Grow(sim, seed, id, 0, 1.0, 0.3);
                    for (int d = 0; d < 120; d += 17)
                    {
                        ShapeMetrics.RunDays(sim, c, Math.Min(17, 120 - d));
                        c = TreeGraph.FromSnapshot(c.ToSnapshot());
                    }
                    Assert.IsTrue(a.ToSnapshot().BitwiseEquals(c.ToSnapshot()), id + " seed " + seed + " chunked");
                }
            }
            Assert.AreEqual(18, TreeFactory.PlantingDraws);
            Assert.AreEqual(8, ShootDraws.DrawsPerShoot);
        }
    }
}
