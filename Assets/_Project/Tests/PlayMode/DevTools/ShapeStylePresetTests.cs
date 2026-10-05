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
    }
}
