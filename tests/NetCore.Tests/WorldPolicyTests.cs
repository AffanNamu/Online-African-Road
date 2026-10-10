using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ARO.NetCore;
using Xunit;

public class PropScatterTests
{
    static List<PropInstance> Everything(RouteModel m, float density = 1f)
    {
        var all = new List<PropInstance>(); const float size = 200f; var c = m.Main;
        float minX = c.RingX.Min() - 200f, maxX = c.RingX.Max() + 200f, minZ = c.RingZ.Min() - 200f, maxZ = c.RingZ.Max() + 200f;
        for (float x = (float)Math.Floor(minX / size) * size; x < maxX; x += size)
            for (float z = (float)Math.Floor(minZ / size) * size; z < maxZ; z += size) all.AddRange(PropScatter.PlaceChunk(m, x, z, x + size, z + size, density));
        return all;
    }

    [Fact] public void ScatterIsDeterministic()
    {
        var m = Fx.Shipped; var p = m.Main.Spline.PositionAt(21000f); float x = (float)Math.Floor(p.X / 200f) * 200f, z = (float)Math.Floor(p.Z / 200f) * 200f;
        var a = PropScatter.PlaceChunk(m, x, z, x + 200f, z + 200f); var b = PropScatter.PlaceChunk(RouteModel.Build(Fx.LoadRoute()), x, z, x + 200f, z + 200f);
        Assert.NotEmpty(a); Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++) { Assert.Equal(a[i].Prop, b[i].Prop); Assert.Equal(a[i].Pos.X, b[i].Pos.X); Assert.Equal(a[i].YawDeg, b[i].YawDeg); }
    }

    [Fact] public void ChunkingNeverDuplicatesOrDropsAnInstance()
    {
        var m = Fx.Shipped; var chunked = Everything(m);
        var whole = new List<PropInstance>(); var c = m.Main;
        whole.AddRange(PropScatter.PlaceChunk(m, c.RingX.Min() - 500f, c.RingZ.Min() - 500f, c.RingX.Max() + 500f, c.RingZ.Max() + 500f));
        Assert.Equal(whole.Count, chunked.Count);
        Assert.Equal(whole.Count, whole.Select(p => (p.Origin, p.Pos.X, p.Pos.Z)).Distinct().Count());
        Assert.Equal(chunked.Count, chunked.Select(p => (p.Origin, p.Pos.X, p.Pos.Z)).Distinct().Count());
    }

    [Fact] public void NothingIsPlacedOnTheRoadOrInsideAFormation()
    {
        var m = Fx.Shipped; var all = Everything(m); Assert.True(all.Count > 1000, "expected a populated corridor, got " + all.Count);
        foreach (var p in all.Where(p => p.Origin != null && !m.Spec.props.Any(h => h.id == p.Origin)))
            Assert.False(m.Terrain.InsideFormation(p.Pos.X, p.Pos.Z, PropScatter.ExclusionMargin - 0.01f), p.Prop + " was placed on the road");
    }

    [Fact] public void DensityMultiplierScalesTheCountAndZeroRemovesAllRuleProps()
    {
        var m = Fx.Shipped; int full = Everything(m, 1f).Count(p => !m.Spec.props.Any(h => h.id == p.Origin)), half = Everything(m, 0.5f).Count(p => !m.Spec.props.Any(h => h.id == p.Origin));
        Assert.InRange(half, full * 0.4, full * 0.6);
        Assert.Equal(0, Everything(m, 0f).Count(p => !m.Spec.props.Any(h => h.id == p.Origin)));
    }

    [Fact] public void ZoneFiltersAreHonoured()
    {
        var m = Fx.Shipped;
        foreach (var p in Everything(m).Where(p => p.Origin == "commercial_shops"))
        {
            m.Main.Spline.Nearest(p.Pos.X, p.Pos.Z, 200f, out float s, out _, out _);
            Assert.Equal(Zone.Commercial, m.Main.ZoneAt(s));
        }
        Assert.Contains(Everything(m), p => p.Origin == "commercial_shops");
        Assert.Contains(Everything(m), p => p.Origin == "rural_palms");
    }

    [Fact] public void SidesAndOffsetsAreHonoured()
    {
        var m = Fx.Shipped;
        foreach (var p in Everything(m).Where(p => p.Origin == "rural_poles"))              // rule: left side only
        {
            m.Main.Spline.Nearest(p.Pos.X, p.Pos.Z, 200f, out float s, out float lat, out _);
            Assert.True(lat < 0f, "left of travel means a negative lateral");
            float off = Math.Abs(lat) - m.Main.HalfFormationAt(s);
            Assert.InRange(off, 2.9f, 5.3f);
        }
    }

    [Fact] public void HandPlacedPropsLandWhereTheyWereAsked()
    {
        var m = Fx.Shipped; var h = m.Spec.props.First(p => p.id == "fuel_aro_km20");
        var all = Everything(m); var f = all.Single(p => p.Origin == h.id);
        m.Main.Spline.Nearest(f.Pos.X, f.Pos.Z, 100f, out float s, out float lat, out _);
        Assert.InRange(s, h.s - 2f, h.s + 2f); Assert.Equal(h.lateral, lat, 0);
        Assert.Equal(m.Terrain.HeightAt(f.Pos.X, f.Pos.Z), f.Pos.Y, 3);
        Assert.Equal("ARO", f.Text);
    }

    [Fact] public void RoadsideContentFacesTheRoad()
    {
        var m = Fx.Shipped;
        foreach (var p in Everything(m).Where(p => p.Origin == "commercial_shops").Take(40))
        {
            m.Main.Spline.Nearest(p.Pos.X, p.Pos.Z, 200f, out float s, out float lat, out _);
            m.Main.Spline.FrameAt(s, out _, out var fwd, out var right);
            float yaw = p.YawDeg * (float)Math.PI / 180f; float fx = (float)Math.Sin(yaw), fz = (float)Math.Cos(yaw);
            float toward = (fx * right.X + fz * right.Z) * -Math.Sign(lat);               // positive when the prop looks at the road
            Assert.True(toward > 0.8f, "shop front does not face the road (" + toward + ")");
        }
    }

    [Fact] public void EmptyAreasGiveNothing() => Assert.Empty(PropScatter.PlaceChunk(Fx.Shipped, 5e6f, 5e6f, 5e6f + 200f, 5e6f + 200f));
}

public class QualityAndLodTests
{
    [Fact] public void TiersAreMonotonicInEveryCostLever()
    {
        var t = QualityTiers.All.ToArray();
        for (int i = 0; i + 1 < t.Length; i++)
        {
            Assert.True(t[i].ShadowDistance <= t[i + 1].ShadowDistance); Assert.True(t[i].ShadowCascades <= t[i + 1].ShadowCascades);
            Assert.True(t[i].ShadowResolution <= t[i + 1].ShadowResolution); Assert.True(t[i].MsaaSamples <= t[i + 1].MsaaSamples);
            Assert.True(t[i].RenderScale <= t[i + 1].RenderScale); Assert.True(t[i].LodBias <= t[i + 1].LodBias);
            Assert.True(t[i].LoadRadiusChunks <= t[i + 1].LoadRadiusChunks); Assert.True(t[i].PropDensity <= t[i + 1].PropDensity);
            Assert.True(t[i].PropCullScale <= t[i + 1].PropCullScale); Assert.True(t[i].TerrainDetail <= t[i + 1].TerrainDetail);
            Assert.True(t[i].MaxTrafficAgents <= t[i + 1].MaxTrafficAgents); Assert.True(t[i].TrafficDensity <= t[i + 1].TrafficDensity); Assert.True(t[i].FarClip <= t[i + 1].FarClip);
        }
    }

    [Fact] public void EveryTierIsInternallyConsistent()
    {
        foreach (var t in QualityTiers.All)
        {
            Assert.True(t.UnloadRadiusChunks > t.LoadRadiusChunks, t.Name + ": unload must exceed load or chunks thrash");
            Assert.Contains(t.MsaaSamples, new[] { 1, 2, 4, 8 }); Assert.InRange(t.RenderScale, 0.5f, 1f);
            Assert.Contains(t.ShadowResolution, new[] { 512, 1024, 2048, 4096 }); Assert.InRange(t.ShadowCascades, 1, 4);
            Assert.True(t.FarClip > t.LoadRadiusChunks * LodPolicy.ChunkMetres, t.Name + ": the far clip must cover the loaded chunks");
            Assert.True(t.Tonemapping, "tonemapping is cheap and always on");
        }
        Assert.False(QualityTiers.Low.Bloom); Assert.False(QualityTiers.Medium.Bloom); Assert.True(QualityTiers.High.Bloom);
    }

    [Theory]
    [InlineData(true, 8000, 8000, null, "low")] [InlineData(false, 2000, 4000, null, "low")] [InlineData(false, 4000, 1000, null, "medium")]
    [InlineData(false, 16000, 8000, null, "high")] [InlineData(false, 0, 0, null, "medium")] [InlineData(true, 8000, 8000, "high", "high")]
    [InlineData(false, 16000, 8000, "LOW", "low")] [InlineData(false, 4000, 1000, "ultra", "medium")]
    public void DetectPicksConservatively(bool mobile, int mem, int gfx, string requested, string expected) =>
        Assert.Equal(expected, QualityTiers.Detect(new DeviceInfo { IsMobile = mobile, SystemMemoryMb = mem, GraphicsMemoryMb = gfx, Requested = requested }).Name);

    [Fact] public void TerrainResolutionFallsWithDistanceAndTier()
    {
        foreach (var t in QualityTiers.All)
        {
            int r0 = LodPolicy.TerrainCells(0, t), r1 = LodPolicy.TerrainCells(1, t), r2 = LodPolicy.TerrainCells(2, t), r3 = LodPolicy.TerrainCells(3, t);
            Assert.True(r0 >= r1 && r1 > r2 && r2 > r3, t.Name); Assert.All(new[] { r0, r1, r2, r3 }, c => { Assert.True(c >= 8); Assert.Equal(0, c % 2); });
        }
        Assert.True(LodPolicy.TerrainCells(0, QualityTiers.High) > LodPolicy.TerrainCells(0, QualityTiers.Low));
        Assert.Equal(40, LodPolicy.TerrainCells(0, QualityTiers.Medium));         // 5 m cells beside the player at the default tier
    }

    [Fact] public void CollidersAndShadowsStayNearThePlayer()
    {
        Assert.True(LodPolicy.CollidersEnabled(0)); Assert.True(LodPolicy.CollidersEnabled(2)); Assert.False(LodPolicy.CollidersEnabled(3));
        Assert.True(LodPolicy.ShadowsEnabled(1, QualityTiers.Medium)); Assert.False(LodPolicy.ShadowsEnabled(5, QualityTiers.Medium));
        Assert.Equal(2, LodPolicy.Ring(5, -3, 3, -3)); Assert.Equal(4, LodPolicy.Ring(-1, 3, 3, 0));
    }

    [Fact] public void PropLodLevelsStepDownWithDistance()
    {
        var def = new PropDef { id = "x", cullDistance = 300f, lodTriangles = new[] { 900, 300, 60 } };
        var t = QualityTiers.Medium; int last = -1;
        foreach (float d in new[] { 1f, 60f, 130f, 200f, 290f, 2000f }) { int l = LodPolicy.PropLodLevel(def, d, t); Assert.True(l >= last); Assert.InRange(l, 0, 2); last = l; }
        Assert.Equal(0, LodPolicy.PropLodLevel(def, 1f, t)); Assert.Equal(2, LodPolicy.PropLodLevel(def, 5000f, t));
        Assert.True(LodPolicy.PropCullDistance(def, QualityTiers.Low) < LodPolicy.PropCullDistance(def, QualityTiers.High));
        Assert.True(LodPolicy.PropLodLevel(def, 100f, QualityTiers.High) <= LodPolicy.PropLodLevel(def, 100f, QualityTiers.Low), "a higher lodBias keeps detail longer");
    }

    [Fact] public void PerfBudgetsFlagOnlyWhatWasExceededAndScaleWithTheTier()
    {
        Assert.Empty(PerfCheck.Evaluate(new PerfSample { Triangles = 500000, DrawCalls = 200, TextureMb = 100, HeapMb = 300, FrameMs = 10 }, PerfBudget.Desktop));
        var v = PerfCheck.Evaluate(new PerfSample { Triangles = 2000000, DrawCalls = 900, TextureMb = 100, HeapMb = 300, FrameMs = 40 }, PerfBudget.Desktop);
        Assert.Equal(3, v.Count); Assert.Contains(v, x => x.StartsWith("triangles")); Assert.Contains(v, x => x.StartsWith("draw calls")); Assert.Contains(v, x => x.StartsWith("frame time"));
        Assert.Same(PerfBudget.Mobile, PerfBudget.For(QualityTiers.Low)); Assert.Same(PerfBudget.Desktop, PerfBudget.For(QualityTiers.High));
        Assert.True(PerfBudget.Mobile.MaxTriangles < PerfBudget.Desktop.MaxTriangles && PerfBudget.Mobile.MaxDrawCalls < PerfBudget.Desktop.MaxDrawCalls);
    }
}

public class PropCatalogTests
{
    static List<string> Errors(Action<PropCatalogSpec> mutate) { var c = Fx.Clone(Fx.LoadCatalog()); mutate(c); return PropCatalogValidator.Validate(c); }
    static void Rejects(Action<PropCatalogSpec> mutate, string expect) { var e = Errors(mutate); Assert.True(e.Any(x => x.Contains(expect, StringComparison.OrdinalIgnoreCase)), $"expected '{expect}', got:\n" + string.Join("\n", e)); }

    [Fact] public void ShippedCatalogIsValid() { var e = PropCatalogValidator.Validate(Fx.LoadCatalog()); Assert.True(e.Count == 0, string.Join("\n", e)); }

    [Fact] public void EveryShippedPropIsHonestlyMarkedAsADevPlaceholder()
    {
        foreach (var p in Fx.LoadCatalog().props) { Assert.Equal("dev", p.status); Assert.StartsWith("dev:", p.source); Assert.Contains("PLACEHOLDER", p.description); }
    }

    [Fact] public void EveryPropUsedByTheRouteExistsAndEveryCategoryHasAtLeastOne()
    {
        var ids = Fx.LoadCatalog().props.Select(p => p.id).ToHashSet(); var spec = Fx.LoadRoute();
        foreach (var r in spec.propRules) Assert.Contains(r.prop, ids);
        foreach (var p in spec.props) Assert.Contains(p.prop, ids);
        foreach (var c in new[] { "vegetation", "buildings", "props", "signs", "lights" }) Assert.Contains(Fx.LoadCatalog().props, p => p.category == c);
    }

    [Fact] public void ConventionViolationsAreRejected()
    {
        Rejects(c => c.props = new PropDef[0], "empty");
        Rejects(c => c.props[0].id = "Palm Tree", "snake_case");
        Rejects(c => c.props[1].id = c.props[0].id, "duplicate");
        Rejects(c => c.props[0].category = "furniture", "category");
        Rejects(c => c.props[0].status = "final", "status");
        Rejects(c => c.props[0].collider = "sphere", "collider");
        Rejects(c => c.props[0].heightM = 0f, "dimensions");
        Rejects(c => c.props[0].widthM = 500f, "dimensions");
        Rejects(c => c.props[0].cullDistance = 5f, "cullDistance");
        Rejects(c => c.props[0].lodTriangles = null, "LOD levels");
        Rejects(c => c.props[0].lodTriangles = new[] { 100, 200 }, "strictly decrease");
        Rejects(c => c.props[0].lodTriangles = new[] { 90000, 100 }, "budget");
        Rejects(c => c.props[0].lodTriangles = new[] { 100, 50, 25, 12, 6 }, "LOD levels");
        Rejects(c => c.props[0].source = "Props/x", "dev prop's source");
        Rejects(c => { c.props[0].status = "production"; c.props[0].source = "dev:x"; }, "production prop's source");
        Rejects(c => c.props[0].source = "", "source is required");
        Rejects(c => c.schemaVersion = 3, "schemaVersion");
    }

    [Fact] public void ProductionPropsFollowTheResourcesConvention()
    {
        var c = Fx.Clone(Fx.LoadCatalog()); c.props[0].status = "production"; c.props[0].source = "Props/palm_tree";
        Assert.Empty(PropCatalogValidator.Validate(c));
    }
}

public class RouteIntegrationTests
{
    [Fact] public void RouteMetadataAgreesWithTheWorldMap()
    {
        var map = System.Text.Json.JsonSerializer.Deserialize<WorldMapData>(File.ReadAllText(Fx.Path("world_map.json")), Fx.Opt); var spec = Fx.LoadRoute();
        var r = map.routes.Single(x => x.gameRouteId == spec.routeId);
        Assert.Equal(spec.meta.worldMapRouteId, r.id);
        Assert.Equal(spec.meta.realRoadKm, (float)r.roadKm, 1);
        Assert.Equal(spec.meta.status, r.status);
        Assert.Equal(spec.name, r.name.Replace(" → ", " to ").Replace("→", "to"), ignoreCase: true);
        // Every route that claims a playable game route must be backed by route data.
        foreach (var x in map.routes.Where(x => !string.IsNullOrEmpty(x.gameRouteId))) Assert.Equal(spec.routeId, x.gameRouteId);
    }

    [Fact] public void GameLengthClaimIsTrueAndCoverageIsHonest()
    {
        var m = Fx.Shipped; float km = m.Length / 1000f;
        Assert.InRange(km, m.Spec.meta.gameLengthKm * 0.99f, m.Spec.meta.gameLengthKm * 1.01f);
        float coverage = km / m.Spec.meta.realRoadKm;
        Assert.InRange(coverage, 0.30f, 0.45f);
        Assert.Contains(((int)Math.Round(coverage * 100f)).ToString(), m.Spec.meta.gameCoverage);          // the prose states the same percentage
        Assert.Contains(m.Spec.meta.gameLengthKm.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture), m.Spec.meta.gameCoverage);
        Assert.Equal("prototype", m.Spec.meta.status);
    }

    [Fact] public void EverySeededJobLocationIsOnOrBesideTheRoad()
    {
        string seed = File.ReadAllText(Fx.Path("seed.sql")); var m = Fx.Shipped;
        var rows = Regex.Matches(seed, @"\('(Lagos|Ibadan)',\s*'([a-z0-9-]+)',\s*'[^']*',\s*'[a-z]+',\s*(-?\d+),\s*(-?\d+)\)");
        Assert.True(rows.Count >= 8, "found " + rows.Count);
        foreach (Match r in rows)
        {
            float x = float.Parse(r.Groups[3].Value), z = float.Parse(r.Groups[4].Value);
            Assert.True(m.Main.Spline.Nearest(x, z, 150f, out _, out _, out float d), r.Groups[2].Value + " is not near the road");
            Assert.True(d <= 100f, $"{r.Groups[2].Value} is {d:0} m from the road");
        }
    }

    [Fact] public void KeyLocationsAreControlPointsAndWaypointsMatchTheirStations()
    {
        var m = Fx.Shipped; var sp = m.Main.Spline; var cps = m.Spec.controlPoints;
        foreach (var w in m.Spec.waypoints)
        {
            int i = Array.FindIndex(cps, c => c.id == w.id);
            if (i >= 0) Assert.Equal(sp.ControlS(i), w.s, 0);
            Assert.Equal(w.s, m.WaypointS(w.id));
        }
        Assert.Equal(-1f, m.WaypointS("nowhere"));
        Assert.Contains(m.Spec.waypoints, w => w.kind == "service");
    }

    [Fact] public void SpecYieldsOneRunPerProfileChangeAndEveryRunHasGeometry()
    {
        var c = Fx.Shipped.Main;
        Assert.True(c.Runs.Length >= 8);
        for (int i = 1; i < c.Runs.Length; i++) Assert.Equal(c.Runs[i - 1].S1, c.Runs[i].S0, 3);
        Assert.Equal(0f, c.Runs[0].S0); Assert.Equal(c.Length, c.Runs.Last().S1, 1);
        Assert.Equal(3, Fx.Shipped.Sides.Count);
        Assert.Equal(2, Fx.Shipped.Main.Bridges.Length);
    }

    [Fact] public void RouteSurfaceAndZoneLookupsFollowTheControlPoints()
    {
        var c = Fx.Shipped.Main;
        Assert.Equal(Zone.Industrial, c.ZoneAt(100f)); Assert.Equal(Surface.Good, c.SurfaceAt(100f));
        Assert.Equal(Zone.Commercial, c.ZoneAt(3000f)); Assert.Equal(Surface.Damaged, c.SurfaceAt(2900f));
        Assert.Equal(Surface.Damaged, c.SurfaceAt(33000f)); Assert.Equal(Zone.Highway, c.ZoneAt(40000f));
        Assert.Equal(2, c.SectionAt(100f).Spec.lanesPerDirection); Assert.True(c.SectionAt(12000f).HasMedian);
    }

    [Fact] public void ProjectAndPositionRoundTrip()
    {
        var m = Fx.Shipped; foreach (float s in new[] { 10f, 5000f, 20000f, 47000f })
        { var p = m.PositionAt(s); Assert.Equal(s, m.DistanceAlong(p.X, p.Z, out float lat), 0); Assert.Equal(0f, lat, 1); }
    }
}

public class DevPropGeometryTests
{
    static PropDef[] All => Fx.LoadCatalog().props;

    [Fact] public void EveryCataloguedPropGeneratesValidGeometryAtEveryLod()
    {
        foreach (var def in All)
            for (int lod = 0; lod < def.lodTriangles.Length; lod++)
            {
                var mb = DevPropGeometry.Build(def, lod);
                Assert.True(mb.Validate(out var err), $"{def.id} lod{lod}: {err}");
                Assert.True(mb.TriangleCount > 0, $"{def.id} lod{lod} is empty");
            }
    }

    [Fact] public void TriangleCountsStayInsideTheCatalogBudgetAndFallWithEveryLod()
    {
        foreach (var def in All)
        {
            int prev = int.MaxValue;
            for (int lod = 0; lod < def.lodTriangles.Length; lod++)
            {
                int tris = DevPropGeometry.Build(def, lod).TriangleCount;
                Assert.True(tris <= def.lodTriangles[lod], $"{def.id} lod{lod}: {tris} triangles exceeds the catalog budget {def.lodTriangles[lod]}");
                Assert.True(tris < prev, $"{def.id} lod{lod} ({tris}) is not cheaper than the previous level ({prev})");
                prev = tris;
            }
        }
    }

    [Fact] public void PropsMatchTheirRealWorldSizeAndStandOnTheGround()
    {
        foreach (var def in All)
        {
            var mb = DevPropGeometry.Build(def, 0); mb.MinMax(out var lo, out var hi);
            Assert.InRange(lo.Y, -0.25f, 0.2f);                                             // pivot at the base
            Assert.InRange(hi.Y - lo.Y, def.heightM * 0.55f, def.heightM * 1.25f);         // height matches the catalog
            Assert.True(hi.X - lo.X <= def.widthM * 1.35f + 0.5f, $"{def.id} is {hi.X - lo.X:0.0} m wide, catalog says {def.widthM}");
            Assert.True(hi.Z - lo.Z <= def.depthM * 1.5f + 3f, $"{def.id} is {hi.Z - lo.Z:0.0} m deep, catalog says {def.depthM}");
        }
    }

    [Fact] public void LodClampingAndUnknownPropsAreSafe()
    {
        var def = All.First(p => p.id == "bush");
        Assert.Equal(DevPropGeometry.Build(def, 0).TriangleCount, DevPropGeometry.Build(def, -5).TriangleCount);
        Assert.Equal(DevPropGeometry.Build(def, def.lodTriangles.Length - 1).TriangleCount, DevPropGeometry.Build(def, 99).TriangleCount);
        var unknown = new PropDef { id = "mystery", heightM = 2, widthM = 2, depthM = 2, lodTriangles = new[] { 100 } };
        Assert.True(DevPropGeometry.Build(unknown, 0).TriangleCount > 0);               // falls back to a plain box rather than failing
    }

    [Fact] public void FoliageIsTwoSidedAndWallsFaceOutwards()
    {
        var palm = DevPropGeometry.Build(All.First(p => p.id == "palm_tree"), 0);
        var tris = Fx.Tris(palm, PropSub.Foliage).ToList();
        Assert.Contains(tris, t => t.n.Y > 0); Assert.Contains(tris, t => t.n.Y < 0);
        var house = DevPropGeometry.Build(All.First(p => p.id == "concrete_house"), 0);
        var centre = new V3(0, 3f, 0);
        foreach (var (n, c) in Fx.Tris(house, PropSub.Wall)) Assert.True(MeshBuffers.Dot(n, c - centre) > 0f, "a wall faces inwards");
    }

    [Fact] public void SignsCarryAPanelSlotForText()
    {
        foreach (var id in new[] { "road_sign", "billboard", "fuel_station", "shop_row" })
            Assert.True(DevPropGeometry.Build(All.First(p => p.id == id), 0).Triangles[PropSub.SignPanel].Count > 0, id);
    }
}

public class PropBatcherTests
{
    static MeshBuffers Geo(string id, int lod) => DevPropGeometry.Build(Fx.LoadCatalog().props.First(p => p.id == id), lod);

    [Fact] public void MergingKeepsEveryTriangleAndVertex()
    {
        var one = Geo("concrete_house", 0);
        var list = Enumerable.Range(0, 5).Select(i => new PropInstance { Prop = "concrete_house", Pos = new V3(i * 20, 3, 0), Scale = 1f }).ToList();
        var merged = PropBatcher.Merge(list, Geo, 0, new V3(0, 0, 0));
        Assert.Equal(one.TriangleCount * 5, merged.TriangleCount); Assert.Equal(one.VertexCount * 5, merged.VertexCount);
        Assert.True(merged.Validate(out var err), err);
        for (int s = 0; s < PropSub.Count; s++) Assert.Equal(one.Triangles[s].Count * 5, merged.Triangles[s].Count);
    }

    [Fact] public void YawScaleAndPositionAreApplied()
    {
        var p = PropBatcher.Place(new V3(0, 1, 2), new V3(10, 5, 20), 0f, 1f); Assert.Equal(10f, p.X, 4); Assert.Equal(6f, p.Y, 4); Assert.Equal(22f, p.Z, 4);
        var east = PropBatcher.Place(new V3(0, 0, 2), new V3(0, 0, 0), 90f, 1f); Assert.Equal(2f, east.X, 3); Assert.Equal(0f, east.Z, 3);          // front (+z) turns to face east
        var right = PropBatcher.Place(new V3(1, 0, 0), new V3(0, 0, 0), 90f, 1f); Assert.Equal(-1f, right.Z, 3);                                   // clockwise from above
        var big = PropBatcher.Place(new V3(1, 1, 1), new V3(0, 0, 0), 0f, 3f); Assert.Equal(3f, big.Y, 4);
    }

    [Fact] public void OriginMakesVerticesChunkLocal()
    {
        var list = new List<PropInstance> { new PropInstance { Prop = "bush", Pos = new V3(1005, 7, 2003), Scale = 1f } };
        var m = PropBatcher.Merge(list, Geo, 0, new V3(1000, 5, 2000)); m.MinMax(out var lo, out var hi);
        Assert.InRange(lo.X, 0f, 10f); Assert.InRange(lo.Z, -1f, 10f); Assert.InRange(lo.Y, 1.5f, 2.5f);
    }

    [Fact] public void IncludeFilterAndEmptyInputAndMissingGeometryAreSafe()
    {
        var list = new List<PropInstance> { new PropInstance { Prop = "bush", Scale = 1f }, new PropInstance { Prop = "palm_tree", Scale = 1f }, new PropInstance { Prop = "ghost", Scale = 1f } };
        var onlyPalm = PropBatcher.Merge(list, (id, lod) => id == "ghost" ? null : Geo(id, lod), 0, new V3(0, 0, 0), i => i.Prop != "bush");
        Assert.Equal(Geo("palm_tree", 0).TriangleCount, onlyPalm.TriangleCount);
        Assert.True(PropBatcher.Merge(new List<PropInstance>(), Geo, 0, new V3(0, 0, 0)).IsEmpty);
    }

    [Fact] public void LowerLodsMakeSmallerBatches()
    {
        var list = Enumerable.Range(0, 20).Select(i => new PropInstance { Prop = i % 2 == 0 ? "palm_tree" : "concrete_house", Pos = new V3(i * 15, 0, 0), Scale = 1f }).ToList();
        int t0 = PropBatcher.Merge(list, Geo, 0, new V3(0, 0, 0)).TriangleCount, t1 = PropBatcher.Merge(list, Geo, 1, new V3(0, 0, 0)).TriangleCount;
        Assert.True(t1 < t0);
    }

    [Fact] public void BoxCollidersOnlyForBoxProps()
    {
        var cat = Fx.LoadCatalog().props.ToDictionary(p => p.id);
        var list = new List<PropInstance>
        {
            new PropInstance { Prop = "concrete_house", Pos = new V3(0, 2, 0), Scale = 1.5f, YawDeg = 30f }, new PropInstance { Prop = "palm_tree", Pos = new V3(5, 0, 5), Scale = 1f }, new PropInstance { Prop = "ghost", Scale = 1f }
        };
        var boxes = PropBatcher.BoxColliders(list, id => cat.TryGetValue(id, out var d) ? d : null);
        Assert.Single(boxes);
        Assert.Equal(cat["concrete_house"].heightM * 1.5f, boxes[0].Size.Y, 3); Assert.Equal(2f + cat["concrete_house"].heightM * 0.75f, boxes[0].Centre.Y, 3); Assert.Equal(30f, boxes[0].YawDeg);
    }
}
