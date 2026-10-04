using System.Collections;
using NUnit.Framework;
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
    }
}
