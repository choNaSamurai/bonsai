using UnityEngine;

namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>
    /// PA-103S style slice: look parameters of the Textured preview (render-only, no gameplay numbers; prototype of the
    /// PA-103 <c>TreeRenderConfig</c>/<c>FoliageConfig</c> look fields — owner Technical Artist). Values tuned against
    /// <c>realism_l2_painterly.jpg</c>; see TECH_DECISIONS TD-034…TD-028.
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

        /// <summary>Sprite width = pad width / columns × overlap (several columns) or × cover (single sprite).
        /// PA-102P: pad sprite rects now carry ~8–9 px of transparent margin (≈ 3.4% of the width), so these and
        /// PadTuftScale / PadLarge|MediumMinRadii are × 1.034 vs PA-103S to keep the visible foliage size.</summary>
        public float PadClusterOverlap = 1.345f;
        public float PadSingleCover = 1.16f;

        /// <summary>Extra rows for tall pads (apex dome): up to PadMaxRows, each narrower by PadRowShrink and lifted by PadRowLift × width.</summary>
        public int PadMaxRows = 3;
        public float PadRowShrink = 0.78f;
        public float PadRowLift = 0.2f;

        /// <summary>PA-102P (TA): the apex dome's extra rows rise less (one dome-shaped sprite reads as the crown).</summary>
        public float PadDomeRowLift = 0.12f;

        /// <summary>PA-102P (TA): Nebari roots use the trunk bark (barkAge 1) with a per-root tone in [min, max] (hash), so roots do not merge.</summary>
        public float RootToneMin = 0.85f;
        public float RootToneMax = 0.95f;

        /// <summary>Rows are added until they cover this share of the puff-union height.</summary>
        public float PadHeightCover = 0.9f;

        /// <summary>Max |slope| of a pad's base line (pads stay near-horizontal, like the reference).</summary>
        public float PadMaxSlope = 0.35f;

        /// <summary>Anchor (sprite pivot) lowered by this × mean puff ry below the puff centres → onto the branch line.</summary>
        public float PadLift = 0.35f;

        /// <summary>Small pads: one tuft per node, width = 2·rx·PadTuftScale.</summary>
        public float PadTuftScale = 1.14f;

        /// <summary>Size class by sprite width: ≥ PadLargeMinRadii·PuffRadius → L, ≥ PadMediumMinRadii·PuffRadius → M, else S.</summary>
        public float PadLargeMinRadii = 4.65f;
        public float PadMediumMinRadii = 2.69f;

        /// <summary>Underside of a pad as a whole is darker by this share (0 if the sprites already bake a dark underside).</summary>
        public float PadUndersideShade = 0f;

        /// <summary>Fraction of the pad height (from the bottom) over which the underside shade fades out.</summary>
        public float PadUndersideHeight = 0.6f;

        /// <summary>Soft shadow underlay: darker translucent duplicate of each puff, offset by (x, −y) × sprite size, scaled.
        /// PA-102P: off by default (alpha 0) — the L2 reference has no cast shadow under pads; the old olive-grey
        /// (0.30, 0.33, 0.24, 0.32) read as a grey drop shadow. If depth is wanted, raise alpha to ≤ 0.10: the tint is
        /// already a warm umber of the reference trunk shadow, and the offset is small.</summary>
        public Color PadShadowTint = new Color(0.42f, 0.33f, 0.22f, 0f);
        public Vector2 PadShadowOffset = new Vector2(0f, 0.05f);
        public float PadShadowScale = 0.98f;

        /// <summary>PA-102P tip coverage: the "solid" part of a pad sprite (α ≥ 0.5) is approximated by an ellipse centred at
        /// (0.5, PadSolidCenterY) of the quad with radii PadSolidRadiusX/Y × the quad size (the atlas has ~3% transparent margin and
        /// a lacy rim); an uncovered tip gets an S sprite PadTipSpriteRadii × PuffRadius wide, ≤ PadExtraSpritesMax per pad.</summary>
        public float PadSolidCenterY = 0.5f;
        public float PadSolidRadiusX = 0.30f;
        public float PadSolidRadiusY = 0.22f;
        public float PadTipSpriteRadii = 2.4f;
        public int PadExtraSpritesMax = 6;

        /// <summary>Fresh growth brightness (BALANCE §1.2.5 FreshTintValue is used; this is the cap of the result).</summary>
        public float FreshBrightnessMax = 1.2f;

        // ---- Pot, soil, background ----
        /// <summary>Trunk base (y = 0) sits this share of the way from the front to the back inner edge of the pot opening.</summary>
        /// <remarks>PA-102Q (TD-041): 0.45 → 0.70 — the trunk base 6.3 units below the pot top (was 9.3): in the L2 reference the root crown
        /// starts near the back rim, and BALANCE §1.4 counts heights from there; 1.0 (back inner edge) left the roots over the bare rim.</remarks>
        public float TreeBaseInOpening = 0.70f;

        /// <summary>Pot without an opening (fallback): soil ground line this high above the Pot Lip.</summary>
        public float SoilSurfaceY = 0.4f;

        /// <summary>Minimal soil strip width relative to the pot inner width (the strip keeps its own aspect).</summary>
        public float SoilWidthRatio = 0.88f; // PA-102Q (TA): 1.02 drew the strip 1.15× wider than the pot it was cut for

        /// <summary>Soil-front band (ground line and moss height — TreeRenderConfig.SoilFrontGroundY / SoilFrontMossHeightUnits): width share of the opening, soft edge share, band depth below the ground.</summary>
        public float SoilFrontWidthRatio = 0.92f;
        public float SoilFrontEdgeFade = 0.12f;
        public float SoilFrontDepthShare = 0.12f; // PA-102Q (TA): band below the ground line 5.5 → 2.2 units (roots on the moss stay visible)

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
