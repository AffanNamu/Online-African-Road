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
}
