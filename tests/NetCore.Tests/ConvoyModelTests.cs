using System.Collections.Generic;
using ARO.NetCore;
using Xunit;

public class ConvoyModelTests
{
    static ConvoyFacts F(bool inConvoy = true, string phase = "lobby", bool anyReady = false, PendingOp pending = PendingOp.None, SessionState s = SessionState.Offline, bool was = false)
        => new ConvoyFacts { InConvoy = inConvoy, Phase = phase, AnyMemberReady = anyReady, Pending = pending, Session = s, WasInConvoy = was };

    // ---- convoy states
    [Fact] public void NoConvoyWhenTheServerSaysNone() => Assert.Equal(ConvoyUiState.NoConvoy, ConvoyStates.Derive(F(inConvoy: false)));
    [Fact] public void DisbandedWhenAConvoyVanishedUnderUs() => Assert.Equal(ConvoyUiState.Disbanded, ConvoyStates.Derive(F(inConvoy: false, was: true)));
    [Fact] public void CreatingJoiningStartingAreOurOwnPendingCalls()
    {
        Assert.Equal(ConvoyUiState.Creating, ConvoyStates.Derive(F(inConvoy: false, pending: PendingOp.Creating)));
        Assert.Equal(ConvoyUiState.Joining, ConvoyStates.Derive(F(inConvoy: false, pending: PendingOp.Joining)));
        Assert.Equal(ConvoyUiState.Starting, ConvoyStates.Derive(F(pending: PendingOp.Starting)));
    }
    [Fact] public void SearchingOnlyWhileWeHaveNoConvoy()
    {
        Assert.Equal(ConvoyUiState.Searching, ConvoyStates.Derive(F(inConvoy: false, pending: PendingOp.Searching)));
        Assert.Equal(ConvoyUiState.InLobby, ConvoyStates.Derive(F(pending: PendingOp.Searching)));      // a refresh in the background does not change what the convoy is doing
    }
    [Fact] public void LobbyBecomesReadyCheckOnceAMemberIsReady()
    {
        Assert.Equal(ConvoyUiState.InLobby, ConvoyStates.Derive(F()));
        Assert.Equal(ConvoyUiState.ReadyCheck, ConvoyStates.Derive(F(anyReady: true)));
    }
    [Fact] public void DrivingNeedsTheLiveSessionToBeInSession()
    {
        Assert.Equal(ConvoyUiState.InSession, ConvoyStates.Derive(F(phase: "driving", s: SessionState.InSession)));
        Assert.Equal(ConvoyUiState.Reconnecting, ConvoyStates.Derive(F(phase: "driving", s: SessionState.Reconnecting)));
        Assert.Equal(ConvoyUiState.Starting, ConvoyStates.Derive(F(phase: "driving", s: SessionState.Offline)));       // on the road, but we have not joined
        Assert.Equal(ConvoyUiState.Starting, ConvoyStates.Derive(F(phase: "driving", s: SessionState.Connecting)));
        Assert.Equal(ConvoyUiState.Starting, ConvoyStates.Derive(F(phase: "driving", s: SessionState.Failed)));
    }
    [Fact] public void TheLobbyIgnoresALiveSession() => Assert.Equal(ConvoyUiState.InLobby, ConvoyStates.Derive(F(s: SessionState.InSession)));
    [Fact] public void EveryStateHasALabel() { foreach (ConvoyUiState s in System.Enum.GetValues(typeof(ConvoyUiState))) Assert.False(string.IsNullOrEmpty(ConvoyStates.Label(s))); }

    // ---- member states
    static ConvoyStates.MemberFacts M(bool invited = false, bool ready = false, bool leader = false, bool connected = true, double since = 600) => new ConvoyStates.MemberFacts { Invited = invited, Ready = ready, IsLeader = leader, Connected = connected, SecondsSinceJoin = since };
    [Fact] public void InvitedMembersAreInvitedWhateverElse() => Assert.Equal(MemberUiState.Invited, ConvoyStates.Member(M(invited: true, connected: false), "lobby"));
    [Fact] public void OfflineMembersAreDisconnected()
    {
        Assert.Equal(MemberUiState.Disconnected, ConvoyStates.Member(M(ready: true, connected: false), "lobby"));
        Assert.Equal(MemberUiState.Disconnected, ConvoyStates.Member(M(connected: false), "driving"));
    }
    [Fact] public void LobbyMembersAreReadyNotReadyOrJustJoined()
    {
        Assert.Equal(MemberUiState.Ready, ConvoyStates.Member(M(ready: true), "lobby"));
        Assert.Equal(MemberUiState.Ready, ConvoyStates.Member(M(leader: true), "lobby"));
        Assert.Equal(MemberUiState.NotReady, ConvoyStates.Member(M(), "lobby"));
        Assert.Equal(MemberUiState.Joined, ConvoyStates.Member(M(since: 3), "lobby"));
        Assert.Equal(MemberUiState.NotReady, ConvoyStates.Member(M(since: 15), "lobby"));
        Assert.Equal(MemberUiState.NotReady, ConvoyStates.Member(M(since: -1), "lobby"));       // unknown join time is never "just joined"
    }
    [Fact] public void ConnectedMembersOfADrivingConvoyAreDriving() => Assert.Equal(MemberUiState.Driving, ConvoyStates.Member(M(), "driving"));
    [Fact] public void EveryMemberStateHasALabel() { foreach (MemberUiState s in System.Enum.GetValues(typeof(MemberUiState))) Assert.False(string.IsNullOrEmpty(ConvoyStates.Label(s))); }

    // ---- the start gate
    [Fact] public void StartNeedsTheLeaderARouteReadyMembersAndNobodyOffline()
    {
        Assert.False(ConvoyStates.CanStart(false, false, true, 0, 0).Allowed);
        Assert.False(ConvoyStates.CanStart(true, true, true, 0, 0).Allowed);
        Assert.False(ConvoyStates.CanStart(true, false, false, 0, 0).Allowed);
        var wait = ConvoyStates.CanStart(true, false, true, 2, 0); Assert.False(wait.Allowed); Assert.Contains("2 players", wait.Reason);
        Assert.Contains("1 player", ConvoyStates.CanStart(true, false, true, 1, 0).Reason);
        var off = ConvoyStates.CanStart(true, false, true, 0, 1); Assert.False(off.Allowed); Assert.Contains("offline", off.Reason);
        var ok = ConvoyStates.CanStart(true, false, true, 0, 0); Assert.True(ok.Allowed); Assert.Equal("Start Driving", ok.Reason);
    }

    // ---- voice: never claims a connection nobody confirmed
    [Fact] public void TheNullProviderIsUnavailableForEveryone()
    {
        IVoiceProvider v = new NullVoiceProvider();
        Assert.False(v.IsAvailable); Assert.Equal(VoiceState.Unavailable, v.Local); Assert.Equal(VoiceState.Unavailable, v.StateOf("anyone"));
        Assert.Equal("Not available", VoiceText.Label(v.Local)); Assert.DoesNotContain("Connected", VoiceText.Label(v.Local));
    }
    [Fact] public void EveryVoiceStateHasALabel() { foreach (VoiceState s in System.Enum.GetValues(typeof(VoiceState))) Assert.False(string.IsNullOrEmpty(VoiceText.Label(s))); Assert.Equal("Connected", VoiceText.Label(VoiceState.Connected)); Assert.Equal("Speaking", VoiceText.Label(VoiceState.Speaking)); }

    // ---- chat content
    [Fact] public void ChatTextLosesMarkupControlCharactersAndRunsOfSpaces()
    {
        Assert.Equal("size=900big/size tail", ChatText.Clean("<size=900>big</size>\u0007  tail"));
        Assert.Equal("a b", ChatText.Clean("a\n\t b"));
        Assert.Equal("", ChatText.Clean(null)); Assert.Equal("", ChatText.Clean("  \u0001 "));
        Assert.DoesNotContain("<", ChatText.Clean("<color=red>x</color>"));
    }
    [Fact] public void ChatTextIsBounded() => Assert.Equal(ChatText.MaxLength, ChatText.Clean(new string('x', 5000)).Length);
    [Fact] public void ModeratedLinesNeverShowTheirText()
    {
        Assert.Equal("hello", ChatText.Display("hello", "ok"));
        Assert.DoesNotContain("rude", ChatText.Display("rude words", "hidden")); Assert.Contains("hidden", ChatText.Display("rude words", "hidden"));
        Assert.DoesNotContain("rude", ChatText.Display("rude words", "removed")); Assert.Contains("removed", ChatText.Display(null, "removed"));
        Assert.Equal("", ChatText.Display(null, "ok"));
    }

    // ---- geometry
    static readonly (double x, double z)[] Line = { (0, 0), (100, 0), (100, 100) };
    [Fact] public void RouteProgressAlongAPolyline()
    {
        Assert.Equal(0.0, ConvoyMath.RouteProgress(Line, 0, 0).Value, 6);
        Assert.Equal(0.25, ConvoyMath.RouteProgress(Line, 50, 0).Value, 6);
        Assert.Equal(0.5, ConvoyMath.RouteProgress(Line, 100, 0).Value, 6);
        Assert.Equal(0.75, ConvoyMath.RouteProgress(Line, 100, 50).Value, 6);
        Assert.Equal(1.0, ConvoyMath.RouteProgress(Line, 100, 100).Value, 6);
    }
    [Fact] public void RouteProgressSnapsOffRoutePointsToTheNearestPart()
    {
        Assert.Equal(0.25, ConvoyMath.RouteProgress(Line, 50, -30).Value, 6);
        Assert.Equal(1.0, ConvoyMath.RouteProgress(Line, 100, 500).Value, 6);       // past the end clamps to the end
        Assert.Equal(0.0, ConvoyMath.RouteProgress(Line, -500, 0).Value, 6);
    }
    [Fact] public void RouteProgressIsUnknownForUnusableRoutes()
    {
        Assert.Null(ConvoyMath.RouteProgress(null, 1, 1)); Assert.Null(ConvoyMath.RouteProgress(new (double, double)[] { (0, 0) }, 1, 1)); Assert.Null(ConvoyMath.RouteProgress(new (double, double)[] { (5, 5), (5, 5) }, 1, 1));
    }
    [Fact] public void DistanceAndSpread()
    {
        Assert.Equal(5.0, ConvoyMath.Distance(0, 0, 3, 4), 9);
        Assert.Equal(0.0, ConvoyMath.Spread(new (double, double)[] { (1, 1) })); Assert.Equal(0.0, ConvoyMath.Spread(null));
        Assert.Equal(10.0, ConvoyMath.Spread(new (double, double)[] { (0, 0), (6, 8), (3, 4) }), 9);
    }

    // ---- live roster: server membership x live avatars
    [Fact] public void MembersWithoutAnAvatarAreDisconnectedAndHaveNoPosition()
    {
        var members = new[] { ("Ada", true, true), ("Bola", false, true), ("Chi", false, true) };
        var avatars = new Dictionary<string, (double x, double z)> { ["ada"] = (0, 0), ["BOLA"] = (30, 40) };
        var rows = LiveRoster.Build(members, avatars, Line, "driving");
        Assert.Equal(3, rows.Count);
        Assert.True(rows[0].Present); Assert.Null(rows[0].DistanceToLeaderM); Assert.Equal(MemberUiState.Driving, rows[0].State);
        Assert.True(rows[1].Present); Assert.Equal(50.0, rows[1].DistanceToLeaderM.Value, 9); Assert.NotNull(rows[1].RouteProgress);
        Assert.False(rows[2].Present); Assert.Equal(MemberUiState.Disconnected, rows[2].State); Assert.Null(rows[2].DistanceToLeaderM); Assert.Null(rows[2].RouteProgress);
    }
    [Fact] public void NoLeaderAvatarMeansNoDistances()
    {
        var rows = LiveRoster.Build(new[] { ("Ada", true, true), ("Bola", false, true) }, new Dictionary<string, (double x, double z)> { ["Bola"] = (1, 1) }, null, "driving");
        Assert.Null(rows[1].DistanceToLeaderM); Assert.Null(rows[1].RouteProgress); Assert.False(rows[0].Present);
    }
}
