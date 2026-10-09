using System;
using System.Diagnostics;
using PocketArbor.Spike.TreeGen;
using UnityEngine;

namespace PocketArbor.Spike.Render
{
    /// <summary>
    /// PA-102X-S C6: analytic pot mesh — the zero set of the sketch's potD built from rings, no isosurface.
    /// Every vertex lies exactly on potD = 0 (same formulas as potD / pshape / potB / ell / sdRR of sketch_v11):
    ///  * body wall: level 2.5 of pshape(b(y)), b(y) = (hw, hd) * potB scale; vertical below y = -dep (scale clamped),
    ///    tapered above it — a crease at y = -dep (the light band at the foot of the body in the sketch);
    ///  * lip: level 1.4 of pshape(hw + 3.5, hd + 3.5), y in [-1.5, 4.5]; rim top at y = 4.5;
    ///  * cavity wall: level 0 of pshape(b(y) - 8) from the rim down to below the soil (y = -1.5) + a floor there;
    ///  * 4 feet: level 1.2 of sdRR(17, 9, r 4) (rectangle) or circles r 10 (oval), y in [-dep - 8.2, -dep + 2.2].
    /// potD composes these with max/min of offset fields, so its edges are SHARP (2.5 / 1.4 / 1.2 are plan offsets that
    /// give the plan corner radii 14.5 / 13.4 / 5.2, not edge fillets). Edges are therefore hard (duplicated rings with
    /// the two face normals); <see cref="Bevel"/> &gt; 0 adds a narrow smooth chamfer instead (the sketch's 0.15 normal
    /// epsilon + AA soften its edges by ~0.3 units). Normals are the analytic potD gradient of each part.
    /// Hidden parts (lip underside, body bottom, foot bottoms) are omitted unless <see cref="BottomCaps"/>.
    /// Deterministic; no managed allocations after the first call with the largest pot (scratch arrays are reused).
    /// Worker-thread safe (no Unity object API).
    /// </summary>
    public sealed class ParametricPotBuilder
    {
        /// <summary>Target spacing of samples along straight sides (rectangle), sketch units.</summary>
        public float SideStep = 5f;
        /// <summary>Segments per quarter arc of the rounded rectangle (r 12..14.5: chord error ≤ 0.07 units).</summary>
        public int ArcSegments = 8;
        /// <summary>Target mean spacing of samples along the oval (uniform elliptic angle: denser at the ends).</summary>
        public float EllipseStep = 4f;
        /// <summary>Samples around each foot.</summary>
        public int FootSegments = 28;
        /// <summary>Floor of the cavity just below the soil (covers holes of the soil grid at the wall).</summary>
        public bool CavityFloor = true;
        /// <summary>Closes the parts the sketch camera never sees (body bottom, foot bottoms).</summary>
        public bool BottomCaps;
        /// <summary>Chamfer width on the rim / lip / foot edges (0 = hard edges, as potD).</summary>
        public float Bevel;
        /// <summary>
        /// Body-wall rings under the lip (descending y, first = -1.5): the per-vertex bake samples the lip's soft shadow
        /// (sketch shd, 2.2-unit min step → a dark / light / dark groove right under the lip) here.
        /// </summary>
        public float[] UnderLipRings = { -1.5f, -2.25f, -3f, -3.75f, -4.5f, -5.5f, -7f, -9f };
        /// <summary>Max ring spacing on the lower body wall (between the last under-lip ring and y = -dep).</summary>
        public float LowerWallStep = 14f;
        /// <summary>Extra ring across the rim top (baked light across its 12.9-unit width).</summary>
        public bool RimMidRing;
        /// <summary>Extra ring at y = 4 on the cavity wall (potB scale clamp crease, 0.5 units under the rim).</summary>
        public bool CavityCreaseRing;

        public int Vertices { get; private set; }
        public int Triangles { get; private set; }
        public int RingSamples { get; private set; }
        public double LastMs { get; private set; }

        private readonly Contour _main = new Contour(), _foot = new Contour();
        private GeoBuffer _b;
        private float _hw, _hd, _dep, _kind;
        private bool _ell;

        private const float SoilY = 0f, RimY = 4.5f, LipLowY = -1.5f, BelowSoilY = -1.5f;
        private const float Corner = 12f;

        // Part bases for Ring().
        private const int BaseBody = 0, BaseLip = 1, BaseCavity = 2, BaseFoot = 3;
        // Normal modes for Ring().
        private const int NrmOut = 0, NrmIn = 1, NrmUp = 2, NrmDown = 3;

        public int Build(SpeciesLook s, GeoBuffer buf, float kind = 2f) => Build(s.PotShape, s.PotHw, s.PotHd, s.PotDep, buf, kind);

        public int BuildFromPotDef(PotDef p, GeoBuffer buf, float kind = 2f) => Build(p.Shape, (float)p.Hw, (float)p.Hd, (float)p.Dep, buf, kind);

        /// <summary>Appends the pot to <paramref name="buf"/> (sketch space). Returns the number of triangles added.</summary>
        public int Build(int shape, float hw, float hd, float dep, GeoBuffer buf, float kind = 2f)
        {
            long t0 = Stopwatch.GetTimestamp();
            _b = buf;
            _hw = hw;
            _hd = hd;
            _dep = dep;
            _ell = shape == 1;
            _kind = kind;
            int v0 = buf.VCount, i0 = buf.ICount;

            if (_ell)
            {
                double a = hw, b = hd;
                double per = Math.PI * (3.0 * (a + b) - Math.Sqrt((3.0 * a + b) * (a + 3.0 * b)));
                int n = 4 * Math.Max(8, (int)Math.Ceiling(per / Math.Max(0.5f, EllipseStep) / 4.0));
                _main.SetupEllipse(hw, hd, n);
            }
            else
            {
                int nsx = Math.Max(1, Mathf.CeilToInt((hd - Corner) / SideStep));
                int nsz = Math.Max(1, Mathf.CeilToInt((hw - Corner) / SideStep));
                _main.SetupRect(Corner, nsx, nsz, Math.Max(2, ArcSegments));
            }
            RingSamples = _main.N;

            BuildBody();
            BuildFeet();

            Vertices = buf.VCount - v0;
            Triangles = (buf.ICount - i0) / 3;
            LastMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
            _b = null;
            return Triangles;
        }

        // ------------------------------------------------------------------ profile

        private void BuildBody()
        {
            Contour c = _main;
            int n = c.N;
            float bv = Mathf.Max(0f, Bevel);
            float yBot = -_dep - 2.5f;

            // Body foot band (vertical: potB scale clamped below y = -dep), hard crease at y = -dep.
            int r = Ring(c, yBot + bv, BaseBody, 2.5f, NrmOut, 0f, 0f, 0f);
            if (BottomCaps || bv > 0f)
            {
                int cap = Ring(c, yBot, BaseBody, 2.5f - bv, NrmDown, 0f, 0f, 0f);
                if (bv > 0f) Strip(cap, r, n);
                if (BottomCaps) Fan(Center(0f, yBot, 0f, Vector3.down), cap, n);
            }
            Strip(r, Ring(c, -_dep, BaseBody, 2.5f, NrmOut, 0f, 0f, 0f), n);

            // Tapered body wall up to the lip underside; rings dense under the lip (its shadow band) and sparse below.
            r = Ring(c, -_dep, BaseBody, 2.5f, NrmOut, 1f, 0f, 0f);
            int nu = UnderLipRings.Length;
            float yFill = nu > 0 ? Mathf.Max(UnderLipRings[nu - 1], -_dep + 1f) : LipLowY;
            int fill = Math.Max(1, Mathf.CeilToInt((_dep + yFill) / Mathf.Max(1f, LowerWallStep)));
            for (int k = 1; k < fill; k++) r = StripTo(r, Ring(c, -_dep + (_dep + yFill) * k / fill, BaseBody, 2.5f, NrmOut, 1f, 0f, 0f), n);
            for (int k = nu - 1; k >= 0; k--)
            {
                float y = UnderLipRings[k];
                if (y < yFill - 1e-3f || y > LipLowY) continue;
                r = StripTo(r, Ring(c, y, BaseBody, 2.5f, NrmOut, 1f, 0f, 0f), n);
            }
            if (nu == 0 || UnderLipRings[0] != LipLowY) StripTo(r, Ring(c, LipLowY, BaseBody, 2.5f, NrmOut, 1f, 0f, 0f), n);

            // Lip outer wall (vertical), its underside is hidden from the sketch camera.
            r = Ring(c, LipLowY + bv, BaseLip, 1.4f, NrmOut, 0f, 0f, 0f);
            if (bv > 0f) Strip(Ring(c, LipLowY, BaseLip, 1.4f - bv, NrmDown, 0f, 0f, 0f), r, n);
            r = StripTo(r, Ring(c, RimY - bv, BaseLip, 1.4f, NrmOut, 0f, 0f, 0f), n);

            // Rim top (y = 4.5) from the lip edge to the cavity edge; middle ring for the baked light.
            int top0 = Ring(c, RimY, BaseLip, 1.4f - bv, NrmUp, 0f, 0f, 0f);
            if (bv > 0f) Strip(r, top0, n);
            int top2 = Ring(c, RimY, BaseCavity, bv, NrmUp, 0f, 0f, 0f);
            if (RimMidRing)
            {
                int top1 = LerpRing(top0, top2, n, 0.5f, Vector3.up);
                Strip(top0, top1, n);
                Strip(top1, top2, n);
            }
            else Strip(top0, top2, n);

            // Cavity wall down to below the soil (potB scale clamped above y = 4: smooth crease ring at y = 4).
            r = Ring(c, RimY - bv, BaseCavity, 0f, NrmIn, 0f, 0f, 0f);
            if (bv > 0f) Strip(top2, r, n);
            if (CavityCreaseRing) r = StripTo(r, Ring(c, 4f, BaseCavity, 0f, NrmIn, 0.5f, 0f, 0f), n);
            r = StripTo(r, Ring(c, 2f, BaseCavity, 0f, NrmIn, 1f, 0f, 0f), n);
            r = StripTo(r, Ring(c, SoilY, BaseCavity, 0f, NrmIn, 1f, 0f, 0f), n);
            StripTo(r, Ring(c, BelowSoilY, BaseCavity, 0f, NrmIn, 1f, 0f, 0f), n);
            if (CavityFloor)
                Fan(Center(0f, BelowSoilY, 0f, Vector3.up), Ring(c, BelowSoilY, BaseCavity, 0f, NrmUp, 0f, 0f, 0f), n);
        }

        private void BuildFeet()
        {
            Contour c = _foot;
            int n = Math.Max(8, FootSegments / 4 * 4);
            if (_ell) c.SetupEllipse(10f, 10f, n);
            else c.SetupRect(4f, 1, 2, Math.Max(1, (n - 12) / 4));
            n = c.N;
            float bv = Mathf.Max(0f, Bevel);
            float yB = -_dep - 8.2f, yM = -_dep - 2.5f, yT = -_dep + 2.2f;
            for (int i = 0; i < 4; i++)
            {
                float cx, cz;
                if (_ell)
                {
                    float a = i < 2 ? .75f : 2.39f;
                    cx = Mathf.Sin(a) * (i % 2 == 0 ? 1f : -1f) * _hw * .8f;
                    cz = Mathf.Cos(a) * _hd * .8f;
                }
                else
                {
                    cx = (i % 2 == 0 ? 1f : -1f) * (_hw - 30f);
                    cz = (i < 2 ? 1f : -1f) * (_hd - 15f);
                }
                int r = Ring(c, yB + bv, BaseFoot, 1.2f, NrmOut, 0f, cx, cz);
                if (BottomCaps || bv > 0f)
                {
                    int cap = Ring(c, yB, BaseFoot, 1.2f - bv, NrmDown, 0f, cx, cz);
                    if (bv > 0f) Strip(cap, r, n);
                    if (BottomCaps) Fan(Center(cx, yB, cz, Vector3.down), cap, n);
                }
                r = StripTo(r, Ring(c, yM, BaseFoot, 1.2f, NrmOut, 0f, cx, cz), n);
                r = StripTo(r, Ring(c, yT - bv, BaseFoot, 1.2f, NrmOut, 0f, cx, cz), n);
                // Top cap: mostly inside the body; visible where a foot sticks out of the body outline (as in potD).
                int top = Ring(c, yT, BaseFoot, 1.2f - bv, NrmUp, 0f, cx, cz);
                if (bv > 0f) Strip(r, top, n);
                Fan(Center(cx, yT, cz, Vector3.up), top, n);
            }
        }

        // ------------------------------------------------------------------ rings

        private static float BScale(float y, float dep) => PotSdf.BScale(y, dep);

        /// <summary>
        /// Adds one ring: the level-<paramref name="d"/> contour of the part's plan shape at height y.
        /// <paramref name="taper"/> scales d(potB)/dy in the wall normal (0 vertical section, 1 tapered, 0.5 crease).
        /// </summary>
        private int Ring(Contour c, float y, int baseKind, float d, int nmode, float taper, float ox, float oz)
        {
            float bx, bz, dbx = 0f, dbz = 0f;
            switch (baseKind)
            {
                case BaseBody:
                case BaseCavity:
                {
                    float s = BScale(y, _dep);
                    bx = _hw * s;
                    bz = _hd * s;
                    if (baseKind == BaseCavity)
                    {
                        bx -= 8f;
                        bz -= 8f;
                    }
                    float sp = taper * 0.1f / (_dep + 4f);
                    dbx = _hw * sp;
                    dbz = _hd * sp;
                    break;
                }
                case BaseLip:
                    bx = _hw + 3.5f;
                    bz = _hd + 3.5f;
                    break;
                default: // foot
                    bx = _ell ? 10f : 17f;
                    bz = _ell ? 10f : 9f;
                    break;
            }
            GeoBuffer b = _b;
            int v0 = b.VCount;
            for (int i = 0; i < c.N; i++)
            {
                c.Eval(i, bx, bz, d, out float x, out float z, out float gx, out float gz, out float gbx, out float gbz);
                Vector3 nrm;
                if (nmode == NrmUp) nrm = Vector3.up;
                else if (nmode == NrmDown) nrm = Vector3.down;
                else
                {
                    // potD gradient of the wall: (dF/dx, dF/dbx * dbx/dy + dF/dbz * dbz/dy, dF/dz); the cavity is carved,
                    // so its pot normal is -grad(cav).
                    var g = new Vector3(gx, gbx * dbx + gbz * dbz, gz);
                    float m = g.magnitude;
                    nrm = m > 1e-12f ? g / m : new Vector3(gx, 0f, gz);
                    if (nmode == NrmIn) nrm = -nrm;
                }
                b.AddVertex(new Vector3(x + ox, y, z + oz), nrm, 0f, _kind);
            }
            return v0;
        }

        private int LerpRing(int ra, int rb, int n, float t, Vector3 nrm)
        {
            GeoBuffer b = _b;
            int v0 = b.VCount;
            for (int i = 0; i < n; i++) b.AddVertex(Vector3.LerpUnclamped(b.Pos[ra + i], b.Pos[rb + i], t), nrm, 0f, _kind);
            return v0;
        }

        private int Center(float x, float y, float z, Vector3 nrm) => _b.AddVertex(new Vector3(x, y, z), nrm, 0f, _kind);

        private int StripTo(int ra, int rb, int n)
        {
            Strip(ra, rb, n);
            return rb;
        }

        /// <summary>Quad strip between two rings; winding chosen so that cross(b - a, c - a) follows the vertex normals.</summary>
        private void Strip(int ra, int rb, int n)
        {
            GeoBuffer b = _b;
            double s = 0;
            for (int i = 0; i < n; i++)
            {
                int j = i + 1 == n ? 0 : i + 1;
                Vector3 a0 = b.Pos[ra + i], a1 = b.Pos[ra + j], b1 = b.Pos[rb + j], b0 = b.Pos[rb + i];
                Vector3 nn = b.Nrm[ra + i] + b.Nrm[ra + j] + b.Nrm[rb + j] + b.Nrm[rb + i];
                s += Vector3.Dot(Vector3.Cross(a1 - a0, b1 - a0) + Vector3.Cross(b1 - a0, b0 - a0), nn);
            }
            bool flip = s < 0;
            for (int i = 0; i < n; i++)
            {
                int j = i + 1 == n ? 0 : i + 1;
                int a0 = ra + i, a1 = ra + j, b1 = rb + j, b0 = rb + i;
                if (!flip)
                {
                    b.AddTri(a0, a1, b1);
                    b.AddTri(a0, b1, b0);
                }
                else
                {
                    b.AddTri(a0, b1, a1);
                    b.AddTri(a0, b0, b1);
                }
            }
        }

        private void Fan(int center, int ring, int n)
        {
            GeoBuffer b = _b;
            Vector3 c = b.Pos[center];
            double s = 0;
            for (int i = 0; i < n; i++)
            {
                int j = i + 1 == n ? 0 : i + 1;
                s += Vector3.Dot(Vector3.Cross(b.Pos[ring + i] - c, b.Pos[ring + j] - c), b.Nrm[center]);
            }
            bool flip = s < 0;
            for (int i = 0; i < n; i++)
            {
                int j = i + 1 == n ? 0 : i + 1;
                if (!flip) b.AddTri(center, ring + i, ring + j);
                else b.AddTri(center, ring + j, ring + i);
            }
        }

        // ------------------------------------------------------------------ plan contours

        /// <summary>
        /// Sample layout of a closed plan contour, shared by all rings of one part (so ring i of two rings correspond).
        /// Rounded rectangle (sketch sdRR, exact distance): level d around half extents b, corner radius r0 -> straight
        /// parts b - r0, arcs r0 + d. Oval (sketch ell, approximate distance): rays u_i from the centre, the level-d point
        /// solves ell(rho u) = d in closed form (ell is linear in rho along a ray): rho = (1 + d B / A) / A,
        /// A = |u / ab|, B = |u / ab^2|.
        /// </summary>
        private sealed class Contour
        {
            public int N;
            public bool Ell;
            public float R0;
            // Rect: T 0 = x side (A = sign x, B = t), 1 = z side (A = sign z, B = t), 2 = arc (A = sx, B = sz, C = phi).
            // Oval: A, B = unit ray direction.
            public byte[] T = new byte[0];
            public float[] A = new float[0], B = new float[0], C = new float[0];

            private void Ensure(int n)
            {
                if (T.Length < n)
                {
                    T = new byte[n];
                    A = new float[n];
                    B = new float[n];
                    C = new float[n];
                }
                N = n;
            }

            public void SetupEllipse(float a0, float b0, int n)
            {
                Ensure(n);
                Ell = true;
                for (int i = 0; i < n; i++)
                {
                    double t = 2.0 * Math.PI * i / n;
                    double x = a0 * Math.Cos(t), z = b0 * Math.Sin(t), l = Math.Sqrt(x * x + z * z);
                    T[i] = 3;
                    A[i] = (float)(x / l);
                    B[i] = (float)(z / l);
                    C[i] = 0f;
                }
            }

            /// <param name="nsx">segments per half of the x = ±X sides</param>
            /// <param name="nsz">segments per half of the z = ±Z sides</param>
            /// <param name="na">segments per quarter arc</param>
            public void SetupRect(float r0, int nsx, int nsz, int na)
            {
                Ensure(4 * (nsx + nsz + na));
                Ell = false;
                R0 = r0;
                int k = 0;
                for (int q = 0; q < 4; q++)
                {
                    // side, then the arc that follows it (counter-clockwise in x/z)
                    bool xSide = q % 2 == 0;
                    int ns = 2 * (xSide ? nsx : nsz);
                    float sign = q < 2 ? 1f : -1f;
                    for (int j = 0; j < ns; j++)
                    {
                        float t = -1f + 2f * j / ns;
                        T[k] = (byte)(xSide ? 0 : 1);
                        A[k] = sign;
                        // x+ side: z from -h to +h; z+ side: x from +h to -h; x- side: z from +h to -h; z- side: x from -h to +h
                        B[k] = q == 0 || q == 3 ? t : -t;
                        C[k] = 0f;
                        k++;
                    }
                    float phi0 = q * 0.5f * Mathf.PI;
                    float sx = q == 0 || q == 3 ? 1f : -1f, sz = q < 2 ? 1f : -1f;
                    for (int j = 0; j < na; j++)
                    {
                        T[k] = 2;
                        A[k] = sx;
                        B[k] = sz;
                        C[k] = phi0 + 0.5f * Mathf.PI * j / na;
                        k++;
                    }
                }
            }

            /// <summary>Point i of the level-d contour around half extents (bx, bz); plan gradient (gx, gz) of the level
            /// function and its derivatives with respect to bx, bz (for the taper term of the wall normal).</summary>
            public void Eval(int i, float bx, float bz, float d, out float x, out float z, out float gx, out float gz, out float gbx, out float gbz)
            {
                if (Ell)
                {
                    double ux = A[i], uz = B[i];
                    double ia = ux / bx, ib = uz / bz, ja = ux / ((double)bx * bx), jb = uz / ((double)bz * bz);
                    double aa = Math.Sqrt(ia * ia + ib * ib), bb = Math.Sqrt(ja * ja + jb * jb);
                    double rho = (1.0 + d * bb / aa) / aa;
                    double px = rho * ux, pz = rho * uz;
                    EllGrad(px, pz, bx, bz, out double egx, out double egz, out double ega, out double egb);
                    x = (float)px;
                    z = (float)pz;
                    gx = (float)egx;
                    gz = (float)egz;
                    gbx = (float)ega;
                    gbz = (float)egb;
                    return;
                }
                float hx = bx - R0, hz = bz - R0;
                switch (T[i])
                {
                    case 0:
                        x = A[i] * (bx + d);
                        z = B[i] * hz;
                        gx = A[i];
                        gz = 0f;
                        break;
                    case 1:
                        z = A[i] * (bz + d);
                        x = B[i] * hx;
                        gx = 0f;
                        gz = A[i];
                        break;
                    default:
                    {
                        float r = R0 + d, cs = Mathf.Cos(C[i]), sn = Mathf.Sin(C[i]);
                        x = A[i] * hx + r * cs;
                        z = B[i] * hz + r * sn;
                        gx = cs;
                        gz = sn;
                        break;
                    }
                }
                // sdRR depends on |q| - b: d/db = -|grad|.
                gbx = -Math.Abs(gx);
                gbz = -Math.Abs(gz);
            }

            /// <summary>Analytic gradient of the sketch's ell(p, ab) = k0 (k0 - 1) / k1 in x, z and in the axes a, b.</summary>
            private static void EllGrad(double x, double z, double a, double b, out double gx, out double gz, out double ga, out double gb)
            {
                double a2 = a * a, b2 = b * b, a4 = a2 * a2, b4 = b2 * b2;
                double k0 = Math.Max(Math.Sqrt(x * x / a2 + z * z / b2), 1e-9);
                double k1 = Math.Max(Math.Sqrt(x * x / a4 + z * z / b4), 1e-9);
                double f0 = (2.0 * k0 - 1.0) / k1, f1 = -k0 * (k0 - 1.0) / (k1 * k1);
                gx = f0 * x / (a2 * k0) + f1 * x / (a4 * k1);
                gz = f0 * z / (b2 * k0) + f1 * z / (b4 * k1);
                ga = f0 * (-x * x / (a2 * a * k0)) + f1 * (-2.0 * x * x / (a4 * a * k1));
                gb = f0 * (-z * z / (b2 * b * k0)) + f1 * (-2.0 * z * z / (b4 * b * k1));
            }
        }
    }
}
