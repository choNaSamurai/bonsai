// PA-102X-S spike: species table SP and setSpecies() of sketch_v11 (JS lines 42-44, 472).
using System;

namespace PocketArbor.Spike.TreeGen
{
    /// <summary>Pot parameters (JS SP[*].pot). Shader uniforms: uPot = (Hw, Hd, Shape, Dep), uPotC = Color, uPotT = Type.</summary>
    public sealed class PotDef
    {
        /// <summary>0 = rectangular, 1 = oval.</summary>
        public readonly int Shape;
        public readonly double Hw;
        public readonly double Hd;
        public readonly double Dep;
        public readonly D3 Color;
        /// <summary>Glaze type: 0 = tokoname, 1 = kuro (black), 2 = celadon.</summary>
        public readonly int Type;

        public PotDef(int shape, double hw, double hd, double dep, D3 color, int type)
        {
            Shape = shape;
            Hw = hw;
            Hd = hd;
            Dep = dep;
            Color = color;
            Type = type;
        }
    }

    /// <summary>Species parameters (JS SP entry). Fields keep the sketch names.</summary>
    public sealed class SpeciesDef
    {
        public readonly string Key;
        /// <summary>0 = momiji, 1 = yamazakura, 2 = kuromatsu (uSpB uniform).</summary>
        public readonly int Id;
        public readonly double Len;
        public readonly double Wid;
        /// <summary>Leaves per branch site.</summary>
        public readonly int Per;
        /// <summary>Pad leaf density.</summary>
        public readonly double Fill;
        /// <summary>Leaves per pad sample.</summary>
        public readonly int Fper;
        public readonly bool Hang;
        /// <summary>Leaf translucency (uTr).</summary>
        public readonly double Tr;
        /// <summary>Foliage density for crown shadow (uFD = Fd * fol).</summary>
        public readonly double Fd;
        public readonly D3 Base;
        public readonly D3 Nw;
        public readonly double NewMix;
        public readonly D3 Bark;
        public readonly bool Tips;
        public readonly double Stiff;
        public readonly double Bump;
        public readonly double Moss;
        public readonly PotDef Pot;

        public SpeciesDef(string key, int id, double len, double wid, int per, double fill, int fper, bool hang,
            double tr, double fd, D3 @base, D3 nw, double newMix, D3 bark, bool tips, double stiff, double bump,
            double moss, PotDef pot)
        {
            Key = key;
            Id = id;
            Len = len;
            Wid = wid;
            Per = per;
            Fill = fill;
            Fper = fper;
            Hang = hang;
            Tr = tr;
            Fd = fd;
            Base = @base;
            Nw = nw;
            NewMix = newMix;
            Bark = bark;
            Tips = tips;
            Stiff = stiff;
            Bump = bump;
            Moss = moss;
            Pot = pot;
        }

        /// <summary>Copy with another pot (JS setSpecies for bunjin).</summary>
        public SpeciesDef WithPot(PotDef pot)
        {
            return new SpeciesDef(Key, Id, Len, Wid, Per, Fill, Fper, Hang, Tr, Fd, Base, Nw, NewMix, Bark, Tips,
                Stiff, Bump, Moss, pot);
        }
    }

    /// <summary>JS SP table + setSpecies().</summary>
    public static class SketchSpecies
    {
        public static readonly SpeciesDef Momiji = new SpeciesDef("momiji", 0, 8.5, 8.5, 3, 10, 1, false, 1.9, .05,
            new D3(.085, .2, .03), new D3(.22, .06, .02), .55, new D3(.075, .07, .062), false, 1, .7, .85,
            new PotDef(0, 140, 62, 40, new D3(.17, .08, .045), 0));

        public static readonly SpeciesDef Yamazakura = new SpeciesDef("yamazakura", 1, 12, 5.6, 3, 3.6, 1, true, 1.5,
            .04, new D3(.06, .14, .03), new D3(.2, .075, .03), .75, new D3(.078, .054, .044), false, .9, .9, .5,
            new PotDef(1, 136, 66, 38, new D3(.11, .16, .12), 2));

        public static readonly SpeciesDef Kuromatsu = new SpeciesDef("kuromatsu", 2, 14, 1.0, 26, 0.5, 14, false, .6,
            .075, new D3(.022, .055, .03), new D3(.07, .12, .045), .35, new D3(.058, .054, .05), true, .35, 2.6, .6,
            new PotDef(0, 140, 64, 48, new D3(.032, .029, .026), 1));

        public static readonly string[] SpeciesKeys = { "momiji", "yamazakura", "kuromatsu" };
        public static readonly string[] StyleKeys = { "moyogi", "chokkan", "shakan", "bunjin", "fukinagashi" };

        public static SpeciesDef Get(string key)
        {
            switch (key)
            {
                case "momiji": return Momiji;
                case "yamazakura": return Yamazakura;
                case "kuromatsu": return Kuromatsu;
                default: throw new ArgumentException("Unknown species: " + key, nameof(key));
            }
        }

        // bunjin variants are built once (ForStyle is called every frame by the Chronicle — C5: no allocations)
        private static readonly SpeciesDef MomijiBunjin = BunjinPot(Momiji);
        private static readonly SpeciesDef YamazakuraBunjin = BunjinPot(Yamazakura);
        private static readonly SpeciesDef KuromatsuBunjin = BunjinPot(Kuromatsu);

        private static SpeciesDef BunjinPot(SpeciesDef b)
        {
            return b.WithPot(new PotDef(1, 98, 52, 30, b.Pot.Color, b.Pot.Type));
        }

        /// <summary>JS setSpecies(): bunjin swaps the pot for a small oval {shape:1, hw:98, hd:52, dep:30}. Cached instances.</summary>
        public static SpeciesDef ForStyle(string speciesKey, string style)
        {
            SpeciesDef b = Get(speciesKey);
            if (style != "bunjin") return b;
            return b.Id == 0 ? MomijiBunjin : b.Id == 1 ? YamazakuraBunjin : KuromatsuBunjin;
        }
    }
}
