using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ARO.NetCore;
using Xunit;
using Xunit.Abstractions;

/// <summary>
/// Network traffic (docs/TRAFFIC_NETWORK_PROPOSAL.md, approved by the owner on 2026-11-22): the tests the proposal promised, written against the Kano city route. No two vehicles overlap on a lane or
/// across a junction; a joining vehicle waits for its gap; nobody enters a roundabout in front of a circulating vehicle; every vehicle that leaves a lane continues on a connected one; nobody stands
/// still for ever over ten simulated minutes; vehicles appear and vanish out of sight, within the tier's cap; the single-corridor model of every other route is untouched.
/// </summary>
public class TrafficNetworkTests
{
    readonly ITestOutputHelper _out; public TrafficNetworkTests(ITestOutputHelper o) { _out = o; }
    static readonly JsonSerializerOptions Opt = new JsonSerializerOptions { IncludeFields = true };
    static RouteSpec Load(string id) => JsonSerializer.Deserialize<RouteSpec>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, id + ".route.json")), Opt);
    static RouteModel _city; static RouteModel City => _city ??= RouteModel.Build(Load("ng-kano-city"));
    static TrafficNet _net; static TrafficNet Net => _net ??= TrafficNet.Build(City);

    /// <summary>Where the player stands in the long runs: on the A2 inside the city, in the middle-ring street grid and beside a roundabout of the ring road.</summary>
    static IEnumerable<(string name, float x, float z)> Spots()
    {
        var m = City; var a2 = m.Main.Spline.PositionAt(2200f); yield return ("a2", a2.X, a2.Z);
        var st = m.CorridorById("fi02_street_1").Spline.PositionAt(400f); yield return ("street", st.X, st.Z);
        var rb = m.Rings.First(r => r.Id == "rr_a2"); yield return ("ring", rb.Cx + rb.OuterPavedRadius + 30f, rb.Cz);
    }

    sealed class Run
    {
        public NetTrafficSim Sim; public int Moves, BadMoves, Overlaps, EntryCuts, Commits, BadCommits, Teleports, MaxStill; public float MaxStillSeconds;
        public readonly Dictionary<NetMove, int> ByMove = new Dictionary<NetMove, int>(); public readonly HashSet<string> RoadsUsed = new HashSet<string>(); public int RingEntries, MaxAgents, Spawned, BadSpawns;
        public double Ms; public List<string> Notes = new List<string>();
    }

    static Run Simulate(float x, float z, float seconds, int maxAgents = 40, int seed = 7, float dt = 0.05f)
    {
        var cfg = new TrafficConfig { MaxAgents = maxAgents, Seed = seed, DensityPerKmPerDir = 6f * 0.8f };
        var sim = new NetTrafficSim(Net, cfg); var run = new Run { Sim = sim }; sim.SetPlayerPosition(x, z);
        sim.Moved = (a, from, to) =>
        {
            run.Moves++; var f = Net.Lanes[from]; var t = Net.Lanes[to];
            bool ok = f.Kind == NetLaneKind.Connector ? f.To == to : t.Kind == NetLaneKind.Connector && t.From == from;
            if (!ok) run.BadMoves++;
            if (t.Kind == NetLaneKind.Connector) { run.ByMove[t.Move] = run.ByMove.TryGetValue(t.Move, out int n) ? n + 1 : 1; }
            else run.RoadsUsed.Add(t.Corridor.Id);
            if (t.IsLoop)
            {   // the vehicle now on the ring: the circulating vehicle behind it must have room and time (no cutting in)
                run.RingEntries++;
                foreach (var b in sim.Agents) if (b != a && b.Lane == to)
                {
                    float gap = TrafficNet.Wrap(a.U - a.Len - b.U, t.Length); if (gap > t.Length * 0.5f) continue;
                    if (gap < 2f || (b.Speed > 1f && gap / b.Speed < 0.8f)) { run.EntryCuts++; run.Notes.Add($"ring cut: {t} gap {gap:0.0} m, follower {b.Speed:0.0} m/s"); }
                }
            }
        };
        sim.Committed = (a, cn) =>
        {
            run.Commits++;
            for (int i = 0; i < cn.Priority.Length; i++)
            {
                var pl = Net.Lanes[cn.Priority[i]]; float uc = cn.PriorityU[i];
                foreach (var b in sim.Agents) if (b.Lane == pl.Index)
                {
                    float d = pl.IsLoop ? TrafficNet.Wrap(uc - b.U, pl.Length) : uc - b.U; if (d < 0f || d > 160f) continue;
                    if (d < 8f || d / Math.Max(0.5f, b.Speed) < sim.MinGapSeconds - 1e-3f) { run.BadCommits++; run.Notes.Add($"commit into a gap of {d:0.0} m at {b.Speed:0.0} m/s on {pl}"); }
                }
            }
        };
        var last = new Dictionary<int, V3>(); var seen = new HashSet<int>(); var sw = System.Diagnostics.Stopwatch.StartNew();
        int steps = (int)(seconds / dt);
        for (int k = 0; k < steps; k++)
        {
            sim.Step(dt);
            run.MaxAgents = Math.Max(run.MaxAgents, sim.Agents.Count);
            foreach (var a in sim.Agents)
            {
                if (seen.Add(a.Id))
                {
                    run.Spawned++; var p0 = Net.CentrePoint(Net.Lanes[a.Lane], a.U); float d0 = (float)Math.Sqrt((p0.X - x) * (p0.X - x) + (p0.Z - z) * (p0.Z - z));
                    if (d0 < cfg.SpawnMin - 25f || d0 > cfg.SpawnAhead + 25f) run.BadSpawns++;
                }
                sim.Pose(a, out var pos, out _);
                if (last.TryGetValue(a.Id, out var lp)) { float dx = pos.X - lp.X, dz = pos.Z - lp.Z, j = (float)Math.Sqrt(dx * dx + dz * dz); if (j > a.Speed * dt + 2.5f) { run.Teleports++; if (run.Notes.Count < 30) run.Notes.Add($"jump {j:0.0} m on {Net.Lanes[a.Lane]} (prev {(a.Prev >= 0 ? Net.Lanes[a.Prev].ToString() : "-")})"); } }
                last[a.Id] = pos;
                if (a.Still > run.MaxStillSeconds) run.MaxStillSeconds = a.Still;
            }
            foreach (var g in sim.Agents.GroupBy(a => a.Lane))
            {
                var lane = Net.Lanes[g.Key]; var l = g.OrderBy(a => a.U).ToList();
                for (int i = 0; i < l.Count; i++)
                {
                    if (!lane.IsLoop && i + 1 >= l.Count) break; if (l.Count < 2) break;
                    var f = l[i]; var ld = l[(i + 1) % l.Count];
                    float gap = lane.IsLoop ? TrafficNet.Wrap(ld.U - ld.LenOnLane - f.U, lane.Length) : ld.U - ld.LenOnLane - f.U;      // the part of the leader on this lane (its tail may still be in the junction box)
                    if (lane.IsLoop && gap > lane.Length - ld.LenOnLane - f.LenOnLane + 0.5f) gap = -1f;
                    if (gap < -0.05f) { run.Overlaps++; if (run.Notes.Count < 30) run.Notes.Add($"overlap {gap:0.00} on {lane}"); }
                }
            }
        }
        run.Ms = sw.Elapsed.TotalMilliseconds / steps;
        return run;
    }

    static readonly Dictionary<string, Run> _runs = new Dictionary<string, Run>();
    static Run TenMinutes(string spot) { lock (_runs) { if (!_runs.TryGetValue(spot, out var r)) { var s = Spots().First(p => p.name == spot); _runs[spot] = r = Simulate(s.x, s.z, 600f); } return r; } }
    void Report(string spot, Run r)
    {
        _out.WriteLine($"{spot}: {r.Spawned} vehicles, at most {r.MaxAgents} at once, {r.Moves} lane changes, {r.Commits} gaps accepted, {r.RingEntries} ring entries, {r.RoadsUsed.Count} roads, longest stop {r.MaxStillSeconds:0} s, {r.Ms:0.000} ms per step");
        _out.WriteLine("  moves: " + string.Join(", ", r.ByMove.OrderBy(k => k.Key).Select(k => k.Key + " " + k.Value)));
        foreach (var n in r.Notes.Take(12)) _out.WriteLine("  " + n);
    }

    // ------------------------------------------------------------------ the network

    [Fact] public void EveryRoadAndRoundaboutOfTheCityIsInTheNetwork()
    {
        var m = City; var net = Net;
        foreach (var c in m.AllCorridors)
        {
            if (c.IsRing) { Assert.NotNull(net.Lane(c.Id, 1)); Assert.Null(net.Lane(c.Id, -1)); }
            else { Assert.NotNull(net.Lane(c.Id, 1)); Assert.NotNull(net.Lane(c.Id, -1)); }
        }
        foreach (var l in net.Lanes.Where(l => l.Kind == NetLaneKind.Road))
            Assert.True(l.Out.Any(o => net.Lanes[o].FromU >= l.Length - 1e-3f), l + " has no way on at its end");                 // every lane leads somewhere (a turning round at a free end)
        foreach (var cn in net.Connectors)
        {
            var f = net.Lanes[cn.From]; var t = net.Lanes[cn.To];
            Assert.NotEqual(NetLaneKind.Connector, f.Kind); Assert.NotEqual(NetLaneKind.Connector, t.Kind);
            Assert.InRange(cn.FromU, 0f, f.Length); Assert.InRange(cn.ToU, 0f, t.Length); Assert.InRange(cn.Length, 1f, 90f);
            Assert.Equal(cn.Yield, cn.Priority.Length > 0); Assert.Equal(cn.Priority.Length, cn.PriorityU.Length);
        }
        foreach (var rb in m.Rings)
        {
            var ring = net.Lane(rb.Id, 1);
            Assert.Equal(rb.Legs.Count, ring.Out.Count(o => net.Lanes[o].Move == NetMove.RingExit));
            Assert.Equal(rb.Legs.Count, ring.In.Count(o => net.Lanes[o].Move == NetMove.RingEntry));
            Assert.All(ring.In.Select(o => net.Lanes[o]), e => Assert.True(e.Yield && e.Priority.Single() == ring.Index, "entering a roundabout gives way to the ring"));
        }
        int joins = m.Spec.roads.Sum(r => r.joins.Length);
        Assert.Equal(joins * 4, net.Connectors.Count(c => c.Move == NetMove.Right || c.Move == NetMove.Left));                          // per join: off and on, in both directions of the road joined
        Assert.True(net.Connectors.Count(c => c.Move == NetMove.Straight) >= 100, "the street grid's crossroads are crossed straight on");
        int uturns = net.Connectors.Count(c => c.Move == NetMove.UTurn);
        Assert.InRange(uturns, 1, 30);                                                                                                   // only the true dead ends: site roads and the city's edges
        Assert.All(net.Connectors.Where(c => c.Move == NetMove.UTurn), c => Assert.True(net.Lanes[c.From].DeadEnd));
        _out.WriteLine($"{net.Lanes.Count} lanes, {net.Connectors.Count()} connectors, {uturns} dead ends");
    }

    [Fact] public void LanesAreOnTheRightOfTheirDirectionAndInsideTheRoad()
    {
        var net = Net;
        foreach (var l in net.Lanes.Where(l => l.Kind == NetLaneKind.Road).Take(400))
            for (float u = 5f; u < l.Length - 5f; u += 37f)
            {
                float s = l.S(u); float lat = TrafficNet.LateralOf(l.Corridor, l.Dir, s, 0);
                Assert.True(lat * l.Dir > 0f, $"{l} at {s:0}: lane centre {lat:0.0} is not on the right of the travel direction");
                Assert.True(Math.Abs(lat) < l.Corridor.HalfPavedAt(s) + 0.01f, $"{l} at {s:0}: lane centre {lat:0.0} off the pavement");
            }
        foreach (var rb in City.Rings)
        {
            var c = rb.Corridor;
            for (int seed = 0; seed < 4; seed++) { float lat = TrafficNet.LateralOf(c, 1, 3f, seed); Assert.True(Math.Abs(lat) < rb.Section.PavedHalfWidth, rb.Id); }
            c.Spline.TangentAt(0f, out float tx, out float tz); var p = c.Spline.PositionAt(0f);
            float cross = (p.X - rb.Cx) * tz - (p.Z - rb.Cz) * tx;
            Assert.True(cross > 0f, rb.Id + ": the ring's stations must run anticlockwise seen from above, x east and z north (right-hand traffic: the island on the driver's left)");
        }
    }

    // ------------------------------------------------------------------ ten simulated minutes at three places

    [Theory, InlineData("a2"), InlineData("street"), InlineData("ring")]
    public void NoVehicleOverlapsAnotherOrJumps(string spot)
    {
        var r = TenMinutes(spot); Report(spot, r);
        Assert.True(r.Spawned > 30, $"{r.Spawned} vehicles in ten minutes");
        Assert.Equal(0, r.Overlaps);
        Assert.Equal(0, r.Teleports);
    }

    [Theory, InlineData("a2"), InlineData("street"), InlineData("ring")]
    public void EveryVehicleThatLeavesALaneContinuesOnAConnectedOne(string spot)
    {
        var r = TenMinutes(spot); Report(spot, r);
        Assert.True(r.Moves > 50, $"{r.Moves} moves"); Assert.Equal(0, r.BadMoves);
    }

    [Theory, InlineData("a2"), InlineData("street"), InlineData("ring")]
    public void JoiningVehiclesWaitForTheirGapAndNobodyCutsIntoARoundabout(string spot)
    {
        var r = TenMinutes(spot); Report(spot, r);
        Assert.True(r.Commits > 10, $"{r.Commits} gaps accepted");
        Assert.Equal(0, r.BadCommits); Assert.Equal(0, r.EntryCuts);
    }

    [Theory, InlineData("a2"), InlineData("street"), InlineData("ring")]
    public void NobodyStandsStillForEver(string spot)
    {
        var r = TenMinutes(spot); Report(spot, r);
        Assert.True(r.MaxStillSeconds < 60f, $"a vehicle stood still for {r.MaxStillSeconds:0} s");
    }

    [Theory, InlineData("a2"), InlineData("street"), InlineData("ring")]
    public void VehiclesAppearOutOfSightAndStayWithinTheCap(string spot)
    {
        var r = TenMinutes(spot); Report(spot, r);
        Assert.Equal(0, r.BadSpawns); Assert.InRange(r.MaxAgents, 10, 40);
    }

    [Fact] public void TrafficTurnsAtJunctionsAndGoesRoundTheRoundabouts()
    {
        var all = new[] { "a2", "street", "ring" }.Select(TenMinutes).ToList();
        int Move(NetMove mv) => all.Sum(r => r.ByMove.TryGetValue(mv, out int n) ? n : 0);
        foreach (var mv in new[] { NetMove.Right, NetMove.Left, NetMove.Straight, NetMove.RingEntry, NetMove.RingExit }) Assert.True(Move(mv) > 5, $"{Move(mv)} {mv} moves");
        Assert.True(all.Sum(r => r.RoadsUsed.Count) > 40, "traffic must spread over the side roads");
        var ring = TenMinutes("ring"); Assert.True(ring.RingEntries > 10, $"{ring.RingEntries} vehicles entered a roundabout near the ring-road spot");
    }

    [Fact] public void TheSameSeedGivesTheSameTraffic()
    {
        var s = Spots().First(); string Digest(NetTrafficSim sim) => string.Join(";", sim.Agents.Select(a => $"{a.Id}:{a.Lane}:{a.U:0.###}:{a.Speed:0.###}"));
        var a = Simulate(s.x, s.z, 60f); var b = Simulate(s.x, s.z, 60f);
        Assert.Equal(Digest(a.Sim), Digest(b.Sim)); Assert.NotEmpty(a.Sim.Agents);
        var c = Simulate(s.x, s.z, 60f, seed: 8); Assert.NotEqual(Digest(a.Sim), Digest(c.Sim));
    }

    [Fact] public void TrafficBrakesForThePlayerAndWaitsForItAtAJunction()
    {
        // the player stands still on the A2 inside the city: the vehicles behind it on its lane stop behind it, never through it
        var m = City; var p = m.Main.Spline.PositionAt(2200f); m.Main.Spline.TangentAt(2200f, out float tx, out float tz);
        var sim = new NetTrafficSim(Net, new TrafficConfig { MaxAgents = 40, Seed = 3 });
        sim.SetPlayer(p.X, p.Z, tx, tz, 0f, 8f); Assert.NotNull(sim.Player);
        var lane = Net.Lanes[sim.Player.Lane]; Assert.Same(m.Main, lane.Corridor); Assert.Equal(1, lane.Dir);
        int behind = 0;
        for (int k = 0; k < 6000; k++)
        {
            sim.SetPlayer(p.X, p.Z, tx, tz, 0f, 8f); sim.Step(0.05f);
            foreach (var a in sim.Agents.Where(a => a.Lane == lane.Index && a.U < sim.Player.U))
            {
                Assert.True(a.U <= sim.Player.U - sim.Player.Len + 0.05f, $"vehicle {a.Id} drove into the player ({a.U:0.0} vs {sim.Player.U:0.0})");
                if (sim.Player.U - sim.Player.Len - a.U < 3f) behind++;
            }
        }
        Assert.True(behind > 0, "no vehicle ever queued behind the stationary player");
    }

    [Fact] public void OnlyTheKanoCityAsksForNetworkTraffic()
    {
        Assert.True(Load("ng-kano-city").traffic.network);
        foreach (var id in new[] { "ng-lagos-ibadan", "ng-kano-city-e1", "ng-kano-city-f1", "ng-kano-city-p1", "ng-kano-e5-f1" })
        {
            var s = Load(id); Assert.False(s.traffic != null && s.traffic.network, id + " keeps the single-corridor model");
        }
    }

    /// <summary>The single-corridor model every other route uses is untouched by the network model: the same seed gives exactly the traffic it gave before (digest recorded on 2026-11-22, before NetTrafficSim existed in the game).</summary>
    [Fact] public void TheSingleCorridorModelIsUnchanged()
    {
        var sim = new TrafficSim(20000f, new TrafficConfig { Seed = 7 }); float ps = 3000f;
        for (int k = 0; k < 4000; k++) { ps += 0.05f * 15f; sim.SetPlayer(ps, 15f, 1, 8f); sim.Step(0.05f, ps); }
        string text = string.Join(";", sim.Agents.Select(a => $"{a.Id}:{a.Kind}:{a.Dir}:{a.S:0.000}:{a.Speed:0.000}"));
        ulong h = 1469598103934665603UL; foreach (char c in text) { h ^= c; h *= 1099511628211UL; }
        _out.WriteLine($"{sim.Agents.Count} vehicles, digest {h:x16}");
        Assert.Equal(DIGEST, h.ToString("x16"));
    }
    const string DIGEST = "c4a941cd7014b615";

    [Fact] public void ThePhoneTierCostStaysSmall()
    {
        var s = Spots().First(p => p.name == "street");
        var r = Simulate(s.x, s.z, 120f, maxAgents: 20, dt: 0.02f);
        _out.WriteLine($"{r.Ms:0.000} ms per 20 ms step with {r.MaxAgents} vehicles");
        Assert.True(r.Ms < 2.0, $"{r.Ms:0.000} ms per step");
    }
}
