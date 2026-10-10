using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ARO.NetCore;
using Xunit;

public class BackendCoreTests
{
    // ---------------------------------------------------------------- failure classification + retries
    [Theory]
    [InlineData(0, true, false, null, FailureKind.Offline)]
    [InlineData(0, false, false, null, FailureKind.Offline)]
    [InlineData(0, true, true, null, FailureKind.Timeout)]
    [InlineData(401, false, false, null, FailureKind.SessionExpired)]
    [InlineData(400, false, false, "not_authenticated", FailureKind.SessionExpired)]
    [InlineData(403, false, false, null, FailureKind.Forbidden)]
    [InlineData(404, false, false, null, FailureKind.NotFound)]
    [InlineData(409, false, false, null, FailureKind.Conflict)]
    [InlineData(429, false, false, null, FailureKind.RateLimited)]
    [InlineData(400, false, false, "rate_limited", FailureKind.RateLimited)]
    [InlineData(400, false, false, "job_full", FailureKind.Client)]
    [InlineData(500, false, false, null, FailureKind.Server)]
    [InlineData(503, false, false, null, FailureKind.Server)]
    public void FailuresAreClassified(long http, bool conn, bool timedOut, string code, FailureKind expected) =>
        Assert.Equal(expected, ApiFailure.Classify(http, conn, timedOut, code));

    [Fact] public void OnlyTransientFailuresRetry()
    {
        foreach (var k in new[] { FailureKind.Offline, FailureKind.Timeout, FailureKind.Server }) Assert.True(ApiFailure.Retryable(k), k.ToString());
        foreach (var k in new[] { FailureKind.SessionExpired, FailureKind.Forbidden, FailureKind.NotFound, FailureKind.RateLimited, FailureKind.Conflict, FailureKind.Client, FailureKind.None })
            Assert.False(ApiFailure.Retryable(k), k.ToString());
    }

    [Fact] public void ReadsRetryButWritesNever()
    {
        var p = RetryPolicy.Reads;
        Assert.True(p.ShouldRetry(1, FailureKind.Offline, true));
        Assert.True(p.ShouldRetry(2, FailureKind.Server, true));
        Assert.False(p.ShouldRetry(3, FailureKind.Server, true));                 // attempts exhausted
        Assert.False(p.ShouldRetry(1, FailureKind.Offline, false));               // a write whose reply was lost may have succeeded
        Assert.False(p.ShouldRetry(1, FailureKind.Client, true));                 // a business rule will not change its mind
        Assert.False(p.ShouldRetry(1, FailureKind.RateLimited, true));
    }

    [Fact] public void BackoffGrowsAndIsCapped()
    {
        var p = new RetryPolicy(10, 0.5, 4);
        Assert.Equal(0, p.DelayBefore(1)); Assert.Equal(0.5, p.DelayBefore(2)); Assert.Equal(1.0, p.DelayBefore(3)); Assert.Equal(2.0, p.DelayBefore(4)); Assert.Equal(4.0, p.DelayBefore(5)); Assert.Equal(4.0, p.DelayBefore(9));
    }

    [Fact] public void EveryFailureKindHasAPlayerMessage()
    {
        foreach (FailureKind k in Enum.GetValues(typeof(FailureKind))) Assert.False(string.IsNullOrWhiteSpace(ApiFailure.Describe(k)), k.ToString());
    }

    // ---------------------------------------------------------------- loadable
    [Fact] public void LoadableTellsTheTruthAboutItsState()
    {
        var l = new Loadable<int[]>(); int changes = 0; l.Changed += () => changes++;
        Assert.Equal(LoadState.Idle, l.State); Assert.True(l.NeedsLoad(0, 30));
        l.Begin(); Assert.Equal(LoadState.Loading, l.State); Assert.False(l.NeedsLoad(1, 30)); Assert.False(l.HasAnswer);
        l.Succeed(new[] { 1, 2 }, false, 5); Assert.Equal(LoadState.Ready, l.State); Assert.True(l.HasAnswer); Assert.Equal(2, l.Value.Length);
        Assert.False(l.NeedsLoad(20, 30)); Assert.True(l.NeedsLoad(36, 30));
        l.Begin(); Assert.Equal(2, l.Value.Length);                                  // refresh keeps the previous value so the card does not blank
        l.Fail(FailureKind.Offline, "offline", 40); Assert.Equal(LoadState.Unavailable, l.State); Assert.False(l.HasAnswer); Assert.Equal("offline", l.Error); Assert.Equal(FailureKind.Offline, l.Failure);
        l.Succeed(new int[0], true, 50); Assert.Equal(LoadState.Empty, l.State); Assert.True(l.HasAnswer); Assert.Null(l.Error);
        l.Reset(); Assert.Equal(LoadState.Idle, l.State); Assert.Null(l.Value);
        Assert.Equal(6, changes);
    }

    // ---------------------------------------------------------------- request bodies
    [Fact] public void ArgsAreBuiltAsValidJson()
    {
        string json = JsonArgs.Obj(("p_name", "a \"quoted\" \\ name\nline"), ("p_n", 5), ("p_big", 9007199254740993L), ("p_f", 1.5), ("p_flag", true), ("p_null", null), ("p_ids", new[] { "x", "y" }));
        var doc = JsonDocument.Parse(json).RootElement;
        Assert.Equal("a \"quoted\" \\ name\nline", doc.GetProperty("p_name").GetString());
        Assert.Equal(5, doc.GetProperty("p_n").GetInt32());
        Assert.Equal(9007199254740993L, doc.GetProperty("p_big").GetInt64());
        Assert.Equal(1.5, doc.GetProperty("p_f").GetDouble());
        Assert.True(doc.GetProperty("p_flag").GetBoolean());
        Assert.Equal(JsonValueKind.Null, doc.GetProperty("p_null").ValueKind);
        Assert.Equal(new[] { "x", "y" }, doc.GetProperty("p_ids").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact] public void ArgsDoNotDependOnTheCulture()
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try { System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE"); Assert.Equal("{\"x\":1.5}", JsonArgs.Obj(("x", 1.5))); }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }

    [Fact] public void HostileStringsCannotBreakOutOfTheBody()
    {
        foreach (var evil in new[] { "\"}; drop table x; --", "\u2028\u2029", "\0\u0001\u001f", "{\"p_admin\":true}", new string('x', 5000) })
        {
            var doc = JsonDocument.Parse(JsonArgs.Obj(("a", evil), ("b", 1))).RootElement;
            Assert.Equal(evil, doc.GetProperty("a").GetString()); Assert.Equal(1, doc.GetProperty("b").GetInt32()); Assert.Equal(2, doc.EnumerateObject().Count());
        }
    }

    [Fact] public void NonFiniteNumbersAreRefused()
    {
        Assert.Throws<ArgumentException>(() => JsonArgs.Obj(("x", double.NaN)));
        Assert.Throws<ArgumentException>(() => JsonArgs.Obj(("x", float.PositiveInfinity)));
        Assert.Throws<ArgumentException>(() => JsonArgs.Obj(("x", new object())));
    }

    // ---------------------------------------------------------------- heartbeat
    [Fact] public void ASessionIsOpenedBeforeAnyHeartbeatAndTheCadenceFollowsTheServerTimeout()
    {
        var c = new HeartbeatClock();
        Assert.Equal(PresenceAction.StartSession, c.Next(0)); c.Began(); Assert.Equal(PresenceAction.None, c.Next(0.1));      // one request at a time
        c.SessionStarted(1, 90); Assert.True(c.HasSession); Assert.Equal(30, c.IntervalSeconds);
        Assert.Equal(PresenceAction.None, c.Next(30)); Assert.Equal(PresenceAction.SendHeartbeat, c.Next(31));
        c.Began(); c.BeatAccepted(31.5); Assert.Equal(PresenceAction.None, c.Next(60)); Assert.Equal(PresenceAction.SendHeartbeat, c.Next(61.5));
    }

    [Theory] [InlineData(15, 10)] [InlineData(60, 20)] [InlineData(300, 45)] [InlineData(0, 30)]
    public void TheIntervalIsAThirdOfTheTimeoutWithinSaneBounds(double ttl, double expected) { var c = new HeartbeatClock(); c.SessionStarted(0, ttl); Assert.Equal(expected, c.IntervalSeconds); }

    [Fact] public void NetworkFailuresBackOffInsteadOfHammering()
    {
        var c = new HeartbeatClock(); c.SessionStarted(0, 90);
        double t = 31; var gaps = new List<double>();
        for (int i = 0; i < 6; i++) { Assert.Equal(PresenceAction.SendHeartbeat, c.Next(t)); c.Began(); c.Failed(t, false); double next = t; while (c.Next(next) == PresenceAction.None) next += 0.5; gaps.Add(next - t); t = next; }
        Assert.Equal(new[] { 5.0, 10.0, 20.0, 40.0, 60.0, 60.0 }, gaps.ToArray());
        Assert.True(c.HasSession);                                                           // still the same session
    }

    [Fact] public void AnExpiredSessionIsReplacedImmediately()
    {
        var c = new HeartbeatClock(); c.SessionStarted(0, 90); c.Began(); c.Failed(40, true);
        Assert.False(c.HasSession); Assert.Equal(PresenceAction.StartSession, c.Next(40));
        c.Began(); c.SessionStarted(41, 90); Assert.Equal(0, c.Failures);
    }

    [Fact] public void ConnectedMeansTheLastAnswerIsInsideTheTimeout()
    {
        var c = new HeartbeatClock(); Assert.False(c.Connected(0));
        c.SessionStarted(10, 90); Assert.True(c.Connected(50)); Assert.True(c.Connected(99)); Assert.False(c.Connected(100.5));
        c.BeatAccepted(60); Assert.True(c.Connected(149)); c.Reset(); Assert.False(c.Connected(150)); Assert.Equal(PresenceAction.StartSession, c.Next(150));
    }

    // ---------------------------------------------------------------- loopback-only switches
    const string Local = "http://127.0.0.1:8123/index.html";

    [Fact] public void TheBackendCanBeOverriddenOnlyLocally()
    {
        Assert.True(BackendOverride.TryParse(Local + "?backend=http%3A%2F%2F127.0.0.1%3A54321&anon=abc.def", out var url, out var anon));
        Assert.Equal("http://127.0.0.1:54321", url); Assert.Equal("abc.def", anon);
        Assert.True(BackendOverride.TryParse("http://localhost:1/index.html?smoke=dashboard&backend=http://localhost:2/&anon=k", out url, out anon)); Assert.Equal("http://localhost:2", url);
    }

    [Theory]
    [InlineData("https://play.example.com/index.html?backend=http://127.0.0.1:1&anon=k")]             // public page, loopback target
    [InlineData("http://127.0.0.1:1/index.html?backend=https://evil.example.com&anon=k")]             // loopback page, public target
    [InlineData("http://127.0.0.1:1/index.html?backend=http://127.0.0.1.evil.com&anon=k")]            // look-alike host
    [InlineData("http://evil.com/?x=http://127.0.0.1/index.html?backend=http://127.0.0.1:1&anon=k")]  // loopback text inside a public URL
    [InlineData("http://127.0.0.1:1/index.html?backend=http://127.0.0.1:1")]                          // no key
    [InlineData("http://127.0.0.1:1/index.html?anon=k")]
    [InlineData("http://127.0.0.1:1/index.html?backend=file:///etc/passwd&anon=k")]
    [InlineData("")] [InlineData(null)]
    public void NothingElseIsHonoured(string page) => Assert.False(BackendOverride.TryParse(page, out _, out _));

    // ---------------------------------------------------------------- fault injection
    [Fact] public void FaultsAreParsedAndConsumed()
    {
        var plan = FaultPlan.Parse(Local + "?fault=list_available_jobs:slow:9,get_content:error:2,get_my_convoy:offline");
        Assert.Equal(3, plan.RuleCount);
        var f = plan.Next("/rest/v1/rpc/list_available_jobs"); Assert.Equal(FaultKind.Slow, f.Kind); Assert.Equal(9, f.Seconds);
        Assert.Equal(FaultKind.None, plan.Next("/rest/v1/rpc/list_available_jobs").Kind);            // one-shot
        Assert.Equal(FaultKind.Error, plan.Next("/rest/v1/rpc/get_content").Kind); Assert.Equal(FaultKind.Error, plan.Next("/rest/v1/rpc/get_content").Kind); Assert.Equal(FaultKind.None, plan.Next("/rest/v1/rpc/get_content").Kind);
        Assert.Equal(FaultKind.Offline, plan.Next("/rest/v1/rpc/get_my_convoy").Kind);
        Assert.Equal(FaultKind.None, plan.Next("/rest/v1/rpc/heartbeat").Kind); Assert.Equal(FaultKind.None, plan.Next(null).Kind);
    }

    [Fact] public void FaultsNeverApplyOnAPublicPage()
    {
        Assert.Equal(0, FaultPlan.Parse("https://play.example.com/index.html?fault=get_content:error:5").RuleCount);
        Assert.Equal(0, FaultPlan.Parse(null).RuleCount); Assert.Equal(0, FaultPlan.Parse(Local).RuleCount); Assert.Equal(0, FaultPlan.Parse(Local + "?fault=").RuleCount);
        Assert.Equal(0, FaultPlan.Parse(Local + "?fault=broken").RuleCount);
    }

    // ---------------------------------------------------------------- presentation helpers
    [Theory] [InlineData("payout", NoticeLevel.Good)] [InlineData("level_up", NoticeLevel.Good)] [InlineData("achievement", NoticeLevel.Good)]
    [InlineData("error", NoticeLevel.Bad)] [InlineData("system", NoticeLevel.Info)] [InlineData("server", NoticeLevel.Info)] [InlineData("job_event", NoticeLevel.Info)]
    [InlineData("convoy_event", NoticeLevel.Info)] [InlineData("friend_event", NoticeLevel.Info)] [InlineData("something_new", NoticeLevel.Info)]
    public void NotificationTypesMapToColours(string type, NoticeLevel level) => Assert.Equal(level, NoticeMap.LevelFor(type));

    [Fact] public void AgoIsMeasuredOnServerTime()
    {
        var now = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("just now", TimeText.Ago(now.AddSeconds(-5), now)); Assert.Equal("just now", TimeText.Ago(now.AddSeconds(30), now));    // a future stamp (clock skew) is never "negative"
        Assert.Equal("5 min ago", TimeText.Ago(now.AddMinutes(-5), now)); Assert.Equal("3 h ago", TimeText.Ago(now.AddHours(-3), now)); Assert.Equal("2 d ago", TimeText.Ago(now.AddDays(-2), now));
        Assert.Equal("5 min ago", TimeText.AgoFrom("2026-10-10T11:54:30.123456+00:00", "2026-10-10T12:00:00+00:00"));
        Assert.Equal("", TimeText.AgoFrom("not a date", "2026-10-10T12:00:00+00:00"));
    }
}

public class WorldMapServerStateTests
{
    static readonly JsonSerializerOptions Opt = new JsonSerializerOptions { IncludeFields = true };
    static WorldMapModel Model() => new WorldMapModel(JsonSerializer.Deserialize<WorldMapData>(File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "world_map.json")), Opt));
    static ServerRouteState S(string code, string st, int lvl) => new ServerRouteState { code = code, effectiveStatus = st, unlockLevel = lvl };

    [Fact] public void WithoutServerStateTheLegacyRulesStillApply()
    {
        var m = Model(); Assert.False(m.HasServerState);
        Assert.Equal(RouteState.Prototype, m.StateFor(m.Route("lagos-ibadan"), 1));
        Assert.Equal(RouteState.PlannedAvailable, m.StateFor(m.Route("lagos-benin-city"), 5));
    }

    [Fact] public void ItFailsClosedUntilTheServerHasAnswered()
    {
        var m = Model(); m.RequireServerState = true;
        foreach (var r in m.Routes) { Assert.Equal(RouteState.Unavailable, m.StateFor(r, 99)); Assert.False(WorldMapModel.IsDrivable(m.StateFor(r, 99))); }
        foreach (var c in m.Cities) Assert.Equal(CityState.Unavailable, m.StateFor(c, 99));
    }

    [Fact] public void TheServerDecidesWhatIsDrivable()
    {
        var m = Model(); m.RequireServerState = true;
        m.ApplyServerState(m.Routes.Select(r => S(r.id, r.id == "lagos-ibadan" ? "OPEN" : "PLANNED", r.unlockLevel)));
        Assert.True(m.HasServerState);
        Assert.Equal(RouteState.Prototype, m.StateFor(m.Route("lagos-ibadan"), 1));
        Assert.Equal(CityState.Open, m.StateFor(m.City("lagos"), 1)); Assert.Equal(CityState.Open, m.StateFor(m.City("ibadan"), 1));
        Assert.Equal(CityState.Planned, m.StateFor(m.City("accra"), 99).Equals(CityState.Locked) ? CityState.Locked : m.StateFor(m.City("accra"), 99));
    }

    [Fact] public void AnOpenRouteAboveThePlayersLevelIsLockedNotOpen()
    {
        var m = Model(); m.RequireServerState = true;
        m.ApplyServerState(m.Routes.Select(r => S(r.id, r.id == "lagos-ibadan" ? "LOCKED" : "PLANNED", r.unlockLevel)));
        Assert.Equal(RouteState.PlannedLocked, m.StateFor(m.Route("lagos-ibadan"), 50));
        Assert.False(WorldMapModel.IsDrivable(m.StateFor(m.Route("lagos-ibadan"), 50)));
        Assert.NotEqual(CityState.Open, m.StateFor(m.City("lagos"), 50));
    }

    [Fact] public void PlannedRoutesFollowTheLevelButNeverBecomeDrivable()
    {
        var m = Model(); m.RequireServerState = true;
        m.ApplyServerState(m.Routes.Select(r => S(r.id, "PLANNED", r.unlockLevel)));
        var benin = m.Route("lagos-benin-city");
        Assert.Equal(RouteState.PlannedLocked, m.StateFor(benin, benin.unlockLevel - 1));
        Assert.Equal(RouteState.PlannedAvailable, m.StateFor(benin, benin.unlockLevel));
        Assert.False(WorldMapModel.IsDrivable(m.StateFor(benin, 99)));
        Assert.All(m.Routes, r => Assert.False(WorldMapModel.IsDrivable(m.StateFor(r, 99))));
    }

    [Fact] public void ARouteTheServerDoesNotKnowIsNeverOffered()
    {
        var m = Model(); m.RequireServerState = true;
        m.ApplyServerState(new[] { S("lagos-ibadan", "OPEN", 1) });
        Assert.Equal(RouteState.PlannedLocked, m.StateFor(m.Route("kaduna-kano"), 99));
    }

    [Fact] public void ClearingServerStateFailsClosedAgain()
    {
        var m = Model(); m.RequireServerState = true; m.ApplyServerState(new[] { S("lagos-ibadan", "OPEN", 1) }); m.ClearServerState();
        Assert.False(m.HasServerState); Assert.Equal(RouteState.Unavailable, m.StateFor(m.Route("lagos-ibadan"), 1));
    }

    [Fact] public void EveryServerRouteCodeInTheSeedMatchesAMapRoute()
    {
        // supabase/seed_world.sql is generated from world_map.json; their route codes must stay identical or the overlay silently misses routes.
        string seed = File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "seed_world.sql")); var m = Model();
        foreach (var r in m.Routes) Assert.Contains("('" + r.id + "'", seed);
        foreach (var c in m.Cities) Assert.Contains("'" + c.id + "'", seed);
    }
}
