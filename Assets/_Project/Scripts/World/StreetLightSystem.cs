using System.Collections.Generic;
using System.Globalization;
using ARO.NetCore;
using UnityEngine;
using UnityEngine.Rendering;

namespace ARO.World
{
    /// <summary>
    /// The night of the world: every lamp of the loaded chunks, lit by the clock the game already has (TimeOfDay) and dressed by the weather the game already has (WeatherSystem, read only).
    /// Four layers, cheapest first (docs/NIGHT_LIGHTING_PLAN.md section 5):
    ///  1. emissive lamp, window, bulb and beacon faces: three shared materials whose colour is set once per frame (no cost per lamp; the geometry is already in the chunk's prop mesh);
    ///  2. ONE additive glow mesh for all loaded chunks: a halo around every lamp head, a pool of light draped on the road and a reflection streak for the wet road (one draw call);
    ///  3. ONE alpha-blended mesh of cosmetic puddles that appear as the road gets wetter (a second draw call, only when it is wet);
    ///  4. a small pool of real spot lights (tier budget) moved onto the lamps nearest the player ahead of them, with a fade at every hand-over.
    /// The decisions (lamp factor, which lamps get real lights, where the geometry goes) are pure code in NetCore with unit tests; this class applies them. Chunks register their lamps from ChunkStreamer.
    /// </summary>
    public sealed class StreetLightSystem : MonoBehaviour
    {
        /// <summary>Shared lit materials for vehicles that are not the player's (ambient traffic). Null until the system has started or when the emissive shader is unavailable.</summary>
        public static Material TrafficHead, TrafficTail;

        static readonly int LevelId = Shader.PropertyToID("_Level"), BeaconId = Shader.PropertyToID("_Beacon"), WeightsId = Shader.PropertyToID("_Weights"), FadeId = Shader.PropertyToID("_Fade"),
            AtlasId = Shader.PropertyToID("_Atlas"), EmissionId = Shader.PropertyToID("_EmissionColor"), BaseColorId = Shader.PropertyToID("_BaseColor"), DayId = Shader.PropertyToID("_Day"),
            WetId = Shader.PropertyToID("_Wet"), SkyId = Shader.PropertyToID("_Sky");

        const float LampLightIntensity = 90f, LampLightAngle = 130f, LampLightInnerAngle = 60f, FadeRate = 4f, RebuildGap = 0.4f, SelectGap = 0.2f;

        sealed class ChunkData { public Vector3 origin; public List<Lamp> lamps; public GlowBuffers glow; public PuddleBuffers puddles; }

        TimeOfDay _tod; WeatherSystem _weather; RouteModel _model; WorldMaterials _mats; QualityTier _tier; Transform _target;
        NightLighting.Mode _mode = NightLighting.Mode.Auto;
        Material _glow, _puddle; Texture2D _atlas; MeshFilter _glowMf, _puddleMf; MeshRenderer _glowMr, _puddleMr; Mesh _glowMesh, _puddleMesh;
        readonly Dictionary<Vector2Int, ChunkData> _chunks = new Dictionary<Vector2Int, ChunkData>();
        readonly List<Lamp> _real = new List<Lamp>(); readonly Dictionary<long, Lamp> _byKey = new Dictionary<long, Lamp>();
        bool _dirty; float _nextRebuild, _nextSelect, _nextLog, _nextSky;
        Light[] _pool; long[] _poolKey; float[] _poolFade; bool[] _poolWanted; LampSelector _selector;
        int _lamps, _halos, _pools, _streaks, _glowVerts, _puddlePatches, _rebuilds;
        float _level, _wet; bool _ready;

        public float Level => _level;
        /// <summary>The vehicle the real lights follow (set when the player's vehicle spawns).</summary>
        public void SetTarget(Transform t) { _target = t; }
        public bool WantsPuddles => _puddle != null && _tier != null && _tier.WetEffects;
        public int LampCount => _lamps;

        public void Init(TimeOfDay tod, WeatherSystem weather, RouteDefinition route, WorldMaterials mats, QualityTier tier, Transform target)
        {
            _tod = tod; _weather = weather; _model = route != null ? route.Model : null; _mats = mats; _tier = tier != null ? tier : QualityTiers.Medium; _target = target;
            string url = Application.absoluteURL; bool smoke = !string.IsNullOrEmpty(url) && url.Contains("smoke=");
            _mode = smoke ? NightLighting.ModeFromUrl(url) : NightLighting.Mode.Auto;      // ?lamps=on|off exists on smoke pages only (CI renders the same night with and without lamps)

            var glowT = Resources.Load<Material>("Lighting/ARO_LampGlow"); var puddleT = Resources.Load<Material>("Lighting/ARO_Puddle"); var emitT = Resources.Load<Material>("Lighting/ARO_LampEmit");
            bool glowOk = glowT != null && glowT.shader != null && glowT.shader.isSupported, puddleOk = puddleT != null && puddleT.shader != null && puddleT.shader.isSupported;
            bool emitOk = emitT != null && emitT.shader != null && emitT.shader.isSupported && _mats != null && _mats.Emissive;
            if (glowOk)
            {
                _glow = new Material(glowT) { name = "ARO_LampGlow_Live" };
                _atlas = BuildAtlas(); _glow.SetTexture(AtlasId, _atlas);
                (_glowMf, _glowMr) = MakeRenderer("LampGlow", _glow); _glowMesh = new Mesh { name = "LampGlow" }; _glowMesh.MarkDynamic(); _glowMf.sharedMesh = _glowMesh;
            }
            if (puddleOk && _tier.WetEffects)
            {
                _puddle = new Material(puddleT) { name = "ARO_Puddle_Live" };
                (_puddleMf, _puddleMr) = MakeRenderer("Puddles", _puddle); _puddleMesh = new Mesh { name = "Puddles" }; _puddleMesh.MarkDynamic(); _puddleMf.sharedMesh = _puddleMesh;
            }
            if (emitOk)
            {
                TrafficHead = new Material(emitT) { name = "ARO_TrafficHead" }; TrafficHead.SetColor(BaseColorId, new Color(0.72f, 0.72f, 0.68f));
                TrafficTail = new Material(emitT) { name = "ARO_TrafficTail" }; TrafficTail.SetColor(BaseColorId, new Color(0.30f, 0.04f, 0.04f));
            }
            int n = Mathf.Max(0, _tier.LampLights);
            _pool = new Light[n]; _poolKey = new long[n]; _poolFade = new float[n]; _poolWanted = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var go = new GameObject("LampLight_" + i); go.transform.SetParent(transform, false); go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                var l = go.AddComponent<Light>(); l.type = LightType.Spot; l.spotAngle = LampLightAngle; l.innerSpotAngle = LampLightInnerAngle; l.shadows = LightShadows.None; l.enabled = false; l.intensity = 0f; _pool[i] = l; _poolKey[i] = long.MinValue;
            }
            _selector = new LampSelector(n, Mathf.Max(60f, _tier.LampGlowRange * 0.25f));
            _ready = true;
            Debug.Log($"[Lamp] init glow={(glowOk ? "ok" : "UNAVAILABLE")} puddles={(_puddle != null ? "ok" : puddleOk ? "off(tier)" : "UNAVAILABLE")} emissive={(emitOk ? "ok" : "UNAVAILABLE(plain materials)")} realLights={n} glowRange={_tier.LampGlowRange:0} wetEffects={_tier.WetEffects} mode={_mode} " +
                      $"additionalLights={(UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset != null ? UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset.additionalLightsRenderingMode.ToString() : "n/a")} perObjectLimit={(UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset != null ? UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset.maxAdditionalLightsCount : 0)}");
        }

        (MeshFilter, MeshRenderer) MakeRenderer(string name, Material m)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(transform, false);
            var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off; mr.enabled = false;
            return (go.GetComponent<MeshFilter>(), mr);
        }

        static Texture2D BuildAtlas()
        {
            var bytes = LampGlowTexture.Build(); var px = new Color32[bytes.Length];
            for (int i = 0; i < bytes.Length; i++) px[i] = new Color32(bytes[i], bytes[i], bytes[i], 255);
            var t = new Texture2D(LampGlowTexture.Width, LampGlowTexture.Height, TextureFormat.RGBA32, false) { name = "LampGlowAtlas", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            t.SetPixels32(px); t.Apply(false, true); return t;
        }

        float GroundY(float x, float z) => LampField.SurfaceHeight(_model, x, z);

        /// <summary>A chunk's lamps (and the cosmetic puddles of its stretch of road). Called once per chunk by ChunkStreamer; the geometry is built here, once, against the real road surface.</summary>
        public void RegisterChunk(Vector2Int id, Vector3 origin, List<Lamp> lamps, List<Puddle> puddles)
        {
            if (!_ready || _model == null) return;
            var d = new ChunkData { origin = new Vector3(origin.x, 0f, origin.z), lamps = lamps }; var o = new V3(origin.x, 0f, origin.z);
            if (lamps != null && lamps.Count > 0) d.glow = LampGlowGeometry.Build(lamps, o, GroundY, _tier.WetEffects);
            if (WantsPuddles && puddles != null && puddles.Count > 0) d.puddles = PuddleGeometry.Build(puddles, o, GroundY);
            _chunks[id] = d; _dirty = true;
        }

        public void UnregisterChunk(Vector2Int id) { if (_chunks.Remove(id)) _dirty = true; }

        // ------------------------------------------------------------------------------------------------------------------ per frame
        void Update()
        {
            if (!_ready) return;
            float clock = _tod != null ? _tod.LampFactor : 0f;
            _level = NightLighting.Apply(_mode, clock);
            _wet = _weather != null ? _weather.Wetness : 0f;
            ApplyUniforms();
            if (_dirty && Time.unscaledTime >= _nextRebuild) { _nextRebuild = Time.unscaledTime + RebuildGap; _dirty = false; Rebuild(); }
            if (_pool.Length > 0 && _target != null && Time.unscaledTime >= _nextSelect) { _nextSelect = Time.unscaledTime + SelectGap; Select(); }
            FadeLights();
            if (Time.unscaledTime >= _nextLog) { _nextLog = Time.unscaledTime + 5f; LogState(clock); }
        }

        void ApplyUniforms()
        {
            float day = _tod != null ? Mathf.Clamp01(_tod.State.Day) : 1f, off = Mathf.Lerp(0.14f, 1f, day), beacon = NightLighting.BeaconLevel(Time.time);
            bool lit = _level > 0.005f;
            if (_glow != null)
            {
                _glow.SetFloat(LevelId, _level); _glow.SetFloat(BeaconId, beacon);
                _glow.SetVector(WeightsId, new Vector4(1f, WeatherLook.PoolBoost(_wet), _tier.WetEffects ? WeatherLook.StreakStrength(_wet) : 0f, 0f));
                float r = Mathf.Max(60f, _tier.LampGlowRange); _glow.SetVector(FadeId, new Vector4(r * 0.6f, r, 0f, 0f));
                if (_glowMr != null) _glowMr.enabled = lit && _glowMesh.vertexCount > 0;
            }
            if (_puddle != null)
            {
                _puddle.SetFloat(WetId, _wet);
                if (Time.unscaledTime >= _nextSky) { _nextSky = Time.unscaledTime + 0.5f; var a = RenderSettings.ambientSkyColor; _puddle.SetColor(SkyId, new Color(a.r * 1.5f, a.g * 1.6f, a.b * 1.9f, 1f)); }
                if (_puddleMr != null) _puddleMr.enabled = _wet > 0.12f && _puddleMesh.vertexCount > 0;
            }
            if (_mats != null && _mats.Emissive)
            {
                Emit(_mats.Prop[PropSub.Lamp], new Color(1.00f, 0.80f, 0.48f) * (1.7f * _level), off);
                Emit(_mats.Prop[PropSub.Glow], new Color(1.00f, 0.80f, 0.50f) * (1.0f * _level), off);
                Emit(_mats.Prop[PropSub.Beacon], new Color(1.00f, 0.10f, 0.06f) * (2.2f * _level * beacon), off);
            }
            Emit(TrafficHead, new Color(1.0f, 0.93f, 0.75f) * (2.2f * _level), off);
            Emit(TrafficTail, new Color(1.0f, 0.06f, 0.04f) * (1.5f * _level), off);
        }

        static void Emit(Material m, Color c, float day)
        {
            if (m == null) return;
            m.SetColor(EmissionId, c); m.SetFloat(DayId, day);
        }

        void Rebuild()
        {
            _rebuilds++;
            if (_chunks.Count == 0) { if (_glowMesh != null) _glowMesh.Clear(); if (_puddleMesh != null) _puddleMesh.Clear(); _real.Clear(); _byKey.Clear(); _lamps = _halos = _pools = _streaks = _glowVerts = _puddlePatches = 0; return; }
            Vector3 c = Vector3.zero; foreach (var d in _chunks.Values) { c = d.origin; break; }
            _real.Clear(); _byKey.Clear(); _lamps = _halos = _pools = _streaks = _puddlePatches = 0;
            var verts = new List<Vector3>(); var uv0 = new List<Vector2>(); var uv1 = new List<Vector4>(); var cols = new List<Color>(); var tris = new List<int>();
            var pv = new List<Vector3>(); var pu0 = new List<Vector2>(); var pu1 = new List<Vector2>(); var pt = new List<int>();
            foreach (var d in _chunks.Values)
            {
                var off = d.origin - c;
                if (d.lamps != null) { _lamps += d.lamps.Count; foreach (var l in d.lamps) if (l.Real && !_byKey.ContainsKey(l.Key)) { _real.Add(l); _byKey[l.Key] = l; } }
                var g = d.glow;
                if (g != null)
                {
                    int b = verts.Count; _halos += g.Halos; _pools += g.Pools; _streaks += g.Streaks;
                    for (int i = 0; i < g.Vertices.Count; i++)
                    {
                        var v = g.Vertices[i]; verts.Add(new Vector3(v.X + off.x, v.Y + off.y, v.Z + off.z)); uv0.Add(new Vector2(g.Corner[i].X, g.Corner[i].Y));
                        var q = g.Data[i]; uv1.Add(new Vector4(q[0], q[1], q[2], q[3])); var k = g.Colour[i]; cols.Add(new Color(k.R, k.G, k.B, 1f));
                    }
                    for (int i = 0; i < g.Triangles.Count; i++) tris.Add(b + g.Triangles[i]);
                }
                var p = d.puddles;
                if (p != null)
                {
                    int b = pv.Count; _puddlePatches += p.Patches;
                    for (int i = 0; i < p.Vertices.Count; i++) { var v = p.Vertices[i]; pv.Add(new Vector3(v.X + off.x, v.Y + off.y, v.Z + off.z)); pu0.Add(new Vector2(p.Corner[i].X, p.Corner[i].Y)); pu1.Add(new Vector2(p.Threshold[i], 0f)); }
                    for (int i = 0; i < p.Triangles.Count; i++) pt.Add(b + p.Triangles[i]);
                }
            }
            _glowVerts = verts.Count;
            if (_glowMesh != null) Fill(_glowMesh, verts, uv0, uv1, cols, tris); if (_glowMf != null) _glowMf.transform.position = c;
            if (_puddleMesh != null) { FillPuddle(_puddleMesh, pv, pu0, pu1, pt); _puddleMf.transform.position = c; }
        }

        static void Fill(Mesh m, List<Vector3> v, List<Vector2> uv0, List<Vector4> uv1, List<Color> col, List<int> tri)
        {
            m.Clear(); m.indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.SetVertices(v); m.SetUVs(0, uv0); m.SetUVs(1, uv1); m.SetColors(col); m.SetTriangles(tri, 0, true); m.RecalculateBounds();
        }

        static void FillPuddle(Mesh m, List<Vector3> v, List<Vector2> uv0, List<Vector2> uv1, List<int> tri)
        {
            m.Clear(); m.indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            m.SetVertices(v); m.SetUVs(0, uv0); m.SetUVs(1, uv1); m.SetTriangles(tri, 0, true); m.RecalculateBounds();
        }

        // ------------------------------------------------------------------------------------------------------------------ real lights
        void Select()
        {
            var fwd = _target.forward; var p = _target.position;
            var sel = _selector.Update(_real, new V3(p.x, p.y, p.z), fwd.x, fwd.z);
            for (int i = 0; i < _pool.Length; i++) _poolWanted[i] = false;
            // keep the slots whose lamp is still selected
            for (int s = 0; s < sel.Count; s++)
                for (int i = 0; i < _pool.Length; i++) if (_poolKey[i] == sel[s]) { _poolWanted[i] = true; break; }
            // give a free slot (faded out) to every newly selected lamp
            for (int s = 0; s < sel.Count; s++)
            {
                long key = sel[s]; bool has = false; for (int i = 0; i < _pool.Length; i++) if (_poolKey[i] == key) { has = true; break; }
                if (has || !_byKey.TryGetValue(key, out var lamp)) continue;
                for (int i = 0; i < _pool.Length; i++)
                    if (!_poolWanted[i] && _poolFade[i] <= 0.001f)
                    {
                        _poolKey[i] = key; _poolWanted[i] = true; var l = _pool[i]; l.transform.position = new Vector3(lamp.Pos.X, lamp.Pos.Y - 0.3f, lamp.Pos.Z);
                        l.color = new Color(lamp.R, lamp.G, lamp.B); l.range = Mathf.Max(8f, lamp.Range); break;
                    }
            }
        }

        void FadeLights()
        {
            int on = 0;
            for (int i = 0; i < _pool.Length; i++)
            {
                _poolFade[i] = Mathf.MoveTowards(_poolFade[i], _poolWanted[i] && _level > 0.005f ? 1f : 0f, FadeRate * Time.deltaTime);
                var l = _pool[i];
                if (_poolFade[i] <= 0.001f) { l.enabled = false; if (!_poolWanted[i]) _poolKey[i] = long.MinValue; continue; }
                float k = 1f; if (_byKey.TryGetValue(_poolKey[i], out var lamp)) k = lamp.Intensity;
                l.intensity = LampLightIntensity * k * _level * _poolFade[i]; l.enabled = true; on++;
            }
            _activeLights = on;
        }
        int _activeLights;

        void LogState(float clock)
        {
            var ci = CultureInfo.InvariantCulture;
            Debug.Log($"[Lamp] level={_level.ToString("0.000", ci)} clock={clock.ToString("0.000", ci)} mode={_mode} chunks={_chunks.Count} lamps={_lamps} halos={_halos} pools={_pools} streaks={_streaks} glowVerts={_glowVerts} puddles={_puddlePatches} " +
                      $"wet={_wet.ToString("0.00", ci)} realLights={_activeLights}/{_pool.Length} glowDraw={(_glowMr != null && _glowMr.enabled ? "on" : "off")} puddleDraw={(_puddleMr != null && _puddleMr.enabled ? "on" : "off")} rebuilds={_rebuilds}");
        }

        void OnDestroy()
        {
            if (_glow != null) Destroy(_glow); if (_puddle != null) Destroy(_puddle); if (_atlas != null) Destroy(_atlas);
            if (_glowMesh != null) Destroy(_glowMesh); if (_puddleMesh != null) Destroy(_puddleMesh);
            if (TrafficHead != null) Destroy(TrafficHead); if (TrafficTail != null) Destroy(TrafficTail); TrafficHead = TrafficTail = null;
        }
    }
}
