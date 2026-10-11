using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ARO.NetCore
{
    // ===================================================================================================== Friends: pure rules (no Unity types, unit tested)
    public enum FriendsTab { All, Online, Requests, Recent, Find }
    public enum FriendSort { OnlineFirst, Name, Level, RecentlyActive }
    /// <summary>What the server says this player is to the viewer (the backend's friend_status, never guessed).</summary>
    public enum Relation { None, Friends, Outgoing, Incoming, Self }
    public enum FriendAction { InviteConvoy, Message, More, AddFriend, RequestSent, Accept, Decline, CancelRequest, ViewProfile, RemoveFriend, BlockPlayer, AlreadyFriends }

    /// <summary>One public player card as the screen shows it. Filled from the backend's player cards; every field is what the server sent (empty when it did not).</summary>
    public sealed class PersonCard
    {
        public string Id = "", Name = "", Username = "", Country = "", Status = "none", Activity = "", Route = "", City = "", LastSeen = "", Vehicle = "", VehicleId = "", Since = "", LastPlayed = "", RequestId = "";
        public int Level, Times;
        public bool Online, HasPresence;
        public Relation Relation => FriendsRules.RelationOf(Status);
    }

    public sealed class ActionState
    {
        public FriendAction Action; public string Label; public bool Enabled = true; public string Reason;     // Reason: why a disabled action cannot be used (shown on tap)
    }

    public static class FriendsRules
    {
        public static Relation RelationOf(string friendStatus)
        {
            switch (friendStatus) { case "friends": return Relation.Friends; case "outgoing": return Relation.Outgoing; case "incoming": return Relation.Incoming; case "self": return Relation.Self; default: return Relation.None; }
        }

        // ---------------------------------------------------------------- lists
        public static string Norm(string s) => (s ?? "").Trim().ToLowerInvariant();

        /// <summary>The cards a tab shows, narrowed by the search text (name or username contains it, any case). Requests and Find are not card lists.</summary>
        public static List<PersonCard> Filter(IEnumerable<PersonCard> cards, FriendsTab tab, string query)
        {
            string q = Norm(query); var src = (cards ?? new PersonCard[0]).Where(c => c != null);
            if (tab == FriendsTab.Online) src = src.Where(c => c.Online);
            if (q.Length > 0) src = src.Where(c => Norm(c.Name).Contains(q) || Norm(c.Username).Contains(q));
            return src.ToList();
        }

        public static List<PersonCard> Sort(IEnumerable<PersonCard> cards, FriendSort sort)
        {
            var src = (cards ?? new PersonCard[0]).Where(c => c != null);
            switch (sort)
            {
                case FriendSort.Name: return src.OrderBy(c => Norm(c.Name), StringComparer.Ordinal).ToList();
                case FriendSort.Level: return src.OrderByDescending(c => c.Level).ThenBy(c => Norm(c.Name), StringComparer.Ordinal).ToList();
                case FriendSort.RecentlyActive: return src.OrderByDescending(c => c.Online).ThenByDescending(c => c.LastSeen ?? "", StringComparer.Ordinal).ThenBy(c => Norm(c.Name), StringComparer.Ordinal).ToList();
                default: return src.OrderByDescending(c => c.Online).ThenBy(c => Norm(c.Name), StringComparer.Ordinal).ToList();
            }
        }
        public static string SortLabel(FriendSort s) => s == FriendSort.Name ? "Name (A-Z)" : s == FriendSort.Level ? "Level" : s == FriendSort.RecentlyActive ? "Recently Active" : "Online First";

        public static int OnlineCount(IEnumerable<PersonCard> cards) => (cards ?? new PersonCard[0]).Count(c => c != null && c.Online);

        // ---------------------------------------------------------------- text
        public static string StatusText(PersonCard c) => c.HasPresence ? (c.Online ? "Online" : "Offline") : "";

        /// <summary>What an online friend is doing, from the presence the server shares (menu, driving, bus, convoy, idle); an offline one says when they were last seen. Empty when the server shares nothing.</summary>
        public static string ActivityText(PersonCard c, DateTimeOffset serverNow, Func<string, string> routeName = null)
        {
            if (!c.HasPresence) return "";
            if (!c.Online) return string.IsNullOrEmpty(c.LastSeen) ? "" : "Last seen " + AgoOf(c.LastSeen, serverNow);
            string route = string.IsNullOrEmpty(c.Route) ? "" : (routeName != null ? routeName(c.Route) : null) ?? Pretty(c.Route);
            switch (c.Activity)
            {
                case "driving": return route.Length > 0 ? "Driving " + route : "Driving";
                case "bus": return route.Length > 0 ? "Running a bus route, " + route : "Running a bus route";
                case "convoy": return route.Length > 0 ? "In a convoy, " + route : "In a convoy";
                case "idle": return "Idle";
                case "menu": return "In the menu";
                default: return "Online";
            }
        }

        public static string AgoOf(string iso, DateTimeOffset now) => TimeText.TryParse(iso, out var t) ? TimeText.Ago(t, now) : "a while ago";
        public static string PlayedText(PersonCard c, DateTimeOffset serverNow)
        {
            if (string.IsNullOrEmpty(c.LastPlayed)) return "";
            string s = "Played together " + AgoOf(c.LastPlayed, serverNow); if (c.Times > 1) s += " (" + c.Times + " times)"; return s;
        }
        /// <summary>"lagos-ibadan" -> "Lagos - Ibadan" when no better name is known.</summary>
        public static string Pretty(string code) => string.Join(" → ", (code ?? "").Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));

        // ---------------------------------------------------------------- avatars (no photo exists for a player: initials on a colour that depends on the player id)
        public static string Initials(string name)
        {
            var parts = Regex.Split((name ?? "").Trim(), @"[\s_\-.]+").Where(p => p.Length > 0).ToArray();
            if (parts.Length == 0) return "?";
            string a = parts[0].Substring(0, 1), b = parts.Length > 1 ? parts[1].Substring(0, 1) : (parts[0].Length > 1 ? parts[0].Substring(1, 1) : "");
            return (a + b).ToUpperInvariant();
        }
        /// <summary>A stable hue 0..359 per player id, so the same player always has the same colour.</summary>
        public static int AvatarHue(string id)
        {
            unchecked { int h = 17; foreach (char ch in id ?? "") h = h * 31 + ch; return Math.Abs(h % 360); }
        }

        // ---------------------------------------------------------------- finding players (the server answers an exact username only)
        static readonly Regex UsernameShape = new Regex("^[a-z0-9_]{3,24}$", RegexOptions.Compiled);
        public static string NormaliseUsername(string input) { var s = Norm(input); return s.StartsWith("@") ? s.Substring(1) : s; }
        public static bool IsUsernameShape(string input) => UsernameShape.IsMatch(NormaliseUsername(input));

        // ---------------------------------------------------------------- what can be done with a card
        /// <summary>The primary actions of a card in a friends or recent list. Disabled ones carry the reason; nothing is offered that the backend would refuse for a known reason.</summary>
        public static List<ActionState> RowActions(PersonCard c, bool viewerInConvoy)
        {
            var l = new List<ActionState>();
            switch (c.Relation)
            {
                case Relation.Friends:
                    l.Add(new ActionState { Action = FriendAction.InviteConvoy, Label = "Invite Convoy", Enabled = c.Online && viewerInConvoy, Reason = !viewerInConvoy ? "Join or create a convoy first, then invite your friends." : !c.Online ? c.Name + " is offline." : null });
                    l.Add(new ActionState { Action = FriendAction.Message, Label = "Message", Enabled = false, Reason = "Messages are not available yet. Friends can be invited to a convoy and talk in its chat." });
                    l.Add(new ActionState { Action = FriendAction.More, Label = "...", Enabled = true });
                    break;
                case Relation.None: l.Add(new ActionState { Action = FriendAction.AddFriend, Label = "Add Friend", Enabled = true }); l.Add(new ActionState { Action = FriendAction.More, Label = "...", Enabled = true }); break;
                case Relation.Outgoing: l.Add(new ActionState { Action = FriendAction.RequestSent, Label = "Request Sent", Enabled = false, Reason = "Waiting for " + c.Name + " to answer." }); l.Add(new ActionState { Action = FriendAction.More, Label = "...", Enabled = true }); break;
                case Relation.Incoming: l.Add(new ActionState { Action = FriendAction.Accept, Label = "Accept", Enabled = true }); l.Add(new ActionState { Action = FriendAction.Decline, Label = "Decline", Enabled = true }); break;
                case Relation.Self: break;
            }
            return l;
        }

        /// <summary>The actions in the ... menu / bottom sheet of a card.</summary>
        public static List<ActionState> MenuActions(PersonCard c)
        {
            var l = new List<ActionState> { new ActionState { Action = FriendAction.ViewProfile, Label = "View Profile" } };
            if (c.Relation == Relation.Friends) l.Add(new ActionState { Action = FriendAction.RemoveFriend, Label = "Remove Friend" });
            else if (c.Relation == Relation.Outgoing) l.Add(new ActionState { Action = FriendAction.CancelRequest, Label = "Cancel Request" });
            else if (c.Relation == Relation.Incoming) { l.Add(new ActionState { Action = FriendAction.Accept, Label = "Accept" }); l.Add(new ActionState { Action = FriendAction.Decline, Label = "Decline" }); }
            else if (c.Relation == Relation.None) l.Add(new ActionState { Action = FriendAction.AddFriend, Label = "Add Friend" });
            if (c.Relation != Relation.Self) l.Add(new ActionState { Action = FriendAction.BlockPlayer, Label = "Block Player" });
            return l;
        }

        /// <summary>The chip shown on a card whose action is not an action: already friends, request pending, that is you.</summary>
        public static string RelationChip(PersonCard c) => c.Relation == Relation.Friends ? "Friends" : c.Relation == Relation.Outgoing ? "Request sent" : c.Relation == Relation.Incoming ? "Wants to be your friend" : c.Relation == Relation.Self ? "You" : "";

        // ---------------------------------------------------------------- long lists: only the rows near the window are built
        /// <summary>Indices of the rows to build for a scroll position: those in view plus a buffer on both sides. (-1, -1) when there is nothing to show.</summary>
        public static void VisibleRange(float scrollY, float viewH, float rowPitch, int count, int buffer, out int first, out int last)
        {
            if (count <= 0 || rowPitch <= 0f) { first = last = -1; return; }
            first = Math.Max(0, (int)Math.Floor(scrollY / rowPitch) - buffer);
            last = Math.Min(count - 1, (int)Math.Ceiling((scrollY + viewH) / rowPitch) + buffer);
            if (first > last) first = last = Math.Max(0, Math.Min(count - 1, first));
        }

        /// <summary>The one-line summary of a count that may be a filter result: "28", or "3 of 28" while searching.</summary>
        public static string CountText(int shown, int total) => shown == total ? total.ToString() : shown + " of " + total;
    }
}
