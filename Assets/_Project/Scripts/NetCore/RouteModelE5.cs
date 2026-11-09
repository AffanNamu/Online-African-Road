using System;
using System.Collections.Generic;
using System.Linq;

namespace ARO.NetCore
{
    // ====================================================================================================================================================================================
    // E5 (docs/ROAD_ENGINE_E5.md): roundabouts and grade separation. Everything here is built from `RouteSpec.rings` and `RouteSpec.roads` (schemaVersion 3); a route that declares neither never reaches this
    // code, which is why the golden master of every existing route cannot change.
    // ====================================================================================================================================================================================

    /// <summary>The circular central island of a roundabout: a flat disc (its rim is the kerb of the ring's inner edge), optionally with a paved apron. Answers the same questions a site plate does.</summary>
    public sealed class RingIsland
    {
        public string Id; public RingSpec Spec;
        public float Cx, Cz, Radius, Apron, TopY;
        public int CoreSub, ApronSub;
        public float SignedDistance(float x, float z) { float dx = x - Cx, dz = z - Cz; return (float)Math.Sqrt(dx * dx + dz * dz) - Radius; }
        public bool Contains(float x, float z, float margin = 0f) => SignedDistance(x, z) <= margin;

        public static int CoverSub(string cover) => cover == "paved" ? RoadSub.Sidewalk : cover == "earth" ? RoadSub.Verge : RoadSub.Planter;

        /// <summary>The island's own props (monument, trees, signs), x and z being metres east and north of the island centre.</summary>
        public IEnumerable<PropInstance> Props(float minX, float minZ, float maxX, float maxZ)
        {
            foreach (var p in Spec.props ?? new SitePropSpec[0])
            {
                float wx = Cx + p.x, wz = Cz + p.z;
                if (wx < minX || wx >= maxX || wz < minZ || wz >= maxZ) continue;
                yield return new PropInstance { Prop = p.prop, Origin = Spec.id + "/" + p.id, Text = p.text, Pos = new V3(wx, TopY, wz), YawDeg = p.yawDeg, Scale = p.scale > 0f ? p.scale : 1f };
            }
        }
    }

    /// <summary>One road that meets a ring: where (angle, station on the ring), which corridor, and which end of it touches the ring.</summary>
    public struct RingLegInfo
    {
        public string CorridorId; public bool AtStart; public float Angle, RingS, HalfPaved, HalfFormation; public bool Generated;
    }

    /// <summary>A raised splitter island in the middle of a two-way leg, next to the ring: a teardrop that makes entering traffic turn right and keeps it from cutting the island.</summary>
    public struct RingSplitter { public string CorridorId; public bool AtStart; public float Length, Width; }

    /// <summary>A built roundabout: the circulating corridor, the island, the legs and the splitters. Pure data; the mesh is made by RingGeometry.</summary>
    public sealed class Roundabout
    {
        public RingSpec Spec; public RoadCorridor Corridor; public RingIsland Island; public RoadCrossSection Section;
        public float Cx, Cz, CentreY, CentreRadius, OuterPavedRadius, StartRad;
        public readonly List<RingLegInfo> Legs = new List<RingLegInfo>();
        public readonly List<RingSplitter> Splitters = new List<RingSplitter>();
        public string Id => Spec.id;
        /// <summary>Number of circulating lanes (all lanes of the profile circulate the same way).</summary>
        public int Lanes => Section.LaneCentres.Length;
        /// <summary>Radius of the centre of circulating lane i (0 = innermost) from the centre of the island.</summary>
        public float LaneRadius(int i) => CentreRadius + Section.LaneCentres[i];
        public float InnerPavedRadius => CentreRadius - Section.PavedHalfWidth;

        public float AngleToS(float angle)
        {
            double a = (angle - StartRad) % (2.0 * Math.PI); if (a < 0) a += 2.0 * Math.PI;
            return (float)(a / (2.0 * Math.PI)) * Corridor.Length;
        }
        public float SToAngle(float s) => StartRad + 2f * (float)Math.PI * s / Corridor.Length;

        internal static Roundabout Build(RouteSpec spec, RingSpec rs, Dictionary<string, RoadCrossSection> sections, Func<string, RoadCorridor> find, List<RoadCorridor> sides)
        {
            var sec = sections[rs.profile];
            float rc = rs.radius + sec.FormationHalfWidth, rout = rc + sec.PavedHalfWidth;
            float cx = rs.x, cz = rs.z, cy = rs.elevation;
            RoadCorridor attached = null; bool attachStart = false; float attachAngle = 0f;
            if (!string.IsNullOrEmpty(rs.attachTo))
            {
                attached = find(rs.attachTo);
                if (attached == null) throw new ArgumentException("ring '" + rs.id + "': attachTo '" + rs.attachTo + "' is not a corridor");
                attachStart = rs.attachEnd == "start";
                attached.Spline.FrameAt(attachStart ? 0f : attached.Length, out var p, out var f, out _);
                float dx = attachStart ? -f.X : f.X, dz = attachStart ? -f.Z : f.Z, dl = (float)Math.Sqrt(dx * dx + dz * dz); dx /= dl; dz /= dl;
                cx = p.X + dx * rout; cz = p.Z + dz * rout; cy = p.Y - sec.HeightAt(sec.PavedHalfWidth);
                attachAngle = (float)Math.Atan2(-dz, -dx);                     // the bearing of the attach point seen from the ring's centre
            }

            // the ring's own station zero lies in the middle of the widest gap between legs, so no mouth and no flare ever straddles the seam
            var angles = new List<float>();
            if (attached != null) angles.Add(attachAngle);
            foreach (var l in rs.legs ?? new RingLegSpec[0]) angles.Add(l.angleDeg * (float)Math.PI / 180f);
            foreach (var cid in rs.connect ?? new string[0])
            {
                var c = find(cid); if (c == null) continue;
                angles.Add(AngleOfEnd(c, cx, cz, rout, out _));
            }
            float startRad = 0f;
            if (angles.Count > 0)
            {
                var sorted = angles.Select(a => Norm(a)).OrderBy(a => a).ToList(); float best = -1f;
                for (int i = 0; i < sorted.Count; i++)
                {
                    float a0 = sorted[i], a1 = i + 1 < sorted.Count ? sorted[i + 1] : sorted[0] + 2f * (float)Math.PI, gap = a1 - a0;
                    if (gap > best) { best = gap; startRad = Norm(a0 + gap * 0.5f); }
                }
            }

            Zone zone = Zone.Urban; Surface surface = Surface.Good;
            RouteVocab.TryZone(rs.zone, out zone); RouteVocab.TrySurface(rs.surface, out surface);
            var spline = RoadSpline.Circle(cx, cz, rc, cy, startRad);
            var ring = new RoadCorridor
            {
                Id = rs.id, IsMain = false, IsRing = true, E5 = true, NoStopLine = true, StepOverride = 1.5f, Spline = spline, Seed = spec.seed ^ Mathx.StableHash(rs.id),
                Runs = new[] { new RoadRun { S0 = 0f, S1 = spline.Length, Section = sec, Zone = zone, Surface = surface } }
            };
            ring.BuildRings();
            sides.Add(ring);
            var rb = new Roundabout { Spec = rs, Corridor = ring, Section = sec, Cx = cx, Cz = cz, CentreY = cy, CentreRadius = rc, OuterPavedRadius = rout, StartRad = startRad };
            rb.Island = new RingIsland { Id = rs.id + "_island", Spec = rs, Cx = cx, Cz = cz, Radius = rs.radius, Apron = rs.apron, TopY = cy + sec.EdgeY, CoreSub = RingIsland.CoverSub(rs.island), ApronSub = RoadSub.Sidewalk };

            var gaps = new List<MarkingGap>(); var fillets = new List<Fillet>();
            void AddLeg(RoadCorridor c, bool atStart, float angle, bool generated)
            {
                var runAtRing = atStart ? c.Runs[0] : c.Runs[c.Runs.Length - 1];
                var lsec = runAtRing.Section; float k = rs.mouthScale > 1.001f ? rs.mouthScale : 1f;
                if (k > 1f) { if (atStart) runAtRing.ScaleAtStart = k; else runAtRing.ScaleAtEnd = k; c.InvalidateCaches(); }
                var leg = new RingLegInfo { CorridorId = c.Id, AtStart = atStart, Angle = angle, RingS = rb.AngleToS(angle), HalfPaved = lsec.PavedHalfWidth * k, HalfFormation = lsec.FormationHalfWidth * k, Generated = generated };
                rb.Legs.Add(leg);
                float reach = (leg.HalfFormation + Math.Max(0f, rs.flare)) * rc / rout;
                AddWrapped(gaps, leg.RingS - reach, leg.RingS + reach, 1, ring.Length);
                if (rs.flare > 0f) fillets.Add(new Fillet { S = leg.RingS, Side = 1, Radius = rs.flare, HalfMouth = leg.HalfPaved });
            }

            if (attached != null) AddLeg(attached, attachStart, attachAngle, false);
            foreach (var l in rs.legs ?? new RingLegSpec[0])
            {
                float a = l.angleDeg * (float)Math.PI / 180f, ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                var lsec = sections[l.profile];
                float y0 = ring.SurfaceY(rb.AngleToS(a), sec.PavedHalfWidth);
                var xs = new List<float> { cx + ca * rout, cx + ca * (rout + l.length * 0.5f), cx + ca * (rout + l.length) };
                var zs = new List<float> { cz + sa * rout, cz + sa * (rout + l.length * 0.5f), cz + sa * (rout + l.length) };
                var ys = new List<float> { y0, y0 + l.rise * 0.5f, y0 + l.rise };
                var sp = new RoadSpline(xs, zs, ys);
                Zone lz = zone; Surface ls = surface;
                if (!string.IsNullOrEmpty(l.zone)) RouteVocab.TryZone(l.zone, out lz);
                if (!string.IsNullOrEmpty(l.surface)) RouteVocab.TrySurface(l.surface, out ls);
                var c = new RoadCorridor { Id = l.id, IsMain = false, E5 = true, NoStopLine = true, Spline = sp, Seed = spec.seed ^ Mathx.StableHash(l.id), Runs = new[] { new RoadRun { S0 = 0f, S1 = sp.Length, Section = lsec, Zone = lz, Surface = ls } } };
                c.BuildRings(); sides.Add(c);
                AddLeg(c, true, a, true);
            }
            foreach (var cid in rs.connect ?? new string[0])
            {
                var c = find(cid); if (c == null) throw new ArgumentException("ring '" + rs.id + "': connect '" + cid + "' is not a corridor");
                float a = AngleOfEnd(c, cx, cz, rout, out bool atStart);
                AddLeg(c, atStart, a, false);
            }
            ring.MarkingGaps = gaps.ToArray(); ring.Fillets = fillets.ToArray();

            // splitters and the yield lines and crossings every leg gets (markings are data on the leg's corridor, painted by the same code as any pavement symbol)
            foreach (var leg in rb.Legs)
            {
                var c = find(leg.CorridorId);
                var add = new List<MarkingSymbolSpec>();
                string entry = leg.AtStart ? "left" : "right";             // inbound lanes: the left-hand lanes flow towards the start of a road, the right-hand lanes towards its end
                float at(float d) => leg.AtStart ? d : c.Length - d;
                if (rs.yieldLines) add.Add(new MarkingSymbolSpec { id = rs.id + "_y_" + leg.CorridorId, kind = "yield", corridor = c.Id, s = at(4.5f), side = entry });
                if (rs.zebra) add.Add(new MarkingSymbolSpec { id = rs.id + "_z_" + leg.CorridorId, kind = "zebra", corridor = c.Id, s = at(Math.Max(10f, rs.splitterLength + 4f)), side = "both" });
                if (add.Count > 0) c.Symbols = c.Symbols.Concat(add).ToArray();
                if (rs.splitterLength > 1f && c.Runs[0].Section.Spec.lanesPerDirection >= 1)
                    rb.Splitters.Add(new RingSplitter { CorridorId = c.Id, AtStart = leg.AtStart, Length = rs.splitterLength, Width = rs.splitterWidth });
            }
            return rb;
        }

        static float Norm(float a) { double t = a % (2.0 * Math.PI); if (t < 0) t += 2.0 * Math.PI; return (float)t; }

        /// <summary>The angle (seen from the ring's centre) of the end of corridor c that lies nearer to the ring's outer paved edge.</summary>
        static float AngleOfEnd(RoadCorridor c, float cx, float cz, float rout, out bool atStart)
        {
            var p0 = c.Spline.PositionAt(0f); var p1 = c.Spline.PositionAt(c.Length);
            float d0 = Math.Abs((float)Math.Sqrt((p0.X - cx) * (p0.X - cx) + (p0.Z - cz) * (p0.Z - cz)) - rout);
            float d1 = Math.Abs((float)Math.Sqrt((p1.X - cx) * (p1.X - cx) + (p1.Z - cz) * (p1.Z - cz)) - rout);
            atStart = d0 <= d1; var p = atStart ? p0 : p1;
            return (float)Math.Atan2(p.Z - cz, p.X - cx);
        }

        static void AddWrapped(List<MarkingGap> list, float s0, float s1, int side, float len)
        {
            if (s0 < 0f) { list.Add(new MarkingGap { S0 = s0 + len, S1 = len, Side = side }); s0 = 0f; }
            if (s1 > len) { list.Add(new MarkingGap { S0 = 0f, S1 = s1 - len, Side = side }); s1 = len; }
            list.Add(new MarkingGap { S0 = s0, S1 = s1, Side = side });
        }
    }

    /// <summary>A place where two corridors of the network are meant to touch (a leg and its ring, a slip road and the road it joins, a stub and its main road): overlaps near it are not errors.</summary>
    public struct NetConnection { public string A, B; public float X, Z, Radius; }

    public sealed partial class RouteModel
    {
        public IReadOnlyList<Roundabout> Rings { get; private set; } = new Roundabout[0];
        public IReadOnlyList<RingIsland> Islands { get; private set; } = new RingIsland[0];
        public IReadOnlyList<NetConnection> Connections { get; private set; } = new NetConnection[0];
        /// <summary>True when some corridor runs on a grade-separated deck: ground height queries then have to pick the right level.</summary>
        public bool HasLevels { get; private set; }
        /// <summary>True when the route declares E5 elements; the exact rule for the ends of roads (no flat caps beyond them) then applies, as it does for routes with site plates.</summary>
        public bool ExactEnds => Sites.Count > 0 || Rings.Count > 0 || HasE5Roads;
        public bool HasE5Roads { get; private set; }

        static void BuildE5(RouteSpec spec, RoadCorridor main, Dictionary<string, RoadCrossSection> sections, List<RoadCorridor> sides, out List<Roundabout> rings, out List<NetConnection> connections)
        {
            rings = new List<Roundabout>(); connections = new List<NetConnection>();
            RoadCorridor Find(string id) => main.Id == id ? main : sides.FirstOrDefault(c => c.Id == id);
            foreach (var r in spec.roads ?? new RoadSpec[0])
            {
                var cps = r.controlPoints;
                var spline = new RoadSpline(cps.Select(c => c.x).ToList(), cps.Select(c => c.z).ToList(), cps.Select(c => c.elevation).ToList());
                var corridor = new RoadCorridor { Id = r.id, IsMain = false, E5 = true, NoStopLine = true, Spline = spline, Seed = spec.seed ^ Mathx.StableHash(r.id), Bridges = r.decks ?? new BridgeSpec[0] };
                corridor.Runs = MakeRuns(cps, spline, sections);
                corridor.BuildRings();
                sides.Add(corridor);
            }
            foreach (var r in spec.roads ?? new RoadSpec[0])
            {
                var corridor = Find(r.id);
                foreach (var j in r.joins ?? new RoadJoinSpec[0])
                {
                    var other = Find(j.corridor); if (other == null) continue;
                    var gaps = other.MarkingGaps.ToList();
                    gaps.Add(new MarkingGap { S0 = j.s - j.gap, S1 = j.s + j.gap, Side = j.side >= 0 ? 1 : -1 });
                    other.MarkingGaps = gaps.ToArray();
                    var p = corridor.Spline.PositionAt(j.end == "end" ? corridor.Length : 0f);
                    connections.Add(new NetConnection { A = corridor.Id, B = other.Id, X = p.X, Z = p.Z, Radius = Math.Max(30f, j.gap * 2f + 10f) });
                }
            }
            foreach (var rs in spec.rings ?? new RingSpec[0])
            {
                var rb = Roundabout.Build(spec, rs, sections, Find, sides);
                rings.Add(rb);
                foreach (var leg in rb.Legs)
                {
                    var c = Find(leg.CorridorId); var p = c.Spline.PositionAt(leg.AtStart ? 0f : c.Length);
                    connections.Add(new NetConnection { A = rb.Id, B = leg.CorridorId, X = p.X, Z = p.Z, Radius = 40f });
                }
            }
        }
    }
}
