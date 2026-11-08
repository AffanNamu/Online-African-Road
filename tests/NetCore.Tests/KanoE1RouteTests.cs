using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ARO.NetCore;
using Xunit;

/// <summary>
/// The E1 street-kit test corridor (Assets/_Project/Resources/Routes/ng-kano-e1-streets.route.json): an arterial with a planter median, a collector street with kerbs and walkways, a local street, a service road for
/// logistics sites, dropped kerbs at the road mouths and pavement symbols. It is an ENGINE TEST route, not a playable one. It must pass the game's own validator, build clean meshes in every
/// chunk the streamer would ask for, and keep the numbers it has (its own golden master, so a later stage cannot change what E1 built without a deliberate regeneration).
/// </summary>
public class KanoE1RouteTests
{
    static readonly JsonSerializerOptions Opt = new JsonSerializerOptions { IncludeFields = true };
    static string RepoRoot() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !File.Exists(Path.Combine(d.FullName, "CLAUDE.md"))) d = d.Parent; return d?.FullName ?? throw new InvalidOperationException("repository root not found"); }
    public static RouteSpec Load() => JsonSerializer.Deserialize<RouteSpec>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "ng-kano-e1-streets.route.json")), Opt);
    static RouteModel _m; static RouteModel Model => _m ??= RouteModel.Build(Load());
    const float Chunk = 200f;

    static IEnumerable<(int, int)> Chunks(RouteModel m) =>
        m.AllCorridors.SelectMany(c => Enumerable.Range(0, c.RingS.Length).Select(i => ((int)Math.Floor(c.RingX[i] / Chunk), (int)Math.Floor(c.RingZ[i] / Chunk)))).Distinct().OrderBy(t => t.Item1).ThenBy(t => t.Item2);

    /// <summary>Every chunk that a road ring or a corner or edge sample of a plate falls into (a plate reaches chunks no road passes through).</summary>
    static IEnumerable<(int, int)> ChunksWithSites(RouteModel m)
    {
        var set = new HashSet<(int, int)>(Chunks(m));
        foreach (var p in m.Sites)
            for (float u = -p.HalfW; u <= p.HalfW + 1e-3f; u += 20f) for (float v = -p.HalfD; v <= p.HalfD + 1e-3f; v += 20f)
            { var w = p.ToWorld(u, v); set.Add(((int)Math.Floor(w.X / Chunk), (int)Math.Floor(w.Z / Chunk))); }
        return set.OrderBy(t => t.Item1).ThenBy(t => t.Item2);
    }

    [Fact] public void TheRoutePassesTheGamesOwnValidatorWithTheShippedCatalogue()
    {
        var known = new HashSet<string>(Fx.LoadCatalog().props.Select(p => p.id));
        var errors = RouteSpecValidator.Validate(Load(), known);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact] public void EveryProfileIsExactlyItsRoadClassFromTheCatalogue()
    {
        foreach (var p in Load().profiles)
        {
            var want = RoadClasses.Make(p.roadClass, p.id);
            Assert.Equal(JsonSerializer.Serialize(want, Opt), JsonSerializer.Serialize(p, Opt));        // the data file may not drift from the class definitions
        }
    }

    [Fact] public void EveryChunkOfEveryCorridorBuildsACleanMeshAndTheRoadIsAnE1Road()
    {
        var m = Model; int withE1 = 0, tris = 0;
        foreach (var (cx, cz) in Chunks(m))
            foreach (var c in m.AllCorridors)
            {
                var mb = RoadGeometry.BuildChunk(c, cx * Chunk, cz * Chunk, (cx + 1) * Chunk, (cz + 1) * Chunk, new V3(cx * Chunk, 0, cz * Chunk), (x, z) => m.Terrain.HeightAt(x, z));
                Assert.True(mb.Validate(out var err), c.Id + " chunk " + cx + "," + cz + ": " + err);
                if (mb.Triangles.Length == RoadSub.CountE1 && mb.Triangles[RoadSub.Sidewalk].Count + mb.Triangles[RoadSub.KerbPainted].Count + mb.Triangles[RoadSub.Kerb].Count > 0) withE1++;
                tris += mb.TriangleCount;
            }
        // the site plates: every chunk that holds a piece builds a clean mesh, and the pieces add up to the whole plate
        foreach (var plate in m.Sites)
        {
            float area = 0f;
            foreach (var (cx, cz) in ChunksWithSites(m))
            {
                var mb = SiteGeometry.BuildChunk(plate, cx * Chunk, cz * Chunk, (cx + 1) * Chunk, (cz + 1) * Chunk, new V3(cx * Chunk, 0, cz * Chunk));
                Assert.True(mb.Validate(out var err), plate.Id + " chunk " + cx + "," + cz + ": " + err);
                area += Fx.Area(mb, plate.TopSub);
            }
            Assert.Equal(plate.Spec.widthM * plate.Spec.depthM, area, 1);
        }
        Assert.True(withE1 >= 6, "expected kerbed road in at least six chunk meshes, got " + withE1);
        Assert.InRange(tris, 20000, 200000);
    }

    [Fact] public void TheCorridorHoldsTheArterialAndTheCollectorWithTheirSymbolsAndDropsTheKerbsAtTheMouths()
    {
        var m = Model; var main = m.Main;
        Assert.Equal(6, m.Sides.Count); Assert.Equal(2, m.Sites.Count);
        Assert.Equal("planter", main.Runs[0].Section.Spec.medianStyle); Assert.True(main.Runs[0].Section.HasMedian);
        Assert.False(main.Runs[main.Runs.Length - 1].Section.HasMedian);
        Assert.All(m.Sides, s => Assert.True(s.Runs[0].Section.UsesE1Strips)); Assert.Empty(m.CheckSites());
        Assert.Equal(12, main.Symbols.Length); Assert.Equal(1, m.CorridorById("j_cross").Symbols.Length); Assert.Equal(1, m.CorridorById("j_cross_left").Symbols.Length); Assert.Single(main.MedianOpenings); Assert.Equal(1, m.CorridorById("j_local").Symbols.Length); Assert.Equal(2, m.CorridorById("j_service").Symbols.Length);
        Assert.True(main.KerbDropped(840f, 1) && !main.KerbDropped(840f, -1));         // j_local leaves to the right
        Assert.True(main.KerbDropped(1020f, -1) && !main.KerbDropped(1020f, 1));        // j_service leaves to the left
    }

    // footprint of a prop in plate coordinates: (centre, half sizes, yaw)
    static (float cx, float cz, float hw, float hd, float yaw) Foot(SitePropSpec p, PropDef d) => (p.x, p.z, d.widthM * 0.5f * p.scale, d.depthM * 0.5f * p.scale, p.yawDeg);
    static float[][] Corners((float cx, float cz, float hw, float hd, float yaw) f)
    {
        double a = Math.PI * f.yaw / 180.0; float s = (float)Math.Sin(a), c = (float)Math.Cos(a);      // forward (s, c) and right (c, -s) in (u, v)
        var res = new List<float[]>();
        foreach (var (su, sv) in new[] { (-1, -1), (-1, 1), (1, 1), (1, -1) }) res.Add(new[] { f.cx + c * su * f.hw + s * sv * f.hd, f.cz - s * su * f.hw + c * sv * f.hd });
        return res.ToArray();
    }
    static bool Separated(float[][] a, float[][] b, float gap)
    {
        foreach (var poly in new[] { a, b })
            for (int i = 0; i < 4; i++)
            {
                float ex = poly[(i + 1) % 4][0] - poly[i][0], ez = poly[(i + 1) % 4][1] - poly[i][1], nx = -ez, nz = ex, len = (float)Math.Sqrt(nx * nx + nz * nz); nx /= len; nz /= len;
                float minA = float.MaxValue, maxA = float.MinValue, minB = float.MaxValue, maxB = float.MinValue;
                foreach (var q in a) { float d = q[0] * nx + q[1] * nz; minA = Math.Min(minA, d); maxA = Math.Max(maxA, d); }
                foreach (var q in b) { float d = q[0] * nx + q[1] * nz; minB = Math.Min(minB, d); maxB = Math.Max(maxB, d); }
                if (maxA + gap <= minB || maxB + gap <= minA) return true;
            }
        return false;
    }

    [Fact] public void TheYardAndTheTerminalAreFurnishedInsideTheirPlatesWithoutOverlap()
    {
        var cat = Fx.LoadCatalog().props.ToDictionary(p => p.id); var m = Model;
        foreach (var plate in m.Sites)
        {
            var solid = new List<(string id, float[][] c)>();
            foreach (var p in plate.Spec.props)
            {
                var d = cat[p.prop]; if (d.category == "lights" || d.category == "vegetation") continue;       // poles and canopies are not footprints
                var c = Corners(Foot(p, d));
                foreach (var q in c) { Assert.True(Math.Abs(q[0]) <= plate.HalfW + 1e-3f && Math.Abs(q[1]) <= plate.HalfD + 1e-3f, plate.Id + "/" + p.id + " (" + p.prop + ") sticks out of the plate"); }
                solid.Add((p.id, c));
            }
            for (int i = 0; i < solid.Count; i++) for (int j = i + 1; j < solid.Count; j++)
                Assert.True(Separated(solid[i].c, solid[j].c, 0.2f), plate.Id + ": " + solid[i].id + " and " + solid[j].id + " overlap");
        }
    }

    [Fact] public void ThePlatesAreLevelWithTheEndsOfTheirRoadsAndFurnishedTheSameEveryTime()
    {
        var m = Model; var a = PropScatter.PlaceChunk(m, -1000f, -1000f, 3000f, 1000f); var b = PropScatter.PlaceChunk(m, -1000f, -1000f, 3000f, 1000f);
        Assert.Equal(a.Count, b.Count); for (int i = 0; i < a.Count; i++) { Assert.Equal(a[i].Prop, b[i].Prop); Assert.Equal(a[i].Pos.X, b[i].Pos.X); Assert.Equal(a[i].Pos.Z, b[i].Pos.Z); }
        Assert.Equal(m.Sites.Sum(p => p.Spec.props.Length), a.Count(i => i.Origin != null && i.Origin.StartsWith("site_")));
        foreach (var plate in m.Sites)
        {
            var road = m.CorridorById(plate.Spec.corridor);
            Assert.Equal(road.Spline.ElevationAt(road.Length), plate.BaseY, 3);
            Assert.All(a.Where(i => i.Origin != null && i.Origin.StartsWith(plate.Id + "/")), i => Assert.Equal(plate.BaseY, i.Pos.Y, 3));
        }
    }

    [Fact] public void TheRouteKeepsTheNumbersItHadWhenE1WasMade()
    {
        var m = Model; var h = new System.Text.StringBuilder();
        foreach (var (cx, cz) in Chunks(m))
            foreach (var c in m.AllCorridors)
            {
                var mb = RoadGeometry.BuildChunk(c, cx * Chunk, cz * Chunk, (cx + 1) * Chunk, (cz + 1) * Chunk, new V3(cx * Chunk, 0, cz * Chunk), (x, z) => m.Terrain.HeightAt(x, z));
                h.Append(c.Id).Append(':').Append(mb.VertexCount).Append('/'); foreach (var t in mb.Triangles) h.Append(t.Count).Append(',');
                foreach (var v in mb.Vertices) h.Append(Math.Round(v.X, 3)).Append(Math.Round(v.Y, 3)).Append(Math.Round(v.Z, 3));
            }
        foreach (var plate in m.Sites)
            foreach (var (cx, cz) in ChunksWithSites(m))
            {
                var mb = SiteGeometry.BuildChunk(plate, cx * Chunk, cz * Chunk, (cx + 1) * Chunk, (cz + 1) * Chunk, new V3(cx * Chunk, 0, cz * Chunk));
                h.Append(plate.Id).Append(':').Append(mb.VertexCount).Append('/'); foreach (var tr in mb.Triangles) h.Append(tr.Count).Append(',');
                foreach (var v in mb.Vertices) h.Append(Math.Round(v.X, 3)).Append(Math.Round(v.Y, 3)).Append(Math.Round(v.Z, 3));
            }
        foreach (var inst in PropScatter.PlaceChunk(m, -1000f, -1000f, 3000f, 1000f)) h.Append(inst.Prop).Append(Math.Round(inst.Pos.X, 2)).Append(Math.Round(inst.Pos.Z, 2)).Append(Math.Round(inst.YawDeg, 1));
        for (float x = 900f; x <= 1500f; x += 37f) for (float z = -300f; z <= 400f; z += 41f) h.Append(Math.Round(m.Terrain.HeightAt(x, z), 3));
        string digest; using (var sha = System.Security.Cryptography.SHA256.Create()) digest = string.Concat(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(h.ToString())).Select(b => b.ToString("x2")));
        string path = Path.Combine(RepoRoot(), "tests", "NetCore.Tests", "golden", "e1_streets.sha256");
        if (Environment.GetEnvironmentVariable("GOLDEN_REGEN") == "1") { File.WriteAllText(path, digest + "\n"); return; }
        Assert.True(File.Exists(path), "golden/e1_streets.sha256 missing: GOLDEN_REGEN=1 dotnet test --filter KanoE1Route (only for a deliberate change)");
        Assert.Equal(File.ReadAllText(path).Trim(), digest);
    }
}
