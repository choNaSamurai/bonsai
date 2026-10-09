using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using PocketArbor.Spike.Render;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

namespace PocketArbor.Spike.Editor
{
    /// <summary>
    /// PA-102X-S phase 1 harness (G6, G7). Run via
    ///   AGENT=ua .agent-workspace/pa102xs/unity-run.sh exec PocketArbor.Spike.Editor.SpikeHarness.Run [-spikeKeys k1,k2]
    /// Renders golden configurations with the sketch camera to 1360x960 RGBA PNG (2x2 SSAA), compares alpha IoU with
    /// the sketch references, writes sheets, results.md, the depth test (G6) and the spike scene.
    /// </summary>
    public static class SpikeHarness
    {
        private const int W = 1360, H = 960, SS = 2;
        private const float IoUGate = 0.92f;
        private static readonly string[] Species = { "momiji", "yamazakura", "kuromatsu" };
        private static readonly string[] Styles = { "moyogi", "chokkan", "shakan" };
        private static readonly int[] Ages = { 30, 60, 100 };
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private sealed class Row
        {
            public string Key;
            public float IoU;
            public int OnlyRef, OnlyUnity, Union;
            public SketchTreeRenderer.Stats S;
            public double RenderMs, GenMs;
            public string Error;
        }

        /// <summary>Quality preset (27 configs, G6, sheets, scene) and then the budget preset (27 configs, sheets) in one Unity run.</summary>
        public static void Run()
        {
            string[] keys = KeysFromArgs() ?? DefaultKeys();
            Execute(keys, true, true, true, false);
            try
            {
                Execute(keys, true, false, false, true);
            }
            catch (Exception e)
            {
                Debug.LogError("SPIKE| budget preset failed: " + e);
            }
        }

        public static void Smoke() { Execute(KeysFromArgs() ?? new[] { "momiji_moyogi_s3_t100" }, false, true, false, ArgValue("-spikeLod") == "budget"); }
        public static void DepthOnly() { Execute(new string[0], false, true, false, false); }

        private static string[] DefaultKeys()
        {
            var keys = new List<string>();
            foreach (string sp in Species)
            foreach (string st in Styles)
            foreach (int t in Ages)
                keys.Add(sp + "_" + st + "_s3_t" + t);
            return keys.ToArray();
        }

        private static string[] KeysFromArgs()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-spikeKeys") return args[i + 1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            return null;
        }

        private static void Log(string s) { Debug.Log("SPIKE| " + s); }

        private static void Execute(string[] keys, bool sheets, bool depthTest, bool makeScene, bool budget)
        {
            string ws = GoldenFrameLoader.WorkspaceFolder();
            if (ws == null) throw new DirectoryNotFoundException(".agent-workspace/pa102xs not found from " + Application.dataPath);
            string outDir = budget ? Path.Combine(ws, "unity", "budget") : Path.Combine(ws, "unity");
            if (ArgValue("-spikeOut") != null) outDir = Path.Combine(ws, "unity", ArgValue("-spikeOut") + (budget ? "_budget" : ""));
            string dbgDir = Path.Combine(outDir, "debug");
            Directory.CreateDirectory(outDir);
            Directory.CreateDirectory(dbgDir);
            Log("source " + (UseGenerator() ? "generator port (TreeGen)" : "golden files") + " | workspace " + ws + " | GPU " + SystemInfo.graphicsDeviceName + " | API " + SystemInfo.graphicsDeviceType + " | pipeline " + (GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "builtin"));

            ShaderUtil.allowAsyncCompilation = false;
            CheckShaders();

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camGo = new GameObject("SketchCamera");
            var cam = camGo.AddComponent<Camera>();
            SketchCamera.Apply(cam);
            // HideAndDontSave: EditorSceneManager.NewScene (spike scene) unloads unreferenced assets.
            var rt = new RenderTexture(W * SS, H * SS, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                antiAliasing = 1, name = "PA102XS_Target", hideFlags = HideFlags.HideAndDontSave
            };
            rt.Create();
            var read = new Texture2D(W * SS, H * SS, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave };
            var tree = new SketchTreeRenderer(null, HideFlags.None);
            if (budget)
            {
                tree.WoodBuilder.UseBudgetPreset();
                tree.PotVoxel = 2.5f;
            }
            Log("tessellation preset: " + (budget ? "budget" : "quality"));
            var frame = new SketchFrame();
            var rows = new List<Row>();

            try
            {
                // Warm-up (shader compilation, first pot) on the first key — not measured.
                if (keys.Length > 0)
                {
                    LoadFrame(ws, keys[0], frame);
                    var wu = tree.Show(frame);
                    RenderPixels(cam, rt, read);
                    Log(string.Format(Inv, "warm-up {0}: pot {1:0.0} ms (Surface Nets, voxel {2}), pot {3} verts / {4} tris", keys[0], wu.PotMs, tree.PotVoxel, wu.PotVerts, wu.PotTris));
                }

                foreach (string key in keys)
                {
                    var row = new Row { Key = key };
                    rows.Add(row);
                    try
                    {
                        row.GenMs = LoadFrame(ws, key, frame);
                        row.S = tree.Show(frame);
                        var sw = Stopwatch.StartNew();
                        Color32[] big = RenderPixels(cam, rt, read);
                        row.RenderMs = sw.Elapsed.TotalMilliseconds;
                        Color32[] img = SpikeImages.Downsample(big, W * SS, H * SS, SS);
                        SpikeImages.SavePng(Path.Combine(outDir, key + ".png"), img, W, H);
                        string refPath = Path.Combine(ws, "refs", key + ".png");
                        if (File.Exists(refPath))
                        {
                            Color32[] refPx = SpikeImages.LoadPng(refPath, out int rw, out int rh);
                            if (rw != W || rh != H) throw new InvalidDataException("ref size " + rw + "x" + rh);
                            row.IoU = SpikeImages.AlphaIoU(refPx, img, out _, out row.Union, out row.OnlyRef, out row.OnlyUnity);
                            SpikeImages.SavePng(Path.Combine(dbgDir, "diff_" + key + ".png"), SpikeImages.MaskDiff(refPx, img), W, H);
                        }
                        Log(string.Format(Inv, "{0} parts verts: trunk {1} roots {2} limbs {3} soil {4}", key, row.S.TrunkVerts, row.S.RootVerts, row.S.LimbVerts, row.S.SoilVerts));
                        Log(string.Format(Inv, "{0} IoU {1:0.0000} | wood {2:0.0} ms {3} v {4} t | leaves {5} {6:0.0} ms | upload {7:0.0} ms | bake {8:0.0} ms | render {9:0.0} ms",
                            key, row.IoU, row.S.WoodMs, row.S.WoodVerts, row.S.WoodTris, row.S.Leaves, row.S.LeafMs, row.S.UploadMs, row.S.BakeMs, row.RenderMs));
                    }
                    catch (Exception e)
                    {
                        row.Error = e.GetType().Name + ": " + e.Message;
                        Debug.LogError("SPIKE| " + key + " failed: " + e);
                    }
                }

                string depthReport = depthTest && !budget ? DepthTest(cam, rt, read, tree, frame, ws, outDir) : "";
                if (rows.Count > 0) WriteResults(Path.Combine(outDir, "results.md"), rows, depthReport, tree.PotVoxel, budget);
                if (sheets) WriteSheets(ws, outDir, rows);
                if (makeScene && !budget) MakeScene(ws);
            }
            finally
            {
                if (cam != null) cam.targetTexture = null;
                tree.Dispose();
                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }
                if (read != null) UnityEngine.Object.DestroyImmediate(read);
            }
        }

        private static string ArgValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        private static bool UseGenerator()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-spikeSource") return args[i + 1] != "golden";
            return true;
        }

        /// <summary>key = species_style_s{seed}_t{30|60|100}; generator port by default, golden files with -spikeSource golden. Returns ms.</summary>
        private static double LoadFrame(string ws, string key, SketchFrame frame)
        {
            var sw = Stopwatch.StartNew();
            if (UseGenerator())
            {
                string[] p = key.Split('_');
                if (p.Length != 4) throw new ArgumentException("bad key " + key);
                int seed = int.Parse(p[2].Substring(1), Inv);
                float t = int.Parse(p[3].Substring(1), Inv) / 100f;
                TreeGenAdapter.Generate(p[0], p[1], seed, t, frame);
                if (frame.Key != key) throw new InvalidDataException("generator key " + frame.Key + " != " + key);
            }
            else if (!GoldenFrameLoader.TryLoad(Path.Combine(ws, "golden"), key, frame)) throw new FileNotFoundException("golden " + key);
            return sw.Elapsed.TotalMilliseconds;
        }

        private static void CheckShaders()
        {
            string[] names = { SketchTreeRenderer.WoodShader, SketchTreeRenderer.PotShader, SketchTreeRenderer.LeafShader, SketchTreeRenderer.BakeShader, "PocketArbor/Spike/DepthProbe" };
            bool bad = false;
            foreach (string n in names)
            {
                Shader s = Shader.Find(n);
                if (s == null) { Debug.LogError("SPIKE-SHADER| missing " + n); bad = true; continue; }
                ShaderMessage[] msgs = ShaderUtil.GetShaderMessages(s);
                foreach (ShaderMessage m in msgs)
                {
                    string line = "SPIKE-SHADER| " + n + " [" + m.severity + "] " + m.file + ":" + m.line + " " + m.message + " " + m.messageDetails;
                    if (m.severity.ToString() == "Error") { Debug.LogError(line); bad = true; }
                    else Debug.LogWarning(line);
                }
                if (ShaderUtil.ShaderHasError(s)) { Debug.LogError("SPIKE-SHADER| has errors: " + n); bad = true; }
                Log("shader ok? " + n + " supported=" + s.isSupported + " passes=" + s.passCount);
            }
            if (bad) throw new InvalidOperationException("spike shaders have errors (see SPIKE-SHADER lines)");
        }

        private static Color32[] RenderPixels(Camera cam, RenderTexture rt, Texture2D read)
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            read.Apply(false);
            RenderTexture.active = prev;
            return read.GetPixels32();
        }

        // ---------------------------------------------------------------- G6

        private static string DepthTest(Camera cam, RenderTexture rt, Texture2D read, SketchTreeRenderer tree, SketchFrame frame, string ws, string outDir)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## G6 — глибина в URP 2D Renderer (" + SystemInfo.graphicsDeviceType + ", " + (GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "?") + ")");
            sb.AppendLine();
            int layerBack = EnsureSortingLayer("SpikeBack");
            int layerFront = EnsureSortingLayer("SpikeFront");
            sb.AppendLine("Тимчасові Sorting Layers у копії проєкту: SpikeBack id=" + layerBack + ", SpikeFront id=" + layerFront + " (порядок: Default, SpikeBack, SpikeFront).");
            sb.AppendLine();

            // A. Synthetic probe: near red quad vs far green quad, far one drawn LATER.
            tree.Root.SetActive(false);
            Shader probe = SketchTreeRenderer.FindShader("PocketArbor/Spike/DepthProbe");
            var red = new Material(probe) { color = Color.red };
            var green = new Material(probe) { color = Color.green };
            MeshRenderer near = ProbeQuad(cam, "ProbeNear", red, 300f, new Vector2(-60f, 0f));
            MeshRenderer far = ProbeQuad(cam, "ProbeFar", green, 600f, new Vector2(60f, 0f));
            sb.AppendLine("### A. Синтетичний: червоний квад ближче, зелений далі, перекриття в центрі");
            sb.AppendLine("| Варіант | порядок малювання | центр перекриття | очікується | результат |");
            sb.AppendLine("|---|---|---|---|---|");
            ProbeCase(sb, cam, rt, read, near, far, 0, 0, 0, 1, "той самий шар, order", "near→far");
            ProbeCase(sb, cam, rt, read, near, far, 0, 0, 1, 0, "той самий шар, order", "far→near");
            if (layerBack != 0 && layerFront != 0)
            {
                ProbeCase(sb, cam, rt, read, near, far, layerBack, layerFront, 0, 0, "різні Sorting Layers", "near(SpikeBack)→far(SpikeFront)");
                ProbeCase(sb, cam, rt, read, near, far, layerFront, layerBack, 0, 0, "різні Sorting Layers", "far(SpikeBack)→near(SpikeFront)");
                GameObject lightGo = GlobalLightFor(layerFront);
                ProbeCase(sb, cam, rt, read, near, far, layerBack, layerFront, 0, 0, "різні шари + Light2D лише на SpikeFront (окремі layer batches)", "near→far");
                ProbeCase(sb, cam, rt, read, near, far, layerFront, layerBack, 0, 0, "різні шари + Light2D лише на SpikeFront (окремі layer batches)", "far→near");
                if (lightGo != null) UnityEngine.Object.DestroyImmediate(lightGo);
            }
            UnityEngine.Object.DestroyImmediate(near.gameObject);
            UnityEngine.Object.DestroyImmediate(far.gameObject);
            UnityEngine.Object.DestroyImmediate(red);
            UnityEngine.Object.DestroyImmediate(green);
            tree.Root.SetActive(true);

            // B. Real tree: wood/pot drawn before leaves vs leaves drawn first; the images must match.
            sb.AppendLine();
            sb.AppendLine("### B. Дерево: деревина↔листя у різному порядку малювання (момідзі мойоґі t=1)");
            {
                LoadFrame(ws, "momiji_moyogi_s3_t100", frame);
                tree.Show(frame);
                tree.SetOrders(0, 0, 1);
                Color32[] woodFirst = RenderPixels(cam, rt, read);
                tree.SetOrders(2, 2, 1);
                Color32[] leavesFirst = RenderPixels(cam, rt, read);
                int d1 = SpikeImages.CountDiff(woodFirst, leavesFirst, 2);
                string v1 = string.Format(Inv, "| Default, order: деревина 0 / листя 1 vs деревина 2 / листя 1 | різних пікселів {0} з {1} ({2:0.000}%) |", d1, woodFirst.Length, 100.0 * d1 / woodFirst.Length);
                sb.AppendLine("| Варіант | Різниця |");
                sb.AppendLine("|---|---|");
                sb.AppendLine(v1);
                SpikeImages.SavePng(Path.Combine(outDir, "debug", "g6_leaves_first.png"), SpikeImages.Downsample(leavesFirst, W * SS, H * SS, SS), W, H);
                if (layerBack != 0 && layerFront != 0)
                {
                    tree.SetOrders(0, 0, 0);
                    tree.Wood.sortingLayerID = layerFront; tree.Pot.sortingLayerID = layerFront; tree.Leaves.sortingLayerID = layerBack;
                    GameObject lightGo = GlobalLightFor(layerFront);
                    Color32[] layered = RenderPixels(cam, rt, read);
                    int d2 = SpikeImages.CountDiff(woodFirst, layered, 2);
                    sb.AppendLine(string.Format(Inv, "| листя на SpikeBack (раніше), деревина на SpikeFront (пізніше) + Light2D на SpikeFront | різних пікселів {0} ({1:0.000}%) |", d2, 100.0 * d2 / woodFirst.Length));
                    if (lightGo != null) UnityEngine.Object.DestroyImmediate(lightGo);
                    tree.Wood.sortingLayerID = 0; tree.Pot.sortingLayerID = 0; tree.Leaves.sortingLayerID = 0;
                }
                tree.SetOrders(0, 0, 1);
            }
            string s = sb.ToString();
            Log(s.Replace("\n", " / "));
            return s;
        }

        private static void ProbeCase(StringBuilder sb, Camera cam, RenderTexture rt, Texture2D read, MeshRenderer near, MeshRenderer far,
            int nearLayer, int farLayer, int nearOrder, int farOrder, string variant, string order)
        {
            near.sortingLayerID = nearLayer;
            far.sortingLayerID = farLayer;
            near.sortingOrder = nearOrder;
            far.sortingOrder = farOrder;
            Color32[] px = RenderPixels(cam, rt, read);
            Color32 c = px[(H * SS / 2) * W * SS + W * SS / 2];
            string got = c.r > 200 && c.g < 60 ? "червоний" : c.g > 200 && c.r < 60 ? "зелений" : "інше (" + c + ")";
            sb.AppendLine("| " + variant + " | " + order + " | " + got + " | червоний | " + (got == "червоний" ? "✅" : "❌") + " |");
        }

        private static MeshRenderer ProbeQuad(Camera cam, string name, Material mat, float dist, Vector2 offset)
        {
            Transform t = cam.transform;
            Vector3 c = t.position + t.forward * dist + t.right * offset.x + t.up * offset.y;
            var go = new GameObject(name);
            var mesh = new Mesh();
            Vector3 r = t.right * 100f, u = t.up * 100f;
            mesh.vertices = new[] { c - r - u, c + r - u, c - r + u, c + r + u };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            return mr;
        }

        private static int EnsureSortingLayer(string name)
        {
            foreach (SortingLayer l in SortingLayer.layers) if (l.name == name) return l.id;
            try
            {
                UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
                if (assets == null || assets.Length == 0) return 0;
                var tm = new SerializedObject(assets[0]);
                SerializedProperty arr = tm.FindProperty("m_SortingLayers");
                int idx = arr.arraySize;
                arr.InsertArrayElementAtIndex(idx);
                SerializedProperty e = arr.GetArrayElementAtIndex(idx);
                e.FindPropertyRelative("name").stringValue = name;
                e.FindPropertyRelative("uniqueID").intValue = 0x3A10000 + idx * 7919 + name.Length;
                tm.ApplyModifiedPropertiesWithoutUndo();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("SPIKE| cannot add sorting layer " + name + ": " + ex.Message);
                return 0;
            }
            foreach (SortingLayer l in SortingLayer.layers) if (l.name == name) return l.id;
            return 0;
        }

        private static GameObject GlobalLightFor(int layerId)
        {
            try
            {
                var go = new GameObject("SpikeGlobalLight2D");
                var light = go.AddComponent<Light2D>();
                light.lightType = Light2D.LightType.Global;
                var so = new SerializedObject(light);
                SerializedProperty p = so.FindProperty("m_ApplyToSortingLayers");
                if (p != null && p.isArray)
                {
                    p.arraySize = 1;
                    p.GetArrayElementAtIndex(0).intValue = layerId;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                else Debug.LogWarning("SPIKE| Light2D.m_ApplyToSortingLayers not found");
                return go;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("SPIKE| Light2D setup failed: " + ex.Message);
                return null;
            }
        }

        // ---------------------------------------------------------------- results & sheets

        private static void WriteResults(string path, List<Row> rows, string depthReport, float potVoxel, bool outDirIsBudget)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# PA-102X-S фаза 1 — результати рендеру B* у редакторі Unity");
            sb.AppendLine();
            sb.AppendLine("- Згенеровано: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv) + ", Unity " + Application.unityVersion + ", GPU " + SystemInfo.graphicsDeviceName + " (" + SystemInfo.graphicsDeviceType + "), batchmode.");
            sb.AppendLine("- Тесселяція: " + (outDirIsBudget ? "**budget** (грубіша, під бюджет ADR-0011)" : "**quality**") + ".");
            sb.AppendLine("- **Заміри — редактор на Mac (Mono, Debug-код), не мобільні.** Рендер 2720×1920 → 1360×960 (SSAA 2×2), IoU альфи (≥128) проти `refs/`.");
            sb.AppendLine("- Джерело дерева: " + (UseGenerator() ? "C#-порт генератора (TreeGen, G1), gen ms — `SketchGenerator.Scene` (план кешовано)" : "golden-файли ескізу") + ".");
            sb.AppendLine("- wood ms — побудова мешу деревини+ґрунту (CPU); leaves ms — меш листя з сортуванням front-to-back; upload — data-текстура SDF + глобали; bake — 3 GPU-проходи (snap, light, mat) із синхронізацією (readback); горщик (Surface Nets, воксель " + potVoxel.ToString(Inv) + ") будується раз на породу й тут не враховано.");
            sb.AppendLine();
            sb.AppendLine("| Конфігурація | IoU | лише ескіз px | лише Unity px | gen ms | wood ms | wood verts | wood tris | гілок (limbs) | leaves | leaves ms | upload ms | bake ms (CPU fill+upload / GPU+sync) | render ms | pot tris |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            float mn = 1f, sum = 0f;
            int n = 0, pass = 0;
            foreach (Row r in rows)
            {
                if (r.Error != null)
                {
                    sb.AppendLine("| " + r.Key + " | ❌ " + r.Error + " ||||||||||||||");
                    continue;
                }
                n++;
                sum += r.IoU;
                mn = Mathf.Min(mn, r.IoU);
                if (r.IoU >= IoUGate) pass++;
                sb.AppendLine(string.Format(Inv, "| {0} | {1:0.0000}{2} | {3} | {4} | {15:0.0} | {5:0.0} | {6} | {7} | {8} | {9} | {10:0.0} | {11:0.0} | {12:0.0} ({16:0.0} / {17:0.0}) | {13:0.0} | {14} |",
                    r.Key, r.IoU, r.IoU >= IoUGate ? "" : " ❌", r.OnlyRef, r.OnlyUnity, r.S.WoodMs, r.S.WoodVerts, r.S.WoodTris, r.S.Limbs, r.S.Leaves, r.S.LeafMs,
                    r.S.UploadMs, r.S.BakeMs, r.RenderMs, r.S.PotTris, r.GenMs, r.S.BakeFillMs, r.S.BakeGpuMs));
            }
            sb.AppendLine();
            if (n > 0) sb.AppendLine(string.Format(Inv, "**IoU: мін {0:0.0000}, середнє {1:0.0000}; ≥ {2} — {3} з {4}.**", mn, sum / n, IoUGate, pass, n));
            sb.AppendLine();
            sb.AppendLine(depthReport);
            File.WriteAllText(path, sb.ToString());
            Log("results -> " + path);
        }

        private static void WriteSheets(string ws, string outDir, List<Row> rows)
        {
            var bg = new Color32(238, 236, 231, 255);
            var sep = new Color32(150, 146, 140, 255);
            var band = new Color32(96, 94, 90, 255);
            const int cw = W / 2, ch = H / 2, gap = 4, pad = 24;
            int pairW = cw * 2 + gap;
            foreach (string sp in Species)
            {
                var cv = new SpikeImages.Canvas(3 * pairW + 2 * pad, 3 * ch + 2 * pad, band);
                for (int si = 0; si < Styles.Length; si++)
                for (int ti = 0; ti < Ages.Length; ti++)
                    cv.Fill(ti * (pairW + pad), si * (ch + pad), pairW, ch, bg);
                for (int si = 0; si < Styles.Length; si++)
                for (int ti = 0; ti < Ages.Length; ti++)
                {
                    string key = sp + "_" + Styles[si] + "_s3_t" + Ages[ti];
                    int x = ti * (pairW + pad), y = si * (ch + pad);
                    PlacePair(cv, ws, outDir, key, x, y, 2, 0, 0, W, H, 1, gap);
                    cv.Fill(x + cw, y, gap, ch, sep);
                }
                SpikeImages.SavePng(Path.Combine(outDir, "sheet_" + sp + ".png"), cv.Px, cv.W, cv.H);
            }
            // Full-resolution pairs (moyogi, t = 1) per species.
            foreach (string sp in Species)
            {
                string key = sp + "_moyogi_s3_t100";
                var cv = new SpikeImages.Canvas(2 * W + gap, H, bg);
                PlacePair(cv, ws, outDir, key, 0, 0, 1, 0, 0, W, H, 1, gap);
                cv.Fill(W, 0, gap, H, sep);
                SpikeImages.SavePng(Path.Combine(outDir, "sheet_" + sp + "_moyogi_t100_full.png"), cv.Px, cv.W, cv.H);
            }
            // Close-ups 2x: trunk base / nebari and branch joints (moyogi, t = 1).
            CloseUp(ws, outDir, "sheet_closeup_base.png", 420, 640, 520, 300);
            CloseUp(ws, outDir, "sheet_closeup_joints.png", 440, 300, 480, 360);
            Log("sheets -> " + outDir);
        }

        private static void CloseUp(string ws, string outDir, string name, int cx, int cyTop, int cwid, int chei)
        {
            var bg = new Color32(238, 236, 231, 255);
            const int gap = 6, pad = 18;
            var cv = new SpikeImages.Canvas(2 * (cwid * 2) + gap, Species.Length * (chei * 2) + (Species.Length - 1) * pad, bg);
            for (int i = 0; i < Species.Length; i++)
            {
                string key = Species[i] + "_moyogi_s3_t100";
                PlacePair(cv, ws, outDir, key, 0, i * (chei * 2 + pad), 1, cx, cyTop, cwid, chei, 2, gap);
            }
            SpikeImages.SavePng(Path.Combine(outDir, name), cv.Px, cv.W, cv.H);
        }

        private static void PlacePair(SpikeImages.Canvas cv, string ws, string outDir, string key, int x, int top, int down, int cx, int cyTop, int cwid, int chei, int up, int gap)
        {
            string refPath = Path.Combine(ws, "refs", key + ".png"), uniPath = Path.Combine(outDir, key + ".png");
            int cellW = cwid / down * up;
            if (File.Exists(refPath))
            {
                Color32[] a = SpikeImages.LoadPng(refPath, out int aw, out int ah);
                cv.Blit(a, aw, ah, x, top, down, cx, cyTop, cwid, chei, up);
            }
            if (File.Exists(uniPath))
            {
                Color32[] b = SpikeImages.LoadPng(uniPath, out int bw, out int bh);
                cv.Blit(b, bw, bh, x + cellW + gap, top, down, cx, cyTop, cwid, chei, up);
            }
        }

        // ---------------------------------------------------------------- scene

        private static void MakeScene(string ws)
        {
            try
            {
                const string dir = "Assets/_Project/Spikes/PA102XS/Scenes";
                const string path = dir + "/PA102XS_Spike.unity";
                Directory.CreateDirectory(dir);
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var camGo = new GameObject("SketchCamera");
                var cam = camGo.AddComponent<Camera>();
                SketchCamera.Apply(cam);
                cam.backgroundColor = new Color(0.93f, 0.92f, 0.9f, 1f);
                var view = new GameObject("SketchTree").AddComponent<SketchTreeView>();
                view.sketchCamera = cam;
                EditorSceneManager.SaveScene(scene, path);
                string dst = Path.Combine(ws, "src", "Scenes");
                Directory.CreateDirectory(dst);
                File.Copy(path, Path.Combine(dst, "PA102XS_Spike.unity"), true);
                Log("scene -> " + path + " (copied to src/Scenes)");

                // Verify: reopen the saved scene and build the tree through the component, as the owner's editor will.
                var opened = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                SketchTreeView v = null;
                foreach (GameObject go in opened.GetRootGameObjects())
                {
                    v = go.GetComponentInChildren<SketchTreeView>();
                    if (v != null) break;
                }
                if (v == null) throw new InvalidOperationException("SketchTreeView not found in the reopened scene");
                if (v.sketchCamera == null) throw new InvalidOperationException("SketchTreeView.sketchCamera lost on save");
                v.Rebuild();
                Log("scene reopened OK: " + v.LastSource + " | " + v.LastStats);
            }
            catch (Exception e)
            {
                Debug.LogError("SPIKE| scene check FAILED: " + e);
            }
        }
    }
}
