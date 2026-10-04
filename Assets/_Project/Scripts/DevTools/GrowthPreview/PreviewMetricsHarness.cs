using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Tree;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>
    /// PA-102P measurement harness (AC1–AC8, AC19–AC21): renders the Textured preview in the reference frame (z = 1, pot-aligned,
    /// 768×1376, TD-028), builds the tree mask (bark + roots + pads, α ≥ 0.5, y &lt; 1068 px from the top) and the foliage mask, and
    /// writes metrics.csv / summary.txt plus the comparison PNGs. Batch:
    /// <c>-executeMethod PocketArbor.DevTools.GrowthPreview.PreviewMetricsHarness.RunBatch -metricsOut &lt;dir&gt; [-seeds 30] [-noImages]</c>
    /// (Metal, without -nographics). Dev-only: allocations are fine.
    /// </summary>
    public static class PreviewMetricsHarness
    {
        public const int FrameWidth = 768;
        public const int FrameHeight = 1376;
        public const int PotTopPx = 1068;
        public const uint FixtureSeed = 20261004u;
        public const string MaskL2 = PreviewArtLibrary.DefaultRoot + "/ref_mask_l2_momiji.png";
        public const string MaskNight = PreviewArtLibrary.DefaultRoot + "/ref_mask_night_kuromatsu.png";
        public const string NightReferencePath = "Assets/_Project/Art/Source/A_Concept/concept_night.jpg";

        static readonly int[] StyledDays = { 20, 40, 60, 80, 90, 100, 110, 120 };
        static readonly int[] AcDays = { 80, 90, 100, 110, 120 };
        static readonly SpeciesId[] AllSpecies = { SpeciesId.Momiji, SpeciesId.Kuromatsu, SpeciesId.Yamazakura };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>When set, foliage components of measured frames are listed here (fixture debugging).</summary>
        static StringBuilder Components;

        // BALANCE §1.3.1 «Вимір вигинів».
        const int BendSamples = 64;
        const float BendMinCurvature = 0.002f;
        const float BendMinArc = 8f;

        /// <summary>One measured frame.</summary>
        public sealed class Row
        {
            public SpeciesId Species;
            public bool Styled;
            public uint Seed;
            public int Day;
            public int Nodes;
            public float IoU, IoUDirect, IoUMirror;
            public bool Mirror;
            public float HP, WP, Th0H, FlareH;
            public int Tiers;
            public bool TiersAlternate;
            public int FoliageComponents;
            public int BendChanges;
            public float BendRangeH;
            public float DomeWH, DomeHH;
            public int Tips, TipsCovered;
            public int TwigSamples, TwigOutside;
            public int Roots, RootsLeft, RootsRight;
            public float RootEndRatio;
            public float Zoom;
        }

        public static void RunBatch()
        {
            int exitCode = 0;
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                string outDir = Arg(args, "-metricsOut") ?? "Temp/pa102p-metrics";
                int seeds = int.TryParse(Arg(args, "-seeds"), out int n) ? n : 30;
                bool images = Array.IndexOf(args, "-noImages") < 0;
                Overrides = Arg(args, "-cfg");
                Run(outDir, seeds, images);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                exitCode = 1;
            }
            EditorApplication.Exit(exitCode);
        }

        public static List<Row> Run(string outDir, int seeds, bool images)
        {
            Directory.CreateDirectory(outDir);
            EditorSceneManager.OpenScene(GrowthPreviewEditorTools.ScenePath, OpenSceneMode.Single);
            var controller = UnityEngine.Object.FindFirstObjectByType<GrowthPreviewController>();
            if (controller == null) throw new InvalidOperationException("GrowthPreviewController not found");
            Camera camera = controller.TargetCamera != null ? controller.TargetCamera : Camera.main;
            controller.SetTextured(true);
            controller.ReloadArt();
            controller.SetReferenceOverlay(false, 0.5f);
            controller.SetOnboardingCut(true);
            ApplyOverrides(controller);

            bool[] l2 = LoadMask(MaskL2);
            bool[] night = LoadMask(MaskNight);
            if (l2 == null) throw new FileNotFoundException("reference mask missing", MaskL2);

            var rows = new List<Row>();
            var components = new StringBuilder();
            var csv = new StringBuilder("species,mode,seed,day,nodes,iou,iou_direct,iou_mirror,mirror,H_P,W_P,th0_H,flare_H,tiers,tiers_alternate,"
                + "foliage_components,bend_sign_changes,bend_range_H,dome_W_H,dome_H_H,tips,tips_covered,twig_samples,twig_outside,roots,roots_left,roots_right,root_end_ratio,zoom\n");

            foreach (SpeciesId species in AllSpecies)
            {
                bool[] reference = species == SpeciesId.Kuromatsu && night != null ? night : l2;
                controller.SetSpecies(species);
                foreach (bool styled in new[] { true, false })
                {
                    controller.SetStyled(styled);
                    for (int s = 0; s <= seeds; s++)
                    {
                        uint seed = s == 0 ? FixtureSeed : (uint)s;
                        controller.SetSeed(seed);
                        int[] days = styled ? StyledDays : new[] { 20, 80 };
                        foreach (int day in days)
                        {
                            controller.SetDay(day);
                            if (s == 0 && styled && day >= 80)
                            {
                                Components = components;
                                components.Append(species).Append(" D").Append(day).Append('\n');
                            }
                            else Components = null;
                            Row row = Measure(controller, camera, reference, species, styled, seed, day, out bool[] ours);
                            rows.Add(row);
                            Append(csv, row);
                            if (images && s == 0 && styled && (day == 80 || day == 120))
                                SaveOverlay(Path.Combine(outDir, "overlay_" + species + "_d" + day + ".png"), ours, reference, row.Mirror);
                        }
                    }
                }
            }
            Components = null;
            File.WriteAllText(Path.Combine(outDir, "components_fixture.txt"), components.ToString());
            File.WriteAllText(Path.Combine(outDir, "metrics.csv"), csv.ToString());
            string summary = Summarize(rows);
            File.WriteAllText(Path.Combine(outDir, "summary.txt"), summary);
            Debug.Log("[PA-102P metrics]\n" + summary);

            if (images) CaptureImages(controller, camera, outDir);
            return rows;
        }

        /// <summary>Tuning experiments: "-cfg Field=value,…" sets public fields of GrowthConfig / SpeciesConfig (all species) / StyleConfig.</summary>
        static string Overrides;

        static void ApplyOverrides(GrowthPreviewController controller)
        {
            if (string.IsNullOrEmpty(Overrides)) return;
            GrowthPreviewModel model = controller.Model;
            foreach (string pair in Overrides.Split(','))
            {
                string[] kv = pair.Split('=');
                if (kv.Length != 2) continue;
                if (kv[0] == "PuffRadiusScale")
                {
                    FoliageConfig.PuffRadiusScale = float.Parse(kv[1], Inv);
                    continue;
                }
                bool done = Set(model.Config, kv[0], kv[1]) | Set(model.Style, kv[0], kv[1]);
                foreach (SpeciesId species in AllSpecies)
                {
                    controller.SetSpecies(species);
                    done |= Set(model.SpeciesConfig, kv[0], kv[1]);
                }
                if (!done) throw new ArgumentException("unknown config field " + kv[0]);
                Debug.Log("[PA-102P metrics] override " + pair);
            }
        }

        static bool Set(object target, string field, string value)
        {
            var f = target.GetType().GetField(field);
            if (f == null) return false;
            f.SetValue(target, Convert.ChangeType(double.Parse(value, Inv), f.FieldType, Inv));
            return true;
        }

        // ---- One frame -------------------------------------------------------------------------------

        static Row Measure(GrowthPreviewController controller, Camera camera, bool[] reference, SpeciesId species, bool styled, uint seed, int day, out bool[] tree)
        {
            TreePreviewMeshBuilder builder = controller.Builder;
            TexturedPreviewView view = controller.TexturedView;
            float zoom = controller.FramedZoom;
            Rect frame = ReferenceFrame(controller, camera);

            view.SetTreeOnly(true);
            Color32[] all = RenderPixels(camera, true);
            view.SetTreeOnly(true, showBark: false, showPads: true);
            Color32[] pads = RenderPixels(camera, true);
            view.SetTreeOnly(false);
            controller.FrameReference(camera);

            tree = Mask(all);
            bool[] foliage = Mask(pads);
            float direct = IoU(tree, reference, false);
            float mirror = IoU(tree, reference, true);

            var row = new Row
            {
                Species = species, Styled = styled, Seed = seed, Day = day, Nodes = builder.NodeCount, Zoom = zoom,
                IoUDirect = direct, IoUMirror = mirror, IoU = Mathf.Max(direct, mirror), Mirror = mirror > direct,
            };

            float p = builder.LastPotWidth;
            float h = Mathf.Max(1e-3f, builder.LastHeight);
            row.HP = builder.LastHeight / p;
            row.WP = builder.LastCrownWidth / p;
            row.Th0H = builder.LastTrunkBase / h;
            row.FlareH = builder.LastTrunkBaseFlared / h;

            TierStats(builder, out row.Tiers, out row.TiersAlternate);
            FoliageComponents(foliage, tree, h, frame, out row.FoliageComponents, out row.DomeWH, out row.DomeHH);
            Bends(builder, h, out row.BendChanges, out row.BendRangeH);
            Coverage(builder, foliage, frame, out row.Tips, out row.TipsCovered, out row.TwigSamples, out row.TwigOutside);
            Roots(builder, seed, out row.Roots, out row.RootsLeft, out row.RootsRight, out row.RootEndRatio);
            return row;
        }

        /// <summary>World rect of the 768×1376 reference frame (pot-aligned as the overlay, TD-028); the camera is set to it.</summary>
        static Rect ReferenceFrame(GrowthPreviewController controller, Camera camera)
        {
            if (!controller.FrameReference(camera))
            {
                // No reference jpg: the z = 1 pot frame of TD-030 (same alignment up to the pot sprite's own symmetry).
                Rect r = PotFraming.FrameAt(1f, controller.Builder.LastPotWidth, controller.TexturedView.PotRect.yMax);
                camera.orthographic = true;
                camera.orthographicSize = 0.5f * r.height;
                camera.transform.position = new Vector3(r.center.x, r.center.y, -100f);
                return r;
            }
            return controller.TexturedView.OverlayRect;
        }

        static Color32[] RenderPixels(Camera camera, bool transparent)
        {
            CameraClearFlags flags = camera.clearFlags;
            Color background = camera.backgroundColor;
            var rt = new RenderTexture(FrameWidth, FrameHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            var readback = new Texture2D(FrameWidth, FrameHeight, TextureFormat.RGBA32, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                if (transparent)
                {
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                }
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                readback.ReadPixels(new Rect(0, 0, FrameWidth, FrameHeight), 0, 0);
                readback.Apply();
                return readback.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                camera.clearFlags = flags;
                camera.backgroundColor = background;
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(readback);
            }
        }

        /// <summary>α ≥ 0.5 in the zone above the pot top (rows are bottom-up: y_from_top &lt; 1068 ⇔ row ≥ 1376 − 1068).</summary>
        static bool[] Mask(Color32[] pixels)
        {
            var mask = new bool[FrameWidth * FrameHeight];
            int minRow = FrameHeight - PotTopPx;
            for (int y = minRow; y < FrameHeight; y++)
            {
                for (int x = 0; x < FrameWidth; x++)
                {
                    int i = y * FrameWidth + x;
                    mask[i] = pixels[i].a >= 128;
                }
            }
            return mask;
        }

        static bool[] LoadMask(string path)
        {
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!tex.LoadImage(File.ReadAllBytes(path)) || tex.width != FrameWidth || tex.height != FrameHeight) return null;
                Color32[] px = tex.GetPixels32();
                var mask = new bool[px.Length];
                int minRow = FrameHeight - PotTopPx;
                for (int i = 0; i < px.Length; i++) mask[i] = i / FrameWidth >= minRow && px[i].r > 127;
                return mask;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        /// <summary>|A ∩ B| / |A ∪ B|; <paramref name="mirror"/> flips A about the frame centre (= pot centre, TA).</summary>
        public static float IoU(bool[] a, bool[] b, bool mirror)
        {
            long inter = 0, union = 0;
            for (int y = 0; y < FrameHeight; y++)
            {
                int row = y * FrameWidth;
                for (int x = 0; x < FrameWidth; x++)
                {
                    bool va = a[row + (mirror ? FrameWidth - 1 - x : x)];
                    bool vb = b[row + x];
                    if (va && vb) inter++;
                    if (va || vb) union++;
                }
            }
            return union == 0 ? 0f : inter / (float)union;
        }

        // ---- Shape metrics ---------------------------------------------------------------------------

        static void TierStats(TreePreviewMeshBuilder b, out int tiers, out bool alternate)
        {
            var list = new List<KeyValuePair<float, int>>();
            TreeShapeAnalysis shape = b.Shape;
            for (int i = 1; i < b.NodeCount; i++)
            {
                NodeData n = b.Node(i);
                if (!shape.IsPrimary(i) || n.Status == NodeStatus.Bud) continue;
                list.Add(new KeyValuePair<float, int>(b.PointOn(n.ParentIndex, (float)n.AttachT).y, n.BranchSide));
            }
            list.Sort((x, y) => x.Key.CompareTo(y.Key));
            tiers = list.Count;
            alternate = true;
            for (int k = 1; k < list.Count; k++) alternate &= list[k].Value != list[k - 1].Value;
        }

        /// <summary>Components of the foliage mask with area ≥ 0.5% of the tree mask; the top one (dome) → width / H and height / H.</summary>
        static void FoliageComponents(bool[] foliage, bool[] tree, float h, Rect frame, out int count, out float domeW, out float domeH)
        {
            long treeArea = 0;
            for (int i = 0; i < tree.Length; i++) if (tree[i]) treeArea++;
            long minArea = (long)(0.005 * treeArea);
            var label = new int[foliage.Length];
            var stack = new Stack<int>();
            count = 0;
            int bestTop = -1, bestMinX = 0, bestMaxX = 0, bestMinY = 0;
            int next = 0;
            for (int start = 0; start < foliage.Length; start++)
            {
                if (!foliage[start] || label[start] != 0) continue;
                next++;
                label[start] = next;
                stack.Push(start);
                long area = 0;
                int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    area++;
                    int x = i % FrameWidth, y = i / FrameWidth;
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    if (x > 0) Visit(i - 1);
                    if (x < FrameWidth - 1) Visit(i + 1);
                    if (y > 0) Visit(i - FrameWidth);
                    if (y < FrameHeight - 1) Visit(i + FrameWidth);
                }
                if (area < minArea) continue;
                count++;
                if (Components != null)
                {
                    float u = frame.width / FrameWidth;
                    // centre x in units from the frame centre (pot centre), centre y / H above the pot top, size in units
                    Components.AppendFormat(Inv, "  pad x {0,6:0.0} u  yc/H {1:0.000}  w {2:0.0} × h {3:0.0} u\n",
                        ((minX + maxX) * 0.5f - FrameWidth * 0.5f) * u, ((minY + maxY) * 0.5f - (FrameHeight - PotTopPx)) * u / h,
                        (maxX - minX + 1) * u, (maxY - minY + 1) * u);
                }
                if (maxY > bestTop)
                {
                    bestTop = maxY; bestMinX = minX; bestMaxX = maxX; bestMinY = minY;
                }

                void Visit(int j)
                {
                    if (!foliage[j] || label[j] != 0) return;
                    label[j] = next;
                    stack.Push(j);
                }
            }
            float upp = frame.width / FrameWidth;
            domeW = bestTop < 0 ? 0f : (bestMaxX - bestMinX + 1) * upp / h;
            domeH = bestTop < 0 ? 0f : (bestTop - bestMinY + 1) * upp / h;
        }

        /// <summary>BALANCE §1.3.1: 64 arc-uniform samples of the trunk-chain C1 axis, signed curvature sign changes with |κ| ≥ 0.002
        /// and ≥ 8 units of arc between changes; range = max − min deviation from the base→top chord, / H.</summary>
        public static void Bends(TreePreviewMeshBuilder b, float h, out int changes, out float rangeH)
        {
            var dense = new List<Vector2>();
            b.TrunkAxis(dense);
            changes = 0;
            rangeH = 0f;
            if (dense.Count < 3) return;
            var arc = new float[dense.Count];
            for (int i = 1; i < dense.Count; i++) arc[i] = arc[i - 1] + (dense[i] - dense[i - 1]).magnitude;
            float total = arc[dense.Count - 1];
            if (total <= 1e-3f) return;
            var pts = new Vector2[BendSamples];
            int j = 1;
            for (int k = 0; k < BendSamples; k++)
            {
                float target = total * k / (BendSamples - 1);
                while (j < dense.Count - 1 && arc[j] < target) j++;
                float span = arc[j] - arc[j - 1];
                pts[k] = Vector2.Lerp(dense[j - 1], dense[j], span > 1e-6f ? (target - arc[j - 1]) / span : 0f);
            }
            float ds = total / (BendSamples - 1);
            int sign = 0;
            float lastChange = float.NegativeInfinity;
            for (int k = 1; k < BendSamples - 1; k++)
            {
                Vector2 a = pts[k] - pts[k - 1], c = pts[k + 1] - pts[k];
                float cross = a.x * c.y - a.y * c.x;
                float kappa = 2f * cross / Mathf.Max(1e-6f, a.magnitude * c.magnitude * (a + c).magnitude);
                if (Mathf.Abs(kappa) < BendMinCurvature) continue;
                int sg = kappa > 0f ? 1 : -1;
                float s = k * ds;
                if (sign == 0) sign = sg;
                else if (sg != sign && s - lastChange >= BendMinArc)
                {
                    changes++;
                    sign = sg;
                    lastChange = s;
                }
            }
            Vector2 chord = pts[BendSamples - 1] - pts[0];
            Vector2 normal = new Vector2(-chord.y, chord.x).normalized;
            float min = 0f, max = 0f;
            for (int k = 0; k < BendSamples; k++)
            {
                float d = Vector2.Dot(pts[k] - pts[0], normal);
                min = Mathf.Min(min, d);
                max = Mathf.Max(max, d);
            }
            rangeH = (max - min) / h;
        }

        /// <summary>AC7: tips of InPad nodes with LeafScale ≥ 0.5 inside the foliage mask; bark centre line of InPad ord ≥ 2 nodes
        /// (up to the trimmed end) outside the foliage mask dilated by 1 px.</summary>
        static void Coverage(TreePreviewMeshBuilder b, bool[] foliage, Rect frame, out int tips, out int covered, out int twigs, out int outside)
        {
            tips = covered = twigs = outside = 0;
            TreeShapeAnalysis shape = b.Shape;
            for (int i = 1; i < b.NodeCount; i++)
            {
                if (!shape.InPad(i)) continue;
                NodeData n = b.Node(i);
                if (n.LeafScale >= 0.5f)
                {
                    tips++;
                    if (Sample(foliage, frame, b.TipOf(i), 0)) covered++;
                }
                if (shape.Order(i) < 2) continue;
                float end = b.BarkEndT(i);
                for (int s = 0; s <= 8; s++)
                {
                    twigs++;
                    if (!Sample(foliage, frame, b.PointOn(i, end * s / 8f), 1)) outside++;
                }
            }
        }

        static bool Sample(bool[] mask, Rect frame, Vector2 world, int dilate)
        {
            int x = Mathf.FloorToInt((world.x - frame.xMin) / frame.width * FrameWidth);
            int y = Mathf.FloorToInt((world.y - frame.yMin) / frame.height * FrameHeight);
            for (int dy = -dilate; dy <= dilate; dy++)
            {
                for (int dx = -dilate; dx <= dilate; dx++)
                {
                    int px = x + dx, py = y + dy;
                    if (px < 0 || py < 0 || px >= FrameWidth || py >= FrameHeight) continue;
                    if (mask[py * FrameWidth + px]) return true;
                }
            }
            return false;
        }

        static void Roots(TreePreviewMeshBuilder b, uint seed, out int roots, out int left, out int right, out float endRatio)
        {
            roots = b.LastRootCount;
            left = right = 0;
            endRatio = 1f;
            var samples = new List<Vector3>();
            for (int r = 0; r < roots; r++)
            {
                b.NebariRoot(seed, r, roots, samples);
                // Screen left = −x.
                if (samples[samples.Count - 1].x < 0f) left++;
                else right++;
                endRatio = Mathf.Min(endRatio, samples[samples.Count - 1].z / samples[0].z);
            }
        }

        // ---- Output ----------------------------------------------------------------------------------

        static void Append(StringBuilder csv, Row r)
        {
            csv.Append(r.Species).Append(',').Append(r.Styled ? "styled" : "wild").Append(',').Append(r.Seed).Append(',').Append(r.Day).Append(',')
                .Append(r.Nodes).Append(',').Append(F(r.IoU)).Append(',').Append(F(r.IoUDirect)).Append(',').Append(F(r.IoUMirror)).Append(',')
                .Append(r.Mirror ? "yes" : "no").Append(',').Append(F(r.HP)).Append(',').Append(F(r.WP)).Append(',').Append(F(r.Th0H)).Append(',')
                .Append(F(r.FlareH)).Append(',').Append(r.Tiers).Append(',').Append(r.TiersAlternate ? 1 : 0).Append(',').Append(r.FoliageComponents)
                .Append(',').Append(r.BendChanges).Append(',').Append(F(r.BendRangeH)).Append(',').Append(F(r.DomeWH)).Append(',').Append(F(r.DomeHH))
                .Append(',').Append(r.Tips).Append(',').Append(r.TipsCovered).Append(',').Append(r.TwigSamples).Append(',').Append(r.TwigOutside)
                .Append(',').Append(r.Roots).Append(',').Append(r.RootsLeft).Append(',').Append(r.RootsRight).Append(',').Append(F(r.RootEndRatio))
                .Append(',').Append(F(r.Zoom)).Append('\n');
        }

        static string Summarize(List<Row> rows)
        {
            var sb = new StringBuilder();
            foreach (SpeciesId species in AllSpecies)
            {
                foreach (bool styled in new[] { true, false })
                {
                    sb.Append("== ").Append(species).Append(styled ? " Styled" : " Wild").Append(" (median of seeds 1…N; fixture in [])\n");
                    foreach (int day in styled ? StyledDays : new[] { 20, 80 })
                    {
                        List<Row> seeds = rows.FindAll(r => r.Species == species && r.Styled == styled && r.Day == day && r.Seed != FixtureSeed);
                        Row fx = rows.Find(r => r.Species == species && r.Styled == styled && r.Day == day && r.Seed == FixtureSeed);
                        if (seeds.Count == 0 || fx == null) continue;
                        int tiersOk = seeds.FindAll(r => r.Tiers >= 6 && r.Tiers <= 7).Count;
                        int altOk = seeds.FindAll(r => r.TiersAlternate).Count;
                        int bendOk = seeds.FindAll(r => r.BendChanges >= 2).Count;
                        sb.AppendFormat(Inv,
                            "D{0,3}: IoU {1:0.000} [{2:0.000}] min {3:0.000} | H/P {4:0.00} [{5:0.00}] W/P {6:0.00} [{7:0.00}] th0/H 1/{8:0.0} flare/H 1/{9:0.0} | tiers {10:0} [{11}] 6–7: {12}/{13} alt {14}/{13} | comps {15:0} [{16}] | bends {17:0} [{18}] ≥2: {19}/{13} range/H {20:0.000} | dome W/H {21:0.00} H/H {22:0.00} | tips {23:0.000} twigOut {24:0.000} | roots {25} L{26}/R{27} end {28:0.00} | zoom {29:0.00}\n",
                            day, Median(seeds, r => r.IoU), fx.IoU, seeds.ConvertAll(r => r.IoU).ToArray().Length > 0 ? Min(seeds, r => r.IoU) : 0f,
                            Median(seeds, r => r.HP), fx.HP, Median(seeds, r => r.WP), fx.WP, 1f / Mathf.Max(1e-4f, Median(seeds, r => r.Th0H)),
                            1f / Mathf.Max(1e-4f, Median(seeds, r => r.FlareH)), Median(seeds, r => r.Tiers), fx.Tiers, tiersOk, seeds.Count, altOk,
                            Median(seeds, r => r.FoliageComponents), fx.FoliageComponents, Median(seeds, r => r.BendChanges), fx.BendChanges, bendOk,
                            Median(seeds, r => r.BendRangeH), Median(seeds, r => r.DomeWH), Median(seeds, r => r.DomeHH),
                            Sum(seeds, r => r.TipsCovered) / Mathf.Max(1f, Sum(seeds, r => r.Tips)), Sum(seeds, r => r.TwigOutside) / Mathf.Max(1f, Sum(seeds, r => r.TwigSamples)),
                            fx.Roots, fx.RootsLeft, fx.RootsRight, fx.RootEndRatio, Median(seeds, r => r.Zoom));
                    }
                    if (styled)
                    {
                        // AC5: H(D20)/H(D80) and H(D120)/H(D80), medians of per-seed ratios.
                        var r20 = new List<float>();
                        var r120 = new List<float>();
                        foreach (Row r80 in rows.FindAll(r => r.Species == species && r.Styled && r.Day == 80 && r.Seed != FixtureSeed))
                        {
                            Row a = rows.Find(r => r.Species == species && r.Styled && r.Day == 20 && r.Seed == r80.Seed);
                            Row c = rows.Find(r => r.Species == species && r.Styled && r.Day == 120 && r.Seed == r80.Seed);
                            if (a != null) r20.Add(a.HP / r80.HP);
                            if (c != null) r120.Add(c.HP / r80.HP);
                        }
                        sb.AppendFormat(Inv, "AC5: H(D20)/H(D80) {0:0.000}, H(D120)/H(D80) {1:0.000}\n", MedianOf(r20), MedianOf(r120));
                    }
                }
            }
            return sb.ToString();
        }

        static float Median(List<Row> rows, Func<Row, float> f) => MedianOf(rows.ConvertAll(r => f(r)));
        static float Min(List<Row> rows, Func<Row, float> f)
        {
            float m = float.MaxValue;
            foreach (Row r in rows) m = Mathf.Min(m, f(r));
            return m;
        }

        static float Sum(List<Row> rows, Func<Row, float> f)
        {
            float s = 0f;
            foreach (Row r in rows) s += f(r);
            return s;
        }

        static float MedianOf(List<float> values)
        {
            if (values.Count == 0) return float.NaN;
            values.Sort();
            int n = values.Count;
            return (n & 1) == 1 ? values[n / 2] : 0.5f * (values[n / 2 - 1] + values[n / 2]);
        }

        static string F(float v) => v.ToString("0.0000", Inv);

        // ---- Images ----------------------------------------------------------------------------------

        static void SaveOverlay(string path, bool[] ours, bool[] reference, bool mirror)
        {
            var tex = new Texture2D(FrameWidth, FrameHeight, TextureFormat.RGBA32, false);
            var px = new Color32[FrameWidth * FrameHeight];
            for (int y = 0; y < FrameHeight; y++)
            {
                for (int x = 0; x < FrameWidth; x++)
                {
                    int i = y * FrameWidth + x;
                    bool a = ours[y * FrameWidth + (mirror ? FrameWidth - 1 - x : x)], b = reference[i];
                    px[i] = a && b ? new Color32(235, 235, 235, 255) : a ? new Color32(220, 60, 50, 255) : b ? new Color32(40, 150, 220, 255) : new Color32(25, 25, 25, 255);
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        static void CaptureImages(GrowthPreviewController controller, Camera camera, string outDir)
        {
            controller.SetSeed(FixtureSeed);
            int[] sheetDays = { 20, 40, 80, 120 };
            const int cellW = FrameWidth / 2, cellH = FrameHeight / 2;
            foreach (SpeciesId species in AllSpecies)
            {
                controller.SetSpecies(species);
                var sheet = new Texture2D(cellW * sheetDays.Length, cellH * 2, TextureFormat.RGBA32, false);
                for (int m = 0; m < 2; m++)
                {
                    bool styled = m == 0;
                    controller.SetStyled(styled);
                    for (int d = 0; d < sheetDays.Length; d++)
                    {
                        controller.SetDay(sheetDays[d]);
                        ReferenceFrame(controller, camera);
                        Color32[] full = RenderPixels(camera, false);
                        // Top row Styled, bottom row Wild (texture rows are bottom-up).
                        Blit(sheet, full, d * cellW, (1 - m) * cellH);
                    }
                }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(outDir, "sheet_" + species + "_styled_top_wild_bottom_d20_40_80_120.png"), sheet.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(sheet);
            }

            controller.SetStyled(true);
            Compare(controller, camera, SpeciesId.Momiji, 80, PreviewArtLibrary.ReferencePath, Path.Combine(outDir, "compare_momiji.png"));
            Compare(controller, camera, SpeciesId.Momiji, 120, PreviewArtLibrary.ReferencePath, Path.Combine(outDir, "compare_momiji_d120.png"));
            Compare(controller, camera, SpeciesId.Kuromatsu, 80, NightReferencePath, Path.Combine(outDir, "compare_kuromatsu.png"));
        }

        static void Blit(Texture2D sheet, Color32[] full, int x0, int y0)
        {
            const int cellW = FrameWidth / 2, cellH = FrameHeight / 2;
            var cell = new Color32[cellW * cellH];
            for (int y = 0; y < cellH; y++)
            {
                for (int x = 0; x < cellW; x++) cell[y * cellW + x] = full[(2 * y) * FrameWidth + 2 * x];
            }
            sheet.SetPixels32(x0, y0, cellW, cellH, cell);
        }

        /// <summary>Ours (fixture, Styled, Textured, pot-aligned reference frame) on the left, the reference image on the right.</summary>
        static void Compare(GrowthPreviewController controller, Camera camera, SpeciesId species, int day, string referencePath, string outPath)
        {
            if (!File.Exists(referencePath)) return;
            controller.SetSpecies(species);
            controller.SetDay(day);
            ReferenceFrame(controller, camera);
            Color32[] ours = RenderPixels(camera, false);
            var reference = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            reference.LoadImage(File.ReadAllBytes(referencePath));
            var outTex = new Texture2D(FrameWidth * 2, FrameHeight, TextureFormat.RGBA32, false);
            outTex.SetPixels32(0, 0, FrameWidth, FrameHeight, ours);
            if (reference.width == FrameWidth && reference.height == FrameHeight) outTex.SetPixels32(FrameWidth, 0, FrameWidth, FrameHeight, reference.GetPixels32());
            outTex.Apply();
            File.WriteAllBytes(outPath, outTex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(outTex);
            UnityEngine.Object.DestroyImmediate(reference);
        }

        static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
