using ARO.NetCore;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ARO.World
{
    /// <summary>
    /// The rendering foundation: picks a quality tier (explicit ?quality=low|medium|high, otherwise from the device), and applies it to the URP
    /// asset, the camera, the sun, culling and post-processing. Every effect has a cost lever in QualityTier; nothing is enabled that the tier
    /// does not allow. Time of day and weather stay in TimeOfDay / WeatherSystem; this class exposes hooks to them and reacts to the clock.
    /// </summary>
    public sealed class RenderingRig : MonoBehaviour
    {
        public static QualityTier Current { get; private set; } = QualityTiers.Medium;
        public QualityTier Tier { get; private set; } = QualityTiers.Medium;
        public TimeOfDay timeOfDay; public WeatherSystem weather;

        Volume _volume; VolumeProfile _profile; ColorAdjustments _grade; Vignette _vignette; Bloom _bloom; Tonemapping _tonemap;
        Camera _cam; Light _sun; float _nextGrade;
        public bool PostProcessingActive { get; private set; }
        public string PostProcessingNote { get; private set; } = "not applied";

        /// <summary>?quality=low|medium|high in the page URL wins; otherwise the device decides.</summary>
        public static QualityTier Select(string url, bool mobile, int systemMemoryMb, int graphicsMemoryMb)
        {
            string requested = null;
            if (!string.IsNullOrEmpty(url))
            {
                int i = url.IndexOf("quality=", System.StringComparison.OrdinalIgnoreCase);
                if (i >= 0) { string v = url.Substring(i + 8); int end = v.IndexOfAny(new[] { '&', '#' }); requested = end >= 0 ? v.Substring(0, end) : v; }
            }
            return QualityTiers.Detect(new DeviceInfo { IsMobile = mobile, SystemMemoryMb = systemMemoryMb, GraphicsMemoryMb = graphicsMemoryMb, Requested = requested });
        }

        public void Init(Camera cam, Light sun)
        {
            _cam = cam; _sun = sun;
            Tier = Select(Application.absoluteURL, Application.isMobilePlatform, SystemInfo.systemMemorySize, SystemInfo.graphicsMemorySize);
            Current = Tier;
            Apply();
            Debug.Log($"[Render] tier={Tier.Name} (url='{Application.absoluteURL}') shadows={Tier.ShadowDistance:0}m x{Tier.ShadowCascades} msaa={Tier.MsaaSamples} renderScale={Tier.RenderScale:0.0} loadRadius={Tier.LoadRadiusChunks} " +
                      $"post={(PostProcessingActive ? "on" : "off")} ({PostProcessingNote}) pipeline={(UniversalRenderPipeline.asset != null ? UniversalRenderPipeline.asset.name : "NONE")}");
        }

        void Apply()
        {
            QualitySettings.lodBias = Tier.LodBias;
            var urp = UniversalRenderPipeline.asset;
            if (urp != null)
            {
                urp.shadowDistance = Tier.ShadowDistance; urp.shadowCascadeCount = Tier.ShadowCascades;
                urp.msaaSampleCount = Tier.MsaaSamples; urp.renderScale = Tier.RenderScale;
            }
            if (_cam != null) _cam.farClipPlane = Tier.FarClip;
            if (_sun != null) { _sun.shadows = Tier.ShadowCascades > 0 ? (Tier.SoftShadows ? LightShadows.Soft : LightShadows.Hard) : LightShadows.None; _sun.shadowStrength = 0.92f; _sun.shadowBias = 0.05f; _sun.shadowNormalBias = 0.4f; }
            try { BuildPost(); } catch (System.Exception e) { PostProcessingActive = false; PostProcessingNote = "failed: " + e.Message; Debug.LogWarning("[Render] post-processing unavailable: " + e.Message); }
        }

        void BuildPost()
        {
            if (_cam == null) return;
            _cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var go = new GameObject("PostProcessing"); go.transform.SetParent(transform, false);
            _volume = go.AddComponent<Volume>(); _volume.isGlobal = true; _volume.priority = 10f;
            _profile = ScriptableObject.CreateInstance<VolumeProfile>(); _volume.sharedProfile = _profile;
            if (Tier.Tonemapping) { _tonemap = _profile.Add<Tonemapping>(true); _tonemap.mode.Override(TonemappingMode.ACES); }
            if (Tier.ColorGrading) { _grade = _profile.Add<ColorAdjustments>(true); _grade.postExposure.Override(0.1f); _grade.contrast.Override(10f); _grade.saturation.Override(8f); }
            if (Tier.Vignette) { _vignette = _profile.Add<Vignette>(true); _vignette.intensity.Override(0.22f); _vignette.smoothness.Override(0.5f); }
            if (Tier.Bloom) { _bloom = _profile.Add<Bloom>(true); _bloom.threshold.Override(1.1f); _bloom.intensity.Override(0.25f); _bloom.scatter.Override(0.6f); }
            PostProcessingActive = true; PostProcessingNote = $"tonemap={Tier.Tonemapping} grade={Tier.ColorGrading} vignette={Tier.Vignette} bloom={Tier.Bloom}";
        }

        void Update()
        {
            // Hook: grade follows the clock (cheaper than anything per-pixel): darker, cooler exposure at night, warmer at the horizon.
            if (_grade == null || timeOfDay == null || Time.unscaledTime < _nextGrade) return;
            _nextGrade = Time.unscaledTime + 1f;
            float h = timeOfDay.hour, golden = Mathf.Max(Mathf.InverseLerp(1.6f, 0f, Mathf.Abs(h - 6.3f)), Mathf.InverseLerp(1.6f, 0f, Mathf.Abs(h - 17.7f)));
            _grade.postExposure.value = timeOfDay.IsNight ? -0.35f : 0.1f + golden * 0.15f;
            _grade.colorFilter.Override(Color.Lerp(Color.white, new Color(1f, 0.93f, 0.82f), golden));
        }

        void OnDestroy() { if (_profile != null) Destroy(_profile); }
    }
}
