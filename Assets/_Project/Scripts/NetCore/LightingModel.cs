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

    /// <summary>A colour as three floats, so the lighting maths stays outside the engine and can be tested.</summary>
    public struct Rgb
    {
        public float R, G, B;
        public Rgb(float r, float g, float b) { R = r; G = g; B = b; }
        public static Rgb Lerp(Rgb a, Rgb b, float t) { t = t < 0f ? 0f : t > 1f ? 1f : t; return new Rgb(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t); }
        public static Rgb operator *(Rgb c, float k) => new Rgb(c.R * k, c.G * k, c.B * k);
        public float Mean => (R + G + B) / 3f;
        public bool IsFinite => !(float.IsNaN(R) || float.IsNaN(G) || float.IsNaN(B) || float.IsInfinity(R) || float.IsInfinity(G) || float.IsInfinity(B));
    }

    /// <summary>Everything that lights the world at one moment. Pure data: <c>TimeOfDay</c> and <c>RenderingRig</c> apply it.</summary>
    public sealed class LightingState
    {
        public float Hour, Elevation, Day, Golden, OvercastIn;
        public float SunElevationDeg; public bool SunEnabled; public float SunIntensity; public Rgb SunColor;
        public bool MoonEnabled; public float MoonIntensity; public Rgb MoonColor;
        public float SkyExposure, Atmosphere; public Rgb SkyTint;
        public Rgb AmbientSky, AmbientEquator, AmbientGround;
        public Rgb FogColor; public float FogDensity;
        public float PostExposure; public Rgb PostFilter;
        /// <summary>One number for "how much light is there": the sun and moon plus the ambient. Used by tests, never by the renderer.</summary>
        /// <summary>The light on flat ground, per channel: the sun's share at its elevation plus the sky ambient. What an orange body or sandy terrain is multiplied by; above 1.0 it clips in a gamma pipeline.</summary>
        public Rgb FlatGroundLight { get { float k = SunEnabled ? SunIntensity * (float)Math.Max(0.0, Math.Sin(SunElevationDeg * Math.PI / 180.0)) : 0f; return new Rgb(SunColor.R * k + AmbientSky.R, SunColor.G * k + AmbientSky.G, SunColor.B * k + AmbientSky.B); } }
        public float IlluminationIndex => SunIntensity * (SunEnabled ? 1f : 0f) + MoonIntensity * (MoonEnabled ? 1f : 0f) + AmbientSky.Mean * 0.5f + AmbientEquator.Mean * 0.5f;
    }

    /// <summary>
    /// The sun, moon, sky, ambient light, fog and exposure for an hour of day and an overcast amount (0 clear .. 1 overcast).
    /// The sun is on the equator model used since the first version (up at 6:00, overhead at 12:00, down at 18:00). What this adds over that version: a moon
    /// so a night road has shape, a floor on night ambient light so nothing is pure black, warm horizon light and haze at dawn and sunset, a deeper horizon
    /// sky, and exposure that follows the light. Every curve is smooth in the hour (tested), so nothing pops as the day advances.
    /// </summary>
    public static class LightingModel
    {
        /// <summary>The moon's fixed direction (elevation and azimuth in degrees). It lights the scene; it is not drawn in the sky.</summary>
        public const float MoonElevationDeg = 42f, MoonAzimuthDeg = 150f;

        static float Smooth(float t) { t = t < 0f ? 0f : t > 1f ? 1f : t; return t * t * (3f - 2f * t); }
        static float InvLerp(float a, float b, float v) => b == a ? 0f : Math.Min(1f, Math.Max(0f, (v - a) / (b - a)));
        static float Lerp(float a, float b, float t) { t = t < 0f ? 0f : t > 1f ? 1f : t; return a + (b - a) * t; }
        static float Clamp01(float v) => float.IsNaN(v) ? 0f : v < 0f ? 0f : v > 1f ? 1f : v;

        // Sun colour by elevation: deep orange at the horizon, warm white high up (the original gradient).
        static readonly float[] SunKeyT = { 0.00f, 0.18f, 0.45f, 1.00f };
        static readonly Rgb[] SunKeyC = { new Rgb(1f, 0.45f, 0.18f), new Rgb(1f, 0.78f, 0.55f), new Rgb(1f, 0.96f, 0.88f), new Rgb(1f, 0.98f, 0.94f) };
        static Rgb SunGradient(float t)
        {
            t = Clamp01(t);
            for (int i = 1; i < SunKeyT.Length; i++) if (t <= SunKeyT[i]) return Rgb.Lerp(SunKeyC[i - 1], SunKeyC[i], (t - SunKeyT[i - 1]) / (SunKeyT[i] - SunKeyT[i - 1]));
            return SunKeyC[SunKeyC.Length - 1];
        }

        public static LightingState Evaluate(float hour, float overcast)
        {
            if (float.IsNaN(hour) || float.IsInfinity(hour)) hour = 12f;
            hour = hour % 24f; if (hour < 0f) hour += 24f;
            overcast = Clamp01(overcast);

            float ang = (hour - 6f) / 12f * (float)Math.PI;
            float elev = (float)Math.Sin(ang);                                   // -1 .. 1
            float side = (float)Math.Cos(ang);                                   // +1 at 06:00, -1 at 18:00, 0 at noon and midnight (continuous: no morning/evening switch)
            float dawnW = Smooth(0.5f + 0.5f * side);                            // 1 on the morning side of noon, 0 on the evening side
            float day = Smooth(InvLerp(-0.08f, 0.25f, elev));                    // 0 in the dark, 1 in full daylight
            float sunPow = Smooth(InvLerp(-0.08f, 0.16f, elev));                 // the direct sun is already strong a few degrees up (a low sun is not dim, it only lights surfaces at a grazing angle)
            float golden = Smooth(InvLerp(0.38f, 0.0f, Math.Abs(elev))) * Smooth(InvLerp(-0.10f, 0.05f, elev)) * (1f - 0.7f * overcast);   // close to the horizon with the sun up (dawn, sunset)
            float oc = overcast;

            var st = new LightingState { Hour = hour, Elevation = elev, Day = day, Golden = golden, OvercastIn = overcast };
            float gDawn = golden * dawnW, gDusk = golden * (1f - dawnW);          // the golden-hour weight on each side of noon

            // ---- sun. At dawn and sunset the light is lifted a little above the true elevation: a sun at 6 degrees gives flat ground 10% of its light, which leaves
            // the terrain a dark brown; at about 17 degrees the ground is lit and still throws long shadows. The lift fades out by 22 degrees, so the sun still rises monotonically.
            st.SunElevationDeg = (float)(Math.Asin(Math.Max(-1f, Math.Min(1f, elev))) * 180.0 / Math.PI) + 13f * golden;
            st.SunEnabled = elev > -0.075f;                                   // switched on where its intensity is ~0, so it never pops
            var sunCol = SunGradient(elev * 2f);
            sunCol = Rgb.Lerp(sunCol, new Rgb(1.00f, 0.82f, 0.76f), 0.45f * gDawn);   // dawn: paler, rosier light
            sunCol = Rgb.Lerp(sunCol, new Rgb(1.00f, 0.70f, 0.40f), 0.30f * gDusk);   // sunset: deeper gold (not deeper orange: the procedural sky multiplies its blue by the sun colour, and orange x blue is the olive-green zenith measured at f096663)
            st.SunColor = sunCol;
            // Noon 0.85 (was 1.35): in a gamma pipeline the surface's light is sun + ambient and a value above 1.0 clips, so an orange body (0.85, 0.55, 0.10) at 1.35 + 0.45
            // came out as flat yellow (255, 255, 58). Golden hour is stronger than the old curve (a clear low sun is bright); dawn a little softer than sunset.
            st.SunIntensity = 0.85f * sunPow * (1f + 0.62f * golden) * (1f - 0.10f * gDawn) * Lerp(1f, 0.35f, oc);

            // ---- moon: only while the sun is down; fades in as the sky darkens so the road keeps its shape at night
            float moon = Smooth(InvLerp(0.12f, -0.12f, elev));
            st.MoonEnabled = moon > 0.01f;
            st.MoonIntensity = 0.20f * moon * Lerp(1f, 0.45f, oc);                // about a seventh of the sun: night reads as night but the road keeps its shape
            st.MoonColor = new Rgb(0.60f, 0.70f, 1.00f);

            // ---- sky
            st.SkyExposure = Lerp(0.30f, 1.25f, day) * Lerp(1f, 0.6f, oc);
            st.Atmosphere = Lerp(0.9f, 1.4f, 1f - Math.Abs(elev)) + golden * 0.12f + oc * 0.6f;      // thicker air near the horizon reddens the sky. The extra golden-hour thickness was 0.45 (+0.15 at dawn): measured, it turned the upper sky olive green
            var tint = Rgb.Lerp(new Rgb(0.5f, 0.5f, 0.5f), new Rgb(0.45f, 0.5f, 0.6f), 1f - oc);
            tint = Rgb.Lerp(tint, new Rgb(0.46f, 0.49f, 0.60f), 0.6f * gDawn);     // dawn sky: cooler at the top
            st.SkyTint = Rgb.Lerp(tint, new Rgb(0.52f, 0.49f, 0.52f), 0.6f * gDusk);   // sunset sky: a touch warmer, still blue enough at the top

            // ---- ambient (trilight): a deep blue floor at night, a gentle daylight blue, and a warm, brighter-than-physical fill at dawn and sunset (the lit sky bounces light onto the ground)
            var skyAmb = Rgb.Lerp(new Rgb(0.050f, 0.070f, 0.130f), new Rgb(0.30f, 0.38f, 0.52f), day);
            var eq = Rgb.Lerp(new Rgb(0.040f, 0.048f, 0.075f), new Rgb(0.36f, 0.36f, 0.34f), day);
            var grd = Rgb.Lerp(new Rgb(0.025f, 0.025f, 0.030f), new Rgb(0.17f, 0.15f, 0.12f), day);
            skyAmb = Rgb.Lerp(skyAmb, Rgb.Lerp(new Rgb(0.42f, 0.35f, 0.34f), new Rgb(0.34f, 0.35f, 0.44f), dawnW), golden * 0.85f);
            eq = Rgb.Lerp(eq, Rgb.Lerp(new Rgb(0.42f, 0.32f, 0.24f), new Rgb(0.38f, 0.34f, 0.30f), dawnW), golden * 0.85f);
            grd = Rgb.Lerp(grd, Rgb.Lerp(new Rgb(0.17f, 0.13f, 0.10f), new Rgb(0.15f, 0.13f, 0.11f), dawnW), golden * 0.85f);
            st.AmbientSky = Rgb.Lerp(skyAmb, skyAmb * 0.7f, oc);
            st.AmbientEquator = eq * Lerp(1f, 0.8f, oc);
            st.AmbientGround = grd;

            // ---- fog: dark blue haze at night, pale daytime haze (harmattan-like), orange haze at sunset, a pink-grey mist at dawn; denser under cloud
            var fog = Rgb.Lerp(new Rgb(0.030f, 0.040f, 0.075f), new Rgb(0.72f, 0.76f, 0.80f), day);
            fog = Rgb.Lerp(fog, Rgb.Lerp(new Rgb(0.95f, 0.62f, 0.40f), new Rgb(0.88f, 0.68f, 0.66f), dawnW), golden * 0.50f);
            st.FogColor = Rgb.Lerp(fog, fog * 0.75f, oc);
            st.FogDensity = Lerp(0.00035f, 0.0011f, oc) * (1f + 1.0f * gDawn);   // morning mist: about twice the evening haze

            // ---- exposure and tint of the final image (applied only where the tier has a colour grade)
            float expBlend = Smooth(InvLerp(-0.10f, 0.10f, elev));                // below the horizon: the night grade; just above it: the daytime grade (+ a little at golden hour)
            st.PostExposure = Lerp(0.0f, 0.12f * golden, expBlend);
            var warm = Rgb.Lerp(new Rgb(1f, 0.92f, 0.80f), new Rgb(0.98f, 0.93f, 0.90f), dawnW);   // sunset filter is warm, dawn's is rosy
            st.PostFilter = Rgb.Lerp(Rgb.Lerp(new Rgb(0.88f, 0.94f, 1.0f), new Rgb(1f, 1f, 1f), day), warm, golden);
            return st;
        }
    }

    /// <summary>What the (placeholder) headlights are. Production vehicle prefabs bring their own lights; this is the fallback and the reference for tests.</summary>
    public static class HeadlightSpec
    {
        public const float Intensity = 240f, Range = 100f, OuterAngleDeg = 55f, InnerAngleDeg = 25f, PitchDownDeg = 1.5f;
    }
}
