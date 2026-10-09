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
using Debug = UnityEngine.Debug;

namespace PocketArbor.Spike.Editor
{
    /// <summary>
    /// PA-102X-S C6 (TA): parametric pot vs Surface Nets vs sketch, and LowEnd bark vs full bark.
    ///   unity-run.sh exec PocketArbor.Spike.Editor.SpikeC6.Run [-c6Parts pot,bark] [-c6Out c6_ta]
    /// Writes sheets + results_c6.md into .agent-workspace/pa102xs/unity/&lt;c6Out&gt;/.
    /// </summary>
    public static class SpikeC6
    {
        private const int W = 1360, H = 960, SS = 2;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly string[] PotKeys = { "momiji_moyogi_s3_t100", "yamazakura_moyogi_s3_t100", "kuromatsu_moyogi_s3_t100", "momiji_bunjin_s3_t100" };
        private static readonly string[] PotNames = { "токонаме (прямокутний)", "селадон (овальний)", "куро (прямокутний)", "bunjin (овальний мілкий)" };
        private static readonly string[] BarkKeys =
        {
            "kuromatsu_moyogi_s3_t100", "kuromatsu_chokkan_s3_t100", "kuromatsu_shakan_s3_t100", "kuromatsu_moyogi_s3_t60", "kuromatsu_moyogi_s3_t30",
            "momiji_moyogi_s3_t100", "yamazakura_moyogi_s3_t100"
        };

        private static void Log(string s) { Debug.Log("SPIKE-C6| " + s); }

        private static string Arg(string name)
        {
            string[] a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        public static void Run()
        {
            string parts = Arg("-c6Parts") ?? "pot,bark";
            string ws = GoldenFrameLoader.WorkspaceFolder() ?? throw new DirectoryNotFoundException("pa102xs workspace");
            string outDir = Path.Combine(ws, "unity", Arg("-c6Out") ?? "c6_ta");
            Directory.CreateDirectory(outDir);
            ShaderUtil.allowAsyncCompilation = false;
            foreach (string n in new[] { SketchTreeRenderer.WoodShader, SketchTreeRenderer.PotShader })
            {
                Shader s = Shader.Find(n);
                if (s == null || ShaderUtil.ShaderHasError(s))
                {
                    if (s != null)
                        foreach (ShaderMessage m in ShaderUtil.GetShaderMessages(s))
                            Debug.LogError("SPIKE-C6-SHADER| " + n + " [" + m.severity + "] " + m.file + ":" + m.line + " " + m.message + " " + m.messageDetails);
                    throw new InvalidOperationException("shader error / missing: " + n);
                }
                foreach (ShaderMessage m in ShaderUtil.GetShaderMessages(s))
                    Debug.LogWarning("SPIKE-C6-SHADER| " + n + " [" + m.severity + "] " + m.file + ":" + m.line + " " + m.message);
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("SketchCamera").AddComponent<Camera>();
            SketchCamera.Apply(cam);
            var rt = new RenderTexture(W * SS, H * SS, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1, hideFlags = HideFlags.HideAndDontSave };
            rt.Create();
            var read = new Texture2D(W * SS, H * SS, TextureFormat.RGBA32, false, false) { hideFlags = HideFlags.HideAndDontSave };
            var tree = new SketchTreeRenderer(null, HideFlags.None);
            var frame = new SketchFrame();
            var md = new StringBuilder();
            md.AppendLine("# PA-102X-S C6 — параметричний горщик і LowEnd-кора (редактор)");
            md.AppendLine();
            md.AppendLine("- " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", Inv) + ", Unity " + Application.unityVersion + ", " + SystemInfo.graphicsDeviceName + " (" + SystemInfo.graphicsDeviceType + "), batchmode; пресет деревини budget (за замовчуванням).");
            md.AppendLine("- Рендер 2720×1920 → 1360×960 (SSAA 2×2), як `SpikeHarness`. **Заміри часу — редактор (Mono), не мобільні.**");
            md.AppendLine();
            try
            {
                if (parts.Contains("pot")) PotPart(ws, outDir, cam, rt, read, tree, frame, md);
                if (parts.Contains("bark")) BarkPart(ws, outDir, cam, rt, read, tree, frame, md);
            }
            finally
            {
                File.WriteAllText(Path.Combine(outDir, "results_c6.md"), md.ToString());
                cam.targetTexture = null;
                tree.Dispose();
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(read);
                Shader.SetGlobalFloat("_SkBarkLodMat", 0f);
            }
            Log("done -> " + outDir);
        }

        // ================================================================== pot

        private sealed class PotVariant
        {
            public string Name;
            public bool Param, MeshNrm;
            public float Voxel;
        }

        private static void PotPart(string ws, string outDir, Camera cam, RenderTexture rt, Texture2D read, SketchTreeRenderer tree, SketchFrame frame, StringBuilder md)
        {
            var variants = new[]
            {
                new PotVariant { Name = "sn20", Param = false, Voxel = 2.0f },
                new PotVariant { Name = "sn15", Param = false, Voxel = 1.5f },
                new PotVariant { Name = "param", Param = true, MeshNrm = true },
                new PotVariant { Name = "param_sdfnrm", Param = true, MeshNrm = false },
            };
            var imgs = new Dictionary<string, Color32[]>();
            if (float.TryParse(Arg("-c6Bevel") ?? "", NumberStyles.Float, Inv, out float bevel)) tree.PotBuilder.Bevel = bevel;
            md.AppendLine("Фаска параметричного горщика `Bevel` = " + tree.PotBuilder.Bevel.ToString("0.###", Inv) + ".");
            md.AppendLine();
            md.AppendLine("## Горщик: Surface Nets проти параметричного");
            md.AppendLine();
            md.AppendLine("IoU — маски альфи ≥ 128 проти `refs/`: **кадр** — усе зображення; **горщик** — смуга кадру нижче переднього краю губи (лише горщик: стінка, губа спереду, ніжки). ΔRGB — середня |Δ| (0–255) по пікселях, непрозорих в обох, у смузі горщика. Горщик: трикутники / вершини меша; «меш + запікання» — `Stats.PotMs` у синхронному `Show` (побудова меша, заповнення входів, GPU-запікання з синхронізацією).");
            md.AppendLine();
            md.AppendLine("| Конфігурація | варіант | IoU кадру | IoU горщика | ΔRGB горщика | трикутн. | вершин | меш + запікання, мс |");
            md.AppendLine("|---|---|---|---|---|---|---|---|");
            for (int k = 0; k < PotKeys.Length; k++)
            {
                string key = PotKeys[k];
                Color32[] refPx = SpikeImages.LoadPng(Path.Combine(ws, "refs", key + ".png"), out _, out _);
                LoadFrame(key, frame);
                SpeciesLook look = SpeciesLook.For(frame.Species, frame.Style);
                int bandTop = PotBandTop(look);
                foreach (PotVariant v in variants)
                {
                    tree.PotVoxel = v.Param ? 2f : v.Voxel;
                    tree.ParametricPot = !v.Param; // force a pot rebuild even when the mode stays the same
                    tree.ParametricPot = v.Param;
                    tree.PotMeshNormals = v.MeshNrm;
                    SketchTreeRenderer.Stats st = tree.Show(frame);
                    Color32[] img = Render(cam, rt, read);
                    imgs[key + "|" + v.Name] = img;
                    SpikeImages.SavePng(Path.Combine(outDir, key + "_" + v.Name + ".png"), img, W, H);
                    float iou = SpikeImages.AlphaIoU(refPx, img, out _, out _, out _, out _);
                    float iouPot = BandIoU(refPx, img, bandTop, out double dRgb);
                    md.AppendLine(string.Format(Inv, "| {0} | {1} | {2:0.0000} | {3:0.0000} | {4:0.00} | {5} | {6} | {7:0.0} |",
                        key, v.Name, iou, iouPot, dRgb, st.PotTris, st.PotVerts, st.PotMs));
                    Log(string.Format(Inv, "{0} {1}: IoU {2:0.0000} pot {3:0.0000} dRGB {4:0.00} tris {5} verts {6} build {7:0.00} ms", key, v.Name, iou, iouPot, dRgb, st.PotTris, st.PotVerts, st.PotMs));
                }
            }
            tree.ParametricPot = true;
            tree.PotMeshNormals = true;
            md.AppendLine();
            md.AppendLine("`sn20` — Surface Nets, воксель 2.0 (фаза 2c), `sn15` — воксель 1.5 (фаза 1 quality), `param` — C6, нормаль меша (за замовчуванням), `param_sdfnrm` — C6 з попіксельною нормаллю potD (як Surface Nets).");
            md.AppendLine();

            PotSheets(ws, outDir, imgs);
            PotTiming(md);
        }

        /// <summary>First image row (top-based, 1360x960) below the front lip's top edge: only the pot from there down.</summary>
        private static int PotBandTop(SpeciesLook s)
        {
            float sy = SketchConst.GY - (4.5f * SketchConst.CE - (s.PotHd + 4.9f) * SketchConst.SE) + 1.5f;
            return Mathf.CeilToInt(sy * 2f);
        }

        private static float BandIoU(Color32[] a, Color32[] b, int topRow, out double dRgb)
        {
            int inter = 0, union = 0, n = 0;
            double sum = 0;
            for (int yTop = topRow; yTop < H; yTop++)
            {
                int row = (H - 1 - yTop) * W;
                for (int x = 0; x < W; x++)
                {
                    Color32 p = a[row + x], q = b[row + x];
                    bool ia = p.a >= 128, ib = q.a >= 128;
                    if (ia && ib)
                    {
                        inter++;
                        sum += (Math.Abs(p.r - q.r) + Math.Abs(p.g - q.g) + Math.Abs(p.b - q.b)) / 3.0;
                        n++;
                    }
                    if (ia || ib) union++;
                }
            }
            dRgb = n > 0 ? sum / n : 0;
            return union == 0 ? 1f : (float)inter / union;
        }

        private static void PotSheets(string ws, string outDir, Dictionary<string, Color32[]> imgs)
        {
            var bg = new Color32(238, 236, 231, 255);
            // Sheet 1: pot crops 1x, columns sketch | Surface Nets (2.0) | parametric | alpha diff (sketch vs parametric).
            const int cx = 380, cy = 726, cw = 600, ch = 234, gap = 8, pad = 16;
            var cv = new SpikeImages.Canvas(4 * cw + 3 * gap, PotKeys.Length * (ch + pad) - pad, bg);
            for (int k = 0; k < PotKeys.Length; k++)
            {
                string key = PotKeys[k];
                Color32[] refPx = SpikeImages.LoadPng(Path.Combine(ws, "refs", key + ".png"), out _, out _);
                int top = k * (ch + pad);
                cv.Blit(refPx, W, H, 0, top, 1, cx, cy, cw, ch);
                cv.Blit(imgs[key + "|sn20"], W, H, cw + gap, top, 1, cx, cy, cw, ch);
                cv.Blit(imgs[key + "|param"], W, H, 2 * (cw + gap), top, 1, cx, cy, cw, ch);
                cv.Blit(SpikeImages.MaskDiff(refPx, imgs[key + "|param"]), W, H, 3 * (cw + gap), top, 1, cx, cy, cw, ch);
            }
            SpikeImages.SavePng(Path.Combine(outDir, "sheet_pot_sketch_sn_param.png"), cv.Px, cv.W, cv.H);

            // Sheet 2: base close-up 2x (trunk base, lip, wall, feet), columns sketch | Surface Nets | parametric.
            const int bx = 380, by = 700, bw = 380, bh = 260, up = 2;
            var cb = new SpikeImages.Canvas(3 * bw * up + 2 * gap, PotKeys.Length * (bh * up + pad) - pad, bg);
            for (int k = 0; k < PotKeys.Length; k++)
            {
                string key = PotKeys[k];
                Color32[] refPx = SpikeImages.LoadPng(Path.Combine(ws, "refs", key + ".png"), out _, out _);
                int top = k * (bh * up + pad);
                cb.Blit(refPx, W, H, 0, top, 1, bx, by, bw, bh, up);
                cb.Blit(imgs[key + "|sn20"], W, H, bw * up + gap, top, 1, bx, by, bw, bh, up);
                cb.Blit(imgs[key + "|param"], W, H, 2 * (bw * up + gap), top, 1, bx, by, bw, bh, up);
            }
            SpikeImages.SavePng(Path.Combine(outDir, "sheet_pot_closeup_base.png"), cb.Px, cb.W, cb.H);

            // Sheet 3: full frames sketch | parametric (the whole picture, 1/2 scale).
            var cf = new SpikeImages.Canvas(2 * (W / 2) + gap, PotKeys.Length * (H / 2 + pad) - pad, bg);
            for (int k = 0; k < PotKeys.Length; k++)
            {
                string key = PotKeys[k];
                Color32[] refPx = SpikeImages.LoadPng(Path.Combine(ws, "refs", key + ".png"), out _, out _);
                int top = k * (H / 2 + pad);
                cf.Blit(refPx, W, H, 0, top, 2);
                cf.Blit(imgs[key + "|param"], W, H, W / 2 + gap, top, 2);
            }
            SpikeImages.SavePng(Path.Combine(outDir, "sheet_pot_full.png"), cf.Px, cf.W, cf.H);
            Log("pot sheets -> " + outDir);
        }

        private static void PotTiming(StringBuilder md)
        {
            md.AppendLine("### Будівник (`ParametricPotBuilder`), редактор");
            md.AppendLine();
            md.AppendLine("| Горщик | кілець × відліків | трикутн. | вершин | медіана, мс | max, мс | алокації за 200 побудов, Б | Surface Nets 2.0 / 1.5, мс | вершини на potD = 0 (\\|d\\| ≤ 0.01) | max d зовні (без дна порожнини) | детермінізм |");
            md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
            var looks = new[] { SpeciesLook.For("momiji", "moyogi"), SpeciesLook.For("yamazakura", "moyogi"), SpeciesLook.For("kuromatsu", "moyogi"), SpeciesLook.For("momiji", "bunjin") };
            var b = new ParametricPotBuilder();
            var g = new GeoBuffer(1 << 13, 1 << 15);
            var g2 = new GeoBuffer(1 << 13, 1 << 15);
            var times = new double[200];
            for (int k = 0; k < looks.Length; k++)
            {
                SpeciesLook s = looks[k];
                for (int i = 0; i < 5; i++) { g.Clear(); b.Build(s, g); } // warm-up (JIT, scratch growth)
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < times.Length; i++)
                {
                    g.Clear();
                    long t0 = Stopwatch.GetTimestamp();
                    b.Build(s, g);
                    times[i] = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                }
                long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
                Array.Sort(times);
                // Determinism: a second builder instance gives bit-identical buffers.
                g2.Clear();
                new ParametricPotBuilder().Build(s, g2);
                bool same = g.VCount == g2.VCount && g.ICount == g2.ICount;
                for (int i = 0; same && i < g.VCount; i++) same = g.Pos[i] == g2.Pos[i] && g.Nrm[i] == g2.Nrm[i];
                for (int i = 0; same && i < g.ICount; i++) same = g.Idx[i] == g2.Idx[i];
                // potD on the vertices.
                var sdf = new PotSdf(s);
                int on = 0;
                float maxOut = 0f;
                for (int i = 0; i < g.VCount; i++)
                {
                    Vector3 p = g.Pos[i];
                    float d = sdf.Eval(p.x, p.y, p.z);
                    if (Mathf.Abs(d) <= 0.01f) on++;
                    bool floor = Mathf.Abs(p.y + 1.5f) < 1e-3f && sdf.Inner(p.x, p.y, p.z) < 0.01f && g.Nrm[i].y > 0.99f;
                    if (!floor && d > maxOut) maxOut = d;
                }
                // Surface Nets reference timings (worker code path, 1 run each after the JIT warm-up above).
                double sn20 = SnMs(s, 2.0f), sn15 = SnMs(s, 1.5f);
                md.AppendLine(string.Format(Inv, "| {0} | {1} | {2} | {3} | {4:0.000} | {5:0.000} | {6} | {7:0} / {8:0} | {9} / {10} | {11:0.0000} | {12} |",
                    PotNames[k], b.RingSamples, b.Triangles, b.Vertices, times[times.Length / 2], times[times.Length - 1], alloc, sn20, sn15, on, g.VCount, maxOut, same ? "так" : "НІ"));
                Log(string.Format(Inv, "builder {0}: {1} tris {2} verts median {3:0.000} ms max {4:0.000} alloc {5} B, SN {6:0}/{7:0} ms, on-surface {8}/{9}, maxOut {10:0.0000}, det {11}",
                    PotNames[k], b.Triangles, b.Vertices, times[times.Length / 2], times[times.Length - 1], alloc, sn20, sn15, on, g.VCount, maxOut, same));
            }
            md.AppendLine();
            md.AppendLine("Вершини не на potD = 0 — сховані: верх ніжок усередині тіла, дно порожнини під ґрунтом (y = −1.5, навмисно зовні тіла).");
            md.AppendLine();
        }

        private static double SnMs(SpeciesLook look, float voxel)
        {
            var g = new GeoBuffer(1 << 15, 1 << 17);
            var sdf = new PotSdf(look);
            float hw = look.PotHw + 8f, hd = look.PotHd + 8f;
            Vector3 min = new Vector3(-hw, -look.PotDep - 9f, -hd), max = new Vector3(hw, 6.5f, hd);
            var sw = Stopwatch.StartNew();
            SurfaceNets.Build(sdf, min, max, voxel, g, 2f, p => p.y < -1f && sdf.Inner(p.x, p.y, p.z) < 0.6f);
            return sw.Elapsed.TotalMilliseconds;
        }

        // ================================================================== bark

        private static void BarkPart(string ws, string outDir, Camera cam, RenderTexture rt, Texture2D read, SketchTreeRenderer tree, SketchFrame frame, StringBuilder md)
        {
            float lod = 0.4f;
            float.TryParse(Arg("-c6BarkLod") ?? "0.4", NumberStyles.Float, Inv, out lod);
            md.AppendLine("## Кора: повна проти LowEnd (`SK_BARK_LOW`)");
            md.AppendLine();
            md.AppendLine("|ΔRGB| — середня по каналах (0–255). **Деревина** — рендер лише деревини+ґрунту (листя й горщик вимкнені), пікселі, непрозорі в обох; **кадр** — повний кадр (з листям і горщиком). `low` — LowEnd без LOD, `low+lod` — LowEnd + LOD тонких гілок (`_SkBarkLodMat` = " + lod.ToString("0.##", Inv) + ").");
            md.AppendLine();
            md.AppendLine("| Конфігурація | варіант | деревина: сер. \\|Δ\\| | p95 | > 8, % | > 16, % | кадр: сер. \\|Δ\\| | > 16, % |");
            md.AppendLine("|---|---|---|---|---|---|---|---|");
            tree.ParametricPot = true;
            tree.PotMeshNormals = true;
            var full = new Dictionary<string, Color32[]>();
            var low = new Dictionary<string, Color32[]>();
            foreach (string key in BarkKeys)
            {
                LoadFrame(key, frame);
                tree.Show(frame);
                Color32[] fFull = null, fWood = null;
                for (int v = 0; v < 3; v++)
                {
                    bool isLow = v > 0;
                    if (v == 2 && !key.StartsWith("kuromatsu", StringComparison.Ordinal)) break;
                    tree.LowEndBark = isLow;
                    Shader.SetGlobalFloat("_SkBarkLodMat", v == 2 ? lod : 0f);
                    tree.Leaves.enabled = true;
                    tree.Pot.enabled = true;
                    Color32[] img = Render(cam, rt, read);
                    tree.Leaves.enabled = false;
                    tree.Pot.enabled = false;
                    Color32[] wood = Render(cam, rt, read);
                    tree.Leaves.enabled = true;
                    tree.Pot.enabled = true;
                    string vn = v == 0 ? "full" : v == 1 ? "low" : "low+lod";
                    SpikeImages.SavePng(Path.Combine(outDir, "bark_" + key + "_" + vn.Replace('+', '_') + ".png"), img, W, H);
                    if (v == 0)
                    {
                        fFull = img;
                        fWood = wood;
                        full[key] = img;
                        continue;
                    }
                    if (v == 1) low[key] = img;
                    Diff(fWood, wood, out double mw, out double p95, out double o8, out double o16);
                    Diff(fFull, img, out double mf, out _, out _, out double of16);
                    md.AppendLine(string.Format(Inv, "| {0} | {1} | {2:0.00} | {3:0} | {4:0.00} | {5:0.00} | {6:0.00} | {7:0.00} |", key, vn, mw, p95, o8, o16, mf, of16));
                    Log(string.Format(Inv, "bark {0} {1}: wood mean {2:0.00} p95 {3:0} >8 {4:0.00}% >16 {5:0.00}% | frame {6:0.00}", key, vn, mw, p95, o8, o16, mf));
                }
            }
            tree.LowEndBark = false;
            Shader.SetGlobalFloat("_SkBarkLodMat", 0f);
            md.AppendLine();
            BarkSheets(ws, outDir, full, low);
        }

        private static void Diff(Color32[] a, Color32[] b, out double mean, out double p95, out double over8, out double over16)
        {
            var hist = new int[256];
            int n = 0;
            double sum = 0;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].a < 128 || b[i].a < 128) continue;
                int d = (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) + 1) / 3;
                hist[d]++;
                sum += (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b)) / 3.0;
                n++;
            }
            mean = n > 0 ? sum / n : 0;
            int acc = 0, c8 = 0, c16 = 0;
            p95 = 0;
            bool got = false;
            for (int d = 0; d < 256; d++)
            {
                acc += hist[d];
                if (!got && acc >= 0.95 * n) { p95 = d; got = true; }
                if (d > 8) c8 += hist[d];
                if (d > 16) c16 += hist[d];
            }
            over8 = n > 0 ? 100.0 * c8 / n : 0;
            over16 = n > 0 ? 100.0 * c16 / n : 0;
        }

        private static void BarkSheets(string ws, string outDir, Dictionary<string, Color32[]> full, Dictionary<string, Color32[]> low)
        {
            var bg = new Color32(238, 236, 231, 255);
            const int gap = 8, pad = 16;
            // Close-up 2x of the trunk: sketch | full | LowEnd | |Δ| x4 — Kuromatsu t=1 (3 styles) + Momiji + Yamazakura.
            string[] keys = { "kuromatsu_moyogi_s3_t100", "kuromatsu_chokkan_s3_t100", "kuromatsu_moyogi_s3_t60", "momiji_moyogi_s3_t100", "yamazakura_moyogi_s3_t100" };
            const int bx = 480, by = 540, bw = 360, bh = 300, up = 2;
            var cv = new SpikeImages.Canvas(4 * bw * up + 3 * gap, keys.Length * (bh * up + pad) - pad, bg);
            for (int k = 0; k < keys.Length; k++)
            {
                string key = keys[k];
                if (!full.ContainsKey(key) || !low.ContainsKey(key)) continue;
                Color32[] refPx = SpikeImages.LoadPng(Path.Combine(ws, "refs", key + ".png"), out _, out _);
                int top = k * (bh * up + pad);
                cv.Blit(refPx, W, H, 0, top, 1, bx, by, bw, bh, up);
                cv.Blit(full[key], W, H, bw * up + gap, top, 1, bx, by, bw, bh, up);
                cv.Blit(low[key], W, H, 2 * (bw * up + gap), top, 1, bx, by, bw, bh, up);
                cv.Blit(AbsDiff(full[key], low[key], 4), W, H, 3 * (bw * up + gap), top, 1, bx, by, bw, bh, up);
            }
            SpikeImages.SavePng(Path.Combine(outDir, "sheet_bark_closeup.png"), cv.Px, cv.W, cv.H);
            // Full frames 1/2: full | LowEnd for Kuromatsu t=1 moyogi and t=0.6.
            string[] fk = { "kuromatsu_moyogi_s3_t100", "kuromatsu_moyogi_s3_t60" };
            var cf = new SpikeImages.Canvas(2 * (W / 2) + gap, fk.Length * (H / 2 + pad) - pad, bg);
            for (int k = 0; k < fk.Length; k++)
            {
                int top = k * (H / 2 + pad);
                cf.Blit(full[fk[k]], W, H, 0, top, 2);
                cf.Blit(low[fk[k]], W, H, W / 2 + gap, top, 2);
            }
            SpikeImages.SavePng(Path.Combine(outDir, "sheet_bark_full_vs_low.png"), cf.Px, cf.W, cf.H);
            Log("bark sheets -> " + outDir);
        }

        /// <summary>|a - b| x gain as an opaque grey image (white = no difference).</summary>
        private static Color32[] AbsDiff(Color32[] a, Color32[] b, int gain)
        {
            var o = new Color32[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].a < 128 && b[i].a < 128) { o[i] = new Color32(255, 255, 255, 255); continue; }
                int d = (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b)) / 3 * gain;
                byte v = (byte)Math.Max(0, 255 - Math.Min(255, d));
                o[i] = new Color32(v, v, v, 255);
            }
            return o;
        }

        // ================================================================== helpers

        private static void LoadFrame(string key, SketchFrame frame)
        {
            string[] p = key.Split('_');
            int seed = int.Parse(p[2].Substring(1), Inv);
            float t = int.Parse(p[3].Substring(1), Inv) / 100f;
            TreeGenAdapter.Generate(p[0], p[1], seed, t, frame);
        }

        private static Color32[] Render(Camera cam, RenderTexture rt, Texture2D read)
        {
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            read.Apply(false);
            RenderTexture.active = prev;
            return SpikeImages.Downsample(read.GetPixels32(), W * SS, H * SS, SS);
        }
    }
}
