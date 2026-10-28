using System.Collections.Generic;
using ARO.NetCore;
using Xunit;

public class RedrawQueueTests
{
    [Fact] public void RedrawsComeOutOldestKeyFirstAndTheQueueIsEmptyAfterwards()
    {
        var q = new RedrawQueue(); var ran = new List<string>();
        q.Add("a", () => ran.Add("a")); q.Add("b", () => ran.Add("b")); q.Add("c", () => ran.Add("c"));
        Assert.Equal(3, q.Count);
        foreach (var a in q.Take()) a();
        Assert.Equal(new[] { "a", "b", "c" }, ran); Assert.Equal(0, q.Count); Assert.Empty(q.Take());
    }

    [Fact] public void ARepeatedKeyKeepsItsPlaceButRunsTheNewestRedraw()
    {
        var q = new RedrawQueue(); var ran = new List<string>();
        q.Add("a", () => ran.Add("a1")); q.Add("b", () => ran.Add("b")); q.Add("a", () => ran.Add("a2"));
        foreach (var a in q.Take()) a();
        Assert.Equal(new[] { "a2", "b" }, ran);
    }

    [Fact] public void ACancelledKeyDoesNotRunAndCanBeQueuedAgain()
    {
        var q = new RedrawQueue(); var ran = new List<string>();
        q.Add("a", () => ran.Add("a")); q.Add("b", () => ran.Add("b")); q.Cancel("a"); q.Cancel("never-queued");
        Assert.Equal(1, q.Count);
        q.Add("a", () => ran.Add("a-again"));
        foreach (var a in q.Take()) a();
        Assert.Equal(new[] { "b", "a-again" }, ran);
    }

    [Fact] public void RedrawsQueuedWhileTheTakenOnesRunBelongToTheNextFrame()
    {
        var q = new RedrawQueue(); var ran = new List<string>();
        q.Add("a", () => { ran.Add("a"); q.Add("nested", () => ran.Add("nested")); });
        foreach (var a in q.Take()) a();
        Assert.Equal(new[] { "a" }, ran); Assert.Equal(1, q.Count);
        foreach (var a in q.Take()) a();
        Assert.Equal(new[] { "a", "nested" }, ran);
    }

    [Fact] public void ClearDropsEverything()
    {
        var q = new RedrawQueue(); q.Add("a", () => { }); q.Clear(); Assert.Equal(0, q.Count); Assert.Empty(q.Take());
    }
}
