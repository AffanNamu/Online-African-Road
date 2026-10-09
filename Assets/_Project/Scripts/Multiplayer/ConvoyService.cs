using System;
using System.Threading.Tasks;
using ARO.Backend;
using ARO.Core;
using UnityEngine;

namespace ARO.Multiplayer
{
    [Serializable] public class CountDto { public int count; }
    [Serializable] public class LeaderDto { public string display_name; }
    [Serializable] public class ConvoyDto { public string id, name, leader_id; public LeaderDto leader; public CountDto[] convoy_members; public int Members => convoy_members != null && convoy_members.Length > 0 ? convoy_members[0].count : 0; }

    /// <summary>
    /// Convoy = durable social record in Supabase (membership, leader, join code behind RLS) + a live Relay session in Unity.
    /// Movement is never sent through Supabase. The join code is only released to convoy members by the server.
    /// </summary>
    public class ConvoyService
    {
        readonly SupabaseClient _api; readonly SessionService _session;
        public string CurrentConvoyId { get; private set; }
        public string CurrentConvoyName { get; private set; }
        public ConvoyService(SupabaseClient api, SessionService session) { _api = api; _session = session; }
        public SessionService Session => _session;

        public Task<Result<ConvoyDto[]>> ListOpen() =>
            _api.Select<ConvoyDto>("convoys?disbanded_at=is.null&order=created_at.desc&limit=20&select=id,name,leader_id,leader:profiles!leader_id(display_name),convoy_members(count)");

        /// <summary>Create the live session first (so we have a code), then record the convoy. Rolls the session back if the record fails.</summary>
        public async Task<Result<string>> Create(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 3) return Result<string>.Fail("invalid_name", "Convoy names need at least 3 characters.");
            string code = await _session.CreateAsync(name.Trim());
            if (code == null) return Result<string>.Fail("session_failed", _session.Message ?? "Could not start a multiplayer session.");
            var r = await _api.Rpc("create_convoy", $"{{\"p_name\":\"{Escape(name.Trim())}\",\"p_session_code\":\"{code}\"}}");
            if (!r.Ok) { await _session.LeaveAsync(); return Result<string>.Fail(r.ErrorCode, r.UserMessage); }
            CurrentConvoyId = r.Value.Trim('"'); CurrentConvoyName = name.Trim();
            return Result<string>.Success(code);
        }

        /// <summary>Join as a member (server-side), then fetch the code (members only) and join the live session.</summary>
        public async Task<Result<bool>> Join(ConvoyDto convoy)
        {
            var j = await _api.Rpc("join_convoy", $"{{\"p_convoy\":\"{convoy.id}\"}}");
            if (!j.Ok) return Result<bool>.Fail(j.ErrorCode, j.UserMessage);
            var c = await _api.Rpc("get_convoy_session_code", $"{{\"p_convoy\":\"{convoy.id}\"}}");
            string code = c.Ok ? c.Value.Trim('"') : null;
            if (string.IsNullOrEmpty(code) || code == "null")
            { await _api.Rpc("leave_convoy"); return Result<bool>.Fail("no_session", "This convoy has no active session right now."); }
            if (!await _session.JoinByCodeAsync(code))
            { await _api.Rpc("leave_convoy"); return Result<bool>.Fail("session_failed", _session.Message ?? "Could not join the session."); }
            CurrentConvoyId = convoy.id; CurrentConvoyName = convoy.name;
            return Result<bool>.Success(true);
        }

        public async Task Leave()
        {
            await _session.LeaveAsync();
            await _api.Rpc("leave_convoy");
            CurrentConvoyId = null; CurrentConvoyName = null;
        }

        static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
