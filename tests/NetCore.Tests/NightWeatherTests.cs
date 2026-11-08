using System;
using System.Collections.Generic;
using System.Linq;
using ARO.NetCore;
using Xunit;

public class WeatherLookTests
{
    [Theory] [InlineData("clear")] [InlineData("CLOUDY")] [InlineData(" rain ")] [InlineData("wet")]
    public void TheTestWeathersAreKnown(string name) { Assert.True(WeatherLook.TryPreset(name, out var p)); Assert.Equal(name.Trim().ToLowerInvariant(), p.Name); }

    [Theory] [InlineData("")] [InlineData(null)] [InlineData("storm")]
    public void UnknownWeathersAreRefused(string name) => Assert.False(WeatherLook.TryPreset(name, out _));

    [Fact] public void ThePresetsDescribeWhatTheirNamesSay()
    {
        Assert.True(WeatherLook.Clear.Cloud == 0f && WeatherLook.Clear.Rain == 0f && WeatherLook.Clear.Wetness == 0f);
        Assert.True(WeatherLook.Cloudy.Cloud > 0.5f && WeatherLook.Cloudy.Rain == 0f && WeatherLook.Cloudy.Wetness == 0f);       // dark sky, dry road
        Assert.True(WeatherLook.Rain.Rain > 0.5f && WeatherLook.Rain.Wetness > 0.5f);
        Assert.True(WeatherLook.Wet.Rain == 0f && WeatherLook.Wet.Wetness > 0.5f);                                               // the rain has stopped, the road is still wet
    }

    [Fact] public void OvercastIsTheLargerOfCloudAndTheRainsOwnDarkening()
    {
        Assert.Equal(0f, WeatherLook.Overcast(0f, 0f)); Assert.Equal(0.85f, WeatherLook.Overcast(0.85f, 0f), 4);
        Assert.Equal(1f, WeatherLook.Overcast(0f, 1f)); Assert.Equal(0.55f, WeatherLook.Overcast(0.2f, 0.5f), 4);
        Assert.Equal(WeatherLook.Overcast(0f, 0.7f), 0.77f, 3);            // the weather system's own rule (rain x 1.1), so rain-only behaviour is unchanged
        Assert.InRange(WeatherLook.Overcast(float.NaN, 2f), 0f, 1f);
    }

    [Fact] public void WetLookGrowsWithWetnessAndIsZeroOnADryRoad()
    {
        Assert.Equal(0f, WeatherLook.StreakStrength(0f)); Assert.Equal(1f, WeatherLook.StreakStrength(1f)); Assert.True(WeatherLook.StreakStrength(0.5f) > 0.5f);
        float prev = -1f; for (float w = 0f; w <= 1f; w += 0.05f) { float s = WeatherLook.StreakStrength(w); Assert.True(s >= prev); prev = s; }
        Assert.Equal(1f, WeatherLook.PoolBoost(0f)); Assert.True(WeatherLook.PoolBoost(1f) > 1.3f);
    }

    [Fact] public void APuddleAppearsAboveItsThresholdAndNeverOnADryRoad()
    {
        Assert.Equal(0f, WeatherLook.PuddleVisible(0f, 0.15f)); Assert.Equal(0f, WeatherLook.PuddleVisible(0.15f, 0.15f)); Assert.Equal(1f, WeatherLook.PuddleVisible(0.9f, 0.5f));
        Assert.InRange(WeatherLook.PuddleVisible(0.56f, 0.5f), 0.1f, 0.9f);       // fades in, no pop
        for (float t = 0.15f; t <= 0.85f; t += 0.1f) for (float w = 0f; w <= 1f; w += 0.01f) Assert.InRange(WeatherLook.PuddleVisible(w, t), 0f, 1f);
    }
}

public class PuddleFieldTests
{
    static List<Puddle> All(RouteModel m)
    {
        var all = new List<Puddle>(); var c = m.Main;
        for (float x = (float)Math.Floor(c.RingX.Min() / 200f) * 200f - 200f; x < c.RingX.Max() + 200f; x += 200f)
            for (float z = (float)Math.Floor(c.RingZ.Min() / 200f) * 200f - 200f; z < c.RingZ.Max() + 200f; z += 200f) all.AddRange(PuddleField.PlaceChunk(m, x, z, x + 200f, z + 200f));
        return all;
    }
    static readonly Lazy<List<Puddle>> Shipped = new Lazy<List<Puddle>>(() => All(Fx.Shipped));

    [Fact] public void TheRouteHasPuddlesAndTheyAreDeterministic()
    {
        Assert.True(Shipped.Value.Count > 100, "expected puddles along 47 km, got " + Shipped.Value.Count);
        var again = All(RouteModel.Build(Fx.LoadRoute())); Assert.Equal(Shipped.Value.Select(p => p.Key), again.Select(p => p.Key));
        Assert.Equal(Shipped.Value.Count, Shipped.Value.Select(p => p.Key).Distinct().Count());
    }

    [Fact] public void ChunkingNeverDuplicatesOrDropsAPuddle()
    {
        var m = Fx.Shipped; var c = m.Main; var whole = PuddleField.PlaceChunk(m, c.RingX.Min() - 500f, c.RingZ.Min() - 500f, c.RingX.Max() + 500f, c.RingZ.Max() + 500f);
        Assert.Equal(whole.Select(p => p.Key).OrderBy(k => k), Shipped.Value.Select(p => p.Key).OrderBy(k => k));
    }

    [Fact] public void PuddlesLieOnThePavedRoadNeverInTheMedianAndOnTheSurface()
    {
        var m = Fx.Shipped;
        foreach (var p in Shipped.Value)
        {
            m.Main.Spline.Nearest(p.Pos.X, p.Pos.Z, 100f, out float s, out float lat, out _);
            Assert.True(Math.Abs(lat) <= m.Main.HalfPavedAt(s) + 0.2f, "a puddle off the paved width at s=" + s);
            var sec = m.Main.SectionAt(s); if (sec.HasMedian) Assert.True(Math.Abs(lat) > sec.Spec.medianWidth * 0.5f, "a puddle in the median at s=" + s);
            Assert.Equal(m.Main.SurfaceY(s, lat), p.Pos.Y, 1);
            Assert.InRange(p.Threshold, 0.15f, 0.85f); Assert.InRange(p.LengthM, 1f, 8f); Assert.InRange(p.WidthM, 0.5f, 3f);
            Assert.Equal(1f, p.DirX * p.DirX + p.DirZ * p.DirZ, 2);
        }
    }

    [Fact] public void DamagedAndWornRoadHoldMoreWaterThanNewAsphalt()
    {
        var m = Fx.Shipped; double[] metres = new double[3], count = new double[3];
        for (float s = 0; s < m.Main.Length; s += 10f) metres[(int)m.Main.SurfaceAt(s)] += 10;
        foreach (var p in Shipped.Value) { m.Main.Spline.Nearest(p.Pos.X, p.Pos.Z, 100f, out float s, out _, out _); count[(int)m.Main.SurfaceAt(s)]++; }
        double Rate(int i) => metres[i] < 1 ? double.NaN : count[i] / (metres[i] / 1000.0);
        if (metres[(int)Surface.Good] > 1 && metres[(int)Surface.Worn] > 1) Assert.True(Rate((int)Surface.Worn) > Rate((int)Surface.Good), "worn road should hold more puddles than good road");
        if (metres[(int)Surface.Good] > 1 && metres[(int)Surface.Damaged] > 1) Assert.True(Rate((int)Surface.Damaged) > Rate((int)Surface.Good));
    }

    [Fact] public void AMoreWetRoadShowsMorePuddles()
    {
        int dry = Shipped.Value.Count(p => WeatherLook.PuddleVisible(0.2f, p.Threshold) > 0.5f), wet = Shipped.Value.Count(p => WeatherLook.PuddleVisible(0.9f, p.Threshold) > 0.5f);
        Assert.True(wet > dry * 2, $"{wet} visible when soaked vs {dry} when damp"); Assert.Equal(0, Shipped.Value.Count(p => WeatherLook.PuddleVisible(0f, p.Threshold) > 0f));
    }

    [Fact] public void ThePuddleMeshIsValidFiniteAndFollowsTheSurface()
    {
        var some = Shipped.Value.Take(60).ToList(); var origin = some[0].Pos; var m = Fx.Shipped;
        var g = PuddleGeometry.Build(some, origin, (x, z) => LampField.SurfaceHeight(m, x, z));
        Assert.Equal(60 * 9, g.VertexCount); Assert.Equal(60, g.Patches); Assert.Null(g.Validate());
        Assert.All(g.Threshold, t => Assert.InRange(t, 0.15f, 0.85f));
        Assert.Equal(g.Vertices.Count, g.Corner.Count);
    }
}

public class VehicleLightPolicyTests
{
    [Fact] public void TheBeamFollowsTheLightsAndHighBeamNeedsThem()
    {
        Assert.Equal(VehicleLightPolicy.Beam.Off, VehicleLightPolicy.BeamOf(false, false)); Assert.Equal(VehicleLightPolicy.Beam.Off, VehicleLightPolicy.BeamOf(false, true));
        Assert.Equal(VehicleLightPolicy.Beam.Low, VehicleLightPolicy.BeamOf(true, false)); Assert.Equal(VehicleLightPolicy.Beam.High, VehicleLightPolicy.BeamOf(true, true));
    }

    [Fact] public void HazardsFlashBothSidesAndOverrideTheIndicators()
    {
        VehicleLightPolicy.Indicators(-1, false, true, out var l, out var r); Assert.True(l && !r);
        VehicleLightPolicy.Indicators(1, false, true, out l, out r); Assert.True(!l && r);
        VehicleLightPolicy.Indicators(0, false, true, out l, out r); Assert.True(!l && !r);
        VehicleLightPolicy.Indicators(1, true, true, out l, out r); Assert.True(l && r);
        VehicleLightPolicy.Indicators(0, true, true, out l, out r); Assert.True(l && r);
        VehicleLightPolicy.Indicators(1, true, false, out l, out r); Assert.True(!l && !r);            // the dark half of the blink
    }

    [Fact] public void TailLampsAreDimWithLightsBrightOnTheBrakeAndTheBrakeWorksWithoutLights()
    {
        Assert.Equal(0f, VehicleLightPolicy.TailLevel(false, false)); Assert.Equal(0.2f, VehicleLightPolicy.TailLevel(true, false), 4);
        Assert.Equal(1f, VehicleLightPolicy.TailLevel(true, true)); Assert.Equal(1f, VehicleLightPolicy.TailLevel(false, true));
    }

    [Fact] public void OnlyAHighBeamAimedAtSomethingCloseDazzles()
    {
        Assert.True(VehicleLightPolicy.Dazzles(true, 60f, 0.97f)); Assert.False(VehicleLightPolicy.Dazzles(false, 60f, 0.97f));
        Assert.False(VehicleLightPolicy.Dazzles(true, 300f, 0.97f)); Assert.False(VehicleLightPolicy.Dazzles(true, 60f, 0.2f));
    }

    [Fact] public void HighBeamIsStrongerLongerAndNarrowerThanLowBeam()
    {
        Assert.True(HeadlightSpec.HighIntensity > HeadlightSpec.Intensity && HeadlightSpec.HighRange > HeadlightSpec.Range);
        Assert.True(HeadlightSpec.HighOuterAngleDeg < HeadlightSpec.OuterAngleDeg && HeadlightSpec.HighInnerAngleDeg < HeadlightSpec.InnerAngleDeg && HeadlightSpec.HighPitchDownDeg < HeadlightSpec.PitchDownDeg);
        Assert.True(HeadlightSpec.HighInnerAngleDeg < HeadlightSpec.HighOuterAngleDeg);
    }

    [Fact] public void TheNetFlagsCarryHighBeamAndHazardsAndOldPayloadsStillDecode()
    {
        byte f = VehicleNetCodec.PackFlags(true, 1, false, true, false, true, true);
        Assert.True(VehicleNetCodec.Has(f, VehicleNetCodec.HighBeam) && VehicleNetCodec.Has(f, VehicleNetCodec.Hazard) && VehicleNetCodec.Has(f, VehicleNetCodec.Lights));
        Assert.Equal(1, VehicleNetCodec.IndicatorOf(f)); Assert.Equal(64, VehicleNetCodec.HighBeam); Assert.Equal(128, VehicleNetCodec.Hazard);
        byte old = VehicleNetCodec.PackFlags(true, -1, true, false, true);          // the call every earlier client made
        Assert.False(VehicleNetCodec.Has(old, VehicleNetCodec.HighBeam)); Assert.False(VehicleNetCodec.Has(old, VehicleNetCodec.Hazard)); Assert.Equal(-1, VehicleNetCodec.IndicatorOf(old));
        Assert.Equal(0, VehicleNetCodec.PackFlags(false, 0, false, false, false));
        // every flag is its own bit
        var bits = new[] { VehicleNetCodec.Lights, VehicleNetCodec.IndicatorLeft, VehicleNetCodec.IndicatorRight, VehicleNetCodec.Horn, VehicleNetCodec.Brake, VehicleNetCodec.Reverse, VehicleNetCodec.HighBeam, VehicleNetCodec.Hazard };
        Assert.Equal(8, bits.Select(b => (int)b).Distinct().Count()); Assert.Equal(255, bits.Aggregate(0, (a, b) => a | b));
    }
}

public class NightTierTests
{
    [Fact] public void TheNightBudgetsNeverRiseFromHighToLow()
    {
        var l = QualityTiers.Low; var m = QualityTiers.Medium; var h = QualityTiers.High;
        Assert.True(l.LampLights <= m.LampLights && m.LampLights <= h.LampLights); Assert.True(l.LampGlowRange <= m.LampGlowRange && m.LampGlowRange <= h.LampGlowRange);
        Assert.True(l.AdditionalLightsPerObject <= m.AdditionalLightsPerObject && m.AdditionalLightsPerObject <= h.AdditionalLightsPerObject);
        Assert.True(!l.WetEffects || m.WetEffects); Assert.True(!m.WetEffects || h.WetEffects);
    }

    [Fact] public void EveryTierHasSaneNightValuesAndTheLowTierCostsNoRealLamps()
    {
        foreach (var t in QualityTiers.All)
        {
            Assert.InRange(t.LampLights, 0, 16); Assert.InRange(t.LampGlowRange, 100f, 1500f); Assert.InRange(t.AdditionalLightsPerObject, 1, 8);
            Assert.True(t.AdditionalLightsPerObject >= 4, "the two headlights and the lamps near them need at least four lights per object");
        }
        Assert.Equal(0, QualityTiers.Low.LampLights);          // the mobile budget: emissive lamps, halos and pools only
    }

    [Fact] public void ACopyOfATierKeepsTheNightValues()
    {
        var c = QualityTiers.High.Clone(); Assert.Equal(8, c.LampLights); Assert.True(c.WetEffects); Assert.Equal(600f, c.LampGlowRange);
    }
}
