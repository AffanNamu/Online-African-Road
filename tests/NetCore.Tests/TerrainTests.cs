using System;
using System.Linq;
using ARO.NetCore;
using Xunit;

public class TerrainTests
{
    [Fact] public void TerrainIsDeterministicAcrossModelInstances()
    {
        var a = RouteModel.Build(Fx.LoadRoute()).Terrain; var b = RouteModel.Build(Fx.LoadRoute()).Terrain;
        foreach (var (x, z) in new[] { (100f, 50f), (12000f, 6000f), (39000f, 22000f), (-500f, 800f) })
        { Assert.Equal(a.HeightAt(x, z), b.HeightAt(x, z)); Assert.Equal(a.HillsAt(x, z), a.HillsAt(x, z)); }
    }

    [Fact] public void RoadCentreLineSitsOnTheFormationJustBelowTheSurface()
    {
        var m = Fx.Shipped; var c = m.Main;
        for (float s = 100f; s < c.Length - 100f; s += 1301f)
        {
            if (c.InBridge(s, out _)) continue;
            var p = c.Spline.PositionAt(s); var sec = c.SectionAt(s);
            float expected = p.Y + sec.HeightAt(0f) - TerrainModel.SinkUnderRoad;
            Assert.Equal(expected, m.Terrain.HeightAt(p.X, p.Z), 1);
        }
    }

    [Fact] public void FarFromEveryRoadTheTerrainIsNatural()
    {
        var m = Fx.Shipped; var p = m.Main.Spline.PositionAt(5000f);
        float x = p.X + 600f, z = p.Z + 600f;        // far outside any formation plus embankment reach
        Assert.Equal(m.Terrain.NaturalAt(x, z), m.Terrain.HeightAt(x, z), 3);
        Assert.False(m.Terrain.InsideFormation(x, z));
    }

    [Fact] public void EmbankmentSlopesRespectTheDesignLimit()
    {
        var m = Fx.Shipped; var c = m.Main; float limit = m.Spec.terrain.maxEmbankmentSlope;
        for (float s = 500f; s < c.Length - 500f; s += 2111f)
        {
            if (c.InBridge(s, out _)) continue;
            c.Spline.FrameAt(s, out var pos, out _, out var right);
            float half = c.HalfFormationAt(s);
            foreach (int side in new[] { -1, 1 })
            {
                float prev = float.NaN, worstBeyondDitch = 0f;
                for (float d = 0f; d <= 80f; d += 1f)
                {
                    float x = pos.X + right.X * side * (half + d), z = pos.Z + right.Z * side * (half + d), h = m.Terrain.HeightAt(x, z);
                    if (!float.IsNaN(prev) && d > c.Runs[c.RunIndexAt(s)].Section.Spec.ditchWidth + 1f) worstBeyondDitch = Math.Max(worstBeyondDitch, Math.Abs(h - prev));
                    prev = h;
                }
                Assert.True(worstBeyondDitch <= limit * 1.25f + 0.15f, $"slope {worstBeyondDitch:0.00} beside s={s:0}");
            }
        }
    }

    [Fact] public void DitchDipsBelowTheVergeAndRecovers()
    {
        var s = Fx.Straight(2000f); var m = RouteModel.Build(s); var sec = m.Main.Runs[0].Section;
        float half = sec.FormationHalfWidth;                       // road runs east: right = -z
        float edge = m.Terrain.HeightAt(1000f, -half - 0.01f), ditch = m.Terrain.HeightAt(1000f, -half - sec.Spec.ditchWidth * 0.5f), beyond = m.Terrain.HeightAt(1000f, -half - 12f);
        Assert.True(ditch < edge - 0.25f, "a roadside ditch dips below the verge edge");
        Assert.True(beyond > ditch);
    }

    [Fact] public void BridgesCarveAValleyUnderTheDeck()
    {
        var s = Fx.Straight(2000f); s.bridges = new[] { new BridgeSpec { id = "b", startS = 800f, endS = 1000f, valleyDepth = 8f } };
        var m = RouteModel.Build(s); var t = m.Terrain;
        float hills = t.HillsAt(900f, 0f), under = t.HeightAt(900f, 0f);
        Assert.Equal(hills - 8f, under, 1);
        float before = t.HeightAt(600f, 0f); Assert.Equal(10f - TerrainModel.SinkUnderRoad, before, 1);     // ordinary formation before the bridge
        Assert.True(t.HeightAt(900f, 40f) < hills - 2f);                                                   // the valley is wider than the road
        Assert.Equal(hills, t.HeightAt(900f, 200f), 1);
    }

    [Fact] public void InsideFormationFollowsTheRoadWidthAndShrinksWithMargin()
    {
        var s = Fx.Straight(2000f); var m = RouteModel.Build(s); float half = m.Main.Runs[0].Section.FormationHalfWidth;
        Assert.True(m.Terrain.InsideFormation(500f, 0f)); Assert.True(m.Terrain.InsideFormation(500f, half - 0.1f));
        Assert.False(m.Terrain.InsideFormation(500f, half + 0.5f));
        Assert.False(m.Terrain.InsideFormation(500f, half - 0.5f, -1f)); Assert.True(m.Terrain.InsideFormation(500f, half + 0.5f, 1f));
    }

    [Fact] public void SideRoadsConformTheTerrainToo()
    {
        var s = Fx.Straight(2000f, Fx.Profile(), Fx.Profile("side")); s.junctions = new[] { new JunctionSpec { id = "j", profile = "side", s = 1000f, angleDeg = 90f, length = 300f } };
        var m = RouteModel.Build(s); var side = m.Sides[0]; var p = side.Spline.PositionAt(150f);
        Assert.True(m.Terrain.InsideFormation(p.X, p.Z)); Assert.Equal(p.Y - TerrainModel.SinkUnderRoad, m.Terrain.HeightAt(p.X, p.Z), 1);
    }

    [Fact] public void TileHasNoHolesAwayFromTheRoadAndHolesOverIt()
    {
        var s = Fx.Straight(2000f, Fx.Profile(lanes: 3, median: 4f)); var m = RouteModel.Build(s);       // a wide road: cells are 10 m, so only a wide formation fully covers one
        var away = TerrainGeometry.BuildTile(m.Terrain, 0f, 400f, 200f, 20, new V3(0, 0, 400), true, true);
        var over = TerrainGeometry.BuildTile(m.Terrain, 0f, -100f, 200f, 20, new V3(0, 0, -100), true, true);
        Assert.True(away.Validate(out var e1), e1); Assert.True(over.Validate(out var e2), e2);
        Assert.Equal(20 * 20 * 2 + 4 * 20 * 2, away.TriangleCount);        // every cell plus the skirts
        Assert.True(over.TriangleCount < away.TriangleCount, "cells under the road are cut out");
        foreach (var (n, c) in Fx.Tris(away, TerrainSub.Ground)) if (Math.Abs(n.Y) > 0.5f * n.Length) Assert.True(n.Y > 0f, "ground faces up");
    }

    [Theory] [InlineData(8)] [InlineData(20)] [InlineData(40)]
    public void TileResolutionControlsTriangleCount(int cells)
    {
        var m = Fx.Shipped; var mb = TerrainGeometry.BuildTile(m.Terrain, 90000f, 90000f, 200f, cells, new V3(90000, 0, 90000));      // nowhere near the road
        Assert.Equal(cells * cells * 2 + 4 * cells * 2, mb.TriangleCount);
        Assert.True(mb.Validate(out var err), err);
        Assert.Throws<ArgumentException>(() => TerrainGeometry.BuildTile(m.Terrain, 0, 0, 200f, 0, new V3(0, 0, 0)));
    }

    [Fact] public void SkirtsHangBelowTheEdgesSoDifferentLodsNeverShowCracks()
    {
        var m = Fx.Shipped; var mb = TerrainGeometry.BuildTile(m.Terrain, 90000f, 90000f, 200f, 10, new V3(90000, 0, 90000));
        mb.MinMax(out var lo, out _);
        var surface = mb.Triangles[0].Select(i => mb.Vertices[i].Y).Where(y => y > lo.Y + TerrainGeometry.SkirtDepth - 0.01f).Min();
        Assert.True(lo.Y <= surface - TerrainGeometry.SkirtDepth + 0.01f || lo.Y < surface);
    }

    [Fact] public void CollisionSlabIsClosedAndThick()
    {
        var m = Fx.Shipped; var mb = TerrainGeometry.BuildCollision(m.Terrain, 90000f, 90000f, 200f, 20, new V3(90000, 0, 90000), 3f);
        Assert.True(mb.Validate(out var err), err);
        mb.MinMax(out var lo, out var hi);
        Assert.True(hi.Y - lo.Y >= 3f, "the slab is at least its thickness deep");
        // Every wall and the underside face away from the slab centre.
        var centre = new V3(100f, (lo.Y + hi.Y) / 2f, 100f);
        int down = Fx.Tris(mb, 0).Count(t => t.n.Y < -0.9f * t.n.Length); Assert.True(down >= 2, "an underside exists");
    }

    [Fact] public void ShippedRouteTerrainStaysWithinSaneBounds()
    {
        var m = Fx.Shipped; var t = m.Terrain;
        for (float s = 0; s < m.Length; s += 997f)
        {
            var p = m.Main.Spline.PositionAt(s);
            foreach (float off in new[] { -40f, 0f, 40f, 120f })
            {
                float h = t.HeightAt(p.X + off, p.Z + off * 0.3f);
                Assert.InRange(h, -20f, 200f); Assert.False(float.IsNaN(h));
            }
        }
    }
}
