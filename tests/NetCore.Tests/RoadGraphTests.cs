using System;
using System.Linq;
using ARO.NetCore;
using Xunit;

/// <summary>E4b: the road graph of a route (nothing in the game uses it yet).</summary>
public class RoadGraphTests
{
    static RouteSpec Network(int crossings = 10)
    {
        var s = Fx.Straight(3200f, Fx.Profile(lanes: 2), Fx.Profile("side", lanes: 1));
        s.schemaVersion = 2;
        s.junctions = Enumerable.Range(0, crossings).Select(i => new JunctionSpec { id = "x" + i, kind = i % 2 == 0 ? "four_way" : "t_junction", profile = "side", s = 200f + i * 250f, length = 100f + 10f * i, lengthLeft = 90f }).ToArray();
        return s;
    }

    [Fact] public void NodesAndEdgesFollowTheJunctions()
    {
        var m = RouteModel.Build(Network()); var g = RoadGraph.Build(m);
        int stubs = m.Sides.Count;                                   // 5 four-ways (2 stubs) + 5 T junctions
        Assert.Equal(15, stubs);
        Assert.Equal(2 + 10 + stubs, g.Nodes.Count);                 // main ends, 10 stations, 15 stub ends
        Assert.Equal(11 + stubs, g.Edges.Count);                     // 11 main segments, 15 stubs
        Assert.Equal(m.Main.Length, g.Edges.Where(e => e.Corridor == m.Main).Sum(e => e.Length), 2);
        foreach (var c in m.Sides) { var e = g.Edges.Single(x => x.Corridor == c); Assert.Equal(c.Length + e.Lead, e.Length, 2); Assert.InRange(e.Lead, 4f, 12f); }       // a side road starts at the paved edge, not on the centre line
        Assert.Equal(2, g.NodeById("junction:200").Edges.Count(e => e.Corridor != m.Main));                  // the first station is a four-way
        Assert.Equal(1, g.NodeById("junction:450").Edges.Count(e => e.Corridor != m.Main));                  // the second a T
    }

    [Fact] public void LocateNamesTheRoadAPointIsOn()
    {
        var m = RouteModel.Build(Network()); var g = RoadGraph.Build(m);
        Assert.True(g.Locate(700f, 2f, 50f, out var e, out float s, out float lat)); Assert.Same(m.Main, e.Corridor); Assert.Equal(700f, s, 0); Assert.InRange(Math.Abs(lat), 1.9f, 2.1f);
        var stub = m.CorridorById("x2"); var p = stub.Spline.PositionAt(60f);
        Assert.True(g.Locate(p.X, p.Z, 20f, out e, out s, out lat)); Assert.Equal("side:x2", e.Id); Assert.Equal(60f, s, 0);
        var left = m.CorridorById("x2_left"); p = left.Spline.PositionAt(40f);
        Assert.True(g.Locate(p.X, p.Z, 20f, out e, out s, out _)); Assert.Equal("side:x2_left", e.Id);
        Assert.False(g.Locate(1500f, 900f, 50f, out _, out _, out _));
    }

    [Fact] public void ShortestPathGoesAlongTheMainRoadAndOutTheStubs()
    {
        var m = RouteModel.Build(Network()); var g = RoadGraph.Build(m);
        var from = g.NodeById("end:x0"); var to = g.NodeById("end:x9");
        var path = g.ShortestPath(from, to); Assert.NotNull(path);
        Assert.Equal("side:x0", path.First().Edge.Id); Assert.False(path.First().Forward, "the first stub is walked from its far end towards the main road");
        Assert.Equal("side:x9", path.Last().Edge.Id);
        float expected = m.CorridorById("x0").Length + g.Edges.Single(e => e.Id == "side:x0").Lead + (9 * 250f) + m.CorridorById("x9").Length + g.Edges.Single(e => e.Id == "side:x9").Lead;
        Assert.Equal(expected, RoadGraph.Length(path), 1);
        Assert.Equal(path.Count, 2 + 9);                                                    // two stubs and nine main segments between the stations
        Assert.Empty(g.ShortestPath(from, from));
        // symmetric
        Assert.Equal(RoadGraph.Length(path), RoadGraph.Length(g.ShortestPath(to, from)), 2);
    }

    [Fact] public void SamplingWalksThePathInOrderWithoutGaps()
    {
        var m = RouteModel.Build(Network()); var g = RoadGraph.Build(m);
        var path = g.ShortestPath(g.NodeById("end:x1"), g.NodeById("end:x4")); var pts = g.Sample(path, 10f);
        Assert.True(pts.Count > 30);
        for (int i = 1; i < pts.Count; i++) Assert.True(Math.Sqrt(Math.Pow(pts[i].X - pts[i - 1].X, 2) + Math.Pow(pts[i].Z - pts[i - 1].Z, 2)) <= 10.5f, "gap at sample " + i);
        Assert.Equal("x1", pts[0].Corridor); Assert.Equal("x4", pts[pts.Count - 1].Corridor);
        Assert.Equal(m.CorridorById("x1").Length, pts[0].S, 1); Assert.Equal(m.CorridorById("x4").Length, pts[pts.Count - 1].S, 1);       // starts at the far end of one stub, walks in, along the main road, and out to the far end of the other
    }

    [Fact] public void ARouteWithoutJunctionsIsOneEdge()
    {
        var g = RoadGraph.Build(RouteModel.Build(Fx.Straight(1000f)));
        Assert.Equal(2, g.Nodes.Count); Assert.Single(g.Edges);
        var shipped = RoadGraph.Build(Fx.Shipped); int stubs = Fx.Shipped.Sides.Count;
        Assert.Equal(2 + stubs * 2, shipped.Nodes.Count);                      // each T junction adds its station and its far end
        Assert.Equal(stubs + stubs + 1, shipped.Edges.Count);                  // one main segment per gap plus one edge per stub
    }
}
