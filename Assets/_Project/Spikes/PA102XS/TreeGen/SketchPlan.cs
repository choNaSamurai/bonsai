// PA-102X-S spike: result of JS build(style, seed) — the plan of the mature tree (sketch_v11 lines 48-85).
using System;

namespace PocketArbor.Spike.TreeGen
{
    public struct SketchBend
    {
        public double S;
        public int Out;
    }

    public sealed class SketchRoot
    {
        public double Az, Len, Th, E, Wob, Ch;
        public SketchNoise Nz;
    }

    public sealed class SketchBranch
    {
        public double S;
        public int Side;
        public bool Back;
        public double Rel, Az, Len, Elev, Droop, Rise, Wph, Wamp, Thk;
        public int Nsec;
        public int Ss;
        public double Ts;
        public readonly double[] Pj = new double[7];
    }

    public struct SketchApex
    {
        public double F, Az, El, Len;

        public SketchApex(double f, double az, double el, double len)
        {
            F = f;
            Az = az;
            El = el;
            Len = len;
        }
    }

    /// <summary>JS object M returned by build().</summary>
    public sealed class SketchPlan
    {
        public string Style;
        public int Seed;
        public SketchNoise Nz;
        public SketchNoise Nl;
        /// <summary>Trunk polyline (N+1) x 2 interleaved x,y.</summary>
        public readonly double[] P = new double[(SketchLayout.N + 1) * 2];
        /// <summary>Trunk angle per sample (N+1).</summary>
        public readonly double[] T = new double[SketchLayout.N + 1];
        /// <summary>Number of bends (JS m.n).</summary>
        public int N;
        public double Rmax;
        public double Cx;
        public double Tx;
        public double ZA, Zc, Zl;
        public SketchBend[] Bends;
        public SketchRoot[] Roots;
        public SketchBranch[] Branches;
        public SketchApex[] Apex;
        public readonly double[] Apj = new double[7];

        /// <summary>JS M.zf(s): depth wobble of the trunk axis.</summary>
        public double Zf(double s)
        {
            const double LMAX = SketchLayout.LMAX;
            return ZA * (Math.Sin(Math.PI * 1.4 * s / LMAX + Zc) - Math.Sin(Zc))
                   + Zl * LMAX * Math.Pow(Math.Max(0, s) / LMAX, 3);
        }

        /// <summary>JS at(s): 2D trunk axis point at arc length s (x, y).</summary>
        public void At(double s, out double x, out double y)
        {
            if (s <= 0)
            {
                double a = T[0];
                x = Math.Sin(a) * s;
                y = Math.Cos(a) * s;
                return;
            }

            double f = Math.Min(s / SketchLayout.DS, SketchLayout.N - 1e-4);
            int i = (int)Math.Floor(f);
            double u = f - i;
            x = P[i * 2] + (P[i * 2 + 2] - P[i * 2]) * u;
            y = P[i * 2 + 1] + (P[i * 2 + 3] - P[i * 2 + 1]) * u;
        }

        /// <summary>JS Rt(t): trunk base radius at age t.</summary>
        public double Rt(double t)
        {
            return Rmax * (0.1 + 0.9 * Math.Pow(t, 1.15));
        }
    }
}
