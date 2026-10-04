using PocketArbor.Domain.Tree;

namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>
    /// [RENDER] parameters of BALANCE §1.1 (CurvatureOffsetRatio) and §1.2.2–§1.2.4 for the PA-102S preview.
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

        // §1.2.4 Nebari: roots = clamp(round(NebariRootsMin + (th0 − TrunkInitialThickness) / NebariThicknessPerRoot), min, max).
        public int NebariRootsMin = 2;
        public int NebariRootsMax = 7;
        public float NebariThicknessPerRoot = 3f;
        public float NebariRootLengthMin = 1.6f;
        public float NebariRootLengthMax = 2.4f;
        public float NebariRootThicknessRatio = 0.35f;
        public float NebariFanDeg = 80f;
        public float NebariFanJitterDeg = 8f;

        // Base flare: w(y) = th0·(1 + F·e^{−y / (NebariFlareHeightRatio·th0)}), F = FlareMin + FlareGain·clamp01((th0 − th_init) / (th_max − th_init)).
        public float NebariFlareMin = 0.25f;
        public float NebariFlareGain = 0.5f;
        public float NebariFlareHeightRatio = 1.5f;

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

        public static FoliageConfig For(SpeciesId species)
        {
            switch (species)
            {
                case SpeciesId.Momiji: return new FoliageConfig { PuffRadius = 9f, PadFlatten = 0.65f };
                case SpeciesId.Yamazakura: return new FoliageConfig { PuffRadius = 8f, PadFlatten = 0.60f };
                default: return new FoliageConfig { PuffRadius = 7f, PadFlatten = 0.55f };
            }
        }
    }
}
