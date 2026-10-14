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

        Material _sky; bool _wasNight; string _source = LightingHour.SourceClock; float _nextLog;
        /// <summary>Where the current hour comes from: url, setting, clock or accelerated.</summary>
        public string HourSource => _source;
        static readonly Gradient SunColour = MakeGradient();

        static Gradient MakeGradient()
        {
            var g = new Gradient();
            g.SetKeys(new[] {
                new GradientColorKey(new Color(1f, 0.45f, 0.18f), 0.00f),   // horizon (sunrise/sunset)
                new GradientColorKey(new Color(1f, 0.78f, 0.55f), 0.18f),
                new GradientColorKey(new Color(1f, 0.96f, 0.88f), 0.45f),   // high sun
                new GradientColorKey(new Color(1f, 0.98f, 0.94f), 1.00f) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            return g;
        }

        void Awake()
        {
            _sky = new Material(Shader.Find("Skybox/Procedural"));
            RenderSettings.skybox = _sky; RenderSettings.sun = sun;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared;
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
            // Equator ~06:00/18:00. Elevation in [-1,1].
            float ang = (hour - 6f) / 12f * Mathf.PI;
            float elev = Mathf.Sin(ang);
            float day = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.08f, 0.25f, elev));
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(Mathf.Asin(Mathf.Clamp(elev, -1f, 1f)) * Mathf.Rad2Deg, -20f, 0f);
                sun.color = SunColour.Evaluate(Mathf.Clamp01(elev * 2f));
                sun.intensity = Mathf.Lerp(0f, 1.35f, day) * Mathf.Lerp(1f, 0.35f, overcast);
                sun.enabled = elev > -0.05f;
            }
            _sky.SetFloat("_Exposure", Mathf.Lerp(0.15f, 1.25f, day) * Mathf.Lerp(1f, 0.6f, overcast));
            _sky.SetFloat("_AtmosphereThickness", Mathf.Lerp(0.9f, 1.4f, 1f - Mathf.Abs(elev)) + overcast * 0.6f);
            _sky.SetColor("_SkyTint", Color.Lerp(new Color(0.5f, 0.5f, 0.5f), new Color(0.45f, 0.5f, 0.6f), 1f - overcast));
            Color skyAmb = Color.Lerp(new Color(0.02f, 0.03f, 0.06f), new Color(0.45f, 0.58f, 0.78f), day);
            RenderSettings.ambientSkyColor = Color.Lerp(skyAmb, skyAmb * 0.7f, overcast);
            RenderSettings.ambientEquatorColor = Color.Lerp(new Color(0.02f, 0.02f, 0.03f), new Color(0.50f, 0.50f, 0.48f), day) * Mathf.Lerp(1f, 0.8f, overcast);
            RenderSettings.ambientGroundColor = Color.Lerp(new Color(0.01f, 0.01f, 0.01f), new Color(0.22f, 0.20f, 0.16f), day);
            // Harmattan-style haze toward the horizon in daytime; thicker fog with overcast/rain.
            Color fog = Color.Lerp(new Color(0.02f, 0.03f, 0.05f), new Color(0.72f, 0.76f, 0.80f), day);
            RenderSettings.fogColor = Color.Lerp(fog, fog * 0.75f, overcast);
            RenderSettings.fogDensity = Mathf.Lerp(0.00035f, 0.0011f, overcast);
            LogState(elev);
        }

        static string F(float v) => v.ToString("0.000", CultureInfo.InvariantCulture);
        static string C(Color c) => "(" + F(c.r) + "," + F(c.g) + "," + F(c.b) + ")";

        /// <summary>One line every 5 s with everything that decides how bright the world is, so a screenshot can be explained by numbers (tests read it).</summary>
        void LogState(float elev)
        {
            if (Time.unscaledTime < _nextLog) return; _nextLog = Time.unscaledTime + 5f;
            Debug.Log($"[Light] hour={F(hour)} source={_source} night={IsNight} elev={F(elev)} sun={(sun != null && sun.enabled ? "on" : "off")} sunIntensity={F(sun != null ? sun.intensity : 0f)} sunColor={C(sun != null ? sun.color : Color.black)} " +
                      $"skyExposure={F(_sky.GetFloat("_Exposure"))} ambientSky={C(RenderSettings.ambientSkyColor)} ambientEquator={C(RenderSettings.ambientEquatorColor)} ambientGround={C(RenderSettings.ambientGroundColor)} " +
                      $"fogDensity={F(RenderSettings.fogDensity)} overcast={F(overcast)}");
        }
    }
}
