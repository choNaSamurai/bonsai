// PA-102X-S spike: constants of sketch_v11 (JS lines 34, 45, 88-89, 149).
// Units are frame pixels of the 680x480 sketch (scale 1). Axes: x right, y up, z towards the viewer (right-handed).
namespace PocketArbor.Spike.TreeGen
{
    /// <summary>
    /// Array layout and camera constants of the sketch. This is the contract between the generator and the renderer.
    /// <para>DAT (TW float4 entries, x,y,z,r):</para>
    /// <list type="bullet">
    /// <item>[0 .. NT]               trunk spine samples (NT+1)</item>
    /// <item>[OB .. OB+NTC)          trunk chunk bounds (cx,cy,cz,rad), chunk c covers samples c*4 .. c*4+4</item>
    /// <item>[ORB .. ORB+MAXR*RC)    root chunk bounds, RC per root, chunk c covers root samples c*4 .. c*4+4</item>
    /// <item>[OSG .. OSG+NSG)        trunk segment-group bounds, group g covers samples g*16 .. g*16+16</item>
    /// <item>[ORW .. ORW+MAXR)       whole-root bounds (RP samples)</item>
    /// <item>[OR .. OR+MAXR*RP)      root spline samples, RP per root (only the first Nr roots are live)</item>
    /// <item>[OBR .. OBR+MAXCH*5)    branch chunks, 5 samples each (only the first Nc chunks are live)</item>
    /// <item>[OPF .. OPF+MAXPAD*7)   foliage-pad blobs, 7 per pad: centre + 6 around (only the first Np pads are live)</item>
    /// </list>
    /// Entries beyond the live counters are NOT cleared between scenes (same as the sketch): never read them.
    /// <para>CB (MAXCH float4): cx,cy,cz, w. |w| = rad + 1000*childCount; w &lt; 0 marks a "limb" chunk (JS putChunk(limb=true)).</para>
    /// <para>GM (MAXG x 8): cx,cy,cz,rad, kg, c0 (first chunk), cn (chunk count), ks.</para>
    /// <para>PE (MAXPAD x 8): cx, cy+0.2b, cz, 0.95a, hx.x, hx.z, 0.62b, 0.95b.</para>
    /// <para>LEAF / XLEAF (16 floats): P.xyz, D.xyz, N.xyz, len, wid, colour.rgb, ex, 0.</para>
    /// </summary>
    public static class SketchLayout
    {
        // --- DAT / uniform layout (JS line 88) ---
        public const int TW = 1024;
        public const int NT = 96;
        public const int NTC = 24;
        public const int RP = 13;
        public const int MAXR = 7;
        public const int CP = 7;
        public const int RC = 3;
        public const int NSG = 6;
        public const int MAXG = 9;
        public const int MAXCPG = 14;
        public const int MAXCH = 124;
        public const int OB = NT + 1;                         // 97
        public const int ORB = OB + NTC;                      // 121
        public const int OSG = ORB + MAXR * RC;               // 142
        public const int ORW = OSG + NSG;                     // 148
        public const int NB = NTC + MAXR * RC + NSG + MAXR;   // 58 (uB uniform: DAT[OB .. OB+NB))
        public const int OR = OB + NB;                        // 155
        public const int OBR = OR + MAXR * RP;                // 246
        public const int MAXPAD = 9;
        public const int OPF = OBR + MAXCH * 5;               // 866

        public const int MAXLEAF = 24000;
        public const int MAXX = 6000;
        public const int LeafStride = 16;
        public const int GmStride = 8;
        public const int PeStride = 8;

        // --- camera / frame (JS line 34, 45) ---
        public const double W = 680;
        public const double H = 480;
        public const double OX = W / 2;
        public const double GY = 400;
        public const double LMAX = 290;
        public const int N = 600;
        public const double DS = LMAX / N;
        public const double SE = 0.28;
        public static readonly double CE = System.Math.Sqrt(1 - SE * SE);
        public const double T0 = -260;
        public const double T1 = 480;

        /// <summary>View vector used for leaf orientation (JS LV = [0, SE, CE]).</summary>
        public static readonly D3 LV = new D3(0, SE, CE);

        /// <summary>Default light (golden env.LL: July, 10:00).</summary>
        public static readonly D3 DefaultLight = new D3(-0.4482191045323742, 0.7323608444722203, 0.5125887511603558);
        public static readonly D3 DefaultSunColor = new D3(1, 0.9299999999999999, 0.82);
        public static readonly D3 DefaultAmbient = new D3(0.85, 0.8999999999999999, 0.9999999999999998);
    }
}
