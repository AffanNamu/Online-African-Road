using System;
using System.Collections.Generic;
using System.Linq;
using ARO.NetCore;
using Xunit;

public class NightLightingTests
{
    static float Factor(float hour, float overcast = 0f) => NightLighting.LampFactor(LightingModel.Evaluate(hour, overcast));

    [Fact] public void LampsAreOffAtNoonAndOnAtNight()
    {
        Assert.Equal(0f, Factor(LightingHour.Noon), 4); Assert.Equal(0f, Factor(10f), 4); Assert.Equal(0f, Factor(15f), 4);
        Assert.Equal(1f, Factor(LightingHour.Night), 4); Assert.Equal(1f, Factor(0f), 4); Assert.Equal(1f, Factor(3f), 4);
    }

    [Fact] public void TheRampIsInTheEveningAndTheMirrorImageAtDawn()
    {
        Assert.True(Factor(17.0f) < 0.05f, "lamps should still be off an hour before sunset");
        Assert.InRange(Factor(LightingHour.Sunset), 0.05f, 0.6f);       // starting to glow at the sunset preset
        Assert.True(Factor(18.5f) > 0.99f, "fully on by 18:30");
        Assert.Equal(Factor(LightingHour.Sunset), Factor(LightingHour.Dawn), 3);     // dawn and sunset presets are symmetric about noon
        Assert.Equal(Factor(17.9f), Factor(6.1f), 3);
        Assert.True(Factor(6.7f) < 0.02f, "off shortly after sunrise");
    }

    [Fact] public void TheFactorIsAlwaysInRangeAndSmoothInTheHour()
    {
        float prev = Factor(0f);
        for (int minute = 1; minute <= 24 * 60; minute++)
            foreach (float oc in new[] { 0f, 0.5f, 1f })
            {
                float f = Factor(minute / 60f, oc); Assert.InRange(f, 0f, 1f);
                if (oc == 0f) { Assert.True(Math.Abs(f - prev) < 0.04f, $"lamp factor jumps by {Math.Abs(f - prev)} at minute {minute}"); prev = f; }
            }
    }

    [Fact] public void CloudLightsTheStreetEarlierNeverLater()
    {
        for (float h = 0f; h < 24f; h += 0.05f)
        {
            Assert.True(Factor(h, 1f) >= Factor(h, 0f) - 1e-4f, $"overcast made the lamps dimmer at {h}");
        }
        Assert.True(Factor(17.2f, 1f) > Factor(17.2f, 0f) + 0.05f, "a dark rainy late afternoon should have the lamps on sooner");
    }

    [Fact] public void BadInputsAreHarmless()
    {
        Assert.Equal(0f, NightLighting.LampFactor(float.NaN, 0f)); Assert.Equal(0f, NightLighting.LampFactor((LightingState)null));
        Assert.InRange(NightLighting.LampFactor(-0.5f, float.NaN), 0f, 1f); Assert.Equal(1f, NightLighting.LampFactor(-1f, 5f)); Assert.Equal(0f, NightLighting.LampFactor(1f, -3f));
    }

    [Fact] public void AutomaticSwitchingHasAHysteresisBand()
    {
        Assert.False(NightLighting.AutoLampsOn(false, 0.49f)); Assert.True(NightLighting.AutoLampsOn(false, 0.5f));
        Assert.True(NightLighting.AutoLampsOn(true, 0.40f)); Assert.False(NightLighting.AutoLampsOn(true, 0.35f));
        // wandering inside the band changes nothing, in either state
        Assert.False(NightLighting.AutoLampsOn(false, 0.42f)); Assert.True(NightLighting.AutoLampsOn(true, 0.42f));
    }

    [Fact] public void TheBeaconFlashesAndNeverGoesFullyDark()
    {
        var levels = Enumerable.Range(0, 400).Select(i => NightLighting.BeaconLevel(i * 0.01f)).ToList();
        Assert.Contains(levels, l => l >= 0.99f); Assert.Contains(levels, l => l > 0f && l < 0.1f);
        Assert.All(levels, l => Assert.InRange(l, 0.01f, 1f));
        Assert.Equal(NightLighting.BeaconLevel(0.05f), NightLighting.BeaconLevel(2.05f), 4);      // period 2 s
        Assert.NotEqual(NightLighting.BeaconLevel(0.05f, 0f), NightLighting.BeaconLevel(0.05f, 0.2f));     // masts can be out of step
        Assert.InRange(NightLighting.BeaconLevel(-3.3f), 0f, 1f);
    }

    [Theory]
    [InlineData("http://x/?smoke=drive&lamps=off", NightLighting.Mode.Off)] [InlineData("http://x/?lamps=ON", NightLighting.Mode.On)]
    [InlineData("http://x/?lamps=auto", NightLighting.Mode.Auto)] [InlineData("http://x/?lamps=banana", NightLighting.Mode.Auto)] [InlineData("http://x/", NightLighting.Mode.Auto)] [InlineData(null, NightLighting.Mode.Auto)]
    public void TheTestPageModeIsParsed(string url, NightLighting.Mode expected) => Assert.Equal(expected, NightLighting.ModeFromUrl(url));

    [Fact] public void TheModeOverridesTheClockOnlyWhenAsked()
    {
        Assert.Equal(0.3f, NightLighting.Apply(NightLighting.Mode.Auto, 0.3f)); Assert.Equal(1f, NightLighting.Apply(NightLighting.Mode.On, 0.3f)); Assert.Equal(0f, NightLighting.Apply(NightLighting.Mode.Off, 0.9f));
    }
}

public class LightSpecCatalogTests
{
    static List<string> Errors(Action<PropCatalogSpec> mutate) { var c = Fx.Clone(Fx.LoadCatalog()); mutate(c); return PropCatalogValidator.Validate(c); }
    static void Rejects(Action<PropCatalogSpec> mutate, string expect) { var e = Errors(mutate); Assert.True(e.Any(x => x.Contains(expect, StringComparison.OrdinalIgnoreCase)), $"expected '{expect}', got:\n" + string.Join("\n", e)); }
    static PropDef Def(PropCatalogSpec c, string id) => c.props.First(p => p.id == id);

    [Fact] public void TheShippedCatalogWithLightsIsValid() { var e = PropCatalogValidator.Validate(Fx.LoadCatalog()); Assert.True(e.Count == 0, string.Join("\n", e)); }

    [Fact] public void EveryLuminaireAndEveryLitBuildingHasAtLeastOneHead()
    {
        var cat = Fx.LoadCatalog();
        foreach (var id in new[] { "street_light", "street_light_village", "highway_light", "junction_mast", "concrete_house", "apartment_block", "shop_row", "kiosk", "market_stall", "fuel_station", "bus_stop", "billboard", "warehouse", "telecom_mast" })
            Assert.True(Def(cat, id).HasLight, id + " has no light");
        foreach (var p in cat.props.Where(p => p.category == "lights" && p.id != "utility_pole")) Assert.True(p.HasLight, p.id + " is a luminaire without a light spec");
        Assert.False(Def(cat, "palm_tree").HasLight);
    }

    [Fact] public void BadLightSpecsAreRejected()
    {
        Rejects(c => Def(c, "street_light").light.kind = "laser", "kind");
        Rejects(c => Def(c, "street_light").light.heads[0].y = 99f, "head height");
        Rejects(c => Def(c, "street_light").light.heads[0].x = 50f, "20 m");
        Rejects(c => Def(c, "street_light").light.r = 2f, "colour");
        Rejects(c => Def(c, "street_light").light.intensity = 20f, "intensity");
        Rejects(c => Def(c, "street_light").light.range = 1f, "range");
        Rejects(c => Def(c, "street_light").light.haloSize = 30f, "halo");
        Rejects(c => Def(c, "street_light").light.poolRadius = 80f, "pool");
        Rejects(c => Def(c, "telecom_mast").light.poolRadius = 3f, "beacon");
        Rejects(c => Def(c, "telecom_mast").light.realLight = true, "beacon");
        Rejects(c => Def(c, "street_light").light.heads = Enumerable.Range(0, 9).Select(i => new LampHead { y = 1f }).ToArray(), "8 heads");
    }

    [Fact] public void ALightWithNoHeadsIsNotALight()
    {
        var c = Fx.Clone(Fx.LoadCatalog()); Def(c, "street_light").light.heads = new LampHead[0]; Assert.False(Def(c, "street_light").HasLight);
        Def(c, "street_light").light = null; Assert.False(Def(c, "street_light").HasLight);
        Assert.Empty(PropCatalogValidator.Validate(c));
    }

    [Fact] public void EveryLitPropHasEmissiveGeometryUnderEachOfItsHeads()
    {
        // A head in the catalog must sit on the lit geometry the dev model really has (within a lamp's size), at the finest LOD; otherwise a halo would float where nothing glows.
        foreach (var def in Fx.LoadCatalog().props.Where(p => p.HasLight))
        {
            var mb = DevPropGeometry.Build(def, 0); var subs = def.light.kind == "beacon" ? new[] { PropSub.Beacon } : def.light.kind == "lamp" ? new[] { PropSub.Lamp } : new[] { PropSub.Glow, PropSub.Lamp };   // a building's own lights are glow or a lamp head
            var verts = subs.SelectMany(sub => mb.Triangles[sub]).Distinct().Select(i => mb.Vertices[i]).ToList();
            Assert.True(verts.Count > 0, def.id + " has a light but no vertices in its emissive sub-mesh");
            float tol = def.light.kind == "glow" ? 1.4f : 0.6f;
            foreach (var h in def.light.heads)
            {
                float best = verts.Min(v => (float)Math.Sqrt((v.X - h.x) * (v.X - h.x) + (v.Y - h.y) * (v.Y - h.y) + (v.Z - h.z) * (v.Z - h.z)));
                Assert.True(best <= tol, $"{def.id}: head ({h.x}, {h.y}, {h.z}) is {best:0.00} m from the nearest lit vertex (limit {tol})");
            }
        }
    }

    [Fact] public void OnlyEmissiveSubMaterialsAreFlaggedEmissive()
    {
        for (int s = 0; s < PropSub.Count; s++) Assert.Equal(s >= PropSub.Lamp, PropSub.IsEmissive(s));
        Assert.Equal(11, PropSub.Count);
    }

    [Fact] public void PropsWithoutALightGainNoEmissiveGeometry()
    {
        foreach (var def in Fx.LoadCatalog().props.Where(p => !p.HasLight))
        {
            var mb = DevPropGeometry.Build(def, 0);
            for (int s = PropSub.Lamp; s < PropSub.Count; s++) Assert.True(mb.Triangles[s].Count == 0, def.id + " has emissive geometry but no light spec");
        }
    }
}

public class PropScatterLightingTests
{
    static List<PropInstance> All(RouteModel m, float density = 1f)
    {
        var all = new List<PropInstance>(); var c = m.Main;
        for (float x = (float)Math.Floor(c.RingX.Min() / 200f) * 200f - 200f; x < c.RingX.Max() + 200f; x += 200f)
            for (float z = (float)Math.Floor(c.RingZ.Min() / 200f) * 200f - 200f; z < c.RingZ.Max() + 200f; z += 200f) all.AddRange(PropScatter.PlaceChunk(m, x, z, x + 200f, z + 200f, density));
        return all;
    }
    static float StationOf(RouteModel m, PropInstance p) { m.Main.Spline.Nearest(p.Pos.X, p.Pos.Z, 300f, out float s, out _, out _); return s; }
    static float LateralOf(RouteModel m, PropInstance p) { m.Main.Spline.Nearest(p.Pos.X, p.Pos.Z, 300f, out _, out float lat, out _); return lat; }

    static RouteSpec Divided(params PropRule[] rules)
    {
        var undivided = Fx.Profile("plain", 2); var divided = Fx.Profile("divided", 2, 3.5f, 4f);
        var spec = Fx.Straight(2000f, undivided, divided);
        spec.controlPoints = new[]
        {
            new ControlPoint { id = "a", name = "A", x = 0, z = 0, elevation = 10, zone = "urban", surface = "good", profile = "plain" },
            new ControlPoint { id = "b", name = "B", x = 1000, z = 0, elevation = 10, zone = "urban", surface = "good", profile = "divided" },
            new ControlPoint { id = "c", name = "C", x = 2000, z = 0, elevation = 10, zone = "urban", surface = "good", profile = "divided" },
        };
        spec.propRules = rules; return spec;
    }
    static PropRule R(string side, string profiles = null, bool fixedDensity = false) => new PropRule { id = "r", prop = "street_light", zones = "", profiles = profiles, side = side, spacingMin = 40, spacingMax = 40, offsetMin = 1f, offsetMax = 1f, presence = 1f, yawJitterDeg = 0, fixedDensity = fixedDensity, exclusionMargin = 0.5f };

    [Fact] public void ARuleWithoutTheNewFieldsBehavesAsBefore()
    {
        var m = RouteModel.Build(Divided(R("both")));
        var inst = All(m); Assert.NotEmpty(inst);
        Assert.Contains(inst, p => LateralOf(m, p) > 0); Assert.Contains(inst, p => LateralOf(m, p) < 0);
        Assert.Contains(inst, p => StationOf(m, p) < 900f); Assert.Contains(inst, p => StationOf(m, p) > 1100f);   // both profiles
    }

    [Fact] public void AProfileFilterKeepsARuleOnItsProfiles()
    {
        var m = RouteModel.Build(Divided(R("both", "divided")));
        var inst = All(m); Assert.NotEmpty(inst);
        Assert.All(inst, p => Assert.True(StationOf(m, p) >= 990f, "a rule for the divided profile placed a prop on the plain road at s=" + StationOf(m, p)));
    }

    [Fact] public void TheMedianSideStandsOnTheCentreLineOfADividedRoadOnly()
    {
        var m = RouteModel.Build(Divided(R("median")));
        var inst = All(m); Assert.NotEmpty(inst);
        foreach (var p in inst)
        {
            float s = StationOf(m, p); Assert.True(s >= 990f, "a median light on the undivided road at s=" + s);
            Assert.InRange(LateralOf(m, p), -0.05f, 0.05f);
            Assert.Equal(m.Main.SurfaceY(s, 0f), p.Pos.Y, 2);                 // standing on the median, not on the terrain 0.35 m below it
            Assert.True(m.Terrain.InsideFormation(p.Pos.X, p.Pos.Z), "the median is inside the formation");
        }
    }

    [Fact] public void TheMedianLightIsTurnedAlongTheRoad()
    {
        var m = RouteModel.Build(Divided(R("median")));    // the straight road runs east: heading 90 degrees
        foreach (var p in All(m)) Assert.InRange(((p.YawDeg % 360f) + 360f) % 360f, 89f, 91f);
    }

    [Fact] public void FixedDensityIgnoresTheTierDensityAndOrdinaryRulesDoNot()
    {
        var thinned = RouteModel.Build(Divided(R("both"))); var kept = RouteModel.Build(Divided(R("both", null, true)));
        Assert.True(All(thinned, 0.5f).Count < All(thinned, 1f).Count * 0.7f);
        Assert.Equal(All(kept, 1f).Count, All(kept, 0.3f).Count);
        Assert.Empty(All(kept, 0f));       // a density of 0 still removes everything
    }

    [Fact] public void TheExclusionMarginIsPerRuleAndDefaultsToTheEngineValue()
    {
        var near = RouteModel.Build(Divided(R("both"))); var far = RouteModel.Build(Divided(new PropRule { id = "r", prop = "street_light", zones = "", side = "both", spacingMin = 40, spacingMax = 40, offsetMin = 1f, offsetMax = 1f, presence = 1f, yawJitterDeg = 0 }));
        Assert.NotEmpty(All(near)); Assert.Empty(All(far));        // an offset of 1 m is inside the 3 m default clearance, so the default rule drops it
        var bad = Divided(R("both")); bad.propRules[0].exclusionMargin = -2f; Assert.Contains(RouteSpecValidator.Validate(bad, null), e => e.Contains("exclusionMargin"));
    }

    [Fact] public void AMedianRuleNeedsADividedProfileInTheRoute()
    {
        var spec = Fx.Straight(2000f); spec.propRules = new[] { R("median") };
        Assert.Contains(RouteSpecValidator.Validate(spec, null), e => e.Contains("median", StringComparison.OrdinalIgnoreCase));
        var ok = Divided(R("median", "divided")); Assert.Empty(RouteSpecValidator.Validate(ok, null));
        var wrong = Divided(R("median", "plain")); Assert.Contains(RouteSpecValidator.Validate(wrong, null), e => e.Contains("no median"));
        var unknown = Divided(R("both", "nope")); Assert.Contains(RouteSpecValidator.Validate(unknown, null), e => e.Contains("unknown road profile"));
    }

    [Fact] public void ASetPieceOnTheRoadStandsOnTheSurface()
    {
        var spec = Divided(); spec.props = new[] { new PropPlacementSpec { id = "on_median", prop = "street_light", s = 1500f, lateral = 0f, scale = 1f }, new PropPlacementSpec { id = "beside", prop = "street_light", s = 1500f, lateral = 30f, scale = 1f } };
        var m = RouteModel.Build(spec); var all = PropScatter.PlaceChunk(m, 1400f, -100f, 1600f, 100f);
        var on = all.Single(p => p.Origin == "on_median"); var beside = all.Single(p => p.Origin == "beside");
        Assert.Equal(m.Main.SurfaceY(1500f, 0f), on.Pos.Y, 2); Assert.Equal(m.Terrain.HeightAt(beside.Pos.X, beside.Pos.Z), beside.Pos.Y, 3);
    }
}

public class LightingLayoutTests
{
    static readonly RouteModel M = Fx.Shipped;
    static readonly Lazy<List<PropInstance>> Everything = new Lazy<List<PropInstance>>(() =>
    {
        var all = new List<PropInstance>(); var c = M.Main;
        for (float x = (float)Math.Floor(c.RingX.Min() / 200f) * 200f - 200f; x < c.RingX.Max() + 200f; x += 200f)
            for (float z = (float)Math.Floor(c.RingZ.Min() / 200f) * 200f - 200f; z < c.RingZ.Max() + 200f; z += 200f) all.AddRange(PropScatter.PlaceChunk(M, x, z, x + 200f, z + 200f, 1f));
        return all;
    });
    static List<PropInstance> Placed(params string[] rules) => Everything.Value.Where(p => rules.Contains(p.Origin)).ToList();
    static float S(PropInstance p) { M.Main.Spline.Nearest(p.Pos.X, p.Pos.Z, 300f, out float s, out _, out _); return s; }
    static double LampsPerKm(string rule, Func<float, bool> stretch) => LampsPerKm(new[] { rule }, stretch);
    static double LampsPerKm(string[] rules, Func<float, bool> stretch)
    {
        double metres = 0; for (float s = 0; s < M.Main.Length; s += 10f) if (stretch(s)) metres += 10;
        return metres < 1 ? 0 : Placed(rules).Count(p => stretch(S(p))) / (metres / 1000.0);
    }
    static readonly string[] Village = { "lights_village_a", "lights_village_b" }, OpenExpressway = { "lights_median_expressway_open_a", "lights_median_expressway_open_b" };
    static bool InVillageWindow(float s) => (s >= 33000f && s <= 34700f) || (s >= 35800f && s <= 36900f);
    static bool InOpenWindow(float s) => (s >= 19400f && s <= 21700f) || (s >= 27300f && s <= 29300f);
    static Func<float, bool> Profile(params string[] ids) => s => ids.Contains(M.Main.SectionAt(s).Spec.id);

    [Fact] public void TheOldOneSizeFitsAllRuleIsGone()
    {
        Assert.DoesNotContain(Fx.LoadRoute().propRules, r => r.id == "urban_lights");
        foreach (var id in new[] { "lights_village_a", "lights_village_b", "lights_residential", "lights_commercial", "lights_industrial", "lights_median_city", "lights_median_expressway", "lights_median_expressway_open_a", "lights_median_expressway_open_b" })
            Assert.Contains(Fx.LoadRoute().propRules, r => r.id == id);
    }

    [Fact] public void EveryEnvironmentGetsItsOwnFixtureAndTheyAreNotAllTheSame()
    {
        var rules = Fx.LoadRoute().propRules.Where(r => r.id.StartsWith("lights_")).ToList();
        Assert.True(rules.Select(r => r.prop).Distinct().Count() >= 3, "a village lane, a city street and an expressway should not share one fixture");
        Assert.Equal("street_light_village", rules.Single(r => r.id == "lights_village_a").prop);
        Assert.Equal("highway_light", rules.Single(r => r.id == "lights_median_expressway").prop);
        Assert.Equal("street_light", rules.Single(r => r.id == "lights_residential").prop);
    }

    [Fact] public void LightingDensityDependsOnTheEnvironment()
    {
        // lamps per kilometre of the matching road, one side counted: a village lane is the sparsest, a commercial street the densest of the side-mounted layouts
        double village = LampsPerKm(Village, InVillageWindow), residential = LampsPerKm("lights_residential", s => M.Main.ZoneAt(s) == Zone.Urban && Profile("urban_4lane", "commercial_2lane")(s));
        double commercial = LampsPerKm("lights_commercial", s => M.Main.ZoneAt(s) == Zone.Commercial), industrial = LampsPerKm("lights_industrial", s => M.Main.ZoneAt(s) == Zone.Industrial && Profile("urban_4lane")(s));
        Assert.True(village > 0, "no village lights"); Assert.True(residential > 0 && commercial > 0 && industrial > 0, "a city layout has no lamps");
        Assert.True(village < industrial, $"village {village:0.0}/km should be sparser than an industrial road {industrial:0.0}/km");
        Assert.True(industrial < residential, $"industrial {industrial:0.0}/km should be sparser than residential {residential:0.0}/km");
        Assert.True(residential < commercial, $"residential {residential:0.0}/km should be sparser than a commercial street {commercial:0.0}/km");
        Assert.InRange(village, 5, 25); Assert.InRange(commercial, 45, 80);
    }

    [Fact] public void ExpresswaysAndDividedRoadsAreLitFromTheMedianAndOnlyThere()
    {
        foreach (var id in new[] { "lights_median_city", "lights_median_expressway", "lights_median_expressway_open_a", "lights_median_expressway_open_b" })
        {
            var placed = Placed(id); Assert.NotEmpty(placed);
            foreach (var p in placed) { float s = S(p); Assert.True(M.Main.SectionAt(s).HasMedian, id + " placed on an undivided road at s=" + s); M.Main.Spline.Nearest(p.Pos.X, p.Pos.Z, 300f, out _, out float lat, out _); Assert.InRange(lat, -0.05f, 0.05f); }
        }
        // no divided stretch is left without a median light, and no undivided stretch has one
        for (float s = 300f; s < M.Main.Length - 300f; s += 500f)
        {
            bool divided = M.Main.SectionAt(s).HasMedian; if (M.Main.SectionAt(s - 250f).HasMedian != divided || M.Main.SectionAt(s + 250f).HasMedian != divided) continue;     /* not near a change of profile */ bool near = Fx.LoadRoute().propRules.Where(r => r.side == "median").SelectMany(r => Placed(r.id)).Any(p => Math.Abs(S(p) - s) < 200f);
            if (!divided) Assert.False(near, "a median light within 200 m of the undivided road at s=" + s);
            else if (M.Main.ZoneAt(s) != Zone.Rural) Assert.True(near, "a divided road in town has no median light at s=" + s);
        }
    }

    [Fact] public void TheExpresswayIsDenserInTownAndSparserInOpenCountry()
    {
        double town = LampsPerKm("lights_median_expressway", s => M.Main.SectionAt(s).HasMedian && M.Main.ZoneAt(s) == Zone.Highway), open = LampsPerKm(OpenExpressway, InOpenWindow);
        Assert.True(town > open * 1.2, $"expressway in town {town:0.0}/km vs open country {open:0.0}/km");
    }

    [Fact] public void RuralStretchesAreLimitedOrUnlitNotAllTheSame()
    {
        // regional variation: the open expressway and the old road are dark between their settlements
        var all = Fx.LoadRoute().propRules.Where(r => r.id.StartsWith("lights_")).SelectMany(r => Placed(r.id)).ToList();
        double dark = 0, total = 0;
        for (float s = 16500f; s < 37500f; s += 50f) { total++; if (!all.Any(p => Math.Abs(S(p) - s) < 250f)) dark++; }
        Assert.True(dark / total > 0.45, $"only {dark / total:P0} of the open country is unlit: every road would look the same");
        Assert.True(dark / total < 0.95, "the open country has no lighting at all");
        // the lit settlements really are lit: a lamp within 300 m of each window centre
        foreach (float centre in new[] { 20500f, 28300f, 33800f, 36300f }) Assert.Contains(all, p => Math.Abs(S(p) - centre) < 800f);
        // and a city street is lit everywhere along its length (no 300 m gap in the commercial zone)
        for (float s = 3100f; s < 4800f; s += 100f) Assert.Contains(Placed("lights_commercial"), p => Math.Abs(S(p) - s) < 120f);
    }

    [Fact] public void EveryJunctionAndBridgeEndHasItsOwnLight()
    {
        var spec = Fx.LoadRoute();
        foreach (var j in spec.junctions) Assert.Contains(spec.props, p => p.prop == "junction_mast" && Math.Abs(p.s - j.s) < 5f);
        foreach (var b in spec.bridges) { Assert.Contains(spec.props, p => p.prop == "highway_light" && p.s < b.startS && b.startS - p.s < 30f); Assert.Contains(spec.props, p => p.prop == "highway_light" && p.s > b.endS && p.s - b.endS < 30f); }
        // a junction mast stands on the side OPPOSITE the side road (so the turning headlights light it)
        foreach (var j in spec.junctions) { var mast = spec.props.Single(p => p.prop == "junction_mast" && Math.Abs(p.s - j.s) < 5f); Assert.True(Math.Sign(mast.lateral) == -Math.Sign(j.angleDeg), j.id + ": the mast is on the same side as the side road"); }
    }

    [Fact] public void StreetLightsKeepTheirSpacingOnEveryTier()
    {
        foreach (var r in Fx.LoadRoute().propRules.Where(r => r.id.StartsWith("lights_"))) Assert.True(r.fixedDensity, r.id + " would be thinned on the Low tier");
    }

    [Fact] public void NothingIsLitInsideTheRoadOrOnTheWrongProfile()
    {
        foreach (var r in Fx.LoadRoute().propRules.Where(r => r.id.StartsWith("lights_") && r.side != "median"))
            foreach (var p in Placed(r.id))
            {
                Assert.False(M.Terrain.InsideFormation(p.Pos.X, p.Pos.Z, (r.exclusionMargin >= 0f ? r.exclusionMargin : PropScatter.ExclusionMargin) - 0.01f), r.id + " on the road");
                var profiles = (r.profiles ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries); if (profiles.Length > 0) Assert.Contains(M.Main.SectionAt(S(p)).Spec.id, profiles);
            }
    }
}

public class LampFieldTests
{
    static PropDef Lit(float x, float y, float z, string kind = "lamp", float scale = 1f) => new PropDef
    {
        id = "t", heightM = 9, widthM = 3, depthM = 1, lodTriangles = new[] { 10 },
        light = new LightSpec { kind = kind, r = 1f, g = 0.5f, b = 0.25f, intensity = 2f, range = 20f, haloSize = 2f, poolRadius = 5f, poolShift = 1.5f, realLight = true, heads = new[] { new LampHead { x = x, y = y, z = z } } }
    };

    [Fact] public void AHeadIsPlacedByTheInstancePositionYawAndScale()
    {
        var def = Lit(0f, 8f, 1.5f); var inst = new PropInstance { Prop = "t", Pos = new V3(100f, 5f, 200f), YawDeg = 90f, Scale = 2f };
        var l = LampField.FromInstances(null, new[] { inst }, id => def).Single();
        // yaw 90 turns local +z (1.5 m, doubled to 3 m) to world +x
        Assert.Equal(103f, l.Pos.X, 3); Assert.Equal(21f, l.Pos.Y, 3); Assert.Equal(200f, l.Pos.Z, 3);
        Assert.Equal(40f, l.Range, 3); Assert.Equal(4f, l.Halo, 3); Assert.Equal(10f, l.PoolRadius, 3); Assert.Equal(3f, l.PoolShift, 3);
        Assert.Equal(1f, l.OutX, 3); Assert.Equal(0f, l.OutZ, 3);          // the head points away from the pole along world +x
        Assert.True(l.Real); Assert.Equal(LampKind.Lamp, l.Kind);
    }

    [Fact] public void OnlyLitPropsBecomeLampsAndGlowOrBeaconNeverTakeARealLight()
    {
        var plain = new PropDef { id = "p", heightM = 1, widthM = 1, depthM = 1, lodTriangles = new[] { 10 } };
        var inst = new[] { new PropInstance { Prop = "p", Pos = new V3(0, 0, 0), Scale = 1f }, new PropInstance { Prop = "g", Pos = new V3(5, 0, 0), Scale = 1f }, new PropInstance { Prop = "b", Pos = new V3(9, 0, 0), Scale = 1f }, new PropInstance { Prop = "missing", Pos = new V3(9, 0, 0), Scale = 1f } };
        var defs = new Dictionary<string, PropDef> { ["p"] = plain, ["g"] = Lit(0, 3, 0, "glow"), ["b"] = Lit(0, 3, 0, "beacon") };
        var lamps = LampField.FromInstances(null, inst, id => defs.TryGetValue(id, out var d) ? d : null);
        Assert.Equal(2, lamps.Count); Assert.All(lamps, l => Assert.False(l.Real));
        Assert.Equal(new[] { LampKind.Glow, LampKind.Beacon }, lamps.Select(l => l.Kind).ToArray());
    }

    [Fact] public void ALampOnTheShippedRouteKnowsTheRoadDirectionAndFacesTheRoad()
    {
        var m = Fx.Shipped; var cat = Fx.LoadCatalog(); var defs = cat.props.ToDictionary(p => p.id);
        var all = new List<PropInstance>(); var c = m.Main;
        for (float x = (float)Math.Floor(c.RingX.Min() / 200f) * 200f - 200f; x < c.RingX.Max() + 200f; x += 200f)
            for (float z = (float)Math.Floor(c.RingZ.Min() / 200f) * 200f - 200f; z < c.RingZ.Max() + 200f; z += 200f) all.AddRange(PropScatter.PlaceChunk(m, x, z, x + 200f, z + 200f, 1f));
        var lamps = LampField.FromInstances(m, all, id => defs.TryGetValue(id, out var d) ? d : null);
        Assert.True(lamps.Count > 500, "the route should have many lamps, has " + lamps.Count);
        Assert.Equal(lamps.Count, lamps.Select(l => l.Key).Distinct().Count());                 // keys identify lamps
        foreach (var l in lamps) { Assert.True(l.Pos.IsFinite); Assert.Equal(1f, l.DirX * l.DirX + l.DirZ * l.DirZ, 2); Assert.Equal(1f, l.OutX * l.OutX + l.OutZ * l.OutZ, 2); }
        // a side-mounted street light's arm points to the road: the pool shift moves the pool towards the centre line
        var side = all.Where(p => p.Origin == "lights_residential").Take(30).ToList(); Assert.NotEmpty(side);
        var sideLamps = LampField.FromInstances(m, side, id => defs[id]);
        foreach (var l in sideLamps)
        {
            m.Main.Spline.Nearest(l.Pos.X, l.Pos.Z, 200f, out _, out float lat0, out _); m.Main.Spline.Nearest(l.Pos.X + l.OutX * 2f, l.Pos.Z + l.OutZ * 2f, 200f, out _, out float lat1, out _);
            Assert.True(Math.Abs(lat1) < Math.Abs(lat0), "the pool would be shifted away from the road");
        }
    }

    [Fact] public void KeysAreStableForTheSamePositionAndDifferForDifferentOnes()
    {
        Assert.Equal(LampField.KeyOf(new V3(10.01f, 5f, 3f)), LampField.KeyOf(new V3(10.02f, 5f, 3f)));
        Assert.NotEqual(LampField.KeyOf(new V3(10f, 5f, 3f)), LampField.KeyOf(new V3(10.3f, 5f, 3f)));
        Assert.NotEqual(LampField.KeyOf(new V3(10f, 5f, 3f)), LampField.KeyOf(new V3(3f, 5f, 10f)));
    }

    [Fact] public void TheGroundUnderALampIsTheRoadSurfaceOnTheRoadAndTheTerrainBeside()
    {
        var m = Fx.Shipped; m.Main.Spline.FrameAt(1500f, out var pos, out _, out var right);
        float onRoad = LampField.SurfaceHeight(m, pos.X + right.X * 3f, pos.Z + right.Z * 3f);
        Assert.Equal(m.Main.SurfaceY(1500f, 3f), onRoad, 1);
        Assert.True(onRoad > m.Terrain.HeightAt(pos.X + right.X * 3f, pos.Z + right.Z * 3f) + 0.2f, "the pool must lie on the road, not on the terrain 0.35 m below it");
        float bx = pos.X + right.X * 40f, bz = pos.Z + right.Z * 40f; Assert.Equal(m.Terrain.HeightAt(bx, bz), LampField.SurfaceHeight(m, bx, bz), 3);
    }
}

public class LampSelectorTests
{
    static Lamp L(float x, float z, bool real = true) => new Lamp { Key = LampField.KeyOf(new V3(x, 9f, z)), Pos = new V3(x, 9f, z), Real = real, Kind = LampKind.Lamp };
    static List<Lamp> Row(int n, float spacing, float x0 = 0f) => Enumerable.Range(0, n).Select(i => L(x0 + i * spacing, 5f)).ToList();

    [Fact] public void TheBudgetIsNeverExceededAndZeroMeansNone()
    {
        var lamps = Row(40, 20f); var player = new V3(300f, 0f, 0f);
        Assert.Equal(4, new LampSelector(4, 200f).Update(lamps, player, 1f, 0f).Count);
        Assert.Empty(new LampSelector(0, 200f).Update(lamps, player, 1f, 0f));
        Assert.Equal(8, new LampSelector(8, 1000f).Update(lamps, player, 1f, 0f).Distinct().Count());
        Assert.Equal(2, new LampSelector(8, 25f).Update(lamps, new V3(0f, 0f, 0f), 1f, 0f).Count);      // only the lamps at x = 0 and x = 20 are within 25 m
    }

    [Fact] public void TheNearestLampsWinAndNothingBeyondTheRadiusIsChosen()
    {
        var lamps = Row(40, 20f); var sel = new LampSelector(2, 60f).Update(lamps, new V3(200f, 0f, 0f), 0f, 0f);
        Assert.Equal(2, sel.Count); Assert.All(sel, k => Assert.Contains(k, new[] { lamps[10].Key }.Concat(new[] { lamps[9].Key, lamps[11].Key })));
        Assert.Contains(lamps[10].Key, sel);
        Assert.Empty(new LampSelector(4, 10f).Update(lamps, new V3(5000f, 0f, 0f), 1f, 0f));
    }

    [Fact] public void LampsAheadAreFavouredOverLampsBehind()
    {
        var lamps = new List<Lamp> { L(-20f, 0f), L(21f, 0f) };       // one 20 m behind, one 21 m ahead
        var sel = new LampSelector(1, 100f).Update(lamps, new V3(0f, 0f, 0f), 1f, 0f);
        Assert.Equal(lamps[1].Key, sel.Single());
        Assert.Equal(lamps[0].Key, new LampSelector(1, 100f).Update(lamps, new V3(0f, 0f, 0f), -1f, 0f).Single());
    }

    [Fact] public void ALampThatHasALightKeepsItUnlessAnotherIsClearlyNearer()
    {
        var a = L(0f, 30f); var b = L(0f, -29f); var sel = new LampSelector(1, 100f);
        sel.Update(new List<Lamp> { a, b }, new V3(0f, 0f, 0f), 0f, 0f); long first = sel.Selected.Single();
        // the other lamp is now marginally nearer: no swap
        var p = new V3(0f, 0f, first == a.Key ? -2f : 2f); sel.Update(new List<Lamp> { a, b }, p, 0f, 0f); Assert.Equal(first, sel.Selected.Single());
        // clearly nearer: swap
        p = new V3(0f, 0f, first == a.Key ? -25f : 25f); sel.Update(new List<Lamp> { a, b }, p, 0f, 0f); Assert.NotEqual(first, sel.Selected.Single());
    }

    [Fact] public void LampsThatMayNotTakeARealLightAreIgnoredAndTheResultIsDeterministic()
    {
        var lamps = new List<Lamp> { L(1f, 0f, false), L(2f, 0f, false), L(50f, 0f) };
        Assert.Equal(lamps[2].Key, new LampSelector(3, 100f).Update(lamps, new V3(0f, 0f, 0f), 1f, 0f).Single());
        var many = Row(30, 7f); var s1 = new LampSelector(5, 100f).Update(many, new V3(50f, 0f, 0f), 1f, 0f).ToList(); var s2 = new LampSelector(5, 100f).Update(Enumerable.Reverse(many).ToList(), new V3(50f, 0f, 0f), 1f, 0f).ToList();
        Assert.Equal(s1, s2);        // the input order does not matter
    }

    [Fact] public void ARepeatedUpdateIsStableAndDuplicatesAreCollapsed()
    {
        var lamps = Row(20, 15f); lamps.Add(lamps[3]); var sel = new LampSelector(6, 120f);
        var first = sel.Update(lamps, new V3(90f, 0f, 0f), 1f, 0f).ToList(); var second = sel.Update(lamps, new V3(90f, 0f, 0f), 1f, 0f).ToList();
        Assert.Equal(first, second); Assert.Equal(first.Count, first.Distinct().Count());
    }
}

public class LampGlowTests
{
    static Lamp Street(float x = 100f, float z = 50f) => new Lamp { Key = 1, Pos = new V3(x, 9f, z), DirX = 1f, DirZ = 0f, OutX = 0f, OutZ = 1f, Kind = LampKind.Lamp, R = 1f, G = 0.7f, B = 0.35f, Intensity = 1f, Range = 24f, Halo = 1.8f, PoolRadius = 6f, PoolShift = 1.5f, Real = true };
    static float Flat(float x, float z) => 10f;

    [Fact] public void AStreetLampMakesAHaloAPoolAndAStreak()
    {
        var g = LampGlowGeometry.Build(new[] { Street() }, new V3(100f, 0f, 50f), Flat);
        Assert.Equal(1, g.Halos); Assert.Equal(1, g.Pools); Assert.Equal(1, g.Streaks);
        Assert.Equal(4 + 9 + 12, g.VertexCount); Assert.Equal((2 + 8 + 10) * 3, g.Triangles.Count);
        Assert.Null(g.Validate());
    }

    [Fact] public void ABeaconIsOnlyAHaloAndAGlowWithoutAPoolIsOnlyAHalo()
    {
        var beacon = Street(); beacon.Kind = LampKind.Beacon; var glow = Street(); glow.Kind = LampKind.Glow; glow.PoolRadius = 0f;
        var g = LampGlowGeometry.Build(new[] { beacon, glow }, new V3(0, 0, 0), Flat);
        Assert.Equal(2, g.Halos); Assert.Equal(0, g.Pools); Assert.Equal(0, g.Streaks); Assert.Equal(8, g.VertexCount);
        Assert.Equal(1f, g.Data[0][3]); Assert.Equal(0f, g.Data[4][3]);        // only the beacon pulses
    }

    [Fact] public void PositionsAreRelativeToTheOriginAndFollowTheGround()
    {
        var g = LampGlowGeometry.Build(new[] { Street() }, new V3(100f, 2f, 50f), (x, z) => 10f + 0.01f * (x - 100f));
        var halo = g.Vertices.Take(4).ToList(); Assert.All(halo, v => { Assert.Equal(0f, v.X, 3); Assert.Equal(7f, v.Y, 3); Assert.Equal(0f, v.Z, 3); });   // the head, 9 m up, minus the origin
        var pool = g.Vertices.Skip(4).Take(9).ToList();
        Assert.All(pool, v => Assert.Equal(10f + 0.01f * v.X + LampGlowGeometry.Lift - 2f, v.Y, 3));       // on the sloping ground, lifted a little
        Assert.Equal(1.5f, pool.Average(v => v.Z), 3);                                                      // shifted by the pool shift along the head's outward direction
        var streak = g.Vertices.Skip(13).ToList(); Assert.True(streak.Max(v => v.X) - streak.Min(v => v.X) > 2f * LampGlowGeometry.StreakHalfLength - 0.1f);     // long along the road (x)
        Assert.True(streak.Max(v => v.Z) - streak.Min(v => v.Z) < 2f * LampGlowGeometry.StreakHalfWidth + 0.1f);                                               // thin across it
    }

    [Fact] public void KindsAndIntensitiesAreCarriedPerVertex()
    {
        var l = Street(); l.Intensity = 2.5f; var g = LampGlowGeometry.Build(new[] { l }, new V3(0, 0, 0), Flat);
        Assert.All(g.Vertices.Select((v, i) => i), i => Assert.Equal(2.5f, g.Data[i][2]));
        Assert.Equal(new[] { 0f, 1f, 2f }, g.Data.Select(d => d[0]).Distinct().OrderBy(k => k).ToArray());
        Assert.All(g.Data.Take(4), d => Assert.Equal(0.9f, d[1], 3));      // half the halo size
        Assert.All(g.Colour, c => Assert.Equal(0.7f, c.G, 3));
    }

    [Fact] public void ManyLampsStayValidAndFinite()
    {
        var lamps = Enumerable.Range(0, 300).Select(i => Street(100f + i * 30f, 50f + (i % 7) * 3f)).ToList();
        var g = LampGlowGeometry.Build(lamps, new V3(100f, 0f, 50f), (x, z) => 10f + (float)Math.Sin(x * 0.01));
        Assert.Equal(300, g.Halos); Assert.Null(g.Validate()); Assert.Equal(300 * 25, g.VertexCount);
    }

    [Fact] public void TheAtlasIsFiniteBrightWhereItShouldBeAndZeroAtTheEdges()
    {
        for (int kind = 0; kind < 3; kind++)
            for (float u = 0f; u <= 1f; u += 0.05f)
                for (float v = 0f; v <= 1f; v += 0.05f) { float s = LampGlowTexture.Sample(kind, u, v); Assert.InRange(s, 0f, 1f); }
        Assert.True(LampGlowTexture.Sample(0, 0.5f, 0.5f) > 0.95f, "the halo has a hot core");
        Assert.True(LampGlowTexture.Sample(1, 0.5f, 0.5f) > 0.8f, "the pool is brightest in the middle");
        Assert.True(LampGlowTexture.Sample(2, 0.5f, 0.5f) > 0.9f, "the streak is brightest in the middle");
        Assert.True(LampGlowTexture.Sample(0, 0.0f, 0.5f) < 0.01f && LampGlowTexture.Sample(1, 1f, 0.5f) < 0.01f && LampGlowTexture.Sample(2, 0.5f, 0f) < 0.01f && LampGlowTexture.Sample(2, 0f, 0.5f) < 0.01f, "the edges must be black or the quads would show");
        Assert.True(LampGlowTexture.Sample(2, 0.5f, 0.9f) < LampGlowTexture.Sample(2, 0.5f, 0.6f), "the streak fades along its length");
    }

    [Fact] public void TheAtlasBufferHasTheRightSizeAndUsesTheWholeRange()
    {
        var px = LampGlowTexture.Build(); Assert.Equal(LampGlowTexture.Width * LampGlowTexture.Height, px.Length);
        Assert.True(px.Max() > 240); Assert.True(px.Min() < 5);
        for (int k = 0; k < 3; k++) { int sum = 0; for (int y = 0; y < LampGlowTexture.Height; y++) for (int x = 0; x < LampGlowTexture.Tile; x++) sum += px[y * LampGlowTexture.Width + k * LampGlowTexture.Tile + x]; Assert.True(sum > 2000, "tile " + k + " is empty"); }
    }
}
