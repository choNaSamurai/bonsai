using UnityEngine;

namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>
    /// PA-103S style slice: look parameters of the Textured preview (render-only, no gameplay numbers; prototype of the
    /// PA-103 <c>TreeRenderConfig</c>/<c>FoliageConfig</c> look fields — owner Technical Artist). Values tuned against
    /// <c>realism_l2_painterly.jpg</c>; see TECH_DECISIONS TD-025…TD-028.
    /// </summary>
    public sealed class TexturedStyleConfig
    {
        // ---- Bark (ADR-0004 §1) ----
        /// <summary>World units covered by one bark texture tile (both across and along the ribbon → isotropic texels).</summary>
        public float BarkTileWorld = 18f;

        /// <summary>barkAge = smoothstep(YoungMaxWidth, TrunkMinWidth, w) (+ order bias): young bark below, trunk bark above.</summary>
        public float YoungMaxWidth = 1.6f;
        public float TrunkMinWidth = 6f;

        /// <summary>Added to barkAge on the trunk chain (ord 0 leans to the trunk texture).</summary>
        public float TrunkChainAgeBias = 0.35f;

        /// <summary>Lateral collar: width × (1 + CollarFlare) at the base, easing to × 1 over the collar length.</summary>
        public float CollarFlare = 0.35f;

        /// <summary>A lateral ribbon fades in over this × parent half-width from its start on the parent's centre line.</summary>
        public float CollarFadeIn = 0.8f;

        /// <summary>Terminal tip: a cap section this far past the tip (× tip width), width → CapWidthRatio × tip width.</summary>
        public float CapLengthRatio = 0.6f;
        public float CapWidthRatio = 0.25f;

        public Color TrunkTint = Color.white;
        public Color YoungTint = Color.white;
        public Vector3 LightDir = new Vector3(-0.55f, 0.6f, 0.6f);
        public float ShadeDark = 0.55f;
        public float ShadeLight = 1.08f;
        public Color InkColor = new Color(0.13f, 0.085f, 0.06f, 0.9f);
        public float InkPx = 2.2f;
        public float InkMaxShare = 0.3f;
        public float AAExpandPx = 1f;

        // ---- Foliage pads (ADR-0004 §2) ----
        /// <summary>A full pad is covered by sprites no wider than this × PuffRadius (wider pads get more columns).</summary>
        public float PadSpriteMaxWidthRatio = 6f;

        /// <summary>Sprite width = pad width / columns × overlap (several columns) or × cover (single sprite).</summary>
        public float PadClusterOverlap = 1.3f;
        public float PadSingleCover = 1.12f;

        /// <summary>Extra rows for tall pads (apex dome): up to PadMaxRows, each narrower by PadRowShrink and lifted by PadRowLift × width.</summary>
        public int PadMaxRows = 3;
        public float PadRowShrink = 0.78f;
        public float PadRowLift = 0.2f;

        /// <summary>Rows are added until they cover this share of the puff-union height.</summary>
        public float PadHeightCover = 0.9f;

        /// <summary>Max |slope| of a pad's base line (pads stay near-horizontal, like the reference).</summary>
        public float PadMaxSlope = 0.35f;

        /// <summary>Anchor (sprite pivot) lowered by this × mean puff ry below the puff centres → onto the branch line.</summary>
        public float PadLift = 0.35f;

        /// <summary>Small pads: one tuft per node, width = 2·rx·PadTuftScale.</summary>
        public float PadTuftScale = 1.1f;

        /// <summary>Size class by sprite width: ≥ PadLargeMinRadii·PuffRadius → L, ≥ PadMediumMinRadii·PuffRadius → M, else S.</summary>
        public float PadLargeMinRadii = 4.5f;
        public float PadMediumMinRadii = 2.6f;

        /// <summary>Underside of a pad as a whole is darker by this share (0 if the sprites already bake a dark underside).</summary>
        public float PadUndersideShade = 0f;

        /// <summary>Fraction of the pad height (from the bottom) over which the underside shade fades out.</summary>
        public float PadUndersideHeight = 0.6f;

        /// <summary>Soft shadow underlay: darker translucent duplicate of each puff, offset by (x, −y) × sprite size, scaled.</summary>
        public Color PadShadowTint = new Color(0.30f, 0.33f, 0.24f, 0.32f);
        public Vector2 PadShadowOffset = new Vector2(0f, 0.12f);
        public float PadShadowScale = 0.98f;

        /// <summary>Fresh growth brightness (BALANCE §1.2.5 FreshTintValue is used; this is the cap of the result).</summary>
        public float FreshBrightnessMax = 1.2f;

        // ---- Pot, soil, background ----
        /// <summary>Trunk base (y = 0) sits this share of the way from the front to the back inner edge of the pot opening.</summary>
        public float TreeBaseInOpening = 0.45f;

        /// <summary>Pot without an opening (fallback): soil ground line this high above the Pot Lip.</summary>
        public float SoilSurfaceY = 0.4f;

        /// <summary>Minimal soil strip width relative to the pot inner width (the strip keeps its own aspect).</summary>
        public float SoilWidthRatio = 1.02f;

        /// <summary>Soil-front band: ground line height (world), width share of the opening, soft edge share, band depth below the ground.</summary>
        public float SoilFrontGroundY = -0.4f;
        public float SoilFrontWidthRatio = 0.92f;
        public float SoilFrontEdgeFade = 0.12f;
        public float SoilFrontDepthShare = 0.3f;

        public float SoilFrontBandPx(int soilHeight) => SoilFrontDepthShare * soilHeight;

        /// <summary>Moisture W at or above which the moist soil strip is used.</summary>
        public float MoistSoilThreshold = 0.3f;

        /// <summary>World units per washi background tile.</summary>
        public float WashiTileWorld = 140f;

        // ---- Reference overlay (realism_l2_painterly.jpg, 768×1376 px, measured 2026-10-05) ----
        /// <summary>Outer rim of the reference pot (px from the left) and the top of its back rim (px from the top): matched to
        /// the opaque x-extent and the top edge of the preview pot sprite.</summary>
        public float RefPotOuterLeftPx = 101f;
        public float RefPotOuterRightPx = 667f;
        public float RefPotTopPx = 1068f;
    }
}
