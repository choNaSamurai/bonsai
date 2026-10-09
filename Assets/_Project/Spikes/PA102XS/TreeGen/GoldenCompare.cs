// PA-102X-S spike: comparison of a generated scene against golden data (G1 criterion).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PocketArbor.Spike.TreeGen
{
    /// <summary>Per-array comparison statistics.</summary>
    public sealed class ArrayDiff
    {
        public string Name;
        public int Compared;
        /// <summary>Max |a - b|.</summary>
        public double MaxAbs;
        /// <summary>Element index of <see cref="MaxAbs"/> (in the flat array).</summary>
        public int MaxAbsIndex = -1;
        /// <summary>Max |a - b| / max(|a|, |b|) over elements with |a - b| above the absolute tolerance.</summary>
        public double MaxRelOverAbsTol;
        /// <summary>Elements failing both tolerances.</summary>
        public int Violations;
        public int FirstViolation = -1;
        public double FirstViolationExpected, FirstViolationActual;

        public bool Pass => Violations == 0;
    }

    /// <summary>Result of comparing one configuration.</summary>
    public sealed class GoldenDiff
    {
        public string Key;
        public readonly List<string> CounterMismatches = new List<string>();
        public readonly List<ArrayDiff> Arrays = new List<ArrayDiff>();

        public bool Pass
        {
            get
            {
                if (CounterMismatches.Count > 0) return false;
                for (int i = 0; i < Arrays.Count; i++)
                    if (!Arrays[i].Pass) return false;
                return true;
            }
        }

        public ArrayDiff Get(string name)
        {
            for (int i = 0; i < Arrays.Count; i++)
                if (Arrays[i].Name == name) return Arrays[i];
            return null;
        }

        public string Summary()
        {
            var sb = new StringBuilder();
            sb.Append(Key).Append(Pass ? " PASS" : " FAIL");
            for (int i = 0; i < CounterMismatches.Count; i++) sb.Append(" [").Append(CounterMismatches[i]).Append(']');
            for (int i = 0; i < Arrays.Count; i++)
            {
                ArrayDiff a = Arrays[i];
                sb.Append(' ').Append(a.Name).Append('=').Append(a.MaxAbs.ToString("0.###E+0", CultureInfo.InvariantCulture));
                if (!a.Pass)
                    sb.Append("(!").Append(a.Violations).Append(" first@").Append(a.FirstViolation).Append(' ')
                        .Append(a.FirstViolationExpected.ToString("R", CultureInfo.InvariantCulture)).Append("->")
                        .Append(a.FirstViolationActual.ToString("R", CultureInfo.InvariantCulture)).Append(')');
            }

            return sb.ToString();
        }
    }

    public static class GoldenCompare
    {
        public const double AbsTol = 1e-3;
        public const double RelTol = 1e-4;

        public static readonly string[] ArrayNames = { "P", "T", "DAT", "CB", "GM", "PE", "LEAF", "XLEAF", "PADS" };

        /// <summary>|Δ| ≤ 1e-3 or relative ≤ 1e-4.</summary>
        public static bool Within(double expected, double actual)
        {
            if (double.IsNaN(expected) || double.IsNaN(actual)) return double.IsNaN(expected) && double.IsNaN(actual);
            double d = Math.Abs(expected - actual);
            if (d <= AbsTol) return true;
            double m = Math.Max(Math.Abs(expected), Math.Abs(actual));
            return d <= RelTol * m;
        }

        public static GoldenDiff Compare(GoldenData golden, SketchPlan plan, SketchScene actual)
        {
            SketchScene e = golden.Scene;
            var r = new GoldenDiff { Key = golden.Key };
            Counter(r, "nr", e.Nr, actual.Nr);
            Counter(r, "ng", e.Ng, actual.Ng);
            Counter(r, "nb", e.Nb, actual.Nb);
            Counter(r, "nl", e.Nl, actual.Nl);
            Counter(r, "nx", e.Nx, actual.Nx);
            Counter(r, "nc", e.Nc, actual.Nc);
            Counter(r, "np", e.Np, actual.Np);
            if (!Within(e.L, actual.L)) r.CounterMismatches.Add("L " + e.L + "!=" + actual.L);
            if (!Within(e.R, actual.R)) r.CounterMismatches.Add("R " + e.R + "!=" + actual.R);

            var p = new ArrayDiff { Name = "P" };
            Range(p, golden.P, plan.P, 0, golden.P.Length);
            r.Arrays.Add(p);
            var t = new ArrayDiff { Name = "T" };
            Range(t, golden.T, plan.T, 0, golden.T.Length);
            r.Arrays.Add(t);

            // DAT: live regions only (the sketch never clears slots beyond nr / nc / np).
            int nr = Math.Min(e.Nr, actual.Nr), nc = Math.Min(e.Nc, actual.Nc), np = Math.Min(e.Np, actual.Np);
            var dat = new ArrayDiff { Name = "DAT" };
            RangeF(dat, e.Dat, actual.Dat, 0, (SketchLayout.NT + 1) * 4);
            RangeF(dat, e.Dat, actual.Dat, SketchLayout.OB * 4, SketchLayout.NTC * 4);
            RangeF(dat, e.Dat, actual.Dat, SketchLayout.ORB * 4, nr * SketchLayout.RC * 4);
            RangeF(dat, e.Dat, actual.Dat, SketchLayout.OSG * 4, SketchLayout.NSG * 4);
            RangeF(dat, e.Dat, actual.Dat, SketchLayout.ORW * 4, nr * 4);
            RangeF(dat, e.Dat, actual.Dat, SketchLayout.OR * 4, nr * SketchLayout.RP * 4);
            RangeF(dat, e.Dat, actual.Dat, SketchLayout.OBR * 4, nc * 5 * 4);
            RangeF(dat, e.Dat, actual.Dat, SketchLayout.OPF * 4, np * 7 * 4);
            r.Arrays.Add(dat);

            r.Arrays.Add(Flat("CB", e.Cb, actual.Cb, nc * 4));
            r.Arrays.Add(Flat("GM", e.Gm, actual.Gm, Math.Min(e.Ng, actual.Ng) * 8));
            r.Arrays.Add(Flat("PE", e.Pe, actual.Pe, np * 8));
            r.Arrays.Add(Flat("LEAF", e.Leaf, actual.Leaf, Math.Min(e.Nl, actual.Nl) * 16));
            r.Arrays.Add(Flat("XLEAF", e.XLeaf, actual.XLeaf, Math.Min(e.Nx, actual.Nx) * 16));

            var pads = new ArrayDiff { Name = "PADS" };
            for (int i = 0; i < np; i++)
            {
                Pad a = e.Pads[i], b = actual.Pads[i];
                int o = i * 16;
                One(pads, o, a.C.X, b.C.X);
                One(pads, o + 1, a.C.Y, b.C.Y);
                One(pads, o + 2, a.C.Z, b.C.Z);
                One(pads, o + 3, a.A, b.A);
                One(pads, o + 4, a.B, b.B);
                One(pads, o + 5, a.Yb, b.Yb);
                One(pads, o + 6, a.Hx.X, b.Hx.X);
                One(pads, o + 7, a.Hx.Z, b.Hx.Z);
                One(pads, o + 8, a.Pp.X, b.Pp.X);
                One(pads, o + 9, a.Pp.Z, b.Pp.Z);
                One(pads, o + 10, a.Gp, b.Gp);
                One(pads, o + 11, a.Key, b.Key);
                One(pads, o + 12, a.Dome ? 1 : 0, b.Dome ? 1 : 0);
            }

            r.Arrays.Add(pads);
            return r;
        }

        private static void Counter(GoldenDiff r, string name, int e, int a)
        {
            if (e != a) r.CounterMismatches.Add(name + " " + e + "!=" + a);
        }

        private static ArrayDiff Flat(string name, float[] e, float[] a, int len)
        {
            var d = new ArrayDiff { Name = name };
            RangeF(d, e, a, 0, len);
            return d;
        }

        private static void Range(ArrayDiff d, double[] e, double[] a, int start, int len)
        {
            for (int i = start; i < start + len; i++) One(d, i, e[i], a[i]);
        }

        private static void RangeF(ArrayDiff d, float[] e, float[] a, int start, int len)
        {
            for (int i = start; i < start + len; i++) One(d, i, e[i], a[i]);
        }

        private static void One(ArrayDiff d, int i, double e, double a)
        {
            d.Compared++;
            double diff = Math.Abs(e - a);
            if (double.IsNaN(diff)) diff = double.IsNaN(e) && double.IsNaN(a) ? 0 : double.PositiveInfinity;
            if (d.MaxAbsIndex < 0 || diff > d.MaxAbs)
            {
                d.MaxAbs = diff;
                d.MaxAbsIndex = i;
            }

            if (diff > AbsTol)
            {
                double rel = diff / Math.Max(Math.Abs(e), Math.Abs(a));
                if (rel > d.MaxRelOverAbsTol) d.MaxRelOverAbsTol = rel;
            }

            if (!Within(e, a))
            {
                if (d.Violations == 0)
                {
                    d.FirstViolation = i;
                    d.FirstViolationExpected = e;
                    d.FirstViolationActual = a;
                }

                d.Violations++;
            }
        }
    }
}
