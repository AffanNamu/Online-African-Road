using System;
using System.Collections.Generic;
using System.Linq;
using ARO.NetCore;
using Xunit;

/// <summary>The autopilot paths of the E5 test routes and the path follower that drives them (the same code the smoke pages run in the real build).</summary>
public class AutoPathTests
{
    static IEnumerable<(RouteModel m, string name)> All()
    {
        foreach (var n in AutoPaths.Names(KanoE5RouteTests.Roundabouts)) yield return (KanoE5RouteTests.Roundabouts, n);
        foreach (var n in AutoPaths.Names(KanoE5RouteTests.F1)) yield return (KanoE5RouteTests.F1, n);
    }

    [Fact] public void EveryNamedPathBuildsForEveryVehicle()
    {
        int n = 0;
        foreach (var (m, name) in All())
            foreach (var v in DesignVehicle.All)
            {
                var p = AutoPaths.Build(m, name, v);
                Assert.True(p != null && p.Points.Count > 10 && p.Length > 100f, name + " " + v.Name);
                Assert.True(p.CruiseKmh >= 20f && p.CruiseKmh <= 60f);
                n++;
            }
        Assert.True(n >= 11 * 4, "paths " + n);
        Assert.Null(AutoPaths.Build(KanoE5RouteTests.F1, "nonsense", DesignVehicle.Bus));
        Assert.Null(AutoPaths.Build(KanoE5RouteTests.F1, "ring:ghost", DesignVehicle.Bus));
    }

    /// <summary>Drives a kinematic bicycle with the follower and reports what happened.</summary>
    static (bool done, float maxErr, float seconds, float struck, float maxY, float minY) Simulate(RouteModel m, DesignVehicle v, AutoPath path)
    {
        var f = new PathFollower(path.Points, v.Wheelbase, v.MaxSteerDeg, path.CruiseKmh);
        float th = (float)Math.Atan2(path.Points[1].Y - path.Points[0].Y, path.Points[1].X - path.Points[0].X);
        float x = path.Points[0].X - v.Wheelbase * (float)Math.Cos(th), z = path.Points[0].Y - v.Wheelbase * (float)Math.Sin(th), speed = 0f, t = 0f, dt = 0.05f, struck = 0f, maxY = float.MinValue, minY = float.MaxValue;
        while (!f.Done && t < 600f)
        {
            f.Control(x, z, th, speed, out float steer, out float throttle, out float brake);
            speed = Math.Max(0f, speed + (throttle * 2.5f - brake * 6f - 0.15f) * dt);
            float delta = -steer * f.MaxSteerRad;
            x += speed * dt * (float)Math.Cos(th); z += speed * dt * (float)Math.Sin(th); th += speed * dt / v.Wheelbase * (float)Math.Tan(delta); t += dt;
            foreach (var q in SweptPath.Footprint(v, x, z, th)) if (m.PavedExcess(q.X, q.Y) > 0.02f && (SweptPath.OnIslandCore(m, q.X, q.Y) || !m.Terrain.InsideFormation(q.X, q.Y, 0.5f))) { struck += speed * dt; break; }
            float g = m.GroundHeight(x, z, path.StartY); maxY = Math.Max(maxY, g); minY = Math.Min(minY, g);
        }
        return (f.Done, f.MaxAbsCrossTrack, t, struck, maxY, minY);
    }

    [Fact] public void TheFollowerDrivesEveryPathWithEveryVehicleAndStaysOnTheLane()
    {
        var fails = new List<string>(); int n = 0;
        foreach (var (m, name) in All())
            foreach (var v in DesignVehicle.All)
            {
                var p = AutoPaths.Build(m, name, v); var r = Simulate(m, v, p); n++;
                string tag = name + " " + v.Name;
                if (!r.done) fails.Add(tag + ": did not finish in " + r.seconds.ToString("0") + " s");
                else if (r.maxErr > 1.0f) fails.Add(tag + ": strayed " + r.maxErr.ToString("0.00") + " m from the path");
                else if (r.struck > 0.01f) fails.Add(tag + ": struck something for " + r.struck.ToString("0.0") + " m");
            }
        Assert.True(fails.Count == 0, fails.Count + " of " + n + ":\n" + string.Join("\n", fails));
    }

    [Fact] public void TheUpperPathStaysOnTheDeckAndTheLowerPathStaysOnTheGroundUnderIt()
    {
        var m = KanoE5RouteTests.F1; var v = DesignVehicle.Bus;
        var up = AutoPaths.Build(m, "upper", v); var r = Simulate(m, v, up);
        Assert.True(r.done && r.maxY > 17f, "the upper path climbs to the deck: max " + r.maxY);
        // the same x, z on the lower path is 7.8 m below: the level is chosen by the height of whatever asks
        var low = AutoPaths.Build(m, "lower", v); var r2 = Simulate(m, v, low);
        Assert.True(r2.done && r2.maxY < 10.5f && r2.minY > 9.5f, "the lower path stays on the ground: " + r2.minY + " .. " + r2.maxY);
    }
}
