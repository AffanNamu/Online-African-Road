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
/// The Kano CITY master plan (docs/kano/city/*.json) checked as data: districts do not overlap and sit inside the study area, the road network is one connected hierarchy that reaches every district
/// from the expressway entrance, every junction type that the current road engine cannot build maps to a backlog item, landmarks agree with the verified frame, every concept reference is broken into assets
/// with valid classes and ids, the registry follows the vocabulary, and the streaming model (a distance-band LOD ladder) meets every quality tier's budget while the CURRENT engine does not.
/// The plan is engine-neutral planning data: nothing here is read by the game. Regenerate the data with `python3 tools/kano/make_city_plan.py` and the statistics with `KANO_REGEN=1 dotnet test --filter KanoCity`.
/// </summary>
public class KanoCityPlanTests
{
    static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !(Directory.Exists(Path.Combine(d.FullName, "Assets")) && Directory.Exists(Path.Combine(d.FullName, "docs")))) d = d.Parent;
        Assert.NotNull(d); return d.FullName;
    }
    static string Doc(string rel) => Path.Combine(RepoRoot(), "docs", rel.Replace('/', Path.DirectorySeparatorChar));
    static JsonElement Load(string rel) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Doc(rel)));
    static JsonElement Plan() => Load("kano/city/city_plan.json");
    static string Backlog() => File.ReadAllText(Doc("ROAD_ENGINE_BACKLOG.md"));

    static (double x, double z)[] Poly(JsonElement d) => d.GetProperty("polygon").EnumerateArray().Select(p => (p[0].GetDouble(), p[1].GetDouble())).ToArray();
    static bool Convex((double x, double z)[] p)
    {
        int sign = 0;
        for (int i = 0; i < p.Length; i++)
        {
            var a = p[i]; var b = p[(i + 1) % p.Length]; var c = p[(i + 2) % p.Length];
            double cross = (b.x - a.x) * (c.z - b.z) - (b.z - a.z) * (c.x - b.x);
            if (Math.Abs(cross) < 1e-9) continue; int s = cross > 0 ? 1 : -1; if (sign == 0) sign = s; else if (s != sign) return false;
        }
        return true;
    }
    static bool Inside((double x, double z)[] p, (double x, double z) q)
    {
        bool c = false;
        for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
            if ((p[i].z > q.z) != (p[j].z > q.z) && q.x < (p[j].x - p[i].x) * (q.z - p[i].z) / (p[j].z - p[i].z) + p[i].x) c = !c;
        return c;
    }
    static bool Separated((double x, double z)[] a, (double x, double z)[] b)     // separating axis on two convex polygons; touching edges count as separated
    {
        foreach (var poly in new[] { a, b })
            for (int i = 0; i < poly.Length; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % poly.Length]; double nx = q.z - p.z, nz = p.x - q.x;
                double aMin = double.MaxValue, aMax = double.MinValue, bMin = double.MaxValue, bMax = double.MinValue;
                foreach (var c in a) { double d = c.x * nx + c.z * nz; aMin = Math.Min(aMin, d); aMax = Math.Max(aMax, d); }
                foreach (var c in b) { double d = c.x * nx + c.z * nz; bMin = Math.Min(bMin, d); bMax = Math.Max(bMax, d); }
                if (aMax <= bMin + 1e-6 || bMax <= aMin + 1e-6) return true;
            }
        return false;
    }

    [Fact]
    public void DistrictsAreConvexInsideTheStudyAreaAndDoNotOverlap()
    {
        var plan = Plan(); var sa = plan.GetProperty("studyArea"); double x0 = sa.GetProperty("xzMin")[0].GetDouble(), z0 = sa.GetProperty("xzMin")[1].GetDouble(), x1 = sa.GetProperty("xzMax")[0].GetDouble(), z1 = sa.GetProperty("xzMax")[1].GetDouble();
        var ds = plan.GetProperty("districts").EnumerateArray().ToList(); var by = ds.ToDictionary(d => d.GetProperty("id").GetString());
        Assert.True(ds.Count >= 10);
        foreach (var d in ds)
        {
            var p = Poly(d); Assert.True(Convex(p), d.GetProperty("id").GetString() + " is not convex");
            Assert.All(p, v => Assert.True(v.x >= x0 && v.x <= x1 && v.z >= z0 && v.z <= z1, d.GetProperty("id").GetString() + " leaves the study area"));
            if (d.TryGetProperty("parent", out var par)) Assert.All(p, v => Assert.True(Inside(Poly(by[par.GetString()]), v), d.GetProperty("id").GetString() + " is not inside its parent"));
        }
        var top = ds.Where(d => !d.TryGetProperty("parent", out _)).ToList();
        for (int i = 0; i < top.Count; i++) for (int j = i + 1; j < top.Count; j++)
            Assert.True(Separated(Poly(top[i]), Poly(top[j])), top[i].GetProperty("id").GetString() + " overlaps " + top[j].GetProperty("id").GetString());
        double chunks = (x1 - x0) / 200 * (z1 - z0) / 200; Assert.Equal(sa.GetProperty("chunks").GetDouble(), chunks);        // the study area sits on the 200 m chunk lattice
        Assert.Equal(0.0, x0 % 200); Assert.Equal(0.0, z0 % 200);
    }

    static (Dictionary<string, JsonElement> nodes, List<JsonElement> edges, Dictionary<string, List<string>> adj) Graph(JsonElement plan)
    {
        var nodes = plan.GetProperty("nodes").EnumerateArray().ToDictionary(n => n.GetProperty("id").GetString()); var edges = plan.GetProperty("edges").EnumerateArray().ToList();
        var adj = nodes.Keys.ToDictionary(k => k, _ => new List<string>());
        foreach (var e in edges) { string a = e.GetProperty("a").GetString(), b = e.GetProperty("b").GetString(); Assert.True(nodes.ContainsKey(a) && nodes.ContainsKey(b), "edge " + e.GetProperty("id") + " names a missing node"); adj[a].Add(b); adj[b].Add(a); }
        return (nodes, edges, adj);
    }
    static HashSet<string> Reach(Dictionary<string, List<string>> adj, string from, Func<string, string, bool> allow = null)
    {
        var seen = new HashSet<string> { from }; var q = new Queue<string>(); q.Enqueue(from);
        while (q.Count > 0) { var n = q.Dequeue(); foreach (var m in adj[n]) if (!seen.Contains(m) && (allow == null || allow(n, m))) { seen.Add(m); q.Enqueue(m); } }
        return seen;
    }

    [Fact]
    public void TheRoadNetworkIsOneConnectedHierarchyThatReachesEveryDistrictFromTheExpresswayEntrance()
    {
        var plan = Plan(); var (nodes, edges, adj) = Graph(plan);
        Assert.Equal(nodes.Count, Reach(adj, "X_T1").Count);                                          // one connected network
        var classes = plan.GetProperty("roadClasses").EnumerateObject().Select(c => c.Name).ToHashSet();
        var seenPairs = new HashSet<string>();
        foreach (var e in edges)
        {
            Assert.Contains(e.GetProperty("cls").GetString(), classes);
            double len = e.GetProperty("lengthM").GetDouble(); Assert.InRange(len, 40.0, 3200.0);
            string key = string.Compare(e.GetProperty("a").GetString(), e.GetProperty("b").GetString(), StringComparison.Ordinal) < 0 ? e.GetProperty("a").GetString() + "|" + e.GetProperty("b").GetString() : e.GetProperty("b").GetString() + "|" + e.GetProperty("a").GetString();
            Assert.True(seenPairs.Add(key), "duplicate edge " + key);
            string cls = e.GetProperty("cls").GetString(), ta = nodes[e.GetProperty("a").GetString()].GetProperty("type").GetString(), tb = nodes[e.GetProperty("b").GetString()].GetProperty("type").GetString();
            if (cls == "expressway") Assert.All(new[] { ta, tb }, t => Assert.Contains(t, new[] { "terminal", "gateway", "T", "interchange" }));      // an expressway meets only gateways, service junctions and interchanges, never a plain crossing
            if (cls == "local" || cls == "lane") Assert.All(new[] { e.GetProperty("a").GetString(), e.GetProperty("b").GetString() }, id => Assert.StartsWith("AN_", id));   // the city graph holds no local streets except inside hero districts
        }
        // every district access node is reachable from the entrance using collectors or better only (lanes and local streets are inside districts)
        var major = Reach(adj, "X_T1", (a, b) => { var e = edges.First(x => (x.GetProperty("a").GetString() == a && x.GetProperty("b").GetString() == b) || (x.GetProperty("a").GetString() == b && x.GetProperty("b").GetString() == a)); var c = e.GetProperty("cls").GetString(); return c != "local" && c != "lane"; });
        foreach (var d in plan.GetProperty("districts").EnumerateArray())
        {
            string an = "AN_" + d.GetProperty("id").GetString();
            if (d.TryGetProperty("parent", out _)) Assert.Contains(an, Reach(adj, "X_T1"));        // children are reached through their parent by local streets and lanes
            else Assert.True(major.Contains(an), an + " is not reachable by collectors or better");
        }
        // hierarchy: every collector joins a higher class within two hops of its ends (no isolated collector islands)
        var higher = new HashSet<string>();
        foreach (var e in edges.Where(x => new[] { "arterial", "arterial_ring", "expressway" }.Contains(x.GetProperty("cls").GetString())))
        { higher.Add(e.GetProperty("a").GetString()); higher.Add(e.GetProperty("b").GetString()); }
        foreach (var e in edges.Where(x => x.GetProperty("cls").GetString() == "collector"))
        {
            var frontier = new HashSet<string> { e.GetProperty("a").GetString(), e.GetProperty("b").GetString() }; var seen = new HashSet<string>(frontier);
            for (int hop = 0; hop < 2; hop++) { var next = new HashSet<string>(); foreach (var n in frontier) foreach (var m in adj[n]) if (seen.Add(m)) next.Add(m); frontier = next; }
            Assert.True(seen.Overlaps(higher), "collector " + e.GetProperty("a").GetString() + "-" + e.GetProperty("b").GetString() + " is an island more than two hops from any arterial or expressway");
        }
        Assert.True(edges.Count(x => x.GetProperty("cls").GetString() == "arterial_ring") >= 8);
    }

    [Fact]
    public void EveryJunctionTypeTheEngineCannotBuildMapsToABacklogItem()
    {
        var plan = Plan(); var backlog = Backlog();
        foreach (var t in plan.GetProperty("nodeTypes").EnumerateObject())
            foreach (var rb in t.Value.GetProperty("rb").EnumerateArray()) Assert.Contains("| " + rb.GetString() + " |", backlog);
        foreach (var c in plan.GetProperty("roadClasses").EnumerateObject())
            foreach (var n in c.Value.GetProperty("needs").EnumerateArray().Select(x => x.GetString()).Where(x => x.StartsWith("RB-"))) Assert.Contains("| " + n.Split(' ')[0] + " |", backlog);
        var types = plan.GetProperty("nodes").EnumerateArray().Select(n => n.GetProperty("type").GetString()).ToList();
        Assert.All(types.Distinct(), t => Assert.True(plan.GetProperty("nodeTypes").TryGetProperty(t, out _), "node type " + t + " has no engine mapping"));
        Assert.True(types.Count(t => t == "roundabout") >= 4 && types.Count(t => t == "interchange") >= 1 && types.Count(t => t == "gate_cross") >= 8);     // the plan really contains what the engine cannot yet build
    }

    [Fact]
    public void LandmarksAgreeWithTheVerifiedFrameAndUnverifiedOnesSayTheyAreUnverified()
    {
        var plan = Plan(); var frame = Load("kano/kano_world_frame.json"); var ds = plan.GetProperty("districts").EnumerateArray().ToDictionary(d => d.GetProperty("id").GetString());
        var anchors = frame.GetProperty("reservedAnchors").EnumerateArray().ToDictionary(a => a.GetProperty("id").GetString());
        foreach (var l in plan.GetProperty("landmarks").EnumerateArray())
        {
            var xz = (l.GetProperty("xz")[0].GetDouble(), l.GetProperty("xz")[1].GetDouble()); string pos = l.GetProperty("position").GetString();
            Assert.True(Inside(Poly(ds[l.GetProperty("district").GetString()]), xz), l.GetProperty("id").GetString() + " is outside its district");
            if (l.GetProperty("verified").GetBoolean())
            {
                var f = anchors[l.GetProperty("id").GetString()].GetProperty("xz_m"); Assert.True(Math.Abs(f[0].GetDouble() - xz.Item1) <= 1.0 && Math.Abs(f[1].GetDouble() - xz.Item2) <= 1.0, l.GetProperty("id").GetString() + " differs from the verified frame");
                Assert.StartsWith("VERIFIED", pos);
            }
            else Assert.Contains("UNVERIFIED", pos);
        }
        Assert.Equal(2, plan.GetProperty("landmarks").EnumerateArray().Count(l => l.GetProperty("verified").GetBoolean()));      // only Gidan Rumfa and Dala Hill
    }

    [Fact]
    public void PhaseOneDistrictsNeedOnlyReferencesThatExistAndBlockedOnesAreNotScheduledEarly()
    {
        var plan = Plan(); var reg = Load("kano/reference_register.json"); var have = reg.GetProperty("references").EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToHashSet();
        var missing = reg.GetProperty("missingReferences").EnumerateArray().Select(m => m.GetProperty("id").GetString()).ToHashSet();
        foreach (var d in plan.GetProperty("districts").EnumerateArray())
        {
            var req = d.GetProperty("requires").EnumerateArray().Select(x => x.GetString()).ToList(); int phase = d.GetProperty("phase").GetInt32(); string id = d.GetProperty("id").GetString();
            Assert.All(req, r => Assert.True(have.Contains(r) || missing.Contains(r), id + " requires unknown reference " + r));
            if (phase == 1) Assert.All(req, r => Assert.StartsWith("R-0", r));                          // phase 1 depends on concepts that were supplied, never on a missing area
            if (req.Any(r => r.StartsWith("M-"))) Assert.True(phase >= 2, id + " needs a missing reference yet is scheduled in phase 1");
            if (req.Contains("M-01") || req.Contains("M-11")) Assert.True(phase >= 3, id + " is a traditional or landmark district: it cannot start before phase 3");
        }
    }

    [Fact]
    public void EveryConceptIsBrokenIntoAssetsWithValidClassesAndIdsAndTheRegistryMatches()
    {
        var m = Load("kano/city/reference_asset_matrix.json"); var reg = Load("kano/reference_register.json"); var registry = Load("kano/city/asset_registry.json"); var backlog = Backlog();
        var concepts = reg.GetProperty("references").EnumerateArray().Where(r => r.GetProperty("kind").GetString() == "generated_concept").Select(r => r.GetProperty("id").GetString()).ToList();
        Assert.Equal(10, concepts.Count);
        var refs = m.GetProperty("references").EnumerateArray().ToDictionary(r => r.GetProperty("ref").GetString());
        Assert.All(concepts, c => { Assert.True(refs.ContainsKey(c), c + " is missing from the matrix"); Assert.True(refs[c].GetProperty("assets").GetArrayLength() >= 4, c + " is broken into too few assets"); Assert.True(refs[c].GetProperty("comparisonChecklist").GetArrayLength() >= 3); });
        var prefix = new Dictionary<string, string> { ["GLOBAL"] = "aro_", ["REGIONAL"] = "rg_", ["CITY_SPECIFIC"] = "kano_", ["LANDMARK"] = "lmk_kano_" };
        var vocab = registry.GetProperty("statusVocabulary").EnumerateArray().Select(x => x.GetString()).ToList(); var ids = new HashSet<string>();
        var entries = registry.GetProperty("assets").EnumerateArray().ToList();
        Assert.Equal(m.GetProperty("counts").GetProperty("assets").GetInt32(), entries.Count);
        foreach (var e in entries)
        {
            string id = e.GetProperty("id").GetString(), cls = e.GetProperty("assetClass").GetString(); Assert.True(ids.Add(id), "duplicate asset id " + id);
            Assert.True(prefix.ContainsKey(cls), id + " has an unknown class"); Assert.StartsWith(prefix[cls], id);
            Assert.Matches("^[a-z0-9]+(_[a-z0-9]+)*$", id);
            if (cls == "REGIONAL") Assert.StartsWith("rg_ngn_", id);
            if (cls == "CITY_SPECIFIC" || cls == "LANDMARK") Assert.Equal("kano", e.GetProperty("city").GetString());
            string st = e.GetProperty("status").GetString(); Assert.Contains(st, vocab);
            Assert.Contains(st, new[] { "needs_concept", "concept_approved" });                     // nothing is further along than its concept: no Blender source exists yet
            Assert.True(e.GetProperty("blenderSource").ValueKind == JsonValueKind.Null && e.GetProperty("export").ValueKind == JsonValueKind.Null && e.GetProperty("unityPrefab").ValueKind == JsonValueKind.Null);
            Assert.False(e.GetProperty("breakdownApproved").GetBoolean());
            foreach (var c in e.GetProperty("concepts").EnumerateArray()) Assert.Contains(c.GetString(), concepts);
            foreach (var rb in e.GetProperty("engineDependencies").EnumerateArray()) Assert.Contains("| " + rb.GetString() + " |", backlog);
            if (st == "needs_concept") { Assert.True(e.TryGetProperty("blockedBy", out _)); Assert.Equal(0, e.GetProperty("concepts").GetArrayLength()); } else Assert.True(e.GetProperty("concepts").GetArrayLength() > 0);
            if (e.TryGetProperty("composedOf", out var co)) foreach (var c in co.EnumerateArray()) Assert.Contains(c.GetString(), registry.GetProperty("assets").EnumerateArray().Select(x => x.GetProperty("id").GetString()));
        }
        // the shared library is real: GLOBAL assets are used by more than one concept, and no landmark is reused as a generic asset
        Assert.True(entries.Count(e => e.GetProperty("assetClass").GetString() == "GLOBAL" && e.GetProperty("concepts").GetArrayLength() > 1) >= 15);
        Assert.All(entries.Where(e => e.GetProperty("assetClass").GetString() == "LANDMARK"), e => Assert.Equal("needs_concept", e.GetProperty("status").GetString()));
    }

    // ------------------------------------------------------------------------------------------------- streaming model and generated statistics
    static string Num(double v) => Math.Round(v, 1).ToString("0.#", CultureInfo.InvariantCulture);
    static (double tris, double draws, double[] plots) Band(JsonElement st, double scale, double density)
    {
        var edges = st.GetProperty("bandEdgesM").EnumerateArray().Select(x => x.GetDouble()).ToArray(); var caps = st.GetProperty("capsTrisPerPlot").EnumerateArray().Select(x => x.GetDouble()).ToArray();
        var indiv = st.GetProperty("individualBands").EnumerateArray().Select(x => x.GetBoolean()).ToArray(); var a = st.GetProperty("assumptions");
        double F = a.GetProperty("frustumFraction").GetDouble(), atl = a.GetProperty("mergedAtlasesPerChunk").GetDouble(), chunkHa = a.GetProperty("chunkHa").GetDouble(); double tri = 0, dr = 0; var plots = new double[5];
        for (int i = 0; i < 5; i++)
        {
            double r0 = edges[i] * scale, r1 = edges[i + 1] * scale, ha = Math.PI * (r1 * r1 - r0 * r0) / 10000.0, n = ha * density * F; plots[i] = n; tri += n * caps[i];
            dr += indiv[i] ? n : ha / chunkHa * atl * F;
        }
        return (tri, dr, plots);
    }
    static IEnumerable<(string id, double density)> DensityRows(JsonElement plan) =>
        plan.GetProperty("districts").EnumerateArray().Where(d => !d.TryGetProperty("parent", out _)).Select(d => (d.GetProperty("id").GetString(), d.GetProperty("densityPlotsPerHa").GetDouble()))
            .Concat(plan.GetProperty("fillerZones").EnumerateArray().Select(f => (f.GetProperty("id").GetString(), f.GetProperty("densityPlotsPerHa").GetDouble())));

    [Fact]
    public void TheStreamingLadderMeetsEveryTiersBudgetForEveryDistrictWhileTheCurrentEngineDoesNot()
    {
        var plan = Plan(); var st = plan.GetProperty("streaming"); var tg = st.GetProperty("targets"); double triShare = tg.GetProperty("triangleShare").GetDouble(), drawShare = tg.GetProperty("drawShare").GetDouble();
        var tiers = new[] { ("low", PerfBudget.Mobile), ("medium", PerfBudget.Desktop), ("high", PerfBudget.Desktop) }; double F = st.GetProperty("assumptions").GetProperty("frustumFraction").GetDouble();
        foreach (var (tn, budget) in tiers)
        {
            double scale = st.GetProperty("tierRadiusScale").GetProperty(tn).GetDouble();
            foreach (var (id, density) in DensityRows(plan))
            {
                var (tri, dr, _) = Band(st, scale, density);
                Assert.True(tri <= budget.MaxTriangles * triShare, $"{id} on {tn}: {tri:0} triangles exceed {triShare:P0} of {budget.MaxTriangles}");
                Assert.True(dr <= budget.MaxDrawCalls * drawShare, $"{id} on {tn}: {dr:0} draw calls exceed {drawShare:P0} of {budget.MaxDrawCalls}");
            }
        }
        // the CURRENT engine: production prefabs exist only in a 5 x 5 chunk square (100 ha), one draw call per plot in view, no merging
        double window = Math.Pow((2 * st.GetProperty("currentEngine").GetProperty("productionRing").GetInt32() + 1) * st.GetProperty("currentEngine").GetProperty("chunkM").GetDouble() / 100.0, 2);
        var dense = DensityRows(plan).Where(r => r.density >= 12.0).ToList(); Assert.True(dense.Count >= 4);
        Assert.All(dense, r => Assert.True(window * r.density * F > PerfBudget.Desktop.MaxDrawCalls * drawShare, r.id + ": the current engine would NOT exceed the draw-call target, so the plan's case for city-density streaming (RB-15) is not proven"));
    }

    static string StatsJson()
    {
        var plan = Plan(); var st = plan.GetProperty("streaming"); var sb = new StringBuilder("{\n\"note\":\"GENERATED by KanoCityPlanTests. Planning numbers from the plan's own densities (ASSUMPTIONS, to be replaced by reference measurement) and the game's own quality-tier budgets. Upper bounds: no occlusion credit. Not measurements of any model.\",\n");
        var nodes = plan.GetProperty("nodes").EnumerateArray().ToList(); var edges = plan.GetProperty("edges").EnumerateArray().ToList();
        sb.Append("\"network\":{\"nodes\":" + nodes.Count + ",\"edges\":" + edges.Count + ",\"nodeTypes\":{" + string.Join(",", nodes.GroupBy(n => n.GetProperty("type").GetString()).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => "\"" + g.Key + "\":" + g.Count())) + "},");
        sb.Append("\"kmByClass\":{" + string.Join(",", edges.GroupBy(e => e.GetProperty("cls").GetString()).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => "\"" + g.Key + "\":" + Num(g.Sum(e => e.GetProperty("lengthM").GetDouble()) / 1000.0))) + "},");
        var unsupported = new[] { "cross4", "roundabout", "interchange", "gate_cross" }; sb.Append("\"junctionsTheEngineCannotBuild\":" + nodes.Count(n => unsupported.Contains(n.GetProperty("type").GetString())) + "},\n");
        var ds = plan.GetProperty("districts").EnumerateArray().Where(d => !d.TryGetProperty("parent", out _)).ToList();
        sb.Append("\"districts\":[" + string.Join(",", plan.GetProperty("districts").EnumerateArray().Select(d => "{\"id\":\"" + d.GetProperty("id").GetString() + "\",\"areaHa\":" + Num(d.GetProperty("areaHa").GetDouble()) + ",\"netAreaHa\":" + Num(d.GetProperty("netAreaHa").GetDouble()) + ",\"plots\":" + d.GetProperty("plots").GetInt32() + ",\"phase\":" + d.GetProperty("phase").GetInt32() + "}")) + "],\n");
        var tiers = new[] { ("low", PerfBudget.Mobile), ("medium", PerfBudget.Desktop), ("high", PerfBudget.Desktop) }; var rows = new List<string>();
        foreach (var (tn, budget) in tiers)
        {
            double scale = st.GetProperty("tierRadiusScale").GetProperty(tn).GetDouble(); var per = new List<string>();
            foreach (var (id, density) in DensityRows(plan)) { var (tri, dr, plots) = Band(st, scale, density); per.Add("{\"id\":\"" + id + "\",\"density\":" + Num(density) + ",\"plotsInView\":[" + string.Join(",", plots.Select(Num)) + "],\"triangles\":" + Math.Round(tri) + ",\"drawCalls\":" + Num(dr) + "}"); }
            rows.Add("\"" + tn + "\":{\"radiusScale\":" + Num(scale) + ",\"budget\":{\"triangles\":" + budget.MaxTriangles + ",\"drawCalls\":" + budget.MaxDrawCalls + "},\"rows\":[" + string.Join(",", per) + "]}");
        }
        sb.Append("\"streamingLadder\":{" + string.Join(",", rows) + "},\n");
        double F = st.GetProperty("assumptions").GetProperty("frustumFraction").GetDouble(); double window = Math.Pow((2 * st.GetProperty("currentEngine").GetProperty("productionRing").GetInt32() + 1) * st.GetProperty("currentEngine").GetProperty("chunkM").GetDouble() / 100.0, 2);
        sb.Append("\"currentEngineDrawCallsInView\":{" + string.Join(",", DensityRows(plan).Select(r => "\"" + r.id + "\":" + Num(window * r.density * F))) + "},\n");
        var matrix = Load("kano/city/reference_asset_matrix.json").GetProperty("counts"); sb.Append("\"matrix\":{\"assets\":" + matrix.GetProperty("assets").GetInt32() + ",\"conceptBacked\":" + matrix.GetProperty("conceptBacked").GetInt32() + ",\"blocked\":" + matrix.GetProperty("blocked").GetInt32() + ",\"byClass\":{" +
                  string.Join(",", matrix.GetProperty("byClass").EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => "\"" + p.Name + "\":" + p.Value.GetInt32())) + "}}\n}\n");
        return sb.ToString();
    }

    [Fact]
    public void TheCityStatisticsAreTheOutputOfTheTestedPlan()
    {
        var path = Doc("kano/city/city_stats.json"); var text = StatsJson();
        if (Environment.GetEnvironmentVariable("KANO_REGEN") == "1") { File.WriteAllText(path, text, new UTF8Encoding(false)); return; }
        Assert.True(File.Exists(path), "city_stats.json is missing: regenerate with KANO_REGEN=1 dotnet test --filter KanoCity");
        Assert.True(File.ReadAllText(path) == text, "city_stats.json is out of date with the plan: regenerate with KANO_REGEN=1 dotnet test --filter KanoCity");
    }
}
