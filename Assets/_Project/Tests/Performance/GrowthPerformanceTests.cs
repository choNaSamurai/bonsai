using NUnit.Framework;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Soil;
using PocketArbor.Domain.Tree;
using Unity.PerformanceTesting;

namespace PocketArbor.Performance.Tests
{
    /// <summary>DailyStep cost (PA-102, PA-102S AC17: ≤ 2× PA-102 ≈ 14 µs) and TreeShapeAnalysis. Measured, no hard threshold yet.</summary>
    public sealed class GrowthPerformanceTests
    {
        const double Wet = 0.5;
        const double Warm = 25.0;

        static GrowthSimulator CreateSimulator() =>
            new GrowthSimulator(GrowthConfig.CreateDefault(), SoilConfig.CreateDefault(), SpeciesCatalog.CreateDefault());

        static TreeGraph GrowTree(GrowthSimulator sim, int days)
        {
            var config = sim.Config;
            var tree = TreeFactory.PlantSapling(new TreeGenome(20261004u, SpeciesId.Kuromatsu), config, SpeciesConfig.Kuromatsu());
            tree.SetGrowthFrozen(false);
            TreePruning.Prune(tree, tree.Nodes[2].Id, config);
            var input = new GrowthHourInput(Wet, Warm);
            for (int h = 0; h < days * config.HoursPerGrowthDay; h++) sim.SimulateHour(tree, in input);
            return tree;
        }

        [Test, Performance]
        public void DailyStep_200Nodes_Nebari()
        {
            var sim = CreateSimulator();
            var tree = GrowTree(sim, 150);
            Assert.AreEqual(200, tree.Count);
            Measure.Method(() => sim.DailyStep(tree))
                .WarmupCount(10)
                .MeasurementCount(50)
                .IterationsPerMeasurement(10)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void DailyStep_SpawningTree_Day60()
        {
            var sim = CreateSimulator();
            var source = GrowTree(sim, 60).ToSnapshot();
            TreeGraph tree = null;
            Measure.Method(() => sim.DailyStep(tree))
                .SetUp(() => tree = TreeGraph.FromSnapshot(source))
                .WarmupCount(10)
                .MeasurementCount(50)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void SimulateHour_FullDay_200Nodes()
        {
            var sim = CreateSimulator();
            var tree = GrowTree(sim, 150);
            var input = new GrowthHourInput(Wet, Warm);
            Measure.Method(() =>
                {
                    for (int h = 0; h < 24; h++) sim.SimulateHour(tree, in input);
                })
                .WarmupCount(10)
                .MeasurementCount(50)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void TreeShapeAnalysis_200Nodes()
        {
            var sim = CreateSimulator();
            var tree = GrowTree(sim, 150);
            var shape = new TreeShapeAnalysis();
            var species = SpeciesConfig.Kuromatsu();
            Measure.Method(() => shape.Analyze(tree, sim.Config, species))
                .WarmupCount(10)
                .MeasurementCount(50)
                .IterationsPerMeasurement(10)
                .GC()
                .Run();
        }

        /// <summary>⚑T PA-102T T6: DailyStep with the tier balance — 0 B GC (spawning tree and 200 nodes) and DailyStep@200 median ≤ 30 µs.</summary>
        [Test]
        public void DailyStep_TierBalance_ZeroGcAndUnder30Microseconds()
        {
            var sim = CreateSimulator();
            var spawning = GrowTree(sim, 60);
            for (int k = 0; k < 5; k++) sim.DailyStep(TreeGraph.FromSnapshot(spawning.ToSnapshot())); // warm-up (JIT)
            var probe = TreeGraph.FromSnapshot(spawning.ToSnapshot());
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 20; k++) sim.DailyStep(probe);
            long spawningBytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, spawningBytes, "DailyStep while spawning (tier balance every shoot) allocates");

            var full = GrowTree(sim, 150);
            Assert.AreEqual(200, full.Count);
            for (int k = 0; k < 20; k++) sim.DailyStep(full);
            before = System.GC.GetAllocatedBytesForCurrentThread();
            var times = new double[200];
            var watch = new System.Diagnostics.Stopwatch();
            for (int k = 0; k < times.Length; k++)
            {
                watch.Restart();
                sim.DailyStep(full);
                watch.Stop();
                times[k] = watch.Elapsed.TotalMilliseconds * 1000.0;
            }
            long fullBytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
            System.Array.Sort(times);
            double median = times[times.Length / 2];
            TestContext.Out.WriteLine("DailyStep@200 median " + median.ToString("0.0") + " µs; spawning D60 20 steps: " + spawningBytes + " B");
            Assert.AreEqual(0L, fullBytes, "DailyStep@200 allocates");
            Assert.LessOrEqual(median, 30.0, "DailyStep@200 ≤ 30 µs (BALANCE §1.6.7)");
        }
    }
}