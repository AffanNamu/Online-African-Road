using System;

namespace ARO.NetCore
{
    /// <summary>What the gradient sky is painted with at one moment. Colours are what the sky should look like on screen (before the final tonemap), not physical radiance.</summary>
    public sealed class SkyGradientState
    {
        public Rgb Zenith, Mid, Horizon, Ground, Glow;
        /// <summary>Strength of the wide glow around the sun (0 at night), its tightness (higher = smaller) and the brightness of the sun disc.</summary>
        public float GlowStrength, GlowPower, DiscStrength;
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

        // Clear day.
        static readonly Rgb DayZ = new Rgb(0.20f, 0.44f, 0.82f), DayM = new Rgb(0.50f, 0.68f, 0.88f), DayH = new Rgb(0.72f, 0.83f, 0.92f);
        // Dawn golden hour: a cool blue sky over a peach-pink horizon.
        static readonly Rgb DawnZ = new Rgb(0.26f, 0.40f, 0.72f), DawnM = new Rgb(0.66f, 0.66f, 0.80f), DawnH = new Rgb(1.00f, 0.74f, 0.58f);
        // Sunset golden hour: a deeper blue sky over a gold-orange horizon.
        static readonly Rgb DuskZ = new Rgb(0.17f, 0.34f, 0.68f), DuskM = new Rgb(0.55f, 0.62f, 0.78f), DuskH = new Rgb(1.00f, 0.60f, 0.26f);
        // Night: dark blue, a little lighter at the horizon (the night rendering keeps the sky readable; the old sky's night mean was about 57/255).
        static readonly Rgb NightZ = new Rgb(0.07f, 0.11f, 0.26f), NightM = new Rgb(0.13f, 0.19f, 0.36f), NightH = new Rgb(0.20f, 0.28f, 0.45f);
        static readonly Rgb Overcast = new Rgb(0.62f, 0.65f, 0.68f);

        public static SkyGradientState Evaluate(LightingState st)
        {
            float oc = Clamp01(st.OvercastIn), day = Clamp01(st.Day), golden = Clamp01(st.Golden);
            float ang = (st.Hour - 6f) / 12f * (float)Math.PI;
            float dawnW = Smooth(0.5f + 0.5f * (float)Math.Cos(ang));            // 1 in the morning, 0 in the evening, continuous through noon and midnight (same as LightingModel)
            float gDawn = golden * dawnW, gDusk = golden * (1f - dawnW);

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
            s.GlowPower = 6f;
            s.DiscStrength = sunUp * (1f - oc) * (0.60f + 0.40f * golden);
            return s;
        }
    }
}
