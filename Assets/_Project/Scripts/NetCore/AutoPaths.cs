using System;
using System.Collections.Generic;
using System.Linq;

namespace ARO.NetCore
{
    /// <summary>A path an autopilot can follow on a route, with where to start it and how fast to go. Used by the smoke pages (CI proves in the real build that cars, buses and trucks can drive the E5 roads) and by tests.</summary>
    public sealed class AutoPath
    {
        public string Name; public List<V2> Points = new List<V2>();
        public float StartY;                  // the surface height at the first point (a vehicle on the lower road and one on the deck stand at different heights over the same x, z)
        public float CruiseKmh = 30f;
        public float Length { get { float l = 0f; for (int i = 1; i < Points.Count; i++) l += (float)Math.Sqrt(Math.Pow(Points[i].X - Points[i - 1].X, 2) + Math.Pow(Points[i].Y - Points[i - 1].Y, 2)); return l; } }
    }

    /// <summary>The named autopilot paths of the E5 test routes. Pure geometry from the route model; the same paths the swept-path tests drive.</summary>
    public static class AutoPaths
    {
        /// <summary>The names the route supports, for a page that lists them.</summary>
        public static IEnumerable<string> Names(RouteModel m)
        {
            foreach (var r in m.Rings) yield return "ring:" + r.Id;
            if (m.HasLevels) { yield return "upper"; yield return "upper_south"; yield return "lower"; yield return "lower_back"; }
            if (m.CorridorById("ramp_off") != null) yield return "ramp_off";
            if (m.CorridorById("ramp_on") != null) yield return "ramp_on";
        }

        public static AutoPath Build(RouteModel m, string name, DesignVehicle v)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name.StartsWith("ring:"))
            {
                var ring = m.Rings.FirstOrDefault(r => r.Id == name.Substring(5)); if (ring == null) return null;
                // in on the first leg, out on the leg that is furthest round the ring counter-clockwise (the longest way through)
                var entry = ring.Legs[0]; RingLegInfo exit = ring.Legs.Where(l => l.CorridorId != entry.CorridorId).OrderByDescending(l => Sweep(entry.Angle, l.Angle)).First();
                return Wrap(name, SweptPath.RingPath(m, ring, entry, exit, ring.Lanes - 1, 1, v.EasyTurnRadius, 60f), 28f, StartY(m, m.CorridorById(entry.CorridorId), entry));
            }
            var main = m.Main; float lane = main.Runs[0].Section.LaneCentres.Where(x => x > 0f).Min();
            switch (name)
            {
                case "upper": return Wrap(name, SweptPath.LanePath(main, lane, 440f, main.Length - 380f), 45f, main.SurfaceY(440f, lane));
                case "upper_south": return Wrap(name, SweptPath.LanePath(main, -lane, main.Length - 380f, 440f), 45f, main.SurfaceY(main.Length - 380f, -lane));
                case "lower": case "lower_back":
                {
                    var ring = m.Rings[0]; var w = ring.Legs.FirstOrDefault(l => l.CorridorId.EndsWith("_w")); var e = ring.Legs.FirstOrDefault(l => l.CorridorId.EndsWith("_e"));
                    if (w.CorridorId == null || e.CorridorId == null) return null;
                    var a = name == "lower" ? w : e; var b = name == "lower" ? e : w;
                    return Wrap(name, SweptPath.RingPath(m, ring, a, b, ring.Lanes - 1, 1, v.EasyTurnRadius, 90f), 28f, StartY(m, m.CorridorById(a.CorridorId), a));
                }
                case "ramp_off": case "ramp_on":
                {
                    var c = m.CorridorById(name); if (c == null) return null;
                    float lane2 = c.Runs[0].Section.LaneCentres.Where(x => x > 0f).Min();
                    return Wrap(name, SweptPath.LanePath(c, lane2, 6f, c.Length - 6f), 25f, c.SurfaceY(6f, lane2));
                }
            }
            return null;
        }

        static float Sweep(float from, float to) { double d = (to - from) % (2 * Math.PI); if (d < 0) d += 2 * Math.PI; return (float)d; }

        static float StartY(RouteModel m, RoadCorridor c, RingLegInfo leg)
        {
            // the first path point is 60 to 90 m out along the leg
            var p = c.Spline.PositionAt(leg.AtStart ? Math.Min(c.Length, 60f) : Math.Max(0f, c.Length - 60f)); return p.Y;
        }

        static AutoPath Wrap(string name, List<V2> pts, float kmh, float y) => new AutoPath { Name = name, Points = pts, CruiseKmh = kmh, StartY = y };
    }

    /// <summary>
    /// Steering and speed commands that keep a vehicle's front axle on a path (Stanley controller) at a speed that fits the bend ahead. Pure and deterministic: the Unity autopilot and the tests use it
    /// unchanged. Inputs are the vehicle's rear-axle position and heading (radians, atan2(dz, dx)); outputs are in the VehicleInputState range.
    /// </summary>
    public sealed class PathFollower
    {
        readonly List<V2> _pts; int _idx;
        public readonly float Wheelbase, MaxSteerRad, CruiseMs;
        public float CrossTrack { get; private set; }
        public float Progress => _idx / (float)Math.Max(1, _pts.Count - 1);
        public bool Done { get; private set; }
        public float MaxAbsCrossTrack { get; private set; }

        public PathFollower(IList<V2> path, float wheelbase, float maxSteerDeg, float cruiseKmh)
        {
            _pts = Resample(path, 1f); Wheelbase = wheelbase; MaxSteerRad = maxSteerDeg * (float)Math.PI / 180f; CruiseMs = cruiseKmh / 3.6f;
        }

        public IReadOnlyList<V2> Points => _pts;

        /// <summary>One control step. steer -1 (left) .. 1 (right) as in VehicleInputState: positive Stanley angles turn LEFT in the (x east, z north) plane, so the sign is flipped here.</summary>
        public void Control(float rearX, float rearZ, float heading, float speedMs, out float steer, out float throttle, out float brake, float steerAuthority = 1f)
        {
            float fx = rearX + Wheelbase * (float)Math.Cos(heading), fz = rearZ + Wheelbase * (float)Math.Sin(heading);
            float best = float.MaxValue; int bi = _idx;
            for (int i = _idx; i < Math.Min(_pts.Count - 1, _idx + 40); i++) { float d = Sq(_pts[i].X - fx, _pts[i].Y - fz); if (d < best) { best = d; bi = i; } }
            _idx = bi;
            if (_idx >= _pts.Count - 3) { Done = true; steer = 0f; throttle = 0f; brake = 1f; return; }
            var a = _pts[_idx]; var b = _pts[_idx + 1];
            float phi = (float)Math.Atan2(b.Y - a.Y, b.X - a.X);
            float e = (fx - a.X) * -(float)Math.Sin(phi) + (fz - a.Y) * (float)Math.Cos(phi);        // + = left of the path
            CrossTrack = e; MaxAbsCrossTrack = Math.Max(MaxAbsCrossTrack, Math.Abs(e));
            float delta = Norm(phi - heading) - (float)Math.Atan2(1.6f * e, Math.Max(2f, speedMs));
            steer = -Math.Max(-1f, Math.Min(1f, delta / (MaxSteerRad * Math.Max(0.2f, steerAuthority))));                              // delta > 0 = turn left = negative steer input
            // speed: the cruise speed, lower for a tight bend within the next 25 m
            float target = CruiseMs; float ahead = TurnAhead(_idx, 25f);
            if (ahead > 0.02f) target = Math.Max(2.5f, Math.Min(target, (float)Math.Sqrt(1.2f / ahead)));       // lateral acceleration limit about 1.2 m/s2
            float err = target - speedMs;
            throttle = err > 0f ? Math.Min(1f, err * 0.4f) : 0f; brake = err < -1.5f ? Math.Min(1f, (-err - 1.5f) * 0.3f) : 0f;
        }

        float TurnAhead(int from, float metres)
        {
            int n = Math.Min(_pts.Count - 2, from + (int)metres); float worst = 0f;
            for (int i = from + 1; i < n; i++)
            {
                float h0 = (float)Math.Atan2(_pts[i].Y - _pts[i - 1].Y, _pts[i].X - _pts[i - 1].X), h1 = (float)Math.Atan2(_pts[i + 1].Y - _pts[i].Y, _pts[i + 1].X - _pts[i].X);
                worst = Math.Max(worst, Math.Abs(Norm(h1 - h0)));                                  // radians per metre (points are 1 m apart)
            }
            return worst;
        }

        static float Sq(float a, float b) => a * a + b * b;
        static float Norm(float a) { while (a > Math.PI) a -= 2f * (float)Math.PI; while (a < -Math.PI) a += 2f * (float)Math.PI; return a; }

        static List<V2> Resample(IList<V2> p, float step)
        {
            var r = new List<V2> { p[0] }; float carry = 0f;
            for (int i = 1; i < p.Count; i++)
            {
                float dx = p[i].X - p[i - 1].X, dz = p[i].Y - p[i - 1].Y, len = (float)Math.Sqrt(dx * dx + dz * dz); if (len < 1e-4f) continue;
                float t = step - carry;
                while (t <= len) { r.Add(new V2(p[i - 1].X + dx * t / len, p[i - 1].Y + dz * t / len)); t += step; }
                carry = len - (t - step);
            }
            r.Add(p[p.Count - 1]);
            return r;
        }
    }
}
