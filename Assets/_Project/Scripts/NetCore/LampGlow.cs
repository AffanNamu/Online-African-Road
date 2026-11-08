using System;
using System.Collections.Generic;

namespace ARO.NetCore
{
    /// <summary>Which picture of the glow atlas a vertex uses, and how the shader treats it.</summary>
    public static class GlowKind { public const float Halo = 0f, Pool = 1f, Streak = 2f; }

    /// <summary>
    /// Engine-neutral buffers of the glow mesh: ONE additive mesh that carries, for every lamp of the loaded chunks, a camera-facing halo, a pool of light draped on the ground and
    /// (street lamps) a reflection streak for the wet road. The Unity layer turns it into a Mesh with a custom additive unlit shader; the lamp level, wetness and distance fade are shader
    /// uniforms, so a whole night's lighting is a few material writes per frame and one draw call.
    /// Per vertex: position, atlas corner (u, v in 0..1), colour, and a data vector (kind, half size in metres for halos, intensity, beacon flag).
    /// </summary>
    public sealed class GlowBuffers
    {
        public readonly List<V3> Vertices = new List<V3>();
        public readonly List<V2> Corner = new List<V2>();
        public readonly List<Rgb> Colour = new List<Rgb>();
        public readonly List<float[]> Data = new List<float[]>();     // [kind, halfSize, intensity, beaconFlag]
        public readonly List<int> Triangles = new List<int>();
        public int Halos, Pools, Streaks;
        public int VertexCount => Vertices.Count;

        public int Add(V3 p, float u, float v, Rgb c, float kind, float half, float intensity, float beacon)
        {
            Vertices.Add(p); Corner.Add(new V2(u, v)); Colour.Add(c); Data.Add(new[] { kind, half, intensity, beacon }); return Vertices.Count - 1;
        }

        public void Quad(int a, int b, int c, int d) { Triangles.Add(a); Triangles.Add(c); Triangles.Add(b); Triangles.Add(b); Triangles.Add(c); Triangles.Add(d); }

        /// <summary>Every index in range, every number finite. Returns the first problem or null.</summary>
        public string Validate()
        {
            int n = Vertices.Count;
            if (Corner.Count != n || Colour.Count != n || Data.Count != n) return "attribute counts differ";
            if (Triangles.Count % 3 != 0) return "triangle index count is not a multiple of 3";
            foreach (int i in Triangles) if (i < 0 || i >= n) return "index " + i + " out of range";
            for (int i = 0; i < n; i++)
            {
                if (!Vertices[i].IsFinite) return "vertex " + i + " is not finite";
                if (!Colour[i].IsFinite) return "colour " + i + " is not finite";
                foreach (float f in Data[i]) if (float.IsNaN(f) || float.IsInfinity(f)) return "data " + i + " is not finite";
            }
            return null;
        }
    }

    public static class LampGlowGeometry
    {
        /// <summary>How far above the surface the flat pieces are laid (the shader adds a depth offset on top).</summary>
        public const float Lift = 0.05f;
        public const int PoolSegments = 2;                 // 2 x 2 cells: follows camber and grade
        public const int StreakStations = 6;
        public const float StreakHalfLength = 11f, StreakHalfWidth = 0.85f;

        /// <summary>
        /// Builds the glow mesh for <paramref name="lamps"/>. Positions are relative to <paramref name="origin"/> (a mesh in world coordinates 40 km from the origin would lose precision).
        /// <paramref name="groundY"/> gives the surface height at (x, z) in world coordinates. Halos for every lamp; pools for lamps and glow lights that have a radius; streaks for street lamps.
        /// </summary>
        public static GlowBuffers Build(IList<Lamp> lamps, V3 origin, Func<float, float, float> groundY, bool streaks = true)
        {
            var g = new GlowBuffers();
            foreach (var l in lamps)
            {
                var col = new Rgb(l.R, l.G, l.B); float beacon = l.Kind == LampKind.Beacon ? 1f : 0f;
                // halo: one quad; the shader turns it to face the camera around the centre
                if (l.Halo > 0.01f)
                {
                    var c = new V3(l.Pos.X - origin.X, l.Pos.Y - origin.Y, l.Pos.Z - origin.Z); float half = l.Halo * 0.5f;
                    int a = g.Add(c, 0f, 0f, col, GlowKind.Halo, half, l.Intensity, beacon), b = g.Add(c, 1f, 0f, col, GlowKind.Halo, half, l.Intensity, beacon);
                    int cc = g.Add(c, 0f, 1f, col, GlowKind.Halo, half, l.Intensity, beacon), d = g.Add(c, 1f, 1f, col, GlowKind.Halo, half, l.Intensity, beacon);
                    g.Quad(a, b, cc, d); g.Halos++;
                }
                if (l.Kind == LampKind.Beacon || l.PoolRadius <= 0.05f) continue;
                float px = l.Pos.X + l.OutX * l.PoolShift, pz = l.Pos.Z + l.OutZ * l.PoolShift;
                // pool: a square grid draped on the surface, brightest in the middle (the atlas tile is a soft disc)
                {
                    int n = PoolSegments, row = n + 1, baseIndex = g.VertexCount; float r = l.PoolRadius;
                    for (int j = 0; j <= n; j++)
                        for (int i = 0; i <= n; i++)
                        {
                            float u = i / (float)n, v = j / (float)n, x = px + (u * 2f - 1f) * r, z = pz + (v * 2f - 1f) * r;
                            g.Add(new V3(x - origin.X, groundY(x, z) + Lift - origin.Y, z - origin.Z), u, v, col, GlowKind.Pool, 0f, l.Intensity, 0f);
                        }
                    for (int j = 0; j < n; j++) for (int i = 0; i < n; i++) g.Quad(baseIndex + j * row + i, baseIndex + j * row + i + 1, baseIndex + (j + 1) * row + i, baseIndex + (j + 1) * row + i + 1);
                    g.Pools++;
                }
                // wet-road streak: a long thin strip along the road, strongest at the middle
                if (streaks && l.Kind == LampKind.Lamp)
                {
                    int baseIndex = g.VertexCount; float rx = -l.DirZ, rz = l.DirX;      // across the road
                    for (int k = 0; k < StreakStations; k++)
                    {
                        float v = k / (float)(StreakStations - 1), along = (v * 2f - 1f) * StreakHalfLength;
                        for (int side = 0; side < 2; side++)
                        {
                            float across = (side == 0 ? -1f : 1f) * StreakHalfWidth, x = px + l.DirX * along + rx * across, z = pz + l.DirZ * along + rz * across;
                            g.Add(new V3(x - origin.X, groundY(x, z) + Lift - origin.Y, z - origin.Z), side, v, col, GlowKind.Streak, 0f, l.Intensity, 0f);
                        }
                    }
                    for (int k = 0; k < StreakStations - 1; k++) g.Quad(baseIndex + k * 2, baseIndex + k * 2 + 1, baseIndex + (k + 1) * 2, baseIndex + (k + 1) * 2 + 1);
                    g.Streaks++;
                }
            }
            return g;
        }
    }

    /// <summary>
    /// The pictures of the glow atlas, generated in code (no art files): three square tiles side by side, halo | pool | streak. One channel (0..1).
    /// The same function is used by the game to fill the texture and by the tests to check it, so what is drawn is what was verified.
    /// </summary>
    public static class LampGlowTexture
    {
        public const int Tile = 64, Width = Tile * 3, Height = Tile;

        static float Smooth(float t) { t = t < 0f ? 0f : t > 1f ? 1f : t; return t * t * (3f - 2f * t); }

        /// <summary>The brightness (0..1) of tile `kind` (0 halo, 1 pool, 2 streak) at tile coordinates u, v in 0..1.</summary>
        public static float Sample(int kind, float u, float v)
        {
            float x = u * 2f - 1f, y = v * 2f - 1f, r = (float)Math.Sqrt(x * x + y * y);
            if (kind == 0)
            {   // a hot core inside a wide orange halo
                float core = (float)Math.Exp(-(r / 0.13f) * (r / 0.13f)), halo = (float)Math.Exp(-(r / 0.42f) * (r / 0.42f)) * 0.55f;
                return Math.Min(1f, (core + halo) * Smooth((1f - r) / 0.18f));
            }
            if (kind == 1)
            {   // a soft pool: bright under the lamp, falling to nothing at the edge
                float f = Math.Max(0f, 1f - r); return f * f * (3f - 2f * f) * 0.9f;
            }
            {   // streak: narrow across the road, long and fading along it; brightest near the middle
                float across = (float)Math.Exp(-(x / 0.42f) * (x / 0.42f)), along = (float)Math.Pow(Math.Max(0f, 1f - Math.Abs(y)), 1.5f);
                return across * along * Smooth((1f - Math.Abs(x)) / 0.25f);
            }
        }

        /// <summary>Width x Height bytes, row by row from v = 0, 0..255.</summary>
        public static byte[] Build()
        {
            var px = new byte[Width * Height];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                {
                    int kind = x / Tile; float u = ((x % Tile) + 0.5f) / Tile, v = (y + 0.5f) / Height;
                    float s = Sample(kind, u, v); px[y * Width + x] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(s * 255f)));
                }
            return px;
        }
    }
}
