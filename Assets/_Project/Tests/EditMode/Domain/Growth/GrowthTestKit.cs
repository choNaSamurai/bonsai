using System;
using NUnit.Framework;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Soil;
using PocketArbor.Domain.Tree;

namespace PocketArbor.Domain.Tests.Growth
{
    /// <summary>Shared fixtures for growth tests.</summary>
    internal static class GrowthTestKit
    {
        /// <summary>Fixture seed of the tempo AC (BALANCE §1.1 «Вимірювані AC»).</summary>
        public const uint FixtureSeed = 20261004u;

        // W ≥ W_opt and T where T_factor == 1 exactly → g = 1.
        public const double WetMoisture = 0.5;
        public const double OptimalTemperature = 25.0;

        public static GrowthConfig Config() => GrowthConfig.CreateDefault();

        public static GrowthSimulator Simulator(GrowthConfig config = null) =>
            new GrowthSimulator(config ?? Config(), SoilConfig.CreateDefault(), SpeciesCatalog.CreateDefault());

        /// <summary>Planted sapling with the Freeze Guard lifted (onboarding completed).</summary>
        public static TreeGraph Sapling(uint seed, SpeciesId species = SpeciesId.Kuromatsu, double vigor = 1.0, double bendBias = 0.0, GrowthConfig config = null)
        {
            var tree = TreeFactory.PlantSapling(new TreeGenome(seed, species, vigor, bendBias), config ?? Config(), SpeciesConfig.CreateDefault(species));
            tree.SetGrowthFrozen(false);
            return tree;
        }

        /// <summary>State after the onboarding cut: N = 2 (trunk + leader).</summary>
        public static TreeGraph PostOnboarding(uint seed, SpeciesId species = SpeciesId.Kuromatsu, double vigor = 1.0, double bendBias = 0.0, GrowthConfig config = null)
        {
            var cfg = config ?? Config();
            var tree = Sapling(seed, species, vigor, bendBias, cfg);
            var result = TreePruning.Prune(tree, tree.Nodes[2].Id, cfg);
            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(2, tree.Count);
            return tree;
        }

        public static void RunDay(GrowthSimulator simulator, TreeGraph tree, double moisture, double temperatureC)
        {
            var input = new GrowthHourInput(moisture, temperatureC);
            int hours = simulator.Config.HoursPerGrowthDay;
            for (int h = 0; h < hours; h++) simulator.SimulateHour(tree, in input);
        }

        public static void RunDays(GrowthSimulator simulator, TreeGraph tree, int days, double moisture = WetMoisture, double temperatureC = OptimalTemperature)
        {
            for (int d = 0; d < days; d++) RunDay(simulator, tree, moisture, temperatureC);
        }

        /// <summary>Asserts ADR-0003 / BALANCE §1.1 structural and numeric invariants.</summary>
        public static void AssertInvariants(TreeGraph tree, GrowthConfig config)
        {
            ReadOnlySpan<NodeData> nodes = tree.Nodes;
            Assert.LessOrEqual(nodes.Length, config.MaxNodes);
            Assert.AreEqual(-1, nodes[0].ParentIndex);
            Assert.AreEqual(0, nodes[0].Depth);
            double maxWorld = config.MaxShootWorldAngleDeg * GrowthFormulas.DegToRad + 1e-9;
            for (int i = 0; i < nodes.Length; i++)
            {
                NodeData n = nodes[i];
                if (i > 0)
                {
                    Assert.Less(n.ParentIndex, i, "topological order at " + i);
                    Assert.GreaterOrEqual(n.ParentIndex, 0);
                    Assert.AreEqual(nodes[n.ParentIndex].Depth + 1, n.Depth, "depth at " + i);
                }
                Assert.LessOrEqual(n.Depth, config.MaxDepth, "depth > MaxDepth at " + i);
                AssertFinite(n.Length, "Length", i);
                AssertFinite(n.TargetLength, "TargetLength", i);
                AssertFinite(n.Thickness, "Thickness", i);
                AssertFinite(n.BaseAngle, "BaseAngle", i);
                AssertFinite(n.CurrentAngle, "CurrentAngle", i);
                AssertFinite(n.Curvature, "Curvature", i);
                AssertFinite(n.LeafScale, "LeafScale", i);
                Assert.That(n.AttachT, Is.InRange(0.0, 1.0), "AttachT at " + i);
                Assert.LessOrEqual(n.Thickness, GrowthFormulas.MaxThicknessAt(n.Depth, config) + 1e-9, "thickness > th_max at " + i);
                Assert.LessOrEqual(Math.Abs(tree.WorldAngle(i)), maxWorld, "world angle > 90° at " + i);
                for (int j = i + 1; j < nodes.Length; j++) Assert.AreNotEqual(n.Id, nodes[j].Id, "duplicate id");
            }
        }

        static void AssertFinite(double value, string field, int index)
        {
            Assert.IsFalse(double.IsNaN(value) || double.IsInfinity(value), field + " not finite at " + index);
        }
    }
}
