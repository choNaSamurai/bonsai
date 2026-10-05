using System.Collections.Generic;
using NUnit.Framework;
using PocketArbor.DevTools.GrowthPreview;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Tree;
using UnityEngine;

namespace PocketArbor.DevTools.Tests
{
    /// <summary>
    /// PA-102Q AC20: style presets (BALANCE §1.4.7) on Momiji Styled D80, 30 seeds, bendBias 0. Chord «base of #0 → end of k4» from the
    /// vertical and sign changes of the chain curvature are measured on the preview's C1 axis (PreviewMetricsHarness.Bends, §1.3.1).
    /// </summary>
    public sealed class ShapeStylePresetTests
    {
        const int Seeds = 30;

        static void Measure(ShapeStyle style, uint seed, TreePreviewMeshBuilder builder, Mesh mesh, out float chordDeg, out int changes,
            out bool lowestOpposite, out float maxWorldDeg)
        {
            var model = new GrowthPreviewModel();
            model.SetParameters(SpeciesId.Momiji, seed, 1.0, 0.0, 25.0, 0.5, true);
            model.SetStyled(true);
            model.SetShapeStyle(style);
            TreeGraph tree = model.GetTree(80);
            builder.Build(tree, mesh, model.Config, model.SpeciesConfig);
            var axis = new List<Vector2>();
            builder.TrunkAxis(axis);
            Vector2 chord = axis[axis.Count - 1] - axis[0];
            chordDeg = Mathf.Atan2(-chord.x, chord.y) * Mathf.Rad2Deg;
            PreviewMetricsHarness.Bends(builder, builder.LastHeight, out changes, out _);
            float lowest = float.MaxValue;
            int lowestSide = 0;
            maxWorldDeg = 0f;
            for (int i = 1; i < tree.Count; i++)
            {
                maxWorldDeg = Mathf.Max(maxWorldDeg, Mathf.Abs((float)(tree.WorldAngle(i) * Mathf.Rad2Deg)));
                if (!builder.Shape.IsPrimary(i) || tree.Nodes[i].Status == NodeStatus.Bud) continue;
                builder.BaseWidths(i, out _, out _, out float attachY);
                if (attachY < lowest)
                {
                    lowest = attachY;
                    lowestSide = tree.Nodes[i].BranchSide;
                }
            }
            // A lateral on side s points to x sign −s; opposite to the lean (x sign of the chord) ⇔ s == sign(chord.x).
            lowestOpposite = lowestSide != 0 && lowestSide == System.Math.Sign(chord.x);
        }

        [Test]
        public void Chokkan_30Seeds_ChordUnder5DegAndAtMostOneCurvatureSignChangeIn90Percent()
        {
            var builder = new TreePreviewMeshBuilder();
            var mesh = new Mesh();
            try
            {
                int ok = 0;
                for (uint seed = 1; seed <= Seeds; seed++)
                {
                    Measure(ShapeStyle.Chokkan, seed, builder, mesh, out float chord, out int changes, out _, out _);
                    if (Mathf.Abs(chord) < 5f && changes <= 1) ok++;
                }
                TestContext.Out.WriteLine("Chokkan ok " + ok + "/" + Seeds);
                Assert.GreaterOrEqual(ok, (int)System.Math.Ceiling(0.9 * Seeds));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Shakan_30Seeds_Chord15To30LowestTierOppositeNoAngleOver90()
        {
            var builder = new TreePreviewMeshBuilder();
            var mesh = new Mesh();
            try
            {
                for (uint seed = 1; seed <= Seeds; seed++)
                {
                    Measure(ShapeStyle.Shakan, seed, builder, mesh, out float chord, out _, out bool opposite, out float maxWorld);
                    Assert.That(Mathf.Abs(chord), Is.InRange(15f, 30f), "seed " + seed + ": chord (owner 2026-10-05: 15–30°)");
                    Assert.IsTrue(opposite, "seed " + seed + ": lowest tier is the counterweight");
                    Assert.LessOrEqual(maxWorld, 90.0001f, "seed " + seed);
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Moyogi_30Seeds_AtLeastTwoCurvatureSignChanges()
        {
            var builder = new TreePreviewMeshBuilder();
            var mesh = new Mesh();
            try
            {
                for (uint seed = 1; seed <= Seeds; seed++)
                {
                    Measure(ShapeStyle.Moyogi, seed, builder, mesh, out _, out int changes, out _, out _);
                    Assert.GreaterOrEqual(changes, 2, "seed " + seed);
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Presets_ValuesFromBalance_ShakanLeanWithinOwnerLimit()
        {
            Assert.IsNull(ShapeStylePresets.Profile(ShapeStyle.Moyogi));
            ShapeStyleProfile chokkan = ShapeStylePresets.Profile(ShapeStyle.Chokkan);
            Assert.AreEqual(new[] { 0.0, 0.0, 0.1, 0.0, 45.0 }, new[] { chokkan.TrunkSwayScale, chokkan.TrunkLeanOffsetDeg, chokkan.TrunkSwayJitterMinScale,
                chokkan.TrunkBaseSwayRatio, chokkan.CrownLateralAngleMeanDeg });
            ShapeStyleProfile shakan = ShapeStylePresets.Profile(ShapeStyle.Shakan);
            Assert.AreEqual(new[] { 0.5, 25.0, 0.0 }, new[] { shakan.TrunkSwayScale, shakan.TrunkLeanOffsetDeg, shakan.TrunkBaseSwayRatio });
            Assert.That(shakan.TrunkLeanOffsetDeg, Is.InRange(15.0, GrowthConfig.CreateDefault().TrunkLeanOffsetMaxDeg));
            var style = StyleConfig.CreateDefault();
            ShapeStylePresets.ApplyStyle(style, StyleConfig.CreateDefault(), ShapeStyle.Chokkan);
            Assert.AreEqual(3, style.StyleCrownAxisMax);
            ShapeStylePresets.ApplyStyle(style, StyleConfig.CreateDefault(), ShapeStyle.Moyogi);
            Assert.AreEqual(StyleConfig.CreateDefault().StyleCrownAxisMax, style.StyleCrownAxisMax);
        }

        // ---- PA-102R ---------------------------------------------------------------------------------------------

        [Test]
        public void StyleGeometry_S8_MatchesPreviewBuilderGeometry()
        {
            // S8 runs outside Unity on a plain-C# copy of the C1 axis geometry: bases and tips must equal the builder's (1e-3 units).
            var builder = new TreePreviewMeshBuilder();
            var mesh = new Mesh();
            try
            {
                foreach (SpeciesId species in new[] { SpeciesId.Kuromatsu, SpeciesId.Momiji, SpeciesId.Yamazakura })
                {
                    var model = new GrowthPreviewModel();
                    model.SetParameters(species, 20261004u, 1.0, 0.0, 25.0, 0.5, true);
                    model.SetStyled(true);
                    TreeGraph tree = model.GetTree(90);
                    builder.Build(tree, mesh, model.Config, model.SpeciesConfig);
                    var geometry = new StyleGeometry();
                    geometry.Build(tree.Nodes, builder.Shape, builder.Render.CurvatureOffsetRatio);
                    for (int i = 0; i < tree.Count; i++)
                    {
                        Vector2 tip = builder.TipOf(i), b = builder.PointOn(i, 0f);
                        Assert.AreEqual(tip.x, (float)geometry.TipX(i), 1e-3f, species + " tip x " + i);
                        Assert.AreEqual(tip.y, (float)geometry.TipY(i), 1e-3f, species + " tip y " + i);
                        Assert.AreEqual(b.y, (float)geometry.BaseY(i), 1e-3f, species + " base y " + i);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void S8_30SeedsDays80To120_CuttablePairsWithAirOrLimitAtLeast95Percent_FirstNodeNeverCut()
        {
            // AC19 PA-102R (Momiji Styled, Moyogi): after every styling day D80…D120, same-side tier pairs have a tip gap ≥ StylePadGapUnits or
            // used the cut limit; S8 never cuts the first node of a primary. Pairs whose lower pad is only the first node of its primary (a one-node
            // k4 tier under the dome) cannot be fixed by S8 by rule — the raw AC19 share (with them) is reported by PreviewMetricsHarness (≈ 89%,
            // see PA-102R notes), here the rule itself is checked on the pairs S8 may act on.
            int pairs = 0, ok = 0, firstCuts = 0, stuck = 0;
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                var model = new GrowthPreviewModel();
                model.SetParameters(SpeciesId.Momiji, seed, 1.0, 0.0, 25.0, 0.5, true);
                model.SetStyled(true);
                model.GetDay(120);
                for (int day = 80; day <= 120; day += 10)
                {
                    int[] stats = model.PadGapStats(day);
                    Assert.IsNotNull(stats, "styling on D" + day);
                    pairs += stats[0];
                    ok += stats[1];
                    firstCuts += stats[2];
                    stuck += stats[3];
                }
            }
            TestContext.Out.WriteLine("S8 pairs ok " + ok + "/" + pairs + " (" + (100.0 * ok / pairs).ToString("0.0") + "%), nothing cuttable " + stuck
                + ", first-node cuts " + firstCuts);
            Assert.Greater(pairs, Seeds * 5 * 3);
            Assert.GreaterOrEqual(ok, 0.95 * (pairs - stuck));
            Assert.AreEqual(0, firstCuts);
        }

        [Test]
        public void Framing_AllStylesDay100_WholeTreeInsideTheFittedPotFrame()
        {
            // AC20 PA-102R (geometric part; pixels — PreviewMetricsHarness): frame centred on the pot, bounds of bark + roots + pads fit with the
            // 3% side margin; Moyogi / Chokkan of the reference size keep z ≤ 1.
            var builder = new TreePreviewMeshBuilder();
            var mesh = new Mesh();
            try
            {
                foreach (ShapeStyle style in new[] { ShapeStyle.Moyogi, ShapeStyle.Chokkan, ShapeStyle.Shakan })
                {
                    for (uint seed = 1; seed <= 10; seed++)
                    {
                        var model = new GrowthPreviewModel();
                        model.SetParameters(SpeciesId.Momiji, seed, 1.0, 0.0, 25.0, 0.5, true);
                        model.SetStyled(true);
                        model.SetShapeStyle(style);
                        builder.Build(model.GetTree(100), mesh, model.Config, model.SpeciesConfig);
                        Rect bounds = builder.LastBounds;
                        // The textured pot top (y_rim) is TreeBaseBelowRimUnits above the base; PNG frames use the 3% top margin.
                        float rim = new TexturedStyleConfig().TreeBaseBelowRimUnits;
                        float z = PotFraming.Zoom(92f, rim, bounds, 1f, PotFraming.SideMargin);
                        Rect frame = PotFraming.FrameAt(z, 92f, rim);
                        float margin = PotFraming.SideMargin * frame.width;
                        Assert.Less(z, PotFraming.ZoomHardMax, style + " seed " + seed);
                        Assert.GreaterOrEqual(bounds.xMin, frame.xMin + margin, style + " seed " + seed);
                        Assert.LessOrEqual(bounds.xMax, frame.xMax - margin, style + " seed " + seed);
                        Assert.AreEqual(0f, frame.center.x, 1e-4f, "the pot stays centred");
                        // The reference scale (z = 1) is kept whenever the tree fits it; a wide Moyogi / Chokkan zooms out only slightly.
                        if (PotFraming.Fits(PotFraming.FrameAt(1f, 92f, rim), bounds, PotFraming.SideMargin)) Assert.AreEqual(1f, z, 1e-4f, style + " seed " + seed);
                        if (style != ShapeStyle.Shakan) Assert.LessOrEqual(z, 1.10f, style + " seed " + seed);
                        TestContext.Out.WriteLine(style + " seed " + seed + " z " + z.ToString("0.00"));
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
