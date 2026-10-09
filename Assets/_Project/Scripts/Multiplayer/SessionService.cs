using System;
using System.Threading.Tasks;
using ARO.NetCore;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using NetState = ARO.NetCore.SessionState;        // SessionState also exists in the Unity SDK
using SdkState = Unity.Services.Multiplayer.SessionState;

namespace ARO.Multiplayer
{
    /// <summary>
    /// Unity Multiplayer Services session (Relay) wrapper. The Multiplayer SDK starts/stops the scene's NetworkManager
    /// for us (host on create, client on join). Lifecycle decisions (reconnect / give up) come from the unit-tested
    /// <see cref="SessionLifecycle"/>; this class only performs the engine/SDK side effects.
    /// </summary>
    public class SessionService
    {
        public const int MaxPlayers = 8;
        readonly SessionLifecycle _life = new SessionLifecycle(new ReconnectPolicy(5, 1, 15));
        ISession _session; bool _intentionalLeave; bool _initialised;

        public NetState State => _life.State;
        public string Message => _life.LastMessage;
        public string Code => _session?.Code;
        public bool IsHost => _session != null && _session.IsHost;
        public event Action<NetState, string> StateChanged;

        public SessionService() { _life.StateChanged += s => StateChanged?.Invoke(s, _life.LastMessage); }

        async Task EnsureServices()
        {
            if (_initialised && AuthenticationService.Instance.IsSignedIn) return;
            if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
            // UGS identity is anonymous and only used for Relay/session membership. Game identity + economy stay in Supabase.
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
            _initialised = true;
        }

        /// <summary>Create a Relay-backed session as host. Returns the join code (null on failure; see Message).</summary>
        public async Task<string> CreateAsync(string name)
        {
            if (!_life.BeginConnect()) return null;
            try
            {
                await EnsureServices();
                var opts = new SessionOptions { MaxPlayers = MaxPlayers, Name = name, IsPrivate = true }.WithRelayNetwork();
                _session = await MultiplayerService.Instance.CreateSessionAsync(opts);
                Hook(_session); _life.OnConnected();
                return _session.Code;
            }
            catch (Exception e) { Debug.LogWarning("[Session] create failed: " + e); _life.OnConnectFailed(Friendly(e)); return null; }
        }

        public async Task<bool> JoinByCodeAsync(string code)
        {
            if (!_life.BeginConnect()) return false;
            try
            {
                await EnsureServices();
                _session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);
                Hook(_session); _life.OnConnected(); return true;
            }
            catch (Exception e) { Debug.LogWarning("[Session] join failed: " + e); _life.OnConnectFailed(Friendly(e)); return false; }
        }

        public async Task LeaveAsync()
        {
            _intentionalLeave = true;
            var s = _session; _session = null;
            try { if (s != null) await s.LeaveAsync(); } catch (Exception e) { Debug.LogWarning("[Session] leave: " + e.Message); }
            _life.OnDisconnected(DisconnectReason.Intentional);
            _intentionalLeave = false;
        }

        void Hook(ISession s)
        {
            s.StateChanged += OnSessionState;
            s.RemovedFromSession += () => HandleLoss(DisconnectReason.Kicked);
            s.Deleted += () => HandleLoss(DisconnectReason.HostLeft);
        }

        void OnSessionState(SdkState state)
        {
            if (_intentionalLeave) return;
            if (state == SdkState.Disconnected) HandleLoss(DisconnectReason.TransportFailure);
            else if (state == SdkState.Deleted) HandleLoss(DisconnectReason.HostLeft);
        }

        async void HandleLoss(DisconnectReason reason)
        {
            if (_intentionalLeave || _life.State != NetState.InSession) return;
            var decision = _life.OnDisconnected(reason);
            string sessionId = _session?.Id;
            while (decision.Reconnect && sessionId != null)
            {
                await Awaitable.WaitForSecondsAsync((float)decision.Delay.TotalSeconds);   // WebGL-safe (no threads)
                if (_life.State != NetState.Reconnecting) return;                       // player left meanwhile
                try
                {
                    _session = await MultiplayerService.Instance.ReconnectToSessionAsync(sessionId);
                    Hook(_session); _life.OnConnected(); return;
                }
                catch (Exception e) { Debug.LogWarning("[Session] reconnect failed: " + e.Message); decision = _life.OnReconnectFailed(); }
            }
        }

        static string Friendly(Exception e)
        {
            string m = e.Message ?? "";
            if (m.Contains("full", StringComparison.OrdinalIgnoreCase)) return "That convoy session is full.";
            if (m.Contains("not found", StringComparison.OrdinalIgnoreCase) || m.Contains("404")) return "That session no longer exists.";
            return "Could not reach the multiplayer service. Check your connection.";
        }
    }
}
