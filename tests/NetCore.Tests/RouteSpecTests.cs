using System;
using System.Collections.Generic;
using System.Linq;
using ARO.NetCore;
using Xunit;

public class RouteSpecTests
{
    static List<string> Errors(Action<RouteSpec> mutate, ISet<string> props = null) { var s = Fx.Clone(Fx.LoadRoute()); mutate(s); return RouteSpecValidator.Validate(s, props); }
    static void Rejects(Action<RouteSpec> mutate, string expect, ISet<string> props = null)
    {
        var e = Errors(mutate, props); Assert.True(e.Any(x => x.Contains(expect, StringComparison.OrdinalIgnoreCase)), $"expected an error containing '{expect}', got:\n" + string.Join("\n", e));
    }

    [Fact] public void ShippedRouteIsValidAndUsesOnlyCataloguedProps()
    {
        var props = new HashSet<string>(Fx.LoadCatalog().props.Select(p => p.id));
        var e = RouteSpecValidator.Validate(Fx.LoadRoute(), props); Assert.True(e.Count == 0, string.Join("\n", e));
    }

    [Fact] public void NullAndWrongVersionAreRejected()
    {
        Assert.Contains("null", RouteSpecValidator.Validate(null)[0]);
        Rejects(s => s.schemaVersion = 2, "schemaVersion");
    }

    [Fact] public void IdentityAndMetaAreRequired()
    {
        Rejects(s => s.routeId = "Bad Id", "routeId"); Rejects(s => s.name = " ", "name");
        Rejects(s => s.meta = null, "meta is required"); Rejects(s => s.meta.realRoadKm = 0, "realRoadKm");
        Rejects(s => s.meta.gameCoverage = "", "gameCoverage"); Rejects(s => s.meta.elevationSource = null, "elevationSource");
    }

    [Fact] public void ControlPointProblemsAreReported()
    {
        Rejects(s => s.controlPoints = s.controlPoints.Take(1).ToArray(), "at least two control points");
        Rejects(s => s.controlPoints[3].x = float.NaN, "finite");
        Rejects(s => s.controlPoints[2].id = s.controlPoints[1].id, "duplicate");
        Rejects(s => s.controlPoints[2].zone = "marsh", "unknown zone");
        Rejects(s => s.controlPoints[2].surface = "gravel", "unknown surface");
        Rejects(s => s.controlPoints[2].profile = "ghost", "does not exist");
        Rejects(s => { s.controlPoints[2].x = s.controlPoints[1].x + 5f; s.controlPoints[2].z = s.controlPoints[1].z; }, "closer than");
        Rejects(s => s.controlPoints[4].elevation = 9000f, "elevation out of range");
    }

    [Fact] public void ImpossibleGeometryIsRejected()
    {
        Rejects(s => s.controlPoints[6].elevation += 600f, "grade");                               // a 600 m cliff
        var zig = Fx.Straight(2000f); zig.controlPoints = new[]
        {
            new ControlPoint { id = "a", x = 0, z = 0, elevation = 0, zone = "rural", surface = "good", profile = "p" },
            new ControlPoint { id = "b", x = 100, z = 0, elevation = 0, zone = "rural", surface = "good", profile = "p" },
            new ControlPoint { id = "c", x = 100, z = 60, elevation = 0, zone = "rural", surface = "good", profile = "p" },
            new ControlPoint { id = "d", x = 0, z = 60, elevation = 0, zone = "rural", surface = "good", profile = "p" },
        };
        Assert.Contains(RouteSpecValidator.Validate(zig), x => x.Contains("radius"));            // a hairpin no truck can drive
    }

    [Fact] public void ProfileProblemsAreReported()
    {
        Rejects(s => s.profiles = new RoadProfileSpec[0], "at least one road profile");
        Rejects(s => s.profiles[1].id = s.profiles[0].id, "duplicate profile");
        Rejects(s => s.profiles[0].lanesPerDirection = 9, "lanesPerDirection");
        Rejects(s => s.profiles[0].centreLine = "zigzag", "centreLine");
        Rejects(s => s.profiles[0].edgeBarrier = "wall", "edgeBarrier");
        Rejects(s => s.profiles[0].camberPercent = 20f, "camber");
        Rejects(s => s.profiles[0].ditchDepth = 9f, "ditch");
    }

    [Fact] public void TerrainProblemsAreReported()
    {
        Rejects(s => s.terrain = null, "terrain is required");
        Rejects(s => s.terrain.maxEmbankmentSlope = 3f, "maxEmbankmentSlope");
        Rejects(s => s.terrain.hillAmplitude = -1f, "amplitudes");
        Rejects(s => s.terrain.hillWavelength = 10f, "wavelengths");
    }

    [Fact] public void BridgeProblemsAreReported()
    {
        Rejects(s => s.bridges[0].endS = s.bridges[0].startS - 5f, "span must satisfy");
        Rejects(s => s.bridges[0].endS = s.bridges[0].startS + 10f, "too short");
        Rejects(s => s.bridges[0].startS = -10f, "span must satisfy");
        Rejects(s => s.bridges[0].endS = 1e9f, "span must satisfy");
        Rejects(s => s.bridges[0].valleyDepth = 90f, "valleyDepth");
        Rejects(s => s.bridges[0].deckThickness = 0f, "deckThickness");
        Rejects(s => s.bridges[0].pierSpacing = 2f, "pierSpacing");
        Rejects(s => s.bridges[1].id = s.bridges[0].id, "duplicate");
        Rejects(s => { s.bridges[1].startS = s.bridges[0].endS + 5f; s.bridges[1].endS = s.bridges[1].startS + 140f; }, "closer than 30 m");
        Rejects(s => { s.bridges[0].startS = 9000f; s.bridges[0].endS = 9500f; }, "profile changes on the bridge");     // straddles the expressway entry
    }

    [Fact] public void JunctionProblemsAreReported()
    {
        Rejects(s => s.junctions[0].profile = "ghost", "does not exist");
        Rejects(s => s.junctions[0].kind = "roundabout", "t_junction");
        Rejects(s => s.junctions[0].s = 10f, "at least 60 m");
        Rejects(s => s.junctions[0].angleDeg = 5f, "angleDeg");
        Rejects(s => s.junctions[0].length = 5f, "length");
        Rejects(s => s.junctions[1].rise = 100f, "steeper");
        Rejects(s => s.junctions[1].s = s.junctions[0].s + 20f, "closer than 150 m");
        Rejects(s => s.junctions[0].s = 12320f, "bridge");
        Rejects(s => s.junctions[0].s = 7000f, "divided");                                           // on the divided Ikeja section
        Rejects(s => s.junctions[2].id = s.junctions[0].id, "duplicate");
    }

    [Fact] public void PropAndWaypointAndTrafficProblemsAreReported()
    {
        var known = new HashSet<string>(Fx.LoadCatalog().props.Select(p => p.id));
        Rejects(s => s.propRules[0].prop = "unobtainium", "unknown prop", known);
        Rejects(s => s.propRules[0].side = "up", "side");
        Rejects(s => { s.propRules[0].offsetMin = 9f; s.propRules[0].offsetMax = 2f; }, "offsets");
        Rejects(s => s.propRules[0].spacingMin = 0.5f, "spacing");
        Rejects(s => s.propRules[0].presence = 2f, "presence");
        Rejects(s => s.propRules[0].scaleMin = 0f, "scale");
        Rejects(s => s.propRules[0].zones = "urban,swamp", "unknown zone");
        Rejects(s => s.props[0].s = 1e9f, "outside the route");
        Rejects(s => s.props[1].id = s.props[0].id, "duplicate");
        Rejects(s => s.props[0].prop = "ghost", "unknown prop", known);
        Rejects(s => s.waypoints[2].s = 10f, "increasing order");
        Rejects(s => s.waypoints[1].id = s.waypoints[0].id, "duplicate");
        Rejects(s => s.traffic.densityScale = 9f, "densityScale");
        Rejects(s => s.traffic.speedLimitScale = 0f, "speedLimitScale");
    }

    [Fact] public void JsonRoundTripPreservesTheSpec()
    {
        var a = Fx.LoadRoute(); var b = Fx.Clone(a);
        Assert.Equal(a.controlPoints.Length, b.controlPoints.Length); Assert.Equal(a.controlPoints[5].elevation, b.controlPoints[5].elevation);
        Assert.Empty(RouteSpecValidator.Validate(b));
    }

    [Fact] public void TryBuildReportsInsteadOfThrowing()
    {
        var s = Fx.Clone(Fx.LoadRoute()); s.controlPoints[1].zone = "marsh";
        Assert.False(RouteModel.TryBuild(s, out var m, out var errors)); Assert.Null(m); Assert.NotEmpty(errors);
        Assert.True(RouteModel.TryBuild(Fx.LoadRoute(), out m, out errors)); Assert.NotNull(m); Assert.Empty(errors);
        Assert.Throws<ArgumentException>(() => RouteModel.Build(s));
    }
}
