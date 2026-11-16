using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ARO.NetCore;
using Xunit;

/// <summary>
/// Route data packing (2026-11-16): frontage rules may name a shared lot list (RouteSpec.mixes, FrontageRule.mixId / anchorsId) instead of each repeating its own; FrontageShared.Resolve, run by the
/// validator, copies the list back into each rule. Packing must be lossless (the same buildings in the same places as the route written out), mistakes in it must be reported, it must run once
/// (the game validates and then builds), no lot object may be shared between two rules, and a route without shared mixes must not be touched.
/// </summary>
public class SharedMixTests
{
    static readonly JsonSerializerOptions Opt = new JsonSerializerOptions { IncludeFields = true };
    static RouteSpec Load(string id) => JsonSerializer.Deserialize<RouteSpec>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, id + ".route.json")), Opt);
    static List<FrontagePlacement> All(RouteModel m) => Enumerable.Range(0, m.Spec.frontage.Length).SelectMany(i => FrontagePlacer.AllLots(m, i)).ToList();
    static ISet<string> Known => new HashSet<string>(Fx.LoadCatalog().props.Select(p => p.id));

    /// <summary>The same route with every distinct lot list written once under `mixes` and named by the rules (what tools/kano/make_city.py pack_mixes writes).</summary>
    static RouteSpec Packed(RouteSpec s)
    {
        var p = Fx.Clone(s); var mixes = new List<FrontageMixSpec>();
        string Name(FrontageLot[] lots)
        {
            var key = JsonSerializer.Serialize(lots, Opt); var hit = mixes.FindIndex(m => JsonSerializer.Serialize(m.lots, Opt) == key);
            if (hit >= 0) return mixes[hit].id;
            mixes.Add(new FrontageMixSpec { id = "m" + mixes.Count, lots = Fx.Clone(lots) }); return mixes[mixes.Count - 1].id;
        }
        foreach (var r in p.frontage) { r.mixId = Name(r.mix); r.mix = null; if (r.anchors != null && r.anchors.Length > 0) { r.anchorsId = Name(r.anchors); r.anchors = null; } }
        p.mixes = mixes.ToArray(); return p;
    }

    [Theory, InlineData("ng-kano-city-e1"), InlineData("ng-kano-city-f1"), InlineData("ng-kano-city-p1")]
    public void APackedRouteBuildsExactlyTheBuildingsOfTheRouteWrittenOut(string id)
    {
        var plain = Load(id); Assert.True(plain.mixes == null || plain.mixes.Length == 0, "the test routes write their mixes out");
        var packed = Packed(plain); Assert.All(packed.frontage, r => Assert.True(r.mix == null && !string.IsNullOrEmpty(r.mixId)));
        Assert.Empty(RouteSpecValidator.Validate(packed, Known));
        var a = All(RouteModel.Build(plain)); var b = All(RouteModel.Build(packed));
        Assert.True(a.Count > 50, $"{a.Count} lots");
        Assert.Equal(FrontageTests.Digest(a), FrontageTests.Digest(b));
        for (int i = 0; i < plain.frontage.Length; i++)
        {
            Assert.Equal(JsonSerializer.Serialize(plain.frontage[i].mix, Opt), JsonSerializer.Serialize(packed.frontage[i].mix, Opt));
            Assert.Equal(JsonSerializer.Serialize(plain.frontage[i].anchors ?? new FrontageLot[0], Opt), JsonSerializer.Serialize(packed.frontage[i].anchors ?? new FrontageLot[0], Opt));
        }
    }

    [Fact] public void TheCityWritesEachMixOnceAndEveryRuleGetsItsOwnCopy()
    {
        var spec = Load("ng-kano-city");
        Assert.True(spec.mixes != null && spec.mixes.Length >= 10 && spec.mixes.Length < spec.frontage.Length / 4, $"{spec.mixes?.Length} shared mixes for {spec.frontage.Length} rules");
        Assert.All(spec.frontage, r => Assert.True((r.mix == null || r.mix.Length == 0) && !string.IsNullOrEmpty(r.mixId), r.id + " names its mix"));
        Assert.Empty(RouteSpecValidator.Validate(spec, Known));
        Assert.All(spec.frontage, r => Assert.True(r.mix != null && r.mix.Length > 0, r.id + " has its mix after loading"));
        var seen = new HashSet<FrontageLot>(ReferenceEqualityComparer.Instance);
        foreach (var r in spec.frontage) foreach (var l in r.mix.Concat(r.anchors ?? new FrontageLot[0])) Assert.True(seen.Add(l), r.id + ": a lot object shared with another rule");
        foreach (var m in spec.mixes) foreach (var l in m.lots) Assert.DoesNotContain(l, seen);
    }

    [Fact] public void MistakesInSharedMixesAreReported()
    {
        List<string> Bad(Action<RouteSpec> spoil) { var s = Packed(Load("ng-kano-city-e1")); spoil(s); return RouteSpecValidator.Validate(s, Known); }
        Assert.Contains(Bad(s => s.frontage[0].mixId = "nope"), e => e.Contains("shared mix 'nope' does not exist"));
        Assert.Contains(Bad(s => s.frontage[0].mix = s.mixes[0].lots), e => e.Contains("give mix or mixId, not both"));
        Assert.Contains(Bad(s => s.mixes = s.mixes.Concat(new[] { new FrontageMixSpec { id = s.mixes[0].id, lots = s.mixes[0].lots } }).ToArray()), e => e.Contains("shared mix id is missing or duplicate"));
        Assert.Contains(Bad(s => s.mixes = s.mixes.Concat(new[] { new FrontageMixSpec { id = "empty", lots = new FrontageLot[0] } }).ToArray()), e => e.Contains("shared mix 'empty' needs at least one lot"));
        Assert.Contains(Bad(s => s.frontage[0].anchorsId = "nope"), e => e.Contains("shared mix 'nope' does not exist"));
        Assert.Contains(Bad(s => s.mixes[0].lots[0].prop = "zzz"), e => e.Contains("unknown prop 'zzz'"));           // the shared lots go through every check of a rule's own mix
    }

    [Fact] public void ResolvingRunsOnceSoTheGameCanValidateAndThenBuild()
    {
        var spec = Packed(Load("ng-kano-city-e1"));
        Assert.Empty(RouteSpecValidator.Validate(spec, Known));
        var first = spec.frontage.Select(r => r.mix).ToArray();
        Assert.Empty(RouteSpecValidator.Validate(spec, Known));                  // a second pass would see a mix and a mixId together if it resolved again
        var m = RouteModel.Build(spec);                                          // Build validates once more
        for (int i = 0; i < first.Length; i++) Assert.Same(first[i], m.Spec.frontage[i].mix);
    }

    [Theory, InlineData("ng-lagos-ibadan"), InlineData("ng-kano-city-e1"), InlineData("ng-kano-e5-f1")]
    public void ARouteWithoutSharedMixesIsNotTouched(string id)
    {
        var spec = Load(id); var before = (spec.frontage ?? new FrontageRule[0]).Select(r => (r.mix, r.anchors)).ToArray();
        RouteSpecValidator.Validate(spec, Known);
        var after = (spec.frontage ?? new FrontageRule[0]).Select(r => (r.mix, r.anchors)).ToArray();
        Assert.Equal(before.Length, after.Length);
        for (int i = 0; i < before.Length; i++) { Assert.Same(before[i].mix, after[i].mix); Assert.Same(before[i].anchors, after[i].anchors); }
        Assert.True(spec.mixes == null || spec.mixes.Length == 0);
    }
}
