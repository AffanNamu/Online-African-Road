using System;
using System.Collections.Generic;
using System.Linq;
using ARO.NetCore;
using Xunit;

/// <summary>
/// E3 junction forms (docs/ROAD_ENGINE_E3.md), all under schemaVersion 2: four-way crossings, junctions closer than 150 m, junctions on a divided road with a median opening, flared fillets.
/// A road that does not use them is refused these fields under schemaVersion 1 and builds exactly as before (the golden master).
/// </summary>
public class JunctionFormsTests
{
    static RouteSpec Spec(int version, Action<JunctionSpec[]> edit = null, params RoadProfileSpec[] profiles)
    {
        var s = Fx.Straight(2000f, profiles.Length > 0 ? profiles : new[] { Fx.Profile(lanes: 1), Fx.Profile("side", lanes: 1) });
        s.schemaVersion = version;
        s.junctions = new[] { new JunctionSpec { id = "x", profile = "side", s = 1000f, angleDeg = 90f, length = 200f, kind = version >= 2 ? "four_way" : "t_junction" } };
        edit?.Invoke(s.junctions); return s;
    }
    static List<string> Errors(RouteSpec s) => RouteSpecValidator.Validate(s);
    static MeshBuffers Whole(RouteModel m, RoadCorridor c = null) => Fx.WholeRoad(c ?? m.Main, (x, z) => m.Terrain.HeightAt(x, z));

    // ---------------------------------------------------------------- the version gate
    [Fact] public void SchemaVersionTwoIsAcceptedAndOthersAreNot()
    {
        Assert.Empty(Errors(Spec(2)));
        var s = Spec(2); s.schemaVersion = 3; Assert.Contains(Errors(s), e => e.Contains("schemaVersion"));
        s = Spec(2); s.schemaVersion = 0; Assert.Contains(Errors(s), e => e.Contains("schemaVersion"));
    }

    [Fact] public void TheNewFieldsAreRefusedUnderVersionOne()
    {
        Assert.Contains(Errors(Spec(1, j => j[0].kind = "four_way")), e => e.Contains("four_way needs schemaVersion 2"));
        Assert.Contains(Errors(Spec(1, j => j[0].fillet = 5f)), e => e.Contains("need schemaVersion 2"));
        Assert.Contains(Errors(Spec(1, j => j[0].lengthLeft = 100f)), e => e.Contains("need schemaVersion 2"));
        var two = Spec(1); two.junctions = new[] { new JunctionSpec { id = "a", profile = "side", s = 800f, length = 100f }, new JunctionSpec { id = "b", profile = "side", s = 900f, length = 100f } };
        Assert.Contains(Errors(two), e => e.Contains("closer than 150"));
        two.schemaVersion = 2; Assert.Empty(Errors(two));                                                       // 100 m apart is fine under version 2
    }

    // ---------------------------------------------------------------- four-way
    [Fact] public void AFourWayJunctionBuildsTwoOppositeStubsAtTheSameStation()
    {
        var m = RouteModel.Build(Spec(2, j => { j[0].lengthLeft = 120f; }));
        Assert.Equal(2, m.Sides.Count);
        var r = m.CorridorById("x"); var l = m.CorridorById("x_left");
        Assert.NotNull(r); Assert.NotNull(l);
        float paved = m.Main.HalfPavedAt(1000f);
        var rs = r.Spline.PositionAt(0f); var ls = l.Spline.PositionAt(0f); var re = r.Spline.PositionAt(r.Length); var le = l.Spline.PositionAt(l.Length);
        Assert.Equal(1000f, rs.X, 1); Assert.Equal(-paved, rs.Z, 1); Assert.Equal(paved, ls.Z, 1);          // right = south, left = north, both leave the paved edges at s = 1000
        Assert.Equal(1000f, re.X, 0); Assert.Equal(1000f, le.X, 0);
        Assert.Equal(200f, r.Length, 0); Assert.Equal(120f, l.Length, 0);
        Assert.True(re.Z < -150f && le.Z > 100f);
    }

    [Fact] public void TheOppositeStubMayHaveItsOwnProfileAndBothInterruptTheEdgeLines()
    {
        var wide = Fx.Profile("wide", lanes: 2);
        var s = Spec(2, j => j[0].profileLeft = "wide", Fx.Profile(lanes: 1), Fx.Profile("side", lanes: 1), wide);
        var m = RouteModel.Build(s);
        Assert.Equal("side", m.CorridorById("x").Runs[0].Section.Spec.id); Assert.Equal("wide", m.CorridorById("x_left").Runs[0].Section.Spec.id);
        Assert.Equal(2, m.Main.MarkingGaps.Length); Assert.Contains(m.Main.MarkingGaps, g => g.Side == 1); Assert.Contains(m.Main.MarkingGaps, g => g.Side == -1);
        var plain = RouteModel.Build(Spec(2, j => j[0].kind = "t_junction"));
        var both = Fx.Area(Whole(m), RoadSub.MarkWhite); var one = Fx.Area(Whole(plain), RoadSub.MarkWhite);
        Assert.True(both < one - 1f, "the left edge line stops for the opposite stub too");
    }

    [Fact] public void FourWayData_IsCheckedTheOppositeStubIdAndProfile()
    {
        Assert.Contains(Errors(Spec(2, j => j[0].profileLeft = "ghost")), e => e.Contains("profileLeft"));
        Assert.Contains(Errors(Spec(2, j => j[0].lengthLeft = 10f)), e => e.Contains("lengthLeft"));
        var s = Spec(2); s.junctions = new[] { s.junctions[0], new JunctionSpec { id = "x_left", profile = "side", s = 1400f, length = 100f } };
        Assert.Contains(Errors(s), e => e.Contains("already used"));
    }

    [Fact] public void StubsAndSitesCanBeReferencedByTheOppositeStubsName()
    {
        var s = Spec(2);
        s.symbols = new[] { new MarkingSymbolSpec { id = "z", kind = "zebra", corridor = "x_left", s = 60f, side = "both" } };
        s.props = new[] { new PropPlacementSpec { id = "p", prop = "road_sign", corridor = "x_left", s = 50f, lateral = 6f } };
        s.sites = new[] { new SitePlateSpec { id = "yard", corridor = "x_left", s = 200f, forward = 30f, widthM = 40f, depthM = 60f } };
        Assert.Empty(Errors(s));
        var m = RouteModel.Build(s);
        Assert.Single(m.CorridorById("x_left").Symbols); Assert.Equal(1, m.Sites.Count);
        Assert.True(m.Sites[0].Cz > 100f, "the yard is on the north (left) stub");
        Assert.NotEmpty(PropScatter.PlaceChunk(m, 900f, 0f, 1100f, 300f).Where(i => i.Origin == "p"));
    }

    // ---------------------------------------------------------------- relaxed spacing
    [Fact] public void JunctionsMayBeSixtyMetresApartButTheirRoadsMayNotOverlap()
    {
        Assert.Empty(Errors(Spec(2, null)));
        var s = Spec(2); s.junctions = new[] { new JunctionSpec { id = "a", profile = "side", s = 900f, length = 100f }, new JunctionSpec { id = "b", profile = "side", s = 965f, length = 100f } };
        Assert.Empty(Errors(s));
        s.junctions[1].s = 950f; Assert.Contains(Errors(s), e => e.Contains("closer than 60"));
        var wide = Fx.Profile("wide", lanes: 4, laneWidth: 4f, shoulder: 3f); var w = Spec(2, null, Fx.Profile(lanes: 1), wide);        // half formation 20 m; with 15 m flares the stubs need 72 m
        w.junctions = new[] { new JunctionSpec { id = "a", profile = "wide", s = 900f, length = 100f, fillet = 15f }, new JunctionSpec { id = "b", profile = "wide", s = 965f, length = 100f, fillet = 15f } };
        Assert.Contains(Errors(w), e => e.Contains("would overlap"));
        w.junctions[1].angleDeg = -90f; Assert.Empty(Errors(w));                                                // opposite sides do not collide
    }

    // ---------------------------------------------------------------- median opening
    static RouteSpec Divided(string style, bool opening, int version = 2)
    {
        var main = Fx.Profile("div", lanes: 2, median: 3f, shoulder: 0.5f); main.medianStyle = style; main.innerShoulderWidth = 0.3f;
        if (style != "jersey") { main.kerbHeight = 0.12f; main.sidewalkWidth = 2f; main.drainWidth = 0.3f; main.kerbPaint = "black_white"; }
        var s = Fx.Straight(2000f, main, Fx.Profile("side", lanes: 1)); s.schemaVersion = version;
        s.junctions = new[] { new JunctionSpec { id = "x", profile = "side", s = 1000f, length = 200f, medianOpening = opening } };
        return s;
    }

    [Fact] public void ADividedRoadNeedsAnOpeningAndAPlanterOrPaintedMedianForAJunction()
    {
        Assert.Contains(Errors(Divided("planter", false, 1)), e => e.Contains("interchange"));
        Assert.Contains(Errors(Divided("planter", false)), e => e.Contains("needs medianOpening"));
        Assert.Contains(Errors(Divided("jersey", true)), e => e.Contains("cannot be opened"));
        Assert.Empty(Errors(Divided("planter", true))); Assert.Empty(Errors(Divided("painted", true)));
        var plain = Spec(2); plain.junctions[0].medianOpening = true; Assert.Contains(Errors(plain), e => e.Contains("needs a divided section"));
    }

    [Fact] public void TheMedianIsCutToCarriagewayAcrossTheJunction()
    {
        var m = RouteModel.Build(Divided("planter", true)); var mb = Whole(m);
        Assert.True(mb.Validate(out var err), err);
        float half = m.CorridorById("x").Runs[0].Section.FormationHalfWidth;
        bool InOpening(V3 c) => Math.Abs(c.X - 1000f) < half - 4.5f;
        int planterIn = 0, rimIn = 0, planterOut = 0, asphaltOnMedianIn = 0;
        foreach (var (_, c) in Fx.Tris(mb, RoadSub.Planter)) { if (InOpening(c)) planterIn++; else planterOut++; }
        foreach (var (_, c) in Fx.Tris(mb, RoadSub.KerbPainted)) if (InOpening(c) && Math.Abs(c.Z) < 1.5f) rimIn++;
        foreach (var (n, c) in Fx.Tris(mb, RoadSub.Asphalt)) if (InOpening(c) && Math.Abs(c.Z) < 1.2f) { asphaltOnMedianIn++; Assert.True(n.Y > 0f); Assert.InRange(c.Y, 9.95f, 10.01f); }
        Assert.Equal(0, planterIn); Assert.Equal(0, rimIn); Assert.True(planterOut > 20); Assert.True(asphaltOnMedianIn > 0, "the median band is carriageway");
        var closed = Whole(RouteModel.Build(Divided("planter", false, 1).Also(sp => { sp.junctions = new JunctionSpec[0]; })));
        Assert.True(closed.Triangles[RoadSub.Planter].Count > mb.Triangles[RoadSub.Planter].Count, "the unopened median has planter all along");
    }

    [Fact] public void ThePaintedMedianOpensToo()
    {
        var m = RouteModel.Build(Divided("painted", true)); var mb = Whole(m); Assert.True(mb.Validate(out var err), err);
        int concreteInOpening = 0;
        foreach (var (_, c) in Fx.Tris(mb, RoadSub.Concrete)) if (Math.Abs(c.X - 1000f) < 3f && Math.Abs(c.Z) < 1.5f) concreteInOpening++;
        Assert.Equal(0, concreteInOpening);
    }

    // ---------------------------------------------------------------- fillets
    [Fact] public void AFilletIsTheSpandrelBetweenTheCornerAndTheCircle()
    {
        var polys = RoadGeometry.FilletPolygons(1000f, 4f, 3f, 8f);
        Assert.Equal(2, polys.Count);
        foreach (var p in polys)
        {
            float area = 0f; for (int i = 1; i + 1 < p.Length; i++) area += Math.Abs((p[i].X - p[0].X) * (p[i + 1].Y - p[0].Y) - (p[i + 1].X - p[0].X) * (p[i].Y - p[0].Y)) * 0.5f;
            Assert.InRange(area, 64f * (1f - (float)Math.PI / 4f) * 0.97f, 64f * (1f - (float)Math.PI / 4f) * 1.10f);
            Assert.All(p, v => Assert.InRange(v.Y, 4f - 1e-3f, 12f + 1e-3f));
        }
        var down = polys[0]; var up = polys[1];
        Assert.Equal(1003f, down[0].X, 3); Assert.Equal(4f, down[0].Y, 3);                                       // corner: station 1003 (the mouth's downstream edge), the main road's paved edge
        Assert.Equal(1003f, down[1].X, 3); Assert.Equal(12f, down[1].Y, 3);                                      // arc starts on the side road's edge, R out
        Assert.Equal(1011f, down[down.Length - 1].X, 3); Assert.Equal(4f, down[down.Length - 1].Y, 3);          // and ends on the main road's edge, R down the road
        Assert.Equal(997f, up[0].X, 3); Assert.Equal(989f, up[up.Length - 1].X, 3);
    }

    [Fact] public void FilletsAddAsphaltOnlyOutsideThePavedEdgesAndBothCornersOfEveryStub()
    {
        var plain = RouteModel.Build(Spec(2, null)); var flared = RouteModel.Build(Spec(2, j => j[0].fillet = 8f));
        var a0 = Fx.Area(Whole(plain), RoadSub.Asphalt); var mbF = Whole(flared); var a1 = Fx.Area(mbF, RoadSub.Asphalt);
        float one = 64f * (1f - (float)Math.PI / 4f);
        Assert.InRange(a1 - a0, 4f * one * 0.95f, 4f * one * 1.15f);                                              // a four-way: four corners
        Assert.True(mbF.Validate(out var err), err);
        foreach (var (n, _) in Fx.Tris(mbF, RoadSub.Asphalt)) Assert.True(n.Y > 0f);
        var t = RouteModel.Build(Spec(2, j => { j[0].kind = "t_junction"; j[0].fillet = 8f; }));
        Assert.InRange(Fx.Area(Whole(t), RoadSub.Asphalt) - Fx.Area(Whole(RouteModel.Build(Spec(2, j => j[0].kind = "t_junction"))), RoadSub.Asphalt), 2f * one * 0.95f, 2f * one * 1.15f);
        Assert.Equal(2, flared.Main.Fillets.Length);                                                              // one per stub, two corners each
    }

    [Fact] public void TheEdgeLineGapGrowsWithTheFilletAndTheFlareLiesOnTheRoadSurface()
    {
        var plain = RouteModel.Build(Spec(2, j => j[0].kind = "t_junction")); var flared = RouteModel.Build(Spec(2, j => { j[0].kind = "t_junction"; j[0].fillet = 8f; }));
        Assert.Equal(plain.Main.MarkingGaps[0].S1 - plain.Main.MarkingGaps[0].S0 + 16f, flared.Main.MarkingGaps[0].S1 - flared.Main.MarkingGaps[0].S0, 3);
        var mb = Whole(flared);
        var ys = Fx.Tris(mb, RoadSub.Asphalt).Where(t => Math.Abs(t.c.X - 1000f) > 9f && Math.Abs(t.c.X - 1000f) < 19f && t.c.Z < -4.2f).Select(t => t.c.Y).ToArray();
        Assert.NotEmpty(ys); Assert.All(ys, y => Assert.InRange(y, 10f + flared.Main.SectionAt(1000f).HeightAt(flared.Main.SectionAt(1000f).PavedHalfWidth) + RoadGeometry.FilletLift - 0.005f, 10f + RoadGeometry.FilletLift + 0.005f));
    }

    [Theory] [InlineData("angle")] [InlineData("big")] [InlineData("neg")] [InlineData("kerbed")]
    public void BadFilletsAreRefused(string what)
    {
        RouteSpec s;
        if (what == "kerbed") { var k = Fx.Profile("kmain", lanes: 1); k.kerbHeight = 0.12f; k.sidewalkWidth = 2f; s = Spec(2, j => { j[0].fillet = 6f; j[0].kind = "t_junction"; }, k, Fx.Profile("side", lanes: 1)); }
        else s = Spec(2, j => { j[0].fillet = what == "big" ? 30f : what == "neg" ? -1f : 6f; if (what == "angle") j[0].angleDeg = 60f; });
        Assert.Contains(Errors(s), e => e.Contains("fillet"));
    }

    [Fact] public void RoutesWithoutTheNewFieldsAreUntouched()
    {
        var a = Fx.WholeRoad(RouteModel.Build(Spec(1, j => j[0].kind = "t_junction")).Main); var b = Fx.WholeRoad(RouteModel.Build(Spec(2, j => j[0].kind = "t_junction")).Main);
        Assert.Equal(a.VertexCount, b.VertexCount); for (int i = 0; i < a.Triangles.Length; i++) Assert.Equal(a.Triangles[i].Count, b.Triangles[i].Count);
        Assert.Equal(RoadSub.Count, a.Triangles.Length);
    }
}

static class SpecExt { public static T Also<T>(this T v, Action<T> f) { f(v); return v; } }
