using PocketArbor.Domain.Tree;

namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>⚑Q BALANCE §1.4.1(а): fall-off of the Nebari base flare along the #0 axis.</summary>
    public enum NebariFlareProfile
    {
        /// <summary>PA-102P: 1 + F·e^{−y/(h·th0)} over the height (kept for comparison).</summary>
        Exponential = 0,
        /// <summary>PA-102Q: 1 + F·e^{−(s/(h·th0))²}, s = arc length along the #0 axis (ADR-0004 rev.4: follows a leaning base).</summary>
        Gaussian = 1,
    }

    /// <summary>
    /// [RENDER] parameters of BALANCE §1.1 (CurvatureOffsetRatio), §1.2.2 and ⚑P §1.3.0 / §1.3.3 / §1.3.4 for the preview.
    /// Plain C# prototype of the PA-103 <c>TreeRenderConfig</c> ScriptableObject (owner: Technical Artist / PA-103).
    /// </summary>
    public sealed class TreeRenderConfig
    {
        /// <summary>Bezier control-point offset = Curvature × ratio × Length (BALANCE §1).</summary>
        public float CurvatureOffsetRatio = 0.25f;

        /// <summary>tipWidth = max(Thickness(apical child), Thickness × ratio); no apical child → Thickness × ratio (§1.2.2).</summary>
        public float TerminalTipRatio = 0.45f;

        /// <summary>An apical child blends from the parent's tipWidth to its own profile over this share of its length (§1.2.2).</summary>
        public float JointBlend = 0.15f;

        /// <summary>⚑P BALANCE §1.3.0: fixed pot width (outer front rim), units — no longer sized to the crown. Prototype of
        /// <c>PotConfig.FrontWidthUnits</c> (Tokoname, PA-203).</summary>
        public float PotWidthUnits = 92f;

        // ⚑P BALANCE §1.3.3 Nebari: roots = clamp(round(NebariRootsMin + (th0 − TrunkInitialThickness) / NebariThicknessPerRoot), min, max).
        public int NebariRootsMin = 2;
        public int NebariRootsMax = 7;
        public float NebariThicknessPerRoot = 2.2f;

        /// <summary>Visible root length = th0 × [min, max] (long/short alternation, hash).</summary>
        public float NebariRootLengthMin = 0.9f;
        public float NebariRootLengthMax = 1.4f;

        /// <summary>Root base thickness = th0 × [min, max] (alternation); the visible end keeps NebariRootTipRatio of it.</summary>
        public float NebariRootThicknessMin = 0.30f;
        public float NebariRootThicknessMax = 0.42f;
        public float NebariRootTipRatio = 0.45f;

        /// <summary>First segment leaves the flare this many degrees below horizontal (hash), then eases to near-horizontal.</summary>
        public float NebariRootDropMinDeg = 35f;
        public float NebariRootDropMaxDeg = 55f;

        /// <summary>Visible part on the moss: degrees below horizontal at its end (gentle droop towards the buried tip).</summary>
        public float NebariRootEndDropDeg = 6f;

        /// <summary>Buried tip (last NebariRootTipBuryRatio of the length): degrees below horizontal as it dives under the moss.</summary>
        public float NebariRootBuryDropDeg = 30f;

        /// <summary>Cross wave amplitude = NebariRootWaviness × th0, one period per root, phase by hash.</summary>
        public float NebariRootWaviness = 0.15f;

        /// <summary>Fan ±NebariFanDeg ± jitter; roots with |fan| ≤ NebariFrontRootDeg face the viewer and are × NebariFrontRootScale shorter.</summary>
        public float NebariFanDeg = 75f;

        /// <summary>Azimuth of root i = NebariFanSlots[i]·NebariFanDeg + hash·jitter (slot table by index, ADR-0004 rev.3 §1):
        /// ≥ 2 roots per side from n ≥ 4, one front root from n ≥ 5.</summary>
        public float[] NebariFanSlots = { -0.80f, 0.80f, -0.45f, 0.45f, 0.12f, -1.00f, 1.00f };
        public float NebariFanJitterDeg = 10f;
        public float NebariFrontRootDeg = 25f;
        public float NebariFrontRootScale = 0.6f;

        /// <summary>⚑Q BALANCE §1.4.2: a root starts on the flare this × th0 above the soil (D80 3.8 units).</summary>
        public float NebariRootStartHeightRatio = 0.35f; // PA-102Q tuning: 0.25 → 0.35 (range 0.15–0.35; roots leave the flare above the moss): TD-042

        /// <summary>⚑Q BALANCE §1.4.2: only the last this share of a root's length dips under the moss line (the rest lies on the moss).</summary>
        public float NebariRootTipBuryRatio = 0.25f;

        /// <summary>ADR-0004 rev.4: the visible part's centre line stays this far above the moss line (never below it).</summary>
        public float NebariRootMossClearance = 0.3f;

        /// <summary>The buried tip ends this far (+ its half width) below the moss line.</summary>
        public float NebariRootEndDepth = 0.8f;

        /// <summary>Moss tuft over a root tip (SoilFront mesh): width × root tip width, height above the moss line.</summary>
        public float NebariTipTuftWidthRatio = 2.6f;
        public float NebariTipTuftHeight = 1.2f;

        /// <summary>⚑Q BALANCE §1.4.2: ground line of the SoilFront band (world y) and the height of its opaque moss above it (≤ 2.5 units,
        /// asset — TA). Moss line = SoilFrontGroundY + SoilFrontMossHeightUnits.</summary>
        public float SoilFrontGroundY = -0.4f;
        public float SoilFrontMossHeightUnits = 2.5f;

        public float MossLineY => SoilFrontGroundY + SoilFrontMossHeightUnits;

        // Base flare: w = w_axis·(1 + F·fall-off), F = FlareMin + FlareGain·clamp01((th0 − th_init) / (th_max − th_init)).
        public float NebariFlareMin = 0.25f;
        /// <summary>⚑Q BALANCE §1.4.1(а): 0.70 → 1.30 (F at D80 ≈ 0.98).</summary>
        public float NebariFlareGain = 1.30f;
        /// <summary>⚑Q BALANCE §1.4.1(а): 1.5 → 0.22 with the Gaussian fall-off (D80: < 1% above 0.47·th0 = 7.2 units).</summary>
        public float NebariFlareHeightRatio = 0.28f; // PA-102Q tuning: 0.22 → 0.28 (range 0.18–0.28): the peak was under the 2.1-unit moss band (TD-042)
        public NebariFlareProfile NebariFlareProfile = NebariFlareProfile.Gaussian;

        // ⚑P BALANCE §1.3.4 [RENDER] pads.
        /// <summary>Bark of InPad nodes with ord ≥ 2 ends this × PuffRadius before the tip (inside the pad).</summary>
        public float PadTwigInsetRatio = 0.35f;

        /// <summary>A bud (Status = Bud) shows no bark strip longer than this (units).</summary>
        public float BudBarkMaxLength = 1.5f;

        /// <summary>Fade of a newly appearing root (not used by the static preview).</summary>
        public float NebariRootFadeSec = 0.6f;
    }

    /// <summary>[RENDER] foliage-pad parameters of BALANCE §1.2.5 (prototype of PA-103 <c>FoliageConfig</c>).</summary>
    public sealed class FoliageConfig
    {
        public float PuffRadius;
        public int PuffsPerNode = 2;

        /// <summary>Puff ellipse: rx = PuffWidthRatio·r, ry = PadFlatten·rx.</summary>
        public float PuffWidthRatio = 1.25f;
        public float PadFlatten;

        /// <summary>Underside of a pad is this much darker (painterly shade).</summary>
        public float PadUndersideShade = 0.25f;

        /// <summary>Fresh growth (AgeDays &lt; MatureDays) is this much lighter in value.</summary>
        public float FreshTintValue = 0.15f;

        /// <summary>⚑P BALANCE §1.3.4: sprite rows of a side pad / of the apex dome (the component with the crown axis or the chain top).</summary>
        public int PadMaxRowsSide = 1; // PA-102P tuning: 2 → 1 (±1 row allowed; TA: side pads of L2 are one sprite row, TD-031)
        public int PadMaxRowsDome = 3;

        /// <summary>Dev tuning knob of the PA-102P harness (-cfg PuffRadiusScale=…): scales PuffRadius of every species.</summary>
        public static float PuffRadiusScale = 1f;

        public static FoliageConfig For(SpeciesId species)
        {
            FoliageConfig c = ForSpecies(species);
            c.PuffRadius *= PuffRadiusScale;
            return c;
        }

        static FoliageConfig ForSpecies(SpeciesId species)
        {
            switch (species)
            {
                // PA-102Q tuning: Momiji PuffRadius 9 → 8.55 (−5%, PA-102P range ±15%; TD-042).
                case SpeciesId.Momiji: return new FoliageConfig { PuffRadius = 8.55f, PadFlatten = 0.65f };
                case SpeciesId.Yamazakura: return new FoliageConfig { PuffRadius = 8f, PadFlatten = 0.60f };
                // ⚑Q BALANCE §1.4.5: Kuromatsu PuffRadius 7 → 8, PadFlatten 0.55 → 0.62 (denser, thicker shelves).
                default: return new FoliageConfig { PuffRadius = 8f, PadFlatten = 0.62f, PadMaxRowsSide = 1 };
            }
        }
    }
}
