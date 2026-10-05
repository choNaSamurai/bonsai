using System.Collections;
using NUnit.Framework;
using PocketArbor.Domain.Growth;
using PocketArbor.DevTools.GrowthPreview;
using PocketArbor.Domain.Tree;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PocketArbor.DevTools.Tests
{
    public sealed class GrowthPreviewTests
    {
        [UnityTest]
        public IEnumerator Scene_ScrubDays0To120ForAllSpecies_RendersWithoutErrors()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(GrowthPreviewEditorTools.ScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            var controller = Object.FindFirstObjectByType<GrowthPreviewController>();
            Assert.IsNotNull(controller, "controller in scene");
            Assert.IsNotNull(controller.TargetCamera, "camera wired");

            foreach (SpeciesId species in new[] { SpeciesId.Kuromatsu, SpeciesId.Momiji, SpeciesId.Yamazakura })
            {
                controller.SetSpecies(species);
                for (int day = 0; day <= GrowthPreviewModel.MaxPreviewDay; day++)
                {
                    controller.SetDay(day);
                    Assert.Greater(controller.NodeCount, 0);
                    Assert.Greater(controller.PreviewMesh.vertexCount, 0);
                    if (day % 20 == 0) yield return null; // let a frame render (incl. OnGUI)
                }
                Assert.Greater(controller.NodeCount, 100, species + " grows at G = 1");
                yield return null;
            }

            controller.CutRandomBranch();
            yield return null;
        }

        [Test]
        public void Model_CachedDays_MatchFreshReplay()
        {
            var cached = new GrowthPreviewModel();
            var fresh = new GrowthPreviewModel();
            cached.GetDay(GrowthPreviewModel.MaxPreviewDay);
            Assert.IsTrue(cached.GetDay(57).BitwiseEquals(fresh.GetDay(57)));
        }

        [Test]
        public void Model_CutIsReplayedDeterministically()
        {
            var model = new GrowthPreviewModel();
            int before = model.GetDay(30).NodeCount;
            PruneResult result = model.CutRandomBranch(30);
            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(before - result.RemovedCount + (result.PhoenixSpawned ? 1 : 0), model.GetDay(30).NodeCount);
            var again = model.GetDay(60);
            Assert.IsTrue(again.BitwiseEquals(model.GetDay(60)));
        }

        [Test]
        public void Model_CutAllTrunkChildren_PhoenixAppears()
        {
            var model = new GrowthPreviewModel();
            model.SetParameters(SpeciesId.Momiji, 3u, 1.0, 0.0, 25.0, 0.5, true); // N = 2 after onboarding cut
            PruneResult result = model.CutRandomBranch(0); // only the leader can be cut
            Assert.IsTrue(result.PhoenixSpawned);
            Assert.AreEqual(2, model.GetDay(0).NodeCount);
            Assert.AreEqual(NodeStatus.Bud, model.GetDay(0).Nodes[1].Status);
        }

        [Test]
        public void Model_DayReachingMaxNodes_WithinBalanceRange()
        {
            var model = new GrowthPreviewModel();
            int day = model.DayReachingMaxNodes();
            Assert.That(day, Is.InRange(66, 110));
        }

        [Test]
        public void Model_Styled_PruneToStyleCutsAndKeepsTiers()
        {
            foreach (SpeciesId species in new[] { SpeciesId.Kuromatsu, SpeciesId.Momiji, SpeciesId.Yamazakura })
            {
                var wild = new GrowthPreviewModel();
                wild.SetParameters(species, 20261004u, 1.0, 0.0, 25.0, 0.5, false);
                var styled = new GrowthPreviewModel();
                styled.SetParameters(species, 20261004u, 1.0, 0.0, 25.0, 0.5, false);
                styled.SetStyled(true);

                TreeGraph tree = styled.GetTree(80);
                Assert.Less(tree.Count, wild.GetTree(80).Count, species + ": styling removes nodes");
                var shape = new TreeShapeAnalysis();
                shape.Analyze(tree, styled.Config, styled.SpeciesConfig);
                int primaries = 0;
                for (int i = 1; i < tree.Count; i++)
                {
                    if (shape.IsPrimary(i)) primaries++;
                    Assert.IsFalse(tree.Nodes[i].ParentIndex == 0 && tree.Nodes[i].AttachT < 0.5, "wild twig is gone");
                }
                Assert.GreaterOrEqual(primaries, 3, species + ": ≥ 3 tiers after styling");
                Assert.IsTrue(styled.GetDay(80).BitwiseEquals(styled.GetDay(80)));
            }
        }

        [Test]
        public void Styled_FixtureDays80To120_NoNodeViolatesS2ToS7()
        {
            // PA-102P AC18: after PruneToStyle (BALANCE §1.3.7) nothing is left that S2–S7 would cut.
            foreach (SpeciesId species in new[] { SpeciesId.Kuromatsu, SpeciesId.Momiji, SpeciesId.Yamazakura })
            {
                var model = new GrowthPreviewModel();
                model.SetParameters(species, 20261004u, 1.0, 0.0, 25.0, 0.5, true);
                model.SetStyled(true);
                var styler = new MoyogiStyler();
                var ids = new System.Collections.Generic.List<int>();
                foreach (int day in new[] { 80, 90, 100, 110, 120 })
                {
                    TreeGraph tree = model.GetTree(day);
                    for (var rule = MoyogiStyler.Rule.S2CrownHeight; rule <= MoyogiStyler.Rule.S7Dome; rule++)
                    {
                        styler.Collect(tree, model.Config, model.SpeciesConfig, model.Style, rule, ids);
                        Assert.AreEqual(0, ids.Count, species + " D" + day + " " + rule + " still has " + ids.Count + " node(s)");
                    }
                }
            }
        }

        [Test]
        public void Styled_StyleDays_TwentyToOneTwentyEveryTen()
        {
            var style = new StyleConfig();
            for (int day = 0; day <= 130; day++) Assert.AreEqual(day >= 20 && day <= 120 && day % 10 == 0, style.IsStyleDay(day), "day " + day);
        }

        [Test]
        public void MeshBuilder_FixedPotAndNebari_Day80()
        {
            // PA-102P: pot = 92 units regardless of the crown (BALANCE §1.3.0); Nebari §1.3.3: 7 roots at th0 ≥ 13.9, ≥ 2 per side,
            // visible end ≥ 0.45 of the base thickness; PA-102Q: roots lie on the moss, only the last 25% buried.
            var builder = new TreePreviewMeshBuilder();
            var mesh = new Mesh();
            var samples = new System.Collections.Generic.List<Vector3>();
            try
            {
                var model = new GrowthPreviewModel();
                model.SetParameters(SpeciesId.Momiji, 20261004u, 1.0, 0.0, 25.0, 0.5, true);
                model.SetStyled(true);
                foreach (int day in new[] { 0, 80 })
                {
                    builder.Build(model.GetTree(day), mesh, model.Config, model.SpeciesConfig);
                    Assert.AreEqual(92f, builder.LastPotWidth);
                }
                Assert.AreEqual(7, builder.LastRootCount);
                int left = 0, right = 0;
                for (int r = 0; r < builder.LastRootCount; r++)
                {
                    builder.NebariRoot(20261004u, r, builder.LastRootCount, samples);
                    if (samples[samples.Count - 1].x < 0f) left++;
                    else right++;
                    Assert.GreaterOrEqual(samples[samples.Count - 1].z / samples[0].z, 0.45f - 1e-4f, "root " + r + " end thickness");
                    // ⚑Q BALANCE §1.4.2 / ADR-0004 rev.4: the tip dives under the moss line; the first 75% lie on the moss (centre ≥ moss line).
                    float moss = builder.Render.MossLineY;
                    Assert.Less(samples[samples.Count - 1].y + 0.5f * samples[samples.Count - 1].z, moss, "root " + r + " tip fully under the moss line");
                    for (int k = 1; k < samples.Count * 3 / 4 - 1; k++)
                        Assert.GreaterOrEqual(samples[k].y, moss - 1e-3f, "root " + r + " sample " + k + " lies on the moss");
                }
                Assert.GreaterOrEqual(left, 2);
                Assert.GreaterOrEqual(right, 2);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void PotFraming_SaplingToFullTree_PotAlwaysInsideFrame()
        {
            // TD-030 prototype: ZoomMin 0.78 (owner) — the whole pot (top − 0.161·H_ref of the L2 frame) stays above the frame bottom.
            foreach (float treeHeight in new[] { 0f, 20f, 60f, 130f, 200f })
            {
                float z = PotFraming.Zoom(92f, 0f, new Rect(-30f, 0f, 60f, treeHeight));
                Rect frame = PotFraming.FrameAt(z, 92f, 0f);
                // ADR-0004 rev.5 (PA-102R AC20, conscious update): fitting wins over ZoomMax; ZoomHardMax 1.60 is the cap.
                Assert.That(z, Is.InRange(PotFraming.ZoomMin, PotFraming.ZoomHardMax));
                float potBottom = -(1290f - 1068f) / 1376f * (PotFraming.FrameAt(1f, 92f, 0f).height);
                Assert.GreaterOrEqual(potBottom, frame.yMin, "pot bottom inside at z " + z);
                Assert.GreaterOrEqual(frame.width * PotFraming.PotShareOfWidth / z, 91.9f);
            }
            Assert.AreEqual(PotFraming.ZoomMin, PotFraming.Zoom(92f, 0f, new Rect(-10f, 0f, 20f, 20f)), 1e-4f, "a sapling gets the closest frame");
        }

        /// <remarks>PA-102S AC20 (flared base / width at 2/3 of the chain ≥ 4.0) is superseded by ⚑Q BALANCE §1.4.1 / §1.4.0: the flare is
        /// short (Gaussian, 0.22·th0) and the upper trunk stays thick (w(0.60H) ≈ 0.48·w(0.10H)) → the ratio is ≈ 3–5. The §1.4.0 profile
        /// itself is checked on 30 seeds by PreviewMetricsHarness (AC6); here: the base still flares (≥ 2.5) and the flare is gone by 0.06 H.</remarks>
        [Test]
        public void MeshBuilder_Day40And80_FlaredBaseAndShortFlare()
        {
            var builder = new TreePreviewMeshBuilder();
            var mesh = new Mesh();
            try
            {
                foreach (SpeciesId species in new[] { SpeciesId.Kuromatsu, SpeciesId.Momiji, SpeciesId.Yamazakura })
                {
                    foreach (bool styled in new[] { false, true })
                    {
                        var model = new GrowthPreviewModel();
                        model.SetParameters(species, 20261004u, 1.0, 0.0, 25.0, 0.5, false);
                        model.SetStyled(styled);
                        foreach (int day in new[] { 40, 80 })
                        {
                            builder.Build(model.GetTree(day), mesh, model.Config, model.SpeciesConfig);
                            TestContext.Out.WriteLine(species + (styled ? " styled" : " wild") + " D" + day + ": taper " + builder.LastTaperRatio.ToString("0.00") + ", pads " + builder.LastPadCount);
                            Assert.GreaterOrEqual(builder.LastTaperRatio, 2.5f, species + " D" + day);
                            // ⚑R BALANCE §1.5.1 (conscious update): heights from y_rim, TreeBaseBelowRimUnits (5.0) above the trunk base.
                            float rim = new TexturedStyleConfig().TreeBaseBelowRimUnits;
                            float h = builder.LastHeight - rim;
                            float w06 = builder.TrunkWidthAtHeight(rim + 0.06f * h), w10 = builder.TrunkWidthAtHeight(rim + 0.10f * h);
                            Assert.LessOrEqual(w06 / w10, 1.15f, species + " D" + day + ": flare ends below 0.06 H");
                            Assert.Greater(mesh.vertexCount, 0);
                        }
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
