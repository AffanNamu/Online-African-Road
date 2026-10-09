using System;
using System.Collections.Generic;
using ARO.NetCore;
using Xunit;

public class SessionLifecycleTests
{
    static SessionLifecycle InSession(ReconnectPolicy p = null)
    { var s = new SessionLifecycle(p); Assert.True(s.BeginConnect()); Assert.True(s.OnConnected()); return s; }

    [Fact] public void StartsOffline() => Assert.Equal(SessionState.Offline, new SessionLifecycle().State);

    [Fact] public void ConnectHappyPath()
    {
        var s = new SessionLifecycle(); var seen = new List<SessionState>(); s.StateChanged += seen.Add;
        Assert.True(s.BeginConnect()); Assert.True(s.OnConnected());
        Assert.Equal(new[] { SessionState.Connecting, SessionState.InSession }, seen);
    }

    [Fact] public void CannotBeginConnectTwice()
    {
        var s = new SessionLifecycle(); Assert.True(s.BeginConnect());
        Assert.False(s.BeginConnect());
        s.OnConnected(); Assert.False(s.BeginConnect());
    }

    [Fact] public void OnConnectedIgnoredWhenOffline()
    { var s = new SessionLifecycle(); Assert.False(s.OnConnected()); Assert.Equal(SessionState.Offline, s.State); }

    [Fact] public void ConnectFailureEndsInFailedAndAllowsRetry()
    {
        var s = new SessionLifecycle(); s.BeginConnect();
        var d = s.OnConnectFailed("Session full");
        Assert.False(d.Reconnect); Assert.Equal(SessionState.Failed, s.State); Assert.Equal("Session full", d.Message);
        Assert.True(s.BeginConnect());
    }

    [Fact] public void IntentionalDisconnectGoesOfflineWithoutReconnect()
    {
        var s = InSession(); var d = s.OnDisconnected(DisconnectReason.Intentional);
        Assert.False(d.Reconnect); Assert.Equal(SessionState.Offline, s.State);
    }

    [Theory] [InlineData(DisconnectReason.HostLeft)] [InlineData(DisconnectReason.Kicked)]
    public void HostLeftOrKickedIsTerminal(DisconnectReason r)
    {
        var s = InSession(); var d = s.OnDisconnected(r);
        Assert.False(d.Reconnect); Assert.Equal(SessionState.Failed, s.State); Assert.False(string.IsNullOrEmpty(d.Message));
    }

    [Theory] [InlineData(DisconnectReason.TransportFailure)] [InlineData(DisconnectReason.Timeout)]
    public void TransportLossTriggersReconnectWithFirstDelay(DisconnectReason r)
    {
        var s = InSession(new ReconnectPolicy(5, 1, 15)); var d = s.OnDisconnected(r);
        Assert.True(d.Reconnect); Assert.Equal(TimeSpan.FromSeconds(1), d.Delay);
        Assert.Equal(SessionState.Reconnecting, s.State); Assert.Equal(1, s.Attempt);
    }

    [Fact] public void BackoffDoublesAndCaps()
    {
        var p = new ReconnectPolicy(8, 1, 10);
        for (int i = 0; i < 6; i++) Assert.Equal(new double[] { 1, 2, 4, 8, 10, 10 }[i], p.DelayFor(i + 1).TotalSeconds);
    }

    [Fact] public void GivesUpAfterMaxAttempts()
    {
        var s = InSession(new ReconnectPolicy(3, 1, 15)); s.OnDisconnected(DisconnectReason.TransportFailure);
        Assert.True(s.OnReconnectFailed().Reconnect);   // attempt 2
        Assert.True(s.OnReconnectFailed().Reconnect);   // attempt 3
        var last = s.OnReconnectFailed();
        Assert.False(last.Reconnect); Assert.Equal(SessionState.Failed, s.State);
    }

    [Fact] public void SuccessfulReconnectResetsAttempts()
    {
        var s = InSession(); s.OnDisconnected(DisconnectReason.Timeout); s.OnReconnectFailed();
        Assert.True(s.OnConnected()); Assert.Equal(SessionState.InSession, s.State); Assert.Equal(0, s.Attempt);
    }

    [Fact] public void DisconnectWhileOfflineOrFailedIsIgnored()
    {
        var s = new SessionLifecycle();
        Assert.False(s.OnDisconnected(DisconnectReason.TransportFailure).Reconnect); Assert.Equal(SessionState.Offline, s.State);
    }

    [Fact] public void TransportLossWhileConnectingFailsInsteadOfReconnecting()
    {
        var s = new SessionLifecycle(); s.BeginConnect();
        Assert.False(s.OnDisconnected(DisconnectReason.TransportFailure).Reconnect); Assert.Equal(SessionState.Failed, s.State);
    }

    [Fact] public void ReconnectFailureIgnoredOutsideReconnecting()
    { var s = InSession(); Assert.False(s.OnReconnectFailed().Reconnect); Assert.Equal(SessionState.InSession, s.State); }

    [Fact] public void LeaveAlwaysEndsOffline()
    { var s = InSession(); s.Leave(); Assert.Equal(SessionState.Offline, s.State); s.Leave(); Assert.Equal(SessionState.Offline, s.State); }

    [Fact] public void LeaveDuringReconnectCancelsIt()
    {
        var s = InSession(); s.OnDisconnected(DisconnectReason.Timeout); s.Leave();
        Assert.Equal(SessionState.Offline, s.State); Assert.False(s.OnReconnectFailed().Reconnect);
    }

    [Fact] public void PolicyRejectsBadArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReconnectPolicy(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReconnectPolicy().DelayFor(0));
    }
}
