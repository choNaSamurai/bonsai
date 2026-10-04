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
            _bPos.Clear(); _bUv0.Clear(); _bUv1.Clear(); _bUv2.Clear(); _bCol.Clear(); _bIdx.Clear();
            _pPos.Clear(); _pUv0.Clear(); _pUv1.Clear(); _pCol.Clear(); _pIdx.Clear();
            _padKeys.Clear();
            _puffRecords.Clear();

            FoliageConfig foliage = Prepare(tree, growth, species);
            uint seed = tree.Genome.Seed;

            int roots = NebariRootCount(growth);
            for (int r = 0; r < roots; r++)
            {
                NebariRoot(seed, r, roots, out Vector2 a, out Vector2 c, out Vector2 b, out float thickness);
                AddBarkRoot(a, c, b, thickness, Hash01(seed, _nodes[0].Id, 307u + (uint)r) * style.BarkTileWorld * 4f, style);
            }
            int treeStart = _bPos.Count;

            _trunkSamples.Clear();
            for (int i = 0; i < _count; i++) AddBarkNode(i, seed, style);
            LastTaperRatio = MeasureTaper();

            if (atlas != null) AddTexturedPads(foliage, growth, style, atlas, seed);

            float minX = float.MaxValue, maxX = float.MinValue, maxY = 0f;
            for (int v = treeStart; v < _bPos.Count; v++) Extend(_bPos[v], ref minX, ref maxX, ref maxY);
            for (int v = 0; v < _pPos.Count; v++) Extend(_pPos[v], ref minX, ref maxX, ref maxY);
            LastHeight = maxY;
            LastCrownWidth = maxX > minX ? maxX - minX : 0f;
            LastTrunkBase = _th0;
            LastTrunkBaseFlared = _th0 * (1f + _flare);

            barkMesh.Clear();
            barkMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            barkMesh.SetVertices(_bPos);
            barkMesh.SetUVs(0, _bUv0);
            barkMesh.SetUVs(1, _bUv1);
            barkMesh.SetUVs(2, _bUv2);
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
            float arc0 = axisRoot ? Hash01(seed, node.Id, 401u) * style.BarkTileWorld * 4f : _arcEnd[parent];
            _arcStart[i] = arc0;

            // A lateral starts on its parent's centre line: collar flare + ink fading in once the ribbon leaves the parent.
            float parentHalf = 0f, collarLength = 1f;
            if (lateral)
            {
                Vector2 attach = Eval(parent, (float)node.AttachT);
                parentHalf = 0.5f * Width(parent, (float)node.AttachT, attach.y);
                collarLength = parentHalf + (float)node.Thickness;
            }

            int start = _bPos.Count;
            float arc = arc0;
            Vector2 previous = Eval(i, 0f);
            Vector2 lastTangent = _dir[i];
            float lastWidth = 0f;
            for (int s = 0; s <= SegmentSamples; s++)
            {
                float t = s / (float)SegmentSamples;
                Vector2 point = Eval(i, t);
                Vector2 tangent = Eval(i, Mathf.Min(1f, t + 0.01f)) - Eval(i, Mathf.Max(0f, t - 0.01f));
                tangent = tangent.sqrMagnitude > 1e-10f ? tangent.normalized : _dir[i];
                arc += (point - previous).magnitude;
                float width = Width(i, t, point.y);
                float ink = 1f;
                Color tint = Color.white;
                if (lateral)
                {
                    float local = arc - arc0;
                    width *= 1f + style.CollarFlare * (1f - Mathf.SmoothStep(0f, 1f, local / collarLength));
                    ink = Mathf.SmoothStep(0f, 1f, (local - 0.6f * parentHalf) / (0.6f * parentHalf + 0.5f * (float)node.Thickness + 1e-3f));
                    // The square start of the ribbon (inside the parent) fades in → no flap where it pokes out of the parent.
                    tint.a = Mathf.SmoothStep(0f, 1f, local / (style.CollarFadeIn * parentHalf + 1e-3f));
                }
                if (trunkChain) _trunkSamples.Add(new Vector2(point.y, width));
                AddBarkSection(point, tangent, width, arc, BarkAge(width, trunkChain, style), ink, tint);
                previous = point;
                lastTangent = tangent;
                lastWidth = width;
            }
            _arcEnd[i] = arc;

            int sections = SegmentSamples + 1;
            if (_shape.ApicalChild(i) < 0)
            {
                // Rounded-off terminal tip: one extra section past the tip, nearly closed.
                float capLength = style.CapLengthRatio * lastWidth;
                AddBarkSection(previous + lastTangent * capLength, lastTangent, lastWidth * style.CapWidthRatio, arc + capLength,
                    BarkAge(lastWidth, trunkChain, style), 1f, Color.white);
                sections++;
            }
            AddRibbonIndices(start, sections);
        }

        void AddBarkRoot(Vector2 a, Vector2 c, Vector2 b, float startWidth, float arc0, TexturedStyleConfig style)
        {
            const int samples = 10;
            int start = _bPos.Count;
            float arc = arc0;
            Vector2 previous = a;
            for (int s = 0; s <= samples; s++)
            {
                float t = s / (float)samples;
                float u = 1f - t;
                Vector2 point = u * u * a + 2f * u * t * c + t * t * b;
                Vector2 tangent = 2f * u * (c - a) + 2f * t * (b - c);
                tangent = tangent.sqrMagnitude > 1e-8f ? tangent.normalized : Vector2.right;
                arc += (point - previous).magnitude;
                float width = Mathf.Max(0.15f, startWidth * (1f - t * 0.92f));
                float ink = Mathf.SmoothStep(0f, 1f, (arc - arc0 - 0.2f * _th0) / (0.4f * _th0 + 1e-3f));
                AddBarkSection(point, tangent, width, arc, BarkAge(width, true, style), ink, Color.white);
                previous = point;
            }
            AddRibbonIndices(start, samples + 1);
        }

        float BarkAge(float width, bool trunkChain, TexturedStyleConfig style) =>
            Mathf.Clamp01(Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(style.YoungMaxWidth, style.TrunkMinWidth, width))
                + (trunkChain ? style.TrunkChainAgeBias : 0f));

        void AddBarkSection(Vector2 point, Vector2 tangent, float width, float arc, float barkAge, float ink, Color tint)
        {
            Vector2 side = new Vector2(-tangent.y, tangent.x);
            Vector2 half = side * (width * 0.5f);
            Color c = _linear ? tint.linear : tint;
            _bPos.Add(point + half);
            _bUv0.Add(new Vector2(0f, arc));
            _bUv1.Add(new Vector4(width, barkAge, ink, 0f));
            _bUv2.Add(side);
            _bCol.Add(c);
            _bPos.Add(point - half);
            _bUv0.Add(new Vector2(1f, arc));
            _bUv1.Add(new Vector4(width, barkAge, ink, 0f));
            _bUv2.Add(side);
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
                if (_shape.PadSize(root) >= growth.PadFullNodes) AddPadCluster(root, foliage, growth, style, atlas, seed, fresh);
                else AddPadTufts(root, foliage, growth, style, atlas, seed, fresh);
                if (_padPuffs.Count == 0) continue;
                _padKeys.Add(_padOrder[p].x);
                EmitPad(style);
            }
        }

        /// <summary>
        /// Full pad (PadSize ≥ PadFullNodes): the union of its puff ellipses (§1.2.5) is covered by a few whole-pad sprites —
        /// a row of n = ceil(width / max sprite width) along the pad's slope line, plus narrower rows on top while the union
        /// is taller than one row (apex dome). Variant/mirroring hash = (Seed, Id of the pad root, slot).
        /// </summary>
        void AddPadCluster(int root, FoliageConfig foliage, GrowthConfig growth, TexturedStyleConfig style, PadAtlas atlas, uint seed, float fresh)
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
            float slope = varX > 1f ? Mathf.Clamp((sxy / n - (sx / n) * (sy / n)) / varX, -style.PadMaxSlope, style.PadMaxSlope) : 0f;
            float intercept = sy / n - slope * sx / n;
            float brightness = fresh * freshNodes / Mathf.Max(1, nodes);

            float width = maxX - minX;
            float maxSprite = style.PadSpriteMaxWidthRatio * foliage.PuffRadius;
            int columns = Mathf.Max(1, Mathf.CeilToInt(width / maxSprite - 0.15f));
            float spriteWidth = width / columns * (columns > 1 ? style.PadClusterOverlap : style.PadSingleCover);
            float unionHeight = top - bottom;
            for (int row = 0; row < style.PadMaxRows; row++)
            {
                int count = Mathf.Max(1, columns - row);
                float rowWidth = spriteWidth * Mathf.Pow(style.PadRowShrink, row);
                float span = width * Mathf.Pow(style.PadRowShrink, row);
                float rowMin = 0.5f * (minX + maxX) - 0.5f * span;
                for (int j = 0; j < count; j++)
                {
                    float x = rowMin + (j + 0.5f) * span / count;
                    float y = intercept + slope * x - meanRy * style.PadLift + row * style.PadRowLift * rowWidth;
                    AddClusterSprite(atlas, seed, _nodes[root].Id, row * 16 + j, new Vector2(x, y), rowWidth, foliage.PuffRadius, style, brightness);
                }
                // Next row only while the union is taller than what the rows so far cover.
                float covered = rowWidth * 0.45f + row * style.PadRowLift * rowWidth;
                if (covered >= unionHeight * style.PadHeightCover) break;
            }
        }

        void AddClusterSprite(PadAtlas atlas, uint seed, int id, int slot, Vector2 anchor, float width, float puffRadius, TexturedStyleConfig style, float brightness)
        {
            PadSizeClass size = width >= style.PadLargeMinRadii * puffRadius ? PadSizeClass.L
                : width >= style.PadMediumMinRadii * puffRadius ? PadSizeClass.M : PadSizeClass.S;
            PadSprite sprite = atlas.Pick(size, Hash01(seed, id, 500u + (uint)slot));
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
