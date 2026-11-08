using System;

namespace ARO.NetCore
{
    /// <summary>
    /// When the lamps of the world are lit. One pure function of the lighting state that <c>TimeOfDay</c> already produces (the sun's elevation and the overcast amount), so there is no
    /// second clock: street lights, shop and window lights, the aviation beacons and the vehicles' automatic headlights all follow the same number.
    /// The ramp is smooth in the hour (no pop), symmetric between evening and dawn, and moves earlier under cloud (a dark afternoon lights the street sooner).
    /// </summary>
    public static class NightLighting
    {
        /// <summary>Sine of the sun's elevation at which the lamps start to glow (about 9 degrees up) and at which they are fully on (about 2 degrees down).</summary>
        public const float OnStart = 0.16f, OnFull = -0.04f;
        /// <summary>Cloud moves both thresholds up by this much at full overcast.</summary>
        public const float OvercastShift = 0.10f;
        /// <summary>Automatic switching (vehicle headlights) uses a band so the lights never chatter around one value.</summary>
        public const float AutoOn = 0.50f, AutoOff = 0.35f;

        static float Clamp01(float v) => float.IsNaN(v) ? 0f : v < 0f ? 0f : v > 1f ? 1f : v;
        static float Smooth(float t) { t = Clamp01(t); return t * t * (3f - 2f * t); }

        /// <summary>0 in daylight, 1 at night, smooth in between. <paramref name="elevation"/> is the sine of the sun's elevation (LightingState.Elevation), <paramref name="overcast"/> is 0..1.</summary>
        public static float LampFactor(float elevation, float overcast)
        {
            if (float.IsNaN(elevation)) return 0f;
            float shift = OvercastShift * Clamp01(overcast), start = OnStart + shift, full = OnFull + shift;
            return Smooth((start - elevation) / (start - full));
        }

        public static float LampFactor(LightingState st) => st == null ? 0f : LampFactor(st.Elevation, st.OvercastIn);

        /// <summary>The automatic on/off decision with hysteresis: on above AutoOn, off below AutoOff, unchanged in between.</summary>
        public static bool AutoLampsOn(bool previous, float factor) => previous ? factor > AutoOff : factor >= AutoOn;

        /// <summary>
        /// Aviation-style beacon on a mast: a short double flash every two seconds (0..1). Deterministic in time, so a test can step through it; <paramref name="phase"/> (seconds) lets masts differ.
        /// </summary>
        public static float BeaconLevel(float timeSeconds, float phase = 0f)
        {
            float t = (timeSeconds + phase) % 2f; if (t < 0f) t += 2f;
            if (t < 0.12f) return 1f;
            if (t >= 0.30f && t < 0.42f) return 1f;
            return 0.06f;       // a faint afterglow of the lens, never fully black
        }

        // ------------------------------------------------------------------------------------------------ test hook (smoke pages only)
        /// <summary>?lamps= on a smoke page: auto (the clock decides), on or off (CI renders a night scene with and without the lamps).</summary>
        public enum Mode { Auto, On, Off }

        public static Mode ModeFromUrl(string url)
        {
            string v = QueryValue(url, "lamps");
            if (string.Equals(v, "on", StringComparison.OrdinalIgnoreCase)) return Mode.On;
            if (string.Equals(v, "off", StringComparison.OrdinalIgnoreCase)) return Mode.Off;
            return Mode.Auto;
        }

        /// <summary>The factor the world actually uses: the clock's, unless a test page forced the lamps on or off.</summary>
        public static float Apply(Mode mode, float clockFactor) => mode == Mode.On ? 1f : mode == Mode.Off ? 0f : clockFactor;

        /// <summary>Raw value of a page-address parameter (<c>lamps=off</c>), or null. Not unescaped beyond what the callers need; tests only.</summary>
        public static string QueryValue(string url, string name)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(name)) return null;
            int q = url.IndexOf('?'); if (q < 0) return null;
            foreach (var part in url.Substring(q + 1).Split('&', '#'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0 || !string.Equals(part.Substring(0, eq), name, StringComparison.OrdinalIgnoreCase)) continue;
                return Uri.UnescapeDataString(part.Substring(eq + 1));
            }
            return null;
        }
    }
}
