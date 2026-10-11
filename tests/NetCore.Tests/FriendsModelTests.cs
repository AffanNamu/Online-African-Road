using System;
using System.Linq;
using ARO.NetCore;
using Xunit;

public class FriendsModelTests
{
    static readonly DateTimeOffset Now = new DateTimeOffset(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);
    static PersonCard P(string name, int level = 1, bool online = false, string status = "friends", string activity = "", string route = "", string lastSeen = "", bool presence = true, string username = null) =>
        new PersonCard { Id = name.ToLower(), Name = name, Username = username ?? name.ToLower(), Level = level, Online = online, Status = status, Activity = activity, Route = route, LastSeen = lastSeen, HasPresence = presence };

    // ---- relation
    [Theory] [InlineData("friends", Relation.Friends)] [InlineData("outgoing", Relation.Outgoing)] [InlineData("incoming", Relation.Incoming)] [InlineData("self", Relation.Self)] [InlineData("none", Relation.None)] [InlineData("", Relation.None)] [InlineData(null, Relation.None)] [InlineData("weird", Relation.None)]
    public void RelationComesFromTheServersStatus(string s, Relation r) => Assert.Equal(r, FriendsRules.RelationOf(s));

    // ---- filtering and sorting
    [Fact] public void OnlineTabKeepsOnlyOnlineFriends() { var l = new[] { P("A", online: true), P("B"), P("C", online: true) }; Assert.Equal(new[] { "A", "C" }, FriendsRules.Filter(l, FriendsTab.Online, "").Select(c => c.Name)); }
    [Fact] public void AllTabKeepsEveryone() => Assert.Equal(3, FriendsRules.Filter(new[] { P("A", online: true), P("B"), P("C") }, FriendsTab.All, null).Count);
    [Fact] public void SearchMatchesNameAnyCase() => Assert.Equal(new[] { "KingTunde" }, FriendsRules.Filter(new[] { P("KingTunde"), P("MusaPro") }, FriendsTab.All, "tUNd").Select(c => c.Name));
    [Fact] public void SearchMatchesUsername() => Assert.Single(FriendsRules.Filter(new[] { P("A", username: "oga_driver"), P("B", username: "musa") }, FriendsTab.All, "oga_"));
    [Fact] public void SearchIgnoresSurroundingSpaces() => Assert.Single(FriendsRules.Filter(new[] { P("Musa"), P("Tunde") }, FriendsTab.All, "  musa  "));
    [Fact] public void SearchAndOnlineTabCombine() => Assert.Single(FriendsRules.Filter(new[] { P("Musa", online: true), P("Musab"), P("Tunde", online: true) }, FriendsTab.Online, "mus"));
    [Fact] public void NoMatchGivesAnEmptyListNotNull() => Assert.Empty(FriendsRules.Filter(new[] { P("A") }, FriendsTab.All, "zzz"));
    [Fact] public void NullInputGivesAnEmptyList() { Assert.Empty(FriendsRules.Filter(null, FriendsTab.All, "x")); Assert.Empty(FriendsRules.Sort(null, FriendSort.Name)); }
    [Fact] public void OnlineFirstThenByName() => Assert.Equal(new[] { "Bea", "Cy", "Al", "Di" }, FriendsRules.Sort(new[] { P("Al"), P("Bea", online: true), P("Cy", online: true), P("Di") }, FriendSort.OnlineFirst).Select(c => c.Name));
    [Fact] public void SortByNameIsCaseInsensitive() => Assert.Equal(new[] { "al", "Bea", "Cy" }, FriendsRules.Sort(new[] { P("Cy"), P("al"), P("Bea") }, FriendSort.Name).Select(c => c.Name));
    [Fact] public void SortByLevelHighestFirstTiesByName() => Assert.Equal(new[] { "Zed", "Al", "Bo" }, FriendsRules.Sort(new[] { P("Bo", 3), P("Zed", 9), P("Al", 3) }, FriendSort.Level).Select(c => c.Name));
    [Fact] public void RecentlyActiveListsOnlineThenNewestLastSeen() => Assert.Equal(new[] { "On", "New", "Old" }, FriendsRules.Sort(new[] { P("Old", lastSeen: "2026-10-01T00:00:00Z"), P("On", online: true), P("New", lastSeen: "2026-10-10T00:00:00Z") }, FriendSort.RecentlyActive).Select(c => c.Name));
    [Fact] public void SortDoesNotChangeTheInput() { var l = new[] { P("B"), P("A") }; FriendsRules.Sort(l, FriendSort.Name); Assert.Equal("B", l[0].Name); }
    [Fact] public void OnlineCount() => Assert.Equal(2, FriendsRules.OnlineCount(new[] { P("A", online: true), P("B"), P("C", online: true) }));
    [Fact] public void EverySortHasALabel() => Assert.All(Enum.GetValues(typeof(FriendSort)).Cast<FriendSort>(), s => Assert.False(string.IsNullOrEmpty(FriendsRules.SortLabel(s))));
    [Fact] public void CountTextShowsFilterResults() { Assert.Equal("28", FriendsRules.CountText(28, 28)); Assert.Equal("3 of 28", FriendsRules.CountText(3, 28)); }

    // ---- text
    [Fact] public void StatusTextFollowsPresence() { Assert.Equal("Online", FriendsRules.StatusText(P("A", online: true))); Assert.Equal("Offline", FriendsRules.StatusText(P("A"))); Assert.Equal("", FriendsRules.StatusText(P("A", presence: false))); }
    [Theory] [InlineData("driving", "lagos-ibadan", "Driving Lagos → Ibadan")] [InlineData("driving", "", "Driving")] [InlineData("menu", "", "In the menu")] [InlineData("idle", "", "Idle")]
    [InlineData("convoy", "lagos-ibadan", "In a convoy, Lagos → Ibadan")] [InlineData("bus", "", "Running a bus route")] [InlineData("surprise", "", "Online")]
    public void ActivityTextForAnOnlineFriend(string activity, string route, string expected) => Assert.Equal(expected, FriendsRules.ActivityText(P("A", online: true, activity: activity, route: route), Now));
    [Fact] public void ActivityUsesTheKnownRouteName() => Assert.Equal("Driving Lagos → Ibadan (128 km)", FriendsRules.ActivityText(P("A", online: true, activity: "driving", route: "lagos-ibadan"), Now, r => "Lagos → Ibadan (128 km)"));
    [Fact] public void ActivityFallsBackWhenTheRouteIsUnknown() => Assert.Equal("Driving Lagos → Ibadan", FriendsRules.ActivityText(P("A", online: true, activity: "driving", route: "lagos-ibadan"), Now, r => null));
    [Fact] public void OfflineSaysLastSeen() { Assert.Equal("Last seen 2 h ago", FriendsRules.ActivityText(P("A", lastSeen: "2026-10-11T10:00:00Z"), Now)); Assert.Equal("Last seen 5 d ago", FriendsRules.ActivityText(P("A", lastSeen: "2026-10-06T12:00:00Z"), Now)); }
    [Fact] public void OfflineWithoutLastSeenSaysNothing() => Assert.Equal("", FriendsRules.ActivityText(P("A"), Now));
    [Fact] public void NoPresenceSaysNothing() => Assert.Equal("", FriendsRules.ActivityText(P("A", online: true, activity: "driving", presence: false), Now));
    [Fact] public void UnparsableLastSeenIsNotInvented() => Assert.Equal("Last seen a while ago", FriendsRules.ActivityText(P("A", lastSeen: "garbage"), Now));
    [Fact] public void PlayedTogetherText() { var c = P("A", status: "none"); c.LastPlayed = "2026-10-11T10:00:00Z"; c.Times = 1; Assert.Equal("Played together 2 h ago", FriendsRules.PlayedText(c, Now)); c.Times = 4; Assert.Equal("Played together 2 h ago (4 times)", FriendsRules.PlayedText(c, Now)); c.LastPlayed = ""; Assert.Equal("", FriendsRules.PlayedText(c, Now)); }
    [Theory] [InlineData("lagos-ibadan", "Lagos \u2192 Ibadan")] [InlineData("benin_city-abuja", "Benin \u2192 City \u2192 Abuja")] [InlineData("", "")]
    public void PrettyRouteCode(string code, string expected) => Assert.Equal(expected, FriendsRules.Pretty(code));

    // ---- avatars
    [Theory] [InlineData("OgaDriver", "OG")] [InlineData("Oga Driver", "OD")] [InlineData("king_tunde", "KT")] [InlineData("X", "X")] [InlineData("", "?")] [InlineData("  ", "?")] [InlineData(null, "?")] [InlineData("a-b-c", "AB")]
    public void Initials(string name, string expected) => Assert.Equal(expected, FriendsRules.Initials(name));
    [Fact] public void AvatarHueIsStableAndInRange() { Assert.Equal(FriendsRules.AvatarHue("abc"), FriendsRules.AvatarHue("abc")); foreach (var id in new[] { "a", "b", "00000000-0000-0000-0000-000000000001", "", null, new string('z', 500) }) Assert.InRange(FriendsRules.AvatarHue(id), 0, 359); }
    [Fact] public void DifferentPlayersUsuallyGetDifferentHues() => Assert.True(Enumerable.Range(0, 20).Select(i => FriendsRules.AvatarHue("player-" + i)).Distinct().Count() > 10);

    // ---- usernames
    [Theory] [InlineData("Musa_Pro", "musa_pro")] [InlineData("  @Musa  ", "musa")] [InlineData("@@x", "@x")] [InlineData(null, "")]
    public void NormaliseUsername(string input, string expected) => Assert.Equal(expected, FriendsRules.NormaliseUsername(input));
    [Theory] [InlineData("musa", true)] [InlineData("@musa_9", true)] [InlineData("ab", false)] [InlineData("has space", false)] [InlineData("bad-dash", false)] [InlineData("", false)] [InlineData("a_very_long_username_over_24", false)] [InlineData("drop;table", false)]
    public void UsernameShape(string input, bool ok) => Assert.Equal(ok, FriendsRules.IsUsernameShape(input));

    // ---- actions
    static string Kinds(System.Collections.Generic.IEnumerable<ActionState> a) => string.Join(",", a.Select(x => x.Action));
    [Fact] public void AnOnlineFriendInAConvoyCanBeInvited() { var a = FriendsRules.RowActions(P("A", online: true), true); Assert.Equal("InviteConvoy,Message,More", Kinds(a)); Assert.True(a[0].Enabled); }
    [Fact] public void InviteExplainsWhenYouAreNotInAConvoy() { var a = FriendsRules.RowActions(P("A", online: true), false)[0]; Assert.False(a.Enabled); Assert.Contains("convoy", a.Reason); }
    [Fact] public void InviteExplainsWhenTheFriendIsOffline() { var a = FriendsRules.RowActions(P("Musa"), true)[0]; Assert.False(a.Enabled); Assert.Contains("Musa is offline", a.Reason); }
    [Fact] public void MessageIsNeverEnabledBecauseThereIsNoMessagingYet() => Assert.All(new[] { true, false }, inConvoy => { var m = FriendsRules.RowActions(P("A", online: true), inConvoy).First(x => x.Action == FriendAction.Message); Assert.False(m.Enabled); Assert.False(string.IsNullOrEmpty(m.Reason)); });
    [Fact] public void AStrangerCanBeAdded() => Assert.Equal("AddFriend,More", Kinds(FriendsRules.RowActions(P("A", status: "none"), true)));
    [Fact] public void APendingRequestIsShownAsSentAndCannotBeSentAgain() { var a = FriendsRules.RowActions(P("A", status: "outgoing"), true); Assert.Equal("RequestSent,More", Kinds(a)); Assert.False(a[0].Enabled); }
    [Fact] public void AnIncomingRequestCanBeAnsweredFromTheRow() => Assert.Equal("Accept,Decline", Kinds(FriendsRules.RowActions(P("A", status: "incoming"), false)));
    [Fact] public void YourselfHasNoActions() { Assert.Empty(FriendsRules.RowActions(P("A", status: "self"), true)); Assert.Equal("ViewProfile", Kinds(FriendsRules.MenuActions(P("A", status: "self")))); }
    [Fact] public void FriendMenuHasProfileRemoveBlock() => Assert.Equal("ViewProfile,RemoveFriend,BlockPlayer", Kinds(FriendsRules.MenuActions(P("A"))));
    [Fact] public void StrangerMenuHasProfileAddBlock() => Assert.Equal("ViewProfile,AddFriend,BlockPlayer", Kinds(FriendsRules.MenuActions(P("A", status: "none"))));
    [Fact] public void OutgoingMenuCanCancel() => Assert.Equal("ViewProfile,CancelRequest,BlockPlayer", Kinds(FriendsRules.MenuActions(P("A", status: "outgoing"))));
    [Fact] public void IncomingMenuCanAnswer() => Assert.Equal("ViewProfile,Accept,Decline,BlockPlayer", Kinds(FriendsRules.MenuActions(P("A", status: "incoming"))));
    [Fact] public void EveryActionHasALabel() => Assert.All(new[] { "friends", "none", "outgoing", "incoming", "self" }, s => { foreach (var a in FriendsRules.RowActions(P("A", online: true, status: s), true).Concat(FriendsRules.MenuActions(P("A", status: s)))) Assert.False(string.IsNullOrEmpty(a.Label)); });
    [Fact] public void ARelationChipExistsForEveryRelationExceptNone() { Assert.Equal("Friends", FriendsRules.RelationChip(P("A"))); Assert.Equal("Request sent", FriendsRules.RelationChip(P("A", status: "outgoing"))); Assert.Equal("", FriendsRules.RelationChip(P("A", status: "none"))); Assert.Equal("You", FriendsRules.RelationChip(P("A", status: "self"))); }

    // ---- long lists
    [Fact] public void VisibleRangeAtTheTop() { FriendsRules.VisibleRange(0, 700, 100, 100, 2, out int f, out int l); Assert.Equal(0, f); Assert.Equal(9, l); }
    [Fact] public void VisibleRangeInTheMiddle() { FriendsRules.VisibleRange(1000, 700, 100, 100, 2, out int f, out int l); Assert.Equal(8, f); Assert.Equal(19, l); }
    [Fact] public void VisibleRangeAtTheEnd() { FriendsRules.VisibleRange(9300, 700, 100, 100, 3, out int f, out int l); Assert.Equal(99, l); Assert.True(f >= 90); }
    [Fact] public void VisibleRangeOfAShortListIsTheWholeList() { FriendsRules.VisibleRange(0, 700, 100, 4, 2, out int f, out int l); Assert.Equal(0, f); Assert.Equal(3, l); }
    [Fact] public void VisibleRangeOfNothingIsNothing() { FriendsRules.VisibleRange(0, 700, 100, 0, 2, out int f, out int l); Assert.Equal(-1, f); Assert.Equal(-1, l); }
    [Fact] public void VisibleRangeNeverLeavesTheList() { foreach (float y in new[] { -50f, 0f, 55f, 99999f }) { FriendsRules.VisibleRange(y, 700, 100, 30, 2, out int f, out int l); Assert.InRange(f, 0, 29); Assert.InRange(l, 0, 29); Assert.True(f <= l); } }
    [Fact] public void ABuiltWindowIsMuchSmallerThanAHundredFriends() { FriendsRules.VisibleRange(2000, 700, 100, 100, 2, out int f, out int l); Assert.True(l - f + 1 <= 14); }
}
