using ARO.NetCore;
using Xunit;

/// <summary>The decision that stops a background reload from destroying and recreating buttons the player is about to click (see RefreshGate).</summary>
public class RefreshGateTests
{
    [Fact] public void TheFirstReloadRedrawsAndAnIdenticalOneDoesNot()
    {
        var g = new RefreshGate();
        Assert.True(g.Changed("friends", "a"));          // nothing drawn yet
        Assert.False(g.Changed("friends", "a"));         // the 15-second reload brought nothing new
        Assert.False(g.Changed("friends", "a"));         // and again
    }

    [Fact] public void ChangedDataRedrawsOnce()
    {
        var g = new RefreshGate(); g.Changed("friends", "a");
        Assert.True(g.Changed("friends", "b"));          // a request arrived
        Assert.False(g.Changed("friends", "b"));
        Assert.True(g.Changed("friends", "a"));          // and went away again: the screen must follow
    }

    [Fact] public void ARedrawForAnotherReasonIsRemembered()
    {
        var g = new RefreshGate(); g.Changed("garage", "a");
        g.Drew("garage", "b");                            // a Repair redrew the page with the new data
        Assert.False(g.Changed("garage", "b"));          // the reload that follows has nothing to add
        Assert.True(g.Changed("garage", "c"));
    }

    [Fact] public void PagesAreIndependent()
    {
        var g = new RefreshGate(); g.Changed("friends", "x");
        Assert.True(g.Changed("garage", "x"));           // the same key on another page is still new there
        Assert.False(g.Changed("friends", "x"));
    }

    [Fact] public void AnUnknownKeyAlwaysRedraws()
    {
        var g = new RefreshGate(); g.Changed("profile", "a");
        Assert.True(g.Changed("profile", null));         // the key could not be built: never risk a stale screen
        Assert.True(g.Changed("profile", null));
        Assert.True(g.Changed("profile", "a"));          // forgotten, so the known key redraws too
    }

    [Fact] public void ForgettingAPageRedrawsItNextTime()
    {
        var g = new RefreshGate(); g.Changed("friends", "a"); g.Forget("friends");
        Assert.True(g.Changed("friends", "a"));
    }

    [Fact] public void SeveralReloadsFinishingTogetherRedrawOnlyOnce()
    {   // the friends page runs three reloads at once (friends, requests, recent); each finishing asks the gate with the same combined key
        var g = new RefreshGate(); g.Changed("friends", "old");
        string key = RefreshGate.Key("friends:3", "requests:1", "recent:5");
        int redraws = 0; for (int i = 0; i < 4; i++) if (g.Changed("friends", key)) redraws++;
        Assert.Equal(1, redraws);
    }

    [Fact] public void AKeyWithAnUnknownPartIsUnknown()
    {
        Assert.Null(RefreshGate.Key("a", null, "c"));
        Assert.Equal("a\u001fb", RefreshGate.Key("a", "b"));
    }

    [Fact] public void ALoadThatFinishesAfterANewerOneStartedIsStale()
    {
        var l = new Loadable<int[]>();
        l.Begin(); int first = l.Generation;              // the 45-second reload starts
        l.Begin(); int second = l.Generation;             // the player repairs; the page reloads again
        Assert.False(l.IsCurrent(first));                 // the older answer, arriving late, must not overwrite the newer one
        Assert.True(l.IsCurrent(second));
        l.Succeed(new[] { 2 }, false, 5);
        Assert.Equal(new[] { 2 }, l.Value);
    }

    [Fact] public void ALoadWithNothingNewerIsCurrent()
    {
        var l = new Loadable<int>(); l.Begin();
        Assert.True(l.IsCurrent(l.Generation));
    }
}
