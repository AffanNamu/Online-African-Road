using System;
using System.Collections.Generic;

namespace ARO.NetCore
{
    /// <summary>
    /// How the weather looks at night, as pure numbers. The weather SIMULATION (rain, wetness, grip) stays in <c>WeatherSystem</c> and is only read; this turns what it reports into the strength of
    /// the visual wet-road layers (lamp reflection streaks, standing-water patches). Nothing here changes grip, physics or the world.
    /// </summary>
    public static class WeatherLook
    {
        /// <summary>A named test weather for smoke pages (?weather=): clear night, cloudy night, rain, and wet roads after the rain has stopped.</summary>
        public struct Preset { public string Name; public float Rain, Wetness, Cloud; }

        public static readonly Preset Clear = new Preset { Name = "clear", Rain = 0f, Wetness = 0f, Cloud = 0f };
        public static readonly Preset Cloudy = new Preset { Name = "cloudy", Rain = 0f, Wetness = 0f, Cloud = 0.85f };
        public static readonly Preset Rain = new Preset { Name = "rain", Rain = 0.9f, Wetness = 0.9f, Cloud = 0.9f };
        public static readonly Preset Wet = new Preset { Name = "wet", Rain = 0f, Wetness = 0.85f, Cloud = 0.5f };

        public static bool TryPreset(string name, out Preset p)
        {
            p = Clear; if (string.IsNullOrEmpty(name)) return false;
            switch (name.Trim().ToLowerInvariant())
            {
                case "clear": p = Clear; return true;
                case "cloudy": p = Cloudy; return true;
                case "rain": p = Rain; return true;
                case "wet": p = Wet; return true;
            }
            return false;
        }

        static float Clamp01(float v) => float.IsNaN(v) ? 0f : v < 0f ? 0f : v > 1f ? 1f : v;

        /// <summary>The overcast amount the lighting model gets: cloud cover on its own, or the rain's own darkening (the weather system's existing rule, rain x 1.1), whichever is larger.</summary>
        public static float Overcast(float cloud, float rain) => Math.Min(1f, Math.Max(Clamp01(cloud), Clamp01(rain) * 1.1f));

        /// <summary>Strength (0..1) of the reflection streaks under the lamps: none on a dry road, full on a soaked one; a little earlier than the puddles.</summary>
        public static float StreakStrength(float wetness) { float w = Clamp01(wetness); return Math.Min(1f, w * 1.25f); }

        /// <summary>Extra brightness of the lamp pools on wet asphalt (a wet surface throws more of the light back at the driver).</summary>
        public static float PoolBoost(float wetness) => 1f + 0.35f * Clamp01(wetness);

        /// <summary>How much of the standing water is visible: a puddle with threshold t shows once the road is wetter than t and fades in over 0.12.</summary>
        public static float PuddleVisible(float wetness, float threshold) { float t = (Clamp01(wetness) - threshold) / 0.12f; t = t < 0f ? 0f : t > 1f ? 1f : t; return t * t * (3f - 2f * t); }
    }

    /// <summary>One cosmetic standing-water patch on the road surface. Visual only: no depth, no splash, no grip effect.</summary>
    public struct Puddle
    {
        public long Key;
        public V3 Pos;               // centre on the surface (x, y, z)
        public float DirX, DirZ;     // along the road
        public float LengthM, WidthM;
        public float Threshold;      // wetness at which it appears, 0.15..0.85
    }

    /// <summary>
    /// Where standing water would collect on the road, deterministic from the route seed so every client sees the same puddles. Worn and damaged surfaces and open country get more, new good asphalt
    /// few; they lie near the lane edges and in the wheel paths, never on a median or off the paved width. This is COSMETIC: the engine has no water layer (RB-21 is not approved) and a puddle is a
    /// flat patch drawn on the road.
    /// </summary>
    public static class PuddleField
    {
        public const float StepM = 18f;

        static float Density(Surface sf, Zone z)
        {
            float d = sf == Surface.Damaged ? 0.55f : sf == Surface.Worn ? 0.32f : 0.12f;
            return z == Zone.Rural ? d * 1.2f : z == Zone.Highway ? d * 0.8f : d;
        }

        public static List<Puddle> PlaceChunk(RouteModel m, float minX, float minZ, float maxX, float maxZ)
        {
            var res = new List<Puddle>(); var main = m.Main; float L = main.Length;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < main.RingS.Length; i++)
                if (main.RingX[i] >= minX - 40f && main.RingX[i] < maxX + 40f && main.RingZ[i] >= minZ - 40f && main.RingZ[i] < maxZ + 40f) { lo = Math.Min(lo, main.RingS[i]); hi = Math.Max(hi, main.RingS[i]); }
            if (hi < lo) return res;
            int k0 = Math.Max(0, (int)Math.Floor(lo / StepM) - 1), k1 = (int)Math.Ceiling(hi / StepM) + 1;
            for (int k = k0; k <= k1; k++)
            {
                float s = (k + Mathx.Hash01(m.Spec.seed + 4001, k, 1)) * StepM; if (s < 5f || s > L - 5f) continue;
                if (Mathx.Hash01(m.Spec.seed + 4003, k, 2) > Density(main.SurfaceAt(s), main.ZoneAt(s))) continue;
                var run = main.RunAt(s); float half = main.HalfPavedAt(s); var sec = run.Section;
                // lateral: in the lane next to the edge or the outer wheel path; on a divided road never in the median
                float edge = Math.Max(0.5f, half - 0.4f), inner = sec.HasMedian ? sec.Spec.medianWidth * 0.5f + sec.Spec.innerShoulderWidth + 0.6f : 0.8f;
                float t = Mathx.Hash01(m.Spec.seed + 4007, k, 3), side = Mathx.Hash01(m.Spec.seed + 4009, k, 4) < 0.5f ? -1f : 1f;
                float lat = side * Mathx.Lerp(inner, edge, 0.25f + 0.75f * t);
                main.Spline.FrameAt(s, out var pos, out var fwd, out var right);
                float x = pos.X + right.X * lat, z = pos.Z + right.Z * lat;
                if (x < minX || x >= maxX || z < minZ || z >= maxZ) continue;
                float len = Mathx.Lerp(2.5f, 7.0f, Mathx.Hash01(m.Spec.seed + 4013, k, 5)), wid = Mathx.Lerp(1.1f, 2.6f, Mathx.Hash01(m.Spec.seed + 4021, k, 6));
                if (main.InBridge(s, out _)) { len *= 0.6f; wid *= 0.6f; }
                float y = main.SurfaceY(s, lat);
                res.Add(new Puddle
                {
                    Key = LampField.KeyOf(new V3(x, y, z)), Pos = new V3(x, y, z), DirX = fwd.X, DirZ = fwd.Z, LengthM = len, WidthM = wid,
                    Threshold = Mathx.Lerp(0.15f, 0.85f, Mathx.Hash01(m.Spec.seed + 4027, k, 7))
                });
            }
            return res;
        }
    }

    /// <summary>The standing-water mesh: one flat, draped patch per puddle (alpha-blended, dark water with a sky tint). Per vertex: position, patch coordinates (u, v) and the puddle's appearance threshold.</summary>
    public sealed class PuddleBuffers
    {
        public readonly List<V3> Vertices = new List<V3>();
        public readonly List<V2> Corner = new List<V2>();
        public readonly List<float> Threshold = new List<float>();
        public readonly List<int> Triangles = new List<int>();
        public int Patches => Triangles.Count / 24;      // 2 x 2 cells, 8 triangles per patch
        public int VertexCount => Vertices.Count;

        public string Validate()
        {
            int n = Vertices.Count; if (Corner.Count != n || Threshold.Count != n) return "attribute counts differ";
            foreach (int i in Triangles) if (i < 0 || i >= n) return "index out of range";
            for (int i = 0; i < n; i++) if (!Vertices[i].IsFinite) return "vertex " + i + " is not finite";
            return Triangles.Count % 3 == 0 ? null : "index count";
        }
    }

    public static class PuddleGeometry
    {
        public const float Lift = 0.035f;       // under the lamp pools (0.05) so a lit puddle shows its light

        /// <summary>Patches relative to <paramref name="origin"/>, each a 3 x 3 grid following the surface (<paramref name="groundY"/>).</summary>
        public static PuddleBuffers Build(IList<Puddle> puddles, V3 origin, Func<float, float, float> groundY)
        {
            var g = new PuddleBuffers();
            foreach (var p in puddles)
            {
                float rx = -p.DirZ, rz = p.DirX; int baseIndex = g.VertexCount;
                for (int j = 0; j < 3; j++)
                    for (int i = 0; i < 3; i++)
                    {
                        float u = i / 2f, v = j / 2f, along = (v - 0.5f) * p.LengthM, across = (u - 0.5f) * p.WidthM;
                        float x = p.Pos.X + p.DirX * along + rx * across, z = p.Pos.Z + p.DirZ * along + rz * across;
                        g.Vertices.Add(new V3(x - origin.X, groundY(x, z) + Lift - origin.Y, z - origin.Z)); g.Corner.Add(new V2(u, v)); g.Threshold.Add(p.Threshold);
                    }
                for (int j = 0; j < 2; j++)
                    for (int i = 0; i < 2; i++)
                    {
                        int a = baseIndex + j * 3 + i, b = a + 1, c = a + 3, d = c + 1;
                        g.Triangles.Add(a); g.Triangles.Add(c); g.Triangles.Add(b); g.Triangles.Add(b); g.Triangles.Add(c); g.Triangles.Add(d);
                    }
            }
            return g;
        }
    }

    /// <summary>The state of a vehicle's lights as the driver and other players see it. Pure and tested; the Unity layer only draws it.</summary>
    public static class VehicleLightPolicy
    {
        public enum Beam { Off, Low, High }

        /// <summary>High beam only works with the lights on: switching the lights off also dips the beam.</summary>
        public static Beam BeamOf(bool lightsOn, bool highBeam) => !lightsOn ? Beam.Off : highBeam ? Beam.High : Beam.Low;

        /// <summary>The indicator lamps for a blink phase. Hazards override the indicators: both sides flash together.</summary>
        public static void Indicators(int indicator, bool hazard, bool blinkOn, out bool left, out bool right)
        {
            left = right = false; if (!blinkOn) return;
            if (hazard) { left = right = true; return; }
            left = indicator < 0; right = indicator > 0;
        }

        /// <summary>Rear lamps: dim red with the lights on, bright on the brake; the brake works with the lights off too.</summary>
        public static float TailLevel(bool lightsOn, bool brake) => brake ? 1f : lightsOn ? 0.2f : 0f;

        /// <summary>Whether the driver is dazzling: a high beam toward something within <paramref name="rangeM"/> that is ahead (-1 to 1 is cos of the angle). Used by traffic to dip for the player.</summary>
        public static bool Dazzles(bool highBeam, float distanceM, float cosAngle, float rangeM = 120f) => highBeam && distanceM < rangeM && cosAngle > 0.9f;
    }
}
