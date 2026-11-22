using System;
using System.Collections.Generic;
using System.Linq;

namespace ARO.NetCore
{
    public enum NetLaneKind { Road, Ring, Connector }
    public enum NetMove { None, Straight, Right, Left, UTurn, RingEntry, RingExit }

    /// <summary>
    /// One directed traffic lane of the network (docs/TRAFFIC_NETWORK_PROPOSAL.md, approved by the owner on 2026-11-22): a road travelled in one direction (Dir +1 towards increasing station, -1 towards
    /// decreasing), the circulating carriageway of a roundabout (one lane, anticlockwise: right-hand traffic), or a connector through a junction box from one lane to another. Position along a lane is U,
    /// metres from the lane's start in its direction of travel (on a ring, U is the ring's own station and wraps).
    /// </summary>
    public sealed class NetLane
    {
        public int Index; public NetLaneKind Kind; public RoadCorridor Corridor; public int Dir = 1; public float Length;
        public float Weight = 1f;                                   // share of the traffic (road class: lanes per direction squared)
        public readonly List<int> Out = new List<int>();           // connectors leaving this lane, ordered by FromU
        public readonly List<int> In = new List<int>();            // connectors arriving on this lane
        public bool DeadEnd;                                        // road lanes: only a turning-round connector leaves the end of this lane (a cul-de-sac or the edge of the map)
        // connectors only
        public int From = -1, To = -1; public float FromU, ToU, Speed = 7f; public NetMove Move; public bool Yield;
        public int Junction = -1;                                   // conflict point: vehicles on two connectors of one junction never cross the box at the same time unless both turn right off the priority road
        public int[] Priority = new int[0]; public float[] PriorityU = new float[0];     // the lanes a yielding vehicle gives way to, and the conflict point on each

        public bool IsLoop => Kind == NetLaneKind.Ring;
        /// <summary>The corridor station of lane position u (roads and rings).</summary>
        public float S(float u) => Kind == NetLaneKind.Ring ? TrafficNet.Wrap(u, Length) : Dir > 0 ? u : Length - u;
        public float UOf(float s) => Kind == NetLaneKind.Ring ? TrafficNet.Wrap(s, Length) : Dir > 0 ? s : Length - s;
        public override string ToString() => Kind == NetLaneKind.Connector ? $"conn#{Index} {Move} {From}->{To}" : $"{Corridor.Id}{(Dir > 0 ? "+" : "-")}";
    }

    /// <summary>
    /// The traffic network of a route: every road and roundabout of the RouteModel as lanes, joined by connectors at every junction (the road joins of E5, the junction stubs of the main road, the legs of
    /// every roundabout), with a turning-round connector at every free end. Pure and deterministic; built once per route. Routes use it only when their data asks for it (traffic.network = true):
    /// every other route keeps the single-corridor TrafficSim, unchanged.
    /// </summary>
    public sealed class TrafficNet
    {
        public readonly List<NetLane> Lanes = new List<NetLane>();
        public RouteModel Model { get; private set; }
        public float SpeedScale = 1f;
        readonly Dictionary<(string, int), int> _lane = new Dictionary<(string, int), int>();
        int _junctions;

        public NetLane Lane(string corridor, int dir) => _lane.TryGetValue((corridor, dir), out int i) ? Lanes[i] : null;
        public IEnumerable<NetLane> Connectors => Lanes.Where(l => l.Kind == NetLaneKind.Connector);

        public static float Wrap(float u, float len) { if (len <= 0f) return u; float r = u % len; return r < 0f ? r + len : r; }
        /// <summary>Distance travelled along lane `l` from `from` to `to` (on a ring, always forwards and in [0, Length)).</summary>
        public static float Ahead(NetLane l, float from, float to) => l.IsLoop ? Wrap(to - from, l.Length) : to - from;

        struct JoinRec { public RoadCorridor Through, Side; public bool AtStart; public float S; public int SideSign, Junction; }      // Junction: -1 until grouped

        public static TrafficNet Build(RouteModel m, float speedScale = 1f)
        {
            var net = new TrafficNet { Model = m, SpeedScale = speedScale };
            foreach (var c in m.AllCorridors)
            {
                if (c.IsRing) net.AddLane(c, NetLaneKind.Ring, 1);
                else { net.AddLane(c, NetLaneKind.Road, 1); net.AddLane(c, NetLaneKind.Road, -1); }
            }
            var handled = new HashSet<(string, bool)>();         // (corridor, atStart) ends that meet another road or a ring
            var joins = new List<JoinRec>();
            RoadCorridor Find(string id) => m.AllCorridors.FirstOrDefault(c => c.Id == id);

            foreach (var r in m.Spec.roads ?? new RoadSpec[0])
                foreach (var j in r.joins ?? new RoadJoinSpec[0])
                {
                    var c = Find(r.id); var d = Find(j.corridor); if (c == null || d == null || d.IsRing) continue;
                    joins.Add(new JoinRec { Through = d, Side = c, AtStart = j.end != "end", S = Mathx.Clamp(j.s, 0f, d.Length), SideSign = j.side >= 0 ? 1 : -1, Junction = -1 });
                }
            foreach (var c in m.Sides)                                // the junction stubs of the main road (schemaVersion 1 and 2): a stub starts at the main road's paved edge
            {
                if (c.E5 || c.IsRing || c.IsMain) continue;
                var p = c.Spline.PositionAt(0f);
                if (!m.Main.Spline.Nearest(p.X, p.Z, 1000f, out float s, out _, out _)) continue;
                m.Main.Spline.FrameAt(s, out var mp, out _, out var right);
                float side = (p.X - mp.X) * right.X + (p.Z - mp.Z) * right.Z;
                joins.Add(new JoinRec { Through = m.Main, Side = c, AtStart = true, S = s, SideSign = side >= 0f ? 1 : -1, Junction = -1 });
            }
            // a crossroads is two joins at one station on opposite sides: they share one junction (one conflict point)
            for (int i = 0; i < joins.Count; i++)
            {
                if (joins[i].Junction >= 0) continue; var ji = joins[i]; ji.Junction = net._junctions++; joins[i] = ji;
                for (int k = i + 1; k < joins.Count; k++)
                    if (joins[k].Junction < 0 && joins[k].Through == ji.Through && Math.Abs(joins[k].S - ji.S) < 4f) { var jk = joins[k]; jk.Junction = ji.Junction; joins[k] = jk; }
            }
            foreach (var j in joins) { net.Join(j); handled.Add((j.Side.Id, j.AtStart)); }
            foreach (var g in joins.GroupBy(j => j.Junction))           // straight across a crossroads
            {
                var list = g.ToList();
                foreach (var a in list) foreach (var b in list)
                    if (a.SideSign != b.SideSign && a.Side != b.Side) net.Straight(a, b);
            }
            foreach (var rb in m.Rings) foreach (var leg in rb.Legs) { var c = Find(leg.CorridorId); if (c == null) continue; net.Leg(rb, leg, c); handled.Add((c.Id, leg.AtStart)); }
            foreach (var c in m.AllCorridors)                          // free ends: a turning round (a cul-de-sac, a site road, the edge of the map)
            {
                if (c.IsRing) continue;
                foreach (bool atStart in new[] { true, false })
                {
                    if (handled.Contains((c.Id, atStart))) continue;
                    var inbound = net.Lane(c.Id, atStart ? -1 : 1); var outbound = net.Lane(c.Id, atStart ? 1 : -1);
                    inbound.DeadEnd = true;
                    net.Connect(inbound, inbound.Length, outbound, Math.Min(2f, outbound.Length), NetMove.UTurn, false, -1, new int[0], new float[0], 3f);
                }
            }
            foreach (var l in net.Lanes) if (l.Kind != NetLaneKind.Connector) l.Out.Sort((a, b) => net.Lanes[a].FromU.CompareTo(net.Lanes[b].FromU) != 0 ? net.Lanes[a].FromU.CompareTo(net.Lanes[b].FromU) : a.CompareTo(b));
            foreach (var cn in net.Connectors) cn.Length = Math.Max(1f, net.ConnectorLength(cn));
            return net;
        }

        NetLane AddLane(RoadCorridor c, NetLaneKind kind, int dir)
        {
            var l = new NetLane { Index = Lanes.Count, Kind = kind, Corridor = c, Dir = dir, Length = c.Length };
            int n = LaneCount(c, dir, c.Length * 0.5f); l.Weight = Math.Max(1, n) * Math.Max(1, n);
            Lanes.Add(l); _lane[(c.Id, dir)] = l.Index; return l;
        }

        NetLane Connect(NetLane from, float fromU, NetLane to, float toU, NetMove move, bool yield, int junction, int[] prio, float[] prioU, float speed)
        {
            var c = new NetLane
            {
                Index = Lanes.Count, Kind = NetLaneKind.Connector, From = from.Index, To = to.Index, FromU = Mathx.Clamp(fromU, 0f, from.Length), ToU = to.IsLoop ? Wrap(toU, to.Length) : Mathx.Clamp(toU, 0f, to.Length),
                Move = move, Yield = yield, Junction = junction, Priority = prio, PriorityU = prioU, Speed = speed, Weight = to.Weight
            };
            Lanes.Add(c); from.Out.Add(c.Index); to.In.Add(c.Index); return c;
        }

        void Join(JoinRec j)
        {
            var c = j.Side; var d = j.Through;
            var inbound = Lane(c.Id, j.AtStart ? -1 : 1); var outbound = Lane(c.Id, j.AtStart ? 1 : -1);
            float k = Math.Max(3f, c.HalfPavedAt(j.AtStart ? 0f : c.Length));
            foreach (int dir in new[] { 1, -1 })
            {
                var dl = Lane(d.Id, dir); var opp = Lane(d.Id, -dir);
                float uj = dl.UOf(j.S); bool right = j.SideSign * dir > 0;
                // off the priority road: a right turn just goes; a left turn crosses the oncoming lane and gives way to it
                Connect(dl, uj - k, outbound, Math.Min(1.5f, outbound.Length), right ? NetMove.Right : NetMove.Left, !right, j.Junction,
                        right ? new int[0] : new[] { opp.Index }, right ? new float[0] : new[] { opp.UOf(j.S) }, right ? 6f : 7f);
                // onto the priority road: always gives way (a right turn to the near lane, a left turn to both)
                Connect(inbound, inbound.Length, dl, uj + k, right ? NetMove.Right : NetMove.Left, true, j.Junction,
                        right ? new[] { dl.Index } : new[] { dl.Index, opp.Index }, right ? new[] { uj } : new[] { uj, opp.UOf(j.S) }, right ? 6f : 7f);
            }
        }

        void Straight(JoinRec a, JoinRec b)
        {
            var inbound = Lane(a.Side.Id, a.AtStart ? -1 : 1); var outbound = Lane(b.Side.Id, b.AtStart ? 1 : -1); var d = a.Through;
            var p = Lane(d.Id, 1); var q = Lane(d.Id, -1);
            Connect(inbound, inbound.Length, outbound, Math.Min(1.5f, outbound.Length), NetMove.Straight, true, a.Junction, new[] { p.Index, q.Index }, new[] { p.UOf(a.S), q.UOf(a.S) }, 8f);
        }

        void Leg(Roundabout rb, RingLegInfo leg, RoadCorridor c)
        {
            var ring = Lane(rb.Id, 1); int junction = _junctions++;
            var inbound = Lane(c.Id, leg.AtStart ? -1 : 1); var outbound = Lane(c.Id, leg.AtStart ? 1 : -1);
            float k = Math.Max(3f, leg.HalfPaved * rb.CentreRadius / Math.Max(1f, rb.OuterPavedRadius));
            Connect(inbound, inbound.Length, ring, leg.RingS + k, NetMove.RingEntry, true, junction, new[] { ring.Index }, new[] { Wrap(leg.RingS, ring.Length) }, 6f);
            Connect(ring, Wrap(leg.RingS - k, ring.Length), outbound, Math.Min(1.5f, outbound.Length), NetMove.RingExit, false, junction, new int[0], new float[0], 7f);
        }

        // ------------------------------------------------------------------ geometry

        /// <summary>Driving lanes of corridor c for direction dir at station s (a ring: all its lanes, they all circulate).</summary>
        public static int LaneCount(RoadCorridor c, int dir, float s)
        {
            var lc = c.SectionAt(Mathx.Clamp(s, 0f, c.Length)).LaneCentres; if (lc == null || lc.Length == 0) return 1;
            if (c.IsRing) return lc.Length;
            int n = 0; foreach (var x in lc) if (dir > 0 ? x > 0f : x < 0f) n++;
            return Math.Max(1, n);
        }

        /// <summary>Signed lateral offset (right of +s positive) of the lane a vehicle with `seed` drives in, on corridor c, direction dir, at station s.</summary>
        public static float LateralOf(RoadCorridor c, int dir, float s, int seed)
        {
            s = Mathx.Clamp(s, 0f, c.Length); var sec = c.SectionAt(s); var lc = sec.LaneCentres; float k = c.ScaleAt(s);
            if (lc == null || lc.Length == 0) return dir * sec.CarriagewayHalfWidth * 0.5f * k;
            if (c.IsRing) return lc[Math.Abs(seed) % lc.Length] * k;
            var mine = new List<float>(); foreach (var x in lc) if (dir > 0 ? x > 0f : x < 0f) mine.Add(x);
            if (mine.Count == 0) return dir * Math.Max(1.5f, sec.CarriagewayHalfWidth * 0.5f) * k;
            if (dir < 0) mine.Reverse();                                   // innermost first in both directions
            return mine[Math.Abs(seed) % mine.Count] * k;
        }

        /// <summary>Point, height and heading on a road or ring lane at lane position u, in the lane of a vehicle with `seed`.</summary>
        public void PointOn(NetLane l, float u, int seed, out V3 pos, out V3 fwd)
        {
            var c = l.Corridor; float s = l.IsLoop ? Wrap(u, l.Length) : Mathx.Clamp(l.S(Mathx.Clamp(u, 0f, l.Length)), 0f, c.Length);
            c.Spline.FrameAt(s, out var p, out var f, out var r);
            float lat = LateralOf(c, l.Dir, s, seed);
            pos = new V3(p.X + r.X * lat, c.SurfaceY(s, lat), p.Z + r.Z * lat);
            fwd = l.Dir > 0 ? f : new V3(-f.X, 0f, -f.Z);
        }

        void Bezier(NetLane cn, int seed, out V3 p0, out V3 p1, out V3 p2, out V3 p3)
        {
            PointOn(Lanes[cn.From], cn.FromU, seed, out p0, out var f0); PointOn(Lanes[cn.To], cn.ToU, seed, out p3, out var f3);
            float dx = p3.X - p0.X, dz = p3.Z - p0.Z, d = (float)Math.Sqrt(dx * dx + dz * dz);
            float h = cn.Move == NetMove.UTurn ? Math.Max(3f, d * 0.9f) : Math.Max(1f, d * 0.4f);
            p1 = new V3(p0.X + f0.X * h, p0.Y, p0.Z + f0.Z * h); p2 = new V3(p3.X - f3.X * h, p3.Y, p3.Z - f3.Z * h);
        }

        static V3 Cubic(V3 a, V3 b, V3 c, V3 d, float t)
        {
            float u = 1f - t, w0 = u * u * u, w1 = 3f * u * u * t, w2 = 3f * u * t * t, w3 = t * t * t;
            return new V3(a.X * w0 + b.X * w1 + c.X * w2 + d.X * w3, a.Y * w0 + b.Y * w1 + c.Y * w2 + d.Y * w3, a.Z * w0 + b.Z * w1 + c.Z * w2 + d.Z * w3);
        }

        float ConnectorLength(NetLane cn)
        {
            Bezier(cn, 0, out var a, out var b, out var c, out var d); float len = 0f; var prev = a;
            for (int i = 1; i <= 16; i++) { var p = Cubic(a, b, c, d, i / 16f); float dx = p.X - prev.X, dz = p.Z - prev.Z; len += (float)Math.Sqrt(dx * dx + dz * dz); prev = p; }
            return len;
        }

        /// <summary>Point and heading at position u of any lane (a connector: along its curve, by arc length fraction).</summary>
        public void PoseOn(NetLane l, float u, int seed, out V3 pos, out V3 fwd)
        {
            if (l.Kind != NetLaneKind.Connector) { PointOn(l, u, seed, out pos, out fwd); return; }
            Bezier(l, seed, out var a, out var b, out var c, out var d); float t = Mathx.Clamp01(u / Math.Max(0.01f, l.Length));
            pos = Cubic(a, b, c, d, t); var q = Cubic(a, b, c, d, Math.Min(1f, t + 0.02f)); var o = Cubic(a, b, c, d, Math.Max(0f, t - 0.02f));
            float dx = q.X - o.X, dz = q.Z - o.Z, n = (float)Math.Sqrt(dx * dx + dz * dz);
            fwd = n > 1e-5f ? new V3(dx / n, 0f, dz / n) : new V3(0f, 0f, 1f);
        }

        /// <summary>A cheap centre-line point of lane l at u (distances to the player; no lane offset).</summary>
        public V3 CentrePoint(NetLane l, float u)
        {
            if (l.Kind != NetLaneKind.Connector) return l.Corridor.Spline.PositionAt(l.IsLoop ? Wrap(u, l.Length) : l.S(Mathx.Clamp(u, 0f, l.Length)));
            var a = CentrePoint(Lanes[l.From], l.FromU); var b = CentrePoint(Lanes[l.To], l.ToU); return V3.Lerp(a, b, Mathx.Clamp01(u / Math.Max(0.01f, l.Length)));
        }

        /// <summary>Speed limit (m/s) at position u of lane l: by zone and surface like the single-corridor model, 8.5 m/s on a roundabout, the turning speed on a connector.</summary>
        public float LimitAt(NetLane l, float u)
        {
            if (l.Kind == NetLaneKind.Connector) return l.Speed;
            if (l.IsLoop) return 8.5f * SpeedScale;
            var run = l.Corridor.RunAt(l.S(Mathx.Clamp(u, 0f, l.Length)));
            float v = run.Zone == Zone.Urban ? 11f : run.Zone == Zone.Commercial ? 9f : run.Zone == Zone.Industrial ? 14f : run.Zone == Zone.Rural ? 20f : 25f;
            return v * (run.Surface == Surface.Damaged ? 0.55f : run.Surface == Surface.Worn ? 0.85f : 1f) * SpeedScale;
        }
    }

    public sealed class NetAgent
    {
        public int Id; public TrafficKind Kind; public IdmParams P; public float DesiredFactor = 1f; public bool External;
        public int Lane; public float U, Speed;          // U: front bumper on the current lane
        public int Next = -1; public float ToGo;          // the connector (on a road or ring lane) or the lane (on a connector) it moves to next, and the distance to that point
        public int Prev = -1, Prev2 = -1; public float PrevU, PrevRun, Prev2U, Run;   // where it came from (the rear of a long vehicle can still be there, two lanes back on a short turn): it left Prev at PrevU after running PrevRun metres on it, Prev2 at Prev2U; Run: metres on this lane
        public bool Committed; public float Wait, Still;  // past its yield line; seconds held at the line; seconds standing still
        public string EntryLeg;                           // on a ring: the leg it came from (it never leaves by the same leg)
        public float Len => P.Length;
        /// <summary>The part of the vehicle on its current lane: just after a short turn the rest of a long vehicle is still in the junction box behind the lane's entry point.</summary>
        public float LenOnLane => Math.Min(P.Length, Run);
    }

    /// <summary>Counts per step (the CI drive and the tests read them).</summary>
    public struct NetTrafficStats { public int Agents, OnMain, OnRoads, OnRings, InJunctions, Waiting; }

    /// <summary>
    /// Network traffic (docs/TRAFFIC_NETWORK_PROPOSAL.md, approved 2026-11-22): today's car following (IDM, the same parameters per vehicle kind) on every lane of a TrafficNet; at every junction a vehicle
    /// picks its next lane (straight on more often than a turn, weighted by road class, never a U-turn except at a dead end); a vehicle joining a priority road, turning left across it or entering a
    /// roundabout waits at its line for a gap (about 4 s, shrinking to 1.5 s after a long wait so nobody waits for ever); vehicles appear 350 to 800 m from the player, vanish beyond 1,100 m, with the
    /// quality tier's cap. Pure C#: unit-tested without an engine. The player is an external obstacle on the lane it drives in.
    /// </summary>
    public sealed class NetTrafficSim
    {
        public readonly TrafficNet Net; public readonly TrafficConfig Cfg;
        public readonly List<NetAgent> Agents = new List<NetAgent>();
        public float GapSeconds = 4f, MinGapSeconds = 1.5f, ReleaseRate = 0.1f;
        public const float SampleStep = 20f, CellMetres = 200f;
        /// <summary>Called when a vehicle moves onto another lane (from, to): the tests check that every move follows a connector.</summary>
        public Action<NetAgent, int, int> Moved;
        /// <summary>Called when a vehicle at a yield line accepts its gap (the tests check what was on the priority lanes at that moment).</summary>
        public Action<NetAgent, NetLane> Committed;
        readonly Random _rng; int _nextId = 1; NetAgent _player;
        float _px, _pz; readonly Dictionary<int, List<NetAgent>> _byLane = new Dictionary<int, List<NetAgent>>();
        // spawn samples: every 20 m of every road and ring lane, bucketed by 200 m cells
        readonly List<(int lane, float u, float x, float z)> _samples = new List<(int, float, float, float)>();
        readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
        long _candCell = long.MinValue; int[] _cand = new int[0]; float[] _candCum = new float[0]; float _candKm;

        public NetTrafficSim(TrafficNet net, TrafficConfig cfg = null)
        {
            Net = net ?? throw new ArgumentNullException(nameof(net)); Cfg = cfg ?? new TrafficConfig(); _rng = new Random(Cfg.Seed);
            foreach (var l in net.Lanes)
            {
                if (l.Kind == NetLaneKind.Connector) continue;
                for (float u = l.IsLoop ? 0f : 15f; u <= l.Length - (l.IsLoop ? SampleStep : 15f); u += SampleStep)
                {
                    var p = net.CentrePoint(l, u); int i = _samples.Count; _samples.Add((l.Index, u, p.X, p.Z));
                    long key = Key(p.X, p.Z); if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new List<int>(); list.Add(i);
                }
            }
        }

        static long Key(float x, float z) => ((long)(int)Math.Floor(x / CellMetres) << 32) ^ (uint)(int)Math.Floor(z / CellMetres);

        // ------------------------------------------------------------------ player

        /// <summary>The player's vehicle at (x, z) heading (fx, fz): it is put on the lane of the nearest road (its direction from the heading) so traffic brakes for it and waits for it at junctions.</summary>
        public void SetPlayer(float x, float z, float fx, float fz, float speed, float length)
        {
            _px = x; _pz = z; NetLane best = null; float bestU = 0f, bestD = float.MaxValue;
            foreach (var c in Net.Model.CorridorsNear(x, z))
            {
                if (!c.Spline.Nearest(x, z, c.MaxFormationHalf + 2f, out float s, out _, out float dist) || dist >= bestD) continue;
                if (c.IsEnd(s) && !c.IsRing) continue;
                c.Spline.TangentAt(s, out float tx, out float tz); int dir = c.IsRing || tx * fx + tz * fz >= 0f ? 1 : -1;
                var l = Net.Lane(c.Id, dir); if (l == null) continue;
                best = l; bestU = l.UOf(s); bestD = dist;
            }
            if (best == null) { _player = null; return; }
            if (_player == null) _player = new NetAgent { Id = 0, External = true, Kind = TrafficKind.Car, P = new IdmParams { Length = length, DesiredSpeed = 1, MaxAccel = 1, ComfortDecel = 1, TimeHeadway = 1, MinGap = 1 } };
            _player.Lane = best.Index; _player.U = best.IsLoop ? TrafficNet.Wrap(bestU + length * 0.5f, best.Length) : Math.Min(best.Length, bestU + length * 0.5f); _player.Speed = speed; _player.P.Length = length; _player.Run = 1000f;      // the whole player's vehicle is on its lane
        }

        /// <summary>Only the player's position (no obstacle): used before the player's vehicle exists.</summary>
        public void SetPlayerPosition(float x, float z) { _px = x; _pz = z; }
        public void ClearPlayer() { _player = null; }
        public NetAgent Player => _player;

        // ------------------------------------------------------------------ step

        public void Step(float dt)
        {
            if (dt <= 0f) return;
            Agents.RemoveAll(a => Dist(a) > Cfg.DespawnRadius);
            TrySpawn();
            Group();
            int n = Agents.Count; var acc = new float[n];
            for (int i = 0; i < n; i++) acc[i] = Accel(Agents[i], dt);
            Integrate(acc, dt);
            Transfer();
        }

        void Group()
        {
            foreach (var l in _byLane.Values) l.Clear();
            void Put(NetAgent a) { if (!_byLane.TryGetValue(a.Lane, out var l)) _byLane[a.Lane] = l = new List<NetAgent>(); l.Add(a); }
            foreach (var a in Agents) Put(a);
            if (_player != null) Put(_player);
            foreach (var l in _byLane.Values) l.Sort((x, y) => x.U != y.U ? x.U.CompareTo(y.U) : x.Id.CompareTo(y.Id));
        }

        List<NetAgent> On(int lane) => _byLane.TryGetValue(lane, out var l) ? l : null;

        float Dist(NetAgent a) { var p = Net.CentrePoint(Net.Lanes[a.Lane], a.U - a.Len * 0.5f); float dx = p.X - _px, dz = p.Z - _pz; return (float)Math.Sqrt(dx * dx + dz * dz); }

        /// <summary>Bumper-to-bumper gap to whatever is ahead of agent a (same lane, a vehicle about to merge into it, the lanes of its plan, its yield line) and that obstacle's speed.</summary>
        float Leader(NetAgent a, out float lv)
        {
            var lane = Net.Lanes[a.Lane]; float best = float.PositiveInfinity; lv = 0f;
            var here = On(a.Lane);
            if (here != null && here.Count > 1)
            {
                int i = here.IndexOf(a);
                NetAgent l = i + 1 < here.Count ? here[i + 1] : lane.IsLoop ? here[0] : null;
                if (l != null && l != a) { float g = TrafficNet.Ahead(lane, a.U, l.U - l.LenOnLane); if (lane.IsLoop && g > lane.Length - l.LenOnLane - 0.5f) g = 0.01f; if (g < best) { best = g; lv = l.Speed; } }
            }
            foreach (int ci in lane.In)                               // a vehicle on a connector that will arrive ahead of a
            {
                var cn = Net.Lanes[ci]; var on = On(ci); if (on == null) continue;
                foreach (var b in on)
                {
                    float vf = cn.ToU - (cn.Length - b.U); float g = TrafficNet.Ahead(lane, a.U, vf) - b.Len;
                    if (lane.IsLoop ? TrafficNet.Ahead(lane, a.U, vf) > 60f : vf <= a.U || vf - a.U > 60f) continue;
                    if (g < best) { best = Math.Max(0.01f, g); lv = b.Speed; }
                }
            }
            if (a.Next >= 0 && a.ToGo < best && a.ToGo < 120f)
            {
                if (lane.Kind != NetLaneKind.Connector)
                {
                    var cn = Net.Lanes[a.Next]; var on = On(a.Next);
                    if (on != null && on.Count > 0) { var b = on[0]; float g = a.ToGo + b.U - b.Len; if (g < best) { best = Math.Max(0.01f, g); lv = b.Speed; } }
                    else { float g = FirstAhead(Net.Lanes[cn.To], cn.ToU, out float v2); if (!float.IsPositiveInfinity(g)) { g += a.ToGo + cn.Length; if (g < best) { best = Math.Max(0.01f, g); lv = v2; } } }
                    if (cn.Yield && !a.Committed && a.ToGo < 60f) { float g = Math.Max(0.01f, a.ToGo - 0.3f); if (g < best) { best = g; lv = 0f; } }      // the yield line
                }
                else
                {
                    var t = Net.Lanes[a.Next]; float g = FirstAhead(t, lane.ToU, out float v2);
                    if (!float.IsPositiveInfinity(g)) { g += a.ToGo; if (g < best) { best = Math.Max(0.01f, g); lv = v2; } }
                }
            }
            return best;
        }

        /// <summary>Gap from lane position u to the rear of the first vehicle whose front is at or ahead of u on lane t (infinity when none within 120 m).</summary>
        float FirstAhead(NetLane t, float u, out float speed)
        {
            speed = 0f; var on = On(t.Index); if (on == null) return float.PositiveInfinity; float best = float.PositiveInfinity;
            foreach (var b in on) { float d = TrafficNet.Ahead(t, u, b.U); if (d < 0f || d > 120f) continue; float g = d - b.Len; if (g < best) { best = g; speed = b.Speed; } }
            return best;
        }

        float Accel(NetAgent a, float dt)
        {
            var lane = Net.Lanes[a.Lane];
            float desired = Math.Min(a.P.DesiredSpeed * a.DesiredFactor, Net.LimitAt(lane, a.U) * 1.05f);
            if (a.Next >= 0 && lane.Kind != NetLaneKind.Connector)
            {
                var cn = Net.Lanes[a.Next]; desired = Math.Min(desired, (float)Math.Sqrt(cn.Speed * cn.Speed + 2f * 1.5f * Math.Max(0f, a.ToGo - 2f)));      // slow down for the turn
                if (cn.Yield && !a.Committed && a.ToGo < 15f && CanGo(a, cn)) { a.Committed = true; if (cn.Junction >= 0) _reserved[cn.Junction] = a.Id; Committed?.Invoke(a, cn); }
                if (cn.Yield && !a.Committed && a.ToGo < 4f && a.Speed < 1f) a.Wait += dt;
            }
            float gap = Leader(a, out float lv);
            return Idm.Acceleration(a.P, a.Speed, desired, gap, lv);
        }

        /// <summary>Gap acceptance at a yield line: nothing on a priority lane reaches the conflict point within the required time (4 s, down to 1.5 s after a long wait), nothing is in the box, the
        /// junction is not being crossed by another yielding vehicle, and the lane it joins has room for it.</summary>
        public bool CanGo(NetAgent a, NetLane cn)
        {
            float need = Math.Max(MinGapSeconds, GapSeconds - ReleaseRate * a.Wait);
            for (int i = 0; i < cn.Priority.Length; i++)
            {
                var pl = Net.Lanes[cn.Priority[i]]; float uc = cn.PriorityU[i]; var on = On(pl.Index); if (on == null) continue;
                foreach (var b in on)
                {
                    float d;
                    if (pl.IsLoop) { float past = TrafficNet.Wrap(b.U - uc, pl.Length); if (past < b.Len + 4f) return false; d = TrafficNet.Wrap(uc - b.U, pl.Length); }
                    else { d = uc - b.U; if (d < 0f) { if (b.U - b.Len < uc + 4f) return false; continue; } }
                    if (d > 160f) continue;
                    if (d < 8f || d / Math.Max(0.5f, b.Speed) < need) return false;
                }
                foreach (int ci in pl.In)                            // a vehicle arriving on that lane from a connector just upstream of the conflict point
                {
                    var c2 = Net.Lanes[ci]; if (c2 == cn) continue; var on2 = On(ci); if (on2 == null || on2.Count == 0) continue;
                    float d = TrafficNet.Ahead(pl, c2.ToU, uc); if (d >= 0f && d < 30f) return false;
                }
            }
            if (cn.Junction >= 0 && _reserved.TryGetValue(cn.Junction, out int holder) && holder != a.Id && Agents.Any(x => x.Id == holder)) return false;     // one yielding vehicle crosses a box at a time
            if (cn.Junction >= 0)
                foreach (var c2 in JunctionConnectors(cn.Junction))
                {
                    if (c2 == cn || (c2.Move == NetMove.Right && !c2.Yield) || c2.Move == NetMove.RingExit) continue;
                    var on2 = On(c2.Index); if (on2 != null && on2.Count > 0) return false;
                }
            var t = Net.Lanes[cn.To]; float room = FirstAhead(t, cn.ToU, out _);
            if (room < a.Len + 3f) return false;
            return true;
        }

        Dictionary<int, List<NetLane>> _junctionConns; readonly Dictionary<int, int> _reserved = new Dictionary<int, int>();
        IEnumerable<NetLane> JunctionConnectors(int j)
        {
            if (_junctionConns == null) { _junctionConns = new Dictionary<int, List<NetLane>>(); foreach (var c in Net.Connectors) if (c.Junction >= 0) { if (!_junctionConns.TryGetValue(c.Junction, out var l)) _junctionConns[c.Junction] = l = new List<NetLane>(); l.Add(c); } }
            return _junctionConns.TryGetValue(j, out var r) ? (IEnumerable<NetLane>)r : new NetLane[0];
        }

        void Integrate(float[] acc, float dt)
        {
            var index = new Dictionary<NetAgent, int>(); for (int i = 0; i < Agents.Count; i++) index[Agents[i]] = i;
            foreach (var kv in _byLane.OrderBy(k => k.Key))
            {
                var lane = Net.Lanes[kv.Key]; var list = kv.Value;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var a = list[i]; if (a.External) continue;
                    a.Speed = Math.Max(0f, a.Speed + acc[index[a]] * dt); float move = a.Speed * dt;
                    NetAgent l = i + 1 < list.Count ? list[i + 1] : lane.IsLoop && list.Count > 1 ? list[0] : null;
                    if (l != null && l != a)
                    {
                        float g = TrafficNet.Ahead(lane, a.U, l.U - l.LenOnLane); if (lane.IsLoop && g > lane.Length - l.LenOnLane - 0.5f) g = 0f;
                        if (move > g - 0.2f) { move = Math.Max(0f, g - 0.2f); a.Speed = Math.Min(a.Speed, l.Speed); }          // hard no-overlap guarantee on a lane
                    }
                    a.U += move; a.ToGo -= move; a.Run += move; if (lane.IsLoop) a.U = TrafficNet.Wrap(a.U, lane.Length);
                    a.Still = a.Speed < 0.3f ? a.Still + dt : 0f;
                }
            }
        }

        void Transfer()
        {
            var gone = new List<NetAgent>();
            foreach (var a in Agents)
            {
                for (int guard = 0; guard < 3 && a.ToGo <= 0f && a.Next >= 0; guard++)
                {
                    var lane = Net.Lanes[a.Lane]; float over = -a.ToGo;
                    if (lane.Kind != NetLaneKind.Connector)
                    {
                        var cn = Net.Lanes[a.Next];
                        if (cn.Move == NetMove.UTurn && Dist(a) > Cfg.SpawnMin) { gone.Add(a); break; }          // a dead end out of sight: the vehicle has left the map
                        bool hold = cn.Yield && !a.Committed;
                        var on = On(cn.Index); if (on != null) foreach (var b in on) if (b != a && b.U - b.Len < over + 0.2f) hold = true;
                        if (hold) { a.U = cn.FromU; a.ToGo = 0f; a.Speed = 0f; break; }
                        int from = a.Lane; Shift(a, cn.FromU); a.Run = over; a.Lane = cn.Index; a.U = over; a.Next = cn.To; a.ToGo = cn.Length - over;
                        Moved?.Invoke(a, from, a.Lane);
                    }
                    else
                    {
                        var t = Net.Lanes[lane.To]; float u = t.IsLoop ? TrafficNet.Wrap(lane.ToU + over, t.Length) : lane.ToU + over;
                        bool hold = false; var on = On(t.Index);
                        if (on != null) foreach (var b in on) { if (b == a) continue; float d = TrafficNet.Ahead(t, lane.ToU, b.U); if (d >= 0f && d < 120f && d - b.Len < over + 0.2f) hold = true; }
                        if (hold) { a.U = lane.Length; a.ToGo = 0f; a.Speed = 0f; break; }
                        int from = a.Lane; Shift(a, lane.Length); a.Run = over; a.Lane = t.Index; a.U = u;
                        if (lane.Junction >= 0 && _reserved.TryGetValue(lane.Junction, out int holder) && holder == a.Id) _reserved.Remove(lane.Junction);
                        a.EntryLeg = lane.Move == NetMove.RingEntry ? Net.Lanes[lane.From].Corridor.Id : null; a.Committed = false; a.Wait = 0f;
                        Plan(a);
                        Moved?.Invoke(a, from, a.Lane);
                    }
                }
            }
            foreach (var a in gone) Agents.Remove(a);
        }

        static void Shift(NetAgent a, float leftAt) { a.Prev2 = a.Prev; a.Prev2U = a.PrevU; a.Prev = a.Lane; a.PrevU = leftAt; a.PrevRun = a.Run - (a.U - leftAt); }

        // ------------------------------------------------------------------ route choice

        float TargetWeight(NetLane cn)
        {
            var t = Net.Lanes[cn.To]; float w = t.Weight;
            if (t.DeadEnd && t.Out.Count == 1) w *= 0.15f;              // a cul-de-sac takes little traffic
            if (cn.Move == NetMove.UTurn) w *= 0.01f;
            return w;
        }

        /// <summary>Choose the next connector for an agent that has just arrived on a road or ring lane at its U.</summary>
        void Plan(NetAgent a)
        {
            var lane = Net.Lanes[a.Lane]; a.Next = -1; a.ToGo = float.PositiveInfinity;
            if (lane.IsLoop)
            {
                var exits = lane.Out.Where(o => Net.Lanes[o].Move == NetMove.RingExit).ToList();
                var options = exits.Where(o => Net.Lanes[Net.Lanes[o].To].Corridor.Id != a.EntryLeg).ToList(); if (options.Count == 0) options = exits;
                if (options.Count == 0) return;
                int pick = Pick(options); a.Next = pick; a.ToGo = TrafficNet.Ahead(lane, a.U, Net.Lanes[pick].FromU);
                if (a.ToGo < 0.5f) a.ToGo += lane.Length;
                return;
            }
            var ahead = lane.Out.Where(o => Net.Lanes[o].FromU >= a.U + 0.5f || Net.Lanes[o].FromU >= lane.Length - 1e-3f).ToList();
            int k = 0;
            while (k < ahead.Count)
            {
                float u0 = Net.Lanes[ahead[k]].FromU; var group = new List<int>();
                while (k < ahead.Count && Net.Lanes[ahead[k]].FromU - u0 < 3f) group.Add(ahead[k++]);
                bool terminal = group.Any(g => Net.Lanes[g].FromU >= lane.Length - 1e-3f) || k >= ahead.Count;
                if (!terminal)
                {
                    float wTurn = group.Sum(g => TargetWeight(Net.Lanes[g])), wOn = 3f * lane.Weight * (lane.DeadEnd && !ahead.Skip(k).Any(o => Net.Lanes[o].Move != NetMove.UTurn) ? 0.1f : 1f);
                    if (_rng.NextDouble() * (wTurn + wOn) >= wTurn) continue;
                }
                int pick = Pick(group); a.Next = pick; a.ToGo = Net.Lanes[pick].FromU - a.U; return;
            }
        }

        int Pick(List<int> options)
        {
            float total = options.Sum(o => TargetWeight(Net.Lanes[o]) * (Net.Lanes[o].Move == NetMove.Straight ? 3f : 1f));
            double r = _rng.NextDouble() * total;
            foreach (int o in options) { r -= TargetWeight(Net.Lanes[o]) * (Net.Lanes[o].Move == NetMove.Straight ? 3f : 1f); if (r < 0) return o; }
            return options[options.Count - 1];
        }

        // ------------------------------------------------------------------ spawning

        void Candidates()
        {
            long key = Key(_px, _pz); if (key == _candCell) return; _candCell = key;
            float cx = ((float)Math.Floor(_px / CellMetres) + 0.5f) * CellMetres, cz = ((float)Math.Floor(_pz / CellMetres) + 0.5f) * CellMetres;
            float lo = Math.Max(0f, Cfg.SpawnMin - CellMetres), hi = Cfg.SpawnAhead + CellMetres; int r = (int)Math.Ceiling(hi / CellMetres);
            var list = new List<int>(); var cum = new List<float>(); float sum = 0f; int inDisc = 0;
            int ix = (int)Math.Floor(_px / CellMetres), iz = (int)Math.Floor(_pz / CellMetres);
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (!_cells.TryGetValue(((long)(ix + dx) << 32) ^ (uint)(iz + dz), out var cell)) continue;
                    foreach (int i in cell)
                    {
                        var s = _samples[i]; float d = (float)Math.Sqrt((s.x - cx) * (s.x - cx) + (s.z - cz) * (s.z - cz));
                        if (d <= Cfg.SpawnAhead) inDisc++;
                        if (d < lo || d > hi) continue;
                        list.Add(i); sum += Net.Lanes[s.lane].Weight; cum.Add(sum);
                    }
                }
            _cand = list.ToArray(); _candCum = cum.ToArray(); _candKm = inDisc * SampleStep / 1000f;
        }

        public int Target { get { Candidates(); return Math.Min(Cfg.MaxAgents, Math.Max(_cand.Length > 0 ? 2 : 0, (int)Math.Round(Cfg.DensityPerKmPerDir * _candKm))); } }

        void TrySpawn()
        {
            if (Agents.Count >= Target || _cand.Length == 0) return;
            float total = _candCum[_candCum.Length - 1];
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float r = (float)_rng.NextDouble() * total; int lo = 0, hi = _candCum.Length - 1;
                while (lo < hi) { int mid = (lo + hi) >> 1; if (_candCum[mid] < r) lo = mid + 1; else hi = mid; }
                var s = _samples[_cand[lo]]; float d = (float)Math.Sqrt((s.x - _px) * (s.x - _px) + (s.z - _pz) * (s.z - _pz));
                if (d < Cfg.SpawnMin || d > Cfg.SpawnAhead) continue;
                var lane = Net.Lanes[s.lane]; var kind = PickKind(); var p = IdmParams.For(kind);
                if (!SpawnClear(lane, s.u, p.Length)) continue;
                float factor = 0.85f + (float)_rng.NextDouble() * 0.25f;
                var a = new NetAgent { Id = _nextId++, Kind = kind, P = p, DesiredFactor = factor, Lane = lane.Index, U = s.u, Run = 1000f };
                Plan(a);
                float v = Math.Min(p.DesiredSpeed * factor, Net.LimitAt(lane, s.u)) * 0.8f;
                if (a.Next >= 0) { var cn = Net.Lanes[a.Next]; v = Math.Min(v, (float)Math.Sqrt(cn.Speed * cn.Speed + 2f * 1.5f * Math.Max(0f, a.ToGo - 2f))); if (cn.Yield) v = Math.Min(v, (float)Math.Sqrt(2f * 1.5f * Math.Max(0f, a.ToGo - 2f))); }
                float lg = FirstAhead(lane, s.u, out float lspeed); if (!float.IsPositiveInfinity(lg)) v = Math.Min(v, lspeed);
                a.Speed = v; Agents.Add(a);
                return;                                                 // at most one spawn per step (no bursts)
            }
        }

        bool SpawnClear(NetLane lane, float u, float length)
        {
            float need = Cfg.MinSpawnGap + length;
            foreach (var a in Agents.Concat(_player != null ? new[] { _player } : new NetAgent[0]))
            {
                var l = Net.Lanes[a.Lane];
                if (a.Lane == lane.Index) { float d = lane.IsLoop ? Math.Min(TrafficNet.Wrap(a.U - u, lane.Length), TrafficNet.Wrap(u - a.U, lane.Length)) : Math.Abs(a.U - u); if (d < need + a.Len) return false; }
                else if (l.Kind == NetLaneKind.Connector && (l.To == lane.Index || l.From == lane.Index)) { float at = l.To == lane.Index ? l.ToU : l.FromU; float d = lane.IsLoop ? Math.Min(TrafficNet.Wrap(at - u, lane.Length), TrafficNet.Wrap(u - at, lane.Length)) : Math.Abs(at - u); if (d < need + a.Len) return false; }
            }
            return true;
        }

        TrafficKind PickKind()
        {
            int r = _rng.Next(100);
            return r < 45 ? TrafficKind.Car : r < 60 ? TrafficKind.Taxi : r < 75 ? TrafficKind.Minibus : r < 90 ? TrafficKind.Truck : TrafficKind.Motorcycle;
        }

        // ------------------------------------------------------------------ poses and counts

        /// <summary>World position (the road surface under the vehicle's centre) and heading of an agent. A vehicle whose centre is still behind the start of its lane is drawn on the lane it came from.</summary>
        public void Pose(NetAgent a, out V3 pos, out V3 fwd)
        {
            var lane = Net.Lanes[a.Lane]; float back = a.Len * 0.5f - a.Run;
            if (back > 0f && a.Prev >= 0)
            {
                var pl = Net.Lanes[a.Prev];
                if (back > a.PrevRun && a.Prev2 >= 0) { var p2 = Net.Lanes[a.Prev2]; float u2 = a.Prev2U - (back - a.PrevRun); Net.PoseOn(p2, p2.IsLoop ? TrafficNet.Wrap(u2, p2.Length) : Math.Max(0f, u2), a.Id, out pos, out fwd); return; }
                float pu = a.PrevU - back; Net.PoseOn(pl, pl.IsLoop ? TrafficNet.Wrap(pu, pl.Length) : Math.Max(0f, pu), a.Id, out pos, out fwd); return;
            }
            float c = a.U - a.Len * 0.5f;
            Net.PoseOn(lane, lane.IsLoop ? TrafficNet.Wrap(c, lane.Length) : Math.Max(0f, c), a.Id, out pos, out fwd);
        }

        public NetTrafficStats Stats()
        {
            var s = new NetTrafficStats { Agents = Agents.Count };
            foreach (var a in Agents)
            {
                var l = Net.Lanes[a.Lane];
                if (l.Kind == NetLaneKind.Connector) s.InJunctions++; else if (l.IsLoop) s.OnRings++; else if (l.Corridor.IsMain) s.OnMain++; else s.OnRoads++;
                if (a.Wait > 0f) s.Waiting++;
            }
            return s;
        }
    }
}
