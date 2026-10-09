using UnityEngine;

namespace PocketArbor.Spike.Render
{
    /// <summary>
    /// B* wood mesh from the sketch arrays: tubes along exactly the primitives the SDF uses
    /// (trunk: 96 round cones DAT[0..96]; roots: B-spline points OR..; limbs: chunks OBR.. grouped by CB sign),
    /// plus the soil disc inside the pot. Vertices are only an initial guess: the GPU bake pass snaps every
    /// vertex along its normal onto the zero set of the sketch's full map() (with smin fillets), so joints
    /// get the sketch's smooth-min instead of collars. TMax limits that snap per vertex.
    /// </summary>
    public sealed class WoodMeshBuilder
    {
        // Tessellation (sketch units).
        public float RingSpacingK = 0.55f, RingSpacingMin = 0.45f, RingSpacingMax = 1.8f;
        public float SideLength = 2.0f;
        public int MinSides = 6, MaxSides = 44;
        public float SoilStep = 1.6f;

        private Vector3[] _cp = new Vector3[512];
        private float[] _cr = new float[512];
        private Vector3[] _sp = new Vector3[16384];
        private float[] _sr = new float[16384];
        private Vector3[] _st = new Vector3[16384];
        private readonly int[] _chunkGroup = new int[SketchConst.MAXCH];
        // Index ranges of limbs (for cuts: the tree without a branch = the same vertices, fewer indices).
        private int[] _limbGroup = new int[256], _limbIdx0 = new int[256], _limbIdxN = new int[256];

        /// <summary>Group (GM index) of every chunk of the last build, -1 if none.</summary>
        public int[] ChunkGroup => _chunkGroup;
        private int[] _soilMap = new int[1];

        public int LimbCount { get; private set; }
        public int TrunkVerts { get; private set; }
        public int RootVerts { get; private set; }
        public int LimbVerts { get; private set; }
        public int SoilVerts { get; private set; }

        /// <summary>Coarser preset closer to the ADR-0011 budget (20-35k wood triangles).</summary>
        public void UseBudgetPreset()
        {
            RingSpacingK = 1.0f; RingSpacingMin = 0.6f; RingSpacingMax = 3.2f;
            SideLength = 3.4f; MinSides = 5; MaxSides = 28;
            SoilStep = 3.0f;
        }

        public void Build(SketchFrame f, SpeciesLook look, GeoBuffer b)
        {
            b.Clear();
            float kr = f.Kr;
            float tmaxBase = Mathf.Max(1.5f, 0.6f * kr);

            // Trunk: 97 spheres, round cones between them.
            int n = 0;
            for (int k = 0; k <= SketchConst.NT; k++) n = PushCp(n, f.Dat, k);
            Tube(b, n, true, tmaxBase);
            TrunkVerts = b.VCount;

            // Roots: 13 B-spline points each.
            for (int r = 0; r < f.Nr; r++)
            {
                n = 0;
                for (int k = 0; k < SketchConst.RP; k++) n = PushCp(n, f.Dat, SketchConst.OR + r * SketchConst.RP + k);
                Tube(b, n, true, tmaxBase);
            }

            RootVerts = b.VCount - TrunkVerts;
            // Limbs: a chunk with CB.w < 0 starts a limb, following chunks with CB.w >= 0 continue it.
            for (int c = 0; c < SketchConst.MAXCH; c++) _chunkGroup[c] = -1;
            for (int g = 0; g < f.Ng; g++)
            {
                int c0 = (int)f.Gm[g * 8 + 5], cn = (int)f.Gm[g * 8 + 6];
                for (int c = c0; c < c0 + cn && c < SketchConst.MAXCH; c++) _chunkGroup[c] = g;
            }
            LimbCount = 0;
            int cc = 0;
            while (cc < f.Nc)
            {
                int start = cc;
                n = PushChunk(0, f.Dat, cc, true);
                cc++;
                while (cc < f.Nc && f.Cb[cc * 4 + 3] >= 0f)
                {
                    n = PushChunk(n, f.Dat, cc, false);
                    cc++;
                }
                int g = _chunkGroup[start];
                float kg = g >= 0 ? f.Gm[g * 8 + 4] : 1f, ks = g >= 0 ? f.Gm[g * 8 + 7] : 1f;
                bool first = g >= 0 && (int)f.Gm[g * 8 + 5] == start;
                float tmax = Mathf.Max(0.8f, 0.6f * (first ? Mathf.Max(kg, ks) : ks));
                int i0 = b.ICount;
                Tube(b, n, true, tmax);
                if (LimbCount == _limbGroup.Length)
                {
                    System.Array.Resize(ref _limbGroup, LimbCount * 2);
                    System.Array.Resize(ref _limbIdx0, LimbCount * 2);
                    System.Array.Resize(ref _limbIdxN, LimbCount * 2);
                }
                _limbGroup[LimbCount] = g;
                _limbIdx0[LimbCount] = i0;
                _limbIdxN[LimbCount] = b.ICount - i0;
                LimbCount++;
            }

            LimbVerts = b.VCount - TrunkVerts - RootVerts;
            Soil(b, look);
            SoilVerts = b.VCount - TrunkVerts - RootVerts - LimbVerts;
        }

        /// <summary>Indices of <paramref name="b"/> without the limbs of group <paramref name="group"/> (into <paramref name="dst"/>). Returns count.</summary>
        public int IndicesWithoutGroup(GeoBuffer b, int group, ref int[] dst)
        {
            if (dst == null || dst.Length < b.ICount) dst = new int[b.ICount];
            int n = 0, at = 0;
            for (int l = 0; l < LimbCount; l++)
            {
                int i0 = _limbIdx0[l];
                System.Array.Copy(b.Idx, at, dst, n, i0 - at);
                n += i0 - at;
                at = i0 + _limbIdxN[l];
                if (_limbGroup[l] != group)
                {
                    System.Array.Copy(b.Idx, i0, dst, n, _limbIdxN[l]);
                    n += _limbIdxN[l];
                }
            }
            System.Array.Copy(b.Idx, at, dst, n, b.ICount - at);
            n += b.ICount - at;
            return n;
        }

        private int PushCp(int n, float[] dat, int idx)
        {
            _cp[n] = new Vector3(dat[idx * 4], dat[idx * 4 + 1], dat[idx * 4 + 2]);
            _cr[n] = dat[idx * 4 + 3];
            return n + 1;
        }

        private int PushChunk(int n, float[] dat, int chunk, bool includeFirst)
        {
            int b = SketchConst.OBR + chunk * 5;
            for (int k = includeFirst ? 0 : 1; k < 5; k++) n = PushCp(n, dat, b + k);
            return n;
        }

        private static Vector3 AnyPerp(Vector3 t)
        {
            Vector3 a = Mathf.Abs(t.x) < 0.9f ? Vector3.right : Vector3.up;
            return Vector3.Cross(t, a).normalized;
        }

        /// <summary>Sweeps rings along the control polyline (linear centre and radius per segment, as sdRC/sdTC).</summary>
        private void Tube(GeoBuffer b, int n, bool capEnd, float tmax)
        {
            if (n < 2) return;
            int m = 0;
            float rmax = 0f;
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 a = _cp[i], c = _cp[i + 1];
                float ra = _cr[i], rc = _cr[i + 1];
                float len = (c - a).magnitude;
                rmax = Mathf.Max(rmax, Mathf.Max(ra, rc));
                if (len < 1e-4f) continue;
                float ds = Mathf.Clamp(RingSpacingK * Mathf.Min(ra, rc), RingSpacingMin, RingSpacingMax);
                int sub = Mathf.Clamp(Mathf.CeilToInt(len / ds), 1, 64);
                for (int s = 0; s < sub; s++)
                {
                    if (m >= _sp.Length - 2) break;
                    float u = (float)s / sub;
                    _sp[m] = a + (c - a) * u;
                    _sr[m] = ra + (rc - ra) * u;
                    m++;
                }
            }
            _sp[m] = _cp[n - 1];
            _sr[m] = _cr[n - 1];
            m++;
            if (m < 2) return;

            for (int i = 0; i < m; i++)
            {
                Vector3 d = _sp[Mathf.Min(i + 1, m - 1)] - _sp[Mathf.Max(i - 1, 0)];
                _st[i] = d.sqrMagnitude > 1e-12f ? d.normalized : (i > 0 ? _st[i - 1] : Vector3.up);
            }

            int sides = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * rmax / SideLength), MinSides, MaxSides);
            sides += sides & 1;

            Vector3 N = AnyPerp(_st[0]);
            int prev = -1;
            for (int i = 0; i < m; i++)
            {
                Vector3 T = _st[i];
                N -= T * Vector3.Dot(N, T);
                if (N.sqrMagnitude < 1e-8f) N = AnyPerp(T);
                N.Normalize();
                Vector3 B = Vector3.Cross(T, N);
                int ring = b.VCount;
                for (int j = 0; j < sides; j++)
                {
                    float a = j * (2f * Mathf.PI / sides);
                    Vector3 er = N * Mathf.Cos(a) + B * Mathf.Sin(a);
                    b.AddVertex(_sp[i] + er * _sr[i], er, tmax, 0f);
                }
                if (prev >= 0) Stitch(b, prev, ring, sides);
                prev = ring;
            }

            if (!capEnd) return;
            {
                Vector3 c = _sp[m - 1], T = _st[m - 1];
                float r = _sr[m - 1];
                N -= T * Vector3.Dot(N, T);
                if (N.sqrMagnitude < 1e-8f) N = AnyPerp(T);
                N.Normalize();
                Vector3 B = Vector3.Cross(T, N);
                const int caps = 3;
                for (int q = 1; q < caps; q++)
                {
                    float phi = q * (0.5f * Mathf.PI) / caps;
                    float sp = Mathf.Sin(phi), cp = Mathf.Cos(phi);
                    int ring = b.VCount;
                    for (int j = 0; j < sides; j++)
                    {
                        float a = j * (2f * Mathf.PI / sides);
                        Vector3 er = N * Mathf.Cos(a) + B * Mathf.Sin(a);
                        Vector3 nn = er * cp + T * sp;
                        b.AddVertex(c + nn * r, nn, tmax, 0f);
                    }
                    Stitch(b, prev, ring, sides);
                    prev = ring;
                }
                int apex = b.AddVertex(c + T * r, T, tmax, 0f);
                for (int j = 0; j < sides; j++)
                {
                    int j1 = (j + 1) % sides;
                    b.AddTri(prev + j, prev + j1, apex);
                }
            }
        }

        // Outward winding: cross(a_{j+1} - a_j, b_j - a_j) = cross(e_theta, T) = e_r.
        private static void Stitch(GeoBuffer b, int a0, int b0, int sides)
        {
            for (int j = 0; j < sides; j++)
            {
                int j1 = (j + 1) % sides;
                b.AddTri(a0 + j, a0 + j1, b0 + j);
                b.AddTri(a0 + j1, b0 + j1, b0 + j);
            }
        }

        /// <summary>Flat soil grid at y = 0 inside the pot cavity (+1.5 margin into the wall).</summary>
        private void Soil(GeoBuffer b, SpeciesLook s)
        {
            float sc = PotSdf.BScale(0f, s.PotDep);
            float bx = s.PotHw * sc - 8f, bz = s.PotHd * sc - 8f;
            bool ell = s.PotShape == 1;
            float step = SoilStep;
            int hx = Mathf.CeilToInt((bx + 3f) / step), hz = Mathf.CeilToInt((bz + 3f) / step);
            int w = 2 * hx + 1, h = 2 * hz + 1;
            if (_soilMap.Length < w * h) _soilMap = new int[w * h];
            for (int iz = 0; iz < h; iz++)
            for (int ix = 0; ix < w; ix++)
            {
                float x = (ix - hx) * step, z = (iz - hz) * step;
                _soilMap[iz * w + ix] = PotSdf.PShape(ell, x, z, bx, bz) < 1.5f ? b.AddVertex(new Vector3(x, 0f, z), Vector3.up, 3f, 1f) : -1;
            }
            for (int iz = 0; iz < h - 1; iz++)
            for (int ix = 0; ix < w - 1; ix++)
            {
                int v00 = _soilMap[iz * w + ix], v10 = _soilMap[iz * w + ix + 1];
                int v01 = _soilMap[(iz + 1) * w + ix], v11 = _soilMap[(iz + 1) * w + ix + 1];
                if (v00 < 0 || v10 < 0 || v01 < 0 || v11 < 0) continue;
                b.AddTri(v00, v01, v10);
                b.AddTri(v10, v01, v11);
            }
        }
    }
}
