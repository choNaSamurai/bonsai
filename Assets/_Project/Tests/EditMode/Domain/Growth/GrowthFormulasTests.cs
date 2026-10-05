using System;
using NUnit.Framework;
using PocketArbor.Domain.Growth;

namespace PocketArbor.Domain.Tests.Growth
{
    public sealed class GrowthFormulasTests
    {
        const double Eps = 1e-12;

        // BALANCE §1: depthScale / th_max at D_max = 8.
        [TestCase(0, 1.000, 24.0)]
        [TestCase(1, 0.786, 18.9)]
        [TestCase(2, 0.596, 14.3)]
        [TestCase(3, 0.429, 10.3)]
        [TestCase(4, 0.287, 6.9)]
        [TestCase(5, 0.171, 4.1)]
        [TestCase(6, 0.100, 2.4)]
        [TestCase(7, 0.100, 2.4)]
        [TestCase(8, 0.100, 2.4)]
        public void DepthScale_BalanceTable_MatchesWithinRounding(int depth, double expectedScale, double expectedThMax)
        {
            var config = GrowthConfig.CreateDefault();
            Assert.AreEqual(expectedScale, GrowthFormulas.DepthScale(depth, config), 0.0005);
            Assert.AreEqual(expectedThMax, GrowthFormulas.MaxThicknessAt(depth, config), 0.05);
        }

        [TestCase(9)]
        [TestCase(16)]
        [TestCase(255)]
        [TestCase(int.MaxValue)]
        [TestCase(-1)]
        public void DepthScale_DepthOutsideRange_IsFiniteAndClamped(int depth)
        {
            double scale = GrowthFormulas.DepthScale(depth, GrowthConfig.CreateDefault());
            Assert.IsFalse(double.IsNaN(scale) || double.IsInfinity(scale));
            Assert.GreaterOrEqual(scale, 0.1);
            Assert.LessOrEqual(scale, 1.0);
            if (depth > 8) Assert.AreEqual(0.1, scale, Eps);
        }

        [Test]
        public void DepthScale_ZeroMaxDepth_ReturnsMinimumWithoutNaN()
        {
            Assert.AreEqual(0.1, GrowthFormulas.DepthScale(3, 0, 1.8, 0.1), Eps);
        }

        [TestCase(0.0, 0.0)]
        [TestCase(0.05, 0.0)]
        [TestCase(0.10, 0.0)]
        [TestCase(0.25, 0.5)]
        [TestCase(0.40, 1.0)]
        [TestCase(0.90, 1.0)]
        [TestCase(double.NaN, 0.0)]
        [TestCase(double.PositiveInfinity, 1.0)]
        [TestCase(double.NegativeInfinity, 0.0)]
        public void MoistureFactor_Values_MatchBalance(double moisture, double expected)
        {
            Assert.AreEqual(expected, GrowthFormulas.MoistureFactor(moisture, 0.10, 0.40), 1e-9);
        }

        // TempFactorMin = 0 → exactly the TZ formula.
        [TestCase(-10.0, 0.0)]
        [TestCase(5.0, 0.0)]
        [TestCase(14.0, 0.5)]
        [TestCase(18.5, 0.75)]
        [TestCase(23.0, 1.0)]
        [TestCase(36.0, 1.0)]
        [TestCase(38.0, 1.0)]
        [TestCase(44.0, 0.5)]
        [TestCase(45.0, 1.0 - 7.0 / 12.0)]
        [TestCase(50.0, 0.2)]
        [TestCase(80.0, 0.2)]
        [TestCase(double.NaN, 0.0)]
        [TestCase(double.NegativeInfinity, 0.0)]
        [TestCase(double.PositiveInfinity, 0.2)]
        public void TemperatureFactor_FloorZero_MatchesTzFormula(double temperature, double expected)
        {
            var config = GrowthConfig.CreateDefault();
            config.TempFactorMin = 0.0;
            Assert.AreEqual(expected, GrowthFormulas.TemperatureFactor(temperature, config), 1e-9);
        }

        // Default TempFactorMin = 0.25 (owner decision 2026-10-04): T_factor = max(0.25, T_factor_TZ).
        [TestCase(-10.0, 0.25)]
        [TestCase(5.0, 0.25)]
        [TestCase(14.0, 0.5)]
        [TestCase(23.0, 1.0)]
        [TestCase(45.0, 1.0 - 7.0 / 12.0)] // TZ value 0.4167 is above the floor
        [TestCase(50.0, 0.25)]
        [TestCase(double.NaN, 0.25)]
        public void TemperatureFactor_DefaultWinterFloor_ClampsFromBelow(double temperature, double expected)
        {
            var config = GrowthConfig.CreateDefault();
            Assert.AreEqual(0.25, config.TempFactorMin);
            Assert.AreEqual(expected, GrowthFormulas.TemperatureFactor(temperature, config), 1e-9);
        }

        [Test]
        public void Thicken_FullMoisture_MatchesExponentialSaturation()
        {
            double th = 4.0, thMax = 24.0, k = 0.25, ds = 1.0;
            double expected = th + (thMax - th) * (1.0 - Math.Exp(-(k * ds / thMax)));
            Assert.AreEqual(expected, GrowthFormulas.Thicken(th, thMax, k, 1.0, ds, 1.0), Eps);
        }

        [Test]
        public void Thicken_ManyDays_ApproachesButNeverExceedsMax()
        {
            double th = 1.0;
            for (int day = 0; day < 100000; day++)
            {
                double next = GrowthFormulas.Thicken(th, 24.0, 0.75, 1.0, 1.0, 1.0);
                Assert.GreaterOrEqual(next, th);
                Assert.LessOrEqual(next, 24.0);
                th = next;
            }
            Assert.AreEqual(24.0, th, 1e-6);
        }

        [Test]
        public void Thicken_HugeDeltaT_ClampsToMaxWithoutOvershoot()
        {
            Assert.AreEqual(24.0, GrowthFormulas.Thicken(1.0, 24.0, 0.25, 1.0, 1.0, 1e12), Eps);
        }

        [Test]
        public void Thicken_AboveMax_NeverShrinks()
        {
            Assert.AreEqual(30.0, GrowthFormulas.Thicken(30.0, 24.0, 0.25, 1.0, 1.0, 1.0), Eps);
        }

        [TestCase(0.0)]
        [TestCase(-1.0)]
        [TestCase(double.NaN)]
        public void Thicken_NoMoisture_NoChange(double moisture)
        {
            Assert.AreEqual(4.0, GrowthFormulas.Thicken(4.0, 24.0, 0.25, moisture, 1.0, 1.0), Eps);
        }

        [TestCase(double.NaN, 24.0)]
        [TestCase(4.0, double.NaN)]
        [TestCase(4.0, double.PositiveInfinity)]
        [TestCase(4.0, 0.0)]
        public void Thicken_InvalidInputs_ReturnsInputThickness(double thickness, double thMax)
        {
            double result = GrowthFormulas.Thicken(thickness, thMax, 0.25, 1.0, 1.0, 1.0);
            Assert.IsTrue(result.Equals(thickness));
        }

        [Test]
        public void Elongate_GrowthFactorZero_NoChange()
        {
            Assert.AreEqual(3.0, GrowthFormulas.Elongate(3.0, 30.0, 0.5, 0.0), Eps);
        }

        [Test]
        public void Elongate_GrowthFactorOne_MatchesFormula()
        {
            double expected = 3.0 + 27.0 * (1.0 - Math.Exp(-0.5));
            Assert.AreEqual(expected, GrowthFormulas.Elongate(3.0, 30.0, 0.5, 1.0), Eps);
        }

        [Test]
        public void Elongate_NaNGrowthFactor_TreatedAsZero()
        {
            Assert.AreEqual(3.0, GrowthFormulas.Elongate(3.0, 30.0, 0.5, double.NaN), Eps);
        }

        [Test]
        public void Triangular_ExtremeDraws_StayWithinSpread()
        {
            Assert.AreEqual(30.0, GrowthFormulas.Triangular(45.0, 15.0, 0.0, 0.0), Eps);
            Assert.AreEqual(45.0, GrowthFormulas.Triangular(45.0, 15.0, 0.5, 0.5), Eps);
            Assert.Less(GrowthFormulas.Triangular(45.0, 15.0, 0.9999999, 0.9999999), 60.0);
        }

        [TestCase(0, 1.0)]
        [TestCase(4, 0.5)]
        [TestCase(7, 0.125)]
        [TestCase(8, 0.0)]
        [TestCase(12, 0.0)]
        public void CandidateWeight_Depth_IsOneMinusDepthOverMax(int depth, double expected)
        {
            Assert.AreEqual(expected, GrowthFormulas.CandidateWeight(depth, 8), Eps);
        }

        [Test]
        public void GrowthConfig_Defaults_MatchBalance()
        {
            var c = GrowthConfig.CreateDefault();
            Assert.DoesNotThrow(c.Validate);
            Assert.AreEqual(200, c.MaxNodes);
            Assert.AreEqual(8, c.MaxDepth);
            Assert.AreEqual(0.23, c.BaseGrowthProbability, "PA-102S AC15 compensation (BALANCE §1.2.10), was 0.20");
            Assert.AreEqual(1, c.ShootsPerDayMin);
            Assert.AreEqual(3, c.ShootsPerDayMax);
            Assert.AreEqual(0.25, c.ThickeningRate);
            Assert.AreEqual(24.0, c.MaxThickness);
            Assert.AreEqual(3.0, c.NebariThickeningMultiplier);
            Assert.AreEqual(0.25, c.TempFactorMin);
            Assert.AreEqual(0.40, c.MoistureOptimal);
            Assert.AreEqual(24, c.HoursPerGrowthDay);
            Assert.AreEqual(3, c.MaxChildrenPerNode);
            Assert.AreEqual(90.0, c.MaxShootWorldAngleDeg);
        }

        [Test]
        public void GrowthConfig_ShapeDefaults_MatchBalanceSection12()
        {
            var c = GrowthConfig.CreateDefault();
            // ⚑Q BALANCE §1.4.1(в): TrunkBaseSlotT0 0.70 → 0.78 (range 0.74–0.82, checked in IdealShapeTests).
            Assert.AreEqual(new[] { 0.45, 0.85, 0.95, 0.04, 0.05, 0.97 },
                new[] { c.LateralSlotT0, c.LateralSlotT1, c.TrunkBaseSlotT1, c.AttachTJitter, c.AttachTMin, c.AttachTMax });
            Assert.AreEqual(new[] { 0.70, 0.90, 0.50, 0.30 },
                new[] { c.SegmentTipTaper, c.ApicalThicknessRatio, c.LateralThicknessRatio, c.MinShootThickness });
            Assert.AreEqual(5, c.TrunkChainSegments);
            // ⚑P BALANCE §1.3.1: TrunkSwayDecay 0.85 → 0.88.
            Assert.AreEqual(new[] { 0.88, 3.0, 0.10, 0.70, 45.0 },
                new[] { c.TrunkSwayDecay, c.TrunkSwayJitterDeg, c.TrunkCurvatureJitter, c.OrderLengthFactor, c.ArcUpMinWorldAngleDeg });
            Assert.AreEqual(new[] { 2.5, 1.5, 1.0, 0.25 }, new[] { c.ApicalDominanceWeight, c.TierWeight, c.PadWeight, c.InteriorWeight });
            Assert.AreEqual(4, c.PadFullNodes);
            Assert.AreEqual(0.30, c.SaplingTwigAttachT);
            Assert.AreEqual(30.0, c.SaplingTwigWorldAngleDeg);
        }

        [TestCase(10.0, 0.0, 10.0)]
        [TestCase(10.0, 1.0, 7.0)]
        [TestCase(10.0, 0.5, 8.5)]
        [TestCase(10.0, -3.0, 10.0)]
        [TestCase(10.0, 7.0, 7.0)]
        [TestCase(10.0, double.NaN, 10.0)]
        public void ThicknessAt_AlongParent_LinearTaperToSegmentTipTaper(double parent, double t, double expected)
        {
            Assert.AreEqual(expected, GrowthFormulas.ThicknessAt(parent, t, 0.70), Eps);
        }

        [Test]
        public void ThicknessCap_ApicalAndLateral_MatchBalanceRatios()
        {
            var c = GrowthConfig.CreateDefault();
            // Apical at the tip: 0.90 × 0.70 = 0.63 of the parent base (BALANCE §1.2.2).
            Assert.AreEqual(0.63 * 10.0, GrowthFormulas.ThicknessCap(10.0, 1.0, true, c), 1e-12);
            // Lateral of #0 slot 0 at Д40 (th0 = 10.8 → th_at(0.70) = 8.53 → cap 4.27).
            Assert.AreEqual(0.5 * 10.8 * (1.0 - 0.3 * 0.7), GrowthFormulas.ThicknessCap(10.8, 0.7, false, c), 1e-12);
        }

        [TestCase(-0.1, -1.0)]
        [TestCase(0.0, 1.0)]
        [TestCase(2.0, 1.0)]
        public void SignNonZero_Zero_IsPositive(double value, double expected)
        {
            Assert.AreEqual(expected, GrowthFormulas.SignNonZero(value));
        }

        [Test]
        public void SpeciesConfig_Defaults_MatchBalanceSection3()
        {
            var k = SpeciesConfig.Kuromatsu();
            var m = SpeciesConfig.Momiji();
            var y = SpeciesConfig.Yamazakura();
            // ⚑ Kuromatsu secondary laterals 30° ± 10° (was 45° ± 15°, BALANCE §3 / §1.2.6).
            Assert.AreEqual(new[] { 0.15, 30.0, 10.0, 0.45 }, new[] { k.KBase, k.BranchAngleMeanDeg, k.BranchAngleSpreadDeg, k.CurvatureSpread });
            Assert.AreEqual(new[] { 0.28, 30.0, 10.0, 0.20 }, new[] { m.KBase, m.BranchAngleMeanDeg, m.BranchAngleSpreadDeg, m.CurvatureSpread });
            Assert.AreEqual(new[] { 0.22, 35.0, 20.0, 0.30 }, new[] { y.KBase, y.BranchAngleMeanDeg, y.BranchAngleSpreadDeg, y.CurvatureSpread });
            // BALANCE §1.2.3 / §1.2.5 / §1.2.6: primary angle, spread, sway, droop.
            // ⚑P BALANCE §1.3.1: TrunkSwayDeg 32 / 30 / 26 (was 14 / 9 / 12).
            Assert.AreEqual(new[] { 80.0, 6.0, 32.0, 0.50 }, new[] { k.PrimaryAngleMeanDeg, k.PrimaryAngleSpreadDeg, k.TrunkSwayDeg, k.BranchDroop });
            // ⚑Q BALANCE §1.4.3: Momiji primaries 62° → 66° (range 60–72°, checked in IdealShapeTests).
            Assert.AreEqual(new[] { 10.0, 0.20 }, new[] { m.PrimaryAngleSpreadDeg, m.BranchDroop });
            Assert.That(m.TrunkSwayDeg, Is.InRange(26.0, 34.0), "PA-102P range 30 ± 4° (PA-102Q tuning 26°)");
            Assert.AreEqual(new[] { 70.0, 10.0, 26.0, 0.30 }, new[] { y.PrimaryAngleMeanDeg, y.PrimaryAngleSpreadDeg, y.TrunkSwayDeg, y.BranchDroop });
            // ⚑Q BALANCE §1.4.5: Kuromatsu PadDepth 1 → 2.
            Assert.AreEqual(new[] { 2, 2, 2 }, new[] { k.PadDepth, m.PadDepth, y.PadDepth });
        }
    }
}
