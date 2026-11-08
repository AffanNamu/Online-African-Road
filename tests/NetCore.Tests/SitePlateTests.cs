using System;
using System.Collections.Generic;
using System.Linq;
using ARO.NetCore;
using Xunit;

/// <summary>E2 (docs/ROAD_ENGINE_E2.md): site plates, plate props, props measured from a side road, and data-driven text faces. The road runs due east; a side road leaves to the right at s = 1000 m (heading south).</summary>
public class SitePlateTests
{
    static SitePlateSpec Yard(string id = "yard", string corridor = "j", float s = 200f, float forward = 30f, float w = 40f, float d = 60f) =>
        new SitePlateSpec { id = id, corridor = corridor, s = s, forward = forward, widthM = w, depthM = d, surface = "concrete", kerb = "plain", entrances = new[] { new SiteEntranceSpec { edge = "back", from = -6f, to = 6f } } };

    static RouteSpec Spec(params SitePlateSpec[] sites)
    {
        var s = Fx.Straight(2000f, Fx.Profile(lanes: 1), Fx.Profile("side", lanes: 1));
        s.junctions = new[] { new JunctionSpec { id = "j", profile = "side", s = 1000f, angleDeg = 90f, length = 200f } };
        s.sites = sites; return s;
    }
    static RouteModel Model(params SitePlateSpec[] sites) => RouteModel.Build(Spec(sites));
    static MeshBuffers Whole(SitePlate p) => SiteGeometry.BuildChunk(p, -1e6f, -1e6f, 1e6f, 1e6f, new V3(0, 0, 0));

    // ---------------------------------------------------------------- placement
    [Fact] public void APlateAtTheEndOfAStubFacesAwayFromTheRoadAndSitsOnTheRoadLevel()
    {
        var m = Model(Yard()); var p = m.Sites[0];
        Assert.Equal(1000f, p.Cx, 1); Assert.Equal(-(m.Main.HalfPavedAt(1000f) + 200f + 30f), p.Cz, 1);       // 30 m beyond the stub's end, on its axis, heading south
        Assert.Equal(180f, Math.Abs(p.HeadingDeg), 1);
        Assert.Equal(m.CorridorById("j").Spline.ElevationAt(200f), p.BaseY, 3);                                // level with the end of the stub (which starts at the main road's paved edge, a camber fall below the crown)
        Assert.Equal(RoadSub.Concrete, p.TopSub); Assert.Equal(RoadSub.Kerb, p.KerbSub); Assert.Equal(20f, p.HalfW); Assert.Equal(30f, p.HalfD);
    }

    [Fact] public void LocalAndWorldCoordinatesRoundTripAndTheSignedDistanceIsRight()
    {
        var p = Model(Yard()).Sites[0];
        var w = p.ToWorld(7f, -12f); p.ToLocal(w.X, w.Z, out float u, out float v);
        Assert.Equal(7f, u, 3); Assert.Equal(-12f, v, 3);
        Assert.Equal(-3f, p.SignedDistance(p.ToWorld(17f, 0f).X, p.ToWorld(17f, 0f).Z), 3);                   // 3 m inside the right edge
        var out1 = p.ToWorld(25f, 0f); Assert.Equal(5f, p.SignedDistance(out1.X, out1.Z), 3);
        var corner = p.ToWorld(23f, 34f); Assert.Equal(5f, p.SignedDistance(corner.X, corner.Z), 3);           // 3 m and 4 m beyond the corner: 5 m
        Assert.True(p.Contains(p.Cx, p.Cz) && !p.Contains(out1.X, out1.Z) && p.Contains(out1.X, out1.Z, 5.1f));
        Assert.Equal(p.HalfW, Math.Abs(p.ToWorld(1f, 0f).X - p.ToWorld(0f, 0f).X) < 0.5f ? p.HalfW : p.HalfW);   // (axes are unit vectors)
        Assert.Equal(1f, (float)Math.Sqrt(p.FwdX * p.FwdX + p.FwdZ * p.FwdZ), 4); Assert.Equal(0f, p.FwdX * p.RightX + p.FwdZ * p.RightZ, 4);
    }

    [Fact] public void YawTurnsThePlateToTheRightAndAHeightOffsetRaisesIt()
    {
        var a = Yard("a"); var b = Yard("b"); b.yawDeg = 90f; b.heightOffset = 0.3f; b.forward = 0f; b.lateral = 50f; b.s = 100f; b.corridor = "";   // beside the main road, 50 m to its right
        var m = Model(a, b); var pb = m.Sites[1];
        Assert.Equal(m.Main.Spline.PositionAt(100f).X, pb.Cx, 1);
        Assert.Equal(-50f, pb.Cz, 1);
        Assert.True(Math.Abs(pb.FwdX) < 1e-3f && pb.FwdZ < -0.99f, "turned 90 degrees right of an eastbound road: heading south");
        var sec = m.Main.SectionAt(100f); Assert.Equal(10f + sec.HeightAt(sec.PavedHalfWidth) + 0.3f, pb.BaseY, 3);          // |lateral| beyond the paved edge: the paved-edge height plus the offset
    }

    // ---------------------------------------------------------------- terrain, ground and exclusion
    [Fact] public void TheTerrainIsFlattenedUnderThePlateAndBlendsToTheNaturalGroundAroundIt()
    {
        var spec = Spec(Yard()); spec.terrain.hillAmplitude = 6f; spec.terrain.hillWavelength = 300f;
        var m = RouteModel.Build(spec); var p = m.Sites[0];
        Assert.Equal(p.BaseY - TerrainModel.SinkUnderRoad, m.Terrain.HeightAt(p.Cx, p.Cz), 3);
        var far = p.ToWorld(0f, 30f + 200f);
        Assert.Equal(m.Terrain.NaturalAt(far.X, far.Z), m.Terrain.HeightAt(far.X, far.Z), 2);
        var edge = p.ToWorld(0f, 30.05f); Assert.Equal(p.BaseY + p.KerbHeight, m.Terrain.HeightAt(edge.X, edge.Z), 1);   // outside the edge the ground starts at the top of the kerb
        Assert.Equal(p.BaseY, m.GroundHeight(p.Cx, p.Cz), 3);
        Assert.True(m.Terrain.InsideFormation(p.Cx, p.Cz) && m.Terrain.InsideRoadAlong(p.Cx, p.Cz, -1f));
        Assert.False(m.Terrain.InsideFormation(far.X, far.Z));
    }

    [Fact] public void RoadsterrainOutsideAnyPlateIsUnchanged()
    {
        var with = Model(Yard()); var without = Model();
        foreach (var (x, z) in new[] { (300f, 4f), (1000f, -60f), (1500f, 40f), (100f, -300f) })
            Assert.Equal(without.Terrain.HeightAt(x, z), with.Terrain.HeightAt(x, z), 4);
    }

    // ---------------------------------------------------------------- mesh
    [Fact] public void ThePlateSurfaceIsFlatCompleteAndFacesUp()
    {
        var p = Model(Yard()).Sites[0]; var mb = Whole(p);
        Assert.Equal(RoadSub.CountE1, mb.Triangles.Length); Assert.True(mb.Validate(out var err), err);
        Assert.Equal(40f * 60f, Fx.Area(mb, RoadSub.Concrete), 1);
        foreach (var (n, c) in Fx.Tris(mb, RoadSub.Concrete)) { Assert.True(n.Y > 0f); Assert.Equal(p.BaseY, c.Y, 3); }
    }

    [Fact] public void TheKerbRingStopsAtTheEntranceAndEverywhereElseStandsAboveThePlate()
    {
        var p = Model(Yard()).Sites[0]; var mb = Whole(p);
        var tris = Fx.Tris(mb, RoadSub.Kerb).ToList(); Assert.NotEmpty(tris);
        foreach (var (n, _) in tris) Assert.True(n.Y > 0f);
        Assert.InRange(tris.Max(t => t.c.Y), p.BaseY + 0.11f, p.BaseY + 0.13f);
        // the back edge (v = -30) has a 12 m entrance in the middle: no kerb triangle centred within 5 m of the plate axis on that edge
        int inGap = 0, onBackElsewhere = 0, onFront = 0;
        foreach (var (_, c) in tris)
        {
            p.ToLocal(c.X, c.Z, out float u, out float v);
            if (v < -29f) { if (Math.Abs(u) < 5.5f) inGap++; else onBackElsewhere++; }
            if (v > 29f) onFront++;
        }
        Assert.Equal(0, inGap); Assert.True(onBackElsewhere > 0 && onFront > 0);
    }

    [Fact] public void APaintedKerbUsesItsOwnStripAndNoKerbMeansNoTriangles()
    {
        var a = Yard("a"); a.kerb = "black_white"; var b = Yard("b", forward: 120f); b.kerb = "none"; b.entrances = new SiteEntranceSpec[0];
        var m = Model(a, b);
        var ma = Whole(m.Sites[0]); var mbuf = Whole(m.Sites[1]);
        Assert.True(ma.Triangles[RoadSub.KerbPainted].Count > 0 && ma.Triangles[RoadSub.Kerb].Count == 0);
        Assert.Equal(0, mbuf.Triangles[RoadSub.Kerb].Count + mbuf.Triangles[RoadSub.KerbPainted].Count); Assert.False(m.Sites[1].HasKerb);
    }

    [Fact] public void EachCellAndKerbPieceIsBuiltByExactlyOneChunk()
    {
        var p = Model(Yard()).Sites[0];
        float whole = Fx.Area(Whole(p), RoadSub.Concrete), kerbWhole = Fx.Area(Whole(p), RoadSub.Kerb);
        float a = 0f, k = 0f; int chunks = 0;
        for (float x = 700f; x < 1300f; x += 50f) for (float z = -400f; z < 0f; z += 50f)
        {
            var mb = SiteGeometry.BuildChunk(p, x, z, x + 50f, z + 50f, new V3(x, 0, z)); if (mb.IsEmpty) continue; chunks++;
            Assert.True(mb.Validate(out var err), err); a += Fx.Area(mb, RoadSub.Concrete); k += Fx.Area(mb, RoadSub.Kerb);
        }
        Assert.True(chunks >= 2, "the test chunks should split the plate"); Assert.Equal(whole, a, 2); Assert.Equal(kerbWhole, k, 2);
    }

    // ---------------------------------------------------------------- marks
    static SitePlate WithMarks(params SiteMarkSpec[] marks) { var y = Yard(); y.marks = marks; return Model(y).Sites[0]; }

    [Fact] public void ABayRowHasASeparatorForEveryBayPlusOne()
    {
        var mk = new SiteMarkSpec { kind = "bay_row", x = -14f, z = -10f, count = 8, width = 3.5f, length = 16f, closed = true };
        var pieces = SiteGeometry.MarkPieces(mk); Assert.Equal(9 + 1, pieces.Count);
        var all = pieces.SelectMany(q => q).ToArray();
        Assert.Equal(-14f - SiteGeometry.LineWidth * 0.5f, all.Min(q => q.X), 3); Assert.Equal(-14f + 8 * 3.5f + SiteGeometry.LineWidth * 0.5f, all.Max(q => q.X), 3);
        Assert.Equal(-10f, all.Min(q => q.Y), 3); Assert.Equal(6f, all.Max(q => q.Y), 3);
        var p = WithMarks(mk); var mb = Whole(p); Assert.InRange(Fx.Area(mb, RoadSub.MarkWhite), 9 * 16f * 0.12f + 0.5f, 9 * 16f * 0.12f + 4f);
    }

    [Fact] public void MarksTurnAboutTheirAnchor()
    {
        var line = new SiteMarkSpec { kind = "line", x = 0f, z = 0f, length = 10f, width = 0.2f, angleDeg = 90f };       // 90 degrees: along +u
        var q = SiteGeometry.MarkPieces(line).SelectMany(x => x).ToArray();
        Assert.Equal(10f, q.Max(v => v.X), 3); Assert.Equal(0.1f, q.Max(v => v.Y), 3); Assert.Equal(-0.1f, q.Min(v => v.Y), 3);
    }

    [Fact] public void AHatchFillsItsRectangleWithStripesThatNeverLeaveIt()
    {
        var mk = new SiteMarkSpec { kind = "hatch", x = 5f, z = 8f, width = 8f, length = 6f };
        var pieces = SiteGeometry.MarkPieces(mk); Assert.InRange(pieces.Count, 8, 20);
        foreach (var v in pieces.SelectMany(x => x)) { Assert.InRange(v.X, 1f - 1e-3f, 9f + 1e-3f); Assert.InRange(v.Y, 5f - 1e-3f, 11f + 1e-3f); }
        float area = SiteGeometry.MarkPieces(mk).Sum(pc => { float a = 0f; for (int i = 0; i < pc.Length; i++) { var s0 = pc[i]; var s1 = pc[(i + 1) % pc.Length]; a += s0.X * s1.Y - s1.X * s0.Y; } return Math.Abs(a) * 0.5f; });
        Assert.InRange(area / (8f * 6f), 0.24f, 0.34f);                                                                // 0.35 m bars every 1.2 m: 29 percent of the rectangle
    }

    [Fact] public void PlateArrowsPointTheWayTheMarkIsTurned()
    {
        var mk = new SiteMarkSpec { kind = "arrow_straight", x = 0f, z = -5f, angleDeg = 0f };
        var pts = SiteGeometry.MarkPieces(mk).SelectMany(x => x).ToArray();
        Assert.Equal(-5f + RoadGeometry.ArrowLength, pts.Max(v => v.Y), 3); Assert.Equal(-5f, pts.Min(v => v.Y), 3);
        var right = new SiteMarkSpec { kind = "arrow_right", x = 0f, z = 0f }; Assert.True(SiteGeometry.MarkPieces(right).SelectMany(x => x).Max(v => v.X) > 0.8f, "a right arrow leans to +u");
        var left = new SiteMarkSpec { kind = "arrow_left", x = 0f, z = 0f }; Assert.True(SiteGeometry.MarkPieces(left).SelectMany(x => x).Min(v => v.X) < -0.8f);
    }

    [Fact] public void MarksOnlyBelongToTheChunkThatHoldsTheirAnchorAndAllFaceUp()
    {
        var p = WithMarks(new SiteMarkSpec { kind = "line", x = 0f, z = 0f, length = 5f }, new SiteMarkSpec { kind = "arrow_left", x = 5f, z = 5f }, new SiteMarkSpec { kind = "hatch", x = -8f, z = 10f, width = 6f, length = 6f, yellow = true });
        var mb = Whole(p); Assert.True(mb.Validate(out var err), err);
        foreach (var (n, _) in Fx.Tris(mb, RoadSub.MarkWhite)) Assert.True(n.Y > 0f);
        Assert.True(Fx.Area(mb, RoadSub.MarkYellow) > 3f);
        var anchor = p.ToWorld(0f, 0f); var elsewhere = SiteGeometry.BuildChunk(p, anchor.X + 100f, anchor.Z + 100f, anchor.X + 150f, anchor.Z + 150f, new V3(0, 0, 0));
        Assert.Equal(0f, Fx.Area(elsewhere, RoadSub.MarkWhite));
    }

    // ---------------------------------------------------------------- props
    [Fact] public void PlateAndSideRoadPropsAreFurnishedInTheirOwnChunkEvenFarFromTheMainRoad()
    {
        var y = Yard(); y.props = new[] { new SitePropSpec { id = "w1", prop = "warehouse", x = -10f, z = 5f, yawDeg = 90f }, new SitePropSpec { id = "l1", prop = "street_light", x = 18f, z = -28f, scale = 1f, text = "T" } };
        var spec = Spec(y); spec.props = new[] { new PropPlacementSpec { id = "sign_j", prop = "road_sign", text = "Yard", corridor = "j", s = 100f, lateral = 6f } };
        var m = RouteModel.Build(spec); var p = m.Sites[0];
        var wh = p.ToWorld(-10f, 5f);
        var inst = PropScatter.PlaceChunk(m, wh.X - 50f, wh.Z - 50f, wh.X + 50f, wh.Z + 50f);        // this box is 200+ m from the main road
        var w = inst.Single(i => i.Origin == "yard/w1");
        Assert.Equal(wh.X, w.Pos.X, 3); Assert.Equal(p.BaseY, w.Pos.Y, 3); Assert.Equal(p.HeadingDeg + 90f, w.YawDeg, 2);
        var sign = PropScatter.PlaceChunk(m, 900f, -250f, 1100f, -50f).Single(i => i.Origin == "sign_j");
        Assert.Equal("Yard", sign.Text);         var sc = m.CorridorById("j"); sc.Spline.FrameAt(100f, out var pos, out _, out var right); Assert.Equal(pos.X + right.X * 6f, sign.Pos.X, 3); Assert.Equal(pos.Z + right.Z * 6f, sign.Pos.Z, 3);
        // each prop appears in exactly one chunk of a tiling
        int count = 0;
        for (float x = wh.X - 100f; x < wh.X + 100f; x += 40f) for (float z = wh.Z - 100f; z < wh.Z + 100f; z += 40f) count += PropScatter.PlaceChunk(m, x, z, x + 40f, z + 40f).Count(i => i.Origin == "yard/w1");
        Assert.Equal(1, count);
    }

    // ---------------------------------------------------------------- the road beside a plate
    [Fact] public void ARoadsKerbDropsAlongAPlateBesideIt()
    {
        var kerbed = Fx.Profile("main", lanes: 1, shoulder: 0.5f); kerbed.kerbHeight = 0.12f; kerbed.sidewalkWidth = 2f; kerbed.drainWidth = 0.3f;
        var s = Fx.Straight(2000f, kerbed);
        var y = new SitePlateSpec { id = "side", corridor = "", s = 700f, lateral = 30f, widthM = 40f, depthM = 30f, surface = "asphalt", kerb = "none" };
        s.sites = new[] { y };
        var m = RouteModel.Build(s);
        Assert.True(m.Main.KerbDropped(700f, 1) && !m.Main.KerbDropped(700f, -1) && !m.Main.KerbDropped(1200f, 1));
        var plain = RouteModel.Build(Fx.Straight(2000f, kerbed));
        var mbDrop = Fx.WholeRoad(m.Main); var mbKerb = Fx.WholeRoad(plain.Main);
        Assert.True(Fx.Area(mbDrop, RoadSub.Sidewalk) < Fx.Area(mbKerb, RoadSub.Sidewalk) - 20f, "the walkway is paving along the plate");
    }

    // ---------------------------------------------------------------- validation
    static List<string> Errors(Action<SitePlateSpec> edit, bool addKnown = true)
    {
        var y = Yard(); y.marks = new[] { new SiteMarkSpec { kind = "line", x = 0, z = 0, length = 3f } }; y.props = new[] { new SitePropSpec { id = "p", prop = "warehouse", x = 0, z = 0 } };
        edit(y); var spec = Spec(y); return RouteSpecValidator.Validate(spec, addKnown ? new HashSet<string>(Fx.LoadCatalog().props.Select(p => p.id)) : null);
    }

    [Fact] public void AValidPlateHasNoErrors() => Assert.Empty(Errors(y => { }));

    [Theory]
    [InlineData("corridor")] [InlineData("s")] [InlineData("size")] [InlineData("yaw")] [InlineData("surface")] [InlineData("kerb")] [InlineData("kerbheight")] [InlineData("offset")] [InlineData("entrance")] [InlineData("narrow")]
    [InlineData("mark")] [InlineData("markoutside")] [InlineData("prop")] [InlineData("propoutside")] [InlineData("lateral")]
    public void BrokenPlatesAreRefused(string what)
    {
        var errs = Errors(y =>
        {
            switch (what)
            {
                case "corridor": y.corridor = "nowhere"; break; case "s": y.s = 999f; break; case "size": y.widthM = 3f; break; case "yaw": y.yawDeg = 270f; break;
                case "surface": y.surface = "lava"; break; case "kerb": y.kerb = "gold"; break; case "kerbheight": y.kerbHeight = 0.5f; break; case "offset": y.heightOffset = 3f; break;
                case "entrance": y.entrances = new[] { new SiteEntranceSpec { edge = "top" } }; break; case "narrow": y.entrances = new[] { new SiteEntranceSpec { edge = "front", from = 0f, to = 1f } }; break;
                case "mark": y.marks = new[] { new SiteMarkSpec { kind = "graffiti" } }; break; case "markoutside": y.marks = new[] { new SiteMarkSpec { kind = "line", x = 99f } }; break;
                case "prop": y.props[0].prop = "unobtainium"; break; case "propoutside": y.props[0].x = 99f; break; case "lateral": y.lateral = 900f; break;
            }
        });
        Assert.Contains(errs, e => e.Contains("site 'yard'"));
    }

    [Fact] public void PlatesThatOverlapEachOtherOrLieOnARoadAreRefusedWhenTheModelIsBuilt()
    {
        var ex1 = Assert.Throws<ArgumentException>(() => Model(Yard("a"), Yard("b", forward: 40f)));
        Assert.Contains("overlaps", ex1.Message);
        var onRoad = Yard("r"); onRoad.corridor = ""; onRoad.s = 500f; onRoad.lateral = 0f; onRoad.forward = 0f;       // dead on the main road
        var ex2 = Assert.Throws<ArgumentException>(() => Model(onRoad)); Assert.Contains("lies on road", ex2.Message);
        var okay = Model(Yard("a"), Yard("b", forward: 100f)); Assert.Equal(2, okay.Sites.Count);
    }

    [Fact] public void ASideRoadPropMustNameARealCorridor()
    {
        var spec = Spec(); spec.props = new[] { new PropPlacementSpec { id = "x", prop = "road_sign", corridor = "ghost", s = 10f } };
        Assert.Contains(RouteSpecValidator.Validate(spec), e => e.Contains("neither the main road nor a junction"));
        spec.props[0].corridor = "j"; spec.props[0].s = 500f; Assert.Contains(RouteSpecValidator.Validate(spec), e => e.Contains("outside its corridor"));
    }

    // ---------------------------------------------------------------- text faces
    [Fact] public void TheFourBuiltInSignFacesKeepTheirOldNumbers()
    {
        var cat = Fx.LoadCatalog(); PropDef D(string id) => cat.props.Single(p => p.id == id);
        Assert.True(PropLabelLayout.Resolve(D("road_sign"), out var f)); var rs = D("road_sign");
        Assert.Equal(rs.heightM - 0.6f, f.Local.Y, 4); Assert.Equal(0.08f + rs.depthM * 0.5f + 0.02f, f.Local.Z, 4); Assert.Equal(rs.widthM * 0.92f, f.Width, 4); Assert.Equal(1.25f, f.Height, 4);
        Assert.True(PropLabelLayout.Resolve(D("billboard"), out f)); var b = D("billboard"); Assert.Equal(b.widthM * 0.9f, f.Width, 4); Assert.Equal(3.6f, f.Height, 4); Assert.Equal(b.heightM - 2.1f, f.Local.Y, 4);
        Assert.True(PropLabelLayout.Resolve(D("fuel_station"), out f)); var fs = D("fuel_station"); Assert.Equal(fs.widthM * 0.55f, f.Width, 4); Assert.Equal(5.5f, f.Local.Y, 4);
        Assert.True(PropLabelLayout.Resolve(D("bus_stop"), out f)); Assert.Equal(0.14f, f.Height, 4);
        Assert.False(PropLabelLayout.Resolve(D("kiosk"), out _)); Assert.False(PropLabelLayout.Resolve(null, out _));
    }

    [Fact] public void AnyCataloguedPropCanDeclareATextFace()
    {
        var kiosk = Fx.Clone(Fx.LoadCatalog().props.Single(p => p.id == "kiosk"));
        kiosk.label = new LabelSpec { x = 0.1f, y = 2f, z = 1.2f, width = 1.5f, height = 0.4f, yawDeg = 180f, colour = "yellow", fontMax = 40 };
        Assert.True(kiosk.HasLabel); Assert.True(PropLabelLayout.Resolve(kiosk, out var f));
        Assert.Equal(1.5f, f.Width); Assert.Equal(180f, f.YawDeg); Assert.Equal("yellow", f.Colour); Assert.Equal(40, f.FontMax); Assert.Equal(2f, f.Local.Y);
        var cat = Fx.Clone(Fx.LoadCatalog()); var k = cat.props.Single(p => p.id == "kiosk"); k.label = kiosk.label;
        Assert.Empty(PropCatalogValidator.Validate(cat));
        k.label.colour = "pink"; Assert.Contains(PropCatalogValidator.Validate(cat), e => e.Contains("label") && e.Contains("colour"));
        k.label.colour = "white"; k.label.width = 99f; Assert.Contains(PropCatalogValidator.Validate(cat), e => e.Contains("label"));
    }
}
