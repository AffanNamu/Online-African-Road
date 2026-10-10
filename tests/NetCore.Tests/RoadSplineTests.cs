using System;
using System.Linq;
using ARO.NetCore;
using Xunit;

public class RoadSplineTests
{
    static RoadSpline Make(float[] x, float[] z, float[] y) => new RoadSpline(x, z, y);

    [Fact] public void PassesExactlyThroughEveryControlPoint()
    {
        var spec = Fx.LoadRoute(); var cps = spec.controlPoints;
        var sp = new RoadSpline(cps.Select(c => c.x).ToList(), cps.Select(c => c.z).ToList(), cps.Select(c => c.elevation).ToList());
        for (int i = 0; i < cps.Length; i++)
        {
            var p = sp.PositionAt(sp.ControlS(i));
            Assert.Equal(cps[i].x, p.X, 1); Assert.Equal(cps[i].z, p.Z, 1); Assert.Equal(cps[i].elevation, p.Y, 1);
        }
    }

    [Fact] public void StraightLineHasExactLengthAndConstantHeading()
    {
        var sp = Make(new[] { 0f, 1000f }, new[] { 0f, 0f }, new[] { 5f, 5f });
        Assert.Equal(1000f, sp.Length, 2);
        sp.TangentAt(500f, out float tx, out float tz); Assert.Equal(1f, tx, 4); Assert.Equal(0f, tz, 4);
        Assert.Equal(0f, sp.CurvatureAt(500f), 5);
    }

    [Fact] public void RightIsClockwiseOfForward()
    {
        var sp = Make(new[] { 0f, 1000f }, new[] { 0f, 0f }, new[] { 0f, 0f });   // heading east
        sp.FrameAt(100f, out _, out var fwd, out var right);
        Assert.Equal(1f, fwd.X, 4); Assert.Equal(-1f, right.Z, 4);                  // right of east is south (-z)
    }

    [Fact] public void ArcLengthSamplingIsUniform()
    {
        var sp = Fx.Shipped.Main.Spline;
        for (float s = 0; s + 5f < sp.Length; s += 997f)
        {
            var a = sp.PositionAt(s); var b = sp.PositionAt(s + 5f);
            float d = (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
            Assert.InRange(d, 4.7f, 5.01f);
        }
    }

    [Fact] public void ElevationNeverOvershootsBetweenControlPoints()
    {
        var sp = Make(new[] { 0f, 1000f, 2000f, 3000f }, new[] { 0f, 0f, 0f, 0f }, new[] { 0f, 50f, 52f, 10f });
        for (int i = 0; i < 3; i++)
        {
            float lo = Math.Min(new[] { 0f, 50f, 52f, 10f }[i], new[] { 0f, 50f, 52f, 10f }[i + 1]), hi = Math.Max(new[] { 0f, 50f, 52f, 10f }[i], new[] { 0f, 50f, 52f, 10f }[i + 1]);
            for (float s = sp.ControlS(i); s <= sp.ControlS(i + 1); s += 10f) Assert.InRange(sp.ElevationAt(s), lo - 0.01f, hi + 0.01f);
        }
    }

    [Fact] public void CircleThroughFourPointsHasThatRadius()
    {
        const float R = 500f; var xs = new float[5]; var zs = new float[5];
        for (int i = 0; i < 5; i++) { double a = i * Math.PI / 8; xs[i] = (float)(R * Math.Cos(a)); zs[i] = (float)(R * Math.Sin(a)); }
        var sp = Make(xs, zs, new float[5]);
        float mid = 1f / Math.Abs(sp.CurvatureAt(sp.Length / 2f));
        Assert.InRange(mid, R * 0.9f, R * 1.1f);                              // the interior follows the circle (the ends are less constrained)
    }

    [Fact] public void CurvatureSignIsPositiveForRightTurns()
    {
        var east = Make(new[] { 0f, 500f, 1000f }, new[] { 0f, 0f, -500f }, new[] { 0f, 0f, 0f });     // east then south = right turn
        Assert.True(east.CurvatureAt(east.ControlS(1)) > 0f);
        var left = Make(new[] { 0f, 500f, 1000f }, new[] { 0f, 0f, 500f }, new[] { 0f, 0f, 0f });
        Assert.True(left.CurvatureAt(left.ControlS(1)) < 0f);
    }

    [Fact] public void NearestFindsStationAndSignedLateral()
    {
        var sp = Make(new[] { 0f, 2000f }, new[] { 0f, 0f }, new[] { 0f, 0f });      // east: right = -z
        Assert.True(sp.Nearest(1000f, -5f, 50f, out float s, out float lat, out float dist));
        Assert.Equal(1000f, s, 1); Assert.Equal(5f, lat, 2); Assert.Equal(5f, dist, 2);
        Assert.True(sp.Nearest(700f, 8f, 50f, out s, out lat, out _)); Assert.Equal(-8f, lat, 2);
        Assert.False(sp.Nearest(1000f, 500f, 50f, out _, out _, out _));
    }

    [Fact] public void NearestMatchesBruteForceOnTheShippedRoute()
    {
        var sp = Fx.Shipped.Main.Spline; var rng = new Random(3);
        for (int n = 0; n < 40; n++)
        {
            float s0 = (float)rng.NextDouble() * sp.Length; var p = sp.PositionAt(s0);
            float x = p.X + (float)(rng.NextDouble() - 0.5) * 60f, z = p.Z + (float)(rng.NextDouble() - 0.5) * 60f;
            Assert.True(sp.Nearest(x, z, 200f, out float s, out _, out float dist));
            float best = float.MaxValue;
            for (float t = Math.Max(0, s0 - 120f); t < Math.Min(sp.Length, s0 + 120f); t += 1f) { var q = sp.PositionAt(t); best = Math.Min(best, (float)Math.Sqrt((q.X - x) * (q.X - x) + (q.Z - z) * (q.Z - z))); }
            Assert.InRange(dist, best - 0.8f, best + 0.05f);
        }
    }

    [Fact] public void RejectsMalformedControlPoints()
    {
        Assert.Throws<ArgumentException>(() => Make(new[] { 0f }, new[] { 0f }, new[] { 0f }));                                  // one point
        Assert.Throws<ArgumentException>(() => Make(new[] { 0f, 0f }, new[] { 0f, 0f }, new[] { 0f, 0f }));                      // coincident
        Assert.Throws<ArgumentException>(() => Make(new[] { 0f, float.NaN }, new[] { 0f, 10f }, new[] { 0f, 0f }));              // NaN x
        Assert.Throws<ArgumentException>(() => Make(new[] { 0f, 10f }, new[] { 0f, 10f }, new[] { 0f, float.PositiveInfinity }));// infinite elevation
        Assert.Throws<ArgumentException>(() => Make(new[] { 0f, 10f, 20f }, new[] { 0f, 10f }, new[] { 0f, 1f, 2f }));            // mismatched lengths
    }

    [Fact] public void MinRadiusAndGradeAreReportedForTheShippedRoute()
    {
        var sp = Fx.Shipped.Main.Spline;
        Assert.True(sp.MinRadius() >= RouteSpecValidator.MinRadiusMetres * 2f, "tightest curve " + sp.MinRadius());
        Assert.True(sp.MaxGrade() < 0.02f, "steepest grade " + sp.MaxGrade());
    }

    [Fact] public void HashIsDeterministicAndStringHashIsStable()
    {
        Assert.Equal(Mathx.Hash01(5, 3, 4), Mathx.Hash01(5, 3, 4));
        Assert.NotEqual(Mathx.Hash01(5, 3, 4), Mathx.Hash01(5, 4, 3));
        Assert.Equal(Mathx.StableHash("warehouse_road"), Mathx.StableHash("warehouse_road"));
    }

    [Fact] public void StableHashHasPinnedValues()
    {
        // FNV-1a 32-bit of "" is the offset basis; "a" is a well-known test vector (0xE40C292C).
        Assert.Equal(unchecked((int)2166136261u), Mathx.StableHash(""));
        Assert.Equal(unchecked((int)0xE40C292Cu), Mathx.StableHash("a"));
        for (int i = 0; i < 1000; i++) { float h = Mathx.Hash01(1, i, -i); Assert.InRange(h, 0f, 0.99999994f); }
    }
}

public class GroundHeightTests
{
    [Fact] public void OnTheRoadItIsTheSurfaceOffTheRoadItIsTheTerrain()
    {
        var m = Fx.Shipped; var c = m.Main;
        var p = c.Spline.PositionAt(5000f); var sec = c.SectionAt(5000f);
        Assert.Equal(p.Y + sec.HeightAt(0f), m.GroundHeight(p.X, p.Z), 2);
        float far = 400f;
        Assert.Equal(m.Terrain.HeightAt(p.X + far, p.Z + far), m.GroundHeight(p.X + far, p.Z + far), 3);
        Assert.True(m.GroundHeight(p.X, p.Z) > m.Terrain.HeightAt(p.X, p.Z), "the road surface sits above the sunk terrain under it");
    }

    [Fact] public void SideRoadsCountAsRoad()
    {
        var m = Fx.Shipped; var side = m.Sides[1]; var p = side.Spline.PositionAt(100f);
        Assert.Equal(p.Y + side.SectionAt(100f).HeightAt(0f), m.GroundHeight(p.X, p.Z), 1);
    }
}
