using System;

namespace ARO.Backend
{
    // JsonUtility DTOs for the backend-foundation endpoints (supabase/migrations/20261010*). Field names ARE the JSON keys the server sends;
    // tools/check_api_contract.py compares every field below with the real output of the real RPC, so a rename on either side fails CI.
    //
    // JsonUtility cannot represent null for nested objects: a JSON null becomes a default instance. Every optional nested object
    // therefore has an "exists" test (a non-empty id) instead of a null check - see the `Exists` members.

    // ---------------------------------------------------------------- shared
    [Serializable] public class PresenceLite { public bool online; public string last_seen_at, activity, route_code, city_slug, session_id; }

    /// <summary>A public player card (friends, recent players, convoy members, search results). Never contains coins or e-mail.</summary>
    [Serializable] public class PersonDto
    {
        public string player_id, display_name, username, avatar_id, country_code, friend_status; public int level; public PresenceLite presence;
        // optional: only some lists carry these (friends: since; recent players: last_played_at, times)
        public string since, last_played_at; public int times;
        public bool Exists => !string.IsNullOrEmpty(player_id);
    }

    // ---------------------------------------------------------------- profile / progression / dashboard summary
    [Serializable] public class ProfileFull
    {
        public string id, display_name, username, avatar_id, country_code, created_at, updated_at, last_seen_at, server_time;
        public int level, jobs_completed, jobs_total; public long experience, xp_level_start, xp_next_level, coins, lifetime_earnings; public double distance_km;
    }
    [Serializable] public class AchievementCounts { public int unlocked, total; }
    [Serializable] public class MilestoneDto { public string id, title; public double target, current, fraction; }
    [Serializable] public class ProgressionDto
    {
        public int level, jobs_completed; public long experience, xp_level_start, xp_next_level, xp_into_level, xp_to_next; public double level_progress, distance_km;
        public AchievementCounts achievements; public MilestoneDto next_milestone;
        // optional: only the standalone get_my_progression reply carries the server clock
        public string server_time;
    }
    [Serializable] public class AchievementItem { public string id, title, description, metric, unlocked_at; public double threshold, current; public bool Unlocked => !string.IsNullOrEmpty(unlocked_at); }
    [Serializable] public class AchievementList { public AchievementItem[] items; }
    [Serializable] public class WalletInfo { public long balance, lifetime_earnings; public string server_time; }

    [Serializable] public class VehicleCustomization { // optional:
    public string color, livery, plate; }
    [Serializable] public class VehicleInfo
    {
        public string id, definition_id, name, model, nickname, category, status, acquired_at;
        public bool selected; public double condition_pct, damage_pct, fuel_l, fuel_capacity_l, fuel_pct, mileage_km, top_speed_kmh;
        public int cargo_capacity_kg, passenger_capacity; public VehicleCustomization customization;
        public bool Exists => !string.IsNullOrEmpty(id);
    }
    [Serializable] public class VehicleList { public VehicleInfo[] items; public string selected_id, server_time; }
    [Serializable] public class CatalogItem
    {
        public string definition_id, name, category, description; public long price; public int unlock_level, cargo_capacity_kg, passenger_capacity, owned;
        public double top_speed_kmh, fuel_capacity_l; public bool unlocked, purchasable, can_afford;
    }
    [Serializable] public class CatalogList { public CatalogItem[] items; }

    [Serializable] public class SummaryProfile { public string id, display_name, username, avatar_id, country_code; public int level; }
    [Serializable] public class JobsSummary { public int available; public long best_reward; }
    [Serializable] public class WorldSummary { public int cities, routes, routes_open; }
    [Serializable] public class ConvoySummary { public string id, name; public bool is_leader; public int capacity, member_count; public bool Exists => !string.IsNullOrEmpty(id); }
    [Serializable] public class ActiveJobSummary
    {
        public string assignment_id, status, job_code; public double distance_km, verified_km; public long reward;
        public bool Exists => !string.IsNullOrEmpty(assignment_id);
    }
    [Serializable] public class DashboardSummary
    {
        public string server_time; public SummaryProfile profile; public long coins; public ProgressionDto progression; public VehicleInfo vehicle; public int vehicle_count;
        public JobsSummary jobs; public ActiveJobSummary active_job; public WorldSummary world;
        public int online_players, friends_online, pending_friend_requests, pending_convoy_invites, unread_notifications; public ConvoySummary convoy;
    }

    // ---------------------------------------------------------------- presence
    [Serializable] public class SessionStarted { public string session_id, server_time; public double ttl_seconds; }
    [Serializable] public class HeartbeatAck { public string server_time; public int online_players; }
    [Serializable] public class MyPresence { public bool online; public string session_id, activity, route_code, city_slug, last_seen_at, convoy_id, convoy_name, server_time; }

    // ---------------------------------------------------------------- friends / recent players
    [Serializable] public class PeopleList { public PersonDto[] items; public string server_time; }
    [Serializable] public class FriendRequestItem { public string request_id, created_at; public PersonDto person; }
    [Serializable] public class FriendRequests { public FriendRequestItem[] incoming, outgoing; public string server_time; }
    [Serializable] public class FriendSendResult { public string request_id, status; }
    [Serializable] public class PublicProfile
    {
        public string player_id, display_name, username, avatar_id, country_code, friend_status, member_since; public int level, jobs_completed, achievements;
        public long experience; public double distance_km; public PresenceLite presence;
    }

    // ---------------------------------------------------------------- convoys (lobby, route, ready check, chat)
    [Serializable] public class ConvoyRoute { public string code, name, from, to, status; public double distance_km; public int est_minutes, unlock_level; public bool Exists => !string.IsNullOrEmpty(code); }
    [Serializable] public class ConvoyMemberDto { public string player_id, display_name, username, avatar_id, country_code, joined_at, vehicle; public int level; public bool is_leader, ready; public PresenceLite presence; }
    [Serializable] public class ConvoyInfo
    {
        public string id, name, leader_id, status, phase, language, created_at, started_at; public bool is_leader, is_private, session_active, all_ready; public int capacity, member_count; public ConvoyMemberDto[] members;
        public ConvoyRoute route; public string[] tags;
        public bool Exists => !string.IsNullOrEmpty(id);
        public bool Driving => phase == "driving";
    }
    [Serializable] public class MyConvoy { public ConvoyInfo convoy; public string server_time; }
    [Serializable] public class OpenConvoy { public string id, name, leader, created_at, phase, language; public int capacity, member_count; public bool session_active, joinable; public ConvoyRoute route; public string[] tags; }
    [Serializable] public class OpenConvoyList { public int total; public OpenConvoy[] items; public string server_time; }
    [Serializable] public class ConvoyInviteItem { public string invite_id, convoy_id, convoy_name, created_at, expires_at; public PersonDto from; }
    [Serializable] public class ConvoyInviteList { public ConvoyInviteItem[] items; }
    [Serializable] public class ChatSender { public string player_id, display_name, avatar_id; public int level; }
    [Serializable] public class ChatLine { public long id; public string kind, body, created_at; public ChatSender sender; public bool System => kind == "system"; }
    [Serializable] public class ChatPage { public ChatLine[] items; public string server_time; }
    [Serializable] public class ChatPosted { public long id; }
    [Serializable] public class ActivePlayer { public string player_id, display_name, username, avatar_id, country_code, friend_status, vehicle, activity; public int level; public bool in_convoy; }
    [Serializable] public class ActivePlayerList { public int total; public ActivePlayer[] items; public string server_time; }
    [Serializable] public class ServerStatus { public int players_online, active_convoys, convoys_driving; public string server_time; }

    // ---------------------------------------------------------------- notifications
    [Serializable] public class NotificationItem { public string id, type, title, message, created_at; public bool read; }
    [Serializable] public class NotificationList { public NotificationItem[] items; public int unread; public string server_time; }

    // ---------------------------------------------------------------- world
    [Serializable] public class WorldCity
    {
        public string slug, name, country_code, country, region, status, effective_status; public double lat, lng, map_x, map_y; public int unlock_level;
    }
    [Serializable] public class WorldRoute
    {
        public string code, name, region, from, to, distance_source, status, effective_status, locked_reason, game_route_id, description;
        public string[] via; public double distance_km; public int est_minutes, unlock_level;
    }
    [Serializable] public class WorldState { public int player_level; public WorldCity[] cities; public WorldRoute[] routes; public string server_time; }

    // ---------------------------------------------------------------- job board (list_available_jobs / get_my_active_job)
    [Serializable] public class BoardPlace { public string slug, name, city, city_slug; public double world_x, world_z; }
    [Serializable] public class BoardCargo { public string type, category, description, image_key; public int weight_kg; public bool fragile, special_handling; }
    [Serializable] public class BoardRequirements { public string category; public int min_level, max_participants; public bool has_vehicle; }
    [Serializable] public class BoardEstimate { public double fuel_l; public long fuel_cost, profit; }
    [Serializable] public class BoardJob
    {
        public string id, code, status, created_at, expires_at, blocked_reason; public BoardPlace origin, destination; public double distance_km; public int est_minutes;
        public BoardCargo cargo; public long reward; public int xp_reward, difficulty, slots_taken; public BoardRequirements requirements; public BoardEstimate estimate; public bool eligible;
    }
    [Serializable] public class JobBoardPage { public int total; public BoardJob[] items; public string server_time; }
    [Serializable] public class ActiveJobInfo
    {
        public string assignment_id, status, vehicle_id, accepted_at, started_at; public double verified_km, progress_pct; public BoardJob job;
        public bool Exists => !string.IsNullOrEmpty(assignment_id);
    }
    [Serializable] public class ActiveJobReply { public ActiveJobInfo active; public string server_time; }

    // ---------------------------------------------------------------- content
    [Serializable] public class ContentItem { public string id, kind, category, title, body, image_key, publish_at, expires_at, source, eyebrow, headline, headline_accent, action, button; public int priority; }
    [Serializable] public class ContentList { public ContentItem[] items; }

    // ---------------------------------------------------------------- leaderboards
    [Serializable] public class LeaderboardRow { public string player_id, display_name, username, avatar_id, country_code; public int rank, level; public double value; }
    [Serializable] public class LeaderboardMe { public int rank; public double value; }
    [Serializable] public class LeaderboardPage { public string metric, period, server_time; public int total, limit, offset; public LeaderboardRow[] items; public LeaderboardMe me; }
}
