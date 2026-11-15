using System;
using System.Collections.Generic;
using ARO.NetCore;
using Xunit;


/// <summary>The colours of the custom gradient sky. These pin the properties the procedural sky could not give (an independent warm horizon and a clean blue zenith).</summary>
public class SkyGradientModelTests
{
    static SkyGradientState At(float hour, float overcast = 0f) => SkyGradientModel.Evaluate(LightingModel.Evaluate(hour, overcast));
    static float Sat(Rgb c) { float mx = Math.Max(c.R, Math.Max(c.G, c.B)), mn = Math.Min(c.R, Math.Min(c.G, c.B)); return mx <= 0f ? 0f : (mx - mn) / mx; }
    static float Luma(Rgb c) => 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;
    static float Warm(Rgb c) => c.R / Math.Max(c.B, 0.001f);

    [Fact]
    public void EveryColourIsFiniteAndInRangeAtEveryHourAndWeather()
    {
        foreach (float oc in new[] { 0f, 0.5f, 1f })
            for (float h = 0f; h < 24f; h += 0.1f)
            {
                var s = At(h, oc);
                foreach (var c in new[] { s.Zenith, s.Mid, s.Horizon, s.Ground, s.Glow })
                {
                    Assert.True(c.IsFinite, $"hour {h}");
                    Assert.InRange(c.R, 0f, 1.001f); Assert.InRange(c.G, 0f, 1.001f); Assert.InRange(c.B, 0f, 1.001f);
                }
                Assert.InRange(s.GlowStrength, 0f, 1f); Assert.InRange(s.DiscStrength, 0f, 1f);
            }
    }

    [Theory]
    [InlineData(6.4f)] [InlineData(9f)] [InlineData(12f)] [InlineData(15f)] [InlineData(17.6f)]
    public void TheZenithStaysBlueInDaylightAndAtTheGoldenHours(float hour)
    {
        var z = At(hour).Zenith;
        Assert.True(z.B >= z.R + 0.15f, $"hour {hour}: zenith ({z.R:0.00},{z.G:0.00},{z.B:0.00}) is not blue");
        Assert.True(Sat(z) >= 0.40f, $"hour {hour}: zenith saturation {Sat(z):0.00} (the procedural sky measured 0.15 to 0.19 here)");
    }

    [Fact]
    public void TheHorizonIsWarmAtDawnAndWarmerAtSunset()
    {
        var dawn = At(LightingHour.Dawn).Horizon; var dusk = At(LightingHour.Sunset).Horizon;
        Assert.True(Warm(dawn) >= 1.4f, $"dawn horizon R/B {Warm(dawn):0.00}");
        Assert.True(Warm(dusk) >= 1.8f, $"sunset horizon R/B {Warm(dusk):0.00}");
        Assert.True(Warm(dusk) > Warm(dawn) + 0.3f, "sunset must read deeper than dawn");
        Assert.True(Sat(dusk) > Sat(At(LightingHour.Noon).Horizon) + 0.3f, "a golden horizon is far more saturated than the noon haze");
    }

    [Fact]
    public void DawnAndSunsetAreDifferentSkies()
    {
        var a = At(LightingHour.Dawn); var b = At(LightingHour.Sunset);
        float d = Math.Abs(a.Horizon.R - b.Horizon.R) + Math.Abs(a.Horizon.G - b.Horizon.G) + Math.Abs(a.Horizon.B - b.Horizon.B)
                + Math.Abs(a.Zenith.R - b.Zenith.R) + Math.Abs(a.Zenith.G - b.Zenith.G) + Math.Abs(a.Zenith.B - b.Zenith.B);
        Assert.True(d > 0.4f, $"dawn and sunset differ by only {d:0.00}");
    }

    [Fact]
    public void TheMidSkyBetweenAGoldHorizonAndABlueZenithIsNeitherMuddyNorGreen()
    {
        foreach (float h in new[] { LightingHour.Dawn, LightingHour.Sunset })
        {
            var m = At(h).Mid;
            Assert.True(m.B >= m.G - 0.02f && m.B >= m.R - 0.12f, $"hour {h}: mid sky ({m.R:0.00},{m.G:0.00},{m.B:0.00}) is not blue-ish (an olive or grey mid sky is what a straight gold-to-blue blend gives)");
            Assert.True(Sat(m) >= 0.15f, $"hour {h}: mid sky saturation {Sat(m):0.00}");
        }
    }

    [Fact]
    public void NightIsDarkButNotBlack()
    {
        var s = At(LightingHour.Night);
        Assert.InRange(Luma(s.Zenith), 0.05f, 0.20f);
        Assert.True(Luma(s.Horizon) > Luma(s.Zenith), "the night horizon is lighter than the zenith");
        Assert.Equal(0f, s.GlowStrength); Assert.Equal(0f, s.DiscStrength);
    }

    [Fact]
    public void CloudWashesTheColourOut()
    {
        foreach (float h in new[] { LightingHour.Noon, LightingHour.Sunset })
            Assert.True(Sat(At(h, 1f).Zenith) < Sat(At(h, 0f).Zenith) * 0.6f, $"hour {h}");
    }

    [Fact]
    public void TheSunGlowIsStrongestAtTheGoldenHours()
    {
        float noon = At(LightingHour.Noon).GlowStrength, dawn = At(LightingHour.Dawn).GlowStrength, dusk = At(LightingHour.Sunset).GlowStrength;
        Assert.True(dawn > noon * 2f && dusk > noon * 2f, $"glow noon {noon:0.00} dawn {dawn:0.00} sunset {dusk:0.00}");
    }

    [Fact]
    public void TheSkyChangesSmoothlyWithTheHour()
    {
        SkyGradientState prev = null; float worst = 0f;
        for (float h = 0f; h < 24f; h += 1f / 60f)
        {
            var s = At(h);
            if (prev != null)
                foreach (var (a, b) in new[] { (prev.Zenith, s.Zenith), (prev.Mid, s.Mid), (prev.Horizon, s.Horizon) })
                    worst = Math.Max(worst, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))));
            prev = s;
        }
        Assert.True(worst < 0.05f, $"largest one-minute colour step {worst:0.000}");   // the same limit LightingModelTests uses for the lighting itself
    }

    // What the chase camera sees (fitted to CI frames, see SkyGradientModel.DisplaySaturation): the upper part of the frame samples the sky near sin(elevation) 0.37, the band above the horizon near 0.04.
    const float SkyTop = 0.37f, SkyHorizonBand = 0.04f;
    static float DisplayedSat(Rgb c) { c = SkyGradientModel.Displayed(c); float mx = Math.Max(c.R, Math.Max(c.G, c.B)), mn = Math.Min(c.R, Math.Min(c.G, c.B)); return mx <= 0f ? 0f : (mx - mn) / mx; }

    [Theory]
    [InlineData(6.4f)] [InlineData(17.6f)]
    public void TheVisibleUpperSkyIsClearlyBlueAtTheGoldenHours(float hour)
    {
        var c = SkyGradientModel.ColourAt(At(hour), SkyTop);
        Assert.True(DisplayedSat(c) >= 0.36f, $"hour {hour}: displayed saturation {DisplayedSat(c):0.00} (CI criterion 0.30, kept 0.06 above it)");
        Assert.True(Warm(SkyGradientModel.Displayed(c)) <= 0.85f, $"hour {hour}: displayed R/B {Warm(SkyGradientModel.Displayed(c)):0.00}");
    }

    [Fact]
    public void TheVisibleNoonSkyIsBlue()
    {
        var c = SkyGradientModel.ColourAt(At(LightingHour.Noon), SkyTop);
        Assert.True(DisplayedSat(c) >= 0.42f, $"displayed saturation {DisplayedSat(c):0.00} (CI criterion 0.35)");
        Assert.True(Warm(SkyGradientModel.Displayed(c)) <= 0.70f);
    }

    [Fact]
    public void TheBandAboveTheHorizonIsWarmAtDawnAndSunset()
    {
        float dawn = Warm(SkyGradientModel.Displayed(SkyGradientModel.ColourAt(At(LightingHour.Dawn), SkyHorizonBand)));
        float dusk = Warm(SkyGradientModel.Displayed(SkyGradientModel.ColourAt(At(LightingHour.Sunset), SkyHorizonBand)));
        Assert.True(dawn >= 1.6f, $"dawn band R/B {dawn:0.00} (CI criterion 1.4)");
        Assert.True(dusk >= 2.2f, $"sunset band R/B {dusk:0.00} (CI criterion 1.8)");
        Assert.True(dusk - dawn >= 0.6f, $"sunset is only {dusk - dawn:0.00} warmer than dawn (CI criterion 0.5)");
    }
    // ---- the clouds of ARO/SkyClouds (owner direction of 2026-11-15: a realistic sky)
    static SkyCloudState Clouds(float hour, float overcast = 0f) { var st = LightingModel.Evaluate(hour, overcast); return SkyCloudModel.Evaluate(st, SkyGradientModel.Evaluate(st)); }

    [Fact] public void CloudsAreWhiteOverGreyBlueBasesByDayAndNeverClip()
    {
        var c = Clouds(12f);
        Assert.InRange(Luma(c.Light), 0.90f, 0.95f); Assert.True(Math.Max(c.Light.R, Math.Max(c.Light.G, c.Light.B)) * 255f < 245f, "sunlit white stays clear of the blown-out count (250 of 255)");
        Assert.InRange(Luma(c.Dark), 0.45f, 0.65f); Assert.True(c.Dark.B > c.Dark.R + 0.1f, "the bases are grey-blue");
        Assert.Equal(SkyCloudModel.FairCover, c.Cover, 3); Assert.Equal(0f, c.Stars, 3);
    }

    [Fact] public void TheLowSunPaintsTheCloudsWarmAndDawnDiffersFromDusk()
    {
        var dawn = Clouds(6.4f); var dusk = Clouds(17.6f);
        Assert.True(dawn.Light.R > dawn.Light.B + 0.15f && dusk.Light.R > dusk.Light.B + 0.25f, "warm sunlit sides");
        Assert.True(dusk.Light.B < dawn.Light.B, "dusk is more orange than dawn");
        Assert.True(Luma(dawn.Light) > Luma(dawn.Dark) + 0.2f && Luma(dusk.Light) > Luma(dusk.Dark) + 0.2f);
        Assert.True(Luma(dawn.Light) > 0.7f && Luma(dusk.Light) > 0.65f, "the low sun lights the clouds brightly");
        Assert.True(Math.Max(dawn.Light.R, dusk.Light.R) * 255f < 250f, "and never to a blown-out pixel");
        Assert.True(dawn.Stars < 0.05f && dusk.Stars < 0.05f, "no stars in a golden sky");
    }

    [Fact] public void AtNightTheCloudsAreDimShapesUnderStars()
    {
        var n = Clouds(23f);
        Assert.InRange(Luma(n.Light), 0.2f, 0.4f); Assert.InRange(Luma(n.Dark), 0.08f, 0.2f); Assert.InRange(n.Stars, 0.8f, 0.9f);
        Assert.True(Clouds(23f, 1f).Stars < 0.1f, "no stars through an overcast sky");
    }

    [Fact] public void OvercastFillsTheSkyWithFlatGrey()
    {
        var o = Clouds(12f, 1f);
        Assert.True(o.Cover > 1.1f); Assert.True(Math.Abs(o.Light.R - o.Light.B) < 0.08f, "grey, not blue or warm"); Assert.True(Luma(o.Light) < Luma(Clouds(12f).Light));
    }

    [Fact] public void TheCloudColoursMoveSmoothlyThroughTheDay()
    {
        // Dawn and dusk are fast (the clouds go from night blue to sunlit gold in about half an hour) but never a jump: at steps of 18 seconds nothing moves by more than a fiftieth.
        var prev = Clouds(0f);
        for (float h = 0.005f; h <= 24f; h += 0.005f)
        {
            var c = Clouds(h); Assert.True(c.Light.IsFinite && c.Dark.IsFinite);
            Assert.True(Math.Abs(Luma(c.Light) - Luma(prev.Light)) < 0.02f && Math.Abs(Luma(c.Dark) - Luma(prev.Dark)) < 0.02f && Math.Abs(c.Stars - prev.Stars) < 0.03f, $"a jump at hour {h:0.000}");
            Assert.True(Luma(c.Light) >= Luma(c.Dark)); prev = c;
        }
    }
}
