using System;
using System.Linq;
using ARO.Core;
using ARO.NetCore;
using UnityEngine;

namespace ARO.Game
{
    /// <summary>
    /// Keeps the player's settings on this device (PlayerPrefs: the browser's storage in WebGL) and applies them to the running game. They are loaded
    /// before the first scene so the world is built with the player's graphics and traffic choices; saving applies them at once. A damaged value falls
    /// back to its default (see <see cref="GameSettings.Parse"/>), so a bad save can never stop the game from starting.
    /// </summary>
    public static class SettingsStore
    {
        const string Key = "aro.settings.v1";
        public static GameSettings Current { get; private set; } = new GameSettings();
        static GameObject _fps;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            Current = Load(); Apply(Current, startup: true);
            Debug.Log($"[Settings] loaded changed={Current.ChangedFrom(new GameSettings()).Count()} stored={(PlayerPrefs.HasKey(Key) ? "yes" : "no")}");
        }

        public static GameSettings Load()
        {
            try { return GameSettings.Parse(PlayerPrefs.GetString(Key, "")); }
            catch (Exception e) { Debug.LogWarning("[Settings] could not read the saved settings: " + e.Message); return new GameSettings(); }
        }

        /// <summary>Stores and applies. False when the device refused to store them (they are still applied for this session).</summary>
        public static bool Save(GameSettings s)
        {
            bool stored = true;
            try { PlayerPrefs.SetString(Key, s.Serialize()); PlayerPrefs.Save(); }
            catch (Exception e) { stored = false; Debug.LogWarning("[Settings] could not store the settings: " + e.Message); }
            Current = s.Clone(); Apply(Current, startup: false);
            return stored;
        }

        /// <summary>Full screen is a session choice: the browser only allows it after a click, so it is applied on Save, never at start-up.</summary>
        public static bool IsFullScreen => Screen.fullScreen;

        public static void Apply(GameSettings s, bool startup)
        {
            RuntimeSettings.CameraShake = SettingsEffect.Percent(s.Int("camera_shake"));
            RuntimeSettings.EngineVolume = SettingsEffect.Percent(s.Int("engine_volume"));
            RuntimeSettings.SteeringGain = SettingsEffect.SteeringGain(s.Int("steering"));
            RuntimeSettings.FixedHour = SettingsEffect.FixedHour(s.Get("time_of_day"));
            RuntimeSettings.ShowPlayerNames = s.Bool("show_names");
            RuntimeSettings.ShowMinimap = s.Bool("minimap");
            RuntimeSettings.Shadows = s.Bool("shadows");
            RuntimeSettings.PostProcessing = s.Bool("post");
            RuntimeSettings.SpeedMph = s.Get("speed_unit") == "mph";
            RuntimeSettings.QualityPreference = s.Get("quality");
            RuntimeSettings.TrafficChoice = s.Get("traffic");
            RuntimeSettings.Version++;
            AudioListener.volume = SettingsEffect.Percent(s.Int("master_volume"));
            SetFps(s.Bool("show_fps"));
            if (!startup) { bool want = s.Get("display_mode") == "fullscreen"; if (Screen.fullScreen != want) Screen.fullScreen = want; }
            Debug.Log($"[Settings] applied master={AudioListener.volume:0.00} engine={RuntimeSettings.EngineVolume:0.00} steering={RuntimeSettings.SteeringGain:0.00} shake={RuntimeSettings.CameraShake:0.00} " +
                      $"hour={RuntimeSettings.FixedHour:0.0} names={RuntimeSettings.ShowPlayerNames} minimap={RuntimeSettings.ShowMinimap} shadows={RuntimeSettings.Shadows} post={RuntimeSettings.PostProcessing} " +
                      $"speed={SettingsEffect.SpeedUnit(RuntimeSettings.SpeedMph)} quality={RuntimeSettings.QualityPreference} traffic={RuntimeSettings.TrafficChoice} fps={s.Bool("show_fps")} startup={startup}");
        }

        static void SetFps(bool on)
        {
            if (on && _fps == null) { _fps = new GameObject("FpsCounter", typeof(FpsOverlay)); UnityEngine.Object.DontDestroyOnLoad(_fps); }
            else if (!on && _fps != null) { UnityEngine.Object.Destroy(_fps); _fps = null; }
        }
    }

    /// <summary>The Show FPS Counter setting: a small frames-per-second label in the bottom-right corner.</summary>
    public sealed class FpsOverlay : MonoBehaviour
    {
        float _fps = 60f; GUIStyle _style;
        void Update() { _fps = Mathf.Lerp(_fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f), 0.05f); }
        void OnGUI()
        {
            if (_style == null) { _style = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerRight }; _style.normal.textColor = new Color(0.95f, 0.78f, 0.2f); }
            GUI.Label(new Rect(Screen.width - 170, Screen.height - 40, 160, 32), $"{_fps:0} FPS", _style);
        }
    }
}
