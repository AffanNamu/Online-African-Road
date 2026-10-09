using System;

namespace ARO.NetCore
{
    /// <summary>Compact quantisation of the vehicle state replicated to other players (bandwidth: 5 bytes + transform).</summary>
    public static class VehicleNetCodec
    {
        public const byte Lights = 1, IndicatorLeft = 2, IndicatorRight = 4, Horn = 8, Brake = 16, Reverse = 32;

        public static byte PackFlags(bool lights, int indicator, bool horn, bool brake, bool reverse)
        {
            int f = 0;
            if (lights) f |= Lights;
            if (indicator < 0) f |= IndicatorLeft; else if (indicator > 0) f |= IndicatorRight;
            if (horn) f |= Horn;
            if (brake) f |= Brake;
            if (reverse) f |= Reverse;
            return (byte)f;
        }
        public static bool Has(byte flags, byte bit) => (flags & bit) != 0;
        public static int IndicatorOf(byte flags) => Has(flags, IndicatorLeft) ? -1 : Has(flags, IndicatorRight) ? 1 : 0;

        /// <summary>-1..1 to sbyte. Out-of-range and NaN are clamped (a hostile peer must not be able to inject garbage).</summary>
        public static sbyte QuantizeSteer(float v)
        {
            if (float.IsNaN(v)) return 0;
            return (sbyte)Math.Round(Math.Max(-1f, Math.Min(1f, v)) * 127f);
        }
        public static float DequantizeSteer(sbyte q) => Math.Max(-1f, Math.Min(1f, q / 127f));

        /// <summary>km/h to 0.1 km/h units in a ushort.</summary>
        public static ushort QuantizeSpeed(float kmh)
        {
            if (float.IsNaN(kmh) || kmh < 0) return 0;
            return (ushort)Math.Min(65535, Math.Round(kmh * 10f));
        }
        public static float DequantizeSpeed(ushort q) => q / 10f;

        /// <summary>0..100 percent to a byte (0.4 % resolution).</summary>
        public static byte QuantizeDamage(float pct)
        {
            if (float.IsNaN(pct)) return 0;
            return (byte)Math.Round(Math.Max(0f, Math.Min(100f, pct)) * 2.5f);
        }
        public static float DequantizeDamage(byte q) => Math.Min(100f, q / 2.5f);
    }
}
