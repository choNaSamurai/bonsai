using System;
using NUnit.Framework;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Random;
using PocketArbor.Domain.Tree;

namespace PocketArbor.Domain.Tests.Growth
{
    /// <summary>
    /// PA-102R model acceptance criteria (BALANCE §1.5): AC14 formulas (pad lengths by tier, irregular S profile with the SWAY hash, chain bow,
    /// reach compensation, DomeLateralWeight, slots), AC15 Pot Lip / alternation, AC16 RNG / determinism / rules version, ADR-0010 rev.2
    /// TrunkSwayProfile4 value semantics. «30 seeds» = 1…30, G = 1, vigor 1, from the post-onboarding state.
    /// </summary>
    public sealed class IterationThreeShapeTests
    {
        const int Seeds = ShapeMetrics.MonteCarloSeeds;
        const double Exact = 1e-12;
        static readonly SpeciesId[] AllSpecies = { SpeciesId.Kuromatsu, SpeciesId.Momiji, SpeciesId.Yamazakura };

        static ShootDraws Draws(double u5 = 0.5, double u6 = 0.5, double u7 = 0.5, double u8 = 0.5) =>
            new ShootDraws { U1 = 0.5, U2 = 0.5, U3 = 0.5, U4 = 0.5, U5 = u5, U6 = u6, U7 = u7, U8 = u8 };

        // ---- Defaults (§1.5 Proposed, within §1.5.11) ----------------------------------------------------------

        [Test]
        public void Defaults_Section15_WithinTuningRanges()
        {
            // BALANCE §1.5 Proposed values, tuned by GP inside the §1.5.11 ranges (TD-047).
            var c = GrowthConfig.CreateDefault();
            Assert.That(c.PadLengthFactor, Is.InRange(0.80, 1.00));
            Assert.That(c.TrunkSwayIrregularity, Is.InRange(0.0, 0.30));
            Assert.That(c.TrunkBowCurvature, Is.InRange(0.0, 0.25));
            Assert.That(c.PrimaryReachCompensation, Is.InRange(0.0, 1.2));
            Assert.That(c.DomeLateralWeight, Is.InRange(1.0, 2.5));
            Assert.That(c.DomeWeightMultiplier, Is.InRange(1.5, 3.0));
            Assert.That(c.CrownAxisLengthFactor, Is.InRange(0.50, 0.70));
            Assert.That(c.TrunkBaseSlotT0, Is.InRange(0.66, 0.76));
            Assert.That(c.CrownLateralSlotT0, Is.InRange(0.45, 0.70));
            Assert.That(c.TrunkBaseSwayRatio, Is.InRange(0.30, 0.50));
            Assert.That(c.TrunkTargetLength, Is.InRange(34.0, 42.0));
            Assert.AreEqual(new[] { 0.5, 0.6, 1.4 }, new[] { c.PrimaryReachSinMin, c.PrimaryReachMinRatio, c.PrimaryReachMaxRatio });
            var k = SpeciesConfig.Kuromatsu();
            var m = SpeciesConfig.Momiji();
            var y = SpeciesConfig.Yamazakura();
            Assert.AreEqual(new[] { 32.0, 26.0 }, new[] { k.TrunkSwayDeg, y.TrunkSwayDeg });
            Assert.That(m.TrunkSwayDeg, Is.InRange(26.0, 34.0));
            foreach (var s in new[] { k, m, y }) Assert.That(s.PadTierDecayCompensation, Is.InRange(0.3, 1.0));
            Assert.That(k.PrimaryLengthFactor, Is.InRange(0.70, 0.90));
            Assert.That(m.PrimaryLengthFactor, Is.InRange(0.75, 0.95));
            Assert.AreEqual(new[] { 1.00, 1.25, 0.95, 0.65 }, new[] { k.TrunkSwayProfile.K1, k.TrunkSwayProfile.K2, k.TrunkSwayProfile.K3, k.TrunkSwayProfile.K4 });
            Assert.AreEqual(new[] { 1.00, 1.30, 0.90, 0.60 }, new[] { m.TrunkSwayProfile.K1, m.TrunkSwayProfile.K2, m.TrunkSwayProfile.K3, m.TrunkSwayProfile.K4 });
            // Yamazakura tuned within ±0.15 of 1.00 / 1.20 / 0.85 / 0.60 (TD-047).
            double[] yp = { 1.00, 1.20, 0.85, 0.60 };
            for (int kk = 1; kk <= 4; kk++) Assert.That(y.TrunkSwayProfile[kk], Is.InRange(yp[kk - 1] - 0.15 - 1e-12, yp[kk - 1] + 0.15 + 1e-12), "Yamazakura k" + kk);
            // §1.5.11: [k2] ≥ [k1], [k4] ≥ 0.5.
            foreach (var s in new[] { k, m, y })
            {
                Assert.GreaterOrEqual(s.TrunkSwayProfile.K2, s.TrunkSwayProfile.K1);
                Assert.GreaterOrEqual(s.TrunkSwayProfile.K4, 0.5);
            }
        }

        // ---- AC14: SWAY hash and A_k ---------------------------------------------------------------------------

        static double ReferenceHash01(uint seed, int id, uint salt)
        {
            unchecked
            {
                uint h = seed ^ (0x9E3779B9u * (uint)(id + 1)) ^ (salt * 0x85EBCA6Bu);
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (h >> 8) / 16777216.0;
            }
        }

        [Test]
        public void SwayHash_SameSeed_SameValueInMinusOneOne_FromStableHashWithSwaySalt()
        {
            Assert.AreEqual(0x59415753u, StableHash.SwaySalt, "ASCII \"SWAY\"");
            for (uint seed = 1; seed <= 200; seed++)
            {
                for (int k = 1; k <= 4; k++)
                {
                    double h = ShootBuilder.SwayHash(k, seed);
                    Assert.That(h, Is.InRange(-1.0, 1.0));
                    Assert.AreEqual(2.0 * ReferenceHash01(seed, k, StableHash.SwaySalt) - 1.0, h, 0.0, "seed " + seed + " k " + k);
                    Assert.AreEqual(h, ShootBuilder.SwayHash(k, seed), 0.0, "deterministic");
                }
            }
            // Not constant across seeds / segments.
            Assert.AreNotEqual(ShootBuilder.SwayHash(1, 1u), ShootBuilder.SwayHash(2, 1u));
            Assert.AreNotEqual(ShootBuilder.SwayHash(1, 1u), ShootBuilder.SwayHash(1, 2u));
        }

        [TestCase(SpeciesId.Momiji, 0.0)]
        [TestCase(SpeciesId.Kuromatsu, 0.6)]
        [TestCase(SpeciesId.Yamazakura, -0.6)]
        public void TrunkSwayAmplitude_ProfileTimesIrregularity_Exact(SpeciesId id, double bendBias)
        {
            var config = GrowthConfig.CreateDefault();
            var species = SpeciesConfig.CreateDefault(id);
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                var genome = new TreeGenome(seed, id, 1.0, bendBias);
                for (int k = 1; k <= 4; k++)
                {
                    double expected = species.TrunkSwayDeg * (1.0 + 0.5 * Math.Abs(genome.BendBias)) * species.TrunkSwayProfile[k]
                        * (1.0 + 0.20 * (2.0 * ReferenceHash01(seed, k, 0x59415753u) - 1.0));
                    Assert.AreEqual(expected, ShootBuilder.TrunkSwayAmplitudeDeg(k, genome, config, species), Exact, id + " seed " + seed + " k " + k);
                }
            }
        }

        [Test]
        public void TrunkSwayAmplitude_BeyondProfileOrUnset_FallsBackToDecay()
        {
            var config = GrowthConfig.CreateDefault();
            var species = SpeciesConfig.Momiji();
            config.TrunkSwayIrregularity = 0.0;
            var genome = new TreeGenome(3u, SpeciesId.Momiji);
            Assert.AreEqual(30.0 * 0.60 * 0.88, ShootBuilder.TrunkSwayAmplitudeDeg(5, genome, config, species), Exact, "k > 4: K4·decay^(k−4)");
            species.TrunkSwayProfile = TrunkSwayProfile4.Unset;
            for (int k = 1; k <= 5; k++)
                Assert.AreEqual(30.0 * Math.Pow(0.88, k - 1), ShootBuilder.TrunkSwayAmplitudeDeg(k, genome, config, species), Exact, "unset: PA-102Q rule");
        }

        [Test]
        public void TrunkSwayProfile4_StyleOverridesPerComponent_CloneDoesNotAlias()
        {
            var config = GrowthConfig.CreateDefault();
            var species = SpeciesConfig.Momiji();
            var profile = new ShapeStyleProfile { TrunkSwayProfile = new TrunkSwayProfile4(double.NaN, 1.4, double.NaN, double.NaN) };
            species.Style = profile;
            ShapeParams shape = ShapeParams.Resolve(config, species);
            Assert.AreEqual(new[] { 1.00, 1.40, 0.90, 0.60 }, new[] { shape.SwayProfileAt(1), shape.SwayProfileAt(2), shape.SwayProfileAt(3), shape.SwayProfileAt(4) });
            ShapeStyleProfile clone = profile.Clone();
            clone.TrunkSwayProfile = new TrunkSwayProfile4(2.0, 2.0, 2.0, 2.0);
            Assert.AreEqual(1.4, profile.TrunkSwayProfile.K2, "a value type: the clone never writes through to the original");
            Assert.AreEqual(1.30, SpeciesConfig.Momiji().TrunkSwayProfile.K2);
        }

        // ---- AC14: chain bow ----------------------------------------------------------------------------------

        [Test]
        public void ChainSegment_Curvature_BowTimesSigmaPlusJitter()
        {
            var config = GrowthConfig.CreateDefault();
            foreach (double sigma in new[] { 1.0, 0.5, 0.0 })
            {
                var species = SpeciesConfig.Momiji();
                species.Style = new ShapeStyleProfile { TrunkSwayScale = sigma, TrunkSwayJitterMinScale = 0.1 };
                ShapeParams shape = ShapeParams.Resolve(config, species);
                for (uint seed = 1; seed <= 6; seed++)
                {
                    var tree = TreeFactory.PlantSapling(new TreeGenome(seed, SpeciesId.Momiji), config, species);
                    int tierPhase = TreeShapeAnalysis.TierPhase(seed);
                    // Leader k1 is node #1; build k2 on it with u6 = u7 = 0.5 (tri = 0) and with u6 = u7 = 1 (tri = +spread).
                    NodeData k2 = ShootBuilder.Build(tree, 1, Draws(), config, species, applyApicalDelay: false);
                    Assert.AreEqual(2, k2.Depth);
                    double bow = tierPhase * 1.0 * 0.15 * sigma; // (−1)^2 = +1
                    Assert.AreEqual(bow, k2.Curvature, Exact, "sigma " + sigma + " seed " + seed);
                    NodeData k2j = ShootBuilder.Build(tree, 1, Draws(u6: 1.0, u7: 1.0), config, species, applyApicalDelay: false);
                    Assert.AreEqual(bow + 0.10 * Math.Max(sigma, 0.1), k2j.Curvature, Exact);
                    Assert.AreEqual(-tierPhase * 0.15 * sigma, ShootBuilder.TrunkBowCurvature(1, seed, in shape), Exact, "k1: (−1)^1");
                }
            }
        }

        [Test]
        public void ChainSegment_PositiveBow_BulgesTowardsTheSideTheSegmentLeans()
        {
            // Sign convention (BALANCE §1.5.4 а: «опуклість назовні вигину»): segment k leans to s_k = TierPhase·(−1)^k (positive world angle =
            // towards −x); a positive Curvature moves the mid-span along the left normal (−dir.y, dir.x) — for s_k = +1 that is towards −x.
            var config = GrowthConfig.CreateDefault();
            var species = SpeciesConfig.Momiji();
            for (uint seed = 1; seed <= 8; seed++)
            {
                var tree = TreeFactory.PlantSapling(new TreeGenome(seed, SpeciesId.Momiji), config, species);
                NodeData k2 = ShootBuilder.Build(tree, 1, Draws(), config, species, applyApicalDelay: false);
                double world = tree.WorldAngle(1) + k2.BaseAngle;
                double leanSide = Math.Sign(world); // s_2 (|A_2| ≫ jitter)
                Assert.AreEqual(TreeShapeAnalysis.TierPhase(seed), leanSide, "s_2 = TierPhase");
                double normalX = -Math.Cos(world); // x of (−dir.y, dir.x) with dir = (−sin θ, cos θ)
                Assert.AreEqual(-leanSide, Math.Sign(normalX * k2.Curvature), "bulge towards −x for a segment leaning to +side");
            }
        }

        // ---- AC14: pad lengths by tier ---------------------------------------------------------------------------

        [Test]
        public void PadNode_TargetLength_TierCompensatedExponentAndPadFactor_FirstNodeUnchanged()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = ShapeMetrics.Simulator(config);
            foreach (SpeciesId id in AllSpecies)
            {
                var species = SpeciesConfig.CreateDefault(id);
                double c = species.PadTierDecayCompensation;
                int checkedNodes = 0, firstNodes = 0;
                for (uint seed = 1; seed <= 5; seed++)
                {
                    var tree = ShapeMetrics.Grow(sim, seed, id, 90);
                    var shape = ShapeMetrics.Analyze(tree, config);
                    for (int p = 1; p < tree.Count; p++)
                    {
                        if (shape.Order(p) != 1 || !shape.IsPrimary(TopLateral(tree, p))) continue;
                        int primary = TopLateral(tree, p);
                        int kt = tree.Nodes[tree.Nodes[primary].ParentIndex].Depth;
                        foreach (double u5 in new[] { 0.0, 0.37, 0.999 })
                        {
                            // Continuation (apical) — skip parents that already have one (the builder would make a lateral).
                            NodeData child = ShootBuilder.Build(tree, p, Draws(u5: u5), config, species);
                            int d = tree.Nodes[p].Depth + 1;
                            double expected = config.ShootBaseLength * Math.Pow(config.ShootLengthDecay, d - 1 - c * kt) * (1.0 + 0.20 * (2.0 * u5 - 1.0))
                                * config.PadLengthFactor;
                            if (child.BranchSide == 0) expected *= config.BranchApicalLengthMultiplier * config.OrderLengthFactor * species.PrimaryLengthFactor;
                            else expected *= config.OrderLengthFactor * config.OrderLengthFactor;
                            Assert.AreEqual(expected, child.TargetLength, Exact, id + " seed " + seed + " parent " + p);
                            checkedNodes++;
                        }
                    }
                    // The first node of a primary: no pad factor, decay from its absolute depth (reach compensation aside).
                    var flat = GrowthConfig.CreateDefault();
                    flat.PrimaryReachCompensation = 0.0;
                    for (int k = 0; k < tree.Count; k++)
                    {
                        if (!shape.IsTrunkChain(k)) continue;
                        NodeData primaryNode = ShootBuilder.Build(tree, k, Draws(), flat, species);
                        if (primaryNode.BranchSide == 0) continue;
                        // ⚑T §1.6.2(б) (conscious update): slot 0 of k4 is the crown shoulder (× CrownLateralLengthFactor, no primary factor).
                        if (shape.ChainSlot1Taken(k) && ShootBuilder.IsShoulderSlot(tree.Nodes[k].Depth, flat)) continue;
                        Assert.AreEqual(flat.ShootBaseLength * Math.Pow(0.85, tree.Nodes[k].Depth) * 0.70 * species.PrimaryLengthFactor, primaryNode.TargetLength, Exact, id + " chain k" + tree.Nodes[k].Depth);
                        firstNodes++;
                    }
                }
                Assert.Greater(checkedNodes, 30, id.ToString());
                Assert.Greater(firstNodes, 5, id.ToString());
            }
        }

        [Test]
        public void PadNode_MomijiTierMultipliers_MatchBalanceTable()
        {
            // §1.5.2(а): 0.85^(−0.6·k)·0.92 = 0.92 · 1.01 · 1.12 · 1.23 · 1.36 (k0…k4).
            double[] expected = { 0.92, 1.01, 1.12, 1.23, 1.36 };
            for (int k = 0; k < 5; k++) Assert.AreEqual(expected[k], Math.Pow(0.85, -0.6 * k) * 0.92, 0.006, "k" + k);
        }

        static int TopLateral(TreeGraph tree, int i)
        {
            int top = -1;
            for (int j = i; j > 0; j = tree.Nodes[j].ParentIndex) if (tree.Nodes[j].BranchSide != 0) top = j;
            return top;
        }

        // ---- AC14: reach compensation ------------------------------------------------------------------------------

        [Test]
        public void ReachCompensation_StraightChainAndShakanLean_OffsetZeroLengthUnchanged()
        {
            var config = GrowthConfig.CreateDefault();
            foreach (double lean in new[] { 0.0, 25.0 })
            {
                var species = SpeciesConfig.Momiji();
                // σ = 0 and no jitter: every chain segment lies exactly on the lean line (λ is excluded from o).
                species.Style = new ShapeStyleProfile { TrunkSwayScale = 0.0, TrunkSwayJitterMinScale = 0.0, TrunkLeanOffsetDeg = lean };
                ShapeParams shape = ShapeParams.Resolve(config, species);
                var tree = TreeFactory.PlantSapling(new TreeGenome(5u, SpeciesId.Momiji), config, species);
                double world = 1.2;
                Assert.AreEqual(20.0, ShootBuilder.ReachCompensatedLength(tree, 1, 0.85, 1, world, 20.0, config, in shape), 1e-9, "lean " + lean);
                Assert.AreEqual(20.0, ShootBuilder.ReachCompensatedLength(tree, 0, 0.70, -1, -world, 20.0, config, in shape), 1e-9, "lean " + lean);
            }
        }

        [Test]
        public void ReachCompensation_Formula_ExactAndClamped()
        {
            var config = GrowthConfig.CreateDefault();
            var species = SpeciesConfig.Momiji();
            ShapeParams shape = ShapeParams.Resolve(config, species);
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                var tree = TreeFactory.PlantSapling(new TreeGenome(seed, SpeciesId.Momiji), config, species);
                foreach (int side in new[] { -1, 1 })
                {
                    double theta = side * 72.0 * GrowthFormulas.DegToRad;
                    double attachT = 0.85;
                    // Attachment point on k1 (#1) with TargetLength and world angles, lean = 0 (bendBias 0).
                    double w0 = tree.WorldAngle(0), w1 = tree.WorldAngle(1);
                    double x = -tree.Nodes[0].TargetLength * Math.Sin(w0) - attachT * tree.Nodes[1].TargetLength * Math.Sin(w1);
                    double o = -side * x;
                    double l = 20.0;
                    double expected = Math.Min(Math.Max(l - config.PrimaryReachCompensation * o / Math.Max(Math.Abs(Math.Sin(theta)), 0.5), 0.6 * l), 1.4 * l);
                    Assert.AreEqual(expected, ShootBuilder.ReachCompensatedLength(tree, 1, attachT, side, theta, l, config, in shape), Exact, "seed " + seed);
                }
            }
            var big = GrowthConfig.CreateDefault();
            big.PrimaryReachCompensation = 1000.0;
            ShapeParams bigShape = ShapeParams.Resolve(big, species);
            var t = TreeFactory.PlantSapling(new TreeGenome(2u, SpeciesId.Momiji), big, species);
            double a = ShootBuilder.ReachCompensatedLength(t, 1, 0.85, 1, 1.2, 20.0, big, in bigShape);
            double b = ShootBuilder.ReachCompensatedLength(t, 1, 0.85, -1, -1.2, 20.0, big, in bigShape);
            Assert.AreEqual(new[] { 12.0, 28.0 }, new[] { Math.Min(a, b), Math.Max(a, b) }, "clamp 0.6L / 1.4L");
        }

        // ---- AC14: DomeLateralWeight, slots -------------------------------------------------------------------------

        [Test]
        public void DomeLateralWeight_Zero_CrownAxisNodesWithFreeSlotNeverChosen_RateUnchanged()
        {
            var zero = GrowthConfig.CreateDefault();
            zero.DomeLateralWeight = 0.0;
            var sim0 = ShapeMetrics.Simulator(zero);
            var sim = ShapeMetrics.Simulator(GrowthConfig.CreateDefault());
            for (uint seed = 1; seed <= 10; seed++)
            {
                var tree = ShapeMetrics.Grow(sim0, seed, SpeciesId.Momiji, 150);
                var shape = ShapeMetrics.Analyze(tree, zero);
                for (int i = 0; i < tree.Count; i++)
                    if (shape.IsCrownAxis(i)) Assert.AreEqual(0, shape.ChildCount(i), "seed " + seed + ": crown node " + i);
                // Σw (rate) does not depend on the role weight: the same state, one day each way → the same ShootCredit.
                var probe = ShapeMetrics.Grow(sim, seed, SpeciesId.Momiji, 70);
                var copy = TreeGraph.FromSnapshot(probe.ToSnapshot());
                ShapeMetrics.RunDays(sim, probe, 1);
                ShapeMetrics.RunDays(sim0, copy, 1);
                Assert.AreEqual(BitConverter.DoubleToInt64Bits(probe.Growth.ShootCredit), BitConverter.DoubleToInt64Bits(copy.Growth.ShootCredit));
                Assert.AreEqual(probe.Count, copy.Count);
            }
        }

        [Test]
        public void DomeLateralWeight_Default_DomeLateralsBornEarlierThanWithoutIt()
        {
            // §1.5.6: weight 1.5·2 = 3w (was 0.25·3 = 0.75w) → crown-axis laterals appear earlier (median day of the first one, 30 seeds).
            var config = GrowthConfig.CreateDefault();
            var old = GrowthConfig.CreateDefault();
            old.DomeLateralWeight = old.InteriorWeight;
            double now = MedianFirstDomeLateralDay(config), before = MedianFirstDomeLateralDay(old);
            TestContext.Out.WriteLine("first dome lateral: median D" + now + " (InteriorWeight instead: D" + before + ")");
            Assert.Less(now, before);
        }

        static double MedianFirstDomeLateralDay(GrowthConfig config)
        {
            var sim = ShapeMetrics.Simulator(config);
            var days = new double[Seeds];
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                var tree = ShapeMetrics.Grow(sim, seed, SpeciesId.Momiji, 0);
                days[seed - 1] = 151;
                for (int day = 1; day <= 150; day++)
                {
                    ShapeMetrics.RunDays(sim, tree, 1);
                    var shape = ShapeMetrics.Analyze(tree, config);
                    bool found = false;
                    for (int i = 1; i < tree.Count && !found; i++) found = tree.Nodes[i].BranchSide != 0 && shape.IsCrownAxis(tree.Nodes[i].ParentIndex);
                    if (!found) continue;
                    days[seed - 1] = day;
                    break;
                }
            }
            return ShapeMetrics.Median(days, Seeds);
        }

        [Test]
        public void Slots_TrunkBaseSlot0AndCrownLateralSlot0_ConfiguredValueWithinJitter()
        {
            // §1.5.3 (а, г): slot 0 of #0 = TrunkBaseSlotT0 ± 0.04 (Proposed 0.70, tuned 0.72), crown-axis slot 0 = CrownLateralSlotT0 ± 0.04.
            var config = GrowthConfig.CreateDefault();
            var sim = ShapeMetrics.Simulator(config);
            int baseSlot0 = 0, crownSlot0 = 0;
            double t0 = config.TrunkBaseSlotT0, c0 = config.CrownLateralSlotT0, j = config.AttachTJitter;
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                var tree = ShapeMetrics.Grow(sim, seed, SpeciesId.Momiji, 150);
                var shape = ShapeMetrics.Analyze(tree, config);
                for (int i = 1; i < tree.Count; i++)
                {
                    NodeData n = tree.Nodes[i];
                    if (n.BranchSide == 0) continue;
                    if (n.ParentIndex == 0 && n.AttachT < 0.5 * (t0 + config.TrunkBaseSlotT1))
                    {
                        Assert.That(n.AttachT, Is.InRange(t0 - j - 1e-12, t0 + j + 1e-12));
                        baseSlot0++;
                    }
                    if (shape.IsCrownAxis(n.ParentIndex) && n.AttachT < 0.5 * (c0 + config.LateralSlotT1))
                    {
                        Assert.That(n.AttachT, Is.InRange(c0 - j - 1e-12, c0 + j + 1e-12));
                        crownSlot0++;
                    }
                }
            }
            Assert.Greater(baseSlot0, 20);
            Assert.Greater(crownSlot0, 5);
        }

        // ---- AC15: Pot Lip and alternation ----------------------------------------------------------------------------

        [Test]
        public void ChainAngles_30Seeds3Species3BendBiases_Under75DegAndAlternating()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = ShapeMetrics.Simulator(config);
            double maxAbs = 0.0;
            foreach (SpeciesId id in AllSpecies)
            {
                foreach (double bb in new[] { -0.6, 0.0, 0.6 })
                {
                    for (uint seed = 1; seed <= Seeds; seed++)
                    {
                        var tree = ShapeMetrics.Grow(sim, seed, id, 120, 1.0, bb);
                        var shape = ShapeMetrics.Analyze(tree, config);
                        double lean = tree.Genome.BendBias * config.TrunkBendScaleDeg;
                        int tierPhase = TreeShapeAnalysis.TierPhase(seed);
                        int segments = 0;
                        for (int i = 0; i < tree.Count; i++)
                        {
                            if (!shape.IsTrunkChain(i)) continue;
                            double theta = tree.WorldAngle(i) / GrowthFormulas.DegToRad;
                            maxAbs = Math.Max(maxAbs, Math.Abs(theta));
                            Assert.LessOrEqual(Math.Abs(theta), 75.0, id + " bb " + bb + " seed " + seed + " k" + tree.Nodes[i].Depth);
                            int k = tree.Nodes[i].Depth;
                            if (k < 1) continue;
                            Assert.AreEqual(tierPhase * ((k & 1) == 0 ? 1 : -1), Math.Sign(theta - lean), id + " bb " + bb + " seed " + seed + " k" + k);
                            segments++;
                        }
                        Assert.AreEqual(4, segments, id + " seed " + seed + ": k1…k4 by D120");
                    }
                }
            }
            TestContext.Out.WriteLine("max |θ| of chain segments: " + maxAbs.ToString("0.0") + "°");
        }

        // ---- AC16: RNG and determinism ------------------------------------------------------------------------------

        [Test]
        public void SwayAndReach_NoExtraDraws_SameTreeTwice()
        {
            var config = GrowthConfig.CreateDefault();
            foreach (SpeciesId id in AllSpecies)
            {
                for (uint seed = 1; seed <= 5; seed++)
                {
                    var a = ShapeMetrics.Grow(ShapeMetrics.Simulator(config), seed, id, 120, 1.0, 0.3);
                    var b = ShapeMetrics.Grow(ShapeMetrics.Simulator(config), seed, id, 120, 1.0, 0.3);
                    Assert.IsTrue(a.ToSnapshot().BitwiseEquals(b.ToSnapshot()), id + " seed " + seed);
                }
            }
        }
    }
}
