using ARO.NetCore;
using Xunit;

public class DrawBreakdownTests
{
    [Fact] public void CountsDrawsRenderersAndTrianglesPerGroup()
    {
        var b = new DrawBreakdown();
        b.Add("kit", 1, new[] { 10, 11 }, 2, 100); b.Add("kit", 1, new[] { 10, 11 }, 2, 100); b.Add("kit", 2, new[] { 10 }, 1, 40); b.Add("rest", 9, new[] { 50 }, 1, 7);
        var k = b.Get("kit"); Assert.Equal(3, k.Renderers); Assert.Equal(5, k.Draws); Assert.Equal(240, k.Triangles);
        Assert.Equal(1, b.Get("rest").Draws); Assert.Equal(6, b.Draws("kit", "rest"));
    }

    [Fact] public void DistinctCountsAreTheBatchingFloors()
    {
        var b = new DrawBreakdown();
        for (int i = 0; i < 40; i++) b.Add("kit", 1, new[] { 10, 11 }, 2, 12);            // forty identical instances of one two-material mesh
        b.Add("kit", 2, new[] { 10 }, 1, 12);                                              // a second mesh sharing material 10
        var k = b.Get("kit");
        Assert.Equal(81, k.Draws);                                                         // unbatched
        Assert.Equal(2, k.Materials.Count);                                                // merging everything with the same material: two draws
        Assert.Equal(2, k.Meshes.Count);
        Assert.Equal(3, k.Pairs.Count);                                                    // perfect instancing of identical meshes: three draws
    }

    [Fact] public void SameMaterialOnDifferentMeshesIsADifferentPair()
    {
        var b = new DrawBreakdown(); b.Add("g", 1, new[] { 7 }, 1, 3); b.Add("g", 2, new[] { 7 }, 1, 3);
        Assert.Equal(2, b.Get("g").Pairs.Count); Assert.Equal(1, b.Get("g").Materials.Count);
    }

    [Fact] public void MissingGroupReportsZeros()
    {
        var b = new DrawBreakdown();
        Assert.Equal("nothing renderers=0 draws=0 tris=0 distinctMaterials=0 distinctMeshes=0 distinctMeshMaterialPairs=0", b.Summary("nothing"));
        Assert.Equal(0, b.Draws("nothing"));
    }

    [Fact] public void RankedListsMostDrawsFirstAndStripsThePrefix()
    {
        var b = new DrawBreakdown();
        b.Add("asset:pier", 1, new[] { 1, 2 }, 2, 10); b.Add("asset:pier", 1, new[] { 1, 2 }, 2, 10); b.Add("asset:villa", 3, new[] { 1, 2, 3 }, 3, 500); b.Add("asset:tree", 4, new[] { 5, 6 }, 2, 90); b.Add("rest", 8, new[] { 9 }, 1, 1);
        Assert.Equal("pier=4/2/20, villa=3/1/500, tree=2/1/90", b.Ranked("asset:"));
    }

    [Fact] public void RankedBreaksTiesByName()
    {
        var b = new DrawBreakdown(); b.Add("a:z", 1, new[] { 1 }, 1, 1); b.Add("a:b", 2, new[] { 1 }, 1, 1);
        Assert.Equal("b=1/1/1, z=1/1/1", b.Ranked("a:"));
    }

    [Fact] public void RendererWithoutMaterialsStillCounts()
    {
        var b = new DrawBreakdown(); b.Add("kit", 1, null, 0, 12);
        Assert.Equal(1, b.Get("kit").Renderers); Assert.Equal(0, b.Get("kit").Draws); Assert.Empty(b.Get("kit").Pairs);
    }
}
