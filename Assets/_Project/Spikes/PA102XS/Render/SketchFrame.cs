using System;
using UnityEngine;

namespace PocketArbor.Spike.Render
{
    /// <summary>
    /// Constants of sketch_v11: camera, DAT layout, limits. Sketch space: x right, y up, z toward the viewer
    /// (right-handed). Unity world = sketch with z negated (the tree root has localScale z = -1).
    /// </summary>
    public static class SketchConst
    {
        public const float FrameW = 680f, FrameH = 480f, OX = 340f, GY = 400f;
        public const float SE = 0.28f, CE = 0.96f; // CE = sqrt(1 - SE^2) exactly
        public const float T0 = -260f, T1 = 480f;
        public const int TW = 1024, NT = 96, NTC = 24, RP = 13, MAXR = 7, CP = 7, RC = 3, NSG = 6, MAXG = 9, MAXCPG = 14, MAXCH = 124;
        public const int OB = NT + 1;                         // 97
        public const int ORB = OB + NTC;                      // 121
        public const int OSG = ORB + MAXR * RC;               // 142
        public const int ORW = OSG + NSG;                     // 148
        public const int NB = NTC + MAXR * RC + NSG + MAXR;   // 58
        public const int OR = OB + NB;                        // 155
        public const int OBR = OR + MAXR * RP;                // 246
        public const int MAXPAD = 9;
        public const int OPF = OBR + MAXCH * 5;               // 866
        public const int MAXLEAF = 24000;

        // Row 1 of the SDF data texture (row 0 = DAT).
        public const int RowCb = 0, RowGm = 128, RowPe = 160;
    }

    /// <summary>
    /// One tree state in the sketch_v11 array layout — the render-side mirror of TreeGen.SketchScene
    /// (same arrays, same counters). Filled either from golden files or from the C# generator port.
    /// </summary>
    public sealed class SketchFrame
    {
        /// <summary>Configuration key (allocates; for logs/tools, not per frame).</summary>
        public string Key => MakeKey(Species, Style, Seed, T);
        public string Species = "momiji";
        public string Style = "moyogi";
        public int Seed;
        public float T;

        public float L, R;
        public int Nr, Ng, Nb, Nl, Nx, Nc, Np;

        public readonly float[] Dat = new float[SketchConst.TW * 4];
        public readonly float[] Cb = new float[SketchConst.MAXCH * 4];
        public readonly float[] Gm = new float[SketchConst.MAXG * 8];
        public readonly float[] Pe = new float[SketchConst.MAXPAD * 8];
        public readonly float[] Leaf = new float[SketchConst.MAXLEAF * 16];

        // Environment (sketch defaults: July, 10:00, no rain/wind).
        public Vector3 LightDir = new Vector3(-0.4482191f, 0.7323608f, 0.5125888f);
        public Vector3 SunColor = new Vector3(1f, 0.93f, 0.82f);
        public Vector3 Ambient = new Vector3(0.85f, 0.9f, 1f);
        public float SunIntensity = 1f; // uSI
        public float Sun = 1f;          // uSun (overcast factor)
        public float Foliage = 1f;      // ENV.fol

        public float Kr => 0.28f * R + 0.5f;
        public float Age => Mathf.Min(1f, T * 1.25f);

        public void CopyFrom(SketchFrame o)
        {
            Species = o.Species; Style = o.Style; Seed = o.Seed; T = o.T; L = o.L; R = o.R;
            Nr = o.Nr; Ng = o.Ng; Nb = o.Nb; Nl = o.Nl; Nx = o.Nx; Nc = o.Nc; Np = o.Np;
            System.Array.Copy(o.Dat, Dat, Dat.Length);
            System.Array.Copy(o.Cb, Cb, Cb.Length);
            System.Array.Copy(o.Gm, Gm, Gm.Length);
            System.Array.Copy(o.Pe, Pe, Pe.Length);
            System.Array.Copy(o.Leaf, Leaf, Math.Min(Leaf.Length, o.Nl * 16));
            LightDir = o.LightDir; SunColor = o.SunColor; Ambient = o.Ambient; SunIntensity = o.SunIntensity; Sun = o.Sun; Foliage = o.Foliage;
        }

        /// <summary>
        /// Cut simulation in the SDF arrays: drops branch group <paramref name="g"/> from GM (its chunks stay in CB, unreferenced)
        /// and, when pads map 1:1 to primary groups (all shown, apex dome last), its pad from PE.
        /// </summary>
        public void RemoveGroup(int g)
        {
            if (g < 0 || g >= Ng) return;
            bool padsAligned = Np == Nb + 1 && g < Nb;
            System.Array.Copy(Gm, (g + 1) * 8, Gm, g * 8, (Ng - g - 1) * 8);
            Ng--;
            System.Array.Clear(Gm, Ng * 8, 8);
            if (padsAligned)
            {
                System.Array.Copy(Pe, (g + 1) * 8, Pe, g * 8, (Np - g - 1) * 8);
                Np--;
                System.Array.Clear(Pe, Np * 8, 8);
            }
            if (g < Nb) Nb--;
        }

        public static string MakeKey(string species, string style, int seed, float t)
        {
            return species + "_" + style + "_s" + seed + "_t" + Mathf.RoundToInt(t * 100f);
        }
    }

    /// <summary>Per-species render constants of the sketch (SP table) that are not part of the arrays.</summary>
    public sealed class SpeciesLook
    {
        public int Id;
        public float Fd, Tr, Bump, Moss, Stiff = 1f;
        public int PotShape;     // 0 rounded rectangle, 1 ellipse
        public float PotHw, PotHd, PotDep;
        public Vector3 PotColor;
        public int PotGlaze;     // uPotT: 0 tokoname, 1 kuro, 2 celadon

        public string PotKey => PotShape + "_" + PotHw + "_" + PotHd + "_" + PotDep;

        public bool SamePot(SpeciesLook o)
        {
            return o != null && o.PotShape == PotShape && o.PotHw == PotHw && o.PotHd == PotHd && o.PotDep == PotDep;
        }

        private static readonly SpeciesLook[] Cache = new SpeciesLook[6];

        /// <summary>Cached per (species, bunjin pot) — allocation-free after the first call. Do not mutate.</summary>
        public static SpeciesLook For(string species, string style)
        {
            int id = species == "yamazakura" ? 1 : species == "kuromatsu" ? 2 : 0;
            int slot = id * 2 + (style == "bunjin" ? 1 : 0);
            return Cache[slot] ?? (Cache[slot] = Create(species, style));
        }

        private static SpeciesLook Create(string species, string style)
        {
            SpeciesLook s;
            switch (species)
            {
                case "yamazakura":
                    s = new SpeciesLook { Id = 1, Stiff = .9f, Fd = .04f, Tr = 1.5f, Bump = .9f, Moss = .5f, PotShape = 1, PotHw = 136, PotHd = 66, PotDep = 38, PotColor = new Vector3(.11f, .16f, .12f), PotGlaze = 2 };
                    break;
                case "kuromatsu":
                    s = new SpeciesLook { Id = 2, Stiff = .35f, Fd = .075f, Tr = .6f, Bump = 2.6f, Moss = .6f, PotShape = 0, PotHw = 140, PotHd = 64, PotDep = 48, PotColor = new Vector3(.032f, .029f, .026f), PotGlaze = 1 };
                    break;
                default:
                    s = new SpeciesLook { Id = 0, Stiff = 1f, Fd = .05f, Tr = 1.9f, Bump = .7f, Moss = .85f, PotShape = 0, PotHw = 140, PotHd = 62, PotDep = 40, PotColor = new Vector3(.17f, .08f, .045f), PotGlaze = 0 };
                    break;
            }
            if (style == "bunjin")
            {
                s.PotShape = 1; s.PotHw = 98; s.PotHd = 52; s.PotDep = 30;
            }
            return s;
        }
    }
}
