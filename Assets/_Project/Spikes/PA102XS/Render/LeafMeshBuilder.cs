using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace PocketArbor.Spike.Render
{
    /// <summary>
    /// All leaves in one mesh: 4 vertices per LEAF record (16 floats, sketch layout), the quad is expanded
    /// in the vertex shader exactly like the sketch's LVS. Optional front-to-back order for early-Z.
    /// Attributes: POSITION = P, NORMAL = N, TANGENT.xyz = D, UV0 = corner q, UV1 = (len, wid), UV2 = (rgb, exposure).
    /// </summary>
    public sealed class LeafMeshBuilder
    {
        private Vector3[] _pos = new Vector3[4];
        private Vector3[] _nrm = new Vector3[4];
        private Vector4[] _tan = new Vector4[4];
        private Vector2[] _uv0 = new Vector2[4];
        private Vector2[] _uv1 = new Vector2[4];
        private Vector4[] _uv2 = new Vector4[4];
        private int[] _idx = new int[6];
        private float[] _key = new float[1];
        private int[] _order = new int[1];

        private static readonly Vector2[] Corners = { new Vector2(-.5f, 0f), new Vector2(.5f, 0f), new Vector2(-.5f, 1f), new Vector2(.5f, 1f) };

        public int Build(SketchFrame f, Mesh mesh, bool frontToBack)
        {
            int n = Mathf.Min(f.Nl, SketchConst.MAXLEAF);
            Ensure(n);
            float[] L = f.Leaf;
            for (int i = 0; i < n; i++)
            {
                _order[i] = i;
                int o = i * 16;
                // sketch depth t = dot(P, (0, -SE, -CE)); smaller = nearer to the viewer
                _key[i] = -SketchConst.SE * L[o + 1] - SketchConst.CE * L[o + 2];
            }
            if (frontToBack) Array.Sort(_key, _order, 0, n);

            Vector3 mn = new Vector3(1e9f, 1e9f, 1e9f), mx = -mn;
            for (int k = 0; k < n; k++)
            {
                int i = _order[k], o = i * 16, v = k * 4;
                Vector3 p = new Vector3(L[o], L[o + 1], L[o + 2]);
                Vector4 d = new Vector4(L[o + 3], L[o + 4], L[o + 5], 0f);
                Vector3 nn = new Vector3(L[o + 6], L[o + 7], L[o + 8]);
                Vector2 sz = new Vector2(L[o + 9], L[o + 10]);
                Vector4 col = new Vector4(L[o + 11], L[o + 12], L[o + 13], L[o + 14]);
                for (int c = 0; c < 4; c++)
                {
                    _pos[v + c] = p;
                    _nrm[v + c] = nn;
                    _tan[v + c] = d;
                    _uv0[v + c] = Corners[c];
                    _uv1[v + c] = sz;
                    _uv2[v + c] = col;
                }
                int t = k * 6;
                _idx[t] = v; _idx[t + 1] = v + 2; _idx[t + 2] = v + 1;
                _idx[t + 3] = v + 1; _idx[t + 4] = v + 2; _idx[t + 5] = v + 3;
                mn = Vector3.Min(mn, p);
                mx = Vector3.Max(mx, p);
            }

            mesh.Clear();
            mesh.indexFormat = n * 4 > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            int vc = n * 4;
            mesh.SetVertices(_pos, 0, vc);
            mesh.SetNormals(_nrm, 0, vc);
            mesh.SetTangents(_tan, 0, vc);
            mesh.SetUVs(0, _uv0, 0, vc);
            mesh.SetUVs(1, _uv1, 0, vc);
            mesh.SetUVs(2, _uv2, 0, vc);
            mesh.SetIndices(_idx, 0, n * 6, MeshTopology.Triangles, 0, false);
            Bounds b = new Bounds();
            if (n > 0) b.SetMinMax(mn, mx);
            b.Expand(40f);
            mesh.bounds = b;
            return n;
        }

        private void Ensure(int n)
        {
            if (_key.Length < n) { _key = new float[n]; _order = new int[n]; }
            int v = n * 4;
            if (_pos.Length < v)
            {
                _pos = new Vector3[v];
                _nrm = new Vector3[v];
                _tan = new Vector4[v];
                _uv0 = new Vector2[v];
                _uv1 = new Vector2[v];
                _uv2 = new Vector4[v];
                _idx = new int[n * 6];
            }
        }
    }
}
