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
    /// PA-102Q measurement harness — structural shape metrics of BALANCE §1.4.0 (AC1–AC13, AC20; IoU is a log column only, AC11).
    /// Renders the Textured preview in the reference frame (z = 1, pot-aligned, 768×1376, TD-028); masks: tree (bark + roots + pads),
    /// foliage (pads), moss (SoilFront) — α ≥ 0.5. Pads = foliage components ≥ 0.5% of the tree mask; the dome = the component holding the
    /// crown axis (or the chain top); laterals sorted by yc ↔ reference 1…6; deterministic mirror (the lowest lateral goes right).
    /// Trunk profile and primaries — from the mesh geometry; root visibility — 32 centre-line points not covered by opaque moss.
    /// Writes metrics.csv, summary.txt, components_fixture.txt and the PNGs. Batch:
    /// <c>-executeMethod PocketArbor.DevTools.GrowthPreview.PreviewMetricsHarness.RunBatch -metricsOut &lt;dir&gt; [-seeds 30] [-noImages]
    /// [-cfg Field=v,…] [-only Momiji]</c> (Metal, without -nographics). Dev-only: allocations are fine.
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
        static readonly int[] WildDays = { 20, 80 };
        static readonly SpeciesId[] AllSpecies = { SpeciesId.Momiji, SpeciesId.Kuromatsu, SpeciesId.Yamazakura };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // ---- Reference (BALANCE §1.4.0) ------------------------------------------------------------------
        /// <summary>yc/H, xc (units, + = right), w, h (units) of laterals 1…6 and of «dome ∪ #7» (index 6), L2 Momiji.</summary>
        public static readonly float[] RefYc = { 0.265f, 0.333f, 0.474f, 0.558f, 0.655f, 0.730f, 0.873f };
        public static readonly float[] RefXc = { 30.6f, -30.8f, 28.9f, -25.1f, 23.6f, -16.6f, 4.7f };
        static readonly float[] RefWL2 = { 44.0f, 43.4f, 37.7f, 37.5f, 38.4f, 35.4f, 60.8f };
        static readonly float[] RefHL2 = { 20.2f, 20.3f, 15.9f, 16.6f, 15.3f, 13.8f, 32.2f };
        static readonly float[] RefWNight = { 46.0f, 44.1f, 37.8f, 37.2f, 38.1f, 33.6f, 59.2f };
        static readonly float[] RefHNight = { 21.4f, 21.1f, 17.4f, 16.7f, 15.4f, 13.2f, 30.4f };
        const float RefDomeWH = 0.466f, RefDomeHH = 0.247f;
        public static readonly float[] ProfileFractions = { 0.02f, 0.06f, 0.10f, 0.30f, 0.60f };
        const int Pads = 7; // 6 laterals + dome
        const int RootPoints = 32;

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
            public ShapeStyle Style;
            public uint Seed;
            public int Day;
            public int Nodes;
            public float IoU, IoUDirect, IoUMirror;
            public bool Mirror;
            public float HP, WP, Th0H, FlareH, HMask, HPMask, PotTopY, HModel;
            public float LastHeightProxy() => HModel;
            public int Tiers;
            public bool TiersAlternate;
            public int FoliageComponents, Laterals;
            public readonly float[] Yc = Fill(Pads), YcBase = Fill(Pads), Xc = Fill(Pads), W = Fill(Pads), H = Fill(Pads);
            public int BendChanges;
            public float BendRangeH;
            public float DomeWH, DomeHH;
            public readonly float[] Profile = Fill(5), ProfilePot = Fill(5);
            public float ProfileMonotone = float.NaN;
            public float PrimaryMin = float.NaN, PrimaryMax = float.NaN, PrimaryMedian = float.NaN, LowestAttachH = float.NaN, LowestAttachHPot = float.NaN;
            public readonly List<float> PrimaryRatios = new List<float>();
            public int Tips, TipsCovered;
            public int TwigSamples, TwigOutside;
            public int Roots, RootsLeft, RootsRight;
            public float RootEndRatio;
            public readonly List<float> RootVisibility = new List<float>();
            public float Zoom;
            public float ChordDeg, MaxWorldDeg;
            public bool LowestTierOpposite;

            public float RootVisMedian => MedianOf(new List<float>(RootVisibility));
            public float RootVisMin => RootVisibility.Count == 0 ? float.NaN : MinOf(RootVisibility);
            public float[] RefW => Species == SpeciesId.Kuromatsu ? RefWNight : RefWL2;
            public float[] RefH => Species == SpeciesId.Kuromatsu ? RefHNight : RefHL2;

            /// <summary>Pads (of 7) whose yc/H is within <paramref name="tol"/> of the reference.</summary>
            public int YcHits(float tol)
            {
                int n = 0;
                for (int k = 0; k < Pads; k++) if (!float.IsNaN(Yc[k]) && Mathf.Abs(Yc[k] - RefYc[k]) <= tol) n++;
                return n;
            }
        }

        static float[] Fill(int n)
        {
            var a = new float[n];
            for (int i = 0; i < n; i++) a[i] = float.NaN;
            return a;
        }

        public static void RunBatch()
        {
            int exitCode = 0;
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                string outDir = Arg(args, "-metricsOut") ?? "Temp/pa102q-metrics";
                int seeds = int.TryParse(Arg(args, "-seeds"), out int n) ? n : 30;
                bool images = Array.IndexOf(args, "-noImages") < 0;
                Overrides = Arg(args, "-cfg");
                string only = Arg(args, "-only");
                Run(outDir, seeds, images, only);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                exitCode = 1;
            }
            EditorApplication.Exit(exitCode);
        }

        public static List<Row> Run(string outDir, int seeds, bool images, string only = null)
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
            controller.SetShapeStyle(ShapeStyle.Moyogi);
            ApplyOverrides(controller);

            bool[] l2 = LoadMask(MaskL2);
            bool[] night = LoadMask(MaskNight);
            if (l2 == null) throw new FileNotFoundException("reference mask missing", MaskL2);

            var rows = new List<Row>();
            var components = new StringBuilder();
            var csv = new StringBuilder(CsvHeader());

            foreach (SpeciesId species in AllSpecies)
            {
                if (only != null && species.ToString() != only) continue;
                bool[] reference = species == SpeciesId.Kuromatsu && night != null ? night : l2;
                controller.SetSpecies(species);
                foreach (bool styled in new[] { true, false })
                {
                    controller.SetStyled(styled);
                    for (int s = 0; s <= seeds; s++)
                    {
                        uint seed = s == 0 ? FixtureSeed : (uint)s;
                        controller.SetSeed(seed);
                        foreach (int day in styled ? StyledDays : WildDays)
                        {
                            controller.SetDay(day);
                            if (s == 0 && styled && day >= 80)
                            {
                                Components = components;
                                components.Append(species).Append(" D").Append(day).Append('\n');
                            }
                            else Components = null;
                            Row row = Measure(controller, camera, reference, species, styled, seed, day, out bool[] ours, true);
                            rows.Add(row);
                            Append(csv, row);
                            if (images && s == 0 && styled && (day == 80 || day == 120))
                                SaveOverlay(Path.Combine(outDir, "overlay_" + species + "_d" + day + ".png"), ours, reference, row.Mirror);
                        }
                    }
                }
            }
            Components = null;

            // AC20: styles (Momiji Styled D80, 30 seeds + fixture; model/mesh metrics only).
            if (only == null || only == "Momiji")
            {
                controller.SetSpecies(SpeciesId.Momiji);
                controller.SetStyled(true);
                foreach (ShapeStyle style in new[] { ShapeStyle.Moyogi, ShapeStyle.Chokkan, ShapeStyle.Shakan })
                {
                    controller.SetShapeStyle(style);
                    for (int s = 0; s <= seeds; s++)
                    {
                        controller.SetSeed(s == 0 ? FixtureSeed : (uint)s);
                        controller.SetDay(80);
                        Row row = StyleRow(controller, style, s == 0 ? FixtureSeed : (uint)s);
                        rows.Add(row);
                    }
                }
                controller.SetShapeStyle(ShapeStyle.Moyogi);
            }

            File.WriteAllText(Path.Combine(outDir, "components_fixture.txt"), components.ToString());
            File.WriteAllText(Path.Combine(outDir, "metrics.csv"), csv.ToString());
            string summary = Summarize(rows);
            File.WriteAllText(Path.Combine(outDir, "summary.txt"), summary);
            Debug.Log("[PA-102Q metrics]\n" + summary);

            if (images) CaptureImages(controller, camera, outDir);
            return rows;
        }

        /// <summary>Tuning experiments: "-cfg Field=value,…" sets public fields of GrowthConfig / SpeciesConfig (all species) / StyleConfig /
        /// TreeRenderConfig; "Species.Field=v" (e.g. Momiji.PrimaryAngleMeanDeg=64) — one species only; PuffRadiusScale — FoliageConfig.</summary>
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
                string field = kv[0];
                string speciesOnly = null;
                int dot = field.IndexOf('.');
                if (dot > 0)
                {
                    speciesOnly = field.Substring(0, dot);
                    field = field.Substring(dot + 1);
                }
                bool done = speciesOnly == null && (Set(model.Config, field, kv[1]) | Set(model.Style, field, kv[1]) | Set(controller.Builder.Render, field, kv[1])
                    | Set(controller.Style, field, kv[1]));
                foreach (SpeciesId species in AllSpecies)
                {
                    if (speciesOnly != null && species.ToString() != speciesOnly) continue;
                    controller.SetSpecies(species);
                    done |= Set(model.SpeciesConfig, field, kv[1]);
                }
                if (!done) throw new ArgumentException("unknown config field " + kv[0]);
                Debug.Log("[PA-102Q metrics] override " + pair);
            }
        }

        static bool Set(object target, string field, string value)
        {
            var f = target.GetType().GetField(field);
            if (f == null) return false;
            object v = f.FieldType.IsEnum ? Enum.Parse(f.FieldType, value) : Convert.ChangeType(double.Parse(value, Inv), f.FieldType, Inv);
            f.SetValue(target, v);
            return true;
        }

        // ---- One frame -------------------------------------------------------------------------------

        static Row Measure(GrowthPreviewController controller, Camera camera, bool[] reference, SpeciesId species, bool styled, uint seed, int day,
            out bool[] tree, bool masks)
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
            view.SetSoilFrontOnly(true);
            Color32[] moss = RenderPixels(camera, true);
            view.SetSoilFrontOnly(false);
            controller.FrameReference(camera);

            tree = Mask(all);
            bool[] foliage = Mask(pads);
            bool[] mossMask = MaskAll(moss);
            float direct = IoU(tree, reference, false);
            float mirror = IoU(tree, reference, true);

            var row = new Row
            {
                Species = species, Styled = styled, Style = controller.ShapeStyle, Seed = seed, Day = day, Nodes = builder.NodeCount, Zoom = zoom,
                IoUDirect = direct, IoUMirror = mirror,
            };

            float p = builder.LastPotWidth;
            float h = Mathf.Max(1e-3f, builder.LastHeight);
            row.HP = builder.LastHeight / p;
            row.WP = builder.LastCrownWidth / p;
            row.HModel = h;
            row.Th0H = builder.LastTrunkBase / h;
            row.FlareH = builder.LastTrunkBaseFlared / h;
            float upp = frame.width / FrameWidth;
            row.PotTopY = frame.yMax - PotTopPx * upp;

            TierStats(builder, out row.Tiers, out row.TiersAlternate);
            PadMetrics(builder, foliage, tree, frame, row);
            row.IoU = row.Mirror ? mirror : direct;
            Bends(builder, h, out row.BendChanges, out row.BendRangeH);
            Coverage(builder, foliage, frame, out row.Tips, out row.TipsCovered, out row.TwigSamples, out row.TwigOutside);
            Profile(builder, row, h);
            Primaries(builder, row, h);
            Roots(builder, seed, mossMask, frame, row);
            return row;
        }

        static Row StyleRow(GrowthPreviewController controller, ShapeStyle style, uint seed)
        {
            TreePreviewMeshBuilder b = controller.Builder;
            var row = new Row { Species = SpeciesId.Momiji, Styled = true, Style = style, Seed = seed, Day = 80, Nodes = b.NodeCount };
            float h = Mathf.Max(1e-3f, b.LastHeight);
            row.HP = b.LastHeight / b.LastPotWidth;
            row.WP = b.LastCrownWidth / b.LastPotWidth;
            Bends(b, h, out row.BendChanges, out row.BendRangeH);
            TierStats(b, out row.Tiers, out row.TiersAlternate);
            // Chord «base of #0 → end of k4» from vertical (§1.3.1 measure), signed: + = leaning left (positive world angle).
            var axis = new List<Vector2>();
            b.TrunkAxis(axis);
            Vector2 chord = axis[axis.Count - 1] - axis[0];
            row.ChordDeg = Mathf.Atan2(-chord.x, chord.y) * Mathf.Rad2Deg;
            float lowest = float.MaxValue;
            int lowestSide = 0;
            float maxWorld = 0f;
            TreeShapeAnalysis shape = b.Shape;
            for (int i = 1; i < b.NodeCount; i++)
            {
                Vector2 d = b.TipOf(i) - b.PointOn(i, 0f);
                if (d.sqrMagnitude > 1e-6f) maxWorld = Mathf.Max(maxWorld, Mathf.Abs(Mathf.Atan2(-d.x, d.y) * Mathf.Rad2Deg));
                NodeData n = b.Node(i);
                if (!shape.IsPrimary(i) || n.Status == NodeStatus.Bud) continue;
                b.BaseWidths(i, out _, out _, out float attachY);
                if (attachY < lowest)
                {
                    lowest = attachY;
                    lowestSide = n.BranchSide;
                }
            }
            row.MaxWorldDeg = maxWorld;
            // A lateral on side s points to x sign −s; the lean points to x sign of the chord → opposite ⇔ s == sign(chord.x).
            row.LowestTierOpposite = lowestSide != 0 && Mathf.Abs(row.ChordDeg) > 1f && lowestSide == Math.Sign(chord.x);
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

        /// <summary>α ≥ 0.5 over the whole frame (moss mask for root visibility).</summary>
        static bool[] MaskAll(Color32[] pixels)
        {
            var mask = new bool[pixels.Length];
            for (int i = 0; i < pixels.Length; i++) mask[i] = pixels[i].a >= 128;
            return mask;
        }

        // ---- §1.4.0 pads ---------------------------------------------------------------------------------

        struct Comp
        {
            public int MinX, MaxX, MinY, MaxY; // rows bottom-up
            public long Area;
            public int Label;
        }

        /// <summary>
        /// Foliage components ≥ 0.5% of the tree mask → dome (holds a crown-axis / chain-top tip) + laterals (sorted by yc), deterministic
        /// mirror (lowest lateral on the left → x → −x), match to the reference 1…6 (6 laterals → in order; else nearest yc on the same side).
        /// yc/H: zero = pot top row (as the TA masks), H = top of the tree mask above it; YcBase: zero = trunk base y = 0, H = LastHeight.
        /// </summary>
        static void PadMetrics(TreePreviewMeshBuilder b, bool[] foliage, bool[] tree, Rect frame, Row row)
        {
            long treeArea = 0;
            int treeTop = -1;
            for (int i = 0; i < tree.Length; i++)
            {
                if (!tree[i]) continue;
                treeArea++;
                treeTop = Math.Max(treeTop, i / FrameWidth);
            }
            long minArea = (long)(0.005 * treeArea);
            var label = new int[foliage.Length];
            var stack = new Stack<int>();
            var comps = new List<Comp>();
            int next = 0;
            for (int start = 0; start < foliage.Length; start++)
            {
                if (!foliage[start] || label[start] != 0) continue;
                next++;
                label[start] = next;
                stack.Push(start);
                var c = new Comp { MinX = int.MaxValue, MaxX = -1, MinY = int.MaxValue, MaxY = -1, Label = next };
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    c.Area++;
                    int x = i % FrameWidth, y = i / FrameWidth;
                    c.MinX = Math.Min(c.MinX, x); c.MaxX = Math.Max(c.MaxX, x); c.MinY = Math.Min(c.MinY, y); c.MaxY = Math.Max(c.MaxY, y);
                    if (x > 0) Visit(i - 1);
                    if (x < FrameWidth - 1) Visit(i + 1);
                    if (y > 0) Visit(i - FrameWidth);
                    if (y < FrameHeight - 1) Visit(i + FrameWidth);
                }
                if (c.Area >= minArea) comps.Add(c);

                void Visit(int j)
                {
                    if (!foliage[j] || label[j] != 0) return;
                    label[j] = next;
                    stack.Push(j);
                }
            }
            row.FoliageComponents = comps.Count;

            float upp = frame.width / FrameWidth;
            int potRow = FrameHeight - PotTopPx; // bottom-up row of the pot top
            float hMask = Mathf.Max(1f, (treeTop + 1 - potRow) * upp);
            row.HMask = hMask;
            row.HPMask = hMask / b.LastPotWidth;
            float hBase = Mathf.Max(1e-3f, b.LastHeight);

            // Dome: the component under a crown-axis tip (else the chain top), searched within 6 px.
            int domeLabel = -1;
            TreeShapeAnalysis shape = b.Shape;
            for (int pass = 0; pass < 2 && domeLabel < 0; pass++)
            {
                for (int i = b.NodeCount - 1; i >= 0 && domeLabel < 0; i--)
                {
                    bool want = pass == 0 ? shape.IsCrownAxis(i) : shape.IsTrunkChain(i);
                    if (!want) continue;
                    domeLabel = LabelNear(label, frame, b.TipOf(i), 6, comps);
                }
            }
            int domeIndex = -1;
            for (int k = 0; k < comps.Count; k++) if (comps[k].Label == domeLabel) domeIndex = k;
            if (domeIndex < 0)
            {
                int best = -1;
                for (int k = 0; k < comps.Count; k++) if (best < 0 || comps[k].MaxY > comps[best].MaxY) best = k;
                domeIndex = best;
            }

            var laterals = new List<KeyValuePair<float, int>>();
            for (int k = 0; k < comps.Count; k++)
            {
                if (k == domeIndex) continue;
                laterals.Add(new KeyValuePair<float, int>(0.5f * (comps[k].MinY + comps[k].MaxY), k));
            }
            laterals.Sort((x, y) => x.Key.CompareTo(y.Key));
            row.Laterals = laterals.Count;
            float CenterX(Comp c) => ((c.MinX + c.MaxX + 1) * 0.5f - FrameWidth * 0.5f) * upp;
            row.Mirror = laterals.Count > 0 && CenterX(comps[laterals[0].Value]) < 0f;
            float sign = row.Mirror ? -1f : 1f;

            void Store(int slot, Comp c)
            {
                float ycRow = (c.MinY + c.MaxY + 1) * 0.5f;
                row.Yc[slot] = (ycRow - potRow) * upp / hMask;
                float yWorld = frame.yMin + ycRow * upp;
                row.YcBase[slot] = yWorld / hBase;
                row.Xc[slot] = sign * CenterX(c);
                row.W[slot] = (c.MaxX - c.MinX + 1) * upp;
                row.H[slot] = (c.MaxY - c.MinY + 1) * upp;
            }

            if (laterals.Count == 6)
            {
                for (int k = 0; k < 6; k++) Store(k, comps[laterals[k].Value]);
            }
            else
            {
                for (int k = 0; k < 6; k++)
                {
                    int best = -1;
                    float bestD = float.MaxValue;
                    foreach (var l in laterals)
                    {
                        Comp c = comps[l.Value];
                        if (Mathf.Sign(sign * CenterX(c)) != Mathf.Sign(RefXc[k])) continue;
                        float yc = ((c.MinY + c.MaxY + 1) * 0.5f - potRow) * upp / hMask;
                        float d = Mathf.Abs(yc - RefYc[k]);
                        if (d < bestD)
                        {
                            bestD = d;
                            best = l.Value;
                        }
                    }
                    if (best >= 0) Store(k, comps[best]);
                }
            }
            if (domeIndex >= 0)
            {
                Store(6, comps[domeIndex]);
                row.DomeWH = row.W[6] / hMask;
                row.DomeHH = row.H[6] / hMask;
            }

            if (Components != null)
            {
                Components.AppendFormat(Inv, "  H(mask, from pot top) {0:0.0} u, H(model) {1:0.0} u, pot top y {2:0.0}, laterals {3}, mirror {4}\n",
                    hMask, hBase, row.PotTopY, laterals.Count, row.Mirror);
                for (int i = 0; i < b.NodeCount; i++)
                {
                    NodeData n = b.Node(i);
                    bool chain = shape.IsTrunkChain(i), crown = shape.IsCrownAxis(i), primary = shape.IsPrimary(i);
                    if (!chain && !crown && !primary) continue;
                    Vector2 p0 = b.PointOn(i, 0f), p1 = b.TipOf(i);
                    Components.AppendFormat(Inv, "    node {0,3} {1,-7} d{2} side {3,2} T {4:0.00} L {5:0.0}/{6:0.0} th {7:0.00} base ({8:0.0},{9:0.0}) tip ({10:0.0},{11:0.0}) age {12} pad {13}\n",
                        i, chain ? "chain" : crown ? "crown" : "primary", n.Depth, n.BranchSide, n.AttachT, n.Length, n.TargetLength, n.Thickness, p0.x, p0.y, p1.x, p1.y,
                        n.AgeDays, shape.PadId(i));
                }
                for (int k = 0; k < Pads; k++)
                {
                    Components.AppendFormat(Inv, "  {0} yc/H {1:0.000} (ref {2:0.000}; base-zero {3:0.000})  xc {4,6:0.0} (ref {5,6:0.0})  w {6:0.0} × h {7:0.0} (ref {8:0.0} × {9:0.0})\n",
                        k < 6 ? "#" + (k + 1) : "dome", row.Yc[k], RefYc[k], row.YcBase[k], row.Xc[k], RefXc[k], row.W[k], row.H[k], row.RefW[k], row.RefH[k]);
                }
            }
        }

        static int LabelNear(int[] label, Rect frame, Vector2 world, int radius, List<Comp> comps)
        {
            int x = Mathf.FloorToInt((world.x - frame.xMin) / frame.width * FrameWidth);
            int y = Mathf.FloorToInt((world.y - frame.yMin) / frame.height * FrameHeight);
            for (int r = 0; r <= radius; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int px = x + dx, py = y + dy;
                        if (px < 0 || py < 0 || px >= FrameWidth || py >= FrameHeight) continue;
                        int l = label[py * FrameWidth + px];
                        if (l == 0) continue;
                        foreach (Comp c in comps) if (c.Label == l) return l;
                    }
                }
            }
            return -1;
        }

        // ---- §1.4.0 trunk profile, primaries, roots ------------------------------------------------------

        /// <summary>Mesh width of the trunk chain at f·H: Profile — heights from the trunk base y = 0 with H = LastHeight (the §1.4.1 arithmetic);
        /// ProfilePot — heights f·H_mask above the pot top row (log); monotone share above 0.06 H (rise ≤ 2%).</summary>
        static void Profile(TreePreviewMeshBuilder b, Row row, float h)
        {
            for (int k = 0; k < ProfileFractions.Length; k++)
            {
                row.Profile[k] = b.TrunkWidthAtHeight(ProfileFractions[k] * h);
                row.ProfilePot[k] = b.TrunkWidthAtHeight(row.PotTopY + ProfileFractions[k] * row.HMask);
            }
            float top = b.TrunkChainTopY;
            float y0 = 0.06f * h;
            if (top <= y0) return;
            const int steps = 64;
            int ok = 0, total = 0;
            float previous = b.TrunkWidthAtHeight(y0);
            for (int s = 1; s <= steps; s++)
            {
                float w = b.TrunkWidthAtHeight(y0 + (top - y0) * s / steps * 0.999f);
                if (float.IsNaN(w) || float.IsNaN(previous)) continue;
                total++;
                if (w <= previous * 1.02f) ok++;
                previous = w;
            }
            row.ProfileMonotone = total > 0 ? ok / (float)total : float.NaN;
        }

        static void Primaries(TreePreviewMeshBuilder b, Row row, float h)
        {
            TreeShapeAnalysis shape = b.Shape;
            float lowest = float.MaxValue;
            for (int i = 1; i < b.NodeCount; i++)
            {
                if (!shape.IsPrimary(i) || b.Node(i).Status == NodeStatus.Bud) continue;
                b.BaseWidths(i, out float own, out float trunk, out float attachY);
                if (trunk > 1e-3f) row.PrimaryRatios.Add(own / trunk);
                lowest = Mathf.Min(lowest, attachY);
            }
            if (row.PrimaryRatios.Count > 0)
            {
                row.PrimaryMin = MinOf(row.PrimaryRatios);
                row.PrimaryMax = -MinOf(row.PrimaryRatios.ConvertAll(v => -v));
                row.PrimaryMedian = MedianOf(new List<float>(row.PrimaryRatios));
            }
            if (lowest < float.MaxValue)
            {
                row.LowestAttachH = lowest / h;
                row.LowestAttachHPot = (lowest - row.PotTopY) / Mathf.Max(1f, row.HMask);
            }
        }

        static void Roots(TreePreviewMeshBuilder b, uint seed, bool[] moss, Rect frame, Row row)
        {
            row.Roots = b.LastRootCount;
            row.RootEndRatio = 1f;
            var samples = new List<Vector3>();
            for (int r = 0; r < row.Roots; r++)
            {
                b.NebariRoot(seed, r, row.Roots, samples);
                // Screen left = −x.
                if (samples[samples.Count - 1].x < 0f) row.RootsLeft++;
                else row.RootsRight++;
                row.RootEndRatio = Mathf.Min(row.RootEndRatio, samples[samples.Count - 1].z / samples[0].z);
                // Samples are uniform in arc length: 32 points by linear interpolation of the sample index.
                int visible = 0;
                for (int k = 0; k < RootPoints; k++)
                {
                    float f = (samples.Count - 1) * k / (float)(RootPoints - 1);
                    int a = Mathf.Min(samples.Count - 2, Mathf.FloorToInt(f));
                    Vector2 point = Vector2.Lerp(samples[a], samples[a + 1], f - a);
                    if (!Sample(moss, frame, point, 0)) visible++;
                }
                row.RootVisibility.Add(visible / (float)RootPoints);
            }
        }

        static float MinOf(List<float> values)
        {
            float m = float.MaxValue;
            foreach (float v in values) if (!float.IsNaN(v)) m = Mathf.Min(m, v);
            return m == float.MaxValue ? float.NaN : m;
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

        // ---- Output ----------------------------------------------------------------------------------

        static string CsvHeader()
        {
            var sb = new StringBuilder("species,mode,style,seed,day,nodes,iou_log,iou_direct,iou_mirror,mirror,H_P,W_P,H_mask_P,th0_H,flare_H,tiers,tiers_alternate,"
                + "foliage_components,laterals,yc_hits_005");
            for (int k = 0; k < Pads; k++)
            {
                string n = k < 6 ? "p" + (k + 1) : "dome";
                sb.Append(',').Append(n).Append("_yc_H,").Append(n).Append("_yc_H_base,").Append(n).Append("_xc,").Append(n).Append("_w,").Append(n).Append("_h");
            }
            sb.Append(",dome_W_H,dome_H_H");
            foreach (float f in ProfileFractions) sb.Append(",w_").Append(f.ToString("0.00", Inv)).Append("H");
            foreach (float f in ProfileFractions) sb.Append(",w_pot_").Append(f.ToString("0.00", Inv)).Append("H");
            sb.Append(",profile_monotone,primary_min,primary_median,primary_max,lowest_attach_H,lowest_attach_H_pot,bend_sign_changes,bend_range_H,"
                + "tips,tips_covered,twig_samples,twig_outside,roots,roots_left,roots_right,root_end_ratio,root_vis_median,root_vis_min,chord_deg,max_world_deg,"
                + "lowest_tier_opposite,zoom\n");
            return sb.ToString();
        }

        static void Append(StringBuilder csv, Row r)
        {
            csv.Append(r.Species).Append(',').Append(r.Styled ? "styled" : "wild").Append(',').Append(r.Style).Append(',').Append(r.Seed).Append(',')
                .Append(r.Day).Append(',').Append(r.Nodes).Append(',').Append(F(r.IoU)).Append(',').Append(F(r.IoUDirect)).Append(',').Append(F(r.IoUMirror))
                .Append(',').Append(r.Mirror ? "yes" : "no").Append(',').Append(F(r.HP)).Append(',').Append(F(r.WP)).Append(',').Append(F(r.HPMask))
                .Append(',').Append(F(r.Th0H)).Append(',').Append(F(r.FlareH)).Append(',').Append(r.Tiers).Append(',').Append(r.TiersAlternate ? 1 : 0)
                .Append(',').Append(r.FoliageComponents).Append(',').Append(r.Laterals).Append(',').Append(r.YcHits(0.05f));
            for (int k = 0; k < Pads; k++)
                csv.Append(',').Append(F(r.Yc[k])).Append(',').Append(F(r.YcBase[k])).Append(',').Append(F(r.Xc[k])).Append(',').Append(F(r.W[k])).Append(',').Append(F(r.H[k]));
            csv.Append(',').Append(F(r.DomeWH)).Append(',').Append(F(r.DomeHH));
            foreach (float v in r.Profile) csv.Append(',').Append(F(v));
            foreach (float v in r.ProfilePot) csv.Append(',').Append(F(v));
            csv.Append(',').Append(F(r.ProfileMonotone)).Append(',').Append(F(r.PrimaryMin)).Append(',').Append(F(r.PrimaryMedian)).Append(',').Append(F(r.PrimaryMax))
                .Append(',').Append(F(r.LowestAttachH)).Append(',').Append(F(r.LowestAttachHPot)).Append(',').Append(r.BendChanges).Append(',').Append(F(r.BendRangeH))
                .Append(',').Append(r.Tips).Append(',').Append(r.TipsCovered).Append(',').Append(r.TwigSamples).Append(',').Append(r.TwigOutside)
                .Append(',').Append(r.Roots).Append(',').Append(r.RootsLeft).Append(',').Append(r.RootsRight).Append(',').Append(F(r.RootEndRatio))
                .Append(',').Append(F(r.RootVisMedian)).Append(',').Append(F(r.RootVisMin)).Append(',').Append(F(r.ChordDeg)).Append(',').Append(F(r.MaxWorldDeg))
                .Append(',').Append(r.LowestTierOpposite ? 1 : 0).Append(',').Append(F(r.Zoom)).Append('\n');
        }

        static string Ok(bool pass) => pass ? "OK " : "FAIL";

        static bool In(float v, float lo, float hi) => !float.IsNaN(v) && v >= lo && v <= hi;

        static string Summarize(List<Row> rows)
        {
            var sb = new StringBuilder();
            sb.Append("PA-102Q metrics (BALANCE §1.4.0). Median of seeds 1…N, fixture 20261004 in []. yc/H: zero = pot top row, H = mask top above it\n")
              .Append("(as the TA masks); «base» = zero at the trunk base y = 0, H = model height. Profile: heights from y = 0, H = model height.\n\n");
            foreach (SpeciesId species in AllSpecies)
            {
                foreach (bool styled in new[] { true, false })
                {
                    List<Row> mode = rows.FindAll(r => r.Species == species && r.Styled == styled && r.Style == ShapeStyle.Moyogi && r.Day > 0 && r.Tips >= 0 && r.HMask > 0f);
                    if (mode.Count == 0) continue;
                    sb.Append("== ").Append(species).Append(styled ? " Styled" : " Wild").Append('\n');
                    foreach (int day in styled ? StyledDays : WildDays)
                    {
                        List<Row> seeds = mode.FindAll(r => r.Day == day && r.Seed != FixtureSeed);
                        Row fx = mode.Find(r => r.Day == day && r.Seed == FixtureSeed);
                        if (seeds.Count == 0 || fx == null) continue;
                        DaySummary(sb, species, styled, day, seeds, fx);
                    }
                    if (styled)
                    {
                        var r20 = new List<float>();
                        var r120 = new List<float>();
                        foreach (Row r80 in mode.FindAll(r => r.Day == 80 && r.Seed != FixtureSeed))
                        {
                            Row a = mode.Find(r => r.Day == 20 && r.Seed == r80.Seed);
                            Row c = mode.Find(r => r.Day == 120 && r.Seed == r80.Seed);
                            if (a != null) r20.Add(a.HP / r80.HP);
                            if (c != null) r120.Add(c.HP / r80.HP);
                        }
                        float m20 = MedianOf(r20), m120 = MedianOf(r120);
                        sb.AppendFormat(Inv, "AC9 tempo: H20/H80 {0:0.000} [0.55, 0.75] {1} · H120/H80 {2:0.000} [0.97, 1.08] {3}\n", m20, Ok(In(m20, 0.55f, 0.75f)),
                            m120, Ok(In(m120, 0.97f, 1.08f)));
                    }
                    else
                    {
                        List<Row> w80 = mode.FindAll(r => r.Day == 80 && r.Seed != FixtureSeed);
                        float hp = Median(w80, r => r.HP);
                        sb.AppendFormat(Inv, "AC9 Wild H80/P {0:0.00} ≤ 1.9 {1}\n", hp, Ok(hp <= 1.9f));
                    }
                    sb.Append('\n');
                }
            }

            List<Row> styleRows = rows.FindAll(r => r.HMask == 0f && r.Species == SpeciesId.Momiji);
            if (styleRows.Count > 0)
            {
                sb.Append("== AC20 styles (Momiji Styled D80, chord «base #0 → end k4» from vertical; sign changes of the chain curvature)\n");
                foreach (ShapeStyle style in new[] { ShapeStyle.Moyogi, ShapeStyle.Chokkan, ShapeStyle.Shakan })
                {
                    List<Row> sr = styleRows.FindAll(r => r.Style == style && r.Seed != FixtureSeed);
                    Row fx = styleRows.Find(r => r.Style == style && r.Seed == FixtureSeed);
                    if (sr.Count == 0) continue;
                    int n = sr.Count;
                    int chokkanOk = sr.FindAll(r => Mathf.Abs(r.ChordDeg) < 5f && r.BendChanges <= 1).Count;
                    int shakanChord = sr.FindAll(r => Mathf.Abs(r.ChordDeg) >= 15f && Mathf.Abs(r.ChordDeg) <= 30f).Count;
                    int shakanTier = sr.FindAll(r => r.LowestTierOpposite).Count;
                    int under90 = sr.FindAll(r => r.MaxWorldDeg <= 90.01f).Count;
                    int bends2 = sr.FindAll(r => r.BendChanges >= 2).Count;
                    sb.AppendFormat(Inv, "{0,-8} |chord| median {1:0.0}° [fx {2:0.0}°] min {3:0.0}° max {4:0.0}° | bends median {5:0} [fx {6}] ≥2: {7}/{8} | "
                        + "chord<5°∧≤1 change: {9}/{8} | chord 15–30°: {10}/{8} | lowest tier opposite: {11}/{8} | max|θ| ≤ 90°: {12}/{8} | H/P {13:0.00} W/P {14:0.00}",
                        style, Median(sr, r => Mathf.Abs(r.ChordDeg)), fx != null ? Mathf.Abs(fx.ChordDeg) : float.NaN, MinOf(sr.ConvertAll(r => Mathf.Abs(r.ChordDeg))),
                        -MinOf(sr.ConvertAll(r => -Mathf.Abs(r.ChordDeg))), Median(sr, r => r.BendChanges), fx != null ? fx.BendChanges : -1, bends2, n,
                        chokkanOk, shakanChord, shakanTier, under90, Median(sr, r => r.HP), Median(sr, r => r.WP));
                    if (style == ShapeStyle.Chokkan) sb.Append("  → AC20 Chokkan (≥ 90%) ").Append(Ok(chokkanOk >= 0.9f * n));
                    if (style == ShapeStyle.Shakan) sb.Append("  → AC20 Shakan (100%) ").Append(Ok(shakanChord == n && shakanTier == n && under90 == n));
                    if (style == ShapeStyle.Moyogi) sb.Append("  → bends ≥ 2 (100%) ").Append(Ok(bends2 == n));
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }

        static void DaySummary(StringBuilder sb, SpeciesId species, bool styled, int day, List<Row> seeds, Row fx)
        {
            int n = seeds.Count;
            bool momiji = species == SpeciesId.Momiji;
            bool kuro = species == SpeciesId.Kuromatsu;
            float ycTol = momiji ? 0.05f : 0.07f;
            float xTol = momiji ? 4f : 6f;
            float sizeTol = momiji ? 0.15f : 0.20f;
            int tiersOk = seeds.FindAll(r => r.Tiers >= (momiji ? 6 : 5) && r.Tiers <= 7).Count;
            int altOk = seeds.FindAll(r => r.TiersAlternate).Count;
            int bendOk = seeds.FindAll(r => r.BendChanges >= 2).Count;
            int six = seeds.FindAll(r => r.Laterals == 6).Count;
            int hits6 = seeds.FindAll(r => r.YcHits(0.05f) >= 6).Count;
            float hp = Median(seeds, r => r.HP), wp = Median(seeds, r => r.WP), th = Median(seeds, r => r.Th0H), hpm = Median(seeds, r => r.HPMask);
            sb.AppendFormat(Inv, "D{0,3}: N {1:0} | H/P {2:0.00} [{3:0.00}] (mask {4:0.00}) W/P {5:0.00} [{6:0.00}] th0/H 1/{7:0.00} | tiers {8:0} [{9}] ok {10}/{11} alt {12}/{11} "
                + "| comps {13:0} [{14}] six laterals {15}/{11} | bends {16:0} [{17}] ≥2 {18}/{11} range/H {19:0.000} | tips {20:0.0000} twigOut {21:0.0000} | IoU(log) {22:0.000} [{23:0.000}]\n",
                day, Median(seeds, r => r.Nodes), hp, fx.HP, hpm, wp, fx.WP, 1f / Mathf.Max(1e-4f, th), Median(seeds, r => r.Tiers), fx.Tiers, tiersOk, n, altOk,
                Median(seeds, r => r.FoliageComponents), fx.FoliageComponents, six, Median(seeds, r => r.BendChanges), fx.BendChanges, bendOk,
                Median(seeds, r => r.BendRangeH), Sum(seeds, r => r.TipsCovered) / Mathf.Max(1f, Sum(seeds, r => r.Tips)),
                Sum(seeds, r => r.TwigOutside) / Mathf.Max(1f, Sum(seeds, r => r.TwigSamples)), Median(seeds, r => r.IoU), fx.IoU);
            if (!styled || day < 80) return;

            // AC1–AC4 / AC12 pads.
            var line = new StringBuilder("      pad   yc/H med [fx] (ref)      | base-zero | xc med [fx] (ref)        | w med [fx] (ref)       | h med [fx] (ref)\n");
            bool ac1 = true, ac2 = true, ac3 = true, ac1fx = fx.YcHits(0.05f) >= 6, ac2fx = true, ac3fx = true, hMin = true;
            float[] refW = fx.RefW, refH = fx.RefH;
            for (int k = 0; k < Pads; k++)
            {
                int kk = k;
                float yc = Median(seeds, r => r.Yc[kk]), yb = Median(seeds, r => r.YcBase[kk]), xc = Median(seeds, r => r.Xc[kk]);
                float w = Median(seeds, r => r.W[kk]), h = Median(seeds, r => r.H[kk]);
                ac1 &= Mathf.Abs(yc - RefYc[k]) <= ycTol;
                ac2 &= Mathf.Abs(xc - RefXc[k]) <= xTol;
                ac2fx &= Mathf.Abs(fx.Xc[k] - RefXc[k]) <= 6f;
                if (k < 6)
                {
                    ac3 &= Mathf.Abs(w / refW[k] - 1f) <= sizeTol && Mathf.Abs(h / refH[k] - 1f) <= sizeTol;
                    ac3fx &= Mathf.Abs(fx.W[k] / refW[k] - 1f) <= 0.20f && Mathf.Abs(fx.H[k] / refH[k] - 1f) <= 0.20f;
                    if (kuro && k < 4) hMin &= h >= 14f;
                }
                line.AppendFormat(Inv, "      {0,-4}  {1:0.000} [{2:0.000}] ({3:0.000}) | {4:0.000}     | {5,6:0.0} [{6,6:0.0}] ({7,6:0.0}) | {8:0.0} [{9:0.0}] ({10:0.0}) | {11:0.0} [{12:0.0}] ({13:0.0})\n",
                    k < 6 ? "#" + (k + 1) : "dome", yc, fx.Yc[k], RefYc[k], yb, xc, fx.Xc[k], RefXc[k], w, fx.W[k], refW[k], h, fx.H[k], refH[k]);
            }
            float dw = Median(seeds, r => r.DomeWH), dh = Median(seeds, r => r.DomeHH);
            bool ac4 = momiji ? In(dw, 0.40f, 0.54f) && In(dh, 0.21f, 0.28f) : kuro ? In(dw, 0.37f, 0.56f) && In(dh, 0.19f, 0.29f) : true;
            bool ac4fx = Mathf.Abs(fx.DomeWH / RefDomeWH - 1f) <= 0.2f && Mathf.Abs(fx.DomeHH / RefDomeHH - 1f) <= 0.2f;
            sb.Append(line);
            sb.AppendFormat(Inv, "      AC1 yc ±{0:0.00}: {1} (≥6/7 within ±0.05: {2}/{3} ≥ 80% {4}; fixture {5}/7 {6}) | AC2 xc ±{7:0}: {8} (fixture ±6 {9}) | AC3 w,h ±{10:0}%: {11} (fixture ±20% {12}){13}\n",
                ycTol, Ok(ac1), hits6, n, Ok(hits6 >= 0.8f * n), fx.YcHits(0.05f), Ok(ac1fx), xTol, Ok(ac2), Ok(ac2fx), sizeTol * 100f, Ok(ac3), Ok(ac3fx),
                kuro ? " | h #1–#4 ≥ 14 " + Ok(hMin) : "");
            sb.AppendFormat(Inv, "      AC4 dome∪#7 W/H {0:0.000} [{1:0.000}] H/H {2:0.000} [{3:0.000}]: {4} (fixture ±20% {5})\n", dw, fx.DomeWH, dh, fx.DomeHH, Ok(ac4), Ok(ac4fx));

            // AC5 proportions.
            bool hpOk = momiji ? In(hp, 1.28f, 1.56f) : kuro ? In(hp, 1.19f, 1.60f) : In(hp, 1.42f * 0.85f, 1.42f * 1.15f);
            bool wpOk = momiji ? In(wp, 1.04f, 1.27f) : kuro ? In(wp, 1.00f, 1.35f) : wp <= 1.33f;
            bool thOk = day == 80 ? In(th, 1f / 9.35f, 1f / 7.73f) : th >= 1f / 9.35f;
            sb.AppendFormat(Inv, "      AC5 H/P {0:0.00} {1} · W/P {2:0.00} {3} (fixture {4:0.00} ≤ 1.30 {5}) · th0/H 1/{6:0.00} {7}\n", hp, Ok(hpOk), wp, Ok(wpOk), fx.WP,
                Ok(fx.WP <= 1.30f), 1f / th, Ok(thOk));

            // AC6 profile (D80), AC7 primaries, AC8 roots (D80).
            float w10 = Median(seeds, r => r.Profile[2]);
            float hm = Median(seeds, r => r.LastHeightProxy());
            float r02 = Median(seeds, r => r.Profile[0] / r.Profile[2]), r06 = Median(seeds, r => r.Profile[1] / r.Profile[2]);
            float r30 = Median(seeds, r => r.Profile[3] / r.Profile[2]), r60 = Median(seeds, r => r.Profile[4] / r.Profile[2]);
            float w10H = Median(seeds, r => r.Profile[2] / r.LastHeightProxy());
            float mono = Median(seeds, r => r.ProfileMonotone);
            int monoAll = seeds.FindAll(r => r.ProfileMonotone >= 0.999f).Count;
            float lo30 = kuro ? 0.60f : 0.65f, hi30 = kuro ? 0.90f : 0.88f, lo60 = kuro ? 0.35f : 0.38f, hi60 = kuro ? 0.65f : 0.60f;
            bool ac6 = In(w10H, 1f / 10.5f, 1f / 8f) && In(r02, 1.45f, 2.10f) && r06 <= (momiji ? 1.12f : 1.15f) && In(r30, lo30, hi30) && In(r60, lo60, hi60) && monoAll == n;
            bool ac6fx = In(fx.Profile[2] / fx.LastHeightProxy(), 1f / 10.5f, 1f / 8f) && In(fx.Profile[0] / fx.Profile[2], 1.45f, 2.10f)
                && fx.Profile[1] / fx.Profile[2] <= 1.15f && In(fx.Profile[3] / fx.Profile[2], lo30, hi30) && In(fx.Profile[4] / fx.Profile[2], lo60, hi60);
            float rp02 = Median(seeds, r => r.ProfilePot[0] / r.ProfilePot[2]), rp06 = Median(seeds, r => r.ProfilePot[1] / r.ProfilePot[2]);
            sb.AppendFormat(Inv, "      AC6 profile: w(0.10H)/H 1/{0:0.00} · w(.02)/w(.10) {1:0.00} · w(.06) {2:0.00} · w(.30) {3:0.00} · w(.60) {4:0.00} · monotone 100%: {5}/{6} → {7} (fixture {8})"
                + " | pot-zero variant: w(.02)/w(.10) {9:0.00}, w(.06) {10:0.00}{11}\n",
                1f / w10H, r02, r06, r30, r60, monoAll, n, day == 80 ? Ok(ac6) : "(info)", day == 80 ? Ok(ac6fx) : "-", rp02, rp06, float.IsNaN(hm) ? "" : "");
            int primEach = seeds.FindAll(r => r.PrimaryRatios.Count > 0 && r.PrimaryMin >= 0.28f && r.PrimaryMax <= 0.45f).Count;
            var allRatios = new List<float>();
            foreach (Row r in seeds) allRatios.AddRange(r.PrimaryRatios);
            float primMed = MedianOf(allRatios);
            float lowAttach = Median(seeds, r => r.LowestAttachH);
            float lowAttachPot = Median(seeds, r => r.LowestAttachHPot);
            bool ac7 = primEach == n && In(primMed, 0.32f, 0.40f) && In(lowAttach, 0.17f, 0.25f);
            sb.AppendFormat(Inv, "      AC7 primaries base/trunk: median {0:0.000} [0.32, 0.40], min {1:0.000} max {2:0.000}, trees with all in [0.28, 0.45] {3}/{4} (fixture {5:0.000}…{6:0.000}) · lowest attach {7:0.000} H [0.17, 0.25] (pot-zero {8:0.000}) → {9}\n",
                primMed, MinOf(allRatios), -MinOf(allRatios.ConvertAll(v => -v)), primEach, n, fx.PrimaryMin, fx.PrimaryMax, lowAttach, lowAttachPot, Ok(ac7));
            if (day == 80)
            {
                var vis = new List<float>();
                foreach (Row r in seeds) vis.AddRange(r.RootVisibility);
                float visMed = MedianOf(vis), visMin = MinOf(vis);
                int visEach = vis.FindAll(v => v >= 0.5f).Count;
                int rootsOk = seeds.FindAll(r => r.Roots == 7 && r.RootsLeft >= 2 && r.RootsRight >= 2 && r.RootEndRatio >= 0.449f).Count;
                sb.AppendFormat(Inv, "      AC8 roots: visibility median {0:0.000} (≥ 0.65) min {1:0.000}, roots ≥ 0.50: {2}/{3} → {4} · count 7 / ≥2 per side / end ≥ 0.45: {5}/{6} [fixture {7} L{8}/R{9} end {10:0.00} vis {11:0.00}]\n",
                    visMed, visMin, visEach, vis.Count, Ok(visMed >= 0.65f && visEach == vis.Count), rootsOk, n, fx.Roots, fx.RootsLeft, fx.RootsRight, fx.RootEndRatio, fx.RootVisMedian);
            }
        }

        static float Median(List<Row> rows, Func<Row, float> f)
        {
            var values = new List<float>();
            foreach (Row r in rows)
            {
                float v = f(r);
                if (!float.IsNaN(v) && !float.IsInfinity(v)) values.Add(v);
            }
            return MedianOf(values);
        }

        static float Sum(List<Row> rows, Func<Row, float> f)
        {
            float s = 0f;
            foreach (Row r in rows) s += f(r);
            return s;
        }

        static float MedianOf(List<float> values)
        {
            values.RemoveAll(v => float.IsNaN(v) || float.IsInfinity(v));
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
            StylesSheet(controller, camera, 100, Path.Combine(outDir, "styles_momiji.png"));
            StylesSheet(controller, camera, 80, Path.Combine(outDir, "styles_momiji_d80.png"));
            Compare(controller, camera, SpeciesId.Momiji, 80, PreviewArtLibrary.ReferencePath, Path.Combine(outDir, "compare_momiji.png"));
            Compare(controller, camera, SpeciesId.Momiji, 120, PreviewArtLibrary.ReferencePath, Path.Combine(outDir, "compare_momiji_d120.png"));
            Compare(controller, camera, SpeciesId.Kuromatsu, 80, NightReferencePath, Path.Combine(outDir, "compare_kuromatsu.png"));
        }

        /// <summary>AC20/AC21: Momiji Styled, fixture, Moyogi | Chokkan | Shakan side by side (full reference frames).</summary>
        static void StylesSheet(GrowthPreviewController controller, Camera camera, int day, string outPath)
        {
            controller.SetSpecies(SpeciesId.Momiji);
            controller.SetSeed(FixtureSeed);
            var styles = new[] { ShapeStyle.Moyogi, ShapeStyle.Chokkan, ShapeStyle.Shakan };
            var sheet = new Texture2D(FrameWidth * styles.Length, FrameHeight, TextureFormat.RGBA32, false);
            for (int k = 0; k < styles.Length; k++)
            {
                controller.SetShapeStyle(styles[k]);
                controller.SetDay(day);
                ReferenceFrame(controller, camera);
                sheet.SetPixels32(k * FrameWidth, 0, FrameWidth, FrameHeight, RenderPixels(camera, false));
            }
            controller.SetShapeStyle(ShapeStyle.Moyogi);
            sheet.Apply();
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);
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
