using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Tree;
using UnityEngine;

namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>
    /// ⚑T PA-102T additions of the harness (BALANCE §1.6, AC T2, T8–T19, T21): nodes by tier group, first day a group's pad is full, Wild overlap of
    /// different pads (per-pad masks: one render per emitted pad, α ≥ 0.5), merges / puff spacing, roots §1.6.4 (main / minor, forks), bark colour
    /// (CIELAB of the trunk 0.10–0.30 H, young Momiji tips vs bases), mesh identity across species bark (T18) and the criterion-B verdict (§1.6.9).
    /// </summary>
    public static partial class PreviewMetricsHarness
    {
        /// <summary>-wildOnly: only Wild rows (the T16 «before» run with the PA-102R config).</summary>
        static bool WildOnly;

        static readonly TierGroup[] BalancedGroups = { TierGroup.T0, TierGroup.T1, TierGroup.T2, TierGroup.T3, TierGroup.T4, TierGroup.T5, TierGroup.Dome };

        public sealed partial class Row
        {
            /// <summary>N_g of t0…t5, dome (rendered tree).</summary>
            public readonly int[] GroupNodes = { -1, -1, -1, -1, -1, -1, -1 };
            /// <summary>First day the group has a pad with PadSize ≥ PadFullNodes (Momiji Styled, D80 rows; −1 = never by D120).</summary>
            public int FirstFullT4 = -1, FirstFullT5 = -1, FirstFullDome = -1;
            public float OverlapShare = float.NaN;
            public int PadsHalfCovered = -1, RenderPads, SideMerges, DomeMerges, PuffsThinned, UnderlaysSkipped;
            public float MinPuffSpacing = float.NaN;
            public int RootsForked, RootsMain, RootsMainThickOk, RootsMinorThickOk;
            public float ForkVisMin = float.NaN, MainStartMinH = float.NaN, MainStartMaxH = float.NaN;
            public float BarkL = float.NaN, BarkA = float.NaN, BarkB = float.NaN, YoungTipA = float.NaN, YoungBaseA = float.NaN;
            public bool BarkSpeciesTexture;
            public int CeilingGroups = -1, CeilingOver = -1, CeilingFirstNodeCuts;
        }

        static void MeasureT(GrowthPreviewController controller, Camera camera, Row row, Rect frame, bool[] foliage, bool[] moss, bool styled, int day, uint seed)
        {
            TreePreviewMeshBuilder b = controller.Builder;
            TreeShapeAnalysis shape = b.Shape;
            for (int g = 0; g < BalancedGroups.Length; g++) row.GroupNodes[g] = shape.GroupCount(BalancedGroups[g]);
            row.SideMerges = b.LastSideMerges;
            row.DomeMerges = b.LastDomeMerges;
            row.PuffsThinned = b.LastPuffsThinned;
            row.MinPuffSpacing = b.LastMinPuffSpacing;
            row.UnderlaysSkipped = b.LastUnderlaysSkipped;
            row.RenderPads = b.LastPadRanges.Count;
            row.BarkSpeciesTexture = controller.TexturedView.BarkIsSpeciesTexture;
            int[] gap = styled ? controller.Model.PadGapStats(day) : null;
            if (gap != null && gap.Length >= 6)
            {
                row.CeilingGroups = gap[4];
                row.CeilingOver = gap[5];
            }

            if (!styled && (day == 80 || day == 120)) PadOverlap(controller, camera, row, foliage);
            RootsT(b, seed, moss, frame, row);
            if (day == 80) BarkColour(controller, camera, row, frame);
            if (styled && day == 80 && row.Species == SpeciesId.Momiji) FirstFullDays(controller, row);
        }

        /// <summary>T16: per-pad masks (one render per emitted pad), overlap share = pixels covered by ≥ 2 pads / foliage area; pads ≥ 50% covered by one other.</summary>
        static void PadOverlap(GrowthPreviewController controller, Camera camera, Row row, bool[] foliage)
        {
            TreePreviewMeshBuilder b = controller.Builder;
            TexturedPreviewView view = controller.TexturedView;
            Mesh mesh = view.PadMesh;
            int[] all = mesh.triangles;
            IReadOnlyList<TreePreviewMeshBuilder.PadRange> ranges = b.LastPadRanges;
            int pads = Mathf.Min(ranges.Count, 255);
            var count = new byte[FrameWidth * FrameHeight];
            var owners = new byte[FrameWidth * FrameHeight * 4];
            var area = new long[pads];
            view.SetTreeOnly(true, showBark: false, showPads: true);
            // One render per pad (≈ 100–200 per frame): reuse the render target and read back through the texture's own buffer — a fresh
            // Color32[] + bool[] (≈ 5 MB) per pad grew the Editor heap until the batch run was killed (exit 137, 2026-10-05; TD-076).
            var rt = new RenderTexture(FrameWidth, FrameHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            var readback = new Texture2D(FrameWidth, FrameHeight, TextureFormat.RGBA32, false);
            CameraClearFlags flags = camera.clearFlags;
            Color background = camera.backgroundColor;
            RenderTexture previous = RenderTexture.active;
            int minRow = FrameHeight - PotTopPx;
            try
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                camera.targetTexture = rt;
                for (int k = 0; k < pads; k++)
                {
                    TreePreviewMeshBuilder.PadRange r = ranges[k];
                    var sub = new int[r.IndexCount];
                    Array.Copy(all, r.IndexStart, sub, 0, r.IndexCount);
                    mesh.SetTriangles(sub, 0, true);
                    camera.Render();
                    RenderTexture.active = rt;
                    readback.ReadPixels(new Rect(0, 0, FrameWidth, FrameHeight), 0, 0);
                    readback.Apply(false);
                    Unity.Collections.NativeArray<Color32> px = readback.GetPixelData<Color32>(0);
                    // Same mask as Mask(): α ≥ 0.5, rows above the pot top only.
                    for (int i = minRow * FrameWidth; i < px.Length; i++)
                    {
                        if (px[i].a < 128) continue;
                        area[k]++;
                        int c = count[i];
                        if (c < 4) owners[i * 4 + c] = (byte)k;
                        if (c < 255) count[i] = (byte)(c + 1);
                    }
                }
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                camera.clearFlags = flags;
                camera.backgroundColor = background;
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(readback);
                mesh.SetTriangles(all, 0, true);
                view.SetTreeOnly(false);
            }
            long leaf = 0, overlap = 0;
            var pair = new long[pads, pads];
            for (int i = 0; i < count.Length; i++)
            {
                if (foliage[i]) leaf++;
                int c = count[i];
                if (c < 2) continue;
                overlap++;
                int n = Mathf.Min(4, c);
                for (int a = 0; a < n; a++)
                    for (int bb = a + 1; bb < n; bb++)
                    {
                        pair[owners[i * 4 + a], owners[i * 4 + bb]]++;
                        pair[owners[i * 4 + bb], owners[i * 4 + a]]++;
                    }
            }
            row.OverlapShare = leaf > 0 ? overlap / (float)leaf : float.NaN;
            int half = 0;
            for (int k = 0; k < pads; k++)
            {
                if (area[k] == 0) continue;
                for (int j = 0; j < pads; j++)
                {
                    if (j == k || pair[k, j] < 0.5f * area[k]) continue;
                    half++;
                    TreePreviewMeshBuilder.PadRange rk = ranges[k], rj = ranges[j];
                    Debug.LogFormat("[T16] {0} seed {1} D{2}: pad root {3} (dome {4}, members {5}, area {6}, group {7}, size {8}) covered {9:0.00} by root {10} (dome {11}, members {12}, group {13})",
                        row.Species, row.Seed, row.Day, rk.Root, rk.Dome, rk.Members, area[k], b.Shape.GroupOf(rk.Root), b.Shape.PadSize(rk.Root), pair[k, j] / (float)area[k],
                        rj.Root, rj.Dome, rj.Members, b.Shape.GroupOf(rj.Root));
                    break;
                }
            }
            row.PadsHalfCovered = half;
        }

        /// <summary>T15: roots §1.6.4 — main / minor thickness classes, forks, fork-branch visibility (32 points not under opaque moss), start of main roots.</summary>
        static void RootsT(TreePreviewMeshBuilder b, uint seed, bool[] moss, Rect frame, Row row)
        {
            var samples = new List<Vector3>();
            var fork = new List<Vector3>();
            TreeRenderConfig render = b.Render;
            float hRim = Mathf.Max(1e-3f, row.HMask);
            float th0 = Mathf.Max(1e-3f, b.LastTrunkBase);
            float visMin = float.NaN, sMin = float.NaN, sMax = float.NaN;
            for (int r = 0; r < b.LastRootCount; r++)
            {
                b.NebariRoot(seed, r, b.LastRootCount, samples, fork);
                float t = samples[0].z / th0;
                if (b.IsMainRoot(r))
                {
                    row.RootsMain++;
                    if (t >= render.NebariRootThicknessMainMin - 1e-4f && t <= render.NebariRootThicknessMainMax + 1e-4f) row.RootsMainThickOk++;
                    float start = (samples[0].y - row.PotTopY) / hRim;
                    sMin = float.IsNaN(sMin) ? start : Mathf.Min(sMin, start);
                    sMax = float.IsNaN(sMax) ? start : Mathf.Max(sMax, start);
                }
                else if (t >= render.NebariRootThicknessMinorMin - 1e-4f && t <= render.NebariRootThicknessMinorMax + 1e-4f) row.RootsMinorThickOk++;
                if (fork.Count < 2) continue;
                row.RootsForked++;
                int visible = 0;
                for (int k = 0; k < RootPoints; k++)
                {
                    float f = (fork.Count - 1) * k / (float)(RootPoints - 1);
                    int a = Mathf.Min(fork.Count - 2, Mathf.FloorToInt(f));
                    if (!Sample(moss, frame, Vector2.Lerp(fork[a], fork[a + 1], f - a), 0)) visible++;
                }
                float v = visible / (float)RootPoints;
                visMin = float.IsNaN(visMin) ? v : Mathf.Min(visMin, v);
            }
            row.ForkVisMin = visMin;
            row.MainStartMinH = sMin;
            row.MainStartMaxH = sMax;
        }

        /// <summary>T17: mean CIELAB of the trunk bark (bark-only render, |x − axis| ≤ 0.35·w_trunk, rows 0.10–0.30 H above y_rim) and of young Momiji shoots
        /// (Thickness ≤ 2.5, ord ≥ 1): centre-line pixels at t 0.15 (base) and 0.85·BarkEndT (tip).</summary>
        static void BarkColour(GrowthPreviewController controller, Camera camera, Row row, Rect frame)
        {
            TreePreviewMeshBuilder b = controller.Builder;
            TexturedPreviewView view = controller.TexturedView;
            view.SetTreeOnly(true, showBark: true, showPads: false);
            Color32[] px;
            try { px = RenderPixels(camera, true); }
            finally { view.SetTreeOnly(false); }
            float hRim = Mathf.Max(1e-3f, row.HMask);
            float upp = frame.width / FrameWidth;
            double L = 0, A = 0, B = 0;
            int n = 0;
            for (float y = row.PotTopY + 0.10f * hRim; y <= row.PotTopY + 0.30f * hRim; y += upp)
            {
                float cx = TrunkAxisX(b, y), w = b.TrunkWidthAtHeight(y);
                if (float.IsNaN(cx) || float.IsNaN(w)) continue;
                int py = Mathf.FloorToInt((y - frame.yMin) / upp);
                for (float x = cx - 0.35f * w; x <= cx + 0.35f * w; x += upp)
                {
                    int pxi = Mathf.FloorToInt((x - frame.xMin) / upp);
                    if (pxi < 0 || pxi >= FrameWidth || py < 0 || py >= FrameHeight) continue;
                    Color32 c = px[py * FrameWidth + pxi];
                    if (c.a < 128) continue;
                    Lab(c, out float l, out float a, out float bb);
                    L += l; A += a; B += bb;
                    n++;
                }
            }
            if (n > 0)
            {
                row.BarkL = (float)(L / n);
                row.BarkA = (float)(A / n);
                row.BarkB = (float)(B / n);
            }
            double tipA = 0, baseA = 0;
            int tips = 0, bases = 0;
            TreeShapeAnalysis shape = b.Shape;
            for (int i = 1; i < b.NodeCount; i++)
            {
                NodeData node = b.Node(i);
                if (shape.Order(i) < 1 || node.Thickness > b.Render.BarkYoungMaxThickness || node.Status == NodeStatus.Bud) continue;
                if (SampleA(px, frame, b.PointOn(i, 0.15f), out float a0)) { baseA += a0; bases++; }
                if (shape.ApicalChild(i) < 0 && SampleA(px, frame, b.PointOn(i, 0.85f * b.BarkEndT(i)), out float a1)) { tipA += a1; tips++; }
            }
            if (tips > 0) row.YoungTipA = (float)(tipA / tips);
            if (bases > 0) row.YoungBaseA = (float)(baseA / bases);
        }

        static bool SampleA(Color32[] px, Rect frame, Vector2 world, out float a)
        {
            a = 0f;
            int x = Mathf.FloorToInt((world.x - frame.xMin) / frame.width * FrameWidth), y = Mathf.FloorToInt((world.y - frame.yMin) / frame.height * FrameHeight);
            if (x < 0 || y < 0 || x >= FrameWidth || y >= FrameHeight) return false;
            Color32 c = px[y * FrameWidth + x];
            if (c.a < 200) return false;
            Lab(c, out _, out a, out _);
            return true;
        }

        /// <summary>sRGB (D65) → CIELAB.</summary>
        public static void Lab(Color32 c, out float l, out float a, out float b)
        {
            float r = Lin(c.r / 255f), g = Lin(c.g / 255f), bl = Lin(c.b / 255f);
            float x = (0.4124f * r + 0.3576f * g + 0.1805f * bl) / 0.95047f;
            float y = 0.2126f * r + 0.7152f * g + 0.0722f * bl;
            float z = (0.0193f * r + 0.1192f * g + 0.9505f * bl) / 1.08883f;
            float fx = F3(x), fy = F3(y), fz = F3(z);
            l = 116f * fy - 16f;
            a = 500f * (fx - fy);
            b = 200f * (fy - fz);
        }

        static float Lin(float v) => v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
        static float F3(float t) => t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;

        /// <summary>T2: first day (1…120) a pad of t4 / t5 / dome has PadSize ≥ PadFullNodes (leafy pad nodes) on the Styled tree.</summary>
        static void FirstFullDays(GrowthPreviewController controller, Row row)
        {
            GrowthPreviewModel model = controller.Model;
            var shape = new TreeShapeAnalysis();
            int full = model.Config.PadFullNodes;
            for (int d = 1; d <= GrowthPreviewModel.MaxPreviewDay && (row.FirstFullT4 < 0 || row.FirstFullT5 < 0 || row.FirstFullDome < 0); d++)
            {
                TreeGraph tree = model.GetTree(d);
                shape.Analyze(tree, model.Config, model.SpeciesConfig);
                for (int i = 0; i < tree.Count; i++)
                {
                    if (!shape.IsLeafy(i) || shape.PadId(i) != i || shape.PadSize(i) < full) continue;
                    TierGroup g = shape.GroupOf(i);
                    if (g == TierGroup.T4 && row.FirstFullT4 < 0) row.FirstFullT4 = d;
                    if (g == TierGroup.T5 && row.FirstFullT5 < 0) row.FirstFullT5 = d;
                    if (g == TierGroup.Dome && row.FirstFullDome < 0) row.FirstFullDome = d;
                }
            }
        }

        // ---- CSV / summary -----------------------------------------------------------------------------------

        static string CsvHeaderT() =>
            "N_t0,N_t1,N_t2,N_t3,N_t4,N_t5,N_dome,first_full_t4,first_full_t5,first_full_dome,overlap_share,pads_half_covered,render_pads,side_merges,dome_merges,"
            + "puffs_thinned,min_puff_spacing_R,underlays_skipped,roots_forked,roots_main,roots_main_thick_ok,roots_minor_thick_ok,fork_vis_min,main_start_min_H,"
            + "main_start_max_H,bark_L,bark_a,bark_b,young_tip_a,young_base_a,bark_species_texture,s10_groups,s10_over";

        static void AppendT(StringBuilder csv, Row r)
        {
            foreach (int v in r.GroupNodes) csv.Append(v).Append(',');
            csv.Append(r.FirstFullT4).Append(',').Append(r.FirstFullT5).Append(',').Append(r.FirstFullDome).Append(',').Append(F(r.OverlapShare)).Append(',')
                .Append(r.PadsHalfCovered).Append(',').Append(r.RenderPads).Append(',').Append(r.SideMerges).Append(',').Append(r.DomeMerges).Append(',')
                .Append(r.PuffsThinned).Append(',').Append(F(r.MinPuffSpacing)).Append(',').Append(r.UnderlaysSkipped).Append(',').Append(r.RootsForked).Append(',')
                .Append(r.RootsMain).Append(',').Append(r.RootsMainThickOk).Append(',').Append(r.RootsMinorThickOk).Append(',').Append(F(r.ForkVisMin)).Append(',')
                .Append(F(r.MainStartMinH)).Append(',').Append(F(r.MainStartMaxH)).Append(',').Append(F(r.BarkL)).Append(',').Append(F(r.BarkA)).Append(',')
                .Append(F(r.BarkB)).Append(',').Append(F(r.YoungTipA)).Append(',').Append(F(r.YoungBaseA)).Append(',').Append(r.BarkSpeciesTexture ? 1 : 0).Append(',')
                .Append(r.CeilingGroups).Append(',').Append(r.CeilingOver);
        }

        static float Pct(List<Row> rows, Func<Row, float> f, float p)
        {
            var v = new List<float>();
            foreach (Row r in rows)
            {
                float x = f(r);
                if (!float.IsNaN(x)) v.Add(x);
            }
            if (v.Count == 0) return float.NaN;
            v.Sort();
            return v[Mathf.Clamp(Mathf.FloorToInt(p * (v.Count - 1) + 1e-4f), 0, v.Count - 1)];
        }

        static void SummarizeT(StringBuilder sb, List<Row> rows)
        {
            sb.Append("\n==== PA-102T (BALANCE §1.6) — AC T2, T8–T19, criterion B (§1.6.9). Medians of seeds 1…N; reference — PA-102R table ====\n");
            string[] gn = { "t0", "t1", "t2", "t3", "t4", "t5", "dome" };
            bool b1 = false, b2 = false, b3 = false, b4 = false;
            foreach (SpeciesId species in AllSpecies)
            {
                List<Row> st = rows.FindAll(r => r.Species == species && r.Styled && r.Style == ShapeStyle.Moyogi && r.HMask > 0f && r.Seed != FixtureSeed);
                if (st.Count > 0)
                {
                    sb.Append("== ").Append(species).Append(" Styled — nodes by tier group (after styling), median [p20]\n");
                    foreach (int day in StyledDays)
                    {
                        List<Row> d = st.FindAll(r => r.Day == day);
                        if (d.Count == 0) continue;
                        sb.AppendFormat(Inv, "  D{0,3}:", day);
                        for (int g = 0; g < 7; g++)
                        {
                            int gg = g;
                            sb.AppendFormat(Inv, " {0} {1:0} [{2:0}]", gn[g], Median(d, r => r.GroupNodes[gg]), Pct(d, r => r.GroupNodes[gg], 0.2f));
                        }
                        int over = d.FindAll(r => r.GroupNodes[0] > 15 || r.GroupNodes[1] > 15).Count;
                        sb.AppendFormat(Inv, " | t0/t1 > 15: {0}/{1} | S10 groups over T_g after the pass: {2}\n", over, d.Count, Sum(d, r => Math.Max(0, r.CeilingOver)));
                    }
                    if (species == SpeciesId.Momiji)
                    {
                        List<Row> d80 = st.FindAll(r => r.Day == 80);
                        float t4 = Median(d80, r => r.GroupNodes[4]), t5 = Median(d80, r => r.GroupNodes[5]), dome = Median(d80, r => r.GroupNodes[6]);
                        float p20 = Pct(d80, r => r.GroupNodes[5], 0.2f);
                        bool cap = d80.TrueForAll(r => r.GroupNodes[0] <= 15 && r.GroupNodes[1] <= 15);
                        float f4 = Median(d80, r => r.FirstFullT4 < 0 ? 999 : r.FirstFullT4), f5 = Median(d80, r => r.FirstFullT5 < 0 ? 999 : r.FirstFullT5);
                        float fd = Median(d80, r => r.FirstFullDome < 0 ? 999 : r.FirstFullDome);
                        bool t2 = t4 >= 8 && t5 >= 6 && dome >= 14 && p20 >= 5 && cap && f5 <= 58 && fd <= 58 && f4 <= 45;
                        sb.AppendFormat(Inv, "T2 D80 N(t4) {0:0} ≥ 8, N(t5) {1:0} ≥ 6 (p20 {2:0} ≥ 5), N(dome) {3:0} ≥ 14, t0/t1 ≤ 15 in 100% {4}; first day PadSize ≥ PadFullNodes: "
                            + "t4 D{5:0} ≤ 45, t5 D{6:0} ≤ 58, dome D{7:0} ≤ 58 → {8}\n", t4, t5, p20, dome, cap ? "yes" : "no", f4, f5, fd, Ok(t2));
                        b4 = t5 < 5f;
                        // Criterion B (§1.6.9) items 1–3.
                        foreach (int day in AcDays)
                        {
                            List<Row> d = st.FindAll(r => r.Day == day);
                            if (d.Count == 0) continue;
                            int six = d.FindAll(r => r.Laterals == 6).Count;
                            if (six < 0.8f * d.Count) b1 = true;
                            if (Median(d, r => r.W[4]) < 0.85f * RefWL2[4] || Median(d, r => r.W[5]) < 0.85f * RefWL2[5]) b3 = true;
                        }
                        List<Row> m80 = st.FindAll(r => r.Day == 80);
                        float dw = Median(m80, r => r.DomeWH), dh = Median(m80, r => r.DomeHH);
                        b2 = dw < 0.40f || dh < 0.21f;
                        var line = new StringBuilder();
                        foreach (int day in AcDays)
                        {
                            List<Row> d = st.FindAll(r => r.Day == day);
                            line.AppendFormat(Inv, " D{0} six {1}/{2} w#5 {3:0.0} w#6 {4:0.0} h#1–#6 {5:0.0}/{6:0.0}/{7:0.0}/{8:0.0}/{9:0.0}/{10:0.0} merges(side) {11:0};",
                                day, d.FindAll(r => r.Laterals == 6).Count, d.Count, Median(d, r => r.W[4]), Median(d, r => r.W[5]), Median(d, r => r.H[0]),
                                Median(d, r => r.H[1]), Median(d, r => r.H[2]), Median(d, r => r.H[3]), Median(d, r => r.H[4]), Median(d, r => r.H[5]), Sum(d, r => r.SideMerges));
                        }
                        sb.Append("T11/T10/T16(Styled):").Append(line).Append('\n');
                        sb.AppendFormat(Inv, "T21 criterion B: (1) six separate laterals < 80% on some day D80…D120: {0}; (2) dome∪#7 D80 W/H {1:0.000} < 0.40 or H/H {2:0.000} < 0.21: {3}; "
                            + "(3) median w#5 < 32.6 or w#6 < 30.1 on some day: {4}; (4) median N(t5) D80 {5:0} < 5: {6} → {7}\n",
                            b1 ? "YES" : "no", dw, dh, b2 ? "YES" : "no", b3 ? "YES" : "no", t5, b4 ? "YES" : "no", b1 || b2 || b3 || b4 ? "TRANSITION TO B (owner)" : "stay on A+");
                    }
                }
                List<Row> wild = rows.FindAll(r => r.Species == species && !r.Styled && r.Seed != FixtureSeed && !float.IsNaN(r.OverlapShare));
                foreach (int day in new[] { 80, 120 })
                {
                    List<Row> d = wild.FindAll(r => r.Day == day);
                    if (d.Count == 0) continue;
                    float med = Median(d, r => r.OverlapShare), max = -MinOf(d.ConvertAll(r => -r.OverlapShare));
                    int half = d.FindAll(r => r.PadsHalfCovered > 0).Count;
                    float spacing = MinOf(d.ConvertAll(r => r.MinPuffSpacing));
                    bool ok = med <= 0.08f && max <= 0.15f && half == 0 && !(spacing < 0.6f - 1e-3f);
                    sb.AppendFormat(Inv, "T16 {0} Wild D{1}: overlap of different pads median {2:0.0}% (≤ 8) max {3:0.0}% (≤ 15) · seeds with a pad ≥ 50% covered {4}/{5} (0) · "
                        + "min puff spacing {6:0.00}·R (≥ 0.60) · merges side {7:0} dome {8:0} · render pads {9:0} → {10}\n", species, day, 100f * med, 100f * max, half, d.Count,
                        spacing, Median(d, r => r.SideMerges), Median(d, r => r.DomeMerges), Median(d, r => r.RenderPads), Ok(ok));
                }
                List<Row> r80 = rows.FindAll(r => r.Species == species && r.Styled && r.Day == 80 && r.HMask > 0f && r.Seed != FixtureSeed && r.Style == ShapeStyle.Moyogi);
                if (r80.Count > 0)
                {
                    int countOk = r80.FindAll(r => r.Roots == Mathf.Clamp((int)Math.Round(2.0 + (r.Th0H * r.HMask - 4.0) / 1.6, MidpointRounding.AwayFromZero), 2, 10)).Count;
                    int sidesOk = r80.FindAll(r => r.RootsLeft >= 3 && r.RootsRight >= 3).Count;
                    int mainOk = r80.FindAll(r => r.RootsMainThickOk == r.RootsMain && r.RootsMinorThickOk == r.Roots - r.RootsMain).Count;
                    int forkOk = r80.FindAll(r => r.RootsForked >= 2 && r.ForkVisMin >= 0.5f).Count;
                    // As PA-102R AC9: the start of the main roots = the highest one (the lower-side root of a leaning base starts lower — logged as «min»).
                    int startOk = r80.FindAll(r => r.MainStartMaxH >= 0.025f && r.MainStartMaxH <= 0.05f).Count;
                    var vis = new List<float>();
                    foreach (Row r in r80) vis.AddRange(r.RootVisibility);
                    float visMed = MedianOf(new List<float>(vis)), visMin = MinOf(vis);
                    bool t15 = countOk == r80.Count && sidesOk == r80.Count && mainOk == r80.Count && forkOk == r80.Count && startOk == r80.Count && visMed >= 0.65f && visMin >= 0.5f;
                    sb.AppendFormat(Inv, "T15 {0} roots D80: count {1:0} (formula ok {2}/{3}) · ≥ 3 per side {4}/{3} · thickness classes {5}/{3} · ≥ 2 forks, branch vis ≥ 0.5 {6}/{3} · "
                        + "main start [0.025, 0.05] H {7}/{3} (median {8:0.000}…{9:0.000}) · visibility median {10:0.000} min {11:0.000} → {12}\n",
                        species, Median(r80, r => r.Roots), countOk, r80.Count, sidesOk, mainOk, forkOk, startOk, Median(r80, r => r.MainStartMinH),
                        Median(r80, r => r.MainStartMaxH), visMed, visMin, Ok(t15));
                    float L = Median(r80, r => r.BarkL), a = Median(r80, r => r.BarkA), bb = Median(r80, r => r.BarkB);
                    string verdict = species == SpeciesId.Kuromatsu ? Ok(L <= 38f)
                        : species == SpeciesId.Momiji ? Ok(L >= 45f && L <= 62f && Mathf.Abs(a) <= 8f && bb >= 8f) : Ok(a >= 12f && a > bb);
                    sb.AppendFormat(Inv, "T17 {0} trunk bark CIELAB L* {1:0.0} a* {2:0.0} b* {3:0.0} (species texture: {4}) → {5}", species, L, a, bb,
                        r80.Exists(r => r.BarkSpeciesTexture) ? "yes" : "no — placeholder tone", verdict);
                    if (species == SpeciesId.Momiji)
                    {
                        float tip = Median(r80, r => r.YoungTipA), bas = Median(r80, r => r.YoungBaseA);
                        sb.AppendFormat(Inv, " · young shoots a* tips {0:0.0} vs bases {1:0.0} (+8) → {2}", tip, bas, Ok(tip >= bas + 8f));
                    }
                    sb.Append('\n');
                }
            }
            // T17 ΔE between species.
            var labs = new List<Vector3>();
            foreach (SpeciesId species in AllSpecies)
            {
                List<Row> r80 = rows.FindAll(r => r.Species == species && r.Styled && r.Day == 80 && r.HMask > 0f && r.Seed != FixtureSeed && r.Style == ShapeStyle.Moyogi);
                if (r80.Count > 0) labs.Add(new Vector3(Median(r80, r => r.BarkL), Median(r80, r => r.BarkA), Median(r80, r => r.BarkB)));
            }
            if (labs.Count == 3)
            {
                float d01 = (labs[0] - labs[1]).magnitude, d02 = (labs[0] - labs[2]).magnitude, d12 = (labs[1] - labs[2]).magnitude;
                sb.AppendFormat(Inv, "T17 ΔE Kuromatsu–Momiji {0:0.0} · Kuromatsu–Yamazakura {1:0.0} · Momiji–Yamazakura {2:0.0} (each ≥ 15) → {3}\n", d01, d02, d12,
                    Ok(Mathf.Min(d01, Mathf.Min(d02, d12)) >= 15f));
            }
        }

        // ---- Images ------------------------------------------------------------------------------------------

        /// <summary>T20 PNGs of PA-102T: Wild overlap before/after the merge rule, bark of 3 species (trunk crops).</summary>
        static void CaptureImagesT(GrowthPreviewController controller, Camera camera, string outDir)
        {
            controller.SetSeed(FixtureSeed);
            controller.SetStyled(false);
            const int cellW = FrameWidth / 2, cellH = FrameHeight / 2;
            int[] days = { 80, 120 };
            var sheet = new Texture2D(cellW * 6, cellH * 2, TextureFormat.RGBA32, false);
            float ratio = FoliageConfig.FieldOverrides.TryGetValue("PadMergeOverlapRatio", out float rv) ? rv : new FoliageConfig().PadMergeOverlapRatio;
            float spacing = FoliageConfig.FieldOverrides.TryGetValue("PuffMinSpacingRatio", out float sv) ? sv : new FoliageConfig().PuffMinSpacingRatio;
            for (int s = 0; s < AllSpecies.Length; s++)
            {
                controller.SetSpecies(AllSpecies[s]);
                for (int d = 0; d < days.Length; d++)
                {
                    controller.SetDay(days[d]);
                    for (int m = 0; m < 2; m++)
                    {
                        // m = 0: before (no merge, no puff thinning), m = 1: after.
                        FoliageConfig.FieldOverrides["PadMergeOverlapRatio"] = m == 0 ? 0f : ratio;
                        FoliageConfig.FieldOverrides["PuffMinSpacingRatio"] = m == 0 ? 0f : spacing;
                        controller.TexturedView.Rebuild(controller.Model.GetTree(days[d]), controller.Model, controller.Builder, controller.Art);
                        FitFrame(controller, camera);
                        Blit(sheet, RenderPixels(camera, false), (s * 2 + m) * cellW, (1 - d) * cellH);
                    }
                }
            }
            FoliageConfig.FieldOverrides["PadMergeOverlapRatio"] = ratio;
            FoliageConfig.FieldOverrides["PuffMinSpacingRatio"] = spacing;
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(outDir, "wild_overlap_before_after.png"), sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet);

            // Bark: 3 species, Styled D80 fixture, trunk crop (0 … 0.45 H, 2.4× scale) — and full frames under it.
            controller.SetStyled(true);
            const int crop = 512;
            var bark = new Texture2D(crop * 3, crop + cellH, TextureFormat.RGBA32, false);
            for (int s = 0; s < AllSpecies.Length; s++)
            {
                controller.SetSpecies(AllSpecies[s]);
                controller.SetDay(80);
                controller.TexturedView.Rebuild(controller.Model.GetTree(80), controller.Model, controller.Builder, controller.Art);
                Rect frame = ReferenceFrame(controller, camera);
                Color32[] full = RenderPixels(camera, false);
                float potTop = controller.TexturedView.PotRect.yMax;
                float h = controller.Builder.LastHeight;
                Vector2 centre = new Vector2(TrunkAxisXSafe(controller.Builder, potTop + 0.2f * h), potTop + 0.18f * h);
                bark.SetPixels32(s * crop, cellH, crop, crop, Crop(full, centre, 0.42f * h, frame, crop));
                var cell = new Color32[cellH * crop];
                for (int y = 0; y < cellH; y++)
                    for (int x = 0; x < crop; x++)
                    {
                        int fx = Mathf.Clamp((FrameWidth - 2 * crop) / 2 + 2 * x, 0, FrameWidth - 1);
                        cell[y * crop + x] = full[(2 * y) * FrameWidth + fx];
                    }
                bark.SetPixels32(s * crop, 0, crop, cellH, cell);
            }
            bark.Apply();
            File.WriteAllBytes(Path.Combine(outDir, "bark_species.png"), bark.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(bark);

            // T18: the same tree with the bark of each species — bark mesh and renderer count identical.
            controller.SetSpecies(SpeciesId.Momiji);
            controller.SetDay(80);
            TexturedPreviewView view = controller.TexturedView;
            Vector3[] refPos = null;
            Vector2[] refUv = null;
            bool same = true;
            foreach (SpeciesId sp in AllSpecies)
            {
                view.BarkSpeciesOverride = sp;
                view.Rebuild(controller.Model.GetTree(80), controller.Model, controller.Builder, controller.Art);
                Vector3[] pos = view.BarkMesh.vertices;
                var uvs = new List<Vector2>();
                view.BarkMesh.GetUVs(0, uvs);
                if (refPos == null)
                {
                    refPos = pos;
                    refUv = uvs.ToArray();
                    continue;
                }
                same &= pos.Length == refPos.Length && uvs.Count == refUv.Length;
                for (int i = 0; same && i < pos.Length; i++) same &= pos[i] == refPos[i] && uvs[i] == refUv[i];
            }
            view.BarkSpeciesOverride = null;
            view.Rebuild(controller.Model.GetTree(80), controller.Model, controller.Builder, controller.Art);
            string t18 = string.Format(Inv, "T18 bark mesh / UV identical for the bark of 3 species (Momiji fixture D80): {0}; twist shear by species {1:0.00}/{2:0.00}/{3:0.00}\n",
                Ok(same), SpeciesRenderProfile.For(SpeciesId.Kuromatsu).BarkTwistShear, SpeciesRenderProfile.For(SpeciesId.Momiji).BarkTwistShear,
                SpeciesRenderProfile.For(SpeciesId.Yamazakura).BarkTwistShear);
            File.AppendAllText(Path.Combine(outDir, "summary.txt"), t18);
        }

        static float TrunkAxisXSafe(TreePreviewMeshBuilder b, float y)
        {
            float x = TrunkAxisX(b, y);
            return float.IsNaN(x) ? 0f : x;
        }
    }
}
