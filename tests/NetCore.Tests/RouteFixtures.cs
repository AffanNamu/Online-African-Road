using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ARO.NetCore;

/// <summary>Shared builders: the shipped route data, and tiny synthetic routes with known geometry.</summary>
static class Fx
{
    public static readonly JsonSerializerOptions Opt = new JsonSerializerOptions { IncludeFields = true };
    public static string Path(string f) => System.IO.Path.Combine(AppContext.BaseDirectory, f);
    public static RouteSpec LoadRoute() => JsonSerializer.Deserialize<RouteSpec>(File.ReadAllText(Path("ng-lagos-ibadan.route.json")), Opt);
    public static PropCatalogSpec LoadCatalog() => JsonSerializer.Deserialize<PropCatalogSpec>(File.ReadAllText(Path("prop_catalog.json")), Opt);
    public static T Clone<T>(T v) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Opt), Opt);
    static RouteModel _shipped; public static RouteModel Shipped => _shipped ??= RouteModel.Build(LoadRoute());

    public static RoadProfileSpec Profile(string id = "p", int lanes = 1, float laneWidth = 3.5f, float median = 0f, float shoulder = 1.5f, float verge = 1f, string centre = "dashed", string barrier = "none") =>
        new RoadProfileSpec { id = id, lanesPerDirection = lanes, laneWidth = laneWidth, medianWidth = median, shoulderWidth = shoulder, vergeWidth = verge, centreLine = centre, edgeBarrier = barrier };

    /// <summary>A dead-straight road heading east (+x) from the origin at 10 m elevation on perfectly flat terrain.</summary>
    public static RouteSpec Straight(float length = 2000f, params RoadProfileSpec[] profiles)
    {
        if (profiles.Length == 0) profiles = new[] { Profile() };
        var pts = new[]
        {
            new ControlPoint { id = "a", name = "A", x = 0, z = 0, elevation = 10, zone = "rural", surface = "good", profile = profiles[0].id },
            new ControlPoint { id = "b", name = "B", x = length, z = 0, elevation = 10, zone = "rural", surface = "good", profile = profiles[0].id },
        };
        return new RouteSpec
        {
            schemaVersion = 1, routeId = "test-straight", name = "Test", seed = 7,
            meta = new RouteMeta { country = "NG", from = "A", to = "B", status = "prototype", worldMapRouteId = "x", realRoadKm = 2f, gameLengthKm = 2f, gameCoverage = "all of it", elevationSource = "design" },
            terrain = new TerrainSpec { baseElevation = 10f, hillAmplitude = 0f, detailAmplitude = 0f, maxEmbankmentSlope = 0.5f },
            profiles = profiles, controlPoints = pts, bridges = new BridgeSpec[0], junctions = new JunctionSpec[0], propRules = new PropRule[0], props = new PropPlacementSpec[0],
            traffic = new TrafficSpec(), waypoints = new WaypointSpec[0]
        };
    }

    /// <summary>All triangles of a submesh as (normal, centroid).</summary>
    public static System.Collections.Generic.IEnumerable<(V3 n, V3 c)> Tris(MeshBuffers mb, int sub)
    {
        var t = mb.Triangles[sub];
        for (int i = 0; i < t.Count; i += 3)
        {
            var a = mb.Vertices[t[i]]; var b = mb.Vertices[t[i + 1]]; var c = mb.Vertices[t[i + 2]];
            yield return (MeshBuffers.Cross(b - a, c - a), new V3((a.X + b.X + c.X) / 3f, (a.Y + b.Y + c.Y) / 3f, (a.Z + b.Z + c.Z) / 3f));
        }
    }

    public static float Area(MeshBuffers mb, int sub) => Tris(mb, sub).Sum(t => t.n.Length * 0.5f);
    public static MeshBuffers WholeRoad(RoadCorridor c, Func<float, float, float> ground = null) => RoadGeometry.BuildChunk(c, -1e6f, -1e6f, 1e6f, 1e6f, new V3(0, 0, 0), ground);
}
