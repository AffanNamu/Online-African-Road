using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using ARO.NetCore;
using Xunit;

/// <summary>
/// The Kano vertical-slice DRAFT checked by the game's OWN rules: the route data must pass RouteSpecValidator and build a RouteModel, every planned prefab must
/// pass PropCatalogValidator and must not collide with an id already in the game (a production prefab replaces the placeholder of the same id in EVERY route),
/// the deterministic scatter must not put two plots on top of each other or a plot on a road, and the numbers the plan quotes (instances, triangles, draw-call
/// upper bound per 1 km window) are generated here from the real scatter, not typed. The Blender greybox is built from the two generated files, so what is
/// reviewed is what the game would place.
/// Regenerate the files with: KANO_REGEN=1 dotnet test --filter KanoSlice
/// </summary>
public class KanoSliceTests
{
    static readonly JsonSerializerOptions Opt = new JsonSerializerOptions { IncludeFields = true };
    static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !(Directory.Exists(Path.Combine(d.FullName, "Assets")) && Directory.Exists(Path.Combine(d.FullName, "docs")))) d = d.Parent;
        Assert.NotNull(d); return d.FullName;
    }
    static string KanoPath(string f) => Path.Combine(RepoRoot(), "docs", "kano", f);
    static RouteSpec Draft() => JsonSerializer.Deserialize<RouteSpec>(File.ReadAllText(KanoPath("ng-kano-slice.route.draft.json")), Opt);
    static PropCatalogSpec Inventory() => JsonSerializer.Deserialize<PropCatalogSpec>(File.ReadAllText(KanoPath("kano_asset_inventory.json")), Opt);
    static HashSet<string> KnownIds() { var s = new HashSet<string>(Fx.LoadCatalog().props.Select(p => p.id)); foreach (var p in Inventory().props) s.Add(p.id); return s; }

    const float Chunk = 200f;       // ChunkStreamer.chunkSize
    static RouteModel _model; static RouteModel Model => _model ??= RouteModel.Build(Draft());

    // ---- every chunk of the corridor, exactly as the streamer would ask for them
    static List<(int cx, int cz, List<PropInstance> inst)> Chunks(float density = 1f)
    {
        var m = Model; var ids = new HashSet<(int, int)>();
        foreach (var c in m.AllCorridors) for (int i = 0; i < c.RingS.Length; i++)
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                ids.Add(((int)Math.Floor(c.RingX[i] / Chunk) + dx, (int)Math.Floor(c.RingZ[i] / Chunk) + dz));
        return ids.OrderBy(t => t.Item1).ThenBy(t => t.Item2)
                  .Select(t => (t.Item1, t.Item2, PropScatter.PlaceChunk(m, t.Item1 * Chunk, t.Item2 * Chunk, (t.Item1 + 1) * Chunk, (t.Item2 + 1) * Chunk, density))).ToList();
    }

    [Fact]
    public void TheDraftRoutePassesTheGamesOwnValidatorAndBuildsARoadModel()
    {
        var spec = Draft();
        var errors = RouteSpecValidator.Validate(spec, KnownIds());
        Assert.True(errors.Count == 0, string.Join("\n", errors));
        Assert.True(RouteModel.TryBuild(spec, out var m, out var e2), string.Join("\n", e2 ?? new List<string>()));
        Assert.InRange(m.Length, 1600f, 1760f);                       // the 1.65 km the draft claims
        Assert.Equal(6, m.Sides.Count);                               // six T-junctions (>= 150 m apart, never on the divided entrance: the validator enforces both)
        Assert.All(m.AllCorridors, c => Assert.True(c.Runs.All(r => r.Section.PavedHalfWidth > 0)));
        Assert.Equal("kaduna-kano", spec.meta.worldMapRouteId);       // the slice is the Kano end of a route that already exists on the world map
        var map = System.Text.Json.JsonSerializer.Deserialize<WorldMapData>(File.ReadAllText(Fx.Path("world_map.json")), Opt);
        Assert.Contains(map.routes, r => r.id == spec.meta.worldMapRouteId);
    }

    [Fact]
    public void ThePlannedPrefabsPassTheCatalogueBudgetsAndDoNotReuseAnExistingId()
    {
        var inv = Inventory();
        var errors = PropCatalogValidator.Validate(inv);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
        var existing = new HashSet<string>(Fx.LoadCatalog().props.Select(p => p.id));
        var clash = inv.props.Where(p => existing.Contains(p.id)).Select(p => p.id).ToList();
        Assert.True(clash.Count == 0, "reusing an id would replace the placeholder in every route, Lagos-Ibadan included: " + string.Join(", ", clash));
        Assert.All(inv.props, p => Assert.StartsWith("kano_", p.id));
        // every planned prefab names the reference gap(s) that block it, and none is marked ready while the register says the references are missing
        var rawInv = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(KanoPath("kano_asset_inventory.json"))).GetProperty("props").EnumerateArray().ToList();
        var register = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(KanoPath("reference_register.json"))).GetProperty("missingReferences").EnumerateArray().ToDictionary(e => e.GetProperty("id").GetString());
        foreach (var rp in rawInv)
        {
            var ids = rp.GetProperty("referenceIds").EnumerateArray().Select(e => e.GetString()).ToList();
            Assert.True(ids.Count > 0, rp.GetProperty("id").GetString() + " is planned with no reference slot in the register");
            Assert.All(ids, i => Assert.True(register.ContainsKey(i), "unknown reference id " + i));
            if (rp.GetProperty("readyToModel").GetBoolean()) Assert.All(ids, i => Assert.DoesNotContain("MISSING", register[i].GetProperty("status").GetString()));
        }
        // every prop the route uses is planned: nothing refers to an id nobody will model
        var spec = Draft(); var used = spec.propRules.Select(r => r.prop).Concat(spec.props.Select(p => p.prop)).ToHashSet();
        Assert.True(used.All(u => inv.props.Any(p => p.id == u)), "route uses ids missing from the inventory: " + string.Join(", ", used.Where(u => inv.props.All(p => p.id != u))));
        // and every planned prop is used (an unused planned asset is wasted modelling)
        Assert.True(inv.props.All(p => used.Contains(p.id)), "planned but unused: " + string.Join(", ", inv.props.Where(p => !used.Contains(p.id)).Select(p => p.id)));
    }

    // ---- oriented footprints
    sealed class Foot { public string Prop, Cat; public float X, Z, Yaw, W, D; }
    static IEnumerable<Foot> Feet(PropCatalogSpec inv, IEnumerable<PropInstance> insts)
    {
        var by = inv.props.ToDictionary(p => p.id);
        foreach (var i in insts) { var d = by[i.Prop]; yield return new Foot { Prop = i.Prop, Cat = d.category, X = i.Pos.X, Z = i.Pos.Z, Yaw = i.YawDeg, W = d.widthM * i.Scale, D = d.depthM * i.Scale }; }
    }
    // footprint corners; the prefab's front (+Z) points along yaw (bearing), width along its right
    static (float x, float z)[] Corners(Foot f, float shrink = 0f)
    {
        double a = f.Yaw * Math.PI / 180.0; float fx = (float)Math.Sin(a), fz = (float)Math.Cos(a), rx = fz, rz = -fx;
        float hw = f.W / 2 - shrink, hd = f.D / 2 - shrink;
        return new[] { (f.X + rx * hw + fx * hd, f.Z + rz * hw + fz * hd), (f.X - rx * hw + fx * hd, f.Z - rz * hw + fz * hd), (f.X - rx * hw - fx * hd, f.Z - rz * hw - fz * hd), (f.X + rx * hw - fx * hd, f.Z + rz * hw - fz * hd) };
    }
    static bool Separated((float x, float z)[] a, (float x, float z)[] b)      // separating-axis test of two convex quads
    {
        foreach (var poly in new[] { a, b })
            for (int i = 0; i < 4; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % 4]; float nx = q.z - p.z, nz = p.x - q.x;
                float aMin = float.MaxValue, aMax = float.MinValue, bMin = float.MaxValue, bMax = float.MinValue;
                foreach (var c in a) { float d = c.x * nx + c.z * nz; aMin = Math.Min(aMin, d); aMax = Math.Max(aMax, d); }
                foreach (var c in b) { float d = c.x * nx + c.z * nz; bMin = Math.Min(bMin, d); bMax = Math.Max(bMax, d); }
                if (aMax <= bMin || bMax <= aMin) return true;
            }
        return false;
    }
    static readonly HashSet<string> SolidCategories = new HashSet<string> { "buildings" };

    [Fact]
    public void NoTwoBuildingsOverlapAndNoBuildingStandsOnARoad()
    {
        var inv = Inventory(); var all = Chunks().SelectMany(c => c.inst).ToList();
        var b = Feet(inv, all).Where(f => SolidCategories.Contains(f.Cat)).ToList();
        Assert.True(b.Count > 60, "expected a populated slice, got " + b.Count + " buildings");
        var problems = new List<string>();
        for (int i = 0; i < b.Count; i++)
            for (int j = i + 1; j < b.Count; j++)
            {
                if (Math.Abs(b[i].X - b[j].X) > 40 || Math.Abs(b[i].Z - b[j].Z) > 40) continue;
                if (!Separated(Corners(b[i], 0.05f), Corners(b[j], 0.05f))) problems.Add($"overlap {b[i].Prop}@({b[i].X:0},{b[i].Z:0}) / {b[j].Prop}@({b[j].X:0},{b[j].Z:0})");
            }
        // against every corridor: a building must clear the road FORMATION by at least its apron setback (the footprint's road-side edge starts at/after the formation edge)
        foreach (var f in b)
            foreach (var cor in Model.AllCorridors)
                foreach (var (x, z) in Corners(f))
                    if (cor.Spline.Nearest(x, z, cor.MaxFormationHalf + 2f, out float s, out float lat, out _))
                    {
                        int ri = cor.RunIndexAt(s); float half = cor.Runs[ri].Section.FormationHalfWidth * cor.ScaleAt(ri, s);
                        if (Math.Abs(lat) < half - 0.05f) problems.Add($"{f.Prop}@({f.X:0},{f.Z:0}) has a corner {half - Math.Abs(lat):0.0} m inside the formation of {cor.Id}");
                    }
        Assert.True(problems.Count == 0, problems.Count + " problems:\n" + string.Join("\n", problems.Distinct().Take(25)));
    }

    [Fact]
    public void TheMosqueFacesTheQiblaWhateverTheRoadDoes()
    {
        var mosque = Chunks().SelectMany(c => c.inst).Single(i => i.Prop == "kano_mosque_neighbourhood_a");
        var frame = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(KanoPath("kano_world_frame.json")));
        double qibla = frame.GetProperty("qibla").GetProperty("bearingDeg").GetDouble();
        double diff = ((mosque.YawDeg - qibla + 540) % 360) - 180;
        Assert.InRange(Math.Abs(diff), 0, 1.0);
    }

    [Fact]
    public void TheWorldFrameIsOnTheChunkLatticeAndSaysWhatIsAndIsNotVerified()
    {
        var f = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(KanoPath("kano_world_frame.json")));
        var start = f.GetProperty("slice").GetProperty("startXZ"); Assert.Equal(0.0, start[0].GetDouble() % Chunk); Assert.Equal(0.0, start[1].GetDouble() % Chunk);
        var spec = Draft(); Assert.Equal(start[0].GetDouble(), spec.controlPoints[0].x, 1); Assert.Equal(start[1].GetDouble(), spec.controlPoints[0].z, 1);
        Assert.True(f.GetProperty("slice").GetProperty("fictional").GetBoolean());
        foreach (var a in f.GetProperty("reservedAnchors").EnumerateArray())
            if (a.GetProperty("lat").ValueKind == JsonValueKind.Null) Assert.Contains("NOT verified", a.GetProperty("verified").GetString());    // no invented coordinates
        // the world map's Kano is about 8 km from the reference point: the file records the discrepancy rather than hiding it
        Assert.InRange(f.GetProperty("worldMapDiscrepancy").GetProperty("distanceKm").GetDouble(), 7.5, 9.0);
    }

    [Fact]
    public void EveryWorkaroundIsDeclaredAndDataOnly()
    {
        var reg = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(KanoPath("kano_workarounds.json")));
        var backlog = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "ROAD_ENGINE_BACKLOG.md"));
        var prefixes = new List<string>(); var spec = Draft(); var inv = Inventory();
        foreach (var w in reg.GetProperty("workarounds").EnumerateArray())
        {
            Assert.True(w.GetProperty("dataOnly").GetBoolean(), w.GetProperty("id").GetString() + ": a workaround whose removal needs code is not allowed");
            Assert.False(string.IsNullOrWhiteSpace(w.GetProperty("removalPath").GetString()));
            foreach (var b in w.GetProperty("standsInFor").EnumerateArray()) Assert.Contains("| " + b.GetString() + " |", backlog);   // the backlog item that removes it exists
            if (w.TryGetProperty("placementIdPrefix", out var pre))
            {
                prefixes.Add(pre.GetString());
                Assert.True(spec.props.Any(p => p.id.StartsWith(pre.GetString())), w.GetProperty("id").GetString() + " is declared but nothing uses it (stale)");
            }
            if (w.TryGetProperty("junctionIds", out var js)) foreach (var j in js.EnumerateArray()) Assert.Contains(spec.junctions, x => x.id == j.GetString());
        }
        var setPieces = reg.GetProperty("setPieces").EnumerateArray().Select(e => e.GetString()).ToHashSet();
        var undeclared = spec.props.Where(p => !prefixes.Any(pre => p.id.StartsWith(pre)) && !setPieces.Contains(p.id)).Select(p => p.id).ToList();
        Assert.True(undeclared.Count == 0, "placements that are neither a declared workaround nor a declared set piece: " + string.Join(", ", undeclared));
        // W-01: every plot prefab has its no-apron twin planned, so the apron can be dropped by renaming ids in the rules
        var plots = inv.props.Where(p => p.tags != null && p.tags.Contains("plot")).ToList();
        Assert.True(plots.Count >= 7);
        var raw = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(KanoPath("kano_asset_inventory.json"))).GetProperty("props").EnumerateArray().ToDictionary(e => e.GetProperty("id").GetString());
        Assert.All(plots, p => Assert.Equal(p.id + "_noapron", raw[p.id].GetProperty("noApronVariant").GetString()));
    }

    // ---- generated evidence: geometry for the Blender greybox, and the budget numbers the plan quotes
    static string Num(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);

    static string GeometryJson()
    {
        var m = Model; var sb = new StringBuilder(); var chunks = Chunks();
        sb.Append("{\n\"note\":\"GENERATED by tests/NetCore.Tests/KanoSliceTests.cs from docs/kano/ng-kano-slice.route.draft.json with the game's own RouteModel and PropScatter. Do not edit; regenerate with KANO_REGEN=1 dotnet test --filter KanoSlice. Axes: x east, z north, y up (Unity); yaw is a bearing in degrees (prefab front +Z).\",\n");
        sb.Append("\"routeLengthM\":" + Num(m.Length) + ",\n\"corridors\":[\n");
        bool firstC = true;
        foreach (var c in m.AllCorridors)
        {
            if (!firstC) sb.Append(",\n"); firstC = false;
            sb.Append("{\"id\":\"" + c.Id + "\",\"main\":" + (c.IsMain ? "true" : "false") + ",\"lengthM\":" + Num(c.Length) + ",\"runs\":[");
            sb.Append(string.Join(",", c.Runs.Select(r => "{\"s0\":" + Num(r.S0) + ",\"s1\":" + Num(r.S1) + ",\"zone\":\"" + r.Zone + "\",\"surface\":\"" + r.Surface + "\",\"profile\":\"" + r.Section.Spec.id + "\",\"hasMedian\":" + (r.Section.HasMedian ? "true" : "false") +
                ",\"carriagewayHalf\":" + Num(r.Section.CarriagewayHalfWidth) + ",\"pavedHalf\":" + Num(r.Section.PavedHalfWidth) + ",\"formationHalf\":" + Num(r.Section.FormationHalfWidth) +
                ",\"section\":[" + string.Join(",", r.Section.Points.Select(p => "[" + Num(p.X) + "," + Num(p.Y) + "," + p.Strip + "]")) + "]}")));
            sb.Append("],\"samples\":[");
            var parts = new List<string>();
            for (float s = 0; s < c.Length + 4.99f; s += 5f)
            {
                float ss = Math.Min(s, c.Length); c.Spline.FrameAt(ss, out var pos, out var fwd, out var right);
                parts.Add("[" + Num(ss) + "," + Num(pos.X) + "," + Num(pos.Y) + "," + Num(pos.Z) + "," + Num(fwd.X) + "," + Num(fwd.Z) + "," + Num(c.ScaleAt(ss)) + "," + c.RunIndexAt(ss) + "]");
                if (ss >= c.Length) break;
            }
            sb.Append(string.Join(",", parts)); sb.Append("]}");
        }
        // the game's own ground (road cut and fill included) on a coarse grid around every corridor, so the greybox stands on what the game would
        float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
        foreach (var c in m.AllCorridors) for (int i = 0; i < c.RingS.Length; i++) { minX = Math.Min(minX, c.RingX[i]); maxX = Math.Max(maxX, c.RingX[i]); minZ = Math.Min(minZ, c.RingZ[i]); maxZ = Math.Max(maxZ, c.RingZ[i]); }
        const float step = 20f, margin = 340f; float x0 = (float)Math.Floor((minX - margin) / step) * step, z0 = (float)Math.Floor((minZ - margin) / step) * step;
        int nx = (int)Math.Ceiling((maxX + margin - x0) / step) + 1, nz = (int)Math.Ceiling((maxZ + margin - z0) / step) + 1;
        sb.Append("\n],\n\"terrain\":{\"x0\":" + Num(x0) + ",\"z0\":" + Num(z0) + ",\"step\":" + Num(step) + ",\"nx\":" + nx + ",\"nz\":" + nz + ",\"heights\":[");
        var hs = new List<string>();
        for (int iz = 0; iz < nz; iz++) for (int ix = 0; ix < nx; ix++) hs.Add(Num(Math.Round(m.Terrain.HeightAt(x0 + ix * step, z0 + iz * step) * 20.0) / 20.0));
        sb.Append(string.Join(",", hs)); sb.Append("]},\n\"instances\":[\n");
        var rows = chunks.SelectMany(c => c.inst).OrderBy(i => i.Origin, StringComparer.Ordinal).ThenBy(i => i.Pos.X).ThenBy(i => i.Pos.Z)
                         .Select(i => "{\"prop\":\"" + i.Prop + "\",\"origin\":\"" + i.Origin + "\",\"x\":" + Num(i.Pos.X) + ",\"y\":" + Num(i.Pos.Y) + ",\"z\":" + Num(i.Pos.Z) + ",\"yaw\":" + Num(i.YawDeg) + ",\"scale\":" + Num(i.Scale) + ",\"text\":\"" + (i.Text ?? "") + "\"}");
        sb.Append(string.Join(",\n", rows)); sb.Append("\n]\n}\n");
        return sb.ToString();
    }

    static string StatsJson()
    {
        var m = Model; var inv = Inventory(); var by = inv.props.ToDictionary(p => p.id); var chunks = Chunks();
        var all = chunks.SelectMany(c => c.inst).ToList();
        var sb = new StringBuilder("{\n\"note\":\"GENERATED by KanoSliceTests (see kano_slice_geometry.json). Triangles are the PLANNED LOD budgets of docs/kano/kano_asset_inventory.json, not measurements of any model (none exists). Draw calls are an UPPER BOUND: one per production instance inside its cull distance (a plot prefab is planned as one mesh with one atlas material), counted in all directions (no frustum culling), excluding terrain, road, vehicles and UI. Frame time on a real GPU is unvalidated.\",\n");
        sb.Append("\"routeLengthM\":" + Num(m.Length) + ",\n");
        double good = 0, worn = 0, dmg = 0; foreach (var r in m.Main.Runs) { double l = r.S1 - r.S0; if (r.Surface == Surface.Good) good += l; else if (r.Surface == Surface.Worn) worn += l; else dmg += l; }
        sb.Append("\"surfaceMetres\":{\"good\":" + Num(good) + ",\"worn\":" + Num(worn) + ",\"damaged\":" + Num(dmg) + "},\n");
        sb.Append("\"junctions\":[" + string.Join(",", m.Spec.junctions.Select(j => "{\"id\":\"" + j.id + "\",\"s\":" + Num(j.s) + ",\"angleDeg\":" + Num(j.angleDeg) + ",\"lengthM\":" + Num(j.length) + "}")) + "],\n");
        sb.Append("\"instanceTotals\":{" + string.Join(",", all.GroupBy(i => i.Prop).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => "\"" + g.Key + "\":" + g.Count())) + "},\n");
        sb.Append("\"instanceTotal\":" + all.Count + ",\n");
        // Per quality tier, the worst moment: the player stands on the road centre line every 25 m; a production prefab is drawn only inside its cull distance (LodPolicy.PropCullDistance, scaled by the
        // tier) at the LOD level LodPolicy.PropLodLevel gives for that distance: the policy the generated placeholders follow and that BlenderAssetBuilder reproduces in each prefab's LODGroup.
        // Counted in ALL directions (no frustum culling), so it is an upper bound. PropDensity scales presence. "Instantiated" is what ChunkStreamer keeps alive (every instance of chunks within 2 rings).
        var tiers = new[] { (Name: "low", T: QualityTiers.Low, B: PerfBudget.Mobile), (Name: "medium", T: QualityTiers.Medium, B: PerfBudget.Desktop), (Name: "high", T: QualityTiers.High, B: PerfBudget.Desktop) };
        var tierJson = new List<string>();
        foreach (var tier in tiers)
        {
            var chunkList = Chunks(tier.T.PropDensity); var dict = chunkList.ToDictionary(c => (c.cx, c.cz), c => c.inst);
            var every = chunkList.SelectMany(c => c.inst).ToList();
            int worstCalls = 0, worstTris = 0, worstB = 0, worstLoaded = 0; float worstS = 0;
            for (float st = 0; st <= m.Main.Length; st += 25f)
            {
                var p = m.Main.Spline.PositionAt(st); int pcx = (int)Math.Floor(p.X / Chunk), pcz = (int)Math.Floor(p.Z / Chunk);
                int calls = 0, tris = 0, bld = 0, loaded = 0;
                for (int dx = -2; dx <= 2; dx++) for (int dz = -2; dz <= 2; dz++)
                {
                    if (!dict.TryGetValue((pcx + dx, pcz + dz), out var list)) continue;
                    loaded += list.Count;
                    foreach (var i in list)
                    {
                        var d = by[i.Prop]; float dist = (float)Math.Sqrt((i.Pos.X - p.X) * (i.Pos.X - p.X) + (i.Pos.Z - p.Z) * (i.Pos.Z - p.Z));
                        if (dist > LodPolicy.PropCullDistance(d, tier.T)) continue;
                        calls++; tris += d.lodTriangles[LodPolicy.PropLodLevel(d, dist, tier.T)]; if (d.category == "buildings") bld++;
                    }
                }
                if (calls > worstCalls) { worstCalls = calls; worstS = st; }
                worstTris = Math.Max(worstTris, tris); worstB = Math.Max(worstB, bld); worstLoaded = Math.Max(worstLoaded, loaded);
            }
            tierJson.Add("\"" + tier.Name + "\":{\"propDensity\":" + Num(tier.T.PropDensity) + ",\"propCullScale\":" + Num(tier.T.PropCullScale) + ",\"instancesInSlice\":" + every.Count + ",\"worstMoment\":{\"routeStationM\":" + Num(worstS) + ",\"instantiatedUpperBound\":" + worstLoaded + ",\"drawnUpperBoundDrawCalls\":" + worstCalls + ",\"plannedTriangles\":" + worstTris +
                         ",\"buildingsDrawn\":" + worstB + "},\"budget\":{\"name\":\"" + tier.B.Name + "\",\"triangles\":" + tier.B.MaxTriangles + ",\"drawCalls\":" + tier.B.MaxDrawCalls + "}}");
        }
        sb.Append("\"tiers\":{" + string.Join(",", tierJson) + "}\n}\n");
        return sb.ToString();
    }

    static void CheckGenerated(string file, string text)
    {
        var path = KanoPath(file);
        if (Environment.GetEnvironmentVariable("KANO_REGEN") == "1") { File.WriteAllText(path, text, new UTF8Encoding(false)); return; }
        Assert.True(File.Exists(path), file + " is missing: regenerate with KANO_REGEN=1 dotnet test --filter KanoSlice");
        Assert.True(File.ReadAllText(path) == text, file + " is out of date with the route draft and the scatter: regenerate with KANO_REGEN=1 dotnet test --filter KanoSlice");
    }

    [Fact] public void TheGreyboxGeometryFileIsTheGamesOwnOutput() => CheckGenerated("kano_slice_geometry.json", GeometryJson());
    [Fact] public void TheBudgetNumbersInThePlanAreTheGamesOwnOutput() => CheckGenerated("kano_slice_stats.json", StatsJson());

    // Texture bytes of a block-compressed texture with a full mip chain. The five constants below are what a REAL Unity 6 import produced on the WebGL target (TextureReport run 41277975374).
    static long BlockMipBytes(int w, int h, int blockBytes, int bw = 4, int bh = 4)
    {
        long sum = 0; int n = (int)Math.Floor(Math.Log(Math.Max(w, h), 2)) + 1;
        for (int i = 0; i < n; i++) { sum += (long)((Math.Max(1, w) + bw - 1) / bw) * ((Math.Max(1, h) + bh - 1) / bh) * blockBytes; w /= 2; h /= 2; }
        return sum;
    }

    [Fact]
    public void TheTextureBudgetArithmeticReproducesUnitysOwnProbeNumbers()
    {
        const int Dxt1 = 8, Dxt5 = 16;
        Assert.Equal(2796216, BlockMipBytes(2048, 2048, Dxt1));     // 2048 opaque albedo, DXT1
        Assert.Equal(1398128, BlockMipBytes(1024, 1024, Dxt5));     // 1024 alpha / normal map, DXT5
        Assert.Equal(699064, BlockMipBytes(1024, 1024, Dxt1));      // 1024 ORM, DXT1
        Assert.Equal(349544, BlockMipBytes(1024, 512, Dxt1));       // power-of-two with npotScale None stays DXT1
        Assert.Equal(1398120, BlockMipBytes(2048, 1024, Dxt1));     // the 1672 x 944 world map with default settings: resized and DXT1
        const double MiB = 1048576.0;
        double building = BlockMipBytes(2048, 2048, Dxt1) + BlockMipBytes(1024, 1024, Dxt5) + BlockMipBytes(1024, 1024, Dxt1);
        double prop = BlockMipBytes(1024, 1024, Dxt1) + BlockMipBytes(512, 512, Dxt5) + BlockMipBytes(512, 512, Dxt1);
        double veg = BlockMipBytes(1024, 1024, Dxt5) + BlockMipBytes(512, 512, Dxt5);
        double plan = (6 * building + 4 * prop + 2 * veg) / MiB;
        Assert.InRange(plan, 35.95, 36.05);                         // the 36.0 MiB quoted in docs/TEXTURE_BUDGET.md
        const double measuredDrivingBaselineMb = 8.4;               // [Perf] textureMB, medium tier, real WebGL player in CI
        Assert.True(plan + measuredDrivingBaselineMb <= PerfBudget.Desktop.MaxTextureMb * 0.5, "textures exceed half of the desktop budget");
        Assert.True(plan + measuredDrivingBaselineMb <= PerfBudget.Mobile.MaxTextureMb * 0.5, "textures exceed half of the mobile budget (if the target compresses)");
        var f = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(KanoPath("texture_findings.json")));
        Assert.Contains("36.0 MiB", f.GetProperty("summary").GetString());
    }

    [Fact]
    public void TheSliceStaysInsideTheBudgetEnvelopeItClaimsOnEveryTier()
    {
        // DESIGN TARGET: production props may use at most 60% of a tier's draw-call budget and 45% of its triangle budget in one 1 km window, leaving the rest for terrain,
        // road, vehicles and UI. A target to design to, then to MEASURE in Unity; the numbers are upper bounds computed from the real scatter, not frame measurements.
        var stats = JsonSerializer.Deserialize<JsonElement>(StatsJson());
        foreach (var tier in stats.GetProperty("tiers").EnumerateObject())
        {
            var w = tier.Value.GetProperty("worstMoment"); var b = tier.Value.GetProperty("budget");
            int calls = w.GetProperty("drawnUpperBoundDrawCalls").GetInt32(), callBudget = b.GetProperty("drawCalls").GetInt32();
            long tris = w.GetProperty("plannedTriangles").GetInt64(), triBudget = b.GetProperty("triangles").GetInt64();
            Assert.True(calls <= callBudget * 0.6, $"{tier.Name}: drawn upper-bound draw calls {calls} exceed 60% of the {callBudget} budget");
            Assert.True(tris <= triBudget * 0.45, $"{tier.Name}: planned triangles {tris} exceed 45% of the {triBudget} budget");
        }
    }
}
