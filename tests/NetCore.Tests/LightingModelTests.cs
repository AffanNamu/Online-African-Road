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
