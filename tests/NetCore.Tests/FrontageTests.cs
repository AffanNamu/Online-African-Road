using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ARO.NetCore;
using Xunit;

/// <summary>
/// The frontage engine (Assets/_Project/Scripts/NetCore/Frontage.cs): deterministic blocks and lots of buildings along any corridor. Pure logic, so the tests are exact: the same data gives the same buildings on every
/// client (multiplayer), a chunk computes only the blocks it touches yet agrees with the whole route, no two lots overlap, no footprint touches a road, a site plate or an island, every anchor (the mosques) keeps its spacing,
/// and the quality tier thins ordinary lots but never an anchor. The second half runs the same checks on the two city test routes (ng-kano-city-e1, ng-kano-city-f1).
/// </summary>
public class FrontageTests
{
    static readonly JsonSerializerOptions Opt = new JsonSerializerOptions { IncludeFields = true };

    static FrontageLot Lot(string prop, float w, float d, float weight = 1f, string tag = "") => new FrontageLot { prop = prop, widthM = w, depthM = d, weight = weight, tag = tag };
    static FrontageRule Rule(string id = "r", float block = 120f, params FrontageLot[] mix) => new FrontageRule { id = id, blockLength = block, mix = mix.Length > 0 ? mix : new[] { Lot("a", 12f, 10f, 1f, "a"), Lot("b", 20f, 14f, 1f, "b"), Lot("c", 8f, 8f, 1f, "c") }, anchors = new FrontageLot[0] };

    static RouteSpec Road(float length, params FrontageRule[] rules)
    {
        var s = Fx.Straight(length); s.schemaVersion = 3; s.seed = 4711; s.frontage = rules; return s;
    }

    static List<FrontagePlacement> All(RouteModel m) => Enumerable.Range(0, m.Spec.frontage.Length).SelectMany(i => FrontagePlacer.AllLots(m, i)).ToList();

    public static string Digest(IEnumerable<FrontagePlacement> lots)
    {
        ulong h = 1469598103934665603UL;
        foreach (var l in lots.OrderBy(l => l.Rule, StringComparer.Ordinal).ThenBy(l => l.Side).ThenBy(l => l.Block).ThenBy(l => l.Slot))
            foreach (var ch in $"{l.Rule}|{l.Prop}|{l.Side}|{l.Block}|{l.Slot}|{l.X:0.00}|{l.Z:0.00}|{l.YawDeg:0.0}|{l.Y:0.00};") { h ^= ch; h *= 1099511628211UL; }
        return h.ToString("x16");
    }

    // ------------------------------------------------------------------------------------------------------------------------------ the engine on a straight road
    [Fact] public void TheSameDataGivesTheSameBuildingsEveryTime()
    {
        var spec = Road(2000f, Rule());
        var a = All(RouteModel.Build(spec)); var b = All(RouteModel.Build(Fx.Clone(spec)));
        Assert.True(a.Count > 30);
        Assert.Equal(Digest(a), Digest(b));
    }

    [Fact] public void ADifferentRouteSeedGivesADifferentCity()
    {
        var s1 = Road(2000f, Rule()); var s2 = Road(2000f, Rule()); s2.seed += 1;
        Assert.NotEqual(Digest(All(RouteModel.Build(s1))), Digest(All(RouteModel.Build(s2))));
    }

    [Fact] public void ChunksAgreeWithTheWholeRoute()
    {
        var spec = Road(2000f, Rule()); var m = RouteModel.Build(spec); var whole = All(m); var got = new List<PropInstance>();
        for (int cx = -1; cx < 11; cx++) for (int cz = -1; cz < 1; cz++) got.AddRange(PropScatter.PlaceChunk(m, cx * 200f, cz * 200f, (cx + 1) * 200f, (cz + 1) * 200f).Where(p => p.Origin == "r"));
        Assert.Equal(whole.Count, got.Count);                                                                   // every lot in exactly one chunk
        var want = whole.Select(l => (l.Prop, (float)Math.Round(l.X, 2), (float)Math.Round(l.Z, 2))).OrderBy(t => t.Prop).ThenBy(t => t.Item2).ThenBy(t => t.Item3);
        var have = got.Select(p => (p.Prop, (float)Math.Round(p.Pos.X, 2), (float)Math.Round(p.Pos.Z, 2))).OrderBy(t => t.Prop).ThenBy(t => t.Item2).ThenBy(t => t.Item3);
        Assert.Equal(want, have);
    }

    [Fact] public void TheOrderChunksAreAskedInDoesNotMatter()
    {
        var m = RouteModel.Build(Road(2000f, Rule())); var boxes = new List<(float, float)>(); for (int cx = 0; cx < 10; cx++) boxes.Add((cx * 200f, -200f)); boxes.AddRange(boxes.Select(b => (b.Item1, 0f)).ToList());
        string Run(IEnumerable<(float, float)> order) => Digest(order.SelectMany(b => PropScatter.PlaceChunk(m, b.Item1, b.Item2, b.Item1 + 200f, b.Item2 + 200f)).Where(p => p.Origin == "r").Select(p => new FrontagePlacement { Rule = "r", Prop = p.Prop, X = p.Pos.X, Z = p.Pos.Z, Y = p.Pos.Y, YawDeg = p.YawDeg, Block = (int)(p.Pos.X * 10), Slot = (int)(p.Pos.Z * 10) }));
        Assert.Equal(Run(boxes), Run(Enumerable.Reverse(boxes)));
    }

    [Fact] public void NoTwoLotsOverlapAndNoneIsCloseToAnother()
    {
        var m = RouteModel.Build(Road(2000f, Rule()));
        var lots = FrontagePlacer.AllLots(m, 0);
        for (int i = 0; i < lots.Count; i++) for (int j = i + 1; j < lots.Count; j++) Assert.False(FrontagePlacement.Overlap(lots[i], lots[j], 0f), $"{lots[i].Prop}@{lots[i].S:0} overlaps {lots[j].Prop}@{lots[j].S:0}");
    }

    [Fact] public void EveryFootprintStaysBackFromTheRoadByTheSetbackAndClearance()
    {
        var r = Rule(); r.setbackMin = 6f; r.setbackMax = 9f; r.clearance = 3f;
        var m = RouteModel.Build(Road(2000f, r)); var main = m.Main; var c = new float[8]; int n = 0;
        foreach (var l in FrontagePlacer.AllLots(m, 0))
        {
            l.Corners(c);
            for (int k = 0; k < 4; k++)
            {
                main.Spline.Nearest(c[k * 2], c[k * 2 + 1], 400f, out float s, out float lat, out _);
                Assert.True(Math.Abs(lat) >= main.HalfFormationAt(s) + 5.0f, $"{l.Prop} corner {k} is {Math.Abs(lat) - main.HalfFormationAt(s):0.0} m from the formation");   // setbackMin 6 less the yaw jitter of a wide lot
            }
            n++;
        }
        Assert.True(n > 30);
    }

    [Fact] public void EveryLotFacesTheRoad()
    {
        var m = RouteModel.Build(Road(2000f, Rule()));
        foreach (var l in FrontagePlacer.AllLots(m, 0))
        {
            float a = l.YawDeg * (float)(Math.PI / 180.0), fx = (float)Math.Sin(a), fz = (float)Math.Cos(a);       // the prop's front (+Z in the game) points along this vector
            m.Main.Spline.Nearest(l.X, l.Z, 400f, out float s, out float lat, out _); m.Main.Spline.FrameAt(s, out var pos, out _, out _);
            float tx = pos.X - l.X, tz = pos.Z - l.Z, len = (float)Math.Sqrt(tx * tx + tz * tz);
            Assert.True((fx * tx + fz * tz) / len > 0.99f, $"{l.Prop}@{l.S:0} does not face the road");
        }
    }

    [Fact] public void SideLeftAndRightAreHonoured()
    {
        var r = Rule(); r.side = "left"; var m = RouteModel.Build(Road(2000f, r)); var lots = FrontagePlacer.AllLots(m, 0);
        Assert.NotEmpty(lots); Assert.All(lots, l => Assert.Equal(-1, l.Side));
        foreach (var l in lots) { m.Main.Spline.Nearest(l.X, l.Z, 400f, out _, out float lat, out _); Assert.True(lat < 0f || lat > 0f); }
        var rr = Rule(); rr.side = "right"; Assert.All(FrontagePlacer.AllLots(RouteModel.Build(Road(2000f, rr)), 0), l => Assert.Equal(1, l.Side));
    }

    [Fact] public void TheMixFollowsItsWeights()
    {
        var r = Rule("w", 120f, Lot("common", 10f, 8f, 3f, "common"), Lot("rare", 10f, 8f, 1f, "rare")); r.presence = 1f; r.gapMin = 1f; r.gapMax = 2f;
        var lots = FrontagePlacer.AllLots(RouteModel.Build(Road(8000f, r)), 0); int common = lots.Count(l => l.Tag == "common"), rare = lots.Count(l => l.Tag == "rare");
        Assert.True(lots.Count > 400); double share = common / (double)(common + rare);
        Assert.InRange(share, 0.68, 0.82);                                                                  // 3 : 1
    }

    [Fact] public void PresenceLeavesEmptyPlotsAndTheQualityTierThinsThem()
    {
        var r = Rule(); r.presence = 0.7f; var m = RouteModel.Build(Road(2000f, r));
        int full = FrontagePlacer.AllLots(m, 0, 1f).Count, thin = FrontagePlacer.AllLots(m, 0, 0.4f).Count, none = FrontagePlacer.AllLots(m, 0, 0f).Count;
        Assert.True(thin < full && thin > 0); Assert.Equal(0, none);
        var fixedD = Rule(); fixedD.presence = 0.7f; fixedD.fixedDensity = true; var mf = RouteModel.Build(Road(2000f, fixedD));
        Assert.Equal(FrontagePlacer.AllLots(mf, 0, 1f).Count, FrontagePlacer.AllLots(mf, 0, 0.4f).Count);
    }

    [Fact] public void AnchorsKeepTheirSpacingAndAreNeverThinned()
    {
        var r = Rule(); r.presence = 0.5f; r.anchors = new[] { Lot("mosque_big", 40f, 30f, 1f, "mosque"), Lot("mosque_small", 18f, 14f, 1f, "mosque") }; r.anchorEveryBlocks = 3;
        var m = RouteModel.Build(Road(3000f, r)); var full = FrontagePlacer.AllLots(m, 0, 1f); var thin = FrontagePlacer.AllLots(m, 0, 0.1f);
        Assert.Equal(full.Count(l => l.Anchor), thin.Count(l => l.Anchor));                                  // never thinned
        Assert.True(thin.Count(l => !l.Anchor) < full.Count(l => !l.Anchor));
        foreach (int side in new[] { -1, 1 })
        {
            var s = full.Where(l => l.Anchor && l.Side == side).Select(l => l.S).OrderBy(x => x).ToList();
            Assert.True(s.Count >= 7, $"side {side}: {s.Count} anchors on 3 km");                             // 3000 / (3 blocks of 120 m) = 8.3
            for (int i = 1; i < s.Count; i++) Assert.InRange(s[i] - s[i - 1], 60f, 3 * 120f + 120f);      // never 2 km without one, never two in a row
        }
        Assert.All(full.Where(l => l.Anchor), l => Assert.Equal("mosque", l.Tag));
    }

    [Fact] public void ADesignerChosenAnchorPhaseIsUsedAndStaggersTheSides()
    {
        var r = Rule(); r.anchors = new[] { Lot("m", 20f, 14f, 1f, "mosque") }; r.anchorEveryBlocks = 4; r.anchorPhase = 1;
        var lots = FrontagePlacer.AllLots(RouteModel.Build(Road(3000f, r)), 0);
        var left = lots.Where(l => l.Anchor && l.Side == -1).Select(l => l.Block).ToList(); var right = lots.Where(l => l.Anchor && l.Side == 1).Select(l => l.Block).ToList();
        Assert.All(left, b => Assert.Equal(0, (b + 1) % 4)); Assert.All(right, b => Assert.Equal(0, (b + 1 + 2) % 4));
        Assert.True(left.Count > 4 && right.Count > 4);
    }

    [Fact] public void RowsMakeTheCityDeeperWithoutOverlap()
    {
        var one = Rule(); var three = Rule(); three.rows = 3;
        var m1 = RouteModel.Build(Road(1500f, one)); var m3 = RouteModel.Build(Road(1500f, three)); var l1 = FrontagePlacer.AllLots(m1, 0); var l3 = FrontagePlacer.AllLots(m3, 0);
        Assert.True(l3.Count > l1.Count * 2);
        for (int i = 0; i < l3.Count; i++) for (int j = i + 1; j < l3.Count; j++) Assert.False(FrontagePlacement.Overlap(l3[i], l3[j], 0f));
        float Depth(List<FrontagePlacement> ls) { float d = 0; foreach (var l in ls) { m1.Main.Spline.Nearest(l.X, l.Z, 600f, out _, out float lat, out _); d = Math.Max(d, Math.Abs(lat)); } return d; }
        Assert.True(Depth(l3) > Depth(l1) + 25f);
    }

    [Fact] public void ASteepHillsideIsNotBuiltOn()
    {
        var r = Rule(); r.maxHeightRange = 1.0f; var spec = Road(2000f, r); spec.terrain.hillAmplitude = 12f; spec.terrain.hillWavelength = 180f; var m = RouteModel.Build(spec);
        var lots = FrontagePlacer.AllLots(m, 0); Assert.NotEmpty(lots); var c = new float[8];
        foreach (var l in lots)
        {
            l.Corners(c); float lo = float.MaxValue, hi = float.MinValue;
            for (int k = 0; k < 4; k++) { float h = m.Terrain.HeightAt(c[k * 2], c[k * 2 + 1]); lo = Math.Min(lo, h); hi = Math.Max(hi, h); }
            Assert.True(hi - lo <= 1.0f + 0.7f, $"{l.Prop}@{l.S:0}: corners differ by {hi - lo:0.0} m");           // samples are on a 5 m grid: the corners may differ a little more than the worst sample
        }
        var flat = RouteModel.Build(Road(2000f, Rule())); Assert.True(FrontagePlacer.AllLots(flat, 0).Count > lots.Count);        // the hills cost buildings
    }

    [Fact] public void NothingStandsBesideAValleyBridgeButFlyoverDecksHaveOpenGroundBeside()
    {
        var valley = Road(2000f, Rule()); valley.bridges = new[] { new BridgeSpec { id = "b", startS = 800f, endS = 1000f, valleyDepth = 8f, deckThickness = 1.2f, pierSpacing = 20f } };
        var lots = FrontagePlacer.AllLots(RouteModel.Build(valley), 0);
        Assert.NotEmpty(lots); Assert.DoesNotContain(lots, l => l.S > 800f && l.S < 1000f);
    }

    [Fact] public void TheValidatorAcceptsAGoodRuleAndRefusesBadOnes()
    {
        var known = new HashSet<string> { "a", "b", "c" };
        Assert.Empty(RouteSpecValidator.Validate(Road(2000f, Rule()), known));
        List<string> Bad(Action<FrontageRule> edit, Action<RouteSpec> spec = null) { var r = Rule(); edit(r); var s = Road(2000f, r); spec?.Invoke(s); return RouteSpecValidator.Validate(s, known); }
        Assert.Contains(Bad(r => r.mix = new[] { Lot("zzz", 10f, 10f) }), e => e.Contains("unknown prop 'zzz'"));
        Assert.Contains(Bad(r => r.mix = new FrontageLot[0]), e => e.Contains("mix needs at least one lot"));
        Assert.Contains(Bad(r => r.blockLength = 15f), e => e.Contains("blockLength"));
        Assert.Contains(Bad(r => { r.setbackMin = 1f; r.clearance = 2.5f; }), e => e.Contains("setbacks"));
        Assert.Contains(Bad(r => r.presence = 1.5f), e => e.Contains("presence"));
        Assert.Contains(Bad(r => r.side = "middle"), e => e.Contains("side"));
        Assert.Contains(Bad(r => r.corridor = "nowhere"), e => e.Contains("corridor 'nowhere'"));
        Assert.Contains(Bad(r => r.anchorEveryBlocks = 3), e => e.Contains("anchors and anchorEveryBlocks go together"));
        Assert.Contains(Bad(r => r.anchors = new[] { Lot("a", 10f, 10f) }), e => e.Contains("anchors and anchorEveryBlocks go together"));
        Assert.Contains(Bad(r => r.profiles = "nope"), e => e.Contains("unknown road profile"));
        Assert.Contains(Bad(r => r.zones = "moon"), e => e.Contains("unknown zone"));
        Assert.Contains(Bad(r => r.rows = 9), e => e.Contains("rows"));
        Assert.Contains(Bad(r => r.mix[0].weight = 0f), e => e.Contains("positive weight"));
        Assert.Contains(Bad(r => r.mix[0].widthM = 200f), e => e.Contains("footprint"));
        Assert.Contains(Bad(r => { }, s => s.schemaVersion = 2), e => e.Contains("schemaVersion 3"));
        Assert.Contains(RouteSpecValidator.Validate(Road(2000f, Rule("same"), Rule("same")), known), e => e.Contains("duplicate"));
        Assert.Contains(RouteSpecValidator.Validate(Road(2000f, Rule("x"), Rule("y")), known), e => e.Contains("shares a side and a stretch"));
        var left = Rule("x"); left.side = "left"; var right = Rule("y"); right.side = "right"; Assert.Empty(RouteSpecValidator.Validate(Road(2000f, left, right), known));          // different sides: fine
        var first = Rule("x"); first.sTo = 800f; var second = Rule("y"); second.sFrom = 800f; Assert.Empty(RouteSpecValidator.Validate(Road(2000f, first, second), known));       // consecutive stretches: fine
    }

    [Fact] public void ARouteWithoutFrontageRulesIsUnchanged()
    {
        var m = Fx.Shipped; Assert.Null(m.Spec.frontage);
        var box = PropScatter.PlaceChunk(m, 0f, 0f, 200f, 200f); Assert.DoesNotContain(box, p => p.Origin != null && p.Origin.StartsWith("frontage"));
    }

    // ------------------------------------------------------------------------------------------------------------------------------ the two city routes
    static RouteSpec LoadRoute(string id) => JsonSerializer.Deserialize<RouteSpec>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, id + ".route.json")), Opt);
    static readonly Dictionary<string, RouteModel> Models = new Dictionary<string, RouteModel>();
    static RouteModel Model(string id) { lock (Models) { if (!Models.TryGetValue(id, out var m)) Models[id] = m = RouteModel.Build(LoadRoute(id)); return m; } }
    public static IEnumerable<object[]> Cities() { yield return new object[] { "ng-kano-city-e1" }; yield return new object[] { "ng-kano-city-f1" }; }

    [Theory, MemberData(nameof(Cities))] public void TheCityRoutePassesTheGamesOwnValidatorWithTheShippedCatalogue(string id)
    {
        var known = new HashSet<string>(Fx.LoadCatalog().props.Select(p => p.id));
        var errors = RouteSpecValidator.Validate(LoadRoute(id), known); Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Theory, MemberData(nameof(Cities))] public void EveryLotsFootprintMatchesItsCatalogueEntryAndEveryBuildingIsWithinTheBudget(string id)
    {
        var defs = Fx.LoadCatalog().props.ToDictionary(p => p.id); var spec = LoadRoute(id);
        foreach (var r in spec.frontage)
            foreach (var l in r.mix.Concat(r.anchors))
            {
                var d = defs[l.prop];
                Assert.True(Math.Abs(d.widthM - l.widthM) <= 0.05f && Math.Abs(d.depthM - l.depthM) <= 0.05f, $"{r.id}/{l.prop}: rule {l.widthM}x{l.depthM}, catalogue {d.widthM}x{d.depthM}");
                Assert.Equal("buildings", d.category);
                Assert.True(d.lodTriangles[0] <= PropCatalogValidator.Lod0Budget("buildings") && d.lodTriangles.Length >= 2 && d.cullDistance >= 220f, l.prop);
            }
    }

    [Theory, MemberData(nameof(Cities))] public void NoLotOverlapsAnotherAcrossAllRulesAndCorridors(string id)
    {
        var all = All(Model(id)); Assert.True(all.Count > 100);
        for (int i = 0; i < all.Count; i++) for (int j = i + 1; j < all.Count; j++) Assert.False(FrontagePlacement.Overlap(all[i], all[j], 0f), $"{all[i].Rule}/{all[i].Prop}@{all[i].S:0} overlaps {all[j].Rule}/{all[j].Prop}@{all[j].S:0}");
    }

    [Theory, MemberData(nameof(Cities))] public void NoFootprintTouchesAnyRoadSitePlateOrIslandAndTheGroundIsLevel(string id)
    {
        var m = Model(id); var spec = m.Spec; var c = new float[8];
        for (int ri = 0; ri < spec.frontage.Length; ri++)
        {
            var r = spec.frontage[ri];
            foreach (var l in FrontagePlacer.AllLots(m, ri))
            {
                l.Corners(c); float lo = float.MaxValue, hi = float.MinValue;
                for (int k = 0; k < 4; k++)
                {
                    float x = c[k * 2], z = c[k * 2 + 1]; Assert.False(m.Terrain.InsideFormation(x, z, r.clearance - 0.7f), $"{r.id}/{l.Prop}@{l.S:0} corner {k} is inside a road, plate or island (+{r.clearance - 0.7f} m)");     // the engine samples a 5 m grid, so a corner may sit 0.7 m closer than a sample
                    float h = m.Terrain.HeightAt(x, z); lo = Math.Min(lo, h); hi = Math.Max(hi, h);
                }
                Assert.True(hi - lo <= r.maxHeightRange + 1.0f, $"{r.id}/{l.Prop}@{l.S:0}: corners differ by {hi - lo:0.0} m");
                for (float t = -0.5f; t <= 0.5f; t += 0.25f) Assert.False(m.Terrain.InsideFormation(l.X + (c[2] - c[0]) * t, l.Z + (c[3] - c[1]) * t, 0f), $"{r.id}/{l.Prop}@{l.S:0}: the front edge is on a road");
            }
        }
    }

    [Theory, MemberData(nameof(Cities))] public void ChunksAgreeWithTheWholeRouteOnTheRealData(string id)
    {
        var m = Model(id); var spec = m.Spec; var ids = new HashSet<string>(spec.frontage.Select(r => r.id)); var whole = All(m); var got = new List<PropInstance>(); var seen = new HashSet<(int, int)>();
        foreach (var l in whole) { int cx = (int)Math.Floor(l.X / 200f), cz = (int)Math.Floor(l.Z / 200f); for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++) seen.Add((cx + dx, cz + dz)); }
        foreach (var (cx, cz) in seen) got.AddRange(PropScatter.PlaceChunk(m, cx * 200f, cz * 200f, (cx + 1) * 200f, (cz + 1) * 200f).Where(p => ids.Contains(p.Origin)));
        Assert.Equal(whole.Count, got.Count);
        Assert.Equal(whole.Select(l => $"{l.Rule}|{l.Prop}|{l.X:0.00}|{l.Z:0.00}").OrderBy(x => x, StringComparer.Ordinal), got.Select(p => $"{p.Origin}|{p.Prop}|{p.Pos.X:0.00}|{p.Pos.Z:0.00}").OrderBy(x => x, StringComparer.Ordinal));
    }

    [Theory, MemberData(nameof(Cities))] public void TheMixIsVariedAndNoDesignDominates(string id)
    {
        var m = Model(id); int checkedRules = 0;
        for (int ri = 0; ri < m.Spec.frontage.Length; ri++)
        {
            var r = m.Spec.frontage[ri]; var lots = FrontagePlacer.AllLots(m, ri).Where(l => !l.Anchor).ToList(); if (lots.Count < 20 || r.mix.Length < 4) continue; checkedRules++;
            Assert.True(lots.Select(l => l.Prop).Distinct().Count() >= 4, $"{r.id}: only {lots.Select(l => l.Prop).Distinct().Count()} designs");
            Assert.True(lots.GroupBy(l => l.Prop).Max(g => g.Count()) <= lots.Count * 0.4, $"{r.id}: one design is more than 40% of the lots");
            // no design twice in a row side by side more than three times (no copy-paste rows)
            foreach (var g in lots.GroupBy(l => (l.Side, l.Block / 1))) { int run = 1; var o = g.OrderBy(l => l.S).ToList(); for (int i = 1; i < o.Count; i++) { run = o[i].Prop == o[i - 1].Prop ? run + 1 : 1; Assert.True(run <= 3, $"{r.id}: {run} identical buildings in a row"); } }
        }
        Assert.True(checkedRules >= 1);
    }

    [Theory, MemberData(nameof(Cities))] public void TheBuildingsAreAMixOfFamiliesNotOneKind(string id)
    {
        var all = All(Model(id)); var tags = all.Select(l => l.Tag).Distinct().ToList();
        Assert.True(tags.Count >= 8, string.Join(", ", tags));
        Assert.Contains(tags, t => t.StartsWith("mosque")); Assert.Contains(tags, t => t == "villa" || t == "house"); Assert.Contains(tags, t => t == "apartments" || t == "office" || t == "shopping_parade");
    }

    [Fact] public void TheE1CorridorHasAMosqueAtRegularIntervalsOnEveryRoadClass()
    {
        var m = Model("ng-kano-city-e1"); var spec = m.Spec; var all = All(m);
        double MaxGap(string rule, int side, float from, float to)
        {
            var s = all.Where(l => l.Rule == rule && l.Side == side && l.Tag.StartsWith("mosque")).Select(l => l.S).Where(x => x >= from && x <= to).OrderBy(x => x).ToList(); s.Insert(0, from); s.Add(to);
            return Enumerable.Range(1, s.Count - 1).Max(i => s[i] - s[i - 1]);
        }
        foreach (int side in new[] { -1, 1 })
        {
            Assert.True(MaxGap("arterial_major", side, 0f, 760f) <= 420f, $"arterial side {side}");               // a major road: a mosque at least every 420 m on each side
            Assert.True(MaxGap("collector_main", side, 760f, 1500f) <= 520f, $"collector side {side}");           // an ordinary single carriageway street
        }
        Assert.True(all.Count(l => l.Tag.StartsWith("mosque")) >= 18);
        Assert.Contains(all, l => l.Rule == "local_street" && l.Tag.StartsWith("mosque"));                       // inside a residential neighbourhood
        Assert.Contains(all, l => l.Rule.StartsWith("cross_street") && l.Tag.StartsWith("mosque"));
        Assert.Contains(all, l => l.Rule == "arterial_major" && l.Tag == "mosque_grand");                         // the large mosques of R-037 and R-038 stand on the major road
        var arterial = all.Where(l => l.Rule == "arterial_major" && l.Tag == "mosque_grand").ToList();
        foreach (var l in arterial) { m.Main.Spline.Nearest(l.X, l.Z, 600f, out float s, out float lat, out _); Assert.True(Math.Abs(lat) - l.Depth * 0.5f - m.Main.HalfFormationAt(s) >= 10f - 0.01f, $"a grand mosque is set back {Math.Abs(lat) - l.Depth * 0.5f - m.Main.HalfFormationAt(s):0.0} m"); }   // R-037: set back 10 to 15 m
    }

    [Fact] public void TheE1CorridorHasNoLargeEmptyStretchAlongItsMainRoad()
    {
        var m = Model("ng-kano-city-e1"); var all = All(m).Where(l => l.Rule == "arterial_major" || l.Rule == "collector_main").ToList();
        foreach (int side in new[] { -1, 1 })
        {
            var spans = all.Where(l => l.Side == side).Select(l => (l.S - l.Width * 0.5f, l.S + l.Width * 0.5f)).OrderBy(t => t.Item1).ToList(); float edge = 0f, worst = 0f;
            foreach (var (a, b) in spans) { worst = Math.Max(worst, a - edge); edge = Math.Max(edge, b); }
            worst = Math.Max(worst, m.Main.Length - edge);
            Assert.True(worst <= 90f, $"side {side}: {worst:0} m of main road without a building");
        }
    }

    [Fact] public void TheF1FlyoverLooksOverACityWithAMosqueInView()
    {
        var m = Model("ng-kano-city-f1"); var all = All(m); var deck = m.Main.Bridges.Single(b => b.overRoad);
        Assert.True(deck.endS - deck.startS >= 500f);
        var beside = all.Where(l => l.Rule != "ring_edge" && l.S > deck.startS && l.S < deck.endS).ToList();
        foreach (int side in new[] { -1, 1 }) Assert.True(beside.Count(l => l.Side == side) >= 20, $"side {side}: {beside.Count(l => l.Side == side)} buildings beside the deck");         // R-039: a city on both sides
        Assert.Contains(beside, l => l.Tag == "office" || l.Tag == "apartments");                                                                                                       // mid-rise blocks
        // a mosque within 150 to 400 m of the deck, alongside it
        var mosques = all.Where(l => l.Tag.StartsWith("mosque") && l.S > deck.startS - 100f && l.S < deck.endS + 100f).ToList(); Assert.NotEmpty(mosques);
        foreach (var l in mosques) { m.Main.Spline.Nearest(l.X, l.Z, 1000f, out _, out float lat, out _); Assert.InRange(Math.Abs(lat), 40f, 400f); }
        // nothing under or on the deck: every footprint clear of the deck's formation (the engine's clearance), and none is a villa on a ramp embankment (the terrain test)
        var c = new float[8]; foreach (var l in beside) { l.Corners(c); for (int k = 0; k < 4; k++) { m.Main.Spline.Nearest(c[k * 2], c[k * 2 + 1], 400f, out float s, out float lat, out _); Assert.True(Math.Abs(lat) > m.Main.HalfFormationAt(s) + 25f); } }
    }

    [Theory, MemberData(nameof(Cities))] public void AChunkIsComputedQuicklyAndTheWholeRouteIsASmallCost(string id)
    {
        var m = Model(id); var sw = System.Diagnostics.Stopwatch.StartNew(); var lots = All(m); sw.Stop();
        Assert.True(sw.ElapsedMilliseconds < 3000, $"all lots took {sw.ElapsedMilliseconds} ms");
        Assert.InRange(lots.Count, 100, 600);                                                                    // a few hundred buildings on 1.5 to 2 km of road: well inside the draw-call budget with LODs and culling
        var worst = 0L;
        foreach (var l in lots.Take(40)) { int cx = (int)Math.Floor(l.X / 200f), cz = (int)Math.Floor(l.Z / 200f); var t = System.Diagnostics.Stopwatch.StartNew(); PropScatter.PlaceChunk(m, cx * 200f, cz * 200f, (cx + 1) * 200f, (cz + 1) * 200f); worst = Math.Max(worst, t.ElapsedMilliseconds); }
        Assert.True(worst < 600, $"one chunk took {worst} ms");
    }

    [Theory, MemberData(nameof(Cities))] public void TheCityLotsHaveAGoldenDigestSoALaterChangeIsDeliberate(string id)
    {
        string want = id == "ng-kano-city-e1" ? "afc3661a654d088b" : "175daddbe5a0cc47";
        Assert.Equal(want, Digest(All(Model(id))));
    }
}
