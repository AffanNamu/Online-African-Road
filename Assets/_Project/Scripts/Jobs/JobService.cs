using System.Globalization;
using System.Threading.Tasks;
using ARO.Backend;
using ARO.Core;
using UnityEngine;

namespace ARO.Jobs
{
    /// <summary>Job board + assignment lifecycle. Thin client over server RPCs; the server decides outcomes.</summary>
    public class JobService
    {
        readonly SupabaseClient _api;
        public JobService(SupabaseClient api) { _api = api; }

        const string BoardQuery =
            "jobs?status=eq.open&expires_at=gt.now()&order=created_at.desc&limit=50" +
            "&select=*,origin:locations!origin_id(slug,name,world_x,world_z),destination:locations!destination_id(slug,name,world_x,world_z)";

        public Task<Result<JobDto[]>> LoadBoard() => _api.Select<JobDto>(BoardQuery);

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
