using System.Collections.Generic;
using System.Threading.Tasks;
using ARO.Backend;
using ARO.Core;
using ARO.Multiplayer;
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

        public void Init(GameServices svc, DrivingSession drive, Hud hud, BusSession bus)
        {
            _svc = svc; _drive = drive; _hud = hud; _bus = bus;
            _canvas = UIKit.CreateCanvas("Menus", 10);
            UIKit.Box(_canvas.transform, "Backdrop", new Color(0.03f, 0.04f, 0.06f, 0.78f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _toast = UIKit.Label(_canvas.transform, "", 26, UIKit.Accent, TextAnchor.LowerCenter, new Vector2(0, 0), new Vector2(1, 0.12f), Vector2.zero, Vector2.zero);
            _drive.JobCompleted += async _ => { await _svc.RefreshPlayer(); };
            _bus.RunCompleted += async _ => { await _svc.RefreshPlayer(); };
            _svc.Session.StateChanged += (st, msg) => { if (!string.IsNullOrEmpty(msg)) Toast(msg, 6f); };
            _ = StartUp();
        }

        async Task StartUp()
        {
            if (SmokeMode.Dashboard)
            {   // CI preview only: fixture data so the layout can be screenshotted without an account
                await Task.Yield();
                _svc.Profile = new ProfileDto { id = "fixture", display_name = "Demo Driver", level = 5, experience = 2050, jobs_completed = 12, distance_km = 140 };
                _svc.Wallet = new WalletDto { player_id = "fixture", balance = 48750 };
                _svc.Definitions = new[] { new VehicleDefDto { id = "truck_light_01", category = "truck", name = "Savanna 4x2 Light Truck", fuel_capacity_l = 120, max_speed_kmh = 110 } };
                _svc.Vehicles = new[] { new OwnedVehicleDto { id = "fixture-truck", definition_id = "truck_light_01", fuel_l = 90, damage_pct = 4 } };
                LocationDto L(string n) => new LocationDto { slug = n, name = n };
                _fixtureJobs = new[]
                {
                    new JobDto { id = "1", code = "AFR-000101", cargo_type = "Electronics", required_category = "truck", difficulty = 3, distance_km = 3.0, reward = 266, origin = L("Apapa Port Depot"), destination = L("Mile 12 Market") },
                    new JobDto { id = "2", code = "AFR-000102", cargo_type = "Fuel Drums", required_category = "truck", difficulty = 2, distance_km = 4.4, reward = 410, origin = L("Mile 12 Market"), destination = L("Ikeja Industrial Estate") },
                    new JobDto { id = "3", code = "AFR-000103", cargo_type = "Building Materials", required_category = "truck", difficulty = 4, distance_km = 6.1, reward = 780, origin = L("Ikeja Industrial Estate"), destination = L("Ojoo Freight Depot") },
                    new JobDto { id = "4", code = "AFR-000104", cargo_type = "Food & Produce", required_category = "truck", difficulty = 5, distance_km = 9.8, reward = 1260, origin = L("Ojoo Freight Depot"), destination = L("Bodija Market") },
                };
                Debug.Log("[Smoke] dashboard scenario: fixture data, no sign-in");
                ShowMenu();
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
            Toast("Checking saved session...");
            if (await _svc.Api.TryCompleteOAuthRedirect(url) || await _svc.Api.TryRestoreSession()) await EnterMenu(); else ShowLogin(null);
        }

        void Update()
        {
            if (_toast != null && Time.unscaledTime > _toastUntil) _toast.text = "";
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && _svc.Api.IsSignedIn)
            {
                if (_drive.Active) { _drive.Pause(true); _hud.SetVisible(false); _canvas.gameObject.SetActive(true); Show(Screen.Menu); }
                else if (_drive.Vehicle != null) Resume();
            }
        }

        void Toast(string m, float s = 4f) { _toast.text = m; _toastUntil = Time.unscaledTime + s; if (_dash != null && _dash.gameObject.activeInHierarchy) _dash.Toast(m, s); }

        // ---------------------------------------------------------------- panels
        RectTransform NewPanel(string title, float h = 760f)
        {
            if (_panel != null) Destroy(_panel.gameObject);
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
                case Screen.Jobs: _ = ShowJobs(); break;
                case Screen.Garage: ShowGarage(); break;
                case Screen.Profile: ShowProfile(); break;
                case Screen.Convoy: _ = ShowConvoy(); break;
                case Screen.Bus: _ = ShowBus(); break;
                case Screen.Shop: ShowShop(); break;
            }
        }

        // ---------------------------------------------------------------- login
        LoginScreen _login;

        void ShowLogin(string info)
        {
            if (_panel != null) Destroy(_panel.gameObject);
            if (_login != null) Destroy(_login.gameObject);
            if (_dash != null) Destroy(_dash.gameObject);
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
            if (err != null) { _svc.Api.SignOut(); ShowLogin(err); return; }
            ShowMenu();
        }

        // ---------------------------------------------------------------- menu = the dashboard
        DashboardScreen _dash; JobDto[] _fixtureJobs;

        void ShowMenu()
        {
            if (_panel != null) { Destroy(_panel.gameObject); _panel = null; }
            _canvas.gameObject.SetActive(false);
            if (_dash != null) Destroy(_dash.gameObject);
            _dash = DashboardScreen.Create(_svc, _drive, _bus, DashActions(), _fixtureJobs);
        }

        void OpenPanel(System.Action show) { if (_dash != null) _dash.gameObject.SetActive(false); _canvas.gameObject.SetActive(true); show(); }
        void HideMenus() { _canvas.gameObject.SetActive(false); if (_dash != null) _dash.gameObject.SetActive(false); }

        DashboardActions DashActions() => new DashboardActions
        {
            Jobs = () => OpenPanel(() => _ = ShowJobs()), Garage = () => OpenPanel(ShowGarage), Shop = () => OpenPanel(ShowShop),
            Convoy = () => OpenPanel(() => _ = ShowConvoy()), Bus = () => OpenPanel(() => _ = ShowBus()), Profile = () => OpenPanel(ShowProfile),
            FreeDrive = () => { if (_drive.Vehicle != null) Resume(); else { HideMenus(); StartDrive(null); } },
            Resume = Resume,
            SignOut = () => { _svc.Api.SignOut(); if (_dash != null) Destroy(_dash.gameObject); ShowLogin(null); },
            AcceptJob = job => _ = AcceptJob(job),
            QuickJob = () => _ = QuickJob(),
        };

        /// <summary>One tap: take the best-paying open job the player has a vehicle for.</summary>
        async Task QuickJob()
        {
            if (_busy) return; if (_drive.Job != null) { Toast("Finish or abandon your current job first."); return; }
            Toast("Finding a job...");
            var r = await _svc.Jobs.LoadBoard();
            if (!r.Ok) { Toast(r.UserMessage, 5f); return; }
            JobDto best = null;
            foreach (var j in r.Value) if (FindVehicleFor(j.required_category) != null && (best == null || j.reward > best.reward)) best = j;
            if (best == null) { Toast("No open job matches a vehicle you own."); return; }
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
        async Task ShowJobs()
        {
            var p = NewPanel("Job Board", 900f);
            UIKit.Label(p, "Loading jobs...", 22, UIKit.Muted, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var r = await _svc.Jobs.LoadBoard();
            p = NewPanel("Job Board", 900f);
            if (!r.Ok) { UIKit.Label(p, r.UserMessage, 22, UIKit.Bad, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero); UIKit.Btn(p, "BACK", ShowMenu, false); return; }
            if (_drive.Job != null) UIKit.Label(p, $"Active: {_drive.Job.code}. Finish or abandon it first.", 22, UIKit.Accent, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
            if (r.Value.Length == 0) UIKit.Label(p, "No open jobs right now. Jobs are generated by the server; check back soon.", 22, UIKit.Muted, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            int shown = 0;
            foreach (var j in r.Value)
            {
                if (shown++ >= 6) break;
                var job = j;
                UIKit.Btn(p, $"{job.code}  {job.origin.name} → {job.destination.name}\n{job.cargo_type} · {job.distance_km:F1} km · {Stars(job.difficulty)} · {job.reward:N0} coins",
                    async () => await AcceptJob(job), false, 84);
            }
            UIKit.Btn(p, "BACK", ShowMenu, true, 48);
        }
        static string Stars(int d) => new string('★', d) + new string('☆', 5 - d);

        async Task AcceptJob(JobDto job)
        {
            if (_busy) return; if (_drive.Job != null) { Toast("Finish or abandon your current job first."); return; }
            var owned = FindVehicleFor(job.required_category);
            if (owned == null) { Toast("You have no " + job.required_category + " for this job."); return; }
            _busy = true; var r = await _svc.Jobs.Accept(job.id, owned.id); _busy = false;
            if (!r.Ok) { Toast(r.UserMessage, 6f); await ShowJobs(); return; }
            StartDrive(job, r.Value, owned);
        }

        OwnedVehicleDto FindVehicleFor(string category)
        {
            var cat = new Dictionary<string, string>(); foreach (var d in _svc.Definitions) cat[d.id] = d.category;
            foreach (var v in _svc.Vehicles) if (cat.TryGetValue(v.definition_id, out var c) && c == category) return v;
            return null;
        }

        // ---------------------------------------------------------------- garage
        void ShowGarage()
        {
            var p = NewPanel("Garage", 800f);
            var names = new Dictionary<string, VehicleDefDto>(); foreach (var d in _svc.Definitions) names[d.id] = d;
            foreach (var v in _svc.Vehicles)
            {
                var veh = v; names.TryGetValue(v.definition_id, out var def);
                UIKit.Label(p, $"{(def != null ? def.name : v.definition_id)}\nFuel {v.fuel_l:F0}/{(def != null ? def.fuel_capacity_l : 0):F0} L · Damage {v.damage_pct:F0}% · {v.odometer_km:F1} km",
                    22, UIKit.TextCol, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<LayoutElement>().preferredHeight = 70;
                UIKit.Btn(p, "REFUEL (full tank)", async () => await Service(() => _svc.Api.Rpc("buy_fuel", $"{{\"p_vehicle\":\"{veh.id}\",\"p_liters\":9999}}"), "Refuelled."), false, 48);
                UIKit.Btn(p, "REPAIR", async () => await Service(() => _svc.Api.Rpc("repair_vehicle", $"{{\"p_vehicle\":\"{veh.id}\"}}"), "Repaired."), false, 48);
            }
            UIKit.Btn(p, "BACK", ShowMenu, true, 48);
        }

        async Task Service(System.Func<Task<Result<string>>> call, string ok)
        {
            if (_busy) return; _busy = true; var r = await call(); _busy = false;
            Toast(r.Ok ? ok : r.UserMessage, 5f);
            await _svc.RefreshPlayer(); ShowGarage();
        }

        // ---------------------------------------------------------------- convoy
        async Task ShowConvoy()
        {
            var p = NewPanel("Convoy", 900f);
            if (_svc.Session.State == ARO.NetCore.SessionState.InSession)
            {
                UIKit.Label(p, $"{_svc.Convoys.CurrentConvoyName}\nSession code: {_svc.Session.Code}   {(_svc.Session.IsHost ? "(you are hosting)" : "")}", 24, UIKit.TextCol, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<LayoutElement>().preferredHeight = 80;
                var sb = new System.Text.StringBuilder(); var leader = NetworkVehicle.Players.Leader;
                foreach (var m in NetworkVehicle.Players.Members) sb.AppendLine((m == leader ? "★ " : "● ") + m.Name);
                UIKit.Label(p, sb.Length > 0 ? sb.ToString() : "Waiting for drivers...", 24, UIKit.Muted, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<LayoutElement>().preferredHeight = 240;
                UIKit.Btn(p, "REFRESH", () => _ = ShowConvoy(), false, 48);
                UIKit.Btn(p, "LEAVE CONVOY", async () => { await _svc.Convoys.Leave(); Toast("You left the convoy."); await ShowConvoy(); }, false, 48);
                UIKit.Btn(p, "BACK", ShowMenu, true, 48);
                return;
            }
            UIKit.Label(p, "Loading convoys...", 22, UIKit.Muted, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var list = await _svc.Convoys.ListOpen();
            p = NewPanel("Convoy", 900f);
            if (!list.Ok) UIKit.Label(p, list.UserMessage, 22, UIKit.Bad, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            else if (list.Value.Length == 0) UIKit.Label(p, "No open convoys. Create one below.", 22, UIKit.Muted, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            int shown = 0;
            if (list.Ok) foreach (var c in list.Value)
            {
                if (shown++ >= 4) break; var convoy = c;
                UIKit.Btn(p, $"{convoy.name}  ·  led by {(convoy.leader != null ? convoy.leader.display_name : "?")}  ·  {convoy.Members}/{SessionService.MaxPlayers}",
                    async () => { if (_busy) return; _busy = true; Toast("Joining convoy..."); var r = await _svc.Convoys.Join(convoy); _busy = false; Toast(r.Ok ? "Joined convoy." : r.UserMessage, 5f); await ShowConvoy(); }, false, 60);
            }
            var nameField = UIKit.Input(p, "New convoy name");
            UIKit.Btn(p, "CREATE CONVOY", async () =>
            {
                if (_busy) return; _busy = true; Toast("Creating convoy session...");
                var r = await _svc.Convoys.Create(nameField.text); _busy = false;
                Toast(r.Ok ? "Convoy created. Share it from the list." : r.UserMessage, 6f); await ShowConvoy();
            });
            UIKit.Btn(p, "REFRESH", () => _ = ShowConvoy(), false, 48);
            UIKit.Btn(p, "BACK", ShowMenu, false, 48);
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
            UIKit.Btn(p, "SHOP", ShowShop, false, 48);
            UIKit.Btn(p, "BACK", ShowMenu, true, 48);
        }

        async Task BeginBusRoute(BusRouteDto route, OwnedVehicleDto bus)
        {
            if (_busy) return; _busy = true; Toast("Starting route...");
            string err = await _bus.Begin(route, bus); _busy = false;
            if (err != null) { Toast(err, 6f); await ShowBus(); return; }
            HideMenus(); _hud.SetVisible(true); _drive.Pause(false);
        }

        // ---------------------------------------------------------------- shop
        void ShowShop()
        {
            var p = NewPanel("Vehicle Shop", 900f);
            UIKit.Label(p, $"Wallet: {_svc.Wallet.balance:N0} coins", 24, UIKit.Accent, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
            int shown = 0;
            foreach (var d in _svc.Definitions)
            {
                if (d.price <= 0 || shown++ >= 6) continue;
                var def = d; int owned = 0; foreach (var v in _svc.Vehicles) if (v.definition_id == def.id) owned++;
                string detail = def.category == "bus" ? $"{def.passenger_capacity} seats" : $"{def.cargo_capacity_kg:N0} kg cargo";
                UIKit.Btn(p, $"{def.name}  ·  {def.price:N0} coins\n{detail} · {def.max_speed_kmh:F0} km/h" + (owned > 0 ? $"  ·  you own {owned}" : ""),
                    async () => await BuyVehicle(def), false, 84);
            }
            UIKit.Btn(p, "BACK", ShowMenu, true, 48);
        }

        async Task BuyVehicle(VehicleDefDto def)
        {
            if (_busy) return;
            if (_svc.Wallet.balance < def.price) { Toast($"Not enough money: {def.name} costs {def.price:N0} coins."); return; }
            _busy = true; var r = await _svc.Bus.BuyVehicle(def.id); _busy = false;
            Toast(r.Ok ? $"Bought {def.name}." : r.UserMessage, 5f);
            await _svc.RefreshPlayer(); ShowShop();
        }

        // ---------------------------------------------------------------- profile
        void ShowProfile()
        {
            var p = NewPanel("Profile");
            var pr = _svc.Profile;
            UIKit.Label(p, $"{pr.display_name}\nLevel {pr.level} · {pr.experience:N0} XP\nWallet: {_svc.Wallet.balance:N0} coins\nJobs completed: {pr.jobs_completed}\nDistance: {pr.distance_km:F1} km\nVehicles owned: {_svc.Vehicles.Length}",
                26, UIKit.TextCol, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject.AddComponent<LayoutElement>().preferredHeight = 260;
            if (_drive.Job != null) UIKit.Btn(p, "ABANDON CURRENT JOB", () => { _drive.AbandonJob(); ShowProfile(); }, false);
            if (_bus.Active) UIKit.Btn(p, "ABANDON BUS ROUTE", async () => { await _bus.Abandon(); ShowProfile(); }, false);
            UIKit.Btn(p, "BACK", ShowMenu);
        }
    }
}
