using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ARO.Core;
using ARO.NetCore;
using UnityEngine;

namespace ARO.Backend
{
    /// <summary>
    /// Shared plumbing for the domain services. Every service talks to the backend ONLY through SupabaseClient (auth, retries, timeouts, error
    /// mapping live there), keeps each piece of data in a Loadable so a screen can show Loading / Empty / Unavailable honestly, and never
    /// substitutes invented data when a call fails.
    /// </summary>
    public abstract class ServiceBase
    {
        protected readonly SupabaseClient Api;
        protected ServiceBase(SupabaseClient api) { Api = api; }
        protected static double Now => Time.unscaledTime;

        /// <summary>Runs a read, publishes its honest state on the Loadable and returns the raw result.</summary>
        protected async Task<Result<T>> Track<T>(Loadable<T> state, Func<Task<Result<T>>> call, Func<T, bool> isEmpty = null)
        {
            state.Begin();
            var r = await call();
            if (r.Ok) state.Succeed(r.Value, isEmpty != null && isEmpty(r.Value), Now);
            else state.Fail(KindOf(r.ErrorCode), r.UserMessage, Now);
            return r;
        }

        /// <summary>Maps the canonical error codes SupabaseClient emits back to a failure kind.</summary>
        public static FailureKind KindOf(string code)
        {
            switch (code)
            {
                case "offline": return FailureKind.Offline;
                case "timeout": return FailureKind.Timeout;
                case "session_expired": return FailureKind.SessionExpired;
                case "rate_limited": return FailureKind.RateLimited;
                case "server_error": return FailureKind.Server;
                case "forbidden": return FailureKind.Forbidden;
                default: return FailureKind.Client;
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- PlayerService
    /// <summary>Profile, the dashboard summary (one round trip for the home screen) and public player lookups.</summary>
    public sealed class PlayerService : ServiceBase
    {
        public readonly Loadable<DashboardSummary> Summary = new Loadable<DashboardSummary>();
        public PlayerService(SupabaseClient api) : base(api) { }

        public Task<Result<DashboardSummary>> LoadSummary() => Track(Summary, () => Api.Call<DashboardSummary>("get_dashboard_summary"));
        public Task<Result<ProfileFull>> GetProfile() => Api.Call<ProfileFull>("get_my_profile");

        /// <summary>Only display name, username, avatar and country can be changed; null leaves a field alone. XP, level, distance and coins have no client path.</summary>
        public Task<Result<ProfileFull>> UpdateProfile(string displayName = null, string username = null, string avatarId = null, string countryCode = null) =>
            Api.Call<ProfileFull>("update_my_profile", JsonArgs.Obj(("p_display_name", displayName), ("p_username", username), ("p_avatar_id", avatarId), ("p_country_code", countryCode)), idempotent: false);

        public Task<Result<PublicProfile>> GetPublic(string playerId) => Api.Call<PublicProfile>("get_public_profile", JsonArgs.Obj(("p_player", playerId)));
        public Task<Result<PersonDto>> FindByUsername(string username) => Api.Call<PersonDto>("find_player", JsonArgs.Obj(("p_username", username)));
        public Task<Result<WalletInfo>> GetWallet() => Api.Call<WalletInfo>("get_my_wallet");
    }

    // ---------------------------------------------------------------------------------------------------------------- PresenceService
    /// <summary>
    /// Keeps the player's server-side session alive. The server decides who is online: a session with no heartbeat for the timeout is offline,
    /// whatever the client does. Call Tick every frame; it talks to the server only when HeartbeatClock says so.
    /// </summary>
    public sealed class PresenceService : ServiceBase
    {
        public readonly HeartbeatClock Clock = new HeartbeatClock();
        public readonly Loadable<int> Online = new Loadable<int>();
        public string SessionId { get; private set; }
        string _lastActivity, _lastRoute; double _lastSentAt = double.NegativeInfinity;
        public PresenceService(SupabaseClient api) : base(api) { }

        /// <summary>True while the last answer from the server is inside the presence timeout.</summary>
        public bool Connected => Api.IsSignedIn && Clock.Connected(Now);

        public void Tick(string activity, string routeCode, string citySlug)
        {
            if (!Api.IsSignedIn) return;
            double now = Now;
            if (Clock.HasSession && (activity != _lastActivity || routeCode != _lastRoute) && now - _lastSentAt >= 5) Clock.Nudge(now);
            switch (Clock.Next(now))
            {
                case PresenceAction.StartSession: Clock.Began(); _ = StartSession(activity, routeCode, citySlug); break;
                case PresenceAction.SendHeartbeat: Clock.Began(); _ = Beat(activity, routeCode, citySlug); break;
            }
        }

        async Task StartSession(string activity, string route, string city)
        {
            var r = await Api.Call<SessionStarted>("start_session", JsonArgs.Obj(("p_client", "webgl")), idempotent: false);
            if (!r.Ok) { Clock.Failed(Now, false); Online.Fail(KindOf(r.ErrorCode), r.UserMessage, Now); return; }
            SessionId = r.Value.session_id; Clock.SessionStarted(Now, r.Value.ttl_seconds);
            Debug.Log("[Presence] session started " + SessionId);
            await Beat(activity, route, city, true);
        }

        async Task Beat(string activity, string route, string city, bool follow = false)
        {
            if (follow) Clock.Began();
            _lastActivity = activity; _lastRoute = route; _lastSentAt = Now;
            var r = await Api.Call<HeartbeatAck>("heartbeat", JsonArgs.Obj(("p_session", SessionId), ("p_activity", activity), ("p_route", route), ("p_city", city)), idempotent: false);
            if (r.Ok) { Clock.BeatAccepted(Now); Online.Succeed(r.Value.online_players, false, Now); return; }
            bool gone = r.ErrorCode == "session_expired" || r.ErrorCode == "session_not_found";
            Clock.Failed(Now, gone);
            if (gone) SessionId = null;
            if (!gone) Online.Fail(KindOf(r.ErrorCode), r.UserMessage, Now);
        }

        public Task<Result<long>> GetOnlineCount() => Api.CallNumber("get_online_count");
        public Task<Result<MyPresence>> GetMine() => Api.Call<MyPresence>("get_my_presence");

        /// <summary>Logs out cleanly so the player disappears from the online count immediately instead of after the timeout.</summary>
        public async Task EndSession()
        {
            string id = SessionId; Clock.Reset(); SessionId = null; Online.Reset();
            if (string.IsNullOrEmpty(id) || !Api.IsSignedIn) return;
            await Api.Rpc("end_session", JsonArgs.Obj(("p_session", id)));
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- FriendService
    public sealed class FriendService : ServiceBase
    {
        public readonly Loadable<PeopleList> Friends = new Loadable<PeopleList>();
        public readonly Loadable<PeopleList> Recent = new Loadable<PeopleList>();
        public readonly Loadable<FriendRequests> Requests = new Loadable<FriendRequests>();
        public FriendService(SupabaseClient api) : base(api) { }

        public Task<Result<PeopleList>> LoadFriends() => Track(Friends, () => Api.Call<PeopleList>("list_friends"), v => v.items == null || v.items.Length == 0);
        public Task<Result<PeopleList>> LoadRecent(int limit = 20) => Track(Recent, () => Api.Call<PeopleList>("list_recent_players", JsonArgs.Obj(("p_limit", limit))), v => v.items == null || v.items.Length == 0);
        public Task<Result<FriendRequests>> LoadRequests() => Track(Requests, () => Api.Call<FriendRequests>("list_friend_requests"), v => (v.incoming == null || v.incoming.Length == 0) && (v.outgoing == null || v.outgoing.Length == 0));
        public Task<Result<string>> Status(string playerId) => Api.CallText("get_friend_status", JsonArgs.Obj(("p_player", playerId)), idempotent: true);
        public Task<Result<FriendSendResult>> Send(string playerId) => Api.Call<FriendSendResult>("send_friend_request", JsonArgs.Obj(("p_player", playerId)), idempotent: false);
        public Task<Result<string>> Respond(string requestId, bool accept) => Api.CallText("respond_friend_request", JsonArgs.Obj(("p_request", requestId), ("p_accept", accept)));
        public Task<Result<string>> Remove(string playerId) => Api.Rpc("remove_friend", JsonArgs.Obj(("p_player", playerId)));
    }

    // ---------------------------------------------------------------------------------------------------------------- NotificationService
    public sealed class NotificationService : ServiceBase
    {
        public readonly Loadable<NotificationList> Inbox = new Loadable<NotificationList>();
        public readonly Loadable<int> Unread = new Loadable<int>();
        double _nextPoll;
        public NotificationService(SupabaseClient api) : base(api) { }

        public int UnreadCount => Unread.State == LoadState.Ready || Unread.State == LoadState.Empty ? Unread.Value : 0;

        /// <summary>Cheap badge refresh (one tiny call), at most every `everySeconds`. Safe to call every frame.</summary>
        public void PollUnread(double everySeconds = 30)
        {
            if (!Api.IsSignedIn || Unread.State == LoadState.Loading || Now < _nextPoll) return;
            _nextPoll = Now + everySeconds; _ = RefreshUnread();
        }

        public async Task<Result<long>> RefreshUnread()
        {
            Unread.Begin();
            var r = await Api.CallNumber("get_unread_notification_count");
            if (r.Ok) Unread.Succeed((int)r.Value, r.Value == 0, Now); else Unread.Fail(KindOf(r.ErrorCode), r.UserMessage, Now);
            return r;
        }

        public async Task<Result<NotificationList>> LoadInbox(int limit = 30)
        {
            var r = await Track(Inbox, () => Api.Call<NotificationList>("list_notifications", JsonArgs.Obj(("p_limit", limit), ("p_before", null), ("p_unread_only", false))), v => v.items == null || v.items.Length == 0);
            if (r.Ok) Unread.Succeed(r.Value.unread, r.Value.unread == 0, Now);
            return r;
        }

        public async Task<Result<string>> MarkRead(string id)
        {
            var r = await Api.Rpc("mark_notification_read", JsonArgs.Obj(("p_id", id)));
            if (r.Ok) _ = RefreshUnread();
            return r;
        }

        public async Task<Result<long>> MarkAllRead()
        {
            var r = await Api.CallNumber("mark_all_notifications_read", "{}", idempotent: false);
            if (r.Ok) Unread.Succeed(0, true, Now);
            return r;
        }

        public void Reset() { Inbox.Reset(); Unread.Reset(); _nextPoll = 0; }
    }

    // ---------------------------------------------------------------------------------------------------------------- VehicleService
    public sealed class VehicleService : ServiceBase
    {
        public readonly Loadable<VehicleList> Garage = new Loadable<VehicleList>();
        public readonly Loadable<CatalogList> Catalog = new Loadable<CatalogList>();
        public VehicleService(SupabaseClient api) : base(api) { }

        public Task<Result<VehicleList>> LoadGarage() => Track(Garage, () => Api.Call<VehicleList>("list_my_vehicles"), v => v.items == null || v.items.Length == 0);
        public Task<Result<CatalogList>> LoadCatalog() => Track(Catalog, () => Api.Call<CatalogList>("list_vehicle_catalog"), v => v.items == null || v.items.Length == 0);
        public Task<Result<JobFitDto>> JobFit(string definitionId) => Api.Call<JobFitDto>("get_vehicle_job_fit", JsonArgs.Obj(("p_definition", definitionId)));
        public Task<Result<string>> SetActive(string vehicleId) => Api.Rpc("set_active_vehicle", JsonArgs.Obj(("p_vehicle", vehicleId)));
        public Task<Result<string>> Rename(string vehicleId, string name) => Api.Rpc("rename_vehicle", JsonArgs.Obj(("p_vehicle", vehicleId), ("p_name", name)));
        public Task<Result<string>> Customize(string vehicleId, string colorHex, string livery, string plate)
        {
            var parts = new List<string>();
            if (colorHex != null) parts.Add("\"color\":" + JsonArgs.Escape(colorHex));
            if (livery != null) parts.Add("\"livery\":" + JsonArgs.Escape(livery));
            if (plate != null) parts.Add("\"plate\":" + JsonArgs.Escape(plate));
            return Api.Rpc("set_vehicle_customization", "{\"p_vehicle\":" + JsonArgs.Escape(vehicleId) + ",\"p_data\":{" + string.Join(",", parts) + "}}");
        }

        /// <summary>Maps the server's vehicle card to the shape the older panels use.</summary>
        public static OwnedVehicleDto ToOwned(VehicleInfo v) => new OwnedVehicleDto
        {
            id = v.id, definition_id = v.definition_id, fuel_l = v.fuel_l, damage_pct = v.damage_pct, odometer_km = v.mileage_km,
            name = v.name, status = v.status, selected = v.selected,
        };
    }

    // ---------------------------------------------------------------------------------------------------------------- WorldService
    public sealed class WorldService : ServiceBase
    {
        public readonly Loadable<WorldState> State = new Loadable<WorldState>();
        public WorldService(SupabaseClient api) : base(api) { }

        public async Task<Result<WorldState>> Load(WorldMapModel model = null)
        {
            var r = await Track(State, () => Api.Call<WorldState>("get_world_state"), v => v.routes == null || v.routes.Length == 0);
            if (model != null) { if (r.Ok) Apply(model, r.Value); else model.ClearServerState(); }
            return r;
        }

        /// <summary>The server's verdict replaces any client-side idea of what is open (routes are matched by code = map route id).</summary>
        public static void Apply(WorldMapModel model, WorldState w)
        {
            model.ApplyServerState((w.routes ?? new WorldRoute[0]).Select(r => new ServerRouteState { code = r.code, effectiveStatus = r.effective_status, unlockLevel = r.unlock_level }));
        }
    }

    // ---------------------------------------------------------------------------------------------------------------- ProgressionService
    public sealed class ProgressionService : ServiceBase
    {
        public readonly Loadable<ProgressionDto> Progress = new Loadable<ProgressionDto>();
        public ProgressionService(SupabaseClient api) : base(api) { }
        public Task<Result<ProgressionDto>> Load() => Track(Progress, () => Api.Call<ProgressionDto>("get_my_progression"));
        public Task<Result<AchievementList>> Achievements() => Api.Call<AchievementList>("list_my_achievements");
    }

    // ---------------------------------------------------------------------------------------------------------------- LeaderboardService
    public sealed class LeaderboardService : ServiceBase
    {
        public LeaderboardService(SupabaseClient api) : base(api) { }
        /// <summary>metric: xp | distance | jobs | earnings; period: all | weekly (weekly has no xp board). Ranks are computed by the server from its own records.</summary>
        public Task<Result<LeaderboardPage>> Get(string metric, string period = "all", int limit = 20, int offset = 0) =>
            Api.Call<LeaderboardPage>("get_leaderboard", JsonArgs.Obj(("p_metric", metric), ("p_period", period), ("p_limit", limit), ("p_offset", offset)));
    }

    // ---------------------------------------------------------------------------------------------------------------- ContentService
    /// <summary>Authored in-house content (news). Served by the backend so it can change without a new build; it is labelled authored, never external news.</summary>
    public sealed class ContentService : ServiceBase
    {
        public readonly Loadable<ContentList> News = new Loadable<ContentList>();
        public readonly Loadable<ContentList> Hero = new Loadable<ContentList>();
        public ContentService(SupabaseClient api) : base(api) { }
        public Task<Result<ContentList>> LoadNews(int limit = 8) => Track(News, () => Api.Call<ContentList>("get_content", JsonArgs.Obj(("p_kind", "news"), ("p_limit", limit))), v => v.items == null || v.items.Length == 0);
        /// <summary>The dashboard's hero banners (copy, picture key and button action), managed on the server.</summary>
        public Task<Result<ContentList>> LoadHero(int limit = 6) => Track(Hero, () => Api.Call<ContentList>("get_content", JsonArgs.Obj(("p_kind", "hero"), ("p_limit", limit))), v => v.items == null || v.items.Length == 0);
    }
}
