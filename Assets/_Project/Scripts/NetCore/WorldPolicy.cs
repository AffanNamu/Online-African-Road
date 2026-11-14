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
        public int LampLights;                      // real lights moved onto the lamps nearest the player (street lighting); the look does not depend on them
        public float LampGlowRange;                 // metres: how far the halo, pool and streak layer of a lamp is drawn
        public bool WetEffects;                     // reflection streaks under lamps and cosmetic puddles on wet roads
        public int AdditionalLightsPerObject;       // URP's per-object limit of additional lights

        /// <summary>A copy, so a player's choice never changes the shared tier objects.</summary>
        public QualityTier Clone() => (QualityTier)MemberwiseClone();
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
            PropDensity = 0.5f, PropCullScale = 0.6f, TerrainDetail = 0.5f, MaxTrafficAgents = 20, TrafficDensity = 3f, FarClip = 900f,
            LampLights = 0, LampGlowRange = 250f, WetEffects = true, AdditionalLightsPerObject = 4
        };
        public static readonly QualityTier Medium = new QualityTier
        {
            Name = "medium", ShadowDistance = 110f, ShadowCascades = 2, ShadowResolution = 2048, SoftShadows = false, MsaaSamples = 2, RenderScale = 1f,
            Tonemapping = true, ColorGrading = true, Vignette = true, Bloom = false, LodBias = 1f, LoadRadiusChunks = 3, UnloadRadiusChunks = 5,
            PropDensity = 0.8f, PropCullScale = 1f, TerrainDetail = 1f, MaxTrafficAgents = 40, TrafficDensity = 5f, FarClip = 1500f,
            LampLights = 4, LampGlowRange = 400f, WetEffects = true, AdditionalLightsPerObject = 6
        };
        public static readonly QualityTier High = new QualityTier
        {
            Name = "high", ShadowDistance = 150f, ShadowCascades = 3, ShadowResolution = 4096, SoftShadows = true, MsaaSamples = 4, RenderScale = 1f,
            Tonemapping = true, ColorGrading = true, Vignette = true, Bloom = true, LodBias = 1.5f, LoadRadiusChunks = 4, UnloadRadiusChunks = 6,
            PropDensity = 1f, PropCullScale = 1.4f, TerrainDetail = 1.25f, MaxTrafficAgents = 80, TrafficDensity = 8f, FarClip = 2200f,
            LampLights = 8, LampGlowRange = 600f, WetEffects = true, AdditionalLightsPerObject = 8
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
        /// <summary>Optional light emitted by the prop (street luminaire, shop or window light, mast beacon). JsonUtility gives a missing object a default instance, so test <see cref="HasLight"/>, never `light != null`.</summary>
        public LightSpec light;
        public bool HasLight => light != null && light.heads != null && light.heads.Length > 0;
        /// <summary>Optional text face (E2, RB-07): where a prop shows the text of a placement (sign, destination board, terminal name). JsonUtility gives a missing object a default instance, so test <see cref="HasLabel"/>.</summary>
        public LabelSpec label;
        public bool HasLabel => label != null && label.width > 0.01f && label.height > 0.01f;
        /// <summary>Gameplay interaction points (P1: the pumps a vehicle refuels at; entries and exits; later docks and bus bays), in the prop's local axes. Data from the transport kit catalogue. See <see cref="ServicePoints"/>.</summary>
        public SocketSpec[] sockets;
        public bool HasSockets => sockets != null && sockets.Length > 0;
        /// <summary>
        /// Collision boxes in the prop's local axes (metres before the instance scale). When present they REPLACE the single catalogue box: a place a vehicle drives into (a forecourt under a canopy, between
        /// pump islands) is solid only where the model is (columns, islands, the shop, walls). Only with collider "box".
        /// </summary>
        public BoxSpec[] boxes;
        public bool HasBoxes => boxes != null && boxes.Length > 0;
    }

    /// <summary>An interaction point on a prop, in the prop's local axes (x right, y up, z forward), metres before the instance scale. kind: fuel_pump, truck_fuel_stop, drive_in, drive_out, dock, bus_bay, ...</summary>
    [Serializable]
    public class SocketSpec
    {
        public string name, kind;
        public float x, y, z, yawDeg;
    }

    /// <summary>An axis-aligned box in the prop's local axes (min corner x0, y0, z0 and max corner x1, y1, z1), metres before the instance scale.</summary>
    [Serializable]
    public class BoxSpec
    {
        public float x0, y0, z0, x1, y1, z1;
    }

    /// <summary>A text face on a prop, in the prop's local axes (x right, y up, z forward = the way the prop faces), metres before the instance scale. The text reads from in front of the prop.</summary>
    [Serializable]
    public class LabelSpec
    {
        public float x, y, z;               // centre of the face
        public float width, height;         // 0 = no face. Both default to 0 on purpose: JsonUtility (the game) gives a catalogue entry WITHOUT a `label` a default instance, and that must read as "no label" (HasLabel)
        public float yawDeg;                // turns the face about the vertical axis relative to the prop (0 = facing the way the prop faces; 180 = on its back)
        public string colour = "white";     // white | black | yellow | red | green
        public int fontMax;                 // 0 = fit to the face
    }

    /// <summary>One light source on a prop, in the prop's local axes (x right, y up, z forward = the way the prop faces), metres before the instance scale.</summary>
    [Serializable] public class LampHead { public float x, y, z; }

    /// <summary>
    /// What a prop lights at night. The same spec drives the emissive head (through the prop's lamp sub-material), the camera-facing halo, the pool of light on the ground, the wet-road streak and,
    /// for the few lamps near the player, a real light. kind: lamp (street luminaire: halo, pool, streak, real-light candidate), glow (a building, stall or canopy light: halo and an optional small pool)
    /// or beacon (a blinking red mast light: halo only).
    /// </summary>
    [Serializable]
    public class LightSpec
    {
        public string kind = "lamp";
        public float r = 1f, g = 0.72f, b = 0.38f;       // light colour (gamma space, like every colour in the project)
        public float intensity = 1f;                      // multiplies the glow brightness and the real light's intensity
        public float range = 22f;                         // range of the real light, metres
        public float haloSize = 1.6f;                     // camera-facing halo, metres across
        public float poolRadius = 6f;                     // radius of the pool of light on the ground under the head; 0 = none
        public float poolShift = 0f;                      // metres the pool is moved from under the head, in the direction the head points away from the pole
        public bool realLight = true;                     // may take one of the pooled real lights when near the player
        public bool shiftFront;                           // the pool shifts along the prop's own forward axis (a wall light over a shop front) instead of away from the pole axis (a lamp on an arm)
        public LampHead[] heads;
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
                if (p.HasLight) ValidateLight(p, tag, e);
                if (p.HasLabel) ValidateLabel(p, tag, e);
                if (p.HasSockets) ValidateSockets(p, tag, e);
                if (p.HasBoxes) ValidateBoxes(p, tag, e);
            }
            return e;
        }

        public static readonly string[] SocketKinds = { "fuel_pump", "truck_fuel_stop", "drive_in", "drive_out", "dock", "bus_bay", "boarding", "loading" };

        static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        static void ValidateSockets(PropDef p, string tag, List<string> e)
        {
            var names = new HashSet<string>(); tag += " socket";
            foreach (var s in p.sockets)
            {
                if (s == null) { e.Add(tag + ": a socket is null"); continue; }
                if (string.IsNullOrEmpty(s.name) || !IdRx.IsMatch(s.name)) e.Add(tag + " '" + s.name + "': name must be lowercase_snake_case");
                else if (!names.Add(s.name)) e.Add(tag + " '" + s.name + "': duplicate name");
                if (!SocketKinds.Contains(s.kind)) e.Add(tag + " '" + s.name + "': kind must be one of " + string.Join(", ", SocketKinds));
                if (!Finite(s.x) || !Finite(s.y) || !Finite(s.z) || Math.Abs(s.x) > p.widthM * 0.5f + 3f || Math.Abs(s.z) > p.depthM * 0.5f + 3f || s.y < -0.5f || s.y > p.heightM)
                    e.Add(tag + " '" + s.name + "': the point must lie within the prop's footprint (3 m margin) and height");
                if (!Finite(s.yawDeg) || s.yawDeg < -360f || s.yawDeg > 360f) e.Add(tag + " '" + s.name + "': yawDeg must be -360..360");
            }
        }

        static void ValidateBoxes(PropDef p, string tag, List<string> e)
        {
            tag += " box"; int i = 0;
            if (p.collider != "box") e.Add(tag + "es need collider \"box\"");
            foreach (var b in p.boxes)
            {
                string t = tag + " " + i++;
                if (b == null) { e.Add(t + ": null"); continue; }
                if (!(Finite(b.x0) && Finite(b.y0) && Finite(b.z0) && Finite(b.x1) && Finite(b.y1) && Finite(b.z1)) || b.x1 - b.x0 < 0.05f || b.y1 - b.y0 < 0.05f || b.z1 - b.z0 < 0.05f) e.Add(t + ": min must be below max on every axis (at least 5 cm)");
                if (b.x0 < -p.widthM * 0.5f - 1f || b.x1 > p.widthM * 0.5f + 1f || b.z0 < -p.depthM * 0.5f - 1f || b.z1 > p.depthM * 0.5f + 1f || b.y0 < -0.5f || b.y1 > p.heightM + 0.5f)
                    e.Add(t + ": the box must lie within the prop's footprint and height (1 m margin)");
            }
        }

        public static readonly string[] LabelColours = { "white", "black", "yellow", "red", "green" };

        static void ValidateLabel(PropDef p, string tag, List<string> e)
        {
            var l = p.label; tag += " label";
            if (!(l.width >= 0.05f && l.width <= 30f && l.height >= 0.05f && l.height <= 30f)) e.Add(tag + ": width and height must be 0.05..30 m");
            if (float.IsNaN(l.x) || float.IsNaN(l.y) || float.IsNaN(l.z) || Math.Abs(l.x) > 40f || Math.Abs(l.z) > 40f || l.y < 0f || l.y > p.heightM + 1f) e.Add(tag + ": the face must lie within 40 m of the prop axis and no higher than the prop");
            if (!LabelColours.Contains(l.colour)) e.Add(tag + ": colour must be one of " + string.Join(", ", LabelColours));
            if (l.fontMax < 0 || l.fontMax > 400) e.Add(tag + ": fontMax must be 0..400");
            if (l.yawDeg < -360f || l.yawDeg > 360f) e.Add(tag + ": yawDeg must be -360..360");
        }

        public static readonly string[] LightKinds = { "lamp", "glow", "beacon" };

        static void ValidateLight(PropDef p, string tag, List<string> e)
        {
            var l = p.light; tag += " light";
            if (!LightKinds.Contains(l.kind)) e.Add(tag + ": kind must be one of " + string.Join(", ", LightKinds));
            if (l.heads.Length > 8) e.Add(tag + ": at most 8 heads");
            foreach (var h in l.heads)
            {
                if (h == null) { e.Add(tag + ": a head is null"); continue; }
                if (float.IsNaN(h.x) || float.IsNaN(h.y) || float.IsNaN(h.z) || Math.Abs(h.x) > 20f || Math.Abs(h.z) > 20f) e.Add(tag + ": head position must be within 20 m of the pole axis");
                if (h.y < 0f || h.y > p.heightM * 1.05f) e.Add(tag + ": head height must be between 0 and the prop height");
            }
            if (!(l.r >= 0f && l.r <= 1f && l.g >= 0f && l.g <= 1f && l.b >= 0f && l.b <= 1f)) e.Add(tag + ": colour components must be 0..1");
            if (!(l.intensity >= 0f && l.intensity <= 8f)) e.Add(tag + ": intensity must be 0..8");
            if (!(l.range >= 3f && l.range <= 60f)) e.Add(tag + ": range must be 3..60 m");
            if (!(l.haloSize >= 0f && l.haloSize <= 8f)) e.Add(tag + ": haloSize must be 0..8 m");
            if (!(l.poolRadius >= 0f && l.poolRadius <= 20f)) e.Add(tag + ": poolRadius must be 0..20 m");
            if (!(l.poolShift >= -10f && l.poolShift <= 10f)) e.Add(tag + ": poolShift must be -10..10 m");
            if (l.kind == "beacon" && (l.poolRadius > 0f || l.realLight)) e.Add(tag + ": a beacon has no ground pool and no real light");
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
            bool nearMain = SRange(main, minX, minZ, maxX, maxZ, 130f, out float sLo, out float sHi);
            if (!nearMain) { AddSideProps(m, minX, minZ, maxX, maxZ, res); FrontagePlacer.AddToChunk(m, minX, minZ, maxX, maxZ, density, res); return res; }      // side roads and site plates can lie farther from the main road than the scatter reaches

            var rules = spec.propRules ?? new PropRule[0];
            for (int ri = 0; ri < rules.Length; ri++)
            {
                var r = rules[ri]; float from = Math.Max(0f, r.sFrom), to = r.sTo > r.sFrom ? Math.Min(L, r.sTo) : L;
                float size = (r.spacingMin + r.spacingMax) * 0.5f, jitter = (r.spacingMax - r.spacingMin) * 0.25f;
                var zones = ParseZones(r.zones); var profiles = ParseProfiles(r.profiles);
                bool median = r.side == "median"; float dens = r.fixedDensity && density > 0f ? 1f : density, margin = r.exclusionMargin >= 0f ? r.exclusionMargin : ExclusionMargin;
                int k0 = Math.Max(0, (int)Math.Floor((Math.Max(sLo, from) - from) / size) - 1), k1 = (int)Math.Ceiling((Math.Min(sHi, to) - from) / size) + 1;
                for (int k = k0; k <= k1; k++)
                {
                    float s = from + (k + 0.5f) * size + (Mathx.Hash01(spec.seed + ri * 7919, k, 11) * 2f - 1f) * jitter;
                    if (s < from || s > to || s < 0f || s > L) continue;
                    if (zones != null && !zones.Contains(main.ZoneAt(s))) continue;
                    if (main.InBridge(s, out _)) continue;
                    if (profiles != null && !profiles.Contains(main.SectionAt(s).Spec.id)) continue;
                    if (median && !main.SectionAt(s).HasMedian) continue;
                    foreach (int side in median ? MedianSide : SidesOf(r.side))
                    {
                        if (Mathx.Hash01(spec.seed + ri * 7919, k, side + 3) > r.presence * dens) continue;
                        main.Spline.FrameAt(s, out var pos, out var fwd, out var right);
                        if (median)
                        {   // on the centre line of a divided road, standing on the median, turned along the road (a double arm then reaches over both carriageways)
                            if (pos.X < minX || pos.X >= maxX || pos.Z < minZ || pos.Z >= maxZ) continue;
                            float headingM = (float)(Math.Atan2(fwd.X, fwd.Z) * 180.0 / Math.PI) + (Mathx.Hash01(spec.seed + ri, k, 13) * 2f - 1f) * r.yawJitterDeg;
                            res.Add(new PropInstance { Prop = r.prop, Origin = r.id, Pos = new V3(pos.X, main.SurfaceY(s, 0f), pos.Z), YawDeg = headingM, Scale = Mathx.Lerp(r.scaleMin, r.scaleMax, Mathx.Hash01(spec.seed + ri, k, 19)) });
                            continue;
                        }
                        float off = main.HalfFormationAt(s) + r.offsetMin + Mathx.Hash01(spec.seed + ri * 104729, k, side + 7) * (r.offsetMax - r.offsetMin);
                        float x = pos.X + right.X * side * off, z = pos.Z + right.Z * side * off;
                        if (x < minX || x >= maxX || z < minZ || z >= maxZ) continue;
                        if (m.Terrain.InsideFormation(x, z, margin)) continue;
                        float towardRoad = (float)(Math.Atan2(-right.X * side, -right.Z * side) * 180.0 / Math.PI);
                        float yaw = (r.faceRoad ? towardRoad : Mathx.Hash01(spec.seed, k, 5) * 360f) + (Mathx.Hash01(spec.seed + ri, k, side + 13) * 2f - 1f) * r.yawJitterDeg;
                        float scale = Mathx.Lerp(r.scaleMin, r.scaleMax, Mathx.Hash01(spec.seed + ri, k, side + 19));
                        res.Add(new PropInstance { Prop = r.prop, Origin = r.id, Pos = new V3(x, m.Terrain.HeightAt(x, z), z), YawDeg = yaw, Scale = scale });
                    }
                }
            }

            foreach (var p in spec.props ?? new PropPlacementSpec[0])
            {
                if (!string.IsNullOrEmpty(p.corridor)) continue;                  // placed from a side road: AddSideProps
                main.Spline.FrameAt(p.s, out var pos, out var fwd, out var right);
                float x = pos.X + right.X * p.lateral, z = pos.Z + right.Z * p.lateral;
                if (x < minX || x >= maxX || z < minZ || z >= maxZ) continue;
                float heading = (float)(Math.Atan2(fwd.X, fwd.Z) * 180.0 / Math.PI);
                float y = Math.Abs(p.lateral) < main.HalfFormationAt(Mathx.Clamp(p.s, 0f, L)) ? main.SurfaceY(Mathx.Clamp(p.s, 0f, L), p.lateral) : m.Terrain.HeightAt(x, z);   // a piece placed on the road itself (a median light) stands on the surface, not on the terrain 0.35 m below it
                res.Add(new PropInstance { Prop = p.prop, Origin = p.id, Text = p.text, Pos = new V3(x, y, z), YawDeg = heading + p.yawDeg, Scale = p.scale });
            }
            AddSideProps(m, minX, minZ, maxX, maxZ, res);
            FrontagePlacer.AddToChunk(m, minX, minZ, maxX, maxZ, density, res);
            return res;
        }

        /// <summary>Hand-placed pieces measured from a side road (PropPlacementSpec.corridor) and the props of every site plate. Independent of the main road's scatter range, so a yard 250 m down a stub is furnished.</summary>
        static void AddSideProps(RouteModel m, float minX, float minZ, float maxX, float maxZ, List<PropInstance> res)
        {
            foreach (var p in m.Spec.props ?? new PropPlacementSpec[0])
            {
                if (string.IsNullOrEmpty(p.corridor) || p.corridor == m.Spec.routeId) continue;
                var c = m.CorridorById(p.corridor); if (c == null) continue;
                float s = Mathx.Clamp(p.s, 0f, c.Length);
                c.Spline.FrameAt(s, out var pos, out var fwd, out var right);
                float x = pos.X + right.X * p.lateral, z = pos.Z + right.Z * p.lateral;
                if (x < minX || x >= maxX || z < minZ || z >= maxZ) continue;
                float heading = (float)(Math.Atan2(fwd.X, fwd.Z) * 180.0 / Math.PI);
                float y = Math.Abs(p.lateral) < c.HalfFormationAt(s) ? c.SurfaceY(s, p.lateral) : m.Terrain.HeightAt(x, z);
                res.Add(new PropInstance { Prop = p.prop, Origin = p.id, Text = p.text, Pos = new V3(x, y, z), YawDeg = heading + p.yawDeg, Scale = p.scale });
            }
            foreach (var site in m.Sites) res.AddRange(site.Props(minX, minZ, maxX, maxZ));
            foreach (var isl in m.Islands) res.AddRange(isl.Props(minX, minZ, maxX, maxZ));       // E5: the props of a roundabout island
        }

        static HashSet<Zone> ParseZones(string z)
        {
            if (string.IsNullOrWhiteSpace(z)) return null;
            var set = new HashSet<Zone>();
            foreach (var part in z.Split(',')) if (RouteVocab.TryZone(part, out var zz)) set.Add(zz);
            return set;
        }

        /// <summary>A comma separated list of road profile ids, or null when the list is empty (= any profile).</summary>
        static HashSet<string> ParseProfiles(string list)
        {
            if (string.IsNullOrWhiteSpace(list)) return null;
            var set = new HashSet<string>();
            foreach (var part in list.Split(',')) { var t = part.Trim(); if (t.Length > 0) set.Add(t); }
            return set.Count == 0 ? null : set;
        }

        static readonly int[] MedianSide = { 0 };
        static IEnumerable<int> SidesOf(string side) => side == "left" ? new[] { -1 } : side == "right" ? new[] { 1 } : new[] { -1, 1 };

        /// <summary>The range of route distances whose centre line lies within `margin` of the box.</summary>
        public static bool SRangeOf(RoadCorridor c, float minX, float minZ, float maxX, float maxZ, float margin, out float lo, out float hi) => SRange(c, minX, minZ, maxX, maxZ, margin, out lo, out hi);

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
