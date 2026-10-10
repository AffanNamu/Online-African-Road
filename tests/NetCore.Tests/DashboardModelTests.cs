using System;
using System.Collections.Generic;
using System.Linq;
using ARO.NetCore;
using Xunit;

public class DashboardModelTests
{
    [Fact] public void FuelAndConditionAreClamped()
    {
        Assert.Equal(75f, Vitals.FuelPercent(90, 120)); Assert.Equal(0f, Vitals.FuelPercent(5, 0)); Assert.Equal(100f, Vitals.FuelPercent(500, 120)); Assert.Equal(0f, Vitals.FuelPercent(-3, 120));
        Assert.Equal(96f, Vitals.ConditionPercent(4)); Assert.Equal(0f, Vitals.ConditionPercent(130)); Assert.Equal(100f, Vitals.ConditionPercent(-5));
    }

    [Theory]
    [InlineData(false, 80, 0, false, TruckState.NoVehicle)] [InlineData(true, 80, 4, false, TruckState.Ready)] [InlineData(true, 9.9, 4, false, TruckState.LowFuel)]
    [InlineData(true, 10, 4, false, TruckState.Ready)] [InlineData(true, 80, 70, false, TruckState.NeedsRepair)] [InlineData(true, 80, 69.9, false, TruckState.Ready)]
    [InlineData(true, 80, 4, true, TruckState.OnJob)] [InlineData(true, 5, 4, true, TruckState.OnJob)] [InlineData(true, 80, 90, true, TruckState.NeedsRepair)]
    public void TruckStatusFollowsTheRules(bool has, double fuel, double dmg, bool onJob, TruckState expected) => Assert.Equal(expected, Vitals.Status(has, fuel, dmg, onJob));

    [Fact] public void EveryStateHasALabel() { foreach (TruckState s in Enum.GetValues(typeof(TruckState))) Assert.False(string.IsNullOrWhiteSpace(Vitals.Label(s))); Assert.Equal("Ready", Vitals.Label(TruckState.Ready)); }

    [Theory] [InlineData(0, 0)] [InlineData(-4, 0)] [InlineData(50, 60)] [InlineData(6.1, 7)] [InlineData(0.1, 1)] [InlineData(128, 154)]
    public void EtaUsesTheFiftyKmhPlanningSpeed(double km, int minutes) => Assert.Equal(minutes, JobMath.EtaMinutes(km));

    [Theory] [InlineData(1, "1 min")] [InlineData(59, "59 min")] [InlineData(60, "1h")] [InlineData(65, "1h 05m")] [InlineData(154, "2h 34m")] [InlineData(600, "10h")]
    public void DurationsReadNaturally(int minutes, string text) => Assert.Equal(text, JobMath.FormatDuration(minutes));

    [Fact] public void DifficultyLabelsAndMoney() { Assert.Equal("Easy", JobMath.Difficulty(1)); Assert.Equal("Easy", JobMath.Difficulty(2)); Assert.Equal("Medium", JobMath.Difficulty(3)); Assert.Equal("Hard", JobMath.Difficulty(5)); Assert.Equal("48,750", Money.Format(48750L)); Assert.Equal("1,260", Money.Format(1259.6)); Assert.Equal("0", Money.Format(0L)); }

    [Theory] [InlineData(0, 50)] [InlineData(49.9, 50)] [InlineData(50, 100)] [InlineData(140, 250)] [InlineData(999, 1000)] [InlineData(25000, 50000)] [InlineData(61234, 75000)]
    public void NextMilestoneIsTheNextRung(double km, double target) { var m = Milestones.Next(km); Assert.Equal(target, m.Target); Assert.InRange(m.Fraction, 0f, 1f); Assert.Equal("Drive " + Money.Format(target) + " km", m.Title); }

    [Fact] public void MilestoneFractionIsDistanceOverTarget() { var m = Milestones.Next(140); Assert.Equal(140.0 / 250.0, m.Fraction, 4); Assert.Equal(140.0, m.Current); Assert.Equal(0f, Milestones.Next(-5).Fraction); }

    static NewsFeedSpec Feed() => new NewsFeedSpec { items = new[]
    {
        new NewsItem { id = "a", title = "Corridor open", body = "Take jobs.", date = "2026-10-10", art = "highway", kind = "update" },
        new NewsItem { id = "b", title = "Convoys", body = "Drive together.", date = "2026-10-12", art = "convoy", kind = "event" },
        new NewsItem { id = "c", title = "Tip", body = "Check fuel.", date = "2026-09-01", art = "highway", kind = "tip" } } };

    [Fact] public void NewsFeedValidatesAndSortsNewestFirst()
    {
        Assert.Empty(NewsFeed.Validate(Feed(), new HashSet<string> { "highway", "convoy" }));
        Assert.Equal(new[] { "b", "a" }, NewsFeed.Latest(Feed(), 2).Select(n => n.id).ToArray());
        Assert.Empty(NewsFeed.Latest(null, 3)); Assert.Empty(NewsFeed.Latest(new NewsFeedSpec(), 3));
    }

    [Fact] public void NewsProblemsAreReported()
    {
        void Rejects(Action<NewsFeedSpec> m, string what, ISet<string> art = null) { var f = Feed(); m(f); var e = NewsFeed.Validate(f, art); Assert.True(e.Any(x => x.Contains(what, StringComparison.OrdinalIgnoreCase)), what + " not reported: " + string.Join("|", e)); }
        Rejects(f => f.items[1].id = "a", "duplicate"); Rejects(f => f.items[0].title = "", "title"); Rejects(f => f.items[0].title = new string('x', 81), "title");
        Rejects(f => f.items[0].body = new string('x', 221), "body"); Rejects(f => f.items[0].date = "10/10/2026", "date"); Rejects(f => f.items[0].kind = "ad", "kind");
        Rejects(f => f.items[0].art = "ghost", "unknown art", new HashSet<string> { "highway" }); Rejects(f => f.schemaVersion = 2, "schemaVersion"); Rejects(f => f.items = null, "items");
        Assert.Contains("null", NewsFeed.Validate(null)[0]);
    }

    [Fact] public void BadDatesSortLast() { var f = Feed(); f.items[0].date = "garbage"; Assert.Equal("a", NewsFeed.Latest(f, 3).Last().id); }

    [Fact] public void NoticesCountUnreadAndCoalesceRepeats()
    {
        var log = new NoticeLog(3);
        log.Add("Job accepted", NoticeLevel.Good, 10); log.Add("Job accepted", NoticeLevel.Good, 12); Assert.Equal(1, log.Count); Assert.Equal(2, log.Recent(1)[0].Count);
        log.Add("Server unreachable", NoticeLevel.Bad, 20); Assert.Equal(2, log.Unread);
        log.MarkAllRead(); Assert.Equal(0, log.Unread);
        log.Add("Server unreachable", NoticeLevel.Bad, 25); Assert.Equal(1, log.Unread);            // a repeat after reading counts as new
        log.Add("Job accepted", NoticeLevel.Good, 100); Assert.Equal(3, log.Count);                    // not coalesced: too long ago / not consecutive
        log.Add("a", NoticeLevel.Info, 101); log.Add("b", NoticeLevel.Info, 102); Assert.Equal(3, log.Count);   // capacity drops the oldest
        Assert.Equal("b", log.Recent(3)[0].Text); Assert.Equal(2, log.Recent(2).Count);
        log.Add("   ", NoticeLevel.Info, 103); log.Add(null, NoticeLevel.Info, 104); Assert.Equal(3, log.Count);
    }

    [Fact] public void PresenceTextIsHonest()
    {
        Assert.Equal("Offline", Presence.Describe(false, 0)); Assert.Equal("Online", Presence.Describe(true, 0)); Assert.Equal("Online", Presence.Describe(true, 1)); Assert.Equal("Convoy · 3", Presence.Describe(true, 3));
    }

    [Theory] [InlineData("KingTunde", "K")] [InlineData("Musa Pro", "MP")] [InlineData("truckerjoy", "T")] [InlineData("bola_drive", "BD")] [InlineData("", "?")] [InlineData(null, "?")] [InlineData("  ", "?")]
    public void InitialsComeFromTheName(string name, string initials) => Assert.Equal(initials, People.Initials(name));

    [Fact] public void AvatarColoursAreStableLegibleAndVaried()
    {
        var seen = new HashSet<string>();
        foreach (var n in new[] { "KingTunde", "MusaPro", "TruckerJoy", "BolaDrive", "Demo Driver", "a", "zzzz" })
        {
            People.AvatarRgb(n, out float r, out float g, out float b); People.AvatarRgb(n, out float r2, out float g2, out float b2);
            Assert.Equal((r, g, b), (r2, g2, b2)); foreach (var c in new[] { r, g, b }) Assert.InRange(c, 0.2f, 0.8f);
            seen.Add($"{r:0.00}{g:0.00}{b:0.00}");
        }
        Assert.True(seen.Count >= 5, "names should not all share one colour");
    }

    [Fact] public void EasingEndpointsAreExact()
    {
        foreach (Func<float, float> f in new Func<float, float>[] { Ease.OutCubic, Ease.OutBack, Ease.InOutSine }) { Assert.Equal(0f, f(0f), 4); Assert.Equal(1f, f(1f), 4); Assert.Equal(f(0f), f(-3f), 4); Assert.Equal(f(1f), f(7f), 4); }
        Assert.True(Ease.OutCubic(0.5f) > 0.5f); Assert.True(Ease.OutBack(0.6f) > 1f - 0.5f); Assert.True(Enumerable.Range(0, 101).Max(i => Ease.OutBack(i / 100f)) > 1f, "back easing overshoots slightly");
    }

    [Fact] public void ProgressAndStaggerAndCountUp()
    {
        Assert.Equal(0f, Tween.Progress(0.1f, 0.3f, 0.5f)); Assert.Equal(0.5f, Tween.Progress(0.55f, 0.3f, 0.5f), 4); Assert.Equal(1f, Tween.Progress(9f, 0.3f, 0.5f)); Assert.Equal(1f, Tween.Progress(0f, 0f, 0f));
        Assert.Equal(0.24f, Tween.StaggerDelay(3, 0.08f, 1f), 4); Assert.Equal(0.5f, Tween.StaggerDelay(99, 0.08f, 0.5f)); Assert.Equal(0f, Tween.StaggerDelay(-2, 0.08f, 1f));
        Assert.Equal(0L, Tween.CountUp(0, 48750, 0f)); Assert.Equal(48750L, Tween.CountUp(0, 48750, 1f)); Assert.Equal(1000L, Tween.CountUp(1000, 1000, 0.5f));
        long prev = -1; for (int i = 0; i <= 20; i++) { long v = Tween.CountUp(0, 48750, i / 20f); Assert.True(v >= prev); prev = v; }
        Assert.Equal(47750L, Tween.CountUp(50000, 47750, 1f)); Assert.Equal(49000L, Tween.CountUp(50000, 47750, 0.0f) - 1000L);   // counts down as well as up
    }

    [Fact] public void PulseAndSkeletonStayInRange()
    {
        for (float t = 0; t < 10f; t += 0.37f) { Assert.InRange(Tween.Pulse(t, 1.5f), 0f, 1f); for (int i = 0; i < 5; i++) Assert.InRange(Tween.SkeletonAlpha(t, i), 0.07f, 0.53f); }
        Assert.Equal(1f, Tween.Pulse(3f, 0f)); Assert.NotEqual(Tween.SkeletonAlpha(1f, 0), Tween.SkeletonAlpha(1f, 3));
    }

    [Fact] public void KenBurnsStaysInsideTheTextureAndNeverZoomsOut()
    {
        foreach (var (bx, by, bw, bh) in new[] { (0f, 0.3f, 1f, 0.49f), (0.3f, 0.46f, 0.7f, 0.344f), (0f, 0f, 1f, 1f), (0.9f, 0.9f, 0.1f, 0.1f) })
            for (float t = 0; t < 40f; t += 0.7f)
            {
                Tween.KenBurns(t, 12f, bx, by, bw, bh, 1.12f, 0.04f, 0.02f, out float x, out float y, out float w, out float h);
                Assert.InRange(x, 0f, 1f); Assert.InRange(y, 0f, 1f); Assert.True(x + w <= 1.0001f && y + h <= 1.0001f, $"crop leaves the texture at t={t}");
                Assert.True(w <= bw + 1e-5f && h <= bh + 1e-5f); Assert.True(w >= bw / 1.12f - 1e-5f); Assert.Equal(bw / bh, w / h, 3);       // aspect preserved
            }
    }

    [Fact] public void KenBurnsIsContinuousAndStartsAtRest()
    {
        Tween.KenBurns(0f, 12f, 0.1f, 0.2f, 0.6f, 0.3f, 1.1f, 0.05f, 0.05f, out float x0, out float y0, out float w0, out float h0);
        Assert.Equal(0.6f, w0, 4); Assert.Equal(0.3f, h0, 4);
        float px = x0, py = y0, pw = w0;
        for (float t = 0.05f; t < 30f; t += 0.05f)
        {
            Tween.KenBurns(t, 12f, 0.1f, 0.2f, 0.6f, 0.3f, 1.1f, 0.05f, 0.05f, out float x, out float y, out float w, out _);
            Assert.True(Math.Abs(x - px) < 0.01f && Math.Abs(y - py) < 0.01f && Math.Abs(w - pw) < 0.01f, $"jump at t={t}"); px = x; py = y; pw = w;
        }
        Tween.KenBurns(5f, 0f, 0.1f, 0.2f, 0.6f, 0.3f, 1.1f, 0f, 0f, out _, out _, out float wz, out _); Assert.Equal(0.6f, wz, 4);       // zero duration is safe
    }

    [Fact] public void RecentPlayersMergeNewestFirstWithoutYouOrDuplicates()
    {
        var old = new[] { new RecentEntry { name = "KingTunde", seenAt = 100 }, new RecentEntry { name = "MusaPro", seenAt = 200 } };
        var m = Recents.Merge(old, new[] { "musapro", "Me", "BolaDrive", " ", null }, "me", 1000);
        Assert.Equal(new[] { "BolaDrive", "musapro", "KingTunde" }, m.Select(e => e.name).ToArray());
        Assert.Equal(1000, m[1].seenAt); Assert.Equal(100, m[2].seenAt);
        Assert.Empty(Recents.Merge(null, null, "me", 5)); Assert.Single(Recents.Merge(null, new[] { "a", "b", "c" }, "me", 5, 1));
        Assert.Equal(8, Recents.Merge(null, Enumerable.Range(0, 20).Select(i => "p" + i), "me", 5).Length);
    }

    [Fact] public void RecentPlayerStatusReadsNaturally()
    {
        var now = 100000L; var here = new HashSet<string> { "A" };
        Assert.Equal("In convoy", Recents.Status(new RecentEntry { name = "A", seenAt = 1 }, here, now));
        Assert.Equal("1 min ago", Recents.Status(new RecentEntry { name = "B", seenAt = now - 5 }, here, now));
        Assert.Equal("30 min ago", Recents.Status(new RecentEntry { name = "B", seenAt = now - 1800 }, null, now));
        Assert.Equal("5 h ago", Recents.Status(new RecentEntry { name = "B", seenAt = now - 18000 }, null, now));
        Assert.Equal("2 d ago", Recents.Status(new RecentEntry { name = "B", seenAt = now - 172800 }, null, now));
        Assert.Equal("1 min ago", Recents.Status(new RecentEntry { name = "B", seenAt = now + 500 }, null, now));      // clock skew never shows the future
    }
}
