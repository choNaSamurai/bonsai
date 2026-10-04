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
        public void MeshBuilder_Day40And80_NebariBaseToTwoThirdsWidthAtLeast4()
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
                            Assert.GreaterOrEqual(builder.LastTaperRatio, 4.0f, species + " D" + day);
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
