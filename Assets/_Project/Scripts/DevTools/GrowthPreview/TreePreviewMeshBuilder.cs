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

        public TreeRenderConfig Render => _render;

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

        /// <summary>AC20: base width with Nebari flare / trunk width at 2/3 of the trunk-chain height (last build).</summary>
        public float LastTaperRatio { get; private set; }

        public int LastPadCount { get; private set; }

        /// <summary>PA-102S proportions (last build, units): tree height above the Pot Lip incl. foliage.</summary>
        public float LastHeight { get; private set; }

        /// <summary>Bark + foliage extents (roots and pot excluded) of the last build — camera framing (TD-030).</summary>
        public Rect LastTreeRect { get; private set; }

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
            if (_shape.Order(i) == 0 && _th0 > 0f)
            {
                // Nebari base flare (BALANCE §1.2.4).
                width *= 1f + _flare * Mathf.Exp(-Mathf.Max(0f, y) / (_render.NebariFlareHeightRatio * _th0));
            }
            return Mathf.Max(0.3f, width);
        }

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
            for (int s = 0; s <= SegmentSamples; s++)
            {
                float t = tEnd * s / SegmentSamples;
                Vector2 point = Eval(i, t);
                Vector2 ahead = Eval(i, Mathf.Min(1f, t + 0.01f));
                Vector2 behind = Eval(i, Mathf.Max(0f, t - 0.01f));
                Vector2 tangent = ahead - behind;
                tangent = tangent.sqrMagnitude > 1e-10f ? tangent.normalized : _dir[i];
                float width = Width(i, t, point.y);
                Vector2 side = new Vector2(-tangent.y, tangent.x) * (width * 0.5f);
                // Light from the left: the right flank is a shade darker.
                AddVertex(point + side, Bark);
                AddVertex(point - side, BarkDark);
                if (trunkChain) _trunkSamples.Add(new Vector2(point.y, width));
                previous = point;
            }
            for (int s = 0; s < SegmentSamples; s++)
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
            for (int r = 0; r < count; r++)
            {
                NebariRoot(seed, r, count, _rootSamples);
                AddRootRibbon(_rootSamples, Bark, BarkDark);
            }
        }

        /// <summary>clamp(round(min + (th0 − th_init) / perRoot), min, max), round half away from zero (ADR-0004 rev.3 §1).</summary>
        public int NebariRootCount(GrowthConfig growth) =>
            Mathf.Clamp((int)Math.Round(_render.NebariRootsMin + (_th0 - growth.TrunkInitialThickness) / _render.NebariThicknessPerRoot,
                MidpointRounding.AwayFromZero), _render.NebariRootsMin, _render.NebariRootsMax);

        /// <summary>
        /// Root r of <paramref name="count"/> as samples (x, y, width) (BALANCE §1.3.3): two C1 cubic segments — from the flared
        /// base down-outwards at NebariRootDrop (35…55° below horizontal, hash), then easing to near-horizontal and ending below the
        /// SoilFront ground line (hidden by moss); a cross wave of NebariRootWaviness·th0 (one period, hashed phase); width from the
        /// base thickness (0.30…0.42·th0, alternating) to NebariRootTipRatio of it. Fan = slot table × 75° ± 10°; front roots (|fan| ≤ 25°)
        /// × 0.6 shorter. Long/short and thick/thin alternate by index; everything else hashes (Seed, Id #0, salt) — not the PCG stream.
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
            float drop = Mathf.Lerp(_render.NebariRootDropMinDeg, _render.NebariRootDropMaxDeg, Hash01(seed, trunkId, 227u + (uint)r)) * Mathf.Deg2Rad;
            float endDrop = _render.NebariRootEndDropDeg * Mathf.Deg2Rad;
            float phase = Hash01(seed, trunkId, 229u + (uint)r) * Mathf.PI * 2f;

            // Projected horizontal reach: roots facing the viewer show less of their length sideways.
            float reach = length * Mathf.Max(0.35f, Mathf.Abs(sin));
            float flareHalf = 0.5f * th0 * (1f + _flare);
            var p0 = new Vector2(sin * flareHalf * 0.75f, _render.NebariRootStartHeight * th0);
            var p2 = new Vector2(p0.x + dir * reach, -_render.NebariRootEndDepth);
            float knee = Mathf.Clamp(_render.NebariRootKnee, 0.1f, 0.9f);
            float kneeY = p2.y + (1f - knee) * reach * Mathf.Tan(2f * endDrop);
            kneeY = Mathf.Min(kneeY, p0.y - 0.2f * (p0.y - p2.y));
            var p1 = new Vector2(p0.x + dir * knee * reach, kneeY);
            var t0 = new Vector2(dir * Mathf.Cos(drop), -Mathf.Sin(drop));
            var t2 = new Vector2(dir * Mathf.Cos(endDrop), -Mathf.Sin(endDrop));
            Vector2 t1 = ((p1 - p0).normalized + (p2 - p1).normalized).normalized;

            const int perSegment = 8;
            int total = 2 * perSegment;
            for (int k = 0; k <= total; k++)
            {
                bool first = k <= perSegment;
                float u = first ? k / (float)perSegment : (k - perSegment) / (float)perSegment;
                Vector2 point = first ? Hermite(p0, t0, p1, t1, u) : Hermite(p1, t1, p2, t2, u);
                Vector2 tangent = first ? HermiteTangent(p0, t0, p1, t1, u) : HermiteTangent(p1, t1, p2, t2, u);
                tangent = tangent.sqrMagnitude > 1e-8f ? tangent.normalized : new Vector2(dir, 0f);
                float t = k / (float)total;
                float wave = _render.NebariRootWaviness * th0 * Mathf.Sin(2f * Mathf.PI * t + phase) * Mathf.Sin(Mathf.PI * t);
                point += new Vector2(-tangent.y, tangent.x) * wave;
                float width = baseWidth * Mathf.Lerp(1f, _render.NebariRootTipRatio, t);
                samples.Add(new Vector3(point.x, point.y, width));
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

        /// <summary>Render hash (randomSeed, Id, salt) → [0, 1) (BALANCE §1.2.9 / TD-018: not the PCG stream).</summary>
        static float Hash01(uint seed, int id, uint salt)
        {
            unchecked
            {
                uint h = seed ^ (0x9E3779B9u * (uint)(id + 1)) ^ (salt * 0x85EBCA6Bu);
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return (h >> 8) * (1f / 16777216f);
            }
        }

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
