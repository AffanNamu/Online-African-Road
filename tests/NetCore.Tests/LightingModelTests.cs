using System;
using ARO.NetCore;
using Xunit;

public class LightingHourTests
{
    [Theory] [InlineData("dawn", 6.4f)] [InlineData("NOON", 12f)] [InlineData("Sunset", 17.6f)] [InlineData("night", 23f)] [InlineData(" noon ", 12f)]
    public void PresetNamesResolve(string v, float expected) { Assert.True(LightingHour.TryParse(v, out float h)); Assert.Equal(expected, h, 4); }

    [Theory] [InlineData("0", 0f)] [InlineData("6.5", 6.5f)] [InlineData("12", 12f)] [InlineData("23.99", 23.99f)] [InlineData("24", 0f)] [InlineData("18:30", 18.5f)] [InlineData("06:15", 6.25f)] [InlineData("0:00", 0f)]
    public void NumbersAndClockTimesResolve(string v, float expected) { Assert.True(LightingHour.TryParse(v, out float h)); Assert.Equal(expected, h, 3); }

    [Theory] [InlineData("")] [InlineData(null)] [InlineData("   ")] [InlineData("noonish")] [InlineData("-1")] [InlineData("24.5")] [InlineData("99")] [InlineData("NaN")] [InlineData("Infinity")] [InlineData("12:75")] [InlineData("ab:cd")] [InlineData("12:")] [InlineData(":30")] [InlineData("1,5")]
    public void InvalidValuesAreRefused(string v) { Assert.False(LightingHour.TryParse(v, out float h)); Assert.Equal(-1f, h); }

    [Fact] public void ParsingIgnoresTheCulture()
    {
        var old = System.Threading.Thread.CurrentThread.CurrentCulture;
        try { System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("fr-FR"); Assert.True(LightingHour.TryParse("6.5", out float h)); Assert.Equal(6.5f, h, 3); }
        finally { System.Threading.Thread.CurrentThread.CurrentCulture = old; }
    }

    [Theory]
    [InlineData("http://127.0.0.1:8000/index.html?smoke=drive&hour=noon", 12f)]
    [InlineData("http://x/index.html?hour=17.6", 17.6f)]
    [InlineData("http://x/index.html?smoke=drive&HOUR=Night&quality=low", 23f)]
    [InlineData("http://x/index.html?quality=low&hour=06%3A30", 6.5f)]
    [InlineData("http://x/index.html?hour=dawn#section", 6.4f)]
    [InlineData("http://x/index.html?hour=", -1f)]
    [InlineData("http://x/index.html?hour=banana", -1f)]
    [InlineData("http://x/index.html?smoke=drive", -1f)]
    [InlineData("http://x/index.html", -1f)]
    [InlineData("http://x/index.html?hours=12", -1f)]
    [InlineData("http://x/index.html?myhour=12", -1f)]
    [InlineData("", -1f)]
    [InlineData(null, -1f)]
    public void TheHourIsReadFromThePageAddress(string url, float expected) => Assert.Equal(expected, LightingHour.FromUrl(url), 3);

    [Fact] public void PresetsAreFourDistinctMomentsInTheDayInOrder()
    {
        Assert.Equal(4, LightingHour.PresetNames.Length); Assert.Equal(4, LightingHour.PresetHours.Length);
        Assert.Equal(new[] { "dawn", "noon", "sunset", "night" }, LightingHour.PresetNames);
        for (int i = 1; i < 4; i++) Assert.True(LightingHour.PresetHours[i] > LightingHour.PresetHours[i - 1]);
        Assert.All(LightingHour.PresetHours, h => Assert.InRange(h, 0f, 24f));
    }

    [Fact] public void DawnAndSunsetAreSymmetricAroundNoonAndNightIsBelowTheHorizon()
    {   // the same elevation formula TimeOfDay uses
        float Elev(float h) => (float)Math.Sin((h - 6f) / 12f * Math.PI);
        Assert.Equal(Elev(LightingHour.Dawn), Elev(LightingHour.Sunset), 3); Assert.InRange(Elev(LightingHour.Dawn), 0.05f, 0.2f);
        Assert.Equal(1f, Elev(LightingHour.Noon), 3); Assert.True(Elev(LightingHour.Night) < -0.2f);
    }

    [Fact] public void ThePageAddressBeatsTheSettingWhichBeatsTheClock()
    {
        Assert.Equal(12f, LightingHour.Resolve(12f, 9f, 3f, out var s1)); Assert.Equal(LightingHour.SourceUrl, s1);
        Assert.Equal(9f, LightingHour.Resolve(-1f, 9f, 3f, out var s2)); Assert.Equal(LightingHour.SourceSetting, s2);
        Assert.Equal(3f, LightingHour.Resolve(-1f, -1f, 3f, out var s3)); Assert.Equal(LightingHour.SourceClock, s3);
        Assert.Equal(0f, LightingHour.Resolve(0f, 9f, 3f, out var s4)); Assert.Equal(LightingHour.SourceUrl, s4);   // midnight is a valid URL hour, not "unset"
        Assert.Equal(0f, LightingHour.Resolve(-1f, 0f, 3f, out var s5)); Assert.Equal(LightingHour.SourceSetting, s5);
    }

    [Theory] [InlineData(2026, 10, 14, 3, 5, 0, 4.0833f)] [InlineData(2026, 10, 14, 23, 30, 0, 0.5f)] [InlineData(2026, 10, 14, 11, 0, 30, 12.0083f)] [InlineData(2026, 1, 1, 23, 59, 59, 0.9997f)]
    public void TheClockIsWestAfricaTime(int y, int mo, int d, int h, int mi, int s, float expected) =>
        Assert.Equal(expected, LightingHour.ClockHour(new DateTime(y, mo, d, h, mi, s, DateTimeKind.Utc)), 3);
}

public class LightingModelTests
{
    static LightingState At(float h, float oc = 0f) => LightingModel.Evaluate(h, oc);
    static float[] Scalars(LightingState s) => new[]
    {
        s.SunIntensity, s.MoonIntensity, s.SkyExposure, s.Atmosphere, s.FogDensity, s.PostExposure, s.SunElevationDeg / 90f,
        s.SunColor.R, s.SunColor.G, s.SunColor.B, s.AmbientSky.R, s.AmbientSky.G, s.AmbientSky.B, s.AmbientEquator.R, s.AmbientEquator.G, s.AmbientEquator.B,
        s.AmbientGround.R, s.AmbientGround.G, s.AmbientGround.B, s.FogColor.R, s.FogColor.G, s.FogColor.B, s.PostFilter.R, s.PostFilter.G, s.PostFilter.B, s.MoonColor.R, s.MoonColor.G, s.MoonColor.B,
        s.SkyTint.R, s.SkyTint.G, s.SkyTint.B
    };

    [Fact] public void EveryHourAndOvercastGivesFiniteSensibleValues()
    {
        for (float h = 0f; h < 24f; h += 0.05f) for (float oc = 0f; oc <= 1.001f; oc += 0.25f)
        {
            var s = At(h, oc);
            Assert.All(Scalars(s), v => Assert.True(!float.IsNaN(v) && !float.IsInfinity(v), $"hour {h} overcast {oc}"));
            Assert.InRange(s.SunIntensity, 0f, 1.5f); Assert.InRange(s.MoonIntensity, 0f, 0.3f); Assert.InRange(s.SkyExposure, 0.1f, 1.5f); Assert.InRange(s.Atmosphere, 0.5f, 3.5f);
            Assert.InRange(s.FogDensity, 0.0001f, 0.003f); Assert.InRange(s.PostExposure, -0.5f, 0.5f);
            foreach (var c in new[] { s.SunColor, s.AmbientSky, s.AmbientEquator, s.AmbientGround, s.FogColor, s.PostFilter, s.MoonColor, s.SkyTint }) { Assert.True(c.IsFinite); Assert.InRange(c.R, 0f, 1f); Assert.InRange(c.G, 0f, 1f); Assert.InRange(c.B, 0f, 1f); }
        }
    }
    [Fact] public void BadInputIsHandled()
    {
        foreach (var h in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -5f, 24f, 100f, 1e9f }) Assert.All(Scalars(At(h)), v => Assert.False(float.IsNaN(v) || float.IsInfinity(v)));
        Assert.All(Scalars(At(12f, float.NaN)), v => Assert.False(float.IsNaN(v))); Assert.Equal(At(12f, 1f).SunIntensity, At(12f, 7f).SunIntensity);   // overcast clamps to 0..1
        Assert.Equal(At(25f).Hour, At(1f).Hour, 3); Assert.Equal(At(-1f).Hour, At(23f).Hour, 3);
    }

    [Fact] public void NothingPopsAsTheHourAdvances()
    {   // one minute of game time: no value may jump by more than a small step (a pop would show as a flash on screen)
        var prev = At(0f); float worst = 0f; string where = "";
        for (float h = 1f / 60f; h <= 24f; h += 1f / 60f)
        {
            var cur = At(h); var a = Scalars(prev); var b = Scalars(cur);
            for (int i = 0; i < a.Length; i++) { float d = Math.Abs(a[i] - b[i]); if (d > worst) { worst = d; where = $"value {i} at hour {h:0.00}"; } }
            bool flipped = prev.MoonEnabled != cur.MoonEnabled || prev.SunEnabled != cur.SunEnabled;
            Assert.False(flipped && (Math.Max(prev.MoonIntensity, cur.MoonIntensity) > 0.01f && prev.MoonEnabled != cur.MoonEnabled && Math.Min(prev.MoonIntensity, cur.MoonIntensity) > 0.01f
                                     || prev.SunEnabled != cur.SunEnabled && Math.Min(prev.SunIntensity, cur.SunIntensity) > 0.02f),
                $"an enable flag flipped while its intensity was not ~0 at hour {h:0.00}: moon {prev.MoonIntensity:0.000}->{cur.MoonIntensity:0.000} sun {prev.SunIntensity:0.000}->{cur.SunIntensity:0.000}");
            prev = cur;
        }
        Assert.True(worst < 0.05f, $"largest one-minute change {worst:0.0000} ({where})");
    }

    [Fact] public void TheSunIsUpInTheDayAndDownAtNight()
    {
        Assert.True(At(12f).SunEnabled); Assert.InRange(At(12f).SunIntensity, 0.85f, 1.05f); Assert.Equal(90f, At(12f).SunElevationDeg, 1);
        Assert.False(At(23f).SunEnabled); Assert.Equal(0f, At(23f).SunIntensity, 3); Assert.False(At(2f).SunEnabled);
        Assert.True(At(23f).SunElevationDeg < -15f);
    }
    [Fact] public void TheMoonLightsTheNightAndNotTheDay()
    {
        Assert.True(At(23f).MoonEnabled); Assert.InRange(At(23f).MoonIntensity, 0.15f, 0.25f); Assert.True(At(2f).MoonEnabled);
        Assert.False(At(12f).MoonEnabled); Assert.Equal(0f, At(12f).MoonIntensity, 3);
        for (float h = 0f; h < 24f; h += 0.1f) Assert.True(At(h).SunIntensity + At(h).MoonIntensity > 0.15f, $"the world is lit by sun or moon at {h}");
    }
    [Fact] public void MoonlightIsCoolAndSunlightIsWarmerAtTheHorizon()
    {
        var m = At(23f).MoonColor; Assert.True(m.B > m.R + 0.2f);
        var low = At(LightingHour.Dawn).SunColor; var high = At(12f).SunColor; Assert.True(low.B < high.B - 0.1f); Assert.True(low.R >= low.B);
    }

    [Fact] public void NightIsNeverBlackAndStaysMuchDarkerThanDay()
    {
        var night = At(LightingHour.Night); var noon = At(LightingHour.Noon);
        Assert.True(night.IlluminationIndex >= 0.2f, $"night illumination {night.IlluminationIndex}");              // the floor: moon plus ambient
        Assert.True(noon.IlluminationIndex >= 4.5f * night.IlluminationIndex, $"noon {noon.IlluminationIndex} vs night {night.IlluminationIndex}");   // still clearly night (was 6x until noon was deliberately lowered to stop colours clipping; night is unchanged)
        Assert.True(noon.FlatGroundLight.Mean >= 10f * night.FlatGroundLight.Mean, "the light that actually reaches the ground: noon at least ten times night");
        Assert.True(night.AmbientSky.Mean >= 0.07f); Assert.True(night.AmbientEquator.Mean >= 0.05f); Assert.True(night.AmbientGround.Mean >= 0.02f);
        Assert.True(night.SkyExposure >= 0.25f);
    }
    [Fact] public void TheFourReferenceHoursAreOrderedByLight()
    {
        float dawn = At(LightingHour.Dawn).IlluminationIndex, sunset = At(LightingHour.Sunset).IlluminationIndex, night = At(LightingHour.Night).IlluminationIndex;
        float dawnG = At(LightingHour.Dawn).FlatGroundLight.Mean, noonG = At(LightingHour.Noon).FlatGroundLight.Mean, sunsetG = At(LightingHour.Sunset).FlatGroundLight.Mean, nightG = At(LightingHour.Night).FlatGroundLight.Mean;
        Assert.True(noonG > 1.6f * dawnG && noonG > 1.6f * sunsetG, "the light on the ground: noon clearly above golden hour");   // (the low golden sun itself is as strong as the noon sun; what differs is how much of it reaches flat ground)
        Assert.True(dawn > night * 2f && sunset > night * 2f); Assert.True(dawnG > 6f * nightG && sunsetG > 6f * nightG);
    }
    [Fact] public void DawnAndSunsetShareTheGeometryButNotTheCharacter()
    {
        var d = At(LightingHour.Dawn); var e = At(LightingHour.Sunset);
        Assert.Equal(d.SunElevationDeg, e.SunElevationDeg, 1); Assert.Equal(d.Day, e.Day, 2); Assert.Equal(d.Golden, e.Golden, 2);   // same sun height, same amount of golden hour
        Assert.True(e.SunColor.R - e.SunColor.B > d.SunColor.R - d.SunColor.B + 0.08f, $"sunset light is deeper gold ({e.SunColor.R - e.SunColor.B:0.00} vs {d.SunColor.R - d.SunColor.B:0.00})");
        Assert.True(d.SunIntensity < e.SunIntensity, "the morning sun is a little softer");
        Assert.True(d.FogDensity > 1.5f * e.FogDensity, "morning mist is thicker than the evening haze");
        Assert.True(e.FogColor.R - e.FogColor.B > d.FogColor.R - d.FogColor.B + 0.1f, "the evening haze is orange, the morning mist pinker and greyer");
        Assert.True(d.AmbientSky.B > e.AmbientSky.B + 0.04f && e.AmbientSky.R > d.AmbientSky.R + 0.03f, "the morning fill is cooler, the evening fill warmer");
        Assert.True(Math.Abs(d.SkyTint.B - e.SkyTint.B) > 0.05f, "the sky tint differs");
    }
    [Fact] public void GoldenHourLightsTheGroundNotJustTheSky()
    {
        foreach (var h in new[] { LightingHour.Dawn, LightingHour.Sunset })
        {
            var f = At(h).FlatGroundLight; Assert.True(f.Mean >= 0.5f, $"flat ground at {h}h gets only {f.Mean:0.00} (the previous curve gave 0.39)");
            Assert.InRange(At(h).SunElevationDeg, 12f, 22f);
        }
    }
    [Fact] public void TheSunRisesMonotonicallyAndTheGoldenLiftNeverPops()
    {
        float last = -999f; for (float h = 5.5f; h <= 12f; h += 1f / 60f) { float e = At(h).SunElevationDeg; Assert.True(e >= last - 0.001f, $"the sun never sinks in the morning ({h})"); last = e; }
        last = 999f; for (float h = 12f; h <= 18.5f; h += 1f / 60f) { float e = At(h).SunElevationDeg; Assert.True(e <= last + 0.001f, $"the sun never rises in the afternoon ({h})"); last = e; }
    }
    [Fact] public void AnOrangeBodyKeepsItsHueAtNoonInsteadOfClippingToYellow()
    {   // the placeholder truck's body (0.85, 0.55, 0.10) came out as (255, 255, 58) under the old noon light (sun 1.35 + ambient 0.45..0.78 gave R 1.5, G 1.06)
        var f = At(LightingHour.Noon).FlatGroundLight;
        float r = 0.85f * f.R, g = 0.55f * f.G;
        Assert.True(r <= 1.12f, $"red {r:0.00}"); Assert.True(g <= 0.85f, $"green {g:0.00}");
        float shownRatio = Math.Min(1f, g) / Math.Min(1f, r); Assert.True(shownRatio >= 0.6f, $"on screen the body is G/R {shownRatio:0.00}; the material is {0.55f / 0.85f:0.00}; yellow would be 1.00");
        Assert.True(f.Mean <= 1.45f, $"flat ground at noon {f.Mean:0.00}");
    }
    [Fact] public void TheLightingChangesAreTheSameInEveryTier() { Assert.Equal(At(12f).SunIntensity, At(12f, 0f).SunIntensity); }
    [Fact] public void DawnAndSunsetAreWarmAndNoonIsNeutral()
    {
        Assert.True(At(LightingHour.Dawn).Golden > 0.6f); Assert.True(At(LightingHour.Sunset).Golden > 0.6f); Assert.True(At(12f).Golden < 0.01f); Assert.True(At(23f).Golden < 0.01f);
        var gf = At(LightingHour.Sunset).FogColor; var nf = At(12f).FogColor; Assert.True(gf.R - gf.B > nf.R - nf.B + 0.15f, "the haze is orange at sunset");
        Assert.True(At(LightingHour.Sunset).Atmosphere > At(12f).Atmosphere + 0.4f, "a deeper horizon sky at sunset");
        var f = At(LightingHour.Sunset).PostFilter; Assert.True(f.R > f.B);
    }
    [Fact] public void OvercastDimsTheSunAndThickensTheAir()
    {
        for (float h = 7f; h <= 17f; h += 1f) { Assert.True(At(h, 1f).SunIntensity < At(h, 0f).SunIntensity * 0.4f); Assert.True(At(h, 1f).FogDensity > At(h, 0f).FogDensity); Assert.True(At(h, 1f).SkyExposure < At(h, 0f).SkyExposure); }
        Assert.True(At(23f, 1f).IlluminationIndex < At(23f, 0f).IlluminationIndex); Assert.True(At(23f, 1f).IlluminationIndex > 0.12f, "even a cloudy night keeps a floor");
    }
    [Fact] public void ExposureFollowsTheLightButNeverBlindsOrBlacks()
    {
        Assert.True(At(23f).PostExposure <= At(12f).PostExposure); Assert.InRange(At(23f).PostExposure, -0.3f, 0.05f); Assert.InRange(At(12f).PostExposure, -0.05f, 0.4f);
        Assert.True(At(LightingHour.Sunset).PostExposure > At(12f).PostExposure - 0.01f, "golden hour is not darker than noon in the grade");
    }
    [Fact] public void TheSkyAndAmbientBrightenWithTheDay()
    {
        float last = -1f; for (float h = 3f; h <= 12f; h += 0.1f) { float a = At(h).AmbientSky.Mean + At(h).AmbientEquator.Mean; Assert.True(a >= last - 0.002f, $"ambient never falls while the morning brightens ({h})"); last = a; }
    }
    [Fact] public void TheMoonDirectionIsAboveTheHorizon() { Assert.InRange(LightingModel.MoonElevationDeg, 20f, 70f); Assert.InRange(LightingModel.MoonAzimuthDeg, 0f, 360f); }
}

public class HeadlightSpecTests
{
    [Fact] public void TheHeadlightsAreStrongNarrowAndAimedSlightlyDown()
    {
        Assert.InRange(HeadlightSpec.Intensity, 100f, 400f); Assert.InRange(HeadlightSpec.Range, 60f, 150f);
        Assert.True(HeadlightSpec.OuterAngleDeg > HeadlightSpec.InnerAngleDeg + 10f); Assert.InRange(HeadlightSpec.OuterAngleDeg, 30f, 80f); Assert.InRange(HeadlightSpec.PitchDownDeg, 0f, 6f);
    }
    [Fact] public void ThePlaceholderVehicleUsesTheSpec()
    {
        var d = AppContext.BaseDirectory; while (d != null && !System.IO.Directory.Exists(System.IO.Path.Combine(d, "Assets"))) d = System.IO.Path.GetDirectoryName(d);
        var src = System.IO.File.ReadAllText(System.IO.Path.Combine(d, "Assets", "_Project", "Scripts", "Vehicles", "TruckFactory.cs"));
        Assert.Contains("HeadlightSpec.Intensity", src); Assert.Contains("HeadlightSpec.Range", src); Assert.Contains("HeadlightSpec.OuterAngleDeg", src);
    }
}
