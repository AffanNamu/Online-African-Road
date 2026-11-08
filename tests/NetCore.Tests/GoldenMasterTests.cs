using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ARO.NetCore;
using Xunit;

/// <summary>
/// The golden master of the shared road engine (docs/ROAD_ENGINE_PROPOSAL.md, section 5 rule 2): every existing route must build EXACTLY as it does today after every engine stage
/// (cross-section elements, site plates, junction forms, the road graph, roundabouts, flyovers). For each shipped route spec this hashes the road mesh of sampled chunks, the terrain
/// tiles, the ground height under a grid of points along the route, and the prop scatter. The expected hashes are committed in tests/NetCore.Tests/golden/golden_master.json.
/// A stage that changes a hash for an existing route is rejected, not "fixed" by regenerating: regeneration (GOLDEN_REGEN=1) is only for a deliberate change to the shipped route DATA or the
/// catalog, and the diff of the JSON is part of the review.
/// </summary>
public class GoldenMasterTests
{
    static string RepoRoot() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d != null && !File.Exists(Path.Combine(d.FullName, "CLAUDE.md"))) d = d.Parent; return d?.FullName ?? throw new InvalidOperationException("repository root not found"); }
    static string GoldenPath => Path.Combine(RepoRoot(), "tests", "NetCore.Tests", "golden", "golden_master.json");

    static readonly Dictionary<string, Func<RouteSpec>> Routes = new Dictionary<string, Func<RouteSpec>>
    {
        ["ng-lagos-ibadan"] = () => Fx.LoadRoute(),
        ["ng-kano-slice-draft"] = () => JsonSerializer.Deserialize<RouteSpec>(File.ReadAllText(Path.Combine(RepoRoot(), "docs", "kano", "ng-kano-slice.route.draft.json")), Fx.Opt),
    };

    sealed class Hasher
    {
        readonly StringBuilder _sb = new StringBuilder();
        public void F(float v) { _sb.Append(Math.Round(v, 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(','); }
        public void I(int v) { _sb.Append(v).Append(','); }
        public void S(string v) { _sb.Append(v).Append('|'); }
        public void Mesh(MeshBuffers mb)
        {
            I(mb.VertexCount); foreach (var v in mb.Vertices) { F(v.X); F(v.Y); F(v.Z); } foreach (var u in mb.Uvs) { F(u.X); F(u.Y); }
            I(mb.Triangles.Length); foreach (var t in mb.Triangles) { I(t.Count); foreach (int i in t) I(i); }
        }
        public string Done() { using (var sha = SHA256.Create()) return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(_sb.ToString())).Select(b => b.ToString("x2"))); }
    }

    static Dictionary<string, string> Compute(RouteSpec spec)
    {
        var m = RouteModel.Build(spec); var main = m.Main; var res = new Dictionary<string, string>();
        // road meshes: every 7th 200 m chunk that holds road, main corridor and every side corridor
        var hr = new Hasher(); var ht = new Hasher(); var hp = new Hasher(); var hg = new Hasher();
        var chunks = new SortedSet<(int, int)>();
        foreach (var c in m.AllCorridors) for (int i = 0; i < c.RingS.Length; i++) chunks.Add(((int)Math.Floor(c.RingX[i] / 200f), (int)Math.Floor(c.RingZ[i] / 200f)));
        int n = 0, built = 0;
        foreach (var (cx, cz) in chunks)
        {
            if (n++ % 7 != 0) continue; built++;
            float x0 = cx * 200f, z0 = cz * 200f; var origin = new V3(x0, 0, z0);
            foreach (var c in m.AllCorridors) { hr.S(c.Id); hr.Mesh(RoadGeometry.BuildChunk(c, x0, z0, x0 + 200f, z0 + 200f, origin, (x, z) => m.Terrain.HeightAt(x, z))); }
            ht.Mesh(TerrainGeometry.BuildTile(m.Terrain, x0, z0, 200f, 20, origin, true, true));
            foreach (var p in PropScatter.PlaceChunk(m, x0, z0, x0 + 200f, z0 + 200f, 1f)) { hp.S(p.Prop); hp.S(p.Origin ?? ""); hp.F(p.Pos.X); hp.F(p.Pos.Y); hp.F(p.Pos.Z); hp.F(p.YawDeg); hp.F(p.Scale); }
        }
        for (float s = 0; s < main.Length; s += main.Length / 400f) { main.Spline.FrameAt(s, out var pos, out _, out var right); foreach (float off in new[] { -9f, -3f, 0f, 3f, 9f, 25f }) { float x = pos.X + right.X * off, z = pos.Z + right.Z * off; hg.F(m.GroundHeight(x, z)); hg.F(m.Terrain.HeightAt(x, z)); } }
        res["chunks"] = built.ToString(); res["road"] = hr.Done(); res["terrain"] = ht.Done(); res["props"] = hp.Done(); res["ground"] = hg.Done();
        return res;
    }

    [Fact] public void EveryExistingRouteBuildsExactlyAsItDidWhenTheGoldenMasterWasMade()
    {
        var now = Routes.ToDictionary(kv => kv.Key, kv => Compute(kv.Value()));
        if (Environment.GetEnvironmentVariable("GOLDEN_REGEN") == "1") { Directory.CreateDirectory(Path.GetDirectoryName(GoldenPath)); File.WriteAllText(GoldenPath, JsonSerializer.Serialize(now, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false)); return; }
        Assert.True(File.Exists(GoldenPath), "tests/NetCore.Tests/golden/golden_master.json is missing: GOLDEN_REGEN=1 dotnet test --filter GoldenMaster (only for a deliberate change)");
        var want = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(File.ReadAllText(GoldenPath));
        foreach (var route in Routes.Keys)
        {
            Assert.True(want.ContainsKey(route), "no golden master for " + route);
            foreach (var part in new[] { "chunks", "road", "terrain", "props", "ground" })
                Assert.True(want[route][part] == now[route][part], $"{route}: the {part} of an existing route changed ({want[route][part]} -> {now[route][part]}). An engine stage must not change an existing route; see ROAD_ENGINE_PROPOSAL.md section 5.");
        }
    }

    [Fact] public void TheGoldenMasterCoversEnoughOfEachRoute()
    {
        foreach (var kv in Routes) { var c = Compute(kv.Value()); Assert.True(int.Parse(c["chunks"]) >= 3, kv.Key + ": too few chunks sampled"); }
    }

    [Fact] public void TheComputationIsDeterministicWithinOneProcess()
    {
        var spec = Fx.LoadRoute(); var a = Compute(spec); var b = Compute(Fx.LoadRoute()); foreach (var k in a.Keys) Assert.Equal(a[k], b[k]);
    }
}
