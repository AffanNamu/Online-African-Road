using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ARO.NetCore
{
    // Everything the dashboard needs to DECIDE (what status, which milestone, how long, what to animate), separated from how it is drawn.
    // Pure and unit-tested; the Unity layer only draws the results.

    public enum TruckState { NoVehicle, Ready, LowFuel, NeedsRepair, OnJob }

    public static class Vitals
    {
        public static float FuelPercent(double fuelL, double capacityL) => capacityL <= 0 ? 0f : (float)Math.Max(0.0, Math.Min(100.0, fuelL / capacityL * 100.0));
        public static float ConditionPercent(double damagePct) => (float)Math.Max(0.0, Math.Min(100.0, 100.0 - damagePct));

        public static TruckState Status(bool hasVehicle, double fuelPercent, double damagePercent, bool onJob)
        {
            if (!hasVehicle) return TruckState.NoVehicle;
            if (damagePercent >= 70.0) return TruckState.NeedsRepair;       // a wrecked truck needs the garage whatever it is doing
            if (onJob) return TruckState.OnJob;
            if (fuelPercent < 10.0) return TruckState.LowFuel;
            return TruckState.Ready;
        }

        public static string Label(TruckState s) => s == TruckState.Ready ? "Ready" : s == TruckState.LowFuel ? "Low fuel" : s == TruckState.NeedsRepair ? "Needs repair" : s == TruckState.OnJob ? "On a job" : "No vehicle";
    }

    public static class JobMath
    {
        public const double AverageKmh = 50.0;      // planning speed for an estimate, not a promise

        public static int EtaMinutes(double distanceKm) => distanceKm <= 0 ? 0 : (int)Math.Max(1, Math.Round(distanceKm / AverageKmh * 60.0));

        public static string FormatDuration(int minutes)
        {
            if (minutes < 60) return minutes + " min";
            int h = minutes / 60, m = minutes % 60;
            return m == 0 ? h + "h" : h + "h " + m.ToString("00", CultureInfo.InvariantCulture) + "m";
        }

        public static string Difficulty(int d) => d <= 2 ? "Easy" : d == 3 ? "Medium" : "Hard";
    }

    public static class Money
    {
        public static string Format(long v) => v.ToString("N0", CultureInfo.InvariantCulture);
        public static string Format(double v) => Format((long)Math.Round(v));
    }

    public struct MilestoneInfo { public double Target, Current; public float Fraction; public string Title; }

    /// <summary>A real, always-available goal derived from the player's lifetime distance (there is no daily-challenge backend yet).</summary>
    public static class Milestones
    {
        public static readonly double[] Ladder = { 50, 100, 250, 500, 1000, 2500, 5000, 10000, 25000 };

        public static MilestoneInfo Next(double distanceKm)
        {
            distanceKm = Math.Max(0, distanceKm);
            double target = Ladder.FirstOrDefault(r => r > distanceKm);
            if (target == 0) target = (Math.Floor(distanceKm / 25000.0) + 1) * 25000.0;
            return new MilestoneInfo { Target = target, Current = distanceKm, Fraction = (float)Math.Min(1.0, distanceKm / target), Title = "Drive " + Money.Format(target) + " km" };
        }
    }

    // ------------------------------------------------------------------------------------------------------------------ news
    [Serializable] public class NewsItem { public string id, title, body, date, art, kind; }
    [Serializable] public class NewsFeedSpec { public int schemaVersion = 1; public NewsItem[] items; }

    public static class NewsFeed
    {
        public static readonly string[] Kinds = { "update", "event", "tip" };

        public static List<string> Validate(NewsFeedSpec f, ISet<string> knownArt = null)
        {
            var e = new List<string>();
            if (f == null) { e.Add("news feed is null"); return e; }
            if (f.schemaVersion != 1) e.Add("schemaVersion must be 1");
            if (f.items == null) { e.Add("items is required"); return e; }
            var ids = new HashSet<string>();
            foreach (var n in f.items)
            {
                if (n == null) { e.Add("a news item is null"); continue; }
                string tag = "news '" + n.id + "'";
                if (string.IsNullOrEmpty(n.id) || !ids.Add(n.id)) e.Add(tag + ": id missing or duplicate");
                if (string.IsNullOrWhiteSpace(n.title) || n.title.Length > 80) e.Add(tag + ": title must be 1..80 characters");
                if (string.IsNullOrWhiteSpace(n.body) || n.body.Length > 220) e.Add(tag + ": body must be 1..220 characters");
                if (!DateTime.TryParseExact(n.date ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) e.Add(tag + ": date must be yyyy-MM-dd");
                if (!Kinds.Contains(n.kind)) e.Add(tag + ": kind must be one of " + string.Join(", ", Kinds));
                if (knownArt != null && !string.IsNullOrEmpty(n.art) && !knownArt.Contains(n.art)) e.Add(tag + ": unknown art '" + n.art + "'");
            }
            return e;
        }

        /// <summary>Newest first. Invalid dates sort last.</summary>
        public static List<NewsItem> Latest(NewsFeedSpec f, int count)
        {
            if (f == null || f.items == null) return new List<NewsItem>();
            DateTime D(NewsItem n) => DateTime.TryParseExact(n.date ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.MinValue;
            return f.items.Where(n => n != null).OrderByDescending(D).Take(count).ToList();
        }
    }

    // ------------------------------------------------------------------------------------------------------------------ notices (the bell)
    public enum NoticeLevel { Info, Good, Bad }
    public sealed class Notice { public string Text; public NoticeLevel Level; public double At; public int Count = 1; public bool Read; }

    /// <summary>In-session events the player was told about (server messages, payouts, errors). Repeats coalesce so a retry loop cannot flood it.</summary>
    public sealed class NoticeLog
    {
        readonly List<Notice> _items = new List<Notice>(); readonly int _capacity;
        public NoticeLog(int capacity = 20) { _capacity = Math.Max(1, capacity); }
        public int Unread => _items.Where(n => !n.Read).Sum(n => 1);
        public int Count => _items.Count;

        public void Add(string text, NoticeLevel level, double atSeconds)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var last = _items.Count > 0 ? _items[_items.Count - 1] : null;
            if (last != null && last.Text == text && atSeconds - last.At < 30.0) { last.Count++; last.At = atSeconds; last.Read = false; return; }
            _items.Add(new Notice { Text = text.Trim(), Level = level, At = atSeconds });
            if (_items.Count > _capacity) _items.RemoveAt(0);
        }

        public void MarkAllRead() { foreach (var n in _items) n.Read = true; }
        public List<Notice> Recent(int count) => _items.AsEnumerable().Reverse().Take(count).ToList();
    }

    // ------------------------------------------------------------------------------------------------------------------ presence / people
    public static class Presence
    {
        public static string Describe(bool signedIn, int convoyMembers) => !signedIn ? "Offline" : convoyMembers > 1 ? "Convoy · " + convoyMembers : "Online";
    }

    [Serializable] public class RecentEntry { public string name; public long seenAt; }
    [Serializable] public class RecentPlayersData { public RecentEntry[] entries = new RecentEntry[0]; }

    /// <summary>People you have shared a convoy with. Stored on this device only (no friends backend yet): the newest sighting wins, you are never listed, the list is capped.</summary>
    public static class Recents
    {
        public static RecentEntry[] Merge(RecentEntry[] existing, IEnumerable<string> presentNow, string self, long nowUnix, int cap = 8)
        {
            var map = new Dictionary<string, RecentEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in existing ?? new RecentEntry[0]) if (e != null && !string.IsNullOrWhiteSpace(e.name)) map[e.name.Trim()] = new RecentEntry { name = e.name.Trim(), seenAt = e.seenAt };
            foreach (var n in presentNow ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(n)) continue; string name = n.Trim();
                if (string.Equals(name, self?.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                map[name] = new RecentEntry { name = name, seenAt = nowUnix };
            }
            return map.Values.OrderByDescending(e => e.seenAt).ThenBy(e => e.name, StringComparer.OrdinalIgnoreCase).Take(Math.Max(1, cap)).ToArray();
        }

        /// <summary>"Online now" for people in the current session, otherwise how long ago they were seen.</summary>
        public static string Status(RecentEntry e, ISet<string> presentNow, long nowUnix)
        {
            if (presentNow != null && presentNow.Contains(e.name)) return "In convoy";
            long d = Math.Max(0, nowUnix - e.seenAt);
            if (d < 3600) return Math.Max(1, d / 60) + " min ago";
            if (d < 86400) return d / 3600 + " h ago";
            return d / 86400 + " d ago";
        }
    }

    public static class People
    {
        public static string Initials(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "?";
            var parts = name.Trim().Split(new[] { ' ', '_', '-', '.' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            string s = parts.Length == 1 ? parts[0].Substring(0, 1) : parts[0].Substring(0, 1) + parts[1].Substring(0, 1);
            return s.ToUpperInvariant();
        }

        /// <summary>Stable avatar colour for a name (HSV with fixed saturation/value, so every colour is legible on the dark UI).</summary>
        public static void AvatarRgb(string name, out float r, out float g, out float b)
        {
            float h = (Mathx.Hash01(Mathx.StableHash(name ?? ""), 7) * 360f) / 60f, c = 0.55f * 0.78f, x = c * (1f - Math.Abs(h % 2f - 1f)), m = 0.78f - c;
            float rr = 0, gg = 0, bb = 0;
            switch ((int)Math.Floor(h) % 6) { case 0: rr = c; gg = x; break; case 1: rr = x; gg = c; break; case 2: gg = c; bb = x; break; case 3: gg = x; bb = c; break; case 4: rr = x; bb = c; break; default: rr = c; bb = x; break; }
            r = rr + m; g = gg + m; b = bb + m;
        }
    }

    // ------------------------------------------------------------------------------------------------------------------ motion
    public static class Ease
    {
        public static float OutCubic(float t) { t = Mathx.Clamp01(t); float u = 1f - t; return 1f - u * u * u; }
        public static float OutBack(float t) { t = Mathx.Clamp01(t); const float c1 = 1.70158f, c3 = c1 + 1f; float u = t - 1f; return 1f + c3 * u * u * u + c1 * u * u; }
        public static float InOutSine(float t) => -((float)Math.Cos(Math.PI * Mathx.Clamp01(t)) - 1f) / 2f;
    }

    public static class Tween
    {
        /// <summary>0..1 progress of an animation that starts after `delay` and lasts `duration`.</summary>
        public static float Progress(float time, float delay, float duration) => duration <= 0f ? 1f : Mathx.Clamp01((time - delay) / duration);
        public static float StaggerDelay(int index, float step, float max) => Math.Min(max, Math.Max(0, index) * step);
        public static long CountUp(long from, long to, float progress) => from + (long)Math.Round((to - from) * Ease.OutCubic(progress));
        /// <summary>Smooth 0..1 oscillation (bell pulse, player marker ring).</summary>
        public static float Pulse(float time, float period) => period <= 0f ? 1f : 0.5f + 0.5f * (float)Math.Sin(2.0 * Math.PI * time / period);
        /// <summary>Skeleton alpha: every placeholder breathes slightly out of phase with its neighbour.</summary>
        public static float SkeletonAlpha(float time, int index) => 0.30f + 0.22f * Pulse(time + index * 0.18f, 1.3f);

        /// <summary>
        /// Slow zoom-and-drift over a picture ("Ken Burns"). `baseUv` is the crop to show at rest; the result always stays inside the 0..1 texture,
        /// zoom never goes below 1 and the motion is continuous. Returns x, y, width, height in UV space.
        /// </summary>
        public static void KenBurns(float time, float duration, float baseX, float baseY, float baseW, float baseH, float zoomEnd, float driftX, float driftY, out float x, out float y, out float w, out float h)
        {
            float t = Ease.InOutSine(duration <= 0f ? 0f : (time % (duration * 2f) <= duration ? (time % (duration * 2f)) / duration : 2f - (time % (duration * 2f)) / duration));
            float zoom = Mathx.Lerp(1f, Math.Max(1f, zoomEnd), t);
            w = baseW / zoom; h = baseH / zoom;
            float cx = baseX + baseW * 0.5f + driftX * (t - 0.5f), cy = baseY + baseH * 0.5f + driftY * (t - 0.5f);
            x = Mathx.Clamp(cx - w * 0.5f, 0f, 1f - w); y = Mathx.Clamp(cy - h * 0.5f, 0f, 1f - h);
        }
    }
}
