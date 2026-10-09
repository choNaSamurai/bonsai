// PA-102X-S spike: port of JS scene(t), genLeaves(), genExtras(), padHit() and helpers (sketch_v11 lines 86-189).
// Everything the sketch writes to a Float32Array is stored as float here at the same index and read back as float;
// intermediate maths is double, in the same evaluation order as JS.
using System;
using System.Collections.Generic;

namespace PocketArbor.Spike.TreeGen
{
    public static partial class SketchGenerator
    {
        private const int NT = SketchLayout.NT, NTC = SketchLayout.NTC, RP = SketchLayout.RP, CP = SketchLayout.CP;
        private const int RC = SketchLayout.RC, NSG = SketchLayout.NSG, OB = SketchLayout.OB, ORB = SketchLayout.ORB;
        private const int OSG = SketchLayout.OSG, ORW = SketchLayout.ORW, OR = SketchLayout.OR, OBR = SketchLayout.OBR;
        private const int OPF = SketchLayout.OPF, MAXLEAF = SketchLayout.MAXLEAF, MAXX = SketchLayout.MAXX;

        private static readonly D3 UP = new D3(0, 1, 0);

        /// <summary>Leaf/branch site (JS site() object).</summary>
        private struct Site
        {
            public D3 P, D;
            public double Sc;
            public int Id0, Id1, Id2, Id3;
            public bool Tip;
            public int Pad;
        }

        /// <summary>Mutable state of one scene() call (JS globals nc, np, SITES, PADS, CPS).</summary>
        private sealed class Ctx
        {
            public SketchPlan M;
            public SpeciesDef Sp;
            public SketchScene S;
            public float[] Dat, Cb, Gm, Pe, Leaf, XLeaf;
            public readonly float[] Cps = new float[CP * 4];
            public int Nc, Np;
            public int Seed;
            public D3 LL;
            public readonly List<Site> Sites = new List<Site>(1024);
            public readonly List<Site> Bs = new List<Site>(64);
            public readonly List<Site> As = new List<Site>(32);
            public Pad[] Pads;
        }

        /// <summary>
        /// JS scene(t) for the plan from <see cref="Build"/>, species key (momiji / yamazakura / kuromatsu) and light LL.
        /// Reuses <paramref name="reuse"/> buffers when given (stale data beyond the counters is not cleared, as in JS).
        /// </summary>
        public static SketchScene Scene(SketchPlan plan, string speciesKey, double t, D3 lightDir, SketchScene reuse = null)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (double.IsNaN(t) || double.IsInfinity(t)) throw new ArgumentOutOfRangeException(nameof(t));
            // JS never sees t outside [0,1] (slider); clamp so Lt(t) = pow(1-t, 1.7) cannot produce NaN.
            t = SketchMath.Cl(t, 0, 1);
            SpeciesDef sp = SketchSpecies.ForStyle(speciesKey, plan.Style);
            SketchScene s = reuse ?? new SketchScene();
            s.SpeciesKey = speciesKey;
            s.Style = plan.Style;
            s.Seed = plan.Seed;
            s.T = t;
            s.Species = sp;
            s.Pot = sp.Pot;
            s.LightDir = lightDir;

            var c = new Ctx
            {
                M = plan, Sp = sp, S = s, Dat = s.Dat, Cb = s.Cb, Gm = s.Gm, Pe = s.Pe, Leaf = s.Leaf, XLeaf = s.XLeaf,
                Pads = s.Pads, Seed = plan.Seed, LL = lightDir
            };
            RunScene(c, t);
            return s;
        }

        private static void Set4(float[] a, int i, double x, double y, double z, double w)
        {
            a[i] = (float)x;
            a[i + 1] = (float)y;
            a[i + 2] = (float)z;
            a[i + 3] = (float)w;
        }

        private static void Set4(float[] a, int i, D3 p, double w)
        {
            Set4(a, i, p.X, p.Y, p.Z, w);
        }

        private static D3 Ax(SketchPlan m, double s)
        {
            double x, y;
            m.At(s, out x, out y);
            return new D3(x + m.Cx, y, m.Zf(s));
        }

        /// <summary>JS bound(i0, cnt, oi): bounding sphere of DAT entries, read back as float.</summary>
        private static void Bound(float[] dat, int i0, int cnt, int oi)
        {
            double cx = 0, cy = 0, cz = 0;
            for (int i = 0; i < cnt; i++)
            {
                cx += dat[(i0 + i) * 4];
                cy += dat[(i0 + i) * 4 + 1];
                cz += dat[(i0 + i) * 4 + 2];
            }

            cx /= cnt;
            cy /= cnt;
            cz /= cnt;
            double rad = 0;
            for (int i = 0; i < cnt; i++)
            {
                int j = (i0 + i) * 4;
                rad = Math.Max(rad, SketchMath.Hypot(dat[j] - cx, dat[j + 1] - cy, dat[j + 2] - cz) + dat[j + 3]);
            }

            Set4(dat, oi * 4, cx, cy, cz, rad);
        }

        /// <summary>JS bspl(C, nOut): quadratic B-spline with clamped ends.</summary>
        private static D4[] Bspl(D4[] C, int nOut)
        {
            int cl = C.Length;
            var E = new D4[cl + 2];
            E[0] = C[0];
            for (int i = 0; i < cl; i++) E[i + 1] = C[i];
            E[cl + 1] = C[cl - 1];
            int np = E.Length - 2;
            var output = new D4[nOut];
            for (int k = 0; k < nOut; k++)
            {
                double v = (double)(k * np) / (nOut - 1);
                int i = Math.Min((int)Math.Floor(v), np - 1) + 1;
                double f = v - (i - 1);
                double w0 = 0.5 * (1 - f) * (1 - f), w1 = 0.5 + f - f * f, w2 = 0.5 * f * f;
                D4 a = E[i - 1], b = E[i], cc = E[i + 1];
                output[k] = new D4(w0 * a.X + w1 * b.X + w2 * cc.X, w0 * a.Y + w1 * b.Y + w2 * cc.Y,
                    w0 * a.Z + w1 * b.Z + w2 * cc.Z, w0 * a.W + w1 * b.W + w2 * cc.W);
            }

            return output;
        }

        /// <summary>JS cbR(c): chunk radius without the child-count thousands.</summary>
        private static double CbR(float[] cb, int c)
        {
            double aw = Math.Abs((double)cb[c * 4 + 3]);
            return aw - 1000 * Math.Floor(aw / 1000);
        }

        /// <summary>JS putChunk(p5, limb): 5 consecutive points of <paramref name="p"/> from <paramref name="start"/>.</summary>
        private static int PutChunk(Ctx c, D4[] p, int start, bool limb)
        {
            int b = OBR + c.Nc * 5;
            double cx = 0, cy = 0, cz = 0;
            for (int i = 0; i < 5; i++)
            {
                D4 q = p[start + i];
                Set4(c.Dat, (b + i) * 4, q.X, q.Y, q.Z, q.W);
                cx += q.X;
                cy += q.Y;
                cz += q.Z;
            }

            cx /= 5;
            cy /= 5;
            cz /= 5;
            double rad = 0;
            for (int i = 0; i < 5; i++)
            {
                D4 q = p[start + i];
                rad = Math.Max(rad, SketchMath.Hypot(q.X - cx, q.Y - cy, q.Z - cz) + q.W);
            }

            Set4(c.Cb, c.Nc * 4, cx, cy, cz, limb ? -rad : rad);
            return c.Nc++;
        }

        /// <summary>JS subBound(c, n): grow chunk c's sphere over its n child chunks; encode n in thousands.</summary>
        private static void SubBound(Ctx c, int ci, int n)
        {
            if (n == 0) return;
            float[] cb = c.Cb;
            double cx = cb[ci * 4], cy = cb[ci * 4 + 1], cz = cb[ci * 4 + 2], rad = CbR(cb, ci);
            for (int k = 1; k <= n; k++)
            {
                int j = (ci + k) * 4;
                double d = SketchMath.Hypot(cb[j] - cx, cb[j + 1] - cy, cb[j + 2] - cz) + CbR(cb, ci + k);
                rad = Math.Max(rad, d);
            }

            double w = cb[ci * 4 + 3];
            double sign = w == 0 ? 1 : (w < 0 ? -1 : 1); // Math.sign(CB[..]||1)
            cb[ci * 4 + 3] = (float)(sign * (rad + 1000 * n));
        }

        /// <summary>JS putGroup(gi, c0, kg, ks).</summary>
        private static void PutGroup(Ctx c, int gi, int c0, double kg, double ks)
        {
            float[] cb = c.Cb;
            double cx = 0, cy = 0, cz = 0;
            int cn = c.Nc - c0;
            for (int k = c0; k < c.Nc; k++)
            {
                cx += cb[k * 4];
                cy += cb[k * 4 + 1];
                cz += cb[k * 4 + 2];
            }

            cx /= cn;
            cy /= cn;
            cz /= cn;
            double rad = 0;
            for (int k = c0; k < c.Nc; k++)
                rad = Math.Max(rad, SketchMath.Hypot(cb[k * 4] - cx, cb[k * 4 + 1] - cy, cb[k * 4 + 2] - cz) + CbR(cb, k));
            int o = gi * 8;
            Set4(c.Gm, o, cx, cy, cz, rad);
            Set4(c.Gm, o + 4, kg, c0, cn, ks);
        }

        /// <summary>JS putPad(c, h, a, b, yb, jit, dome, key, gp).</summary>
        private static void PutPad(Ctx c, D3 ctr, D3 h, double a, double b, double yb, double[] jit, bool dome, int key, double gp)
        {
            double hl = SketchMath.Hypot(h.X, h.Z);
            if (hl == 0 || double.IsNaN(hl)) hl = 1;
            var hx = new D3(h.X / hl, 0, h.Z / hl);
            var pp = new D3(hx.Z, 0, -hx.X);
            int o = OPF + c.Np * 7;
            Set4(c.Dat, o * 4, SketchMath.Add(ctr, UP, b * (dome ? 0.24 : 0.16)), b * (dome ? 0.56 : 0.5));
            for (int j = 1; j < 7; j++)
            {
                double an = (j - 1) / 6.0 * 6.283 + jit[j] * 0.6;
                D3 q = SketchMath.Add(SketchMath.Add(SketchMath.Add(ctr, hx, Math.Cos(an) * 0.6 * a), pp, Math.Sin(an) * 0.6 * b),
                    UP, b * ((dome ? 0.1 : 0.03) + 0.1 * jit[j]));
                Set4(c.Dat, (o + j) * 4, q, b * (dome ? 0.46 : 0.4) * (0.85 + 0.3 * jit[j]));
            }

            c.Pads[c.Np] = new Pad { C = ctr, A = a, B = b, Yb = yb, Hx = hx, Pp = pp, Dome = dome, Key = key, Gp = gp };
            int pe = c.Np * 8;
            Set4(c.Pe, pe, ctr.X, ctr.Y + 0.2 * b, ctr.Z, 0.95 * a);
            Set4(c.Pe, pe + 4, hx.X, hx.Z, 0.62 * b, 0.95 * b);
            // PB / PM of the sketch are never uploaded to the GPU — not ported.
            c.Np++;
        }

        /// <summary>JS site(P, u, sc, id).</summary>
        private static Site MakeSite(D4[] P, double u, double sc, int id0, int id1, int id2, int id3)
        {
            int n = P.Length - 1;
            double f = u * n;
            int i = Math.Min(n - 1, (int)Math.Floor(f));
            double fr = f - i;
            D4 a = P[i], b = P[i + 1];
            return new Site
            {
                P = new D3(a.X + (b.X - a.X) * fr, a.Y + (b.Y - a.Y) * fr, a.Z + (b.Z - a.Z) * fr),
                D = new D3(b.X - a.X, b.Y - a.Y, b.Z - a.Z),
                Sc = sc,
                Id0 = id0,
                Id1 = id1,
                Id2 = id2,
                Id3 = id3,
                Tip = u > 0.95,
                Pad = -1
            };
        }

        private static D4 Lerp4(D4 a, D4 b, double f)
        {
            return new D4(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f, a.Z + (b.Z - a.Z) * f, a.W + (b.W - a.W) * f);
        }

        private static D3 Dir3(D4 a, D4 b)
        {
            return new D3(b.X - a.X, b.Y - a.Y, b.Z - a.Z);
        }

        private static D3 NormOr1(D3 v)
        {
            double l = SketchMath.Hypot(v.X, v.Y, v.Z);
            if (l == 0 || double.IsNaN(l)) l = 1;
            return new D3(v.X / l, v.Y / l, v.Z / l);
        }

        /// <summary>The common 4-point twig spline of secondary / tertiary / apex-fork branches.</summary>
        private static D4[] Twig(D4 a0, D3 dir, double len, double r, double k1, double r1, double k2, double up2,
            double r2, double up3, double tipMin, double tipK)
        {
            var S = new[]
            {
                new D4(a0.X, a0.Y, a0.Z, r),
                new D4(SketchMath.Add(a0, dir, len * k1), r * r1),
                new D4(SketchMath.Add(SketchMath.Add(a0, dir, len * k2), UP, len * up2), r * r2),
                new D4(SketchMath.Add(SketchMath.Add(a0, dir, len), UP, len * up3), Math.Max(tipMin, r * tipK))
            };
            return Bspl(S, 5);
        }

        private static void RunScene(Ctx c, double t)
        {
            SketchPlan M = c.M;
            SpeciesDef SPc = c.Sp;
            float[] DAT = c.Dat;
            double L = SketchMath.Lt(t), R = M.Rt(t), rt = 0.5 + 0.06 * R, s0 = -0.6 * R;

            double TR(double s)
            {
                double u = SketchMath.Cl(s / L, 0, 1);
                return (rt + (R - rt) * Math.Pow(1 - u, 1.2) + R * 0.3 * Math.Exp(-Math.Max(0, s) / (1.3 * R)))
                       * (1 + 0.06 * t * M.Nl[s / 30]);
            }

            // trunk
            for (int k = 0; k <= NT; k++)
            {
                double s = s0 + (L - s0) * k / NT;
                Set4(DAT, k * 4, Ax(M, s), TR(s));
            }

            for (int ch = 0; ch < NTC; ch++) Bound(DAT, ch * 4, 5, OB + ch);
            for (int g = 0; g < NSG; g++) Bound(DAT, g * 16, 17, OSG + g);

            // roots
            int nr = 0;
            float[] CPS = c.Cps;
            PotDef PT = SPc.Pot;
            for (int ri = 0; ri < M.Roots.Length; ri++)
            {
                SketchRoot o = M.Roots[ri];
                double g = SketchMath.Sm((t - o.E) / 0.4);
                if (g <= 0.002) continue;
                double sa = Math.Sin(o.Az), ca = Math.Cos(o.Az), st = 0.5 * R, hwI = PT.Hw - 8, hdI = PT.Hd - 8;
                double lim = (PT.Shape != 0
                                 ? 1 / SketchMath.Hypot(sa / hwI, ca / hdI)
                                 : Math.Min(hwI / Math.Max(Math.Abs(sa), 1e-3), hdI / Math.Max(Math.Abs(ca), 1e-3)))
                             - 12 - st;
                double len = Math.Max(4, Math.Min(o.Len * R * g, lim)), r0 = o.Th * R * (0.35 + 0.65 * g);
                double ch = o.Ch * R * g;
                double c0x, c0y, c1x, c1y;
                M.At(ch + 0.5 * R, out c0x, out c0y);
                M.At(ch, out c1x, out c1y);
                Set4(CPS, 0, M.Cx + c0x + sa * 0.1 * R, c0y, M.Zf(ch + 0.5 * R) + ca * 0.1 * R, 0.3 * r0 + 0.4);
                Set4(CPS, 4, M.Cx + c1x + sa * 0.4 * R, c1y, M.Zf(ch) + ca * 0.4 * R, 0.55 * r0 + 0.5);
                for (int k = 2; k < CP; k++)
                {
                    double u = (double)(k - 2) / (CP - 3), dd = st + u * len;
                    double w = (o.Wob * Math.Sin(u * Math.PI) + 0.25 * o.Nz[u * 2.2]) * len * 0.3;
                    double rr = r0 * Math.Pow(1 - u, 0.9) + 0.8;
                    double y = 0.52 * rr + 0.8 * r0 * Math.Pow(1 - u, 3) - 1.4 * Math.Pow(u, 4) * rr;
                    Set4(CPS, k * 4, M.Cx + sa * dd + ca * w, y, ca * dd - sa * w, rr);
                }

                for (int k = 0; k < RP; k++)
                {
                    double v = (double)(k * (CP - 2)) / (RP - 1);
                    int i = Math.Min((int)Math.Floor(v), CP - 3) + 1;
                    double f = v - (i - 1);
                    double w0 = 0.5 * (1 - f) * (1 - f), w1 = 0.5 + f - f * f, w2 = 0.5 * f * f;
                    int o0 = (i - 1) * 4, o1 = i * 4, o2 = (i + 1) * 4, dst = (OR + nr * RP + k) * 4;
                    for (int q = 0; q < 4; q++) DAT[dst + q] = (float)(w0 * CPS[o0 + q] + w1 * CPS[o1 + q] + w2 * CPS[o2 + q]);
                }

                for (int cc = 0; cc < RC; cc++) Bound(DAT, OR + nr * RP + cc * 4, 5, ORB + nr * RC + cc);
                Bound(DAT, OR + nr * RP, RP, ORW + nr);
                nr++;
            }

            // branches
            c.Nc = 0;
            c.Np = 0;
            c.Sites.Clear();
            int ng = 0, nb = 0;
            double[] usPrimary = SPc.Tips ? new[] { 0.7, 0.85, 1 } : new[] { 0.55, 0.7, 0.85, 1 };
            double[] usSecondary = SPc.Tips ? new[] { 0.5, 0.7, 0.85, 1 } : new[] { 0.45, 0.65, 0.85, 1 };
            double[] usTertiary = SPc.Tips ? new[] { 0.5, 0.75, 1 } : new[] { 0.35, 0.55, 0.75, 0.9, 1 };
            double[] us4 = { 0.3, 0.5, 0.68, 0.84 }, us3 = { 0.42, 0.64, 0.84 };
            for (int bi = 0; bi < M.Branches.Length; bi++)
            {
                SketchBranch b = M.Branches[bi];
                double g = SketchMath.Sm((t - b.Ts) / 0.3);
                if (g <= 0.01 || b.S > L - 6) continue;
                D3 P0 = Ax(M, b.S);
                double rb0 = Math.Max(0.6, b.Thk * TR(b.S) * (0.35 + 0.65 * g)), Lb = b.Len * g;
                var h = new D3(Math.Sin(b.Az), 0, Math.Cos(b.Az));
                var pp = new D3(Math.Cos(b.Az), 0, -Math.Sin(b.Az));
                var C = new D4[6];
                for (int k = 0; k <= 5; k++)
                {
                    double u = k / 5.0;
                    double y = Lb * (Math.Tan(b.Elev) * u - b.Droop * u * u + b.Rise * Math.Pow(u, 4));
                    double w = Lb * b.Wamp * Math.Sin(u * 5.7 + b.Wph) * u;
                    D3 p = SketchMath.Add(SketchMath.Add(SketchMath.Add(P0, h, u * Lb), UP, y), pp, w);
                    C[k] = new D4(p, Math.Max(0.5, rb0 * Math.Pow(1 - 0.7 * u, 1.1)));
                }

                D4[] pts = Bspl(C, 9);
                int c0 = c.Nc;
                PutChunk(c, pts, 0, true);
                PutChunk(c, pts, 4, false);
                List<Site> bs = c.Bs;
                bs.Clear();
                double gl1 = SketchMath.Sm((g - 0.05) / 0.5);
                for (int ui = 0; ui < usPrimary.Length; ui++) bs.Add(MakeSite(pts, usPrimary[ui], gl1, bi, 9, 9, ui));
                double g2 = SketchMath.Sm((g - 0.35) / 0.5);
                double rsAvg = 0.6;
                if (g2 > 0.02)
                {
                    for (int j = 0; j < b.Nsec; j++)
                    {
                        double us = (b.Nsec > 3 ? us4 : us3)[j], fi = us * 8;
                        int i0 = Math.Min(7, (int)Math.Floor(fi));
                        double fr = fi - i0;
                        D4 A = Lerp4(pts[i0], pts[i0 + 1], fr);
                        D3 tg = NormOr1(Dir3(pts[i0], pts[i0 + 1]));
                        double sg = (j % 2 != 0 ? -1 : 1) * b.Ss, ca = Math.Cos(0.75 * sg), sa = Math.Sin(0.75 * sg);
                        var dv = new D3(tg.X * ca + tg.Z * sa, tg.Y * 0.3 + (j % 2 != 0 ? 0.16 : 0.08), -tg.X * sa + tg.Z * ca);
                        double dl = SketchMath.Hypot(dv.X, dv.Y, dv.Z);
                        var dn = new D3(dv.X / dl, dv.Y / dl, dv.Z / dl);
                        double Ls = 0.6 * Lb * (1 - 0.45 * us) * g2, rs = Math.Max(0.45, 0.48 * A.W);
                        rsAvg = rs;
                        D4[] sp = Twig(A, dn, Ls, rs, 0.35, 0.75, 0.7, 0.05, 0.5, 0.12, 0.4, 0.3);
                        int cs = PutChunk(c, sp, 0, true);
                        int nt = 0;
                        for (int ui = 0; ui < usSecondary.Length; ui++) bs.Add(MakeSite(sp, usSecondary[ui], g2, bi, j, 9, ui));
                        double g3 = SketchMath.Sm((g - 0.6) / 0.4);
                        if (g3 > 0.02)
                        {
                            for (int q = 0; q < 2; q++)
                            {
                                double ut = q != 0 ? 0.75 : 0.45, fi2 = ut * 4;
                                int k0 = Math.Min(3, (int)Math.Floor(fi2));
                                double f2 = fi2 - k0;
                                D4 T0 = Lerp4(sp[k0], sp[k0 + 1], f2);
                                D3 t2 = NormOr1(Dir3(sp[k0], sp[k0 + 1]));
                                double s3 = (q != 0 ? -1 : 1) * sg, c3 = Math.Cos(0.7 * s3), n3 = Math.Sin(0.7 * s3);
                                var d3 = new D3(t2.X * c3 + t2.Z * n3, t2.Y * 0.3 + 0.12, -t2.X * n3 + t2.Z * c3);
                                double dl3 = SketchMath.Hypot(d3.X, d3.Y, d3.Z);
                                var dd3 = new D3(d3.X / dl3, d3.Y / dl3, d3.Z / dl3);
                                double Lt3 = 0.5 * Ls * (1 - 0.3 * ut) * g3, r3 = Math.Max(0.35, 0.6 * T0.W);
                                D4[] tq = Twig(T0, dd3, Lt3, r3, 0.4, 0.75, 0.75, 0.05, 0.5, 0.12, 0.32, 0.35);
                                PutChunk(c, tq, 0, true);
                                nt++;
                                for (int ui = 0; ui < usTertiary.Length; ui++) bs.Add(MakeSite(tq, usTertiary[ui], g3, bi, j, q, ui));
                            }
                        }

                        SubBound(c, cs, nt);
                    }
                }

                double gp = SketchMath.Sm((g - 0.05) / 0.7);
                if (gp > 0.02)
                {
                    const double fiP = 5.2;
                    const int i0P = 5;
                    double frP = fiP - i0P;
                    D4 a5 = pts[i0P], a6 = pts[i0P + 1];
                    var pc = new D3(a5.X + (a6.X - a5.X) * frP, a5.Y + (a6.Y - a5.Y) * frP, a5.Z + (a6.Z - a5.Z) * frP);
                    var hd = new D3(pts[8].X - pts[0].X, 0, pts[8].Z - pts[0].Z);
                    double bb = Math.Max(2.5, 0.48 * Lb * (0.55 + 0.45 * gp)), aa = Math.Max(3, 0.6 * Lb * (0.55 + 0.45 * gp));
                    PutPad(c, SketchMath.Add(pc, UP, 0.1 * bb), hd, aa, bb, pc.Y + 0.04 * bb, b.Pj, false, bi, gp);
                }

                {
                    int pi = gp > 0.02 ? c.Np - 1 : -1;
                    for (int k = 0; k < bs.Count; k++)
                    {
                        Site x = bs[k];
                        x.Pad = pi;
                        c.Sites.Add(x);
                    }
                }

                PutGroup(c, ng, c0, 0.9 * rb0 + 0.5, 0.6 * rsAvg + 0.3);
                ng++;
                nb++;
            }

            // apex crown
            double ga = SketchMath.Sm((t - 0.62) / 0.3);
            List<Site> asl = c.As;
            asl.Clear();
            if (ga > 0.01)
            {
                int c0 = c.Nc;
                double rbA = 1;
                for (int ai = 0; ai < M.Apex.Length; ai++)
                {
                    SketchApex a = M.Apex[ai];
                    double s = a.F * L;
                    D3 P0 = Ax(M, s);
                    double rb = Math.Max(0.5, (a.El > 0.8 ? 0.6 : 0.36) * TR(s)), ln = a.Len * LMAX * ga;
                    var dv = new D3(Math.Sin(a.Az) * Math.Cos(a.El), Math.Sin(a.El), Math.Cos(a.Az) * Math.Cos(a.El));
                    rbA = rb;
                    D4[] sp = Twig(new D4(P0, rb), dv, ln, rb, 0.35, 0.72, 0.7, 0.08, 0.48, 0.16, 0.4, 0.3);
                    int cs = PutChunk(c, sp, 0, true);
                    asl.Add(MakeSite(sp, 1, ga, 99, ai, 9, 0));
                    asl.Add(MakeSite(sp, 0.6, ga, 99, ai, 9, 1));
                    if (ga > 0.4)
                    {
                        var T0 = new D4((sp[2].X + sp[3].X) / 2, (sp[2].Y + sp[3].Y) / 2, (sp[2].Z + sp[3].Z) / 2, (sp[2].W + sp[3].W) / 2);
                        double hz = SketchMath.Hypot(dv.X, dv.Z);
                        if (hz == 0 || double.IsNaN(hz)) hz = 1;
                        int sgn = a.Az > 0 ? 1 : -1;
                        var d3 = new D3(dv.X / hz * Math.Cos(0.8 * sgn) + dv.Z / hz * Math.Sin(0.8 * sgn), 0.35,
                            -dv.X / hz * Math.Sin(0.8 * sgn) + dv.Z / hz * Math.Cos(0.8 * sgn));
                        double l3 = SketchMath.Hypot(d3.X, d3.Y, d3.Z);
                        var dd3 = new D3(d3.X / l3, d3.Y / l3, d3.Z / l3);
                        double Lt3 = 0.45 * ln * SketchMath.Sm((ga - 0.4) / 0.5), r3 = Math.Max(0.35, 0.6 * T0.W);
                        D4[] fq = Twig(T0, dd3, Lt3, r3, 0.4, 0.75, 0.75, 0.05, 0.5, 0.12, 0.32, 0.35);
                        PutChunk(c, fq, 0, true);
                        SubBound(c, cs, 1);
                        asl.Add(MakeSite(fq, 1, ga, 99, ai, 1, 0));
                    }
                }

                PutGroup(c, ng, c0, 0.8 * rbA + 0.4, 0.6 * rbA + 0.3);
                ng++;
            }

            // apex dome pad + seedling sites
            {
                double ap = SketchMath.Sm((t - 0.3) / 0.6), bb = 4 + 0.115 * LMAX * ap;
                D3 ctr = SketchMath.Add(Ax(M, L * (0.97 - 0.04 * ap)), UP, 0.02 * LMAX * ap + 1.5);
                PutPad(c, ctr, new D3(1, 0, 0), bb * 1.05, bb, ctr.Y - 0.12 * bb, M.Apj, true, 99, ap);
                double sg0 = 1 - ga;
                if (sg0 > 0.02)
                {
                    for (int k = 0; k < 5; k++)
                    {
                        double s = L * (0.985 - 0.055 * k);
                        if (s < 4) break;
                        double ang = k * 2.4 + c.Seed;
                        asl.Add(new Site
                        {
                            P = Ax(M, s),
                            D = new D3(Math.Cos(ang), 0.3, Math.Sin(ang)),
                            Sc = sg0 * Math.Min(1, 0.45 + t * 3),
                            Id0 = 98,
                            Id1 = k,
                            Id2 = 9,
                            Id3 = 0,
                            Tip = k == 0,
                            Pad = -1
                        });
                    }
                }

                int pi = c.Np - 1;
                for (int k = 0; k < asl.Count; k++)
                {
                    Site x = asl[k];
                    x.Pad = pi;
                    c.Sites.Add(x);
                }
            }

            SketchScene S = c.S;
            S.L = L;
            S.R = R;
            S.Nr = nr;
            S.Ng = ng;
            S.Nb = nb;
            S.Nc = c.Nc;
            S.Np = c.Np;
            S.Nl = GenLeaves(c);
            S.Nx = GenExtras(c, S.Nl);
        }

        /// <summary>JS padHit(P, k): does the light ray from P pass through pad k's ellipsoid (beyond 0.5)?</summary>
        private static bool PadHit(Ctx c, D3 P, int k)
        {
            Pad pd = c.Pads[k];
            double ceX = pd.C.X, ceY = pd.C.Y + 0.2 * pd.B, ceZ = pd.C.Z;
            double ra0 = 0.9 * pd.A, ra1 = 0.6 * pd.B, ra2 = 0.9 * pd.B;
            D3 LL = c.LL;
            D3 e0 = pd.Hx, e2 = pd.Pp;
            double o0 = ((P.X - ceX) * e0.X + (P.Y - ceY) * e0.Y + (P.Z - ceZ) * e0.Z) / ra0;
            double d0 = (LL.X * e0.X + LL.Y * e0.Y + LL.Z * e0.Z) / ra0;
            double o1 = ((P.X - ceX) * 0 + (P.Y - ceY) * 1 + (P.Z - ceZ) * 0) / ra1;
            double d1 = (LL.X * 0 + LL.Y * 1 + LL.Z * 0) / ra1;
            double o2 = ((P.X - ceX) * e2.X + (P.Y - ceY) * e2.Y + (P.Z - ceZ) * e2.Z) / ra2;
            double d2 = (LL.X * e2.X + LL.Y * e2.Y + LL.Z * e2.Z) / ra2;
            double A = d0 * d0 + d1 * d1 + d2 * d2, B = 2 * (o0 * d0 + o1 * d1 + o2 * d2), C = o0 * o0 + o1 * o1 + o2 * o2 - 1;
            double D = B * B - 4 * A * C;
            return D >= 0 && (-B + Math.Sqrt(D)) / (2 * A) > 0.5;
        }

        private static void PutLeaf(float[] dst, int o, D3 P, D3 D, D3 Nn, double len, double wid, D3 col, double ex)
        {
            dst[o] = (float)P.X;
            dst[o + 1] = (float)P.Y;
            dst[o + 2] = (float)P.Z;
            dst[o + 3] = (float)D.X;
            dst[o + 4] = (float)D.Y;
            dst[o + 5] = (float)D.Z;
            dst[o + 6] = (float)Nn.X;
            dst[o + 7] = (float)Nn.Y;
            dst[o + 8] = (float)Nn.Z;
            dst[o + 9] = (float)len;
            dst[o + 10] = (float)wid;
            dst[o + 11] = (float)col.X;
            dst[o + 12] = (float)col.Y;
            dst[o + 13] = (float)col.Z;
            dst[o + 14] = (float)ex;
            dst[o + 15] = 0;
        }

        private static D3 LeafColor(SpeciesDef S, double nm, double tv)
        {
            return new D3((S.Base.X * (1 - nm) + S.Nw.X * nm) * tv, (S.Base.Y * (1 - nm) + S.Nw.Y * nm) * tv,
                (S.Base.Z * (1 - nm) + S.Nw.Z * nm) * tv);
        }

        /// <summary>JS needle direction: cone around ax3 with half-angle off and azimuth az.</summary>
        private static D3 Cone(D3 ax3, D3 tm, double off, double az)
        {
            D3 e1 = SketchMath.Nrm(SketchMath.Cr(ax3, tm)), e2 = SketchMath.Cr(ax3, e1);
            double co = Math.Cos(off), so = Math.Sin(off), ca = Math.Cos(az), sa = Math.Sin(az);
            return SketchMath.Nrm(new D3(ax3.X * co + (e1.X * ca + e2.X * sa) * so, ax3.Y * co + (e1.Y * ca + e2.Y * sa) * so,
                ax3.Z * co + (e1.Z * ca + e2.Z * sa) * so));
        }

        /// <summary>JS genLeaves(): site leaves, then pad leaves.</summary>
        private static int GenLeaves(Ctx c)
        {
            SpeciesDef S = c.Sp;
            float[] LEAF = c.Leaf;
            D3 LV = SketchLayout.LV;
            int n = 0;
            int nPads = c.Np;
            long seed = c.Seed;
            for (int si = 0; si < c.Sites.Count; si++)
            {
                Site st = c.Sites[si];
                if (st.Sc <= 0.02) continue;
                bool hasPd = st.Pad >= 0;
                Pad pd = hasPd ? c.Pads[st.Pad] : default(Pad);
                double sh = 1;
                for (int k = 0; k < nPads; k++)
                    if (k != st.Pad && PadHit(c, st.P, k)) sh *= 0.5;
                long ha = seed * 131 + st.Id0, hb = st.Id1 * 17 + st.Id2;
                for (int k = 0; k < S.Per && n < MAXLEAF; k++)
                {
                    long hc = st.Id3 * 31 + k;
                    D3 P, D, Nn;
                    double len, wid;
                    if (S.Id == 2)
                    {
                        double hl = SketchMath.Hypot(st.D.X, st.D.Z);
                        D3 ax3 = SketchMath.Nrm(new D3(st.D.X, st.D.Y + hl * 1.3 + 0.3 * (hl + 0.01), st.D.Z));
                        double off = 0.2 + 1.05 * SketchMath.Hsh(ha, hb, hc, 1), az = SketchMath.Hsh(ha, hb, hc, 2) * 6.283;
                        D3 tm = Math.Abs(ax3.Y) < 0.9 ? new D3(0, 1, 0) : new D3(1, 0, 0);
                        D = Cone(ax3, tm, off, az);
                        P = new D3(st.P.X + (SketchMath.Hsh(ha, hb, hc, 3) - 0.5) * 1.2, st.P.Y + (SketchMath.Hsh(ha, hb, hc, 4) - 0.5) * 1.2,
                            st.P.Z + (SketchMath.Hsh(ha, hb, hc, 5) - 0.5) * 1.2);
                        len = S.Len * st.Sc * (0.7 + 0.5 * SketchMath.Hsh(ha, hb, hc, 6));
                        wid = S.Wid;
                        D3 side = SketchMath.Nrm(SketchMath.Cr(D, LV));
                        Nn = SketchMath.Nrm(SketchMath.Cr(side, D));
                    }
                    else
                    {
                        var hd = new D3(st.D.X, 0, st.D.Z);
                        if (SketchMath.Hypot(hd.X, hd.Z) < 1e-3)
                        {
                            double a0 = SketchMath.Hsh(ha, hb, hc, 1) * 6.283;
                            hd = new D3(Math.Cos(a0), 0, Math.Sin(a0));
                        }

                        hd = SketchMath.Nrm(hd);
                        double rot = (k % 2 != 0 ? 1 : -1) * (0.45 + 0.8 * SketchMath.Hsh(ha, hb, hc, 2))
                                     + (k > 1 ? (SketchMath.Hsh(ha, hb, hc, 7) - 0.5) * 1.5 : 0);
                        double cr = Math.Cos(rot), sr = Math.Sin(rot);
                        double h3 = SketchMath.Hsh(ha, hb, hc, 3);
                        D = SketchMath.Nrm(new D3(hd.X * cr + hd.Z * sr, S.Hang ? -0.3 - 0.45 * h3 : 0.12 + 0.4 * h3, -hd.X * sr + hd.Z * cr));
                        double ja = SketchMath.Hsh(ha, hb, hc, 4) * 6.283, jr = SketchMath.Hsh(ha, hb, hc, 5) * 0.75 * S.Len * st.Sc;
                        P = new D3(st.P.X + Math.Cos(ja) * jr, st.P.Y + (SketchMath.Hsh(ha, hb, hc, 6) - 0.35) * 0.3 * S.Len * st.Sc,
                            st.P.Z + Math.Sin(ja) * jr);
                        double tl = -0.5 + 0.5 * SketchMath.Hsh(ha, hb, hc, 8), na = SketchMath.Hsh(ha, hb, hc, 9) * 6.283,
                            nr = 0.35 + 0.9 * SketchMath.Hsh(ha, hb, hc, 10);
                        var nn = new D3(D.X * tl + Math.Cos(na) * nr, 1, D.Z * tl + Math.Sin(na) * nr);
                        double dd = SketchMath.Dt(nn, D);
                        Nn = SketchMath.Nrm(new D3(nn.X - D.X * dd, nn.Y - D.Y * dd, nn.Z - D.Z * dd));
                        double sz = 0.8 + 0.4 * SketchMath.Hsh(ha, hb, hc, 11);
                        len = S.Len * st.Sc * sz;
                        wid = S.Wid * st.Sc * sz;
                    }

                    double vert = hasPd ? SketchMath.Cl((P.Y - pd.Yb) / (0.9 * pd.B), 0, 1) : 1;
                    double ex = (0.3 + 0.7 * sh) * (0.45 + 0.55 * vert);
                    double h12 = SketchMath.Hsh(ha, hb, hc, 12);
                    double nm = S.NewMix * (st.Tip ? h12 : 0.25 * h12), tv = 0.85 + 0.3 * SketchMath.Hsh(ha, hb, hc, 13);
                    PutLeaf(LEAF, n * 16, P, D, Nn, len, wid, LeafColor(S, nm, tv), ex);
                    n++;
                }
            }

            for (int pi = 0; pi < nPads; pi++)
            {
                Pad pd = c.Pads[pi];
                if (pd.Gp <= 0.02) continue;
                int cnt = (int)Math.Min(1400, SketchMath.JsRound(Math.PI * pd.A * pd.B * S.Fill / 100));
                double H = (pd.Dome ? 0.9 : 0.55) * pd.B;
                long ha = seed * 977 + pd.Key;
                for (int k = 0; k < cnt && n < MAXLEAF; k++)
                {
                    double ra = Math.Sqrt(SketchMath.Hsh(ha, k, 1, 7)), an = SketchMath.Hsh(ha, k, 2, 7) * 6.283;
                    double u = Math.Cos(an) * ra, v = Math.Sin(an) * ra;
                    D3 rd = SketchMath.Nrm(new D3(pd.Hx.X * u * pd.A + pd.Pp.X * v * pd.B, 0, pd.Hx.Z * u * pd.A + pd.Pp.Z * v * pd.B));
                    double hy = H * Math.Pow(Math.Max(0, 1 - ra * ra), 0.55) * (0.55 + 0.45 * SketchMath.Hsh(ha, k, 3, 7));
                    var P0 = new D3(pd.C.X + pd.Hx.X * u * pd.A + pd.Pp.X * v * pd.B, pd.Yb + hy,
                        pd.C.Z + pd.Hx.Z * u * pd.A + pd.Pp.Z * v * pd.B);
                    double sh = 1;
                    for (int q = 0; q < nPads; q++)
                        if (q != pi && PadHit(c, P0, q)) sh *= 0.5;
                    double vert = SketchMath.Cl(hy / H, 0, 1), ex = (0.3 + 0.7 * sh) * (0.4 + 0.6 * vert);
                    bool tip = ra > 0.75 || vert > 0.85;
                    double sc = 0.45 + 0.55 * pd.Gp;
                    for (int f = 0; f < S.Fper && n < MAXLEAF; f++)
                    {
                        long gb = k * 31 + f;
                        D3 D, Nn, P = P0;
                        double len, wid;
                        if (S.Id == 2)
                        {
                            D3 ax3 = SketchMath.Nrm(new D3(rd.X * 0.7 * ra, 1, rd.Z * 0.7 * ra));
                            double off = 0.15 + 1.0 * SketchMath.Hsh(ha, gb, 1, 11), az = SketchMath.Hsh(ha, gb, 2, 11) * 6.283;
                            D = Cone(ax3, new D3(1, 0, 0), off, az);
                            P = new D3(P0.X + (SketchMath.Hsh(ha, gb, 3, 11) - 0.5) * 1.4, P0.Y + (SketchMath.Hsh(ha, gb, 4, 11) - 0.5) * 1.4,
                                P0.Z + (SketchMath.Hsh(ha, gb, 5, 11) - 0.5) * 1.4);
                            len = S.Len * sc * (0.65 + 0.5 * SketchMath.Hsh(ha, gb, 6, 11));
                            wid = S.Wid;
                            D3 side = SketchMath.Nrm(SketchMath.Cr(D, LV));
                            Nn = SketchMath.Nrm(SketchMath.Cr(side, D));
                        }
                        else
                        {
                            double ja = SketchMath.Hsh(ha, gb, 1, 11) * 6.283;
                            var ho = new D3(Math.Cos(ja), 0, Math.Sin(ja));
                            double g2 = SketchMath.Hsh(ha, gb, 2, 11), g3 = SketchMath.Hsh(ha, gb, 3, 11);
                            D3 nn;
                            if (S.Hang)
                            {
                                D = SketchMath.Nrm(new D3(rd.X * 0.5 + ho.X * 0.6, -0.35 - 0.45 * g2, rd.Z * 0.5 + ho.Z * 0.6));
                                nn = new D3(rd.X * 0.8 + ho.X * 0.5, 0.35 + 0.4 * g3, rd.Z * 0.8 + ho.Z * 0.5);
                            }
                            else
                            {
                                double ed = ra * ra;
                                D = SketchMath.Nrm(new D3(rd.X * (0.5 + ed) + ho.X * 0.7, 0.15 - 0.55 * ed + 0.2 * g2,
                                    rd.Z * (0.5 + ed) + ho.Z * 0.7));
                                nn = new D3(rd.X * (0.9 * ed) + ho.X * 0.45 * g3, 1 - 0.45 * ed, rd.Z * (0.9 * ed) + ho.Z * 0.45 * g3);
                            }

                            double dd = SketchMath.Dt(nn, D);
                            Nn = SketchMath.Nrm(new D3(nn.X - D.X * dd, nn.Y - D.Y * dd, nn.Z - D.Z * dd));
                            double sz = (0.8 + 0.4 * SketchMath.Hsh(ha, gb, 4, 11)) * sc;
                            len = S.Len * sz;
                            wid = S.Wid * sz;
                        }

                        double g7 = SketchMath.Hsh(ha, gb, 7, 11);
                        double nm = S.NewMix * (tip ? g7 : 0.2 * g7), tv = 0.85 + 0.3 * SketchMath.Hsh(ha, gb, 8, 11);
                        PutLeaf(LEAF, n * 16, P, D, Nn, len, wid, LeafColor(S, nm, tv), ex);
                        n++;
                    }
                }
            }

            return n;
        }

        /// <summary>JS genExtras(n): blossoms (yamazakura) and candles (kuromatsu) derived from LEAF (read back as float).</summary>
        private static int GenExtras(Ctx c, int n)
        {
            SpeciesDef S = c.Sp;
            if (S.Id == 0) return 0;
            float[] LEAF = c.Leaf, XLEAF = c.XLeaf;
            D3 LV = SketchLayout.LV;
            long seed = c.Seed;
            int x = 0;
            for (int i = 0; i < n && x < MAXX; i++)
            {
                int o = i * 16;
                double ex = LEAF[o + 14];
                long ha = seed * 53 + i;
                if (S.Id == 1)
                {
                    if (SketchMath.Hsh(ha, 1, 7, 3) > 0.3 || ex < 0.35) continue;
                    int cn = 1 + (int)Math.Floor(SketchMath.Hsh(ha, 2, 7, 3) * 3);
                    for (int k = 0; k < cn && x < MAXX; k++)
                    {
                        long gb = k * 7;
                        double ln = LEAF[o + 9] * 0.2;
                        var P = new D3((double)LEAF[o] + LEAF[o + 3] * ln + (SketchMath.Hsh(ha, gb + 1, 9, 5) - .5) * 5,
                            LEAF[o + 1] - 1.5 * SketchMath.Hsh(ha, gb + 2, 9, 5),
                            (double)LEAF[o + 2] + LEAF[o + 5] * ln + (SketchMath.Hsh(ha, gb + 3, 9, 5) - .5) * 5);
                        D3 Nv = SketchMath.Nrm(new D3(LEAF[o + 6] * 0.35 + (SketchMath.Hsh(ha, gb + 4, 9, 5) - .5) * 0.5,
                            LEAF[o + 7] * 0.35 + LV.Y * 0.65 + 0.25 + (SketchMath.Hsh(ha, gb + 5, 9, 5) - .5) * 0.4,
                            LEAF[o + 8] * 0.35 + LV.Z * 0.65 + (SketchMath.Hsh(ha, gb + 6, 9, 5) - .5) * 0.5));
                        D3 D = SketchMath.Nrm(SketchMath.Cr(Nv, Math.Abs(Nv.X) < 0.9 ? new D3(1, 0, 0) : new D3(0, 1, 0)));
                        double sz = 5 * (0.85 + 0.3 * SketchMath.Hsh(ha, gb + 7, 9, 5));
                        PutLeaf(XLEAF, x * 16, P, D, Nv, sz, sz, new D3(0.97, 0.86 * (0.97 + 0.05 * SketchMath.Hsh(ha, gb + 8, 9, 5)), 0.9), ex);
                        x++;
                    }
                }
                else if (S.Id == 2)
                {
                    if (i % S.Fper != 0 || SketchMath.Hsh(ha, 1, 7, 3) > 0.65 || ex < 0.5 || LEAF[o + 7] < 0.1) continue;
                    D3 D = SketchMath.Nrm(new D3(LEAF[o + 3] * 0.2 + (SketchMath.Hsh(ha, 2, 7, 3) - .5) * 0.3, 1,
                        LEAF[o + 5] * 0.2 + (SketchMath.Hsh(ha, 3, 7, 3) - .5) * 0.3));
                    D3 sd = SketchMath.Nrm(SketchMath.Cr(D, LV));
                    D3 Nv = SketchMath.Nrm(SketchMath.Cr(sd, D));
                    PutLeaf(XLEAF, x * 16, new D3(LEAF[o], LEAF[o + 1] + 5.0, LEAF[o + 2]), D, Nv,
                        20 * (0.8 + 0.4 * SketchMath.Hsh(ha, 4, 7, 3)), 3.8, new D3(0.93, 0.89, 0.72), ex);
                    x++;
                }
            }

            return x;
        }
    }
}
