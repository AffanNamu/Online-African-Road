using System;
using System.Linq;
using ARO.NetCore;
using Xunit;

public class TrafficTests
{
    static TrafficAgent Make(TrafficKind k, int dir, float s, float v) => new TrafficAgent { Kind = k, Dir = dir, S = s, Speed = v, P = IdmParams.For(k) };

    [Fact] public void FreeRoadConvergesToDesiredSpeed()
    {
        var p = IdmParams.For(TrafficKind.Car); float v = 0;
        for (int i = 0; i < 6000; i++) v += Idm.Acceleration(p, v, 20f, float.PositiveInfinity, 0) * 0.05f;
        Assert.InRange(v, 19.7f, 20.01f);
    }

    [Fact] public void NeverExceedsDesiredSpeedOnFreeRoad()
    {
        var p = IdmParams.For(TrafficKind.Car); float v = 0, max = 0;
        for (int i = 0; i < 4000; i++) { v += Idm.Acceleration(p, v, 20f, float.PositiveInfinity, 0) * 0.05f; max = Math.Max(max, v); }
        Assert.True(max <= 20.01f);
    }

    [Fact] public void FollowsSlowerLeaderAtItsSpeed()
    {
        var p = IdmParams.For(TrafficKind.Car); float v = 22f, gap = 80f; const float leader = 12f;
        for (int i = 0; i < 12000; i++) { float a = Idm.Acceleration(p, v, 22f, gap, leader); v = Math.Max(0, v + a * 0.05f); gap += (leader - v) * 0.05f; Assert.True(gap > 0, $"collision at step {i}"); }
        Assert.InRange(v, 11.5f, 12.5f);
        Assert.True(gap > p.MinGap);
    }

    [Fact] public void BrakesHardButNeverBeyondPhysicalLimitForStoppedObstacle()
    {
        var p = IdmParams.For(TrafficKind.Car); float v = 22f, gap = 70f;
        for (int i = 0; i < 4000 && v > 0.01f; i++)
        {
            float a = Idm.Acceleration(p, v, 22f, gap, 0f);
            Assert.True(a >= -Idm.MaxBrake - 1e-3f);
            v = Math.Max(0, v + a * 0.02f); gap -= v * 0.02f;
            Assert.True(gap > 0, "hit the stopped obstacle");
        }
        Assert.True(v < 0.05f);
    }

    [Fact] public void LargerTimeHeadwayMeansLargerFollowingGap()
    {
        float Eq(TrafficKind k) { var p = IdmParams.For(k); float v = 15f, gap = 60f; for (int i = 0; i < 30000; i++) { v = Math.Max(0, v + Idm.Acceleration(p, v, 22f, gap, 15f) * 0.02f); gap += (15f - v) * 0.02f; } return gap; }
        Assert.True(Eq(TrafficKind.Truck) > Eq(TrafficKind.Motorcycle));
    }

    [Fact] public void OppositeDirectionAgentsMoveTowardLowerS()
    {
        var sim = new TrafficSim(10000f, new TrafficConfig { DensityPerKmPerDir = 0 });
        var a = Make(TrafficKind.Car, -1, 5000f, 10f); sim.Agents.Add(a);
        sim.Step(1f, 5000f);
        Assert.True(a.S < 5000f);
    }

    [Fact] public void SameLaneFollowerNeverOverlapsLeaderOverLongRunWithRandomSlowdowns()
    {
        var sim = new TrafficSim(20000f, new TrafficConfig { DensityPerKmPerDir = 0 });
        var rng = new Random(3);
        var lead = Make(TrafficKind.Truck, 1, 400f, 15f); var f1 = Make(TrafficKind.Car, 1, 300f, 22f); var f2 = Make(TrafficKind.Taxi, 1, 200f, 22f);
        sim.Agents.AddRange(new[] { lead, f1, f2 });
        for (int step = 0; step < 6000; step++)
        {
            // leader randomly brakes hard / accelerates by overriding its speed (external disturbance)
            if (step % 200 == 0) lead.Speed = (float)rng.NextDouble() * 20f;
            sim.Step(0.05f, lead.S);
            Assert.True(lead.Rear - f1.S >= -1e-3f, $"f1 overlaps leader at {step}");
            Assert.True(f1.Rear - f2.S >= -1e-3f, $"f2 overlaps f1 at {step}");
        }
    }

    [Fact] public void PlayerIsAnObstacleTrafficBrakesFor()
    {
        var sim = new TrafficSim(20000f, new TrafficConfig { DensityPerKmPerDir = 0 });
        var car = Make(TrafficKind.Car, 1, 100f, 22f); sim.Agents.Add(car);
        sim.SetPlayer(250f, 0f, 1, 8f);          // player stopped ahead in the same direction
        for (int i = 0; i < 2000; i++) { sim.Step(0.05f, 250f); Assert.True(car.S <= 250f - 8f + 1e-3f, "drove through the player"); }
        Assert.True(car.Speed < 0.5f);
    }

    [Fact] public void PlayerInOppositeLaneDoesNotBlock()
    {
        var sim = new TrafficSim(20000f, new TrafficConfig { DensityPerKmPerDir = 0 });
        var car = Make(TrafficKind.Car, 1, 100f, 20f); sim.Agents.Add(car);
        sim.SetPlayer(250f, 0f, -1, 8f);
        for (int i = 0; i < 100; i++) sim.Step(0.05f, 250f);
        Assert.True(car.Speed > 15f);
    }

    [Fact] public void SpeedLimitIsRespected()
    {
        var sim = new TrafficSim(20000f, new TrafficConfig { DensityPerKmPerDir = 0 }) { SpeedLimit = s => 8f };
        var car = Make(TrafficKind.Car, 1, 100f, 0f); sim.Agents.Add(car);
        for (int i = 0; i < 4000; i++) sim.Step(0.05f, car.S);
        Assert.True(car.Speed <= 8f * 1.05f + 0.1f);
    }

    [Fact] public void SpawnsRespectDensityAndNeverNearThePlayer()
    {
        var cfg = new TrafficConfig { DensityPerKmPerDir = 8f, Seed = 11 };
        var sim = new TrafficSim(40000f, cfg);
        for (int i = 0; i < 400; i++) sim.Step(0.1f, 20000f);
        foreach (int d in new[] { 1, -1 })
        {
            int n = sim.Agents.Count(a => a.Dir == d);
            Assert.InRange(n, 1, (int)Math.Ceiling(8f * (cfg.SpawnAhead + cfg.SpawnBehind) / 1000f) + 3);
        }
        Assert.All(sim.Agents, a => Assert.True(Math.Abs(a.S - 20000f) > cfg.SpawnBehindMin - 5f || a.Speed >= 0));
    }

    [Fact] public void NoSpawnAppearsInsideTheNoPopInZone()
    {
        var cfg = new TrafficConfig { DensityPerKmPerDir = 12f, Seed = 5 };
        var sim = new TrafficSim(40000f, cfg);
        var seen = new System.Collections.Generic.HashSet<int>();
        for (int i = 0; i < 600; i++)
        {
            sim.Step(0.1f, 20000f);
            foreach (var a in sim.Agents.Where(a => seen.Add(a.Id)))
            {
                float rel = a.S - 20000f;   // position at first sight (it spawned this very step)
                Assert.True(rel >= cfg.SpawnMin - 2f || (rel <= -cfg.SpawnBehindMin + 2f), $"spawned at rel {rel}");
            }
        }
    }

    [Fact] public void FarAgentsAreDespawnedSoCostStaysBounded()
    {
        var cfg = new TrafficConfig { DensityPerKmPerDir = 6f, Seed = 2 };
        var sim = new TrafficSim(60000f, cfg);
        float player = 5000f;
        for (int i = 0; i < 3000; i++) { player += 2f; sim.Step(0.1f, player); Assert.True(sim.Agents.Count <= cfg.MaxAgents); }
        Assert.All(sim.Agents, a => Assert.True(Math.Abs(a.S - player) <= cfg.DespawnRadius + 50f));
    }

    [Fact] public void DensityScalesWithConfig()
    {
        int Count(float dens) { var sim = new TrafficSim(40000f, new TrafficConfig { DensityPerKmPerDir = dens, Seed = 9 }); for (int i = 0; i < 800; i++) sim.Step(0.1f, 20000f); return sim.Agents.Count; }
        Assert.True(Count(12f) > Count(3f));
        Assert.Equal(0, Count(0f));
    }

    [Fact] public void SimulationIsDeterministicForASeed()
    {
        string Run() { var sim = new TrafficSim(40000f, new TrafficConfig { Seed = 42 }); for (int i = 0; i < 500; i++) sim.Step(0.1f, 20000f + i); return string.Join(",", sim.Agents.Select(a => $"{a.Kind}{a.Dir}{Math.Round(a.S, 2)}")); }
        Assert.Equal(Run(), Run());
    }

    [Fact] public void MixOfVehicleTypesAppears()
    {
        var sim = new TrafficSim(80000f, new TrafficConfig { DensityPerKmPerDir = 10f, MaxAgents = 200, Seed = 21 });
        var kinds = new System.Collections.Generic.HashSet<TrafficKind>(); float player = 5000f;
        for (int i = 0; i < 4000; i++) { player += 3f; sim.Step(0.1f, player); foreach (var a in sim.Agents) kinds.Add(a.Kind); }
        Assert.Equal(5, kinds.Count);
    }

    [Fact] public void InvalidRouteLengthRejected() => Assert.Throws<ArgumentOutOfRangeException>(() => new TrafficSim(0));
}
