// PA-102X-S spike: the generator -> renderer contract (JS scene() result + global arrays of sketch_v11).
using System;

namespace PocketArbor.Spike.TreeGen
{
    /// <summary>Foliage pad (JS PADS entry, line 102). All values in double, as in JS.</summary>
    public struct Pad
    {
        /// <summary>Pad centre.</summary>
        public D3 C;
        /// <summary>Half-extent along Hx.</summary>
        public double A;
        /// <summary>Half-extent along Pp and height scale.</summary>
        public double B;
        /// <summary>Pad base height (leaves above it are lit as "top").</summary>
        public double Yb;
        /// <summary>Horizontal heading (unit, y = 0).</summary>
        public D3 Hx;
        /// <summary>Horizontal perpendicular (Hx.z, 0, -Hx.x).</summary>
        public D3 Pp;
        /// <summary>Apex dome pad.</summary>
        public bool Dome;
        /// <summary>Branch index, 99 for the apex dome.</summary>
        public int Key;
        /// <summary>Growth 0..1.</summary>
        public double Gp;
    }

    /// <summary>
    /// Scene produced by SketchGenerator.Scene or loaded by <see cref="GoldenLoader"/>.
    /// Arrays are allocated at full capacity (see <see cref="SketchLayout"/>); only the prefixes given by the
    /// counters are live. Units: sketch frame pixels, right-handed (x right, y up, z towards viewer).
    /// </summary>
    public sealed class SketchScene
    {
        // --- identity ---
        public string SpeciesKey;
        public string Style;
        public int Seed;
        /// <summary>Age 0..1.</summary>
        public double T;

        // --- parameters ---
        /// <summary>Species parameters (bark, bump, moss, tr, fd, stiff, id, leaf colours...).</summary>
        public SpeciesDef Species;
        /// <summary>Pot actually used (bunjin overrides the species pot).</summary>
        public PotDef Pot;
        /// <summary>Light direction LL used by padHit (unit, towards the light).</summary>
        public D3 LightDir = SketchLayout.DefaultLight;

        // --- JS scene() result ---
        /// <summary>Trunk length Lt(t).</summary>
        public double L;
        /// <summary>Trunk base radius Rt(t) (uRb).</summary>
        public double R;
        /// <summary>Live roots (uNR).</summary>
        public int Nr;
        /// <summary>Live branch groups in Gm (uNG).</summary>
        public int Ng;
        /// <summary>Primary branches drawn.</summary>
        public int Nb;
        /// <summary>Leaves in Leaf.</summary>
        public int Nl;
        /// <summary>Extra quads (blossom / candles) in XLeaf.</summary>
        public int Nx;
        /// <summary>Live chunks in Cb (and DAT[OBR ..]).</summary>
        public int Nc;
        /// <summary>Live pads (uNP).</summary>
        public int Np;

        // --- arrays (float, exactly what the sketch keeps in Float32Array) ---
        /// <summary>DAT: TW*4 floats (data texture), see <see cref="SketchLayout"/>.</summary>
        public readonly float[] Dat = new float[SketchLayout.TW * 4];
        /// <summary>CB: MAXCH*4 floats, live Nc*4.</summary>
        public readonly float[] Cb = new float[SketchLayout.MAXCH * 4];
        /// <summary>GM: MAXG*8 floats, live Ng*8.</summary>
        public readonly float[] Gm = new float[SketchLayout.MAXG * 8];
        /// <summary>PE: MAXPAD*8 floats, live Np*8.</summary>
        public readonly float[] Pe = new float[SketchLayout.MAXPAD * 8];
        /// <summary>LEAF: MAXLEAF*16 floats, live Nl*16.</summary>
        public readonly float[] Leaf = new float[SketchLayout.MAXLEAF * 16];
        /// <summary>XLEAF: MAXX*16 floats, live Nx*16.</summary>
        public readonly float[] XLeaf = new float[SketchLayout.MAXX * 16];
        /// <summary>PADS: MAXPAD, live Np.</summary>
        public readonly Pad[] Pads = new Pad[SketchLayout.MAXPAD];

        // --- derived uniforms (JS draw()) ---
        /// <summary>uAge = min(1, t*1.25).</summary>
        public double Age => Math.Min(1, T * 1.25);
        /// <summary>uKr = 0.28*R + 0.5.</summary>
        public double Kr => 0.28 * R + 0.5;

        /// <summary>Golden key of the current state; the string is cached and rebuilt only when the key changes.</summary>
        public string Key
        {
            get
            {
                int t100 = (int)Math.Floor(T * 100 + 0.5);
                if (_key == null || t100 != _keyT100 || Seed != _keySeed || !string.Equals(SpeciesKey, _keySpecies, StringComparison.Ordinal)
                    || !string.Equals(Style, _keyStyle, StringComparison.Ordinal))
                {
                    _key = MakeKey(SpeciesKey, Style, Seed, T);
                    _keyT100 = t100;
                    _keySeed = Seed;
                    _keySpecies = SpeciesKey;
                    _keyStyle = Style;
                }

                return _key;
            }
        }

        private string _key, _keySpecies, _keyStyle;
        private int _keyT100, _keySeed;

        /// <summary>Generator scratch state (C5: allocated once, reused by every SketchGenerator.Scene call on this scene).</summary>
        internal SketchGenerator.Ctx Work;

        /// <summary>Golden key: species_style_s{seed}_t{round(t*100)}.</summary>
        public static string MakeKey(string species, string style, int seed, double t)
        {
            return species + "_" + style + "_s" + seed + "_t" + (int)Math.Floor(t * 100 + 0.5);
        }
    }
}
