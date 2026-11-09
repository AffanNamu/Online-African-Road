using System;
using System.Collections.Generic;
using System.Linq;
using ARO.NetCore;
using Xunit;

/// <summary>
/// E5 (docs/ROAD_ENGINE_E5.md): the ring corridor, its island, legs and splitters, the network checks, and the ground under a flyover. Everything here builds on a flat test road so the numbers are exact.
/// The golden master (three tests) proves that no route without rings, extra roads or decks changed.
/// </summary>
public class RoadE5Tests
{
    // Main road: straight east from x = 0 to 600 at 10 m. Ring profile: 1 lane per direction (two circulating lanes of 3.5 m), 1.5 m shoulders, 1 m verge: formation 6 m, paved 5 m.
    static RoadProfileSpec RingProfile(string id = "ring") { var p = Fx.Profile(id, lanes: 1, centre: "dashed"); p.centreLineColor = "white"; return p; }

    public static RouteSpec Base(Action<RouteSpec> edit = null)
    {
        var s = Fx.Straight(600f, Fx.Profile("p", lanes: 1), RingProfile());
        s.schemaVersion = 3;
        s.rings = new[]
        {
            new RingSpec { id = "r1", name = "Test ring", profile = "ring", attachTo = "test-straight", attachEnd = "end", radius = 14f, splitterLength = 12f, splitterWidth = 3f,
                legs = new[] { new RingLegSpec { id = "r1_n", profile = "p", angleDeg = 90f, length = 150f }, new RingLegSpec { id = "r1_s", profile = "p", angleDeg = 270f, length = 150f }, new RingLegSpec { id = "r1_e", profile = "p", angleDeg = 0f, length = 150f } } }
        };
        edit?.Invoke(s); return s;
    }
    static List<string> Errors(RouteSpec s) => RouteSpecValidator.Validate(s);
    static RouteModel Model(Action<RouteSpec> edit = null) => RouteModel.Build(Base(edit));

    // ---------------------------------------------------------------- the circle
    [Fact] public void ACircleSplineClosesAndRunsCounterClockwiseWithItsRightSideOutwards()
    {
        var c = RoadSpline.Circle(100f, -50f, 20f, 7f);
        Assert.Equal(2f * Math.PI * 20f, c.Length, 0);
        var a = c.PositionAt(0f); var b = c.PositionAt(c.Length);
        Assert.Equal(a.X, b.X, 3); Assert.Equal(a.Z, b.Z, 3);
        c.FrameAt(c.Length * 0.25f, out var p, out var fwd, out var right);               // a quarter round: at the top (north) of the circle, heading west
        Assert.Equal(100f, p.X, 1); Assert.Equal(-30f, p.Z, 1);
        Assert.True(fwd.X < -0.99f);                                                       // counter-clockwise: east -> north -> west
        Assert.True(right.Z > 0.99f);                                                      // right of a westward heading, seen from above with z north, is north: outwards
        Assert.Equal(7f, p.Y, 3);
    }

    [Fact] public void ACircleStaysWithinThreeCentimetresOfTheTrueRadiusAtTheRingStations()
    {
        var m = Model(); var ring = m.Rings[0].Corridor;
        float worst = 0f;
        for (int k = 0; k + 1 < ring.RingS.Length; k++)
        {
            float mx = (ring.RingX[k] + ring.RingX[k + 1]) * 0.5f - m.Rings[0].Cx, mz = (ring.RingZ[k] + ring.RingZ[k + 1]) * 0.5f - m.Rings[0].Cz;
            worst = Math.Max(worst, Math.Abs(m.Rings[0].CentreRadius - (float)Math.Sqrt(mx * mx + mz * mz)));       // the chord's middle sags towards the centre
        }
        Assert.True(worst < 0.03f, "sag " + worst);
        Assert.True(ring.RingS.Length > 80);
    }

    // ---------------------------------------------------------------- placement and legs
    [Fact] public void ARingAttachedToTheEndOfTheMainRoadMeetsItsPavedEdgeAtTheSameHeight()
    {
        var m = Model(); var r = m.Rings[0];
        var end = m.Main.Spline.PositionAt(m.Main.Length);
        Assert.Equal(600f + r.OuterPavedRadius, r.Cx, 2); Assert.Equal(0f, r.Cz, 2);
        float edge = (float)Math.Sqrt((end.X - r.Cx) * (end.X - r.Cx) + (end.Z - r.Cz) * (end.Z - r.Cz));
        Assert.Equal(r.OuterPavedRadius, edge, 2);
        Assert.Equal(end.Y, r.Corridor.SurfaceY(r.AngleToS((float)Math.PI), r.Section.PavedHalfWidth), 2);
        Assert.Equal(4, r.Legs.Count);                                                      // the main road and the three generated legs
        Assert.Equal(20f, r.CentreRadius, 3); Assert.Equal(25f, r.OuterPavedRadius, 3);     // island 14 + formation 6, + paved 5
    }

    [Fact] public void GeneratedLegsLeaveTheRingRadiallyFromThePavedEdge()
    {
        var m = Model(); var r = m.Rings[0];
        foreach (var id in new[] { "r1_n", "r1_s", "r1_e" })
        {
            var c = m.CorridorById(id); var leg = r.Legs.Single(l => l.CorridorId == id);
            var p0 = c.Spline.PositionAt(0f);
            float d = (float)Math.Sqrt((p0.X - r.Cx) * (p0.X - r.Cx) + (p0.Z - r.Cz) * (p0.Z - r.Cz));
            Assert.Equal(r.OuterPavedRadius, d, 2);
            Assert.Equal((float)Math.Cos(leg.Angle), (p0.X - r.Cx) / d, 2); Assert.Equal((float)Math.Sin(leg.Angle), (p0.Z - r.Cz) / d, 2);
            Assert.True(c.E5 && c.NoStopLine);
        }
        Assert.Equal(150f, m.CorridorById("r1_n").Length, 0);
    }

    [Fact] public void TheRingsStationZeroLiesInTheWidestGapBetweenLegs()
    {
        var m = Model(); var r = m.Rings[0];
        // legs at 0, 90, 180 (the main road) and 270 degrees are evenly spread: the seam sits at a 45-degree offset, never at a mouth
        foreach (var l in r.Legs) { float d = Math.Min(l.RingS, r.Corridor.Length - l.RingS); Assert.True(d > r.Corridor.Length / 16f, l.CorridorId + " at " + l.RingS); }
    }

    [Fact] public void TheKerbIsDroppedOnTheOuterSideAtEveryMouthAndNowhereElse()
    {
        var m = Model(p => { foreach (var pr in p.profiles) { pr.kerbHeight = 0.10f; pr.sidewalkWidth = 1.5f; } }); var r = m.Rings[0]; var ring = r.Corridor;
        foreach (var l in r.Legs) { Assert.True(ring.KerbDropped(l.RingS, 1), l.CorridorId); Assert.False(ring.KerbDropped(l.RingS, -1), "the island side is never dropped"); }
        float between = r.Legs.OrderBy(l => l.RingS).Select(l => l.RingS).ToList().Zip(r.Legs.OrderBy(l => l.RingS).Skip(1).Select(l => l.RingS), (a, b) => (a + b) * 0.5f).First();
        Assert.False(ring.KerbDropped(between, 1));
    }

    // ---------------------------------------------------------------- island
    [Fact] public void TheIslandIsASolidDiscAtTheKerbTopAndTheGroundAgreesWithIt()
    {
        var m = Model(); var r = m.Rings[0]; var isl = m.Islands[0];
        Assert.Same(r.Island, isl);
        Assert.Equal(14f, isl.Radius, 3);
        Assert.Equal(isl.TopY, m.GroundHeight(r.Cx, r.Cz), 3);
        Assert.Equal(isl.TopY - TerrainModel.SinkUnderRoad, m.Terrain.HeightAt(r.Cx, r.Cz), 3);                       // the terrain is sunk under the island like under a road
        Assert.True(m.Terrain.InsideFormation(r.Cx, r.Cz));                                                          // roadside props keep off it
        Assert.True(m.Terrain.InsideRoadAlong(r.Cx, r.Cz, -1f));                                                     // the terrain tile is cut away under it (the island mesh covers it)
        Assert.False(m.Terrain.InsideRoadAlong(r.Cx + 80f, r.Cz + 80f));
        // and the ring's kerb meets the island: the island top is the height of the ring's formation edge
        Assert.Equal(r.Corridor.SurfaceY(0f, -r.Section.FormationHalfWidth), isl.TopY, 2);
    }

    [Fact] public void TheIslandMeshCoversTheWholeDiscOnceAndFacesUp()
    {
        var m = Model(p => p.rings[0].apron = 3f); var r = m.Rings[0];
        float w = 100f; var org = new V3(r.Cx - w, 0f, r.Cz - w);
        var whole = RingGeometry.BuildChunk(r, r.Cx - w, r.Cz - w, r.Cx + w, r.Cz + w, org, id => m.CorridorById(id));
        // split into four chunks: the union must have the same area as the whole
        float Area(MeshBuffers mb, params int[] subs) { float a = 0f; foreach (int sub in subs) foreach (var (n, _) in Fx.Tris(mb, sub)) a += n.Length * 0.5f; return a; }
        float core = Area(whole, RoadSub.Planter);
        float disc = (float)(Math.PI * 11f * 11f);
        Assert.True(core < disc && core > disc * 0.99f, core + " vs " + disc);                                        // the planted core: radius 14 - apron 3 (a polygon of chords: under 1 percent short)
        var parts = new[] { (r.Cx - w, r.Cz - w, r.Cx, r.Cz), (r.Cx, r.Cz - w, r.Cx + w, r.Cz), (r.Cx - w, r.Cz, r.Cx, r.Cz + w), (r.Cx, r.Cz, r.Cx + w, r.Cz + w) };
        float sum = 0f; foreach (var q in parts) sum += Area(RingGeometry.BuildChunk(r, q.Item1, q.Item2, q.Item3, q.Item4, org, id => m.CorridorById(id)), RoadSub.Planter);
        Assert.Equal(core, sum, 1);
        foreach (var (n, _) in Fx.Tris(whole, RoadSub.Planter)) Assert.True(n.Y > 0f);
        Assert.True(Area(whole, RoadSub.Sidewalk) > (float)(Math.PI * (14f * 14f - 11f * 11f)) * 0.98f);                // the apron annulus (plus the splitters' tops)
    }

    [Fact] public void IslandPropsStandOnTheIslandTop()
    {
        var m = Model(p => p.rings[0].props = new[] { new SitePropSpec { id = "tree", prop = "tree_acacia", x = 3f, z = -2f, scale = 1f } }); var isl = m.Islands[0];
        var got = isl.Props(isl.Cx - 20f, isl.Cz - 20f, isl.Cx + 20f, isl.Cz + 20f).ToList();
        Assert.Single(got); Assert.Equal(isl.TopY, got[0].Pos.Y, 3); Assert.Equal(isl.Cx + 3f, got[0].Pos.X, 3);
        Assert.Contains(Errors(Base(p => p.rings[0].props = new[] { new SitePropSpec { id = "far", prop = "tree_acacia", x = 30f, z = 0f } })), e => e.Contains("outside the island"));
    }

    // ---------------------------------------------------------------- the mesh of the ring and splitters
    [Fact] public void TheRingMeshFacesUpHasNoGapAtTheSeamAndMatchesTheAnnulusArea()
    {
        var m = Model(); var r = m.Rings[0];
        float w = 100f; var org = new V3(r.Cx - w, 0f, r.Cz - w);
        var mb = RoadGeometry.BuildChunk(r.Corridor, r.Cx - w, r.Cz - w, r.Cx + w, r.Cz + w, org, (x, z) => m.Terrain.HeightAt(x, z));
        foreach (var (n, _) in Fx.Tris(mb, RoadSub.Asphalt)) Assert.True(n.Y > 0f, "asphalt triangle faces down");
        float asphalt = 0f; foreach (var (n, _) in Fx.Tris(mb, RoadSub.Asphalt)) asphalt += n.Length * 0.5f;
        float rin = r.CentreRadius - r.Section.CarriagewayHalfWidth, rout = r.CentreRadius + r.Section.CarriagewayHalfWidth;
        float exact = (float)(Math.PI * (rout * rout - rin * rin));
        Assert.True(asphalt < exact && asphalt > exact * 0.997f, asphalt + " vs " + exact);   // the chords (1.5 m) cut off a sliver: under 0.3 percent
    }

    [Fact] public void ASplitterIsAConvexTeardropOnTheLegsCentreLineAndItsKerbFacesUp()
    {
        var m = Model(); var r = m.Rings[0]; var sp = r.Splitters.Single(x => x.CorridorId == "r1_n"); var c = m.CorridorById("r1_n");
        var o = RingGeometry.SplitterOutline(c, sp);
        Assert.Equal(4, o.Length);
        Assert.Equal(1.5f, Math.Abs(o[0].Y), 3);                                          // half of the 3 m width at the ring end
        Assert.True(Math.Abs(o[3].Y) < 0.3f);                                             // a narrow tip
        float w = 80f; var org = new V3(c.Spline.PositionAt(0f).X - w, 0f, c.Spline.PositionAt(0f).Z - w);
        var mb = RingGeometry.BuildChunk(r, org.X, org.Z, org.X + 2 * w, org.Z + 2 * w, org, id => m.CorridorById(id));
        Assert.True(Fx.Tris(mb, RoadSub.KerbPainted).Any());
        foreach (var (n, _) in Fx.Tris(mb, RoadSub.KerbPainted)) Assert.True(n.Y > 0f);
        // the top is raised above the road by the splitter height
        float top = Fx.Tris(mb, RoadSub.Sidewalk).Where(t => Math.Abs(t.c.X + org.X - c.Spline.PositionAt(6f).X) < 6f && Math.Abs(t.c.Z + org.Z - c.Spline.PositionAt(6f).Z) < 6f).Select(t => t.c.Y).DefaultIfEmpty(float.NaN).Max();
        Assert.False(float.IsNaN(top));
    }

    [Fact] public void EveryLegGetsAYieldLineAndACrossing()
    {
        var m = Model();
        foreach (var l in m.Rings[0].Legs.Where(x => x.Generated))
        {
            var c = m.CorridorById(l.CorridorId);
            Assert.Contains(c.Symbols, y => y.kind == "yield" && y.side == "left");        // inbound lanes of a leg that starts at the ring are the left-hand lanes
            Assert.Contains(c.Symbols, y => y.kind == "zebra" && y.side == "both");
        }
        var main = m.Main;
        Assert.Contains(main.Symbols, y => y.kind == "yield" && y.side == "right");          // the main road ENDS at the ring: its inbound lanes are the right-hand lanes
    }

    // ---------------------------------------------------------------- validation
    [Fact] public void RingsNeedSchemaVersionThree()
    {
        Assert.Empty(Errors(Base()));
        Assert.Contains(Errors(Base(s => s.schemaVersion = 2)), e => e.Contains("need schemaVersion 3"));
        Assert.Contains(Errors(Base(s => s.schemaVersion = 4)), e => e.Contains("schemaVersion"));
    }

    [Fact] public void BadRingDataIsRefusedWithReasons()
    {
        Assert.Contains(Errors(Base(s => s.rings[0].profile = "nope")), e => e.Contains("profile 'nope'"));
        Assert.Contains(Errors(Base(s => s.rings[0].radius = 2f)), e => e.Contains("radius must be 4..80"));
        Assert.Contains(Errors(Base(s => s.rings[0].island = "lava")), e => e.Contains("island must be"));
        Assert.Contains(Errors(Base(s => s.rings[0].attachTo = "ghost")), e => e.Contains("attachTo 'ghost'"));
        Assert.Contains(Errors(Base(s => s.rings[0].legs[0].id = "r1_s")), e => e.Contains("already used"));
        Assert.Contains(Errors(Base(s => s.rings[0].legs[0].length = 10f)), e => e.Contains("length must be 40..600"));
        Assert.Contains(Errors(Base(s => s.rings[0].legs[0].rise = 20f)), e => e.Contains("steeper than 6%"));
        Assert.Contains(Errors(Base(s => s.rings[0].connect = new[] { "ghost" })), e => e.Contains("connect names corridor 'ghost'"));
        Assert.Contains(Errors(Base(s => s.profiles[1].medianWidth = 3f)), e => e.Contains("no median"));
        Assert.Contains(Errors(Base(s => s.profiles[1].centreLine = "double_solid")), e => e.Contains("double solid"));
        Assert.Contains(Errors(Base(s => s.rings = s.rings.Concat(s.rings).ToArray())), e => e.Contains("already used"));
    }

    [Fact] public void ARingWithTooFewLegsOrOverlappingMouthsIsRefusedWhenBuilt()
    {
        var ex = Assert.Throws<ArgumentException>(() => Model(s => s.rings[0].legs = new[] { s.rings[0].legs[0] }));
        Assert.Contains("at least three legs", ex.Message);
        ex = Assert.Throws<ArgumentException>(() => Model(s => s.rings[0].legs[0].angleDeg = 270f + 3f));                // 3 degrees from the leg at 270: the mouths would overlap
        Assert.Contains("too close to leg", ex.Message);
    }

    [Fact] public void AConnectedRoadThatDoesNotTouchTheRingIsRefused()
    {
        var ex = Assert.Throws<ArgumentException>(() => Model(s =>
        {
            s.roads = new[] { new RoadSpec { id = "stray", controlPoints = new[] {
                new ControlPoint { id = "a", x = 900f, z = 400f, elevation = 10f, zone = "rural", surface = "good", profile = "p" },
                new ControlPoint { id = "b", x = 900f, z = 600f, elevation = 10f, zone = "rural", surface = "good", profile = "p" } } } };
            s.rings[0].connect = new[] { "stray" };
        }));
        Assert.Contains("its end is", ex.Message);
    }

    // ---------------------------------------------------------------- grade separation
    // A flyover: the main road itself climbs, crosses over the ring on a deck and comes down on the far side (a main road that runs straight over a roundabout at the centre).
    // Here a separate 'over' road crosses a cross road 'under' at the origin of a plain flat test road.
    static RouteSpec Flyover(Action<RouteSpec> edit = null, float deckY = 18f, bool deck = true)
    {
        var s = Fx.Straight(1200f, Fx.Profile("p", lanes: 1));
        s.schemaVersion = 3; s.routeId = "test-straight";
        ControlPoint P(string id, float x, float z, float y) => new ControlPoint { id = id, x = x, z = z, elevation = y, zone = "rural", surface = "good", profile = "p" };
        s.roads = new[]
        {
            new RoadSpec { id = "over", role = "flyover", controlPoints = new[] { P("a", 300f, -300f, 10f), P("b", 450f, -150f, deckY), P("c", 600f, -60f, deckY), P("d", 600f, 60f, deckY), P("e", 750f, 150f, deckY), P("f", 900f, 300f, 10f) },
                decks = deck ? new[] { new BridgeSpec { id = "deck", startS = 250f, endS = 620f, valleyDepth = 0f, overRoad = true, deckThickness = 1.2f, pierSpacing = 20f } } : new BridgeSpec[0] }
        };
        edit?.Invoke(s); return s;
    }

    [Fact] public void AnOverRoadThatCrossesTheMainRoadWithoutADeckIsRefused()
    {
        var ex = Assert.Throws<ArgumentException>(() => RouteModel.Build(Flyover(null, 18f, deck: false)));
        Assert.Contains("without a deck", ex.Message);
    }

    [Fact] public void ALevelOverlapIsRefusedAndALowDeckIsRefusedWithItsHeadroom()
    {
        var level = Assert.Throws<ArgumentException>(() => RouteModel.Build(Flyover(null, 10.3f)));
        Assert.Contains("same level", level.Message);
        var low = Assert.Throws<ArgumentException>(() => RouteModel.Build(Flyover(null, 14f)));
        Assert.Contains("headroom under the deck", low.Message);
    }

    static RouteSpec GoodFlyover(Action<RouteSpec> edit = null) => Flyover(s => { s.roads[0].decks[0].pierGaps = new[] { new PierGap { from = 425f, to = 470f } }; edit?.Invoke(s); });

    [Fact] public void APierThatWouldStandOnTheRoadUnderTheDeckIsRefusedUntilAGapIsDeclared()
    {
        var ex = Assert.Throws<ArgumentException>(() => RouteModel.Build(Flyover()));
        Assert.Contains("stands on road 'test-straight'", ex.Message);
        var m = RouteModel.Build(GoodFlyover());
        Assert.True(m.HasLevels);
    }

    [Fact] public void GroundHeightPicksTheLevelAndTheTerrainIsNotRaisedUnderTheDeck()
    {
        var m = RouteModel.Build(GoodFlyover());
        float low = m.GroundHeight(600f, 0f), high = m.GroundHeight(600f, 0f, 18f);
        Assert.True(Math.Abs(low - 10f) < 0.2f, "lowest " + low);                                              // the main road under the deck
        Assert.True(Math.Abs(high - 18f) < 0.2f, "near the deck " + high);                                     // the deck above it
        Assert.Equal(low, m.GroundHeight(600f, 0f, 10f), 3);
        float beside = m.GroundHeight(600f, 25f), besideDeck = m.GroundHeight(600f, 25f, 18f);               // under the deck but off the lower road (the deck is 9 m wide at z = 25? no: at x = 600 it runs along z)
        Assert.True(Math.Abs(beside - 10f) < 0.2f, "terrain under the deck " + beside);
        Assert.True(besideDeck > 17f, "deck level " + besideDeck);
        Assert.True(Math.Abs(m.Terrain.HeightAt(600f, 25f) - 10f) < 0.2f, "terrain follows the ground, not the deck: " + m.Terrain.HeightAt(600f, 25f));
        Assert.False(m.Terrain.InsideRoadAlong(600f, 25f, -1f), "the ground under a deck is not cut away");
        Assert.True(m.Terrain.InsideRoadAlong(600f, 0f, -1f), "the road under the deck is");
        // and well before the deck, where the road climbs on an embankment, the terrain does follow the road
        var p = m.CorridorById("over").Spline.PositionAt(120f);
        Assert.True(Math.Abs(m.Terrain.HeightAt(p.X, p.Z) - (p.Y - TerrainModel.SinkUnderRoad)) < 0.6f);
    }

    [Fact] public void ARouteWithoutDecksKeepsTheFirstCorridorRule()
    {
        var m = Model();
        Assert.False(m.HasLevels);
        Assert.Equal(m.GroundHeight(300f, 0f), m.GroundHeight(300f, 0f, 99f), 4);                              // the third argument changes nothing without decks
    }

    [Fact] public void ARampSteeperThanSixPercentIsRefusedButALoneRoadMayBeAsSteepAsEight()
    {
        var steep = Assert.Throws<ArgumentException>(() => RouteModel.Build(GoodFlyover(s => { s.roads[0].role = "road"; s.roads[0].controlPoints[0].elevation = 0f; })));
        Assert.Contains("steepest grade", steep.Message);
        var ok = RouteModel.Build(GoodFlyover(s => { s.roads[0].role = "road"; s.roads[0].controlPoints[0].elevation = 7.5f; }));         // between 6 and 8 percent: fine for a road
        Assert.True(ok.HasLevels);
        var ramp = Assert.Throws<ArgumentException>(() => RouteModel.Build(GoodFlyover(s => { s.roads[0].role = "flyover"; s.roads[0].controlPoints[0].elevation = 7.5f; })));
        Assert.Contains("limit 6%", ramp.Message);
    }

    [Fact] public void ADeckMustBeNearlyLevel()
    {
        var ex = Assert.Throws<ArgumentException>(() => RouteModel.Build(GoodFlyover(s => s.roads[0].decks[0].startS = 100f)));
        Assert.Contains("nearly level", ex.Message);
    }

    [Fact] public void TheDeckGetsPiersParapetsAndASlabAndTheGapKeepsThemOffTheRoad()
    {
        var m = RouteModel.Build(GoodFlyover()); var over = m.CorridorById("over");
        float w = 700f; var org = new V3(0f, 0f, -w);
        var mb = RoadGeometry.BuildChunk(over, 0f, -w, 1400f, w, org, (x, z) => m.Terrain.HeightAt(x, z));
        Assert.True(Fx.Tris(mb, RoadSub.Concrete).Any(), "parapets, slab and piers are concrete");
        // no dirt verge on the deck
        var deckVerge = Fx.Tris(mb, RoadSub.Verge).Where(t => { over.Spline.Nearest(t.c.X + org.X, t.c.Z + org.Z, 20f, out float s, out _, out _); return s > 270f && s < 460f; }).ToList();
        Assert.Empty(deckVerge);
        // piers stand on the ground and never on the main road
        foreach (var (n, c) in Fx.Tris(mb, RoadSub.Concrete))
        {
            float x = c.X + org.X, z = c.Z + org.Z;
            if (Math.Abs(c.Y + org.Y - 10f) > 0.5f) continue;                                                       // only things at ground level: pier faces
            if (m.Main.Spline.Nearest(x, z, 6f, out float s, out float lat, out _) && s > 1f && s < m.Main.Length - 1f) Assert.True(Math.Abs(lat) > 5f, "pier face on the main road at " + x + ", " + z);
        }
    }
}
