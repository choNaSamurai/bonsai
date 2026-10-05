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

        /// <summary>Visible root length = th0 × [min, max] (long/short alternation, hash). ⚑R BALANCE §1.5.5: 0.9–1.4 → 1.0–1.5.</summary>
        public float NebariRootLengthMin = 1.0f;
        public float NebariRootLengthMax = 1.5f;

        /// <summary>Root base thickness = th0 × [min, max] (alternation); the visible end keeps NebariRootTipRatio of it.
        /// ⚑R BALANCE §1.5.5: 0.30–0.42 → 0.42–0.55 (ranges 0.38–0.48 / 0.50–0.60), tip 0.45 → 0.50.</summary>
        public float NebariRootThicknessMin = 0.42f;
        public float NebariRootThicknessMax = 0.55f;
        public float NebariRootTipRatio = 0.50f;

        /// <summary>⚑R BALANCE §1.5.5: a root leaves the trunk at this angle below horizontal (along the trunk surface — a buttress), then eases
        /// (C1) into the NebariRootDrop descent and the shelf on the moss. Range 60–80°.</summary>
        public float NebariRootStartAngleDeg = 72f;

        /// <summary>ADR-0004 rev.5 «маска стику»: on the inner (axis-facing) edge of a root the ink and the AA fall-off grow from 0 at the start
        /// over this × root base width of arc (smoothstep); the light blends from the trunk's over the same length. The outer edge has full ink.</summary>
        public float NebariRootJoinBlend = 1.0f;

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
        public float NebariRootStartHeightRatio = 0.55f; // ⚑R BALANCE §1.5.5: 0.35 → 0.60 Proposed; PA-102R tuning → 0.55 (range 0.50–0.70; AC9 start ≤ 0.05 H): TD-047

        /// <summary>⚑Q BALANCE §1.4.2: only the last this share of a root's length dips under the moss line (the rest lies on the moss).</summary>
        public float NebariRootTipBuryRatio = 0.25f;

        /// <summary>ADR-0004 rev.4: the visible part's centre line stays this far above the moss line (never below it).</summary>
        public float NebariRootMossClearance = 0.3f;

        /// <summary>The buried tip ends this far (+ its half width) below the moss line.</summary>
        public float NebariRootEndDepth = 0.8f;

        /// <summary>Moss tuft over a root tip (SoilFront mesh): width × root tip width, height above the moss line.</summary>
        public float NebariTipTuftWidthRatio = 1.2f; // PA-102R: 2.6 → 1.2 — the thicker root tips (§1.5.5) keep the PA-102Q tuft width (AC9 visibility): TD-047
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
        public float NebariFlareHeightRatio = 0.40f; // ⚑R BALANCE §1.5.5: 0.28 → 0.40 (range 0.32–0.50; the base is now 5 units below y_rim)
        public NebariFlareProfile NebariFlareProfile = NebariFlareProfile.Gaussian;

        // ⚑P BALANCE §1.3.4 [RENDER] pads.
        /// <summary>Bark of InPad nodes with ord ≥ 2 ends this × PuffRadius before the tip (inside the pad).</summary>
        public float PadTwigInsetRatio = 0.35f;

        /// <summary>A bud (Status = Bud) shows no bark strip longer than this (units).</summary>
        public float BudBarkMaxLength = 1.5f;

        /// <summary>Fade of a newly appearing root (not used by the static preview).</summary>
        public float NebariRootFadeSec = 0.6f;

        /// <summary>⚑R BALANCE §1.5.4(в): bark of ord-0 nodes runs in a spiral — the across texture coordinate shifts by this × arc
        /// (fibre angle ≈ atan 0.3 ≈ 17°), sign by the bend of the segment (TierPhase·(−1)^k), × σ (Chokkan — straight fibres). Range 0.15–0.45 (TA).</summary>
        public float BarkTwistShear = 0.30f;

        /// <summary>AC21 (TD-035 fix): a lateral ribbon (and a Nebari root) is visible only from this many pixels inside its parent's silhouette edge
        /// (covers the parent's ink line); inside it is transparent, so no square «patch» of the collar shows on the parent.</summary>
        public float CollarOverlapPx = 3.2f;

        /// <summary>AC21: over this × the child's thickness outside the parent's edge the child's light, bark age and tone blend from the
        /// parent's (at the edge: no brightness step) to its own.</summary>
        public float CollarBlendRatio = 0.9f;

        /// <summary>AC21 before/after: true = the PA-102Q collar (ribbon from the parent's centre line, alpha / ink fade-in, roots drawn first —
        /// the «square patches» of TD-035). Dev comparison only.</summary>
        public bool CollarLegacy;
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

        /// <summary>⚑R BALANCE §1.5.2(б): max |slope| of the sprite row of a side pad (the apex dome keeps TexturedStyleConfig.PadMaxSlope 0.35).
        /// Range 0.05–0.20.</summary>
        public float PadRowSlopeMaxSide = 0.12f;

        /// <summary>⚑R BALANCE §1.5.2(в): a full side pad (PadSize ≥ PadFullNodes) spans at least this many units (union of its puffs, stretched
        /// evenly about its centre); 0 = off. Range 0 / 24–34.</summary>
        public float PadSideMinSpanUnits = 30f;

        /// <summary>⚑P BALANCE §1.3.4: sprite rows of a side pad / of the apex dome (the component with the crown axis or the chain top).</summary>
        public int PadMaxRowsSide = 1; // PA-102P tuning: 2 → 1 (±1 row allowed; TA: side pads of L2 are one sprite row, TD-031)
        public int PadMaxRowsDome = 3;

        /// <summary>Dev tuning knob of the PA-102P harness (-cfg PuffRadiusScale=…): scales PuffRadius of every species.</summary>
        public static float PuffRadiusScale = 1f;

        /// <summary>Dev tuning knobs of the PA-102R harness (-cfg PadRowSlopeMaxSide=… / PadSideMinSpanUnits=…): NaN = the per-species value.</summary>
        public static float RowSlopeMaxSideOverride = float.NaN;
        public static float SideMinSpanOverride = float.NaN;

        public static FoliageConfig For(SpeciesId species)
        {
            FoliageConfig c = ForSpecies(species);
            c.PuffRadius *= PuffRadiusScale;
            if (!float.IsNaN(RowSlopeMaxSideOverride)) c.PadRowSlopeMaxSide = RowSlopeMaxSideOverride;
            if (!float.IsNaN(SideMinSpanOverride)) c.PadSideMinSpanUnits = SideMinSpanOverride;
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
