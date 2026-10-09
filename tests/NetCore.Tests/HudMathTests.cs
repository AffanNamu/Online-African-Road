using ARO.NetCore;
using Xunit;

public class GaugeTests
{
    [Theory] [InlineData(0f, 0f)] [InlineData(50f, 0.5f)] [InlineData(100f, 1f)] [InlineData(250f, 1f)] [InlineData(-5f, 0f)]
    public void FractionClamps(float v, float expected) => Assert.Equal(expected, Gauge.Fraction(v, 0f, 100f), 4);
    [Fact] public void NanAndDegenerateRangeAreZero() { Assert.Equal(0f, Gauge.Fraction(float.NaN, 0, 100)); Assert.Equal(0f, Gauge.Fraction(5, 10, 10)); }
    [Fact] public void AngleSweepsFromStartToEnd()
    {
        Assert.Equal(225f, Gauge.Angle(0, 0, 120, 225f, -45f), 3);
        Assert.Equal(-45f, Gauge.Angle(120, 0, 120, 225f, -45f), 3);
        Assert.Equal(90f, Gauge.Angle(60, 0, 120, 225f, -45f), 3);
    }
}

public class TripFormatTests
{
    [Theory] [InlineData(0f, "0 m")] [InlineData(85f, "90 m")] [InlineData(999f, "1.0 km")] [InlineData(1000f, "1.0 km")]
    [InlineData(3400f, "3.4 km")] [InlineData(9949f, "9.9 km")] [InlineData(12000f, "12 km")] [InlineData(327000f, "327 km")]
    public void Distance(float m, string expected) => Assert.Equal(expected, TripFormat.Distance(m));
    [Fact] public void DistanceSanitisesBadInput() { Assert.Equal("0 m", TripFormat.Distance(float.NaN)); Assert.Equal("0 m", TripFormat.Distance(-50f)); }
    [Theory] [InlineData(5f, "< 1 min")] [InlineData(60f, "1 min")] [InlineData(750f, "13 min")] [InlineData(3600f, "1h 00m")] [InlineData(10080f, "2h 48m")]
    public void Eta(float s, string expected) => Assert.Equal(expected, TripFormat.Eta(s));
    [Fact] public void EtaHandlesInvalid() { Assert.Equal("--", TripFormat.Eta(float.NaN)); Assert.Equal("--", TripFormat.Eta(float.PositiveInfinity)); Assert.Equal("--", TripFormat.Eta(-1f)); }
    [Fact] public void MoneyHasThousandsSeparatorsAndIsCultureInvariant() => Assert.Equal("482,750", TripFormat.Money(482750));
}

public class EtaEstimatorTests
{
    [Fact] public void ZeroDistanceIsZero() => Assert.Equal(0f, EtaEstimator.Seconds(0f, 10f));
    [Fact] public void StoppedStillGivesFiniteEta() => Assert.InRange(EtaEstimator.Seconds(10000f, 0f), 700f, 900f);
    [Fact] public void FasterCurrentSpeedShortensEta() => Assert.True(EtaEstimator.Seconds(10000f, 30f) < EtaEstimator.Seconds(10000f, 0f));
    [Fact] public void NeverDividesByTinySpeeds() => Assert.True(EtaEstimator.Seconds(1000f, 0f, 0f) <= 1000f / 3f + 0.01f);
}

public class MinimapMathTests
{
    [Fact] public void TargetAheadOfNorthFacingVehicleIsUp()
    {
        Assert.True(MinimapMath.ToMap(0, 100, 0, 0, 0f, 200f, true, out float x, out float y));
        Assert.Equal(0f, x, 3); Assert.Equal(0.5f, y, 3);
    }
    [Fact] public void RotatingKeepsAheadUpWhenHeadingEast()
    {
        // vehicle faces +X (heading 90); a target 100 m along +X is "ahead" so it must map to +Y.
        Assert.True(MinimapMath.ToMap(100, 0, 0, 0, 90f, 200f, true, out float x, out float y));
        Assert.Equal(0f, x, 3); Assert.Equal(0.5f, y, 3);
    }
    [Fact] public void TargetToTheRightMapsRight()
    {
        MinimapMath.ToMap(100, 0, 0, 0, 0f, 200f, true, out float x, out float y);   // heading north, target east = right
        Assert.Equal(0.5f, x, 3); Assert.Equal(0f, y, 3);
    }
    [Fact] public void OutsideRadiusReportsFalseAndClampsToEdge()
    {
        Assert.False(MinimapMath.ToMap(0, 1000, 0, 0, 0f, 200f, false, out float x, out float y));
        MinimapMath.ClampToEdge(ref x, ref y);
        Assert.Equal(0.92f, (float)System.Math.Sqrt(x * x + y * y), 3);
    }
    [Fact] public void NonRotatingMapIsNorthUp()
    {
        MinimapMath.ToMap(0, 100, 0, 0, 123f, 200f, false, out float x, out float y);
        Assert.Equal(0f, x, 3); Assert.Equal(0.5f, y, 3);
    }
}

public class NotificationQueueTests
{
    [Fact] public void ExpiresByTime()
    {
        var q = new NotificationQueue(); q.Push("hi", Severity.Info, 2f);
        q.Tick(1f); Assert.Equal(1, q.Count); q.Tick(1.1f); Assert.Equal(0, q.Count);
    }
    [Fact] public void DuplicateRefreshesInsteadOfStacking()
    {
        var q = new NotificationQueue(); q.Push("Low fuel", Severity.Warning, 2f); q.Tick(1.5f); q.Push("Low fuel", Severity.Warning, 2f);
        Assert.Equal(1, q.Count); q.Tick(1.5f); Assert.Equal(1, q.Count);
    }
    [Fact] public void CapsVisibleDroppingOldest()
    {
        var q = new NotificationQueue(2); q.Push("a"); q.Push("b"); q.Push("c");
        Assert.Equal(2, q.Count); Assert.Equal("b", q.Visible[0].Text); Assert.Equal("c", q.Visible[1].Text);
    }
    [Fact] public void BlankTextIgnored() { var q = new NotificationQueue(); q.Push(""); q.Push("  "); q.Push(null); Assert.Equal(0, q.Count); }
}
