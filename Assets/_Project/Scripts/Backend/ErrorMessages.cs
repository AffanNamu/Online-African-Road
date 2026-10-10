using System.Collections.Generic;

namespace ARO.Backend
{
    /// <summary>Maps server error codes (raise exception 'code') to messages safe to show players.</summary>
    public static class ErrorMessages
    {
        static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            ["job_unavailable"] = "That job is no longer available.",
            ["job_full"] = "That job already has all the drivers it needs.",
            ["wrong_vehicle_category"] = "This job needs a different type of vehicle.",
            ["vehicle_destroyed"] = "Your vehicle is too damaged. Repair it first.",
            ["already_assigned"] = "You are already on this job.",
            ["already_completed"] = "This delivery was already completed.",
            ["out_of_fuel"] = "Refuel before starting the job.",
            ["not_at_pickup"] = "Get closer to the pickup point to load.",
            ["telemetry_stale"] = "Lost contact with the server. Drive on for a moment and try again.",
            ["telemetry_flagged"] = "This job was cancelled: your movement data was rejected by the server.",
            ["delivery_too_fast"] = "Delivery rejected: the trip was faster than the truck can drive.",
            ["distance_implausible"] = "Delivery rejected: the distance driven does not match the route.",
            ["not_at_destination"] = "You are not at the delivery point yet.",
            ["vehicle_state_invalid"] = "Delivery rejected: invalid vehicle state.",
            ["insufficient_funds"] = "Not enough money.",
            ["vehicle_not_owned"] = "You do not own that vehicle.",
            ["unknown_vehicle"] = "That vehicle does not exist.",
            ["not_for_sale"] = "That vehicle is not for sale.",
            ["route_not_found"] = "That bus route no longer exists.",
            ["not_at_stop"] = "Drive to the stop and come to a halt.",
            ["stop_too_soon"] = "You reached this stop faster than the bus can drive. Drive the route normally.",
            ["run_active"] = "You already have a bus route in progress.",
            ["run_not_found"] = "That bus route is no longer active.",
            ["run_not_active"] = "That bus route has already ended.",
            ["job_active"] = "Finish or abandon your freight job before starting a bus route.",
            ["bus_run_active"] = "Finish or abandon your bus route before taking a freight job.",
            ["display_name_taken"] = "That name is taken.",
            ["invalid_display_name"] = "Names are 3-24 letters, numbers, spaces, _ or -.",
            ["convoy_full"] = "That convoy is full.",
            ["already_in_convoy"] = "Leave your current convoy first.",
            ["Invalid login credentials"] = "Wrong email or password.",
            ["User already registered"] = "That email is already registered.",
        };

        public static string ToUser(string code, long http)
        {
            if (code != null && Map.TryGetValue(code, out var m)) return m;
            if (http == 401 || http == 403) return "Session expired. Please sign in again.";
            if (http >= 500) return "Server problem. Please try again shortly.";
            return "Something went wrong (" + (code ?? "unknown") + ").";
        }
    }
}
