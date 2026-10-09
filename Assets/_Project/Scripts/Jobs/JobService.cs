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

        public Task<Result<string>> Start(string assignmentId) =>
            _api.Rpc("start_job", $"{{\"p_assignment\":\"{assignmentId}\"}}");

        public Task<Result<string>> Abandon(string assignmentId) =>
            _api.Rpc("abandon_job", $"{{\"p_assignment\":\"{assignmentId}\"}}");

        /// <summary>Report delivery. The server re-validates time, distance and position before paying.</summary>
        public async Task<Result<CompleteJobResult>> Complete(string assignmentId, float distanceKm, Vector3 finalPos, float fuelL, float damagePct)
        {
            var ci = CultureInfo.InvariantCulture;
            string args = "{" +
                $"\"p_assignment\":\"{assignmentId}\"," +
                $"\"p_distance_km\":{distanceKm.ToString("F3", ci)}," +
                $"\"p_final_x\":{finalPos.x.ToString("F2", ci)}," +
                $"\"p_final_z\":{finalPos.z.ToString("F2", ci)}," +
                $"\"p_fuel_remaining_l\":{fuelL.ToString("F2", ci)}," +
                $"\"p_damage_pct\":{damagePct.ToString("F2", ci)}}}";
            var r = await _api.Rpc("complete_job", args);
            if (!r.Ok) return Result<CompleteJobResult>.Fail(r.ErrorCode, r.UserMessage);
            return Result<CompleteJobResult>.Success(JsonUtility.FromJson<CompleteJobResult>(r.Value));
        }
    }
}
