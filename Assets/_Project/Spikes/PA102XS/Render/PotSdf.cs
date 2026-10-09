using System;
using UnityEngine;

namespace PocketArbor.Spike.Render
{
    /// <summary>Scalar field for <see cref="SurfaceNets"/>.</summary>
    public abstract class IsoField
    {
        public abstract float Eval(float x, float y, float z);

        public Vector3 Gradient(Vector3 p, float e)
        {
            float dx = Eval(p.x + e, p.y, p.z) - Eval(p.x - e, p.y, p.z);
            float dy = Eval(p.x, p.y + e, p.z) - Eval(p.x, p.y - e, p.z);
            float dz = Eval(p.x, p.y, p.z + e) - Eval(p.x, p.y, p.z - e);
            return new Vector3(dx, dy, dz) / (2f * e);
        }
    }

    /// <summary>C# port of the sketch's potD (body, lip, 4 feet, open cavity) — 1:1 with FS.</summary>
    public sealed class PotSdf : IsoField
    {
        private readonly float _hw, _hd, _dep;
        private readonly bool _ell;

        public PotSdf(SpeciesLook s)
        {
            _hw = s.PotHw;
            _hd = s.PotHd;
            _dep = s.PotDep;
            _ell = s.PotShape == 1;
        }

        public float Hw => _hw;
        public float Hd => _hd;
        public float Dep => _dep;

        public static float SdRR(float qx, float qy, float bx, float by, float r)
        {
            float dx = Math.Abs(qx) - bx + r, dy = Math.Abs(qy) - by + r;
            float ox = Math.Max(dx, 0f), oy = Math.Max(dy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(dx, dy), 0f) - r;
        }

        public static float Ell(float x, float z, float ax, float az)
        {
            float qx = x / ax, qz = z / az;
            float k0 = Mathf.Sqrt(qx * qx + qz * qz);
            float kx = x / (ax * ax), kz = z / (az * az);
            float k1 = Mathf.Sqrt(kx * kx + kz * kz);
            return k0 * (k0 - 1f) / Math.Max(k1, 1e-4f);
        }

        public static float PShape(bool ell, float x, float z, float bx, float bz)
        {
            return ell ? Ell(x, z, bx, bz) : SdRR(x, z, bx, bz, 12f);
        }

        /// <summary>potB scale at height y.</summary>
        public static float BScale(float y, float dep)
        {
            return 0.9f + 0.1f * Mathf.Clamp01((y + dep) / (dep + 4f));
        }

        /// <summary>Inner (cavity / soil) shape at height y; negative inside.</summary>
        public float Inner(float x, float y, float z)
        {
            float s = BScale(y, _dep);
            return PShape(_ell, x, z, _hw * s - 8f, _hd * s - 8f);
        }

        public override float Eval(float x, float y, float z)
        {
            float s = BScale(y, _dep);
            float bx = _hw * s, bz = _hd * s;
            float body = Math.Max(PShape(_ell, x, z, bx, bz), Math.Max(y - 2f, -y - _dep)) - 2.5f;
            float lip = Math.Max(PShape(_ell, x, z, _hw + 3.5f, _hd + 3.5f), Math.Abs(y - 1.5f) - 1.6f) - 1.4f;
            float ft = 1e5f;
            for (int i = 0; i < 4; i++)
            {
                float cx, cz, d;
                if (_ell)
                {
                    float a = i < 2 ? .75f : 2.39f;
                    cx = Mathf.Sin(a) * (i % 2 == 0 ? 1f : -1f) * _hw * .8f;
                    cz = Mathf.Cos(a) * _hd * .8f;
                    float ddx = x - cx, ddz = z - cz;
                    d = Mathf.Sqrt(ddx * ddx + ddz * ddz) - 10f;
                }
                else
                {
                    cx = (i % 2 == 0 ? 1f : -1f) * (_hw - 30f);
                    cz = (i < 2 ? 1f : -1f) * (_hd - 15f);
                    d = SdRR(x - cx, z - cz, 17f, 9f, 4f);
                }
                ft = Math.Min(ft, d);
            }
            ft = Math.Max(ft, Math.Abs(y + _dep + 3f) - 4f) - 1.2f;
            float cav = Math.Max(PShape(_ell, x, z, bx - 8f, bz - 8f), -y - (_dep - 10f));
            return Math.Max(Math.Min(Math.Min(body, lip), ft), -cav);
        }
    }

    /// <summary>
    /// Naive Surface Nets on a regular grid. Vertices are projected onto the zero set (Newton on the field),
    /// normals from the field gradient, quads oriented by the gradient. Used for the pot (static per species).
    /// </summary>
    public static class SurfaceNets
    {
        /// <summary>Returns true if a vertex is hidden (quads with 4 hidden vertices are dropped).</summary>
        public delegate bool HiddenTest(Vector3 p);

        private static readonly int[] EdgeA = { 0, 2, 4, 6, 0, 1, 4, 5, 0, 1, 2, 3 };
        private static readonly int[] EdgeB = { 1, 3, 5, 7, 2, 3, 6, 7, 4, 5, 6, 7 };

        public static void Build(IsoField f, Vector3 min, Vector3 max, float h, GeoBuffer buf, float kind, HiddenTest hidden)
        {
            int nx = Mathf.CeilToInt((max.x - min.x) / h);
            int ny = Mathf.CeilToInt((max.y - min.y) / h);
            int nz = Mathf.CeilToInt((max.z - min.z) / h);
            int cx = nx + 1, cy = ny + 1, cz = nz + 1;
            float[] val = new float[cx * cy * cz];
            for (int k = 0; k < cz; k++)
            for (int j = 0; j < cy; j++)
            for (int i = 0; i < cx; i++)
                val[(k * cy + j) * cx + i] = f.Eval(min.x + i * h, min.y + j * h, min.z + k * h);

            int[] cellV = new int[nx * ny * nz];
            for (int i = 0; i < cellV.Length; i++) cellV[i] = -1;
            float[] c = new float[8];
            for (int k = 0; k < nz; k++)
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int mask = 0;
                for (int n = 0; n < 8; n++)
                {
                    int ox = n & 1, oy = (n >> 1) & 1, oz = (n >> 2) & 1;
                    c[n] = val[((k + oz) * cy + (j + oy)) * cx + (i + ox)];
                    if (c[n] < 0f) mask |= 1 << n;
                }
                if (mask == 0 || mask == 255) continue;
                Vector3 sum = Vector3.zero;
                int cnt = 0;
                for (int e = 0; e < 12; e++)
                {
                    int a = EdgeA[e], b = EdgeB[e];
                    if ((c[a] < 0f) == (c[b] < 0f)) continue;
                    float t = c[a] / (c[a] - c[b]);
                    Vector3 pa = new Vector3(a & 1, (a >> 1) & 1, (a >> 2) & 1);
                    Vector3 pb = new Vector3(b & 1, (b >> 1) & 1, (b >> 2) & 1);
                    sum += pa + (pb - pa) * t;
                    cnt++;
                }
                Vector3 p = min + new Vector3(i, j, k) * h + sum / cnt * h;
                for (int it = 0; it < 3; it++)
                {
                    Vector3 g = f.Gradient(p, 0.05f);
                    float d = f.Eval(p.x, p.y, p.z);
                    float g2 = Math.Max(g.sqrMagnitude, 1e-6f);
                    Vector3 step = g * (d / g2);
                    if (step.magnitude > h) step = step.normalized * h;
                    p -= step;
                }
                Vector3 nrm = f.Gradient(p, 0.05f).normalized;
                cellV[(k * ny + j) * nx + i] = buf.AddVertex(p, nrm, 0f, kind);
            }

            // x edges
            for (int k = 1; k < nz; k++)
            for (int j = 1; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                bool s0 = val[(k * cy + j) * cx + i] < 0f, s1 = val[(k * cy + j) * cx + i + 1] < 0f;
                if (s0 == s1) continue;
                Quad(buf, hidden, cellV[((k - 1) * ny + j - 1) * nx + i], cellV[((k - 1) * ny + j) * nx + i],
                    cellV[(k * ny + j) * nx + i], cellV[(k * ny + j - 1) * nx + i]);
            }
            // y edges
            for (int k = 1; k < nz; k++)
            for (int j = 0; j < ny; j++)
            for (int i = 1; i < nx; i++)
            {
                bool s0 = val[(k * cy + j) * cx + i] < 0f, s1 = val[(k * cy + j + 1) * cx + i] < 0f;
                if (s0 == s1) continue;
                Quad(buf, hidden, cellV[((k - 1) * ny + j) * nx + i - 1], cellV[((k - 1) * ny + j) * nx + i],
                    cellV[(k * ny + j) * nx + i], cellV[(k * ny + j) * nx + i - 1]);
            }
            // z edges
            for (int k = 0; k < nz; k++)
            for (int j = 1; j < ny; j++)
            for (int i = 1; i < nx; i++)
            {
                bool s0 = val[(k * cy + j) * cx + i] < 0f, s1 = val[((k + 1) * cy + j) * cx + i] < 0f;
                if (s0 == s1) continue;
                Quad(buf, hidden, cellV[(k * ny + j - 1) * nx + i - 1], cellV[(k * ny + j - 1) * nx + i],
                    cellV[(k * ny + j) * nx + i], cellV[(k * ny + j) * nx + i - 1]);
            }
        }

        private static void Quad(GeoBuffer b, HiddenTest hidden, int v0, int v1, int v2, int v3)
        {
            if (v0 < 0 || v1 < 0 || v2 < 0 || v3 < 0) return;
            if (hidden != null && hidden(b.Pos[v0]) && hidden(b.Pos[v1]) && hidden(b.Pos[v2]) && hidden(b.Pos[v3])) return;
            Vector3 n = b.Nrm[v0] + b.Nrm[v1] + b.Nrm[v2] + b.Nrm[v3];
            Vector3 fn = Vector3.Cross(b.Pos[v1] - b.Pos[v0], b.Pos[v2] - b.Pos[v0]) + Vector3.Cross(b.Pos[v2] - b.Pos[v0], b.Pos[v3] - b.Pos[v0]);
            if (Vector3.Dot(fn, n) >= 0f)
            {
                b.AddTri(v0, v1, v2);
                b.AddTri(v0, v2, v3);
            }
            else
            {
                b.AddTri(v0, v2, v1);
                b.AddTri(v0, v3, v2);
            }
        }
    }
}
