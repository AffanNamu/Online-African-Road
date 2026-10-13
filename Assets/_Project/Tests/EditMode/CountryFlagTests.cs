using System.Linq;
using ARO.Game;
using ARO.NetCore;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ARO.Tests
{
    /// <summary>The national flags as the game loads and draws them: every listed flag loads at its real 4:3 size, the import settings keep it that way, and the renderer draws a neutral placeholder for anything it cannot honestly draw.</summary>
    public class CountryFlagTests
    {
        [Test] public void EveryListedFlagLoadsAtItsRealSize()
        {
            foreach (var code in CountryFlags.All)
            {
                var tex = Resources.Load<Texture2D>(CountryFlags.ResourcePath(code)); Assert.NotNull(tex, "Resources/" + CountryFlags.ResourcePath(code) + " is missing");
                Assert.AreEqual(96, tex.width, code); Assert.AreEqual(72, tex.height, code + ": a flag must keep its 4:3 shape (no power-of-two stretching)");
                var sprite = FlagArt.Get(code); Assert.NotNull(sprite, code); Assert.AreEqual(96f / 72f, sprite.rect.width / sprite.rect.height, 0.001f, code);
            }
        }

        [Test] public void ImportSettingsKeepTheFlagsSharp()
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath("Assets/_Project/Resources/Flags/ng.png");
            Assert.NotNull(imp); Assert.AreEqual(TextureImporterNPOTScale.None, imp.npotScale); Assert.IsTrue(imp.mipmapEnabled); Assert.IsFalse(imp.isReadable);
            Assert.AreEqual(TextureImporterCompression.Uncompressed, imp.textureCompression); Assert.AreEqual(TextureWrapMode.Clamp, imp.wrapMode); Assert.IsTrue(imp.alphaIsTransparency);
        }

        [Test] public void NoCountryAndMalformedAndArtworklessCodesGetNoSprite()
        {
            foreach (var v in new string[] { null, "", " ", "N", "NGA", "N1", "ZZ", "US" }) Assert.IsNull(FlagArt.Get(v), "'" + v + "'");
            Assert.IsNotNull(FlagArt.Get("ng")); Assert.IsNotNull(FlagArt.Get(" NG "));
            Assert.AreSame(FlagArt.Get("NG"), FlagArt.Get("ng"), "one sprite per country, cached");
        }

        [Test] public void FlagDrawsTheFlagOfTheStoredCountryOnly()
        {
            var root = new GameObject("t", typeof(RectTransform));
            try
            {
                Ui.Flag(root.transform, "GH", 10, 10, 32); var gh = root.transform.Find("Flag_GH"); Assert.NotNull(gh); var img = gh.Find("Flag").GetComponent<UnityEngine.UI.Image>();
                Assert.AreSame(FlagArt.Get("GH"), img.sprite); Assert.AreNotSame(FlagArt.Get("NG"), img.sprite); Assert.IsNull(gh.Find("NoFlag"));
                var rt = (RectTransform)gh; Assert.AreEqual(34f, rt.rect.width, 0.01f); Assert.AreEqual(26f, rt.rect.height, 0.01f);       // 32x24 plus the hairline frame
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test] public void MissingCountryDrawsTheNeutralPlaceholderNotAGuess()
        {
            var root = new GameObject("t", typeof(RectTransform));
            try
            {
                foreach (var v in new string[] { null, "", "NGA", "ZZ" })
                {
                    var before = root.transform.childCount; Ui.Flag(root.transform, v, 0, 0, 28); var f = root.transform.GetChild(before);
                    Assert.NotNull(f.Find("NoFlag"), "'" + v + "' draws the placeholder"); Assert.IsNull(f.Find("Flag"), "'" + v + "' must not draw any flag");
                }
                Assert.NotNull(root.transform.Find("Flag_none")); Assert.NotNull(root.transform.Find("Flag_ZZ"));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test] public void CountryTagShowsTheCodeOrTheMissingText()
        {
            var root = new GameObject("t", typeof(RectTransform));
            try
            {
                float w = Ui.CountryTag(root.transform, "NG", 0, 0, 28, 18, Color.white); Assert.Greater(w, 28f);
                float none = Ui.CountryTag(root.transform, "", 0, 0, 28, 18, Color.white); Assert.AreEqual(28f, none, 0.01f, "no country and no text: the placeholder alone");
                Ui.CountryTag(root.transform, null, 0, 0, 28, 18, Color.white, "Regular", "Not set");
                var texts = root.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t => t.text).ToArray(); CollectionAssert.AreEqual(new[] { "NG", "Not set" }, texts);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
