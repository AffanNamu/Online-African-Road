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
            ["delivery_too_fast"] = "Delivery rejected: the trip was faster than the truck can drive.",
            ["distance_implausible"] = "Delivery rejected: the distance driven does not match the route.",
            ["not_at_destination"] = "You are not at the delivery point yet.",
            ["vehicle_state_invalid"] = "Delivery rejected: invalid vehicle state.",
            ["insufficient_funds"] = "Not enough money.",
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
