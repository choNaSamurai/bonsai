// PA-102X-S spike: port of JS build(style, sd) (sketch_v11 lines 48-85). Order of r() calls is load-bearing.
using System;
using System.Collections.Generic;

namespace PocketArbor.Spike.TreeGen
{
    public static partial class SketchGenerator
    {
        private const double LMAX = SketchLayout.LMAX;
        private const int N = SketchLayout.N;
        private const double DS = SketchLayout.DS;

        /// <summary>JS build(style, sd): plan of the mature tree.</summary>
        public static SketchPlan Build(string style, int seed)
        {
            if (style == null) throw new ArgumentNullException(nameof(style));
            var r = new SketchRng(seed * 9973 + style.Length * 131);
            var m = new SketchPlan { Style = style, Seed = seed };
            m.Nz = new SketchNoise(r);
            m.Nl = new SketchNoise(r);
            int side = r.Next() < 0.5 ? -1 : 1;
            int n;
            double a0, q, tx, ratio;
            if (style == "chokkan")
            {
                n = 3;
                a0 = r.Range(0.015, 0.03);
                q = 0.8;
                tx = 0;
                ratio = 5.4;
            }
            else if (style == "moyogi")
            {
                n = 4 + (int)Math.Floor(r.Next() * 2);
                a0 = r.Range(0.72, 0.86);
                q = r.Range(0.8, 0.87);
                tx = side * r.Range(0, 0.05) * LMAX;
                ratio = 5.9;
            }
            else if (style == "bunjin")
            {
                n = 2 + (int)Math.Floor(r.Next() * 2);
                a0 = r.Range(0.4, 0.52);
                q = r.Range(0.82, 0.88);
                tx = side * r.Range(0.1, 0.2) * LMAX;
                ratio = 12.5;
            }
            else if (style == "fukinagashi")
            {
                n = 2 + (int)Math.Floor(r.Next() * 2);
                a0 = r.Range(0.4, 0.5);
                q = r.Range(0.78, 0.85);
                tx = side * r.Range(0.32, 0.38) * LMAX;
                ratio = 6.4;
            }
            else
            {
                n = 2 + (int)Math.Floor(r.Next() * 2);
                a0 = r.Range(0.3, 0.4);
                q = r.Range(0.76, 0.84);
                tx = side * r.Range(0.28, 0.34) * LMAX;
                ratio = 6.2;
            }

            double sb = r.Range(0.03, 0.06) * LMAX;
            double lq = Math.Log(q);
            double l0 = (LMAX - sb) * (-lq) / (1 - Math.Pow(q, n));
            int dir = r.Next() < 0.5 ? -1 : 1;
            const double qa = 0.66;
            double wob = style == "chokkan" ? 0.02 : 0.035;
            const double beta = 1.6;
            double tb = Math.Tanh(beta);
            SketchNoise nz = m.Nz;

            // ph(s), th(s, d)
            double Ph(double s)
            {
                return s <= sb ? 0 : Math.PI * Math.Log(Math.Max(1e-6, 1 + (s - sb) * lq / l0)) / lq;
            }

            double Th(double s, double d)
            {
                double p = Ph(s), k = p / Math.PI;
                return d + dir * a0 * Math.Pow(qa, k) * (1 + 0.25 * nz[k * 1.3 + 3]) * Math.Tanh(beta * Math.Sin(p)) / tb
                       * SketchMath.Sm((s - sb) / (0.22 * l0)) + wob * nz[s / 45 + 20];
            }

            double dd = 0;
            double[] P = m.P, T = m.T;
            for (int it = 0; it < 6; it++)
            {
                P[0] = 0;
                P[1] = 0;
                double x = 0, y = 0;
                for (int i = 0; i <= N; i++)
                {
                    T[i] = Th(i * DS, dd);
                    if (i < N)
                    {
                        double b = Th((i + 0.5) * DS, dd);
                        x += Math.Sin(b) * DS;
                        y += Math.Cos(b) * DS;
                        P[(i + 1) * 2] = x;
                        P[(i + 1) * 2 + 1] = y;
                    }
                }

                if (it < 5) dd -= (x - tx) / y;
            }

            m.N = n;
            m.Tx = tx;
            m.Rmax = LMAX / ratio / 2;
            m.Cx = -P[N * 2] / 2;
            m.ZA = r.Range(-1, 1) * 0.035 * LMAX;
            m.Zc = r.Range(0, 6.283);
            m.Zl = style == "chokkan" ? 0.02 : 0.06;
            m.Bends = new SketchBend[n];
            for (int k = 0; k < n; k++)
                m.Bends[k] = new SketchBend { S = sb + l0 * (Math.Pow(q, k) - 1) / lq, Out = -dir * (k % 2 != 0 ? -1 : 1) };

            bool bj = style == "bunjin", fk = style == "fukinagashi";
            int lee = tx > 0 ? 1 : -1;
            int nR = style == "chokkan" ? 7 : bj ? 4 + (int)Math.Floor(r.Next() * 2) : 5 + (int)Math.Floor(r.Next() * 3);
            double off = r.Next() * 6.283;
            m.Roots = new SketchRoot[nR];
            for (int i = 0; i < nR; i++)
            {
                double az = off + i * 6.283 / nR + r.Range(-0.25, 0.25);
                if (Math.Cos(az) > 0.72) az += (Math.Sin(az) >= 0 ? 1 : -1) * 0.65;
                var o = new SketchRoot { Az = az };
                o.Len = r.Range(2.6, 3.5) * (i % 2 != 0 ? 0.82 : 1.15);
                o.Th = r.Range(0.72, 0.86);
                o.E = i < 2 ? 0 : r.Range(0.08, 0.6);
                o.Wob = r.Range(-0.5, 0.5);
                o.Nz = new SketchNoise(r);
                o.Ch = r.Range(1.5, 2.6);
                m.Roots[i] = o;
            }

            // branches: laterals on the outside of bends, backs between them, shorter and steeper upwards
            const double top = 0.84 * LMAX;
            var B = new List<SketchBranch>(8);

            double Kap(double s)
            {
                int i = (int)SketchMath.Cl(SketchMath.JsRound(s / DS), 1, N - 1);
                return (T[i + 1] - T[i - 1]) / (2 * DS);
            }

            {
                double s = (bj ? r.Range(0.6, 0.66) : r.Range(0.28, 0.31)) * LMAX;
                double gap = (bj ? r.Range(0.09, 0.11) : fk ? r.Range(0.17, 0.19) : r.Range(0.14, 0.16)) * LMAX;
                int prev = 0, prev2 = 0, bk = r.Next() < 0.5 ? -1 : 1, i = 0;
                int maxB = bj ? 4 : fk ? 5 : 8;
                while (s < top && B.Count < maxB)
                {
                    if (i % 3 == 2)
                    {
                        B.Add(new SketchBranch { S = s, Side = bk, Back = true });
                        bk = -bk;
                    }
                    else
                    {
                        double k = Kap(s);
                        int sd = Math.Abs(k) > 0.002 && style != "chokkan" ? (k > 0 ? -1 : 1) : (prev != 0 ? -prev : side);
                        if (sd == prev && sd == prev2) sd = -sd;
                        if (style == "shakan" && B.Count == 0) sd = tx > 0 ? -1 : 1;
                        if (fk) sd = lee;
                        B.Add(new SketchBranch { S = s, Side = sd, Back = false });
                        prev2 = prev;
                        prev = sd;
                    }

                    s += gap;
                    gap *= 0.8;
                    i++;
                }
            }

            double s1 = B[0].S;
            for (int i = 0; i < B.Count; i++)
            {
                SketchBranch b = B[i];
                double rel = SketchMath.Cl((b.S - s1) / (top - s1), 0, 1);
                b.Rel = rel;
                b.Az = fk
                    ? (b.Back ? Math.PI - lee * r.Range(0.5, 0.8) : lee * (Math.PI / 2 + r.Range(-0.35, 0.35)))
                    : b.Back
                        ? Math.PI + b.Side * r.Range(0.4, 0.65)
                        : b.Side * (Math.PI / 2 + (rel > 0.5 && i % 2 != 0 ? -0.45 : 0.2) + r.Range(-0.12, 0.12));
                b.Len = (fk ? 0.2 + 0.38 * Math.Pow(rel, 0.8)
                            : bj ? 0.3 * (1 - 0.55 * Math.Pow(rel, 1.1))
                            : 0.37 * (1 - 0.55 * Math.Pow(rel, 1.1)))
                        * LMAX * (b.Back ? 0.72 : 1) * r.Range(0.92, 1.06);
                b.Elev = fk ? -0.04 + 0.08 * rel + r.Range(-0.04, 0.04)
                    : bj ? -0.18 + 0.4 * rel + r.Range(-0.05, 0.05)
                    : -0.06 + 0.55 * rel + r.Range(-0.05, 0.05);
                b.Droop = 0.24 - 0.18 * rel;
                b.Rise = 0.16 + 0.1 * rel;
                b.Wph = r.Range(0, 6.28);
                b.Wamp = r.Range(0.04, 0.08);
                b.Thk = (0.33 - 0.06 * rel) * r.Range(0.94, 1.0);
                b.Nsec = rel < 0.5 ? 4 : 3;
                b.Ss = r.Next() < 0.5 ? -1 : 1;
                b.Ts = SketchMath.TOfL(b.S + 0.05 * LMAX);
                for (int j = 0; j < 7; j++) b.Pj[j] = r.Next();
            }

            m.Branches = B.ToArray();

            int sa = r.Next() < 0.5 ? -1 : 1;
            if (fk)
            {
                m.Apex = new[]
                {
                    new SketchApex(0.85, lee * 1.35, 0.05, 0.17),
                    new SketchApex(0.875, lee * 2.0, 0.08, 0.15),
                    new SketchApex(0.9, lee * 0.95, 0.15, 0.14),
                    new SketchApex(0.92, lee * 2.4, 0.15, 0.12),
                    new SketchApex(0.945, lee * 1.6, 0.25, 0.11),
                    new SketchApex(0.97, lee * 1.2, 0.45, 0.09)
                };
            }
            else
            {
                m.Apex = new[]
                {
                    new SketchApex(0.85, sa * 1.45, 0.12, 0.15),
                    new SketchApex(0.875, -sa * 1.55, 0.18, 0.14),
                    new SketchApex(0.9, Math.PI - sa * 0.7, 0.3, 0.12),
                    new SketchApex(0.92, sa * 0.45, 0.35, 0.11),
                    new SketchApex(0.945, Math.PI + sa * 0.8, 0.5, 0.1),
                    new SketchApex(0.97, r.Range(-0.4, 0.4), 0.9, 0.08)
                };
            }

            for (int j = 0; j < 7; j++) m.Apj[j] = r.Next();
            return m;
        }
    }
}
