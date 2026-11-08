using System;
using System.Collections.Generic;

namespace ARO.NetCore
{
    public static class LampKind
    {
        public const byte Lamp = 0, Glow = 1, Beacon = 2;
        public static byte Parse(string kind) => kind == "glow" ? Glow : kind == "beacon" ? Beacon : Lamp;
    }

    /// <summary>One light source in the world (the head of a street light, a window, a stall bulb, a mast beacon), in world coordinates. Plain data: the Unity layer draws and lights from it.</summary>
    public struct Lamp
    {
        public long Key;                 // stable identity (from the head position): the same lamp has the same key after its chunk is rebuilt, so the real-light hand-over does not flicker
        public V3 Pos;                   // the head
        public float DirX, DirZ;         // unit direction along the road at the lamp (horizontal): the way a reflection streak runs
        public float OutX, OutZ;         // unit direction from the pole towards where the head points (horizontal): the way the pool of light is shifted
        public byte Kind;                // LampKind
        public float R, G, B, Intensity, Range, Halo, PoolRadius, PoolShift;
        public bool Real;                // may take a pooled real light
    }

    /// <summary>
    /// Turns the props of a chunk into lamps and answers where the ground is under them. Pure and deterministic: the same chunk always yields the same lamps, in the same order.
    /// </summary>
    public static class LampField
    {
        /// <summary>The lamps of a set of prop instances (those whose catalog entry has a light).</summary>
        public static List<Lamp> FromInstances(RouteModel m, IEnumerable<PropInstance> instances, Func<string, PropDef> lookup)
        {
            var res = new List<Lamp>();
            foreach (var inst in instances)
            {
                var def = lookup(inst.Prop); if (def == null || !def.HasLight) continue;
                var l = def.light; byte kind = LampKind.Parse(l.kind);
                // the way the road runs here (a streak lies along it); without a road nearby the prop's own heading is used
                float dx, dz;
                if (m != null && m.Main.Spline.Nearest(inst.Pos.X, inst.Pos.Z, 300f, out float s, out _, out _)) { m.Main.Spline.TangentAt(s, out dx, out dz); }
                else { float y = inst.YawDeg * (float)Math.PI / 180f; dx = (float)Math.Cos(y); dz = -(float)Math.Sin(y); }
                float yaw = inst.YawDeg * (float)Math.PI / 180f, fx = (float)Math.Sin(yaw), fz = (float)Math.Cos(yaw);   // the prop's local +z in world axes
                foreach (var h in l.heads)
                {
                    var world = PropBatcher.Place(new V3(h.x, h.y, h.z), inst.Pos, inst.YawDeg, inst.Scale);
                    // pole axis to head, in world axes: rotate the local (x, z) offset by the yaw
                    float ox = (float)Math.Cos(yaw) * h.x + (float)Math.Sin(yaw) * h.z, oz = -(float)Math.Sin(yaw) * h.x + (float)Math.Cos(yaw) * h.z, ol = (float)Math.Sqrt(ox * ox + oz * oz);
                    if (ol < 0.05f || l.shiftFront) { ox = fx; oz = fz; } else { ox /= ol; oz /= ol; }
                    res.Add(new Lamp
                    {
                        Key = KeyOf(world), Pos = world, DirX = dx, DirZ = dz, OutX = ox, OutZ = oz, Kind = kind,
                        R = l.r, G = l.g, B = l.b, Intensity = l.intensity, Range = l.range * inst.Scale, Halo = l.haloSize * inst.Scale, PoolRadius = l.poolRadius * inst.Scale, PoolShift = l.poolShift * inst.Scale,
                        Real = l.realLight && kind == LampKind.Lamp
                    });
                }
            }
            return res;
        }

        /// <summary>Identity of a lamp: its head position to a tenth of a metre.</summary>
        public static long KeyOf(V3 p)
        {
            unchecked
            {
                long x = (long)Math.Round(p.X * 10.0), y = (long)Math.Round(p.Y * 10.0), z = (long)Math.Round(p.Z * 10.0);
                return x * 73856093L ^ y * 19349663L ^ z * 83492791L;
            }
        }

        /// <summary>
        /// The height of what a person would see at (x, z): the road surface on any road formation (the terrain is deliberately 0.35 m under it), otherwise the terrain.
        /// Used to lay a pool of light on the ground without burying it or floating it.
        /// </summary>
        public static float SurfaceHeight(RouteModel m, float x, float z)
        {
            foreach (var c in m.AllCorridors)
            {
                if (!c.Spline.Nearest(x, z, c.MaxFormationHalf + 1f, out float s, out float lat, out _)) continue;
                if (s <= 1e-3f || s >= c.Spline.Length - 1e-3f) continue;
                if (Math.Abs(lat) <= c.HalfFormationAt(s)) return c.SurfaceY(s, lat);
            }
            return m.Terrain.HeightAt(x, z);
        }
    }
}
