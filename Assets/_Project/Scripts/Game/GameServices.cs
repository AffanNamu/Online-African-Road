using ARO.Backend;
using ARO.Jobs;
using ARO.Multiplayer;
using UnityEngine;

namespace ARO.Game
{
    /// <summary>Composition root for services. Created once by GameBootstrap; no scattered singletons.</summary>
    public class GameServices
    {
        public BackendConfig Config { get; }
        public SupabaseClient Api { get; }
        public JobService Jobs { get; }
        public BusService Bus { get; }
        public SessionService Session { get; }
        public ConvoyService Convoys { get; }
        public ProfileDto Profile;
        public WalletDto Wallet;
        public OwnedVehicleDto[] Vehicles = new OwnedVehicleDto[0];
        public VehicleDefDto[] Definitions = new VehicleDefDto[0];

        public GameServices(BackendConfig cfg)
        {
            Config = cfg; Api = new SupabaseClient(cfg); Jobs = new JobService(Api); Bus = new BusService(Api);
            Session = new SessionService(); Convoys = new ConvoyService(Api, Session);
        }

        /// <summary>Cheap refresh after a mid-route fare so the HUD wallet is current.</summary>
        public async System.Threading.Tasks.Task RefreshWallet()
        {
            var w = await Api.Select<WalletDto>($"player_wallets?player_id=eq.{Api.UserId}");
            if (w.Ok && w.Value.Length > 0) Wallet = w.Value[0];
        }

        public async System.Threading.Tasks.Task<string> RefreshPlayer()
        {
            string uid = Api.UserId;
            var p = await Api.Select<ProfileDto>($"profiles?id=eq.{uid}");
            if (!p.Ok) return p.UserMessage;
            var w = await Api.Select<WalletDto>($"player_wallets?player_id=eq.{uid}");
            if (!w.Ok) return w.UserMessage;
            var v = await Api.Select<OwnedVehicleDto>("vehicle_ownership?order=acquired_at");
            if (!v.Ok) return v.UserMessage;
            var d = await Api.Select<VehicleDefDto>("vehicle_definitions?select=id,category,name,cargo_capacity_kg,passenger_capacity,fuel_capacity_l,max_speed_kmh,price");
            if (!d.Ok) return d.UserMessage;
            if (p.Value.Length == 0 || w.Value.Length == 0) return "Profile not found. Try signing out and in again.";
            Profile = p.Value[0]; Wallet = w.Value[0]; Vehicles = v.Value; Definitions = d.Value;
            return null;
        }
    }
}
