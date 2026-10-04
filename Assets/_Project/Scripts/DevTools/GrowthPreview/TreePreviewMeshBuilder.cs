using System;
using System.Collections.Generic;
using PocketArbor.Domain.Tree;
using UnityEngine;

namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>
    /// Debug mesh of a tree (PA-102V, not the PA-103 renderer): pot + Pot Lip line, branches as tapered cubic
    /// Bezier strips with round joints, leaves and buds as discs. One mesh, vertex colors, drawn in index order.
    /// World: 1 unit = 1 biological unit, Pot Lip at y = 0 (Unity Y-up), positive angle = counter-clockwise.
    /// </summary>
    public sealed class TreePreviewMeshBuilder
    {
        /// <summary>BALANCE §1 proposal for PA-103: control-point offset = Curvature × 0.25 × Length.</summary>
        public const float CurvatureOffsetRatio = 0.25f;

        const int BezierSegments = 10;
        const int DiscSegments = 14;
        const float LeafRadius = 5.5f;
        const float BudRadius = 1.4f;
        const float PotWidth = 70f;
        const float PotHeight = 22f;
        const float PotLipOverhang = 5f;
        const float PotLipThickness = 1.2f;

        // Dev colours (bark/pot are placeholders; leaf colours are BALANCE §3).
        static readonly Color32 Bark = new Color32(0x5D, 0x43, 0x37, 0xFF);
        static readonly Color32 Pot = new Color32(0xA8, 0x6B, 0x4A, 0xFF);
        static readonly Color32 PotLip = new Color32(0x4A, 0x35, 0x25, 0xFF);
        static readonly Color32 Soil = new Color32(0x63, 0x48, 0x32, 0xFF);
        static readonly Color32 BudColor = new Color32(0xC9, 0xD8, 0x6B, 0xFF);

        readonly List<Vector3> _vertices = new List<Vector3>(16384);
        readonly List<Color32> _colors = new List<Color32>(16384);
        readonly List<int> _indices = new List<int>(49152);
        Vector2[] _tips = new Vector2[TreeGraph.Capacity];
        Vector2[] _dirs = new Vector2[TreeGraph.Capacity];
        bool _linear;

        public static Color32 LeafColor(SpeciesId species)
        {
            switch (species)
            {
                case SpeciesId.Momiji: return new Color32(0x5C, 0x8A, 0x42, 0xE6);    // #5C8A42
                case SpeciesId.Yamazakura: return new Color32(0x6A, 0x99, 0x4E, 0xE6); // #6A994E
                default: return new Color32(0x2E, 0x4F, 0x2D, 0xE6);                  // #2E4F2D Kuromatsu
            }
        }

        /// <summary>Fills <paramref name="mesh"/>; returns world bounds of everything drawn.</summary>
        public Bounds Build(TreeGraph tree, Mesh mesh)
        {
            _vertices.Clear();
            _colors.Clear();
            _indices.Clear();
            _linear = QualitySettings.activeColorSpace == ColorSpace.Linear;

            ReadOnlySpan<NodeData> nodes = tree.Nodes;
            ComputeFrames(tree, nodes);

            for (int i = 0; i < nodes.Length; i++) AddBranch(nodes, i);

            // Pot body after branches: it hides the trunk base, nothing is drawn below the Pot Lip.
            AddRect(new Vector2(-PotWidth * 0.5f, -PotHeight), new Vector2(PotWidth * 0.5f, 0f), Pot);
            AddRect(new Vector2(-PotWidth * 0.5f + 2f, -2.5f), new Vector2(PotWidth * 0.5f - 2f, 0f), Soil);

            Color32 leaf = LeafColor(tree.Genome.Species);
            for (int i = 0; i < nodes.Length; i++)
            {
                if (!nodes[i].HasLeaves || nodes[i].LeafScale <= 0f) continue;
                Vector2 tip = _tips[i];
                Vector2 mid = BasePoint(nodes, i) + _dirs[i] * (float)nodes[i].Length * 0.55f;
                AddDisc(tip, LeafRadius * nodes[i].LeafScale, leaf);
                AddDisc(mid, LeafRadius * 0.7f * nodes[i].LeafScale, leaf);
            }

            for (int i = 0; i < nodes.Length; i++)
            {
                if (nodes[i].Status == NodeStatus.Bud) AddDisc(_tips[i], BudRadius, BudColor);
            }

            // Pot Lip line on top of everything: nothing grown may appear below it.
            AddRect(new Vector2(-PotWidth * 0.5f - PotLipOverhang, -PotLipThickness * 0.5f),
                new Vector2(PotWidth * 0.5f + PotLipOverhang, PotLipThickness * 0.5f), PotLip);

            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(_vertices);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_indices, 0, true);

            Bounds bounds = mesh.bounds;
            return bounds;
        }

        void ComputeFrames(TreeGraph tree, ReadOnlySpan<NodeData> nodes)
        {
            for (int i = 0; i < nodes.Length; i++)
            {
                float world = (float)tree.WorldAngle(i);
                _dirs[i] = new Vector2(-Mathf.Sin(world), Mathf.Cos(world));
                _tips[i] = BasePoint(nodes, i) + _dirs[i] * (float)nodes[i].Length;
            }
        }

        Vector2 BasePoint(ReadOnlySpan<NodeData> nodes, int i) =>
            nodes[i].ParentIndex >= 0 ? _tips[nodes[i].ParentIndex] : Vector2.zero;

        void AddBranch(ReadOnlySpan<NodeData> nodes, int i)
        {
            NodeData node = nodes[i];
            Vector2 p0 = BasePoint(nodes, i);
            Vector2 p3 = _tips[i];
            Vector2 dir = _dirs[i];
            Vector2 normal = new Vector2(-dir.y, dir.x);
            float length = (float)node.Length;
            Vector2 offset = normal * ((float)node.Curvature * CurvatureOffsetRatio * length);
            Vector2 p1 = p0 + dir * (length / 3f) + offset;
            Vector2 p2 = p0 + dir * (length * 2f / 3f) + offset;

            float baseWidth = Mathf.Max(0.3f, (float)node.Thickness);
            float tipWidth = baseWidth * 0.6f;
            for (int c = i + 1; c < nodes.Length; c++)
            {
                if (nodes[c].ParentIndex == i) tipWidth = Mathf.Max(tipWidth, (float)nodes[c].Thickness);
            }
            tipWidth = Mathf.Min(tipWidth, baseWidth);

            int start = _vertices.Count;
            for (int s = 0; s <= BezierSegments; s++)
            {
                float t = s / (float)BezierSegments;
                Vector2 point = Bezier(p0, p1, p2, p3, t);
                Vector2 tangent = BezierTangent(p0, p1, p2, p3, t);
                if (tangent.sqrMagnitude < 1e-8f) tangent = dir;
                tangent.Normalize();
                Vector2 side = new Vector2(-tangent.y, tangent.x) * (Mathf.Lerp(baseWidth, tipWidth, t) * 0.5f);
                AddVertex(point + side, Bark);
                AddVertex(point - side, Bark);
            }
            for (int s = 0; s < BezierSegments; s++)
            {
                int a = start + s * 2;
                _indices.Add(a); _indices.Add(a + 1); _indices.Add(a + 2);
                _indices.Add(a + 1); _indices.Add(a + 3); _indices.Add(a + 2);
            }

            if (node.ParentIndex >= 0) AddDisc(p0, baseWidth * 0.5f, Bark); // round joint
            AddDisc(p3, tipWidth * 0.5f, Bark);
        }

        static Vector2 Bezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float u = 1f - t;
            return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }

        static Vector2 BezierTangent(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float u = 1f - t;
            return 3f * u * u * (p1 - p0) + 6f * u * t * (p2 - p1) + 3f * t * t * (p3 - p2);
        }

        void AddDisc(Vector2 center, float radius, Color32 color)
        {
            if (radius <= 0f) return;
            int start = _vertices.Count;
            AddVertex(center, color);
            for (int s = 0; s < DiscSegments; s++)
            {
                float a = s * (Mathf.PI * 2f / DiscSegments);
                AddVertex(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, color);
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
