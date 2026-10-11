using System.Collections.Generic;
using System.Threading.Tasks;
using ARO.Backend;
using ARO.Core;
using ARO.Multiplayer;
using ARO.NetCore;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ARO.Game
{
    /// <summary>Menu/screen state machine: Login -> Menu -> JobBoard / Garage / Profile -> Driving.</summary>
    public class GameFlow : MonoBehaviour
    {
        enum Screen { Login, Menu, Jobs, Garage, Profile, Convoy, Bus, Shop }

        GameServices _svc; DrivingSession _drive; Hud _hud; BusSession _bus;
        Canvas _canvas; RectTransform _panel; Text _toast; float _toastUntil;
        bool _busy;
        bool _sessionLost;                                 // set by SupabaseClient.SessionExpired; handled on the main loop

        public void Init(GameServices svc, DrivingSession drive, Hud hud, BusSession bus)
        {
            _svc = svc; _drive = drive; _hud = hud; _bus = bus;
            _canvas = UIKit.CreateCanvas("Menus", 10);
            UIKit.Box(_canvas.transform, "Backdrop", new Color(0.03f, 0.04f, 0.06f, 0.78f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _toast = UIKit.Label(_canvas.transform, "", 26, UIKit.Accent, TextAnchor.LowerCenter, new Vector2(0, 0), new Vector2(1, 0.12f), Vector2.zero, Vector2.zero);
            // The server writes the payout / level-up / achievement notifications itself; we only refresh what we show.
            _drive.JobCompleted += async r => { if (r != null) Toast($"Job complete: +{r.reward:N0} coins, +{r.xp:N0} XP", 5f); await _svc.RefreshPlayer(); _ = _svc.Notifications.RefreshUnread(); };
            _bus.RunCompleted += async result => { await _svc.RefreshPlayer(); _ = _svc.Notifications.RefreshUnread(); };
            _svc.Session.StateChanged += (st, msg) => { if (!string.IsNullOrEmpty(msg)) Toast(msg, 6f); };
            _svc.Api.SessionExpired += () => _sessionLost = true;
            _ = StartUp();
        }

        async Task StartUp()
        {
            if (SmokeMode.WorldMap)
            {   // CI preview only: fixture data so the map can be exercised without an account or a backend (never reachable from the UI)
                await Task.Yield(); LoadFixture();
                Debug.Log("[Smoke] worldmap scenario: fixture data, no sign-in"); OpenWorldMap();
                return;
            }
            if (SmokeMode.Drive)
            {
                await Task.Yield();
                Debug.Log("[Smoke] drive scenario: skipping sign-in");
                _drive.Enter(new OwnedVehicleDto { id = "smoke-truck", definition_id = "truck_light_01", fuel_l = 120, damage_pct = 0 });
                _canvas.gameObject.SetActive(false); _hud.SetVisible(true); _drive.Pause(false);
                return;
            }
            if (!_svc.Config.IsConfigured) { ShowLogin("Backend not configured (see docs/DEVELOPMENT.md). Jobs and persistence are disabled."); return; }
            string url = Application.absoluteURL ?? "";
            if (url.Contains("error_description="))   // an OAuth provider sent the player back with an error
            {
                string msg = "Sign-in was cancelled or failed. Please try again.";
                int i = url.IndexOf("error_description=", System.StringComparison.Ordinal);
                if (i >= 0) { string d = url.Substring(i + 18); int amp = d.IndexOf('&'); if (amp >= 0) d = d.Substring(0, amp); msg = UnityEngine.Networking.UnityWebRequest.UnEscapeURL(d).Replace('+', ' '); }
                ARO.Backend.WebBridge.ClearUrlHash(); ShowLogin(msg); return;
            }
            if (SmokeMode.AutoLogin(out string email, out string password))
            {   // CI only (loopback pages): sign in with throw-away test credentials so the browser test can reach the dashboard on a real backend
                Toast("Signing in...");
                var si = await _svc.Api.SignIn(email, password);
                if (si.Ok) { await EnterMenu(); return; }
                ShowLogin(si.UserMessage); return;
            }
            Toast("Checking saved session...");
            if (await _svc.Api.TryCompleteOAuthRedirect(url) || await _svc.Api.TryRestoreSession()) await EnterMenu(); else ShowLogin(null);
        }

        void Update()
        {
            if (_toast != null && Time.unscaledTime > _toastUntil) _toast.text = "";
            if (_sessionLost) { _sessionLost = false; LoseSession(); }
            if (_svc.Api.IsSignedIn && _svc.Profile != null)
            {   // presence: the server marks us online while heartbeats arrive; notifications: the bell badge follows the server's unread count
                _svc.Presence.Tick(Activity(), CurrentRouteCode(), null);
                _svc.Notifications.PollUnread();
            }
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && _svc.Api.IsSignedIn && _map == null)
            {
                if (_drive.Active) { _drive.Pause(true); _hud.SetVisible(false); _canvas.gameObject.SetActive(true); Show(Screen.Menu); }
                else if (_drive.Vehicle != null) Resume();
            }
        }

        /// <summary>What the player is doing right now, as the presence service reports it to the server.</summary>
        string Activity() => _bus.Active && _drive.Active ? "bus" : _drive.Active ? "driving" : _svc.Session.State == ARO.NetCore.SessionState.InSession ? "convoy" : "menu";
        string CurrentRouteCode() => _drive.Active && _drive.Route != null && _drive.Route.Spec != null && _drive.Route.Spec.meta != null ? _drive.Route.Spec.meta.worldMapRouteId : null;

        /// <summary>The server rejected our session and it could not be refreshed: back to sign-in, with the reason.</summary>
        void LoseSession()
        {
            Debug.Log("[Session] expired: back to sign-in");
            _svc.Presence.Clock.Reset(); _svc.ClearPlayerCaches();
            if (_drive.Active) { _drive.Pause(true); _hud.SetVisible(false); }
            _canvas.gameObject.SetActive(false);
            ShowLogin("Your session expired. Please sign in again.");
        }

        void Toast(string m, float s = 4f, NoticeLevel? log = null) { _toast.text = m; _toastUntil = Time.unscaledTime + s; if (_dash != null && _dash.gameObject.activeInHierarchy) _dash.Toast(m, s); if (_map != null) _map.Notify(m, s); }

        // ---------------------------------------------------------------- panels
        RectTransform NewPanel(string title, float h = 760f)
        {
            if (_panel != null) Destroy(_panel.gameObject);
            Debug.Log("[Game] panel=" + title);
            var box = UIKit.Box(_canvas.transform, "Panel", UIKit.Panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-380, -h / 2f), new Vector2(380, h / 2f));
            _panel = box;
            UIKit.Label(box, "AFRICAN ROADS ONLINE", 20, UIKit.Accent, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(1, 1), new Vector2(32, -48), new Vector2(-32, -20));
            UIKit.Label(box, title, 44, UIKit.TextCol, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(1, 1), new Vector2(32, -110), new Vector2(-32, -50));
            var stack = UIKit.VStack(box, "Stack", Vector2.zero, 12, 32);
            stack.anchorMin = Vector2.zero; stack.anchorMax = Vector2.one; stack.offsetMin = new Vector2(0, 0); stack.offsetMax = new Vector2(0, -130);
            return stack;
        }

        void Show(Screen s)
        {
            switch (s)
            {
                case Screen.Menu: ShowMenu(); break;
                case Screen.Jobs: ShowMenu(); if (_dash != null) _dash.OpenJobs(); break;
                case Screen.Garage: ShowMenu(); if (_dash != null) _dash.OpenGarage(); break;
                case Screen.Profile: ShowMenu(); if (_dash != null) _dash.OpenProfile(); break;
                case Screen.Convoy: ShowMenu(); if (_dash != null) _dash.OpenMultiplayer(); break;
                case Screen.Bus: _ = ShowBus(); break;
                case Screen.Shop: ShowMenu(); if (_dash != null) _dash.OpenVehicles(); break;
            }
        }

        // ---------------------------------------------------------------- login
        LoginScreen _login;

        void ShowLogin(string info)
        {
            if (_panel != null) Destroy(_panel.gameObject);
            if (_login != null) Destroy(_login.gameObject);
            if (_dash != null) Destroy(_dash.gameObject);
            CloseMap();
            _canvas.gameObject.SetActive(false);   // the login screen is a full-screen canvas of its own
            _login = LoginScreen.Create(_svc, async () =>
            {
                if (_login != null) Destroy(_login.gameObject);
                _canvas.gameObject.SetActive(true);
                await EnterMenu();
            }, info);
        }

        async Task EnterMenu()
        {
            Toast("Loading your profile...");
            string err = await _svc.RefreshPlayer();
            if (err != null) { _svc.Api.SignOut(); _svc.ClearPlayerCaches(); ShowLogin(err); return; }
            _svc.Presence.Clock.Reset();
            _ = LoadWorldState();
            ShowMenu();
        }

        /// <summary>The server's route/city state decides what the map and the dashboard show as open; until it answers they fail closed.</summary>
        async Task LoadWorldState()
        {
            var m = WorldMapScreen.Model(); if (m == null) return;
            await _svc.World.Load(m);
            Debug.Log("[World] state=" + _svc.World.State.State.ToString().ToLowerInvariant() + (_svc.World.State.Error != null ? " error=" + _svc.World.State.Error : ""));
            if (_dash != null && _dash.gameObject.activeInHierarchy) _dash.OnWorldChanged();
        }

        // ---------------------------------------------------------------- menu = the dashboard
        DashboardScreen _dash; JobDto[] _fixtureJobs;

        void LoadFixture()
        {
            _svc.Profile = new ProfileDto { id = "fixture", display_name = "Demo Driver", level = 5, experience = 2050, jobs_completed = 12, distance_km = 140 };
            _svc.Wallet = new WalletDto { player_id = "fixture", balance = 48750 };
            _svc.Definitions = new[] { new VehicleDefDto { id = "truck_light_01", category = "truck", name = "Savanna 4x2 Light Truck", fuel_capacity_l = 120, max_speed_kmh = 110 } };
            _svc.Vehicles = new[] { new OwnedVehicleDto { id = "fixture-truck", definition_id = "truck_light_01", fuel_l = 90, damage_pct = 4 } };
            LocationDto L(string slug, string n) => new LocationDto { slug = slug, name = n };
            _fixtureJobs = new[]
            {
                new JobDto { id = "1", code = "AFR-000101", cargo_type = "Electronics", required_category = "truck", difficulty = 3, distance_km = 3.0, reward = 266, origin = L("lagos-apapa-port", "Apapa Port Depot"), destination = L("lagos-mile12-market", "Mile 12 Market") },
                new JobDto { id = "2", code = "AFR-000102", cargo_type = "Fuel Drums", required_category = "truck", difficulty = 2, distance_km = 4.4, reward = 410, origin = L("lagos-mile12-market", "Mile 12 Market"), destination = L("lagos-ikeja-industrial", "Ikeja Industrial Estate") },
                new JobDto { id = "3", code = "AFR-000103", cargo_type = "Building Materials", required_category = "truck", difficulty = 4, distance_km = 6.1, reward = 780, origin = L("lagos-ikeja-industrial", "Ikeja Industrial Estate"), destination = L("ibadan-ojoo-depot", "Ojoo Freight Depot") },
                new JobDto { id = "4", code = "AFR-000104", cargo_type = "Food & Produce", required_category = "truck", difficulty = 5, distance_km = 9.8, reward = 1260, origin = L("ibadan-ojoo-depot", "Ojoo Freight Depot"), destination = L("ibadan-bodija-market", "Bodija Market") },
            };
        }

        void ShowMenu()
        {
            if (_panel != null) { Destroy(_panel.gameObject); _panel = null; }
            _canvas.gameObject.SetActive(false);
            if (_dash != null) Destroy(_dash.gameObject);
            if (_map != null) { Destroy(_map.gameObject); _map = null; }
            _dash = DashboardScreen.Create(_svc, _drive, _bus, DashActions());
        }

        void OpenPanel(System.Action show) { if (_dash != null) _dash.gameObject.SetActive(false); _canvas.gameObject.SetActive(true); show(); }
        void HideMenus() { _canvas.gameObject.SetActive(false); if (_dash != null) _dash.gameObject.SetActive(false); if (_map != null) { Destroy(_map.gameObject); _map = null; } }

        // ---------------------------------------------------------------- world map (strategic UI only; the 3D world is a separate system)
        WorldMapScreen _map;

        void OpenWorldMap()
        {
            if (_panel != null) { Destroy(_panel.gameObject); _panel = null; }
            _canvas.gameObject.SetActive(false); if (_dash != null) _dash.gameObject.SetActive(false);
            if (_map != null) Destroy(_map.gameObject);
            _map = WorldMapScreen.Create(_svc, _drive, new WorldMapActions
            {
                Back = ShowMenu,
                Jobs = () => { CloseMap(); ShowMenu(); if (_dash != null) _dash.OpenJobs(); },
                Bus = () => { CloseMap(); OpenPanel(() => _ = ShowBus()); },
                FreeDrive = () => { CloseMap(); if (_drive.Vehicle != null) Resume(); else { HideMenus(); StartDrive(null); } },
                AcceptJob = job => _ = AcceptJob(job, false),
            }, _fixtureJobs);
        }
        void CloseMap() { if (_map != null) { Destroy(_map.gameObject); _map = null; } }

        DashboardActions DashActions() => new DashboardActions
        {
            Jobs = () => { if (_dash != null) _dash.OpenJobs(); }, Garage = () => { ShowMenu(); if (_dash != null) _dash.OpenGarage(); }, Shop = () => { ShowMenu(); if (_dash != null) _dash.OpenVehicles(); },
            Convoy = () => { if (_dash != null) _dash.OpenMultiplayer(); }, ConvoyDrive = c => _ = ConvoyDriveFlow(c), Bus = () => OpenPanel(() => _ = ShowBus()), Profile = () => { if (_dash != null) _dash.OpenProfile(); },
            FreeDrive = () => { if (_drive.Vehicle != null) Resume(); else { HideMenus(); StartDrive(null); } },
            Resume = Resume,
            SignOut = () => { _ = _svc.Presence.EndSession(); _svc.Api.SignOut(); _svc.ClearPlayerCaches(); if (_dash != null) Destroy(_dash.gameObject); ShowLogin(null); },
            AcceptJob = job => _ = AcceptJob(job),
            QuickJob = () => _ = QuickJob(),
            WorldMap = OpenWorldMap,
        };

        /// <summary>One tap: take the best-paying open job the player has a vehicle for.</summary>
        async Task QuickJob()
        {
            if (_busy) return; if (_drive.Job != null) { Toast("Finish or abandon your current job first."); return; }
            Toast("Finding a job...");
            var r = await _svc.Jobs.LoadBoard();
            if (!r.Ok) { Toast(r.UserMessage, 5f); return; }
            JobDto best = null;
            foreach (var j in r.Value) if (j.eligible && FindVehicleFor(j.required_category) != null && (best == null || j.reward > best.reward)) best = j;
            if (best == null) { Toast("No open job you can take right now."); return; }
            await AcceptJob(best);
        }

        void Resume() { HideMenus(); _hud.SetVisible(true); _drive.Pause(false); }

        /// <summary>Enter the world. A job needs its own vehicle (the one the server assigned); free drive uses the first truck.</summary>
        void StartDrive(JobDto job, string assignmentId = null, OwnedVehicleDto vehicle = null)
        {
            var owned = vehicle ?? (_drive.Vehicle == null ? FirstTruckOrAny() : null);
            if (owned != null && owned.id != _drive.VehicleId) _drive.Enter(owned);
            if (_drive.Vehicle == null) { Toast("You do not own a vehicle."); return; }
            if (job != null) _drive.BeginJob(job, assignmentId);
            HideMenus(); _hud.SetVisible(true); _drive.Pause(false);
        }

        OwnedVehicleDto FirstTruckOrAny() => FindVehicleFor("truck") ?? (_svc.Vehicles.Length > 0 ? _svc.Vehicles[0] : null);

        // ---------------------------------------------------------------- jobs
        async Task<bool> AcceptJob(JobDto job, bool showBoardOnError = true)
        {
            if (_busy) return false; if (_drive.Job != null) { Toast("Finish or abandon your current job first."); return false; }
            var owned = FindVehicleFor(job.required_category);
            if (owned == null) { Toast("You have no " + job.required_category + " for this job."); return false; }
            _busy = true; var r = await _svc.Jobs.Accept(job.id, owned.id); _busy = false;
            if (!r.Ok) { Toast(r.UserMessage, 6f, NoticeLevel.Bad); if (_dash != null) _dash.ReloadJobsPage(); return false; }
            StartDrive(job, r.Value, owned); return true;
        }

        OwnedVehicleDto FindVehicleFor(string category)
        {
            var cat = new Dictionary<string, string>(); foreach (var d in _svc.Definitions) cat[d.id] = d.category;
            foreach (var v in _svc.Vehicles) if (cat.TryGetValue(v.definition_id, out var c) && c == category) return v;
            return null;
        }

        // ---------------------------------------------------------------- convoy
        /// <summary>
        /// Start (leader) or join (member) the live drive of a convoy. The leader's start is validated by the server (route chosen, everyone ready,
        /// levels) before the live session is created; a failure at any step leaves the convoy in the lobby with the reason on screen.
        /// </summary>
        async Task ConvoyDriveFlow(ConvoyInfo c)
        {
            if (_busy || c == null) return;
            if (FirstTruckOrAny() == null) { Toast("You need a vehicle to drive with the convoy.", 5f, NoticeLevel.Bad); return; }
            _busy = true; if (_dash != null && c.is_leader && !c.Driving) _dash.SetStarting(true);
            try
            {
                if (c.is_leader && !c.Driving)
                {
                    Toast("Starting the convoy...");
                    var r = await _svc.Convoys.StartDriving();
                    if (!r.Ok) { Toast(r.UserMessage, 7f, NoticeLevel.Bad); if (_dash != null) _dash.ReloadMultiplayer(); return; }
                }
                else if (c.is_leader)
                {   // rejoining: the leader hosts the live session; if it is gone the drive cannot continue, so the convoy goes back to the lobby
                    if (_svc.Session.State != ARO.NetCore.SessionState.InSession)
                    {
                        await _svc.Convoys.EndDriving();
                        Toast("The live session ended, so the convoy is back in the lobby. Start it again when everyone is ready.", 7f, NoticeLevel.Bad); if (_dash != null) _dash.ReloadMultiplayer(); return;
                    }
                }
                else if (_svc.Session.State != ARO.NetCore.SessionState.InSession)
                {
                    Toast("Joining the convoy...");
                    var j = await _svc.Convoys.JoinLiveSession(c.id);
                    if (!j.Ok) { Toast(j.UserMessage, 7f, NoticeLevel.Bad); return; }
                }
                HideMenus();
                if (_drive.Vehicle != null) Resume(); else StartDrive(null);
            }
            finally { _busy = false; if (_dash != null) _dash.SetStarting(false); }
        }

        // ---------------------------------------------------------------- bus
        async Task ShowBus()
        {
            var p = NewPanel("Bus Routes", 900f);
            if (_bus.Active)
            {
                UIKit.Label(p, $"{_bus.Route.code}  {_bus.Route.name}\nNext: {_bus.NextStop.location.name}   ·   aboard {_bus.State.Aboard}/{_bus.Capacity}   ·   fares {_bus.State.Revenue:N0}", 24, UIKit.TextCol, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<LayoutElement>().preferredHeight = 100;
                UIKit.Btn(p, "RESUME ROUTE", Resume);
                UIKit.Btn(p, "ABANDON ROUTE (no pay)", async () => { await _bus.Abandon(); await ShowBus(); }, false, 48);
                UIKit.Btn(p, "BACK", ShowMenu, false, 48);
                return;
            }
            UIKit.Label(p, "Loading routes...", 22, UIKit.Muted, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var r = await _svc.Bus.LoadRoutes();
            p = NewPanel("Bus Routes", 900f);
            var bus = FindVehicleFor("bus");
            if (!r.Ok) UIKit.Label(p, r.UserMessage, 22, UIKit.Bad, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            else if (bus == null) UIKit.Label(p, "You need a bus to run a route. Buy one in the shop.", 22, UIKit.Accent, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<LayoutElement>().preferredHeight = 60;
            else if (r.Value.Length == 0) UIKit.Label(p, "No bus routes available yet.", 22, UIKit.Muted, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            if (r.Ok && bus != null)
                foreach (var rt in r.Value)
                {
                    var route = rt;
                    UIKit.Btn(p, $"{route.code}  {route.name}\n{route.stops.Length} stops · {route.fare} coins per passenger · {route.xp_reward} XP",
                        async () => await BeginBusRoute(route, bus), false, 84);
                }
            UIKit.Btn(p, "TRUCKS & BUSES", () => { ShowMenu(); if (_dash != null) _dash.OpenVehicles(); }, false, 48);
            UIKit.Btn(p, "BACK", ShowMenu, true, 48);
        }

        async Task BeginBusRoute(BusRouteDto route, OwnedVehicleDto bus)
        {
            if (_busy) return; _busy = true; Toast("Starting route...");
            string err = await _bus.Begin(route, bus); _busy = false;
            if (err != null) { Toast(err, 6f, NoticeLevel.Bad); await ShowBus(); return; }
            HideMenus(); _hud.SetVisible(true); _drive.Pause(false);
        }
    }
}
