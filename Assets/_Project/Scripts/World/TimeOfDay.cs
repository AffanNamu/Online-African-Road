using System;
using System.Globalization;
using ARO.NetCore;
using UnityEngine;

namespace ARO.World
{
    /// <summary>
    /// Sun, sky, ambient and fog from a clock. Defaults to live West Africa Time (UTC+1) so the world matches
    /// the player's real Lagos day; can run accelerated for testing. Drives a procedural sky (no extra textures).
    /// </summary>
    public class TimeOfDay : MonoBehaviour
    {
        public Light sun;
        [Range(0, 24)] public float hour = 9f;
        public bool followWestAfricaTime = true;
        public float timeScale = 1f;              // 1 = real time when not following the clock; e.g. 60 = 1 min/s
        public float overcast;                    // 0..1, set by WeatherSystem
        public bool IsNight => hour < 5.6f || hour > 18.4f;
        public event Action<bool> NightChanged;

        Material _sky, _gradient; SkyGradientState _lastGradient; bool _wasNight; string _source = LightingHour.SourceClock; float _nextLog;
        /// <summary>Where the current hour comes from: url, setting, clock or accelerated.</summary>
        public string HourSource => _source;
        /// <summary>The lighting for the current hour and weather (sun, moon, sky, ambient, fog, grade). Read by the rendering rig for the post-processing grade.</summary>
        public LightingState State { get; private set; } = LightingModel.Evaluate(12f, 0f);
        Light _moon;

        void Awake()
        {
            _sky = new Material(Shader.Find("Skybox/Procedural"));
            RenderSettings.skybox = _sky; RenderSettings.sun = sun;
            if (ARO.Core.RuntimeSettings.TestSkyGradient) UseGradientSky();
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
            // The moon: a second, shadowless directional light that only lights the world while the sun is down, so a night road keeps its shape.
            var moonGo = new GameObject("Moon"); moonGo.transform.SetParent(transform, false);
            moonGo.transform.rotation = Quaternion.Euler(LightingModel.MoonElevationDeg, LightingModel.MoonAzimuthDeg, 0f);
            _moon = moonGo.AddComponent<Light>(); _moon.type = LightType.Directional; _moon.shadows = LightShadows.None; _moon.enabled = false; _moon.intensity = 0f;
        }

        /// <summary>
        /// Switches the skybox to the custom gradient (Resources/Sky/ARO_SkyGradient, made by SceneBuilder). Only on request (?smoke= pages with sky=gradient); if the material or its shader is
        /// missing or unsupported, the procedural sky stays and the log says so. Ambient light, fog and the sun are untouched either way.
        /// </summary>
        void UseGradientSky()
        {
            var template = Resources.Load<Material>("Sky/ARO_SkyGradient");
            if (template == null || template.shader == null || !template.shader.isSupported) { Debug.LogWarning($"[Light] gradient sky requested but unavailable (material found={template != null}, shader supported={(template != null && template.shader != null && template.shader.isSupported)}); keeping the procedural sky"); return; }
            _gradient = new Material(template); RenderSettings.skybox = _gradient;
            Debug.Log("[Light] sky=gradient (ARO/SkyGradient)");
        }

        void ApplyGradient(LightingState st)
        {
            var tune = ARO.Core.RuntimeSettings.TestSkyPalette == 3 ? SkyTune.None : SkyTune.Default; if (ARO.Core.RuntimeSettings.TestSkyDim >= 0f) tune.BandDim = ARO.Core.RuntimeSettings.TestSkyDim; if (ARO.Core.RuntimeSettings.TestSkyDip >= 0f) tune.Dip = ARO.Core.RuntimeSettings.TestSkyDip;   // horizon study overrides (smoke pages only)
            var g = SkyGradientModel.Evaluate(st, ARO.Core.RuntimeSettings.TestSkyPalette, ARO.Core.RuntimeSettings.TestSkyHaze >= 0f ? ARO.Core.RuntimeSettings.TestSkyHaze : SkyGradientModel.DefaultHazeMix, tune);
            _gradient.SetFloat("_Dip", g.Dip); _gradient.SetFloat("_DipHeight", g.DipHeight);
            _gradient.SetColor("_Zenith", new Color(g.Zenith.R, g.Zenith.G, g.Zenith.B)); _gradient.SetColor("_Mid", new Color(g.Mid.R, g.Mid.G, g.Mid.B));
            _gradient.SetColor("_Horizon", new Color(g.Horizon.R, g.Horizon.G, g.Horizon.B)); _gradient.SetColor("_Haze", new Color(g.Haze.R, g.Haze.G, g.Haze.B)); _gradient.SetFloat("_LowHeight", g.LowHeight); _gradient.SetColor("_Ground", new Color(g.Ground.R, g.Ground.G, g.Ground.B));
            _gradient.SetColor("_GlowColor", new Color(g.Glow.R, g.Glow.G, g.Glow.B));
            float glowK = ARO.Core.RuntimeSettings.TestSkyGlow >= 0f ? ARO.Core.RuntimeSettings.TestSkyGlow : 1f;   // sky study overrides (smoke pages only)
            _gradient.SetFloat("_MidHeight", ARO.Core.RuntimeSettings.TestSkyMid >= 0f ? ARO.Core.RuntimeSettings.TestSkyMid : g.MidHeight); _gradient.SetFloat("_HeightExp", ARO.Core.RuntimeSettings.TestSkyExp >= 0f ? ARO.Core.RuntimeSettings.TestSkyExp : g.HeightExp);
            _gradient.SetFloat("_GlowStrength", g.GlowStrength * glowK); _gradient.SetFloat("_GlowPower", g.GlowPower); _gradient.SetFloat("_DiscStrength", g.DiscStrength);
            var toSun = sun != null ? -sun.transform.forward : Vector3.up;                // a directional light shines along its forward axis; the sun is in the opposite direction
            _lastGradient = g;
            _gradient.SetVector("_SunDir", new Vector4(toSun.x, toSun.y, toSun.z, 0f));
        }

        /// <summary>While true the world's sun and ambient are left alone (the Garage showroom lights its own room and restores everything when it closes).</summary>
        public static bool Suspended;

        void Update()
        {
            if (Suspended) return;
            float setting = ARO.Core.RuntimeSettings.FixedHour;       // the player's Time of Day setting: a fixed hour, or -1 for the real clock
            float urlHour = ARO.Core.RuntimeSettings.TestHour;        // ?hour= in the page address (tests, reviews): beats the setting
            if (urlHour >= 0f || setting >= 0f || followWestAfricaTime) hour = LightingHour.Resolve(urlHour, setting, LightingHour.ClockHour(DateTime.UtcNow), out _source);
            else { hour = Mathf.Repeat(hour + Time.deltaTime * timeScale / 3600f, 24f); _source = "accelerated"; }
            Apply();
            if (IsNight != _wasNight) { _wasNight = IsNight; NightChanged?.Invoke(IsNight); }
        }

        void Apply()
        {
            var st = State = LightingModel.Evaluate(hour, overcast);
            if (ARO.Core.RuntimeSettings.TestAtmosphere >= 0f) st.Atmosphere = ARO.Core.RuntimeSettings.TestAtmosphere;   // sky study only (?smoke= pages)
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(st.SunElevationDeg, -20f, 0f);
                sun.color = new Color(st.SunColor.R, st.SunColor.G, st.SunColor.B);
                sun.intensity = st.SunIntensity;
                sun.enabled = st.SunEnabled;
            }
            if (_moon != null) { _moon.enabled = st.MoonEnabled; _moon.intensity = st.MoonIntensity; _moon.color = new Color(st.MoonColor.R, st.MoonColor.G, st.MoonColor.B); }
            if (_gradient != null) ApplyGradient(st);
            _sky.SetFloat("_Exposure", st.SkyExposure);
            _sky.SetFloat("_AtmosphereThickness", st.Atmosphere);
            _sky.SetColor("_SkyTint", new Color(st.SkyTint.R, st.SkyTint.G, st.SkyTint.B));
            RenderSettings.ambientSkyColor = new Color(st.AmbientSky.R, st.AmbientSky.G, st.AmbientSky.B);
            RenderSettings.ambientEquatorColor = new Color(st.AmbientEquator.R, st.AmbientEquator.G, st.AmbientEquator.B);
            RenderSettings.ambientGroundColor = new Color(st.AmbientGround.R, st.AmbientGround.G, st.AmbientGround.B);
            RenderSettings.fogColor = new Color(st.FogColor.R, st.FogColor.G, st.FogColor.B);
            RenderSettings.fogDensity = st.FogDensity;
            if (_gradient != null && _lastGradient != null && ARO.Core.RuntimeSettings.TestSkyFog > 0f)       // sky study only (?skyfog=): the distant terrain fades into the sky's haze instead of ending on a hard edge
            { RenderSettings.fogColor = new Color(_lastGradient.Haze.R, _lastGradient.Haze.G, _lastGradient.Haze.B); RenderSettings.fogDensity = st.FogDensity * ARO.Core.RuntimeSettings.TestSkyFog; }
            if (_gradient != null && _lastGradient != null && ARO.Core.RuntimeSettings.TestSkyFogD > 0f)       // horizon study only (?skyfogd=): an absolute density, so the haze can be strong enough to reach the terrain 50 to 150 m away
            { RenderSettings.fogColor = new Color(_lastGradient.Haze.R, _lastGradient.Haze.G, _lastGradient.Haze.B); RenderSettings.fogDensity = ARO.Core.RuntimeSettings.TestSkyFogD; }
            LogState(st.Elevation);
        }

        static string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);
        static string C(Color c) => "(" + F(c.r) + "," + F(c.g) + "," + F(c.b) + ")";

        /// <summary>One line every 5 s with everything that decides how bright the world is, so a screenshot can be explained by numbers (tests read it).</summary>
        void LogState(float elev)
        {
            if (Time.unscaledTime < _nextLog) return; _nextLog = Time.unscaledTime + 5f;
            Debug.Log($"[Light] hour={F(hour)} source={_source} night={IsNight} elev={F(elev)} sun={(sun != null && sun.enabled ? "on" : "off")} sunIntensity={F(sun != null ? sun.intensity : 0f)} sunColor={C(sun != null ? sun.color : Color.black)} " +
                      $"sky={(_gradient != null ? "gradient" : "procedural")} skyExposure={F(_sky.GetFloat("_Exposure"))} ambientSky={C(RenderSettings.ambientSkyColor)} ambientEquator={C(RenderSettings.ambientEquatorColor)} ambientGround={C(RenderSettings.ambientGroundColor)} " +
                      $"fogDensity={F(RenderSettings.fogDensity)} overcast={F(overcast)} moon={(_moon != null && _moon.enabled ? "on" : "off")} moonIntensity={F(_moon != null ? _moon.intensity : 0f)} golden={F(State.Golden)} postExposure={F(State.PostExposure)} atmosphere={F(_sky.GetFloat("_AtmosphereThickness"))} skyTint={C(_sky.GetColor("_SkyTint"))} fogColor={C(RenderSettings.fogColor)} fogDensityExact={RenderSettings.fogDensity.ToString("0.00000", CultureInfo.InvariantCulture)}");
        }
    }
}
