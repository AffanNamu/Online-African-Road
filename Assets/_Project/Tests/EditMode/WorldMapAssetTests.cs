using ARO.NetCore;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

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

        [Test] public void ArtworkIsBlockCompressedInTheEditorToo()
        {
            var tex = Resources.Load<Texture2D>("WorldMap/west_africa_world_map");
            Assert.IsTrue(GraphicsFormatUtility.IsCompressedFormat(tex.graphicsFormat), "format was " + tex.graphicsFormat);
        }
    }
}
