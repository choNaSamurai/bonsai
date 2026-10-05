using System;
using System.Reflection;
using NUnit.Framework;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Random;
using PocketArbor.Domain.Tree;

namespace PocketArbor.Domain.Tests.Growth
{
    /// <summary>
    /// PA-102Q model acceptance criteria (BALANCE §1.4): AC14 formulas, AC15 dome weight, AC16 RNG, AC19 ShapeStyleProfile refactor,
    /// plus the §1.4.8 tuning ranges. «30 seeds» = 1…30, G = 1, vigor 1, from the post-onboarding state.
    /// </summary>
    public sealed class IdealShapeTests
    {
        const int Seeds = ShapeMetrics.MonteCarloSeeds;
        const double Exact = 1e-12;
        static readonly SpeciesId[] AllSpecies = { SpeciesId.Kuromatsu, SpeciesId.Momiji, SpeciesId.Yamazakura };

        // ---- Defaults within the §1.4.8 tuning ranges -------------------------------------------------------

        [Test]
        public void GrowthConfig_Section14Defaults_WithinTuningRanges()
        {
            // ⚑R PA-102R (conscious update): ranges of BALANCE §1.5.11 where §1.5 replaced §1.4.8 (slot0 #0, dome, crown axis, decay).
            var c = GrowthConfig.CreateDefault();
            Assert.DoesNotThrow(c.Validate);
            Assert.That(c.TrunkSegmentDecay, Is.InRange(0.80, 0.88));
            Assert.That(c.TrunkChainTaperRatio, Is.InRange(0.70, 0.82));
            Assert.That(c.TrunkChainDepthScaleFactor, Is.InRange(0.4, 0.7));
            Assert.That(c.TrunkBaseSlotT0, Is.InRange(0.66, 0.76));
            Assert.That(c.PrimaryThicknessRatio, Is.InRange(0.32, 0.45));
            Assert.That(c.CrownAxisLengthFactor, Is.InRange(0.50, 0.70));
            Assert.That(c.CrownLateralLengthFactor, Is.InRange(1.0, 1.5));
            Assert.That(c.DomeWeightMultiplier, Is.InRange(1.5, 3.0));
            Assert.That(c.CrownLateralSlotT0, Is.InRange(0.45, 0.70));
            Assert.AreEqual(30.0, c.TrunkLeanOffsetMaxDeg, "owner 2026-10-05: Shakan λ ≤ 30°");
            // Not tunable without GD (§1.4.8).
            Assert.AreEqual(new[] { 0.25, 24.0, 0.23 }, new[] { c.ThickeningRate, c.MaxThickness, c.BaseGrowthProbability });
            Assert.AreEqual(new[] { 200, 1, 3, 5 }, new[] { c.MaxNodes, c.ShootsPerDayMin, c.ShootsPerDayMax, c.TrunkChainSegments });
        }

        [Test]
        public void SpeciesConfig_Section14Defaults_WithinTuningRanges()
        {
            var k = SpeciesConfig.Kuromatsu();
            var m = SpeciesConfig.Momiji();
            Assert.That(m.PrimaryAngleMeanDeg, Is.InRange(60.0, 72.0));
            Assert.That(m.CrownLateralAngleMeanDeg, Is.InRange(50.0, 68.0));
            Assert.That(k.PadDepth, Is.InRange(1, 2));
            Assert.That(m.PrimaryLengthFactor, Is.InRange(0.75, 0.95));
            Assert.That(k.PrimaryLengthFactor, Is.InRange(0.70, 0.90), "⚑R §1.5.7 Kuromatsu");
            Assert.IsNull(m.Style, "default species profile = Moyogi via the plain fields");
        }

        // ---- AC14: formulas -----------------------------------------------------------------------------------

        [TestCase(0.0)]
        [TestCase(0.5)]
        [TestCase(0.999)]
        public void TrunkSegment_TargetLength_DecayAndU5Jitter(double u5)
        {
            var config = GrowthConfig.CreateDefault();
            config.TrunkSegmentDecay = 0.78; // the BALANCE §1.4.1(в) Proposed number (default is tuned within 0.72–0.84)
            var species = SpeciesConfig.Momiji();
            var tree = GrowthTestKit.PostOnboarding(GrowthTestKit.FixtureSeed, SpeciesId.Momiji, config: config);
            var draws = new ShootDraws { U1 = 0.5, U2 = 0.5, U3 = 0.5, U4 = 0.5, U5 = u5, U6 = 0.5, U7 = 0.5, U8 = 0.5 };
            NodeData k2 = ShootBuilder.Build(tree, 1, in draws, config, species, applyApicalDelay: false);
            Assert.AreEqual(0, k2.BranchSide);
            Assert.AreEqual(2, k2.Depth);
            double expected = 22.5 * Math.Pow(0.78, 2 - 1) * (1.0 + 0.08 * (2.0 * u5 - 1.0));
            Assert.AreEqual(config.TrunkSegmentLength * Math.Pow(config.TrunkSegmentDecay, 1) * (1.0 + config.TrunkSegmentJitter * (2.0 * u5 - 1.0)),
                k2.TargetLength, Exact);
            Assert.AreEqual(expected, k2.TargetLength, Exact);
        }

        [Test]
        public void CrownAxis_TargetLengthAtFactor070_Is13_15And11_18And9_50()
        {
            var config = GrowthConfig.CreateDefault();
            config.CrownAxisLengthFactor = 0.70;
            double[] expected = { 13.15, 11.18, 9.50 };
            for (int d = 5; d <= 7; d++)
            {
                double length = config.ShootBaseLength * Math.Pow(config.ShootLengthDecay, d - 1) * config.ApicalLengthMultiplier * config.CrownAxisLengthFactor;
                Assert.AreEqual(expected[d - 5], length, 0.005, "depth " + d);
            }
            // The model (jitter 0) gives exactly the formula.
            config.ShootLengthJitter = 0.0;
            var sim = ShapeMetrics.Simulator(config);
            var tree = ShapeMetrics.Grow(sim, GrowthTestKit.FixtureSeed, SpeciesId.Momiji, 150);
            var shape = ShapeMetrics.Analyze(tree, config);
            int crown = 0;
            for (int i = 0; i < tree.Count; i++)
            {
                if (!shape.IsCrownAxis(i)) continue;
                int d = tree.Nodes[i].Depth;
                Assert.AreEqual(30.0 * Math.Pow(0.85, d - 1) * 1.2 * 0.70, tree.Nodes[i].TargetLength, Exact, "crown depth " + d);
                crown++;
            }
            Assert.Greater(crown, 0);
        }

        [Test]
        public void MaxThickness_Ord0_HalfDepthScale_Ord1_TZ()
        {
            var config = GrowthConfig.CreateDefault();
            config.TrunkChainDepthScaleFactor = 0.5;
            // §1.4.1(б): k1 21.4 · k2 18.9 · k3 16.5 · k4 14.3 · c1 12.3.
            double[] ord0 = { 24.0, 21.4, 18.9, 16.5, 14.3, 12.3 };
            for (int d = 0; d < ord0.Length; d++)
            {
                double formula = 24.0 * GrowthFormulas.DepthScale(0.5 * d, config.MaxDepth, config.DepthScaleExponent, config.DepthScaleMin);
                Assert.AreEqual(formula, GrowthFormulas.MaxThicknessAt(d, true, 0.5, config), Exact);
                Assert.AreEqual(ord0[d], GrowthFormulas.MaxThicknessAt(d, true, 0.5, config), 0.1, "ord 0 depth " + d + " (BALANCE rounds c1 12.23 → 12.3)");
                Assert.AreEqual(GrowthFormulas.MaxThicknessAt(d, config), GrowthFormulas.MaxThicknessAt(d, false, 0.5, config), Exact, "ord ≥ 1 = TZ");
            }
            Assert.AreEqual(GrowthFormulas.MaxThicknessAt(3, config), GrowthFormulas.MaxThicknessAt(3, true, 1.0, config), Exact, "factor 1 = TZ");
        }

        [Test]
        public void ThicknessCap_ChildrenOfOrd0_ApicalTaperRatioLateralPrimaryRatio()
        {
            var config = GrowthConfig.CreateDefault();
            // §1.4.3: lower primary on #0 @ 0.78, th0 15.3 → th_at 11.72 → ceiling 0.38 · 11.72 = 4.45.
            Assert.AreEqual(0.38 * 15.3 * (1.0 - 0.3 * 0.78), GrowthFormulas.AxisChildThicknessCap(15.3, 0.78, false, 0.75, 0.38, config), Exact);
            Assert.AreEqual(4.45, GrowthFormulas.AxisChildThicknessCap(15.3, 0.78, false, 0.75, 0.38, config), 0.01);
            // §1.4.1(б): apical child of an ord-0 node: 0.75 × Thickness(parent) (no th_at).
            Assert.AreEqual(0.75 * 15.3, GrowthFormulas.AxisChildThicknessCap(15.3, 1.0, true, 0.75, 0.38, config), Exact);
            ShapeParams shape = ShapeParams.Resolve(config, SpeciesConfig.Momiji());
            Assert.AreEqual(GrowthFormulas.ThicknessCap(10.0, 0.45, false, config), GrowthFormulas.ThicknessCap(10.0, 0.45, false, false, in shape, config), Exact,
                "children of ord ≥ 1 parents: §1.2.2 unchanged");
        }

        [Test]
        public void Thickness_Day80Momiji_ChainFollowsTaperTableAndPrimariesThin()
        {
            // §1.4.1(б) table (D80 Momiji): #0 15.3 · k1 11.5 · k2 8.6 · k3 6.5 · k4 4.9 — median of 30 seeds within ±25%.
            var config = GrowthConfig.CreateDefault();
            var sim = ShapeMetrics.Simulator(config);
            double[] target = { 15.3, 11.5, 8.6, 6.5, 4.9 };
            var samples = new double[target.Length][];
            for (int k = 0; k < target.Length; k++) samples[k] = new double[Seeds];
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                var tree = ShapeMetrics.Grow(sim, seed, SpeciesId.Momiji, 80);
                var shape = ShapeMetrics.Analyze(tree, config);
                for (int i = 0; i < tree.Count; i++)
                {
                    if (!shape.IsTrunkChain(i)) continue;
                    samples[tree.Nodes[i].Depth][seed - 1] = tree.Nodes[i].Thickness;
                    int apical = shape.ApicalChild(i);
                    if (apical >= 0) Assert.LessOrEqual(tree.Nodes[apical].Thickness, config.TrunkChainTaperRatio * tree.Nodes[i].Thickness + 1e-9);
                }
            }
            for (int k = 0; k < target.Length; k++)
            {
                double median = ShapeMetrics.Median(samples[k], Seeds);
                TestContext.Out.WriteLine("k" + k + " D80 median thickness " + median.ToString("0.00") + " (BALANCE " + target[k] + ")");
                Assert.AreEqual(target[k], median, 0.25 * target[k], "k" + k);
            }
        }

        // ---- AC15: dome weight --------------------------------------------------------------------------------

        [Test]
        public void InDome_CrownAxisAndAllDescendants_ChainAndPrimariesNot()
        {
            // #0 → k1..k4 (chain), c1 (crown) → lateral L → its continuation; primary P on k2.
            var nodes = new NodeData[9];
            for (int d = 0; d <= 5; d++)
                nodes[d] = new NodeData { Id = d, ParentIndex = d - 1, Depth = (byte)d, Status = NodeStatus.Mature, AttachT = 1.0 };
            nodes[6] = new NodeData { Id = 6, ParentIndex = 5, Depth = 6, Status = NodeStatus.Mature, AttachT = 0.45, BranchSide = 1 };
            nodes[7] = new NodeData { Id = 7, ParentIndex = 6, Depth = 7, Status = NodeStatus.Mature, AttachT = 1.0 };
            nodes[8] = new NodeData { Id = 8, ParentIndex = 2, Depth = 3, Status = NodeStatus.Mature, AttachT = 0.85, BranchSide = -1 };
            var shape = new TreeShapeAnalysis();
            shape.Analyze(nodes, new ShapeAnalysisParams(5, 2));
            bool[] expected = { false, false, false, false, false, true, true, true, false };
            for (int i = 0; i < nodes.Length; i++) Assert.AreEqual(expected[i], shape.InDome(i), "node " + i);
        }

        [Test]
        public void DomeWeight_SameState_RateAndShootCountBitwiseIndependentOfMultiplier()
        {
            // s_i ×= DomeWeightMultiplier only redistributes the parent choice: from the same state one day with ×2 and with ×1 gives
            // the same ShootCredit (rate from Σw) and the same number of shoots (30 seeds × D1…D120, Momiji).
            var c2 = GrowthConfig.CreateDefault();
            c2.DomeWeightMultiplier = 2.0;
            var c1 = GrowthConfig.CreateDefault();
            c1.DomeWeightMultiplier = 1.0;
            var sim2 = ShapeMetrics.Simulator(c2);
            var sim1 = ShapeMetrics.Simulator(c1);
            int domeDays = 0;
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                var tree = ShapeMetrics.Grow(sim2, seed, SpeciesId.Momiji, 0);
                for (int day = 1; day <= 120; day++)
                {
                    var probe = TreeGraph.FromSnapshot(tree.ToSnapshot());
                    ShapeMetrics.RunDays(sim1, probe, 1);
                    ShapeMetrics.RunDays(sim2, tree, 1);
                    Assert.AreEqual(BitConverter.DoubleToInt64Bits(probe.Growth.ShootCredit), BitConverter.DoubleToInt64Bits(tree.Growth.ShootCredit),
                        "seed " + seed + " day " + day);
                    Assert.AreEqual(probe.Count, tree.Count, "seed " + seed + " day " + day);
                    if (ShapeMetrics.Analyze(tree, c2).InDome(tree.Count - 1)) domeDays++;
                }
            }
            Assert.Greater(domeDays, 30, "the dome subtree grows at all");
        }

        [Test]
        public void DomeWeight_Zero_NoShootInTheDomeSubtree()
        {
            var config = GrowthConfig.CreateDefault();
            config.DomeWeightMultiplier = 0.0;
            var sim = ShapeMetrics.Simulator(config);
            for (uint seed = 1; seed <= 10; seed++)
            {
                var tree = ShapeMetrics.Grow(sim, seed, SpeciesId.Momiji, 150);
                var shape = ShapeMetrics.Analyze(tree, config);
                for (int i = 0; i < tree.Count; i++)
                {
                    if (shape.InDome(i)) Assert.AreEqual(0, shape.ChildCount(i), "seed " + seed + ": dome node " + i + " got a child with weight 0");
                }
            }
        }

        [Test]
        public void DailyStep_ConfigsAndProfile_NotMutated()
        {
            var config = GrowthConfig.CreateDefault();
            var profile = new ShapeStyleProfile { TrunkSwayScale = 0.5, TrunkLeanOffsetDeg = 40.0, DomeWeightMultiplier = 2.5 };
            var sim = Simulator(config, profile);
            string before = Dump(config) + Dump(profile) + Dump(SpeciesConfig.Momiji());
            var tree = Plant(GrowthTestKit.FixtureSeed, SpeciesId.Momiji, config, profile);
            ShapeMetrics.RunDays(sim, tree, 120);
            var reference = SpeciesConfig.Momiji();
            Assert.AreEqual(before, Dump(config) + Dump(profile) + Dump(reference));
            Assert.AreEqual(40.0, profile.TrunkLeanOffsetDeg, "the clamp to 30° is applied on resolve, not written back");
        }

        static string Dump(object o)
        {
            var sb = new System.Text.StringBuilder();
            foreach (FieldInfo f in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.FieldType == typeof(ShapeStyleProfile)) continue;
                sb.Append(f.Name).Append('=').Append(Convert.ToString(f.GetValue(o), System.Globalization.CultureInfo.InvariantCulture)).Append(';');
            }
            return sb.ToString();
        }

        // ---- AC16: RNG ----------------------------------------------------------------------------------------

        sealed class CountingRandom : IRandomSource
        {
            readonly Pcg32 _inner;
            public int Calls;
            public CountingRandom(ulong seed) => _inner = new Pcg32(seed, 0UL);
            public uint NextUInt() { Calls++; return _inner.NextUInt(); }
            public uint NextUInt(uint bound) { Calls += 1000; return _inner.NextUInt(bound); }
            public double NextDouble() { Calls += 1000; return _inner.NextDouble(); }
        }

        [Test]
        public void PlantSapling_AnyStyleProfile_Exactly18Draws()
        {
            var config = GrowthConfig.CreateDefault();
            foreach (SpeciesId id in AllSpecies)
            {
                var species = SpeciesConfig.CreateDefault(id);
                species.Style = new ShapeStyleProfile { TrunkSwayScale = 0.5, TrunkLeanOffsetDeg = 25.0, TrunkBaseSwayRatio = 0.0 };
                for (uint seed = 1; seed <= 10; seed++)
                {
                    var counting = new CountingRandom(seed);
                    TreeFactory.PlantSapling(new TreeGenome(seed, id), config, species, counting);
                    Assert.AreEqual(18, counting.Calls);
                }
            }
        }

        // ---- AC19: ShapeStyleProfile refactor -----------------------------------------------------------------

        static GrowthSimulator Simulator(GrowthConfig config, ShapeStyleProfile profile)
        {
            var k = SpeciesConfig.Kuromatsu();
            var m = SpeciesConfig.Momiji();
            var y = SpeciesConfig.Yamazakura();
            k.Style = profile;
            m.Style = profile;
            y.Style = profile;
            return new GrowthSimulator(config, Soil.SoilConfig.CreateDefault(), new SpeciesCatalog(k, m, y));
        }

        static TreeGraph Plant(uint seed, SpeciesId id, GrowthConfig config, ShapeStyleProfile profile)
        {
            var species = SpeciesConfig.CreateDefault(id);
            species.Style = profile;
            var tree = TreeFactory.PlantSapling(new TreeGenome(seed, id), config, species);
            tree.SetGrowthFrozen(false);
            Assert.IsTrue(TreePruning.Prune(tree, tree.Nodes[2].Id, config).Succeeded);
            return tree;
        }

        [Test]
        public void MoyogiProfile_30SeedsAllSpeciesDays0To150_BitwiseSameAsPlainConfig()
        {
            var config = GrowthConfig.CreateDefault();
            var plain = Simulator(config, null);
            var moyogi = Simulator(config, ShapeStyleProfile.Moyogi());
            foreach (SpeciesId id in AllSpecies)
            {
                for (uint seed = 1; seed <= Seeds; seed++)
                {
                    var a = Plant(seed, id, config, null);
                    var b = Plant(seed, id, config, ShapeStyleProfile.Moyogi());
                    for (int day = 0; day <= 150; day++)
                    {
                        if (day > 0)
                        {
                            ShapeMetrics.RunDays(plain, a, 1);
                            ShapeMetrics.RunDays(moyogi, b, 1);
                        }
                        Assert.IsTrue(a.ToSnapshot().BitwiseEquals(b.ToSnapshot()), id + " seed " + seed + " day " + day);
                    }
                }
            }
        }

        static readonly string[] OverrideFields =
        {
            "TrunkSwayDeg", "TrunkSwayDecay", "TrunkBaseSwayRatio", "TrunkSegmentLength", "TrunkSegmentDecay", "CrownAxisLengthFactor",
            "TrunkChainTaperRatio", "TrunkChainDepthScaleFactor", "PrimaryThicknessRatio", "PrimaryAngleMeanDeg", "PrimaryAngleSpreadDeg",
            "PrimaryLengthFactor", "BranchDroop", "CrownLateralAngleMeanDeg", "CrownLateralAngleSpreadDeg", "CrownLateralLengthFactor",
            "DomeWeightMultiplier",
        };

        [Test]
        public void ProfileOverride_EachField_SameTreeAsTheConfigField()
        {
            // Every override of the profile is routed to the same place as the config field it replaces (value × 0.9).
            foreach (string field in OverrideFields)
            {
                FieldInfo profileField = typeof(ShapeStyleProfile).GetField(field);
                Assert.IsNotNull(profileField, field);
                foreach (SpeciesId id in new[] { SpeciesId.Momiji, SpeciesId.Kuromatsu })
                {
                    var config = GrowthConfig.CreateDefault();
                    var species = SpeciesConfig.CreateDefault(id);
                    FieldInfo target = typeof(GrowthConfig).GetField(field);
                    object owner = target != null ? (object)config : species;
                    if (target == null) target = typeof(SpeciesConfig).GetField(field);
                    Assert.IsNotNull(target, field);
                    double value = 0.9 * (double)target.GetValue(owner);

                    var profile = new ShapeStyleProfile();
                    profileField.SetValue(profile, value);
                    var viaProfileConfig = GrowthConfig.CreateDefault();
                    var viaProfile = Simulator(viaProfileConfig, profile);

                    var viaFieldConfig = GrowthConfig.CreateDefault();
                    FieldInfo configField = typeof(GrowthConfig).GetField(field);
                    var k = SpeciesConfig.Kuromatsu();
                    var m = SpeciesConfig.Momiji();
                    var y = SpeciesConfig.Yamazakura();
                    if (configField != null) configField.SetValue(viaFieldConfig, value);
                    else foreach (var s in new[] { k, m, y }) typeof(SpeciesConfig).GetField(field).SetValue(s, value);
                    var viaField = new GrowthSimulator(viaFieldConfig, Soil.SoilConfig.CreateDefault(), new SpeciesCatalog(k, m, y));

                    for (uint seed = 1; seed <= 3; seed++)
                    {
                        var a = Plant(seed, id, viaProfileConfig, profile);
                        var speciesField = SpeciesConfig.CreateDefault(id);
                        if (configField == null) typeof(SpeciesConfig).GetField(field).SetValue(speciesField, value);
                        var b = TreeFactory.PlantSapling(new TreeGenome(seed, id), viaFieldConfig, speciesField);
                        b.SetGrowthFrozen(false);
                        TreePruning.Prune(b, b.Nodes[2].Id, viaFieldConfig);
                        ShapeMetrics.RunDays(viaProfile, a, 90);
                        ShapeMetrics.RunDays(viaField, b, 90);
                        Assert.IsTrue(a.ToSnapshot().BitwiseEquals(b.ToSnapshot()), field + " " + id + " seed " + seed);
                    }
                }
            }
        }

        [Test]
        public void ProfileLean_BeyondMax_ClampedTo30AndSigmaZeroStraight()
        {
            var config = GrowthConfig.CreateDefault();
            var species = SpeciesConfig.Momiji();
            species.Style = new ShapeStyleProfile { TrunkSwayScale = 0.0, TrunkLeanOffsetDeg = 45.0 };
            ShapeParams shape = ShapeParams.Resolve(config, species);
            Assert.AreEqual(30.0, shape.LeanOffsetDeg);
            Assert.AreEqual(0.1, shape.JitterScale, "max(σ 0, jitter floor 0.1)");
            for (uint seed = 1; seed <= 4; seed++)
            {
                var genome = new TreeGenome(seed, SpeciesId.Momiji);
                int tp = TreeShapeAnalysis.TierPhase(seed);
                for (int k = 1; k < 5; k++)
                {
                    Assert.AreEqual(30.0 * tp, ShootBuilder.TrunkSegmentWorldAngleDeg(k, genome, in shape, config, 0.0), Exact);
                    Assert.AreEqual(30.0 * tp + 0.3, ShootBuilder.TrunkSegmentWorldAngleDeg(k, genome, in shape, config, 1.0), Exact, "jitter ±0.3°");
                }
                Assert.AreEqual(30.0 * tp, ShootBuilder.TrunkBaseWorldAngleDeg(genome, config, species), Exact);
            }
        }

        [Test]
        public void ProfileNaNOrInfinity_FallsBackToConfig()
        {
            var config = GrowthConfig.CreateDefault();
            var species = SpeciesConfig.Momiji();
            species.Style = new ShapeStyleProfile
            {
                TrunkSwayScale = double.NaN, TrunkLeanOffsetDeg = double.PositiveInfinity, TrunkSwayJitterMinScale = double.NaN,
                PrimaryAngleMeanDeg = double.NegativeInfinity, DomeWeightMultiplier = double.NaN,
            };
            ShapeParams shape = ShapeParams.Resolve(config, species);
            Assert.AreEqual(1.0, shape.SwayScale);
            Assert.AreEqual(0.0, shape.LeanOffsetDeg);
            Assert.AreEqual(1.0, shape.JitterScale);
            Assert.AreEqual(species.PrimaryAngleMeanDeg, shape.PrimaryAngleMeanDeg);
            Assert.AreEqual(config.DomeWeightMultiplier, shape.DomeWeightMultiplier);
        }
    }
}
