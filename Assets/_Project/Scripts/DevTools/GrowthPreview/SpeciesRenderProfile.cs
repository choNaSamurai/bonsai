using PocketArbor.Domain.Tree;
using UnityEngine;

namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>
    /// ⚑T BALANCE §1.6.6 [RENDER] bark of a species — DevTools prototype of <c>PocketArbor.Presentation.Config.SpeciesRenderProfile</c> (TD-067, ADR-0010
    /// rev.3 §7; same field names). Material-only: the bark mesh, its UVs and the draw calls are the same for every species (TD-068, T18).
    /// Textures are looked up in <see cref="PreviewArtLibrary.DefaultRoot"/> as <c>bark_&lt;species&gt;_{trunk,young}.png</c>; missing → the shared
    /// bark_trunk / bark_young neutralised to luminance × <see cref="BarkTint"/> (placeholder, no error).
    /// </summary>
    public sealed class SpeciesRenderProfile
    {
        public SpeciesId Species;
        public string BarkTrunkTexture;
        public string BarkYoungTexture;

        /// <summary>Placeholder tone (sRGB, multiply) — used only while the species texture is missing.</summary>
        public Color BarkTint = Color.white;
        public Color BarkYoungTint = Color.white;

        /// <summary>Tint of the tips of young shoots (Momiji: green at the base → red at the tips); = BarkYoungTint for a uniform young bark.</summary>
        public Color BarkYoungTipTint = Color.white;

        /// <summary>Strength of the texture's cracks / stripes around its mean colour (1 = as painted).</summary>
        public float BarkContrast = 1f;

        /// <summary>Sheen band along the lit edge, 0–1.</summary>
        public float BarkSheen;

        /// <summary>Bark twist shear of this species (replaces TreeRenderConfig.BarkTwistShear in the material).</summary>
        public float BarkTwistShear = 0.30f;

        /// <summary>Tiles of the species texture per 100 units along the trunk (applies to the species texture only; the placeholder keeps the
        /// isotropic TexturedStyleConfig.BarkTileWorld — TD-072 of TA decides the asset format).</summary>
        public float BarkTileV = 2.4f;

        /// <summary>Young-shoot tip gradient: full tip tint at mesh width ≤ x, none at ≥ y (units).</summary>
        public Vector2 YoungTipWidth = new Vector2(0.5f, 0.95f);

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.white;

        public static SpeciesRenderProfile For(SpeciesId species)
        {
            switch (species)
            {
                case SpeciesId.Momiji:
                    return new SpeciesRenderProfile
                    {
                        Species = species, BarkTrunkTexture = "bark_momiji_trunk.png", BarkYoungTexture = "bark_momiji_young.png",
                        BarkTint = Hex("#9A9078"), BarkYoungTint = Hex("#8C9A52"), BarkYoungTipTint = Hex("#A85A44"),
                        BarkContrast = 0.75f, BarkSheen = 0.10f, BarkTwistShear = 0.30f, BarkTileV = 2.4f,
                    };
                case SpeciesId.Yamazakura:
                    return new SpeciesRenderProfile
                    {
                        Species = species, BarkTrunkTexture = "bark_yamazakura_trunk.png", BarkYoungTexture = "bark_yamazakura_young.png",
                        BarkTint = Hex("#8A4A3A"), BarkYoungTint = Hex("#94503C"), BarkYoungTipTint = Hex("#94503C"),
                        BarkContrast = 1.0f, BarkSheen = 0.30f, BarkTwistShear = 0.10f, BarkTileV = 2.0f,
                    };
                default:
                    return new SpeciesRenderProfile
                    {
                        Species = SpeciesId.Kuromatsu, BarkTrunkTexture = "bark_kuromatsu_trunk.png", BarkYoungTexture = "bark_kuromatsu_young.png",
                        BarkTint = Hex("#5A5856"), BarkYoungTint = Hex("#8A6E58"), BarkYoungTipTint = Hex("#8A6E58"),
                        BarkContrast = 1.35f, BarkSheen = 0f, BarkTwistShear = 0.15f, BarkTileV = 1.6f,
                    };
            }
        }

        /// <summary>Dev tuning knobs of the harness (-cfg Momiji.BarkContrast=…): applied by <see cref="For"/> callers via reflection.</summary>
        public static readonly System.Collections.Generic.Dictionary<string, float> FieldOverrides = new System.Collections.Generic.Dictionary<string, float>();

        public static SpeciesRenderProfile Resolve(SpeciesId species)
        {
            SpeciesRenderProfile p = For(species);
            foreach (var kv in FieldOverrides)
            {
                string field = kv.Key;
                int dot = field.IndexOf('.');
                if (dot > 0)
                {
                    if (field.Substring(0, dot) != species.ToString()) continue;
                    field = field.Substring(dot + 1);
                }
                System.Reflection.FieldInfo f = typeof(SpeciesRenderProfile).GetField(field);
                if (f != null && f.FieldType == typeof(float)) f.SetValue(p, kv.Value);
            }
            return p;
        }
    }
}
