using UnityEngine;

namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>
    /// Prototype of the pot-based camera frame of TD-030 / ARCHITECTURE §6.1 (the game's <c>CameraFramer</c> lives in Presentation).
    /// z = 1 is the frame of the L2 reference: the pot's outer rim spans PotShareOfWidth of the frame width and the top of the pot
    /// sits LipShare of the frame height above its bottom (768×1376 portrait). The smallest z ∈ [ZoomMin, ZoomHardMax] that fits the
    /// tree with TopMargin / SideMargin is used; towards ZoomMin the lip rises to LipShareAtZoomMin so that the whole pot stays in the
    /// frame (owner decision: ZoomMin = 0.78 — the pot is never cut). AC harnesses use z = 1 for the metrics.
    /// ADR-0004 rev.5 (AC20 PA-102R): the tree = the vertex bounds of the bark (with roots) and pad meshes + FrameMotionPadUnits on the sides
    /// and the top; the frame is centred on the pot (x = 0), the half width = max(|x_min|, |x_max|); fitting wins over ZoomMax (now only a
    /// dev warning threshold), ZoomHardMax caps degenerate geometry.
    /// </summary>
    public static class PotFraming
    {
        public const float PotShareOfWidth = 566f / 768f;      // 0.737
        public const float LipShare = (1376f - 1068f) / 1376f; // 0.224
        public const float FrameAspect = 768f / 1376f;          // width / height
        public const float ZoomMin = 0.78f;
        public const float ZoomMax = 1.30f;
        public const float ZoomHardMax = 1.60f;
        public const float FrameMotionPadUnits = 2f;
        public const float LipShareAtZoomMin = 0.30f;
        public const float TopMargin = 0.19f;
        public const float SideMargin = 0.03f;
        const float ZoomStep = 0.01f;

        /// <summary>Lip height share of the frame at zoom z (linear from LipShareAtZoomMin at ZoomMin to LipShare at z ≥ 1).</summary>
        public static float LipShareAt(float z) =>
            z >= 1f ? LipShare : Mathf.Lerp(LipShareAtZoomMin, LipShare, Mathf.InverseLerp(ZoomMin, 1f, z));

        /// <summary>World rect of the frame at zoom z for a pot of outer width <paramref name="potWidth"/> centred at x = 0.</summary>
        public static Rect FrameAt(float z, float potWidth, float potTopY)
        {
            float width = z * potWidth / PotShareOfWidth;
            float height = width / FrameAspect;
            float bottom = potTopY - LipShareAt(z) * height;
            return new Rect(-0.5f * width, bottom, width, height);
        }

        /// <summary>Smallest z ≥ <paramref name="zMin"/> (step 0.01) whose frame holds <paramref name="tree"/> (+ FrameMotionPadUnits) within the
        /// margins; ZoomHardMax if none.</summary>
        public static float Zoom(float potWidth, float potTopY, Rect tree, float zMin = ZoomMin, float topMargin = TopMargin)
        {
            for (float z = zMin; z < ZoomHardMax; z += ZoomStep)
            {
                if (Fits(FrameAt(z, potWidth, potTopY), tree, topMargin)) return z;
            }
            return ZoomHardMax;
        }

        /// <summary>Frame centred on the pot (x = 0): the half width needed is max(|x_min|, |x_max|) + pad, the height above the lip y_max + pad.</summary>
        public static bool Fits(Rect frame, Rect tree, float topMargin = TopMargin)
        {
            float half = Mathf.Max(Mathf.Abs(tree.xMin), Mathf.Abs(tree.xMax)) + FrameMotionPadUnits;
            float top = frame.yMax - topMargin * frame.height;
            float side = SideMargin * frame.width;
            return tree.yMax + FrameMotionPadUnits <= top && half <= 0.5f * frame.width - side;
        }
    }
}
