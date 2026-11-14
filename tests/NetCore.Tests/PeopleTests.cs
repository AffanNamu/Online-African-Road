using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ARO.NetCore;
using Xunit;

/// <summary>
/// P2 of docs/FUNCTIONAL_PLACES_PROPOSAL.md (approved 2026-11-14): people built by code (PeopleKit) and where they stand and walk (CrowdPlanner). What is held: every figure builds within its budget with
/// believable proportions; the walk swings legs and arms in opposition; the crowd is the same whichever chunks are loaded; nobody is placed or walks on a carriageway, a shoulder, a plate or an island,
/// or inside a solid prop; fewer people at night; the bus boards and alights the server's numbers.
/// </summary>
public class PeopleTests
{
    static readonly JsonSerializerOptions Opt = new JsonSerializerOptions { IncludeFields = true };
    static readonly Dictionary<string, PropDef> Defs = Fx.LoadCatalog().props.ToDictionary(p => p.id);
    static PropDef Def(string id) => id != null && Defs.TryGetValue(id, out var d) ? d : null;
    static readonly Dictionary<string, RouteModel> Models = new Dictionary<string, RouteModel>();
    static RouteModel Model(string id) { lock (Models) { if (!Models.TryGetValue(id, out var m)) Models[id] = m = RouteModel.Build(JsonSerializer.Deserialize<RouteSpec>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, id + ".route.json")), Opt)); return m; } }
    public static IEnumerable<object[]> Routes => new[] { "ng-kano-city-e1", "ng-kano-city-f1", "ng-lagos-ibadan" }.Select(r => new object[] { r });

    static IEnumerable<(float x0, float z0)> Chunks(RouteModel m, float pad = 300f)
    {
        const float size = 200f; float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var c in m.AllCorridors) { minX = Math.Min(minX, c.RingX.Min()); maxX = Math.Max(maxX, c.RingX.Max()); minZ = Math.Min(minZ, c.RingZ.Min()); maxZ = Math.Max(maxZ, c.RingZ.Max()); }
        if (m.Spec.routeId == "ng-lagos-ibadan")
        {   // the shipped road is 120 km: the windows around its bus stop (km 2) and its fuel station (km 20.5)
            var seen = new HashSet<(float, float)>();
            foreach (float at in new[] { 2000f, 20500f })
            {
                var p = m.Main.Spline.PositionAt(at);
                for (float x = (float)Math.Floor((p.X - 400f) / size) * size; x < p.X + 400f; x += size)
                    for (float z = (float)Math.Floor((p.Z - 400f) / size) * size; z < p.Z + 400f; z += size) if (seen.Add((x, z))) yield return (x, z);
            }
            yield break;
        }
        for (float x = (float)Math.Floor((minX - pad) / size) * size; x < maxX + pad; x += size)
            for (float z = (float)Math.Floor((minZ - pad) / size) * size; z < maxZ + pad; z += size) yield return (x, z);
    }

    static (List<CrowdAgent> agents, List<PropInstance> props) Everything(RouteModel m)
    {
        var agents = new List<CrowdAgent>(); var props = new List<PropInstance>();
        foreach (var (x, z) in Chunks(m)) { var inst = PropScatter.PlaceChunk(m, x, z, x + 200f, z + 200f); props.AddRange(inst); agents.AddRange(CrowdPlanner.PlaceChunk(m, inst, Def, x, z, x + 200f, z + 200f)); }
        return (agents, props);
    }

    // ---------------------------------------------------------------------------------------------------------------- the figures
    [Fact]
    public void TenDesignsInKanoDressTwoOfThemChildrenEachInThreeColourways()
    {
        Assert.Equal(10, PeopleKit.Designs.Length); Assert.Equal(10, PeopleKit.Designs.Select(d => d.Id).Distinct().Count());
        Assert.Equal(2, PeopleKit.Designs.Count(d => d.Child));
        foreach (var d in PeopleKit.Designs) { Assert.Equal(3, d.Colourways.Length); Assert.All(d.Colourways, c => Assert.All(c, i => Assert.InRange(i, 0, PeopleKit.Palette.Length - 1))); Assert.False(string.IsNullOrWhiteSpace(d.What)); }
        Assert.True(PeopleKit.Palette.Length <= PeopleKit.PaletteSize * PeopleKit.PaletteSize);
        Assert.Contains(PeopleKit.Designs, d => d.Id.Contains("hijab")); Assert.Contains(PeopleKit.Designs, d => d.Id.Contains("babban_riga")); Assert.Contains(PeopleKit.Designs, d => d.Id.Contains("headload"));
    }

    [Fact]
    public void EveryFigureBuildsWithinItsBudgetWithBelievableProportions()
    {
        for (int d = 0; d < PeopleKit.Designs.Length; d++)
            for (int c = 0; c < 3; c++)
                for (int s = 0; s < PeopleKit.SkinCount; s++)
                {
                    var m0 = PeopleKit.Mesh(d, c, s, 0); var m1 = PeopleKit.Mesh(d, c, s, 1); string id = PeopleKit.Designs[d].Id;
                    Assert.InRange(m0.TriangleCount, 200, 1000); Assert.True(m1.TriangleCount < m0.TriangleCount * 0.75, $"{id}: LOD1 {m1.TriangleCount} vs {m0.TriangleCount}");
                    Assert.All(m0.Bone, b => Assert.InRange(b, 0, 4)); Assert.Equal(m0.Pos.Length, m0.Uv.Length); Assert.Equal(m0.Pos.Length, m0.Bone.Length);
                    Assert.All(m0.Tris, i => Assert.InRange(i, 0, m0.Pos.Length - 1));
                    Assert.All(m0.Uv, u => { Assert.InRange(u.X, 0f, 1f); Assert.InRange(u.Y, 0f, 1f); });
                    float h = m0.Height, lo = m0.Pos.Min(p => p.Y), wx = m0.Pos.Max(p => Math.Abs(p.X)), wz = m0.Pos.Max(p => Math.Abs(p.Z));
                    if (PeopleKit.Designs[d].Child) Assert.InRange(h, 1.0f, 1.35f); else Assert.InRange(h, 1.65f, id.Contains("headload") ? 2.05f : 1.9f);
                    Assert.InRange(lo, -0.01f, 0.01f); Assert.True(wx <= 0.45f && wz <= 0.35f, $"{id}: {wx:0.00} x {wz:0.00}");
                    Assert.Contains(m0.Uv, u => Math.Abs(u.X - PeopleKit.Cell(PeopleKit.Skin0 + s).X) < 1e-4 && Math.Abs(u.Y - PeopleKit.Cell(PeopleKit.Skin0 + s).Y) < 1e-4);   // the skin tone is used
                }
    }

    [Fact]
    public void TheWalkSwingsLegsAndArmsInOppositionAndTheIdleKeepsTheFeetStill()
    {
        var m = PeopleKit.Mesh(0, 0, 0, 0); var f0 = PeopleKit.Frame(0, 0, 0, 0, 0); var f2 = PeopleKit.Frame(0, 0, 0, 0, 2); var idle = PeopleKit.Frame(0, 0, 0, 0, PeopleKit.WalkFrames + 1);
        Assert.Equal(m.Pos.Length, f2.Length);
        float FootZ(V3[] f, int bone) => Enumerable.Range(0, m.Pos.Length).Where(i => m.Bone[i] == bone).Min(i => f[i].Y) < 0.2f ? Enumerable.Range(0, m.Pos.Length).Where(i => m.Bone[i] == bone && m.Pos[i].Y < 0.1f).Average(i => f[i].Z) : 0f;
        float l = FootZ(f2, PeopleKit.LegL), r = FootZ(f2, PeopleKit.LegR);
        Assert.True(l > 0.25f && r < -0.25f, $"quarter cycle: the left foot forward ({l:0.00}), the right back ({r:0.00})");
        Assert.Equal(PeopleKit.WalkAngle(PeopleKit.ArmL, 0.25f), -PeopleKit.WalkAngle(PeopleKit.ArmR, 0.25f));
        Assert.True(PeopleKit.WalkAngle(PeopleKit.ArmL, 0.25f) < 0 && PeopleKit.WalkAngle(PeopleKit.LegL, 0.25f) > 0, "the left arm swings back while the left leg swings forward");
        for (int i = 0; i < m.Pos.Length; i++)
        {
            if (m.Bone[i] == PeopleKit.Body) Assert.Equal(m.Pos[i].Y + PeopleKit.WalkBob(0.25f), f2[i].Y, 4);
            if (m.Bone[i] == PeopleKit.LegL || m.Bone[i] == PeopleKit.LegR) { Assert.Equal(m.Pos[i].Z, idle[i].Z, 4); Assert.Equal(m.Pos[i].Y, f0[i].Y, 4); }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- the crowd
    [Theory, MemberData(nameof(Routes))]
    public void TheCrowdIsTheSameWhicheverChunksAreLoadedAndEveryAgentBelongsToOneChunk(string route)
    {
        var m = Model(route); var a = Everything(m).agents; var b = Everything(RouteModel.Build(m.Spec)).agents;
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++) { Assert.Equal(a[i].Source, b[i].Source); Assert.Equal(a[i].Anchor.X, b[i].Anchor.X); Assert.Equal(a[i].Anchor.Z, b[i].Anchor.Z); Assert.Equal(a[i].Design, b[i].Design); }
        Assert.Equal(a.Count, a.Select(x => (Math.Round(x.Anchor.X, 3), Math.Round(x.Anchor.Z, 3), x.Source, x.Design)).Distinct().Count());
        if (route == "ng-kano-city-e1") { Assert.True(a.Count >= 150, $"{a.Count} people"); Assert.True(a.Count(x => x.Source.StartsWith("walkway")) >= 20); Assert.True(a.Count(x => x.Source == "station:attendant") >= 10); Assert.Contains(a, x => x.Source == "mosque"); }
        if (route == "ng-lagos-ibadan") { Assert.Contains(a, x => x.Source == "station:attendant"); Assert.Contains(a, x => x.Source == "stop"); }
    }

    [Theory, MemberData(nameof(Routes))]
    public void NobodyStandsOrWalksOnACarriagewayOrInsideASolidProp(string route)
    {
        var m = Model(route); var (agents, props) = Everything(m); var boxes = PropBatcher.BoxColliders(props, Def);
        Assert.NotEmpty(agents);
        var plateAnchored = new HashSet<CrowdAgent>();          // people of a plate's own shelters, kiosks and stalls stand on the plate (never on a carriageway)
        foreach (var (x0, z0) in Chunks(m)) { var inst = PropScatter.PlaceChunk(m, x0, z0, x0 + 200f, z0 + 200f).Where(i => CrowdPlanner.OnPlate(m, i)).ToList(); foreach (var a in CrowdPlanner.PlaceChunk(m, inst, Def, x0, z0, x0 + 200f, z0 + 200f)) if (!a.Source.StartsWith("walkway")) plateAnchored.Add(a); }
        var onPlate = agents.Where(a => plateAnchored.Any(b => b.Source == a.Source && b.Anchor.X == a.Anchor.X && b.Anchor.Z == a.Anchor.Z)).ToHashSet();
        if (route == "ng-kano-city-e1") Assert.True(onPlate.Count(a => a.Source.StartsWith("stop")) >= 8, $"the bus terminal's four shelters have people waiting ({onPlate.Count(a => a.Source.StartsWith("stop"))})");
        foreach (var a in agents)
            foreach (var p in a.Walks ? a.Path.Concat(Mid(a.Path)) : new[] { a.Spot })
            {
                Assert.True(CrowdPlanner.OffRoad(m, p.X, p.Z, 0.25f, plates: !onPlate.Contains(a)), $"{route}: {a.Source} at ({p.X:0.0}, {p.Z:0.0}) is on a road, a plate or an island");
                foreach (var b in boxes)
                {
                    float r = -b.YawDeg * (float)Math.PI / 180f, dx = p.X - b.Centre.X, dz = p.Z - b.Centre.Z, lx = dx * (float)Math.Cos(r) + dz * (float)Math.Sin(r), lz = -dx * (float)Math.Sin(r) + dz * (float)Math.Cos(r);
                    Assert.False(Math.Abs(lx) < b.Size.X * 0.5f - 0.05f && Math.Abs(lz) < b.Size.Z * 0.5f - 0.05f, $"{route}: {a.Source} at ({p.X:0.0}, {p.Z:0.0}) is inside the {b.Prop} box");
                }
            }
    }

    static IEnumerable<V3> Mid(V3[] path) { for (int i = 1; i < path.Length; i++) yield return new V3((path[i].X + path[i - 1].X) * 0.5f, (path[i].Y + path[i - 1].Y) * 0.5f, (path[i].Z + path[i - 1].Z) * 0.5f); }

    [Fact]
    public void WalkersFollowTheWalkwayAtKerbTopHeightAndTurnAtTheEnds()
    {
        var m = Model("ng-kano-city-e1"); var walkers = Everything(m).agents.Where(a => a.Source.StartsWith("walkway")).ToList(); Assert.NotEmpty(walkers);
        foreach (var w in walkers.Take(30))
        {
            var c = m.AllCorridors.First(k => (k.Id ?? "main") == w.Source.Split(':')[1]);
            foreach (var p in w.Path) { Assert.True(c.Spline.Nearest(p.X, p.Z, 60f, out float s, out float lat, out _)); Assert.True(CrowdPlanner.OffRoad(m, p.X, p.Z, 0.3f), "beyond the kerb"); Assert.True(p.Y >= c.SurfaceY(s, 0f) - 0.3f); }
            Assert.True(w.At(0.0, out var a0, out _, out int f0) && f0 < PeopleKit.WalkFrames);
            double tEnd = w.Length / w.Speed; Assert.True(w.At(1000.0, out _, out _, out _));
            Assert.InRange(w.Speed, 1.0f, 1.5f); Assert.InRange(w.Length, 18f, 115f);
        }
    }

    [Fact]
    public void FewerPeopleAtNightAndNeverMoreThanTheBudgetNearestFirst()
    {
        Assert.Equal(1f, CrowdPlanner.Density(12f)); Assert.True(CrowdPlanner.Density(23f) < 0.2f && CrowdPlanner.Density(3f) < 0.2f); Assert.True(CrowdPlanner.Density(20f) < 1f && CrowdPlanner.Density(6f) < 1f); Assert.Equal(CrowdPlanner.Density(-1f), CrowdPlanner.Density(23f));
        var m = Model("ng-kano-city-e1"); var agents = Everything(m).agents; var eye = agents[agents.Count / 2].Anchor;
        var day = CrowdPlanner.Visible(agents, 100.0, eye, 150f, 60, CrowdPlanner.Density(12f)); var night = CrowdPlanner.Visible(agents, 100.0, eye, 150f, 60, CrowdPlanner.Density(23f));
        Assert.True(day.Count <= 60 && day.Count > night.Count, $"{day.Count} by day, {night.Count} at night");
        for (int i = 1; i < day.Count; i++) Assert.True(day[i].dist >= day[i - 1].dist);
        Assert.All(day, v => Assert.True(v.dist <= 150f));
        Assert.True(CrowdPlanner.Visible(agents, 100.0, eye, 150f, 20, 1f).Count <= 20, "the low tier budget");
        Assert.All(night, v => Assert.Contains(day.Select(d => d.agent), a => ReferenceEquals(a, v.agent) || day.Count == 60));      // the night's people are among the day's (the same ones, fewer)
    }

    [Fact]
    public void TheBusBoardsAndAlightsTheServersNumbersFirstOffThenOn()
    {
        var door = new V3(10f, 0.3f, 5f); var kerb = new V3(1f, 0f, 0f); var along = new V3(0f, 0f, 1f);
        var a = CrowdPlanner.BusStop(door, kerb, along, boarded: 5, alighted: 3, t0: 100.0, seed: 7);
        Assert.Equal(8, a.Count); Assert.Equal(5, a.Count(x => x.Source == "bus:board")); Assert.Equal(3, a.Count(x => x.Source == "bus:alight"));
        Assert.Equal(24, CrowdPlanner.BusStop(door, kerb, along, 40, 30, 0, 1).Count);                               // at most 12 of each are drawn
        foreach (var x in a.Where(q => q.Source == "bus:board"))
        {
            Assert.True(x.At(90.0, out var wait, out _, out _), "boarding passengers already wait at the stop"); Assert.True(wait.X - door.X > 2f, "on the kerb side");
            Assert.False(x.At(x.T0 + x.Length / x.Speed + 0.5, out _, out _, out _), "gone into the bus");
            Assert.True(x.T0 > a.Where(q => q.Source == "bus:alight").Max(q => q.T0), "first off, then on");
            var end = x.Path[x.Path.Length - 1]; Assert.Equal(door.X, end.X); Assert.Equal(door.Z, end.Z);
        }
        foreach (var x in a.Where(q => q.Source == "bus:alight")) { Assert.False(x.At(99.0, out _, out _, out _)); Assert.True(x.At(x.T0 + 0.5, out var p, out _, out _)); Assert.True(Math.Abs(p.X - door.X) < 1.5f); }
        Assert.True(CrowdPlanner.EndOf(a) > 100.0 && CrowdPlanner.EndOf(a) < 140.0);
        Assert.Empty(CrowdPlanner.BusStop(door, kerb, along, 0, 0, 0, 1));
    }
}
