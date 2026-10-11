using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ARO.NetCore
{
    // Pure rules for the Multiplayer UI. Nothing here talks to the network: it turns facts the SERVER and the live session reported into the states the
    // screens show, so a state on screen always corresponds to something that is actually true.

    /// <summary>What the player's convoy is doing, as far as this client can honestly say.</summary>
    public enum ConvoyUiState { NoConvoy, Creating, Searching, Joining, InLobby, ReadyCheck, Starting, InSession, Reconnecting, Disbanded }
    public enum MemberUiState { Invited, Joined, NotReady, Ready, Driving, Disconnected }
    /// <summary>Unavailable = no voice system is installed; the UI must then say so instead of showing any connection.</summary>
    public enum VoiceState { Unavailable, Disconnected, Connecting, Connected, Muted, Speaking }
    /// <summary>A server call this client has started and not yet seen the answer to.</summary>
    public enum PendingOp { None, Searching, Creating, Joining, Starting }

    public struct ConvoyFacts
    {
        public bool InConvoy;            // the server says the player is a member of a live convoy
        public bool WasInConvoy;         // the player was in one on the previous poll and did not leave on purpose
        public string Phase;             // "lobby" | "driving" (server)
        public bool AnyMemberReady;      // a member other than the leader has pressed Ready (server)
        public PendingOp Pending;        // a call of ours is in flight
        public SessionState Session;     // the live (Relay) session
    }

    public static class ConvoyStates
    {
        public static ConvoyUiState Derive(ConvoyFacts f)
        {
            switch (f.Pending)
            {
                case PendingOp.Creating: return ConvoyUiState.Creating;
                case PendingOp.Joining: return ConvoyUiState.Joining;
                case PendingOp.Starting: return ConvoyUiState.Starting;
                case PendingOp.Searching: if (!f.InConvoy) return ConvoyUiState.Searching; break;
            }
            if (!f.InConvoy) return f.WasInConvoy ? ConvoyUiState.Disbanded : ConvoyUiState.NoConvoy;
            if (f.Phase == "driving")
            {
                if (f.Session == SessionState.InSession) return ConvoyUiState.InSession;
                if (f.Session == SessionState.Reconnecting) return ConvoyUiState.Reconnecting;
                return ConvoyUiState.Starting;                 // the convoy is on the road but this player has not joined the live session (yet)
            }
            return f.AnyMemberReady ? ConvoyUiState.ReadyCheck : ConvoyUiState.InLobby;
        }

        public static string Label(ConvoyUiState s)
        {
            switch (s)
            {
                case ConvoyUiState.NoConvoy: return "No convoy"; case ConvoyUiState.Creating: return "Creating..."; case ConvoyUiState.Searching: return "Searching...";
                case ConvoyUiState.Joining: return "Joining..."; case ConvoyUiState.InLobby: return "In Lobby"; case ConvoyUiState.ReadyCheck: return "Ready Check";
                case ConvoyUiState.Starting: return "Starting..."; case ConvoyUiState.InSession: return "On the Road"; case ConvoyUiState.Reconnecting: return "Reconnecting...";
                case ConvoyUiState.Disbanded: return "Disbanded"; default: return s.ToString();
            }
        }

        public struct MemberFacts { public bool Invited, Ready, IsLeader, Connected; public double SecondsSinceJoin; }

        /// <summary>One member's state. A member who is offline is Disconnected whatever else is true; the leader counts as ready.</summary>
        public static MemberUiState Member(MemberFacts m, string phase, double justJoinedSeconds = 15.0)
        {
            if (m.Invited) return MemberUiState.Invited;
            if (!m.Connected) return MemberUiState.Disconnected;
            if (phase == "driving") return MemberUiState.Driving;
            if (m.IsLeader || m.Ready) return MemberUiState.Ready;
            return m.SecondsSinceJoin >= 0 && m.SecondsSinceJoin < justJoinedSeconds ? MemberUiState.Joined : MemberUiState.NotReady;
        }

        public static string Label(MemberUiState s)
        {
            switch (s)
            {
                case MemberUiState.Invited: return "Invited"; case MemberUiState.Joined: return "Joined"; case MemberUiState.NotReady: return "Not ready";
                case MemberUiState.Ready: return "Ready"; case MemberUiState.Driving: return "Driving"; case MemberUiState.Disconnected: return "Disconnected"; default: return s.ToString();
            }
        }

        public struct StartCheck { public bool Allowed; public string Reason; }

        /// <summary>
        /// The Start button's gate, for DISPLAY only (the server decides again): a route, no member still not ready, and nobody offline.
        /// </summary>
        public static StartCheck CanStart(bool isLeader, bool driving, bool hasRoute, int notReady, int disconnected)
        {
            if (driving) return new StartCheck { Allowed = false, Reason = "The convoy is already on the road." };
            if (!isLeader) return new StartCheck { Allowed = false, Reason = "Only the leader can start the convoy." };
            if (!hasRoute) return new StartCheck { Allowed = false, Reason = "Choose a route to start." };
            if (notReady > 0) return new StartCheck { Allowed = false, Reason = notReady == 1 ? "Waiting for 1 player to get ready..." : $"Waiting for {notReady} players to get ready..." };
            if (disconnected > 0) return new StartCheck { Allowed = false, Reason = disconnected == 1 ? "1 member is offline." : $"{disconnected} members are offline." };
            return new StartCheck { Allowed = true, Reason = "Start Driving" };
        }
    }

    // ------------------------------------------------------------------------------------------------------------------ voice
    /// <summary>The seam a real voice service plugs into. The UI asks it; it never assumes.</summary>
    public interface IVoiceProvider
    {
        bool IsAvailable { get; }
        VoiceState Local { get; }
        VoiceState StateOf(string playerId);
    }

    /// <summary>No voice system is installed: every state is Unavailable, so the UI can never display "Voice Connected".</summary>
    public sealed class NullVoiceProvider : IVoiceProvider
    {
        public bool IsAvailable => false;
        public VoiceState Local => VoiceState.Unavailable;
        public VoiceState StateOf(string playerId) => VoiceState.Unavailable;
    }

    public static class VoiceText
    {
        public static string Label(VoiceState s)
        {
            switch (s)
            {
                case VoiceState.Connected: return "Connected"; case VoiceState.Connecting: return "Connecting..."; case VoiceState.Muted: return "Muted"; case VoiceState.Speaking: return "Speaking";
                case VoiceState.Disconnected: return "Disconnected"; default: return "Not available";
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------ chat content
    public static class ChatText
    {
        public const int MaxLength = 200;

        /// <summary>
        /// What may enter the UI from a chat line: no control characters, no angle brackets (the UI would read them as markup), collapsed spaces, bounded length.
        /// The server already applies the same rule; this is the second wall.
        /// </summary>
        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(Math.Min(s.Length, MaxLength + 8)); bool space = false;
            foreach (char c in s)
            {
                if (c == '<' || c == '>') continue;
                if (char.IsControl(c) || char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(c);
                if (sb.Length >= MaxLength) break;
            }
            return sb.ToString();
        }

        /// <summary>The text to show for a line, given the server's moderation state. A hidden or removed line shows a notice, never its text.</summary>
        public static string Display(string body, string moderation)
        {
            if (moderation == "hidden") return "This message was hidden after reports.";
            if (moderation == "removed") return "This message was removed by a moderator.";
            return Clean(body);
        }
    }

    // ------------------------------------------------------------------------------------------------------------------ in-session geometry
    /// <summary>Distances and route progress from POSITIONS THE SESSION REPORTED (never invented, never stored in the database).</summary>
    public static class ConvoyMath
    {
        public static double Distance(double ax, double az, double bx, double bz) { double dx = ax - bx, dz = az - bz; return Math.Sqrt(dx * dx + dz * dz); }

        /// <summary>Fraction (0..1) of the way along a polyline that the point's nearest projection lies at. Null when the polyline is unusable.</summary>
        public static double? RouteProgress(IReadOnlyList<(double x, double z)> line, double px, double pz)
        {
            if (line == null || line.Count < 2) return null;
            double total = 0; var seg = new double[line.Count - 1];
            for (int i = 0; i < seg.Length; i++) { seg[i] = Distance(line[i].x, line[i].z, line[i + 1].x, line[i + 1].z); total += seg[i]; }
            if (total <= 0) return null;
            double best = double.MaxValue, bestAlong = 0, acc = 0;
            for (int i = 0; i < seg.Length; i++)
            {
                double dx = line[i + 1].x - line[i].x, dz = line[i + 1].z - line[i].z, len2 = dx * dx + dz * dz;
                double t = len2 <= 0 ? 0 : Math.Max(0, Math.Min(1, ((px - line[i].x) * dx + (pz - line[i].z) * dz) / len2));
                double qx = line[i].x + dx * t, qz = line[i].z + dz * t, d = Distance(px, pz, qx, qz);
                if (d < best) { best = d; bestAlong = acc + seg[i] * t; }
                acc += seg[i];
            }
            return bestAlong / total;
        }

        /// <summary>Largest distance between any two of the points (how spread out the convoy is). 0 for fewer than two.</summary>
        public static double Spread(IReadOnlyList<(double x, double z)> pts)
        {
            double m = 0;
            if (pts == null) return 0;
            for (int i = 0; i < pts.Count; i++) for (int j = i + 1; j < pts.Count; j++) m = Math.Max(m, Distance(pts[i].x, pts[i].z, pts[j].x, pts[j].z));
            return m;
        }
    }

    // ------------------------------------------------------------------------------------------------------------------ in-session roster
    public struct LiveMember
    {
        public string Name; public bool IsLeader; public bool Present;       // Present = the member's avatar is in the live session right now
        public double? DistanceToLeaderM; public double? RouteProgress;     // null when the position is not known
        public MemberUiState State;
    }

    public static class LiveRoster
    {
        /// <summary>
        /// Joins the SERVER's member list (who belongs to the convoy) with the LIVE session's avatars (who is actually in the world).
        /// A member with no avatar is Disconnected; positions come only from avatars that exist. Names are matched case-insensitively (avatar names are self-declared).
        /// </summary>
        public static List<LiveMember> Build(IEnumerable<(string name, bool isLeader, bool connectedServer)> members, IReadOnlyDictionary<string, (double x, double z)> avatars,
                                             IReadOnlyList<(double x, double z)> routeLine, string phase)
        {
            var map = new Dictionary<string, (double x, double z)>(StringComparer.OrdinalIgnoreCase);
            if (avatars != null) foreach (var kv in avatars) map[kv.Key] = kv.Value;
            var list = members.ToList();
            (double x, double z)? leader = null;
            foreach (var m in list) if (m.isLeader && map.TryGetValue(m.name, out var lp)) leader = lp;
            var rows = new List<LiveMember>();
            foreach (var m in list)
            {
                bool present = map.TryGetValue(m.name, out var pos);
                rows.Add(new LiveMember
                {
                    Name = m.name, IsLeader = m.isLeader, Present = present,
                    DistanceToLeaderM = present && leader.HasValue && !m.isLeader ? ConvoyMath.Distance(pos.x, pos.z, leader.Value.x, leader.Value.z) : (double?)null,
                    RouteProgress = present ? ConvoyMath.RouteProgress(routeLine, pos.x, pos.z) : null,
                    State = present ? (phase == "driving" ? MemberUiState.Driving : MemberUiState.Ready) : MemberUiState.Disconnected,
                });
            }
            return rows;
        }
    }
}
