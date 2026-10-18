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
        /// <summary>Test hook (only on ?smoke= pages): a sky atmosphere thickness from ?atm=, so CI can render dawn and sunset at several thicknesses and the value is chosen from real frames. -1 when none.</summary>
        public static float TestAtmosphere = -1f;
        /// <summary>Test hook (only on ?smoke= pages): ?sky=gradient draws the custom gradient sky (ARO/SkyGradient) instead of the procedural one, so CI can render both and the choice is made from frames. False by default.</summary>
        public static bool TestSkyGradient;
        /// <summary>Test hooks for the gradient sky study (smoke pages only): ?skyglow= scales the sun glow, ?skymid= moves the mid-colour stop, ?skyexp= bends the height curve. -1 = the model's own value.</summary>
        public static float TestSkyGlow = -1f, TestSkyMid = -1f, TestSkyExp = -1f;
        /// <summary>Gradient sky study (smoke pages only): ?skypal=3|4 chooses the palette (3 is the control), ?skyhaze=0..1 the haze at the horizon, ?skyfog=k multiplies the fog density and tints the fog with the haze (the only one of these that touches the lit world).</summary>
        public static int TestSkyPalette = 4; public static float TestSkyHaze = -1f, TestSkyFog = -1f;
        /// <summary>Gradient sky horizon study (smoke pages only): ?skydim= scales the horizon band and haze, ?skydip= darkens the sky right at the horizon, ?skyfogd= sets the fog density outright (fog tinted with the haze). -1 = off.</summary>
        public static float TestSkyDim = -1f, TestSkyDip = -1f, TestSkyFogD = -1f;
        public static bool ShowPlayerNames = true, ShowMinimap = true, Shadows = true, PostProcessing = true, SpeedMph;
        public static string QualityPreference = "auto", TrafficChoice = "medium";
        public static int Version;

        /// <summary>What the renderer is actually using (written by the rendering rig, shown by the Settings page).</summary>
        public static string TierName = "", AntiAliasing = "", ViewDistance = "", TierSummary = "";
    }
}
