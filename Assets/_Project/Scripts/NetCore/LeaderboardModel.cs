using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ARO.NetCore
{
    // ===================================================================================================== Leaderboards: pure rules (no Unity types, unit tested)
    /// <summary>The boards the backend can rank: xp, earnings (lifetime money), distance and completed jobs. There is no multiplayer or "overall" metric on the server, so there is none here.</summary>
    public enum BoardMetric { Xp, Earnings, Distance, Jobs }
    /// <summary>The server has "all" and "weekly". Months and seasons do not exist there, so they do not exist here.</summary>
    public enum BoardPeriod { AllTime, Week }
    public enum BoardScope { Global, Friends }

    /// <summary>One ranked player as the screen shows it. Filled from the server's row; the rank and the value are the server's.</summary>
    public sealed class BoardRow
    {
        public int Rank; public string Id = "", Name = "", Username = "", Country = ""; public int Level; public double Value;
    }

    public static class LeaderboardRules
    {
        public const int PageSize = 12;

        public static string MetricKey(BoardMetric m) => m == BoardMetric.Earnings ? "earnings" : m == BoardMetric.Distance ? "distance" : m == BoardMetric.Jobs ? "jobs" : "xp";
        public static string PeriodKey(BoardPeriod p) => p == BoardPeriod.Week ? "weekly" : "all";
        public static string MetricLabel(BoardMetric m) => m == BoardMetric.Earnings ? "Earnings" : m == BoardMetric.Distance ? "Distance" : m == BoardMetric.Jobs ? "Jobs" : "XP";
        public static string PeriodLabel(BoardPeriod p) => p == BoardPeriod.Week ? "This Week" : "All Time";
        public static string ColumnTitle(BoardMetric m) => m == BoardMetric.Earnings ? "Earnings" : m == BoardMetric.Distance ? "Distance" : m == BoardMetric.Jobs ? "Jobs completed" : "Score (XP)";

        // ---------------------------------------------------------------- periods
        /// <summary>XP is not logged per event, so there is no weekly XP board; every other board has one.</summary>
        public static bool PeriodSupported(BoardMetric m, BoardPeriod p) => !(m == BoardMetric.Xp && p == BoardPeriod.Week);
        /// <summary>What is really asked of the server: an unsupported weekly XP falls back to all time (and says so, see <see cref="PeriodNote"/>).</summary>
        public static BoardPeriod EffectivePeriod(BoardMetric m, BoardPeriod p) => PeriodSupported(m, p) ? p : BoardPeriod.AllTime;
        public static string PeriodNote(BoardMetric m, BoardPeriod p) => PeriodSupported(m, p) ? null : "XP is not tracked per week, so this shows all time.";

        // ---------------------------------------------------------------- numbers
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        /// <summary>The number without its unit (table cells; the column header carries the unit).</summary>
        public static string FormatBare(BoardMetric m, double v)
        {
            double a = Math.Max(0, v);
            switch (m)
            {
                case BoardMetric.Earnings: return "₦ " + a.ToString("N0", Inv);
                case BoardMetric.Distance: return (a >= 100 ? a.ToString("N0", Inv) : a.ToString("N1", Inv)) + " km";
                default: return a.ToString("N0", Inv);
            }
        }
        /// <summary>The number with its unit (podium cards, your rank).</summary>
        public static string FormatValue(BoardMetric m, double v) => m == BoardMetric.Xp ? Math.Max(0, v).ToString("N0", Inv) + " XP" : m == BoardMetric.Jobs ? Math.Max(0, v).ToString("N0", Inv) + (Math.Round(v) == 1 ? " job" : " jobs") : FormatBare(m, v);

        public static string RankText(int rank) => rank > 0 ? "#" + rank.ToString("N0", Inv) : "Unranked";
        /// <summary>1, 2, 3 for the podium medals; 0 for everyone else.</summary>
        public static int MedalOf(int rank) => rank >= 1 && rank <= 3 ? rank : 0;
        /// <summary>Why the player has no rank on a board (the server leaves the rank empty while the player's value is zero).</summary>
        public static string UnrankedText(BoardMetric m, BoardPeriod p) => m == BoardMetric.Xp ? "Earn XP by completing jobs to appear on this board." : (p == BoardPeriod.Week ? "Nothing yet this week. " : "") + "Complete a job to appear on this board.";

        // ---------------------------------------------------------------- paging (the server pages by offset)
        public static int PageCount(int total) => total <= 0 ? 0 : (total + PageSize - 1) / PageSize;
        public static int Offset(int page) => Math.Max(0, (page - 1) * PageSize);
        public static int ClampPage(int page, int total) => Math.Max(1, Math.Min(page, Math.Max(1, PageCount(total))));
        public static string ShowingText(int offset, int shown, int total) => shown <= 0 ? "No players" : "Showing " + (offset + 1).ToString("N0", Inv) + " - " + (offset + shown).ToString("N0", Inv) + " of " + total.ToString("N0", Inv) + " players";

        /// <summary>The page buttons to draw: page numbers, with -1 standing for "..." (1 2 3 4 5 ... 120). Always contains the current page, the first and the last.</summary>
        public static List<int> PageWindow(int current, int count)
        {
            var l = new List<int>(); if (count <= 0) return l; current = Math.Max(1, Math.Min(current, count));
            if (count <= 7) { for (int i = 1; i <= count; i++) l.Add(i); return l; }
            if (current <= 4) { l.AddRange(new[] { 1, 2, 3, 4, 5, -1, count }); return l; }
            if (current >= count - 3) { l.AddRange(new[] { 1, -1, count - 4, count - 3, count - 2, count - 1, count }); return l; }
            l.AddRange(new[] { 1, -1, current - 1, current, current + 1, -1, count }); return l;
        }

        // ---------------------------------------------------------------- the podium
        /// <summary>The top players in the order they stand left to right: 2nd, 1st, 3rd (or fewer when fewer are ranked), each with the place it shows.</summary>
        public static List<KeyValuePair<int, BoardRow>> Podium(IList<BoardRow> top)
        {
            var r = new List<KeyValuePair<int, BoardRow>>(); if (top == null) return r;
            if (top.Count >= 2) r.Add(new KeyValuePair<int, BoardRow>(2, top[1]));
            if (top.Count >= 1) r.Add(new KeyValuePair<int, BoardRow>(1, top[0]));
            if (top.Count >= 3) r.Add(new KeyValuePair<int, BoardRow>(3, top[2]));
            return r;
        }

        /// <summary>Whether a row is the player's own (a row is never matched by name: two players can share one).</summary>
        public static bool IsMe(BoardRow r, string myId) => r != null && !string.IsNullOrEmpty(myId) && r.Id == myId;
    }
}
