using System;
using System.Text;
using System.Threading.Tasks;
using ARO.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace ARO.Backend
{
    /// <summary>
    /// Minimal Supabase client (GoTrue auth + PostgREST) over UnityWebRequest, WebGL-safe (no threads).
    /// Uses the anon key + the player's JWT only; every privileged action is an RPC validated server-side.
    /// </summary>
    public class SupabaseClient
    {
        const string RefreshKey = "aro.refresh_token";   // refresh token only; safe to lose
        readonly BackendConfig _cfg;
        SessionDto _session;
        DateTime _expiresAt;

        public SupabaseClient(BackendConfig cfg) { _cfg = cfg; }
        public bool IsSignedIn => _session != null;
        public string UserId => _session?.user?.id;
        public string AccessToken => _session?.access_token;

        // ------------------------------------------------------------ auth
        public async Task<Result<SessionDto>> SignUp(string email, string password, string displayName)
        {
            string body = JsonUtility.ToJson(new SignUpBody { email = email, password = password, data = new Meta { display_name = displayName } });
            return await AuthCall("/auth/v1/signup", body);
        }

        public Task<Result<SessionDto>> SignIn(string email, string password) =>
            AuthCall("/auth/v1/token?grant_type=password",
                JsonUtility.ToJson(new PasswordBody { email = email, password = password }));

        /// <summary>Restore a persisted session at startup. Returns false if none / expired.</summary>
        public async Task<bool> TryRestoreSession()
        {
            string rt = PlayerPrefs.GetString(RefreshKey, "");
            if (string.IsNullOrEmpty(rt)) return false;
            var r = await AuthCall("/auth/v1/token?grant_type=refresh_token", JsonUtility.ToJson(new RefreshBody { refresh_token = rt }));
            if (!r.Ok) PlayerPrefs.DeleteKey(RefreshKey);
            return r.Ok;
        }

        public async Task<Result<bool>> RecoverPassword(string email)
        {
            var r = await Send("POST", "/auth/v1/recover", JsonUtility.ToJson(new RecoverBody { email = email }), authed: false);
            return r.Ok ? Result<bool>.Success(true) : Result<bool>.Fail(r.ErrorCode, r.UserMessage);
        }

        public void SignOut() { _session = null; PlayerPrefs.DeleteKey(RefreshKey); }

        async Task<Result<SessionDto>> AuthCall(string path, string body)
        {
            var r = await Send("POST", path, body, authed: false);
            if (!r.Ok) return Result<SessionDto>.Fail(r.ErrorCode, r.UserMessage);
            var s = JsonUtility.FromJson<SessionDto>(r.Value);
            if (s == null || string.IsNullOrEmpty(s.access_token))
                return Result<SessionDto>.Fail("no_session", "Check your email to confirm your account, then sign in.");
            _session = s; _expiresAt = DateTime.UtcNow.AddSeconds(s.expires_in - 30);
            PlayerPrefs.SetString(RefreshKey, s.refresh_token); PlayerPrefs.Save();
            return Result<SessionDto>.Success(s);
        }

        async Task EnsureFreshToken()
        {
            if (_session == null || DateTime.UtcNow < _expiresAt) return;
            await AuthCall("/auth/v1/token?grant_type=refresh_token", JsonUtility.ToJson(new RefreshBody { refresh_token = _session.refresh_token }));
        }

        // ------------------------------------------------------------ data
        /// <summary>PostgREST select. `query` e.g. "jobs?status=eq.open&select=*,origin:locations!origin_id(*)".</summary>
        public async Task<Result<T[]>> Select<T>(string query)
        {
            var r = await Send("GET", "/rest/v1/" + query, null, authed: true);
            if (!r.Ok) return Result<T[]>.Fail(r.ErrorCode, r.UserMessage);
            var w = JsonUtility.FromJson<ListWrapper<T>>("{\"items\":" + r.Value + "}");
            return Result<T[]>.Success(w?.items ?? new T[0]);
        }

        /// <summary>Call a SECURITY DEFINER function. Returns raw JSON body.</summary>
        public Task<Result<string>> Rpc(string fn, string argsJson = "{}") =>
            Send("POST", "/rest/v1/rpc/" + fn, argsJson, authed: true);

        // ------------------------------------------------------------ transport
        async Task<Result<string>> Send(string method, string path, string body, bool authed)
        {
            if (!_cfg.IsConfigured)
                return Result<string>.Fail("not_configured", "Backend is not configured. See docs/DEVELOPMENT.md.");
            if (authed) await EnsureFreshToken();
            if (authed && _session == null) return Result<string>.Fail("not_authenticated", "Please sign in.");

            using var req = new UnityWebRequest(_cfg.supabaseUrl.TrimEnd('/') + path, method);
            if (body != null) req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("apikey", _cfg.anonKey);
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + (authed ? _session.access_token : _cfg.anonKey));
            req.timeout = 15;

            var tcs = new TaskCompletionSource<bool>();
            req.SendWebRequest().completed += _ => tcs.TrySetResult(true);
            await tcs.Task;

            string text = req.downloadHandler.text;
            if (req.result == UnityWebRequest.Result.Success) return Result<string>.Success(text);
            if (req.result == UnityWebRequest.Result.ConnectionError)
                return Result<string>.Fail("network", "Connection lost. Check your internet and try again.");

            string code = ExtractErrorCode(text);
            Debug.LogWarning($"[Backend] {method} {path} -> {req.responseCode} {text}");
            return Result<string>.Fail(code, ErrorMessages.ToUser(code, req.responseCode));
        }

        static string ExtractErrorCode(string json)
        {
            if (string.IsNullOrEmpty(json)) return "unknown";
            var e = JsonUtility.FromJson<ErrorBody>(json);
            return e?.message ?? e?.msg ?? e?.error_description ?? "unknown";
        }

        [Serializable] class ErrorBody { public string message, msg, error_description; }
        [Serializable] class Meta { public string display_name; }
        [Serializable] class SignUpBody { public string email, password; public Meta data; }
        [Serializable] class PasswordBody { public string email, password; }
        [Serializable] class RefreshBody { public string refresh_token; }
        [Serializable] class RecoverBody { public string email; }
    }
}
