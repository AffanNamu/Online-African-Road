using ARO.NetCore;
using Xunit;

public class VehicleNetCodecTests
{
    [Fact] public void FlagsRoundTrip()
    {
        byte f = VehicleNetCodec.PackFlags(true, -1, true, false, true);
        Assert.True(VehicleNetCodec.Has(f, VehicleNetCodec.Lights)); Assert.True(VehicleNetCodec.Has(f, VehicleNetCodec.Horn));
        Assert.False(VehicleNetCodec.Has(f, VehicleNetCodec.Brake)); Assert.True(VehicleNetCodec.Has(f, VehicleNetCodec.Reverse));
        Assert.Equal(-1, VehicleNetCodec.IndicatorOf(f));
    }
    [Fact] public void IndicatorsAreMutuallyExclusive()
    {
        Assert.Equal(1, VehicleNetCodec.IndicatorOf(VehicleNetCodec.PackFlags(false, 1, false, false, false)));
        Assert.Equal(0, VehicleNetCodec.IndicatorOf(VehicleNetCodec.PackFlags(false, 0, false, false, false)));
        Assert.Equal(0, VehicleNetCodec.PackFlags(false, 0, false, false, false));
    }
    [Theory] [InlineData(-1f)] [InlineData(-0.5f)] [InlineData(0f)] [InlineData(0.25f)] [InlineData(1f)]
    public void SteerRoundTripWithinOneStep(float v) =>
        Assert.InRange(VehicleNetCodec.DequantizeSteer(VehicleNetCodec.QuantizeSteer(v)), v - 1f / 127f, v + 1f / 127f);
    [Theory] [InlineData(5f)] [InlineData(-9f)] [InlineData(float.PositiveInfinity)] [InlineData(float.NaN)]
    public void SteerNeverEscapesRange(float hostile)
    {
        float d = VehicleNetCodec.DequantizeSteer(VehicleNetCodec.QuantizeSteer(hostile));
        Assert.InRange(d, -1f, 1f);
    }
    [Theory] [InlineData(0f, 0)] [InlineData(87.4f, 874)] [InlineData(-5f, 0)] [InlineData(float.NaN, 0)] [InlineData(1e9f, 65535)]
    public void SpeedQuantizes(float kmh, int expected) => Assert.Equal((ushort)expected, VehicleNetCodec.QuantizeSpeed(kmh));
    [Fact] public void SpeedRoundTrip() => Assert.Equal(87.4f, VehicleNetCodec.DequantizeSpeed(VehicleNetCodec.QuantizeSpeed(87.4f)), 1);
    [Theory] [InlineData(0f)] [InlineData(33.3f)] [InlineData(100f)]
    public void DamageRoundTripWithinResolution(float pct) =>
        Assert.InRange(VehicleNetCodec.DequantizeDamage(VehicleNetCodec.QuantizeDamage(pct)), pct - 0.4f, pct + 0.4f);
    [Theory] [InlineData(-10f)] [InlineData(500f)] [InlineData(float.NaN)]
    public void DamageClamped(float hostile) => Assert.InRange(VehicleNetCodec.DequantizeDamage(VehicleNetCodec.QuantizeDamage(hostile)), 0f, 100f);
}
