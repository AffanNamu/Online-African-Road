using System;
using System.Collections.Generic;
using System.Globalization;

namespace ARO.NetCore
{
    /// <summary>Speedometer-style gauge mapping.</summary>
    public static class Gauge
    {
        public static float Fraction(float value, float min, float max)
        {
            if (float.IsNaN(value) || max <= min) return 0f;
            return Math.Max(0f, Math.Min(1f, (value - min) / (max - min)));
        }

        /// <summary>Needle angle in degrees, interpolated between start and end (e.g. 225 -> -45 sweeps clockwise).</summary>
        public static float Angle(float value, float min, float max, float startDeg, float endDeg) =>
            startDeg + (endDeg - startDeg) * Fraction(value, min, max);
    }

    /// <summary>Player-facing number formatting (invariant culture so UI text is identical everywhere).</summary>
    public static class TripFormat
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Distance(float metres)
        {
            if (float.IsNaN(metres) || metres < 0) metres = 0;
            if (metres < 1000f)
            {
                int tens = (int)(Math.Round(metres / 10f, MidpointRounding.AwayFromZero) * 10.0);   // nearest 10 m
                return tens < 1000 ? tens + " m" : "1.0 km";
            }
            if (metres < 10000f) return (metres / 1000f).ToString("0.0", Inv) + " km";
            return Math.Round(metres / 1000f, MidpointRounding.AwayFromZero).ToString("0", Inv) + " km";
        }

        public static string Eta(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0) return "--";
            if (seconds < 60f) return "< 1 min";
            int mins = (int)Math.Round(seconds / 60f, MidpointRounding.AwayFromZero);
            if (mins < 60) return mins + " min";
            return (mins / 60) + "h " + (mins % 60).ToString("00", Inv) + "m";
        }

        public static string Money(long amount) => amount.ToString("N0", Inv);
    }

    public static class EtaEstimator
    {
        /// <summary>Blend of current and typical speed so the ETA does not jump when stopped at lights or pulling away.</summary>
        public static float Seconds(float distanceMetres, float currentSpeedMps, float typicalSpeedMps = 18f)
        {
            if (distanceMetres <= 0f) return 0f;
            float eff = 0.7f * typicalSpeedMps + 0.3f * Math.Max(0f, currentSpeedMps);
            return distanceMetres / Math.Max(3f, eff);
        }
    }

    public static class MinimapMath
    {
        /// <summary>
        /// World (x,z) to minimap coordinates in [-1,1] (up = ahead when rotating). Returns false if outside the radius,
        /// so callers can clamp the marker to the edge instead.
        /// </summary>
        public static bool ToMap(float wx, float wz, float cx, float cz, float headingDeg, float radiusMetres, bool rotate, out float mx, out float my)
        {
            float dx = wx - cx, dz = wz - cz;
            if (rotate)
            {
                // heading 0 = +Z (north). Rotate world so the vehicle's forward points up on the map.
                double r = headingDeg * Math.PI / 180.0, c = Math.Cos(r), s = Math.Sin(r);
                float rx = (float)(dx * c - dz * s), rz = (float)(dx * s + dz * c);
                dx = rx; dz = rz;
            }
            mx = dx / radiusMetres; my = dz / radiusMetres;
            return mx * mx + my * my <= 1f;
        }

        public static void ClampToEdge(ref float mx, ref float my, float margin = 0.92f)
        {
            float len = (float)Math.Sqrt(mx * mx + my * my);
            if (len > margin && len > 0f) { mx = mx / len * margin; my = my / len * margin; }
        }
    }

    public enum Severity { Info, Success, Warning, Error }

    public sealed class Notification
    {
        public string Text { get; internal set; }
        public Severity Level { get; internal set; }
        public float Remaining { get; internal set; }
    }

    /// <summary>Toast queue: caps visible items, de-duplicates identical text (refreshing its timer), expires by time.</summary>
    public sealed class NotificationQueue
    {
        readonly List<Notification> _items = new List<Notification>();
        public int MaxVisible { get; }
        public NotificationQueue(int maxVisible = 3) { MaxVisible = Math.Max(1, maxVisible); }

        public IReadOnlyList<Notification> Visible => _items;
        public int Count => _items.Count;

        public void Push(string text, Severity level = Severity.Info, float ttlSeconds = 4f)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            foreach (var n in _items)
                if (n.Text == text) { n.Remaining = ttlSeconds; n.Level = level; return; }
            _items.Add(new Notification { Text = text, Level = level, Remaining = ttlSeconds });
            while (_items.Count > MaxVisible) _items.RemoveAt(0);   // drop the oldest
        }

        public void Tick(float dt)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                _items[i].Remaining -= dt;
                if (_items[i].Remaining <= 0f) _items.RemoveAt(i);
            }
        }
    }
}
