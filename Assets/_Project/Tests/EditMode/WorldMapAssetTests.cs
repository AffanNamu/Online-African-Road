using ARO.NetCore;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ARO.Tests
{
    /// <summary>The world-map artwork and data as the game loads them (Resources + JsonUtility), not as the .NET tests read them.</summary>
    public class WorldMapAssetTests
    {
        static WorldMapData Load() => JsonUtility.FromJson<WorldMapData>(Resources.Load<TextAsset>("WorldMap/world_map").text);

        [Test] public void DataParsesWithJsonUtilityAndValidates()
        {
            var d = Load();
            Assert.AreEqual(23, d.cities.Length); Assert.AreEqual(12, d.routes.Length);
            var errors = WorldMapValidator.Validate(d);
            Assert.IsEmpty(errors, string.Join("\n", errors));
            var m = new WorldMapModel(d);
            Assert.AreEqual(RouteState.Prototype, m.StateFor(m.Route("lagos-ibadan"), 1));
            Assert.AreEqual("ng-lagos-ibadan", m.Route("lagos-ibadan").gameRouteId);
        }

        [Test] public void ArtworkLoadsWithTheSizeTheDataDeclares()
        {
            var d = Load(); var tex = Resources.Load<Texture2D>("WorldMap/" + d.image.file);
            Assert.NotNull(tex, "Resources/WorldMap/" + d.image.file + " is missing");
            Assert.AreEqual(d.image.textureWidth, tex.width); Assert.AreEqual(d.image.textureHeight, tex.height);
            Assert.AreEqual(d.image.contentWidth / (float)d.image.contentHeight, 1672f / 941f, 0.001f, "the artwork's aspect ratio is the one that was supplied");
            Assert.AreEqual(TextureWrapMode.Clamp, tex.wrapMode); Assert.Greater(tex.mipmapCount, 1);
        }

        [Test] public void ImportSettingsAreTheIntendedOnes()
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath("Assets/_Project/Resources/WorldMap/west_africa_world_map.jpg");
            Assert.NotNull(imp); Assert.IsTrue(imp.mipmapEnabled); Assert.IsFalse(imp.isReadable); Assert.AreEqual(TextureImporterNPOTScale.None, imp.npotScale);
            Assert.AreEqual(2048, imp.maxTextureSize);
            var web = imp.GetPlatformTextureSettings("WebGL"); Assert.IsTrue(web.overridden); Assert.AreEqual(TextureImporterFormat.DXT1, web.format);
            var and = imp.GetPlatformTextureSettings("Android"); Assert.IsTrue(and.overridden); Assert.AreEqual(TextureImporterFormat.ASTC_6x6, and.format);
        }

        // The editor in headless batch mode imports this texture uncompressed (CI saw R8G8B8_UNorm), so this does NOT prove the WebGL/mobile build is block-compressed
        // (that is configured by the platform overrides asserted above). It guards the memory budget even in the uncompressed worst case.
        [Test] public void ArtworkStaysInsideItsMemoryBudgetEvenUncompressed()
        {
            var tex = Resources.Load<Texture2D>("WorldMap/west_africa_world_map");
            long bytes = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex);
            Debug.Log($"[WorldMapAssetTests] artwork format in this editor = {tex.graphicsFormat}, runtime size = {bytes / 1048576f:0.0} MB");
            Assert.Less(bytes, 12L * 1048576L, "artwork uses " + bytes + " bytes");
        }
    }
}
