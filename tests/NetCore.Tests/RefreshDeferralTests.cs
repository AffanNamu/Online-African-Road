using System.Collections.Generic;
using ARO.NetCore;
using Xunit;

/// <summary>A needed redraw waits for the pointer to be released, and always happens eventually (see RefreshDeferral).</summary>
public class RefreshDeferralTests
{
    [Fact] public void WithNoPointerDownARedrawRunsNow()
    {
        var d = new RefreshDeferral(3f);
        Assert.False(d.Defer("friends", () => { }, false, 10f));
        Assert.Equal(0, d.PendingCount);
    }

    [Fact] public void WhilePressedARedrawIsHeldAndRunsOnRelease()
    {
        var d = new RefreshDeferral(3f); var ran = new List<string>();
        Assert.True(d.Defer("friends", () => ran.Add("friends"), true, 10f));
        Assert.Equal(0, d.Flush(true, 10.2f)); Assert.Empty(ran);        // still pressed: nothing runs
        Assert.Equal(1, d.Flush(false, 10.4f)); Assert.Equal(new[] { "friends" }, ran);
        Assert.Equal(0, d.Flush(false, 10.5f)); Assert.Single(ran);      // it runs once
    }

    [Fact] public void TheNewestRequestPerKeyWins()
    {
        var d = new RefreshDeferral(3f); var ran = new List<string>();
        d.Defer("friends", () => ran.Add("old"), true, 1f); d.Defer("friends", () => ran.Add("new"), true, 1.1f);
        Assert.Equal(1, d.PendingCount);
        d.Flush(false, 1.5f);
        Assert.Equal(new[] { "new" }, ran);
    }

    [Fact] public void SeveralRegionsRunInTheOrderTheyWereFirstAsked()
    {
        var d = new RefreshDeferral(3f); var ran = new List<string>();
        d.Defer("FriMain", () => ran.Add("FriMain"), true, 1f); d.Defer("FriTabs", () => ran.Add("FriTabs"), true, 1f); d.Defer("FriMain", () => ran.Add("FriMain2"), true, 1.2f);
        d.Flush(false, 2f);
        Assert.Equal(new[] { "FriMain2", "FriTabs" }, ran);
    }

    [Fact] public void APressLongerThanTheCapStopsHoldingSoTheScreenCannotStayStale()
    {
        var d = new RefreshDeferral(3f); var ran = 0;
        Assert.True(d.Defer("friends", () => ran++, true, 10f));          // pressed at 10
        Assert.Equal(0, d.Flush(true, 12.9f)); Assert.Equal(0, ran);
        Assert.Equal(1, d.Flush(true, 13.1f)); Assert.Equal(1, ran);      // 3.1 s later the pointer is still reported down: apply anyway
        Assert.False(d.Defer("friends", () => ran++, true, 13.2f));       // and a new request is not held either
    }

    [Fact] public void ANewPressStartsANewCountdown()
    {
        var d = new RefreshDeferral(3f);
        d.Holding(true, 10f); d.Holding(false, 11f);                      // released
        Assert.True(d.Holding(true, 20f));                                // pressed again much later: holds again
        Assert.True(d.Holding(true, 22.9f)); Assert.False(d.Holding(true, 23.1f));
    }

    [Fact] public void ARedrawDoneForAnotherReasonCancelsTheHeldOne()
    {
        var d = new RefreshDeferral(3f); var ran = 0;
        d.Defer("friends", () => ran++, true, 1f); d.Cancel("friends");
        Assert.Equal(0, d.PendingCount); Assert.Equal(0, d.Flush(false, 2f)); Assert.Equal(0, ran);
        d.Cancel("nothing-held");                                          // harmless
    }

    [Fact] public void ARedrawThatAsksForAnotherWhileFlushingRunsItDirectlyBecauseNothingIsPressed()
    {
        var d = new RefreshDeferral(3f); int ran = 0; bool innerHeld = true;
        d.Defer("a", () => { ran++; innerHeld = d.Defer("b", () => ran += 10, false, 2f); }, true, 1f);
        Assert.Equal(1, d.Flush(false, 2f));
        Assert.False(innerHeld);                                           // the follow-up is told to run now: nothing is queued, nothing loops
        Assert.Equal(1, ran); Assert.Equal(0, d.PendingCount);
    }
}
