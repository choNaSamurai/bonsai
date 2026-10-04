using System;
using System.Collections.Generic;
using NUnit.Framework;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Tree;

namespace PocketArbor.Domain.Tests.Growth
{
    public sealed class TreeGraphTests
    {
        [TestCase(SpeciesId.Kuromatsu)]
        [TestCase(SpeciesId.Momiji)]
        [TestCase(SpeciesId.Yamazakura)]
        public void PlantSapling_AnySpecies_HasTrunkLeaderAndLateralTwig(SpeciesId speciesId)
        {
            var config = GrowthConfig.CreateDefault();
            var species = SpeciesConfig.CreateDefault(speciesId);
            var tree = TreeFactory.PlantSapling(new TreeGenome(7u, speciesId), config, species);
            var n = tree.Nodes;

            Assert.AreEqual(3, tree.Count);
            Assert.IsTrue(tree.IsGrowthFrozen, "planting starts under the onboarding Freeze Guard");

            Assert.AreEqual(0, n[0].Depth);
            Assert.AreEqual(-1, n[0].ParentIndex);
            Assert.AreEqual(NodeStatus.Mature, n[0].Status);
            Assert.AreEqual(config.TrunkInitialLength, n[0].Length);
            Assert.AreEqual(config.TrunkTargetLength, n[0].TargetLength);
            Assert.AreEqual(config.TrunkInitialThickness, n[0].Thickness);
            Assert.IsFalse(n[0].HasLeaves);
            Assert.LessOrEqual(Math.Abs(n[0].Curvature), species.CurvatureSpread);

            Assert.AreEqual(1, n[1].Depth);
            Assert.AreEqual(0, n[1].ParentIndex);
            Assert.AreEqual(0, n[1].BranchSide, "leader is apical");
            Assert.AreEqual(NodeStatus.Growing, n[1].Status);
            Assert.AreEqual(config.SaplingLeaderLength, n[1].Length);
            Assert.AreEqual(config.SaplingLeaderThickness, n[1].Thickness);
            Assert.AreEqual(config.SaplingLeaderAgeDays, n[1].AgeDays);
            Assert.AreEqual(1f, n[1].LeafScale);
            Assert.LessOrEqual(Math.Abs(n[1].BaseAngle), config.ApicalAngleJitterDeg * GrowthFormulas.DegToRad + 1e-12);
            double apicalTarget = config.ShootBaseLength * config.ApicalLengthMultiplier;
            Assert.That(n[1].TargetLength, Is.InRange(apicalTarget * 0.8, apicalTarget * 1.2));

            Assert.AreEqual(1, n[2].Depth);
            Assert.AreEqual(0, n[2].ParentIndex);
            Assert.AreNotEqual(0, n[2].BranchSide, "twig is lateral");
            Assert.AreEqual(NodeStatus.Growing, n[2].Status);
            Assert.AreEqual(config.SaplingTwigLength, n[2].Length);
            Assert.AreEqual(config.SaplingTwigThickness, n[2].Thickness);
            Assert.AreEqual(config.SaplingTwigAgeDays, n[2].AgeDays);
            Assert.AreEqual(1f, n[2].LeafScale);
            double extreme = (species.BranchAngleMeanDeg + species.BranchAngleSpreadDeg) * GrowthFormulas.DegToRad;
            Assert.AreEqual(n[2].BranchSide * extreme, n[2].BaseAngle, 1e-12);
            Assert.That(n[2].TargetLength, Is.InRange(config.ShootBaseLength * 0.8, config.ShootBaseLength * 1.2));

            Assert.AreEqual(1, tree.Version);
            Assert.AreEqual(0, tree.Growth.GrowthDayIndex);
            Assert.AreEqual(0, tree.Growth.GrowthHourCounter);
            Assert.AreEqual(0.0, tree.Growth.ShootCredit);
        }

        [Test]
        public void PlantSapling_BendBias_TiltsTrunkAndLeader()
        {
            var config = GrowthConfig.CreateDefault();
            var tree = TreeFactory.PlantSapling(new TreeGenome(1u, SpeciesId.Momiji, 1.0, 0.5), config, SpeciesConfig.Momiji());
            Assert.AreEqual(0.5 * config.TrunkBendScaleDeg * GrowthFormulas.DegToRad, tree.Nodes[0].BaseAngle, 1e-12);
            double center = 0.5 * config.ApicalBendScaleDeg * GrowthFormulas.DegToRad;
            Assert.AreEqual(center, tree.Nodes[1].BaseAngle, config.ApicalAngleJitterDeg * GrowthFormulas.DegToRad + 1e-12);
        }

        [Test]
        public void PlantSapling_SameGenome_BitwiseIdenticalSnapshots()
        {
            var a = GrowthTestKit.Sapling(99u).ToSnapshot();
            var b = GrowthTestKit.Sapling(99u).ToSnapshot();
            Assert.IsTrue(a.BitwiseEquals(b));
        }

        [Test]
        public void PlantSapling_DifferentSeeds_DifferentSnapshots()
        {
            Assert.IsFalse(GrowthTestKit.Sapling(1u).ToSnapshot().BitwiseEquals(GrowthTestKit.Sapling(2u).ToSnapshot()));
        }

        [Test]
        public void TreeGenome_OutOfRangeOrNaN_IsSanitized()
        {
            var g = new TreeGenome(1u, (SpeciesId)9, double.NaN, 5.0);
            Assert.AreEqual(SpeciesId.Kuromatsu, g.Species);
            Assert.AreEqual(1.0, g.Vigor);
            Assert.AreEqual(0.6, g.BendBias);
            Assert.AreEqual(0.5, new TreeGenome(1u, SpeciesId.Momiji, 0.1, double.PositiveInfinity).Vigor);
            Assert.AreEqual(0.0, new TreeGenome(1u, SpeciesId.Momiji, 1.0, double.PositiveInfinity).BendBias);
        }

        [Test]
        public void Prune_OnboardingTwig_LeavesTrunkAndLeader()
        {
            var config = GrowthConfig.CreateDefault();
            var tree = GrowthTestKit.Sapling(5u);
            int leaderId = tree.Nodes[1].Id;

            var result = TreePruning.Prune(tree, tree.Nodes[2].Id, config);

            Assert.AreEqual(PruneStatus.Pruned, result.Status);
            Assert.AreEqual(1, result.RemovedCount);
            Assert.IsFalse(result.PhoenixSpawned);
            Assert.AreEqual(2, tree.Count);
            Assert.AreEqual(leaderId, tree.Nodes[1].Id);
        }

        [Test]
        public void Prune_LeaderAfterTwig_SpawnsPhoenixBud()
        {
            var config = GrowthConfig.CreateDefault();
            var tree = GrowthTestKit.PostOnboarding(5u, SpeciesId.Kuromatsu, 1.0, -0.4);

            var result = TreePruning.Prune(tree, tree.Nodes[1].Id, config);

            Assert.AreEqual(PruneStatus.Pruned, result.Status);
            Assert.IsTrue(result.PhoenixSpawned);
            Assert.AreEqual(2, tree.Count);
            var bud = tree.Nodes[1];
            Assert.AreEqual(result.PhoenixNodeId, bud.Id);
            Assert.AreEqual(0, bud.ParentIndex);
            Assert.AreEqual(1, bud.Depth);
            Assert.AreEqual(NodeStatus.Bud, bud.Status);
            Assert.AreEqual(0, bud.AgeDays);
            Assert.AreEqual(config.PhoenixBudLength, bud.Length);
            Assert.AreEqual(config.PhoenixBudThickness, bud.Thickness);
            Assert.AreEqual(0.0, bud.Curvature);
            Assert.AreEqual(0, bud.BranchSide);
            Assert.AreEqual(config.ShootBaseLength * config.ApicalLengthMultiplier, bud.TargetLength, 1e-12);
            Assert.AreEqual(-0.4 * config.ApicalBendScaleDeg * GrowthFormulas.DegToRad, bud.BaseAngle, 1e-12);
        }

        [Test]
        public void Prune_AllBranchesOfGrownTree_PhoenixKeepsTreeAlive()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = GrowthTestKit.Simulator(config);
            var tree = GrowthTestKit.PostOnboarding(11u);
            GrowthTestKit.RunDays(sim, tree, 30);
            Assert.Greater(tree.Count, 20);

            // Cut every child of the trunk, one by one.
            bool phoenix = false;
            for (int guard = 0; guard < 10 && !phoenix; guard++)
            {
                int child = -1;
                for (int i = 1; i < tree.Count; i++)
                {
                    if (tree.Nodes[i].ParentIndex == 0) { child = i; break; }
                }
                var result = TreePruning.Prune(tree, tree.Nodes[child].Id, config);
                Assert.IsTrue(result.Succeeded);
                phoenix = result.PhoenixSpawned;
            }
            Assert.IsTrue(phoenix);

            Assert.AreEqual(2, tree.Count);
            Assert.AreEqual(NodeStatus.Bud, tree.Nodes[1].Status);

            GrowthTestKit.RunDays(sim, tree, 20);
            Assert.Greater(tree.Count, 2, "tree keeps growing after Phoenix");
            GrowthTestKit.AssertInvariants(tree, config);
        }

        [Test]
        public void Prune_Trunk_IsRejectedWithoutMutation()
        {
            var config = GrowthConfig.CreateDefault();
            var tree = GrowthTestKit.Sapling(5u);
            var before = tree.ToSnapshot();

            var result = TreePruning.Prune(tree, tree.Nodes[0].Id, config);

            Assert.AreEqual(PruneStatus.TrunkProtected, result.Status);
            Assert.AreEqual(0, result.RemovedCount);
            Assert.IsTrue(before.BitwiseEquals(tree.ToSnapshot()));
        }

        [TestCase(-1)]
        [TestCase(12345)]
        public void Prune_UnknownId_ReturnsNotFoundWithoutMutation(int id)
        {
            var tree = GrowthTestKit.Sapling(5u);
            var before = tree.ToSnapshot();
            Assert.AreEqual(PruneStatus.NotFound, TreePruning.Prune(tree, id, GrowthConfig.CreateDefault()).Status);
            Assert.IsTrue(before.BitwiseEquals(tree.ToSnapshot()));
        }

        [Test]
        public void Prune_SubtreeOfGrownTree_PreservesTopologyAndRemovesExactlyTheSubtree()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = GrowthTestKit.Simulator(config);
            for (uint seed = 1; seed <= 10; seed++)
            {
                var tree = GrowthTestKit.PostOnboarding(seed);
                GrowthTestKit.RunDays(sim, tree, 50);

                // Pick a depth-2 node with descendants if possible.
                int target = -1;
                for (int i = 1; i < tree.Count && target < 0; i++)
                {
                    if (tree.Nodes[i].Depth == 2 && tree.ChildCount(i) > 0) target = i;
                }
                if (target < 0) target = tree.Count - 1;

                var expectedSurvivors = new List<int>();
                var removedIds = new HashSet<int>();
                var nodes = tree.Nodes;
                var removedIndex = new bool[nodes.Length];
                for (int i = 0; i < nodes.Length; i++)
                {
                    removedIndex[i] = i == target || (nodes[i].ParentIndex >= 0 && removedIndex[nodes[i].ParentIndex]);
                    if (removedIndex[i]) removedIds.Add(nodes[i].Id);
                    else expectedSurvivors.Add(nodes[i].Id);
                }
                var parentIdOf = new Dictionary<int, int>();
                for (int i = 1; i < nodes.Length; i++) parentIdOf[nodes[i].Id] = nodes[nodes[i].ParentIndex].Id;

                int versionBefore = tree.Version;
                var result = TreePruning.Prune(tree, nodes[target].Id, config);

                Assert.AreEqual(removedIds.Count, result.RemovedCount);
                Assert.AreEqual(versionBefore + 1, tree.Version);
                Assert.AreEqual(expectedSurvivors.Count, tree.Count);
                for (int i = 0; i < tree.Count; i++)
                {
                    Assert.AreEqual(expectedSurvivors[i], tree.Nodes[i].Id, "relative order preserved");
                    if (i > 0) Assert.AreEqual(parentIdOf[tree.Nodes[i].Id], tree.Nodes[tree.Nodes[i].ParentIndex].Id, "parent remapped");
                }
                GrowthTestKit.AssertInvariants(tree, config);
                Assert.IsTrue(tree.ToSnapshot().Validate(out var error), error);
            }
        }

        [Test]
        public void Version_EveryMutation_Increments()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = GrowthTestKit.Simulator(config);
            var tree = GrowthTestKit.Sapling(3u);
            Assert.AreEqual(1, tree.Version, "plant");

            TreePruning.Prune(tree, tree.Nodes[2].Id, config);
            Assert.AreEqual(2, tree.Version, "prune");

            for (int day = 1; day <= 5; day++)
            {
                int before = tree.Version;
                for (int h = 0; h < config.HoursPerGrowthDay - 1; h++)
                {
                    sim.SimulateHour(tree, new GrowthHourInput(0.5, 25.0));
                }
                Assert.AreEqual(before, tree.Version, "hourly accumulation does not touch nodes");
                sim.SimulateHour(tree, new GrowthHourInput(0.5, 25.0));
                Assert.AreEqual(before + 1, tree.Version, "DailyStep");
            }

            int beforePhoenix = tree.Version;
            while (tree.Count > 1 && tree.ChildCount(0) > 0)
            {
                int child = -1;
                for (int i = 1; i < tree.Count; i++) if (tree.Nodes[i].ParentIndex == 0) { child = i; break; }
                var r = TreePruning.Prune(tree, tree.Nodes[child].Id, config);
                Assert.AreEqual(++beforePhoenix, tree.Version, "prune / phoenix");
                if (r.PhoenixSpawned) break;
            }
        }

        [Test]
        public void Snapshot_RoundTrip_IsBitwiseIdenticalAndContinuesIdentically()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = GrowthTestKit.Simulator(config);
            var original = GrowthTestKit.PostOnboarding(77u, SpeciesId.Yamazakura, 1.3, 0.2);
            GrowthTestKit.RunDays(sim, original, 40);
            for (int h = 0; h < 7; h++) sim.SimulateHour(original, new GrowthHourInput(0.3, 17.0)); // mid-day state

            var snapshot = original.ToSnapshot();
            var restored = TreeGraph.FromSnapshot(snapshot);
            Assert.IsTrue(snapshot.BitwiseEquals(restored.ToSnapshot()));

            GrowthTestKit.RunDays(sim, original, 20);
            GrowthTestKit.RunDays(sim, restored, 20);
            Assert.IsTrue(original.ToSnapshot().BitwiseEquals(restored.ToSnapshot()));
        }

        [Test]
        public void Snapshot_IsDecoupledFromSourceArray()
        {
            var tree = GrowthTestKit.Sapling(3u);
            var nodes = tree.ToSnapshot().Nodes.ToArray();
            nodes[0].Length = 999.0;
            var snapshot = new TreeSnapshot(tree.Genome, tree.Growth, tree.Version, tree.NextId, nodes);
            nodes[0].Length = 1.0;
            Assert.AreEqual(999.0, snapshot.Nodes[0].Length);
        }

        [Test]
        public void TryFromSnapshot_BrokenTopology_ReturnsFalse()
        {
            var tree = GrowthTestKit.Sapling(3u);
            var nodes = tree.ToSnapshot().Nodes.ToArray();
            nodes[1].ParentIndex = 2; // child before parent
            var broken = new TreeSnapshot(tree.Genome, tree.Growth, tree.Version, tree.NextId, nodes);

            Assert.IsFalse(TreeGraph.TryFromSnapshot(broken, out var graph, out var error));
            Assert.IsNull(graph);
            Assert.IsNotNull(error);
            Assert.Throws<ArgumentException>(() => TreeGraph.FromSnapshot(broken));
        }

        [Test]
        public void TryFromSnapshot_Empty_ReturnsFalse()
        {
            var empty = new TreeSnapshot(new TreeGenome(1u, SpeciesId.Momiji), default, 0, 0, new NodeData[0]);
            Assert.IsFalse(TreeGraph.TryFromSnapshot(empty, out _, out _));
        }
    }
}
