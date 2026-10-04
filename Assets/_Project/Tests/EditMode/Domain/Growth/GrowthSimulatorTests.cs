using System;
using NUnit.Framework;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Tree;

namespace PocketArbor.Domain.Tests.Growth
{
    public sealed class GrowthSimulatorTests
    {
        const double Wet = GrowthTestKit.WetMoisture;
        const double Warm = GrowthTestKit.OptimalTemperature;

        [Test]
        public void SimulateHour_Frozen_AccumulatesNothing()
        {
            var sim = GrowthTestKit.Simulator();
            var tree = GrowthTestKit.Sapling(1u);
            tree.SetGrowthFrozen(true);
            var before = tree.ToSnapshot();

            for (int h = 0; h < 24 * 10; h++)
            {
                Assert.AreEqual(GrowthHourResult.Frozen, sim.SimulateHour(tree, new GrowthHourInput(Wet, Warm)));
            }

            Assert.IsTrue(before.BitwiseEquals(tree.ToSnapshot()));
        }

        [Test]
        public void PlantSapling_WithoutLiftingFreeze_DoesNotGrow()
        {
            var config = GrowthConfig.CreateDefault();
            var tree = TreeFactory.PlantSapling(new TreeGenome(1u, SpeciesId.Momiji), config, SpeciesConfig.Momiji());
            GrowthTestKit.RunDays(GrowthTestKit.Simulator(config), tree, 3);
            Assert.AreEqual(0, tree.Growth.GrowthDayIndex);
            Assert.AreEqual(0, tree.Growth.GrowthHourCounter);
        }

        [Test]
        public void SimulateHour_FullDay_RunsExactlyOneDailyStepAndResetsCounters()
        {
            var sim = GrowthTestKit.Simulator();
            var tree = GrowthTestKit.PostOnboarding(1u);
            for (int h = 0; h < 23; h++)
            {
                Assert.AreEqual(GrowthHourResult.Accumulated, sim.SimulateHour(tree, new GrowthHourInput(Wet, Warm)));
            }
            Assert.AreEqual(23, tree.Growth.GrowthHourCounter);
            Assert.AreEqual(23.0, tree.Growth.SumGrowthFactor, 1e-12);
            Assert.AreEqual(23.0, tree.Growth.SumMoistureFactor, 1e-12);
            Assert.AreEqual(0, tree.Growth.GrowthDayIndex);

            Assert.AreEqual(GrowthHourResult.DayCompleted, sim.SimulateHour(tree, new GrowthHourInput(Wet, Warm)));
            Assert.AreEqual(0, tree.Growth.GrowthHourCounter);
            Assert.AreEqual(0.0, tree.Growth.SumGrowthFactor);
            Assert.AreEqual(0.0, tree.Growth.SumMoistureFactor);
            Assert.AreEqual(1, tree.Growth.GrowthDayIndex);
        }

        [Test]
        public void DailyStep_FirstDay_LeaderMaturesAndOneShootSpawns()
        {
            var sim = GrowthTestKit.Simulator();
            var tree = GrowthTestKit.PostOnboarding(1u);
            GrowthTestKit.RunDays(sim, tree, 1);
            Assert.AreEqual(NodeStatus.Mature, tree.Nodes[1].Status);
            Assert.AreEqual(3, tree.Count, "Σw = 1.875 → rate clamps to ShootsPerDayMin = 1");
            Assert.AreEqual(NodeStatus.Bud, tree.Nodes[2].Status);
            Assert.AreEqual(0.0, tree.Growth.ShootCredit, 1e-12);
        }

        [Test]
        public void DailyStep_GrowthFactorOne_SpawnsOneToThreeShootsPerDay()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = GrowthTestKit.Simulator(config);
            for (uint seed = 1; seed <= 5; seed++)
            {
                var tree = GrowthTestKit.PostOnboarding(seed);
                for (int day = 0; day < 150 && tree.Count < config.MaxNodes; day++)
                {
                    int before = tree.Count;
                    GrowthTestKit.RunDays(sim, tree, 1);
                    int born = tree.Count - before;
                    Assert.LessOrEqual(born, config.ShootsPerDayMax);
                    if (tree.Count < config.MaxNodes) Assert.GreaterOrEqual(born, config.ShootsPerDayMin, "seed " + seed + " day " + (day + 1));
                }
            }
        }

        [Test]
        public void DailyStep_AnyWeather_NeverExceedsShootsPerDayMax()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = GrowthTestKit.Simulator(config);
            var tree = GrowthTestKit.PostOnboarding(42u, SpeciesId.Yamazakura, 1.5);
            var rng = new PocketArbor.Domain.Random.Pcg32(9UL, 3UL);
            for (int day = 0; day < 200; day++)
            {
                int before = tree.Count;
                double w = rng.NextDouble() * 0.6;
                double t = -15.0 + rng.NextDouble() * 65.0;
                GrowthTestKit.RunDay(sim, tree, w, t);
                Assert.LessOrEqual(tree.Count - before, config.ShootsPerDayMax);
                Assert.GreaterOrEqual(tree.Growth.ShootCredit, 0.0);
            }
            GrowthTestKit.AssertInvariants(tree, config);
        }

        [Test]
        public void DailyStep_DryAllDay_NoShootsNoElongationNoThickening()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = GrowthTestKit.Simulator(config);
            var tree = GrowthTestKit.PostOnboarding(3u);
            GrowthTestKit.RunDays(sim, tree, 20);
            var before = tree.ToSnapshot();

            GrowthTestKit.RunDay(sim, tree, 0.05, Warm);

            Assert.AreEqual(before.NodeCount, tree.Count);
            Assert.AreEqual(before.Growth.ShootCredit, tree.Growth.ShootCredit);
            for (int i = 0; i < tree.Count; i++)
            {
                Assert.AreEqual(before.Nodes[i].Length, tree.Nodes[i].Length, "length " + i);
                Assert.AreEqual(before.Nodes[i].Thickness, tree.Nodes[i].Thickness, "thickness " + i);
                Assert.AreEqual(before.Nodes[i].LeafScale, tree.Nodes[i].LeafScale, "leaf " + i);
                Assert.AreEqual(before.Nodes[i].AgeDays + 1, tree.Nodes[i].AgeDays, "calendar age still advances");
            }
        }

        [Test]
        public void DailyStep_WinterFloor_FrostStillGrowsSlowly()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = GrowthTestKit.Simulator(config);
            var tree = GrowthTestKit.PostOnboarding(3u);
            double leaderLength = tree.Nodes[1].Length;

            GrowthTestKit.RunDay(sim, tree, Wet, -10.0);

            // G = 0.25 → rate = clamp(0.2·1·1.875, 1, 3)·0.25 = 0.25 → no shoot yet, credit carried over.
            Assert.AreEqual(2, tree.Count);
            Assert.AreEqual(0.25, tree.Growth.ShootCredit, 1e-12);
            double expected = leaderLength + (tree.Nodes[1].TargetLength - leaderLength) * (1.0 - Math.Exp(-0.5 * 0.25));
            Assert.AreEqual(expected, tree.Nodes[1].Length, 1e-9);
        }

        [Test]
        public void DailyStep_WinterFloorDisabled_FrostStopsGrowth()
        {
            var config = GrowthConfig.CreateDefault();
            config.TempFactorMin = 0.0;
            var sim = GrowthTestKit.Simulator(config);
            var tree = GrowthTestKit.PostOnboarding(3u, config: config);
            double leaderLength = tree.Nodes[1].Length;

            GrowthTestKit.RunDays(sim, tree, 5, Wet, -10.0);

            Assert.AreEqual(2, tree.Count);
            Assert.AreEqual(0.0, tree.Growth.ShootCredit);
            Assert.AreEqual(leaderLength, tree.Nodes[1].Length);
        }

        [Test]
        public void DailyStep_MaxNodesReached_NoNewShootsZeroCreditAndNebariThickening()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = GrowthTestKit.Simulator(config);
            var tree = GrowthTestKit.PostOnboarding(GrowthTestKit.FixtureSeed);
            int days = 0;
            while (tree.Count < config.MaxNodes && days < 400)
            {
                GrowthTestKit.RunDays(sim, tree, 1);
                days++;
            }
            Assert.AreEqual(config.MaxNodes, tree.Count);
            Assert.AreEqual(0.0, tree.Growth.ShootCredit);

            double thMax = GrowthFormulas.MaxThicknessAt(0, config);
            for (int day = 0; day < 30; day++)
            {
                var before = tree.ToSnapshot();
                GrowthTestKit.RunDays(sim, tree, 1);
                Assert.AreEqual(config.MaxNodes, tree.Count, "no new shoots at the limit");
                Assert.AreEqual(0.0, tree.Growth.ShootCredit);

                double expectedTrunk = GrowthFormulas.Thicken(before.Nodes[0].Thickness, thMax,
                    config.ThickeningRate * config.NebariThickeningMultiplier, 1.0, 1.0, 1.0);
                Assert.AreEqual(expectedTrunk, tree.Nodes[0].Thickness, 1e-12, "trunk thickens ×3");
                Assert.Greater(tree.Nodes[0].Thickness, before.Nodes[0].Thickness);

                int other = 1;
                double ds = GrowthFormulas.DepthScale(before.Nodes[other].Depth, config);
                double expectedOther = GrowthFormulas.Thicken(before.Nodes[other].Thickness, config.MaxThickness * ds,
                    config.ThickeningRate, 1.0, ds, 1.0);
                Assert.AreEqual(expectedOther, tree.Nodes[other].Thickness, 1e-12, "non-trunk nodes are not boosted");
            }

            GrowthTestKit.RunDays(sim, tree, 3000);
            Assert.AreEqual(config.MaxNodes, tree.Count);
            Assert.LessOrEqual(tree.Nodes[0].Thickness, config.MaxThickness);
            Assert.Greater(tree.Nodes[0].Thickness, 23.9);
            GrowthTestKit.AssertInvariants(tree, config);
        }

        [Test]
        public void DailyStep_PruneAtLimit_FreesQuotaForNewShoots()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = GrowthTestKit.Simulator(config);
            var tree = GrowthTestKit.PostOnboarding(GrowthTestKit.FixtureSeed);
            GrowthTestKit.RunDays(sim, tree, 150);
            Assert.AreEqual(config.MaxNodes, tree.Count);

            TreePruning.Prune(tree, tree.Nodes[tree.Count - 1].Id, config);
            Assert.AreEqual(config.MaxNodes - 1, tree.Count);
            GrowthTestKit.RunDays(sim, tree, 3);
            Assert.AreEqual(config.MaxNodes, tree.Count);
        }

        [Test]
        public void Grammar_ManySeedsSpeciesAndBends_RespectDepthAngleAndPotLipInvariants()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = GrowthTestKit.Simulator(config);
            double[] bends = { -0.6, 0.0, 0.6 };
            for (int s = 0; s < 3; s++)
            {
                var speciesId = (SpeciesId)s;
                var species = SpeciesConfig.CreateDefault(speciesId);
                foreach (double bend in bends)
                {
                    for (uint seed = 1; seed <= 4; seed++)
                    {
                        var tree = GrowthTestKit.PostOnboarding(seed * 7919u, speciesId, 1.5, bend);
                        GrowthTestKit.RunDays(sim, tree, 120);
                        GrowthTestKit.AssertInvariants(tree, config);
                        AssertShootAngles(tree, config, species, bend);
                        AssertTipsNeverBelowParent(tree);
                    }
                }
            }
        }

        static void AssertShootAngles(TreeGraph tree, GrowthConfig config, SpeciesConfig species, double bend)
        {
            var nodes = tree.Nodes;
            double maxWorld = config.MaxShootWorldAngleDeg * GrowthFormulas.DegToRad;
            double lo = (species.BranchAngleMeanDeg - species.BranchAngleSpreadDeg) * GrowthFormulas.DegToRad - 1e-12;
            double hi = (species.BranchAngleMeanDeg + species.BranchAngleSpreadDeg) * GrowthFormulas.DegToRad + 1e-12;
            double apicalCenter = bend * config.ApicalBendScaleDeg * GrowthFormulas.DegToRad;
            double apicalJitter = config.ApicalAngleJitterDeg * GrowthFormulas.DegToRad + 1e-12;
            var lastSide = new sbyte[nodes.Length];
            var apicalCount = new int[nodes.Length];

            for (int i = 1; i < nodes.Length; i++)
            {
                var n = nodes[i];
                Assert.AreEqual(n.BaseAngle, n.CurrentAngle, "no wire in growth");
                bool clamped = Math.Abs(Math.Abs(tree.WorldAngle(i)) - maxWorld) < 1e-9;
                if (n.BranchSide == 0)
                {
                    apicalCount[n.ParentIndex]++;
                    Assert.LessOrEqual(apicalCount[n.ParentIndex], 1, "single apical child");
                    if (!clamped) Assert.That(n.BaseAngle, Is.InRange(apicalCenter - apicalJitter, apicalCenter + apicalJitter), "apical angle " + i);
                }
                else
                {
                    if (!clamped)
                    {
                        Assert.That(Math.Abs(n.BaseAngle), Is.InRange(lo, hi), "lateral angle " + i);
                        Assert.AreEqual(n.BranchSide, Math.Sign(n.BaseAngle), "lateral side " + i);
                    }
                    if (lastSide[n.ParentIndex] != 0) Assert.AreEqual(-lastSide[n.ParentIndex], n.BranchSide, "alternating sides " + i);
                    lastSide[n.ParentIndex] = n.BranchSide;
                }
                Assert.LessOrEqual(tree.ChildCount(i), 3);
            }
        }

        static void AssertTipsNeverBelowParent(TreeGraph tree)
        {
            var nodes = tree.Nodes;
            var tipY = new double[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                double baseY = nodes[i].ParentIndex >= 0 ? tipY[nodes[i].ParentIndex] : 0.0; // pot lip at y = 0
                tipY[i] = baseY + nodes[i].Length * Math.Cos(tree.WorldAngle(i));
                Assert.GreaterOrEqual(tipY[i], baseY - 1e-9, "Pot Lip (Y-up) at " + i);
            }
        }

        [Test]
        public void SimulateHour_NaNAndInfinityInputs_NeverProduceNaN()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = GrowthTestKit.Simulator(config);
            var tree = GrowthTestKit.PostOnboarding(8u);
            double[] values = { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1e300, 1e300, 0.5, 25.0 };
            for (int day = 0; day < 60; day++)
            {
                for (int h = 0; h < 24; h++)
                {
                    sim.SimulateHour(tree, new GrowthHourInput(values[(day + h) % values.Length], values[(day * 3 + h) % values.Length]));
                }
            }
            GrowthTestKit.AssertInvariants(tree, config);
            Assert.IsFalse(double.IsNaN(tree.Growth.ShootCredit));
        }

        // ---- Determinism -------------------------------------------------------------------------

        static GrowthHourInput[] WeatherHistory(int hours, ulong seed)
        {
            var rng = new PocketArbor.Domain.Random.Pcg32(seed, 77UL);
            var history = new GrowthHourInput[hours];
            for (int h = 0; h < hours; h++)
            {
                double w = 0.05 + 0.5 * rng.NextDouble();
                double t = 5.0 + 25.0 * Math.Sin(h * Math.PI / 12.0) * 0.5 + 15.0 * rng.NextDouble();
                history[h] = new GrowthHourInput(w, t);
            }
            return history;
        }

        [Test]
        public void Simulation_SameGenomeAndHistory_BitwiseIdenticalSnapshot()
        {
            var history = WeatherHistory(24 * 90 + 13, 5UL);
            var a = GrowthTestKit.PostOnboarding(123u, SpeciesId.Momiji, 1.2, -0.3);
            var b = GrowthTestKit.PostOnboarding(123u, SpeciesId.Momiji, 1.2, -0.3);
            GrowthTestKit.Simulator().SimulateHours(a, history);
            GrowthTestKit.Simulator().SimulateHours(b, history);
            Assert.IsTrue(a.ToSnapshot().BitwiseEquals(b.ToSnapshot()));
        }

        [Test]
        public void Simulation_DifferentSeeds_DifferentSnapshots()
        {
            var history = WeatherHistory(24 * 30, 5UL);
            var sim = GrowthTestKit.Simulator();
            var a = GrowthTestKit.PostOnboarding(123u);
            var b = GrowthTestKit.PostOnboarding(124u);
            sim.SimulateHours(a, history);
            sim.SimulateHours(b, history);
            Assert.IsFalse(a.ToSnapshot().BitwiseEquals(b.ToSnapshot()));
        }

        [Test]
        public void Simulation_OnlineHourlyVsBatchCatchUpWithSaves_BitwiseIdentical()
        {
            var history = WeatherHistory(24 * 100 + 5, 11UL);
            var sim = GrowthTestKit.Simulator();

            var online = GrowthTestKit.PostOnboarding(555u, SpeciesId.Yamazakura);
            for (int h = 0; h < history.Length; h++) sim.SimulateHour(online, in history[h]);

            var batch = GrowthTestKit.PostOnboarding(555u, SpeciesId.Yamazakura);
            int days = sim.SimulateHours(batch, history);
            Assert.AreEqual(100, days);

            // Offline catch-up in uneven chunks with save/load (snapshot round-trip) between chunks.
            var resumed = GrowthTestKit.PostOnboarding(555u, SpeciesId.Yamazakura);
            int offset = 0;
            int chunk = 1;
            while (offset < history.Length)
            {
                int length = Math.Min(chunk, history.Length - offset);
                sim.SimulateHours(resumed, new ReadOnlySpan<GrowthHourInput>(history, offset, length));
                resumed = TreeGraph.FromSnapshot(resumed.ToSnapshot());
                offset += length;
                chunk = chunk * 3 % 167 + 1;
            }

            var expected = online.ToSnapshot();
            Assert.IsTrue(expected.BitwiseEquals(batch.ToSnapshot()), "batch");
            Assert.IsTrue(expected.BitwiseEquals(resumed.ToSnapshot()), "chunked with saves");
        }

        // ---- Allocations -------------------------------------------------------------------------

        [Test]
        public void DailyStep_SteadyState_AllocatesZeroBytes()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = GrowthTestKit.Simulator(config);
            var growing = GrowthTestKit.PostOnboarding(GrowthTestKit.FixtureSeed);
            GrowthTestKit.RunDays(sim, growing, 40); // ~100 nodes, still spawning
            var full = GrowthTestKit.PostOnboarding(GrowthTestKit.FixtureSeed);
            GrowthTestKit.RunDays(sim, full, 150); // Nebari
            var input = new GrowthHourInput(Wet, Warm);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int day = 0; day < 5; day++)
            {
                for (int h = 0; h < 24; h++)
                {
                    sim.SimulateHour(growing, in input);
                    sim.SimulateHour(full, in input);
                }
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated);
        }
    }
}
