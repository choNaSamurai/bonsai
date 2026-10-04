using UnityEditor;

namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>
    /// PA-103S: import settings for Technical Artist's preview art, limited to <see cref="PreviewArtLibrary.DefaultRoot"/>
    /// so the Textured preview does not depend on manual importer tweaks. Bark/washi tile (Repeat, mips); pad atlases are
    /// clamped, alpha-is-transparency (colour dilation under straight alpha) with mips; pot/soil are readable (the preview
    /// measures the pot rim and the soil surface line). Uncompressed: dev preview, quality over size.
    /// </summary>
    public sealed class PreviewArtPostprocessor : AssetPostprocessor
    {
        const uint Version = 1;

        public override uint GetVersion() => Version;

        void OnPreprocessTexture()
        {
            if (!IsPreviewArt(assetPath)) return;
            var importer = (TextureImporter)assetImporter;
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);

            importer.textureType = TextureImporterType.Default;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.sRGBTexture = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = true;
            importer.filterMode = UnityEngine.FilterMode.Trilinear;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;

            if (name.StartsWith("bark_") || name.StartsWith("washi_"))
            {
                importer.wrapMode = UnityEngine.TextureWrapMode.Repeat;
                importer.alphaIsTransparency = false;
                importer.isReadable = false;
            }
            else if (name.StartsWith("pads_"))
            {
                importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
                importer.alphaIsTransparency = true;
                importer.isReadable = false;
            }
            else
            {
                // pot_*, soil_* and anything else in the folder.
                importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
                importer.alphaIsTransparency = true;
                importer.isReadable = true;
            }
        }

        public static bool IsPreviewArt(string path) =>
            path != null && path.StartsWith(PreviewArtLibrary.DefaultRoot + "/") && !path.Substring(PreviewArtLibrary.DefaultRoot.Length + 1).Contains("/");
    }
}
