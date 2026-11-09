using System;
using System.Collections.Generic;
using System.Linq;

namespace ARO.NetCore
{
    /// <summary>A vehicle as the road designer sees it: a rectangle on a bicycle model (rear-axle reference). Numbers are the game's own (docs/ROAD_ENGINE_E5.md section 6); a model not yet built uses a stated design value.</summary>
    public sealed class DesignVehicle
    {
        public string Name; public float Width, Length, Wheelbase, FrontOverhang, MaxSteerDeg;
        public float RearOverhang => Length - Wheelbase - FrontOverhang;
        public float MinTurnRadius => Wheelbase / (float)Math.Tan(MaxSteerDeg * Math.PI / 180.0);          // rear axle centre, full lock
        public float MinFrontTurnRadius => Wheelbase / (float)Math.Sin(MaxSteerDeg * Math.PI / 180.0);     // front axle centre, full lock (the path a driver steers: the front axle follows the lane)
        /// <summary>The radius of the turn onto and off a ring that a driver takes with this vehicle: a good margin over full lock, and never tighter than 12 m.</summary>
        public float EasyTurnRadius => Math.Max(12f, MinFrontTurnRadius * 1.15f);

        /// <summary>The game's placeholder truck (TruckFactory: body 2.4 x 6 m, wheels 4.2 m apart, VehicleDefinition steer 32 degrees).</summary>
        public static readonly DesignVehicle TruckLight = new DesignVehicle { Name = "truck_light_01", Width = 2.4f, Length = 6.0f, Wheelbase = 4.2f, FrontOverhang = 0.4f, MaxSteerDeg = 32f };
        /// <summary>Design value for the 6x2 medium truck (no model yet): 2.5 x 9 m, wheelbase 5.6 m, steer 28 degrees (SceneBuilder).</summary>
        public static readonly DesignVehicle TruckMedium = new DesignVehicle { Name = "truck_medium_01", Width = 2.5f, Length = 9.0f, Wheelbase = 5.6f, FrontOverhang = 1.3f, MaxSteerDeg = 28f };
        /// <summary>The city bus placeholder (TruckFactory: body 2.5 x 10 m, wheels 8.2 m apart; steer 30 degrees).</summary>
        public static readonly DesignVehicle Bus = new DesignVehicle { Name = "bus_city_01", Width = 2.5f, Length = 10.0f, Wheelbase = 8.2f, FrontOverhang = 0.4f, MaxSteerDeg = 30f };
        /// <summary>A passenger car, the other traffic of the roads.</summary>
        public static readonly DesignVehicle Car = new DesignVehicle { Name = "car", Width = 1.9f, Length = 4.7f, Wheelbase = 2.8f, FrontOverhang = 0.95f, MaxSteerDeg = 35f };
        public static IEnumerable<DesignVehicle> All { get { yield return Car; yield return TruckLight; yield return TruckMedium; yield return Bus; } }
    }

    public sealed class SweepResult
    {
        public bool Reached;                 // the vehicle got to the end of the path
        public float MaxSteerDeg;            // the largest steering angle the path demanded (not clamped)
        public float MaxPathError;           // the largest distance from the path centre line at the rear axle, metres
        public float OverrunMetres;          // path length driven with some part of the body off the pavement but still on a road formation (a kerb run over)
        public float OverrunDepth;           // how far outside the pavement the body reached at the worst moment, metres (the body's overhang: allowed over a kerb)
        public float WheelDepth;             // how far outside the pavement a WHEEL reached at the worst moment, metres (a wheel on the kerb or the walkway edge: a rider, not a crash)
        public float StruckMetres;           // path length driven with some part of the body on the island core or off every road (a collision)
        public string Why = "";
    }

    /// <summary>
    /// A swept-path check: drives a design vehicle along a path with pure pursuit on a kinematic bicycle model and measures what the body sweeps over. It proves that a design is passable for
    /// cars, trucks and buses (turning radius, tail swing, island clearance); it does not replace driving it.
    /// </summary>
    public static class SweptPath
    {
        public const float Step = 0.25f;

        /// <summary>
        /// The centre line of a vehicle going in on `entry`, round the ring on lane `ringLane` (0 = innermost) and out on `exit`; legs are used on their inbound/outbound lane `legLane` (1 = nearest the centre line).
        /// The turn onto and off the ring is a circular arc of `turnRadius` metres (the rear axle's path), tangent to the leg's lane line and to the ring lane: how a driver takes a roundabout, with no sharp corner.
        /// </summary>
        public static List<V2> RingPath(RouteModel m, Roundabout r, RingLegInfo entry, RingLegInfo exit, int ringLane, int legLane = 1, float turnRadius = 12f, float approach = 60f)
        {
            var path = new List<V2>();
            float ein = LaneOffset(m, entry, legLane), eout = LaneOffset(m, exit, legLane);
            float rl = r.LaneRadius(Math.Max(0, Math.Min(r.Lanes - 1, ringLane))), rt = turnRadius;
            V2 O = new V2(r.Cx, r.Cz);
            V2 Radial(float a) => new V2((float)Math.Cos(a), (float)Math.Sin(a));
            V2 Tang(float a) => new V2(-(float)Math.Sin(a), (float)Math.Cos(a));
            V2 Pt(V2 o, V2 u, float k, V2 w, float j) => new V2(o.X + u.X * k + w.X * j, o.Y + u.Y * k + w.Y * j);

            // entry: the arc's centre lies rt to the driver's right (the counter-clockwise tangent side) of the lane line and rt + rl from the ring's centre
            ein = SettleOffset(m, entry, ein, rt, rl, r.OuterPavedRadius);
            eout = SettleOffset(m, exit, eout, rt, rl, r.OuterPavedRadius);
            float rcIn = (float)Math.Sqrt(Math.Max(1f, (rt + rl) * (rt + rl) - (ein + rt) * (ein + rt)));
            float phiIn = entry.Angle + (float)Math.Atan2(ein + rt, rcIn);
            var cIn = Pt(O, Radial(entry.Angle), rcIn, Tang(entry.Angle), ein + rt);
            // exit: the centre lies rt to the driver's right (the clockwise tangent side) of the outbound lane line
            float rcOut = (float)Math.Sqrt(Math.Max(1f, (rt + rl) * (rt + rl) - (eout + rt) * (eout + rt)));
            float phiOut = exit.Angle - (float)Math.Atan2(eout + rt, rcOut);
            var cOut = Pt(O, Radial(exit.Angle), rcOut, Tang(exit.Angle), -(eout + rt));

            // approach: along the leg's own lane (its centre line plus the lane offset), up to where the entry arc begins; a curved ramp is followed, a straight leg is a straight line
            var cEntry = m.CorridorById(entry.CorridorId);
            float dIn = rcIn - r.OuterPavedRadius;
            for (float d = approach; d > dIn; d -= 2f) path.Add(LanePoint(cEntry, entry, d, ein, true, dIn));
            var startIn = Pt(O, Radial(entry.Angle), rcIn, Tang(entry.Angle), ein);
            var tIn = Pt(O, Radial(phiIn), rl, Tang(phiIn), 0f);
            ArcClockwise(path, cIn, rt, (float)Math.Atan2(startIn.Y - cIn.Y, startIn.X - cIn.X), (float)Math.Atan2(tIn.Y - cIn.Y, tIn.X - cIn.X));
            float sweep = phiOut - phiIn; while (sweep < 0f) sweep += 2f * (float)Math.PI; while (sweep >= 2f * (float)Math.PI) sweep -= 2f * (float)Math.PI;
            int n = Math.Max(2, (int)Math.Ceiling(sweep * rl / 1.5f));
            for (int i = 0; i <= n; i++) { float a = phiIn + sweep * i / n; path.Add(Pt(O, Radial(a), rl, Tang(a), 0f)); }
            var tOut = Pt(O, Radial(phiOut), rl, Tang(phiOut), 0f);
            var endOut = Pt(O, Radial(exit.Angle), rcOut, Tang(exit.Angle), -eout);
            ArcClockwise(path, cOut, rt, (float)Math.Atan2(tOut.Y - cOut.Y, tOut.X - cOut.X), (float)Math.Atan2(endOut.Y - cOut.Y, endOut.X - cOut.X));
            var cExit = m.CorridorById(exit.CorridorId);
            float dOut = rcOut - r.OuterPavedRadius;
            for (float d = dOut + 2f; d <= approach; d += 2f) path.Add(LanePoint(cExit, exit, d, eout, false, dOut));
            return path;
        }

        /// <summary>The lane offset where the entry arc starts (further out than the mouth, so narrower there); found by iteration.</summary>
        static float SettleOffset(RouteModel m, RingLegInfo leg, float mouthOffset, float rt, float rl, float rout)
        {
            var c = m.CorridorById(leg.CorridorId); var sec = (leg.AtStart ? c.Runs[0] : c.Runs[c.Runs.Length - 1]).Section;
            float k = sec.PavedHalfWidth > 0.01f ? leg.HalfPaved / sec.PavedHalfWidth : 1f, baseOff = mouthOffset / k, e = mouthOffset;
            for (int i = 0; i < 4; i++)
            {
                float rc = (float)Math.Sqrt(Math.Max(1f, (rt + rl) * (rt + rl) - (e + rt) * (e + rt)));
                float d = Math.Max(0f, rc - rout), sOnLeg = leg.AtStart ? Math.Min(c.Length, d) : Math.Max(0f, c.Length - d);
                e = baseOff * c.ScaleAt(sOnLeg);
            }
            return e;
        }

        /// <summary>A point on a leg's lane, `d` metres out from the ring along the leg's own centre line. inbound = the lane that leads to the ring.</summary>
        static V2 LanePoint(RoadCorridor c, RingLegInfo leg, float d, float offset, bool inbound, float startD)
        {
            float sOnLeg = leg.AtStart ? Math.Min(c.Length, d) : Math.Max(0f, c.Length - d);
            c.Spline.FrameAt(sOnLeg, out var pos, out _, out var right);
            // lateral of the corridor (+ = right of increasing s): an inbound vehicle on a leg that starts at the ring travels towards the start, so its right is the corridor's left
            float lat = (inbound == leg.AtStart) ? -offset : offset;
            lat *= c.ScaleAt(sOnLeg) / Math.Max(0.01f, c.ScaleAt(leg.AtStart ? Math.Min(c.Length, startD) : Math.Max(0f, c.Length - startD)));   // `offset` is the lane offset at the arc start (startD out): keep that lane, scaled by how the road narrows beyond it
            return new V2(pos.X + right.X * lat, pos.Z + right.Z * lat);
        }

        static void ArcClockwise(List<V2> path, V2 c, float r, float a0, float a1)
        {
            float d = a0 - a1; while (d < 0f) d += 2f * (float)Math.PI; while (d >= 2f * (float)Math.PI) d -= 2f * (float)Math.PI;
            int n = Math.Max(2, (int)Math.Ceiling(d * r / 1.0f));
            for (int i = 0; i <= n; i++) { float a = a0 - d * i / n; path.Add(new V2(c.X + r * (float)Math.Cos(a), c.Y + r * (float)Math.Sin(a))); }
        }

        /// <summary>The centre line of a lane along a corridor from station s0 to s1 (s0 > s1 drives it backwards): `offset` is lateral, positive = right of increasing s.</summary>
        public static List<V2> LanePath(RoadCorridor c, float offset, float s0, float s1, float step = 2f)
        {
            var path = new List<V2>(); float dir = s1 >= s0 ? 1f : -1f, len = Math.Abs(s1 - s0); int n = Math.Max(2, (int)Math.Ceiling(len / step));
            for (int i = 0; i <= n; i++)
            {
                float s = s0 + dir * len * i / n; c.Spline.FrameAt(s, out var pos, out _, out var right);
                float k = c.ScaleAt(s); path.Add(new V2(pos.X + right.X * offset * k, pos.Z + right.Z * offset * k));
            }
            return path;
        }

        /// <summary>Lateral offset (always positive) of lane `lane` (1 = next to the centre line) from the leg's centre line.</summary>
        static float LaneOffset(RouteModel m, RingLegInfo leg, int lane)
        {
            var c = m.CorridorById(leg.CorridorId); var sec = c.Runs[0].Section;
            var pos = sec.LaneCentres.Where(x => x > 0f).OrderBy(x => x).ToArray();
            float k = sec.PavedHalfWidth > 0.01f ? leg.HalfPaved / sec.PavedHalfWidth : 1f;          // the mouth may be wider than the road (RingSpec.mouthScale)
            return pos[Math.Max(0, Math.Min(pos.Length - 1, lane - 1))] * k;
        }

        public static SweepResult Drive(RouteModel m, DesignVehicle v, IList<V2> path)
        {
            var res = new SweepResult();
            var pts = Resample(path, 0.5f); if (pts.Count < 4) { res.Why = "path too short"; return res; }
            float th = (float)Math.Atan2(pts[1].Y - pts[0].Y, pts[1].X - pts[0].X);
            // the path is the FRONT axle's (a driver steers the front wheels along the lane); the rear axle follows inside it
            float fx0 = pts[0].X, fz0 = pts[0].Y;
            float x = fx0 - v.Wheelbase * (float)Math.Cos(th), z = fz0 - v.Wheelbase * (float)Math.Sin(th);
            int idx = 0; int guard = 0; const float k = 1.6f, speed = 4f;
            var last = pts[pts.Count - 1];
            while (guard++ < 80000)
            {
                float fx = x + v.Wheelbase * (float)Math.Cos(th), fz = z + v.Wheelbase * (float)Math.Sin(th);
                float best = float.MaxValue; int bi = idx;
                for (int i = idx; i < Math.Min(pts.Count - 1, idx + 60); i++) { float d = Sq(pts[i].X - fx, pts[i].Y - fz); if (d < best) { best = d; bi = i; } }
                idx = bi;
                if (idx >= pts.Count - 3 && Sq(last.X - fx, last.Y - fz) < 4f) { res.Reached = true; break; }
                int j = Math.Min(pts.Count - 1, idx + 1);
                float phi = (float)Math.Atan2(pts[j].Y - pts[idx].Y, pts[j].X - pts[idx].X);
                float e = (fx - pts[idx].X) * -(float)Math.Sin(phi) + (fz - pts[idx].Y) * (float)Math.Cos(phi);        // + = left of the path
                res.MaxPathError = Math.Max(res.MaxPathError, Math.Abs(e));
                float delta = Norm(phi - th) - (float)Math.Atan2(k * e, speed);
                float steerDeg = Math.Abs(delta) * 180f / (float)Math.PI; res.MaxSteerDeg = Math.Max(res.MaxSteerDeg, steerDeg);
                float lim = v.MaxSteerDeg * (float)Math.PI / 180f; if (delta > lim) delta = lim; if (delta < -lim) delta = -lim;
                x += Step * (float)Math.Cos(th); z += Step * (float)Math.Sin(th); th += Step / v.Wheelbase * (float)Math.Tan(delta);
                bool off = false, struck = false;
                foreach (var q in Footprint(v, x, z, th))
                {
                    float ex = m.PavedExcess(q.X, q.Y);
                    if (ex <= 0.02f) continue;
                    off = true; res.OverrunDepth = Math.Max(res.OverrunDepth, Math.Min(ex, 50f));
                    if (OnIslandCore(m, q.X, q.Y) || !m.Terrain.InsideFormation(q.X, q.Y, 0.5f)) struck = true;       // the body may overhang a kerb; it may not enter the island or leave the road
                }
                foreach (var q in Wheels(v, x, z, th)) res.WheelDepth = Math.Max(res.WheelDepth, Math.Min(m.PavedExcess(q.X, q.Y), 50f));
                if (struck) res.StruckMetres += Step; else if (off) res.OverrunMetres += Step;
            }
            if (!res.Reached) res.Why = "did not reach the end of the path (the largest steering demanded was " + res.MaxSteerDeg.ToString("0") + " degrees against " + v.MaxSteerDeg + ")";
            return res;
        }

        public static bool OnIslandCore(RouteModel m, float x, float z)
        {
            foreach (var r in m.Rings) { float d = (float)Math.Sqrt((x - r.Cx) * (x - r.Cx) + (z - r.Cz) * (z - r.Cz)); if (d < r.Island.Radius - r.Island.Apron) return true; }
            return false;
        }

        /// <summary>The four wheel contact points (axles at the rear axle and one wheelbase ahead of it, 0.15 m in from the side of the body).</summary>
        public static IEnumerable<V2> Wheels(DesignVehicle v, float x, float z, float th)
        {
            float c = (float)Math.Cos(th), s = (float)Math.Sin(th), hw = v.Width * 0.5f - 0.15f;
            foreach (var fx in new[] { 0f, v.Wheelbase })
                foreach (var lat in new[] { -hw, hw })
                    yield return new V2(x + c * fx - s * lat, z + s * fx + c * lat);
        }

        /// <summary>Corners and mid-edge points of the body at rear-axle position (x, z) and heading th.</summary>
        public static IEnumerable<V2> Footprint(DesignVehicle v, float x, float z, float th)
        {
            float c = (float)Math.Cos(th), s = (float)Math.Sin(th), hw = v.Width * 0.5f, front = v.Wheelbase + v.FrontOverhang, rear = -v.RearOverhang;
            foreach (var fx in new[] { front, (front + rear) * 0.5f, rear })
                foreach (var lat in new[] { -hw, hw })
                    yield return new V2(x + c * fx - s * lat, z + s * fx + c * lat);
            yield return new V2(x + c * front, z + s * front);
            yield return new V2(x + c * rear, z + s * rear);
        }

        static float Sq(float a, float b) => a * a + b * b;
        static float Norm(float a) { while (a > Math.PI) a -= 2f * (float)Math.PI; while (a < -Math.PI) a += 2f * (float)Math.PI; return a; }

        static List<V2> Resample(IList<V2> p, float step)
        {
            var r = new List<V2> { p[0] };
            float carry = 0f;
            for (int i = 1; i < p.Count; i++)
            {
                float dx = p[i].X - p[i - 1].X, dz = p[i].Y - p[i - 1].Y, len = (float)Math.Sqrt(dx * dx + dz * dz); if (len < 1e-4f) continue;
                float t = step - carry;
                while (t <= len) { r.Add(new V2(p[i - 1].X + dx * t / len, p[i - 1].Y + dz * t / len)); t += step; }
                carry = len - (t - step);
            }
            if (Sq(r[r.Count - 1].X - p[p.Count - 1].X, r[r.Count - 1].Y - p[p.Count - 1].Y) > 0.01f) r.Add(p[p.Count - 1]);
            return r;
        }
    }
}
