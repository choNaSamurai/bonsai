using System.Collections.Generic;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Tree;
using UnityEngine;

namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>
    /// PA-103S «Textured» output of the preview (DevTools, not the PA-103 renderer): the same axis geometry as the Flat
    /// output, written as two meshes per ADR-0004 rev. 2 —
    /// (1) bark: Nebari roots + every node as a ribbon section of its axis, UV0 = (u01 across, arc length along the axis;
    ///     an axis continues the parent's arc, a lateral axis starts at hash(Seed, Id)), UV1 = (width, barkAge, ink, 0),
    ///     UV2 = side vector (shader: isotropic tiling, trunk↔young blend, cylinder light, ink outline, edge AA);
    /// (2) pads: one sprite quad per puff from the species atlas (variant and mirroring hashed from NodeData.Id, TD-018),
    ///     a full pad is covered by a few whole-pad sprites laid along its slope line (rows for a tall apex dome), a small pad
    ///     by one small tuft per node; size class S/M/L from the sprite width; pads sorted bottom-up (apex last), sprites
    ///     bottom-up inside a pad, each pad preceded by its soft shadow underlay; underside of the pad as a whole is darker.
    /// Dev-only: allocations are fine; meshes are reused (cleared and refilled).
    /// </summary>
    public sealed partial class TreePreviewMeshBuilder
    {
        /// <summary>One emitted puff (tests: variant/mirroring must depend on NodeData.Id only).</summary>
        public readonly struct PuffRecord
        {
            public readonly int NodeId;
            public readonly int Slot;
            public readonly Rect Uv;
            public readonly bool Mirrored;
            public readonly PadSizeClass Size;

            public PuffRecord(int nodeId, int slot, Rect uv, bool mirrored, PadSizeClass size)
            {
                NodeId = nodeId;
                Slot = slot;
                Uv = uv;
                Mirrored = mirrored;
                Size = size;
            }
        }

        struct Puff
        {
            public Vector2 Anchor;
            public float Width;
            public float Height;
            public PadSprite Sprite;
            public bool Mirrored;
            public float Brightness;
            public int Order;
        }

        // Bark stream.
        readonly List<Vector3> _bPos = new List<Vector3>(16384);
        readonly List<Vector2> _bUv0 = new List<Vector2>(16384);
        readonly List<Vector4> _bUv1 = new List<Vector4>(16384);
        readonly List<Vector2> _bUv2 = new List<Vector2>(16384);
        // AC21 collar: (parent normal xy, distance outside the parent edge, light blend ≥ 0 — or −blend length: the shader blends by distance).
        readonly List<Vector4> _bUv3 = new List<Vector4>(16384);
        readonly List<Vector2> _bUv4 = new List<Vector2>(16384); // (ADR-0004 rev.5 edge mask × ink and AA fall-off, parent barkAge for the collar)
        readonly List<Color> _bCol = new List<Color>(16384);
        readonly List<int> _bIdx = new List<int>(49152);

        // Pad stream.
        readonly List<Vector3> _pPos = new List<Vector3>(8192);
        readonly List<Vector2> _pUv0 = new List<Vector2>(8192);
        readonly List<Vector2> _pUv1 = new List<Vector2>(8192);
        readonly List<Color> _pCol = new List<Color>(8192);
        readonly List<int> _pIdx = new List<int>(12288);

        readonly float[] _arcStart = new float[TreeGraph.Capacity];
        readonly float[] _arcEnd = new float[TreeGraph.Capacity];
        readonly float[] _twistEnd = new float[TreeGraph.Capacity];

        /// <summary>Distance written for vertices outside every collar (no transparency, no ink mask).</summary>
        const float NoCollar = 1e4f;
        static readonly Vector4 NoCollarAttr = new Vector4(0f, 0f, NoCollar, 1f);

        /// <summary>AC21 harness: Id of a node whose ribbon is not emitted (−1 = none) — the «without this lateral» reference render.</summary>
        public int DebugSkipNodeId { get; set; } = -1;

        /// <summary>AC9 harness: the Nebari roots are not emitted (the «trunk without roots» silhouette).</summary>
        public bool DebugSkipRoots { get; set; }
        readonly List<Vector3> _padOrder = new List<Vector3>(128); // (key y, Id, root index)
        readonly List<Puff> _padPuffs = new List<Puff>(256);
        readonly List<float> _padKeys = new List<float>(128);
        readonly List<PuffRecord> _puffRecords = new List<PuffRecord>(512);

        /// <summary>Pad sort keys (root base height) in emission order of the last textured build — must be non-decreasing.</summary>
        public IReadOnlyList<float> LastPadOrderKeys => _padKeys;

        /// <summary>Puffs of the last textured build in emission order.</summary>
        public IReadOnlyList<PuffRecord> LastPuffs => _puffRecords;

        /// <summary>Arc length (world units) at the start / end of node <paramref name="index"/> of the last textured build.</summary>
        public void ArcRange(int index, out float start, out float end)
        {
            start = _arcStart[index];
            end = _arcEnd[index];
        }

        /// <summary>Fills the bark and pad meshes; returns world bounds of both (pot/soil are drawn by the view).</summary>
        public Bounds BuildTextured(TreeGraph tree, Mesh barkMesh, Mesh padMesh, GrowthConfig growth, SpeciesConfig species,
            TexturedStyleConfig style, PadAtlas atlas)
        {
            _bPos.Clear(); _bUv0.Clear(); _bUv1.Clear(); _bUv2.Clear(); _bUv3.Clear(); _bUv4.Clear(); _bCol.Clear(); _bIdx.Clear();
            _pPos.Clear(); _pUv0.Clear(); _pUv1.Clear(); _pCol.Clear(); _pIdx.Clear();
            _padKeys.Clear();
            _puffRecords.Clear();
            LastTipOverflow = 0;
            LastExtraTipSprites = 0;

            FoliageConfig foliage = Prepare(tree, growth, species);
            uint seed = tree.Genome.Seed;

            int roots = NebariRootCount(growth);
            _rootTips.Clear();
            _trunkSamples.Clear();
            bool legacy = _render.CollarLegacy;
            // ADR-0004 rev.5: ord-0 axis (trunk chain + crown axis) → roots → lateral axes (PA-102Q legacy: roots first).
            if (legacy) AddBarkRoots(roots, seed, style);
            int treeStart = _bPos.Count;
            for (int i = 0; i < _count; i++) if (_shape.Order(i) == 0) AddBarkNode(i, seed, style);
            if (!legacy) AddBarkRoots(roots, seed, style);
            for (int i = 0; i < _count; i++) if (_shape.Order(i) != 0) AddBarkNode(i, seed, style);
            LastTaperRatio = MeasureTaper();

            if (atlas != null) AddTexturedPads(foliage, growth, style, atlas, seed);

            float minX = float.MaxValue, maxX = float.MinValue, maxY = 0f;
            for (int v = 0; v < _bPos.Count; v++)
            {
                // Roots are excluded from the crown metrics (PA-102Q): ord-0 nodes are written first, then (rev.5) the roots.
                if (v >= treeStart && !IsRootVertex(v)) Extend(_bPos[v], ref minX, ref maxX, ref maxY);
            }
            for (int v = 0; v < _pPos.Count; v++) Extend(_pPos[v], ref minX, ref maxX, ref maxY);
            LastHeight = maxY;
            LastCrownWidth = maxX > minX ? maxX - minX : 0f;
            LastTreeRect = maxX > minX ? Rect.MinMaxRect(minX, 0f, maxX, maxY) : new Rect(0f, 0f, 0f, 0f);
            float bMinX = float.MaxValue, bMaxX = float.MinValue, bMinY = float.MaxValue, bMaxY = float.MinValue;
            for (int v = 0; v < _bPos.Count; v++) Extend4(_bPos[v], ref bMinX, ref bMaxX, ref bMinY, ref bMaxY);
            for (int v = 0; v < _pPos.Count; v++) Extend4(_pPos[v], ref bMinX, ref bMaxX, ref bMinY, ref bMaxY);
            LastBounds = bMaxX > bMinX ? Rect.MinMaxRect(bMinX, bMinY, bMaxX, bMaxY) : new Rect(0f, 0f, 0f, 0f);
            LastTrunkBase = _th0;
            LastTrunkBaseFlared = _th0 * (1f + _flare);

            barkMesh.Clear();
            barkMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            barkMesh.SetVertices(_bPos);
            barkMesh.SetUVs(0, _bUv0);
            barkMesh.SetUVs(1, _bUv1);
            barkMesh.SetUVs(2, _bUv2);
            barkMesh.SetUVs(3, _bUv3);
            barkMesh.SetUVs(4, _bUv4);
            barkMesh.SetColors(_bCol);
            barkMesh.SetTriangles(_bIdx, 0, true);

            padMesh.Clear();
            padMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            padMesh.SetVertices(_pPos);
            padMesh.SetUVs(0, _pUv0);
            padMesh.SetUVs(1, _pUv1);
            padMesh.SetColors(_pCol);
            padMesh.SetTriangles(_pIdx, 0, true);

            Bounds bounds = barkMesh.bounds;
            if (_pPos.Count > 0) bounds.Encapsulate(padMesh.bounds);
            return bounds;
        }

        int _rootVertexStart, _rootVertexEnd;

        bool IsRootVertex(int v) => v >= _rootVertexStart && v < _rootVertexEnd;

        static void Extend4(Vector3 p, ref float minX, ref float maxX, ref float minY, ref float maxY)
        {
            minX = Mathf.Min(minX, p.x);
            maxX = Mathf.Max(maxX, p.x);
            minY = Mathf.Min(minY, p.y);
            maxY = Mathf.Max(maxY, p.y);
        }

        void AddBarkRoots(int roots, uint seed, TexturedStyleConfig style)
        {
            _rootVertexStart = _bPos.Count;
            for (int r = 0; r < roots && !DebugSkipRoots; r++)
            {
                NebariRoot(seed, r, roots, _rootSamples);
                RecordRootTip(_rootSamples);
                float tone = Mathf.Lerp(style.RootToneMin, style.RootToneMax, Hash01(seed, _nodes[0].Id, 311u + (uint)r));
                // ADR-0004 rev.5: v of a root continues the #0 bark at its start (legacy: a hashed phase).
                float arc0 = _render.CollarLegacy ? Hash01(seed, _nodes[0].Id, 307u + (uint)r) * style.BarkTileWorld * 4f
                    : _arcStart[0] + _render.NebariRootStartHeightRatio * _th0;
                AddBarkRoot(_rootSamples, arc0, tone, style);
            }
            _rootVertexEnd = _bPos.Count;
        }

        static void Extend(Vector3 p, ref float minX, ref float maxX, ref float maxY)
        {
            minX = Mathf.Min(minX, p.x);
            maxX = Mathf.Max(maxX, p.x);
            maxY = Mathf.Max(maxY, p.y);
        }

        // ---- Bark ribbons ------------------------------------------------------------------------------

        void AddBarkNode(int i, uint seed, TexturedStyleConfig style)
        {
            NodeData node = _nodes[i];
            int parent = node.ParentIndex;
            bool lateral = parent >= 0 && node.BranchSide != 0;
            bool axisRoot = parent < 0 || lateral;
            bool trunkChain = _shape.IsTrunkChain(i);
            bool ord0 = _shape.Order(i) == 0;
            bool legacy = _render.CollarLegacy;
            // ADR-0004 rev.5: a lateral's bark v starts at the parent's v at AttachT (legacy: hash(Seed, Id)).
            float arc0 = !axisRoot ? _arcEnd[parent]
                : lateral && !legacy ? Mathf.Lerp(_arcStart[parent], _arcEnd[parent], (float)node.AttachT)
                : Hash01(seed, node.Id, 401u) * style.BarkTileWorld * 4f;
            _arcStart[i] = arc0;
            float twist = ord0 && parent >= 0 ? _twistEnd[parent] : 0f;

            // A lateral starts on its parent's centre line: collar flare (smoothstep) + the AC21 collar mask.
            float parentHalf = 0f, collarLength = 1f, collarZone = 0f, parentAge = 0f;
            if (lateral)
            {
                Vector2 attach = Eval(parent, (float)node.AttachT);
                float parentWidth = Width(parent, (float)node.AttachT, attach.y);
                parentHalf = 0.5f * parentWidth;
                collarLength = parentHalf + (float)node.Thickness;
                parentAge = BarkAge(parentWidth, _shape.IsTrunkChain(parent), style);
                float sin = Mathf.Abs(Vector2.Dot(_dir[i], new Vector2(-_dir[parent].y, _dir[parent].x)));
                collarZone = parentHalf / Mathf.Max(0.25f, sin) + (1f + _render.CollarBlendRatio) * (float)node.Thickness + 2f;
            }
            bool skip = node.Id == DebugSkipNodeId;

            int start = _bPos.Count;
            float arc = arc0;
            Vector2 previous = Eval(i, 0f);
            Vector2 lastTangent = _dir[i];
            float lastWidth = 0f;
            float tEnd = BarkEndT(i);
            int sectionCount = Sections(i);
            bool flare = sectionCount != SegmentSamples;
            float tile = Mathf.Max(1e-3f, style.BarkTileWorld);
            for (int s = 0; s <= sectionCount; s++)
            {
                float t = tEnd * SectionT(s, sectionCount, flare);
                Vector2 point = Eval(i, t);
                Vector2 tangent = SectionTangent(i, t);
                float step = (point - previous).magnitude;
                arc += step;
                if (ord0) twist += _swayScale * TwistSign(i, t) * step / tile;
                float width = Width(i, t, point.y);
                float ink = 1f;
                Color tint = Color.white;
                Vector4 collarA = NoCollarAttr, collarB = NoCollarAttr;
                float age = BarkAge(width, trunkChain, style), parentAgeAttr = age;
                if (lateral)
                {
                    float local = arc - arc0;
                    width *= 1f + style.CollarFlare * (1f - Mathf.SmoothStep(0f, 1f, local / collarLength));
                    if (legacy)
                    {
                        ink = Mathf.SmoothStep(0f, 1f, (local - 0.6f * parentHalf) / (0.6f * parentHalf + 0.5f * (float)node.Thickness + 1e-3f));
                        // The square start of the ribbon (inside the parent) fades in → no flap where it pokes out of the parent.
                        tint.a = Mathf.SmoothStep(0f, 1f, local / (style.CollarFadeIn * parentHalf + 1e-3f));
                    }
                    else if (local < collarZone)
                    {
                        // AC21: per vertex — distance outside the parent's silhouette (shader: transparent inside, ink only outside) and the
                        // light / bark-age blend from the parent's (at its edge) to the lateral's own over CollarBlendRatio × thickness.
                        Vector2 side = new Vector2(-tangent.y, tangent.x) * (0.5f * width);
                        float blendLength = Mathf.Max(0.3f, _render.CollarBlendRatio * (float)node.Thickness);
                        collarA = CollarAttribute(parent, point + side, blendLength);
                        collarB = CollarAttribute(parent, point - side, blendLength);
                        parentAgeAttr = parentAge;
                    }
                }
                if (trunkChain) _trunkSamples.Add(new Vector2(point.y, width));
                if (!skip) AddBarkSection(point, tangent, width, arc, age, age, ink, tint, twist, collarA, collarB, 1f, 1f, parentAgeAttr);
                previous = point;
                lastTangent = tangent;
                lastWidth = width;
            }
            _arcEnd[i] = arc;
            _twistEnd[i] = twist;
            if (skip) return;

            int sections = sectionCount + 1;
            if (_shape.ApicalChild(i) < 0)
            {
                // Rounded-off terminal tip: one extra section past the tip, nearly closed.
                float capLength = style.CapLengthRatio * lastWidth;
                float capAge = BarkAge(lastWidth, trunkChain, style);
                AddBarkSection(previous + lastTangent * capLength, lastTangent, lastWidth * style.CapWidthRatio, arc + capLength,
                    capAge, capAge, 1f, Color.white, twist, NoCollarAttr, NoCollarAttr, 1f, 1f);
                sections++;
            }
            AddRibbonIndices(start, sections);
        }

        /// <summary>(parent normal xy, signed distance outside the parent's silhouette, −<paramref name="blendLength"/>: the shader blends light and
        /// bark age from the parent's to the lateral's by smoothstep(0, blendLength, d) per pixel — a per-vertex blend would be linear over a whole
        /// section and step at the collar edge).</summary>
        Vector4 CollarAttribute(int parent, Vector2 vertex, float blendLength)
        {
            float d = SilhouetteDistance(parent, vertex, out Vector2 normal);
            return new Vector4(normal.x, normal.y, d, -blendLength);
        }

        /// <summary>
        /// ⚑R §1.5.4(в) / ADR-0004 rev.5: sign of the bark twist on ord-0 node <paramref name="i"/> at <paramref name="t"/> — TierPhase·(−1)^Depth,
        /// blended (smoothstep) to the neighbour's sign over ±JointBlend around each joint, so the twist phase is continuous and only the fibre
        /// angle changes.
        /// </summary>
        float TwistSign(int i, float t)
        {
            float own = SegmentSign(_nodes[i].Depth);
            float blend = Mathf.Max(1e-3f, _render.JointBlend);
            int parent = _nodes[i].ParentIndex;
            if (parent >= 0 && t < blend) return Mathf.Lerp(SegmentSign(_nodes[parent].Depth), own, Mathf.SmoothStep(0f, 1f, 0.5f + 0.5f * t / blend));
            if (_shape.ApicalChild(i) >= 0 && t > 1f - blend)
                return Mathf.Lerp(own, SegmentSign(_nodes[i].Depth + 1), Mathf.SmoothStep(0f, 1f, 0.5f * (t - (1f - blend)) / blend));
            return own;
        }

        float SegmentSign(int depth) => _tierPhase * ((depth & 1) == 0 ? 1f : -1f);

        void AddBarkRoot(List<Vector3> samplesXyw, float arc0, float tone, TexturedStyleConfig style)
        {
            int samples = samplesXyw.Count - 1;
            int start = _bPos.Count;
            float arc = arc0;
            Vector2 previous = samplesXyw[0];
            bool legacy = _render.CollarLegacy;
            float baseWidth = Mathf.Max(0.15f, samplesXyw[0].z);
            float join = Mathf.Max(0.3f, _render.NebariRootJoinBlend * baseWidth);
            for (int s = 0; s <= samples; s++)
            {
                Vector2 point = samplesXyw[s];
                Vector2 tangent = (Vector2)samplesXyw[Mathf.Min(samples, s + 1)] - (Vector2)samplesXyw[Mathf.Max(0, s - 1)];
                tangent = tangent.sqrMagnitude > 1e-8f ? tangent.normalized : Vector2.right;
                arc += (point - previous).magnitude;
                float width = Mathf.Max(0.15f, samplesXyw[s].z);
                float local = arc - arc0;
                if (legacy)
                {
                    float ink = Mathf.SmoothStep(0f, 1f, (local - 0.2f * _th0) / (0.4f * _th0 + 1e-3f));
                    AddBarkSection(point, tangent, width, arc, 1f, 1f, ink, new Color(tone, tone, tone, 1f), 0f, NoCollarAttr, NoCollarAttr, 1f, 1f);
                }
                else
                {
                    // ADR-0004 rev.5 «маска стику»: the root body is opaque; on its inner (axis-facing) edge the ink and AA fall-off grow from 0
                    // over NebariRootJoinBlend × width, the outer edge continues the trunk contour (full ink); light and tone blend from the trunk's.
                    float ramp = Mathf.SmoothStep(0f, 1f, local / join);
                    Vector2 side = new Vector2(-tangent.y, tangent.x) * (0.5f * width);
                    Vector4 a = RootCollar(point + side, ramp), b = RootCollar(point - side, ramp);
                    float axisX = Eval(0, 0f).x;
                    bool plusOuter = Mathf.Abs(point.x + side.x - axisX) >= Mathf.Abs(point.x - side.x - axisX);
                    float shade = Mathf.Lerp(1f, tone, ramp);
                    // The start section lies on the trunk: it fades in with the same ramp, so its cross cut never shows (no notch).
                    AddBarkSection(point, tangent, width, arc, 1f, 1f, 1f, new Color(shade, shade, shade, ramp), 0f, a, b,
                        plusOuter ? 1f : ramp, plusOuter ? ramp : 1f);
                }
                previous = point;
            }
            AddRibbonIndices(start, samples + 1);
        }

        Vector4 RootCollar(Vector2 vertex, float ramp)
        {
            if (ramp >= 1f) return NoCollarAttr;
            SilhouetteDistance(0, vertex, out Vector2 normal);
            return new Vector4(normal.x, normal.y, NoCollar, ramp);
        }

        float BarkAge(float width, bool trunkChain, TexturedStyleConfig style) =>
            Mathf.Clamp01(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(style.YoungMaxWidth, style.TrunkMinWidth, width))
                + (trunkChain ? style.TrunkChainAgeBias : 0f));

        /// <summary>Two vertices of a ribbon section (A = +side, B = −side): UV1 = (width, barkAge, ink, twist phase φ), UV3 = collar attribute,
        /// UV4.x = edge mask (× ink and AA fall-off).</summary>
        void AddBarkSection(Vector2 point, Vector2 tangent, float width, float arc, float ageA, float ageB, float ink, Color tint, float twist,
            Vector4 collarA, Vector4 collarB, float maskA, float maskB, float parentAge = -1f)
        {
            float pa = parentAge >= 0f ? parentAge : ageA, pb = parentAge >= 0f ? parentAge : ageB;
            Vector2 side = new Vector2(-tangent.y, tangent.x);
            Vector2 half = side * (width * 0.5f);
            Color c = _linear ? tint.linear : tint;
            _bPos.Add(point + half);
            _bUv0.Add(new Vector2(0f, arc));
            _bUv1.Add(new Vector4(width, ageA, ink, twist));
            _bUv2.Add(side);
            _bUv3.Add(collarA);
            _bUv4.Add(new Vector2(maskA, pa));
            _bCol.Add(c);
            _bPos.Add(point - half);
            _bUv0.Add(new Vector2(1f, arc));
            _bUv1.Add(new Vector4(width, ageB, ink, twist));
            _bUv2.Add(side);
            _bUv3.Add(collarB);
            _bUv4.Add(new Vector2(maskB, pb));
            _bCol.Add(c);
        }

        void AddRibbonIndices(int start, int sections)
        {
            for (int s = 0; s < sections - 1; s++)
            {
                int a = start + s * 2;
                _bIdx.Add(a); _bIdx.Add(a + 1); _bIdx.Add(a + 2);
                _bIdx.Add(a + 1); _bIdx.Add(a + 3); _bIdx.Add(a + 2);
            }
        }

        // ---- Foliage pads from atlas sprites -----------------------------------------------------------

        void AddTexturedPads(FoliageConfig foliage, GrowthConfig growth, TexturedStyleConfig style, PadAtlas atlas, uint seed)
        {
            _padOrder.Clear();
            for (int root = 0; root < _count; root++)
            {
                if (!_shape.IsLeafy(root) || _shape.PadId(root) != root) continue;
                _padOrder.Add(new Vector3(_base[root].y, _nodes[root].Id, root));
            }
            // Bottom-up (apex last); equal heights by Id → deterministic.
            _padOrder.Sort((p, q) => p.x != q.x ? p.x.CompareTo(q.x) : p.y.CompareTo(q.y));

            float fresh = Mathf.Min(1f + foliage.FreshTintValue, style.FreshBrightnessMax) - 1f;
            for (int p = 0; p < _padOrder.Count; p++)
            {
                int root = (int)_padOrder[p].z;
                _padPuffs.Clear();
                bool dome = IsDomePad(root);
                if (_shape.PadSize(root) >= growth.PadFullNodes) AddPadCluster(root, foliage, growth, style, atlas, seed, fresh, dome);
                else AddPadTufts(root, foliage, growth, style, atlas, seed, fresh);
                if (_padPuffs.Count == 0) continue;
                CoverPadTips(root, foliage, style, atlas, seed);
                _padKeys.Add(_padOrder[p].x);
                EmitPad(style);
            }
        }

        /// <summary>
        /// Full pad (PadSize ≥ PadFullNodes): the union of its puff ellipses (§1.2.5) is covered by a few whole-pad sprites —
        /// a row of n = ceil(width / max sprite width) along the pad's slope line, plus narrower rows on top while the union
        /// is taller than one row (apex dome). Variant/mirroring hash = (Seed, Id of the pad root, slot).
        /// </summary>
        void AddPadCluster(int root, FoliageConfig foliage, GrowthConfig growth, TexturedStyleConfig style, PadAtlas atlas, uint seed, float fresh, bool dome)
        {
            float minX = float.MaxValue, maxX = float.MinValue, top = float.MinValue, bottom = float.MaxValue, meanRy = 0f;
            float sx = 0f, sy = 0f, sxx = 0f, sxy = 0f;
            int n = 0, freshNodes = 0, nodes = 0;
            for (int i = root; i < _count; i++)
            {
                if (_shape.PadId(i) != root) continue;
                float r = foliage.PuffRadius * _nodes[i].LeafScale;
                if (r <= 0.2f) continue;
                nodes++;
                if (_nodes[i].AgeDays < growth.MatureDays) freshNodes++;
                float rx = r * foliage.PuffWidthRatio;
                float ry = rx * foliage.PadFlatten;
                int puffs = Mathf.Max(1, foliage.PuffsPerNode);
                for (int k = 1; k <= puffs; k++)
                {
                    Vector2 c = Eval(i, k / (float)puffs);
                    minX = Mathf.Min(minX, c.x - rx);
                    maxX = Mathf.Max(maxX, c.x + rx);
                    top = Mathf.Max(top, c.y + ry);
                    bottom = Mathf.Min(bottom, c.y - ry);
                    meanRy += ry;
                    sx += c.x; sy += c.y; sxx += c.x * c.x; sxy += c.x * c.y;
                    n++;
                }
            }
            if (n == 0) return;
            meanRy /= n;
            float varX = sxx / n - (sx / n) * (sx / n);
            // ⚑R BALANCE §1.5.2(б): side pads lie flatter (PadRowSlopeMaxSide 0.12) than the apex dome (PadMaxSlope 0.35).
            float maxSlope = dome ? style.PadMaxSlope : foliage.PadRowSlopeMaxSide;
            float slope = varX > 1f ? Mathf.Clamp((sxy / n - (sx / n) * (sy / n)) / varX, -maxSlope, maxSlope) : 0f;
            float intercept = sy / n - slope * sx / n;
            float brightness = fresh * freshNodes / Mathf.Max(1, nodes);

            // ⚑R BALANCE §1.5.2(в) / ADR-0004 rev.5: a full side pad is laid out over the virtual span S = max(U, PadSideMinSpanUnits) about the
            // middle of its union — more / larger sprites, never a non-uniform stretch.
            if (!dome && foliage.PadSideMinSpanUnits > 0f && maxX - minX < foliage.PadSideMinSpanUnits)
            {
                float mid = 0.5f * (minX + maxX);
                minX = mid - 0.5f * foliage.PadSideMinSpanUnits;
                maxX = mid + 0.5f * foliage.PadSideMinSpanUnits;
            }
            float width = maxX - minX;
            float maxSprite = style.PadSpriteMaxWidthRatio * foliage.PuffRadius;
            int columns = Mathf.Max(1, Mathf.CeilToInt(width / maxSprite - 0.15f));
            float spriteWidth = width / columns * (columns > 1 ? style.PadClusterOverlap : style.PadSingleCover);
            float unionHeight = top - bottom;
            // ⚑P BALANCE §1.3.4: ≤ PadMaxRowsSide rows for a side pad (Kuromatsu 1), ≤ PadMaxRowsDome for the apex dome.
            int maxRows = Mathf.Min(style.PadMaxRows, dome ? foliage.PadMaxRowsDome : foliage.PadMaxRowsSide);
            float rowLift = dome ? style.PadDomeRowLift : style.PadRowLift;
            for (int row = 0; row < maxRows; row++)
            {
                int count = Mathf.Max(1, columns - row);
                float rowWidth = spriteWidth * Mathf.Pow(style.PadRowShrink, row);
                float span = width * Mathf.Pow(style.PadRowShrink, row);
                float rowMin = 0.5f * (minX + maxX) - 0.5f * span;
                for (int j = 0; j < count; j++)
                {
                    float x = rowMin + (j + 0.5f) * span / count;
                    float y = intercept + slope * x - meanRy * style.PadLift + row * rowLift * rowWidth;
                    AddClusterSprite(atlas, seed, _nodes[root].Id, row * 16 + j, new Vector2(x, y), rowWidth, foliage.PuffRadius, style, brightness,
                        dome && row == 0 && j == count / 2);
                }
                // Next row only while the union is taller than what the rows so far cover.
                float covered = rowWidth * 0.45f + row * rowLift * rowWidth;
                if (covered >= unionHeight * style.PadHeightCover) break;
            }
        }

        void AddClusterSprite(PadAtlas atlas, uint seed, int id, int slot, Vector2 anchor, float width, float puffRadius, TexturedStyleConfig style,
            float brightness, bool dome = false)
        {
            PadSizeClass size = width >= style.PadLargeMinRadii * puffRadius ? PadSizeClass.L
                : width >= style.PadMediumMinRadii * puffRadius ? PadSizeClass.M : PadSizeClass.S;
            float hash = Hash01(seed, id, 500u + (uint)slot);
            PadSprite sprite = dome && size == PadSizeClass.L ? atlas.PickDome(hash) : atlas.Pick(size, hash);
            bool mirrored = Hash01(seed, id, 600u + (uint)slot) >= 0.5f;
            _padPuffs.Add(new Puff
            {
                Anchor = anchor,
                Width = width,
                Height = width * sprite.Aspect,
                Sprite = sprite,
                Mirrored = mirrored,
                Brightness = brightness,
                Order = _padPuffs.Count,
            });
            _puffRecords.Add(new PuffRecord(id, slot, sprite.Uv, mirrored, size));
        }

        /// <summary>
        /// ⚑P BALANCE §1.3.4: the apex dome = the pad holding the crown axis or a trunk-chain node, or growing from the crown axis
        /// or from the chain top (k4 laterals before the crown axis grows).
        /// </summary>
        bool IsDomePad(int root)
        {
            int parent = _nodes[root].ParentIndex;
            if (parent >= 0 && (_shape.IsCrownAxis(parent) || (_shape.IsTrunkChain(parent) && _nodes[parent].Depth == _chainTopDepth))) return true;
            for (int i = root; i < _count; i++)
            {
                if (_shape.PadId(i) == root && (_shape.IsCrownAxis(i) || _shape.IsTrunkChain(i))) return true;
            }
            return false;
        }

        /// <summary>
        /// ⚑P BALANCE §1.3.4 / ADR-0004 rev.3 §2 tip coverage: every tip of an InPad node of the pad with LeafScale ≥ 0.5 must lie in
        /// the solid ellipse of one of the pad's sprites; an uncovered tip gets one S sprite centred on it (≤ PadExtraSpritesMax per pad,
        /// overflow counted in <see cref="LastTipOverflow"/>).
        /// </summary>
        void CoverPadTips(int root, FoliageConfig foliage, TexturedStyleConfig style, PadAtlas atlas, uint seed)
        {
            int extra = 0;
            for (int i = root; i < _count; i++)
            {
                if (_shape.PadId(i) != root || !_shape.InPad(i) || _nodes[i].LeafScale < 0.5f) continue;
                Vector2 tip = _tip[i];
                if (TipCovered(tip, style)) continue;
                if (extra >= style.PadExtraSpritesMax)
                {
                    LastTipOverflow++;
                    continue;
                }
                float width = style.PadTipSpriteRadii * foliage.PuffRadius;
                int slot = 200 + extra;
                PadSprite sprite = atlas.Pick(PadSizeClass.S, Hash01(seed, _nodes[i].Id, 500u + (uint)slot));
                float height = width * sprite.Aspect;
                Vector2 anchor = tip + new Vector2((sprite.Pivot.x - 0.5f) * width, (sprite.Pivot.y - style.PadSolidCenterY) * height);
                AddClusterSprite(atlas, seed, _nodes[i].Id, slot, anchor, width, foliage.PuffRadius, style, 0f);
                extra++;
            }
            LastExtraTipSprites += extra;
        }

        bool TipCovered(Vector2 tip, TexturedStyleConfig style)
        {
            for (int q = 0; q < _padPuffs.Count; q++)
            {
                Puff p = _padPuffs[q];
                float pivotX = p.Mirrored ? 1f - p.Sprite.Pivot.x : p.Sprite.Pivot.x;
                float cx = p.Anchor.x + (0.5f - pivotX) * p.Width;
                float cy = p.Anchor.y + (style.PadSolidCenterY - p.Sprite.Pivot.y) * p.Height;
                float dx = (tip.x - cx) / (style.PadSolidRadiusX * p.Width);
                float dy = (tip.y - cy) / (style.PadSolidRadiusY * p.Height);
                if (dx * dx + dy * dy <= 1f) return true;
            }
            return false;
        }

        /// <summary>Tips that stayed uncovered because the pad hit PadExtraSpritesMax (last textured build; AC harness).</summary>
        public int LastTipOverflow { get; private set; }

        /// <summary>Extra S sprites added to cover tips (last textured build).</summary>
        public int LastExtraTipSprites { get; private set; }

        /// <summary>Small pad (PadSize &lt; PadFullNodes): separate tufts — one small sprite per node at its tip (§1.2.5).</summary>
        void AddPadTufts(int root, FoliageConfig foliage, GrowthConfig growth, TexturedStyleConfig style, PadAtlas atlas, uint seed, float fresh)
        {
            for (int i = root; i < _count; i++)
            {
                if (_shape.PadId(i) != root) continue;
                NodeData node = _nodes[i];
                float r = foliage.PuffRadius * node.LeafScale;
                if (r <= 0.2f) continue;
                float rx = r * foliage.PuffWidthRatio;
                float ry = rx * foliage.PadFlatten;
                float width = 2f * rx * style.PadTuftScale;
                Vector2 anchor = _tip[i] - new Vector2(0f, ry * style.PadLift);
                AddClusterSprite(atlas, seed, node.Id, 255, anchor, width, foliage.PuffRadius, style,
                    node.AgeDays < growth.MatureDays ? fresh : 0f);
            }
        }

        /// <summary>Sorts the pad's sprites bottom-up (outer before inner on a tie), emits shadow underlays, then the sprites.</summary>
        void EmitPad(TexturedStyleConfig style)
        {
            float cx = 0f;
            for (int q = 0; q < _padPuffs.Count; q++) cx += _padPuffs[q].Anchor.x;
            cx /= _padPuffs.Count;
            _padPuffs.Sort((a, b) =>
            {
                float ya = Mathf.Round(a.Anchor.y * 4f), yb = Mathf.Round(b.Anchor.y * 4f);
                if (ya != yb) return ya.CompareTo(yb);
                float da = Mathf.Abs(a.Anchor.x - cx), db = Mathf.Abs(b.Anchor.x - cx);
                return da != db ? db.CompareTo(da) : a.Order.CompareTo(b.Order);
            });

            float bottom = float.MaxValue, top = float.MinValue;
            for (int q = 0; q < _padPuffs.Count; q++)
            {
                Puff puff = _padPuffs[q];
                float y0 = puff.Anchor.y - puff.Sprite.Pivot.y * puff.Height;
                bottom = Mathf.Min(bottom, y0);
                top = Mathf.Max(top, y0 + puff.Height);
            }
            float shadeSpan = Mathf.Max(1e-3f, (top - bottom) * style.PadUndersideHeight);

            Color shadow = _linear ? style.PadShadowTint.linear : style.PadShadowTint;
            shadow.a = style.PadShadowTint.a;
            for (int q = 0; q < _padPuffs.Count; q++)
            {
                Puff puff = _padPuffs[q];
                Vector2 offset = new Vector2(style.PadShadowOffset.x * puff.Width, -style.PadShadowOffset.y * puff.Height);
                AddPuffQuad(puff, offset, style.PadShadowScale, shadow, shadow, 0f);
            }
            for (int q = 0; q < _padPuffs.Count; q++)
            {
                Puff puff = _padPuffs[q];
                float y0 = puff.Anchor.y - puff.Sprite.Pivot.y * puff.Height;
                Color low = ShadeColor(1f - style.PadUndersideShade * (1f - Mathf.Clamp01((y0 - bottom) / shadeSpan)));
                Color high = ShadeColor(1f - style.PadUndersideShade * (1f - Mathf.Clamp01((y0 + puff.Height - bottom) / shadeSpan)));
                AddPuffQuad(puff, Vector2.zero, 1f, low, high, puff.Brightness);
            }
        }

        Color ShadeColor(float value)
        {
            float v = _linear ? Mathf.Pow(Mathf.Clamp01(value), 2.2f) : Mathf.Clamp01(value);
            return new Color(v, v, v, 1f);
        }

        void AddPuffQuad(in Puff puff, Vector2 offset, float scale, Color bottomColor, Color topColor, float brightness)
        {
            float w = puff.Width * scale, h = puff.Height * scale;
            float pivotX = puff.Mirrored ? 1f - puff.Sprite.Pivot.x : puff.Sprite.Pivot.x;
            Vector2 min = puff.Anchor + offset - new Vector2(pivotX * w, puff.Sprite.Pivot.y * h);
            Rect uv = puff.Sprite.Uv;
            float u0 = puff.Mirrored ? uv.xMax : uv.xMin;
            float u1 = puff.Mirrored ? uv.xMin : uv.xMax;
            int start = _pPos.Count;
            _pPos.Add(new Vector3(min.x, min.y, 0f)); _pUv0.Add(new Vector2(u0, uv.yMin)); _pCol.Add(bottomColor);
            _pPos.Add(new Vector3(min.x, min.y + h, 0f)); _pUv0.Add(new Vector2(u0, uv.yMax)); _pCol.Add(topColor);
            _pPos.Add(new Vector3(min.x + w, min.y + h, 0f)); _pUv0.Add(new Vector2(u1, uv.yMax)); _pCol.Add(topColor);
            _pPos.Add(new Vector3(min.x + w, min.y, 0f)); _pUv0.Add(new Vector2(u1, uv.yMin)); _pCol.Add(bottomColor);
            for (int k = 0; k < 4; k++) _pUv1.Add(new Vector2(brightness, 0f));
            _pIdx.Add(start); _pIdx.Add(start + 1); _pIdx.Add(start + 2);
            _pIdx.Add(start); _pIdx.Add(start + 2); _pIdx.Add(start + 3);
        }
    }
}
