using System;
using System.Collections.Generic;
using System.Linq;
using ARO.NetCore;
using Xunit;

/// <summary>E4a (docs/ROAD_ENGINE_E4.md): the spatial index for point queries answers EXACTLY what the plain loop over every corridor answers, with far fewer candidates.</summary>
public class CorridorIndexTests
{
    // a 3 km main road with sixty side roads (a four-way every 100 m): a city-sized corridor count
    static RouteSpec Network(int crossings = 30)
    {
        var s = Fx.Straight(3200f, Fx.Profile(lanes: 2, shoulder: 1.5f), Fx.Profile("side", lanes: 1));
        s.schemaVersion = 2; s.terrain.hillAmplitude = 5f; s.terrain.hillWavelength = 400f;
        s.junctions = Enumerable.Range(0, crossings).Select(i => new JunctionSpec { id = "x" + i, kind = "four_way", profile = "side", s = 150f + i * 100f, length = 80f + (i % 5) * 60f, lengthLeft = 100f + (i % 3) * 90f }).ToArray();
        return s;
    }

    [Fact] public void ABigNetworkUsesTheIndexAndASmallRouteDoesNot()
    {
        var big = RouteModel.Build(Network()); Assert.Equal(61, big.Sides.Count + 1); Assert.True(big.Indexed); Assert.True(big.Index.CellCount > 50);
        Assert.False(RouteModel.Build(Fx.Straight(2000f)).Indexed);
        Assert.False(Fx.Shipped.Indexed, "the shipped Lagos to Ibadan route keeps the plain loop");
    }

    [Fact] public void EveryPointQueryGivesTheSameAnswerWithAndWithoutTheIndex()
    {
        var plain = RouteModel.Build(Network()); plain.ForceIndex = false;
        var fast = RouteModel.Build(Network()); fast.ForceIndex = true;
        Assert.False(plain.Indexed); Assert.True(fast.Indexed);
        int n = 0, inside = 0;
        for (int i = 0; i < 4000; i++)
        {
            float x = -50f + 3300f * Mathx.Hash01(11, i, 1), z = -420f + 840f * Mathx.Hash01(11, i, 2); n++;
            Assert.Equal(plain.GroundHeight(x, z), fast.GroundHeight(x, z));
            Assert.Equal(plain.Terrain.HeightAt(x, z), fast.Terrain.HeightAt(x, z));
            bool f = plain.Terrain.InsideFormation(x, z, 0.5f); Assert.Equal(f, fast.Terrain.InsideFormation(x, z, 0.5f)); if (f) inside++;
            Assert.Equal(plain.Terrain.InsideRoadAlong(x, z, -1f), fast.Terrain.InsideRoadAlong(x, z, -1f));
        }
        Assert.True(inside > 200, "the sample should hit the roads often (" + inside + ")");
    }

    [Fact] public void TheIndexAlsoAgreesBeyondTheEndsOfTheStubs()
    {   // straight beyond a stub's end the legacy rule treats the ground as formation for up to the query reach: the index must keep every corridor that rule can see
        var plain = RouteModel.Build(Network()); plain.ForceIndex = false; var fast = RouteModel.Build(Network()); fast.ForceIndex = true;
        foreach (var c in plain.Sides.Take(20))
        {
            var end = c.Spline.PositionAt(c.Length); c.Spline.FrameAt(c.Length, out _, out var fwd, out _);
            for (float d = 0f; d <= 95f; d += 5f)
            {
                float x = end.X + fwd.X * d, z = end.Z + fwd.Z * d;
                Assert.Equal(plain.Terrain.HeightAt(x, z), fast.Terrain.HeightAt(x, z)); Assert.Equal(plain.GroundHeight(x, z), fast.GroundHeight(x, z));
            }
        }
    }

    [Fact] public void TheIndexCutsTheCandidatesToAFewCorridors()
    {
        var m = RouteModel.Build(Network()); long total = 0; int n = 3000;
        for (int i = 0; i < n; i++) total += m.CorridorsNear(-50f + 3300f * Mathx.Hash01(5, i, 1), -420f + 840f * Mathx.Hash01(5, i, 2)).Length;
        double mean = (double)total / n; Assert.True(mean < 14.0, "mean candidates " + mean + " of " + (m.Sides.Count + 1));
        Assert.Equal(m.Sides.Count + 1, RouteModel.Build(Network()).Also(r => r.ForceIndex = false).CorridorsNear(0f, 0f).Length);
        Assert.Empty(m.CorridorsNear(1e6f, 1e6f));
    }

    [Fact] public void CandidatesKeepTheOrderOfAllCorridors()
    {
        var m = RouteModel.Build(Network()); var order = m.AllCorridors.Select((c, i) => (c, i)).ToDictionary(t => t.c, t => t.i);
        for (int i = 0; i < 200; i++)
        {
            var list = m.CorridorsNear(300f + i * 13f, -30f + (i % 7) * 9f).Select(c => order[c]).ToArray();
            Assert.Equal(list.OrderBy(v => v).ToArray(), list); Assert.Equal(list.Length, list.Distinct().Count());
        }
    }

    [Fact] public void SitesAndPropScatterStillWorkOnAnIndexedRoute()
    {
        var s = Network(); s.sites = new[] { new SitePlateSpec { id = "p", corridor = "x3", s = 120f, forward = 25f, widthM = 30f, depthM = 40f } };
        s.junctions[3].length = 120f; s.junctions[3].lengthLeft = 120f;
        var m = RouteModel.Build(s); Assert.True(m.Indexed); var p = m.Sites[0];
        Assert.Equal(p.BaseY, m.GroundHeight(p.Cx, p.Cz), 3); Assert.True(m.Terrain.InsideFormation(p.Cx, p.Cz));
    }
}
