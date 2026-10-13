using UnityEditor;
using UnityEngine;

namespace ARO.Editor
{
    /// <summary>
    /// Import settings for the national flags (Resources/Flags, 96x72 RGBA): kept at their real 4:3 size (no power-of-two stretching), mipmapped so the 24-32 px flags in lists stay clean,
    /// clamped, uncompressed (a block-compressed 96x72 flag smears its edges and the whole set is about 1.5 MB), never CPU-readable.
    /// </summary>
    public sealed class FlagTextureImporter : AssetPostprocessor
    {
        public const string Folder = "Assets/_Project/Resources/Flags/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var t = (TextureImporter)assetImporter;
            t.textureType = TextureImporterType.Default; t.sRGBTexture = true; t.alphaSource = TextureImporterAlphaSource.FromInput; t.alphaIsTransparency = true;
            t.mipmapEnabled = true; t.filterMode = FilterMode.Bilinear; t.wrapMode = TextureWrapMode.Clamp;
            t.npotScale = TextureImporterNPOTScale.None; t.isReadable = false; t.streamingMipmaps = false;
            t.maxTextureSize = 128; t.textureCompression = TextureImporterCompression.Uncompressed;
        }
    }
}
