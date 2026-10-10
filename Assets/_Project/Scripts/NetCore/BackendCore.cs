using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ARO.NetCore
{
    // Pure logic behind the Unity backend layer: how failures are classified and retried, what a "loading" card is allowed to
    // show, how request bodies are built, when to heartbeat, and the CI-only switches. No engine types, so all of it is unit-tested.

    // ------------------------------------------------------------------------------------------------------------------ failures + retries
    public enum FailureKind { None, Offline, Timeout, SessionExpired, Forbidden, NotFound, RateLimited, Conflict, Server, Client }

    public static class ApiFailure
    {
        /// <summary>http = 0 means no HTTP response arrived at all.</summary>
        public static FailureKind Classify(long http, bool connectionError, bool timedOut, string code)
        {
            if (timedOut) return FailureKind.Timeout;
            if (connectionError || http == 0) return FailureKind.Offline;
            if (code == "rate_limited" || http == 429) return FailureKind.RateLimited;
            if (http == 401 || code == "not_authenticated") return FailureKind.SessionExpired;
            if (http == 403) return FailureKind.Forbidden;
            if (http == 404) return FailureKind.NotFound;
            if (http == 409) return FailureKind.Conflict;
            if (http >= 500) return FailureKind.Server;
            return FailureKind.Client;     // 4xx incl. 400 (a function raised a business rule such as job_full): retrying cannot help
        }

        /// <summary>Only problems that can heal by themselves are worth another attempt.</summary>
        public static bool Retryable(FailureKind k) => k == FailureKind.Offline || k == FailureKind.Timeout || k == FailureKind.Server;

        /// <summary>Message for failures the server gave no player-facing text for.</summary>
        public static string Describe(FailureKind k)
        {
            switch (k)
            {
                case FailureKind.Offline: return "You appear to be offline. Check your connection.";
                case FailureKind.Timeout: return "The server took too long to answer. Please try again.";
                case FailureKind.SessionExpired: return "Your session expired. Please sign in again.";
                case FailureKind.Forbidden: return "You are not allowed to do that.";
                case FailureKind.NotFound: return "That no longer exists.";
                case FailureKind.RateLimited: return "Too many requests. Wait a moment and try again.";
                case FailureKind.Conflict: return "That changed while you were doing it. Please try again.";
                case FailureKind.Server: return "Server problem. Please try again shortly.";
                default: return "Something went wrong.";
            }
        }
    }

    /// <summary>Reads are retried with exponential backoff; writes are never retried blindly (a lost reply may still have succeeded).</summary>
    public sealed class RetryPolicy
    {
        public readonly int MaxAttempts; public readonly double BaseDelaySeconds, MaxDelaySeconds;
        public RetryPolicy(int maxAttempts, double baseDelay, double maxDelay) { MaxAttempts = Math.Max(1, maxAttempts); BaseDelaySeconds = baseDelay; MaxDelaySeconds = maxDelay; }
        public static readonly RetryPolicy Reads = new RetryPolicy(3, 0.5, 4.0);

        public bool ShouldRetry(int attemptsMade, FailureKind kind, bool idempotent) => idempotent && attemptsMade < MaxAttempts && ApiFailure.Retryable(kind);

        /// <summary>Seconds to wait before attempt number nextAttempt (2 = the first retry).</summary>
        public double DelayBefore(int nextAttempt) => nextAttempt < 2 ? 0 : Math.Min(MaxDelaySeconds, BaseDelaySeconds * Math.Pow(2, nextAttempt - 2));
    }

    // ------------------------------------------------------------------------------------------------------------------ what a card may show
    /// <summary>Idle = never asked. Loading = asked, no answer yet. Ready/Empty = the server answered. Unavailable = the server did not (or refused): never papered over with invented data.</summary>
    public enum LoadState { Idle, Loading, Ready, Empty, Unavailable }

    /// <summary>One piece of backend data with its honest state. The previous value is kept while reloading (so a refresh does not blank the card) but a failure flips State to Unavailable.</summary>
    public sealed class Loadable<T>
    {
        public LoadState State { get; private set; } = LoadState.Idle;
        public T Value { get; private set; }
        public string Error { get; private set; }
        public FailureKind Failure { get; private set; }
        public double UpdatedAt { get; private set; } = double.NegativeInfinity;
        public event Action Changed;

        public void Begin() { State = LoadState.Loading; Error = null; Failure = FailureKind.None; Changed?.Invoke(); }
        public void Succeed(T value, bool empty, double now) { Value = value; State = empty ? LoadState.Empty : LoadState.Ready; Error = null; Failure = FailureKind.None; UpdatedAt = now; Changed?.Invoke(); }
        public void Fail(FailureKind kind, string message, double now) { State = LoadState.Unavailable; Failure = kind; Error = message; UpdatedAt = now; Changed?.Invoke(); }
        public void Reset() { State = LoadState.Idle; Value = default(T); Error = null; Failure = FailureKind.None; UpdatedAt = double.NegativeInfinity; Changed?.Invoke(); }

        public bool HasAnswer => State == LoadState.Ready || State == LoadState.Empty;
        public bool NeedsLoad(double now, double maxAgeSeconds) => State == LoadState.Idle || (State != LoadState.Loading && now - UpdatedAt > maxAgeSeconds);
    }

    // ------------------------------------------------------------------------------------------------------------------ request bodies
    public static class JsonArgs
    {
        /// <summary>Builds {"k":v,...}. Strings are escaped; null values are sent as null (the server treats a null argument as "not given").</summary>
        public static string Obj(params (string key, object value)[] pairs)
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i < pairs.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(pairs[i].key).Append("\":"); Value(sb, pairs[i].value);
            }
            return sb.Append('}').ToString();
        }

        static void Value(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string s: Quote(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case float f: Number(sb, f); break;
                case double d: Number(sb, d); break;
                case IEnumerable<string> list: sb.Append('['); bool first = true; foreach (var s in list) { if (!first) sb.Append(','); first = false; Quote(sb, s); } sb.Append(']'); break;
                default: throw new ArgumentException("unsupported argument type " + v.GetType().Name);
            }
        }

        static void Number(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) throw new ArgumentException("NaN/Infinity cannot be sent to the server");
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        public static string Escape(string s) { var sb = new StringBuilder(); Quote(sb, s); return sb.ToString(); }

        static void Quote(StringBuilder sb, string s)
        {
            if (s == null) { sb.Append("null"); return; }
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == '\u2028' || c == '\u2029') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    // ------------------------------------------------------------------------------------------------------------------ presence heartbeat
    public enum PresenceAction { None, StartSession, SendHeartbeat }

    /// <summary>
    /// Decides WHEN to talk to the presence endpoints; the service does the talking. Rules: open a session first, then beat roughly every
    /// third of the server's timeout; after a network failure back off (5 s, 10 s, ... max 60 s) instead of hammering; when the server says
    /// the session expired, open a new one immediately. Online means "the last answer arrived inside the timeout".
    /// </summary>
    public sealed class HeartbeatClock
    {
        public double IntervalSeconds { get; private set; } = 30; public double TtlSeconds { get; private set; } = 90;
        public bool HasSession { get; private set; }
        public int Failures { get; private set; }
        public double LastSuccessAt { get; private set; } = double.NegativeInfinity;
        bool _inFlight; double _nextAt;

        public PresenceAction Next(double now) => _inFlight || now < _nextAt ? PresenceAction.None : HasSession ? PresenceAction.SendHeartbeat : PresenceAction.StartSession;
        public void Began() { _inFlight = true; }

        public void SessionStarted(double now, double ttlSeconds)
        {
            HasSession = true; _inFlight = false; Failures = 0; TtlSeconds = ttlSeconds > 0 ? ttlSeconds : 90;
            IntervalSeconds = Math.Max(10, Math.Min(45, TtlSeconds / 3)); LastSuccessAt = now; _nextAt = now + IntervalSeconds;
        }

        /// <summary>Something presence-relevant changed (activity, route): send the next heartbeat soon, but never more often than minGapSeconds after the last success.</summary>
        public void Nudge(double now, double minGapSeconds = 5)
        {
            if (!HasSession || _inFlight) return;
            double at = Math.Max(now, LastSuccessAt + minGapSeconds);
            if (at < _nextAt) _nextAt = at;
        }

        public void BeatAccepted(double now) { _inFlight = false; Failures = 0; LastSuccessAt = now; _nextAt = now + IntervalSeconds; }

        /// <summary>sessionGone: the server no longer knows / has expired the session, so start a fresh one right away.</summary>
        public void Failed(double now, bool sessionGone)
        {
            _inFlight = false;
            if (sessionGone) { HasSession = false; _nextAt = now; return; }
            Failures++; _nextAt = now + Math.Min(60, 5 * Math.Pow(2, Failures - 1));
        }

        public bool Connected(double now) => HasSession && now - LastSuccessAt < TtlSeconds;
        public void Reset() { HasSession = false; _inFlight = false; Failures = 0; _nextAt = 0; LastSuccessAt = double.NegativeInfinity; }
    }

    // ------------------------------------------------------------------------------------------------------------------ CI-only switches
    /// <summary>
    /// Lets the browser tests point the build at a throw-away backend. It is honoured ONLY when the page itself is served from the loopback
    /// interface AND the target is loopback too, so a crafted link on the public site can never redirect a player's login anywhere.
    /// </summary>
    public static class BackendOverride
    {
        public static bool IsLoopbackHost(string host) => host == "localhost" || host == "127.0.0.1" || host == "[::1]" || host == "::1";

        public static bool PageIsLoopback(string pageUrl)
        {
            return Uri.TryCreate(pageUrl ?? "", UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https") && IsLoopbackHost(u.Host);
        }

        public static string Query(string pageUrl, string key)
        {
            if (string.IsNullOrEmpty(pageUrl)) return null;
            int q = pageUrl.IndexOf('?'); if (q < 0) return null;
            string rest = pageUrl.Substring(q + 1); int hash = rest.IndexOf('#'); if (hash >= 0) rest = rest.Substring(0, hash);
            foreach (var kv in rest.Split('&'))
            {
                int eq = kv.IndexOf('='); if (eq <= 0 || kv.Substring(0, eq) != key) continue;
                try { return Uri.UnescapeDataString(kv.Substring(eq + 1)); } catch (UriFormatException) { return null; }
            }
            return null;
        }

        public static bool TryParse(string pageUrl, out string backendUrl, out string anonKey)
        {
            backendUrl = anonKey = null;
            if (!PageIsLoopback(pageUrl)) return false;
            string b = Query(pageUrl, "backend"), a = Query(pageUrl, "anon");
            if (string.IsNullOrEmpty(b) || string.IsNullOrEmpty(a)) return false;
            if (!Uri.TryCreate(b, UriKind.Absolute, out var u) || !(u.Scheme == "http" || u.Scheme == "https") || !IsLoopbackHost(u.Host)) return false;
            backendUrl = b.TrimEnd('/'); anonKey = a; return true;
        }
    }

    public enum FaultKind { None, Slow, Error, Offline }
    public struct Fault { public FaultKind Kind; public double Seconds; }

    /// <summary>
    /// "?fault=list_available_jobs:slow:9,get_content:error:1" makes the matching request slow / fail with HTTP 503 / look offline (N times, then
    /// behave normally). Only active on loopback pages. It injects FAILURES, never data: the loading, error and retry states of the real
    /// dashboard are exercised against the real backend.
    /// </summary>
    public sealed class FaultPlan
    {
        sealed class Rule { public string Match; public FaultKind Kind; public double Arg; public int Remaining; }
        readonly List<Rule> _rules = new List<Rule>();
        public static readonly FaultPlan None = new FaultPlan();
        public int RuleCount => _rules.Count;

        public static FaultPlan Parse(string pageUrl)
        {
            var plan = new FaultPlan();
            if (!BackendOverride.PageIsLoopback(pageUrl)) return plan;
            string spec = BackendOverride.Query(pageUrl, "fault"); if (string.IsNullOrEmpty(spec)) return plan;
            foreach (var part in spec.Split(','))
            {
                var f = part.Split(':'); if (f.Length < 2 || f[0].Length == 0) continue;
                double arg = 0; if (f.Length > 2) double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out arg);
                switch (f[1])
                {
                    case "slow": plan._rules.Add(new Rule { Match = f[0], Kind = FaultKind.Slow, Arg = arg > 0 ? arg : 5, Remaining = 1 }); break;
                    case "error": plan._rules.Add(new Rule { Match = f[0], Kind = FaultKind.Error, Remaining = arg > 0 ? (int)arg : 1 }); break;
                    case "offline": plan._rules.Add(new Rule { Match = f[0], Kind = FaultKind.Offline, Remaining = arg > 0 ? (int)arg : 1 }); break;
                }
            }
            return plan;
        }

        /// <summary>The fault to apply to a request for this path (e.g. "/rest/v1/rpc/get_content"), consuming one use of the matching rule.</summary>
        public Fault Next(string path)
        {
            foreach (var r in _rules)
                if (r.Remaining > 0 && path != null && path.Contains(r.Match)) { r.Remaining--; return new Fault { Kind = r.Kind, Seconds = r.Arg }; }
            return new Fault { Kind = FaultKind.None };
        }
    }

    // ------------------------------------------------------------------------------------------------------------------ presentation helpers
    public static class NoticeMap
    {
        /// <summary>Colour class of a server notification type: good news green, errors red, everything else neutral.</summary>
        public static NoticeLevel LevelFor(string type)
        {
            switch (type)
            {
                case "payout": case "level_up": case "achievement": return NoticeLevel.Good;
                case "error": return NoticeLevel.Bad;
                default: return NoticeLevel.Info;
            }
        }
    }

    public static class TimeText
    {
        /// <summary>"just now" / "5 min ago" / "3 h ago" / "2 d ago", measured on SERVER time (clock skew on the player's device is irrelevant).</summary>
        public static string Ago(DateTimeOffset when, DateTimeOffset now)
        {
            var s = (now - when).TotalSeconds;
            if (s < 60) return "just now";
            if (s < 3600) return (int)(s / 60) + " min ago";
            if (s < 86400) return (int)(s / 3600) + " h ago";
            return (int)(s / 86400) + " d ago";
        }

        public static bool TryParse(string iso, out DateTimeOffset t) =>
            DateTimeOffset.TryParse(iso ?? "", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out t);

        public static string AgoFrom(string iso, string serverNowIso)
        {
            if (!TryParse(iso, out var w)) return "";
            if (!TryParse(serverNowIso, out var n)) n = DateTimeOffset.UtcNow;
            return Ago(w, n);
        }
    }
}
