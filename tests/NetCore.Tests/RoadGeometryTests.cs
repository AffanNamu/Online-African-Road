using System;
using System.Linq;
using ARO.NetCore;
using Xunit;

public class RoadGeometryTests
{
    static RoadCorridor Build(RoadProfileSpec p, float length = 2000f, Action<RouteSpec> tweak = null)
    {
        var s = Fx.Straight(length, p); tweak?.Invoke(s); return RouteModel.Build(s).Main;
    }

    [Fact] public void MeshIsValidFiniteAndAsphaltFacesUp()
    {
        var mb = Fx.WholeRoad(Build(Fx.Profile(lanes: 2)));
        Assert.True(mb.Validate(out var err), err);
        Assert.True(mb.TriangleCount > 100);
        foreach (var (n, _) in Fx.Tris(mb, RoadSub.Asphalt)) Assert.True(n.Y > 0f, "asphalt triangle faces down");
        foreach (var (n, _) in Fx.Tris(mb, RoadSub.Shoulder)) Assert.True(n.Y > 0f, "shoulder triangle faces down");
        foreach (var (n, _) in Fx.Tris(mb, RoadSub.MarkWhite)) Assert.True(n.Y > 0f, "marking faces down");
    }

    [Theory] [InlineData(1, 3.5f)] [InlineData(2, 3.5f)] [InlineData(3, 3.65f)]
    public void AsphaltWidthIsLanesTimesLaneWidthBothWays(int lanes, float laneWidth)
    {
        var mb = Fx.WholeRoad(Build(Fx.Profile(lanes: lanes, laneWidth: laneWidth)));
        var zs = mb.Triangles[RoadSub.Asphalt].Select(i => mb.Vertices[i].Z).ToArray();     // the road runs east, so lateral = z
        Assert.Equal(2f * lanes * laneWidth, zs.Max() - zs.Min(), 2);
    }

    [Fact] public void DividedRoadLeavesRoomForTheMedian()
    {
        var p = Fx.Profile(lanes: 2, laneWidth: 3.5f, median: 4f); p.innerShoulderWidth = 0.6f;
        var mb = Fx.WholeRoad(Build(p));
        var zs = mb.Triangles[RoadSub.Asphalt].Select(i => mb.Vertices[i].Z).ToArray();
        Assert.Equal(2f * (2f + 0.6f + 7f), zs.Max() - zs.Min(), 2);
        Assert.DoesNotContain(zs, z => Math.Abs(z) < 2.5f);                          // no asphalt under the median / its shoulder
        Assert.True(mb.Triangles[RoadSub.Concrete].Count > 0);
    }

    [Fact] public void UndividedRoadWithoutBarriersHasNoConcrete()
    {
        var mb = Fx.WholeRoad(Build(Fx.Profile()));
        Assert.Empty(mb.Triangles[RoadSub.Concrete]); Assert.Empty(mb.Triangles[RoadSub.Metal]);
    }

    [Fact] public void MedianBarrierFacesPointOutwards()
    {
        var p = Fx.Profile(lanes: 1, median: 3f);
        var mb = Fx.WholeRoad(Build(p, 400f));
        // The road runs east, so right = -z. Side faces must point away from the axis; top faces up.
        foreach (var (n, c) in Fx.Tris(mb, RoadSub.Concrete))
        {
            float lateral = -c.Z;           // positive on the right of the centre line
            Assert.True(n.Y > 0.05f * n.Length || n.Z * -1f * Math.Sign(lateral) > 0f, $"a concrete face points inwards (n={n}, lateral={lateral:0.00})");
        }
    }

    [Fact] public void GuardrailAppearsOnlyWhereTheProfileAsksForIt()
    {
        var mb = Fx.WholeRoad(Build(Fx.Profile(barrier: "guardrail"), 200f));
        Assert.True(mb.Triangles[RoadSub.Metal].Count > 0);
        var posts = mb.Triangles[RoadSub.Metal].Select(i => mb.Vertices[i].X).Distinct().Count();
        Assert.True(posts > 20);                                                      // rail rings plus a post every 4 m
    }

    [Fact] public void DashedCentreLineHasTheRightDutyCycle()
    {
        var p = Fx.Profile(centre: "dashed"); p.centreLineColor = "yellow"; p.laneLineDash = 3f; p.laneLineGap = 9f;
        var mb = Fx.WholeRoad(Build(p, 2000f));
        float area = Fx.Area(mb, RoadSub.MarkYellow);
        float expected = 2000f * 0.15f * (3f / 12f);
        Assert.InRange(area, expected * 0.9f, expected * 1.1f);
    }

    [Fact] public void DoubleSolidCentreLineIsContinuous()
    {
        var mb = Fx.WholeRoad(Build(Fx.Profile(centre: "double_solid"), 1000f));
        Assert.InRange(Fx.Area(mb, RoadSub.MarkYellow), 2f * 1000f * 0.12f * 0.97f, 2f * 1000f * 0.12f * 1.03f);
    }

    [Fact] public void EdgeAndLaneLinesAreWhite()
    {
        var mb = Fx.WholeRoad(Build(Fx.Profile(lanes: 3, centre: "none"), 1000f));
        float edge = 2f * 1000f * 0.15f, lanes = 4f * 1000f * 0.12f * 0.25f;          // 2 edge lines + (3-1) x 2 dashed lane lines
        Assert.InRange(Fx.Area(mb, RoadSub.MarkWhite), (edge + lanes) * 0.9f, (edge + lanes) * 1.1f);
        Assert.Equal(0f, Fx.Area(mb, RoadSub.MarkYellow));
    }

    [Fact] public void MarkingsSitJustAboveTheSurface()
    {
        var c = Build(Fx.Profile(lanes: 2, centre: "double_solid")); var mb = Fx.WholeRoad(c);
        var top = mb.Triangles[RoadSub.MarkYellow].Select(i => mb.Vertices[i].Y).ToArray();
        Assert.InRange(top.Min(), 10f + RoadGeometry.MarkingLift - 0.005f, 10f + RoadGeometry.MarkingLift + 0.005f);           // crown of a good road is exactly the elevation
    }

    [Fact] public void EveryRingSegmentIsOwnedByExactlyOneChunk()
    {
        var model = Fx.Shipped; var c = model.Main; const float size = 200f;
        // Find the world extent, tile it with chunks, and compare against one big chunk.
        float minX = c.RingX.Min() - 50f, maxX = c.RingX.Max() + 50f, minZ = c.RingZ.Min() - 50f, maxZ = c.RingZ.Max() + 50f;
        var whole = RoadGeometry.BuildChunk(c, minX, minZ, maxX, maxZ, new V3(0, 0, 0));
        int sum = 0, verts = 0;
        for (float x = (float)Math.Floor(minX / size) * size; x < maxX; x += size)
            for (float z = (float)Math.Floor(minZ / size) * size; z < maxZ; z += size)
            {
                var part = RoadGeometry.BuildChunk(c, x, z, x + size, z + size, new V3(x, 0, z));
                if (part.IsEmpty) continue;
                Assert.True(part.Validate(out var err), err);
                sum += part.Triangles[RoadSub.Asphalt].Count / 3; verts += part.VertexCount;
            }
        Assert.Equal(whole.Triangles[RoadSub.Asphalt].Count / 3, sum);
        Assert.True(whole.Validate(out var e2), e2);
    }

    [Fact] public void ChunkLocalVerticesStayNearTheirChunk()
    {
        var c = Fx.Shipped.Main; var mid = c.Spline.PositionAt(20000f);
        float x0 = (float)Math.Floor(mid.X / 200f) * 200f, z0 = (float)Math.Floor(mid.Z / 200f) * 200f;
        var mb = RoadGeometry.BuildChunk(c, x0, z0, x0 + 200f, z0 + 200f, new V3(x0, 0, z0));
        Assert.False(mb.IsEmpty);
        mb.MinMax(out var lo, out var hi);
        Assert.InRange(lo.X, -60f, 260f); Assert.InRange(hi.Z, -60f, 260f);
    }

    [Fact] public void ChunkWithoutRoadIsEmpty()
    {
        var mb = RoadGeometry.BuildChunk(Fx.Shipped.Main, 1e6f, 1e6f, 1e6f + 200f, 1e6f + 200f, new V3(1e6f, 0, 1e6f));
        Assert.True(mb.IsEmpty); Assert.Equal(0, mb.VertexCount);
    }

    [Fact] public void ProfileChangeMeetsEdgeToEdge()
    {
        var wide = Fx.Profile("wide", lanes: 3, median: 4f, shoulder: 2.5f); var narrow = Fx.Profile("narrow", lanes: 1, shoulder: 1f);
        var s = Fx.Straight(2000f, wide, narrow);
        s.controlPoints = new[]
        {
            new ControlPoint { id = "a", x = 0, z = 0, elevation = 10, zone = "rural", surface = "good", profile = "wide" },
            new ControlPoint { id = "b", x = 1000, z = 0, elevation = 10, zone = "rural", surface = "good", profile = "narrow" },
            new ControlPoint { id = "c", x = 2000, z = 0, elevation = 10, zone = "rural", surface = "good", profile = "narrow" },
        };
        var c = RouteModel.Build(s).Main; Assert.Equal(2, c.Runs.Length);
        float boundary = c.Runs[0].S1;
        Assert.Equal(c.HalfFormationAt(boundary - 0.01f), c.HalfFormationAt(boundary + 0.01f), 2);        // no step in the road edge
        Assert.Equal(c.Runs[0].Section.FormationHalfWidth, c.HalfFormationAt(boundary - 100f), 3);       // untouched away from the change
        Assert.Equal(c.Runs[1].Section.FormationHalfWidth, c.HalfFormationAt(boundary + 100f), 3);
        Assert.True(Fx.WholeRoad(c).Validate(out var err), err);
    }

    [Fact] public void RingTableLandsOnRunBoundariesAndCoversTheRoad()
    {
        foreach (var c in Fx.Shipped.AllCorridors)
        {
            Assert.Equal(0f, c.RingS.First()); Assert.Equal(c.Length, c.RingS.Last(), 2);
            for (int i = 1; i < c.RingS.Length; i++) Assert.True(c.RingS[i] > c.RingS[i - 1]);
            foreach (var r in c.Runs) Assert.Contains(c.RingS, s => Math.Abs(s - r.S0) < 0.01f);
            Assert.True(c.RingS.Zip(c.RingS.Skip(1), (a, b) => b - a).Max() <= 5.01f);       // steps are 4 m, stretched at most 25% to land on a run boundary or the end
        }
    }

    [Fact] public void DamagedRoadGetsFinerRingsThanGoodRoad()
    {
        var m = Fx.Shipped; var c = m.Main;
        float StepAt(float s) { int i = Array.FindIndex(c.RingS, x => x > s); return c.RingS[i] - c.RingS[i - 1]; }
        Assert.InRange(StepAt(33000f), 2.4f, 2.6f);        // damaged
        Assert.InRange(StepAt(12000f), 3.9f, 4.01f);       // good
    }

    [Fact] public void BridgeHasDeckUndersideParapetsAndPiers()
    {
        var p = Fx.Profile(lanes: 2);
        var s = Fx.Straight(2000f, p); s.bridges = new[] { new BridgeSpec { id = "b", startS = 800f, endS = 1000f, valleyDepth = 8f, deckThickness = 1.2f, pierSpacing = 20f } };
        var model = RouteModel.Build(s); var c = model.Main;
        var withGround = Fx.WholeRoad(c, (x, z) => model.Terrain.HeightAt(x, z)); var noBridge = Fx.WholeRoad(Build(p));
        Assert.True(withGround.Triangles[RoadSub.Concrete].Count > 0);
        Assert.Empty(noBridge.Triangles[RoadSub.Concrete]);
        var ys = withGround.Triangles[RoadSub.Concrete].Select(i => withGround.Vertices[i].Y).ToArray();
        float surface = 10f + c.Runs[0].Section.HeightAt(c.Runs[0].Section.PavedHalfWidth);
        Assert.True(ys.Min() < surface - 1.2f - 4f, "piers must reach down into the valley");              // columns go well below the deck
        Assert.True(ys.Any(y => Math.Abs(y - (surface - 1.2f)) < 0.05f), "deck underside is one slab thickness below the surface");
        Assert.DoesNotContain(withGround.Triangles[RoadSub.Verge].Select(i => withGround.Vertices[i]), v => v.X > 805f && v.X < 995f);   // no dirt verge on the deck
        Assert.True(withGround.Validate(out var err), err);
    }

    [Fact] public void JunctionSideRoadLeavesTheMainRoadAtTheRightAngleAndSide()
    {
        var s = Fx.Straight(2000f, Fx.Profile(lanes: 1), Fx.Profile("side", lanes: 1));
        s.junctions = new[] { new JunctionSpec { id = "j", profile = "side", s = 1000f, angleDeg = 90f, length = 200f } };
        var m = RouteModel.Build(s); var side = m.Sides[0];
        var a = side.Spline.PositionAt(0f); var b = side.Spline.PositionAt(side.Length);
        Assert.Equal(1000f, a.X, 0); Assert.True(b.Z < a.Z - 150f, "a right turn from an eastbound road heads south (-z)");
        Assert.Equal(200f, side.Length, 0);
        Assert.Equal(m.Main.HalfPavedAt(1000f), Math.Abs(a.Z), 1);                                       // starts at the paved edge
        var left = Fx.Clone(s); left.junctions[0].angleDeg = -90f; Assert.True(RouteModel.Build(left).Sides[0].Spline.PositionAt(200f).Z > 150f);
        Assert.True(Fx.WholeRoad(side).Validate(out var err), err);
    }

    [Fact] public void JunctionGapsInterruptTheEdgeLineOnThatSideOnly()
    {
        var s = Fx.Straight(2000f, Fx.Profile(lanes: 1), Fx.Profile("side", lanes: 1));
        var plain = Fx.WholeRoad(RouteModel.Build(s).Main);
        s.junctions = new[] { new JunctionSpec { id = "j", profile = "side", s = 1000f, angleDeg = 90f, length = 200f } };
        var withJ = Fx.WholeRoad(RouteModel.Build(s).Main);
        Assert.True(Fx.Area(withJ, RoadSub.MarkWhite) < Fx.Area(plain, RoadSub.MarkWhite) - 0.5f);        // the right edge line stops for the junction mouth
        Assert.InRange(Fx.Area(plain, RoadSub.MarkWhite) - Fx.Area(withJ, RoadSub.MarkWhite), 0.5f, 6f);
    }

    [Fact] public void SideRoadsGetAStopLine()
    {
        var s = Fx.Straight(2000f, Fx.Profile(lanes: 1), Fx.Profile("side", lanes: 1));
        s.junctions = new[] { new JunctionSpec { id = "j", profile = "side", s = 1000f, angleDeg = 90f, length = 200f } };
        var side = RouteModel.Build(s).Sides[0];
        var mb = RoadGeometry.BuildChunk(side, -1e6f, -1e6f, 1e6f, 1e6f, new V3(0, 0, 0));
        Assert.True(Fx.Area(mb, RoadSub.MarkWhite) > 1.5f);
    }

    [Fact] public void WholeShippedRouteGeneratesValidGeometryWithinBudget()
    {
        var m = Fx.Shipped; int tris = 0; const float size = 200f;
        foreach (var c in m.AllCorridors)
        {
            float minX = c.RingX.Min() - 10f, maxX = c.RingX.Max() + 10f, minZ = c.RingZ.Min() - 10f, maxZ = c.RingZ.Max() + 10f;
            for (float x = (float)Math.Floor(minX / size) * size; x < maxX; x += size)
                for (float z = (float)Math.Floor(minZ / size) * size; z < maxZ; z += size)
                {
                    var mb = RoadGeometry.BuildChunk(c, x, z, x + size, z + size, new V3(x, 0, z), (px, pz) => m.Terrain.HeightAt(px, pz));
                    if (mb.IsEmpty) continue;
                    Assert.True(mb.Validate(out var err), $"chunk {x},{z}: {err}");
                    Assert.True(mb.TriangleCount < 12000, $"chunk {x},{z} has {mb.TriangleCount} road triangles");
                    tris += mb.TriangleCount;
                }
        }
        Assert.InRange(tris, 100000, 4000000);
    }

    [Fact] public void MeshBuffersRejectBrokenData()
    {
        var mb = new MeshBuffers(1); mb.Add(new V3(0, 0, 0), new V2(0, 0)); mb.Add(new V3(float.NaN, 0, 0), new V2(0, 0)); mb.Add(new V3(1, 0, 0), new V2(0, 0));
        Assert.False(mb.Validate(out var e1)); Assert.Contains("not finite", e1);
        var ok = new MeshBuffers(1); ok.Add(new V3(0, 0, 0), new V2(0, 0)); ok.Add(new V3(1, 0, 0), new V2(0, 0)); ok.Add(new V3(0, 0, 1), new V2(0, 0));
        ok.Tri(0, 0, 1, 5); Assert.False(ok.Validate(out var e2)); Assert.Contains("out of range", e2);
        var deg = new MeshBuffers(1); deg.Add(new V3(0, 0, 0), new V2(0, 0)); deg.Add(new V3(1, 0, 0), new V2(0, 0)); deg.Tri(0, 0, 1, 1); Assert.False(deg.Validate(out var e3)); Assert.Contains("degenerate", e3);
    }

    [Fact] public void AddBoxFacesPointOutwards()
    {
        var mb = new MeshBuffers(1); RoadGeometry.AddBox(mb, 0, new V3(5, 0, 5), 2f, 3f, 2f, new V3(1, 0, 0), new V3(0, 0, -1));
        Assert.True(mb.Validate(out var err), err);
        var centre = new V3(5, 1.5f, 5);
        foreach (var (n, c) in Fx.Tris(mb, 0)) Assert.True(MeshBuffers.Dot(n, c - centre) > 0f, "a box face points inwards");
    }
}
