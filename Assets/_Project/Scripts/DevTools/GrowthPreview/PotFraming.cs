using UnityEngine;

namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>
    /// Prototype of the pot-based camera frame of TD-030 / ARCHITECTURE §6.1 (the game's <c>CameraFramer</c> lives in Presentation).
    /// z = 1 is the frame of the L2 reference: the pot's outer rim spans PotShareOfWidth of the frame width and the top of the pot
    /// sits LipShare of the frame height above its bottom (768×1376 portrait). The smallest z ∈ [ZoomMin, ZoomMax] that fits the
    /// tree (bark + pads, roots excluded) with TopMargin / SideMargin is used; towards ZoomMin the lip rises to LipShareAtZoomMin so
    /// that the whole pot stays in the frame (owner decision: ZoomMin = 0.78 — the pot is never cut). AC harnesses use z = 1.
    /// </summary>
    public static class PotFraming
    {
        public const float PotShareOfWidth = 566f / 768f;      // 0.737
        public const float LipShare = (1376f - 1068f) / 1376f; // 0.224
        public const float FrameAspect = 768f / 1376f;          // width / height
        public const float ZoomMin = 0.78f;
        public const float ZoomMax = 1.30f;
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

        /// <summary>Smallest z (step 0.01) whose frame holds <paramref name="tree"/> within the margins; ZoomMax if none.</summary>
        public static float Zoom(float potWidth, float potTopY, Rect tree)
        {
            for (float z = ZoomMin; z < ZoomMax; z += ZoomStep)
            {
                if (Fits(FrameAt(z, potWidth, potTopY), tree)) return z;
            }
            return ZoomMax;
        }

        static bool Fits(Rect frame, Rect tree)
        {
            float top = frame.yMax - TopMargin * frame.height;
            float side = SideMargin * frame.width;
            return tree.yMax <= top && tree.xMin >= frame.xMin + side && tree.xMax <= frame.xMax - side;
        }
    }
}
