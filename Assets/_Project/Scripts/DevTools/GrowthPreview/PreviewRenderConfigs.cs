using PocketArbor.Domain.Tree;

namespace PocketArbor.DevTools.GrowthPreview
{
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

        /// <summary>Second (near-horizontal) segment: degrees below horizontal at the hidden end.</summary>
        public float NebariRootEndDropDeg = 6f;

        /// <summary>Share of the root length taken by the first (dropping) segment.</summary>
        public float NebariRootKnee = 0.4f;

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

        /// <summary>Roots start on the flare this × th0 above the soil and end this far below the SoilFront ground line (hidden).</summary>
        public float NebariRootStartHeight = 0.5f;
        public float NebariRootEndDepth = 0.8f;

        // Base flare: w(y) = th0·(1 + F·e^{−y / (NebariFlareHeightRatio·th0)}), F = FlareMin + FlareGain·clamp01((th0 − th_init) / (th_max − th_init)).
        public float NebariFlareMin = 0.25f;
        /// <summary>⚑P BALANCE §1.3.3: 0.5 → 0.70 (F at D80 ≈ 0.65).</summary>
        public float NebariFlareGain = 0.70f;
        public float NebariFlareHeightRatio = 1.5f;

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
                case SpeciesId.Momiji: return new FoliageConfig { PuffRadius = 9f, PadFlatten = 0.65f };
                case SpeciesId.Yamazakura: return new FoliageConfig { PuffRadius = 8f, PadFlatten = 0.60f };
                default: return new FoliageConfig { PuffRadius = 7f, PadFlatten = 0.55f, PadMaxRowsSide = 1 };
            }
        }
    }
}
