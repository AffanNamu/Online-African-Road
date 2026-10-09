using ARO.NetCore;
using Xunit;

public class RosterTests
{
    [Fact] public void LeaderIsEarliestJoiner()
    {
        var r = new Roster(); r.Join("a", "Ada"); r.Join("b", "Bola"); r.Join("c", "Chi");
        Assert.Equal("a", r.Leader.Id);
    }
    [Fact] public void LeadershipTransfersToLongestStandingMember()
    {
        var r = new Roster(); r.Join("a", "Ada"); r.Join("b", "Bola"); r.Join("c", "Chi");
        r.Leave("a"); Assert.Equal("b", r.Leader.Id); r.Leave("b"); Assert.Equal("c", r.Leader.Id);
    }
    [Fact] public void RejoinDoesNotJumpTheQueue()
    {
        var r = new Roster(); r.Join("a", "Ada"); r.Join("b", "Bola");
        Assert.False(r.Join("b", "Bola")); Assert.Equal(2, r.Count); Assert.Equal("a", r.Leader.Id);
    }
    [Fact] public void CapacityIsEnforced()
    {
        var r = new Roster(2); Assert.True(r.Join("a", "A")); Assert.True(r.Join("b", "B"));
        Assert.False(r.Join("c", "C")); Assert.Equal(2, r.Count);
    }
    [Fact] public void ChangedEventFiresOnRealChangesOnly()
    {
        var r = new Roster(); int n = 0; r.Changed += () => n++;
        r.Join("a", "Ada"); r.Join("a", "Ada"); r.Leave("zzz"); r.Leave("a"); r.Clear();
        Assert.Equal(2, n);
    }
    [Fact] public void BlankNamesGetDefaultAndEmptyIdsRejected()
    {
        var r = new Roster(); Assert.True(r.Join("a", "  ")); Assert.Equal("Driver", r.Members[0].Name);
        Assert.False(r.Join("", "x")); Assert.False(r.Join(null, "x"));
    }
    [Fact] public void MembersOrderedByJoin()
    {
        var r = new Roster(); r.Join("z", "Z"); r.Join("a", "A");
        Assert.Equal(new[] { "z", "a" }, new[] { r.Members[0].Id, r.Members[1].Id });
    }
}
