using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PocketArbor.DevTools.GrowthPreview;
using PocketArbor.Domain.Tree;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PocketArbor.DevTools.Tests
{
    /// <summary>PA-103S: Textured style of the growth preview (DevTools).</summary>
    public sealed class TexturedPreviewTests
    {
        const string MissingArtRoot = "Assets/_Project/Art/Runtime/__NoSuchPreviewFolder__";

        [UnityTest]
        public IEnumerator Scene_TexturedWithArtAndWithoutArt_RendersWithoutErrors()
        {
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(GrowthPreviewEditorTools.ScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            var controller = Object.FindFirstObjectByType<GrowthPreviewController>();
            Assert.IsNotNull(controller, "controller in scene");
            controller.SetTextured(true);
            Assert.IsNotNull(controller.TexturedView, "textured view created");
            Assert.IsTrue(controller.TexturedView.ShadersFound, "PreviewBark / PreviewSprite shaders found");

            foreach (string root in new[] { PreviewArtLibrary.DefaultRoot, MissingArtRoot })
            {
                controller.ReloadArt(root);
                TestContext.Out.WriteLine(root + ": " + controller.Art.Summary);
                if (root == MissingArtRoot) StringAssert.Contains("fallback", controller.Art.Summary);
                Mesh bark = controller.TexturedView.BarkMesh;
                Mesh pads = controller.TexturedView.PadMesh;
                foreach (bool styled in new[] { false, true })
                {
                    controller.SetStyled(styled);
                    foreach (SpeciesId species in new[] { SpeciesId.Kuromatsu, SpeciesId.Momiji, SpeciesId.Yamazakura })
                    {
                        controller.SetSpecies(species);
                        for (int day = 0; day <= GrowthPreviewModel.MaxPreviewDay; day += 8)
                        {
                            controller.SetDay(day);
                            Assert.Greater(bark.vertexCount, 0, species + " D" + day + " bark");
                            if (day >= 8) Assert.Greater(pads.vertexCount, 0, species + " D" + day + " pads");
                            Assert.AreSame(bark, controller.TexturedView.BarkMesh, "bark mesh is reused, not recreated");
                            Assert.AreSame(pads, controller.TexturedView.PadMesh, "pad mesh is reused, not recreated");
                            if (day % 40 == 0) yield return null; // render a frame (incl. OnGUI)
                        }
                    }
                }
                controller.SetReferenceOverlay(true, 0.4f);
                yield return null;
                controller.SetReferenceOverlay(false, 0.4f);
            }

            controller.SetTextured(false);
            Assert.IsTrue(controller.GetComponent<MeshRenderer>().enabled, "Flat renderer back on");
            yield return null;
            controller.SetTextured(true);
            yield return null;
        }

        [Test]
        public void BuildTextured_ApicalChild_ContinuesParentArcLength()
        {
            Build(SpeciesId.Momiji, 40, true, out TreePreviewMeshBuilder builder, out TreeGraph tree, out _, out _);
            int checkedJoints = 0;
            for (int i = 1; i < tree.Count; i++)
            {
                NodeData node = tree.Nodes[i];
                builder.ArcRange(i, out float start, out float end);
                Assert.Greater(end, start, "arc grows along node " + i);
                if (node.BranchSide != 0) continue;
                builder.ArcRange(node.ParentIndex, out _, out float parentEnd);
                Assert.AreEqual(parentEnd, start, 1e-4f, "axis continues the parent's arc at node " + i);
                checkedJoints++;
            }
            Assert.Greater(checkedJoints, 5);
        }

        [Test]
        public void BuildTextured_BarkStream_UvAcrossIsZeroOrOneAndAgeInRange()
        {
            Build(SpeciesId.Kuromatsu, 80, false, out _, out _, out Mesh bark, out _);
            var uv0 = new List<Vector2>();
            var uv1 = new List<Vector4>();
            var uv2 = new List<Vector2>();
            bark.GetUVs(0, uv0);
            bark.GetUVs(1, uv1);
            bark.GetUVs(2, uv2);
            Assert.AreEqual(bark.vertexCount, uv1.Count);
            Assert.AreEqual(bark.vertexCount, uv2.Count);
            float maxAge = 0f, minAge = 1f;
            for (int v = 0; v < uv0.Count; v++)
            {
                Assert.IsTrue(uv0[v].x == 0f || uv0[v].x == 1f, "u01 at vertex " + v);
                Assert.Greater(uv1[v].x, 0f, "width");
                Assert.That(uv1[v].y, Is.InRange(0f, 1f), "barkAge");
                Assert.That(uv1[v].z, Is.InRange(0f, 1f), "ink");
                Assert.AreEqual(1f, uv2[v].magnitude, 1e-3f, "side is a unit vector");
                maxAge = Mathf.Max(maxAge, uv1[v].y);
                minAge = Mathf.Min(minAge, uv1[v].y);
            }
            Assert.Greater(maxAge, 0.95f, "trunk uses the trunk bark");
            Assert.Less(minAge, 0.2f, "thin shoots use the young bark");
        }

        [Test]
        public void BuildTextured_Pads_SortedBottomUp()
        {
            foreach (SpeciesId species in new[] { SpeciesId.Kuromatsu, SpeciesId.Momiji, SpeciesId.Yamazakura })
            {
                Build(species, 80, true, out TreePreviewMeshBuilder builder, out _, out _, out Mesh pads);
                IReadOnlyList<float> keys = builder.LastPadOrderKeys;
                Assert.Greater(keys.Count, 3, species + " pads");
                for (int k = 1; k < keys.Count; k++) Assert.GreaterOrEqual(keys[k], keys[k - 1], species + " pad " + k);
                Assert.AreEqual(builder.LastPuffs.Count * 2 * 4, pads.vertexCount, "shadow + puff quad per puff");
            }
        }

        [Test]
        public void BuildTextured_PuffVariant_DependsOnNodeIdOnly()
        {
            var model = new GrowthPreviewModel();
            model.SetParameters(SpeciesId.Momiji, 20261004u, 1.0, 0.0, 25.0, 0.5, false);
            var builder = new TreePreviewMeshBuilder();
            var bark = new Mesh();
            var pads = new Mesh();
            PadAtlas atlas = PreviewArtFallbacks.PadAtlas(new Color32(90, 140, 70, 255), 7u, out Texture2D texture);
            try
            {
                builder.BuildTextured(model.GetTree(40), bark, pads, model.Config, model.SpeciesConfig, new TexturedStyleConfig(), atlas);
                var before = new Dictionary<long, TreePreviewMeshBuilder.PuffRecord>();
                foreach (TreePreviewMeshBuilder.PuffRecord p in builder.LastPuffs) before[Key(p)] = p;

                Assert.IsTrue(model.CutRandomBranch(40).Succeeded);
                builder.BuildTextured(model.GetTree(40), bark, pads, model.Config, model.SpeciesConfig, new TexturedStyleConfig(), atlas);
                int compared = 0;
                foreach (TreePreviewMeshBuilder.PuffRecord p in builder.LastPuffs)
                {
                    if (!before.TryGetValue(Key(p), out TreePreviewMeshBuilder.PuffRecord old) || old.Size != p.Size) continue;
                    Assert.AreEqual(old.Uv, p.Uv, "sprite of node " + p.NodeId);
                    Assert.AreEqual(old.Mirrored, p.Mirrored, "mirroring of node " + p.NodeId);
                    compared++;
                }
                Assert.Greater(compared, 10, "surviving puffs compared after a cut (indices shifted, Ids stable)");
            }
            finally
            {
                Object.DestroyImmediate(bark);
                Object.DestroyImmediate(pads);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void PadAtlas_Parse_TopLeftPixelsToBottomLeftUv()
        {
            const string json = "{\"pads\":[{\"name\":\"a\",\"x\":0,\"y\":0,\"w\":50,\"h\":25,\"pivotX\":0.5,\"pivotY\":0.2,\"size\":\"L\"}," +
                "{\"name\":\"b\",\"x\":50,\"y\":50,\"w\":50,\"h\":50,\"pivotX\":0.4,\"pivotY\":0.3,\"size\":\"S\"}]}";
            PadAtlas atlas = PadAtlas.Parse(null, json, 100, 100);
            Assert.AreEqual(2, atlas.Count);
            PadSprite large = atlas.Pick(PadSizeClass.L, 0.3f);
            Assert.AreEqual(new Rect(0f, 0.75f, 0.5f, 0.25f), large.Uv);
            Assert.AreEqual(0.5f, large.Aspect, 1e-6f);
            Assert.AreEqual(new Vector2(0.5f, 0.2f), large.Pivot);
            PadSprite small = atlas.Pick(PadSizeClass.S, 0.99f);
            Assert.AreEqual(new Rect(0.5f, 0f, 0.5f, 0.5f), small.Uv);
            // No "M" sprites: the nearest class is used (S first), never a crash.
            Assert.AreEqual(small.Uv, atlas.Pick(PadSizeClass.M, 0.5f).Uv);
        }

        [Test]
        public void PadAtlas_ParseInvalidJson_IsEmptyAndPickDoesNotThrow()
        {
            PadAtlas atlas = PadAtlas.Parse(null, "{\"pads\":[{\"w\":0,\"h\":10}]}", 100, 100);
            Assert.AreEqual(0, atlas.Count);
            Assert.DoesNotThrow(() => atlas.Pick(PadSizeClass.M, 0.5f));
            Assert.AreEqual(0, PadAtlas.Parse(null, null, 100, 100).Count);
        }

        [Test]
        public void PotArt_Parse_OpeningLayoutFromBottom()
        {
            PotArt pot = PotArt.Parse(null, "{\"lipY\":270,\"innerLeft\":80,\"innerRight\":1074,\"innerTopY\":420,\"innerTopLeft\":176,\"innerTopRight\":1004}", 1158, 454);
            Assert.IsTrue(pot.HasOpening);
            Assert.AreEqual(270f, pot.BaseY(0f));
            Assert.AreEqual(345f, pot.BaseY(0.5f), 1e-4f);
            Assert.AreEqual(994f, pot.OpeningWidth(0f), 1e-4f);
            Assert.AreEqual(828f, pot.OpeningWidth(1f), 1e-4f);
        }

        [Test]
        public void PotArt_Parse_MissingJsonFieldsUseDefaults()
        {
            PotArt pot = PotArt.Parse(null, "{\"lipY\":160,\"innerLeft\":30,\"innerRight\":470}", 500, 200);
            Assert.IsFalse(pot.HasOpening, "no back edge → no opening");
            Assert.AreEqual(160f, pot.BaseY(0.45f));
            Assert.AreEqual(440f, pot.OpeningWidth(0.45f));
            PotArt none = PotArt.Parse(null, null, 500, 200);
            Assert.Greater(none.InnerRight, none.InnerLeft);
            Assert.Greater(none.LipY, 0f);
            Assert.AreEqual(30f, SoilArt.ParseGround("{\"groundFromTop\":30,\"width\":1056}"));
            Assert.Less(SoilArt.ParseGround(null), 0f);
        }

        static long Key(TreePreviewMeshBuilder.PuffRecord p) => ((long)p.NodeId << 8) | (uint)p.Slot;

        static void Build(SpeciesId species, int day, bool styled, out TreePreviewMeshBuilder builder, out TreeGraph tree, out Mesh bark, out Mesh pads)
        {
            var model = new GrowthPreviewModel();
            model.SetParameters(species, 20261004u, 1.0, 0.0, 25.0, 0.5, false);
            model.SetStyled(styled);
            builder = new TreePreviewMeshBuilder();
            bark = new Mesh { hideFlags = HideFlags.DontSave };
            pads = new Mesh { hideFlags = HideFlags.DontSave };
            PadAtlas atlas = PreviewArtFallbacks.PadAtlas(TreePreviewMeshBuilder.LeafColor(species), 3u, out Texture2D texture);
            tree = model.GetTree(day);
            builder.BuildTextured(tree, bark, pads, model.Config, model.SpeciesConfig, new TexturedStyleConfig(), atlas);
            Object.DestroyImmediate(texture);
        }
    }
}
