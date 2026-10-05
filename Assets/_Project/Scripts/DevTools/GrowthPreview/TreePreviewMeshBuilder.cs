using System;
using System.Collections.Generic;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Tree;
using UnityEngine;

namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>
    /// Debug mesh of a tree (PA-102V/PA-102S preview, not the PA-103 renderer). BALANCE §1.2 render rules, simplified:
    /// every axis (chain of apical children) is a G1/C1 spline of cubic Beziers through its joints (tangent = bisector of
    /// adjacent chords), Curvature added as a bump 12t²(1−t)² that keeps the joint tangents; a lateral starts on its
    /// parent's curve at AttachT; width w(t) = lerp(Thickness, tipWidth, t) with JointBlend; Nebari roots + base flare;
    /// foliage only on IsLeafy nodes, as puffs merged into flat-bottomed, domed pad silhouettes with a darker underside.
    /// World: 1 unit = 1 biological unit, soil / Pot Lip at y = 0 (Unity Y-up), positive angle = counter-clockwise.
    /// One mesh, vertex colours, drawn in append order. Dev-only: allocations are fine.
    /// </summary>
    public sealed partial class TreePreviewMeshBuilder
    {
        const int SegmentSamples = 12;

        /// <summary>ADR-0004 rev.4: extra ribbon sections of #0 inside the Gaussian flare (the flare is ~3 units tall, a section ~3 units).</summary>
        const int FlareExtraSections = 7;
        const int DiscSegments = 18;
        const float BudRadius = 1.4f;
        const float PotLipOverhang = 4f;
        const float PotLipThickness = 1.4f;
        const float PuffLift = 0.35f;

        // Dev colours (bark/pot are placeholders; leaf colours are BALANCE §3).
        static readonly Color32 Bark = new Color32(0x5D, 0x43, 0x37, 0xFF);
        static readonly Color32 BarkDark = new Color32(0x4A, 0x34, 0x2A, 0xFF);
        static readonly Color32 Pot = new Color32(0xB0, 0x6E, 0x4C, 0xFF);
        static readonly Color32 PotLip = new Color32(0x8E, 0x55, 0x3A, 0xFF);
        static readonly Color32 Soil = new Color32(0x4F, 0x5E, 0x2E, 0xFF); // moss
        static readonly Color32 BudColor = new Color32(0xC9, 0xD8, 0x6B, 0xFF);

        readonly TreeRenderConfig _render = new TreeRenderConfig();
        readonly TreeShapeAnalysis _shape = new TreeShapeAnalysis();
        readonly List<Vector3> _vertices = new List<Vector3>(32768);
        readonly List<Color32> _colors = new List<Color32>(32768);
        readonly List<int> _indices = new List<int>(98304);
        readonly List<Vector4> _puffs = new List<Vector4>(256); // cx, cy, rx, ry
        readonly List<Vector2> _trunkSamples = new List<Vector2>(256); // (y, width) along the trunk chain
        readonly List<Vector3> _rootSamples = new List<Vector3>(32); // (x, y, width) of one Nebari root

        readonly Vector2[] _dir = new Vector2[TreeGraph.Capacity];
        readonly Vector2[] _base = new Vector2[TreeGraph.Capacity];
        readonly Vector2[] _tip = new Vector2[TreeGraph.Capacity];
        readonly Vector2[] _tanBase = new Vector2[TreeGraph.Capacity];
        readonly Vector2[] _tanTip = new Vector2[TreeGraph.Capacity];
        readonly float[] _tipWidth = new float[TreeGraph.Capacity];

        NodeData[] _nodes = new NodeData[TreeGraph.Capacity];
        int _count;
        bool _linear;
        float _th0;
        float _flare;
        float _puffRadius;
        int _chainTopDepth;
        float _swayScale = 1f;
        int _tierPhase = 1;

        public TreeRenderConfig Render => _render;

        /// <summary>Sections of node <paramref name="i"/>'s ribbon and the parameter of section <paramref name="s"/>: #0 gets
        /// <see cref="FlareExtraSections"/> more, packed towards the base (t ∝ (s/n)² on the first half), so the Gaussian flare is drawn.</summary>
        int Sections(int i) => i == 0 && _render.NebariFlareProfile == NebariFlareProfile.Gaussian ? SegmentSamples + FlareExtraSections : SegmentSamples;

        static float SectionT(int s, int sections, bool flare)
        {
            float u = s / (float)sections;
            if (!flare) return u;
            // Monotone and continuous: quadratic packing below the middle (dense at the base), linear above.
            return u < 0.5f ? 2f * u * u : u;
        }

        // ---- Read access for the PA-102P measurement harness (last build) ----
        public TreeShapeAnalysis Shape => _shape;
        public int NodeCount => _count;
        public NodeData Node(int i) => _nodes[i];
        public Vector2 TipOf(int i) => _tip[i];
        public Vector2 PointOn(int i, float t) => Eval(i, t);

        /// <summary>Centre line of the trunk chain (C1 spline of #0 … the chain top, BALANCE §1.3.1 «Вимір вигинів»), dense samples.</summary>
        public void TrunkAxis(List<Vector2> points, int samplesPerNode = 32)
        {
            points.Clear();
            int i = 0;
            while (i >= 0 && i < _count && _shape.IsTrunkChain(i))
            {
                for (int s = points.Count == 0 ? 0 : 1; s <= samplesPerNode; s++) points.Add(Eval(i, s / (float)samplesPerNode));
                int next = _shape.ApicalChild(i);
                i = next >= 0 && _shape.IsTrunkChain(next) ? next : -1;
            }
        }

        /// <summary>
        /// ⚑Q BALANCE §1.4.0 «Профіль стовбура»: width of the trunk mesh (with the Nebari flare, without roots and collars) at the point of the
        /// trunk-chain centre line whose height is <paramref name="y"/> (the lowest crossing); NaN if the chain does not reach it.
        /// </summary>
        public float TrunkWidthAtHeight(float y)
        {
            int i = 0;
            while (i >= 0 && i < _count && _shape.IsTrunkChain(i))
            {
                float y0 = Eval(i, 0f).y, y1 = Eval(i, 1f).y;
                if ((y0 - y) * (y1 - y) <= 0f && !Mathf.Approximately(y0, y1))
                {
                    float lo = 0f, hi = 1f;
                    bool rising = y1 > y0;
                    for (int k = 0; k < 24; k++)
                    {
                        float mid = 0.5f * (lo + hi);
                        if ((Eval(i, mid).y < y) == rising) lo = mid;
                        else hi = mid;
                    }
                    float t = 0.5f * (lo + hi);
                    return Width(i, t, Eval(i, t).y);
                }
                int next = _shape.ApicalChild(i);
                i = next >= 0 && _shape.IsTrunkChain(next) ? next : -1;
            }
            return float.NaN;
        }

        /// <summary>Height of the top of the trunk chain (tip of its last node) of the last build.</summary>
        public float TrunkChainTopY
        {
            get
            {
                float top = 0f;
                for (int i = 0; i < _count; i++) if (_shape.IsTrunkChain(i)) top = Mathf.Max(top, _tip[i].y);
                return top;
            }
        }

        /// <summary>Mesh width of node <paramref name="i"/> at its base (no collar) and of its parent at the attachment point (§1.4.0 primaries).</summary>
        public void BaseWidths(int i, out float own, out float parentAtAttach, out float attachY)
        {
            NodeData n = _nodes[i];
            Vector2 attach = Eval(n.ParentIndex, (float)n.AttachT);
            attachY = attach.y;
            own = Width(i, 0f, attach.y);
            parentAtAttach = Width(n.ParentIndex, (float)n.AttachT, attach.y);
        }

        /// <summary>AC20: base width with Nebari flare / trunk width at 2/3 of the trunk-chain height (last build).</summary>
        public float LastTaperRatio { get; private set; }

        public int LastPadCount { get; private set; }

        /// <summary>PA-102S proportions (last build, units): tree height above the Pot Lip incl. foliage.</summary>
        public float LastHeight { get; private set; }

        /// <summary>Bark + foliage extents (roots and pot excluded, y from 0) of the last build (PA-102Q metrics).</summary>
        public Rect LastTreeRect { get; private set; }

        /// <summary>ADR-0004 rev.5 / AC20: min/max of every written vertex of the bark mesh (trunk, flare, roots, collars) and of the pad mesh
        /// (sprites, shadow underlays, tip sprites) — the camera frame bounds (pot excluded).</summary>
        public Rect LastBounds { get; private set; }

        /// <summary>Crown width: x-extent of bark + foliage (roots and pot excluded).</summary>
        public float LastCrownWidth { get; private set; }

        /// <summary>Trunk base width: Thickness(#0) and with the Nebari flare th0·(1 + F).</summary>
        public float LastTrunkBase { get; private set; }
        public float LastTrunkBaseFlared { get; private set; }

        /// <summary>⚑P BALANCE §1.3.0: pot width P (outer front rim) = TreeRenderConfig.PotWidthUnits (fixed, 92 units).</summary>
        public float LastPotWidth { get; private set; }

        /// <summary>Nebari roots drawn by the last build (BALANCE §1.3.3).</summary>
        public int LastRootCount { get; private set; }

        /// <summary>Nebari flare F of the last build: base width = th0·(1 + F).</summary>
        public float LastFlare => _flare;

        public static Color32 LeafColor(SpeciesId species)
        {
            switch (species)
            {
                case SpeciesId.Momiji: return new Color32(0x5C, 0x8A, 0x42, 0xFF);    // #5C8A42
                case SpeciesId.Yamazakura: return new Color32(0x6A, 0x99, 0x4E, 0xFF); // #6A994E
                default: return new Color32(0x2E, 0x4F, 0x2D, 0xFF);                  // #2E4F2D Kuromatsu
            }
        }

        /// <summary>Fills <paramref name="mesh"/>; returns world bounds of everything drawn.</summary>
        public Bounds Build(TreeGraph tree, Mesh mesh, GrowthConfig growth, SpeciesConfig species)
        {
            _vertices.Clear();
            _colors.Clear();
            _indices.Clear();
            FoliageConfig foliage = Prepare(tree, growth, species);
            float potWidth = LastPotWidth;

            // Pot first (everything else overdraws it), sized to the crown.
            float potHeight = potWidth * 0.26f;
            AddRect(new Vector2(-potWidth * 0.5f, -potHeight), new Vector2(potWidth * 0.5f, 0f), Pot);
            AddRect(new Vector2(-potWidth * 0.5f - PotLipOverhang, -PotLipThickness * 2f), new Vector2(potWidth * 0.5f + PotLipOverhang, 0f), PotLip);
            AddRect(new Vector2(-potWidth * 0.5f + 1.5f, -0.6f), new Vector2(potWidth * 0.5f - 1.5f, 1.2f), Soil);

            int rootStart = _vertices.Count;
            AddNebariRoots(tree.Genome.Seed, growth);
            int treeStart = _vertices.Count;
            _trunkSamples.Clear();
            for (int i = 0; i < _count; i++) AddBranch(i);
            LastTaperRatio = MeasureTaper();

            AddFoliage(tree.Genome.Species, foliage, growth);

            for (int i = 0; i < _count; i++)
            {
                if (_nodes[i].Status == NodeStatus.Bud) AddEllipse(_tip[i], BudRadius, BudRadius, BudColor, BudColor);
            }

            float minX = float.MaxValue, maxX = float.MinValue, maxY = 0f;
            for (int v = treeStart; v < _vertices.Count; v++)
            {
                Vector3 p = _vertices[v];
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                maxY = Mathf.Max(maxY, p.y);
            }
            LastHeight = maxY;
            LastCrownWidth = maxX > minX ? maxX - minX : 0f;
            LastTreeRect = maxX > minX ? Rect.MinMaxRect(minX, 0f, maxX, maxY) : new Rect(0f, 0f, 0f, 0f);
            float bMinY = float.MaxValue;
            for (int v = rootStart; v < _vertices.Count; v++)
            {
                Vector3 p = _vertices[v];
                minX = Mathf.Min(minX, p.x);
                maxX = Mathf.Max(maxX, p.x);
                bMinY = Mathf.Min(bMinY, p.y);
            }
            LastBounds = maxX > minX ? Rect.MinMaxRect(minX, bMinY, maxX, maxY) : new Rect(0f, 0f, 0f, 0f);
            LastTrunkBase = _th0;
            LastTrunkBaseFlared = _th0 * (1f + _flare);

            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(_vertices);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_indices, 0, true);
            return mesh.bounds;
        }

        /// <summary>Shared by the Flat and Textured outputs: shape analysis, axis geometry, Nebari flare and the preview pot width.</summary>
        FoliageConfig Prepare(TreeGraph tree, GrowthConfig growth, SpeciesConfig species)
        {
            _linear = QualitySettings.activeColorSpace == ColorSpace.Linear;

            ReadOnlySpan<NodeData> span = tree.Nodes;
            _count = span.Length;
            span.CopyTo(_nodes);
            _shape.Analyze(span, growth.TrunkChainSegments, species.PadDepth);
            LastPadCount = _shape.PadCount;

            _th0 = (float)_nodes[0].Thickness;
            float flareRange = (float)(growth.MaxThickness - growth.TrunkInitialThickness);
            _flare = _render.NebariFlareMin + _render.NebariFlareGain
                * Mathf.Clamp01(flareRange > 0f ? (_th0 - (float)growth.TrunkInitialThickness) / flareRange : 0f);

            ComputeGeometry();
            // ⚑R §1.5.4(в): bark twist × σ, sign TierPhase·(−1)^k.
            _swayScale = (float)ShapeParams.Resolve(growth, species).SwayScale;
            _tierPhase = TreeShapeAnalysis.TierPhase(tree.Genome.Seed);
            FoliageConfig foliage = FoliageConfig.For(tree.Genome.Species);
            _puffRadius = foliage.PuffRadius;
            _chainTopDepth = 0;
            for (int i = 0; i < _count; i++)
            {
                if (_shape.IsTrunkChain(i) && _nodes[i].Depth > _chainTopDepth) _chainTopDepth = _nodes[i].Depth;
            }
            LastPotWidth = _render.PotWidthUnits;
            LastRootCount = NebariRootCount(growth);
            return foliage;
        }

        // ---- Geometry ---------------------------------------------------------------------------------

        void ComputeGeometry()
        {
            var world = new double[_count];
            for (int i = 0; i < _count; i++)
            {
                world[i] = (_nodes[i].ParentIndex >= 0 ? world[_nodes[i].ParentIndex] : 0.0) + _nodes[i].CurrentAngle;
                _dir[i] = new Vector2(-Mathf.Sin((float)world[i]), Mathf.Cos((float)world[i]));
            }

            // Tangents depend only on directions: joints of an axis use the bisector of the adjacent chords.
            for (int i = 0; i < _count; i++)
            {
                int parent = _nodes[i].ParentIndex;
                bool axisRoot = parent < 0 || _nodes[i].BranchSide != 0;
                _tanBase[i] = axisRoot ? _dir[i] : Bisector(_dir[parent], _dir[i]);
                int apical = _shape.ApicalChild(i);
                _tanTip[i] = apical >= 0 ? Bisector(_dir[i], _dir[apical]) : _dir[i];

                float th = (float)_nodes[i].Thickness;
                _tipWidth[i] = apical >= 0 ? Mathf.Max((float)_nodes[apical].Thickness, th * _render.TerminalTipRatio) : th * _render.TerminalTipRatio;
            }

            // Positions in index order: a lateral starts on its parent's curve at AttachT (BALANCE §1.2.1).
            for (int i = 0; i < _count; i++)
            {
                int parent = _nodes[i].ParentIndex;
                if (parent < 0) _base[i] = Vector2.zero;
                else if (_nodes[i].BranchSide == 0) _base[i] = _tip[parent];
                else _base[i] = Eval(parent, (float)_nodes[i].AttachT);
                _tip[i] = _base[i] + _dir[i] * (float)_nodes[i].Length;
            }
        }

        /// <summary>
        /// Unit tangent of node <paramref name="i"/>'s curve at <paramref name="t"/>: the exact joint tangents at the ends (t = 0 → the base tangent,
        /// t = 1 → the tip tangent; the Curvature bump has zero slope there), so the last section of a parent and the first section of its apical
        /// child coincide — no crack at the joint (PA-102R: 1-px holes on sharper S bends); inside — central difference.
        /// </summary>
        Vector2 SectionTangent(int i, float t)
        {
            if (t <= 0f) return _tanBase[i];
            if (t >= 1f) return _tanTip[i];
            Vector2 tangent = Eval(i, Mathf.Min(1f, t + 0.01f)) - Eval(i, Mathf.Max(0f, t - 0.01f));
            return tangent.sqrMagnitude > 1e-10f ? tangent.normalized : _dir[i];
        }

        static Vector2 Bisector(Vector2 a, Vector2 b)
        {
            Vector2 sum = a + b;
            return sum.sqrMagnitude > 1e-8f ? sum.normalized : b;
        }

        Vector2 Eval(int i, float t)
        {
            float length = (float)_nodes[i].Length;
            Vector2 p0 = _base[i];
            Vector2 p3 = _tip[i];
            Vector2 p1 = p0 + _tanBase[i] * (length / 3f);
            Vector2 p2 = p3 - _tanTip[i] * (length / 3f);
            float u = 1f - t;
            Vector2 point = u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
            Vector2 normal = new Vector2(-_dir[i].y, _dir[i].x);
            // Same mid-span offset as the PA-102V Bezier (0.75·c) but with zero slope at the joints → C1 kept.
            float bump = 12f * t * t * u * u;
            return point + normal * ((float)_nodes[i].Curvature * _render.CurvatureOffsetRatio * length * bump);
        }

        float Width(int i, float t, float y)
        {
            NodeData node = _nodes[i];
            float width = Mathf.Lerp((float)node.Thickness, _tipWidth[i], t);
            if (node.ParentIndex >= 0 && node.BranchSide == 0 && t < _render.JointBlend && _render.JointBlend > 0f)
            {
                float s = Mathf.SmoothStep(0f, 1f, t / _render.JointBlend);
                width = Mathf.Lerp(_tipWidth[node.ParentIndex], width, s);
            }
            if (_shape.Order(i) == 0 && _th0 > 0f) width *= 1f + _flare * FlareFalloff(i, t, y);
            return Mathf.Max(0.3f, width);
        }

        /// <summary>
        /// Nebari base flare fall-off at (node <paramref name="i"/>, <paramref name="t"/>, height <paramref name="y"/>) of an ord-0 node:
        /// ⚑Q BALANCE §1.4.1(а) Gaussian e^{−(s/(h·th0))²}, s = arc length along the #0 axis from the soil (ADR-0004 rev.4 — follows a
        /// leaning base; ≈ 0 above #0); PA-102P Exponential e^{−y/(h·th0)} over the height.
        /// </summary>
        float FlareFalloff(int i, float t, float y)
        {
            float scale = _render.NebariFlareHeightRatio * _th0;
            if (scale <= 0f) return 0f;
            if (_render.NebariFlareProfile == NebariFlareProfile.Exponential) return Mathf.Exp(-Mathf.Max(0f, y) / scale);
            float s = i == 0 ? ArcLength0(t) : ArcLength0(1f) + t * (float)_nodes[i].Length;
            float u = s / scale;
            return u > 6f ? 0f : Mathf.Exp(-u * u);
        }

        /// <summary>Arc length of #0 from its base to <paramref name="t"/> (8-point polyline of the C1 curve).</summary>
        float ArcLength0(float t)
        {
            const int steps = 8;
            float length = 0f;
            Vector2 previous = Eval(0, 0f);
            for (int k = 1; k <= steps; k++)
            {
                Vector2 point = Eval(0, t * k / steps);
                length += (point - previous).magnitude;
                previous = point;
            }
            return length;
        }

        /// <summary>Half width of the trunk mesh (with flare) at arc parameter <paramref name="t"/> of #0.</summary>
        public float TrunkHalfWidth(float t) => 0.5f * Width(0, t, Eval(0, t).y);

        /// <summary>
        /// ⚑P BALANCE §1.3.4 [RENDER]: share of node <paramref name="i"/> covered by bark — a bud shows ≤ BudBarkMaxLength; a terminal
        /// InPad node of ord ≥ 2 ends PadTwigInsetRatio·PuffRadius before its tip (inside the pad). Otherwise 1.
        /// </summary>
        public float BarkEndT(int i)
        {
            NodeData node = _nodes[i];
            float length = Mathf.Max(1e-4f, (float)node.Length);
            if (node.Status == NodeStatus.Bud) return Mathf.Clamp01(_render.BudBarkMaxLength / length);
            if (_shape.InPad(i) && _shape.Order(i) >= 2 && _shape.ApicalChild(i) < 0)
                return Mathf.Clamp01(1f - _render.PadTwigInsetRatio * _puffRadius / length);
            return 1f;
        }

        // ---- Bark -------------------------------------------------------------------------------------

        void AddBranch(int i)
        {
            NodeData node = _nodes[i];
            bool trunkChain = _shape.IsTrunkChain(i);
            int start = _vertices.Count;
            Vector2 previous = Eval(i, 0f);
            float tEnd = BarkEndT(i);
            int sections = Sections(i);
            bool flare = sections != SegmentSamples;
            for (int s = 0; s <= sections; s++)
            {
                float t = tEnd * SectionT(s, sections, flare);
                Vector2 point = Eval(i, t);
                Vector2 tangent = SectionTangent(i, t);
                float width = Width(i, t, point.y);
                Vector2 side = new Vector2(-tangent.y, tangent.x) * (width * 0.5f);
                // Light from the left: the right flank is a shade darker.
                AddVertex(point + side, Bark);
                AddVertex(point - side, BarkDark);
                if (trunkChain) _trunkSamples.Add(new Vector2(point.y, width));
                previous = point;
            }
            for (int s = 0; s < sections; s++)
            {
                int a = start + s * 2;
                _indices.Add(a); _indices.Add(a + 1); _indices.Add(a + 2);
                _indices.Add(a + 1); _indices.Add(a + 3); _indices.Add(a + 2);
            }

            if (node.BranchSide != 0) AddEllipse(_base[i], (float)node.Thickness * 0.5f, (float)node.Thickness * 0.5f, Bark, Bark); // collar
            if (_shape.ApicalChild(i) < 0) AddEllipse(previous, _tipWidth[i] * 0.5f, _tipWidth[i] * 0.5f, Bark, Bark);
        }

        float MeasureTaper()
        {
            if (_trunkSamples.Count < 2) return 0f;
            float top = 0f;
            for (int s = 0; s < _trunkSamples.Count; s++) top = Mathf.Max(top, _trunkSamples[s].x);
            float target = top * 2f / 3f;
            float baseWidth = _trunkSamples[0].y;
            for (int s = 1; s < _trunkSamples.Count; s++)
            {
                Vector2 a = _trunkSamples[s - 1];
                Vector2 b = _trunkSamples[s];
                if ((a.x - target) * (b.x - target) > 0f || Mathf.Approximately(a.x, b.x)) continue;
                float width = Mathf.Lerp(a.y, b.y, (target - a.x) / (b.x - a.x));
                return width > 0f ? baseWidth / width : 0f;
            }
            return 0f;
        }

        // ---- Nebari roots (⚑P BALANCE §1.3.3) --------------------------------------------------------------

        void AddNebariRoots(uint seed, GrowthConfig growth)
        {
            int count = NebariRootCount(growth);
            LastRootCount = count;
            _rootTips.Clear();
            for (int r = 0; r < count; r++)
            {
                NebariRoot(seed, r, count, _rootSamples);
                RecordRootTip(_rootSamples);
                AddRootRibbon(_rootSamples, Bark, BarkDark);
            }
        }

        /// <summary>clamp(round(min + (th0 − th_init) / perRoot), min, max), round half away from zero (ADR-0004 rev.3 §1).</summary>
        public int NebariRootCount(GrowthConfig growth) =>
            Mathf.Clamp((int)Math.Round(_render.NebariRootsMin + (_th0 - growth.TrunkInitialThickness) / _render.NebariThicknessPerRoot,
                MidpointRounding.AwayFromZero), _render.NebariRootsMin, _render.NebariRootsMax);

        /// <summary>Where each root of the last build dives under the moss: (x, moss line y, root width there) — SoilFront moss tufts.</summary>
        public IReadOnlyList<Vector3> LastRootTips => _rootTips;

        readonly List<Vector3> _rootTips = new List<Vector3>(8);
        readonly List<Vector3> _rootRaw = new List<Vector3>(64);

        /// <summary>
        /// Root r of <paramref name="count"/> as samples (x, y, width), uniform in arc length (⚑Q BALANCE §1.4.2, ADR-0004 rev.4):
        /// starts on the flare at NebariRootStartHeightRatio·th0, drops at NebariRootDrop (35…55°, hash) to the moss line, lies on the moss
        /// (centre line ≥ moss line + NebariRootMossClearance) for the first 1 − NebariRootTipBuryRatio of its length, then dives under
        /// the moss (only the last NebariRootTipBuryRatio is hidden). Width from the base (0.30…0.42·th0) to NebariRootTipRatio of it;
        /// an upward-only wave of NebariRootWaviness·th0. Fan = slot table × 75° ± 10°; front roots (|fan| ≤ 25°) × 0.6 shorter.
        /// Long/short and thick/thin alternate by index; everything else hashes (Seed, Id #0, salt) — not the PCG stream.
        /// </summary>
        public void NebariRoot(uint seed, int r, int count, List<Vector3> samples)
        {
            samples.Clear();
            float th0 = _th0;
            int trunkId = _nodes[0].Id;
            // ADR-0004 rev.3 §1: fan slot table by index → a new root does not shift the existing ones.
            float[] slots = _render.NebariFanSlots;
            float fan = slots[r % slots.Length] * _render.NebariFanDeg
                + _render.NebariFanJitterDeg * (2f * Hash01(seed, trunkId, 101u + (uint)r) - 1f);
            float sin = Mathf.Sin(fan * Mathf.Deg2Rad);
            float dir = fan < 0f ? -1f : 1f;
            bool front = Mathf.Abs(fan) <= _render.NebariFrontRootDeg;

            float h = Hash01(seed, trunkId, 211u + (uint)r);
            bool even = (r & 1) == 0;
            float lengthFactor = Mathf.Lerp(_render.NebariRootLengthMin, _render.NebariRootLengthMax, even ? 0.6f + 0.4f * h : 0.4f * h);
            float length = th0 * lengthFactor * (front ? _render.NebariFrontRootScale : 1f);
            float hw = Hash01(seed, trunkId, 223u + (uint)r);
            float baseWidth = th0 * Mathf.Lerp(_render.NebariRootThicknessMin, _render.NebariRootThicknessMax, even ? 0.55f + 0.45f * hw : 0.45f * hw);
            float tipWidth = baseWidth * _render.NebariRootTipRatio;
            float drop = Mathf.Lerp(_render.NebariRootDropMinDeg, _render.NebariRootDropMaxDeg, Hash01(seed, trunkId, 227u + (uint)r)) * Mathf.Deg2Rad;
            float endDrop = _render.NebariRootEndDropDeg * Mathf.Deg2Rad;
            float buryDrop = _render.NebariRootBuryDropDeg * Mathf.Deg2Rad;
            float phase = Hash01(seed, trunkId, 229u + (uint)r) * Mathf.PI * 2f;
            float bury = Mathf.Clamp01(_render.NebariRootTipBuryRatio);

            // Projected horizontal reach: roots facing the viewer show less of their length sideways.
            float reach = length * Mathf.Max(0.35f, Mathf.Abs(sin));
            float moss = _render.MossLineY;
            // ⚑R BALANCE §1.5.5 / ADR-0004 rev.5: the root starts on the #0 arc at s0 = NebariRootStartHeightRatio·th0, its centre line inside the
            // silhouette at d0 = (0.5·w_flare − 0.5·t_root)·max(|sin a|, 0.35) from the axis (the outer edge on the trunk contour), heading down
            // the #0 axis deflected outwards by 90° − NebariRootStartAngleDeg (along the trunk surface), then the chord of segment 1 drops at
            // NebariRootDrop to the moss line and eases into the shelf.
            float tStart = TrunkParamAtArc(_render.NebariRootStartHeightRatio * th0);
            Vector2 axisPoint = Eval(0, tStart);
            Vector2 axisTangent = Eval(0, Mathf.Min(1f, tStart + 0.01f)) - Eval(0, Mathf.Max(0f, tStart - 0.01f));
            axisTangent = axisTangent.sqrMagnitude > 1e-10f ? axisTangent.normalized : Vector2.up;
            Vector2 outward = new Vector2(axisTangent.y, -axisTangent.x) * dir;
            float d0 = Mathf.Max(0f, TrunkHalfWidth(tStart) - 0.5f * baseWidth) * Mathf.Max(Mathf.Abs(sin), 0.35f);
            Vector2 p0 = axisPoint + outward * d0;
            float deflect = (90f - _render.NebariRootStartAngleDeg) * Mathf.Deg2Rad;
            Vector2 t0 = (-axisTangent * Mathf.Cos(deflect) + outward * Mathf.Sin(deflect)).normalized;
            float plateau = Mathf.Min(moss + _render.NebariRootMossClearance, p0.y);
            float dropRun = Mathf.Min(0.5f * reach, Mathf.Max(0f, p0.y - plateau) / Mathf.Max(0.2f, Mathf.Tan(drop)));
            float buryRun = bury * reach;
            float flatRun = Mathf.Max(0.05f * reach, reach - dropRun - buryRun);
            var p1 = new Vector2(p0.x + dir * dropRun, plateau);
            var p2 = new Vector2(p1.x + dir * flatRun, plateau);
            var p3 = new Vector2(p2.x + dir * buryRun, moss - _render.NebariRootEndDepth - 0.5f * tipWidth);
            var t1 = new Vector2(dir, 0f);
            var t2 = new Vector2(dir * Mathf.Cos(endDrop), -Mathf.Sin(endDrop));
            var t3 = new Vector2(dir * Mathf.Cos(buryDrop), -Mathf.Sin(buryDrop));

            // Dense polyline of the three C1 Hermite segments, then resampled uniformly in arc length.
            const int perSegment = 12;
            _rootRaw.Clear();
            AppendHermite(p0, t0, p1, t1, perSegment, true);
            AppendHermite(p1, t1, p2, t2, perSegment, false);
            AppendHermite(p2, t2, p3, t3, perSegment, false);
            float total = 0f;
            for (int k = 1; k < _rootRaw.Count; k++) total += ((Vector2)_rootRaw[k] - (Vector2)_rootRaw[k - 1]).magnitude;
            const int resampled = 24;
            int j = 1;
            float walked = 0f;
            for (int k = 0; k <= resampled; k++)
            {
                float target = total * k / resampled;
                while (j < _rootRaw.Count - 1 && walked + ((Vector2)_rootRaw[j] - (Vector2)_rootRaw[j - 1]).magnitude < target)
                {
                    walked += ((Vector2)_rootRaw[j] - (Vector2)_rootRaw[j - 1]).magnitude;
                    j++;
                }
                float span = ((Vector2)_rootRaw[j] - (Vector2)_rootRaw[j - 1]).magnitude;
                Vector2 point = Vector2.Lerp(_rootRaw[j - 1], _rootRaw[j], span > 1e-6f ? Mathf.Clamp01((target - walked) / span) : 0f);
                float t = k / (float)resampled;
                // Upward-only wave (the centre line never sinks below the moss line on the visible part).
                float wave = _render.NebariRootWaviness * th0 * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * t + phase)) * Mathf.Sin(Mathf.PI * t)
                    * (1f - Mathf.SmoothStep(1f - bury - 0.1f, 1f - bury, t));
                point.y += wave;
                samples.Add(new Vector3(point.x, point.y, baseWidth * Mathf.Lerp(1f, _render.NebariRootTipRatio, t)));
            }
        }

        void AppendHermite(Vector2 a, Vector2 ta, Vector2 b, Vector2 tb, int steps, bool first)
        {
            for (int k = first ? 0 : 1; k <= steps; k++)
            {
                Vector2 p = Hermite(a, ta, b, tb, k / (float)steps);
                _rootRaw.Add(new Vector3(p.x, p.y, 0f));
            }
        }

        /// <summary>Arc parameter of #0 at arc length <paramref name="s"/> from its base (bisection on <see cref="ArcLength0"/>; 1 beyond its end).</summary>
        float TrunkParamAtArc(float s)
        {
            if (s <= 0f) return 0f;
            if (ArcLength0(1f) <= s) return 1f;
            float lo = 0f, hi = 1f;
            for (int k = 0; k < 20; k++)
            {
                float mid = 0.5f * (lo + hi);
                if (ArcLength0(mid) < s) lo = mid;
                else hi = mid;
            }
            return 0.5f * (lo + hi);
        }

        /// <summary>
        /// AC21 / ADR-0004 rev.5 «стики без латок»: signed distance of <paramref name="p"/> from the silhouette edge of the axis through node
        /// <paramref name="axisNode"/> (its apical neighbours included; + = outside), and the parent's cylinder normal there
        /// <paramref name="normalXY"/> = side·clamp(offset / half width, −1, 1) (z = √(1 − |xy|²) in the shader). Coarse sampling + refinement.
        /// </summary>
        public float SilhouetteDistance(int axisNode, Vector2 p, out Vector2 normalXY)
        {
            float best = float.MaxValue;
            int bestNode = axisNode;
            float bestT = 0f;
            for (int c = 0; c < 3; c++)
            {
                int node = c == 0 ? axisNode : c == 1 ? _shape.ApicalChild(axisNode)
                    : _nodes[axisNode].BranchSide == 0 ? _nodes[axisNode].ParentIndex : -1;
                if (node < 0 || node >= _count) continue;
                const int steps = 12;
                for (int k = 0; k <= steps; k++)
                {
                    float t = k / (float)steps;
                    float dist = (Eval(node, t) - p).sqrMagnitude;
                    if (dist < best)
                    {
                        best = dist;
                        bestNode = node;
                        bestT = t;
                    }
                }
            }
            float h = 0.5f / 12f;
            for (int iter = 0; iter < 8; iter++)
            {
                float ta = Mathf.Max(0f, bestT - h), tb = Mathf.Min(1f, bestT + h);
                float da = (Eval(bestNode, ta) - p).sqrMagnitude, db = (Eval(bestNode, tb) - p).sqrMagnitude;
                if (da < best) { best = da; bestT = ta; }
                if (db < best) { best = db; bestT = tb; }
                h *= 0.5f;
            }
            Vector2 centre = Eval(bestNode, bestT);
            Vector2 tangent = Eval(bestNode, Mathf.Min(1f, bestT + 0.01f)) - Eval(bestNode, Mathf.Max(0f, bestT - 0.01f));
            tangent = tangent.sqrMagnitude > 1e-10f ? tangent.normalized : _dir[bestNode];
            Vector2 side = new Vector2(-tangent.y, tangent.x);
            float half = Mathf.Max(1e-3f, 0.5f * Width(bestNode, bestT, centre.y));
            normalXY = side * Mathf.Clamp(Vector2.Dot(p - centre, side) / half, -1f, 1f);
            return Mathf.Sqrt(best) - half;
        }

        /// <summary>Arc parameter of #0 where its centre line reaches height <paramref name="y"/> (bisection; 0 below the base).</summary>
        float TrunkParamAtHeight(float y)
        {
            float lo = 0f, hi = 1f;
            if (Eval(0, 1f).y <= y) return 1f;
            for (int k = 0; k < 20; k++)
            {
                float mid = 0.5f * (lo + hi);
                if (Eval(0, mid).y < y) lo = mid;
                else hi = mid;
            }
            return 0.5f * (lo + hi);
        }

        /// <summary>Records where root <paramref name="samples"/> crosses the moss line (for the SoilFront moss tuft).</summary>
        void RecordRootTip(List<Vector3> samples)
        {
            float moss = _render.MossLineY;
            for (int k = samples.Count - 1; k > 0; k--)
            {
                if (samples[k - 1].y < moss) continue;
                Vector3 a = samples[k - 1], b = samples[k];
                float f = Mathf.Abs(a.y - b.y) > 1e-5f ? Mathf.Clamp01((a.y - moss) / (a.y - b.y)) : 0f;
                _rootTips.Add(new Vector3(Mathf.Lerp(a.x, b.x, f), moss, Mathf.Lerp(a.z, b.z, f)));
                return;
            }
        }

        static Vector2 Hermite(Vector2 a, Vector2 ta, Vector2 b, Vector2 tb, float u)
        {
            float scale = (b - a).magnitude;
            float u2 = u * u, u3 = u2 * u;
            return (2f * u3 - 3f * u2 + 1f) * a + (u3 - 2f * u2 + u) * scale * ta + (-2f * u3 + 3f * u2) * b + (u3 - u2) * scale * tb;
        }

        static Vector2 HermiteTangent(Vector2 a, Vector2 ta, Vector2 b, Vector2 tb, float u)
        {
            float scale = (b - a).magnitude;
            float u2 = u * u;
            return (6f * u2 - 6f * u) * a + (3f * u2 - 4f * u + 1f) * scale * ta + (-6f * u2 + 6f * u) * b + (3f * u2 - 2f * u) * scale * tb;
        }

        void AddRootRibbon(List<Vector3> samples, Color32 top, Color32 bottom)
        {
            int start = _vertices.Count;
            for (int s = 0; s < samples.Count; s++)
            {
                Vector2 point = samples[s];
                Vector2 tangent = (Vector2)samples[Mathf.Min(samples.Count - 1, s + 1)] - (Vector2)samples[Mathf.Max(0, s - 1)];
                tangent = tangent.sqrMagnitude > 1e-8f ? tangent.normalized : Vector2.right;
                Vector2 side = new Vector2(-tangent.y, tangent.x) * (samples[s].z * 0.5f);
                AddVertex(point + side, top);
                AddVertex(point - side, bottom);
            }
            for (int s = 0; s < samples.Count - 1; s++)
            {
                int i = start + s * 2;
                _indices.Add(i); _indices.Add(i + 1); _indices.Add(i + 2);
                _indices.Add(i + 1); _indices.Add(i + 3); _indices.Add(i + 2);
            }
        }

        /// <summary>Render hash (randomSeed, Id, salt) → [0, 1): the Domain <see cref="PocketArbor.Domain.Random.StableHash"/> (TD-018, ADR-0003
        /// PA-102R — one implementation; 24-bit result, exact in float).</summary>
        static float Hash01(uint seed, int id, uint salt) => (float)PocketArbor.Domain.Random.StableHash.Hash01(seed, id, salt);

        // ---- Foliage pads (BALANCE §1.2.5) --------------------------------------------------------------

        void AddFoliage(SpeciesId species, FoliageConfig foliage, GrowthConfig growth)
        {
            Color32 leaf = LeafColor(species);
            Color32 shade = Scale(leaf, 1f - foliage.PadUndersideShade);
            Color32 light = Scale(leaf, 1.06f);
            Color32 fresh = Scale(leaf, 1f + foliage.FreshTintValue);

            for (int root = 0; root < _count; root++)
            {
                if (!_shape.IsLeafy(root) || _shape.PadId(root) != root) continue;
                _puffs.Clear();
                for (int i = root; i < _count; i++)
                {
                    if (_shape.PadId(i) != root) continue;
                    float r = foliage.PuffRadius * _nodes[i].LeafScale;
                    if (r <= 0.2f) continue;
                    float rx = r * foliage.PuffWidthRatio;
                    float ry = rx * foliage.PadFlatten;
                    int puffs = Mathf.Max(1, foliage.PuffsPerNode);
                    for (int k = 1; k <= puffs; k++)
                    {
                        Vector2 c = Eval(i, k / (float)puffs) + new Vector2(0f, ry * PuffLift);
                        _puffs.Add(new Vector4(c.x, c.y, rx, ry));
                    }
                }
                if (_puffs.Count == 0) continue;
                if (_shape.PadSize(root) >= growth.PadFullNodes) AddPadSilhouette(shade, leaf, light);
                else
                {
                    for (int p = 0; p < _puffs.Count; p++)
                    {
                        Vector4 q = _puffs[p];
                        AddEllipse(new Vector2(q.x, q.y), q.z, q.w, light, shade);
                    }
                }
            }

            // Fresh growth: lighter puffs on the young tips, over the pad.
            for (int i = 0; i < _count; i++)
            {
                if (!_shape.IsLeafy(i) || _nodes[i].AgeDays >= growth.MatureDays) continue;
                float r = foliage.PuffRadius * _nodes[i].LeafScale * 0.55f;
                if (r <= 0.2f) continue;
                float rx = r * foliage.PuffWidthRatio;
                Vector2 c = _tip[i] + new Vector2(0f, rx * foliage.PadFlatten * 0.6f);
                AddEllipse(c, rx, rx * foliage.PadFlatten, fresh, leaf);
            }
        }

        /// <summary>Union of the puffs, column by column: lumpy domed top, flat (slope-following) bottom, darker underside.</summary>
        void AddPadSilhouette(Color32 shade, Color32 leaf, Color32 light)
        {
            float minX = float.MaxValue, maxX = float.MinValue, meanRy = 0f;
            float sx = 0f, sy = 0f, sxx = 0f, sxy = 0f;
            int n = _puffs.Count;
            for (int p = 0; p < n; p++)
            {
                Vector4 q = _puffs[p];
                minX = Mathf.Min(minX, q.x - q.z);
                maxX = Mathf.Max(maxX, q.x + q.z);
                meanRy += q.w;
                sx += q.x; sy += q.y; sxx += q.x * q.x; sxy += q.x * q.y;
            }
            meanRy /= n;
            float varX = sxx / n - (sx / n) * (sx / n);
            float slope = varX > 1f ? Mathf.Clamp((sxy / n - (sx / n) * (sy / n)) / varX, -0.6f, 0.6f) : 0f;
            float intercept = sy / n - slope * sx / n;

            int columns = Mathf.Clamp(Mathf.CeilToInt((maxX - minX) / 0.6f), 4, 240);
            float step = (maxX - minX) / columns;
            float mid = 0.5f * (minX + maxX);
            float half = Mathf.Max(1f, 0.5f * (maxX - minX));

            // Pass 1: raw union of the puff ellipses per column.
            EnsureColumns(columns + 1);
            float maxRaw = 0f;
            for (int c = 0; c <= columns; c++)
            {
                float x = minX + c * step;
                float top = float.MinValue, bottom = float.MaxValue;
                for (int p = 0; p < n; p++)
                {
                    Vector4 q = _puffs[p];
                    float u = (x - q.x) / q.z;
                    if (u <= -1f || u >= 1f) continue;
                    float h = q.w * Mathf.Sqrt(1f - u * u);
                    top = Mathf.Max(top, q.y + h);
                    bottom = Mathf.Min(bottom, q.y - h);
                }
                _columnTop[c] = top;
                _columnBottom[c] = bottom;
                if (top != float.MinValue) maxRaw = Mathf.Max(maxRaw, top - bottom);
            }

            // Pass 2: dome on top (scaled by local thickness → no steps at the rims), slope-following flat bottom.
            int previous = -1;
            for (int c = 0; c <= columns; c++)
            {
                float top = _columnTop[c];
                if (top == float.MinValue)
                {
                    previous = -1;
                    continue;
                }
                float x = minX + c * step;
                float bottom = _columnBottom[c];
                float raw = top - bottom;
                float u2 = (x - mid) / half;
                top += 0.25f * meanRy * Mathf.Max(0f, 1f - u2 * u2) * (maxRaw > 0f ? raw / maxRaw : 0f);
                float flat = intercept + slope * x - 0.25f * meanRy;
                bottom = Mathf.Min(Mathf.Max(bottom, flat), top - 0.45f * raw);

                int v = _vertices.Count;
                AddVertex(new Vector2(x, bottom), shade);
                AddVertex(new Vector2(x, bottom + 0.42f * (top - bottom)), leaf);
                AddVertex(new Vector2(x, top), light);
                if (previous >= 0)
                {
                    int a = previous;
                    _indices.Add(a); _indices.Add(a + 1); _indices.Add(v + 1);
                    _indices.Add(a); _indices.Add(v + 1); _indices.Add(v);
                    _indices.Add(a + 1); _indices.Add(a + 2); _indices.Add(v + 2);
                    _indices.Add(a + 1); _indices.Add(v + 2); _indices.Add(v + 1);
                }
                previous = v;
            }
        }

        float[] _columnTop = new float[256];
        float[] _columnBottom = new float[256];

        void EnsureColumns(int count)
        {
            if (_columnTop.Length >= count) return;
            _columnTop = new float[count];
            _columnBottom = new float[count];
        }

        static Color32 Scale(Color32 c, float factor) => new Color32(
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.r * factor), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.g * factor), 0, 255),
            (byte)Mathf.Clamp(Mathf.RoundToInt(c.b * factor), 0, 255), c.a);

        // ---- Primitives -------------------------------------------------------------------------------

        /// <summary>Ellipse with a vertical gradient: <paramref name="top"/> colour on the upper rim, <paramref name="bottom"/> below.</summary>
        void AddEllipse(Vector2 center, float rx, float ry, Color32 top, Color32 bottom)
        {
            if (rx <= 0f || ry <= 0f) return;
            int start = _vertices.Count;
            AddVertex(center, Color32.Lerp(bottom, top, 0.6f));
            for (int s = 0; s < DiscSegments; s++)
            {
                float a = s * (Mathf.PI * 2f / DiscSegments);
                float sin = Mathf.Sin(a);
                AddVertex(center + new Vector2(Mathf.Cos(a) * rx, sin * ry), Color32.Lerp(bottom, top, 0.5f + 0.5f * sin));
            }
            for (int s = 0; s < DiscSegments; s++)
            {
                _indices.Add(start);
                _indices.Add(start + 1 + (s + 1) % DiscSegments);
                _indices.Add(start + 1 + s);
            }
        }

        void AddRect(Vector2 min, Vector2 max, Color32 color)
        {
            int start = _vertices.Count;
            AddVertex(new Vector2(min.x, min.y), color);
            AddVertex(new Vector2(min.x, max.y), color);
            AddVertex(new Vector2(max.x, max.y), color);
            AddVertex(new Vector2(max.x, min.y), color);
            _indices.Add(start); _indices.Add(start + 1); _indices.Add(start + 2);
            _indices.Add(start); _indices.Add(start + 2); _indices.Add(start + 3);
        }

        void AddVertex(Vector2 position, Color32 color)
        {
            _vertices.Add(new Vector3(position.x, position.y, 0f));
            // Vertex colours are not gamma-converted by Unity: in a Linear project feed linear values
            // so the sRGB hex colours from BALANCE appear as authored.
            _colors.Add(_linear ? (Color32)((Color)color).linear : color);
        }
    }
}
