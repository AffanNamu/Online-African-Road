using System;

namespace ARO.Backend
{
    // JsonUtility DTOs matching PostgREST responses. Arrays are wrapped by SupabaseClient.

    [Serializable] public class SessionDto { public string access_token, refresh_token; public int expires_in; public UserDto user; }
    [Serializable] public class UserDto { public string id, email; }

    [Serializable] public class ProfileDto
    {
        public string id, display_name; public int level, jobs_completed; public long experience; public double distance_km;
    }
    [Serializable] public class WalletDto { public string player_id; public long balance; }

    [Serializable] public class LocationDto { public string slug, name; public double world_x, world_z; }
    [Serializable] public class JobDto
    {
        public string id, code, cargo_type, required_category, status, expires_at;
        public int cargo_weight_kg, difficulty, xp_reward, max_participants;
        public double distance_km; public long reward;
        public LocationDto origin, destination;   // embedded via PostgREST select
    }
    [Serializable] public class VehicleDefDto
    {
        public string id, category, name, stats; // stats is parsed separately (jsonb)
        public int cargo_capacity_kg, passenger_capacity; public double fuel_capacity_l, max_speed_kmh;
    }
    [Serializable] public class OwnedVehicleDto
    {
        public string id, definition_id; public double fuel_l, damage_pct, odometer_km;
    }
    [Serializable] public class AssignmentDto { public string id, job_id, vehicle_id, status; }

    [Serializable] public class CompleteJobResult { public long reward, bonus, xp, balance; public int level; }
    [Serializable] public class ListWrapper<T> { public T[] items; }
}
