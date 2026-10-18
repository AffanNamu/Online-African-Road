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
        Camera _cam; Light _sun; float _nextGrade, _loggedExposure = 99f; bool _postChecked, _noShadowsTest;
        public bool PostProcessingActive { get; private set; }
        public string PostProcessingNote { get; private set; } = "not applied";

        /// <summary>?quality=low|medium|high in the page URL wins; otherwise the device decides.</summary>
        public static QualityTier Select(string url, bool mobile, int systemMemoryMb, int graphicsMemoryMb, string preference = null)
        {
            string requested = null;
            if (!string.IsNullOrEmpty(url))
            {
                int i = url.IndexOf("quality=", System.StringComparison.OrdinalIgnoreCase);
                if (i >= 0) { string v = url.Substring(i + 8); int end = v.IndexOfAny(new[] { '&', '#' }); requested = end >= 0 ? v.Substring(0, end) : v; }
            }
            if (requested == null && !string.IsNullOrEmpty(preference) && preference != "auto") requested = preference;   // the player's Graphics Quality setting, unless the URL asked for one
            return QualityTiers.Detect(new DeviceInfo { IsMobile = mobile, SystemMemoryMb = systemMemoryMb, GraphicsMemoryMb = graphicsMemoryMb, Requested = requested });
        }

        /// <summary>
        /// Which tonemapper: "neutral" (the default), "aces" or "none", from ?tonemap= in the page address. Colours in this project are in the Gamma colour space, so the tonemapper sees
        /// display-referred values. ACES (a filmic curve for linear light) crushed the dark range there: measured, night frames went from 39 to 19 mean luma with 59% crushed blacks and
        /// the golden-hour terrain got darker. Neutral keeps the dark and middle range and only rolls off the highlights, which is what stops an orange body clipping to yellow.
        /// </summary>
        public static string TonemapChoice(string url)
        {
            if (string.IsNullOrEmpty(url)) return "neutral";
            int i = url.IndexOf("tonemap=", System.StringComparison.OrdinalIgnoreCase); if (i < 0) return "neutral";
            string v = url.Substring(i + 8); int end = v.IndexOfAny(new[] { '&', '#' }); if (end >= 0) v = v.Substring(0, end);
            v = v.ToLowerInvariant(); return v == "aces" || v == "none" ? v : "neutral";
        }

        public void Init(Camera cam, Light sun)
        {
            _cam = cam; _sun = sun;
            Tier = SettingsEffect.WithTraffic(Select(Application.absoluteURL, Application.isMobilePlatform, SystemInfo.systemMemorySize, SystemInfo.graphicsMemorySize, ARO.Core.RuntimeSettings.QualityPreference), ARO.Core.RuntimeSettings.TrafficChoice);
            Current = Tier;
            ARO.Core.RuntimeSettings.TierName = SettingsEffect.QualityName(Tier); ARO.Core.RuntimeSettings.AntiAliasing = SettingsEffect.AntiAliasing(Tier); ARO.Core.RuntimeSettings.ViewDistance = SettingsEffect.ViewDistance(Tier); ARO.Core.RuntimeSettings.TierSummary = SettingsEffect.TierSummary(Tier);
            Apply();
            Debug.Log($"[Render] tier={Tier.Name} (url='{Application.absoluteURL}') shadows={Tier.ShadowDistance:0}m x{Tier.ShadowCascades} msaa={Tier.MsaaSamples} renderScale={Tier.RenderScale:0.0} loadRadius={Tier.LoadRadiusChunks} " +
                      $"post={(PostProcessingActive ? "on" : "off")} ({PostProcessingNote}) pipeline={(UniversalRenderPipeline.asset != null ? UniversalRenderPipeline.asset.name : "NONE")} " +
                      $"colorSpace={QualitySettings.activeColorSpace} hdr={(UniversalRenderPipeline.asset != null && UniversalRenderPipeline.asset.supportsHDR)} cameraHdr={(_cam != null && _cam.allowHDR)} " +
                      $"additionalLights={(UniversalRenderPipeline.asset != null ? UniversalRenderPipeline.asset.additionalLightsRenderingMode.ToString() : "n/a")} maxAdditionalLights={(UniversalRenderPipeline.asset != null ? UniversalRenderPipeline.asset.maxAdditionalLightsCount : 0)} " +
                      $"graphicsApi={SystemInfo.graphicsDeviceType} rendererPostProcessData={PostProcessDataState()}");
        }

        /// <summary>Whether the URP renderer has post-process data. Without it the Volume is never drawn (see SceneBuilder.AssignPostProcessData). Read by reflection: "present", "NULL" or why it could not be read.</summary>
        static string PostProcessDataState()
        {
            try
            {
                var a = UniversalRenderPipeline.asset; if (a == null) return "no-pipeline";
                var list = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(a) as ScriptableRendererData[];
                if (list == null || list.Length == 0 || list[0] == null) return "renderer-data-unreadable";
                var t = list[0].GetType(); var fi = t.GetField("postProcessData"); var pi = t.GetProperty("postProcessData");
                if (fi == null && pi == null) return "member-not-found";
                var v = fi != null ? fi.GetValue(list[0]) : pi.GetValue(list[0]);
                return v == null ? "NULL" : "present";
            }
            catch (System.Exception e) { return "probe-failed:" + e.GetType().Name; }
        }

        /// <summary>What the Volume stack resolves for tonemapping (proves the Volume is seen by the camera, not that URP draws it).</summary>
        string VolumeTonemapState()
        {
            try { var tm = VolumeManager.instance.stack.GetComponent<Tonemapping>(); return tm == null ? "none" : (tm.active ? tm.mode.value.ToString() : "inactive"); }
            catch (System.Exception e) { return "probe-failed:" + e.GetType().Name; }
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
            _noShadowsTest = !string.IsNullOrEmpty(Application.absoluteURL) && Application.absoluteURL.Contains("smoke=") && Application.absoluteURL.Contains("shadows=off");   // test-only: isolates the shadow contribution in the Medium study
            if (_sun != null) { _sun.shadows = Tier.ShadowCascades > 0 && !_noShadowsTest ? (Tier.SoftShadows ? LightShadows.Soft : LightShadows.Hard) : LightShadows.None; _sun.shadowStrength = 0.92f; _sun.shadowBias = 0.05f; _sun.shadowNormalBias = 0.4f; }
            try { BuildPost(); } catch (System.Exception e) { PostProcessingActive = false; PostProcessingNote = "failed: " + e.Message; Debug.LogWarning("[Render] post-processing unavailable: " + e.Message); }
        }

        void BuildPost()
        {
            if (_cam == null) return;
            _cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var go = new GameObject("PostProcessing"); go.transform.SetParent(transform, false);
            _volume = go.AddComponent<Volume>(); _volume.isGlobal = true; _volume.priority = 10f;
            _profile = ScriptableObject.CreateInstance<VolumeProfile>(); _volume.sharedProfile = _profile;
            string tm = TonemapChoice(Application.absoluteURL);
            if (Tier.Tonemapping && tm != "none") { _tonemap = _profile.Add<Tonemapping>(true); _tonemap.mode.Override(tm == "aces" ? TonemappingMode.ACES : TonemappingMode.Neutral); }
            // Saturation 0 (was +8): the orange terrain and truck were already vivid, and the grade is now actually drawn.
            // Test-only overrides, honoured on ?smoke= pages only: ?contrast=0..100 and ?vignette=0..1 let CI render the Medium tier with the grade contrast or the vignette changed, so that what makes
            // Medium darker than Low is measured instead of guessed (the CI "Medium study" passes). Absent or invalid: the tier's own values.
            string url = Application.absoluteURL; bool smokePage = !string.IsNullOrEmpty(url) && url.Contains("smoke=");
            float contrast = smokePage ? ARO.NetCore.LightingHour.QueryNumber(url, "contrast", 0f, 100f) : -1f, vignette = smokePage ? ARO.NetCore.LightingHour.QueryNumber(url, "vignette", 0f, 1f) : -1f;
            if (Tier.ColorGrading) { _grade = _profile.Add<ColorAdjustments>(true); _grade.postExposure.Override(0.1f); _grade.contrast.Override(contrast >= 0f ? contrast : 10f); _grade.saturation.Override(0f); }
            if (Tier.Vignette) { _vignette = _profile.Add<Vignette>(true); _vignette.intensity.Override(vignette >= 0f ? vignette : Tier.VignetteIntensity); _vignette.smoothness.Override(0.5f); }
            if (Tier.Bloom) { _bloom = _profile.Add<Bloom>(true); _bloom.threshold.Override(1.1f); _bloom.intensity.Override(0.25f); _bloom.scatter.Override(0.6f); }
            PostProcessingActive = true; PostProcessingNote = $"tonemap={Tier.Tonemapping}({(Tier.Tonemapping ? tm : "off")}) grade={Tier.ColorGrading} vignette={Tier.Vignette} bloom={Tier.Bloom}";
        }

        int _settingsVersion = -1;
        /// <summary>The Shadows and Post Processing settings apply live; the rest of a tier is fixed when the world is built.</summary>
        void ApplyLiveSettings()
        {
            if (_settingsVersion == ARO.Core.RuntimeSettings.Version) return; _settingsVersion = ARO.Core.RuntimeSettings.Version;
            if (_volume != null) _volume.enabled = ARO.Core.RuntimeSettings.PostProcessing;
            if (_sun != null) _sun.shadows = ARO.Core.RuntimeSettings.Shadows && Tier.ShadowCascades > 0 && !_noShadowsTest ? (Tier.SoftShadows ? LightShadows.Soft : LightShadows.Hard) : LightShadows.None;
        }

        void Update()
        {
            ApplyLiveSettings();
            if (!_postChecked && Time.unscaledTime > 3f && _cam != null)   // once the Volume stack has been evaluated by a few rendered frames
            {
                _postChecked = true;
                Debug.Log($"[Render] post-processing check rendererPostProcessData={PostProcessDataState()} volumeTonemap={VolumeTonemapState()} cameraPost={_cam.GetUniversalAdditionalCameraData().renderPostProcessing} settingOn={ARO.Core.RuntimeSettings.PostProcessing}");
            }
            // Hook: grade follows the clock (cheaper than anything per-pixel): darker, cooler exposure at night, warmer at the horizon.
            if (_grade == null || timeOfDay == null || Time.unscaledTime < _nextGrade) return;
            _nextGrade = Time.unscaledTime + 1f;
            var st = timeOfDay.State;   // the tested lighting model: exposure and tint follow the light
            _grade.postExposure.value = st.PostExposure;
            _grade.colorFilter.Override(new Color(st.PostFilter.R, st.PostFilter.G, st.PostFilter.B));
            if (Mathf.Abs(_grade.postExposure.value - _loggedExposure) > 0.005f) { _loggedExposure = _grade.postExposure.value; Debug.Log($"[Light] grade postExposure={_loggedExposure.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)} night={timeOfDay.IsNight} hour={timeOfDay.hour.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}"); }
        }

        void OnDestroy() { if (_profile != null) Destroy(_profile); }
    }
}
