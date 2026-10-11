using System.Threading.Tasks;
using ARO.Backend;
using ARO.Core;
using ARO.NetCore;
using UnityEngine;

namespace ARO.Multiplayer
{
    /// <summary>
    /// Convoy = durable social record in Supabase (membership, leader, capacity, privacy, invites; the join code stays behind the membership
    /// check) + a live Relay session in Unity. Movement is never sent through Supabase. Membership and permissions are enforced by the server:
    /// this class only asks and reports.
    /// </summary>
    public class ConvoyService
    {
        readonly SupabaseClient _api; readonly SessionService _session;
        public string CurrentConvoyId { get; private set; }
        public string CurrentConvoyName { get; private set; }
        /// <summary>The caller's convoy as the server last reported it (Exists = false when they are in none).</summary>
        public readonly Loadable<MyConvoy> Mine = new Loadable<MyConvoy>();
        public ConvoyService(SupabaseClient api, SessionService session) { _api = api; _session = session; }
        public SessionService Session => _session;

        public Task<Result<OpenConvoyList>> ListOpen(int limit = 20, string search = null, string tag = null) =>
            _api.Call<OpenConvoyList>("list_open_convoys", JsonArgs.Obj(("p_limit", limit), ("p_offset", 0), ("p_search", string.IsNullOrWhiteSpace(search) ? null : search.Trim()), ("p_tag", string.IsNullOrEmpty(tag) ? null : tag)));
        public Task<Result<ActivePlayerList>> ActivePlayers(int limit = 30, string search = null) =>
            _api.Call<ActivePlayerList>("list_active_players", JsonArgs.Obj(("p_limit", limit), ("p_offset", 0), ("p_search", string.IsNullOrWhiteSpace(search) ? null : search.Trim())));
        public Task<Result<ServerStatus>> Status() => _api.Call<ServerStatus>("get_server_status");

        // ---- lobby (every one of these is decided by the server; the reply is the convoy as the server now sees it)
        public Task<Result<ConvoyInfo>> SetRoute(string routeCode) => _api.Call<ConvoyInfo>("set_convoy_route", JsonArgs.Obj(("p_route", routeCode)), idempotent: false);
        public Task<Result<ConvoyInfo>> SetTags(string[] tags, string language = null) => _api.Call<ConvoyInfo>("set_convoy_tags", JsonArgs.Obj(("p_tags", tags), ("p_language", language)), idempotent: false);
        public Task<Result<ConvoyInfo>> SetReady(bool ready) => _api.Call<ConvoyInfo>("set_my_ready", JsonArgs.Obj(("p_ready", ready)), idempotent: false);
        public Task<Result<ChatPage>> Messages(long after = 0, int limit = 50) => _api.Call<ChatPage>("list_convoy_messages", JsonArgs.Obj(("p_after", after), ("p_limit", limit)));
        public Task<Result<ChatPosted>> Post(string body) => _api.Call<ChatPosted>("post_convoy_message", JsonArgs.Obj(("p_body", body)), idempotent: false);

        /// <summary>
        /// Leader: the server checks the ready check, then the live (Relay) session is created and its code stored for members. If the live session cannot be
        /// created the convoy goes back to the lobby, so it never sits "driving" with nobody able to join.
        /// </summary>
        public async Task<Result<ConvoyInfo>> StartDriving()
        {
            var r = await _api.Call<ConvoyInfo>("start_convoy_drive", idempotent: false);
            if (!r.Ok) return r;
            string code = await _session.CreateAsync(r.Value.name);
            if (code == null)
            {
                await _api.Call<ConvoyInfo>("end_convoy_drive", idempotent: false);
                return Result<ConvoyInfo>.Fail("session_failed", _session.Message ?? "Could not start the multiplayer session. The convoy is back in the lobby.");
            }
            var s = await _api.Rpc("set_convoy_session_code", JsonArgs.Obj(("p_convoy", r.Value.id), ("p_code", code)), idempotent: false);
            if (!s.Ok) { await _session.LeaveAsync(); await _api.Call<ConvoyInfo>("end_convoy_drive", idempotent: false); return Result<ConvoyInfo>.Fail(s.ErrorCode, s.UserMessage); }
            return r;
        }

        /// <summary>Member: fetch the code the server releases to members only, and join the live session.</summary>
        public async Task<Result<bool>> JoinLiveSession(string convoyId)
        {
            var c = await _api.Rpc("get_convoy_session_code", JsonArgs.Obj(("p_convoy", convoyId)), idempotent: true);
            string code = c.Ok ? c.Value.Trim('"') : null;
            if (string.IsNullOrEmpty(code) || code == "null") return Result<bool>.Fail("no_session", "The leader's session is not ready yet. Try again in a moment.");
            if (!await _session.JoinByCodeAsync(code)) return Result<bool>.Fail("session_failed", _session.Message ?? "Could not join the session.");
            return Result<bool>.Success(true);
        }

        public Task<Result<ConvoyInfo>> EndDriving() => _api.Call<ConvoyInfo>("end_convoy_drive", idempotent: false);
        /// <summary>Create the durable lobby (no live session yet: that is created when the leader starts driving).</summary>
        public async Task<Result<string>> CreateLobby(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 3) return Result<string>.Fail("invalid_name", "Convoy names need at least 3 characters.");
            var r = await _api.Rpc("create_convoy", JsonArgs.Obj(("p_name", name.Trim())), idempotent: false);
            if (!r.Ok) return r;
            CurrentConvoyId = r.Value.Trim('"'); CurrentConvoyName = name.Trim();
            return Result<string>.Success(CurrentConvoyId);
        }
        /// <summary>Join the durable lobby only (members join the live session when the convoy starts driving).</summary>
        public async Task<Result<string>> JoinLobby(string convoyId, string convoyName)
        {
            var j = await _api.Rpc("join_convoy", JsonArgs.Obj(("p_convoy", convoyId)), idempotent: false);
            if (j.Ok) { CurrentConvoyId = convoyId; CurrentConvoyName = convoyName; }
            return j;
        }

        /// <summary>Server truth about my convoy; also refreshes CurrentConvoyId/Name so the older screens stay in step.</summary>
        public async Task<Result<MyConvoy>> LoadMine()
        {
            Mine.Begin();
            var r = await _api.Call<MyConvoy>("get_my_convoy");
            if (!r.Ok) { Mine.Fail(ServiceBase.KindOf(r.ErrorCode), r.UserMessage, Time.unscaledTime); return r; }
            bool has = r.Value.convoy != null && r.Value.convoy.Exists;
            CurrentConvoyId = has ? r.Value.convoy.id : null; CurrentConvoyName = has ? r.Value.convoy.name : null;
            Mine.Succeed(r.Value, !has, Time.unscaledTime);
            return r;
        }

        public Task<Result<ConvoyInviteList>> Invites() => _api.Call<ConvoyInviteList>("list_convoy_invites");
        public Task<Result<string>> Invite(string playerId) => _api.CallText("invite_to_convoy", JsonArgs.Obj(("p_player", playerId)));
        public Task<Result<string>> RespondToInvite(string inviteId, bool accept) => _api.CallText("respond_convoy_invite", JsonArgs.Obj(("p_invite", inviteId), ("p_accept", accept)));
        public Task<Result<string>> Kick(string playerId) => _api.Rpc("kick_convoy_member", JsonArgs.Obj(("p_player", playerId)));
        public Task<Result<string>> TransferLeader(string playerId) => _api.Rpc("transfer_convoy_leader", JsonArgs.Obj(("p_player", playerId)));
        public Task<Result<ConvoyInfo>> SetOptions(int? capacity, bool? isPrivate) =>
            _api.Call<ConvoyInfo>("set_convoy_options", JsonArgs.Obj(("p_capacity", capacity.HasValue ? (object)capacity.Value : null), ("p_private", isPrivate.HasValue ? (object)isPrivate.Value : null)), idempotent: false);

        /// <summary>Create the live session first (so we have a code), then record the convoy. Rolls the session back if the record fails.</summary>
        public async Task<Result<string>> Create(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 3) return Result<string>.Fail("invalid_name", "Convoy names need at least 3 characters.");
            string code = await _session.CreateAsync(name.Trim());
            if (code == null) return Result<string>.Fail("session_failed", _session.Message ?? "Could not start a multiplayer session.");
            var r = await _api.Rpc("create_convoy", JsonArgs.Obj(("p_name", name.Trim()), ("p_session_code", code)));
            if (!r.Ok) { await _session.LeaveAsync(); return Result<string>.Fail(r.ErrorCode, r.UserMessage); }
            CurrentConvoyId = r.Value.Trim('"'); CurrentConvoyName = name.Trim();
            return Result<string>.Success(code);
        }

        /// <summary>Join as a member (server-side), then fetch the code (members only) and join the live session.</summary>
        public async Task<Result<bool>> Join(string convoyId, string convoyName)
        {
            var j = await _api.Rpc("join_convoy", JsonArgs.Obj(("p_convoy", convoyId)));
            if (!j.Ok) return Result<bool>.Fail(j.ErrorCode, j.UserMessage);
            var c = await _api.Rpc("get_convoy_session_code", JsonArgs.Obj(("p_convoy", convoyId)), idempotent: true);
            string code = c.Ok ? c.Value.Trim('"') : null;
            if (string.IsNullOrEmpty(code) || code == "null")
            { await _api.Rpc("leave_convoy"); return Result<bool>.Fail("no_session", "This convoy has no active session right now."); }
            if (!await _session.JoinByCodeAsync(code))
            { await _api.Rpc("leave_convoy"); return Result<bool>.Fail("session_failed", _session.Message ?? "Could not join the session."); }
            CurrentConvoyId = convoyId; CurrentConvoyName = convoyName;
            return Result<bool>.Success(true);
        }

        public async Task Leave()
        {
            await _session.LeaveAsync();
            await _api.Rpc("leave_convoy");
            CurrentConvoyId = null; CurrentConvoyName = null; Mine.Reset();
        }
    }
}
