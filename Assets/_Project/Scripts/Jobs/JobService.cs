using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ARO.Backend;
using ARO.Core;
using ARO.NetCore;
using UnityEngine;

namespace ARO.Jobs
{
    /// <summary>
    /// Job board + assignment lifecycle. Thin client over server RPCs; the server decides outcomes and every number shown here
    /// (reward, XP, distance, estimated minutes, requirements, eligibility) is the server's. The client never computes a reward.
    /// </summary>
    public class JobService
    {
        readonly SupabaseClient _api;
        public readonly Loadable<JobDto[]> Board = new Loadable<JobDto[]>();
        public JobService(SupabaseClient api) { _api = api; }

        /// <summary>Open jobs this player has not taken yet, newest first, each with the server's eligibility verdict.</summary>
        public async Task<Result<JobDto[]>> LoadBoard(int limit = 50)
        {
            Board.Begin();
            var r = await Page(limit, 0, null, null);
            if (!r.Ok) { Board.Fail(ServiceKind(r.ErrorCode), r.UserMessage, Time.unscaledTime); return Result<JobDto[]>.Fail(r.ErrorCode, r.UserMessage); }
            Board.Succeed(r.Value.Jobs, r.Value.Jobs.Length == 0, Time.unscaledTime);
            return Result<JobDto[]>.Success(r.Value.Jobs);
        }

        /// <summary>One page of the board with the server's own filter (cargo category) and search (cargo, place or city). Total is the server's count for that filter.</summary>
        public sealed class BoardPageResult { public JobDto[] Jobs; public int Total; }
        public async Task<Result<BoardPageResult>> Page(int limit, int offset, string category, string search)
        {
            string args = JsonArgs.Obj(("p_limit", limit), ("p_offset", offset), ("p_category", string.IsNullOrEmpty(category) ? null : category), ("p_search", string.IsNullOrWhiteSpace(search) ? null : search.Trim()));
            var r = await _api.Call<JobBoardPage>("list_available_jobs", args);
            if (!r.Ok) return Result<BoardPageResult>.Fail(r.ErrorCode, r.UserMessage);
            return Result<BoardPageResult>.Success(new BoardPageResult { Jobs = (r.Value.items ?? new BoardJob[0]).Select(ToJobDto).ToArray(), Total = r.Value.total });
        }

        static FailureKind ServiceKind(string code) => ServiceBase.KindOf(code);

        public static JobDto ToJobDto(BoardJob b)
        {
            var o = b.origin ?? new BoardPlace(); var d = b.destination ?? new BoardPlace(); var c = b.cargo ?? new BoardCargo(); var q = b.requirements ?? new BoardRequirements(); var e = b.estimate ?? new BoardEstimate();
            return new JobDto
            {
                id = b.id, code = b.code, status = b.status, expires_at = b.expires_at,
                cargo_type = c.type, cargo_weight_kg = c.weight_kg, required_category = q.category, max_participants = q.max_participants, min_level = q.min_level,
                difficulty = b.difficulty, xp_reward = b.xp_reward, distance_km = b.distance_km, reward = b.reward,
                est_minutes = b.est_minutes, slots_taken = b.slots_taken, eligible = b.eligible, blocked_reason = b.blocked_reason,
                cargo_category = c.category, cargo_description = c.description, cargo_image = c.image_key, fragile = c.fragile, special_handling = c.special_handling,
                has_vehicle = q.has_vehicle, est_fuel_l = e.fuel_l, est_fuel_cost = e.fuel_cost, est_profit = e.profit,
                origin = new LocationDto { slug = o.slug, name = o.name, city = o.city, world_x = o.world_x, world_z = o.world_z },
                destination = new LocationDto { slug = d.slug, name = d.name, city = d.city, world_x = d.world_x, world_z = d.world_z },
            };
        }

        public Task<Result<ActiveJobReply>> Active() => _api.Call<ActiveJobReply>("get_my_active_job");
        public async Task<Result<JobDto>> Details(string jobId)
        {
            var r = await _api.Call<BoardJob>("get_job_details", JsonArgs.Obj(("p_job", jobId)));
            return r.Ok ? Result<JobDto>.Success(ToJobDto(r.Value)) : Result<JobDto>.Fail(r.ErrorCode, r.UserMessage);
        }

        public async Task<Result<string>> Accept(string jobId, string vehicleId)
        {
            var r = await _api.Rpc("accept_job", $"{{\"p_job\":\"{jobId}\",\"p_vehicle\":\"{vehicleId}\"}}");
            return r.Ok ? Result<string>.Success(r.Value.Trim('"')) : r;   // returns assignment id
        }

        static string Pos(Vector3 p) => $"\"p_x\":{p.x.ToString("F2", CultureInfo.InvariantCulture)},\"p_z\":{p.z.ToString("F2", CultureInfo.InvariantCulture)}";

        /// <summary>Load cargo. The server requires the reported position to be at the pickup and starts its own clock.</summary>
        public Task<Result<string>> Start(string assignmentId, Vector3 pos) =>
            _api.Rpc("start_job", $"{{\"p_assignment\":\"{assignmentId}\",{Pos(pos)}}}");

        /// <summary>One position sample. The server judges plausibility with ITS clock; rejected samples are silently ignored.</summary>
        public async Task<Result<TelemetryResult>> SubmitTelemetry(string assignmentId, Vector3 pos)
        {
            var r = await _api.Rpc("submit_telemetry", $"{{\"p_assignment\":\"{assignmentId}\",{Pos(pos)}}}");
            if (!r.Ok) return Result<TelemetryResult>.Fail(r.ErrorCode, r.UserMessage);
            return Result<TelemetryResult>.Success(JsonUtility.FromJson<TelemetryResult>(r.Value));
        }

        public Task<Result<string>> Abandon(string assignmentId) =>
            _api.Rpc("abandon_job", $"{{\"p_assignment\":\"{assignmentId}\"}}");

        /// <summary>
        /// Request delivery. Distance, position, time and fuel are derived server-side from accepted telemetry;
        /// the only client input is the damage figure, which the server bounds and never lets decrease.
        /// </summary>
        public async Task<Result<CompleteJobResult>> Complete(string assignmentId, float damagePct)
        {
            string args = $"{{\"p_assignment\":\"{assignmentId}\",\"p_damage_pct\":{damagePct.ToString("F2", CultureInfo.InvariantCulture)}}}";
            var r = await _api.Rpc("complete_job", args);
            if (!r.Ok) return Result<CompleteJobResult>.Fail(r.ErrorCode, r.UserMessage);
            return Result<CompleteJobResult>.Success(JsonUtility.FromJson<CompleteJobResult>(r.Value));
        }
    }
}
