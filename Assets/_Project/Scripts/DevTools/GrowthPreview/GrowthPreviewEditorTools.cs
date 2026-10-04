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
                if (!string.IsNullOrEmpty(outDir))
                {
                    if (Array.IndexOf(args, "-textured") >= 0) CaptureTextured(outDir, new[] { 40, 80 }, 1024);
                    else Capture(outDir, new[] { 10, 20, 40, 80 }, 1024);
                }
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

        /// <summary>
        /// PA-103S: Textured × Styled × 3 species × <paramref name="days"/> (square PNGs, no MSAA — edge AA is the shader's),
        /// plus Momiji Styled D80 framed exactly like the reference (pot-aligned) next to realism_l2_painterly.jpg:
        /// compare_momiji.png (side by side, same height) and compare_momiji_overlay.png (reference over the preview at 50%).
        /// </summary>
        public static void CaptureTextured(string outDir, int[] days, int size)
        {
            Directory.CreateDirectory(outDir);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var controller = UnityEngine.Object.FindFirstObjectByType<GrowthPreviewController>();
            if (controller == null) throw new InvalidOperationException("GrowthPreviewController not found in " + ScenePath);
            Camera camera = controller.TargetCamera != null ? controller.TargetCamera : Camera.main;
            controller.SetStyled(true);
            controller.SetTextured(true);
            controller.ReloadArt();
            Debug.Log("[GrowthPreview] art: " + controller.Art.Summary);
            File.WriteAllText(Path.Combine(outDir, "art_status.txt"), controller.Art.Summary + "\n");

            foreach (SpeciesId species in new[] { SpeciesId.Kuromatsu, SpeciesId.Momiji, SpeciesId.Yamazakura })
            {
                controller.SetSpecies(species);
                foreach (int day in days)
                {
                    controller.SetDay(day);
                    string file = Path.Combine(outDir, species + "_styled_textured_day" + day.ToString("000") + "_n" + controller.NodeCount + ".png");
                    File.WriteAllBytes(file, RenderToPng(camera, size, size, null));
                    Debug.Log("[GrowthPreview] captured " + file);
                }
            }

            // Compare frame: Momiji Styled D80, camera over the pot-aligned reference rect.
            controller.SetSpecies(SpeciesId.Momiji);
            controller.SetDay(80);
            Texture2D reference = controller.Art.Reference;
            if (reference == null)
            {
                Debug.LogWarning("[GrowthPreview] reference jpg not found: " + PreviewArtLibrary.ReferencePath);
                return;
            }
            int w = reference.width, h = reference.height;
            controller.SetReferenceOverlay(false, 0.5f);
            if (!controller.FrameReference(camera)) throw new InvalidOperationException("reference frame unavailable");
            var ours = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                RenderToPng(camera, w, h, ours);
                var sideBySide = new Texture2D(w * 2, h, TextureFormat.RGBA32, false);
                sideBySide.SetPixels32(0, 0, w, h, ours.GetPixels32());
                sideBySide.SetPixels32(w, 0, w, h, reference.GetPixels32());
                sideBySide.Apply();
                File.WriteAllBytes(Path.Combine(outDir, "compare_momiji.png"), sideBySide.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(sideBySide);

                controller.SetReferenceOverlay(true, 0.5f);
                controller.FrameReference(camera);
                File.WriteAllBytes(Path.Combine(outDir, "compare_momiji_overlay.png"), RenderToPng(camera, w, h, null));
                controller.SetReferenceOverlay(false, 0.5f);
                Debug.Log("[GrowthPreview] captured compare_momiji.png");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ours);
            }
        }

        /// <summary>Renders <paramref name="camera"/> into a w×h sRGB target (no MSAA); returns PNG bytes, optionally keeps pixels.</summary>
        static byte[] RenderToPng(Camera camera, int width, int height, Texture2D keep)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            Texture2D readback = keep != null ? keep : new Texture2D(width, height, TextureFormat.RGBA32, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                readback.Apply();
                return readback.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(rt);
                if (keep == null) UnityEngine.Object.DestroyImmediate(readback);
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
