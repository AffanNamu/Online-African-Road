using System;

namespace ARO.NetCore
{
    /// <summary>What the gradient sky is painted with at one moment. Colours are what the sky should look like on screen (before the final tonemap), not physical radiance.</summary>
    public sealed class SkyGradientState
    {
        /// <summary>Zenith, mid sky, the colour of the horizon BAND (what the chase camera sees just above the terrain), the pale haze exactly at the horizon, the ground and the sun glow.</summary>
        public Rgb Zenith, Mid, Horizon, Haze, Ground, Glow;
        /// <summary>Strength of the wide glow around the sun (0 at night), its tightness (higher = smaller) and the brightness of the sun disc.</summary>
        public float GlowStrength, GlowPower, DiscStrength;
        /// <summary>Where the mid colour sits (as the sine of the elevation: 0.22 is about 13 degrees) and the curve of the climb to the zenith (1 = linear in that sine). A low mid stop keeps the warm horizon band wide enough to be seen from a chase camera that looks down the road.</summary>
        public float LowHeight, MidHeight, HeightExp;
        /// <summary>Darkening of the lowest part of the sky (a "dusty land haze" that meets the terrain): the sky is multiplied by (1 - Dip * (1 - smoothstep(height / DipHeight))), so it is darkest exactly at the horizon and back to normal at DipHeight.</summary>
        public float Dip, DipHeight;
    }

    /// <summary>Optional adjustments of the horizon (all neutral by default): <see cref="BandDim"/> scales the horizon band and the haze, <see cref="Dip"/> darkens the sky right at the horizon up to <see cref="DipHeight"/>.</summary>
    public struct SkyTune
    {
        public float BandDim, Dip, DipHeight;
        public static SkyTune None => new SkyTune { BandDim = 1f, Dip = 0f, DipHeight = 0.10f };
    }

    /// <summary>
    /// The colours of the custom sky-gradient shader (ARO/SkyGradient) for an hour of day and an overcast amount, derived from <see cref="LightingModel"/> so the sky,
    /// sun and ambient light stay in step. It exists because Unity's procedural sky ties its blue channel to the sun colour: a thicker atmosphere warms the horizon but
    /// also turns the zenith pale grey-green (measured: zenith saturation 0.15 to 0.19 at dawn and sunset). Here horizon, mid sky and zenith are separate colours, so a
    /// gold horizon can sit under a clean blue sky. Pure and smooth in the hour; the renderer only copies these numbers into the material.
    /// </summary>
    public static class SkyGradientModel
    {
        static float Smooth(float t) { t = t < 0f ? 0f : t > 1f ? 1f : t; return t * t * (3f - 2f * t); }
        static float Clamp01(float v) => float.IsNaN(v) ? 0f : v < 0f ? 0f : v > 1f ? 1f : v;

        // The camera of the chase view only sees the sky from the horizon to about 25 degrees up, so what reads as "the sky" is the horizon band and the MID stop; the zenith colour is hardly ever on screen.
        // The palette is therefore tuned for those two, with the displayed result predicted by ColourAt and checked against CI frames (see docs/REALISM_AUDIT.md, Appendix C and E).
        // ---- Palette 3: the iteration-3 palette that met the first set of criteria; kept as the control of the Milestone 3 comparison. Strongly saturated, no haze.
        static readonly Rgb DayZ3 = new Rgb(0.16f, 0.40f, 0.84f), DayM3 = new Rgb(0.36f, 0.62f, 0.95f), DayH3 = new Rgb(0.74f, 0.84f, 0.93f);
        static readonly Rgb DawnZ3 = new Rgb(0.18f, 0.34f, 0.74f), DawnM3 = new Rgb(0.36f, 0.56f, 0.92f), DawnH3 = new Rgb(1.00f, 0.62f, 0.38f);
        static readonly Rgb DuskZ3 = new Rgb(0.12f, 0.28f, 0.66f), DuskM3 = new Rgb(0.34f, 0.54f, 0.90f), DuskH3 = new Rgb(1.00f, 0.50f, 0.16f);
        // ---- Palette 4 (default): less saturated blues, a softer warm band, and a pale haze right at the horizon so the sky does not stop on a hard bright edge against the terrain.
        static readonly Rgb DayZ4 = new Rgb(0.20f, 0.44f, 0.82f), DayM4 = new Rgb(0.46f, 0.66f, 0.92f), DayB4 = new Rgb(0.74f, 0.84f, 0.93f), DayHz4 = new Rgb(0.88f, 0.92f, 0.95f);
        static readonly Rgb DawnZ4 = new Rgb(0.24f, 0.38f, 0.72f), DawnM4 = new Rgb(0.47f, 0.61f, 0.88f), DawnB4 = new Rgb(1.00f, 0.66f, 0.46f), DawnHz4 = new Rgb(1.00f, 0.86f, 0.76f);
        static readonly Rgb DuskZ4 = new Rgb(0.16f, 0.32f, 0.68f), DuskM4 = new Rgb(0.45f, 0.59f, 0.86f), DuskB4 = new Rgb(1.00f, 0.54f, 0.22f), DuskHz4 = new Rgb(1.00f, 0.82f, 0.62f);
        // Night: dark blue, a little lighter at the horizon (the night rendering keeps the sky readable; the old sky's night mean was about 57/255).
        static readonly Rgb NightZ = new Rgb(0.07f, 0.11f, 0.26f), NightM = new Rgb(0.13f, 0.19f, 0.36f), NightH = new Rgb(0.20f, 0.28f, 0.45f), NightHz = new Rgb(0.22f, 0.30f, 0.46f);
        static readonly Rgb Overcast = new Rgb(0.62f, 0.65f, 0.68f);


        /// <summary>The fraction of the model's colour saturation that survives to the screen. Fitted (not assumed) to CI frames: first to eight measurements (65%), then corrected to 72% after iteration 3 measured about 10% more saturation than that fit predicted.</summary>
        public const float DisplaySaturation = 0.72f;

        static float SmoothStep(float t) { t = t < 0f ? 0f : t > 1f ? 1f : t; return t * t * (3f - 2f * t); }

        /// <summary>The sky colour in a direction whose sine of elevation is <paramref name="sinElevation"/>: the same arithmetic as the shader (haze, horizon band, mid sky, zenith), before any tonemap.</summary>
        public static Rgb ColourAt(SkyGradientState s, float sinElevation)
        {
            float h = (float)Math.Pow(Clamp01(sinElevation), s.HeightExp);
            Rgb c;
            if (s.LowHeight > 0.0001f && h < s.LowHeight) c = Rgb.Lerp(s.Haze, s.Horizon, SmoothStep(h / s.LowHeight));
            else
            {
                float from = s.LowHeight;
                c = h < s.MidHeight ? Rgb.Lerp(s.Horizon, s.Mid, SmoothStep((h - from) / Math.Max(s.MidHeight - from, 0.001f)))
                                    : Rgb.Lerp(s.Mid, s.Zenith, SmoothStep((h - s.MidHeight) / Math.Max(1f - s.MidHeight, 0.001f)));
            }
            if (s.Dip > 0.0001f) c = c * (1f - s.Dip * (1f - SmoothStep(h / Math.Max(s.DipHeight, 0.001f))));
            return c;
        }

        /// <summary>What a colour is expected to look like on screen: its saturation scaled by <see cref="DisplaySaturation"/> around its own luma.</summary>
        public static Rgb Displayed(Rgb c)
        {
            float l = 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B, k = DisplaySaturation;
            return new Rgb(l + (c.R - l) * k, l + (c.G - l) * k, l + (c.B - l) * k);
        }

        public const int DefaultPalette = 4;
        public const float DefaultHazeMix = 0.6f;

        public static SkyGradientState Evaluate(LightingState st) => Evaluate(st, DefaultPalette, DefaultHazeMix, SkyTune.None);

        /// <param name="palette">3 = the iteration-3 palette (the control), 4 = the current default.</param>
        /// <param name="hazeMix">0 = no haze (the horizon colour runs right down to the horizon), 1 = the full pale haze colour exactly at the horizon (palette 4 only).</param>
        public static SkyGradientState Evaluate(LightingState st, int palette, float hazeMix) => Evaluate(st, palette, hazeMix, SkyTune.None);

        public static SkyGradientState Evaluate(LightingState st, int palette, float hazeMix, SkyTune tune)
        {
            float oc = Clamp01(st.OvercastIn), day = Clamp01(st.Day), golden = Clamp01(st.Golden); hazeMix = Clamp01(hazeMix);
            float ang = (st.Hour - 6f) / 12f * (float)Math.PI;
            float dawnW = Smooth(0.5f + 0.5f * (float)Math.Cos(ang));            // 1 in the morning, 0 in the evening, continuous through noon and midnight (same as LightingModel)
            // The golden weight peaks at about 0.8 on a clear day at the reference hours (6.4 and 17.6); scaled up so the golden palette fully replaces the day palette there instead of leaving a fifth of it in.
            float gDawn = Clamp01(1.25f * golden * dawnW), gDusk = Clamp01(1.25f * golden * (1f - dawnW));

            Rgb Pick(Rgb night, Rgb dayC, Rgb dawnC, Rgb duskC)
            {
                var c = Rgb.Lerp(night, dayC, day);
                c = Rgb.Lerp(c, dawnC, gDawn);
                c = Rgb.Lerp(c, duskC, gDusk);
                // Cloud: toward a flat grey, darker as the light falls.
                var grey = Overcast * (0.30f + 0.70f * day);
                return Rgb.Lerp(c, grey, 0.75f * oc);
            }

            SkyGradientState s;
            if (palette == 3)
                s = new SkyGradientState
                {
                    Zenith = Pick(NightZ, DayZ3, DawnZ3, DuskZ3), Mid = Pick(NightM, DayM3, DawnM3, DuskM3), Horizon = Pick(NightH, DayH3, DawnH3, DuskH3),
                    LowHeight = 0f, MidHeight = 0.22f
                };
            else
            {
                var band = Pick(NightH, DayB4, DawnB4, DuskB4); var haze = Pick(NightHz, DayHz4, DawnHz4, DuskHz4);
                s = new SkyGradientState
                {
                    Zenith = Pick(NightZ, DayZ4, DawnZ4, DuskZ4), Mid = Pick(NightM, DayM4, DawnM4, DuskM4), Horizon = band, Haze = Rgb.Lerp(band, haze, hazeMix),
                    LowHeight = 0.07f, MidHeight = 0.24f
                };
            }
            if (palette == 3) s.Haze = s.Horizon;
            s.HeightExp = 1f;
            float dim = tune.BandDim <= 0f ? 1f : Clamp01(tune.BandDim);          // 0 / unset means neutral
            s.Horizon = s.Horizon * dim; s.Haze = s.Haze * dim;
            s.Dip = Clamp01(tune.Dip); s.DipHeight = tune.DipHeight > 0f ? tune.DipHeight : 0.10f;
            s.Ground = Rgb.Lerp(new Rgb(0.04f, 0.05f, 0.08f), new Rgb(0.45f, 0.47f, 0.46f), day);
            s.Ground = Rgb.Lerp(s.Ground, st.FogColor, 0.35f);
            s.Glow = st.SunColor;
            float sunUp = st.SunEnabled ? Smooth((st.Elevation + 0.05f) / 0.15f) : 0f;
            s.GlowStrength = sunUp * (0.10f + 0.50f * golden) * (1f - 0.8f * oc);
            s.GlowPower = 6f;
            s.DiscStrength = sunUp * (1f - oc) * (0.60f + 0.40f * golden);
            return s;
        }
    }
}
