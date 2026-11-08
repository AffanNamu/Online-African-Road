using System;
using System.Collections.Generic;
using System.Linq;
using ARO.NetCore;
using Xunit;

/// <summary>
/// E1 of the road engine (docs/KANO_TRANSPORT_EXPANSION_PLAN.md section 5.2): kerbs, walkways, gutters, planter and painted medians, the road-class catalogue, dropped kerbs at road mouths and
/// pavement symbols. Everything is optional and defaults to "not there"; GoldenMasterTests proves that the shipped routes are byte-for-byte unchanged, these tests prove the new elements.
/// The test road runs due east, so the driver's right (positive lateral) is world -z.
/// </summary>
public class RoadE1Tests
{
    static RoadProfileSpec Kerbed(string id = "k", float kerb = 0.12f, float walk = 2f, float drain = 0.3f, float shoulder = 0.5f, float verge = 0.8f, int lanes = 1, string paint = "none")
    {
        var p = Fx.Profile(id, lanes: lanes, shoulder: shoulder, verge: verge);
        p.kerbHeight = kerb; p.sidewalkWidth = walk; p.drainWidth = drain; p.kerbPaint = paint; return p;
    }
    static MeshBuffers Whole(RouteModel m, RoadCorridor c = null) => Fx.WholeRoad(c ?? m.Main, (x, z) => m.Terrain.HeightAt(x, z));
    static float Lateral(V3 v) => -v.Z;
    static float Y(RoadCrossSection cs, float x) => cs.HeightAt(x);

    // ---------------------------------------------------------------- profile layout
    [Fact] public void AProfileThatSetsNoE1FieldIsUnchanged()
    {
        var cs = RoadCrossSection.Build(Fx.Profile(lanes: 2));
        Assert.False(cs.UsesE1Strips); Assert.False(cs.HasKerb);
        Assert.DoesNotContain("e1", cs.TopologyKey);
        Assert.All(cs.Points, pt => Assert.True(pt.Strip < RoadSub.Count));
        var m = RouteModel.Build(Fx.Straight(500f, Fx.Profile(lanes: 2)));
        Assert.Equal(RoadSub.Count, Whole(m).Triangles.Length);                      // seven submeshes: a plain road does not carry the E1 slots
    }

    [Fact] public void AKerbedProfileAddsGutterKerbWalkAndKeepsTheOldEdges()
    {
        var p = Kerbed(); var cs = RoadCrossSection.Build(p);
        Assert.True(cs.UsesE1Strips && cs.HasKerb);
        Assert.Equal(3.5f, cs.CarriagewayHalfWidth, 3);
        Assert.Equal(4.0f, cs.PavedHalfWidth, 3);                                      // shoulder edge: where the gutter begins
        float expected = 4.0f + 0.3f + RoadCrossSection.KerbFace + RoadCrossSection.KerbTopWidth + 2f + 0.8f;
        Assert.Equal(expected, cs.FormationHalfWidth, 3);
        foreach (int sub in new[] { RoadSub.Drain, RoadSub.Kerb, RoadSub.Sidewalk, RoadSub.Verge }) Assert.Contains(cs.Points, pt => pt.Strip == sub);
        float ys = Y(cs, cs.PavedHalfWidth);
        float walkMid = cs.PavedHalfWidth + 0.3f + RoadCrossSection.KerbFace + RoadCrossSection.KerbTopWidth + 1f;
        Assert.Equal(ys + 0.12f, Y(cs, walkMid), 3);                                   // the walkway stands exactly one kerb height above the paved shoulder
        Assert.True(Y(cs, cs.PavedHalfWidth + 0.15f) < ys, "the gutter is below the shoulder");
    }

    [Fact] public void ThePaintedKerbHasItsOwnStrip()
    {
        var cs = RoadCrossSection.Build(Kerbed(paint: "black_white"));
        Assert.Contains(cs.Points, pt => pt.Strip == RoadSub.KerbPainted); Assert.DoesNotContain(cs.Points, pt => pt.Strip == RoadSub.Kerb);
        Assert.True(cs.KerbIsPainted);
    }

    [Fact] public void TheCrossSectionIsMirroredLeftAndRight()
    {
        foreach (var p in new[] { Kerbed(lanes: 2, paint: "black_white"), RoadClasses.Make(RoadClasses.Arterial), RoadClasses.Make(RoadClasses.Expressway) })
        {
            var pts = RoadCrossSection.Build(p).Points; int n = pts.Length;
            for (int i = 0; i < n; i++) { Assert.Equal(-pts[i].X, pts[n - 1 - i].X, 4); Assert.Equal(pts[i].Y, pts[n - 1 - i].Y, 4); }
            for (int i = 0; i < n - 1; i++) Assert.Equal(pts[i].Strip, pts[n - 2 - i].Strip);        // the strip between two points is the same seen from either side
            for (int i = 0; i + 1 < n; i++) Assert.True(pts[i + 1].X >= pts[i].X - 1e-5f, "points run left to right");
        }
    }

    [Fact] public void ARoadMeshWithKerbsIsValidAndEveryFaceLooksUp()
    {
        var m = RouteModel.Build(Fx.Straight(800f, Kerbed(paint: "black_white")));
        var mb = Whole(m);
        Assert.Equal(RoadSub.CountE1, mb.Triangles.Length);
        Assert.True(mb.Validate(out var err), err);
        foreach (int sub in new[] { RoadSub.KerbPainted, RoadSub.Sidewalk, RoadSub.Drain })
        {
            Assert.True(mb.Triangles[sub].Count > 0, "no triangles in submesh " + sub);
            foreach (var (n, _) in Fx.Tris(mb, sub)) Assert.True(n.Y > 0f, "submesh " + sub + " has a triangle facing down");
        }
        Assert.Empty(mb.Triangles[RoadSub.Kerb]);
    }

    [Fact] public void TheWalkwayIsOneKerbHeightAboveTheShoulderInTheMesh()
    {
        var m = RouteModel.Build(Fx.Straight(400f, Kerbed()));
        var mb = Whole(m);
        float walk = Fx.Tris(mb, RoadSub.Sidewalk).Average(t => t.c.Y), shoulder = Fx.Tris(mb, RoadSub.Shoulder).Where(t => Lateral(t.c) > 0f).Average(t => t.c.Y);
        Assert.InRange(walk - shoulder, 0.105f, 0.125f);            // the mean of a shoulder triangle is half its camber fall (0.0075 m) above the shoulder edge; the walkway is level
    }

    [Fact] public void KerbsAndWalkwaysCarryNoRoadWear()
    {
        var spec = Fx.Straight(600f, Kerbed());
        foreach (var cp in spec.controlPoints) cp.surface = "damaged";
        var mb = Whole(RouteModel.Build(spec));
        var ys = Fx.Tris(mb, RoadSub.Sidewalk).Select(t => t.c.Y).ToArray();
        Assert.True(ys.Max() - ys.Min() < 0.001f, "the walkway rippled with the road wear: " + (ys.Max() - ys.Min()));
        var asphalt = Fx.Tris(mb, RoadSub.Asphalt).Select(t => t.c.Y).ToArray();
        Assert.True(asphalt.Max() - asphalt.Min() > 0.01f, "the damaged carriageway should still be uneven");
    }

    // ---------------------------------------------------------------- medians
    [Fact] public void ThePlanterMedianIsSoilInAPaintedRimAndHasNoBarrier()
    {
        var p = RoadClasses.Make(RoadClasses.Arterial); var cs = RoadCrossSection.Build(p);
        Assert.True(cs.HasMedian);
        Assert.Equal(RoadCrossSection.PlanterHeight, cs.HeightAt(0f), 3);
        Assert.Equal(1.5f + RoadCrossSection.MedianFace + p.innerShoulderWidth, cs.InnerLaneEdge, 3);
        Assert.Equal(cs.InnerLaneEdge + 2 * p.laneWidth, cs.CarriagewayHalfWidth, 3);
        var m = RouteModel.Build(Fx.Straight(800f, p)); var mb = Whole(m);
        Assert.True(mb.Triangles[RoadSub.Planter].Count > 0); Assert.True(mb.Triangles[RoadSub.KerbPainted].Count > 0);
        Assert.Empty(mb.Triangles[RoadSub.Metal]); Assert.Empty(mb.Triangles[RoadSub.Concrete]);   // no jersey barrier, no concrete median
        Assert.True(mb.Validate(out var err), err);
        var planterTop = Fx.Tris(mb, RoadSub.Planter).Select(t => t.c.Y).Max();
        Assert.Equal(10f + RoadCrossSection.PlanterHeight, planterTop, 2);
    }

    [Fact] public void ThePaintedMedianIsALowIslandWithoutABarrier()
    {
        var p = Fx.Profile("pm", lanes: 2, median: 2f); p.medianStyle = "painted"; p.kerbPaint = "black_white";
        var cs = RoadCrossSection.Build(p);
        Assert.Equal(RoadCrossSection.PaintedIslandHeight, cs.HeightAt(0f), 3);
        var mb = Whole(RouteModel.Build(Fx.Straight(400f, p)));
        Assert.True(mb.Triangles[RoadSub.Concrete].Count > 0); Assert.Empty(mb.Triangles[RoadSub.Metal]);
        Assert.True(Fx.Tris(mb, RoadSub.Concrete).Max(t => t.c.Y) < 10f + 0.11f, "an island, not a 0.8 m jersey barrier");
    }

    [Fact] public void TheJerseyMedianStillCarriesItsBarrier()
    {
        var p = Fx.Profile("j", lanes: 2, median: 3f);
        var mb = Whole(RouteModel.Build(Fx.Straight(400f, p)));
        Assert.True(Fx.Tris(mb, RoadSub.Concrete).Max(t => t.c.Y) > 10f + 0.5f, "the jersey profile rises well above the median top");
        Assert.Equal(RoadSub.Count, mb.Triangles.Length);
    }

    // ---------------------------------------------------------------- dropped kerbs
    [Fact] public void TheKerbDropsToPavingWhereASideRoadLeaves()
    {
        var s = Fx.Straight(2000f, Kerbed("main", walk: 2f), Kerbed("side", walk: 1.5f));
        s.junctions = new[] { new JunctionSpec { id = "j", profile = "side", s = 1000f, angleDeg = 90f, length = 200f } };      // a right turn: the mouth is on the -z side
        var m = RouteModel.Build(s); var mb = Whole(m);
        float half = RoadCrossSection.Build(s.profiles[1]).FormationHalfWidth;
        bool InMouth(V3 c) => Math.Abs(c.X - 1000f) < half - 4.5f;
        int rightKerbInMouth = 0, leftKerbInMouth = 0, rightKerbElsewhere = 0, pavingInMouth = 0;
        foreach (int sub in new[] { RoadSub.Kerb, RoadSub.Sidewalk, RoadSub.Drain })
            foreach (var (_, c) in Fx.Tris(mb, sub))
            {
                if (Lateral(c) > 0f && InMouth(c)) rightKerbInMouth++;
                if (Lateral(c) < 0f && InMouth(c)) leftKerbInMouth++;
                if (Lateral(c) > 0f && Math.Abs(c.X - 1000f) > half + 8f) rightKerbElsewhere++;
            }
        foreach (var (_, c) in Fx.Tris(mb, RoadSub.Shoulder)) if (Lateral(c) > 4.05f && InMouth(c)) pavingInMouth++;
        Assert.Equal(0, rightKerbInMouth); Assert.True(leftKerbInMouth > 0, "the other side keeps its kerb"); Assert.True(rightKerbElsewhere > 0, "the kerb returns beyond the mouth");
        Assert.True(pavingInMouth > 0, "the dropped kerb is paving");
        Assert.True(mb.Validate(out var err), err);
    }

    [Fact] public void DroppedKerbHeightsMeetThePavedEdgeAtTheMouth()
    {
        var s = Fx.Straight(2000f, Kerbed("main"), Kerbed("side"));
        s.junctions = new[] { new JunctionSpec { id = "j", profile = "side", s = 1000f, angleDeg = 90f, length = 200f } };
        var m = RouteModel.Build(s); var mb = Whole(m);
        float ys = 10f + Y(RoadCrossSection.Build(s.profiles[0]), 4.0f);
        var inside = Fx.Tris(mb, RoadSub.Shoulder).Where(t => Lateral(t.c) > 4.05f && Math.Abs(t.c.X - 1000f) < 3f).Select(t => t.c.Y).ToArray();
        Assert.NotEmpty(inside);
        Assert.All(inside, y => Assert.InRange(y, ys - 0.03f, ys + 0.001f));
    }

    // ---------------------------------------------------------------- symbols
    static RouteSpec WithSymbols(RoadProfileSpec p, params MarkingSymbolSpec[] sy) { var s = Fx.Straight(1000f, p); s.symbols = sy; return s; }
    static MarkingSymbolSpec Sym(string kind, float s = 500f, string side = "right", int lane = 0, float length = 0f, int count = 1, bool yellow = false) =>
        new MarkingSymbolSpec { id = kind + s + side + lane, kind = kind, s = s, side = side, lane = lane, length = length, count = count, yellow = yellow };
    static float WhiteArea(RouteSpec spec) { var m = RouteModel.Build(spec); return Fx.Area(Whole(m), RoadSub.MarkWhite); }

    [Fact] public void AZebraCrossingFillsTheCarriagewayWithBars()
    {
        var plain = WhiteArea(Fx.Straight(1000f, Fx.Profile(lanes: 2)));
        var withZ = WhiteArea(WithSymbols(Fx.Profile(lanes: 2), Sym("zebra", side: "both")));
        Assert.InRange(withZ - plain, 14 * 0.5f * 3f * 0.98f, 14 * 0.5f * 3f * 1.02f);       // 14 bars of 0.5 x 3 m across 14 m of carriageway
    }

    [Fact] public void AStopLineCoversOnlyTheLanesOfItsSide()
    {
        var plain = WhiteArea(Fx.Straight(1000f, Fx.Profile(lanes: 2)));
        var one = WhiteArea(WithSymbols(Fx.Profile(lanes: 2), Sym("stop_line", side: "right")));
        var lane2 = WhiteArea(WithSymbols(Fx.Profile(lanes: 2), Sym("stop_line", side: "right", lane: 2)));
        Assert.InRange(one - plain, (7f - 0.2f) * 0.5f * 0.97f, (7f - 0.2f) * 0.5f * 1.03f);
        Assert.InRange(lane2 - plain, (3.5f - 0.2f) * 0.5f * 0.97f, (3.5f - 0.2f) * 0.5f * 1.03f);
    }

    [Fact] public void ArrowsPointTheWayTheirLaneFlows()
    {
        var p = Fx.Profile(lanes: 2); var m = RouteModel.Build(WithSymbols(p, Sym("arrow_straight", 500f, "right", 1), Sym("arrow_straight", 500f, "left", 1)));
        var right = RoadGeometry.SymbolPieces(m.Main, m.Main.Symbols[0]).SelectMany(x => x).ToArray();
        var left = RoadGeometry.SymbolPieces(m.Main, m.Main.Symbols[1]).SelectMany(x => x).ToArray();
        Assert.Equal(500f, right.Min(v => v.X), 2); Assert.Equal(500f + RoadGeometry.ArrowLength, right.Max(v => v.X), 2);          // east-bound traffic: towards increasing s
        Assert.Equal(500f - RoadGeometry.ArrowLength, left.Min(v => v.X), 2); Assert.Equal(500f, left.Max(v => v.X), 2);           // the other carriageway flows the other way
        Assert.All(right, v => Assert.InRange(v.Y, 1.0f, 2.5f));                                    // lane 1 of a 3.5 m lane: centre 1.75 m, arrow head 1 m wide
        Assert.All(left, v => Assert.InRange(v.Y, -2.5f, -1.0f));
    }

    [Theory] [InlineData("right", "arrow_right", 1f)] [InlineData("right", "arrow_left", -1f)] [InlineData("left", "arrow_right", -1f)] [InlineData("left", "arrow_left", 1f)]
    public void TurnArrowsLeanToTheDriversSide(string side, string kind, float expectedLateralSign)
    {
        var m = RouteModel.Build(WithSymbols(Fx.Profile(lanes: 1), Sym(kind, 500f, side, 1)));
        var pts = RoadGeometry.SymbolPieces(m.Main, m.Main.Symbols[0]).SelectMany(x => x).ToArray();
        float centre = (side == "right" ? 1f : -1f) * 1.75f;
        float far = pts.OrderByDescending(v => Math.Abs(v.Y - centre)).First().Y - centre;
        Assert.True(Math.Sign(far) == (int)expectedLateralSign && Math.Abs(far) > 0.6f, kind + " on the " + side + " carriageway should lean " + expectedLateralSign + " but reaches " + far);
    }

    [Fact] public void YieldTrianglesPointAtTheApproachingTraffic()
    {
        var m = RouteModel.Build(WithSymbols(Fx.Profile(lanes: 2), Sym("yield", 500f, "right", 0)));
        var pieces = RoadGeometry.SymbolPieces(m.Main, m.Main.Symbols[0]);
        Assert.InRange(pieces.Count, 6, 12);
        Assert.All(pieces, tri => { Assert.Equal(3, tri.Length); Assert.True(tri[1].X > tri[0].X && tri[2].X > tri[0].X, "apex first, base after it in the flow direction"); });
    }

    [Fact] public void BaysAreLinesAlongTheShoulder()
    {
        var p = Fx.Profile(lanes: 1, shoulder: 2.5f);
        var m = RouteModel.Build(WithSymbols(p, Sym("bay", 500f, "right", 0, length: 3f, count: 4)));
        var pieces = RoadGeometry.SymbolPieces(m.Main, m.Main.Symbols[0]);
        Assert.Equal(5, pieces.Count);                                                          // count + 1 separators
        Assert.All(pieces, q => { Assert.InRange(q.Min(v => v.Y), 3.49f, 3.51f); Assert.InRange(q.Max(v => v.Y), 5.99f, 6.01f); });
    }

    [Fact] public void SymbolsAreBuiltOnceByTheChunkThatHoldsTheirAnchor()
    {
        var m = RouteModel.Build(WithSymbols(Fx.Profile(lanes: 2), Sym("zebra", 500f, "both")));
        var west = RoadGeometry.BuildChunk(m.Main, -100f, -100f, 500f, 100f, new V3(0, 0, 0)); var east = RoadGeometry.BuildChunk(m.Main, 500f, -100f, 1100f, 100f, new V3(0, 0, 0));
        var bare = RouteModel.Build(Fx.Straight(1000f, Fx.Profile(lanes: 2)));
        float b0 = Fx.Area(RoadGeometry.BuildChunk(bare.Main, -100f, -100f, 500f, 100f, new V3(0, 0, 0)), RoadSub.MarkWhite), b1 = Fx.Area(RoadGeometry.BuildChunk(bare.Main, 500f, -100f, 1100f, 100f, new V3(0, 0, 0)), RoadSub.MarkWhite);
        Assert.InRange(Fx.Area(west, RoadSub.MarkWhite) - b0, -0.01f, 0.01f);                  // the anchor is at x = 500, which belongs to the eastern chunk
        Assert.InRange(Fx.Area(east, RoadSub.MarkWhite) - b1, 20f, 22f);
    }

    [Fact] public void SymbolMeshesAreValidAndFaceUp()
    {
        var syms = new[] { Sym("zebra", 100f, "both"), Sym("stop_line", 200f, "right"), Sym("yield", 300f, "right"), Sym("arrow_straight", 400f, "right", 1), Sym("arrow_left", 450f, "right", 2), Sym("arrow_right", 500f, "left", 1), Sym("bay", 600f, "right", 0, 3f, 3) };
        var p = Fx.Profile(lanes: 2, shoulder: 2.5f);
        var mb = Whole(RouteModel.Build(WithSymbols(p, syms)));
        Assert.True(mb.Validate(out var err), err);
        foreach (var (n, _) in Fx.Tris(mb, RoadSub.MarkWhite)) Assert.True(n.Y > 0f, "a marking faces down");
    }

    [Fact] public void YellowSymbolsUseTheYellowPaint()
    {
        var y = RouteModel.Build(WithSymbols(Fx.Profile(lanes: 1, centre: "none"), Sym("stop_line", 500f, "right", 0, yellow: true)));
        Assert.True(Fx.Area(Whole(y), RoadSub.MarkYellow) > 1f);
    }

    [Fact] public void SymbolsOnASideRoadAreBuiltOnThatRoad()
    {
        var s = Fx.Straight(2000f, Fx.Profile(lanes: 1), Fx.Profile("side", lanes: 1));
        s.junctions = new[] { new JunctionSpec { id = "j", profile = "side", s = 1000f, angleDeg = 90f, length = 200f } };
        s.symbols = new[] { new MarkingSymbolSpec { id = "z", kind = "zebra", corridor = "j", s = 60f, side = "both" } };
        var m = RouteModel.Build(s);
        Assert.Empty(m.Main.Symbols); Assert.Single(m.Sides[0].Symbols);
        var bare = Fx.Straight(2000f, Fx.Profile(lanes: 1), Fx.Profile("side", lanes: 1)); bare.junctions = s.junctions;
        float with = Fx.Area(Whole(m, m.Sides[0]), RoadSub.MarkWhite), without = Fx.Area(Whole(RouteModel.Build(bare), RouteModel.Build(bare).Sides[0]), RoadSub.MarkWhite);
        Assert.True(with - without > 6f);
    }

    // ---------------------------------------------------------------- validation
    static List<string> Errors(Action<RouteSpec> edit, params RoadProfileSpec[] profiles)
    {
        var s = Fx.Straight(2000f, profiles.Length > 0 ? profiles : new[] { Fx.Profile(lanes: 2), Fx.Profile("side", lanes: 1) });
        s.junctions = new[] { new JunctionSpec { id = "j", profile = "side", s = 1000f, angleDeg = 90f, length = 200f } };
        edit(s); return RouteSpecValidator.Validate(s);
    }

    [Fact] public void AValidE1RouteHasNoErrors()
    {
        var errs = Errors(s => s.symbols = new[] { Sym("zebra", 500f, "both"), Sym("arrow_left", 700f, "right", 2) }, Kerbed("main", lanes: 2), Kerbed("side"));
        Assert.Empty(errs);
    }

    [Theory]
    [InlineData("kind", "bogus")] [InlineData("side", "up")] [InlineData("corridor", "nowhere")] [InlineData("s", "1")] [InlineData("s", "2500")] [InlineData("lane", "3")] [InlineData("count", "0")]
    public void BrokenSymbolsAreRefused(string field, string value)
    {
        var errs = Errors(s =>
        {
            var y = new MarkingSymbolSpec { id = "y", kind = "zebra", s = 500f, side = "right" };
            switch (field) { case "kind": y.kind = value; break; case "side": y.side = value; break; case "corridor": y.corridor = value; break; case "s": y.s = float.Parse(value); break; case "lane": y.lane = int.Parse(value); y.kind = "stop_line"; break; case "count": y.count = int.Parse(value); break; }
            s.symbols = new[] { y };
        });
        Assert.Contains(errs, e => e.Contains("symbol 'y'"));
    }

    [Fact] public void ArrowsNeedALaneAndBothSidesIsForCrossingsOnly()
    {
        Assert.Contains(Errors(s => s.symbols = new[] { new MarkingSymbolSpec { id = "a", kind = "arrow_straight", s = 500f, lane = 0 } }), e => e.Contains("one lane"));
        Assert.Contains(Errors(s => s.symbols = new[] { new MarkingSymbolSpec { id = "a", kind = "arrow_straight", s = 500f, lane = 1, side = "both" } }), e => e.Contains("'both'"));
    }

    [Fact] public void DuplicateSymbolIdsAreRefused() =>
        Assert.Contains(Errors(s => s.symbols = new[] { Sym("zebra", 500f), Sym("zebra", 500f) }), e => e.Contains("duplicate"));

    [Fact] public void ASymbolOnASideRoadIsCheckedAgainstThatRoad()
    {
        Assert.Empty(Errors(s => s.symbols = new[] { new MarkingSymbolSpec { id = "z", kind = "zebra", corridor = "j", s = 60f, side = "both" } }));
        Assert.Contains(Errors(s => s.symbols = new[] { new MarkingSymbolSpec { id = "z", kind = "zebra", corridor = "j", s = 199f, side = "both" } }), e => e.Contains("inside its corridor"));
        Assert.Contains(Errors(s => s.symbols = new[] { new MarkingSymbolSpec { id = "z", kind = "stop_line", corridor = "j", s = 60f, side = "right", lane = 2 } }), e => e.Contains("does not exist on profile"));
    }

    [Theory]
    [InlineData("kerb", 0.3f)] [InlineData("kerb", 0.03f)] [InlineData("walk", 8f)] [InlineData("drain", 2f)] [InlineData("paint", 0f)] [InlineData("style", 0f)] [InlineData("class", 0f)]
    public void OutOfRangeE1ProfileFieldsAreRefused(string field, float v)
    {
        var p = Kerbed("bad");
        switch (field) { case "kerb": p.kerbHeight = v; break; case "walk": p.sidewalkWidth = v; break; case "drain": p.drainWidth = v; break; case "paint": p.kerbPaint = "stripey"; break; case "style": p.medianStyle = "hedge"; break; case "class": p.roadClass = "motorway"; break; }
        Assert.Contains(Errors(s => { }, p, Fx.Profile("side", lanes: 1)), e => e.Contains("profile 'bad'"));
    }

    [Fact] public void ImpossibleE1CombinationsAreRefused()
    {
        var walkNoKerb = Fx.Profile("bad"); walkNoKerb.sidewalkWidth = 2f;
        Assert.Contains(Errors(s => { }, walkNoKerb, Fx.Profile("side")), e => e.Contains("needs kerbHeight"));
        var kerbRail = Kerbed("bad"); kerbRail.edgeBarrier = "guardrail";
        Assert.Contains(Errors(s => { }, kerbRail, Fx.Profile("side")), e => e.Contains("guardrail"));
        var thin = Fx.Profile("bad", lanes: 2, median: 0.5f); thin.medianStyle = "planter";
        Assert.Contains(Errors(s => { }, thin, Fx.Profile("side")), e => e.Contains("at least 0.9"));
    }

    // ---------------------------------------------------------------- road classes
    [Fact] public void EveryRoadClassBuildsAValidRoad()
    {
        foreach (var cls in RoadClasses.All)
        {
            var p = RoadClasses.Make(cls); var cs = RoadCrossSection.Build(p);
            Assert.Equal(cls, p.roadClass); Assert.True(RoadClasses.IsKnown(cls));
            var spec = Fx.Straight(600f, p);
            Assert.Empty(RouteSpecValidator.Validate(spec));
            var mb = Whole(RouteModel.Build(spec));
            Assert.True(mb.Validate(out var err), cls + ": " + err);
            Assert.True(cs.FormationHalfWidth > 3f && cs.FormationHalfWidth < 40f, cls + " width " + cs.FormationHalfWidth);
        }
        Assert.Throws<ArgumentException>(() => RoadClasses.Make("motorway"));
    }

    [Fact] public void TheClassesDifferInTheWaysThePlanSays()
    {
        var ex = RoadClasses.Make(RoadClasses.Expressway); var ar = RoadClasses.Make(RoadClasses.Arterial); var co = RoadClasses.Make(RoadClasses.Collector);
        var lo = RoadClasses.Make(RoadClasses.Local); var la = RoadClasses.Make(RoadClasses.Lane); var sv = RoadClasses.Make(RoadClasses.Service);
        Assert.Equal(3, ex.lanesPerDirection); Assert.Equal("guardrail", ex.edgeBarrier); Assert.Equal("jersey", ex.medianStyle); Assert.Equal(0f, ex.kerbHeight);
        Assert.Equal(2, ar.lanesPerDirection); Assert.Equal("planter", ar.medianStyle); Assert.Equal("black_white", ar.kerbPaint); Assert.True(ar.sidewalkWidth >= 2.5f);
        Assert.True(co.sidewalkWidth > lo.sidewalkWidth && lo.sidewalkWidth > la.sidewalkWidth);
        Assert.Equal(0f, la.kerbHeight); Assert.True(la.laneWidth < lo.laneWidth && lo.laneWidth < co.laneWidth);
        Assert.True(sv.laneWidth >= 3.75f && sv.sidewalkWidth == 0f && sv.kerbHeight > 0f, "a truck access road: wide lanes, a heavy kerb, no walkway");
        var cat = RoadClasses.Catalogue(); Assert.Equal(RoadClasses.All.Length, cat.Length); Assert.Equal("kano_arterial", cat.First(c => c.roadClass == "arterial").id);
    }

    [Fact] public void ARouteMayMixClassesAndStillBuilds()
    {
        var a = RoadClasses.Make(RoadClasses.Arterial, "a"); var c = RoadClasses.Make(RoadClasses.Collector, "c");
        var s = Fx.Straight(2000f, a, c);
        s.controlPoints = new[]
        {
            new ControlPoint { id = "p0", x = 0, z = 0, elevation = 10, zone = "urban", surface = "good", profile = "a" },
            new ControlPoint { id = "p1", x = 1000, z = 0, elevation = 10, zone = "urban", surface = "good", profile = "c" },
            new ControlPoint { id = "p2", x = 2000, z = 0, elevation = 10, zone = "urban", surface = "good", profile = "c" },
        };
        var m = RouteModel.Build(s); var mb = Whole(m);
        Assert.True(mb.Validate(out var err), err);
        Assert.True(mb.Triangles[RoadSub.Planter].Count > 0 && mb.Triangles[RoadSub.Sidewalk].Count > 0);
    }

    [Fact] public void GroundHeightFollowsTheWalkway()
    {
        var p = Kerbed(); var spec = Fx.Straight(600f, p); var m = RouteModel.Build(spec); var cs = RoadCrossSection.Build(p);
        float walkX = cs.PavedHalfWidth + 0.3f + RoadCrossSection.KerbFace + RoadCrossSection.KerbTopWidth + 1f;
        Assert.Equal(10f + cs.HeightAt(walkX), m.GroundHeight(300f, -walkX), 3);
        Assert.True(m.GroundHeight(300f, -walkX) > m.GroundHeight(300f, -(cs.PavedHalfWidth - 0.3f)) + 0.1f, "the walkway is higher than the carriageway");
    }
}
