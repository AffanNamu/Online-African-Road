using System.Linq;
using ARO.NetCore;
using Xunit;

public class PropPrefabPlanTests
{
    static PropDef Def() => new PropDef { id = "aro_calibration", category = "props", status = "production", source = "Props/aro_calibration", collider = "box", heightM = 2.15f, widthM = 3.3f, depthM = 7.5f, cullDistance = 140f, lodTriangles = new[] { 252, 12 }, castShadow = true, description = "x" };
    static ImportedMeasure Good() => new ImportedMeasure { WidthM = 3.3f, HeightM = 2.15f, DepthM = 7.5f, LodTriangles = new[] { 252, 12 }, MaterialCount = 6, ShaderNames = new[] { "Universal Render Pipeline/Lit" }, HasBoxCollider = true, ColliderSize = new[] { 2f, 1.5f, 6f } };

    [Fact] public void ScreenHeightFallsWithDistanceAndFollowsTheFieldOfView()
    {
        Assert.True(PropPrefabPlan.ScreenHeightAt(10f, 20f) > PropPrefabPlan.ScreenHeightAt(10f, 40f));
        Assert.Equal(1f, PropPrefabPlan.ScreenHeightAt(2f * 10f * (float)System.Math.Tan(30 * System.Math.PI / 180), 10f, 60f), 3);        // an object that exactly fills a 60 degree view
        Assert.Equal(0.5f * PropPrefabPlan.ScreenHeightAt(10f, 20f), PropPrefabPlan.ScreenHeightAt(10f, 40f), 5);
    }

    [Fact] public void LodThresholdsReproduceTheGamesPolicy()
    {
        // LodPolicy.PropLodLevel: level = floor(distance / cull * levels) with bias 1, so LOD i+1 starts at (i+1) * cull / levels
        var def = Def(); var tier = new QualityTier { LodBias = 1f, PropCullScale = 1f };
        float[] h = PropPrefabPlan.LodScreenHeights(3, 10f, 150f);
        Assert.Equal(PropPrefabPlan.ScreenHeightAt(10f, 50f), h[0], 5); Assert.Equal(PropPrefabPlan.ScreenHeightAt(10f, 100f), h[1], 5); Assert.Equal(PropPrefabPlan.ScreenHeightAt(10f, 150f), h[2], 5);
        def.lodTriangles = new[] { 300, 100, 30 }; def.cullDistance = 150f;
        Assert.Equal(0, LodPolicy.PropLodLevel(def, 49f, tier)); Assert.Equal(1, LodPolicy.PropLodLevel(def, 51f, tier)); Assert.Equal(2, LodPolicy.PropLodLevel(def, 101f, tier));   // the same boundaries
    }

    [Fact] public void ThresholdsAreStrictlyDecreasingAndNeverZero()
    {
        foreach (var lv in new[] { 1, 2, 3, 4 }) foreach (var size in new[] { 0.5f, 8f, 60f }) foreach (var cull in new[] { 20f, 140f, 1000f })
        {
            var h = PropPrefabPlan.LodScreenHeights(lv, size, cull);
            Assert.Equal(lv, h.Length); Assert.All(h, x => Assert.InRange(x, PropPrefabPlan.MinScreenHeight * 0.9f, 1f));      // a tiny, far-culled prop reaches the floor; each further level steps 2% below it to stay strictly decreasing
            for (int i = 1; i < h.Length; i++) Assert.True(h[i] < h[i - 1], $"levels {lv} size {size} cull {cull}: {string.Join(",", h)}");
        }
        Assert.Single(PropPrefabPlan.LodScreenHeights(0, 5f, 100f));
    }

    [Fact] public void AModelThatMatchesItsCatalogueEntryHasNoFaults()
    {
        var e = PropPrefabPlan.Check(Def(), Good()); Assert.Empty(e);
    }

    [Fact] public void EachKindOfMismatchIsReported()
    {
        var m = Good(); m.HeightM = 3f; Assert.Contains(PropPrefabPlan.Check(Def(), m), x => x.StartsWith("height"));
        m = Good(); m.WidthM = 1f; Assert.Contains(PropPrefabPlan.Check(Def(), m), x => x.StartsWith("width"));
        m = Good(); m.DepthM = 75f; Assert.Contains(PropPrefabPlan.Check(Def(), m), x => x.StartsWith("depth"));
        m = Good(); m.LodTriangles = new[] { 252 }; Assert.Contains(PropPrefabPlan.Check(Def(), m), x => x.StartsWith("LOD count"));
        m = Good(); m.LodTriangles = new[] { 252, 13 }; Assert.Contains(PropPrefabPlan.Check(Def(), m), x => x.StartsWith("LOD1 triangles"));
        m = Good(); m.MaterialCount = 17; Assert.Contains(PropPrefabPlan.Check(Def(), m), x => x.Contains("materials"));
        m = Good(); m.ShaderNames = new[] { "Standard" }; Assert.Contains(PropPrefabPlan.Check(Def(), m), x => x.Contains("Universal Render Pipeline/Lit"));
        m = Good(); m.ShaderNames = new[] { "Shader Graphs/glTF-pbrMetallicRoughness" }; Assert.Empty(PropPrefabPlan.Check(Def(), m, 0.03f, false));       // GLB through glTFast uses its own shader graphs: interchange, not the game's material
        m = Good(); m.HasBoxCollider = false; Assert.Contains(PropPrefabPlan.Check(Def(), m), x => x.Contains("no BoxCollider"));
        m = Good(); m.ColliderSize = new[] { 30f, 1.5f, 6f }; Assert.Contains(PropPrefabPlan.Check(Def(), m), x => x.Contains("larger than the model"));
        m = Good(); m.RenderersOnCollisionNodes = 1; Assert.Contains(PropPrefabPlan.Check(Def(), m), x => x.Contains("collision node"));
        var d = Def(); d.collider = "none"; Assert.Contains(PropPrefabPlan.Check(d, Good()), x => x.Contains("BoxCollider but the catalogue says"));
    }

    [Fact] public void TheCatalogueRulesApplyToTheEntryToo()
    {
        var d = Def(); d.lodTriangles = new[] { 9000, 100 }; d.category = "props";
        Assert.Contains(PropPrefabPlan.Check(d, Good()), x => x.StartsWith("catalogue:") && x.Contains("budget"));
        Assert.Contains(PropPrefabPlan.Check(null, Good()), x => x.Contains("no definition"));
    }

    [Fact] public void TheToleranceAllowsCentimetreRounding()
    {
        var m = Good(); m.WidthM = 3.3f + 0.04f; Assert.Empty(PropPrefabPlan.Check(Def(), m));         // the manifest rounds to two decimals
    }
}
