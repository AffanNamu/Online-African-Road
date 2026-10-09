using System;
using System.Collections.Generic;
using System.Linq;

namespace ARO.NetCore
{
    public enum TrafficKind { Car, Taxi, Minibus, Truck, Motorcycle }

    /// <summary>Intelligent Driver Model parameters (metres, seconds, m/s, m/s^2).</summary>
    public sealed class IdmParams
    {
        public float DesiredSpeed, MaxAccel, ComfortDecel, TimeHeadway, MinGap, Length;

        public static IdmParams For(TrafficKind k)
        {
            switch (k)
            {
                case TrafficKind.Taxi:       return new IdmParams { DesiredSpeed = 21f, MaxAccel = 1.8f, ComfortDecel = 2.4f, TimeHeadway = 1.0f, MinGap = 1.8f, Length = 4.5f };
                case TrafficKind.Minibus:    return new IdmParams { DesiredSpeed = 18f, MaxAccel = 1.2f, ComfortDecel = 2.2f, TimeHeadway = 1.2f, MinGap = 2.0f, Length = 7.0f };
                case TrafficKind.Truck:      return new IdmParams { DesiredSpeed = 19f, MaxAccel = 0.8f, ComfortDecel = 1.8f, TimeHeadway = 1.8f, MinGap = 3.0f, Length = 12.0f };
                case TrafficKind.Motorcycle: return new IdmParams { DesiredSpeed = 24f, MaxAccel = 2.6f, ComfortDecel = 3.0f, TimeHeadway = 0.9f, MinGap = 1.5f, Length = 2.0f };
                default:                     return new IdmParams { DesiredSpeed = 22f, MaxAccel = 1.5f, ComfortDecel = 2.0f, TimeHeadway = 1.4f, MinGap = 2.0f, Length = 4.5f };
            }
        }
    }

    public static class Idm
    {
        public const float MaxBrake = 9f;   // physical limit, m/s^2

        /// <summary>Acceleration for a follower. gap = bumper-to-bumper distance to the leader (use float.PositiveInfinity for free road).</summary>
        public static float Acceleration(IdmParams p, float speed, float desiredSpeed, float gap, float leaderSpeed)
        {
            desiredSpeed = Math.Max(0.1f, desiredSpeed);
            double free = 1.0 - Math.Pow(speed / desiredSpeed, 4);
            double interact = 0.0;
            if (!float.IsPositiveInfinity(gap))
            {
                double g = Math.Max(gap, 0.01);
                double dv = speed - leaderSpeed;
                double sStar = p.MinGap + Math.Max(0.0, speed * p.TimeHeadway + speed * dv / (2.0 * Math.Sqrt(p.MaxAccel * p.ComfortDecel)));
                interact = (sStar / g) * (sStar / g);
            }
            double a = p.MaxAccel * (free - interact);
            return (float)Math.Max(-MaxBrake, a);
        }
    }

    public sealed class TrafficAgent
    {
        public int Id;
        public TrafficKind Kind;
        public int Dir;            // +1 = travelling toward increasing route distance, -1 = decreasing
        public float S;            // route distance of the FRONT bumper
        public float Speed;
        public float DesiredFactor = 1f;   // per-driver personality (0.85..1.1)
        public bool External;      // e.g. the player: not simulated, only obstructs
        public IdmParams P;

        public float Rear => S - Dir * P.Length;
        /// <summary>Progress along own direction (monotonic for sorting).</summary>
        public float Progress => S * Dir;
    }

    public sealed class TrafficConfig
    {
        public float DensityPerKmPerDir = 6f;   // vehicles per km per direction inside the active window
        public float SpawnMin = 350f;           // never spawn closer than this ahead (no pop-in)
        public float SpawnAhead = 800f;
        public float SpawnBehind = 250f, SpawnBehindMin = 120f;
        public float DespawnRadius = 1100f;
        public float MinSpawnGap = 35f;
        public int MaxAgents = 80;
        public int Seed = 7;
    }

    /// <summary>
    /// 1-D traffic on a two-way route: car following (IDM) per direction, speed limits by position, spawn/despawn windows
    /// around the player (so cost is bounded), and the player as an external obstacle that traffic brakes for.
    /// Pure C#: unit-tested without an engine.
    /// </summary>
    public sealed class TrafficSim
    {
        public readonly float RouteLength;
        public readonly TrafficConfig Cfg;
        public readonly List<TrafficAgent> Agents = new List<TrafficAgent>();
        public Func<float, float> SpeedLimit = s => 22f;   // m/s at route distance s
        readonly Random _rng; int _nextId = 1;
        TrafficAgent _player;

        public TrafficSim(float routeLength, TrafficConfig cfg = null)
        {
            if (routeLength <= 0) throw new ArgumentOutOfRangeException(nameof(routeLength));
            RouteLength = routeLength; Cfg = cfg ?? new TrafficConfig(); _rng = new Random(Cfg.Seed);
        }

        /// <summary>Report the player's vehicle so same-lane traffic brakes for it (and tailgating it is avoided).</summary>
        public void SetPlayer(float s, float speed, int dir, float length)
        {
            if (_player == null) { _player = new TrafficAgent { Id = 0, Kind = TrafficKind.Car, External = true, P = new IdmParams { Length = length, DesiredSpeed = 1, MaxAccel = 1, ComfortDecel = 1, TimeHeadway = 1, MinGap = 1 } }; }
            _player.S = s; _player.Speed = speed; _player.Dir = dir; _player.P.Length = length;
        }

        public void ClearPlayer() { _player = null; }

        public void Step(float dt, float playerS)
        {
            if (dt <= 0) return;
            Despawn(playerS);
            for (int d = -1; d <= 1; d += 2) TrySpawn(d, playerS);

            foreach (int dir in new[] { 1, -1 })
            {
                var lane = Agents.Where(a => a.Dir == dir).ToList();
                if (_player != null && _player.Dir == dir) lane.Add(_player);
                lane.Sort((x, y) => x.Progress.CompareTo(y.Progress));
                var accel = new float[lane.Count];
                for (int i = 0; i < lane.Count; i++)
                {
                    var a = lane[i]; if (a.External) continue;
                    float desired = Math.Min(a.P.DesiredSpeed * a.DesiredFactor, SpeedLimit(Clamp(a.S, 0, RouteLength)) * 1.05f);
                    float gap = float.PositiveInfinity, lv = 0;
                    if (i + 1 < lane.Count) { var l = lane[i + 1]; gap = (l.Rear - a.S) * dir; lv = l.Speed; }
                    accel[i] = Idm.Acceleration(a.P, a.Speed, desired, gap, lv);
                }
                // integrate back-to-front so followers see the leader's NEW position
                for (int i = lane.Count - 1; i >= 0; i--)
                {
                    var a = lane[i]; if (a.External) continue;
                    a.Speed = Math.Max(0f, a.Speed + accel[i] * dt);
                    a.S += dir * a.Speed * dt;
                    if (i + 1 < lane.Count)
                    {
                        var l = lane[i + 1]; float gap = (l.Rear - a.S) * dir;
                        if (gap < 0.2f) { a.S = l.Rear - dir * 0.2f; a.Speed = Math.Min(a.Speed, l.Speed); }   // hard no-overlap guarantee
                    }
                }
            }
        }

        void Despawn(float playerS)
        {
            Agents.RemoveAll(a => Math.Abs(a.S - playerS) > Cfg.DespawnRadius || a.S < -20 || a.S > RouteLength + 20);
        }

        void TrySpawn(int dir, float playerS)
        {
            if (Agents.Count >= Cfg.MaxAgents) return;
            float aheadLo = playerS + Cfg.SpawnMin, aheadHi = playerS + Cfg.SpawnAhead;
            float behindLo = playerS - Cfg.SpawnBehind, behindHi = playerS - Cfg.SpawnBehindMin;
            float windowLen = (aheadHi - aheadLo) + (behindHi - behindLo);
            float target = Cfg.DensityPerKmPerDir * (Cfg.SpawnAhead + Cfg.SpawnBehind) / 1000f;
            int current = Agents.Count(a => a.Dir == dir && a.S >= playerS - Cfg.SpawnBehind && a.S <= playerS + Cfg.SpawnAhead);
            if (current >= Math.Round(target)) return;

            for (int attempt = 0; attempt < 8; attempt++)
            {
                float pick = (float)_rng.NextDouble() * windowLen;
                float s = pick < (aheadHi - aheadLo) ? aheadLo + pick : behindLo + (pick - (aheadHi - aheadLo));
                if (s < 5 || s > RouteLength - 5) continue;
                var kind = PickKind();
                var p = IdmParams.For(kind);
                if (!SpawnClear(dir, s, p.Length)) continue;
                float factor = 0.85f + (float)_rng.NextDouble() * 0.25f;
                float v = Math.Min(p.DesiredSpeed * factor, SpeedLimit(s)) * 0.8f;
                var agent = new TrafficAgent { Id = _nextId++, Kind = kind, Dir = dir, S = s, Speed = v, DesiredFactor = factor, P = p };
                // do not spawn faster than the car in front
                var leader = Agents.Where(a => a.Dir == dir && a.Progress > agent.Progress).OrderBy(a => a.Progress).FirstOrDefault();
                if (leader != null) agent.Speed = Math.Min(agent.Speed, leader.Speed);
                Agents.Add(agent);
                return;   // at most one spawn per direction per step (no bursts)
            }
        }

        bool SpawnClear(int dir, float s, float length)
        {
            foreach (var a in Agents)
            {
                if (a.Dir != dir) continue;
                if (Math.Abs(a.S - s) < Cfg.MinSpawnGap + a.P.Length + length) return false;
            }
            if (_player != null && _player.Dir == dir && Math.Abs(_player.S - s) < Cfg.MinSpawnGap + 20) return false;
            return true;
        }

        TrafficKind PickKind()
        {
            int r = _rng.Next(100);
            return r < 45 ? TrafficKind.Car : r < 60 ? TrafficKind.Taxi : r < 75 ? TrafficKind.Minibus : r < 90 ? TrafficKind.Truck : TrafficKind.Motorcycle;
        }

        static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
