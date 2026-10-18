using System;

namespace ARO.NetCore
{
    /// <summary>What the gradient sky is painted with at one moment. Colours are what the sky should look like on screen (before the final tonemap), not physical radiance.</summary>
    public sealed class SkyGradientState
    {
        public Rgb Zenith, Mid, Horizon, Ground, Glow;
        /// <summary>Strength of the wide glow around the sun (0 at night), its tightness (higher = smaller) and the brightness of the sun disc.</summary>
        public float GlowStrength, GlowPower, DiscStrength;
        /// <summary>Where the mid colour sits (as the sine of the elevation: 0.22 is about 13 degrees) and the curve of the climb to the zenith (1 = linear in that sine). A low mid stop keeps the warm horizon band wide enough to be seen from a chase camera that looks down the road.</summary>
        public float MidHeight, HeightExp;
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
        // The palette is therefore tuned for those two, with the displayed result predicted by ColourAt and checked against CI frames (see docs/REALISM_AUDIT.md, Appendix C).
        // Clear day.
        static readonly Rgb DayZ = new Rgb(0.16f, 0.40f, 0.84f), DayM = new Rgb(0.36f, 0.62f, 0.95f), DayH = new Rgb(0.74f, 0.84f, 0.93f);
        // Dawn golden hour: a clear blue sky over a peach-pink horizon.
        static readonly Rgb DawnZ = new Rgb(0.18f, 0.34f, 0.74f), DawnM = new Rgb(0.36f, 0.56f, 0.92f), DawnH = new Rgb(1.00f, 0.62f, 0.38f);
        // Sunset golden hour: a deeper blue sky over a gold-orange horizon.
        static readonly Rgb DuskZ = new Rgb(0.12f, 0.28f, 0.66f), DuskM = new Rgb(0.34f, 0.54f, 0.90f), DuskH = new Rgb(1.00f, 0.50f, 0.16f);
        // Night: dark blue, a little lighter at the horizon (the night rendering keeps the sky readable; the old sky's night mean was about 57/255).
        static readonly Rgb NightZ = new Rgb(0.07f, 0.11f, 0.26f), NightM = new Rgb(0.13f, 0.19f, 0.36f), NightH = new Rgb(0.20f, 0.28f, 0.45f);
        static readonly Rgb Overcast = new Rgb(0.62f, 0.65f, 0.68f);


        /// <summary>The fraction of the model's colour saturation that survives to the screen. Fitted (not assumed) to eight measurements of CI frames: the final tonemap and the display pipeline desaturate the sky to about 65%.</summary>
        public const float DisplaySaturation = 0.65f;

        static float SmoothStep(float t) { t = t < 0f ? 0f : t > 1f ? 1f : t; return t * t * (3f - 2f * t); }

        /// <summary>The sky colour in a direction whose sine of elevation is <paramref name="sinElevation"/>: the same arithmetic as the shader (horizon to mid to zenith), before any tonemap.</summary>
        public static Rgb ColourAt(SkyGradientState s, float sinElevation)
        {
            float h = (float)Math.Pow(Clamp01(sinElevation), s.HeightExp);
            return h < s.MidHeight ? Rgb.Lerp(s.Horizon, s.Mid, SmoothStep(h / Math.Max(s.MidHeight, 0.001f)))
                                   : Rgb.Lerp(s.Mid, s.Zenith, SmoothStep((h - s.MidHeight) / Math.Max(1f - s.MidHeight, 0.001f)));
        }

        /// <summary>What a colour is expected to look like on screen: its saturation scaled by <see cref="DisplaySaturation"/> around its own luma.</summary>
        public static Rgb Displayed(Rgb c)
        {
            float l = 0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B, k = DisplaySaturation;
            return new Rgb(l + (c.R - l) * k, l + (c.G - l) * k, l + (c.B - l) * k);
        }

        public static SkyGradientState Evaluate(LightingState st)
        {
            float oc = Clamp01(st.OvercastIn), day = Clamp01(st.Day), golden = Clamp01(st.Golden);
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

            var s = new SkyGradientState
            {
                Zenith = Pick(NightZ, DayZ, DawnZ, DuskZ),
                Mid = Pick(NightM, DayM, DawnM, DuskM),
                Horizon = Pick(NightH, DayH, DawnH, DuskH),
                Ground = Rgb.Lerp(new Rgb(0.04f, 0.05f, 0.08f), new Rgb(0.45f, 0.47f, 0.46f), day),
            };
            s.Ground = Rgb.Lerp(s.Ground, st.FogColor, 0.35f);
            s.Glow = st.SunColor;
            float sunUp = st.SunEnabled ? Smooth((st.Elevation + 0.05f) / 0.15f) : 0f;
            s.GlowStrength = sunUp * (0.10f + 0.50f * golden) * (1f - 0.8f * oc);
            s.GlowPower = 6f; s.MidHeight = 0.22f; s.HeightExp = 1f;
            s.DiscStrength = sunUp * (1f - oc) * (0.60f + 0.40f * golden);
            return s;
        }
    }
}
