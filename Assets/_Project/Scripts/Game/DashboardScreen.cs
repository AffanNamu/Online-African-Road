using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ARO.Backend;
using ARO.Multiplayer;
using ARO.NetCore;
using UnityEngine;
using UnityEngine.UI;

namespace ARO.Game
{
    /// <summary>Callbacks the dashboard raises; GameFlow owns what they do.</summary>
    public sealed class DashboardActions
    {
        public Action Jobs, Garage, Shop, Convoy, Bus, Profile, FreeDrive, Resume, SignOut, WorldMap;
        /// <summary>Starts a bus run with the given bus (GameFlow loads the vehicle and hides the menus). Returns an error message, or null when the route has started.</summary>
        public Func<ARO.Backend.BusRouteDto, ARO.Backend.OwnedVehicleDto, Task<string>> StartBus;
        public Action<ARO.Backend.ConvoyInfo> ConvoyDrive;
        public Action<JobDto> AcceptJob;
        public Action QuickJob;
    }

    /// <summary>
    /// The home dashboard: sidebar, header (progression, fuel, presence, notifications), hero carousel, quick actions, your truck, milestone, featured jobs,
    /// popular routes, recent players, news and convoy. EVERY value comes from the backend services (profile summary, job board, world state, content,
    /// recent players, notifications, presence). Each block shows Loading, Empty or Unavailable according to what the server actually said; nothing is
    /// invented when a call fails. Laid out on a 1920x1080 stage that letterboxes to any window shape.
    /// </summary>
    public sealed partial class DashboardScreen : MonoBehaviour
    {
        static readonly Color Gold = Brand.Gold, Ink = Brand.Ink, Muted = Brand.Muted;
        static readonly Color PanelFill = new Color(0.062f, 0.070f, 0.092f, 0.94f), PanelLine = new Color(1f, 1f, 1f, 0.10f);
        static readonly Color White = Color.white, Soft = new Color(0.86f, 0.88f, 0.92f), Blue = new Color(0.35f, 0.65f, 1f);
        const float HeroX = 331, HeroY = 142, HeroW = 1146, HeroH = 318, HeroAspect = HeroW / HeroH;

        GameServices _svc; DrivingSession _drive; BusSession _bus; DashboardActions _act;
        RectTransform _root, _stage, _jobsHost, _popover, _sidebar, _regionParent, _homeRoot, _jobsRoot, _mpRoot, _vehRoot, _garRoot, _setRoot, _friRoot, _lbRoot;
        enum Page { Home, Jobs, Multiplayer, Vehicles, Garage = 4, Friends = 6, Leaderboards = 7, Settings = 9, Onboarding = 30, Profile = 31, BusRoutes = 32 }       // the value is the sidebar row: Home, Jobs, Multiplayer, Trucks & Buses, Garage ... Settings is the tenth
        Page _page = Page.Home; int _badgeCount;
        Text _toast;
        float _toastUntil, _nextSlide, _heroDip = -10f, _nextNews, _nextBell, _nextPill, _nextPoll, _nextContent; int _slide, _lastUnread = -1;
        readonly Dictionary<string, RectTransform> _regions = new Dictionary<string, RectTransform>(); readonly Dictionary<string, string> _keys = new Dictionary<string, string>();
        DashboardSummary Sum => _svc.Player.Summary.Value;

        // job board state
        enum JobState { Loading, Ready, Empty, Error }
        JobState _jobState = JobState.Loading; string _jobError; JobDto[] _jobs; bool _jobsLoaded; int _loadToken;

        // hero
        struct Slide { public string eyebrow, t1, t2, desc, button; public Action go; public Rect uv; public bool textRight; }
        Slide[] _slides; RawImage _heroPhoto; KenBurnsView _kb; Image[] _dots; Image _shadeL, _shadeR;
        Text _heroEyebrow, _heroT1, _heroT2, _heroDesc, _heroBtnText; RectTransform _heroText; GroupFade _heroFade;

        // live text that depends on loaded data
        readonly Text[] _qDesc = new Text[4]; readonly string[] _qDefault = new string[4];
        Image _bellBadge, _presenceDot; Text _bellCount, _presenceLabel; GameObject _jobsBadge, _heroBtnBox;

        // news (served by the backend content service)
        ContentItem[] _newsItems = new ContentItem[0]; int _newsIndex; RectTransform _newsHost; Text _newsCounter;

        // data resolved once per build
        OwnedVehicleDto _vehicle; VehicleDefDto _def;

        public static DashboardScreen Create(GameServices svc, DrivingSession drive, BusSession bus, DashboardActions act)
        {
            var go = new GameObject("Dashboard");
            var d = go.AddComponent<DashboardScreen>(); d.Build(svc, drive, bus, act);
            return d;
        }

        public void Refresh() { if (_root != null) { Destroy(transform.GetChild(0).gameObject); _jobsLoaded = false; BuildCanvas(); } }

        void Build(GameServices svc, DrivingSession drive, BusSession bus, DashboardActions act) { _svc = svc; _drive = drive; _bus = bus; _act = act; BuildCanvas(); }


        // ================================================================================================================ build
        void BuildCanvas()
        {
            _regions.Clear(); _keys.Clear();
            ResolveVehicle();
            var model = WorldMapScreen.Model();
            string place = "Lagos, Nigeria";
            var spec = _drive != null && _drive.Route != null ? _drive.Route.Spec : null;
            if (spec != null && spec.meta != null && model != null) place = spec.meta.from + ", " + model.CountryName(spec.meta.country);
            _heroPlace = place;
            BuildSlides(); RebuildNewsList();

            var canvas = UIKit.CreateCanvas("DashboardCanvas", 20);
            canvas.transform.SetParent(transform, false);
            var root = (RectTransform)canvas.transform;
            var bg = Ui.Flat(root, "Bg", new Color(0.028f, 0.033f, 0.048f, 1f), 0, 0, 10, 10); var brt = bg.rectTransform; brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
            var stageGo = new GameObject("Stage", typeof(RectTransform), typeof(StageFitter)); stageGo.transform.SetParent(root, false);
            _root = (RectTransform)stageGo.transform; _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0.5f, 0.5f); _root.sizeDelta = new Vector2(1920, 1080); _root.anchoredPosition = Vector2.zero;
            _stage = _root;

            Region("Header", 0f); BuildHeader();
            _sidebar = Region("Sidebar", 0.04f); BuildSidebar();
            _homeRoot = Ui.Box(_root, "HomePage", 0, 0, 1920, 1080); _regionParent = _homeRoot;
            Region("Hero", 0.10f); BuildHero();
            Region("Milestone", 0.16f); BuildMilestone();
            Region("Truck", 0.22f); BuildYourTruck();
            Region("Quick", 0.28f); BuildQuick();
            Region("Jobs", 0.34f); BuildFeatured();
            Region("Routes", 0.40f); BuildRoutes();
            Region("Players", 0.46f); BuildPlayers();
            Region("News", 0.50f); BuildNews();
            Region("Convoy", 0.54f); BuildConvoyCard();
            _jobsRoot = Ui.Box(_root, "JobsPage", 0, 0, 1920, 1080); _regionParent = _jobsRoot; BuildJobsPage();
            _mpRoot = Ui.Box(_root, "MultiplayerPage", 0, 0, 1920, 1080); _regionParent = _mpRoot; BuildMultiplayerPage();
            _vehRoot = Ui.Box(_root, "VehiclesPage", 0, 0, 1920, 1080); _regionParent = _vehRoot; BuildVehiclesPage();
            _garRoot = Ui.Box(_root, "GaragePage", 0, 0, 1920, 1080); _regionParent = _garRoot; BuildGaragePage();
            _setRoot = Ui.Box(_root, "SettingsPage", 0, 0, 1920, 1080); _regionParent = _setRoot; BuildSettingsPage();
            _friRoot = Ui.Box(_root, "FriendsPage", 0, 0, 1920, 1080); _regionParent = _friRoot; BuildFriendsPage();
            _lbRoot = Ui.Box(_root, "LeaderboardsPage", 0, 0, 1920, 1080); _regionParent = _lbRoot; BuildLeaderboardsPage();
            _obRoot = Ui.Box(_root, "OnboardingPage", 0, 0, 1920, 1080); _regionParent = _obRoot; BuildOnboardingPage();
            _prRoot = Ui.Box(_root, "ProfilePage", 0, 0, 1920, 1080); _regionParent = _prRoot; BuildProfilePage();
            _brRoot = Ui.Box(_root, "BusRoutesPage", 0, 0, 1920, 1080); _regionParent = _brRoot; BuildBusRoutesPage();
            _regionParent = null;
            if (_svc.Me != null && !_svc.Me.onboarded && !_obDone) _page = Page.Onboarding;                // a new player starts here, above everything
            ApplyPage(); if (_page == Page.Onboarding) ObOpened();

            _stage = _root;                                         // toast and the notice popover sit above everything
            _toast = Ui.Label(_root, "", "SemiBold", 24, Gold, 0, 1010, 1920, 40, TextAnchor.MiddleCenter);
            _toast.rectTransform.sizeDelta = new Vector2(1920, 40);
            BuildPopover();
            ShowSlide(_slide, false); _nextSlide = Time.unscaledTime + 8f; _nextNews = Time.unscaledTime + 10f;
            _nextPoll = Time.unscaledTime + 45f; _nextContent = Time.unscaledTime + 120f;
            if (!_jobsLoaded) { _jobsLoaded = true; _ = LoadJobs(); } else FillJobs();
            _ = LoadHeroContent(); _ = LoadNewsContent(); _ = LoadRecentPlayers();
            _builtAt = Time.unscaledTime;
            Debug.Log("[Dashboard] built for " + (_svc.Profile != null ? _svc.Profile.display_name : "?"));
        }

        string _heroPlace = "Lagos, Nigeria";

        /// <summary>A full-stage container for one region; it fades and rises in (staggered) when the dashboard opens. Everything built after this call lands inside it.</summary>
        RectTransform Region(string name, float delay)
        {
            var r = Ui.Box(_regionParent != null ? _regionParent : _root, name, 0, 0, 1920, 1080); _stage = r; _regions[name] = r; Entrance.On(r, delay); return r;
        }

        /// <summary>Rebuilds one region in place (its data changed). The region container itself stays, so nothing re-animates except what is rebuilt.</summary>
        void Refill(string name, Action build)
        {
            if (!_regions.TryGetValue(name, out var r) || r == null) return;
            for (int i = r.childCount - 1; i >= 0; i--) Destroy(r.GetChild(i).gameObject);
            var prev = _stage; _stage = r; build(); _stage = prev; MarkTargetsDirty();
            if (SmokeMode.E2E) Debug.Log($"[UIDiag] rebuild {name} t={Time.realtimeSinceStartup:0.00}");   // E2E diagnostics: a click that lands while a region is being rebuilt can find nothing under it
        }

        void RefillIfChanged(string name, string key, Action build)
        {
            if (_keys.TryGetValue(name, out var old) && old == key) return;
            bool first = !_keys.ContainsKey(name); _keys[name] = key;
            if (!first) Refill(name, build);
        }

        // ---- background reloads must not redraw what has not changed (RefreshGate explains why: a redraw between a press and its release loses the click)
        readonly RefreshGate _gate = new RefreshGate();
        [Serializable] sealed class SigBox<T> { public T v; }

        /// <summary>
        /// A comparable description of a data object: its JSON without the fields named in <paramref name="ignore"/> (server clocks and the like change on every reply and are not drawn).
        /// Null when it cannot be built, which makes the caller redraw: never a stale screen.
        /// </summary>
        static string JsonKey<T>(T value, params string[] ignore) where T : class
        {
            if (value == null) return "null";
            try
            {
                string s = JsonUtility.ToJson(new SigBox<T> { v = value }); if (s == null || s.Length < 12) return null;
                foreach (var f in ignore) s = System.Text.RegularExpressions.Regex.Replace(s, "\"" + f + "\":\"[^\"]*\",?", "");
                return s;
            }
            catch (Exception) { return null; }
        }

        /// <summary>What a loadable list contributes to a drawing: its data when it has any (a reload in flight keeps showing it), otherwise its state and error.</summary>
        static string Sig<T>(Loadable<T> l, params string[] ignore) where T : class
            => HasData(l) ? JsonKey(l.Value, ignore.Length == 0 ? new[] { "server_time" } : ignore) : l.State + ":" + l.Error;

        /// <summary>Smoke/E2E only: the browser tests need the dashboard's own click positions, re-reported about a second after anything is rebuilt.</summary>
        void MarkTargetsDirty() { if (SmokeMode.LogTargets) { _targetsLogged = false; _builtAt = Time.unscaledTime - 1.5f; } }

        void ResolveVehicle()
        {
            _vehicle = null; _def = null; var list = _svc.Vehicles ?? new OwnedVehicleDto[0];
            if (_drive != null && !string.IsNullOrEmpty(_drive.VehicleId)) _vehicle = list.FirstOrDefault(v => v.id == _drive.VehicleId);
            if (_vehicle == null) _vehicle = list.FirstOrDefault(v => v.selected);
            if (_vehicle == null) _vehicle = list.FirstOrDefault();
            if (_vehicle != null && _svc.Definitions != null) _def = _svc.Definitions.FirstOrDefault(d => d.id == _vehicle.definition_id);
        }

        float FuelPercent() => _vehicle == null ? 0f : Vitals.FuelPercent(_vehicle.fuel_l, _def != null ? _def.fuel_capacity_l : 0);

        // ---------------------------------------------------------------- refresh from the server while the dashboard is open
        /// <summary>Re-reads the player's snapshot (profile, coins, vehicles, counts) and rebuilds only the regions whose numbers changed.</summary>
        async Task RefreshSnapshot()
        {
            string err = await _svc.RefreshPlayer();
            if (this == null || err != null) return;
            ResolveVehicle();
            var pr = Sum != null ? Sum.progression : null; var ms = pr != null ? pr.next_milestone : null;
            RefillIfChanged("Header", $"{_svc.Profile.display_name}|{_svc.Profile.level}|{_svc.Profile.experience}|{(_svc.Wallet != null ? _svc.Wallet.balance : 0)}|{_vehicle?.id}|{(int)FuelPercent()}", BuildHeader);
            RefillIfChanged("Milestone", $"{_svc.Profile.distance_km:0.0}|{_svc.Profile.jobs_completed}|{(ms != null ? ms.title : "")}", BuildMilestone);
            RefillIfChanged("Truck", $"{_vehicle?.id}|{(int)(_vehicle != null ? _vehicle.fuel_l : 0)}|{(int)(_vehicle != null ? _vehicle.damage_pct : 0)}|{_vehicle?.status}", BuildYourTruck);
            var j = Sum != null ? Sum.jobs : null; var c = Sum != null ? Sum.convoy : null; var w = Sum != null ? Sum.world : null;
            RefillIfChanged("Quick", $"{(j != null ? j.available : -1)}|{(j != null ? j.best_reward : 0)}|{(c != null ? c.id : "")}|{(c != null ? c.member_count : 0)}|{(w != null ? w.routes_open : -1)}|{(Sum != null ? Sum.vehicle_count : -1)}", BuildQuick);
            RefillIfChanged("Convoy", $"{(c != null ? c.id : "")}|{(c != null ? c.member_count : 0)}|{NetworkVehicle.Players.Count}", BuildConvoyCard);
        }

        async Task LoadHeroContent()
        {
            if (!_svc.Content.Hero.NeedsLoad(Time.unscaledTime, 120)) return;
            await _svc.Content.LoadHero(); if (this == null) return;
            BuildSlides(); _slide = 0; Refill("Hero", BuildHero); ShowSlide(0, false);
            Debug.Log($"[Dashboard] hero state={_svc.Content.Hero.State.ToString().ToLowerInvariant()} slides={(_slides != null ? _slides.Length : 0)}");
        }

        async Task LoadNewsContent()
        {
            if (!_svc.Content.News.NeedsLoad(Time.unscaledTime, 120)) { RebuildNewsList(); return; }
            var pending = _svc.Content.LoadNews();                      // state flips to Loading synchronously
            RebuildNewsList(); Refill("News", BuildNews);
            await pending; if (this == null) return;
            RebuildNewsList(); _newsIndex = 0; Refill("News", BuildNews);
            Debug.Log($"[Dashboard] news state={_svc.Content.News.State.ToString().ToLowerInvariant()} count={_newsItems.Length}" + (_svc.Content.News.Error != null ? " error=" + _svc.Content.News.Error : ""));
        }

        void RebuildNewsList()
        {
            var n = _svc.Content.News;
            _newsItems = n.State == LoadState.Ready && n.Value != null && n.Value.items != null ? n.Value.items : new ContentItem[0];
        }

        async Task LoadRecentPlayers()
        {
            if (!_svc.Friends.Recent.NeedsLoad(Time.unscaledTime, 120)) return;
            var pending = _svc.Friends.LoadRecent(8);                     // state flips to Loading synchronously
            Refill("Players", BuildPlayers);
            await pending; if (this == null) return;
            Refill("Players", BuildPlayers);
            var rec = _svc.Friends.Recent;
            Debug.Log($"[Dashboard] players state={rec.State.ToString().ToLowerInvariant()} count={(rec.Value != null && rec.Value.items != null ? rec.Value.items.Length : 0)}" + (rec.Error != null ? " error=" + rec.Error : ""));
        }

        // ================================================================================================================ sidebar
        void BuildSidebar()
        {
            Ui.Flat(_stage, "Sidebar", new Color(0.040f, 0.046f, 0.062f, 1f), 0, 0, 300, 1080);
            Ui.Flat(_stage, "SidebarEdge", new Color(1, 1, 1, 0.07f), 299, 0, 1, 1080);
            Ui.Icon(_stage, "logo_mark", White, 26, 24, 84);
            var a = Ui.Label(_stage, "AFRICAN", "ExtraBold", 30, White, 112, 26, 190, 36); Ui.Drop(a, 0.4f);
            Ui.Label(_stage, "ROADS", "ExtraBold", 44, Gold, 112, 54, 190, 52);
            Ui.Label(_stage, "O N L I N E", "Bold", 13, White, 114, 104, 190, 18);

            var items = new (string icon, string label, Action go, string soon)[]
            {
                ("ic_home", "Home", () => ShowPage(Page.Home), null), ("ic_jobs", "Jobs", OpenJobs, null), ("ic_people", "Multiplayer", OpenMultiplayer, null),
                ("ic_bus", "Trucks & Buses", OpenVehicles, null), ("ic_wrench", "Garage", OpenGarage, null), ("ic_map", "Map", _act.WorldMap, null),
                ("ic_people", "Friends", OpenFriends, null), ("ic_trophy", "Leaderboards", OpenLeaderboards, null),
                ("ic_cart", "Store", null, "The store is coming soon."), ("ic_gear", "Settings", OpenSettings, null),
            };
            for (int i = 0; i < items.Length; i++)
            {
                float y = 138 + i * 66f; bool active = i == (int)_page || (i == 3 && _page == Page.BusRoutes);   // Home = 0, Jobs = 1
                if (active) Ui.Rounded(_stage, "NavActive", Gold, 18, y, 264, 60, 14);
                Ui.Icon(_stage, items[i].icon, active ? Ink : new Color(0.82f, 0.84f, 0.88f), 44, y + 13, 34);
                Ui.Label(_stage, items[i].label, active ? "Bold" : "SemiBold", 24, active ? Ink : new Color(0.88f, 0.9f, 0.93f), 104, y + 14, 180, 32);
                var it = items[i];
                Ui.Click(_stage, "Nav_" + it.label.Replace(" ", "").Replace("&", "And"), 18, y, 264, 60, () => { if (it.go != null) it.go(); else if (it.soon != null) Toast(it.soon); });
            }
            // "Drive Africa. Build Together." - a real picture from the key art, not a flat block
            var tex = Brand.Scene();
            const float PX = 18, PY = 818, PW = 264, PH = 168;
            Ui.Outlined(_stage, "PromoFrame", new Color(0, 0, 0, 1), new Color(1, 1, 1, 0.10f), PX, PY, PW, PH, 16);
            Ui.Photo(_stage, "PromoPhoto", tex, Ui.Cover(tex, 0.50f, 0.30f, 0.40f, PW / PH), PX + 1.5f, PY + 1.5f, PW - 3, PH - 3, 15, new Color(0.85f, 0.85f, 0.88f));
            Ui.Gradient(_stage, "PromoShade", new Color(0.02f, 0.025f, 0.04f, 0.0f), new Color(0.02f, 0.025f, 0.04f, 0.92f), false, PX + 2, PY + 40, PW - 4, PH - 42);
            Ui.Label(_stage, "Drive Africa.", "ExtraBold", 24, Gold, PX + 16, PY + 98, 240, 30);
            Ui.Label(_stage, "Build Together.", "ExtraBold", 24, White, PX + 16, PY + 124, 240, 30);

            var so = Ui.Label(_stage, "Sign out", "SemiBold", 20, new Color(0.62f, 0.65f, 0.7f), 44, 1018, 200, 28);
            Ui.Click(_stage, "Nav_SignOut", 30, 1008, 240, 48, () => _act.SignOut());
            SetJobsBadge(_badgeCount);
        }

        /// <summary>The red count on the Jobs item: how many open jobs the server returned.</summary>
        void SetJobsBadge(int n)
        {
            _badgeCount = n;
            if (_jobsBadge != null) Destroy(_jobsBadge);
            if (n <= 0 || _sidebar == null) return;
            var holder = new GameObject("JobsBadge", typeof(RectTransform)); holder.transform.SetParent(_sidebar, false); _jobsBadge = holder;
            var rt = (RectTransform)holder.transform; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1); rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(1920, 1080);
            string s = n > 99 ? "99+" : n.ToString(); float w = s.Length > 2 ? 40 : 30;
            Ui.Rounded(holder.transform, "Badge", new Color(0.92f, 0.22f, 0.22f), 256 - (w - 30), 218, w, 30, 15);
            Ui.Label(holder.transform, s, "Bold", 17, White, 256 - (w - 30), 222, w, 24, TextAnchor.UpperCenter);
        }

        // ================================================================================================================ header
        void Pill(float x, float y, float w, float h, string icon, Action click, string name = "PillBtn")
        {
            Ui.Outlined(_stage, "Pill", new Color(0.05f, 0.055f, 0.07f, 0.88f), new Color(1, 1, 1, 0.14f), x, y, w, h, 16);
            if (icon != null) Ui.Icon(_stage, icon, White, x + (w - 30) / 2f, y + (h - 30) / 2f, 30);
            Ui.Click(_stage, name, x, y, w, h, click);
        }

        void BuildHeader()
        {
            ResolveVehicle();
            Ui.Photo(_stage, "HeaderSky", Brand.Texture("login_bg"), Ui.Top(0f, 0.40f, 1f, 0.1356f), 300, 0, 1620, 124, 0);
            Ui.Gradient(_stage, "HeaderShade", new Color(0.03f, 0.035f, 0.05f, 0.92f), new Color(0.03f, 0.035f, 0.05f, 0.25f), true, 300, 0, 1620, 124);
            Ui.Gradient(_stage, "HeaderFloor", new Color(0.028f, 0.033f, 0.048f, 0f), new Color(0.028f, 0.033f, 0.048f, 1f), false, 300, 70, 1620, 54);

            // everything below is the server's: level, XP window and progress come from the progression block, not from client maths
            var p = _svc.Profile; var pr = Sum != null ? Sum.progression : null; string name = p != null ? p.display_name : "Driver";
            int lvl = pr != null && pr.level > 0 ? pr.level : (p != null ? p.level : 1);
            long into = pr != null ? pr.xp_into_level : 0; long span = pr != null ? Math.Max(1, pr.xp_next_level - pr.xp_level_start) : 100; float frac = pr != null ? Mathf.Clamp01((float)pr.level_progress) : 0f;
            Ui.Disc(_stage, "AvatarRing", Gold, 330, 14, 100);
            Ui.Disc(_stage, "AvatarBg", new Color(0.12f, 0.14f, 0.18f), 334, 18, 92);
            Ui.Icon(_stage, "avatar_default", White, 334, 18, 92);
            Ui.Label(_stage, "Good to see you,", "Regular", 22, new Color(0.9f, 0.92f, 0.95f), 452, 20, 400, 30);
            var nm = Ui.Label(_stage, name, "ExtraBold", 42, White, 452, 44, 700, 54);
            Ui.Icon(_stage, "ic_crown", Gold, 452 + nm.preferredWidth + 14, 56, 32);
            Ui.Label(_stage, "Level " + lvl, "SemiBold", 22, White, 452, 96, 120, 28);
            var fill = Ui.Bar(_stage, 556, 104, 190, 11, frac, Gold); BarGrow.Run(fill, 11, Mathf.Max(11f, 190f * frac), 0.35f, 1.0f);
            var xpText = Ui.Label(_stage, "", "Regular", 18, new Color(0.88f, 0.9f, 0.94f), 762, 98, 220, 26);
            CountText.Run(xpText, 0, into, "", $" / {span:N0} XP", 0.35f, 1.0f);
            Ui.Click(_stage, "Btn_Profile", 326, 10, 520, 108, OpenProfile);                          // the avatar, name and level open your profile

            // right cluster: fuel of the truck you drive, coins, presence, notifications, settings
            BuildFuelChip();
            long balance = _svc.Wallet != null ? _svc.Wallet.balance : 0;
            Pill(1352, 24, 200, 56, null, () => Toast("Your balance is paid out by the server when a job or route completes."), "Btn_Balance");
            Ui.Icon(_stage, "ic_coin", Gold, 1370, 33, 38);
            var bal = Ui.Label(_stage, "", "Bold", 26, White, 1420, 34, 120, 36); CountText.Run(bal, 0, balance, "\u20A6 ", "", 0.3f, 1.1f);

            Pill(1572, 24, 150, 56, null, () => Toast(PresenceToast()), "Btn_Presence");
            _presenceDot = Ui.Disc(_stage, "OnlineDot", Muted, 1594, 45, 14);
            _presenceLabel = Ui.Label(_stage, "", "SemiBold", 22, White, 1620, 34, 100, 34);
            _nextPill = 0f; UpdatePresencePill();

            Pill(1744, 24, 60, 56, "ic_bell", ToggleNotices, "Btn_Bell");
            _bellBadge = Ui.Disc(_stage, "BellBadge", new Color(0.92f, 0.22f, 0.22f), 1782, 14, 26);
            _bellCount = Ui.Label(_stage, "", "Bold", 15, White, 1782, 17, 26, 20, TextAnchor.UpperCenter);
            RefreshBell(true);
            Pill(1824, 24, 60, 56, "ic_gear", OpenSettings, "Btn_Settings");
        }

        void BuildFuelChip()
        {
            Pill(1166, 24, 170, 56, null, () => Toast(_vehicle == null ? "You do not own a vehicle yet." : $"Fuel: {_vehicle.fuel_l:0} L of {(_def != null ? _def.fuel_capacity_l : 0):0} L."), "Btn_Fuel");
            if (_vehicle == null) { Ui.Icon(_stage, "ic_fuel", Muted, 1184, 36, 32); Ui.Label(_stage, "No vehicle", "SemiBold", 18, Muted, 1226, 40, 110, 26); return; }
            float pct = FuelPercent(); Color c = pct < 10f ? Brand.Bad : pct < 25f ? Gold : Brand.Good;
            Ui.Icon(_stage, "ic_fuel", c, 1182, 36, 32);
            Ui.Label(_stage, "Fuel", "Regular", 13, Muted, 1226, 29, 60, 16);
            var t = Ui.Label(_stage, "", "Bold", 22, White, 1226, 44, 70, 28); CountText.Run(t, 0, (long)Mathf.Round(pct), "", "%", 0.3f, 0.9f);
            var bar = Ui.Bar(_stage, 1290, 58, 36, 7, pct / 100f, c); BarGrow.Run(bar, 7, Mathf.Max(7f, 36f * pct / 100f), 0.3f, 0.9f);
        }

        // ---- presence pill: the server's view of whether we are online (heartbeats), and how many players are
        int OnlineCount() => _svc.Presence.Online.State == LoadState.Ready ? _svc.Presence.Online.Value : Sum != null ? Sum.online_players : 0;

        string PresenceToast()
        {
            if (!_svc.Api.IsSignedIn) return "You are not connected.";
            if (!_svc.Presence.Connected) return "Reconnecting to the game server...";
            int n = OnlineCount(); int members = NetworkVehicle.Players.Count;
            string o = n <= 1 ? "You are the only player online right now." : $"{n} players are online right now.";
            return members > 1 ? o + $" You are driving in a convoy of {members}." : o;
        }

        void UpdatePresencePill()
        {
            if (_presenceLabel == null || _presenceDot == null) return;
            string text; Color c; bool live = false;
            if (!_svc.Api.IsSignedIn) { text = "Offline"; c = Muted; }
            else if (_svc.Presence.Connected) { int n = OnlineCount(); text = n > 0 ? n + " online" : "Online"; c = Brand.Good; live = true; }
            else { text = _svc.Presence.Clock.HasSession ? "Reconnecting" : "Connecting"; c = Gold; }
            _presenceLabel.text = text; _presenceLabel.fontSize = text.Length > 10 ? 15 : text.Length > 8 ? 18 : 22;
            _presenceDot.color = c;
            var pulse = _presenceDot.GetComponent<PulseImage>();
            if (live && pulse == null) PulseImage.On(_presenceDot, 2.2f, 0.85f, 1.2f, 0.55f, 1f);
        }

        // ---- notifications (the bell): the server's inbox. The badge is the server's unread count; the list is loaded when opened.
        void RefreshBell(bool force = false)
        {
            int n = _svc.Notifications.UnreadCount;
            if (!force && n == _lastUnread) return; _lastUnread = n;
            Debug.Log("[Dashboard] bell unread=" + n);
            if (_bellBadge == null) return;
            _bellBadge.gameObject.SetActive(n > 0); _bellCount.gameObject.SetActive(n > 0);
            _bellCount.text = n > 9 ? "9+" : n.ToString();
            if (n > 0 && _bellBadge.GetComponent<PulseImage>() == null) PulseImage.On(_bellBadge, 1.4f, 0.92f, 1.14f, 0.7f, 1f);
        }

        void BuildPopover()
        {
            _popover = Ui.Box(_root, "NoticePopover", 0, 0, 1920, 1080); _popover.gameObject.SetActive(false);
        }

        void ToggleNotices()
        {
            if (_popover == null) return;
            if (_popover.gameObject.activeSelf) { _popover.gameObject.SetActive(false); return; }
            _popover.gameObject.SetActive(true); DrawNotices(); _ = ReloadNotices();
        }

        async Task ReloadNotices()
        {
            await _svc.Notifications.LoadInbox();
            if (this == null || _popover == null || !_popover.gameObject.activeSelf) return;
            DrawNotices(); RefreshBell(true); MarkTargetsDirty();
        }

        void DrawNotices()
        {
            for (int i = _popover.childCount - 1; i >= 0; i--) Destroy(_popover.GetChild(i).gameObject);
            var host = _popover; var inbox = _svc.Notifications.Inbox;
            var items = (inbox.Value != null && inbox.Value.items != null && inbox.State != LoadState.Unavailable) ? inbox.Value.items : new NotificationItem[0];
            int shown = Mathf.Min(5, items.Length);
            Ui.Click(host, "Btn_NoticeClose", 0, 0, 1920, 1080, () => _popover.gameObject.SetActive(false));
            float h = 70 + Mathf.Max(1, shown) * 72 + 18; const float X = 1440, Y = 94, W = 460;
            Ui.Outlined(host, "NoticePanel", new Color(0.05f, 0.058f, 0.078f, 0.98f), new Color(Gold.r, Gold.g, Gold.b, 0.55f), X, Y, W, h, 18, 1.5f);
            Ui.Click(host, "NoticeSwallow", X, Y, W, h, () => { });
            Ui.Label(host, "Notifications", "Bold", 24, White, X + 22, Y + 14, 260, 32);
            SmallPillIn(host, "Mark read", X + W - 112, Y + 18, 92, 26, () => _ = MarkAllNotices());
            if (inbox.State == LoadState.Loading && items.Length == 0) Ui.Label(host, "Loading...", "SemiBold", 20, Muted, X + 22, Y + 74, W - 44, 28);
            else if (inbox.State == LoadState.Unavailable)
            {
                Ui.Label(host, "Notifications are unavailable.", "SemiBold", 20, Brand.Bad, X + 22, Y + 74, W - 44, 28);
                Ui.Paragraph(host, inbox.Error ?? "Please try again.", "Regular", 16, new Color(0.6f, 0.63f, 0.69f), X + 22, Y + 104, W - 44, 40);
                SmallPillIn(host, "Try again", X + 22, Y + 140, 100, 26, () => { _ = ReloadNotices(); });
            }
            else if (items.Length == 0)
            {
                Ui.Label(host, "Nothing new.", "SemiBold", 20, Muted, X + 22, Y + 74, W - 44, 28);
                Ui.Paragraph(host, "Payouts, level-ups, convoy and friend updates and server messages show up here.", "Regular", 16, new Color(0.6f, 0.63f, 0.69f), X + 22, Y + 102, W - 44, 40);
            }
            string serverNow = inbox.Value != null ? inbox.Value.server_time : null;
            for (int i = 0; i < shown; i++)
            {
                var n = items[i]; float y = Y + 66 + i * 72;
                var lv = NoticeMap.LevelFor(n.type); Color c = lv == NoticeLevel.Bad ? Brand.Bad : lv == NoticeLevel.Good ? Brand.Good : Blue;
                Ui.Disc(host, "Dot", n.read ? new Color(c.r, c.g, c.b, 0.35f) : c, X + 22, y + 8, 12);
                Ui.Label(host, n.title, n.read ? "Regular" : "SemiBold", 17, n.read ? Soft : White, X + 46, y, W - 160, 22);
                Ui.Label(host, TimeText.AgoFrom(n.created_at, serverNow), "Regular", 13, Muted, X + W - 118, y + 3, 96, 18, TextAnchor.UpperRight);
                Ui.Paragraph(host, n.message, "Regular", 14, n.read ? Muted : new Color(0.84f, 0.86f, 0.9f), X + 46, y + 22, W - 70, 40);
                var item = n; Ui.Click(host, "Notice_" + (i + 1), X + 10, y - 4, W - 20, 68, () => _ = ReadNotice(item));
            }
            Debug.Log($"[Dashboard] notices open count={items.Length} unread={(inbox.Value != null ? inbox.Value.unread : 0)} state={inbox.State.ToString().ToLowerInvariant()}");
            Entrance.On(host, 0f, 0.25f, 10f);
        }

        async Task ReadNotice(NotificationItem n)
        {
            if (n.read) return;
            n.read = true; DrawNotices();                                  // optimistic; the server is told right away
            var r = await _svc.Notifications.MarkRead(n.id);
            if (this == null) return;
            if (!r.Ok) { n.read = false; Toast(r.UserMessage); }
            RefreshBell(true); if (_popover.gameObject.activeSelf) DrawNotices();
        }

        async Task MarkAllNotices()
        {
            var r = await _svc.Notifications.MarkAllRead();
            if (this == null) return;
            if (!r.Ok) { Toast(r.UserMessage); return; }
            await _svc.Notifications.LoadInbox(); if (this == null) return;
            RefreshBell(true); if (_popover.gameObject.activeSelf) DrawNotices(); MarkTargetsDirty();
        }

        // ================================================================================================================ hero
        /// <summary>The hero banners are authored content served by the backend (copy, picture key, button action). Until they arrive the hero says so.</summary>
        void BuildSlides()
        {
            var h = _svc.Content.Hero; var tex = Brand.Scene(); var list = new List<Slide>();
            if (h.State == LoadState.Ready && h.Value != null && h.Value.items != null)
                foreach (var it in h.Value.items)
                {
                    var sl = new Slide { eyebrow = Eyebrow(it.eyebrow), t1 = it.headline ?? "", t2 = it.headline_accent ?? "", desc = it.body ?? "", button = it.button ?? "" };
                    switch (it.image_key)
                    {
                        case "convoy": sl.uv = Ui.Cover(tex, 0.40f, 0.50f, 0.50f, HeroAspect); break;
                        case "bus": sl.uv = Ui.Cover(tex, 0.30f, 0.16f, 0.70f, HeroAspect); break;
                        default: sl.uv = Ui.Cover(tex, 0f, 0.36f, 1f, HeroAspect); sl.textRight = true; break;      // the truck sits on the left of this picture, so the text goes right
                    }
                    switch (it.action)
                    {
                        case "jobs": if (_drive.Vehicle != null) { sl.button = "Resume Driving"; sl.go = () => _act.Resume(); } else sl.go = OpenJobs; break;
                        case "convoy": sl.go = () => _act.Convoy(); break;
                        case "bus": if (_bus != null && _bus.Active) sl.button = "Continue Route"; sl.go = () => _act.Bus(); break;
                        case "map": sl.go = () => _act.WorldMap(); break;
                        case "garage": sl.go = () => _act.Garage(); break;
                        default: sl.go = () => { }; break;
                    }
                    list.Add(sl);
                }
            _slides = list.ToArray();
            if (_slide >= _slides.Length) _slide = 0;
        }

        static string Eyebrow(string pipeSeparated) =>
            string.IsNullOrEmpty(pipeSeparated) ? "" : string.Join("  <color=#F9B521>•</color>  ", pipeSeparated.Split('|'));

        string HeroMessage()
        {
            switch (_svc.Content.Hero.State)
            {
                case LoadState.Unavailable: return "Featured content is unavailable right now.";
                case LoadState.Empty: return "Nothing is featured right now.";
                default: return "Loading featured content...";
            }
        }

        Rect HeroUv() => _slides != null && _slides.Length > 0 ? _slides[Mathf.Clamp(_slide, 0, _slides.Length - 1)].uv : Ui.Cover(Brand.Scene(), 0f, 0.36f, 1f, HeroAspect);

        void BuildHero()
        {
            const float X = HeroX, Y = HeroY, W = HeroW, H = HeroH;
            Ui.Outlined(_stage, "HeroFrame", new Color(0, 0, 0, 1), new Color(Gold.r, Gold.g, Gold.b, 0.85f), X - 2, Y - 2, W + 4, H + 4, 22, 2f);
            var tex = Brand.Scene();
            _heroPhoto = Ui.Photo(_stage, "HeroPhoto", tex, HeroUv(), X, Y, W, H, 20);
            _kb = KenBurnsView.On(_heroPhoto, HeroUv());
            var dark = new Color(0.02f, 0.025f, 0.04f, 0.90f); var clear = new Color(0.02f, 0.025f, 0.04f, 0f);
            _shadeL = Ui.Gradient(_stage, "HeroShadeL", dark, clear, true, X, Y, 760, H);
            _shadeR = Ui.Gradient(_stage, "HeroShadeR", clear, dark, true, X + W - 760, Y, 760, H);
            Ui.Gradient(_stage, "HeroFloor", new Color(0, 0, 0, 0), new Color(0, 0, 0, 0.45f), false, X, Y + H - 90, W, 90);

            _heroText = Ui.Box(_stage, "HeroText", 0, 0, 1920, 1080); _heroFade = GroupFade.On(_heroText);
            _heroEyebrow = Ui.Label(_heroText, "", "SemiBold", 18, Soft, X + 32, Y + 26, 640, 26);
            _heroT1 = Ui.Label(_heroText, "", "ExtraBold", 66, White, X + 30, Y + 62, 760, 80); Ui.Drop(_heroT1);
            _heroT2 = Ui.Label(_heroText, "", "ExtraBold", 66, Gold, X + 30, Y + 124, 760, 80); Ui.Drop(_heroT2);
            _heroDesc = Ui.Paragraph(_heroText, "", "Regular", 22, White, X + 32, Y + 206, 560, 60); Ui.Drop(_heroDesc);
            var btn = Ui.Box(_heroText, "HeroBtnBox", 0, 0, 1920, 1080); _heroBtnBox = btn.gameObject;
            var glow = Ui.Rounded(btn, "HeroButtonGlow", new Color(Gold.r, Gold.g, Gold.b, 0.25f), X + 26, Y + 256, 262, 66, 16); PulseImage.On(glow, 2.4f, 0.98f, 1.05f, 0.35f, 1f);
            Ui.Rounded(btn, "HeroButton", Gold, X + 32, Y + 262, 250, 54, 12);
            _heroBtnText = Ui.Label(btn, "Find a Job", "Bold", 24, Ink, X + 32 + 28, Y + 273, 170, 32);
            Ui.Icon(btn, "icon_arrow", Ink, X + 32 + 250 - 58, Y + 273, 32);
            Ui.Click(btn, "Btn_HeroAction", X + 32, Y + 262, 250, 54, () => { if (_slides != null && _slides.Length > 0) _slides[_slide].go(); });

            Ui.Outlined(_stage, "LocChip", new Color(0.03f, 0.035f, 0.05f, 0.82f), new Color(1, 1, 1, 0.12f), X + W - 238, Y + H - 66, 210, 44, 12);
            Ui.Icon(_stage, "ic_pin", Gold, X + W - 238 + 16, Y + H - 66 + 9, 26);
            Ui.Label(_stage, _heroPlace, "SemiBold", 18, White, X + W - 238 + 50, Y + H - 66 + 10, 160, 26);

            int n = _slides != null ? _slides.Length : 0; _dots = new Image[n];
            for (int i = 0; i < n; i++)
            {
                int idx = i; _dots[i] = Ui.Disc(_stage, "Dot" + i, new Color(1, 1, 1, 0.4f), X + W - 100 + i * 26, Y + 24, 13);
                Ui.Click(_stage, "Btn_Dot" + i, X + W - 106 + i * 26, Y + 16, 26, 28, () => { _slide = idx; ShowSlide(idx, true); _nextSlide = Time.unscaledTime + 9f; });
            }
        }

        void ShowSlide(int i, bool animate)
        {
            if (_heroT1 == null) return;
            if (_slides == null || _slides.Length == 0)
            {   // no banners from the server (yet): say why instead of showing made-up marketing copy
                _heroEyebrow.text = ""; _heroT1.text = ""; _heroT2.text = ""; _heroDesc.text = HeroMessage(); if (_heroBtnBox != null) _heroBtnBox.SetActive(false);
                _heroFade.SetHome(Vector2.zero); _shadeL.enabled = true; _shadeR.enabled = false;
                Debug.Log("[Dashboard] hero state=" + _svc.Content.Hero.State.ToString().ToLowerInvariant());
                return;
            }
            var s = _slides[i];
            Debug.Log("[Dashboard] slide=" + i);
            if (_heroBtnBox != null) _heroBtnBox.SetActive(true);
            _heroEyebrow.text = s.eyebrow; _heroT1.text = s.t1; _heroT2.text = s.t2; _heroDesc.text = s.desc; _heroBtnText.text = s.button;
            float dx = s.textRight ? 548f : 0f; var home = new Vector2(dx, 0f);
            _heroFade.SetHome(home); if (animate) { _heroFade.Play(0.55f, 30f); _heroDip = Time.unscaledTime; }
            _shadeL.enabled = !s.textRight; _shadeR.enabled = s.textRight;
            if (_kb != null) _kb.Rebase(s.uv); else if (_heroPhoto != null) _heroPhoto.uvRect = s.uv;
            for (int k = 0; k < _dots.Length; k++) _dots[k].color = k == i ? Gold : new Color(1, 1, 1, 0.4f);
        }

        void Update()
        {
            float now = Time.unscaledTime;
            if (_slides != null && _slides.Length > 1 && now > _nextSlide) { _slide = (_slide + 1) % _slides.Length; ShowSlide(_slide, true); _nextSlide = now + 8f; }
            if (_heroPhoto != null) { float p = Tween.Progress(now - _heroDip, 0f, 0.7f); _heroPhoto.color = new Color(1, 1, 1, Mathf.Lerp(0.15f, 1f, p)); }   // dips through dark and back on every slide change
            if (_toast != null && _toast.text.Length > 0 && now > _toastUntil) _toast.text = "";
            if (now > _nextBell) { _nextBell = now + 0.5f; RefreshBell(); }
            if (now > _nextPill) { _nextPill = now + 1f; UpdatePresencePill(); }
            if (_newsItems.Length > 1 && now > _nextNews) { _nextNews = now + 10f; NextNews(); }
            if (now > _nextPoll) { _nextPoll = now + 45f; _ = RefreshSnapshot(); }                // counts, coins, fuel, online players stay current while the dashboard is open
            if (now > _nextContent) { _nextContent = now + 120f; _ = LoadHeroContent(); _ = LoadNewsContent(); _ = LoadRecentPlayers(); }
            if (_page == Page.Jobs) JobsUpdate(now); else if (_page == Page.Multiplayer) MpUpdate(now); else if (_page == Page.Vehicles) VehUpdate(now); else if (_page == Page.Garage) GarUpdate(now); else if (_page == Page.Friends) FriUpdate(now); else if (_page == Page.Leaderboards) LbUpdate(now); else if (_page == Page.Profile) PrUpdate(now); else if (_page == Page.BusRoutes) BrUpdate(now); else if (_page == Page.Onboarding) ObUpdate(now);
            if (SmokeMode.LogTargets && !_targetsLogged && now > _builtAt + 2.5f) LogTargets();
        }

        // Smoke / E2E only: the browser tests need the dashboard's own click positions.
        bool _targetsLogged; float _builtAt = float.MaxValue;
        void LogTargets()
        {
            _targetsLogged = true;
            var sb = new System.Text.StringBuilder("[WorldMap] targets (dashboard)");
            foreach (var b in _root.GetComponentsInChildren<Selectable>(false).Where(x => x is Button || x is InputField))
            {
                var c = new Vector3[4]; ((RectTransform)b.transform).GetWorldCorners(c);
                sb.Append($"\n  TARGET {b.name} x={(c[0].x + c[2].x) * 0.5f / Screen.width:0.000} y={1f - (c[0].y + c[2].y) * 0.5f / Screen.height:0.000}");
            }
            Debug.Log(sb.ToString());
        }

        public void Toast(string m, float s = 3f) { if (_toast != null) { _toast.text = m; _toastUntil = Time.unscaledTime + s; } }

        /// <summary>The server's route/city state arrived (or changed): redraw what depends on it.</summary>
        public void OnWorldChanged() { Refill("Routes", BuildRoutes); Refill("Quick", BuildQuick); }

        // ================================================================================================================ milestone + truck
        Image Panel(float x, float y, float w, float h, bool gold = false)
            => Ui.Outlined(_stage, "Panel", PanelFill, gold ? new Color(Gold.r, Gold.g, Gold.b, 0.7f) : PanelLine, x, y, w, h, 18, gold ? 2f : 1.5f);

        /// <summary>The goal card. Daily challenges have a backend foundation but no UI yet, so it shows the server's next distance milestone (from lifetime distance).</summary>
        void BuildMilestone()
        {
            var pr = Sum != null ? Sum.progression : null; var ms = pr != null ? pr.next_milestone : null; bool ok = ms != null && !string.IsNullOrEmpty(ms.id) || (ms != null && ms.target > 0);
            Panel(1497, 142, 402, 148, true);
            Ui.Icon(_stage, "ic_trophy", Gold, 1518, 158, 46);
            Ui.Label(_stage, "Next Milestone", "Bold", 26, White, 1578, 156, 300, 34);
            Ui.Label(_stage, "Daily challenges are coming soon", "Regular", 15, Muted, 1578, 190, 300, 20);
            if (!ok) { Ui.Label(_stage, "Progress is unavailable right now.", "SemiBold", 18, Muted, 1518, 228, 362, 26); return; }
            Ui.Label(_stage, ms.title, "SemiBold", 21, White, 1518, 224, 200, 28);
            float fraction = Mathf.Clamp01((float)ms.fraction);
            var cur = Ui.Label(_stage, "", "Bold", 19, Gold, 1640, 226, 240, 26, TextAnchor.UpperRight);
            CountText.Run(cur, 0, (long)Math.Round(ms.current), "", $" / {Money.Format(ms.target)} km", 0.45f, 1.1f);
            var fill = Ui.Bar(_stage, 1518, 262, 362, 12, fraction, Gold); BarGrow.Run(fill, 12, Mathf.Max(12f, 362f * fraction), 0.5f, 1.1f);
            Ui.Icon(_stage, "ic_check_circle", Brand.Good, 1518, 266, 16);
            Ui.Label(_stage, $"{pr.jobs_completed} jobs completed", "Regular", 14, Soft, 1540, 267, 200, 18);
        }

        /// <summary>The truck picture is a real photograph cut from the approved dashboard concept (tools/crop_ref_art.py).</summary>
        void TruckPhoto(float x, float y, float w, float h, Color tint)
        {
            var tex = Brand.Texture("truck_photo");
            if (tex != null) Ui.Photo(_stage, "TruckPhoto", tex, new Rect(0, 0, 1, 1), x, y, w, h, 10, tint);
            else Ui.Icon(_stage, "truck_thumb", tint, x, y, w);
        }

        void BuildYourTruck()
        {
            Panel(1497, 306, 402, 154);
            Ui.Label(_stage, "Your Truck", "Bold", 26, White, 1518, 316, 200, 34);
            SmallPill(1812, 320, 68, 26, "View All", _act.Garage);
            if (_vehicle == null)
            {   // empty state: no vehicle yet
                TruckPhoto(1508, 346, 126, 68, new Color(1, 1, 1, 0.35f));
                Ui.Label(_stage, "No vehicle yet", "SemiBold", 22, Muted, 1650, 362, 220, 30);
                Ui.Rounded(_stage, "TruckCta", Gold, 1650, 402, 200, 38, 10); Ui.Label(_stage, "Visit the garage", "Bold", 17, Ink, 1650, 410, 200, 24, TextAnchor.UpperCenter);
                Ui.Click(_stage, "Btn_YourTruck", 1497, 306, 402, 154, _act.Garage); return;
            }
            float fuel = FuelPercent(); double damage = _vehicle.damage_pct;
            var state = TruckStateOf(_vehicle.status, fuel, damage);
            Color sc = state == TruckState.Ready ? Brand.Good : state == TruckState.LowFuel ? Gold : state == TruckState.OnJob ? Blue : Brand.Bad;
            TruckPhoto(1508, 334, 126, 68, Color.white);
            var nm = Ui.Label(_stage, _def != null ? _def.name : _vehicle.definition_id, "SemiBold", 21, White, 1642, 352, 200, 56); nm.horizontalOverflow = HorizontalWrapMode.Wrap;
            var dot = Ui.Disc(_stage, "StatusDot", sc, 1642, 396, 14); if (state == TruckState.Ready) PulseImage.On(dot, 2.2f, 0.85f, 1.2f, 0.6f, 1f);
            Ui.Label(_stage, Vitals.Label(state), "SemiBold", 19, sc, 1664, 390, 150, 26);
            Ui.Rounded(_stage, "TruckGo", Gold, 1844, 350, 40, 40, 20); Ui.Icon(_stage, "ic_chevron_right", Ink, 1852, 358, 24);

            // real stats from the vehicle definition and its saved state
            float cond = Vitals.ConditionPercent(damage); Color cc = cond >= 80f ? Brand.Good : cond >= 50f ? Gold : Brand.Bad;
            bool bus = _def != null && _def.category == "bus";
            string cap = _def == null ? "-" : bus ? _def.passenger_capacity + " seats" : Money.Format(_def.cargo_capacity_kg) + " kg";
            Stat(1518, "ic_gauge", "Top speed", _def != null ? $"{_def.max_speed_kmh:0} km/h" : "-", White);
            Stat(1650, bus ? "ic_people" : "ic_weight", bus ? "Capacity" : "Load", cap, White);
            Stat(1780, "ic_wrench", "Condition", $"{cond:0}%", cc);
            Ui.Click(_stage, "Btn_YourTruck", 1497, 306, 402, 154, _act.Garage);
        }

        /// <summary>The server's vehicle status (ready / low_fuel / needs_repair / on_job / on_route / destroyed) as the card's state.</summary>
        TruckState TruckStateOf(string status, float fuelPercent, double damage)
        {
            switch (status)
            {
                case "ready": return TruckState.Ready;
                case "low_fuel": return TruckState.LowFuel;
                case "needs_repair": case "destroyed": return TruckState.NeedsRepair;
                case "on_job": case "on_route": return TruckState.OnJob;
                default: return Vitals.Status(true, fuelPercent, damage, _drive != null && _drive.Job != null);      // an older server that sends no status
            }
        }

        void Stat(float x, string icon, string label, string value, Color vc)
        {
            Ui.Icon(_stage, icon, Muted, x, 428, 22);
            Ui.Label(_stage, label, "Regular", 13, Muted, x + 28, 420, 100, 16);
            Ui.Label(_stage, value, "Bold", 16, vc, x + 28, 435, 104, 22);
        }

        Image SmallPill(float x, float y, float w, float h, string label, Action click) => SmallPillIn(_stage, label, x, y, w, h, click);

        Image SmallPillIn(Transform parent, string label, float x, float y, float w, float h, Action click)
        {
            var o = Ui.Outlined(parent, "SmallPill", new Color(0.05f, 0.055f, 0.07f, 0.9f), new Color(1, 1, 1, 0.16f), x, y, w, h, 8, 1.2f);
            Ui.Label(parent, label, "SemiBold", 15, White, x, y + 3, w, h - 4, TextAnchor.UpperCenter);
            Ui.Click(parent, "Btn_" + label.Replace(" ", ""), x, y, w, h, click); return o;
        }

        // ================================================================================================================ quick actions
        void BuildQuick()
        {
            var scene = Brand.Scene(); var model = WorldMapScreen.Model(); var art = WorldMapScreen.Artwork(model);
            const float cw = 379, ch = 129, aspect = cw / ch;
            var cards = new (string title, string desc, string icon, Color tint, Texture tex, Rect uv, Action go)[]
            {
                ("Quick Job", "Find and start a job\nimmediately", "ic_bus", new Color(0.95f, 0.62f, 0.10f), scene, Ui.Cover(scene, 0.10f, 0.56f, 0.34f, aspect), _act.QuickJob),
                ("Multiplayer\nConvoy", "Drive with friends\nacross Africa", "ic_people", new Color(0.25f, 0.55f, 1f), scene, Ui.Cover(scene, 0.36f, 0.52f, 0.40f, aspect), _act.Convoy),
                ("Explore Map", "Discover cities, roads and\nlocations", "ic_map", new Color(0.20f, 0.80f, 0.55f), art != null ? art : scene, art != null ? Ui.Cover(art, 0.28f, 0.45f, 0.40f, aspect) : Ui.Cover(scene, 0.0f, 0.5f, 0.4f, aspect), _act.WorldMap),
                ("Garage", "Customize and upgrade\nyour vehicles", "ic_wrench", new Color(0.65f, 0.40f, 1f), scene, Ui.Cover(scene, 0.18f, 0.52f, 0.20f, aspect), _act.Garage),
            };
            float w = 382, gap = 18, x = 316; int idx = 0;
            foreach (var c in cards)
            {
                Ui.Outlined(_stage, "QCardFrame", new Color(0, 0, 0, 1), new Color(1, 1, 1, 0.14f), x, 474, w, 132, 18);
                Ui.Photo(_stage, "QCardPhoto", c.tex, c.uv, x + 1.5f, 475.5f, w - 3, 129, 17, new Color(0.62f, 0.62f, 0.66f));
                Ui.Flat(_stage, "QTint", new Color(c.tint.r * 0.3f, c.tint.g * 0.3f, c.tint.b * 0.3f, 0.45f), x + 2, 476, w - 4, 128);
                Ui.Gradient(_stage, "QShade", new Color(0.02f, 0.025f, 0.04f, 0.88f), new Color(0.02f, 0.025f, 0.04f, 0.1f), true, x + 2, 476, w - 4, 128);
                Ui.Rounded(_stage, "QTile", new Color(c.tint.r * 0.28f, c.tint.g * 0.28f, c.tint.b * 0.28f, 0.95f), x + 20, 506, 68, 68, 16);
                Ui.Icon(_stage, c.icon, c.tint, x + 34, 520, 40);
                Ui.Label(_stage, c.title, "Bold", 27, White, x + 104, c.title.Contains("\n") ? 490 : 506, 260, 66).lineSpacing = 0.9f;
                _qDefault[idx] = c.desc;
                _qDesc[idx] = Ui.Paragraph(_stage, c.desc, "Regular", 18, new Color(0.9f, 0.92f, 0.95f), x + 104, c.title.Contains("\n") ? 548 : 544, 240, 48);
                Ui.Rounded(_stage, "QGo", Gold, x + w - 62, 546, 44, 44, 22); Ui.Icon(_stage, "ic_chevron_right", Ink, x + w - 54, 554, 28);
                Ui.Click(_stage, "Btn_Quick_" + c.title.Replace("\n", "").Replace(" ", ""), x, 474, w, 132, () => c.go());
                x += w + gap; idx++;
            }
            UpdateQuickText();
        }

        /// <summary>Quick-card subtitles carry live facts: all of them are the server's numbers (job board counts, convoy, world state, garage).</summary>
        void UpdateQuickText()
        {
            if (_qDesc[0] == null) return;
            var sum = Sum; var st = _svc.Player.Summary.State; bool down = sum == null || st == LoadState.Unavailable;
            string job = _qDefault[0];
            if (!down && sum.jobs != null) job = sum.jobs.available > 0 ? $"{sum.jobs.available} open job{(sum.jobs.available == 1 ? "" : "s")}\nBest pays {Money.Format(sum.jobs.best_reward)} coins" : "No open jobs right now\nCheck back soon";
            else if (down) job = "Job count unavailable\nOpen the job board";
            _qDesc[0].text = job;
            int members = NetworkVehicle.Players.Count;
            _qDesc[1].text = members > 1 ? $"You are in a convoy\n{members} drivers on the road"
                : !down && sum.convoy != null && sum.convoy.Exists ? $"In convoy: {sum.convoy.name}\n{sum.convoy.member_count}/{sum.convoy.capacity} drivers"
                : _qDefault[1];
            var model = WorldMapScreen.Model();
            if (!down && sum.world != null && sum.world.cities > 0) _qDesc[2].text = $"{sum.world.cities} cities · {sum.world.routes} routes\n{sum.world.routes_open} drivable now";
            else if (model != null && model.HasServerState) { int level = PlayerLevel(); int drivable = model.Routes.Count(r => WorldMapModel.IsDrivable(model.StateFor(r, level))); _qDesc[2].text = $"{model.Cities.Count} cities · {model.Routes.Count} routes\n{drivable} drivable now"; }
            else _qDesc[2].text = "Route status unavailable\nOpen the map";
            int n = !down ? sum.vehicle_count : (_svc.Vehicles != null ? _svc.Vehicles.Length : 0);
            _qDesc[3].text = down ? "Garage unavailable\nTry again shortly" : n == 0 ? "You do not own a\nvehicle yet" : $"{n} vehicle{(n == 1 ? "" : "s")} in your garage\nUpgrade and repair";
        }

        int PlayerLevel() => _svc.Profile != null ? _svc.Profile.level : 1;

        // ================================================================================================================ featured jobs
        void BuildFeatured()
        {
            Panel(316, 622, 1065, 292);
            Ui.Label(_stage, "Featured Jobs", "Bold", 26, White, 338, 636, 300, 34);
            SmallPill(1290, 640, 70, 26, "View All", OpenJobs);
            _jobsHost = Ui.Box(_stage, "JobsHost", 316, 622, 1065, 292);
        }

        async Task LoadJobs()
        {
            int token = ++_loadToken;
            SetJobState(JobState.Loading, null); FillJobs();
            var r = await _svc.Jobs.LoadBoard();
            if (this == null || token != _loadToken) return;
            if (!r.Ok) { _jobs = null; SetJobState(JobState.Error, r.UserMessage); }
            else { _jobs = r.Value; SetJobState(_jobs.Length == 0 ? JobState.Empty : JobState.Ready, null); }
            FillJobs();
        }

        void SetJobState(JobState s, string error)
        {
            _jobState = s; _jobError = error;
            Debug.Log("[Dashboard] jobs state=" + s.ToString().ToLowerInvariant() + (error != null ? " error=" + error : "") + (_jobs != null ? " count=" + _jobs.Length : ""));
        }

        void FillJobs()
        {
            if (_jobsHost == null) return;
            for (int i = _jobsHost.childCount - 1; i >= 0; i--) Destroy(_jobsHost.GetChild(i).gameObject);
            UpdateQuickText();
            MarkTargetsDirty();
            SetJobsBadge(_jobState == JobState.Ready && _jobs != null ? _jobs.Length : 0);
            switch (_jobState)
            {
                case JobState.Loading: BuildSkeleton(); return;
                case JobState.Error: BuildJobsMessage("ic_road", "We could not load the job board", _jobError ?? "Please try again.", Brand.Bad, "Try again", "Btn_RetryJobs", () => _ = LoadJobs()); return;
                case JobState.Empty: BuildJobsMessage("ic_box", "No open jobs right now", "The server posts new ones regularly. Check back soon, or run a bus route meanwhile.", Muted, "Open the job board", "Btn_OpenBoard", OpenJobs); return;
            }
            var owned = new HashSet<string>((_svc.Vehicles ?? new OwnedVehicleDto[0]).Select(v => CategoryOf(v.definition_id)));
            var pick = _jobs.OrderByDescending(j => j.eligible).ThenByDescending(j => owned.Contains(j.required_category)).ThenByDescending(j => j.reward).Take(4).ToArray();
            const float cw = 246, gap = 14, x0 = 22;
            for (int i = 0; i < pick.Length; i++)
            {
                var card = Ui.Box(_jobsHost, "Card" + i, 0, 0, 1065, 292); Entrance.On(card, 0.06f * i, 0.5f, 14f);
                JobCard(card, pick[i], x0 + i * (cw + gap), 58, cw);
            }
        }

        void BuildSkeleton()
        {
            const float cw = 246, gap = 14, x0 = 22;
            for (int i = 0; i < 4; i++)
            {
                float x = x0 + i * (cw + gap), y = 58;
                Ui.Outlined(_jobsHost, "SkCard", new Color(0.045f, 0.052f, 0.07f, 0.98f), new Color(1, 1, 1, 0.07f), x, y, cw, 230, 12, 1.2f);
                var blocks = new[] { Ui.Rounded(_jobsHost, "SkPhoto", new Color(1, 1, 1, 0.3f), x + 1.2f, y + 1.2f, cw - 2.4f, 70, 11), Ui.Rounded(_jobsHost, "SkA", new Color(1, 1, 1, 0.3f), x + 10, y + 82, 170, 14, 6),
                    Ui.Rounded(_jobsHost, "SkB", new Color(1, 1, 1, 0.3f), x + 10, y + 106, 120, 14, 6), Ui.Rounded(_jobsHost, "SkC", new Color(1, 1, 1, 0.3f), x + 10, y + 138, 90, 12, 6),
                    Ui.Rounded(_jobsHost, "SkD", new Color(1, 1, 1, 0.3f), x + 10, y + 162, 70, 12, 6), Ui.Rounded(_jobsHost, "SkE", new Color(1, 1, 1, 0.3f), x + 10, y + 186, cw - 20, 28, 7) };
                for (int k = 0; k < blocks.Length; k++) Breath.On(blocks[k], i * 2 + k);
            }
            Ui.Label(_jobsHost, "Loading jobs...", "Regular", 16, Muted, 22, 36, 300, 20);
        }

        void BuildJobsMessage(string icon, string title, string body, Color titleColor, string button, string buttonName, Action onClick)
        {
            Ui.Icon(_jobsHost, icon, new Color(1, 1, 1, 0.22f), 60, 96, 84);
            Ui.Label(_jobsHost, title, "Bold", 26, titleColor == Muted ? White : titleColor, 176, 98, 800, 34);
            Ui.Paragraph(_jobsHost, body, "Regular", 19, Soft, 176, 138, 800, 56);
            Ui.Rounded(_jobsHost, "MsgBtn", Gold, 176, 204, 250, 50, 12); Ui.Label(_jobsHost, button, "Bold", 20, Ink, 176, 215, 250, 30, TextAnchor.UpperCenter);
            Ui.Click(_jobsHost, buttonName, 176, 204, 250, 50, onClick);
        }

        string CategoryOf(string defId) { var d = _svc.Definitions?.FirstOrDefault(x => x.id == defId); return d != null ? d.category : (defId != null && defId.StartsWith("bus") ? "bus" : "truck"); }

        /// <summary>The picture key and label come from the server's cargo catalogue (job.cargo_image / cargo_type); a missing picture falls back to the generic one.</summary>
        static string CargoArt(JobDto j) => "job_" + (string.IsNullOrEmpty(j.cargo_image) ? "general" : j.cargo_image);

        static string BlockedText(JobDto j)
        {
            switch (j.blocked_reason)
            {
                case "level_too_low": return "Level " + j.min_level;
                case "route_locked": case "route_unavailable": return "Route closed";
                case "activity_in_progress": return "Finish current";
                case "no_matching_vehicle": return "Need a " + j.required_category;
                case "job_full": return "Full";
                default: return "Unavailable";
            }
        }

        static string BlockedExplain(JobDto j)
        {
            switch (j.blocked_reason)
            {
                case "level_too_low": return $"This job needs level {j.min_level}.";
                case "route_locked": return "That route is locked at your level.";
                case "route_unavailable": return "That route is not open yet.";
                case "activity_in_progress": return "Finish or abandon your current job or bus route first.";
                case "no_matching_vehicle": return $"You need a {j.required_category} for this job.";
                case "job_full": return "This job already has all the drivers it needs.";
                default: return "You cannot take this job right now.";
            }
        }

        void JobCard(Transform host, JobDto j, float x, float y, float w)
        {
            Ui.Outlined(host, "JobCard", new Color(0.045f, 0.052f, 0.07f, 0.98f), new Color(1, 1, 1, 0.09f), x, y, w, 230, 12, 1.2f);
            var art = Brand.Texture(CargoArt(j)) ?? Brand.Texture("job_general"); Ui.Photo(host, "JobPhoto", art, Ui.Cover(art, 0f, 0.22f, 1f, (w - 2.4f) / 70f), x + 1.2f, y + 1.2f, w - 2.4f, 70, 11);
            Ui.Gradient(host, "JobPhotoShade", new Color(0, 0, 0, 0), new Color(0, 0, 0, 0.7f), false, x + 1.2f, y + 36, w - 2.4f, 35);
            Ui.Label(host, CargoClass(j.cargo_category), "SemiBold", 13, White, x + 10, y + 50, w - 20, 18);
            // origin and destination on two lines so long depot names never collide with the rows below
            string from = j.origin != null ? j.origin.name : "?", to = j.destination != null ? j.destination.name : "?";
            var t1 = Ui.Label(host, from, "Bold", 17, White, x + 10, y + 76, w - 16, 22); t1.horizontalOverflow = HorizontalWrapMode.Wrap; t1.resizeTextForBestFit = true; t1.resizeTextMinSize = 12; t1.resizeTextMaxSize = 17;
            var t2 = Ui.Label(host, "→ " + to, "Bold", 17, Gold, x + 10, y + 98, w - 16, 22); t2.horizontalOverflow = HorizontalWrapMode.Wrap; t2.resizeTextForBestFit = true; t2.resizeTextMinSize = 12; t2.resizeTextMaxSize = 17;
            Ui.Icon(host, "ic_box", Muted, x + 10, y + 124, 20); Ui.Label(host, j.cargo_type, "Regular", 16, new Color(0.84f, 0.86f, 0.9f), x + 36, y + 123, w - 46, 22);
            Ui.Icon(host, "ic_road", Muted, x + 10, y + 148, 20); Ui.Label(host, $"{j.distance_km:0.#} km", "Regular", 16, new Color(0.84f, 0.86f, 0.9f), x + 36, y + 147, 90, 22);
            Ui.Icon(host, "ic_clock", Muted, x + w - 112, y + 148, 20); Ui.Label(host, "~" + JobMath.FormatDuration(j.est_minutes > 0 ? j.est_minutes : JobMath.EtaMinutes(j.distance_km)), "Regular", 16, new Color(0.84f, 0.86f, 0.9f), x + w - 88, y + 147, 84, 22);
            Ui.Icon(host, "ic_coin", Gold, x + 10, y + 172, 20); Ui.Label(host, Money.Format(j.reward), "SemiBold", 16, White, x + 36, y + 171, 90, 22);
            Color dc = j.difficulty <= 2 ? Brand.Good : j.difficulty == 3 ? Gold : Brand.Bad;
            Ui.Icon(host, "ic_bars", dc, x + w - 104, y + 172, 20); Ui.Label(host, JobMath.Difficulty(j.difficulty), "SemiBold", 16, dc, x + w - 80, y + 171, 74, 22);
            // the server says whether THIS player can take the job right now; a blocked job explains why instead of failing on tap
            bool can = j.eligible;
            Ui.Rounded(host, "AcceptBg", can ? Gold : new Color(0.22f, 0.24f, 0.29f), x + 8, y + 196, w - 16, 28, 7);
            Ui.Label(host, can ? "Accept" : BlockedText(j), "Bold", 16, can ? Ink : Muted, x + 8, y + 199, w - 16, 24, TextAnchor.UpperCenter);
            var job = j; Ui.Click(host, can ? "Btn_Accept_" + j.code : "Btn_Blocked_" + j.code, x + 8, y + 194, w - 16, 30, () => { if (job.eligible) _act.AcceptJob(job); else Toast(BlockedExplain(job)); });
        }

        // ================================================================================================================ routes: the real artwork + the validated route data (same source as the world map)
        void BuildRoutes()
        {
            Panel(1397, 622, 502, 292);
            Ui.Label(_stage, "Popular Routes", "Bold", 26, White, 1418, 636, 300, 34);
            SmallPill(1808, 640, 70, 26, "Map", _act.WorldMap);
            const float bx = 1417, by = 678, bw = 462, bh = 218;
            var model = WorldMapScreen.Model(); var tex = WorldMapScreen.Artwork(model);
            if (model == null || tex == null) { Ui.Icon(_stage, "ic_map", new Color(1, 1, 1, 0.2f), bx + 190, by + 54, 70); Ui.Label(_stage, "The map is unavailable right now.", "SemiBold", 18, Muted, bx, by + 130, bw, 26, TextAnchor.UpperCenter); return; }
            // a crop of the artwork around the Gulf of Guinea coast, in artwork pixels (origin top-left)
            const float cx0 = 880f, cy0 = 440f, cw = 760f; float ch = cw * bh / bw;
            float tw = model.Data.image.textureWidth, th = model.Data.image.textureHeight;
            Ui.Photo(_stage, "MapPhoto", tex, new Rect(cx0 / tw, (th - (cy0 + ch)) / th, cw / tw, ch / th), bx, by, bw, bh, 12);
            var host = Ui.Box(_stage, "MapHost", bx, by, bw, bh);
            Ui.Click(_stage, "Btn_RoutesMap", bx, by, bw, bh, _act.WorldMap);          // below the retry note: that tap must retry, not open the map
            Func<MapCity, Vector2> at = c => new Vector2((c.mapX - cx0) / cw * bw, (c.mapY - cy0) / ch * bh);
            Func<MapCity, bool> inside = c => c.mapX > cx0 + 8 && c.mapX < cx0 + cw - 8 && c.mapY > cy0 + 8 && c.mapY < cy0 + ch - 8;
            int level = PlayerLevel(); bool known = model.HasServerState;
            if (!known)
            {   // the server has not told us what is open: show the map, claim nothing, offer a retry
                var ws = _svc.World.State.State;
                var note = Ui.Rounded(_stage, "WorldNote", new Color(0.03f, 0.035f, 0.05f, 0.88f), bx + 10, by + 10, bw - 20, 34, 10);
                Ui.Label(_stage, ws == LoadState.Loading || ws == LoadState.Idle ? "Loading route status..." : "Route status unavailable. Tap to retry.", "SemiBold", 15, ws == LoadState.Unavailable ? Brand.Bad : Muted, bx + 10, by + 15, bw - 20, 24, TextAnchor.UpperCenter);
                if (ws == LoadState.Unavailable) Ui.Click(_stage, "Btn_RetryWorld", bx + 10, by + 10, bw - 20, 34, () => _ = ReloadWorld());
            }
            foreach (var r in model.Routes)
            {
                var st = model.StateFor(r, level); bool open = WorldMapModel.IsDrivable(st);
                for (int i = 1; i < r.via.Length; i++)
                {
                    var a = model.City(r.via[i - 1]); var b = model.City(r.via[i]); if (!inside(a) || !inside(b)) continue;
                    if (open) { Ui.Segment(host, at(a), at(b), 7f, new Color(Gold.r, Gold.g, Gold.b, 0.25f)); Ui.Segment(host, at(a), at(b), 3.5f, Gold); }
                    else Ui.Segment(host, at(a), at(b), 2f, new Color(1f, 1f, 1f, st == RouteState.PlannedLocked ? 0.22f : 0.45f));
                }
            }
            // markers: gold = drivable now, blue ring = planned, red lock = locked at your level. Labels avoid overlapping each other.
            var labelled = new List<Vector2>();
            var order = model.Cities.Where(inside).OrderByDescending(c => model.StateFor(c, level) == CityState.Open).ThenByDescending(c => model.RoutesAt(c.id).Count()).ToList();
            foreach (var c in order)
            {
                var p = at(c); var st = model.StateFor(c, level);
                if (st == CityState.Open)
                {
                    var glow = Ui.Disc(host, "CityGlow", new Color(Gold.r, Gold.g, Gold.b, 0.3f), p.x - 11, p.y - 11, 22);
                    if (IsHomeCity(c)) PulseImage.On(glow, 1.8f, 0.8f, 1.6f, 0.2f, 1f);
                    Ui.Disc(host, "City", Gold, p.x - 5, p.y - 5, 10);
                }
                else if (st == CityState.Planned) { Ui.Disc(host, "CityRing", Blue, p.x - 7, p.y - 7, 14); Ui.Disc(host, "CityDot", new Color(0.05f, 0.08f, 0.14f), p.x - 4, p.y - 4, 8); }
                else if (st == CityState.Unavailable) { Ui.Disc(host, "CityRing", Muted, p.x - 6, p.y - 6, 12); Ui.Disc(host, "CityDot", new Color(0.05f, 0.08f, 0.14f), p.x - 3.5f, p.y - 3.5f, 7); }
                else { Ui.Disc(host, "CityLock", Brand.Bad, p.x - 10, p.y - 10, 20); Ui.Icon(host, "ic_lock", White, p.x - 6.5f, p.y - 6.5f, 13); }
                if (labelled.Count >= 5 || labelled.Any(q => (q - p).magnitude < 64f)) continue;
                labelled.Add(p);
                var l = Ui.Label(host, c.name, "Bold", 15, White, p.x + 12, p.y - 22, 110, 20); Ui.Drop(l, 0.9f);
                int need = model.RoutesAt(c.id).Select(r => r.unlockLevel).DefaultIfEmpty(0).Min();
                if (st == CityState.Planned) Ui.Label(host, "Planned", "SemiBold", 12, Blue, p.x + 12, p.y - 6, 90, 16);
                else if (st == CityState.Locked) Ui.Label(host, $"Lv. {need}", "SemiBold", 12, Brand.Bad, p.x + 12, p.y - 6, 90, 16);
            }
            Ui.Disc(_stage, "LegendLive", Gold, bx + 10, by + bh - 22, 9); Ui.Label(_stage, "Open", "SemiBold", 13, White, bx + 25, by + bh - 27, 60, 18);
            Ui.Disc(_stage, "LegendPlanned", Blue, bx + 76, by + bh - 22, 9); Ui.Label(_stage, "Planned", "SemiBold", 13, new Color(0.9f, 0.9f, 0.9f), bx + 91, by + bh - 27, 80, 18);
            Ui.Disc(_stage, "LegendLocked", Brand.Bad, bx + 160, by + bh - 22, 9); Ui.Label(_stage, "Locked", "SemiBold", 13, new Color(0.9f, 0.9f, 0.9f), bx + 175, by + bh - 27, 80, 18);
        }

        async Task ReloadWorld()
        {
            var pending = _svc.World.Load(WorldMapScreen.Model());
            Refill("Routes", BuildRoutes); await pending; if (this == null) return;
            Debug.Log("[Dashboard] world state=" + _svc.World.State.State.ToString().ToLowerInvariant());
            OnWorldChanged();
        }

        /// <summary>The city the playable route starts from (the player's depot) pulses to say "you are here".</summary>
        bool IsHomeCity(MapCity c)
        {
            var spec = _drive != null && _drive.Route != null ? _drive.Route.Spec : null;
            if (spec != null && spec.meta != null) return string.Equals(c.name, spec.meta.from, StringComparison.OrdinalIgnoreCase);
            return c.id == "lagos";
        }

        // ================================================================================================================ bottom row
        void BuildPlayers()
        {
            Panel(316, 924, 468, 142);
            Ui.Label(_stage, "Recent Players", "Bold", 24, White, 338, 938, 300, 32);
            SmallPill(696, 942, 70, 26, "View All", _act.Convoy);
            var rec = _svc.Friends.Recent;
            if (rec.State == LoadState.Idle || rec.State == LoadState.Loading)
            { Ui.Label(_stage, "Loading players...", "SemiBold", 21, Muted, 338, 990, 420, 28); return; }
            if (rec.State == LoadState.Unavailable)
            {
                Ui.Icon(_stage, "ic_people", new Color(1, 1, 1, 0.22f), 338, 984, 52);
                Ui.Label(_stage, "Recent players are unavailable right now.", "SemiBold", 21, Brand.Bad, 402, 986, 370, 28);
                Ui.Label(_stage, "Tap to try again.", "Regular", 17, new Color(0.55f, 0.58f, 0.64f), 402, 1018, 370, 24);
                Ui.Click(_stage, "Btn_RetryPlayers", 316, 924, 468, 142, () => { _svc.Friends.Recent.Reset(); _ = LoadRecentPlayers(); }); return;
            }
            var people = rec.Value != null && rec.Value.items != null ? rec.Value.items : new PersonDto[0];
            if (people.Length == 0)
            {
                Ui.Icon(_stage, "ic_people", new Color(1, 1, 1, 0.22f), 338, 984, 52);
                Ui.Label(_stage, "Players you drive with appear here.", "SemiBold", 21, Muted, 402, 986, 370, 28);
                Ui.Label(_stage, "Your convoy crew is remembered.", "Regular", 17, new Color(0.55f, 0.58f, 0.64f), 402, 1018, 370, 24);
                Ui.Click(_stage, "Btn_PlayersEmpty", 316, 924, 468, 142, _act.Convoy); return;
            }
            string serverNow = rec.Value.server_time;
            for (int i = 0; i < Math.Min(2, people.Length); i++)
            {
                var e = people[i]; float x = 338 + i * 220; bool online = e.presence != null && e.presence.online;
                People.AvatarRgb(e.display_name, out float r, out float g, out float b);
                Ui.Disc(_stage, "AvRing", online ? Brand.Good : new Color(1, 1, 1, 0.25f), x, 984, 64);
                Ui.Disc(_stage, "Av", new Color(r, g, b), x + 3, 987, 58);
                Ui.Label(_stage, People.Initials(e.display_name), "Bold", 22, White, x + 3, 1004, 58, 28, TextAnchor.UpperCenter);
                var n = Ui.Label(_stage, e.display_name, "Bold", 18, White, x + 74, 988, 124, 24); n.resizeTextForBestFit = true; n.resizeTextMinSize = 12; n.resizeTextMaxSize = 18; n.horizontalOverflow = HorizontalWrapMode.Wrap;
                string status = online ? "Online" : (e.presence != null && !string.IsNullOrEmpty(e.presence.last_seen_at) ? TimeText.AgoFrom(e.presence.last_seen_at, serverNow) : "Offline");
                Ui.Disc(_stage, "St", online ? Brand.Good : Muted, x + 74, 1020, 9);
                Ui.Label(_stage, status, "Regular", 14, online ? Brand.Good : Muted, x + 88, 1015, 110, 18);
            }
        }

        void BuildNews()
        {
            Panel(800, 924, 581, 142);
            Ui.Label(_stage, "Latest News", "Bold", 24, White, 822, 938, 300, 32);
            _newsHost = Ui.Box(_stage, "NewsHost", 800, 924, 581, 142);
            var st = _svc.Content.News.State;
            if (st == LoadState.Idle || st == LoadState.Loading) { Ui.Label(_newsHost, "Loading news...", "SemiBold", 20, Muted, 24, 76, 400, 28); return; }
            if (st == LoadState.Unavailable)
            {
                Ui.Icon(_newsHost, "ic_news", new Color(1, 1, 1, 0.22f), 24, 70, 48); Ui.Label(_newsHost, "News is unavailable right now.", "SemiBold", 20, Brand.Bad, 86, 66, 440, 28);
                Ui.Label(_newsHost, "Tap to try again.", "Regular", 16, new Color(0.55f, 0.58f, 0.64f), 86, 96, 400, 22);
                Ui.Click(_newsHost, "Btn_RetryNews", 0, 0, 581, 142, () => { _svc.Content.News.Reset(); _ = LoadNewsContent(); }); return;
            }
            if (_newsItems.Length == 0) { Ui.Icon(_newsHost, "ic_news", new Color(1, 1, 1, 0.22f), 24, 70, 48); Ui.Label(_newsHost, "No news right now.", "SemiBold", 20, Muted, 86, 76, 400, 28); return; }
            _newsIndex = Mathf.Clamp(_newsIndex, 0, _newsItems.Length - 1);
            ShowNews(_newsIndex);
            var nextBg = Ui.Rounded(_stage, "NewsNext", Gold, 1328, 1010, 40, 40, 20); Ui.Icon(_stage, "ic_chevron_right", Ink, 1336, 1018, 24);
            Ui.Click(_stage, "Btn_NewsNext", 1320, 1002, 56, 56, () => { NextNews(); _nextNews = Time.unscaledTime + 12f; });
            if (_newsItems.Length > 1) _newsCounter = Ui.Label(_stage, "1 / " + _newsItems.Length, "SemiBold", 14, Muted, 1290, 942, 70, 20, TextAnchor.UpperRight);
        }

        void NextNews() { if (_newsItems.Length == 0 || _newsHost == null) return; _newsIndex = (_newsIndex + 1) % _newsItems.Length; ShowNews(_newsIndex); Debug.Log($"[Dashboard] news index={_newsIndex} id={_newsItems[_newsIndex].id}"); }

        void ShowNews(int index)
        {
            for (int i = _newsHost.childCount - 1; i >= 0; i--) Destroy(_newsHost.GetChild(i).gameObject);
            var n = _newsItems[index]; var scene = Brand.Scene(); var model = WorldMapScreen.Model(); var map = WorldMapScreen.Artwork(model);
            Texture tex = scene; Rect uv = Ui.Cover(scene, 0.45f, 0.50f, 0.35f, 150f / 76f);
            switch (n.image_key)
            {
                case "map": if (map != null) { tex = map; uv = Ui.Cover(map, 0.30f, 0.42f, 0.30f, 150f / 76f); } break;
                case "fuel": tex = Brand.Texture("job_tanker") ?? scene; uv = tex == scene ? uv : new Rect(0, 0, 1, 1); break;
                case "convoy": uv = Ui.Cover(scene, 0.40f, 0.50f, 0.45f, 150f / 76f); break;
            }
            var group = Ui.Box(_newsHost, "NewsItem", 0, 0, 581, 142); GroupFade.On(group).Play(0.45f, 16f);
            Ui.Photo(group, "NewsPhoto", tex, uv, 22, 54, 150, 76, 8);
            string kind = n.category ?? "update";
            Color kc = kind == "tip" ? Brand.Good : kind == "event" ? Blue : Gold;
            Ui.Rounded(group, "NewsKind", new Color(kc.r * 0.3f, kc.g * 0.3f, kc.b * 0.3f, 0.95f), 190, 56, 70, 20, 6); Ui.Label(group, kind.ToUpperInvariant(), "Bold", 12, kc, 190, 58, 70, 18, TextAnchor.UpperCenter);
            var title = Ui.Label(group, n.title, "SemiBold", 19, White, 190, 80, 370, 26); title.resizeTextForBestFit = true; title.resizeTextMinSize = 14; title.resizeTextMaxSize = 19;
            Ui.Paragraph(group, n.body, "Regular", 15, new Color(0.84f, 0.86f, 0.9f), 190, 104, 330, 40);
            string date = TimeText.TryParse(n.publish_at, out var d) ? d.ToString("MMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture) : "";
            Ui.Label(group, date, "Regular", 13, Muted, 22, 130, 150, 16);
            if (_newsCounter != null) _newsCounter.text = (index + 1) + " / " + _newsItems.Length;
        }

        void BuildConvoyCard()
        {
            Ui.Outlined(_stage, "ConvoyFrame", new Color(0, 0, 0, 1), new Color(1, 1, 1, 0.14f), 1397, 924, 502, 142, 18);
            var scene = Brand.Scene();
            Ui.Photo(_stage, "ConvoyPhoto", scene, Ui.Cover(scene, 0.40f, 0.50f, 0.50f, 499f / 139f), 1398.5f, 925.5f, 499, 139, 17, new Color(0.7f, 0.7f, 0.74f));
            Ui.Gradient(_stage, "ConvoyShade", new Color(0.02f, 0.025f, 0.04f, 0.9f), new Color(0.02f, 0.025f, 0.04f, 0.1f), true, 1399, 926, 498, 138);
            int members = NetworkVehicle.Players.Count; var cs = Sum != null ? Sum.convoy : null; bool inConvoy = members > 1 || (cs != null && cs.Exists) || (_svc.Convoys != null && !string.IsNullOrEmpty(_svc.Convoys.CurrentConvoyId));
            Ui.Rounded(_stage, "ConvoyTile", new Color(0.1f, 0.2f, 0.4f, 0.95f), 1420, 944, 60, 60, 14); Ui.Icon(_stage, "ic_people", Blue, 1432, 956, 36);
            if (inConvoy) { var d = Ui.Disc(_stage, "LiveDot", Brand.Good, 1496, 962, 12); PulseImage.On(d, 1.6f, 0.85f, 1.25f, 0.5f, 1f); Ui.Label(_stage, members > 1 ? $"{members} drivers on the road" : cs != null && cs.Exists ? $"{cs.name}  ·  {cs.member_count}/{cs.capacity}" : "In a convoy", "SemiBold", 17, Brand.Good, 1514, 957, 300, 24); }
            var t = Ui.Label(_stage, inConvoy ? "Your Convoy" : "Join a Convoy", "ExtraBold", 30, White, 1420, 1010, 360, 38); Ui.Drop(t);
            Ui.Label(_stage, inConvoy ? "Stay together. Share the road and the rewards." : "Drive together. Earn more. Make new friends.", "Regular", 17, new Color(0.9f, 0.92f, 0.95f), 1420, 1040, 420, 24);
            Ui.Rounded(_stage, "ConvoyGo", Gold, 1832, 1004, 46, 46, 23); Ui.Icon(_stage, "ic_chevron_right", Ink, 1841, 1013, 28);
            Ui.Click(_stage, "Btn_JoinConvoy", 1397, 924, 502, 142, OpenMultiplayer);
        }
    }
}
