using System;

namespace ARO.NetCore
{
    public enum SessionState { Offline, Connecting, InSession, Reconnecting, Failed }
    public enum DisconnectReason { Intentional, HostLeft, Kicked, TransportFailure, Timeout }

    public readonly struct ReconnectDecision
    {
        public readonly bool Reconnect;
        public readonly TimeSpan Delay;
        public readonly string Message;   // player-facing
        public ReconnectDecision(bool reconnect, TimeSpan delay, string message) { Reconnect = reconnect; Delay = delay; Message = message; }
    }

    /// <summary>Exponential backoff: base, 2x, 4x ... capped. Deterministic (jitter is applied by the caller if desired).</summary>
    public sealed class ReconnectPolicy
    {
        public int MaxAttempts { get; }
        readonly double _base, _max;
        public ReconnectPolicy(int maxAttempts = 5, double baseSeconds = 1.0, double maxSeconds = 15.0)
        {
            if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
            MaxAttempts = maxAttempts; _base = baseSeconds; _max = maxSeconds;
        }
        /// <summary>Delay before attempt number <paramref name="attempt"/> (1-based).</summary>
        public TimeSpan DelayFor(int attempt)
        {
            if (attempt < 1) throw new ArgumentOutOfRangeException(nameof(attempt));
            double s = Math.Min(_max, _base * Math.Pow(2, attempt - 1));
            return TimeSpan.FromSeconds(s);
        }
    }

    /// <summary>
    /// Pure session state machine (no engine types, so it is unit-tested in CI). The Unity layer reports
    /// events into it and does what the returned decision says.
    /// </summary>
    public sealed class SessionLifecycle
    {
        readonly ReconnectPolicy _policy;
        public SessionState State { get; private set; } = SessionState.Offline;
        public int Attempt { get; private set; }
        public string LastMessage { get; private set; }
        public event Action<SessionState> StateChanged;

        public SessionLifecycle(ReconnectPolicy policy = null) { _policy = policy ?? new ReconnectPolicy(); }

        void Set(SessionState s, string msg = null) { State = s; LastMessage = msg; StateChanged?.Invoke(s); }

        /// <summary>Returns false (and does nothing) if a connection attempt is already underway or active.</summary>
        public bool BeginConnect()
        {
            if (State != SessionState.Offline && State != SessionState.Failed) return false;
            Attempt = 0; Set(SessionState.Connecting); return true;
        }

        public bool OnConnected()
        {
            if (State != SessionState.Connecting && State != SessionState.Reconnecting) return false;
            Attempt = 0; Set(SessionState.InSession); return true;
        }

        public ReconnectDecision OnConnectFailed(string message)
        {
            if (State != SessionState.Connecting) return None();
            Set(SessionState.Failed, message ?? "Could not join the session.");
            return new ReconnectDecision(false, TimeSpan.Zero, LastMessage);
        }

        /// <summary>Report a lost connection. Only transport failures/timeouts are worth retrying.</summary>
        public ReconnectDecision OnDisconnected(DisconnectReason reason)
        {
            if (State == SessionState.Offline || State == SessionState.Failed) return None();
            switch (reason)
            {
                case DisconnectReason.Intentional:
                    Set(SessionState.Offline); return new ReconnectDecision(false, TimeSpan.Zero, null);
                case DisconnectReason.HostLeft:
                    Set(SessionState.Failed, "The convoy host left. The session has ended.");
                    return new ReconnectDecision(false, TimeSpan.Zero, LastMessage);
                case DisconnectReason.Kicked:
                    Set(SessionState.Failed, "You were removed from the session.");
                    return new ReconnectDecision(false, TimeSpan.Zero, LastMessage);
                default:
                    if (State == SessionState.Connecting)
                    { Set(SessionState.Failed, "Connection failed."); return new ReconnectDecision(false, TimeSpan.Zero, LastMessage); }
                    Attempt = 1; Set(SessionState.Reconnecting, "Connection lost. Reconnecting...");
                    return new ReconnectDecision(true, _policy.DelayFor(Attempt), LastMessage);
            }
        }

        /// <summary>A reconnect attempt failed; decide whether to try again.</summary>
        public ReconnectDecision OnReconnectFailed()
        {
            if (State != SessionState.Reconnecting) return None();
            if (Attempt >= _policy.MaxAttempts)
            {
                Set(SessionState.Failed, "Could not reconnect to the session.");
                return new ReconnectDecision(false, TimeSpan.Zero, LastMessage);
            }
            Attempt++; LastMessage = $"Reconnecting (attempt {Attempt}/{_policy.MaxAttempts})...";
            return new ReconnectDecision(true, _policy.DelayFor(Attempt), LastMessage);
        }

        /// <summary>Player chose to leave (always allowed).</summary>
        public void Leave() { if (State != SessionState.Offline) Set(SessionState.Offline); }

        static ReconnectDecision None() => new ReconnectDecision(false, TimeSpan.Zero, null);
    }
}
