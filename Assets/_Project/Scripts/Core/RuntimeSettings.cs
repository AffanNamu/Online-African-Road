namespace ARO.Core
{
    /// <summary>
    /// The player's settings as the running game reads them. SettingsStore (Game) fills these from the saved settings at start-up and again whenever the
    /// player saves; the camera, audio, input, HUD, world and renderer read them. They sit in Core so assemblies that cannot see the settings model
    /// (vehicles, world) can still honour a setting. <see cref="Version"/> changes on every update so a component can poll it cheaply.
    /// </summary>
    public static class RuntimeSettings
    {
        public static float CameraShake = 1f, EngineVolume = 1f, SteeringGain = 1f, FixedHour = -1f;
        /// <summary>The hour from the page address (?hour=noon, ?hour=17.6), for reproducible lighting screenshots; -1 when there is none. It beats the player's Time of Day setting.</summary>
        public static float TestHour = -1f;
        public static bool ShowPlayerNames = true, ShowMinimap = true, Shadows = true, PostProcessing = true, SpeedMph;
        public static string QualityPreference = "auto", TrafficChoice = "medium";
        public static int Version;

        /// <summary>What the renderer is actually using (written by the rendering rig, shown by the Settings page).</summary>
        public static string TierName = "", AntiAliasing = "", ViewDistance = "", TierSummary = "";
    }
}
