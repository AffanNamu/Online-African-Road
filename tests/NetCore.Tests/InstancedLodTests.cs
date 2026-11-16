using System;
using ARO.NetCore;
using Xunit;

/// <summary>RB-15: the instanced renderer chooses the same level of detail as Unity's LODGroup would (so batching never changes the look), culls by distance, and splits draws safely.</summary>
public class InstancedLodTests
{
    static readonly float[] T = { 0.25f, 0.08f, 0.02f };     // a building's three LODs

    [Fact]
    public void RelativeHeightIsUnitysFormula()
    {
        // a 20 m building 100 m away, 60 degree field of view, LOD bias 1: 20 / (2 x 100 x tan 30) = 0.1732
        Assert.Equal(0.1732f, InstancedLod.RelativeHeight(20f, 100f, 60f, 1f), 3);
        Assert.Equal(2f * 0.1732f, InstancedLod.RelativeHeight(20f, 100f, 60f, 2f), 3);       // the bias scales it like distance / bias
        Assert.Equal(InstancedLod.RelativeHeight(20f, 50f, 60f, 1f), InstancedLod.RelativeHeight(20f, 100f, 60f, 2f), 4);
        Assert.True(float.IsPositiveInfinity(InstancedLod.RelativeHeight(20f, 0f, 60f, 1f)));
    }

    [Fact]
    public void TheFirstThresholdReachedIsShownAndBelowTheLastItIsCulled()
    {
        Assert.Equal(0, InstancedLod.Select(0.5f, T)); Assert.Equal(0, InstancedLod.Select(0.25f, T));
        Assert.Equal(1, InstancedLod.Select(0.2f, T)); Assert.Equal(2, InstancedLod.Select(0.05f, T)); Assert.Equal(2, InstancedLod.Select(0.02f, T));
        Assert.Equal(-1, InstancedLod.Select(0.019f, T)); Assert.Equal(-1, InstancedLod.Select(1f, null));
    }

    [Fact]
    public void DetailFallsMonotonicallyWithDistanceAndTheCullDistanceWins()
    {
        int last = 0;
        for (float d = 5f; d < 2000f; d += 5f)
        {
            int l = InstancedLod.Choose(20f, d, 60f, 1f, T, 1e9f); int lv = l < 0 ? 99 : l;
            Assert.True(lv >= last, $"at {d} m the LOD went back from {last} to {lv}"); last = lv;
        }
        Assert.Equal(-1, InstancedLod.Choose(20f, 300f, 60f, 1f, T, 250f));                   // inside the LOD range but beyond the catalogue's cull distance
        Assert.Equal(1, InstancedLod.Choose(20f, 100f, 60f, 1f, T, 250f));
    }

    [Fact]
    public void DrawsAreSplitIntoSafeBatches()
    {
        Assert.Equal(0, InstancedLod.Draws(0)); Assert.Equal(1, InstancedLod.Draws(1)); Assert.Equal(1, InstancedLod.Draws(InstancedLod.MaxPerDraw));
        Assert.Equal(2, InstancedLod.Draws(InstancedLod.MaxPerDraw + 1)); Assert.True(InstancedLod.MaxPerDraw * 64 * 2 <= 65536, "two matrices (object and inverse) of 64 bytes per instance fit 64 KB");
    }

    [Fact]
    public void OnlyValidLodSetsAreAccepted()
    {
        Assert.True(InstancedLod.ValidThresholds(T)); Assert.True(InstancedLod.ValidThresholds(new[] { 1f }));
        Assert.False(InstancedLod.ValidThresholds(null)); Assert.False(InstancedLod.ValidThresholds(new float[0]));
        Assert.False(InstancedLod.ValidThresholds(new[] { 0.1f, 0.2f })); Assert.False(InstancedLod.ValidThresholds(new[] { 0.3f, 0f })); Assert.False(InstancedLod.ValidThresholds(new[] { 1.5f }));
    }
}
