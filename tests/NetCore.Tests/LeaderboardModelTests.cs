using System.Collections.Generic;
using System.Linq;
using ARO.NetCore;
using Xunit;

public class LeaderboardModelTests
{
    static BoardRow R(int rank, string id = null, string name = null) => new BoardRow { Rank = rank, Id = id ?? "id" + rank, Name = name ?? "P" + rank };

    // ---- keys and labels match the server's vocabulary
    [Theory] [InlineData(BoardMetric.Xp, "xp")] [InlineData(BoardMetric.Earnings, "earnings")] [InlineData(BoardMetric.Distance, "distance")] [InlineData(BoardMetric.Jobs, "jobs")]
    public void MetricKeysAreTheServersMetrics(BoardMetric m, string key) => Assert.Equal(key, LeaderboardRules.MetricKey(m));
    [Fact] public void PeriodKeysAreTheServersPeriods() { Assert.Equal("all", LeaderboardRules.PeriodKey(BoardPeriod.AllTime)); Assert.Equal("weekly", LeaderboardRules.PeriodKey(BoardPeriod.Week)); }
    [Fact] public void EveryMetricAndPeriodHasLabels() { foreach (BoardMetric m in System.Enum.GetValues(typeof(BoardMetric))) { Assert.False(string.IsNullOrEmpty(LeaderboardRules.MetricLabel(m))); Assert.False(string.IsNullOrEmpty(LeaderboardRules.ColumnTitle(m))); } foreach (BoardPeriod p in System.Enum.GetValues(typeof(BoardPeriod))) Assert.False(string.IsNullOrEmpty(LeaderboardRules.PeriodLabel(p))); }
    [Fact] public void MetricKeysAreDistinct() => Assert.Equal(4, System.Enum.GetValues(typeof(BoardMetric)).Cast<BoardMetric>().Select(LeaderboardRules.MetricKey).Distinct().Count());

    // ---- periods
    [Fact] public void OnlyWeeklyXpIsUnsupported() { foreach (BoardMetric m in System.Enum.GetValues(typeof(BoardMetric))) { Assert.True(LeaderboardRules.PeriodSupported(m, BoardPeriod.AllTime)); Assert.Equal(m != BoardMetric.Xp, LeaderboardRules.PeriodSupported(m, BoardPeriod.Week)); } }
    [Fact] public void WeeklyXpFallsBackToAllTimeAndSaysSo() { Assert.Equal(BoardPeriod.AllTime, LeaderboardRules.EffectivePeriod(BoardMetric.Xp, BoardPeriod.Week)); Assert.Contains("all time", LeaderboardRules.PeriodNote(BoardMetric.Xp, BoardPeriod.Week)); }
    [Fact] public void SupportedPeriodsAreKeptAndHaveNoNote() { Assert.Equal(BoardPeriod.Week, LeaderboardRules.EffectivePeriod(BoardMetric.Jobs, BoardPeriod.Week)); Assert.Null(LeaderboardRules.PeriodNote(BoardMetric.Jobs, BoardPeriod.Week)); Assert.Null(LeaderboardRules.PeriodNote(BoardMetric.Xp, BoardPeriod.AllTime)); }

    // ---- numbers
    [Fact] public void XpHasThousandsSeparatorsAndUnit() => Assert.Equal("125,400 XP", LeaderboardRules.FormatValue(BoardMetric.Xp, 125400));
    [Fact] public void BareXpHasNoUnit() => Assert.Equal("98,750", LeaderboardRules.FormatBare(BoardMetric.Xp, 98750));
    [Fact] public void EarningsAreInNaira() { Assert.Equal("₦ 1,234,000", LeaderboardRules.FormatValue(BoardMetric.Earnings, 1234000)); Assert.Equal("₦ 0", LeaderboardRules.FormatBare(BoardMetric.Earnings, 0)); }
    [Fact] public void DistanceKeepsOneDecimalWhenSmall() { Assert.Equal("12.5 km", LeaderboardRules.FormatBare(BoardMetric.Distance, 12.5)); Assert.Equal("24,380 km", LeaderboardRules.FormatBare(BoardMetric.Distance, 24380.4)); }
    [Fact] public void JobsAreCounted() { Assert.Equal("423", LeaderboardRules.FormatBare(BoardMetric.Jobs, 423)); Assert.Equal("1 job", LeaderboardRules.FormatValue(BoardMetric.Jobs, 1)); Assert.Equal("2 jobs", LeaderboardRules.FormatValue(BoardMetric.Jobs, 2)); Assert.Equal("0 jobs", LeaderboardRules.FormatValue(BoardMetric.Jobs, 0)); }
    [Fact] public void ANegativeValueIsNeverShown() { Assert.Equal("0", LeaderboardRules.FormatBare(BoardMetric.Xp, -5)); Assert.Equal("0 XP", LeaderboardRules.FormatValue(BoardMetric.Xp, -5)); }
    [Theory] [InlineData(128, "#128")] [InlineData(1, "#1")] [InlineData(1432, "#1,432")] [InlineData(0, "Unranked")] [InlineData(-1, "Unranked")]
    public void RankText(int rank, string expected) => Assert.Equal(expected, LeaderboardRules.RankText(rank));
    [Theory] [InlineData(1, 1)] [InlineData(2, 2)] [InlineData(3, 3)] [InlineData(4, 0)] [InlineData(0, 0)] [InlineData(-3, 0)]
    public void MedalsAreForTheTopThree(int rank, int medal) => Assert.Equal(medal, LeaderboardRules.MedalOf(rank));
    [Fact] public void UnrankedExplainsHowToGetRanked() { Assert.Contains("XP", LeaderboardRules.UnrankedText(BoardMetric.Xp, BoardPeriod.AllTime)); Assert.Contains("this week", LeaderboardRules.UnrankedText(BoardMetric.Jobs, BoardPeriod.Week)); Assert.DoesNotContain("this week", LeaderboardRules.UnrankedText(BoardMetric.Jobs, BoardPeriod.AllTime)); }

    // ---- paging
    [Theory] [InlineData(0, 0)] [InlineData(-4, 0)] [InlineData(1, 1)] [InlineData(12, 1)] [InlineData(13, 2)] [InlineData(1432, 120)] [InlineData(1440, 120)] [InlineData(1441, 121)]
    public void PageCount(int total, int pages) => Assert.Equal(pages, LeaderboardRules.PageCount(total));
    [Theory] [InlineData(1, 0)] [InlineData(2, 12)] [InlineData(120, 1428)] [InlineData(0, 0)] [InlineData(-5, 0)]
    public void OffsetOfAPage(int page, int offset) => Assert.Equal(offset, LeaderboardRules.Offset(page));
    [Theory] [InlineData(5, 30, 3)] [InlineData(0, 30, 1)] [InlineData(-3, 30, 1)] [InlineData(2, 0, 1)] [InlineData(2, 12, 1)]
    public void ClampPage(int page, int total, int expected) => Assert.Equal(expected, LeaderboardRules.ClampPage(page, total));
    [Fact] public void ShowingText() { Assert.Equal("Showing 1 - 12 of 1,432 players", LeaderboardRules.ShowingText(0, 12, 1432)); Assert.Equal("Showing 1,429 - 1,432 of 1,432 players", LeaderboardRules.ShowingText(1428, 4, 1432)); Assert.Equal("No players", LeaderboardRules.ShowingText(0, 0, 0)); }
    [Fact] public void ShortListShowsEveryPage() => Assert.Equal(new[] { 1, 2, 3, 4, 5 }, LeaderboardRules.PageWindow(3, 5));
    [Fact] public void NearTheStartTheWindowIsOneToFive() => Assert.Equal(new[] { 1, 2, 3, 4, 5, -1, 120 }, LeaderboardRules.PageWindow(1, 120));
    [Fact] public void NearTheEndTheWindowIsTheLastFive() => Assert.Equal(new[] { 1, -1, 116, 117, 118, 119, 120 }, LeaderboardRules.PageWindow(120, 120));
    [Fact] public void InTheMiddleTheWindowIsAroundTheCurrentPage() => Assert.Equal(new[] { 1, -1, 49, 50, 51, -1, 120 }, LeaderboardRules.PageWindow(50, 120));
    [Fact] public void NoPagesNoButtons() => Assert.Empty(LeaderboardRules.PageWindow(1, 0));
    [Fact] public void TheWindowAlwaysHasTheCurrentFirstAndLastPage() { for (int count = 1; count <= 40; count++) for (int cur = 1; cur <= count; cur++) { var w = LeaderboardRules.PageWindow(cur, count); Assert.Contains(cur, w); Assert.Contains(1, w); Assert.Contains(count, w); Assert.True(w.Count <= 7); Assert.Equal(w.Where(x => x > 0).OrderBy(x => x), w.Where(x => x > 0)); } }

    // ---- podium
    [Fact] public void PodiumStandsSecondFirstThird() { var p = LeaderboardRules.Podium(new List<BoardRow> { R(1), R(2), R(3), R(4) }); Assert.Equal(new[] { 2, 1, 3 }, p.Select(x => x.Key)); Assert.Equal(new[] { "P2", "P1", "P3" }, p.Select(x => x.Value.Name)); }
    [Fact] public void PodiumWithTwoPlayers() => Assert.Equal(new[] { 2, 1 }, LeaderboardRules.Podium(new List<BoardRow> { R(1), R(2) }).Select(x => x.Key));
    [Fact] public void PodiumWithOnePlayer() => Assert.Equal(new[] { 1 }, LeaderboardRules.Podium(new List<BoardRow> { R(1) }).Select(x => x.Key));
    [Fact] public void PodiumWithNobodyIsEmpty() { Assert.Empty(LeaderboardRules.Podium(new List<BoardRow>())); Assert.Empty(LeaderboardRules.Podium(null)); }

    // ---- who am I
    [Fact] public void TheOwnRowIsFoundByIdNeverByName() { var me = R(5, "me", "Oga"); var other = R(6, "x", "Oga"); Assert.True(LeaderboardRules.IsMe(me, "me")); Assert.False(LeaderboardRules.IsMe(other, "me")); }
    [Fact] public void NoIdMeansNoOwnRow() { Assert.False(LeaderboardRules.IsMe(R(1, ""), "")); Assert.False(LeaderboardRules.IsMe(R(1), null)); Assert.False(LeaderboardRules.IsMe(null, "me")); }
}
