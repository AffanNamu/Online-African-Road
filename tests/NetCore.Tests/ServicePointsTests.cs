using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ARO.NetCore;
using Xunit;

/// <summary>
/// P1 of docs/FUNCTIONAL_PLACES_PROPOSAL.md (approved 2026-11-14): refuel at a pump. The pump points come from the catalogue sockets of the placed stations; a stopped vehicle beside one can refuel.
/// What is held here: the catalogue data (every station has pumps and its own collision boxes, a truck fits beside every pump), the socket-to-world transform, the trigger rule (stopped, near, same level),
/// every pump on every route lies off the carriageway, the price and meter formulas (the server's buy_fuel formula), and the drive-in collision.
/// </summary>
public class ServicePointsTests
{
    static readonly JsonSerializerOptions Opt = new JsonSerializerOptions { IncludeFields = true };
    static readonly Dictionary<string, PropDef> Defs = Fx.LoadCatalog().props.ToDictionary(p => p.id);
    static PropDef Def(string id) => id != null && Defs.TryGetValue(id, out var d) ? d : null;
    static readonly string[] Stations = { "fuel_station", "kano_fuel_station_large_a", "kano_fuel_station_medium_a", "rg_ngn_fuel_station_small_a" };
    public static IEnumerable<object[]> StationIds => Stations.Select(s => new object[] { s });
    public static IEnumerable<object[]> RoutesWithStations => new[] { "ng-kano-city-e1", "ng-kano-city-f1", "ng-lagos-ibadan" }.Select(r => new object[] { r });

    static readonly Dictionary<string, RouteModel> Models = new Dictionary<string, RouteModel>();
    static RouteModel Model(string id)
    {
        lock (Models)
        {
            if (!Models.TryGetValue(id, out var m)) Models[id] = m = RouteModel.Build(JsonSerializer.Deserialize<RouteSpec>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, id + ".route.json")), Opt));
            return m;
        }
    }

    /// <summary>Every placed prop of a route (all chunks over the bounds of all its roads), in chunk order.</summary>
    static List<PropInstance> Everything(RouteModel m)
    {
        const float size = 200f; float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var c in m.AllCorridors) { minX = Math.Min(minX, c.RingX.Min()); maxX = Math.Max(maxX, c.RingX.Max()); minZ = Math.Min(minZ, c.RingZ.Min()); maxZ = Math.Max(maxZ, c.RingZ.Max()); }
        var all = new List<PropInstance>();
        for (float x = (float)Math.Floor((minX - 300f) / size) * size; x < maxX + 300f; x += size)
            for (float z = (float)Math.Floor((minZ - 300f) / size) * size; z < maxZ + 300f; z += size) all.AddRange(PropScatter.PlaceChunk(m, x, z, x + size, z + size));
        return all;
    }

    // ---------------------------------------------------------------------------------------------------------------- catalogue
    [Theory, MemberData(nameof(StationIds))]
    public void EveryStationHasPumpsAndItsOwnCollisionBoxes(string id)
    {
        var d = Def(id); Assert.NotNull(d);
        Assert.True(d.HasSockets && d.sockets.Count(s => ServicePoints.IsFuel(s.kind)) >= 2 - (id.Contains("small") ? 1 : 0), id + ": pumps");
        Assert.True(d.HasBoxes && d.collider == "box", id + ": collision boxes");
        Assert.Equal(d.sockets.Length, d.sockets.Select(s => s.name).Distinct().Count());
    }

    [Fact]
    public void OnlyTheDriveInPlacesReplaceTheirFootprintBox()
    {
        var withBoxes = Defs.Values.Where(d => d.HasBoxes).Select(d => d.id).OrderBy(x => x).ToArray();
        Assert.Equal(Stations.OrderBy(x => x).ToArray(), withBoxes);          // houses, shops and mosques keep one solid box: nobody drives into them
    }

    /// <summary>A 2.4 m wide, 8 m long truck standing at the pump, heading along the island, touches no collision box of the station and stays on its lot.</summary>
    [Theory, MemberData(nameof(StationIds))]
    public void ATruckFitsBesideEveryPumpOfEveryStation(string id)
    {
        var d = Def(id); const float halfW = 1.2f, halfL = 4.0f;
        foreach (var s in d.sockets.Where(s => ServicePoints.IsFuel(s.kind)))
        {
            var p = ServicePoints.FromInstances(new[] { new PropInstance { Prop = id, Pos = new V3(0, 0, 0), YawDeg = 0f, Scale = 1f } }, Def).Single(q => q.Name == s.name);
            ServicePoints.StandingPlace(p, out var at, out float heading);
            float r = heading * (float)Math.PI / 180f, fx = (float)Math.Sin(r), fz = (float)Math.Cos(r);
            Assert.True(Math.Abs(fx) < 1e-3f || Math.Abs(fz) < 1e-3f, $"{id}/{s.name}: the islands of every station run along a local axis");
            float ex = Math.Abs(fx) * halfL + Math.Abs(fz) * halfW, ez = Math.Abs(fz) * halfL + Math.Abs(fx) * halfW;      // the truck's half extents in local x and z
            foreach (var b in d.boxes)
            {
                bool overlap = at.X + ex > b.x0 + 0.02f && at.X - ex < b.x1 - 0.02f && at.Z + ez > b.z0 + 0.02f && at.Z - ez < b.z1 - 0.02f;
                Assert.False(overlap, $"{id}/{s.name}: a truck at ({at.X:0.00}, {at.Z:0.00}) touches the box x {b.x0}..{b.x1} z {b.z0}..{b.z1}");
            }
            Assert.True(Math.Abs(at.X) + ex <= d.widthM * 0.5f + 0.01f && Math.Abs(at.Z) + ez <= d.depthM * 0.5f + 0.01f, $"{id}/{s.name}: the truck stands on the lot");
            float toPump = (float)Math.Sqrt((p.Pos.X - at.X) * (p.Pos.X - at.X) + (p.Pos.Z - at.Z) * (p.Pos.Z - at.Z));
            Assert.True(toPump < ServicePoints.PumpRadius, $"{id}/{s.name}: standing there counts as at the pump");
        }
    }

    [Fact]
    public void TheValidatorRefusesBadSocketsAndBoxes()
    {
        List<string> Errors(Action<PropDef> mutate) { var c = Fx.Clone(Fx.LoadCatalog()); mutate(c.props.First(p => p.id == "kano_fuel_station_large_a")); return PropCatalogValidator.Validate(c); }
        Assert.Empty(Errors(_ => { }));
        Assert.Contains(Errors(d => d.sockets[0].kind = "teleporter"), e => e.Contains("kind must be one of"));
        Assert.Contains(Errors(d => d.sockets[1].name = d.sockets[0].name), e => e.Contains("duplicate name"));
        Assert.Contains(Errors(d => d.sockets[0].x = 90f), e => e.Contains("within the prop's footprint"));
        Assert.Contains(Errors(d => d.sockets[0].z = float.NaN), e => e.Contains("within the prop's footprint"));
        Assert.Contains(Errors(d => { d.boxes[0].x1 = d.boxes[0].x0; }), e => e.Contains("min must be below max"));
        Assert.Contains(Errors(d => d.boxes[0].z1 = 60f), e => e.Contains("within the prop's footprint and height"));
        Assert.Contains(Errors(d => d.collider = "none"), e => e.Contains("need collider"));
    }

    // ---------------------------------------------------------------------------------------------------------------- world points and the trigger rule
    [Fact]
    public void ASocketFollowsItsInstancesPositionYawAndScale()
    {
        var inst = new PropInstance { Prop = "kano_fuel_station_large_a", Pos = new V3(100f, 2f, 50f), YawDeg = 90f, Scale = 1f };
        var pumps = ServicePoints.FromInstances(new[] { inst }, Def).Where(p => p.IsFuel).ToList();
        Assert.Equal(4, pumps.Count);
        var p1 = pumps.Single(p => p.Name == "pump_1");                       // local (8.4, 0, 6.0); yaw 90 turns local +x to world -z and local +z to world +x
        Assert.Equal(106f, p1.Pos.X, 3); Assert.Equal(2f, p1.Pos.Y, 3); Assert.Equal(41.6f, p1.Pos.Z, 3);
        Assert.Equal(180f, Math.Abs(p1.YawDeg), 3);                          // local yaw 90 (towards the island) + instance 90
        Assert.Equal("Pump 1", ServicePoints.Label(p1));
        var big = ServicePoints.FromInstances(new[] { new PropInstance { Prop = inst.Prop, Pos = inst.Pos, YawDeg = 0f, Scale = 2f } }, Def).Single(p => p.Name == "pump_1");
        Assert.Equal(100f + 16.8f, big.Pos.X, 3); Assert.Equal(50f + 12f, big.Pos.Z, 3);
        Assert.Empty(ServicePoints.FromInstances(new[] { new PropInstance { Prop = "palm_tree", Pos = inst.Pos } }, Def));
    }

    [Fact]
    public void APumpIsOfferedOnlyToAStoppedVehicleNearItOnTheSameLevel()
    {
        var pts = new List<ServicePoint>
        {
            new ServicePoint { Name = "pump_1", Kind = "fuel_pump", Pos = new V3(0f, 0f, 0f) },
            new ServicePoint { Name = "pump_2", Kind = "fuel_pump", Pos = new V3(7f, 0f, 0f) },
            new ServicePoint { Name = "entry", Kind = "drive_in", Pos = new V3(1f, 0f, 0f) },
            new ServicePoint { Name = "pump_9", Kind = "fuel_pump", Pos = new V3(1.5f, 9f, 0f) },              // on a deck above
        };
        Assert.Equal(0, ServicePoints.PumpAt(pts, new V3(1.3f, 0.4f, 2f), 0.2f));
        Assert.Equal(1, ServicePoints.PumpAt(pts, new V3(5.0f, 0.4f, 2f), 0.0f));                                // the nearer pump wins
        Assert.Equal(-1, ServicePoints.PumpAt(pts, new V3(1.3f, 0.4f, 2f), 1.0f));                               // still rolling
        Assert.Equal(-1, ServicePoints.PumpAt(pts, new V3(1.3f, 0.4f, 2f), 30f));
        Assert.Equal(-1, ServicePoints.PumpAt(pts, new V3(-6f, 0f, 0f), 0f));                                    // too far
        Assert.Equal(3, ServicePoints.PumpAt(pts, new V3(1.5f, 9f, 0f), 0f));                                    // the deck pump is found from the deck...
        Assert.Equal(-1, ServicePoints.PumpAt(new List<ServicePoint> { pts[3] }, new V3(1.5f, 0f, 0f), 0f));      // ...never from the ground under it
        Assert.Equal(-1, ServicePoints.PumpAt(new List<ServicePoint> { pts[2] }, new V3(1f, 0f, 0f), 0f));       // an entry is not a pump
        Assert.Equal(-1, ServicePoints.PumpAt(new List<ServicePoint>(), new V3(0f, 0f, 0f), 0f));
    }

    // ---------------------------------------------------------------------------------------------------------------- the routes
    [Theory, MemberData(nameof(RoutesWithStations))]
    public void EveryPumpOnTheRouteIsOffTheCarriagewayAndTheTriggerNeverReachesARoad(string route)
    {
        var m = Model(route); var inst = Everything(m);
        var stations = inst.Where(i => Stations.Contains(i.Prop)).ToList(); var pumps = ServicePoints.FromInstances(inst, Def).Where(p => p.IsFuel).ToList();
        Assert.NotEmpty(stations);
        Assert.Equal(stations.Sum(s => Def(s.Prop).sockets.Count(k => ServicePoints.IsFuel(k.kind))), pumps.Count);
        foreach (var p in pumps)
        {
            Assert.False(m.Terrain.InsideFormation(p.Pos.X, p.Pos.Z, 0f), $"{route}: {p.Prop}/{p.Name} at ({p.Pos.X:0}, {p.Pos.Z:0}) is on a road");
            for (int k = 0; k < 24; k++)
            {
                double a = k * Math.PI / 12.0; float x = p.Pos.X + ServicePoints.PumpRadius * (float)Math.Sin(a), z = p.Pos.Z + ServicePoints.PumpRadius * (float)Math.Cos(a);
                Assert.False(m.Terrain.InsideFormation(x, z, 0f), $"{route}: the trigger of {p.Prop}/{p.Name} at ({p.Pos.X:0}, {p.Pos.Z:0}) reaches a road at ({x:0.0}, {z:0.0})");
            }
            ServicePoints.StandingPlace(p, out var at, out _);
            Assert.False(m.Terrain.InsideFormation(at.X, at.Z, 2f), $"{route}: the place to stand at {p.Prop}/{p.Name} is on a road");
        }
        Assert.Equal(pumps.Count, pumps.Select(p => (Math.Round(p.Pos.X, 2), Math.Round(p.Pos.Z, 2))).Distinct().Count());     // every pump is placed once (each instance belongs to one chunk)
        if (route != "ng-lagos-ibadan") Assert.True(stations.Count >= (route == "ng-kano-city-e1" ? 12 : 4), $"{route}: {stations.Count} stations");
    }

    [Theory, MemberData(nameof(RoutesWithStations))]
    public void TheFirstPumpAlongTheRoadIsFoundTheSameWayEveryTime(string route)
    {
        var m = Model(route); float max = route == "ng-lagos-ibadan" ? 22000f : 4000f;
        Assert.True(ServicePoints.FirstPump(m, Def, 200f, max, out var a), route + ": no pump found");
        Assert.True(ServicePoints.FirstPump(RouteModel.Build(m.Spec), Def, 200f, max, out var b));
        Assert.Equal(a.Prop, b.Prop); Assert.Equal(a.Name, b.Name); Assert.Equal(a.Pos.X, b.Pos.X); Assert.Equal(a.Pos.Z, b.Pos.Z);
        Assert.Contains(a.Prop, Stations);
    }

    [Fact]
    public void ADriveInStationIsSolidOnlyWhereItsModelIs()
    {
        var inst = new PropInstance { Prop = "fuel_station", Pos = new V3(500f, 3f, -200f), YawDeg = 37f, Scale = 1f };
        var boxes = PropBatcher.BoxColliders(new[] { inst }, Def);
        Assert.Equal(Def("fuel_station").boxes.Length, boxes.Count);
        foreach (var p in ServicePoints.FromInstances(new[] { inst }, Def).Where(q => q.IsFuel))
        {
            ServicePoints.StandingPlace(p, out var at, out _);
            foreach (var b in boxes)
            {   // the standing place in the box's own frame
                float r = -b.YawDeg * (float)Math.PI / 180f, dx = at.X - b.Centre.X, dz = at.Z - b.Centre.Z;
                float lx = dx * (float)Math.Cos(r) + dz * (float)Math.Sin(r), lz = -dx * (float)Math.Sin(r) + dz * (float)Math.Cos(r);
                Assert.False(Math.Abs(lx) < b.Size.X * 0.5f && Math.Abs(lz) < b.Size.Z * 0.5f, $"{p.Name}: the place to stand is inside a {b.Size.X:0.0} x {b.Size.Z:0.0} box");
            }
        }
        var house = PropBatcher.BoxColliders(new[] { new PropInstance { Prop = "concrete_house", Pos = inst.Pos, YawDeg = 0f, Scale = 1f } }, Def);
        Assert.Single(house);                                                                                     // everything else keeps its one box
    }

    // ---------------------------------------------------------------------------------------------------------------- price and meter
    [Fact]
    public void TheEstimateUsesTheServersFormulaAndTheMeterRunsFromTheReadingToTheAnswer()
    {
        Assert.Equal(90.0, ServicePoints.LitresToFill(30, 120), 6); Assert.Equal(0.0, ServicePoints.LitresToFill(130, 120), 6); Assert.Equal(120.0, ServicePoints.LitresToFill(-3, 120), 6);
        Assert.Equal(180L, ServicePoints.Cost(90, 2)); Assert.Equal(162L, ServicePoints.Cost(80.56, 2)); Assert.Equal(1L, ServicePoints.Cost(0.01, 2)); Assert.Equal(0L, ServicePoints.Cost(0, 2)); Assert.Equal(0L, ServicePoints.Cost(5, 0));
        Assert.Equal(140L, ServicePoints.Cost(70, 2));                       // the same numbers as supabase/tests/54_pump_refuel.sql
        Assert.Equal(30.0, ServicePoints.Meter(30, 120, 0, 4), 6); Assert.Equal(120.0, ServicePoints.Meter(30, 120, 4, 4), 6); Assert.Equal(120.0, ServicePoints.Meter(30, 120, 9, 4), 6);
        double last = 30; for (double t = 0.1; t < 4; t += 0.1) { double v = ServicePoints.Meter(30, 120, t, 4); Assert.True(v >= last - 1e-9 && v <= 120.0); last = v; }
        Assert.Equal(75.0, ServicePoints.Meter(30, 120, 2, 4), 6);
        Assert.Equal("Pump 2  ·  F  Refuel 63 L for 126 coins", ServicePoints.Prompt("Pump 2", 62.6, 126, true, "F"));
        Assert.Equal("Pump 2  ·  F  Refuel 63 L", ServicePoints.Prompt("Pump 2", 62.6, 0, false, "F"));
        Assert.Equal("Pump  ·  Tank full", ServicePoints.Prompt("Pump", 0.2, 1, true, "F"));
        Assert.Equal("Pump 1  ·  Refuelling  45.0 / 90.0 L  ·  90 coins", ServicePoints.FillLine("Pump 1", 45, 90, 180));
        Assert.Equal("Pump 1  ·  Refuelling  90.0 / 90.0 L  ·  1,180 coins", ServicePoints.FillLine("Pump 1", 95, 90, 1180));
        Assert.Equal(2f, ServicePoints.FillSeconds(0)); Assert.Equal(6f, ServicePoints.FillSeconds(500)); Assert.InRange(ServicePoints.FillSeconds(90), 5.5f, 5.7f);
    }
}
