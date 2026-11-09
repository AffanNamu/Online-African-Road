using System;
using System.Collections.Generic;
using System.Linq;

namespace ARO.NetCore
{
    // ====================================================================================================================================================================================
    // E5 network checks (docs/ROAD_ENGINE_E5.md section 5). They run when a route with rings, extra roads or decks is built and throw like the site-plate check does; a route that declares none of those
    // is never checked (its geometry is the golden master). Pure: tested without Unity.
    // ====================================================================================================================================================================================
    public sealed partial class RouteModel
    {
        /// <summary>Vertical clearance a deck must leave above the road under it: the tallest design vehicle (a truck or double-deck bus, 4.6 m) plus a safety margin; measured from the lower surface to the underside of the deck slab.</summary>
        public const float MinClearance = 5.5f;
        /// <summary>Grade limit of ramps and flyover roads (the engine's general maximum is 8 percent; ramps are held to 6 so a loaded truck can climb them).</summary>
        public const float MaxRampGrade = 0.06f;
        /// <summary>Two centre lines closer than this in height count as the same level: an at-grade overlap.</summary>
        public const float SameLevelTolerance = 1.0f;

        public bool IsE5Corridor(RoadCorridor c) => c.E5;

        /// <summary>True when (x, z) lies on the paved part of any road (the carriageway and its shoulders, kerbs and walkways excluded) or on a roundabout's apron: where a vehicle may drive without mounting a kerb.</summary>
        public bool IsPaved(float x, float z)
        {
            foreach (var c in CorridorsNear(x, z))
            {
                if (!c.Spline.Nearest(x, z, c.MaxFormationHalf + 2f, out float s, out float lat, out _)) continue;
                if (s <= 1e-3f || s >= c.Spline.Length - 1e-3f) continue;
                if (Math.Abs(lat) <= c.HalfPavedAt(s)) return true;
            }
            foreach (var r in Rings)
            {
                float d = (float)Math.Sqrt((x - r.Cx) * (x - r.Cx) + (z - r.Cz) * (z - r.Cz));
                if (r.Island.Apron > 0.01f && d <= r.Island.Radius && d >= r.Island.Radius - r.Island.Apron) return true;
            }
            return false;
        }

        /// <summary>Why this network cannot be built: legs that do not meet their ring, roads that cross or overlap without a deck, decks that leave too little headroom, ramps that are too steep, piers that stand on a road.</summary>
        public List<string> CheckNetwork()
        {
            var e = new List<string>();
            foreach (var r in Rings) CheckRing(r, e);
            CheckGrades(e);
            CheckPairs(e);
            CheckPiers(e);
            return e.Distinct().ToList();
        }

        void CheckRing(Roundabout r, List<string> e)
        {
            string tag = "ring '" + r.Id + "'";
            if (r.Spec.radius < 4f || r.Spec.radius > 80f) e.Add(tag + ": island radius must be 4..80 m");
            if (r.CentreRadius < 12f) e.Add(tag + ": the centre line of the carriageway needs a radius of at least 12 m (got " + r.CentreRadius.ToString("0.0") + ")");
            if (r.Legs.Count < 3) e.Add(tag + ": a roundabout needs at least three legs (got " + r.Legs.Count + ")");
            if (r.Spec.apron < 0f || r.Spec.apron > r.Spec.radius * 0.5f) e.Add(tag + ": apron must be 0 .. half the island radius");
            if (r.Spec.flare < 0f || r.Spec.flare > 20f) e.Add(tag + ": flare must be 0..20 m");
            if (r.Spec.flare > 0f && r.Section.HasKerb) e.Add(tag + ": a flared entry is for an unkerbed ring (a kerbed mouth has a dropped kerb instead)");
            if (r.Spec.splitterLength != 0f && (r.Spec.splitterLength < 6f || r.Spec.splitterLength > 40f)) e.Add(tag + ": splitterLength must be 0 or 6..40 m");
            if (r.Spec.splitterLength > 0f && (r.Spec.splitterWidth < 1.2f || r.Spec.splitterWidth > 8f)) e.Add(tag + ": splitterWidth must be 1.2..8 m");
            var ring = r.Corridor;
            for (int i = 0; i < r.Legs.Count; i++)
            {
                var leg = r.Legs[i]; var c = CorridorById(leg.CorridorId); string lt = tag + ", leg '" + leg.CorridorId + "'";
                if (c == null) { e.Add(lt + ": corridor missing"); continue; }
                var p = c.Spline.PositionAt(leg.AtStart ? 0f : c.Length);
                float d = (float)Math.Sqrt((p.X - r.Cx) * (p.X - r.Cx) + (p.Z - r.Cz) * (p.Z - r.Cz));
                if (Math.Abs(d - r.OuterPavedRadius) > 0.4f) e.Add(lt + ": its end is " + d.ToString("0.00") + " m from the island centre, the ring's outer paved edge is at " + r.OuterPavedRadius.ToString("0.00") + " m");
                float yRing = ring.SurfaceY(leg.RingS, r.Section.PavedHalfWidth);
                if (Math.Abs(p.Y - yRing) > 0.25f) e.Add(lt + ": it meets the ring " + (p.Y - yRing).ToString("+0.00;-0.00") + " m above/below the ring surface (limit 0.25 m)");
                c.Spline.FrameAt(leg.AtStart ? 0f : c.Length, out _, out var f, out _);
                float fx = leg.AtStart ? f.X : -f.X, fz = leg.AtStart ? f.Z : -f.Z, rx = (float)Math.Cos(leg.Angle), rz = (float)Math.Sin(leg.Angle);
                float cosA = Math.Max(-1f, Math.Min(1f, fx * rx + fz * rz));
                if ((float)Math.Acos(cosA) * 180f / (float)Math.PI > 12f) e.Add(lt + ": it does not leave the ring radially (more than 12 degrees off)");
                // mouths must not overlap: the arc between two neighbouring legs at the outer edge has to hold both formations
                var next = r.Legs[(i + 1) % r.Legs.Count];
                if (r.Legs.Count > 1)
                {
                    var order = r.Legs.OrderBy(l => l.RingS).ToList(); int k = order.FindIndex(l => l.CorridorId == leg.CorridorId); var nx = order[(k + 1) % order.Count];
                    if (nx.CorridorId != leg.CorridorId)
                    {
                        float da = nx.Angle - leg.Angle; da = (float)(da % (2 * Math.PI)); if (da < 0) da += 2f * (float)Math.PI;
                        float chord = da * r.OuterPavedRadius;
                        if (chord < leg.HalfFormation + nx.HalfFormation + 2f && order.Count > 1) e.Add(lt + ": too close to leg '" + nx.CorridorId + "' (" + chord.ToString("0.0") + " m of ring between them, needs " + (leg.HalfFormation + nx.HalfFormation + 2f).ToString("0.0") + ")");
                    }
                }
            }
        }

        void CheckGrades(List<string> e)
        {
            var roles = (Spec.roads ?? new RoadSpec[0]).ToDictionary(r => r.id, r => r.role);
            foreach (var c in AllCorridors.Where(x => x.E5))
            {
                if (c.IsRing) continue;
                string role = roles.TryGetValue(c.Id, out var rr) ? rr : "road";
                float limit = role == "ramp" || role == "flyover" ? MaxRampGrade : RouteSpecValidator.MaxGrade;
                float g = c.Spline.MaxGrade();
                if (g > limit + 1e-4f) e.Add("road '" + c.Id + "' (" + role + "): steepest grade is " + (g * 100f).ToString("0.0") + "% (limit " + (limit * 100f).ToString("0") + "%)");
                float rad = c.Spline.MinRadius();
                if (rad < RouteSpecValidator.MinRadiusMetres && c.Length > 30f && !(c.Runs.Length == 1 && c.Spline.ControlCount <= 3 && IsStraight(c))) e.Add("road '" + c.Id + "': tightest curve has a radius of " + rad.ToString("0") + " m (minimum " + RouteSpecValidator.MinRadiusMetres + " m)");
            }
            foreach (var c in AllCorridors)
                foreach (var b in c.Bridges)
                    if (b.overRoad && c.Spline.MaxGradeBetween(b.startS, b.endS) > 0.04f) e.Add("deck '" + b.id + "' on '" + c.Id + "': a deck must be nearly level (grade <= 4%)");
        }

        static bool IsStraight(RoadCorridor c)
        {
            var a = c.Spline.PositionAt(0f); var b = c.Spline.PositionAt(c.Length); var m = c.Spline.PositionAt(c.Length * 0.5f);
            float dx = b.X - a.X, dz = b.Z - a.Z, l = (float)Math.Sqrt(dx * dx + dz * dz); if (l < 1e-3f) return false;
            return Math.Abs((m.X - a.X) * dz - (m.Z - a.Z) * dx) / l < 0.05f;
        }

        bool Connected(RoadCorridor a, RoadCorridor b, float x, float z)
        {
            foreach (var cn in Connections)
            {
                if (!((cn.A == a.Id && cn.B == b.Id) || (cn.A == b.Id && cn.B == a.Id))) continue;
                float dx = x - cn.X, dz = z - cn.Z;
                if (dx * dx + dz * dz <= cn.Radius * cn.Radius) return true;
            }
            return false;
        }

        void CheckPairs(List<string> e)
        {
            var all = AllCorridors.ToArray();
            for (int i = 0; i < all.Length; i++)
                for (int j = 0; j < all.Length; j++)
                {
                    if (i == j) continue;
                    var a = all[i]; var b = all[j];
                    if (!(a.E5 || b.E5)) continue;                                      // two corridors of an existing route are not E5's business (the golden master)
                    if (a.IsRing && b.IsRing && string.CompareOrdinal(a.Id, b.Id) > 0) continue;
                    if (j < i && (a.E5 && b.E5)) { }                                    // both directions are checked: a's centre on b's pavement, and b's on a's
                    for (float s = 0f; s <= a.Length; s += 3f)
                    {
                        var p = a.Spline.PositionAt(s);
                        if (!b.Spline.Nearest(p.X, p.Z, b.MaxFormationHalf + 1f, out float sb, out float lat, out _)) continue;
                        if (sb <= 1e-3f || sb >= b.Length - 1e-3f) continue;           // beyond the end of b is not on b
                        float half = b.HalfPavedAt(sb);
                        if (Math.Abs(lat) > half) continue;
                        if (Connected(a, b, p.X, p.Z)) continue;
                        float yb = b.SurfaceY(sb, lat), ya = p.Y + 0f;
                        float dy = ya - yb;
                        string pair = "roads '" + a.Id + "' and '" + b.Id + "' at (" + p.X.ToString("0") + ", " + p.Z.ToString("0") + ")";
                        if (Math.Abs(dy) < SameLevelTolerance) { e.Add(pair + " overlap at the same level (" + dy.ToString("+0.00;-0.00") + " m) without a junction between them"); continue; }
                        // the upper road must be on a deck; only the pair's upper member is judged (from its side)
                        if (dy < 0f) continue;
                        if (!a.OnDeck(s)) { e.Add(pair + ": road '" + a.Id + "' passes " + dy.ToString("0.0") + " m above '" + b.Id + "' without a deck (declare a deck with overRoad)"); continue; }
                        a.InBridge(s, out var br);
                        float clear = dy - br.deckThickness;
                        if (clear < MinClearance) e.Add(pair + ": headroom under the deck of '" + a.Id + "' is " + clear.ToString("0.0") + " m (minimum " + MinClearance.ToString("0.0") + " m)");
                    }
                }
        }

        void CheckPiers(List<string> e)
        {
            foreach (var c in AllCorridors)
                foreach (var b in c.Bridges)
                {
                    if (!b.overRoad) continue;
                    for (float s = b.startS + b.pierSpacing; s < b.endS - 4f; s += b.pierSpacing)
                    {
                        if (b.pierGaps != null && b.pierGaps.Any(g => s >= g.from && s <= g.to)) continue;
                        int ri = c.RunIndexAt(s); var sec = c.Runs[ri].Section; float k = c.ScaleAt(ri, s);
                        c.Spline.FrameAt(s, out var pos, out _, out var right);
                        foreach (int side in new[] { -1, 1 })
                        {
                            float x = side * sec.PavedHalfWidth * 0.5f * k, px = pos.X + right.X * x, pz = pos.Z + right.Z * x;
                            foreach (var o in CorridorsNear(px, pz))
                            {
                                if (o == c) continue;
                                if (!o.Spline.Nearest(px, pz, o.MaxFormationHalf + 1f, out float so, out float lat, out _)) continue;
                                if (so <= 1e-3f || so >= o.Length - 1e-3f) continue;
                                if (Math.Abs(lat) <= o.HalfFormationAt(so)) e.Add("deck '" + b.id + "' of '" + c.Id + "': a pier at s=" + s.ToString("0") + " stands on road '" + o.Id + "' (add a pierGap over it)");
                            }
                        }
                    }
                }
        }
    }
}
