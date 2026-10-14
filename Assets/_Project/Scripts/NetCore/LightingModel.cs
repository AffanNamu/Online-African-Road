using System;
using System.Globalization;

namespace ARO.NetCore
{
    /// <summary>
    /// The hour of day the world is lit for, and where it comes from. Pure and tested; <c>TimeOfDay</c> (Unity) only applies the result.
    /// Precedence, highest first: the page address (<c>?hour=noon</c>, for reproducible test screenshots, like <c>?quality=</c>), then the player's
    /// Time of Day setting (a fixed hour), then the real West Africa clock.
    /// </summary>
    public static class LightingHour
    {
        // The four reference moments used by the lighting tests and reviews. Sun elevation by TimeOfDay's own formula (sin of (hour-6)/12*pi):
        // dawn 6.4 = +6 deg and rising, noon 12 = overhead, sunset 17.6 = +6 deg and falling (symmetric to dawn), night 23 = well below the horizon.
        public const float Dawn = 6.4f, Noon = 12f, Sunset = 17.6f, Night = 23f;
        public static readonly string[] PresetNames = { "dawn", "noon", "sunset", "night" };
        public static readonly float[] PresetHours = { Dawn, Noon, Sunset, Night };

        public const string SourceUrl = "url", SourceSetting = "setting", SourceClock = "clock";

        /// <summary>The hour requested by <c>hour=...</c> in a page address, or -1 when there is none or it is not valid.</summary>
        public static float FromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return -1f;
            int q = url.IndexOf('?'); if (q < 0) return -1f;
            foreach (var part in url.Substring(q + 1).Split('&', '#'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0 || !string.Equals(part.Substring(0, eq), "hour", StringComparison.OrdinalIgnoreCase)) continue;
                return TryParse(Uri.UnescapeDataString(part.Substring(eq + 1)), out float h) ? h : -1f;
            }
            return -1f;
        }

        /// <summary>A preset name (dawn, noon, sunset, night), a decimal hour (<c>6.5</c>) or a clock time (<c>18:30</c>). 0 to 24; 24 means 0.</summary>
        public static bool TryParse(string value, out float hour)
        {
            hour = -1f;
            if (string.IsNullOrWhiteSpace(value)) return false;
            value = value.Trim();
            for (int i = 0; i < PresetNames.Length; i++)
                if (string.Equals(value, PresetNames[i], StringComparison.OrdinalIgnoreCase)) { hour = PresetHours[i]; return true; }
            float h;
            int colon = value.IndexOf(':');
            if (colon > 0)
            {
                if (!int.TryParse(value.Substring(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out int hh)) return false;
                if (!int.TryParse(value.Substring(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int mm) || mm > 59) return false;
                h = hh + mm / 60f;
            }
            else if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out h)) return false;
            if (float.IsNaN(h) || float.IsInfinity(h) || h < 0f || h > 24f) return false;
            hour = h >= 24f ? 0f : h; return true;
        }

        /// <summary>Real West Africa Time (UTC+1, no daylight saving) as an hour of day.</summary>
        public static float ClockHour(DateTime utcNow)
        {
            var wat = utcNow.AddHours(1);
            return wat.Hour + wat.Minute / 60f + wat.Second / 3600f;
        }

        /// <summary>Pick the hour: a valid URL hour, else a fixed setting (>= 0), else the clock. <paramref name="source"/> says which one won.</summary>
        public static float Resolve(float urlHour, float settingHour, float clockHour, out string source)
        {
            if (urlHour >= 0f) { source = SourceUrl; return urlHour; }
            if (settingHour >= 0f) { source = SourceSetting; return settingHour; }
            source = SourceClock; return clockHour;
        }
    }
}
