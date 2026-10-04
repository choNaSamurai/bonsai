using System;
using System.IO;
using PocketArbor.Domain.Tree;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>
    /// Editor utilities for the growth preview: scene creation (menu) and batch PNG capture
    /// (<c>-executeMethod PocketArbor.DevTools.GrowthPreview.GrowthPreviewEditorTools.CaptureBatch -captureOut &lt;dir&gt;</c>).
    /// </summary>
    public static class GrowthPreviewEditorTools
    {
        public const string ScenePath = "Assets/_Project/Scenes/Dev/GrowthPreview.unity";
        static readonly Color Background = new Color(0.93f, 0.92f, 0.89f, 1f);

        [MenuItem("Pocket Arbor/Dev/Create Growth Preview Scene")]
        public static void CreateScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 60f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 500f;
            cameraGo.transform.position = new Vector3(0f, 30f, -100f);

            var previewGo = new GameObject("GrowthPreview");
            previewGo.AddComponent<MeshFilter>();
            previewGo.AddComponent<MeshRenderer>();
            var controller = previewGo.AddComponent<GrowthPreviewController>();
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("_camera").objectReferenceValue = camera;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log("[GrowthPreview] scene saved: " + ScenePath);
        }

        /// <summary>Batch entry: creates the scene if missing (-createScene) and/or captures PNGs (-captureOut dir).</summary>
        public static void CaptureBatch()
        {
            int exitCode = 0;
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                if (Array.IndexOf(args, "-createScene") >= 0) CreateScene();
                string outDir = ArgValue(args, "-captureOut");
                if (!string.IsNullOrEmpty(outDir)) Capture(outDir, new[] { 10, 20, 40, 80 }, 1024);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                exitCode = 1;
            }
            EditorApplication.Exit(exitCode);
        }

        public static void Capture(string outDir, int[] days, int size)
        {
            Directory.CreateDirectory(outDir);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var controller = UnityEngine.Object.FindFirstObjectByType<GrowthPreviewController>();
            if (controller == null) throw new InvalidOperationException("GrowthPreviewController not found in " + ScenePath);
            Camera camera = controller.TargetCamera != null ? controller.TargetCamera : Camera.main;

            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var readback = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var report = new System.Text.StringBuilder("species,mode,day,nodes,pads,taper_ratio,H,W,th0,th0_flared,P\n");
            try
            {
                camera.targetTexture = rt;
                foreach (bool styled in new[] { false, true })
                {
                    controller.SetStyled(styled);
                    foreach (SpeciesId species in new[] { SpeciesId.Kuromatsu, SpeciesId.Momiji, SpeciesId.Yamazakura })
                    {
                        controller.SetSpecies(species);
                        foreach (int day in days)
                        {
                            controller.SetDay(day);
                            camera.Render();
                            RenderTexture previous = RenderTexture.active;
                            RenderTexture.active = rt;
                            readback.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                            readback.Apply();
                            RenderTexture.active = previous;
                            string mode = styled ? "styled" : "wild";
                            string file = Path.Combine(outDir, species + "_" + mode + "_day" + day.ToString("000") + "_n" + controller.NodeCount + ".png");
                            File.WriteAllBytes(file, readback.EncodeToPNG());
                            report.Append(species).Append(',').Append(mode).Append(',').Append(day).Append(',').Append(controller.NodeCount)
                                .Append(',').Append(controller.PadCount).Append(',')
                                .Append(F(controller.TaperRatio)).Append(',').Append(F(controller.Builder.LastHeight)).Append(',')
                                .Append(F(controller.Builder.LastCrownWidth)).Append(',').Append(F(controller.Builder.LastTrunkBase)).Append(',')
                                .Append(F(controller.Builder.LastTrunkBaseFlared)).Append(',').Append(F(controller.Builder.LastPotWidth)).Append('\n');
                            Debug.Log("[GrowthPreview] captured " + file);
                        }
                    }
                }
                File.WriteAllText(Path.Combine(outDir, "metrics.csv"), report.ToString());
            }
            finally
            {
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(readback);
            }
        }

        static string F(float value) => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        static string ArgValue(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
