// PA-102X-S spike: math helpers of sketch_v11 with JS-identical semantics (JS lines 35-37, 46).
using System;

namespace PocketArbor.Spike.TreeGen
{
    /// <summary>Double-precision 3-vector (the sketch keeps all intermediate maths in JS numbers = double).</summary>
    public struct D3
    {
        public double X, Y, Z;

        public D3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public override string ToString()
        {
            return "(" + X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", "
                + Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", "
                + Z.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ")";
        }
    }

    /// <summary>Spline control point: position + radius (JS 4-element arrays).</summary>
    public struct D4
    {
        public double X, Y, Z, W;

        public D4(double x, double y, double z, double w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public D4(D3 p, double w)
        {
            X = p.X;
            Y = p.Y;
            Z = p.Z;
            W = w;
        }

        public D3 Xyz => new D3(X, Y, Z);
    }

    /// <summary>JS helpers with bit-compatible semantics.</summary>
    public static class SketchMath
    {
        /// <summary>JS cl(x,a,b).</summary>
        public static double Cl(double x, double a, double b)
        {
            return x < a ? a : x > b ? b : x;
        }

        /// <summary>JS sm(x): smoothstep on [0,1].</summary>
        public static double Sm(double x)
        {
            x = Cl(x, 0, 1);
            return x * x * (3 - 2 * x);
        }

        /// <summary>
        /// JS Math.round: half rounds up (towards +inf), unlike C# banker's Math.Round.
        /// Written via the exact fractional part, so 0.49999999999999994 -> 0 as in JS (floor(x+0.5) would give 1).
        /// </summary>
        public static double JsRound(double x)
        {
            double r = Math.Floor(x);
            return x - r >= 0.5 ? r + 1 : r;
        }

        /// <summary>JS Math.hypot(a,b) (differs from V8 by at most a few ulp).</summary>
        public static double Hypot(double a, double b)
        {
            return Math.Sqrt(a * a + b * b);
        }

        /// <summary>JS Math.hypot(a,b,c).</summary>
        public static double Hypot(double a, double b, double c)
        {
            return Math.Sqrt(a * a + b * b + c * c);
        }

        /// <summary>JS add(a,b,k) = a + b*k (k is evaluated by the caller first, as in JS).</summary>
        public static D3 Add(D3 a, D3 b, double k)
        {
            return new D3(a.X + b.X * k, a.Y + b.Y * k, a.Z + b.Z * k);
        }

        /// <summary>JS add() applied to a 4-element point: only xyz are used.</summary>
        public static D3 Add(D4 a, D3 b, double k)
        {
            return new D3(a.X + b.X * k, a.Y + b.Y * k, a.Z + b.Z * k);
        }

        /// <summary>JS nrm(v) = v / (hypot(v) || 1).</summary>
        public static D3 Nrm(D3 v)
        {
            double l = Hypot(v.X, v.Y, v.Z);
            if (l == 0 || double.IsNaN(l)) l = 1;
            return new D3(v.X / l, v.Y / l, v.Z / l);
        }

        /// <summary>JS cr(a,b): cross product.</summary>
        public static D3 Cr(D3 a, D3 b)
        {
            return new D3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        }

        /// <summary>JS dt(a,b): dot product.</summary>
        public static double Dt(D3 a, D3 b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }

        /// <summary>
        /// JS hsh(a,b,c,d). In JS the weighted sum is a double (exact below 2^53 for the sketch's inputs),
        /// then ToInt32; here the sum is exact in long and truncated to the low 32 bits — identical result.
        /// </summary>
        public static double Hsh(long a, long b, long c, long d)
        {
            unchecked
            {
                long s = a * 374761393L + b * 668265263L + c * 2246822519L + d * 3266489917L;
                int h = (int)s;
                h = (h ^ (int)((uint)h >> 13)) * 1274126177;
                h ^= (int)((uint)h >> 16);
                return (uint)h / 4294967296.0;
            }
        }

        /// <summary>JS Lt(t): trunk length at age t.</summary>
        public static double Lt(double t)
        {
            return SketchLayout.LMAX * (0.15 + 0.85 * (1 - Math.Pow(1 - t, 1.7)));
        }

        /// <summary>JS tOfL(l): age at which the trunk reaches length l.</summary>
        public static double TOfL(double l)
        {
            double f = l / SketchLayout.LMAX;
            if (f <= 0.15) return 0;
            double v = 1 - (f - 0.15) / 0.85;
            return v <= 0 ? 1 : Cl(1 - Math.Pow(v, 1 / 1.7), 0, 1);
        }
    }

    /// <summary>JS rng(a): mulberry32-like generator, int32 wrap-around arithmetic.</summary>
    public sealed class SketchRng
    {
        private int _a;

        public SketchRng(int seed)
        {
            _a = seed;
        }

        public double Next()
        {
            unchecked
            {
                _a = _a + 0x6D2B79F5;
                int a = _a;
                int t = (a ^ (int)((uint)a >> 15)) * (1 | a);
                t = (t + (t ^ (int)((uint)t >> 7)) * (61 | t)) ^ t;
                return (uint)(t ^ (int)((uint)t >> 14)) / 4294967296.0;
            }
        }

        /// <summary>JS R(a,b) = a + (b-a)*r().</summary>
        public double Range(double a, double b)
        {
            return a + (b - a) * Next();
        }
    }

    /// <summary>JS n1(r): 1D value noise over 256 lattice values drawn from the rng.</summary>
    public sealed class SketchNoise
    {
        private readonly double[] _g = new double[256];

        public SketchNoise(SketchRng r)
        {
            for (int i = 0; i < 256; i++) _g[i] = r.Next() * 2 - 1;
        }

        public double this[double x]
        {
            get
            {
                double fi = Math.Floor(x);
                double f = x - fi;
                double u = f * f * (3 - 2 * f);
                long i = (long)fi;
                double a = _g[(int)((i % 256 + 256) % 256)];
                double b = _g[(int)(((i + 1) % 256 + 256) % 256)];
                return a + (b - a) * u;
            }
        }
    }
}
