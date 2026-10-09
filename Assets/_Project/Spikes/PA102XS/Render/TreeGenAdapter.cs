using System;
using System.Collections.Generic;
using PocketArbor.Spike.TreeGen;

namespace PocketArbor.Spike.Render
{
    /// <summary>
    /// Bridges the C# generator port (TreeGen.SketchScene, GP) to the render frame. The array layout is the same
    /// contract, so this is a plain copy of the live prefixes + counters. Plans are cached per (style, seed).
    /// </summary>
    public static class TreeGenAdapter
    {
        private static readonly Dictionary<string, SketchPlan> Plans = new Dictionary<string, SketchPlan>();
        private static SketchScene _reuse;

        public static bool Generate(string species, string style, int seed, float t, SketchFrame into)
        {
            string pk = style + "#" + seed;
            if (!Plans.TryGetValue(pk, out SketchPlan plan))
            {
                plan = SketchGenerator.Build(style, seed);
                Plans[pk] = plan;
            }
            _reuse = SketchGenerator.Scene(plan, species, t, SketchLayout.DefaultLight, _reuse);
            Copy(_reuse, into);
            return true;
        }

        public static void Copy(SketchScene s, SketchFrame f)
        {
            f.Key = s.Key;
            f.Species = s.SpeciesKey;
            f.Style = s.Style;
            f.Seed = s.Seed;
            f.T = (float)s.T;
            f.L = (float)s.L;
            f.R = (float)s.R;
            f.Nr = s.Nr; f.Ng = s.Ng; f.Nb = s.Nb; f.Nl = s.Nl; f.Nx = s.Nx; f.Nc = s.Nc; f.Np = s.Np;
            Array.Copy(s.Dat, f.Dat, Math.Min(s.Dat.Length, f.Dat.Length));
            Array.Clear(f.Cb, 0, f.Cb.Length);
            Array.Copy(s.Cb, f.Cb, Math.Min(s.Nc * 4, f.Cb.Length));
            Array.Clear(f.Gm, 0, f.Gm.Length);
            Array.Copy(s.Gm, f.Gm, Math.Min(s.Ng * 8, f.Gm.Length));
            Array.Clear(f.Pe, 0, f.Pe.Length);
            Array.Copy(s.Pe, f.Pe, Math.Min(s.Np * 8, f.Pe.Length));
            Array.Copy(s.Leaf, f.Leaf, Math.Min(s.Nl * 16, f.Leaf.Length));
            f.LightDir = new UnityEngine.Vector3((float)s.LightDir.X, (float)s.LightDir.Y, (float)s.LightDir.Z);
        }
    }
}
