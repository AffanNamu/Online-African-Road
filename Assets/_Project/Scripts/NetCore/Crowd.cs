using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ARO.NetCore
{
    /// <summary>
    /// One person of a crowd. Either standing at a spot (idle: chatting at a shop front, waiting at a stop, a pump attendant) or walking a path back and forth (a walkway, a forecourt), or walking
    /// a path once from a start time (passengers boarding or leaving a bus). Where they are at a time t is a pure function of t, so every client sees the same person at the same place.
    /// </summary>
    public sealed class CrowdAgent
    {
        public int Design, Colourway, Skin;
        /// <summary>0..1: the person is out when the hour's density is above it (fewer people at night, the same ones every night).</summary>
        public float Rank;
        public string Source;
        // standing
        public V3 Spot; public float Yaw;
        // walking
        public V3[] Path; public float[] Along; public float Speed;
        /// <summary>Phase offset in metres (walkers) or seconds (idle sway).</summary>
        public float Offset;
        /// <summary>Walks the path once, from T0 (boarding and alighting); invisible before T0 - Lead and after arriving.</summary>
        public bool Once; public double T0; public float Lead;
        public bool Walks => Path != null && Path.Length >= 2;
        public float Length => Along == null || Along.Length == 0 ? 0f : Along[Along.Length - 1];
        public V3 Anchor => Walks ? Path[0] : Spot;
        public bool Child => PeopleKit.Designs[((Design % PeopleKit.Designs.Length) + PeopleKit.Designs.Length) % PeopleKit.Designs.Length].Child;

        /// <summary>Where the person is at time t (seconds), facing (degrees, 0 = +z), the animation frame (0..7 walk, 8..11 idle). False when the person is not there (a one-way walk not started or done).</summary>
        public bool At(double t, out V3 pos, out float yawDeg, out int frame)
        {
            if (!Walks)
            {
                pos = Spot; yawDeg = Yaw; double u = (t * 0.22 + Offset) % 1.0; if (u < 0) u += 1.0;
                frame = PeopleKit.WalkFrames + Math.Min(PeopleKit.IdleFrames - 1, (int)(u * PeopleKit.IdleFrames)); return true;
            }
            float L = Length; double d;
            if (Once)
            {
                d = (t - T0) * Speed;
                if (t < T0 - Lead || d > L) { pos = Path[Path.Length - 1]; yawDeg = 0f; frame = 0; return false; }
                if (d < 0) { pos = Path[0]; yawDeg = Heading(0, true); frame = PeopleKit.WalkFrames; return true; }      // waiting to go
            }
            else { d = (t * Speed + Offset) % (2.0 * L); if (d < 0) d += 2.0 * L; }
            bool fwd = d <= L; float x = (float)(fwd ? d : 2.0 * L - d);
            int i = Seg(x); float a = Along[i], b = Along[i + 1], k = b - a > 1e-5f ? (x - a) / (b - a) : 0f;
            var p0 = Path[i]; var p1 = Path[i + 1];
            pos = new V3(p0.X + (p1.X - p0.X) * k, p0.Y + (p1.Y - p0.Y) * k, p0.Z + (p1.Z - p0.Z) * k);
            yawDeg = Heading(i, fwd);
            double stride = Child ? 0.95 : 1.4;                                                   // metres per walk cycle
            double ph = (d / stride) % 1.0; frame = Math.Min(PeopleKit.WalkFrames - 1, (int)(ph * PeopleKit.WalkFrames));
            return true;
        }

        int Seg(float x) { int lo = 0, hi = Along.Length - 2; while (lo < hi) { int mid = (lo + hi + 1) / 2; if (Along[mid] <= x) lo = mid; else hi = mid - 1; } return lo; }
        float Heading(int i, bool fwd) { var p0 = Path[i]; var p1 = Path[Math.Min(i + 1, Path.Length - 1)]; float y = (float)(Math.Atan2(p1.X - p0.X, p1.Z - p0.Z) * 180.0 / Math.PI); return fwd ? y : y + 180f; }

        public static CrowdAgent Walker(V3[] path, float speed, float offset)
        {
            var a = new CrowdAgent { Path = path, Along = new float[path.Length], Speed = speed, Offset = offset };
            for (int i = 1; i < path.Length; i++) { float dx = path[i].X - path[i - 1].X, dz = path[i].Z - path[i - 1].Z; a.Along[i] = a.Along[i - 1] + (float)Math.Sqrt(dx * dx + dz * dz); }
            return a;
        }
    }

    /// <summary>
    /// P2 of docs/FUNCTIONAL_PLACES_PROPOSAL.md (approved 2026-11-14): who stands and walks where. Pure and deterministic: seeded by the route and the placement, chunk by chunk (every agent belongs to
    /// the one chunk that holds its anchor, so loading order never changes the crowd). People stand at shop and market fronts, mosque fronts, bus stops, kiosks and stalls; a station has an attendant
    /// at a pump and a customer walking between the mart and the forecourt; walkers go back and forth on the walkways of city streets. Nobody is placed or walks on a carriageway or a shoulder (tested),
    /// and in P2 nobody collides with anything: people are visual.
    /// </summary>
    public static class CrowdPlanner
    {
        /// <summary>How many people are out at an hour (0..24): fewer early, late and at night.</summary>
        public static float Density(float hour)
        {
            hour = ((hour % 24f) + 24f) % 24f;
            if (hour >= 7f && hour < 19f) return 1f;
            if (hour >= 19f && hour < 22f) return 0.6f;
            if (hour >= 5f && hour < 7f) return 0.35f;
            return 0.12f;
        }

        static readonly string[] Busy = { "commercial", "shop", "shop_row", "shop_house", "public", "health", "hospital", "fast_food", "restaurant", "bank", "hotel", "government", "motor_park", "police", "market", "civic", "transport" };

        /// <summary>Every person anchored in the chunk [minX, maxX) x [minZ, maxZ), from the chunk's placed props and the route's walkways.</summary>
        public static List<CrowdAgent> PlaceChunk(RouteModel m, IList<PropInstance> instances, Func<string, PropDef> lookup, float minX, float minZ, float maxX, float maxZ)
        {
            var res = new List<CrowdAgent>(); int seed = m.Spec.seed;
            foreach (var inst in instances)
            {
                var d = lookup(inst.Prop); if (d == null) continue;
                int h = Hash(seed, inst.Prop, inst.Pos.X, inst.Pos.Z);
                FromProp(m, d, inst, h, res);
            }
            foreach (var st in Stretches(m))
                for (int k = 0; k < st.Walkers; k++)
                {
                    int h = HI(seed ^ st.Key, k, 41);
                    float sub = Math.Min(st.Length, 40f + U(h, 1) * 70f), a0 = U(h, 2) * (st.Length - sub);
                    var path = st.Slice(a0, a0 + sub, 4f);
                    if (path.Length < 2 || path[0].X < minX || path[0].X >= maxX || path[0].Z < minZ || path[0].Z >= maxZ) continue;
                    var w = CrowdAgent.Walker(path, 1.05f + U(h, 3) * 0.45f, U(h, 4) * 2f * sub);
                    Dress(w, h, "walkway:" + st.Corridor + ":" + st.Side); res.Add(w);
                }
            return res;
        }

        static void FromProp(RouteModel m, PropDef d, PropInstance inst, int h, List<CrowdAgent> res)
        {
            float w = d.widthM * inst.Scale, dep = d.depthM * inst.Scale;
            V3 L(float x, float z) => PropBatcher.Place(new V3(x, 0f, z), inst.Pos, inst.YawDeg, 1f);
            bool clear(V3 p) => OffRoad(m, p.X, p.Z, 0.6f);
            void Idle(V3 p, float yawLocal, int k, string src) { if (!clear(p)) return; var a = new CrowdAgent { Spot = p, Yaw = inst.YawDeg + yawLocal, Offset = U(h, 50 + k) * 4f }; Dress(a, HI(h, k, 7), src); res.Add(a); }
            if (d.HasSockets && d.sockets.Any(s => s != null && ServicePoints.IsFuel(s.kind)))
            {   // a fuel station: an attendant by the first pump, a customer between the mart and the forecourt
                var s = d.sockets.First(q => q != null && ServicePoints.IsFuel(q.kind)); float r = s.yawDeg * (float)Math.PI / 180f, ax = (float)Math.Sin(r), az = (float)Math.Cos(r);
                float lx = s.x + ax * 0.15f - az * 3.0f, lz = s.z + az * 0.15f + ax * 3.0f;                       // beside the island, 3 m along it from the pump: not where a vehicle stands
                Idle(L(lx, lz), s.yawDeg + 180f, 0, "station:attendant");
                var mart = d.HasBoxes ? d.boxes.OrderByDescending(b => (b.x1 - b.x0) * (b.z1 - b.z0)).First() : null;
                if (mart != null)
                {
                    float mx = (mart.x0 + mart.x1) * 0.5f; var p0 = L(mx, mart.z1 + 0.8f); var p1 = L(mx + (U(h, 9) - 0.5f) * 4f, mart.z1 + 6.5f);
                    if (clear(p0) && clear(p1)) { var a = CrowdAgent.Walker(new[] { p0, p1 }, 1.1f, U(h, 10) * 12f); Dress(a, HI(h, 1, 7), "station:customer"); res.Add(a); }
                }
                return;
            }
            bool mosque = Has(d, "mosque"), busy = Busy.Any(t => Has(d, t)) || d.id == "shop_row" || d.id.Contains("market"), home = Has(d, "residential");
            if (d.id == "bus_stop")
            {   // people waiting under the shelter, facing the road
                int n = 2 + (int)(U(h, 3) * 5f);
                for (int k = 0; k < n; k++) Idle(L((k + 0.5f) / n * (w - 0.8f) - (w - 0.8f) * 0.5f, -dep * 0.25f + U(h, 20 + k) * 0.4f), U(h, 30 + k) * 50f - 25f, k, "stop");
                return;
            }
            if (d.id == "kiosk" || d.id == "market_stall")
            {   // a customer or two at the counter, the seller behind
                int n = 1 + (int)(U(h, 3) * 2f);
                for (int k = 0; k < n; k++) Idle(L((k - (n - 1) * 0.5f) * 0.8f, dep * 0.5f + 0.9f), 180f, k, "stall:customer");
                Idle(L(0f, -dep * 0.5f - 0.6f), 0f, 5, "stall:seller");
                return;
            }
            if (!(mosque || busy || home)) return;
            if (home && U(h, 3) > 0.25f) return;                                                             // one home in four has someone at the gate
            int count = mosque ? 2 + (int)(U(h, 4) * 4f) : busy ? 1 + (int)(U(h, 4) * 3f) : 1;
            for (int k = 0; k < count; k++)
            {   // in front of the lot, between its edge and the road: chatting in pairs, facing each other or the street
                float x = (U(h, 60 + k) - 0.5f) * Math.Max(1f, w - 2f), z = dep * 0.5f + 1.0f + U(h, 70 + k) * 1.2f;
                float yaw = k % 2 == 0 ? 90f + U(h, 80 + k) * 40f : -90f - U(h, 80 + k) * 40f;
                Idle(L(x, z), yaw, k, mosque ? "mosque" : busy ? "front" : "gate");
            }
            if (busy && w >= 10f && U(h, 5) < 0.6f)
            {   // someone walking along the frontage
                var p0 = L(-w * 0.5f + 1f, dep * 0.5f + 1.6f); var p1 = L(w * 0.5f - 1f, dep * 0.5f + 1.6f);
                if (clear(p0) && clear(p1) && clear(L(0f, dep * 0.5f + 1.6f))) { var a = CrowdAgent.Walker(new[] { p0, p1 }, 1.0f + U(h, 6) * 0.4f, U(h, 7) * 2f * (w - 2f)); Dress(a, HI(h, 9, 7), "front:walk"); res.Add(a); }
            }
        }

        static bool Has(PropDef d, string tag) => d.tags != null && Array.IndexOf(d.tags, tag) >= 0;

        static void Dress(CrowdAgent a, int h, string src)
        {
            a.Design = (int)(U(h, 11) * PeopleKit.Designs.Length) % PeopleKit.Designs.Length; a.Colourway = (int)(U(h, 12) * 3f) % 3; a.Skin = (int)(U(h, 13) * PeopleKit.SkinCount) % PeopleKit.SkinCount;
            a.Rank = U(h, 14); a.Source = src;
        }

        static float U(int h, int k) => Mathx.Hash01(h, k, 97);
        static int HI(int a, int b, int c) => (int)(Mathx.Hash01(a, b, c) * 16777216f) ^ (a * 31);
        static int Hash(int seed, string id, float x, float z) { unchecked { int h = seed * 486187739; foreach (char c in id) h = h * 31 + c; return HI(h, (int)Math.Round(x * 10f), (int)Math.Round(z * 10f)); } }

        /// <summary>The paved half width (carriageway and shoulders) at s as built: near a change of road class the cross-section is scaled to meet its neighbour edge to edge.</summary>
        static float PavedHalf(RoadCorridor c, float s) { int i = c.RunIndexAt(s); return c.Runs[i].Section.PavedHalfWidth * c.ScaleAt(i, s); }

        /// <summary>True when (x, z) is clear of every carriageway and shoulder (plus `margin`), every roundabout island and every site plate.</summary>
        public static bool OffRoad(RouteModel m, float x, float z, float margin)
        {
            foreach (var c in m.CorridorsNear(x, z))
            {
                if (!c.Spline.Nearest(x, z, c.MaxFormationHalf + 2f, out float s, out float lat, out _)) continue;
                if (Math.Abs(lat) <= PavedHalf(c, s) + margin) return false;
            }
            foreach (var isl in m.Islands) if (isl.Contains(x, z, margin)) return false;
            foreach (var p in m.Sites) if (p.Contains(x, z, margin)) return false;
            return true;
        }

        // ------------------------------------------------------------------------------------------------ walkways
        public sealed class Stretch
        {
            public string Corridor; public int Side, Key, Walkers; public float S0, S1;
            internal RoadCorridor C; internal float[] Lat;     // lateral of the walkway centre at each 2 m sample from S0
            public float Length => S1 - S0;
            /// <summary>Points every `step` metres along the walkway centre between a0 and a1 (metres from S0), at the walkway's surface.</summary>
            public V3[] Slice(float a0, float a1, float step)
            {
                var pts = new List<V3>();
                for (float a = a0; ; a += step)
                {
                    float s = S0 + Math.Min(a, a1); C.Spline.FrameAt(s, out var p, out _, out var right);
                    float lat = WalkwayLat(C, s, Side);
                    pts.Add(new V3(p.X + right.X * lat, C.SurfaceY(s, lat), p.Z + right.Z * lat));
                    if (a >= a1) break;
                }
                return pts.ToArray();
            }
        }

        static readonly ConditionalWeakTable<RouteModel, List<Stretch>> _stretches = new ConditionalWeakTable<RouteModel, List<Stretch>>();

        /// <summary>The walkable stretches of every walkway of the route: the centre of the paved walkway beyond the kerb, broken wherever it would cross another road, a plate or an island.</summary>
        public static List<Stretch> Stretches(RouteModel m) => _stretches.GetValue(m, Build);

        static List<Stretch> Build(RouteModel m)
        {
            var res = new List<Stretch>();
            foreach (var c in m.AllCorridors)
                foreach (int side in new[] { -1, 1 })
                {
                    float L = c.Spline.Length; var lats = new List<float>(); float start = -1f;
                    void Close(float s1)
                    {
                        if (start >= 0f && s1 - start >= 20f)
                        {
                            int key = 0; unchecked { foreach (char ch in (c.Id ?? "main")) key = key * 31 + ch; key = key * 7 + side; }     // stable across runs (string.GetHashCode is not)
                            var st = new Stretch { Corridor = c.Id ?? "main", Side = side, S0 = start, S1 = s1, C = c, Lat = lats.ToArray(), Key = key };
                            st.Walkers = Math.Max(1, (int)Math.Round(st.Length / 100f * 1.6f)); res.Add(st);
                        }
                        start = -1f; lats = new List<float>();
                    }
                    for (float s = 1f; s <= L - 1f; s += 2f)
                    {
                        var sec = c.SectionAt(s); var sp = sec.Spec; bool ok = sp.sidewalkWidth >= 0.8f && sp.kerbHeight > 0.001f && !c.InBridge(s, out _);
                        float lat = 0f;
                        if (ok)
                        {
                            lat = WalkwayLat(c, s, side);
                            c.Spline.FrameAt(s, out var p, out _, out var right); float x = p.X + right.X * lat, z = p.Z + right.Z * lat;
                            ok = OffOtherRoads(m, c, x, z);
                        }
                        if (ok) { if (start < 0f) start = s; lats.Add(lat); } else Close(s - 2f);
                    }
                    Close(L - 1f);
                }
            return res;
        }

        /// <summary>The lateral of the walkway centre at s on a side (the cross-section's own scale applied, as the road is built).</summary>
        internal static float WalkwayLat(RoadCorridor c, float s, int side)
        {
            int i = c.RunIndexAt(s); var sec = c.Runs[i].Section; var sp = sec.Spec;
            return side * c.ScaleAt(i, s) * (sec.PavedHalfWidth + sp.drainWidth + RoadCrossSection.KerbFace + RoadCrossSection.KerbTopWidth + sp.sidewalkWidth * 0.5f);
        }

        static bool OffOtherRoads(RouteModel m, RoadCorridor own, float x, float z)
        {
            foreach (var c in m.CorridorsNear(x, z))
            {
                if (ReferenceEquals(c, own)) continue;
                if (c.Spline.Nearest(x, z, c.MaxFormationHalf + 2f, out float s, out float lat, out _) && Math.Abs(lat) <= PavedHalf(c, s) + 1.0f) return false;
            }
            foreach (var isl in m.Islands) if (isl.Contains(x, z, 1f)) return false;
            foreach (var p in m.Sites) if (p.Contains(x, z, 1f)) return false;
            return true;
        }

        // ------------------------------------------------------------------------------------------------ selection and the bus
        /// <summary>The people to draw: those out at this density, at most `cap`, nearest to `eye` within `maxDist`, nearest first.</summary>
        public static List<(CrowdAgent agent, V3 pos, float yaw, int frame, float dist)> Visible(IEnumerable<CrowdAgent> agents, double t, V3 eye, float maxDist, int cap, float density)
        {
            var all = new List<(CrowdAgent, V3, float, int, float)>();
            foreach (var a in agents)
            {
                if (a.Rank >= density && !a.Once) continue;
                if (!a.At(t, out var p, out float yaw, out int frame)) continue;
                float dx = p.X - eye.X, dz = p.Z - eye.Z, dd = (float)Math.Sqrt(dx * dx + dz * dz); if (dd > maxDist) continue;
                all.Add((a, p, yaw, frame, dd));
            }
            all.Sort((x, y) => x.Item5.CompareTo(y.Item5));
            if (all.Count > cap) all.RemoveRange(cap, all.Count - cap);
            return all;
        }

        /// <summary>
        /// The bus fills up (and empties) before your eyes: `boarded` people walk from where they waited beside the stop to the door, one after the other, and disappear into the bus; `alighted` people
        /// step out of the door and walk away. The numbers are the server's (serve_stop); at most 12 of each are drawn. `kerb` is the direction from the bus towards the kerb (unit, horizontal).
        /// </summary>
        public static List<CrowdAgent> BusStop(V3 door, V3 kerb, V3 along, int boarded, int alighted, double t0, int seed)
        {
            var res = new List<CrowdAgent>();
            int nb = Math.Min(12, Math.Max(0, boarded)), na = Math.Min(12, Math.Max(0, alighted));
            for (int k = 0; k < na; k++)
            {   // first off, then on
                int h = HI(seed, k, 3); float side = (U(h, 1) - 0.5f) * 6f;
                var p0 = door; var p1 = door + kerb * 1.6f + along * (side * 0.2f); var p2 = door + kerb * (3.5f + U(h, 2) * 2f) + along * side;
                var a = CrowdAgent.Walker(new[] { p0, p1, p2 }, 1.2f, 0f); a.Once = true; a.T0 = t0 + 0.7 * k; a.Lead = 0f; Dress(a, h, "bus:alight"); res.Add(a);
            }
            double t1 = t0 + 0.7 * na + 0.5;
            for (int k = 0; k < nb; k++)
            {
                int h = HI(seed, k, 5); float off = (k % 6 - 2.5f) * 0.9f, row = 2.6f + (k / 6) * 0.9f;
                var q = door + kerb * row + along * (off - 2.0f); var near = door + kerb * 1.2f;
                var a = CrowdAgent.Walker(new[] { q, near, door }, 1.25f, 0f); a.Once = true; a.T0 = t1 + 0.65 * k; a.Lead = 30f; Dress(a, h, "bus:board"); res.Add(a);
            }
            return res;
        }

        /// <summary>When the last of a bus stop's walkers is done (the animation's end).</summary>
        public static double EndOf(IEnumerable<CrowdAgent> agents) => agents.Where(a => a.Once).Select(a => a.T0 + a.Length / a.Speed).DefaultIfEmpty(0.0).Max();
    }
}
