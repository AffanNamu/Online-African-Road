using System.Linq;
using System.Threading.Tasks;
using ARO.Backend;
using ARO.Jobs;
using ARO.Multiplayer;
using UnityEngine;

namespace ARO.Game
{
    /// <summary>
    /// Composition root for services. Created once by GameBootstrap; no scattered singletons. Every domain has ONE service and every
    /// request goes through the one SupabaseClient (auth, retries, timeouts, error mapping). Screens never talk HTTP.
    /// </summary>
    public class GameServices
    {
        public BackendConfig Config { get; }
        public SupabaseClient Api { get; }
        // domain services (the spec's PlayerService, PresenceService, FriendService, ConvoyService, NotificationService, VehicleService,
        // JobService, WorldService, ProgressionService, LeaderboardService, ContentService)
        public PlayerService Player { get; }
        public PresenceService Presence { get; }
        public FriendService Friends { get; }
        public ConvoyService Convoys { get; }
        public NotificationService Notifications { get; }
        public VehicleService Garage { get; }
        public JobService Jobs { get; }
        public WorldService World { get; }
        public ProgressionService Progress { get; }
        public LeaderboardService Boards { get; }
        public ContentService Content { get; }
        public BusService Bus { get; }
        public SessionService Session { get; }

        // what the older panels read; always filled from the server (RefreshPlayer), never invented
        public ProfileDto Profile;
        public WalletDto Wallet;
        public OwnedVehicleDto[] Vehicles = new OwnedVehicleDto[0];
        public VehicleDefDto[] Definitions = new VehicleDefDto[0];

        public GameServices(BackendConfig cfg)
        {
            Config = cfg; Api = new SupabaseClient(cfg);
            Player = new PlayerService(Api); Presence = new PresenceService(Api); Friends = new FriendService(Api); Notifications = new NotificationService(Api);
            Garage = new VehicleService(Api); Jobs = new JobService(Api); World = new WorldService(Api); Progress = new ProgressionService(Api);
            Boards = new LeaderboardService(Api); Content = new ContentService(Api); Bus = new BusService(Api);
            Session = new SessionService(); Convoys = new ConvoyService(Api, Session);
        }

        /// <summary>Cheap refresh after a mid-route fare so the HUD wallet is current.</summary>
        public async Task RefreshWallet()
        {
            var w = await Player.GetWallet();
            if (w.Ok) Wallet = new WalletDto { player_id = Api.UserId, balance = w.Value.balance };
        }

        /// <summary>Loads the player's profile, wallet, vehicles and the vehicle catalogue from the server. Returns an error message, or null on success.</summary>
        public async Task<string> RefreshPlayer()
        {
            var s = await Player.LoadSummary();
            if (!s.Ok) return s.UserMessage;
            var g = await Garage.LoadGarage();
            if (!g.Ok) return g.UserMessage;
            var d = await Api.Select<VehicleDefDto>("vehicle_definitions?select=id,category,name,cargo_capacity_kg,passenger_capacity,fuel_capacity_l,max_speed_kmh,price");
            if (!d.Ok) return d.UserMessage;
            var sum = s.Value; var pr = sum.progression ?? new ProgressionDto();
            Profile = new ProfileDto { id = sum.profile.id, display_name = sum.profile.display_name, level = pr.level, experience = pr.experience, distance_km = pr.distance_km, jobs_completed = pr.jobs_completed };
            Wallet = new WalletDto { player_id = sum.profile.id, balance = sum.coins };
            Vehicles = (g.Value.items ?? new VehicleInfo[0]).Select(VehicleService.ToOwned).ToArray(); Definitions = d.Value;
            if (string.IsNullOrEmpty(Profile.id)) return "Profile not found. Try signing out and in again.";
            return null;
        }

        /// <summary>Forget everything cached for the signed-in player (sign-out, expired session).</summary>
        public void ClearPlayerCaches()
        {
            Profile = null; Wallet = null; Vehicles = new OwnedVehicleDto[0];
            Player.Summary.Reset(); Notifications.Reset(); Friends.Friends.Reset(); Friends.Recent.Reset(); Friends.Requests.Reset();
            Garage.Garage.Reset(); World.State.Reset(); Content.News.Reset(); Content.Hero.Reset(); Convoys.Mine.Reset(); Jobs.Board.Reset(); Progress.Progress.Reset();
        }
    }
}
