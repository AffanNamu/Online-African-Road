using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ARO.NetCore
{
    // ===================================================================================================== quality tiers
    /// <summary>One rendering/world quality level. Every number is a cost lever; tiers must be monotonic (Low is never more expensive than Medium than High).</summary>
    public sealed class QualityTier
    {
        public string Name;
        public float ShadowDistance; public int ShadowCascades, ShadowResolution; public bool SoftShadows;
        public int MsaaSamples; public float RenderScale;
        public bool Tonemapping, ColorGrading, Vignette, Bloom;
        public float LodBias;                       // QualitySettings.lodBias
        public int LoadRadiusChunks, UnloadRadiusChunks;
        public float PropDensity;                   // multiplies PropRule.presence
        public float PropCullScale;                 // multiplies each prop's cull distance
        public float TerrainDetail;                 // multiplies terrain cells per tile
        public int MaxTrafficAgents; public float TrafficDensity;
        public float FarClip;
    }

    public struct DeviceInfo
    {
        public bool IsMobile; public int SystemMemoryMb, GraphicsMemoryMb; public string Requested;
    }

    public static class QualityTiers
    {
        public static readonly QualityTier Low = new QualityTier
        {
            Name = "low", ShadowDistance = 60f, ShadowCascades = 1, ShadowResolution = 1024, SoftShadows = false, MsaaSamples = 1, RenderScale = 0.8f,
            Tonemapping = true, ColorGrading = false, Vignette = false, Bloom = false, LodBias = 0.7f, LoadRadiusChunks = 2, UnloadRadiusChunks = 3,
            PropDensity = 0.5f, PropCullScale = 0.6f, TerrainDetail = 0.5f, MaxTrafficAgents = 20, TrafficDensity = 3f, FarClip = 900f
        };
        public static readonly QualityTier Medium = new QualityTier
        {
            Name = "medium", ShadowDistance = 110f, ShadowCascades = 2, ShadowResolution = 2048, SoftShadows = false, MsaaSamples = 2, RenderScale = 1f,
            Tonemapping = true, ColorGrading = true, Vignette = true, Bloom = false, LodBias = 1f, LoadRadiusChunks = 3, UnloadRadiusChunks = 5,
            PropDensity = 0.8f, PropCullScale = 1f, TerrainDetail = 1f, MaxTrafficAgents = 40, TrafficDensity = 5f, FarClip = 1500f
        };
        public static readonly QualityTier High = new QualityTier
        {
            Name = "high", ShadowDistance = 150f, ShadowCascades = 3, ShadowResolution = 4096, SoftShadows = true, MsaaSamples = 4, RenderScale = 1f,
            Tonemapping = true, ColorGrading = true, Vignette = true, Bloom = true, LodBias = 1.5f, LoadRadiusChunks = 4, UnloadRadiusChunks = 6,
            PropDensity = 1f, PropCullScale = 1.4f, TerrainDetail = 1.25f, MaxTrafficAgents = 80, TrafficDensity = 8f, FarClip = 2200f
        };

        public static IReadOnlyList<QualityTier> All => new[] { Low, Medium, High };

        public static bool TryGet(string name, out QualityTier tier)
        {
            tier = null; if (name == null) return false;
            tier = All.FirstOrDefault(t => t.Name == name.Trim().ToLowerInvariant());
            return tier != null;
        }

        /// <summary>Picks a tier: an explicit valid request wins; phones are Low; small or unknown memory is Low/Medium; big desktops are High. Conservative on purpose (WebGL reports clamped numbers).</summary>
        public static QualityTier Detect(DeviceInfo d)
        {
            if (TryGet(d.Requested, out var t)) return t;
            if (d.IsMobile) return Low;
            if (d.SystemMemoryMb > 0 && d.SystemMemoryMb < 3000) return Low;
            if (d.SystemMemoryMb >= 8000 && d.GraphicsMemoryMb >= 3000) return High;
            return Medium;
        }
    }

    // ===================================================================================================== LOD policy
    public static class LodPolicy
    {
        public const float ChunkMetres = 200f;

        /// <summary>Chebyshev distance in chunks between a chunk and the chunk the player is in.</summary>
        public static int Ring(int cx, int cz, int centreX, int centreZ) => Math.Max(Math.Abs(cx - centreX), Math.Abs(cz - centreZ));

        /// <summary>Terrain quads per tile edge: 5 m cells beside the player, 10 m, then 20 m far away. Scaled by the tier's TerrainDetail, never below 8.</summary>
        public static int TerrainCells(int ring, QualityTier tier)
        {
            int baseCells = ring <= 1 ? 40 : ring == 2 ? 20 : 10;
            int c = (int)Math.Round(baseCells * tier.TerrainDetail / 2.0) * 2;      // keep even
            return Math.Max(8, c);
        }

        /// <summary>Collision slabs only exist near the player (a vehicle never needs physics 400 m away).</summary>
        public static bool CollidersEnabled(int ring) => ring <= 2;
        public static int CollisionCells => 20;     // 10 m cells: plenty for wheel contact on smooth terrain; the road mesh carries the precise surface

        public static bool ShadowsEnabled(int ring, QualityTier tier) => ring <= Math.Max(1, tier.LoadRadiusChunks - 1) && tier.ShadowCascades > 0;

        public static float PropCullDistance(PropDef def, QualityTier tier) => Math.Max(20f, def.cullDistance * tier.PropCullScale);
        public static int PropLodLevel(PropDef def, float distance, QualityTier tier)
        {
            int levels = def.lodTriangles != null ? def.lodTriangles.Length : 1;
            float cull = PropCullDistance(def, tier);
            // Equal thirds of the cull distance per level, biased by lodBias (higher bias keeps detail longer).
            float f = distance / (cull / Math.Max(0.1f, tier.LodBias));
            int lod = (int)Math.Floor(f * levels);
            return Math.Max(0, Math.Min(levels - 1, lod));
        }
    }

    // ===================================================================================================== performance budgets
    public sealed class PerfBudget
    {
        public string Name; public long MaxTriangles; public int MaxDrawCalls; public float MaxTextureMb, MaxHeapMb, MaxFrameMs, MaxBuildMb;

        public static readonly PerfBudget Desktop = new PerfBudget { Name = "desktop-webgl", MaxTriangles = 1000000, MaxDrawCalls = 400, MaxTextureMb = 300f, MaxHeapMb = 900f, MaxFrameMs = 16.7f, MaxBuildMb = 120f };
        public static readonly PerfBudget Mobile = new PerfBudget { Name = "mobile", MaxTriangles = 250000, MaxDrawCalls = 150, MaxTextureMb = 150f, MaxHeapMb = 500f, MaxFrameMs = 33.3f, MaxBuildMb = 80f };
        public static PerfBudget For(QualityTier tier) => tier.Name == "low" ? Mobile : Desktop;
    }

    public struct PerfSample
    {
        public long Triangles; public int DrawCalls, ActiveRenderers, LoadedChunks;
        public float TextureMb, HeapMb, FrameMs;
    }

    public static class PerfCheck
    {
        /// <summary>Returns one line per exceeded budget. Values that were not measured (negative or zero where zero is impossible) are skipped, never counted as passing evidence.</summary>
        public static List<string> Evaluate(PerfSample s, PerfBudget b)
        {
            var v = new List<string>();
            if (s.Triangles > b.MaxTriangles) v.Add($"triangles {s.Triangles} > {b.MaxTriangles}");
            if (s.DrawCalls > b.MaxDrawCalls) v.Add($"draw calls {s.DrawCalls} > {b.MaxDrawCalls}");
            if (s.TextureMb > b.MaxTextureMb) v.Add($"texture memory {s.TextureMb:0.0} MB > {b.MaxTextureMb} MB");
            if (s.HeapMb > b.MaxHeapMb) v.Add($"managed heap {s.HeapMb:0.0} MB > {b.MaxHeapMb} MB");
            if (s.FrameMs > b.MaxFrameMs) v.Add($"frame time {s.FrameMs:0.0} ms > {b.MaxFrameMs} ms");
            return v;
        }
    }

    // ===================================================================================================== prop catalog
    [Serializable]
    public class PropDef
    {
        public string id, category, status, description, source, collider = "none";
        public float heightM, widthM, depthM, cullDistance = 200f;
        public int[] lodTriangles;
        public bool castShadow;
        public string[] tags;
    }

    [Serializable] public class PropCatalogSpec { public int schemaVersion = 1; public PropDef[] props; }

    public static class PropCatalogValidator
    {
        public static readonly string[] Categories = { "vegetation", "buildings", "props", "signs", "lights", "environment" };
        static readonly string[] Colliders = { "none", "box", "mesh" };
        static readonly Regex IdRx = new Regex(@"^[a-z0-9]+(_[a-z0-9]+)*$", RegexOptions.Compiled);

        /// <summary>Per-category LOD0 triangle ceilings (a single prop must stay cheap enough to appear hundreds of times).</summary>
        public static int Lod0Budget(string category) => category == "vegetation" ? 3000 : category == "buildings" ? 6000 : category == "signs" ? 800 : category == "lights" ? 1200 : 2500;

        public static List<string> Validate(PropCatalogSpec c)
        {
            var e = new List<string>();
            if (c == null || c.props == null || c.props.Length == 0) { e.Add("catalog is empty"); return e; }
            if (c.schemaVersion != 1) e.Add("schemaVersion must be 1");
            var ids = new HashSet<string>();
            foreach (var p in c.props)
            {
                if (p == null) { e.Add("a prop entry is null"); continue; }
                string tag = "prop '" + p.id + "'";
                if (string.IsNullOrEmpty(p.id) || !IdRx.IsMatch(p.id)) e.Add(tag + ": id must be lowercase_snake_case");
                else if (!ids.Add(p.id)) e.Add(tag + ": duplicate id");
                if (!Categories.Contains(p.category)) e.Add(tag + ": category must be one of " + string.Join(", ", Categories));
                if (p.status != "dev" && p.status != "production") e.Add(tag + ": status must be dev or production");
                if (!Colliders.Contains(p.collider)) e.Add(tag + ": collider must be none, box or mesh");
                if (p.heightM <= 0f || p.heightM > 80f || p.widthM <= 0f || p.widthM > 80f || p.depthM <= 0f || p.depthM > 80f) e.Add(tag + ": dimensions must be 0..80 m (real-world metres)");
                if (p.cullDistance < 20f || p.cullDistance > 2000f) e.Add(tag + ": cullDistance must be 20..2000 m");
                if (p.lodTriangles == null || p.lodTriangles.Length == 0 || p.lodTriangles.Length > 4) e.Add(tag + ": 1 to 4 LOD levels are required");
                else
                {
                    for (int i = 1; i < p.lodTriangles.Length; i++) if (p.lodTriangles[i] >= p.lodTriangles[i - 1]) e.Add(tag + ": lodTriangles must strictly decrease");
                    if (p.lodTriangles[0] > Lod0Budget(p.category)) e.Add(tag + ": LOD0 has " + p.lodTriangles[0] + " triangles (budget " + Lod0Budget(p.category) + " for " + p.category + ")");
                    if (p.lodTriangles.Any(t => t <= 0)) e.Add(tag + ": triangle counts must be positive");
                }
                if (string.IsNullOrEmpty(p.source)) e.Add(tag + ": source is required");
                else if (p.status == "dev" && !p.source.StartsWith("dev:")) e.Add(tag + ": a dev prop's source must be 'dev:<generator>'");
                else if (p.status == "production" && !p.source.StartsWith("Props/")) e.Add(tag + ": a production prop's source must be a Resources path under 'Props/'");
            }
            return e;
        }
    }

    // ===================================================================================================== prop scatter
    public struct PropInstance
    {
        public string Prop, Text, Origin;      // Origin: rule id or placement id
        public V3 Pos; public float YawDeg, Scale;
    }

    /// <summary>
    /// Deterministic roadside scatter. Every slot along the road decides independently from a hash of (route seed, rule, slot, side), so the result
    /// does not depend on which chunks are loaded or in which order: each instance belongs to exactly one chunk.
    /// </summary>
    public static class PropScatter
    {
        public const float ExclusionMargin = 3f;

        public static List<PropInstance> PlaceChunk(RouteModel m, float minX, float minZ, float maxX, float maxZ, float density = 1f)
        {
            var res = new List<PropInstance>();
            var main = m.Main; var spec = m.Spec; float L = main.Length;
            if (!SRange(main, minX, minZ, maxX, maxZ, 130f, out float sLo, out float sHi)) return res;

            var rules = spec.propRules ?? new PropRule[0];
            for (int ri = 0; ri < rules.Length; ri++)
            {
                var r = rules[ri]; float from = Math.Max(0f, r.sFrom), to = r.sTo > r.sFrom ? Math.Min(L, r.sTo) : L;
                float size = (r.spacingMin + r.spacingMax) * 0.5f, jitter = (r.spacingMax - r.spacingMin) * 0.25f;
                var zones = ParseZones(r.zones);
                int k0 = Math.Max(0, (int)Math.Floor((Math.Max(sLo, from) - from) / size) - 1), k1 = (int)Math.Ceiling((Math.Min(sHi, to) - from) / size) + 1;
                for (int k = k0; k <= k1; k++)
                {
                    float s = from + (k + 0.5f) * size + (Mathx.Hash01(spec.seed + ri * 7919, k, 11) * 2f - 1f) * jitter;
                    if (s < from || s > to || s < 0f || s > L) continue;
                    if (zones != null && !zones.Contains(main.ZoneAt(s))) continue;
                    if (main.InBridge(s, out _)) continue;
                    foreach (int side in SidesOf(r.side))
                    {
                        if (Mathx.Hash01(spec.seed + ri * 7919, k, side + 3) > r.presence * density) continue;
                        float off = main.HalfFormationAt(s) + r.offsetMin + Mathx.Hash01(spec.seed + ri * 104729, k, side + 7) * (r.offsetMax - r.offsetMin);
                        main.Spline.FrameAt(s, out var pos, out var fwd, out var right);
                        float x = pos.X + right.X * side * off, z = pos.Z + right.Z * side * off;
                        if (x < minX || x >= maxX || z < minZ || z >= maxZ) continue;
                        if (m.Terrain.InsideFormation(x, z, ExclusionMargin)) continue;
                        float towardRoad = (float)(Math.Atan2(-right.X * side, -right.Z * side) * 180.0 / Math.PI);
                        float yaw = (r.faceRoad ? towardRoad : Mathx.Hash01(spec.seed, k, 5) * 360f) + (Mathx.Hash01(spec.seed + ri, k, side + 13) * 2f - 1f) * r.yawJitterDeg;
                        float scale = Mathx.Lerp(r.scaleMin, r.scaleMax, Mathx.Hash01(spec.seed + ri, k, side + 19));
                        res.Add(new PropInstance { Prop = r.prop, Origin = r.id, Pos = new V3(x, m.Terrain.HeightAt(x, z), z), YawDeg = yaw, Scale = scale });
                    }
                }
            }

            foreach (var p in spec.props ?? new PropPlacementSpec[0])
            {
                main.Spline.FrameAt(p.s, out var pos, out var fwd, out var right);
                float x = pos.X + right.X * p.lateral, z = pos.Z + right.Z * p.lateral;
                if (x < minX || x >= maxX || z < minZ || z >= maxZ) continue;
                float heading = (float)(Math.Atan2(fwd.X, fwd.Z) * 180.0 / Math.PI);
                res.Add(new PropInstance { Prop = p.prop, Origin = p.id, Text = p.text, Pos = new V3(x, m.Terrain.HeightAt(x, z), z), YawDeg = heading + p.yawDeg, Scale = p.scale });
            }
            return res;
        }

        static HashSet<Zone> ParseZones(string z)
        {
            if (string.IsNullOrWhiteSpace(z)) return null;
            var set = new HashSet<Zone>();
            foreach (var part in z.Split(',')) if (RouteVocab.TryZone(part, out var zz)) set.Add(zz);
            return set;
        }

        static IEnumerable<int> SidesOf(string side) => side == "left" ? new[] { -1 } : side == "right" ? new[] { 1 } : new[] { -1, 1 };

        /// <summary>The range of route distances whose centre line lies within `margin` of the box.</summary>
        static bool SRange(RoadCorridor c, float minX, float minZ, float maxX, float maxZ, float margin, out float lo, out float hi)
        {
            lo = float.MaxValue; hi = float.MinValue;
            for (int i = 0; i < c.RingS.Length; i++)
                if (c.RingX[i] >= minX - margin && c.RingX[i] < maxX + margin && c.RingZ[i] >= minZ - margin && c.RingZ[i] < maxZ + margin)
                { lo = Math.Min(lo, c.RingS[i]); hi = Math.Max(hi, c.RingS[i]); }
            return hi >= lo;
        }
    }
}
