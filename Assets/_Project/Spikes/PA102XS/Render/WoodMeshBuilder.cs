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
        // Tessellation (sketch units). Default = "budget" (owner's decision 2026-10-09: ~70k wood triangles).
        public float RingSpacingK = 1.0f, RingSpacingMin = 0.6f, RingSpacingMax = 3.2f;
        public float SideLength = 3.4f;
        public int MinSides = 5, MaxSides = 28;
        public float SoilStep = 3.0f;

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

        /// <summary>Budget preset (default): ~66-72k wood+soil triangles at t = 1.</summary>
        public void UseBudgetPreset()
        {
            RingSpacingK = 1.0f; RingSpacingMin = 0.6f; RingSpacingMax = 3.2f;
            SideLength = 3.4f; MinSides = 5; MaxSides = 28;
            SoilStep = 3.0f;
        }

        /// <summary>Phase-1 quality preset: ~130-144k triangles at t = 1.</summary>
        public void UseQualityPreset()
        {
            RingSpacingK = 0.55f; RingSpacingMin = 0.45f; RingSpacingMax = 1.8f;
            SideLength = 2.0f; MinSides = 6; MaxSides = 44;
            SoilStep = 1.6f;
        }

        // Vertex ranges per part (owner: trunk -1, root r -> -2-r, limb l -> l, soil -100).
        private int[] _rootV0 = new int[SketchConst.MAXR], _rootVN = new int[SketchConst.MAXR];
        private int[] _limbV0 = new int[256], _limbVN = new int[256], _limbC0 = new int[256], _limbC1 = new int[256];
        private float[] _limbK = new float[256];
        private int _soilV0;
        // Sphere list of the SDF primitives (chunk bounds) with the smin radius of their joint and their owner.
        private Vector4[] _sph = new Vector4[512];
        private float[] _sphK = new float[512];
        private int[] _sphOwner = new int[512];
        private int[] _sphSeg = new int[512];
        private int _nsph;
        public int SnapVerts { get; private set; }

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
                _rootV0[r] = b.VCount;
                Tube(b, n, true, tmaxBase);
                _rootVN[r] = b.VCount - _rootV0[r];
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
                int i0 = b.ICount, v0 = b.VCount;
                Tube(b, n, true, tmax);
                if (LimbCount == _limbGroup.Length)
                {
                    int nn = LimbCount * 2;
                    System.Array.Resize(ref _limbGroup, nn);
                    System.Array.Resize(ref _limbIdx0, nn);
                    System.Array.Resize(ref _limbIdxN, nn);
                    System.Array.Resize(ref _limbV0, nn);
                    System.Array.Resize(ref _limbVN, nn);
                    System.Array.Resize(ref _limbC0, nn);
                    System.Array.Resize(ref _limbC1, nn);
                    System.Array.Resize(ref _limbK, nn);
                }
                _limbGroup[LimbCount] = g;
                _limbIdx0[LimbCount] = i0;
                _limbIdxN[LimbCount] = b.ICount - i0;
                _limbV0[LimbCount] = v0;
                _limbVN[LimbCount] = b.VCount - v0;
                _limbC0[LimbCount] = start;
                _limbC1[LimbCount] = cc;
                _limbK[LimbCount] = Mathf.Max(kg, ks);
                LimbCount++;
            }

            LimbVerts = b.VCount - TrunkVerts - RootVerts;
            _soilV0 = b.VCount;
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
                if (_limbGroup[l] != group && _limbGroup[l] != RemovedGroup)
                {
                    System.Array.Copy(b.Idx, i0, dst, n, _limbIdxN[l]);
                    n += _limbIdxN[l];
                }
            }
            System.Array.Copy(b.Idx, at, dst, n, b.ICount - at);
            n += b.ICount - at;
            return n;
        }

        private void AddSphere(float x, float y, float z, float r, float k, int owner)
        {
            if (_nsph == _sph.Length)
            {
                System.Array.Resize(ref _sph, _nsph * 2);
                System.Array.Resize(ref _sphK, _nsph * 2);
                System.Array.Resize(ref _sphOwner, _nsph * 2);
                System.Array.Resize(ref _sphSeg, _nsph * 2);
            }
            _sph[_nsph] = new Vector4(x, y, z, r);
            _sphK[_nsph] = k;
            _sphOwner[_nsph] = owner;
            _nsph++;
        }

        private static float ChunkRadius(float w)
        {
            float aw = Mathf.Abs(w);
            return aw - 1000f * Mathf.Floor(aw / 1000f);
        }

        /// <summary>
        /// "Projection only near joints": a vertex is snapped onto the SDF only if another part's primitive (chunk bound)
        /// is within its smin radius, or it is near the soil; all other vertices get TMax = 0 (the Surf pass skips them).
        /// Call after <see cref="Build"/> on the same buffer. Worker-thread safe (no Unity API).
        /// </summary>
        public int ClassifySnap(GeoBuffer b, SketchFrame f)
        {
            float kr = f.Kr;
            _nsph = 0;
            // Chunk spheres; _sphSeg = first point index (DAT) of the chunk's 5 points, segments are p..p+4.
            for (int c = 0; c < SketchConst.NTC; c++)
            {
                int o = (SketchConst.OB + c) * 4;
                AddSphere(f.Dat[o], f.Dat[o + 1], f.Dat[o + 2], f.Dat[o + 3], kr, -1);
                _sphSeg[_nsph - 1] = c * 4;
            }
            for (int r = 0; r < f.Nr; r++)
            for (int c = 0; c < SketchConst.RC; c++)
            {
                int o = (SketchConst.ORB + r * SketchConst.RC + c) * 4;
                AddSphere(f.Dat[o], f.Dat[o + 1], f.Dat[o + 2], f.Dat[o + 3], kr, -2 - r);
                _sphSeg[_nsph - 1] = SketchConst.OR + r * SketchConst.RP + c * 4;
            }
            for (int l = 0; l < LimbCount; l++)
            for (int c = _limbC0[l]; c < _limbC1[l]; c++)
            {
                AddSphere(f.Cb[c * 4], f.Cb[c * 4 + 1], f.Cb[c * 4 + 2], ChunkRadius(f.Cb[c * 4 + 3]), _limbK[l], l);
                _sphSeg[_nsph - 1] = SketchConst.OBR + c * 5;
            }

            int snap = 0;
            snap += ClassifyRange(b, f, 0, TrunkVerts, -1);
            for (int r = 0; r < f.Nr; r++) snap += ClassifyRange(b, f, _rootV0[r], _rootVN[r], -2 - r);
            for (int l = 0; l < LimbCount; l++) snap += ClassifyRange(b, f, _limbV0[l], _limbVN[l], l);
            // Soil: only near the trunk/roots (soil/wood smin k = 2).
            for (int v = _soilV0; v < b.VCount; v++)
            {
                if (b.TMax[v] <= 0f) continue;
                _minDist = 1e9f;
                if (NearOther(f, b.Pos[v], -100, 2.5f, true))
                {
                    snap++;
                    b.Cost[v] = _minDist < -0.5f ? 30f : 5f;
                }
                else
                {
                    b.TMax[v] = 0f;
                    b.Cost[v] = 0.6f;
                }
            }
            SnapVerts = snap;
            return snap;
        }

        private int ClassifyRange(GeoBuffer b, SketchFrame f, int v0, int vn, int owner)
        {
            int snap = 0;
            for (int v = v0; v < v0 + vn; v++)
            {
                Vector3 p = b.Pos[v];
                _minDist = 1e9f;
                bool near = NearOther(f, p, owner, 0.5f, false) || p.y < 2.5f;
                if (near)
                {
                    snap++;
                    // Buried (inside another part): march fails + projection on the GPU — the expensive texels.
                    b.Cost[v] = _minDist < -0.5f || p.y < -0.5f ? 30f : 5f;
                }
                else
                {
                    b.TMax[v] = 0f;
                    b.Cost[v] = 0.6f;
                }
            }
            return snap;
        }

        /// <summary>True if a primitive (segment capsule) of another owner is within its smin radius (+extra) of p.</summary>
        private float _minDist;

        private bool NearOther(SketchFrame f, Vector3 p, int owner, float extra, bool woodBaseOnly)
        {
            float[] d = f.Dat;
            bool found = false;
            for (int s = 0; s < _nsph; s++)
            {
                int ow = _sphOwner[s];
                if (ow == owner || (woodBaseOnly && ow >= 0)) continue;
                Vector4 c = _sph[s];
                float k = woodBaseOnly ? 0f : _sphK[s];
                float dx = p.x - c.x, dy = p.y - c.y, dz = p.z - c.z;
                float m = c.w + k + extra;
                if (dx * dx + dy * dy + dz * dz >= m * m) continue;
                int i0 = _sphSeg[s];
                for (int j = 0; j < 4; j++)
                {
                    int a = (i0 + j) * 4, bb = (i0 + j + 1) * 4;
                    float ax = d[a], ay = d[a + 1], az = d[a + 2], ra = d[a + 3];
                    float bx = d[bb] - ax, by = d[bb + 1] - ay, bz = d[bb + 2] - az, rb = d[bb + 3];
                    float px = p.x - ax, py = p.y - ay, pz = p.z - az;
                    float l2 = bx * bx + by * by + bz * bz;
                    float h = l2 > 1e-8f ? Mathf.Clamp01((px * bx + py * by + pz * bz) / l2) : 0f;
                    float qx = px - bx * h, qy = py - by * h, qz = pz - bz * h;
                    float dist = Mathf.Sqrt(qx * qx + qy * qy + qz * qz) - (ra + (rb - ra) * h);
                    if (dist < _minDist) _minDist = dist;
                    if (dist < k + extra && dist < -0.5f) return true;
                    if (dist < k + extra) found = true;
                }
            }
            return found;
        }

        /// <summary>
        /// Cut of branch group <paramref name="group"/> (call BEFORE removing it from the frame): per-vertex mask —
        /// 2 = joint zone (re-snap + light + material), 1 = light only (shadow ray, AO range or canopy of the removed
        /// branch/pad), 0 = unchanged; vertices of the removed limbs get 0 (no longer drawn). Worker/main safe.
        /// </summary>
        public void CutMask(GeoBuffer b, SketchFrame f, int group, Vector3 L, byte[] mask, out int n2, out int n1)
        {
            n2 = n1 = 0;
            System.Array.Clear(mask, 0, b.VCount);
            if (group < 0 || group >= f.Ng) return;
            float kg = f.Gm[group * 8 + 4];
            bool pad = f.Np == f.Nb + 1 && group < f.Nb;
            float px = 0, py = 0, pz = 0, pr = 0;
            if (pad)
            {
                int o = group * 8;
                px = f.Pe[o]; py = f.Pe[o + 1]; pz = f.Pe[o + 2];
                pr = Mathf.Max(f.Pe[o + 3], Mathf.Max(f.Pe[o + 6], f.Pe[o + 7]));
            }
            // Removed chunk spheres.
            _nsph = 0;
            for (int l = 0; l < LimbCount; l++)
                if (_limbGroup[l] == group)
                    for (int c = _limbC0[l]; c < _limbC1[l]; c++)
                        AddSphere(f.Cb[c * 4], f.Cb[c * 4 + 1], f.Cb[c * 4 + 2], ChunkRadius(f.Cb[c * 4 + 3]), kg, l);
            int nRemoved = _nsph;
            // Vertices of removed limbs (this cut and earlier ones) are marked 255 first, then skipped.
            for (int l = 0; l < LimbCount; l++)
                if (_limbGroup[l] == group || _limbGroup[l] == RemovedGroup)
                    for (int v = _limbV0[l]; v < _limbV0[l] + _limbVN[l]; v++) mask[v] = 255;
            for (int v = 0; v < b.VCount; v++)
            {
                if (mask[v] == 255) { mask[v] = 0; continue; }
                Vector3 p = b.Pos[v];
                bool joint = false;
                for (int s = 0; s < nRemoved && !joint; s++)
                {
                    Vector4 c = _sph[s];
                    float dx = p.x - c.x, dy = p.y - c.y, dz = p.z - c.z;
                    float m = c.w + kg + 1f;
                    joint = dx * dx + dy * dy + dz * dz < m * m;
                }
                if (joint) { mask[v] = 2; n2++; continue; }
                bool light = pad && RayNear(p, L, px, py, pz, pr, 4f);
                for (int s = 0; s < nRemoved && !light; s++)
                {
                    Vector4 c = _sph[s];
                    light = RayNear(p, L, c.x, c.y, c.z, c.w, 16f);
                }
                if (light)
                {
                    mask[v] = 1;
                    n1++;
                }
            }
        }

        // Within `near` of the sphere, or the shadow ray p + L t passes the sphere with a soft-shadow penumbra.
        private static bool RayNear(Vector3 p, Vector3 L, float cx, float cy, float cz, float r, float near)
        {
            float dx = cx - p.x, dy = cy - p.y, dz = cz - p.z;
            float d2 = dx * dx + dy * dy + dz * dz;
            if (d2 < (r + near) * (r + near)) return true;
            float t = dx * L.x + dy * L.y + dz * L.z;
            if (t <= 0f) return false;
            float q2 = d2 - t * t;
            float m = r + 4f + t * 0.125f;
            return q2 < m * m;
        }

        /// <summary>Group id of limbs/chunks already cut away.</summary>
        public const int RemovedGroup = -2;

        /// <summary>After SketchFrame.RemoveGroup(g): limbs of g become removed, later groups shift down (as in GM).</summary>
        public void OnGroupRemoved(int g)
        {
            for (int l = 0; l < LimbCount; l++)
            {
                if (_limbGroup[l] == g) _limbGroup[l] = RemovedGroup;
                else if (_limbGroup[l] > g) _limbGroup[l]--;
            }
            for (int c = 0; c < SketchConst.MAXCH; c++)
            {
                if (_chunkGroup[c] == g) _chunkGroup[c] = RemovedGroup;
                else if (_chunkGroup[c] > g) _chunkGroup[c]--;
            }
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
