using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ARO.Backend;
using ARO.NetCore;
using UnityEngine;
using UnityEngine.UI;

namespace ARO.Game
{
    /// <summary>
    /// The Friends page. Everything on it is what the backend says: the friend list (with presence: online, activity, route, last seen, and the vehicle a friend
    /// drives), incoming and sent requests, recent players, an exact-username lookup, and the actions (add, accept, decline, cancel, remove, block, invite to the convoy,
    /// view a public profile). Rules (filter, sort, activity text, which actions a card offers, which rows to build) are in <see cref="FriendsRules"/> and unit tested.
    /// Lists build only the rows near the window. Messaging does not exist in the game, so Message is shown disabled with the reason (docs/PLACEHOLDERS.md).
    /// </summary>
    public sealed partial class DashboardScreen
    {
        const float FrLX = 308, FrLY = 268, FrLW = 1090, FrLH = 790, FrRX = 1418, FrRW = 472;
        const float FrRowH = 96, FrPitch = 104;

        FriendsTab _frTab = FriendsTab.All; FriendSort _frSort = FriendSort.OnlineFirst;
        string _frSearch = "", _frSearchPending = ""; float _frSearchDue = -1f; InputField _frSearchField;
        bool _frSortPop, _frBusy, _frOpen; float _nextFr;
        // lookup
        string _frFindFor = ""; PersonCard _frFound; LoadState _frFindState = LoadState.Idle; string _frFindError;
        // list virtualisation
        ScrollArea _frArea; Action<RectTransform, int, int> _frRows; int _frFirst = -2, _frLast = -2, _frCount;
        // dialogs
        PersonCard _frDlgCard; PublicProfile _frProfile; LoadState _frProfState = LoadState.Idle; string _frProfError;
        static readonly Color FrGreen = new Color(0.14f, 0.55f, 0.28f), FrRed = new Color(0.62f, 0.14f, 0.14f), FrField = new Color(0.045f, 0.05f, 0.07f, 0.96f);

        public void OpenFriends() => ShowPage(Page.Friends);

        // ================================================================================================================ data
        PersonCard ToCard(PersonDto p)
        {
            if (p == null) return new PersonCard();
            var pr = p.presence; bool has = (pr != null && (pr.online || !string.IsNullOrEmpty(pr.last_seen_at))) || p.friend_status == "friends";
            return new PersonCard
            {
                Id = p.player_id ?? "", Name = p.display_name ?? "Driver", Username = p.username ?? "", Country = p.country_code ?? "", Status = string.IsNullOrEmpty(p.friend_status) ? "none" : p.friend_status, Level = p.level,
                Online = pr != null && pr.online, HasPresence = has, Activity = pr != null ? pr.activity ?? "" : "", Route = pr != null ? pr.route_code ?? "" : "", City = pr != null ? pr.city_slug ?? "" : "", LastSeen = pr != null ? pr.last_seen_at ?? "" : "",
                Vehicle = p.vehicle ?? "", VehicleId = p.vehicle_id ?? "", Since = p.since ?? "", LastPlayed = p.last_played_at ?? "", Times = p.times,
            };
        }

        List<PersonCard> FrFriends() { var v = _svc.Friends.Friends.Value; return v != null && v.items != null ? v.items.Select(ToCard).ToList() : new List<PersonCard>(); }
        List<PersonCard> FrRecent() { var v = _svc.Friends.Recent.Value; return v != null && v.items != null ? v.items.Select(ToCard).ToList() : new List<PersonCard>(); }
        List<PersonCard> FrIncoming() { var v = _svc.Friends.Requests.Value; return v != null && v.incoming != null ? v.incoming.Select(r => { var c = ToCard(r.person); c.RequestId = r.request_id ?? ""; c.LastPlayed = ""; c.Since = r.created_at ?? ""; return c; }).ToList() : new List<PersonCard>(); }
        List<PersonCard> FrOutgoing() { var v = _svc.Friends.Requests.Value; return v != null && v.outgoing != null ? v.outgoing.Select(r => { var c = ToCard(r.person); c.RequestId = r.request_id ?? ""; c.Since = r.created_at ?? ""; return c; }).ToList() : new List<PersonCard>(); }

        bool FrInConvoy() { var m = _svc.Convoys.Mine.Value; return m != null && m.convoy != null && m.convoy.Exists; }
        DateTimeOffset FrNow() { var f = _svc.Friends.Friends.Value; return f != null && TimeText.TryParse(f.server_time, out var t) ? t : DateTimeOffset.UtcNow; }
        string RouteName(string code) { var m = WorldMapScreen.Model(); var r = m != null ? m.Route(code) : null; return r != null && !string.IsNullOrEmpty(r.name) ? r.name : null; }
        string Activity(PersonCard c) => FriendsRules.ActivityText(c, FrNow(), RouteName);
        string VehiclePictureKey(string defId)
        {
            if (string.IsNullOrEmpty(defId)) return null; var l = _svc.Garage.Catalog.Value; var it = l != null && l.items != null ? l.items.FirstOrDefault(i => i.definition_id == defId) : null;
            return it != null && !string.IsNullOrEmpty(it.picture_key) ? it.picture_key : null;
        }
        /// <summary>A loaded list keeps showing its last answer while it refreshes in the background.</summary>
        static bool HasData<T>(Loadable<T> l) => l.State == LoadState.Ready || l.State == LoadState.Empty || (l.State == LoadState.Loading && l.Value != null);

        // ================================================================================================================ lifecycle
        void FriOpened()
        {
            _frOpen = true; _frSortPop = false; Debug.Log("[Friends] opened");
            LoadFriendsPage(false); _nextFr = Time.unscaledTime + FrPoll;
            if (_svc.Garage.Catalog.State == LoadState.Idle) _ = LoadCatalogForFriends();
            RefreshFriends();
        }
        void FriClosed() { _frOpen = false; _frSortPop = false; if (_modal != null) CloseModal(); Debug.Log("[Friends] closed"); }

        async Task LoadCatalogForFriends() { await _svc.Garage.LoadCatalog(); if (this != null && _page == Page.Friends) RefreshFriendsIfChanged(); }     // the vehicle pictures of friends come from the catalogue

        /// <summary>Seconds between background reloads of the Friends page: 15, or what a browser test page asks for ("frpoll=3") so a changed reload can be made to land at a chosen moment.</summary>
        static float _frPoll = -1f;
        static float FrPoll
        {
            get
            {
                if (_frPoll < 0f)
                {
                    _frPoll = 15f;
                    if (SmokeMode.E2E && float.TryParse(BackendOverride.Query(Application.absoluteURL, "frpoll"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 1f) _frPoll = v;
                }
                return _frPoll;
            }
        }

        void LoadFriendsPage(bool quiet) { _ = LoadFriendsList(quiet); _ = LoadFriendRequests(quiet); _ = LoadRecentList(quiet); _ = LoadMineForFriends(); }

        /// <summary>Invite Convoy is only possible from a convoy, so the page asks the server whether the player is in one.</summary>
        async Task LoadMineForFriends()
        {
            bool was = FrInConvoy(); await _svc.Convoys.LoadMine(); if (this == null) return;
            Debug.Log($"[Friends] convoy member={FrInConvoy().ToString().ToLowerInvariant()}");
            if (_page == Page.Friends && was != FrInConvoy()) RefreshFriends();
        }

        async Task LoadFriendsList(bool quiet)
        {
            var pending = _svc.Friends.LoadFriends(); if (!quiet) RefreshFriends(); var r = await pending; if (this == null) return;
            var l = FrFriends(); Debug.Log($"[Friends] friends state={_svc.Friends.Friends.State.ToString().ToLowerInvariant()} count={l.Count} online={FriendsRules.OnlineCount(l)}" + (r.Ok ? "" : " error=" + r.ErrorCode));
            if (_page == Page.Friends) RefreshFriendsIfChanged();
        }
        async Task LoadFriendRequests(bool quiet)
        {
            var pending = _svc.Friends.LoadRequests(); if (!quiet) RefreshFriends(); var r = await pending; if (this == null) return;
            Debug.Log($"[Friends] requests state={_svc.Friends.Requests.State.ToString().ToLowerInvariant()} incoming={FrIncoming().Count} outgoing={FrOutgoing().Count}" + (r.Ok ? "" : " error=" + r.ErrorCode));
            if (_page == Page.Friends) RefreshFriendsIfChanged();
        }
        async Task LoadRecentList(bool quiet)
        {
            var pending = _svc.Friends.LoadRecent(30); if (!quiet) RefreshFriends(); var r = await pending; if (this == null) return;
            Debug.Log($"[Friends] recent state={_svc.Friends.Recent.State.ToString().ToLowerInvariant()} count={FrRecent().Count}" + (r.Ok ? "" : " error=" + r.ErrorCode));
            if (_page == Page.Friends) RefreshFriendsIfChanged();
        }

        static readonly string[] FrPartNames = { "friends", "requests", "recent", "convoy", "catalogue", "relative times" };
        string[] _frLastParts;

        /// <summary>Everything the lists contribute to what is drawn: the data (without server clocks), the relative times as the cards would print them, the convoy flag and the catalogue pictures.</summary>
        string[] FrParts()
        {
            var now = FrNow(); var ago = new System.Text.StringBuilder();
            void Add(PersonDto p) { if (p == null) return; ago.Append(FriendsRules.AgoOf(p.since, now)).Append('/').Append(FriendsRules.AgoOf(p.last_played_at, now)).Append('/').Append(FriendsRules.AgoOf(p.presence != null ? p.presence.last_seen_at : null, now)).Append(';'); }
            var fl = _svc.Friends.Friends.Value; if (fl != null && fl.items != null) foreach (var p in fl.items) Add(p);
            var rl = _svc.Friends.Recent.Value; if (rl != null && rl.items != null) foreach (var p in rl.items) Add(p);
            var rq = _svc.Friends.Requests.Value; if (rq != null) { if (rq.incoming != null) foreach (var i in rq.incoming) { Add(i.person); ago.Append(FriendsRules.AgoOf(i.created_at, now)).Append(';'); } if (rq.outgoing != null) foreach (var i in rq.outgoing) { Add(i.person); ago.Append(FriendsRules.AgoOf(i.created_at, now)).Append(';'); } }
            return new[] { Sig(_svc.Friends.Friends), Sig(_svc.Friends.Requests), Sig(_svc.Friends.Recent), FrInConvoy().ToString(), Sig(_svc.Garage.Catalog), ago.ToString() };
        }

        /// <summary>E2E mode only: which part of what the page shows changed, so a redraw that lands on a click can be explained.</summary>
        void LogFriendsChange(string[] now)
        {
            var was = _frLastParts; _frLastParts = now; if (!SmokeMode.E2E) return;
            for (int i = 0; i < now.Length; i++)
            {
                string a = was != null && i < was.Length ? was[i] : null, b = now[i];
                if (a == b) continue;
                int d = 0; if (a != null && b != null) while (d < a.Length && d < b.Length && a[d] == b[d]) d++;
                string Cut(string v) => v == null ? "(unknown)" : v.Substring(System.Math.Max(0, d - 30), System.Math.Min(v.Length - System.Math.Max(0, d - 30), 90));
                Debug.Log($"[UIDiag] friends reload: {FrPartNames[i]} changed, rebuilding | was ...{Cut(a)} | now ...{Cut(b)}");
            }
        }

        /// <summary>A background reload finished: redraw only if it brought something the screen does not already show.</summary>
        void RefreshFriendsIfChanged()
        {
            if (!_frOpen && _page != Page.Friends) return;
            var parts = FrParts();
            if (!_gate.Changed("friends", RefreshGate.Key(parts))) { if (SmokeMode.E2E) Debug.Log("[UIDiag] friends reload: nothing changed, no rebuild"); return; }
            LogFriendsChange(parts);
            RefreshFriends();
        }

        void RefreshFriends([System.Runtime.CompilerServices.CallerMemberName] string why = "") { if (!_frOpen && _page != Page.Friends) return; if (SmokeMode.E2E) Debug.Log($"[UIDiag] friends redraw requested by {why} t={Time.realtimeSinceStartup:0.00}"); var parts = FrParts(); _frLastParts = parts; _gate.Drew("friends", RefreshGate.Key(parts)); Refill("FriTabs", BuildFriTabs); Refill("FriMain", BuildFriMain); Refill("FriSide", BuildFriSide); Refill("FriPop", BuildFriPop); }

        // ================================================================================================================ build
        void BuildFriendsPage()
        {
            Region("FriTop", 0.02f); BuildFriTop();
            Region("FriTabs", 0.05f); BuildFriTabs();
            Region("FriMain", 0.08f); BuildFriMain();
            Region("FriSide", 0.12f); BuildFriSide();
            Region("FriPop", 0.30f); BuildFriPop();
        }

        void BuildFriTop()
        {
            Ui.Icon(_stage, "ic_people", Gold, 322, 138, 56);
            var t = Ui.Label(_stage, "Friends", "ExtraBold", 52, White, 396, 128, 600, 64); Ui.Drop(t, 0.5f);
            Ui.Label(_stage, "Connect with other drivers, build your network and explore Africa together.", "Regular", 20, new Color(0.84f, 0.86f, 0.9f), 396, 186, 860, 28);
            // search (built once: it keeps the typed text and the focus)
            const float qx = 1262, qy = 140, qw = 428, qh = 54;
            Ui.Outlined(_stage, "FriSearchBg", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.16f), qx, qy, qw, qh, 14, 1.3f);
            var c = new Color(0.82f, 0.84f, 0.88f);
            Ui.Disc(_stage, "SearchRing", c, qx + 16, qy + 15, 19); Ui.Disc(_stage, "SearchHole", new Color(0.05f, 0.055f, 0.075f, 1f), qx + 18, qy + 17, 15); Ui.Segment(_stage, new Vector2(qx + 32, qy + 31), new Vector2(qx + 40, qy + 39), 3, c);
            var areaGo = new GameObject("FriSearchArea", typeof(RectTransform), typeof(RectMask2D)); var area = Ui.Place(areaGo, _stage, Ui.Anchor.TopLeft, qx + 52, qy, qw - 66, qh);
            var txt = Ui.Label(area, "", "Regular", 19, White, 0, 0, qw - 66, qh, TextAnchor.MiddleLeft);
            var ph = Ui.Label(area, "Search friends, or a username...", "Regular", 19, new Color(1, 1, 1, 0.5f), 0, 0, qw - 66, qh, TextAnchor.MiddleLeft);
            var hit = areaGo.AddComponent<Image>(); hit.color = new Color(1, 1, 1, 0.001f); areaGo.name = "Fri_Search_Input";
            var f = areaGo.AddComponent<InputField>(); f.targetGraphic = hit; f.textComponent = txt; f.placeholder = ph; f.lineType = InputField.LineType.SingleLine; f.characterLimit = 40; f.text = _frSearch; f.transition = Selectable.Transition.None;
            f.customCaretColor = true; f.caretColor = Gold; f.selectionColor = new Color(Gold.r, Gold.g, Gold.b, 0.35f);
            f.onValueChanged.AddListener(v => { _frSearchPending = v ?? ""; _frSearchDue = Time.unscaledTime + 0.3f; });
            _frSearchField = f;
            // add friend
            Ui.Rounded(_stage, "FriAddBg", Gold, 1706, qy, 184, qh, 14); Ui.Icon(_stage, "ic_people", Ink, 1724, qy + 14, 26); Ui.Label(_stage, "Add Friend", "Bold", 20, Ink, 1756, qy + 13, 130, 28);
            Ui.Click(_stage, "Btn_FriAdd", 1706, qy, 184, qh, () => { SetFriTab(FriendsTab.Find); if (_frSearchField != null) _frSearchField.ActivateInputField(); });
        }

        static readonly (FriendsTab t, string label, string icon)[] FriTabs =
        {
            (FriendsTab.All, "All Friends", "ic_people"), (FriendsTab.Online, "Online", "ic_check_circle"), (FriendsTab.Requests, "Requests", "ic_bell"), (FriendsTab.Recent, "Recent Players", "ic_clock"), (FriendsTab.Find, "Find Players", "ic_pin"),
        };

        void BuildFriTabs()
        {
            float x = 308; var friends = FrFriends(); int incoming = FrIncoming().Count;
            foreach (var tab in FriTabs)
            {
                bool on = tab.t == _frTab; int? n = tab.t == FriendsTab.All ? (int?)friends.Count : tab.t == FriendsTab.Online ? FriendsRules.OnlineCount(friends) : tab.t == FriendsTab.Requests ? incoming : tab.t == FriendsTab.Recent ? (int?)FrRecent().Count : null;
                if (tab.t == FriendsTab.All && !HasData(_svc.Friends.Friends)) n = null;
                var probe = Ui.Label(_stage, tab.label, on ? "Bold" : "SemiBold", 19, White, 0, 0, 10, 10); float tw = probe.preferredWidth; Destroy(probe.gameObject);
                bool chip = n.HasValue && (n.Value > 0 || tab.t == FriendsTab.All); float cw = chip ? 14 + n.Value.ToString().Length * 11 + 12 : 0;
                float w = 16 + 26 + 10 + tw + (chip ? 10 + cw : 0) + 16, y = 207, h = 46;
                if (on) Ui.Rounded(_stage, "TabOn", Gold, x, y, w, h, 12); else Ui.Outlined(_stage, "Tab", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.14f), x, y, w, h, 12);
                Ui.Icon(_stage, tab.icon, on ? Ink : new Color(0.86f, 0.88f, 0.92f), x + 14, y + 10, 26); Ui.Label(_stage, tab.label, on ? "Bold" : "SemiBold", 19, on ? Ink : White, x + 48, y + 9, tw + 6, 28);
                if (chip)
                {
                    bool red = tab.t == FriendsTab.Requests && n.Value > 0; float cx = x + 48 + tw + 10;
                    Ui.Rounded(_stage, "CountChip", red ? new Color(0.9f, 0.2f, 0.2f) : on ? new Color(0.07f, 0.065f, 0.06f, 0.28f) : new Color(1, 1, 1, 0.14f), cx, y + 11, cw, 24, 12);
                    Ui.Label(_stage, n.Value.ToString(), "Bold", 15, red ? White : on ? Ink : White, cx, y + 13, cw, 20, TextAnchor.UpperCenter);
                }
                var tt = tab.t; Ui.Click(_stage, "Btn_FriTab_" + tt, x, y, w, h, () => SetFriTab(tt)); x += w + 10;
            }
            int online = FriendsRules.OnlineCount(friends);
            Ui.Outlined(_stage, "OnlineChip", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.10f), x + 14, 207, 230, 46, 12, 1.2f);
            Ui.Disc(_stage, "OnlineDot", online > 0 ? Brand.Good : Muted, x + 32, 224, 12); Ui.Label(_stage, HasData(_svc.Friends.Friends) ? (online == 1 ? "1 friend online" : online + " friends online") : "Checking who is online...", "SemiBold", 17, White, x + 52, 217, 190, 26);
        }

        void SetFriTab(FriendsTab t)
        {
            if (_frTab == t && !(t == FriendsTab.Find)) return;
            bool changed = _frTab != t; _frTab = t; _frSortPop = false; _frFirst = _frLast = -2; _scrollPos.Remove("fri_list");
            if (changed) Debug.Log($"[Friends] tab={t.ToString().ToLowerInvariant()}");
            if (t == FriendsTab.Find && changed) { _frFound = null; _frFindState = LoadState.Idle; _frFindError = null; _frFindFor = ""; }
            RefreshFriends();
        }

        // ---------------------------------------------------------------- the main panel
        void BuildFriMain()
        {
            _frArea = null; _frRows = null; _frFirst = _frLast = -2;
            Ui.Outlined(_stage, "FriPanel", PanelFill, PanelLine, FrLX, FrLY, FrLW, FrLH, 22, 1.5f);
            switch (_frTab)
            {
                case FriendsTab.All: case FriendsTab.Online: BuildFriendList(false); break;
                case FriendsTab.Recent: BuildFriendList(true); break;
                case FriendsTab.Requests: BuildRequestsTab(); break;
                case FriendsTab.Find: BuildFindTab(); break;
            }
        }

        void FriHeader(string title, int? shown, int total)
        {
            Ui.Label(_stage, title, "ExtraBold", 28, White, FrLX + 24, FrLY + 16, 520, 40);
            if (shown.HasValue)
            {
                var probe = Ui.Label(_stage, title, "ExtraBold", 28, White, 0, 0, 10, 10); float tw = probe.preferredWidth; Destroy(probe.gameObject);
                string ct = FriendsRules.CountText(shown.Value, total); float cw = 22 + ct.Length * 10;
                Ui.Rounded(_stage, "HdrCount", new Color(1, 1, 1, 0.12f), FrLX + 24 + tw + 12, FrLY + 22, cw, 28, 14); Ui.Label(_stage, ct, "Bold", 16, White, FrLX + 24 + tw + 12, FrLY + 25, cw, 22, TextAnchor.UpperCenter);
            }
        }

        void BuildFriendList(bool recent)
        {
            var src = _frTab == FriendsTab.Recent ? FrRecent() : FrFriends(); var ld = recent ? _svc.Friends.Recent : _svc.Friends.Friends;
            string title = recent ? "Recent Players" : _frTab == FriendsTab.Online ? "Online Friends" : "My Friends";
            var cards = FriendsRules.Filter(src, _frTab, _frSearch); if (!recent) cards = FriendsRules.Sort(cards, _frSort);
            bool data = HasData(ld);
            FriHeader(title, data ? (int?)cards.Count : null, _frTab == FriendsTab.Online ? FriendsRules.OnlineCount(src) : src.Count);
            if (!recent)
            {   // sort
                const float sw = 236; float sx = FrLX + FrLW - sw - 24, sy = FrLY + 16;
                Ui.Outlined(_stage, "SortBg", FrField, new Color(1, 1, 1, _frSortPop ? 0.6f : 0.2f), sx, sy, sw, 42, 11, 1.3f);
                Ui.Label(_stage, "Sort: " + FriendsRules.SortLabel(_frSort), "SemiBold", 17, White, sx + 14, sy + 8, sw - 46, 26); Ui.Icon(_stage, "ic_chevron_down", Soft, sx + sw - 32, sy + 12, 18);
                Ui.Click(_stage, "Btn_FriSort", sx, sy, sw, 42, () => { _frSortPop = !_frSortPop; Refill("FriPop", BuildFriPop); });
            }
            Ui.Flat(_stage, "Div", new Color(1, 1, 1, 0.06f), FrLX + 20, FrLY + 66, FrLW - 40, 1.5f);
            if (!data) { if (ld.State == LoadState.Unavailable) FriError(ld.Error, recent ? "Btn_FriRetryRecent" : "Btn_FriRetryFriends", () => { if (recent) _ = LoadRecentList(false); else _ = LoadFriendsList(false); }, recent ? "Could not load your recent players." : "Could not load your friends."); else FriLoading(recent ? "Loading recent players..." : "Loading your friends..."); return; }
            if (cards.Count == 0)
            {
                if (_frSearch.Trim().Length > 0) FriEmpty("ic_people", "No one matches \"" + _frSearch.Trim() + "\".", "Check the spelling, or press Enter to look up that exact username.", "Find this username", () => { _ = FindPlayer(_frSearch); });
                else if (recent) FriEmpty("ic_clock", "No recent players yet.", "Players you drive with in a convoy appear here, so you can add them as friends.", null, null);
                else if (_frTab == FriendsTab.Online) FriEmpty("ic_people", "None of your friends are online.", src.Count == 0 ? "Add friends to see when they are on the road." : "They will appear here when they come online. This list refreshes by itself.", null, null);
                else FriEmpty("ic_people", "You have no friends yet.", "Find a driver by username, or add players you met in a convoy.", "Find Players", () => SetFriTab(FriendsTab.Find));
                return;
            }
            var now = FrNow(); bool inConvoy = FrInConvoy(); _frCount = cards.Count;
            _frArea = MakeScroll("fri_list", FrLX + 10, FrLY + 76, FrLW - 20, FrLH - 88, cards.Count * FrPitch + 4);
            _frRows = (host, first, last) => { for (int i = first; i <= last; i++) FriendRow(host, cards[i], i * FrPitch + 2, FrLW - 44, now, inConvoy, recent); };
            RestoreScroll(_frArea); FriRebuildRows(true);
        }

        void FriRebuildRows(bool force)
        {
            if (_frArea == null || _frArea.Content == null || _frRows == null) return;
            FriendsRules.VisibleRange(_frArea.Pos, _frArea.Height, FrPitch, _frCount, 3, out int first, out int last);
            if (!force && first == _frFirst && last == _frLast) return;
            _frFirst = first; _frLast = last; var c = _frArea.Content;
            for (int i = c.childCount - 1; i >= 0; i--) Destroy(c.GetChild(i).gameObject);
            if (first >= 0) _frRows(c, first, last);
            MarkTargetsDirty();
        }

        void FriLoading(string text) { Ui.Label(_stage, text, "SemiBold", 21, Muted, FrLX, FrLY + 300, FrLW, 30, TextAnchor.UpperCenter); }
        void FriError(string detail, string retryName, Action retry, string title)
        {
            Ui.Label(_stage, title, "Bold", 24, Brand.Bad, FrLX + 40, FrLY + 230, FrLW - 80, 34, TextAnchor.UpperCenter);
            Ui.Paragraph(_stage, string.IsNullOrEmpty(detail) ? "Please try again in a moment." : detail, "Regular", 18, Soft, FrLX + 160, FrLY + 272, FrLW - 320, 60, TextAnchor.UpperCenter);
            Ui.Rounded(_stage, "RetryBg", Gold, FrLX + FrLW / 2 - 90, FrLY + 342, 180, 50, 13); Ui.Label(_stage, "Try again", "Bold", 20, Ink, FrLX + FrLW / 2 - 90, FrLY + 353, 180, 28, TextAnchor.UpperCenter); Ui.Click(_stage, retryName, FrLX + FrLW / 2 - 90, FrLY + 342, 180, 50, retry);
        }
        void FriEmpty(string icon, string title, string text, string cta, Action go)
        {
            Ui.Icon(_stage, icon, new Color(1, 1, 1, 0.18f), FrLX + FrLW / 2 - 40, FrLY + 190, 80);
            Ui.Label(_stage, title, "Bold", 26, White, FrLX + 40, FrLY + 290, FrLW - 80, 36, TextAnchor.UpperCenter);
            Ui.Paragraph(_stage, text, "Regular", 18, Soft, FrLX + 200, FrLY + 334, FrLW - 400, 60, TextAnchor.UpperCenter);
            if (cta != null) { Ui.Rounded(_stage, "CtaBg", Gold, FrLX + FrLW / 2 - 120, FrLY + 410, 240, 50, 13); Ui.Label(_stage, cta, "Bold", 20, Ink, FrLX + FrLW / 2 - 120, FrLY + 421, 240, 28, TextAnchor.UpperCenter); Ui.Click(_stage, "Btn_FriEmptyFind", FrLX + FrLW / 2 - 120, FrLY + 410, 240, 50, go); }
        }

        // ---------------------------------------------------------------- avatars and chips
        void FriAvatar(Transform host, PersonCard c, float x, float y, float size, bool dot)
        {
            Ui.Disc(host, "Avatar", Color.HSVToRGB(FriendsRules.AvatarHue(c.Id) / 360f, 0.42f, 0.52f), x, y, size);
            Ui.Label(host, FriendsRules.Initials(c.Name), "ExtraBold", Mathf.RoundToInt(size * 0.36f), White, x, y, size, size, TextAnchor.MiddleCenter);
            if (dot && c.HasPresence) { float d = size * 0.30f; Ui.Disc(host, "DotRing", new Color(0.062f, 0.070f, 0.092f, 1f), x + size - d - 1, y + size - d - 1, d + 2); Ui.Disc(host, "Dot", c.Online ? Brand.Good : Muted, x + size - d + 1, y + size - d + 1, d - 2); }
        }

        void FriButton(Transform host, string name, float x, float y, float w, float h, string label, Color bg, Color fg, Action click, bool enabled = true, string icon = null, int size = 16, string disabledNote = null)
        {
            var bgc = enabled ? bg : new Color(bg.r * 0.55f, bg.g * 0.55f, bg.b * 0.55f, 0.55f);
            if (bg == Color.clear) Ui.Outlined(host, "FBtn", enabled ? new Color(0.05f, 0.055f, 0.075f, 0.9f) : new Color(0.05f, 0.055f, 0.075f, 0.5f), new Color(1, 1, 1, enabled ? 0.28f : 0.10f), x, y, w, h, 11, 1.3f); else Ui.Rounded(host, "FBtn", bgc, x, y, w, h, 11);
            Color txt = enabled ? fg : new Color(fg.r, fg.g, fg.b, 0.45f); float ix = 0;
            if (icon != null) { var probe = Ui.Label(host, label, "Bold", size, White, 0, 0, 10, 10); float tw = probe.preferredWidth; Destroy(probe.gameObject); ix = x + (w - tw - 26) / 2; Ui.Icon(host, icon, txt, ix, y + (h - 20) / 2, 20); Ui.Label(host, label, "Bold", size, txt, ix + 26, y + (h - size - 6) / 2, tw + 8, size + 6, TextAnchor.UpperLeft); }
            else Ui.Label(host, label, "Bold", size, txt, x, y + (h - size - 6) / 2, w, size + 6, TextAnchor.UpperCenter);
            Ui.Click(host, name, x, y, w, h, click);
        }

        // ---------------------------------------------------------------- one row of a friends / recent list
        void FriendRow(RectTransform host, PersonCard c, float y, float w, DateTimeOffset now, bool inConvoy, bool recent)
        {
            Ui.Outlined(host, "FriRow", CardFill, new Color(1, 1, 1, 0.10f), 2, y, w, FrRowH, 16, 1.3f);
            FriAvatar(host, c, 18, y + 16, 64, true);
            var nm = Ui.Label(host, c.Name, "Bold", 23, White, 100, y + 12, 250, 30); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 14; nm.resizeTextMaxSize = 23; nm.horizontalOverflow = HorizontalWrapMode.Wrap; nm.verticalOverflow = VerticalWrapMode.Truncate;
            Ui.TextWithCountry(host, "Level " + c.Level, c.Country, "Regular", 16, new Color(0.78f, 0.8f, 0.86f), 100, y + 42, 250, 22);
            if (c.Relation == Relation.Friends && c.HasPresence) { Ui.Disc(host, "SDot", c.Online ? Brand.Good : Muted, 100, y + 70, 11); Ui.Label(host, FriendsRules.StatusText(c), "SemiBold", 16, c.Online ? Brand.Good : Muted, 118, y + 65, 120, 22); }
            else { string chip = FriendsRules.RelationChip(c); if (chip.Length > 0) Ui.Label(host, chip, "SemiBold", 15, c.Relation == Relation.Friends ? Brand.Good : Gold, 100, y + 66, 220, 22); }
            Ui.Click(host, "Btn_FriProfile_" + c.Username, 2, y, 320, FrRowH, () => FriOpenProfile(c));
            // vehicle + activity
            string key = VehiclePictureKey(c.VehicleId); var tex = key != null ? Brand.Texture("veh_" + key) : null; float vx = 336;
            if (tex != null) Ui.Photo(host, "VehPic", tex, Ui.Cover(tex, 0f, 0f, 1f, 112f / 72f), vx, y + 12, 112, 72, 10);
            else if (!string.IsNullOrEmpty(c.Vehicle)) { Ui.Rounded(host, "VehPh", new Color(1, 1, 1, 0.06f), vx, y + 12, 112, 72, 10); Ui.Icon(host, "ic_car", new Color(1, 1, 1, 0.35f), vx + 36, y + 28, 40); }
            float tx = vx + 126, tw = 214;
            string act = recent ? FriendsRules.PlayedText(c, now) : Activity(c);
            if (!string.IsNullOrEmpty(c.Vehicle)) { Ui.Icon(host, "ic_car", Soft, tx, y + 18, 20); var vn = Ui.Label(host, c.Vehicle, "Bold", 17, White, tx + 28, y + 16, tw - 28, 24); vn.resizeTextForBestFit = true; vn.resizeTextMinSize = 12; vn.resizeTextMaxSize = 17; vn.horizontalOverflow = HorizontalWrapMode.Wrap; vn.verticalOverflow = VerticalWrapMode.Truncate; }
            if (act.Length > 0) { Ui.Icon(host, recent ? "ic_clock" : "ic_pin", new Color(Gold.r, Gold.g, Gold.b, 0.9f), tx, y + (string.IsNullOrEmpty(c.Vehicle) ? 34 : 54), 20); var al = Ui.Label(host, act, "Regular", 16, new Color(0.84f, 0.86f, 0.9f), tx + 28, y + (string.IsNullOrEmpty(c.Vehicle) ? 32 : 52), tw - 28, 24); al.resizeTextForBestFit = true; al.resizeTextMinSize = 11; al.resizeTextMaxSize = 16; al.horizontalOverflow = HorizontalWrapMode.Wrap; al.verticalOverflow = VerticalWrapMode.Truncate; }
            // actions, right aligned
            var acts = FriendsRules.RowActions(c, inConvoy); float rx = w - 14;
            for (int i = acts.Count - 1; i >= 0; i--)
            {
                var a = acts[i]; float bw = a.Action == FriendAction.More ? 48 : a.Action == FriendAction.Message ? 124 : a.Action == FriendAction.InviteConvoy ? 156 : a.Action == FriendAction.RequestSent ? 144 : a.Action == FriendAction.AddFriend ? 136 : 112; rx -= bw; float by = y + (FrRowH - 44) / 2;
                RowActionButton(host, c, a, rx, by, bw, 44); rx -= 10;
            }
        }

        /// <summary>One action button; a disabled one is dimmed and says why when tapped.</summary>
        void RowActionButton(Transform host, PersonCard c, ActionState a, float x, float y, float w, float h)
        {
            string n = c.Username; var cp = c;
            switch (a.Action)
            {
                case FriendAction.InviteConvoy: FriButton(host, "Btn_FriInvite_" + n, x, y, w, h, a.Label, Gold, Ink, () => { if (a.Enabled) _ = DoInvite(cp); else FriNote(a.Reason); }, a.Enabled, "ic_bus", 16); break;
                case FriendAction.Message: FriButton(host, "Btn_FriMessage_" + n, x, y, w, h, a.Label, Color.clear, White, () => FriNote(a.Reason), false, "ic_chat", 16); break;
                case FriendAction.More: Ui.Outlined(host, "MoreBg", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.28f), x, y, w, h, 11, 1.3f); Ui.Disc(host, "d", White, x + 12, y + 20, 5); Ui.Disc(host, "d", White, x + 21, y + 20, 5); Ui.Disc(host, "d", White, x + 30, y + 20, 5); Ui.Click(host, "Btn_FriMore_" + n, x, y, w, h, () => OpenPlayerMenu(cp)); break;
                case FriendAction.AddFriend: FriButton(host, "Btn_FriAddFriend_" + n, x, y, w, h, a.Label, Gold, Ink, () => _ = DoAdd(cp), true, "ic_people", 15); break;
                case FriendAction.RequestSent: FriButton(host, "Btn_FriSent_" + n, x, y, w, h, a.Label, Color.clear, White, () => FriNote(a.Reason), false, null, 15); break;
                case FriendAction.Accept: FriButton(host, "Btn_FriAccept_" + n, x, y, w, h, a.Label, FrGreen, White, () => _ = DoRespond(cp, true), true, null, 16); break;
                case FriendAction.Decline: FriButton(host, "Btn_FriDecline_" + n, x, y, w, h, a.Label, FrRed, White, () => _ = DoRespond(cp, false), true, null, 16); break;
            }
        }

        void FriNote(string text) { if (!string.IsNullOrEmpty(text)) { Debug.Log("[Friends] note: " + text); Toast(text, 4f); } }

        // ---------------------------------------------------------------- Requests tab
        void BuildRequestsTab()
        {
            var ld = _svc.Friends.Requests; bool data = HasData(ld);
            FriHeader("Friend Requests", null, 0); Ui.Flat(_stage, "Div", new Color(1, 1, 1, 0.06f), FrLX + 20, FrLY + 66, FrLW - 40, 1.5f);
            if (!data) { if (ld.State == LoadState.Unavailable) FriError(ld.Error, "Btn_FriRetryRequests", () => _ = LoadFriendRequests(false), "Could not load your requests."); else FriLoading("Loading your requests..."); return; }
            var inc = FriendsRules.Filter(FrIncoming(), FriendsTab.Requests, _frSearch); var outg = FriendsRules.Filter(FrOutgoing(), FriendsTab.Requests, _frSearch);
            if (inc.Count == 0 && outg.Count == 0) { FriEmpty("ic_bell", _frSearch.Trim().Length > 0 ? "No request matches \"" + _frSearch.Trim() + "\"." : "No pending requests.", "When someone asks to be your friend, it shows up here and in your notifications.", null, null); return; }
            var now = FrNow(); const float head = 44; int rows = inc.Count + outg.Count; float contentH = rows * FrPitch + (inc.Count > 0 ? head : 0) + (outg.Count > 0 ? head : 0) + 12;
            var area = MakeScroll("fri_list", FrLX + 10, FrLY + 76, FrLW - 20, FrLH - 88, contentH); var host = area.Content; float y = 2;
            Action<List<PersonCard>, string, bool> section = (list, title, incoming) =>
            {
                if (list.Count == 0) return; Ui.Label(host, title + " (" + list.Count + ")", "Bold", 20, new Color(0.9f, 0.92f, 0.96f), 14, y + 8, 500, 28); y += head;
                foreach (var c in list)
                {
                    Ui.Outlined(host, "FriRow", CardFill, new Color(1, 1, 1, 0.10f), 2, y, FrLW - 44, FrRowH, 16, 1.3f); FriAvatar(host, c, 18, y + 16, 64, false);
                    Ui.Label(host, c.Name, "Bold", 23, White, 100, y + 14, 330, 30); Ui.TextWithCountry(host, "Level " + c.Level, c.Country, "Regular", 16, new Color(0.78f, 0.8f, 0.86f), 100, y + 44, 330, 22);
                    Ui.Label(host, (incoming ? "Asked you " : "Sent ") + FriendsRules.AgoOf(c.Since, now), "Regular", 15, Muted, 100, y + 66, 330, 22);
                    Ui.Click(host, "Btn_FriProfile_" + c.Username, 2, y, 500, FrRowH, () => FriOpenProfile(c));
                    float rx = FrLW - 44 - 14; var cc = c;
                    if (incoming) { rx -= 112; FriButton(host, "Btn_FriDecline_" + c.Username, rx, y + 26, 112, 44, "Decline", FrRed, White, () => _ = DoRespond(cc, false)); rx -= 10; rx -= 112; FriButton(host, "Btn_FriAccept_" + c.Username, rx, y + 26, 112, 44, "Accept", FrGreen, White, () => _ = DoRespond(cc, true)); rx -= 10; rx -= 150; FriButton(host, "Btn_FriViewProfile_" + c.Username, rx, y + 26, 150, 44, "View Profile", Color.clear, White, () => FriOpenProfile(cc)); }
                    else { rx -= 170; FriButton(host, "Btn_FriCancel_" + c.Username, rx, y + 26, 170, 44, "Cancel Request", Color.clear, White, () => _ = DoCancel(cc)); rx -= 10; rx -= 150; FriButton(host, "Btn_FriViewProfile_" + c.Username, rx, y + 26, 150, 44, "View Profile", Color.clear, White, () => FriOpenProfile(cc)); }
                    y += FrPitch;
                }
            };
            section(inc, "Incoming", true); section(outg, "Sent", false); RestoreScroll(area);
        }

        // ---------------------------------------------------------------- Find Players tab
        void BuildFindTab()
        {
            FriHeader("Find Players", null, 0); Ui.Flat(_stage, "Div", new Color(1, 1, 1, 0.06f), FrLX + 20, FrLY + 66, FrLW - 40, 1.5f);
            Ui.Paragraph(_stage, "Type a player's username in the search box above and press Enter. Usernames are unique, like oga_driver. The server answers an exact username, so strangers cannot be browsed.", "Regular", 19, Soft, FrLX + 40, FrLY + 90, FrLW - 280, 70);
            FriButton(_stage, "Btn_FriFindGo", FrLX + FrLW - 220, FrLY + 94, 180, 50, "Search", Gold, Ink, () => _ = FindPlayer(_frSearchPending.Length > 0 ? _frSearchPending : _frSearch), true, null, 20);
            float y = FrLY + 190;
            if (_frFindState == LoadState.Idle) { Ui.Icon(_stage, "ic_pin", new Color(1, 1, 1, 0.16f), FrLX + FrLW / 2 - 40, y + 70, 80); Ui.Label(_stage, "Search for a player by username.", "SemiBold", 22, Muted, FrLX, y + 170, FrLW, 30, TextAnchor.UpperCenter); return; }
            if (_frFindState == LoadState.Loading) { Ui.Label(_stage, "Looking for \"" + _frFindFor + "\"...", "SemiBold", 22, Muted, FrLX, y + 100, FrLW, 30, TextAnchor.UpperCenter); return; }
            if (_frFindState == LoadState.Empty) { Ui.Icon(_stage, "ic_people", new Color(1, 1, 1, 0.16f), FrLX + FrLW / 2 - 40, y + 50, 80); Ui.Label(_stage, "No player with the username \"" + _frFindFor + "\".", "Bold", 24, White, FrLX + 40, y + 150, FrLW - 80, 34, TextAnchor.UpperCenter); Ui.Paragraph(_stage, "Check the spelling. A username has letters, numbers and underscores only.", "Regular", 18, Soft, FrLX + 200, y + 192, FrLW - 400, 50, TextAnchor.UpperCenter); return; }
            if (_frFindState == LoadState.Unavailable) { Ui.Label(_stage, "The search did not work.", "Bold", 24, Brand.Bad, FrLX + 40, y + 70, FrLW - 80, 34, TextAnchor.UpperCenter); Ui.Paragraph(_stage, _frFindError ?? "Please try again.", "Regular", 18, Soft, FrLX + 200, y + 112, FrLW - 400, 50, TextAnchor.UpperCenter); FriButton(_stage, "Btn_FriFindRetry", FrLX + FrLW / 2 - 90, y + 178, 180, 50, "Try again", Gold, Ink, () => _ = FindPlayer(_frFindFor), true, null, 20); return; }
            var c = _frFound; if (c == null) return; var now = FrNow();
            Ui.Label(_stage, "Result", "Bold", 18, Muted, FrLX + 30, y - 4, 200, 24);
            var host = Ui.Box(_stage, "FoundHost", FrLX + 20, y + 26, FrLW - 40, FrRowH + 4); FriendRow(host, c, 0, FrLW - 44, now, FrInConvoy(), true);
        }

        async Task FindPlayer(string raw)
        {
            string u = FriendsRules.NormaliseUsername(raw);
            if (_frTab != FriendsTab.Find) { _frTab = FriendsTab.Find; _frSortPop = false; }
            _frFindFor = u;
            if (u.Length == 0) { Toast("Type a username first."); RefreshFriends(); return; }
            if (!FriendsRules.IsUsernameShape(u)) { _frFound = null; _frFindState = LoadState.Empty; Debug.Log($"[Friends] find \"{u}\" result=not_a_username"); RefreshFriends(); return; }
            _frFindState = LoadState.Loading; RefreshFriends();
            var r = await _svc.Friends.Find(u); if (this == null) return;
            if (r.Ok) { _frFound = ToCard(r.Value); _frFindState = LoadState.Ready; Debug.Log($"[Friends] find \"{u}\" result=found status={_frFound.Status} level={_frFound.Level}"); }
            else if (r.ErrorCode == "player_not_found") { _frFound = null; _frFindState = LoadState.Empty; Debug.Log($"[Friends] find \"{u}\" result=not_found"); }
            else { _frFound = null; _frFindState = LoadState.Unavailable; _frFindError = r.UserMessage; Debug.Log($"[Friends] find \"{u}\" result=error code={r.ErrorCode}"); }
            RefreshFriends();
        }

        // ---------------------------------------------------------------- the side column
        void BuildFriSide()
        {
            BuildFriMe(); BuildFriRequestsPanel(); BuildFriRecentPanel();
        }

        void BuildFriMe()
        {
            const float y = FrLY, h = 240; var pr = _svc.Profile; var friends = FrFriends();
            Ui.Outlined(_stage, "MeCard", PanelFill, PanelLine, FrRX, y, FrRW, h, 22, 1.5f);
            var tex = Brand.Scene(); if (tex != null) Ui.Photo(_stage, "MePhoto", tex, Ui.Cover(tex, 0f, 0f, 1f, FrRW / 120f), FrRX + 2, y + 2, FrRW - 4, 120, 20, new Color(1, 1, 1, 0.55f));
            Ui.Gradient(_stage, "MeFade", new Color(0.062f, 0.07f, 0.092f, 0f), new Color(0.062f, 0.07f, 0.092f, 1f), false, FrRX + 2, y + 62, FrRW - 4, 62);
            if (pr == null) { Ui.Label(_stage, "Your profile is not loaded.", "SemiBold", 19, Muted, FrRX, y + 100, FrRW, 28, TextAnchor.UpperCenter); return; }
            var me = new PersonCard { Id = pr.id ?? "", Name = pr.display_name ?? "Driver", Username = Sum != null && Sum.profile != null ? Sum.profile.username ?? "" : "", Level = pr.level, Online = true, HasPresence = true, Status = "self" };
            FriAvatar(_stage, me, FrRX + 22, y + 22, 84, true);
            var nm = Ui.Label(_stage, me.Name, "ExtraBold", 28, White, FrRX + 122, y + 26, FrRW - 146, 38); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 16; nm.resizeTextMaxSize = 28; nm.horizontalOverflow = HorizontalWrapMode.Wrap; nm.verticalOverflow = VerticalWrapMode.Truncate; Ui.Drop(nm, 0.6f);
            Ui.Label(_stage, "Level " + pr.level, "SemiBold", 18, Soft, FrRX + 122, y + 66, 200, 24);
            Ui.Rounded(_stage, "OnChip", new Color(0.1f, 0.3f, 0.16f, 0.9f), FrRX + 122, y + 94, 92, 26, 13); Ui.Disc(_stage, "d", Brand.Good, FrRX + 132, y + 101, 12); Ui.Label(_stage, "Online", "Bold", 15, Brand.Good, FrRX + 150, y + 97, 60, 22);
            var stats = new[] { (HasData(_svc.Friends.Friends) ? friends.Count.ToString() : "-", "Friends", "ic_people"), (HasData(_svc.Friends.Friends) ? FriendsRules.OnlineCount(friends).ToString() : "-", "Online", "ic_check_circle"), (pr.jobs_completed.ToString("N0"), "Jobs done", "ic_jobs"), (pr.distance_km.ToString("N0") + " km", "Driven", "ic_road") };
            float sw = (FrRW - 24) / 4f;
            for (int i = 0; i < 4; i++)
            {
                float sx = FrRX + 12 + i * sw; Ui.Icon(_stage, stats[i].Item3, Gold, sx + sw / 2 - 11, y + 134, 22);
                Ui.Label(_stage, stats[i].Item1, "ExtraBold", 20, White, sx, y + 158, sw, 26, TextAnchor.UpperCenter); Ui.Label(_stage, stats[i].Item2, "Regular", 13, Muted, sx, y + 184, sw, 18, TextAnchor.UpperCenter);
            }
            FriButton(_stage, "Btn_FriMeProfile", FrRX + 18, y + h - 52, FrRW - 36, 40, "View Profile", Gold, Ink, () => FriOpenProfile(me), true, null, 18);
        }

        void BuildFriRequestsPanel()
        {
            const float y = FrLY + 252, h = 270; var ld = _svc.Friends.Requests; var inc = FrIncoming();
            Ui.Outlined(_stage, "ReqPanel", PanelFill, PanelLine, FrRX, y, FrRW, h, 22, 1.5f);
            Ui.Label(_stage, "Friend Requests", "ExtraBold", 24, White, FrRX + 20, y + 14, 260, 34);
            if (HasData(ld)) { string ct = inc.Count.ToString(); float cw = 22 + ct.Length * 10; Ui.Rounded(_stage, "ReqCount", inc.Count > 0 ? new Color(0.9f, 0.2f, 0.2f) : new Color(1, 1, 1, 0.12f), FrRX + 240, y + 19, cw, 26, 13); Ui.Label(_stage, ct, "Bold", 15, White, FrRX + 240, y + 21, cw, 22, TextAnchor.UpperCenter); }
            Ui.Label(_stage, "View All  >", "SemiBold", 15, Soft, FrRX + FrRW - 110, y + 20, 90, 22, TextAnchor.UpperRight); Ui.Click(_stage, "Btn_FriSideViewRequests", FrRX + FrRW - 120, y + 12, 110, 36, () => SetFriTab(FriendsTab.Requests));
            Ui.Flat(_stage, "Div", new Color(1, 1, 1, 0.06f), FrRX + 16, y + 54, FrRW - 32, 1.5f);
            if (!HasData(ld)) { if (ld.State == LoadState.Unavailable) { Ui.Label(_stage, "Requests could not load.", "SemiBold", 17, Brand.Bad, FrRX + 20, y + 100, FrRW - 40, 24); FriButton(_stage, "Btn_FriSideRetryRequests", FrRX + 20, y + 138, 130, 38, "Try again", Gold, Ink, () => _ = LoadFriendRequests(false)); } else Ui.Label(_stage, "Loading...", "SemiBold", 17, Muted, FrRX, y + 110, FrRW, 24, TextAnchor.UpperCenter); return; }
            if (inc.Count == 0) { Ui.Label(_stage, "No friend requests.", "SemiBold", 18, Muted, FrRX, y + 100, FrRW, 26, TextAnchor.UpperCenter); Ui.Label(_stage, "New ones appear here.", "Regular", 15, Muted, FrRX, y + 128, FrRW, 22, TextAnchor.UpperCenter); return; }
            for (int i = 0; i < Math.Min(3, inc.Count); i++)
            {
                var c = inc[i]; float ry = y + 62 + i * 68;
                FriAvatar(_stage, c, FrRX + 16, ry + 8, 48, false);
                var nm = Ui.Label(_stage, c.Name, "Bold", 18, White, FrRX + 74, ry + 8, 150, 24); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 12; nm.resizeTextMaxSize = 18; nm.horizontalOverflow = HorizontalWrapMode.Wrap; nm.verticalOverflow = VerticalWrapMode.Truncate;
                Ui.Label(_stage, "Level " + c.Level, "Regular", 14, Muted, FrRX + 74, ry + 34, 150, 20);
                Ui.Click(_stage, "Btn_FriSideProfile_" + c.Username, FrRX + 10, ry, 220, 60, () => FriOpenProfile(c));
                var cc = c; FriButton(_stage, "Btn_FriSideDecline_" + c.Username, FrRX + FrRW - 16 - 84, ry + 12, 84, 38, "Decline", FrRed, White, () => _ = DoRespond(cc, false), true, null, 15);
                FriButton(_stage, "Btn_FriSideAccept_" + c.Username, FrRX + FrRW - 16 - 84 - 8 - 84, ry + 12, 84, 38, "Accept", FrGreen, White, () => _ = DoRespond(cc, true), true, null, 15);
            }
            if (inc.Count > 3) Ui.Label(_stage, "+" + (inc.Count - 3) + " more", "SemiBold", 14, Muted, FrRX + 20, y + h - 28, 200, 20);
        }

        void BuildFriRecentPanel()
        {
            const float y = FrLY + 534, h = FrLH - 534; var ld = _svc.Friends.Recent; var rec = FrRecent(); var now = FrNow();
            Ui.Outlined(_stage, "RecPanel", PanelFill, PanelLine, FrRX, y, FrRW, h, 22, 1.5f);
            Ui.Label(_stage, "Recent Players", "ExtraBold", 24, White, FrRX + 20, y + 14, 260, 34);
            if (HasData(ld)) { string ct = rec.Count.ToString(); float cw = 22 + ct.Length * 10; Ui.Rounded(_stage, "RecCount", new Color(1, 1, 1, 0.12f), FrRX + 232, y + 19, cw, 26, 13); Ui.Label(_stage, ct, "Bold", 15, White, FrRX + 232, y + 21, cw, 22, TextAnchor.UpperCenter); }
            Ui.Label(_stage, "View All  >", "SemiBold", 15, Soft, FrRX + FrRW - 110, y + 20, 90, 22, TextAnchor.UpperRight); Ui.Click(_stage, "Btn_FriSideViewRecent", FrRX + FrRW - 120, y + 12, 110, 36, () => SetFriTab(FriendsTab.Recent));
            Ui.Flat(_stage, "Div", new Color(1, 1, 1, 0.06f), FrRX + 16, y + 54, FrRW - 32, 1.5f);
            if (!HasData(ld)) { if (ld.State == LoadState.Unavailable) { Ui.Label(_stage, "Recent players could not load.", "SemiBold", 17, Brand.Bad, FrRX + 20, y + 80, FrRW - 40, 24); FriButton(_stage, "Btn_FriSideRetryRecent", FrRX + 20, y + 116, 130, 38, "Try again", Gold, Ink, () => _ = LoadRecentList(false)); } else Ui.Label(_stage, "Loading...", "SemiBold", 17, Muted, FrRX, y + 90, FrRW, 24, TextAnchor.UpperCenter); return; }
            if (rec.Count == 0) { Ui.Label(_stage, "No recent players yet.", "SemiBold", 18, Muted, FrRX, y + 86, FrRW, 26, TextAnchor.UpperCenter); Ui.Paragraph(_stage, "Drive in a convoy to meet other drivers.", "Regular", 15, Muted, FrRX + 30, y + 114, FrRW - 60, 40, TextAnchor.UpperCenter); return; }
            int fit = Mathf.Max(1, Mathf.FloorToInt((h - 66) / 62f));
            for (int i = 0; i < Math.Min(fit, rec.Count); i++)
            {
                var c = rec[i]; float ry = y + 60 + i * 62; FriAvatar(_stage, c, FrRX + 16, ry + 5, 46, false);
                var nm = Ui.Label(_stage, c.Name, "Bold", 17, White, FrRX + 72, ry + 4, 160, 24); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 12; nm.resizeTextMaxSize = 17; nm.horizontalOverflow = HorizontalWrapMode.Wrap; nm.verticalOverflow = VerticalWrapMode.Truncate;
                var pl = Ui.Label(_stage, "Lvl " + c.Level + "  -  " + FriendsRules.AgoOf(c.LastPlayed, now), "Regular", 13, Muted, FrRX + 72, ry + 30, 170, 18); pl.resizeTextForBestFit = true; pl.resizeTextMinSize = 10; pl.resizeTextMaxSize = 13; pl.horizontalOverflow = HorizontalWrapMode.Wrap; pl.verticalOverflow = VerticalWrapMode.Truncate;
                Ui.Click(_stage, "Btn_FriSideRecentProfile_" + c.Username, FrRX + 10, ry, 230, 54, () => FriOpenProfile(c)); var cc = c;
                if (c.Relation == Relation.None) FriButton(_stage, "Btn_FriSideAdd_" + c.Username, FrRX + FrRW - 16 - 120, ry + 6, 120, 38, "Add Friend", Gold, Ink, () => _ = DoAdd(cc), true, null, 14);
                else Ui.Label(_stage, FriendsRules.RelationChip(c), "SemiBold", 14, c.Relation == Relation.Friends ? Brand.Good : Gold, FrRX + FrRW - 16 - 130, ry + 14, 130, 22, TextAnchor.UpperRight);
            }
        }

        // ---------------------------------------------------------------- popups
        void BuildFriPop()
        {
            if (!_frSortPop || _frTab == FriendsTab.Recent || _frTab == FriendsTab.Requests || _frTab == FriendsTab.Find) return;
            Ui.Click(_stage, "Btn_FriPopClose", 0, 0, 1920, 1080, () => { _frSortPop = false; Refill("FriMain", BuildFriMain); Refill("FriPop", BuildFriPop); }).GetComponent<HoverGlow>().enabled = false;
            const float sw = 236, ih = 40; float sx = FrLX + FrLW - sw - 24, sy = FrLY + 16 + 46, h = 4 * ih + 8;
            Ui.Outlined(_stage, "SortList", new Color(0.07f, 0.078f, 0.10f, 1f), new Color(Gold.r, Gold.g, Gold.b, 0.6f), sx, sy, sw, h, 11, 1.5f);
            int i = 0; foreach (FriendSort s in Enum.GetValues(typeof(FriendSort)))
            {
                float iy = sy + 4 + i * ih; bool on = s == _frSort; if (on) Ui.Rounded(_stage, "Cur", new Color(Gold.r, Gold.g, Gold.b, 0.22f), sx + 4, iy, sw - 8, ih - 2, 8);
                Ui.Label(_stage, FriendsRules.SortLabel(s), on ? "Bold" : "Regular", 17, on ? Gold : White, sx + 16, iy + 7, sw - 30, 26); var ss = s;
                Ui.Click(_stage, "Btn_FriSortOpt_" + s, sx + 4, iy, sw - 8, ih - 2, () => { _frSort = ss; _frSortPop = false; _scrollPos.Remove("fri_list"); Debug.Log("[Friends] sort=" + ss); RefreshFriends(); }); i++;
            }
        }

        // ---------------------------------------------------------------- dialogs
        void OpenPlayerMenu(PersonCard c)
        {
            var acts = FriendsRules.MenuActions(c); float h = 150 + acts.Count * 64; var m = OpenModal(c.Name, 560, h); float x0 = 960 - 280, y0 = 540 - h / 2;
            Ui.Label(m, "Level " + c.Level + (c.HasPresence ? "  -  " + FriendsRules.StatusText(c) : ""), "Regular", 18, Soft, x0 + 28, y0 + 64, 480, 26);
            for (int i = 0; i < acts.Count; i++)
            {
                var a = acts[i]; var cp = c; Color bg = a.Action == FriendAction.RemoveFriend || a.Action == FriendAction.BlockPlayer || a.Action == FriendAction.Decline ? FrRed : a.Action == FriendAction.Accept ? FrGreen : a.Action == FriendAction.ViewProfile ? new Color(1, 1, 1, 0.12f) : Gold;
                Color fg = bg == Gold ? Ink : White; Action act = null;
                switch (a.Action)
                {
                    case FriendAction.ViewProfile: act = () => FriOpenProfile(cp); break;
                    case FriendAction.AddFriend: act = () => { CloseModal(); _ = DoAdd(cp); }; break;
                    case FriendAction.Accept: act = () => { CloseModal(); _ = DoRespond(cp, true); }; break;
                    case FriendAction.Decline: act = () => { CloseModal(); _ = DoRespond(cp, false); }; break;
                    case FriendAction.CancelRequest: act = () => { CloseModal(); _ = DoCancel(cp); }; break;
                    case FriendAction.RemoveFriend: act = () => OpenConfirm(cp, "Remove " + cp.Name + " from your friends?", "You will not see where they are, and you will have to ask again to be friends.", "Remove Friend", () => _ = DoRemove(cp)); break;
                    case FriendAction.BlockPlayer: act = () => OpenConfirm(cp, "Block " + cp.Name + "?", "They cannot invite you or be invited by you, and their chat lines are hidden. You can unblock them in Settings > Gameplay.", "Block", () => _ = DoBlock(cp)); break;
                }
                ModalButton(m, "Btn_FriMenu_" + a.Action, x0 + 28, y0 + 104 + i * 64, 504, a.Label, bg, fg, act);
            }
        }

        void OpenConfirm(PersonCard c, string title, string text, string confirm, Action run)
        {
            var m = OpenModal("Are you sure?", 640, 330); float x0 = 960 - 320, y0 = 540 - 165;
            Ui.Label(m, title, "Bold", 24, White, x0 + 28, y0 + 80, 584, 34); Ui.Paragraph(m, text, "Regular", 18, Soft, x0 + 28, y0 + 124, 584, 80);
            ModalButton(m, "Btn_FriConfirm", x0 + 28, y0 + 240, 280, confirm, FrRed, White, () => { CloseModal(); run(); }); ModalButton(m, "Btn_FriCancelDialog", x0 + 332, y0 + 240, 280, "Cancel", new Color(1, 1, 1, 0.12f), White, CloseModal);
        }

        void FriOpenProfile(PersonCard c)
        {
            _frDlgCard = c; _frProfile = null; _frProfState = LoadState.Loading; _frProfError = null; BuildProfileDialog(); _ = LoadProfile(c);
        }

        async Task LoadProfile(PersonCard c)
        {
            var r = await _svc.Friends.PublicProfile(c.Id); if (this == null || _frDlgCard == null || _frDlgCard.Id != c.Id) return;
            if (r.Ok) { _frProfile = r.Value; _frProfState = LoadState.Ready; } else { _frProfState = LoadState.Unavailable; _frProfError = r.UserMessage; }
            Debug.Log($"[Friends] profile id={c.Username} ok={r.Ok} error={r.ErrorCode}" + (r.Ok ? $" level={r.Value.level} jobs={r.Value.jobs_completed} status={r.Value.friend_status}" : ""));
            if (_modal != null) BuildProfileDialog();
        }

        void BuildProfileDialog()
        {
            var c = _frDlgCard; if (c == null) return; var m = OpenModal(c.Relation == Relation.Self ? "Your profile" : "Player profile", 760, 640); float x0 = 960 - 380, y0 = 540 - 320;
            var pf = _frProfile; var show = pf != null ? new PersonCard { Id = pf.player_id, Name = pf.display_name, Username = pf.username, Country = pf.country_code, Level = pf.level, Status = pf.friend_status ?? c.Status, Online = pf.presence != null && pf.presence.online, HasPresence = pf.presence != null && (pf.presence.online || !string.IsNullOrEmpty(pf.presence.last_seen_at)) || pf.friend_status == "friends" || pf.friend_status == "self", Activity = pf.presence != null ? pf.presence.activity ?? "" : "", Route = pf.presence != null ? pf.presence.route_code ?? "" : "", LastSeen = pf.presence != null ? pf.presence.last_seen_at ?? "" : "", Vehicle = pf.vehicle ?? "", VehicleId = pf.vehicle_id ?? "" } : c;
            FriAvatar(m, show, x0 + 32, y0 + 84, 110, true);
            Ui.Label(m, show.Name, "ExtraBold", 36, White, x0 + 164, y0 + 88, 540, 46); Ui.TextWithCountry(m, (string.IsNullOrEmpty(show.Username) ? "" : "@" + show.Username + "   ") + "Level " + show.Level, show.Country, "Regular", 19, Soft, x0 + 164, y0 + 134, 540, 26, 32);
            string st = FriendsRules.StatusText(show), ac = Activity(show); if (st.Length > 0) { Ui.Disc(m, "d", show.Online ? Brand.Good : Muted, x0 + 166, y0 + 172, 12); Ui.Label(m, st + (ac.Length > 0 && show.Online ? "  -  " + ac : ""), "SemiBold", 18, show.Online ? Brand.Good : Muted, x0 + 186, y0 + 167, 520, 24); }
            string chip = FriendsRules.RelationChip(show); if (chip.Length > 0 && show.Relation != Relation.Self) Ui.Label(m, chip, "Bold", 16, show.Relation == Relation.Friends ? Brand.Good : Gold, x0 + 166, y0 + 196, 400, 22);
            Ui.Flat(m, "Div", new Color(1, 1, 1, 0.08f), x0 + 28, y0 + 232, 704, 1.5f);
            if (_frProfState == LoadState.Loading) { Ui.Label(m, "Loading profile...", "SemiBold", 21, Muted, x0, y0 + 330, 760, 30, TextAnchor.UpperCenter); return; }
            if (_frProfState == LoadState.Unavailable) { Ui.Label(m, "Could not load this profile.", "Bold", 22, Brand.Bad, x0 + 28, y0 + 290, 704, 30, TextAnchor.UpperCenter); Ui.Paragraph(m, _frProfError ?? "Please try again.", "Regular", 18, Soft, x0 + 120, y0 + 330, 520, 50, TextAnchor.UpperCenter); ModalButton(m, "Btn_FriProfileRetry", x0 + 240, y0 + 400, 280, "Try again", Gold, Ink, () => { _frProfState = LoadState.Loading; BuildProfileDialog(); _ = LoadProfile(_frDlgCard); }); return; }
            DateTimeOffset ms; string since = TimeText.TryParse(pf.member_since, out ms) ? ms.UtcDateTime.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) : "-";
            var rows = new List<(string, string)> { ("Experience", pf.experience.ToString("N0") + " XP"), ("Jobs completed", pf.jobs_completed.ToString("N0")), ("Distance driven", pf.distance_km.ToString("N1") + " km"), ("Achievements", pf.achievements.ToString()), ("Member since", since) };
            if (!string.IsNullOrEmpty(pf.vehicle)) rows.Add(("Drives", pf.vehicle));
            for (int i = 0; i < rows.Count; i++) { float ry = y0 + 246 + i * 40; Ui.Label(m, rows[i].Item1, "SemiBold", 18, new Color(0.8f, 0.83f, 0.88f), x0 + 40, ry, 300, 26); Ui.Label(m, rows[i].Item2, "Bold", 19, White, x0 + 340, ry, 372, 26, TextAnchor.UpperRight); }
            Debug.Log($"[Friends] profile dialog shown id={show.Username} rows={rows.Count}");
            float by = y0 + 246 + rows.Count * 40 + 14; var cp = show;
            if (show.Relation == Relation.None) ModalButton(m, "Btn_FriProfileAdd", x0 + 28, by, 704, "Add Friend", Gold, Ink, () => { CloseModal(); _ = DoAdd(cp); });
            else if (show.Relation == Relation.Friends) { ModalButton(m, "Btn_FriProfileInvite", x0 + 28, by, 340, "Invite to Convoy", Gold, Ink, () => { if (show.Online && FrInConvoy()) { CloseModal(); _ = DoInvite(cp); } else FriNote(!FrInConvoy() ? "Join or create a convoy first, then invite your friends." : show.Name + " is offline."); }); ModalButton(m, "Btn_FriProfileRemove", x0 + 392, by, 340, "Remove Friend", FrRed, White, () => OpenConfirm(cp, "Remove " + cp.Name + " from your friends?", "You will not see where they are, and you will have to ask again to be friends.", "Remove Friend", () => _ = DoRemove(cp))); }
            else if (show.Relation == Relation.Incoming) { ModalButton(m, "Btn_FriProfileAccept", x0 + 28, by, 340, "Accept", FrGreen, White, () => { CloseModal(); _ = DoRespond(cp, true); }); ModalButton(m, "Btn_FriProfileDecline", x0 + 392, by, 340, "Decline", FrRed, White, () => { CloseModal(); _ = DoRespond(cp, false); }); }
        }

        // ================================================================================================================ actions (every one is a backend call; the lists are read again afterwards)
        async Task Act(Func<Task> work) { if (_frBusy) { Toast("One moment..."); return; } _frBusy = true; try { await work(); } finally { _frBusy = false; } }

        void FriAfterChange() { LoadFriendsPage(true); _ = RefreshSnapshot(); }          // read again: the answer is the server's, not what we expect it to be

        async Task DoAdd(PersonCard c) => await Act(async () =>
        {
            var r = await _svc.Friends.Send(c.Id); if (this == null) return;
            Debug.Log($"[Friends] add id={c.Username} ok={r.Ok} status={(r.Ok ? r.Value.status : r.ErrorCode)}");
            if (r.Ok)
            {
                bool accepted = r.Value.status == "accepted"; Toast(accepted ? "You and " + c.Name + " are now friends." : "Friend request sent to " + c.Name + ".", 4f);
                if (_frFound != null && _frFound.Id == c.Id) _frFound.Status = accepted ? "friends" : "outgoing";
            }
            else Toast("Could not add " + c.Name + ". " + r.UserMessage, 5f);
            FriAfterChange(); RefreshFriends();
        });

        async Task DoRespond(PersonCard c, bool accept) => await Act(async () =>
        {
            if (string.IsNullOrEmpty(c.RequestId)) { var rq = _svc.Friends.Requests.Value; var it = rq != null && rq.incoming != null ? rq.incoming.FirstOrDefault(x => x.person != null && x.person.player_id == c.Id) : null; c.RequestId = it != null ? it.request_id : ""; }
            if (string.IsNullOrEmpty(c.RequestId)) { Toast("That request is no longer there."); FriAfterChange(); return; }
            var r = await _svc.Friends.Respond(c.RequestId, accept); if (this == null) return;
            Debug.Log($"[Friends] respond id={c.Username} accept={accept} ok={r.Ok} result={(r.Ok ? (r.Value ?? "").Trim('"') : r.ErrorCode)}");
            Toast(r.Ok ? (accept ? c.Name + " is now your friend." : "Request from " + c.Name + " declined.") : "Could not answer the request. " + r.UserMessage, 4f);
            FriAfterChange(); RefreshFriends();
        });

        async Task DoCancel(PersonCard c) => await Act(async () =>
        {
            var r = await _svc.Friends.Remove(c.Id); if (this == null) return;
            Debug.Log($"[Friends] cancel id={c.Username} ok={r.Ok} error={r.ErrorCode}");
            Toast(r.Ok ? "Request to " + c.Name + " cancelled." : "Could not cancel the request. " + r.UserMessage, 4f);
            FriAfterChange(); RefreshFriends();
        });

        async Task DoRemove(PersonCard c) => await Act(async () =>
        {
            var r = await _svc.Friends.Remove(c.Id); if (this == null) return;
            Debug.Log($"[Friends] remove id={c.Username} ok={r.Ok} error={r.ErrorCode}");
            Toast(r.Ok ? c.Name + " is no longer your friend." : "Could not remove " + c.Name + ". " + r.UserMessage, 4f);
            FriAfterChange(); RefreshFriends();
        });

        async Task DoBlock(PersonCard c) => await Act(async () =>
        {
            var r = await _svc.Convoys.Block(c.Id); if (this == null) return;
            Debug.Log($"[Friends] block id={c.Username} ok={r.Ok} error={r.ErrorCode}");
            Toast(r.Ok ? c.Name + " is blocked." : "Could not block " + c.Name + ". " + r.UserMessage, 4f);
            FriAfterChange(); RefreshFriends();
        });

        async Task DoInvite(PersonCard c) => await Act(async () =>
        {
            var r = await _svc.Convoys.InviteFriend(c.Id); if (this == null) return;
            Debug.Log($"[Friends] invite id={c.Username} ok={r.Ok} error={r.ErrorCode}");
            Toast(r.Ok ? "Convoy invite sent to " + c.Name + "." : "Could not invite " + c.Name + ". " + r.UserMessage, 5f);
        });

        // ================================================================================================================ update
        void FriUpdate(float now)
        {
            if (_frSearchDue > 0f && now > _frSearchDue)
            {
                _frSearchDue = -1f; string q = (_frSearchPending ?? "").Trim();
                if (q != _frSearch)
                {
                    _frSearch = q;
                    if (_frTab != FriendsTab.Find)
                    {
                        var basis = _frTab == FriendsTab.Recent ? FrRecent() : _frTab == FriendsTab.Requests ? FrIncoming().Concat(FrOutgoing()).ToList() : FrFriends();
                        Debug.Log($"[Friends] search=\"{q}\" tab={_frTab.ToString().ToLowerInvariant()} shown={FriendsRules.Filter(basis, _frTab == FriendsTab.Online ? FriendsTab.Online : FriendsTab.All, q).Count}");
                        _scrollPos.Remove("fri_list"); Refill("FriMain", BuildFriMain);
                    }
                }
            }
            if (now > _nextFr) { _nextFr = now + FrPoll; if (_modal == null) LoadFriendsPage(true); }
            // wheel scrolling
            float dy = 0f; Vector2 mp = Vector2.zero;
            try { dy = UnityEngine.Input.mouseScrollDelta.y; mp = UnityEngine.Input.mousePosition; } catch (InvalidOperationException) { }
            if (dy != 0f && _modal == null)
            {
                _areas.RemoveAll(a => a.View == null);
                foreach (var a in _areas) if (a.View.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(a.View, mp, null)) { ApplyScroll(a, a.Pos - dy * 60f); break; }
            }
            if (_frArea != null && _frArea.View != null) FriRebuildRows(false);
            bool typing = _frSearchField != null && _frSearchField.isFocused;
            try
            {
                if (typing && (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter))) { _frSearchDue = -1f; _frSearch = (_frSearchPending ?? "").Trim(); _ = FindPlayer(_frSearch); }
                if (!typing && UnityEngine.Input.GetKeyDown(KeyCode.Escape)) { if (_modal != null) CloseModal(); else if (_frSortPop) { _frSortPop = false; RefreshFriends(); } }
            }
            catch (InvalidOperationException) { }
        }
    }
}
