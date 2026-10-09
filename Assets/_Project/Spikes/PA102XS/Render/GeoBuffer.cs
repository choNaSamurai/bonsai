using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PocketArbor.Spike.Render
{
    /// <summary>
    /// Growable vertex/index buffer (sketch space) plus per-vertex bake inputs:
    /// TMax — how far the GPU snap may move the vertex along its normal; Kind — 0 wood tube, 1 soil, 2 pot.
    /// Uv holds the integer texel of the vertex in the bake textures (xy) and the vertex kind (z).
    /// </summary>
    public sealed class GeoBuffer
    {
        public Vector3[] Pos;
        public Vector3[] Nrm;
        public Vector3[] Uv;
        public float[] TMax;
        public float[] Kind;
        /// <summary>Relative bake cost hint per vertex (set by WoodMeshBuilder.ClassifySnap; 1 by default).</summary>
        public float[] Cost;
        public int[] Idx;
        public int VCount, ICount;

        public GeoBuffer(int vcap, int icap)
        {
            Pos = new Vector3[vcap];
            Nrm = new Vector3[vcap];
            Uv = new Vector3[vcap];
            TMax = new float[vcap];
            Kind = new float[vcap];
            Cost = new float[vcap];
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
            Cost[VCount] = 1f;
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
                Uv[v] = new Vector3(g % width, g / width, Kind[v]);
            }
        }

        public void ApplyTo(Mesh mesh, float boundsPad)
        {
            ApplyTo(mesh, boundsPad, ICount);
        }

        /// <summary>Uploads vertices and the first <paramref name="indexCount"/> indices of <see cref="Idx"/>.</summary>
        public void ApplyTo(Mesh mesh, float boundsPad, int indexCount)
        {
            mesh.Clear();
            mesh.indexFormat = VCount > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(Pos, 0, VCount);
            mesh.SetNormals(Nrm, 0, VCount);
            mesh.SetUVs(0, Uv, 0, VCount);
            mesh.SetIndices(Idx, 0, indexCount, MeshTopology.Triangles, 0, false);
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

        /// <summary>Copies vertices and indices (for double buffering).</summary>
        public void CopyFrom(GeoBuffer o)
        {
            while (Pos.Length < o.VCount) GrowV();
            if (Idx.Length < o.ICount) Array.Resize(ref Idx, o.ICount);
            Array.Copy(o.Pos, Pos, o.VCount);
            Array.Copy(o.Nrm, Nrm, o.VCount);
            Array.Copy(o.Uv, Uv, o.VCount);
            Array.Copy(o.TMax, TMax, o.VCount);
            Array.Copy(o.Kind, Kind, o.VCount);
            Array.Copy(o.Cost, Cost, o.VCount);
            Array.Copy(o.Idx, Idx, o.ICount);
            VCount = o.VCount;
            ICount = o.ICount;
        }

        private void GrowV()
        {
            int n = Pos.Length * 2;
            Array.Resize(ref Pos, n);
            Array.Resize(ref Nrm, n);
            Array.Resize(ref Uv, n);
            Array.Resize(ref TMax, n);
            Array.Resize(ref Kind, n);
            Array.Resize(ref Cost, n);
        }
    }
}
