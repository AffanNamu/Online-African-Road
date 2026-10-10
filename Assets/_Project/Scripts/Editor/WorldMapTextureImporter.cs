using UnityEditor;
using UnityEngine;

namespace ARO.Editor
{
    /// <summary>
    /// Import settings for the strategic world-map artwork (Resources/WorldMap). Applied automatically on import, so a replaced image gets the same treatment.
    /// Block-compressed per platform (WebGL: DXT1; Android/iOS: ASTC 6x6), mipmapped so zooming out stays clean, clamped, never CPU-readable.
    /// 1672x944 compressed is about 1 MB on the GPU (about 8 MB if left as RGBA32).
    /// </summary>
    public sealed class WorldMapTextureImporter : AssetPostprocessor
    {
        public const string Folder = "Assets/_Project/Resources/WorldMap/";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var t = (TextureImporter)assetImporter;
            t.textureType = TextureImporterType.Default; t.sRGBTexture = true; t.alphaSource = TextureImporterAlphaSource.None;
            t.mipmapEnabled = true; t.filterMode = FilterMode.Trilinear; t.anisoLevel = 4; t.wrapMode = TextureWrapMode.Clamp;
            t.npotScale = TextureImporterNPOTScale.None; t.isReadable = false; t.streamingMipmaps = false;
            t.maxTextureSize = 2048; t.textureCompression = TextureImporterCompression.Compressed; t.crunchedCompression = false; t.compressionQuality = 75;
            Platform(t, "WebGL", TextureImporterFormat.DXT1); Platform(t, "Android", TextureImporterFormat.ASTC_6x6); Platform(t, "iPhone", TextureImporterFormat.ASTC_6x6);
        }

        static void Platform(TextureImporter t, string name, TextureImporterFormat fmt) =>
            t.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            { name = name, overridden = true, maxTextureSize = 2048, format = fmt, textureCompression = TextureImporterCompression.Compressed, compressionQuality = 75 });
    }
}
