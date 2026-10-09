using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PocketArbor.Spike.Render
{
    /// <summary>
    /// Growable vertex/index buffer (sketch space) plus per-vertex bake inputs:
    /// TMax — how far the GPU snap may move the vertex along its normal; Kind — 0 wood tube, 1 soil, 2 pot.
    /// Uv holds the integer texel of the vertex in the bake textures.
    /// </summary>
    public sealed class GeoBuffer
    {
        public Vector3[] Pos;
        public Vector3[] Nrm;
        public Vector2[] Uv;
        public float[] TMax;
        public float[] Kind;
        public int[] Idx;
        public int VCount, ICount;

        public GeoBuffer(int vcap, int icap)
        {
            Pos = new Vector3[vcap];
            Nrm = new Vector3[vcap];
            Uv = new Vector2[vcap];
            TMax = new float[vcap];
            Kind = new float[vcap];
            Idx = new int[icap];
        }

        public int TriangleCount => ICount / 3;

        public void Clear()
        {
            VCount = 0;
            ICount = 0;
        }

        public int AddVertex(Vector3 p, Vector3 n, float tmax, float kind)
        {
            if (VCount == Pos.Length) GrowV();
            Pos[VCount] = p;
            Nrm[VCount] = n;
            TMax[VCount] = tmax;
            Kind[VCount] = kind;
            return VCount++;
        }

        public void AddTri(int a, int b, int c)
        {
            if (ICount + 3 > Idx.Length) Array.Resize(ref Idx, Idx.Length * 2);
            Idx[ICount++] = a;
            Idx[ICount++] = b;
            Idx[ICount++] = c;
        }

        /// <summary>Vertex v gets bake texel (offset + v) % width, (offset + v) / width.</summary>
        public void AssignBakeTexels(int offset, int width)
        {
            for (int v = 0; v < VCount; v++)
            {
                int g = offset + v;
                Uv[v] = new Vector2(g % width, g / width);
            }
        }

        public void ApplyTo(Mesh mesh, float boundsPad)
        {
            mesh.Clear();
            mesh.indexFormat = VCount > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(Pos, 0, VCount);
            mesh.SetNormals(Nrm, 0, VCount);
            mesh.SetUVs(0, Uv, 0, VCount);
            mesh.SetIndices(Idx, 0, ICount, MeshTopology.Triangles, 0, false);
            Bounds b = ComputeBounds();
            b.Expand(boundsPad * 2f);
            mesh.bounds = b;
        }

        public Bounds ComputeBounds()
        {
            if (VCount == 0) return new Bounds(Vector3.zero, Vector3.one);
            Vector3 mn = Pos[0], mx = Pos[0];
            for (int i = 1; i < VCount; i++)
            {
                mn = Vector3.Min(mn, Pos[i]);
                mx = Vector3.Max(mx, Pos[i]);
            }
            Bounds b = new Bounds();
            b.SetMinMax(mn, mx);
            return b;
        }

        private void GrowV()
        {
            int n = Pos.Length * 2;
            Array.Resize(ref Pos, n);
            Array.Resize(ref Nrm, n);
            Array.Resize(ref Uv, n);
            Array.Resize(ref TMax, n);
            Array.Resize(ref Kind, n);
        }
    }
}
