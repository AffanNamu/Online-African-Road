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
        Assert.True(withE1 >= 6, "expected kerbed road in at least six chunk meshes, got " + withE1);
        Assert.InRange(tris, 20000, 200000);
    }

    [Fact] public void TheCorridorHoldsTheArterialAndTheCollectorWithTheirSymbolsAndDropsTheKerbsAtTheMouths()
    {
        var m = Model; var main = m.Main;
        Assert.Equal(3, m.Sides.Count);
        Assert.Equal("planter", main.Runs[0].Section.Spec.medianStyle); Assert.True(main.Runs[0].Section.HasMedian);
        Assert.False(main.Runs[main.Runs.Length - 1].Section.HasMedian);
        Assert.All(m.Sides, s => Assert.True(s.Runs[0].Section.UsesE1Strips));
        Assert.Equal(12, main.Symbols.Length); Assert.Equal(1, m.CorridorById("j_local").Symbols.Length); Assert.Equal(2, m.CorridorById("j_service").Symbols.Length);
        Assert.True(main.KerbDropped(840f, 1) && !main.KerbDropped(840f, -1));         // j_local leaves to the right
        Assert.True(main.KerbDropped(1020f, -1) && !main.KerbDropped(1020f, 1));        // j_service leaves to the left
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
        string digest; using (var sha = System.Security.Cryptography.SHA256.Create()) digest = string.Concat(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(h.ToString())).Select(b => b.ToString("x2")));
        string path = Path.Combine(RepoRoot(), "tests", "NetCore.Tests", "golden", "e1_streets.sha256");
        if (Environment.GetEnvironmentVariable("GOLDEN_REGEN") == "1") { File.WriteAllText(path, digest + "\n"); return; }
        Assert.True(File.Exists(path), "golden/e1_streets.sha256 missing: GOLDEN_REGEN=1 dotnet test --filter KanoE1Route (only for a deliberate change)");
        Assert.Equal(File.ReadAllText(path).Trim(), digest);
    }
}
