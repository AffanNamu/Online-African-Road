using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ARO.NetCore
{
    // ===================================================================================================== the settings catalogue
    public enum SettingKind { Choice, Toggle, Slider, Info }

    /// <summary>
    /// Live: applied when the player presses Save. OnReload: stored at once, takes effect when the game is next loaded (the world is built once per load).
    /// Soon: no game system exists behind it yet - listed, disabled, and recorded in docs/PLACEHOLDERS.md. Fixed: a real value that cannot be changed.
    /// </summary>
    public enum SettingStatus { Live, OnReload, Soon, Fixed }

    public enum SettingsPanel { Game, Language, Graphics, Gameplay, Audio, Input }
    public enum SettingsTab { Game, Graphics, Controls, Audio, Gameplay, Account, Language }

    public sealed class SettingDef
    {
        public string Id, Label, Note, Default, FixedText;
        public SettingsPanel Panel; public SettingKind Kind; public SettingStatus Status;
        public string[] Options = new string[0], OptionLabels = new string[0];
        public int Min, Max = 100, Step = 1;
        public bool Persist = true;             // false: a session value (the browser decides whether full screen is allowed, so it is never replayed at start-up)
        public bool Server;                     // true: stored in the database (changes are saved at once), not in the settings file

        /// <summary>A value the local settings file holds and the player may change.</summary>
        public bool Local => Kind != SettingKind.Info && !Server;
        public bool Editable => Local && (Status == SettingStatus.Live || Status == SettingStatus.OnReload);
        public string LabelOf(string value) { int i = Array.IndexOf(Options, value); return i >= 0 && i < OptionLabels.Length ? OptionLabels[i] : value; }
    }

    public static class SettingsCatalog
    {
        static SettingDef Choice(string id, string label, SettingsPanel p, SettingStatus s, string def, string[] opts, string[] labels, string note = null, bool persist = true) =>
            new SettingDef { Id = id, Label = label, Panel = p, Kind = SettingKind.Choice, Status = s, Default = def, Options = opts, OptionLabels = labels, Note = note, Persist = persist };
        static SettingDef Toggle(string id, string label, SettingsPanel p, SettingStatus s, bool def, string note = null) =>
            new SettingDef { Id = id, Label = label, Panel = p, Kind = SettingKind.Toggle, Status = s, Default = def ? "1" : "0", Options = new[] { "0", "1" }, OptionLabels = new[] { "Disabled", "Enabled" }, Note = note };
        static SettingDef Slider(string id, string label, SettingsPanel p, SettingStatus s, int def, int step, string note = null) =>
            new SettingDef { Id = id, Label = label, Panel = p, Kind = SettingKind.Slider, Status = s, Default = def.ToString(CultureInfo.InvariantCulture), Min = 0, Max = 100, Step = step, Note = note };
        static SettingDef Info(string id, string label, SettingsPanel p, string text, string note = null) =>
            new SettingDef { Id = id, Label = label, Panel = p, Kind = SettingKind.Info, Status = SettingStatus.Fixed, FixedText = text, Default = "", Note = note };
        static SettingDef ServerToggle(string id, string label, SettingsPanel p, string note) =>
            new SettingDef { Id = id, Label = label, Panel = p, Kind = SettingKind.Toggle, Status = SettingStatus.Live, Default = "1", Options = new[] { "0", "1" }, OptionLabels = new[] { "Disabled", "Enabled" }, Server = true, Persist = false, Note = note };

        const string NoSystem = "The game has no system for this yet.";

        public static readonly IReadOnlyList<SettingDef> All = new List<SettingDef>
        {
            // ---- Game Settings
            Choice("game_mode", "Game Mode", SettingsPanel.Game, SettingStatus.Soon, "simulation", new[] { "simulation", "arcade" }, new[] { "Simulation (Realistic)", "Arcade" }, "Driving-feel modes are not built yet."),
            Choice("speed_unit", "Speed Unit", SettingsPanel.Game, SettingStatus.Live, "kmh", new[] { "kmh", "mph" }, new[] { "Metric (km/h)", "Imperial (mph)" }, "Changes the speedometer."),
            Choice("time_of_day", "Time of Day", SettingsPanel.Game, SettingStatus.Live, "real", new[] { "real", "morning", "noon", "evening", "night" }, new[] { "Real Time (West Africa)", "Morning", "Midday", "Evening", "Night" }, "Real Time follows the clock in West Africa."),
            Choice("traffic", "Traffic Density", SettingsPanel.Game, SettingStatus.OnReload, "medium", new[] { "low", "medium", "high" }, new[] { "Low", "Medium", "High" }, "Takes effect the next time the game loads."),
            Toggle("weather", "Weather System", SettingsPanel.Game, SettingStatus.Soon, true, "Weather is not scheduled in the world yet."),
            Toggle("hints", "In-Game Hints", SettingsPanel.Game, SettingStatus.Soon, true, NoSystem),
            Toggle("nav_assist", "Navigation Assistance", SettingsPanel.Game, SettingStatus.Soon, true, NoSystem),
            Toggle("auto_parking", "Automatic Parking", SettingsPanel.Game, SettingStatus.Soon, false, NoSystem),
            Toggle("minimap", "Show Minimap", SettingsPanel.Game, SettingStatus.Live, true, "The route map in the driving view."),
            Slider("camera_shake", "Camera Shake", SettingsPanel.Game, SettingStatus.Live, 100, 5, "How much the chase camera shakes on rough road."),

            // ---- Language & Region
            Choice("language", "Language", SettingsPanel.Language, SettingStatus.Soon, "en", new[] { "en" }, new[] { "English" }, "More languages are not available yet."),
            Info("region", "Region", SettingsPanel.Language, "Africa (West Africa)", "The world and the clock are West Africa."),
            Info("distance_units", "Distance Units", SettingsPanel.Language, "Kilometres (km)", "Distances in the game are always kilometres."),
            Info("currency", "Currency Display", SettingsPanel.Language, "Nigerian Naira (₦)", "The game's one currency."),

            // ---- Graphics
            Choice("quality", "Graphics Quality", SettingsPanel.Graphics, SettingStatus.OnReload, "auto", new[] { "auto", "low", "medium", "high" }, new[] { "Auto (your device)", "Low", "Medium", "High" }, "Takes effect the next time the game loads."),
            Info("resolution", "Resolution", SettingsPanel.Graphics, "Fits your window", "A web game draws at the size of its window."),
            Choice("display_mode", "Display Mode", SettingsPanel.Graphics, SettingStatus.Live, "windowed", new[] { "windowed", "fullscreen" }, new[] { "Windowed", "Full Screen" }, "Your browser decides whether full screen is allowed.", persist: false),
            Info("vsync", "VSync", SettingsPanel.Graphics, "Set by your browser", "A web game follows the browser's refresh."),
            Info("aa", "Anti-Aliasing", SettingsPanel.Graphics, null, "Set by Graphics Quality."),
            Toggle("shadows", "Shadows", SettingsPanel.Graphics, SettingStatus.Live, true, "Off is the cheapest on a slow device."),
            Choice("textures", "Textures", SettingsPanel.Graphics, SettingStatus.Soon, "high", new[] { "low", "medium", "high" }, new[] { "Low", "Medium", "High" }, NoSystem),
            Info("view_distance", "View Distance", SettingsPanel.Graphics, null, "Set by Graphics Quality."),
            Choice("reflections", "Reflections", SettingsPanel.Graphics, SettingStatus.Soon, "high", new[] { "low", "medium", "high" }, new[] { "Low", "Medium", "High" }, NoSystem),
            Toggle("post", "Post Processing", SettingsPanel.Graphics, SettingStatus.Live, true, "Colour grading, vignette and bloom."),
            Slider("brightness", "Brightness", SettingsPanel.Graphics, SettingStatus.Soon, 100, 5, NoSystem),
            Slider("contrast", "Contrast", SettingsPanel.Graphics, SettingStatus.Soon, 100, 5, NoSystem),
            Slider("saturation", "Saturation", SettingsPanel.Graphics, SettingStatus.Soon, 100, 5, NoSystem),
            Toggle("show_fps", "Show FPS Counter", SettingsPanel.Graphics, SettingStatus.Live, false, "A small counter in the corner of the screen."),

            // ---- Gameplay Preferences
            Toggle("show_names", "Show Player Names", SettingsPanel.Gameplay, SettingStatus.Live, true, "The name above other players' vehicles."),
            ServerToggle("appear_nearby", "Appear in Nearby Players", SettingsPanel.Gameplay, "Saved to your account at once."),
            Toggle("notifications", "Online Notifications", SettingsPanel.Gameplay, SettingStatus.Soon, true, "Notification preferences are not built yet."),
            Toggle("chat_messages", "Chat Messages", SettingsPanel.Gameplay, SettingStatus.Soon, true, NoSystem),
            Toggle("profanity", "Profanity Filter", SettingsPanel.Gameplay, SettingStatus.Soon, true, NoSystem),

            // ---- Audio
            Slider("master_volume", "Master Volume", SettingsPanel.Audio, SettingStatus.Live, 100, 5, "Everything you hear."),
            Slider("engine_volume", "Engine Sound", SettingsPanel.Audio, SettingStatus.Live, 100, 5, "Your vehicle's engine."),
            Slider("environment_volume", "Environment", SettingsPanel.Audio, SettingStatus.Soon, 100, 5, "There is no ambient sound yet."),
            Slider("radio_volume", "Radio Music", SettingsPanel.Audio, SettingStatus.Soon, 100, 5, "There is no radio yet."),
            Slider("ui_volume", "UI Sounds", SettingsPanel.Audio, SettingStatus.Soon, 100, 5, "The menus make no sound yet."),
            Slider("voice_volume", "Voice Chat", SettingsPanel.Audio, SettingStatus.Soon, 100, 5, "Voice chat is not built yet."),

            // ---- Controller / Input
            Slider("steering", "Steering Sensitivity", SettingsPanel.Input, SettingStatus.Live, 50, 5, "How quickly the steering answers keys and sticks."),
            Slider("brake_sens", "Brake Sensitivity", SettingsPanel.Input, SettingStatus.Soon, 50, 5, NoSystem),
            Slider("accel_sens", "Acceleration Sensitivity", SettingsPanel.Input, SettingStatus.Soon, 50, 5, NoSystem),
            Toggle("invert_y", "Invert Y-Axis", SettingsPanel.Input, SettingStatus.Soon, false, NoSystem),
            Toggle("force_feedback", "Force Feedback", SettingsPanel.Input, SettingStatus.Soon, false, NoSystem),
        };

        static readonly Dictionary<string, SettingDef> ById = All.ToDictionary(d => d.Id, StringComparer.Ordinal);
        public static SettingDef Find(string id) => id != null && ById.TryGetValue(id, out var d) ? d : null;
        public static IEnumerable<SettingDef> Of(SettingsPanel p) => All.Where(d => d.Panel == p);

        public static string PanelTitle(SettingsPanel p) => p == SettingsPanel.Game ? "Game Settings" : p == SettingsPanel.Language ? "Language & Region" : p == SettingsPanel.Graphics ? "Graphics Settings"
            : p == SettingsPanel.Gameplay ? "Gameplay Preferences" : p == SettingsPanel.Audio ? "Audio Settings" : "Controller / Input";

        public static string TabLabel(SettingsTab t) => t == SettingsTab.Game ? "Game" : t.ToString();

        /// <summary>The panels a tab shows. The Game tab is the overview of everything; the others show their own area.</summary>
        public static SettingsPanel[] PanelsOf(SettingsTab t) => t == SettingsTab.Game ? new[] { SettingsPanel.Game, SettingsPanel.Language, SettingsPanel.Graphics, SettingsPanel.Gameplay, SettingsPanel.Audio, SettingsPanel.Input }
            : t == SettingsTab.Graphics ? new[] { SettingsPanel.Graphics } : t == SettingsTab.Controls ? new[] { SettingsPanel.Input } : t == SettingsTab.Audio ? new[] { SettingsPanel.Audio }
            : t == SettingsTab.Gameplay ? new[] { SettingsPanel.Gameplay } : t == SettingsTab.Language ? new[] { SettingsPanel.Language } : new SettingsPanel[0];
    }

    // ===================================================================================================== the player's values
    /// <summary>
    /// The player's settings. Only editable values can change (a "Soon" or fixed row never does), every value is validated, and the stored text
    /// survives corruption: an unknown key, a bad number or an option that no longer exists falls back to the default instead of failing.
    /// </summary>
    public sealed class GameSettings
    {
        readonly Dictionary<string, string> _v = new Dictionary<string, string>(StringComparer.Ordinal);
        public GameSettings() { ResetToDefaults(); }

        public void ResetToDefaults() { _v.Clear(); foreach (var d in SettingsCatalog.All) if (d.Local) _v[d.Id] = d.Default; }
        public string Get(string id) => _v.TryGetValue(id ?? "", out var s) ? s : (SettingsCatalog.Find(id)?.Default ?? "");
        public bool Bool(string id) => Get(id) == "1";
        public int Int(string id) => int.TryParse(Get(id), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

        /// <summary>The value the settings file would hold for this row: validated and, for a slider, snapped to its step. Null when the value is not acceptable.</summary>
        public static string Normalise(SettingDef d, string value)
        {
            if (d == null || value == null) return null;
            switch (d.Kind)
            {
                case SettingKind.Toggle: return value == "0" || value == "1" ? value : null;
                case SettingKind.Choice: return Array.IndexOf(d.Options, value) >= 0 ? value : null;
                case SettingKind.Slider:
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return null;
                    n = Math.Max(d.Min, Math.Min(d.Max, n)); int step = Math.Max(1, d.Step); n = d.Min + (int)Math.Round((n - d.Min) / (double)step) * step;
                    return Math.Min(d.Max, n).ToString(CultureInfo.InvariantCulture);
                default: return null;
            }
        }

        /// <summary>Sets a value. False (and no change) when the row is unknown, cannot be changed, or the value is not acceptable.</summary>
        public bool TrySet(string id, string value)
        {
            var d = SettingsCatalog.Find(id); if (d == null || !d.Editable) return false;
            var n = Normalise(d, value); if (n == null) return false; _v[id] = n; return true;
        }
        public bool SetBool(string id, bool on) => TrySet(id, on ? "1" : "0");
        public bool SetInt(string id, int value) => TrySet(id, value.ToString(CultureInfo.InvariantCulture));

        public GameSettings Clone() { var c = new GameSettings(); foreach (var kv in _v) c._v[kv.Key] = kv.Value; return c; }
        public IEnumerable<string> ChangedFrom(GameSettings o) => SettingsCatalog.All.Where(d => d.Local && Get(d.Id) != (o ?? new GameSettings()).Get(d.Id)).Select(d => d.Id);
        public bool SameAs(GameSettings o) => !ChangedFrom(o).Any();
        public bool IsDefault => SameAs(new GameSettings());

        /// <summary>One "id=value" line per stored value (rows that persist and can change); stable order.</summary>
        public string Serialize()
        {
            var sb = new StringBuilder();
            foreach (var d in SettingsCatalog.All) if (d.Editable && d.Persist) sb.Append(d.Id).Append('=').Append(Get(d.Id)).Append('\n');
            return sb.ToString();
        }

        /// <summary>Reads stored text; anything unusable is ignored, so a damaged or older file still gives a valid set of settings.</summary>
        public static GameSettings Parse(string text)
        {
            var s = new GameSettings(); if (string.IsNullOrEmpty(text)) return s;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim(); int eq = line.IndexOf('='); if (eq <= 0) continue;
                var d = SettingsCatalog.Find(line.Substring(0, eq).Trim()); if (d == null || !d.Persist) continue;
                s.TrySet(d.Id, line.Substring(eq + 1).Trim());
            }
            return s;
        }
    }

    // ===================================================================================================== what a setting does
    /// <summary>The numbers each setting produces for the game. Kept here so they are tested without Unity.</summary>
    public static class SettingsEffect
    {
        public static float Percent(int p) => Math.Max(0, Math.Min(100, p)) / 100f;
        /// <summary>Steering response multiplier: 50 % is the original feel (1.0), 0 % half as quick, 100 % one and a half times as quick.</summary>
        public static float SteeringGain(int p) => 0.5f + Percent(p);
        public static float TrafficScale(string v) => v == "low" ? 0.5f : v == "high" ? 1.5f : 1f;
        /// <summary>The fixed clock hour for a Time of Day choice; -1 means follow the real clock.</summary>
        public static float FixedHour(string v) => v == "morning" ? 8f : v == "noon" ? 12f : v == "evening" ? 17.5f : v == "night" ? 22f : -1f;
        public static float SpeedFor(float kmh, bool imperial) => imperial ? kmh * 0.621371f : kmh;
        public static string SpeedUnit(bool imperial) => imperial ? "mph" : "km/h";

        /// <summary>The tier with the player's traffic choice applied. The shared tier objects are never changed.</summary>
        public static QualityTier WithTraffic(QualityTier t, string trafficChoice)
        {
            float k = TrafficScale(trafficChoice); if (Math.Abs(k - 1f) < 0.001f) return t;
            var c = t.Clone(); c.TrafficDensity = t.TrafficDensity * k; c.MaxTrafficAgents = Math.Max(4, (int)Math.Round(t.MaxTrafficAgents * k)); return c;
        }

        public static string AntiAliasing(QualityTier t) => t.MsaaSamples > 1 ? $"MSAA {t.MsaaSamples}x" : "Off";
        public static string ViewDistance(QualityTier t) => $"{t.FarClip:0} m";
        public static string QualityName(QualityTier t) => char.ToUpperInvariant(t.Name[0]) + t.Name.Substring(1);
        public static string TierSummary(QualityTier t) =>
            $"{QualityName(t)}: shadows to {t.ShadowDistance:0} m, {AntiAliasing(t)}, render scale {t.RenderScale:0.0}x, view distance {ViewDistance(t)}, {t.LoadRadiusChunks} chunks around you";
    }

    // ===================================================================================================== controls
    /// <summary>The bindings the driving input reads (KeyboardVehicleInput). Shown read-only: the game cannot rebind keys yet. A test keeps this list equal to the code.</summary>
    public static class ControlBindings
    {
        public sealed class Binding { public string Action, Keys, Device; public string[] KeyProps; }
        public static readonly IReadOnlyList<Binding> All = new List<Binding>
        {
            new Binding { Device = "Keyboard", Action = "Accelerate", Keys = "W  /  Up", KeyProps = new[] { "wKey", "upArrowKey" } },
            new Binding { Device = "Keyboard", Action = "Brake / Reverse", Keys = "S  /  Down", KeyProps = new[] { "sKey", "downArrowKey" } },
            new Binding { Device = "Keyboard", Action = "Steer left", Keys = "A  /  Left", KeyProps = new[] { "aKey", "leftArrowKey" } },
            new Binding { Device = "Keyboard", Action = "Steer right", Keys = "D  /  Right", KeyProps = new[] { "dKey", "rightArrowKey" } },
            new Binding { Device = "Keyboard", Action = "Handbrake", Keys = "Space", KeyProps = new[] { "spaceKey" } },
            new Binding { Device = "Keyboard", Action = "Horn", Keys = "H", KeyProps = new[] { "hKey" } },
            new Binding { Device = "Keyboard", Action = "Lights", Keys = "L", KeyProps = new[] { "lKey" } },
            new Binding { Device = "Keyboard", Action = "Left indicator", Keys = "Q", KeyProps = new[] { "qKey" } },
            new Binding { Device = "Keyboard", Action = "Right indicator", Keys = "E", KeyProps = new[] { "eKey" } },
            new Binding { Device = "Keyboard", Action = "Change camera", Keys = "C", KeyProps = new[] { "cKey" } },
            new Binding { Device = "Mouse", Action = "Camera distance", Keys = "Mouse wheel", KeyProps = new[] { "scroll" } },
            new Binding { Device = "Gamepad", Action = "Steer", Keys = "Left stick", KeyProps = new[] { "leftStick" } },
            new Binding { Device = "Gamepad", Action = "Accelerate", Keys = "Right trigger", KeyProps = new[] { "rightTrigger" } },
            new Binding { Device = "Gamepad", Action = "Brake / Reverse", Keys = "Left trigger", KeyProps = new[] { "leftTrigger" } },
            new Binding { Device = "Gamepad", Action = "Handbrake", Keys = "A (south)", KeyProps = new[] { "buttonSouth" } },
            new Binding { Device = "Gamepad", Action = "Horn", Keys = "X (west)", KeyProps = new[] { "buttonWest" } },
            new Binding { Device = "Gamepad", Action = "Lights", Keys = "D-pad up", KeyProps = new[] { "dpad.up" } },
            new Binding { Device = "Gamepad", Action = "Change camera", Keys = "Y (north)", KeyProps = new[] { "buttonNorth" } },
        };
    }
}
