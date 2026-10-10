using System;
using System.Linq;
using ARO.NetCore;
using Xunit;

public class RoadProfileTests
{
    [Fact] public void UndividedWidthsAddUp()
    {
        var cs = RoadCrossSection.Build(Fx.Profile(lanes: 2, laneWidth: 3.5f, shoulder: 1.5f, verge: 1f));
        Assert.Equal(7f, cs.CarriagewayHalfWidth, 3);        // 2 lanes x 3.5 m each side of the centre line
        Assert.Equal(8.5f, cs.PavedHalfWidth, 3);
        Assert.Equal(9.5f, cs.FormationHalfWidth, 3);
        Assert.False(cs.HasMedian);
    }

    [Fact] public void DividedRoadAddsMedianAndInnerShoulder()
    {
        var p = Fx.Profile(lanes: 3, laneWidth: 3.65f, median: 4f, shoulder: 2.5f, verge: 1f); p.innerShoulderWidth = 0.6f;
        var cs = RoadCrossSection.Build(p);
        Assert.True(cs.HasMedian);
        Assert.Equal(2f + 0.6f + 3 * 3.65f, cs.CarriagewayHalfWidth, 3);
        Assert.Equal(cs.CarriagewayHalfWidth + 2.5f, cs.PavedHalfWidth, 3);
        Assert.Contains(cs.Points, pt => pt.Strip == RoadSub.Concrete);
        Assert.Equal(RoadCrossSection.MedianHeight, cs.HeightAt(0f), 3);              // the median is raised above the carriageway
    }

    [Theory] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void LaneCountsAreReflectedInCentresAndMarkings(int lanes)
    {
        var cs = RoadCrossSection.Build(Fx.Profile(lanes: lanes));
        Assert.Equal(lanes * 2, cs.LaneCentres.Length);
        Assert.Equal(cs.LaneCentres.Length, cs.LaneCentres.Distinct().Count());
        for (int i = 0; i < lanes; i++) Assert.Equal(-cs.LaneCentres[lanes * 2 - 1 - i], cs.LaneCentres[i], 4);     // symmetric
        Assert.Equal(2 * (lanes - 1), cs.Markings.Count(m => m.Name.StartsWith("lane_")));                           // lines BETWEEN lanes
        Assert.Equal(2, cs.Markings.Count(m => m.IsEdge));
    }

    [Fact] public void CentreLineStylesProduceTheRightMarkings()
    {
        Assert.Single(RoadCrossSection.Build(Fx.Profile(centre: "dashed")).Markings, m => m.Name == "centre" && m.Gap > 0f);
        var dbl = RoadCrossSection.Build(Fx.Profile(centre: "double_solid")).Markings.Where(m => m.Name.StartsWith("centre")).ToArray();
        Assert.Equal(2, dbl.Length); Assert.All(dbl, m => { Assert.Equal(0f, m.Gap); Assert.True(m.Yellow); });
        Assert.DoesNotContain(RoadCrossSection.Build(Fx.Profile(centre: "none")).Markings, m => m.Name.StartsWith("centre"));
        Assert.DoesNotContain(RoadCrossSection.Build(Fx.Profile(median: 3f, centre: "dashed")).Markings, m => m.Name == "centre");   // a median replaces the painted line
    }

    [Fact] public void PolylineIsOrderedLeftToRightAndSymmetric()
    {
        foreach (var p in new[] { Fx.Profile(lanes: 2), Fx.Profile(lanes: 3, median: 4f) })
        {
            var pts = RoadCrossSection.Build(p).Points;
            for (int i = 0; i + 1 < pts.Length; i++) Assert.True(pts[i + 1].X >= pts[i].X - 1e-5f, "x must not decrease");
            Assert.Equal(-pts.First().X, pts.Last().X, 4);
            Assert.Equal(pts.First().Y, pts.Last().Y, 4);
        }
    }

    [Fact] public void CamberDropsTowardsTheEdges()
    {
        var p = Fx.Profile(lanes: 2); p.camberPercent = 2f; var cs = RoadCrossSection.Build(p);
        Assert.Equal(0f, cs.HeightAt(0f), 4);
        Assert.Equal(-0.02f * 7f, cs.HeightAt(7f), 3);        // 2 % over 7 m of lanes
        Assert.True(cs.HeightAt(9f) < cs.HeightAt(7f));
        Assert.Equal(cs.HeightAt(5f), cs.HeightAt(-5f), 4);
    }

    [Fact] public void HeightLookupClampsBeyondTheFormation()
    {
        var cs = RoadCrossSection.Build(Fx.Profile());
        Assert.Equal(cs.EdgeY, cs.HeightAt(500f), 4); Assert.Equal(cs.EdgeY, cs.HeightAt(-500f), 4);
    }

    [Fact] public void InvalidProfilesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => RoadCrossSection.Build(Fx.Profile(lanes: 0)));
        Assert.Throws<ArgumentException>(() => RoadCrossSection.Build(Fx.Profile(lanes: 5)));
        Assert.Throws<ArgumentException>(() => RoadCrossSection.Build(Fx.Profile(laneWidth: 1.5f)));
        Assert.Throws<ArgumentException>(() => RoadCrossSection.Build(Fx.Profile(laneWidth: 6f)));
        Assert.Throws<ArgumentException>(() => RoadCrossSection.Build(Fx.Profile(shoulder: -1f)));
        Assert.Throws<ArgumentNullException>(() => RoadCrossSection.Build(null));
    }

    [Fact] public void TopologyKeyDistinguishesLayoutsButNotCamberOrDash()
    {
        string a = RoadCrossSection.Build(Fx.Profile(lanes: 2)).TopologyKey, b = RoadCrossSection.Build(Fx.Profile(lanes: 3)).TopologyKey;
        Assert.NotEqual(a, b);
        var p = Fx.Profile(lanes: 2); p.camberPercent = 4f; p.laneLineDash = 5f;
        Assert.Equal(a, RoadCrossSection.Build(p).TopologyKey);
    }

    [Fact] public void WearIsDeterministicBoundedAndZeroOnGoodRoad()
    {
        Assert.Equal(0f, RoadWear.Height(Surface.Good, 1, 123f, 2f));
        for (float s = 0; s < 500f; s += 1.7f)
        {
            float w = RoadWear.Height(Surface.Damaged, 9, s, 1.5f);
            Assert.InRange(w, -0.06f, 0.06f); Assert.Equal(w, RoadWear.Height(Surface.Damaged, 9, s, 1.5f));
        }
        Assert.True(Enumerable.Range(0, 300).Max(i => Math.Abs(RoadWear.Height(Surface.Damaged, 9, i * 1.3f, 0f))) > Enumerable.Range(0, 300).Max(i => Math.Abs(RoadWear.Height(Surface.Worn, 9, i * 1.3f, 0f))));
    }
}
