using System;
using System.Collections.Generic;
using NUnit.Framework;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Tree;

namespace PocketArbor.Domain.Tests.Growth
{
    /// <summary>
    /// PA-102P model acceptance criteria (BALANCE §1.3): AC9 defaults, AC10 apical delay, AC11 tier sides / slot order / bends,
    /// AC15 LastCutDay, AC16 rules version. «30 seeds» = 1…30, G = 1, vigor 1, from the post-onboarding state.
    /// </summary>
    public sealed class ReferenceShapeTests
    {
        const int Seeds = ShapeMetrics.MonteCarloSeeds;
        static readonly SpeciesId[] AllSpecies = { SpeciesId.Kuromatsu, SpeciesId.Momiji, SpeciesId.Yamazakura };

        // ---- Defaults (BALANCE §1.3 + PA-102P tuning TD-031) ---------------------------------------------

        [Test]
        public void GrowthConfig_Section13Defaults_ProposedOrTunedWithinAllowedRanges()
        {
            var c = GrowthConfig.CreateDefault();
            Assert.DoesNotThrow(c.Validate);
            // Proposed values kept.
            Assert.That(c.TrunkSegmentDecay, Is.InRange(0.72, 0.84), "⚑Q §1.4.8 range (Proposed 0.78)");
            Assert.AreEqual(22.5, c.TrunkSegmentLength);
            Assert.AreEqual(0.08, c.TrunkSegmentJitter);
            Assert.AreEqual(0.88, c.TrunkSwayDecay);
            Assert.AreEqual(0.5, c.TrunkSwayBendGain);
            Assert.That(c.TrunkBaseSwayRatio, Is.InRange(0.2, 0.5), "PA-102P range (Proposed 0.35)");
            Assert.AreEqual(14, c.BackBudDays);
            Assert.AreEqual(2, c.BackBudRadiusEdges);
            Assert.AreEqual(0.23, c.BaseGrowthProbability, "AC17: tempo within ±15% without a change");
            // Tuned inside the PA-102P ranges (TD-031).
            Assert.That(c.TrunkTargetLength, Is.InRange(34.0, 42.0));
            Assert.That(c.CrownAxisLengthFactor, Is.InRange(0.40, 0.70));
            Assert.That(c.BranchApicalLengthMultiplier, Is.InRange(0.45, 0.80));
            Assert.That(c.TrunkApicalDelayBaseDays, Is.InRange(4, 8));
            Assert.That(c.TrunkApicalDelayStepDays, Is.InRange(2, 4));
            Assert.That(c.BackBudWeightMultiplier, Is.InRange(4.0, 10.0));
        }

        [Test]
        public void SpeciesConfig_Section13Defaults_SwayAndDomeAngles()
        {
            var k = SpeciesConfig.Kuromatsu();
            var m = SpeciesConfig.Momiji();
            var y = SpeciesConfig.Yamazakura();
            Assert.AreEqual(new[] { 32.0, 26.0 }, new[] { k.TrunkSwayDeg, y.TrunkSwayDeg });
            Assert.That(m.TrunkSwayDeg, Is.InRange(26.0, 34.0), "PA-102P range 30 ± 4°");
            Assert.AreEqual(new[] { 70.0, 8.0 }, new[] { k.CrownLateralAngleMeanDeg, k.CrownLateralAngleSpreadDeg });
            Assert.That(m.CrownLateralAngleMeanDeg, Is.InRange(50.0, 68.0), "⚑Q §1.4.4 / §1.4.8 (Proposed 60°)");
            Assert.AreEqual(10.0, m.CrownLateralAngleSpreadDeg);
            Assert.AreEqual(new[] { 60.0, 10.0 }, new[] { y.CrownLateralAngleMeanDeg, y.CrownLateralAngleSpreadDeg });
        }

        [Test]
        public void GrowthRules_Version_IsThreeForPa102Q()
        {
            Assert.AreEqual(3, GrowthRules.Version, "ADR-0003 §8: 1 = PA-102S, 2 = PA-102P, 3 = PA-102Q");
        }

        // ---- AC10: apical delay of the trunk --------------------------------------------------------------

        [Test]
        public void ApicalDelay_AllSeedsSpeciesDays0To150_NoOrd0ApicalChildBeforeDelay()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = ShapeMetrics.Simulator(config);
            foreach (SpeciesId species in AllSpecies)
            {
                for (uint seed = 1; seed <= Seeds; seed++)
                {
                    var tree = ShapeMetrics.Grow(sim, seed, species, 0);
                    var known = new HashSet<int>();
                    for (int i = 0; i < tree.Count; i++) known.Add(tree.Nodes[i].Id);
                    for (int day = 1; day <= 150; day++)
                    {
                        // Ages of the parents before the day (AgeDays++ happens before spawning, step 2 → step 6).
                        ShapeMetrics.RunDays(sim, tree, 1);
                        var nodes = tree.Nodes;
                        for (int i = 1; i < nodes.Length; i++)
                        {
                            if (!known.Add(nodes[i].Id) || nodes[i].BranchSide != 0) continue;
                            int p = nodes[i].ParentIndex;
                            if (TreeShapeAnalysis.OrderOf(nodes, p) != 0) continue;
                            int k = nodes[p].Depth;
                            int delay = config.TrunkApicalDelayBaseDays + config.TrunkApicalDelayStepDays * k;
                            Assert.GreaterOrEqual(nodes[p].AgeDays, delay, species + " seed " + seed + " day " + day + ": ord-0 k=" + k + " got an apical child too early");
                        }
                    }
                }
            }
        }

        [Test]
        public void ApicalDelay_DelayedTopWithFreeSlot_TakesLateralsOnly()
        {
            var config = GrowthConfig.CreateDefault();
            config.TrunkApicalDelayBaseDays = 1000; // the chain never gets an apical child
            var sim = ShapeMetrics.Simulator(config);
            var tree = ShapeMetrics.Grow(sim, GrowthTestKit.FixtureSeed, SpeciesId.Momiji, 40);
            var shape = ShapeMetrics.Analyze(tree, config);
            Assert.AreEqual(2, shape.TrunkChainLength, "#0 + the sapling leader only");
            Assert.AreEqual(2, CountLaterals(tree, 1), "the delayed leader filled both lateral slots");
            Assert.Greater(tree.Count, 20, "growth goes on elsewhere; the zero-weight top stays in Σw");
        }

        static int CountLaterals(TreeGraph tree, int parent)
        {
            int n = 0;
            for (int i = parent + 1; i < tree.Count; i++)
            {
                if (tree.Nodes[i].ParentIndex == parent && tree.Nodes[i].BranchSide != 0) n++;
            }
            return n;
        }

        // ---- AC11: tier sides, slot order, bends ----------------------------------------------------------

        [Test]
        public void TierSlots_NoCuts_Slot1BeforeSlot0OnSideSk()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = ShapeMetrics.Simulator(config);
            int checkedNodes = 0;
            foreach (SpeciesId species in AllSpecies)
            {
                for (uint seed = 1; seed <= Seeds; seed++)
                {
                    var tree = ShapeMetrics.Grow(sim, seed, species, 80);
                    var nodes = tree.Nodes;
                    var shape = ShapeMetrics.Analyze(tree, config);
                    for (int p = 1; p < nodes.Length; p++)
                    {
                        // #0 lost the onboarding twig (slot 0) → its order is not "no cuts"; chain nodes k ≥ 1 only.
                        if (!shape.IsTrunkChain(p)) continue;
                        int k = nodes[p].Depth;
                        int sk = ShootBuilder.TierSide(k, tree.Genome.Seed);
                        int slot1Id = -1, slot0Id = -1;
                        for (int i = p + 1; i < nodes.Length; i++)
                        {
                            if (nodes[i].ParentIndex != p || nodes[i].BranchSide == 0) continue;
                            bool slot1 = nodes[i].AttachT >= 0.65;
                            if (slot1)
                            {
                                Assert.That(nodes[i].AttachT, Is.GreaterThanOrEqualTo(0.81));
                                Assert.AreEqual(sk, nodes[i].BranchSide, species + " seed " + seed + " k=" + k + " slot 1");
                                slot1Id = nodes[i].Id;
                            }
                            else
                            {
                                Assert.AreEqual(-sk, nodes[i].BranchSide, species + " seed " + seed + " k=" + k + " slot 0");
                                slot0Id = nodes[i].Id;
                            }
                        }
                        if (slot0Id >= 0)
                        {
                            Assert.GreaterOrEqual(slot1Id, 0, "slot 0 never before slot 1");
                            Assert.Less(slot1Id, slot0Id, "slot 1 is born first");
                        }
                        checkedNodes++;
                    }
                }
            }
            Assert.Greater(checkedNodes, 300);
        }

        [Test]
        public void TrunkBends_AllSpeciesAndBends_SegmentsAndBaseLeanOnSk()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = ShapeMetrics.Simulator(config);
            double maxWorld = 0.0;
            foreach (SpeciesId species in AllSpecies)
            {
                foreach (double bend in new[] { -0.6, 0.0, 0.6 })
                {
                    for (uint seed = 1; seed <= Seeds; seed++)
                    {
                        var tree = ShapeMetrics.Grow(sim, seed, species, 60, 1.0, bend);
                        var shape = ShapeMetrics.Analyze(tree, config);
                        double lean = bend * config.TrunkBendScaleDeg * GrowthFormulas.DegToRad;
                        int found = 0;
                        for (int i = 0; i < tree.Count; i++)
                        {
                            if (!shape.IsTrunkChain(i)) continue;
                            int k = tree.Nodes[i].Depth;
                            Assert.AreEqual(ShootBuilder.TierSide(k, seed), Math.Sign(tree.WorldAngle(i) - lean),
                                species + " bend " + bend + " seed " + seed + " k=" + k);
                            maxWorld = Math.Max(maxWorld, Math.Abs(tree.WorldAngle(i)));
                            found++;
                        }
                        Assert.AreEqual(config.TrunkChainSegments, found, "k = 0…4 exist by D60");
                    }
                }
            }
            Assert.LessOrEqual(maxWorld, 90.0 * GrowthFormulas.DegToRad);
        }

        // ---- AC15: LastCutDay ----------------------------------------------------------------------------

        [Test]
        public void LastCutDay_DefaultStruct_MeansNoCut()
        {
            Assert.AreEqual(NodeData.NoCut, default(NodeData).LastCutDay, "default(NodeData) is not «cut on day 0»");
            var node = new NodeData { LastCutDay = 0 };
            Assert.AreEqual(0, node.LastCutDay, "day 0 is a valid cut day (onboarding)");
            node.LastCutDay = -7;
            Assert.AreEqual(NodeData.NoCut, node.LastCutDay, "below −1 clamps to «no cut»");
        }

        [Test]
        public void LastCutDay_PlantingAnd150DaysWithoutPruning_AllNodesMinusOne()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = ShapeMetrics.Simulator(config);
            foreach (SpeciesId species in AllSpecies)
            {
                var tree = GrowthTestKit.Sapling(GrowthTestKit.FixtureSeed, species);
                GrowthTestKit.RunDays(sim, tree, 150);
                for (int i = 0; i < tree.Count; i++) Assert.AreEqual(NodeData.NoCut, tree.Nodes[i].LastCutDay, species + " node " + i);
            }
        }

        [Test]
        public void LastCutDay_PruneOnboardingAndLater_ParentGetsGrowthDayIndex()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = ShapeMetrics.Simulator(config);
            var tree = GrowthTestKit.PostOnboarding(GrowthTestKit.FixtureSeed, SpeciesId.Momiji);
            Assert.AreEqual(0, tree.Nodes[0].LastCutDay, "onboarding cut on D0 marks the trunk (callus)");
            Assert.AreEqual(NodeData.NoCut, tree.Nodes[1].LastCutDay);

            GrowthTestKit.RunDays(sim, tree, 33);
            int victim = tree.Count - 1;
            int parentId = tree.Nodes[tree.Nodes[victim].ParentIndex].Id;
            int version = tree.Version;
            Assert.IsTrue(TreePruning.Prune(tree, tree.Nodes[victim].Id, config).Succeeded);
            Assert.AreEqual(33, tree.Nodes[tree.IndexOf(parentId)].LastCutDay);
            Assert.Greater(tree.Version, version);

            // A later cut of another child overwrites the value.
            GrowthTestKit.RunDays(sim, tree, 5);
            int parent = tree.IndexOf(parentId);
            for (int i = tree.Count - 1; i > parent; i--)
            {
                if (tree.Nodes[i].ParentIndex != parent) continue;
                TreePruning.Prune(tree, tree.Nodes[i].Id, config);
                Assert.AreEqual(38, tree.Nodes[tree.IndexOf(parentId)].LastCutDay);
                break;
            }
        }

        [Test]
        public void LastCutDay_Snapshot_RoundTripBitwiseAndValidated()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = ShapeMetrics.Simulator(config);
            var tree = GrowthTestKit.PostOnboarding(5u, SpeciesId.Yamazakura);
            GrowthTestKit.RunDays(sim, tree, 30);
            TreePruning.Prune(tree, tree.Nodes[tree.Count - 1].Id, config);
            var snapshot = tree.ToSnapshot();
            Assert.IsTrue(TreeGraph.FromSnapshot(snapshot).ToSnapshot().BitwiseEquals(snapshot));

            var nodes = snapshot.Nodes.ToArray();
            int cut = Array.FindIndex(nodes, n => n.LastCutDay == 30);
            Assert.GreaterOrEqual(cut, 0);
            nodes[cut].LastCutDay = 29;
            Assert.IsFalse(snapshot.BitwiseEquals(new TreeSnapshot(tree.Genome, tree.Growth, tree.Version, tree.NextId, nodes)), "LastCutDay is compared");

            nodes[cut].LastCutDay = 31; // in the future of GrowthDayIndex 30
            Assert.IsFalse(TreeGraph.TryFromSnapshot(new TreeSnapshot(tree.Genome, tree.Growth, tree.Version, tree.NextId, nodes), out _, out string error));
            StringAssert.Contains("LastCutDay", error);
        }

        // ---- AC15: back-budding does not boost ord 0 ------------------------------------------------------

        [Test]
        public void BackBudding_CutOnTrunkChain_DoesNotChangeSelection()
        {
            // Two identical trees; in one the trunk carries a fresh LastCutDay (via a cut of a chain lateral) — the chain node
            // itself must not be boosted: compare against a tree whose only difference is LastCutDay on #0.
            var config = GrowthConfig.CreateDefault();
            var sim = ShapeMetrics.Simulator(config);
            var a = GrowthTestKit.PostOnboarding(11u, SpeciesId.Momiji); // #0.LastCutDay = 0 (fresh for 14 days)
            var snapshot = a.ToSnapshot();
            var nodes = snapshot.Nodes.ToArray();
            nodes[0].LastCutDay = NodeData.NoCut;
            var b = TreeGraph.FromSnapshot(new TreeSnapshot(snapshot.Genome, snapshot.Growth, snapshot.Version, snapshot.NextId, nodes));
            GrowthTestKit.RunDays(sim, a, 12);
            GrowthTestKit.RunDays(sim, b, 12);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a.Nodes[i].ParentIndex, b.Nodes[i].ParentIndex, "same topology at " + i);
                Assert.AreEqual(a.Nodes[i].BranchSide, b.Nodes[i].BranchSide);
            }
        }

        [Test]
        public void BackBudding_ShapeAnalysis_FreshWindowAndRadius()
        {
            // Chain #0 → primary #1 → continuation #2 → continuation #3 (cut child of #3 on day 10).
            var nodes = new[]
            {
                new NodeData { Id = 0, ParentIndex = -1, Depth = 0, Status = NodeStatus.Mature, AttachT = 1.0 },
                new NodeData { Id = 1, ParentIndex = 0, Depth = 1, Status = NodeStatus.Mature, BranchSide = 1, AttachT = 0.95 },
                new NodeData { Id = 2, ParentIndex = 1, Depth = 2, Status = NodeStatus.Mature, AttachT = 1.0 },
                new NodeData { Id = 3, ParentIndex = 2, Depth = 3, Status = NodeStatus.Mature, AttachT = 1.0, LastCutDay = 10 },
            };
            var shape = new TreeShapeAnalysis();
            shape.Analyze(nodes, new ShapeAnalysisParams(5, 2, 23, 14, 2));
            Assert.AreEqual(new[] { 3, 2, 1, 0 }, new[] { shape.BackBudDist(0), shape.BackBudDist(1), shape.BackBudDist(2), shape.BackBudDist(3) });
            shape.Analyze(nodes, new ShapeAnalysisParams(5, 2, 24, 14, 2));
            Assert.AreEqual(TreeShapeAnalysis.NoFreshCut, shape.BackBudDist(3), "14 days later the cut is no longer fresh");
            Assert.AreEqual(TreeShapeAnalysis.NoFreshCut, shape.BackBudDist(0));
            // InPad / AxisRemain on the same axis: PadDepth 2 → #2 and #3 in the pad, #1 bare.
            Assert.AreEqual(new[] { 2, 1, 0 }, new[] { shape.AxisRemain(1), shape.AxisRemain(2), shape.AxisRemain(3) });
            Assert.AreEqual(new[] { false, false, true, true }, new[] { shape.InPad(0), shape.InPad(1), shape.InPad(2), shape.InPad(3) });
        }
    }
}
