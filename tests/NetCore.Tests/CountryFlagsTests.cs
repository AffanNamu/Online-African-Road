using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ARO.NetCore;
using Xunit;

namespace NetCore.Tests
{
    public class CountryFlagsTests
    {
        static string Root()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Assets", "_Project", "Scripts"))) d = d.Parent;
            return d?.FullName ?? throw new InvalidOperationException("repository root not found");
        }
        static string FlagsDir() => Path.Combine(Root(), "Assets", "_Project", "Resources", "Flags");
        static string FlagFile(string code) => Path.Combine(FlagsDir(), code.ToLowerInvariant() + ".png");

        // ---------------------------------------------------------------- the stored value -> what is drawn
        [Theory]
        [InlineData("NG", "NG")] [InlineData("ng", "NG")] [InlineData(" gh ", "GH")] [InlineData("Za", "ZA")]
        public void WellFormedCodesAreNormalised(string raw, string want) => Assert.Equal(want, CountryFlags.Normalize(raw));

        [Theory]
        [InlineData(null)] [InlineData("")] [InlineData("  ")] [InlineData("N")] [InlineData("NGA")] [InlineData("N1")] [InlineData("1N")] [InlineData("N G")] [InlineData("--")] [InlineData("é")] [InlineData("Nigeria")]
        public void MalformedCodesAreNotCodes(string raw) => Assert.Null(CountryFlags.Normalize(raw));

        [Fact] public void MissingAndMalformedValuesNeverGetAFlag()
        {
            foreach (var v in new[] { null, "", " ", "N", "NGA", "N1", "--" }) { Assert.False(CountryFlags.HasFlag(v)); Assert.Null(CountryFlags.AssetName(v)); Assert.Null(CountryFlags.ResourcePath(v)); }
            Assert.Equal(FlagState.NotSet, CountryFlags.StateOf(null)); Assert.Equal(FlagState.NotSet, CountryFlags.StateOf("")); Assert.Equal(FlagState.NotSet, CountryFlags.StateOf("   "));
            Assert.Equal(FlagState.Invalid, CountryFlags.StateOf("NGA")); Assert.Equal(FlagState.Invalid, CountryFlags.StateOf("N1"));
        }

        [Fact] public void AWellFormedCodeWithoutArtworkIsNotAFlagEither()
        {
            foreach (var c in new[] { "ZZ", "US", "FR", "XX" }) { Assert.Equal(FlagState.NoArtwork, CountryFlags.StateOf(c)); Assert.Null(CountryFlags.AssetName(c)); Assert.Equal(c, CountryFlags.CodeText(c)); }
        }

        [Fact] public void TheFlagIsChosenOnlyFromTheCode()
        {
            Assert.Equal("ng", CountryFlags.AssetName("NG")); Assert.Equal("ng", CountryFlags.AssetName("ng")); Assert.Equal("gh", CountryFlags.AssetName(" GH "));
            Assert.Equal("Flags/ng", CountryFlags.ResourcePath("NG")); Assert.Equal("Flags/cv", CountryFlags.ResourcePath("CV"));
            Assert.Equal(FlagState.Known, CountryFlags.StateOf("ZA"));
        }

        [Fact] public void TextNeverInventsACountry()
        {
            Assert.Equal("", CountryFlags.CodeText(null)); Assert.Equal("", CountryFlags.CodeText("")); Assert.Equal("", CountryFlags.CodeText("NGA")); Assert.Equal("NG", CountryFlags.CodeText("ng"));
            Assert.Equal("Country not set", CountryFlags.Describe(null, null)); Assert.Equal("Country not set", CountryFlags.Describe("", " "));
            Assert.Equal("Nigeria", CountryFlags.Describe("NG", "Nigeria")); Assert.Equal("NG", CountryFlags.Describe("ng", null)); Assert.Equal("Côte d'Ivoire", CountryFlags.Describe("CI", " Côte d'Ivoire "));
        }

        [Fact] public void TheTallyCountsWhatIsDrawn()
        {
            var t = CountryFlags.Count(new[] { "NG", "gh", null, "", "ZZ", "NGA", "CI" });
            Assert.Equal((3, 1, 3, "ng,gh,ci"), (t.Known, t.NoArtwork, t.NotSet, t.Drawn));
            var none = CountryFlags.Count(null); Assert.Equal((0, 0, 0, ""), (none.Known, none.NoArtwork, none.NotSet, none.Drawn));
        }

        // ---------------------------------------------------------------- the shipped list and the files
        [Fact] public void TheListIsSortedUniqueAndTwoLetters()
        {
            var codes = CountryFlags.Codes.Split(' ');
            Assert.Equal(55, codes.Length); Assert.Equal(codes.OrderBy(c => c, StringComparer.Ordinal), codes); Assert.Equal(codes.Length, codes.Distinct().Count());
            Assert.All(codes, c => { Assert.Equal(c, CountryFlags.Normalize(c)); Assert.True(CountryFlags.HasFlag(c)); });
            Assert.Equal(codes.Length, CountryFlags.All.Count);
        }

        [Fact] public void EveryListedFlagHasItsFileAndNoFileIsUnlisted()
        {
            var onDisk = Directory.GetFiles(FlagsDir(), "*.png").Select(f => Path.GetFileNameWithoutExtension(f).ToUpperInvariant()).OrderBy(c => c, StringComparer.Ordinal).ToArray();
            Assert.Equal(CountryFlags.Codes.Split(' '), onDisk);
        }

        [Fact] public void EveryFlagIs96By72AndDecodes()
        {
            foreach (var c in CountryFlags.All) { var p = TinyPng.Decode(File.ReadAllBytes(FlagFile(c))); Assert.Equal((96, 72), (p.W, p.H)); }
        }

        [Fact] public void NoTwoCountriesShareAFlagFile()
        {
            var hashes = CountryFlags.All.Select(c => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(FlagFile(c))))).ToList();
            Assert.Equal(hashes.Count, hashes.Distinct().Count());
        }

        [Fact] public void EveryFlagWasRenderedFromTheSourceSvgOfItsOwnCode()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(FlagsDir(), "manifest.json"))); var flags = doc.RootElement.GetProperty("flags");
            Assert.Equal("MIT", doc.RootElement.GetProperty("licence").GetString());
            foreach (var c in CountryFlags.All)
            {
                var f = flags.GetProperty(c); Assert.Equal($"flags/4x3/{c.ToLowerInvariant()}.svg", f.GetProperty("svg").GetString());
                Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(FlagFile(c)))).ToLowerInvariant(), f.GetProperty("png_sha256").GetString());
            }
        }

        [Fact] public void LicenceAndSourceAreDocumentedNextToTheAssets()
        {
            var lic = File.ReadAllText(Path.Combine(FlagsDir(), "LICENSE-flag-icons.txt")); Assert.Contains("MIT License", lic); Assert.Contains("Permission is hereby granted", lic);
            var src = File.ReadAllText(Path.Combine(FlagsDir(), "SOURCE.md")); Assert.Contains("flag-icons", src); Assert.Contains("MIT", src); Assert.Contains("96x72", src);
        }

        // ---------------------------------------------------------------- every country the game ships can be drawn
        [Fact] public void EveryCountryTheServerSeedsHasAFlag()
        {
            var codes = new List<string>();
            foreach (var f in new[] { "seed.sql", "seed_world.sql" })
            {
                var sql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, f));
                foreach (Match ins in Regex.Matches(sql, @"insert into countries\(code, name\) values(.*?)(on conflict|;)", RegexOptions.Singleline | RegexOptions.IgnoreCase))
                    foreach (Match m in Regex.Matches(ins.Groups[1].Value, @"\('([A-Z]{2})'")) codes.Add(m.Groups[1].Value);
            }
            Assert.True(codes.Count >= 16, "the seeds list the West African countries");
            Assert.All(codes, c => Assert.True(CountryFlags.HasFlag(c), c + " is seeded as a country but has no flag"));
        }

        [Fact] public void EveryCityOnTheWorldMapHasItsCountryFlag()
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "world_map.json")));
            var countries = doc.RootElement.GetProperty("cities").EnumerateArray().Select(c => c.GetProperty("country").GetString()).Distinct().ToList();
            Assert.NotEmpty(countries); Assert.All(countries, c => Assert.True(CountryFlags.HasFlag(c), "map city country " + c + " has no flag"));
        }

        // ---------------------------------------------------------------- the right picture for the right code (sampled pixels, so a swapped or mis-rendered file fails)
        enum K { White, Black, Red, Green, Yellow, Orange, Blue, Other }
        static K Classify((byte R, byte G, byte B) c)
        {
            int r = c.R, g = c.G, b = c.B;
            if (r > 215 && g > 215 && b > 215) return K.White;
            if (r < 60 && g < 60 && b < 60) return K.Black;
            if (r > 140 && g < 60 && b < 90) return K.Red;
            if (r > 215 && g > 175 && b < 110) return K.Yellow;
            if (r > 200 && g >= 60 && g <= 175 && b < 90) return K.Orange;
            if (g > 90 && r < 110 && b < 120 && g > r + 40) return K.Green;
            if (b > 110 && r < 90 && b > g - 10) return K.Blue;
            return K.Other;
        }
        static K At(string code, double fx, double fy) { var p = TinyPng.Decode(File.ReadAllBytes(FlagFile(code))); return Classify(p.Pixel((int)(fx * (p.W - 1)), (int)(fy * (p.H - 1)))); }

        static K[] Across(string code, double y = .5) => new[] { At(code, .12, y), At(code, .5, y), At(code, .88, y) };
        static K[] Down(string code, double x = .12) => new[] { At(code, x, .1), At(code, x, .5), At(code, x, .9) };

        [Fact] public void VerticalTricolours()
        {
            Assert.Equal(new[] { K.Green, K.White, K.Green }, Across("NG"));       // Nigeria
            Assert.Equal(new[] { K.Orange, K.White, K.Green }, Across("CI"));      // Cote d'Ivoire
            Assert.Equal(new[] { K.Green, K.Yellow, K.Red }, Across("ML"));        // Mali
            Assert.Equal(new[] { K.Red, K.Yellow, K.Green }, Across("GN"));        // Guinea
        }

        [Fact] public void HorizontalTricolours()
        {
            Assert.Equal(new[] { K.Red, K.White, K.Black }, Down("EG"));           // Egypt
            Assert.Equal(new[] { K.Red, K.Blue, K.Green }, Down("GM"));            // The Gambia
            Assert.Equal(new[] { K.Orange, K.White, K.Green }, Down("NE"));        // Niger
            Assert.Equal(new[] { K.Red, K.Yellow, K.Green }, Down("GH"));          // Ghana
            Assert.Equal(new[] { K.Red, K.Green }, new[] { At("BF", .12, .2), At("BF", .12, .85) });   // Burkina Faso
            Assert.Equal(new[] { K.Black, K.Green }, new[] { At("KE", .5, .03), At("KE", .5, .97) });  // Kenya
        }

        [Fact] public void EmblemsAreDrawnOnTheRightField()
        {
            Assert.Equal(K.Black, At("GH", .5, .55));                               // Ghana: the black star on the yellow band
            Assert.Equal(K.Yellow, At("BF", .5, .5));                               // Burkina Faso: the yellow star on red over green
            Assert.Equal(K.Orange, At("NE", .5, .5));                               // Niger: the orange disc on the white band
            Assert.Equal(K.Green, At("SN", .5, .5)); Assert.Equal(K.Yellow, At("SN", .5, .12));   // Senegal: the green star on the yellow band
            Assert.Equal(K.White, At("TN", .5, .3)); Assert.Equal(K.Red, At("TN", .08, .5));      // Tunisia: the white disc on red
            Assert.Equal(K.Green, At("DZ", .08, .5)); Assert.Equal(K.White, At("DZ", .92, .1));   // Algeria: green hoist, white fly
        }
    }

    /// <summary>A minimal PNG reader for the 8-bit RGBA, non-interlaced files the generator writes (zlib from the framework; filters 0-4).</summary>
    static class TinyPng
    {
        public sealed class Image
        {
            public int W, H; public byte[] Rgba = Array.Empty<byte>();
            public (byte R, byte G, byte B) Pixel(int x, int y) { int i = (y * W + x) * 4; return (Rgba[i], Rgba[i + 1], Rgba[i + 2]); }
        }

        public static Image Decode(byte[] png)
        {
            if (png.Length < 200 && System.Text.Encoding.ASCII.GetString(png).StartsWith("version https://git-lfs")) throw new InvalidDataException("the flag file is a Git LFS pointer, not the picture (the Flags folder is meant to be plain git: check .gitattributes)");
            if (png.Length < 33 || png[0] != 0x89 || png[1] != 'P' || png[2] != 'N' || png[3] != 'G') throw new InvalidDataException("not a PNG");
            int pos = 8, w = 0, h = 0; var idat = new MemoryStream(); byte depth = 0, color = 0, interlace = 0;
            while (pos < png.Length)
            {
                int len = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3]; string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4); int data = pos + 8;
                if (type == "IHDR") { w = (png[data] << 24) | (png[data + 1] << 16) | (png[data + 2] << 8) | png[data + 3]; h = (png[data + 4] << 24) | (png[data + 5] << 16) | (png[data + 6] << 8) | png[data + 7]; depth = png[data + 8]; color = png[data + 9]; interlace = png[data + 12]; }
                else if (type == "IDAT") idat.Write(png, data, len);
                else if (type == "IEND") break;
                pos = data + len + 4;
            }
            if (depth != 8 || color != 6 || interlace != 0) throw new InvalidDataException($"expected 8-bit RGBA non-interlaced, got depth {depth} colour {color} interlace {interlace}");
            idat.Position = 0; var raw = new MemoryStream(); using (var z = new ZLibStream(idat, CompressionMode.Decompress)) z.CopyTo(raw); var d = raw.ToArray();
            int stride = w * 4; var px = new byte[h * stride];
            for (int y = 0; y < h; y++)
            {
                int f = d[y * (stride + 1)], src = y * (stride + 1) + 1, dst = y * stride;
                for (int x = 0; x < stride; x++)
                {
                    int a = x >= 4 ? px[dst + x - 4] : 0, b = y > 0 ? px[dst - stride + x] : 0, c = x >= 4 && y > 0 ? px[dst - stride + x - 4] : 0, v = d[src + x];
                    switch (f)
                    {
                        case 0: break; case 1: v += a; break; case 2: v += b; break; case 3: v += (a + b) / 2; break;
                        case 4: { int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c); v += pa <= pb && pa <= pc ? a : pb <= pc ? b : c; break; }
                        default: throw new InvalidDataException("bad PNG filter " + f);
                    }
                    px[dst + x] = (byte)v;
                }
            }
            return new Image { W = w, H = h, Rgba = px };
        }
    }
}
