using System.Collections.Generic;
using NUnit.Framework;
using PocketArbor.DevTools.GrowthPreview;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Tree;
using UnityEngine;

namespace PocketArbor.DevTools.Tests
{
    /// <summary>
    /// PA-102T (BALANCE §1.6) on the Styled reference: S10 «pad ceiling» invariants (T14), S3 keeps the crown shoulder, nodes by tier group (T2 / criterion B
    /// item 4 — logged and the B-threshold asserted), roots §1.6.4 (T15 structure) and the same bark mesh for every species bark (T18).
    /// </summary>
    public sealed class IterationFourStyledTests
    {
        const int Seeds = 30;

        static GrowthPreviewModel Model(SpeciesId species, uint seed, bool styled = true)
        {
            var model = new GrowthPreviewModel();
            model.SetParameters(species, seed, 1.0, 0.0, 25.0, 0.5, true);
            model.SetStyled(styled);
            return model;
        }

        [Test]
        public void S10_StyledMomiji30Seeds_GroupsAtMostTarget_NeverFirstNodeOrCrownAxis()
        {
            var t5 = new List<int>();
            int shoulders = 0;
            for (uint seed = 1; seed <= Seeds; seed++)
            {
                GrowthPreviewModel model = Model(SpeciesId.Momiji, seed);
                ShapeParams shape = ShapeParams.Resolve(model.Config, model.SpeciesConfig);
                foreach (int day in new[] { 80, 100, 120 })
                {
                    TreeGraph before = model.GetTree(day - 1);
                    TreeGraph tree = model.GetTree(day);
                    var analysis = new TreeShapeAnalysis();
                    analysis.Analyze(tree, model.Config, model.SpeciesConfig);
                    var style = model.Style;
                    for (int g = 0; g <= (int)TierGroup.Dome; g++)
                    {
                        var group = (TierGroup)g;
                        int target = shape.TierTarget(group);
                        int cap = Mathf.CeilToInt((float)(style.StyleTierCapRatio * target) - 1e-4f);
                        Assert.LessOrEqual(analysis.GroupCount(group), Mathf.Max(target, cap), "seed " + seed + " D" + day + " " + group);
                    }
                    if (day == 80) t5.Add(analysis.GroupCount(TierGroup.T5));
                    // Primaries' first nodes and crown-axis nodes alive the day before survive the styling.
                    var beforeShape = new TreeShapeAnalysis();
                    beforeShape.Analyze(before, model.Config, model.SpeciesConfig);
                    for (int i = 0; i < before.Count; i++)
                    {
                        if (!(beforeShape.IsPrimary(i) || beforeShape.IsCrownShoulder(i)) || before.Nodes[i].Status == NodeStatus.Bud) continue;
                        if (beforeShape.IsCrownShoulder(i)) shoulders++;
                        bool intermediate = beforeShape.IsPrimary(i) && before.Nodes[before.Nodes[i].ParentIndex].Depth >= 1
                            && TreeShapeAnalysis.ChainLateralSlot(before.Nodes[i].BranchSide, before.Nodes[before.Nodes[i].ParentIndex].Depth, seed) == 0;
                        if (intermediate) continue; // S3 cuts intermediate tiers
                        Assert.GreaterOrEqual(tree.IndexOf(before.Nodes[i].Id), 0, "seed " + seed + " D" + day + ": tier / shoulder " + before.Nodes[i].Id + " cut");
                    }
                }
            }
            t5.Sort();
            int median = t5[t5.Count / 2];
            TestContext.Out.WriteLine("Momiji Styled D80 N(t5) median " + median + " (T2 ≥ 6; criterion B item 4: < 5)");
            Assert.GreaterOrEqual(median, 5, "criterion B item 4 (BALANCE §1.6.9)");
            Assert.Greater(shoulders, 20, "S3 keeps the crown shoulder");
        }

        [Test]
        public void Roots_D80ThreeSpecies_CountMainMinorForks()
        {
            var builder = new TreePreviewMeshBuilder();
            var mesh = new Mesh();
            var samples = new List<Vector3>();
            var fork = new List<Vector3>();
            try
            {
                foreach (SpeciesId species in new[] { SpeciesId.Kuromatsu, SpeciesId.Momiji, SpeciesId.Yamazakura })
                {
                    for (uint seed = 1; seed <= 10; seed++)
                    {
                        GrowthPreviewModel model = Model(species, seed);
                        TreeGraph tree = model.GetTree(80);
                        builder.Build(tree, mesh, model.Config, model.SpeciesConfig);
                        float th0 = builder.LastTrunkBase;
                        int expected = Mathf.Clamp((int)System.Math.Round(2.0 + (th0 - 4.0) / 1.6, System.MidpointRounding.AwayFromZero), 2, 10);
                        Assert.AreEqual(expected, builder.LastRootCount, species + " seed " + seed);
                        int forks = 0, left = 0, right = 0;
                        for (int r = 0; r < builder.LastRootCount; r++)
                        {
                            builder.NebariRoot(tree.Genome.Seed, r, builder.LastRootCount, samples, fork);
                            float t = samples[0].z / th0;
                            if (builder.IsMainRoot(r)) Assert.That(t, Is.InRange(0.32f - 1e-4f, 0.40f + 1e-4f), "main root " + r);
                            else Assert.That(t, Is.InRange(0.16f - 1e-4f, 0.26f + 1e-4f), "minor root " + r);
                            Assert.AreEqual(t >= builder.Render.NebariRootForkMinRatio, fork.Count > 1, "fork iff thick");
                            if (fork.Count > 1) forks++;
                            if (samples[samples.Count - 1].x < 0f) left++;
                            else right++;
                        }
                        Assert.GreaterOrEqual(forks, 2);
                        Assert.GreaterOrEqual(left, 3);
                        Assert.GreaterOrEqual(right, 3);
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
