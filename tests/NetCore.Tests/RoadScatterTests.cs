using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ARO.NetCore;
using Xunit;

/// <summary>
/// Scatter rules with `allRoads` (opt-in, 2026-11-16, the Kano city): street lights, trees, kiosks and bus stops along every road of the network, not only the main road.
/// A chunk computes the same props whatever its neighbours (every client places the same props), nothing stands on a road, an island or a site plate, the roundabout rings
/// get none, and a rule without the flag keeps placing on the main road only (the golden masters of every older route are unchanged).
/// </summary>
public class RoadScatterTests
{
    static readonly JsonSerializerOptions Opt = new JsonSerializerOptions { IncludeFields = true };
    static RouteModel _city;
    static RouteModel City { get { lock (Opt) return _city ??= RouteModel.Build(JsonSerializer.Deserialize<RouteSpec>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ng-kano-city.route.json")), Opt)); } }
    static HashSet<string> RoadRules(RouteModel m) => new HashSet<string>(m.Spec.propRules.Where(r => r.allRoads).Select(r => r.id));
    static string Key(PropInstance p) => p.Prop + "@" + p.Pos.X.ToString("F2") + "," + p.Pos.Z.ToString("F2") + "," + p.YawDeg.ToString("F1");

    // a square of the D06 estate and the 45-degree arterial, far from the A2 (the main road)
    const float X0 = 1600f, Z0 = 2200f;

    [Fact] public void TheCityLightsAndPlantsItsSideStreets()
    {
        var m = City; var rules = RoadRules(m);
        Assert.Contains("lights_local", rules); Assert.Contains("trees_local_a", rules); Assert.Contains("trees_collector", rules);
        var got = PropScatter.PlaceChunk(m, X0, Z0, X0 + 400f, Z0 + 400f).Where(p => rules.Contains(p.Origin)).ToList();
        Assert.True(got.Count(p => p.Prop.StartsWith("street_light")) >= 20, $"{got.Count(p => p.Prop.StartsWith("street_light"))} street lights in 400 x 400 m of streets");
        Assert.True(got.Count(p => p.Prop.StartsWith("rg_ngn_tree_")) >= 10, $"{got.Count(p => p.Prop.StartsWith("rg_ngn_tree_"))} trees in 400 x 400 m of streets");
        m.Main.Spline.Nearest(X0 + 200f, Z0 + 200f, 100000f, out _, out float lat, out _); Assert.True(Math.Abs(lat) > 1000f, "the square lies far from the main road");
    }

    [Fact] public void AChunkPlacesTheSameRoadPropsWhateverItsNeighbours()
    {
        var m = City; var rules = RoadRules(m);
        var whole = PropScatter.PlaceChunk(m, X0, Z0, X0 + 400f, Z0 + 400f).Where(p => rules.Contains(p.Origin)).Select(Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var parts = new List<string>();
        foreach (var (dx, dz) in new[] { (200f, 200f), (0f, 0f), (200f, 0f), (0f, 200f) })                 // a different order on purpose
            parts.AddRange(PropScatter.PlaceChunk(m, X0 + dx, Z0 + dz, X0 + dx + 200f, Z0 + dz + 200f).Where(p => rules.Contains(p.Origin)).Select(Key));
        Assert.Equal(whole, parts.OrderBy(k => k, StringComparer.Ordinal).ToList());
        var again = PropScatter.PlaceChunk(m, X0, Z0, X0 + 400f, Z0 + 400f).Where(p => rules.Contains(p.Origin)).Select(Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.Equal(whole, again);
    }

    [Fact] public void NothingStandsOnARoadAnIslandOrASitePlateAndTheRingsGetNone()
    {
        var m = City; var rules = RoadRules(m); var medians = new HashSet<string>(m.Spec.propRules.Where(r => r.side == "median").Select(r => r.id));
        var boxes = new List<(float, float)> { (X0, Z0) };
        foreach (var rb in m.Rings.Take(4)) boxes.Add(((float)Math.Floor((rb.Cx - 100f) / 200f) * 200f, (float)Math.Floor((rb.Cz - 100f) / 200f) * 200f));
        foreach (var s in m.Sites) boxes.Add(((float)Math.Floor((s.Cx - 150f) / 200f) * 200f, (float)Math.Floor((s.Cz - 150f) / 200f) * 200f));
        int n = 0;
        foreach (var (x, z) in boxes)
            foreach (var p in PropScatter.PlaceChunk(m, x, z, x + 400f, z + 400f).Where(p => rules.Contains(p.Origin) && !medians.Contains(p.Origin)))
            {
                n++; Assert.False(m.Terrain.InsideFormation(p.Pos.X, p.Pos.Z, 0f), $"{p.Origin} {p.Prop} at ({p.Pos.X:0.0}, {p.Pos.Z:0.0}) stands on a road, an island or a plate");
                foreach (var rb in m.Rings) Assert.True(Math.Sqrt((p.Pos.X - rb.Cx) * (p.Pos.X - rb.Cx) + (p.Pos.Z - rb.Cz) * (p.Pos.Z - rb.Cz)) > rb.CentreRadius + 1f || !InsideRing(rb, p), $"{p.Origin} inside ring {rb.Id}");
            }
        Assert.True(n > 50, $"only {n} road props checked");
    }

    static bool InsideRing(Roundabout rb, PropInstance p) => Math.Sqrt((p.Pos.X - rb.Cx) * (p.Pos.X - rb.Cx) + (p.Pos.Z - rb.Cz) * (p.Pos.Z - rb.Cz)) <= rb.CentreRadius;

    [Fact] public void ARuleWithoutTheFlagStillPlacesOnTheMainRoadOnly()
    {
        var spec = JsonSerializer.Deserialize<RouteSpec>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ng-kano-city.route.json")), Opt);
        foreach (var r in spec.propRules) r.allRoads = false;
        var m = RouteModel.Build(spec); var ids = new HashSet<string>(spec.propRules.Select(r => r.id));
        Assert.DoesNotContain(PropScatter.PlaceChunk(m, X0, Z0, X0 + 400f, Z0 + 400f), p => ids.Contains(p.Origin));
    }
}
