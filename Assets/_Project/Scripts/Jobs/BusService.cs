using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ARO.Backend;
using ARO.Core;
using UnityEngine;

namespace ARO.Jobs
{
    /// <summary>Bus routes, runs and the vehicle shop. Thin client over server RPCs; passengers and fares are decided by the server.</summary>
    public class BusService
    {
        readonly SupabaseClient _api;
        public BusService(SupabaseClient api) { _api = api; }

        const string RoutesQuery =
            "bus_routes?order=code&select=id,code,name,fare,xp_reward,stops:bus_route_stops(seq,demand,alight_pct,location:locations(slug,name,world_x,world_z))";

        public async Task<Result<BusRouteDto[]>> LoadRoutes()
        {
            var r = await _api.Select<BusRouteDto>(RoutesQuery);
            if (!r.Ok) return r;
            foreach (var route in r.Value) route.stops = route.stops.OrderBy(s => s.seq).ToArray();   // PostgREST does not order embeds
            return Result<BusRouteDto[]>.Success(r.Value.Where(x => x.stops.Length >= 2).ToArray());
        }

        static string Pos(Vector3 p) => $"\"p_x\":{p.x.ToString("F2", CultureInfo.InvariantCulture)},\"p_z\":{p.z.ToString("F2", CultureInfo.InvariantCulture)}";

        /// <summary>Returns the run id. The server requires the position to be within 120 m of the first stop.</summary>
        public async Task<Result<string>> Start(string routeId, string vehicleId, Vector3 pos)
        {
            var r = await _api.Rpc("start_bus_run", $"{{\"p_route\":\"{routeId}\",\"p_vehicle\":\"{vehicleId}\",{Pos(pos)}}}");
            return r.Ok ? Result<string>.Success(r.Value.Trim('"')) : r;
        }

        public async Task<Result<TelemetryResult>> SubmitTelemetry(string runId, Vector3 pos)
        {
            var r = await _api.Rpc("submit_bus_telemetry", $"{{\"p_run\":\"{runId}\",{Pos(pos)}}}");
            if (!r.Ok) return Result<TelemetryResult>.Fail(r.ErrorCode, r.UserMessage);
            return Result<TelemetryResult>.Success(JsonUtility.FromJson<TelemetryResult>(r.Value));
        }

        /// <summary>The only input is the run id: the server decides who boards, who gets off and what is paid.</summary>
        public async Task<Result<ServeStopResult>> ServeStop(string runId)
        {
            var r = await _api.Rpc("serve_stop", $"{{\"p_run\":\"{runId}\"}}");
            if (!r.Ok) return Result<ServeStopResult>.Fail(r.ErrorCode, r.UserMessage);
            return Result<ServeStopResult>.Success(JsonUtility.FromJson<ServeStopResult>(r.Value));
        }

        public Task<Result<string>> Abandon(string runId) => _api.Rpc("abandon_bus_run", $"{{\"p_run\":\"{runId}\"}}");

        /// <summary>Buy a vehicle at its catalogue price. The server debits the wallet and grants the vehicle atomically.</summary>
        public async Task<Result<string>> BuyVehicle(string definitionId)
        {
            var r = await _api.Rpc("buy_vehicle", $"{{\"p_definition\":\"{definitionId}\"}}");
            return r.Ok ? Result<string>.Success(r.Value.Trim('"')) : r;
        }
    }
}
