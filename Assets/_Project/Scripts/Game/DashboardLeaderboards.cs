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
    /// The Leaderboards page. Every rank and value is the server's (get_leaderboard, get_friends_leaderboard, search_leaderboard); the client sends only a metric, a period,
    /// a page and some text. Boards: XP, Earnings, Distance, Jobs (the metrics the backend ranks) for All Time or This Week. Global or Friends scope, the podium, your own rank
    /// (from the server, even when you are not on the page shown), a server-side search with global ranks, paging by offset, and a profile on any row.
    /// Rules (formatting, paging window, podium order, which period is supported) are in <see cref="LeaderboardRules"/>, unit tested.
    /// Not shown because the backend has no source for them: rank movement, regions, months and seasons, a multiplayer board (docs/PLACEHOLDERS.md).
    /// </summary>
    public sealed partial class DashboardScreen
    {
        const float LbLX = 308, LbLY = 268, LbRX = 1418, LbRW = 472, LbPodH = 232, LbTableY = 514, LbTableH = 544, LbRowPitch = 37;

        BoardMetric _lbMetric = BoardMetric.Xp; BoardPeriod _lbPeriod = BoardPeriod.AllTime; BoardScope _lbScope = BoardScope.Global; int _lbPage = 1;
        string _lbSearch = "", _lbSearchPending = ""; float _lbSearchDue = -1f, _nextLb; InputField _lbSearchField;
        LeaderboardPage _lbResults; LoadState _lbSearchState = LoadState.Idle; string _lbSearchErr; bool _lbPeriodPop, _lbOpen;
        List<BoardRow> _lbTopGlobal = new List<BoardRow>();
        static readonly Color LbGold = new Color(0.98f, 0.76f, 0.2f), LbSilver = new Color(0.78f, 0.82f, 0.88f), LbBronze = new Color(0.82f, 0.52f, 0.3f);

        public void OpenLeaderboards() => ShowPage(Page.Leaderboards);

        // ================================================================================================================ data
        BoardRow ToRow(LeaderboardRow r) => new BoardRow { Rank = r.rank, Id = r.player_id ?? "", Name = r.display_name ?? "Driver", Username = r.username ?? "", Country = r.country_code ?? "", Level = r.level, Value = r.value };
        List<BoardRow> Rows(LeaderboardPage p) => p != null && p.items != null ? p.items.Select(ToRow).ToList() : new List<BoardRow>();
        string MyId() => _svc.Profile != null ? _svc.Profile.id : null;
        BoardPeriod EffPeriod => LeaderboardRules.EffectivePeriod(_lbMetric, _lbPeriod);
        static bool LbHasData<T>(Loadable<T> l) => l.State == LoadState.Ready || l.State == LoadState.Empty || (l.State == LoadState.Loading && l.Value != null);
        static Color Medal(int place) => place == 1 ? LbGold : place == 2 ? LbSilver : LbBronze;

        // ================================================================================================================ lifecycle
        void LbOpened() { _lbOpen = true; _lbPeriodPop = false; Debug.Log("[Leaderboards] opened"); LoadBoards(false); _nextLb = Time.unscaledTime + 30f; RefreshBoards(); }
        void LbClosed() { _lbOpen = false; _lbPeriodPop = false; if (_modal != null) CloseModal(); Debug.Log("[Leaderboards] closed"); }

        void LoadBoards(bool quiet) { _ = LoadBoardPage(quiet); _ = LoadFriendsBoard(quiet); _ = LoadWeeklyTop(quiet); }

        async Task LoadBoardPage(bool quiet)
        {
            string m = LeaderboardRules.MetricKey(_lbMetric), p = LeaderboardRules.PeriodKey(EffPeriod); int page = _lbScope == BoardScope.Global ? _lbPage : 1;
            var pending = _svc.Boards.LoadBoard(m, p, LeaderboardRules.PageSize, LeaderboardRules.Offset(page)); if (!quiet) RefreshBoards(); var r = await pending; if (this == null) return;
            if (m != LeaderboardRules.MetricKey(_lbMetric) || p != LeaderboardRules.PeriodKey(EffPeriod)) return;       // the player changed the board meanwhile: this answer is for the old one
            if (r.Ok && r.Value.offset == 0) _lbTopGlobal = Rows(r.Value).Take(3).ToList();
            var me = r.Ok && r.Value.me != null ? r.Value.me : null;
            Debug.Log($"[Leaderboards] board metric={m} period={p} page={page} state={_svc.Boards.Board.State.ToString().ToLowerInvariant()} shown={(r.Ok ? Rows(r.Value).Count : 0)} total={(r.Ok ? r.Value.total : 0)} me={(me != null && me.rank > 0 ? me.rank.ToString() : "none")}" + (r.Ok ? "" : " error=" + r.ErrorCode));
            if (_page == Page.Leaderboards) RefreshBoards();
        }

        async Task LoadFriendsBoard(bool quiet)
        {
            string m = LeaderboardRules.MetricKey(_lbMetric), p = LeaderboardRules.PeriodKey(EffPeriod);
            var pending = _svc.Boards.LoadFriends(m, p); if (!quiet) RefreshBoards(); var r = await pending; if (this == null) return;
            if (m != LeaderboardRules.MetricKey(_lbMetric) || p != LeaderboardRules.PeriodKey(EffPeriod)) return;
            Debug.Log($"[Leaderboards] friends metric={m} period={p} state={_svc.Boards.Friends.State.ToString().ToLowerInvariant()} players={(r.Ok ? r.Value.total : 0)} me={(r.Ok && r.Value.me != null ? r.Value.me.rank.ToString() : "none")}" + (r.Ok ? "" : " error=" + r.ErrorCode));
            if (_page == Page.Leaderboards) RefreshBoards();
        }

        async Task LoadWeeklyTop(bool quiet)
        {
            var pending = _svc.Boards.LoadWeekly(); if (!quiet) RefreshBoards(); var r = await pending; if (this == null) return;
            Debug.Log($"[Leaderboards] weekly state={_svc.Boards.Weekly.State.ToString().ToLowerInvariant()} shown={(r.Ok ? Rows(r.Value).Count : 0)}" + (r.Ok ? "" : " error=" + r.ErrorCode));
            if (_page == Page.Leaderboards) RefreshBoards();
        }

        async Task RunLbSearch(string q)
        {
            q = (q ?? "").Trim(); _lbSearch = q;
            if (q.Length < 2) { _lbResults = null; _lbSearchState = LoadState.Idle; RefreshBoards(); return; }
            _lbSearchState = LoadState.Loading; _lbSearchErr = null; RefreshBoards();
            var r = await _svc.Boards.Search(LeaderboardRules.MetricKey(_lbMetric), LeaderboardRules.PeriodKey(EffPeriod), q, 10); if (this == null || q != _lbSearch) return;
            if (r.Ok) { _lbResults = r.Value; _lbSearchState = Rows(r.Value).Count == 0 ? LoadState.Empty : LoadState.Ready; } else { _lbResults = null; _lbSearchState = LoadState.Unavailable; _lbSearchErr = r.UserMessage; }
            Debug.Log($"[Leaderboards] search \"{q}\" state={_lbSearchState.ToString().ToLowerInvariant()} found={(r.Ok ? Rows(r.Value).Count : 0)} total={(r.Ok ? r.Value.total : 0)}" + (r.Ok ? "" : " error=" + r.ErrorCode));
            RefreshBoards();
        }

        void RefreshBoards() { if (!_lbOpen && _page != Page.Leaderboards) return; Refill("LbTabs", BuildLbTabs); Refill("LbPodium", BuildLbPodium); Refill("LbTable", BuildLbTable); Refill("LbSide", BuildLbSide); Refill("LbPop", BuildLbPop); }

        void LbBoardChanged()
        {
            _lbPage = 1; _lbSearch = ""; _lbSearchPending = ""; _lbResults = null; _lbSearchState = LoadState.Idle; if (_lbSearchField != null) _lbSearchField.text = "";
            _lbPeriodPop = false; _svc.Boards.Board.Reset(); _svc.Boards.Friends.Reset(); LoadBoards(false); RefreshBoards();
            foreach (var n in new[] { "LbPodium", "LbTable" }) if (_regions.TryGetValue(n, out var r) && r != null) Entrance.On(r, 0f, 0.22f, 8f);
        }

        // ================================================================================================================ build
        void BuildLeaderboardsPage()
        {
            Region("LbTop", 0.02f); BuildLbTop();
            Region("LbTabs", 0.05f); BuildLbTabs();
            Region("LbPodium", 0.08f); BuildLbPodium();
            Region("LbTable", 0.12f); BuildLbTable();
            Region("LbSide", 0.14f); BuildLbSide();
            Region("LbPop", 0.30f); BuildLbPop();
        }

        void BuildLbTop()
        {
            Ui.Icon(_stage, "ic_trophy", Gold, 322, 136, 58);
            var t = Ui.Label(_stage, "Leaderboards", "ExtraBold", 52, White, 396, 128, 700, 64); Ui.Drop(t, 0.5f);
            Ui.Label(_stage, "Compete with drivers across Africa, climb the rankings and become one of the best drivers on the road.", "Regular", 18, new Color(0.84f, 0.86f, 0.9f), 396, 188, 880, 26);
            const float qx = 1290, qy = 140, qw = 396, qh = 54;
            Ui.Outlined(_stage, "LbSearchBg", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.16f), qx, qy, qw, qh, 14, 1.3f);
            var c = new Color(0.82f, 0.84f, 0.88f);
            Ui.Disc(_stage, "SearchRing", c, qx + 16, qy + 15, 19); Ui.Disc(_stage, "SearchHole", new Color(0.05f, 0.055f, 0.075f, 1f), qx + 18, qy + 17, 15); Ui.Segment(_stage, new Vector2(qx + 32, qy + 31), new Vector2(qx + 40, qy + 39), 3, c);
            var areaGo = new GameObject("LbSearchArea", typeof(RectTransform), typeof(RectMask2D)); var area = Ui.Place(areaGo, _stage, Ui.Anchor.TopLeft, qx + 52, qy, qw - 66, qh);
            var txt = Ui.Label(area, "", "Regular", 19, White, 0, 0, qw - 66, qh, TextAnchor.MiddleLeft); var ph = Ui.Label(area, "Search player by name or username...", "Regular", 18, new Color(1, 1, 1, 0.5f), 0, 0, qw - 66, qh, TextAnchor.MiddleLeft);
            var hit = areaGo.AddComponent<Image>(); hit.color = new Color(1, 1, 1, 0.001f); areaGo.name = "Lb_Search_Input";
            var f = areaGo.AddComponent<InputField>(); f.targetGraphic = hit; f.textComponent = txt; f.placeholder = ph; f.lineType = InputField.LineType.SingleLine; f.characterLimit = 24; f.text = _lbSearch; f.transition = Selectable.Transition.None;
            f.customCaretColor = true; f.caretColor = Gold; f.selectionColor = new Color(Gold.r, Gold.g, Gold.b, 0.35f);
            f.onValueChanged.AddListener(v => { _lbSearchPending = v ?? ""; _lbSearchDue = Time.unscaledTime + 0.45f; });
            _lbSearchField = f;
        }

        static readonly (BoardMetric m, string icon)[] LbTabs = { (BoardMetric.Xp, "ic_crown"), (BoardMetric.Earnings, "ic_coin"), (BoardMetric.Distance, "ic_road"), (BoardMetric.Jobs, "ic_jobs") };

        void BuildLbTabs()
        {
            float x = 308;
            foreach (var tab in LbTabs)
            {
                bool on = tab.m == _lbMetric; string label = LeaderboardRules.MetricLabel(tab.m);
                var probe = Ui.Label(_stage, label, on ? "Bold" : "SemiBold", 19, White, 0, 0, 10, 10); float tw = probe.preferredWidth; Destroy(probe.gameObject); float w = 16 + 26 + 10 + tw + 20, y = 207, h = 46;
                if (on) Ui.Rounded(_stage, "TabOn", Gold, x, y, w, h, 12); else Ui.Outlined(_stage, "Tab", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.14f), x, y, w, h, 12);
                Ui.Icon(_stage, tab.icon, on ? Ink : new Color(0.86f, 0.88f, 0.92f), x + 14, y + 10, 26); Ui.Label(_stage, label, on ? "Bold" : "SemiBold", 19, on ? Ink : White, x + 48, y + 9, tw + 6, 28);
                var mm = tab.m; Ui.Click(_stage, "Btn_LbTab_" + mm, x, y, w, h, () => { if (_lbMetric == mm) return; _lbMetric = mm; Debug.Log("[Leaderboards] metric=" + LeaderboardRules.MetricKey(mm)); LbBoardChanged(); }); x += w + 10;
            }
            // period (a menu) next to the tabs
            string pl = LeaderboardRules.PeriodLabel(_lbPeriod); float pw = 200;
            Ui.Outlined(_stage, "PeriodBg", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, _lbPeriodPop ? 0.6f : 0.16f), x + 14, 207, pw, 46, 12, 1.3f);
            Ui.Icon(_stage, "ic_clock", Soft, x + 28, 218, 24); Ui.Label(_stage, pl, "SemiBold", 18, White, x + 62, 217, pw - 80, 26); Ui.Icon(_stage, "ic_chevron_down", Soft, x + 14 + pw - 30, 220, 18);
            _lbPeriodX = x + 14; Ui.Click(_stage, "Btn_LbPeriod", x + 14, 207, pw, 46, () => { _lbPeriodPop = !_lbPeriodPop; Refill("LbPop", BuildLbPop); Refill("LbTabs", BuildLbTabs); });
            // scope: everyone or friends
            float sx = LbRX; const float sw = 232;
            foreach (BoardScope s in new[] { BoardScope.Global, BoardScope.Friends })
            {
                bool on = s == _lbScope; var ss = s;
                if (on) Ui.Rounded(_stage, "ScopeOn", Gold, sx, 207, sw, 46, 12); else Ui.Outlined(_stage, "Scope", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.14f), sx, 207, sw, 46, 12);
                Ui.Icon(_stage, s == BoardScope.Global ? "icon_globe" : "ic_people", on ? Ink : Soft, sx + 24, 217, 26); Ui.Label(_stage, s == BoardScope.Global ? "Everyone" : "My Friends", on ? "Bold" : "SemiBold", 19, on ? Ink : White, sx + 60, 217, sw - 70, 28);
                Ui.Click(_stage, "Btn_LbScope_" + s, sx, 207, sw, 46, () => { if (_lbScope == ss) return; _lbScope = ss; _lbPage = 1; Debug.Log("[Leaderboards] scope=" + ss.ToString().ToLowerInvariant()); if (ss == BoardScope.Global) _ = LoadBoardPage(false); RefreshBoards(); foreach (var n in new[] { "LbPodium", "LbTable" }) if (_regions.TryGetValue(n, out var r) && r != null) Entrance.On(r, 0f, 0.22f, 8f); });
                sx += sw + 8;
            }
        }
        float _lbPeriodX;

        // ---------------------------------------------------------------- podium
        void BuildLbPodium()
        {
            var ld = _lbScope == BoardScope.Friends ? _svc.Boards.Friends : _svc.Boards.Board; bool data = LbHasData(ld);
            List<BoardRow> top = _lbScope == BoardScope.Friends ? Rows(_svc.Boards.Friends.Value).Where(r => r.Rank >= 1).Take(3).ToList() : _lbTopGlobal;
            if (_lbScope == BoardScope.Friends && top.Count > 0 && top.All(r => r.Value <= 0)) top = new List<BoardRow>();     // a podium of players with nothing is not a podium
            const float w = (1090 - 28) / 3f; var order = LeaderboardRules.Podium(top);
            var slots = new[] { 2, 1, 3 };
            for (int i = 0; i < 3; i++)
            {
                int place = slots[i]; BoardRow r = null; foreach (var kv in order) if (kv.Key == place) r = kv.Value; float x = LbLX + i * (w + 14);
                PodiumCard(x, LbLY, w, LbPodH, place, r, data, ld);
            }
        }

        void PodiumCard(float x, float y, float w, float h, int place, BoardRow r, bool data, Loadable<LeaderboardPage> ld)
        {
            bool first = place == 1; Color mc = Medal(place);
            Ui.Outlined(_stage, "PodCard", new Color(0.055f, 0.062f, 0.082f, 0.97f), first ? new Color(LbGold.r, LbGold.g, LbGold.b, 0.85f) : new Color(1, 1, 1, 0.12f), x, y, w, h, 20, first ? 2.5f : 1.4f);
            var tex = Brand.Texture(place == 1 ? "convoy_b" : place == 2 ? "convoy_a" : "convoy_c");
            if (tex != null && r != null) Ui.Photo(_stage, "PodPhoto", tex, Ui.Cover(tex, 0f, 0f, 1f, w / 120f), x + 2, y + 2, w - 4, 120, 18, new Color(1, 1, 1, first ? 0.5f : 0.36f));
            Ui.Gradient(_stage, "PodFade", new Color(0.055f, 0.062f, 0.082f, 0f), new Color(0.055f, 0.062f, 0.082f, 1f), false, x + 2, y + 66, w - 4, 58);
            // medal
            Ui.Disc(_stage, "Medal", new Color(mc.r * 0.35f, mc.g * 0.35f, mc.b * 0.35f, 1f), x + 16, y + 14, 44); Ui.Disc(_stage, "MedalIn", mc, x + 20, y + 18, 36); Ui.Label(_stage, place.ToString(), "ExtraBold", 22, Ink, x + 20, y + 18, 36, 36, TextAnchor.MiddleCenter);
            if (first) Ui.Icon(_stage, "ic_crown", LbGold, x + w - 52, y + 16, 34);
            if (r == null)
            {
                string msg = !data ? (ld.State == LoadState.Unavailable ? "Unavailable" : "Loading...") : "No driver ranked here yet";
                Ui.Label(_stage, msg, "SemiBold", 19, Muted, x + 20, y + 120, w - 40, 28, TextAnchor.UpperCenter); return;
            }
            var card = new PersonCard { Id = r.Id, Name = r.Name, Username = r.Username, Level = r.Level, Country = r.Country };
            FriAvatar(_stage, card, x + 24, y + 70, first ? 96 : 84, false);
            float ax = x + 24 + (first ? 96 : 84) + 18; var nm = Ui.Label(_stage, r.Name, "ExtraBold", first ? 26 : 23, White, ax, y + 78, w - (ax - x) - 16, 34); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 14; nm.resizeTextMaxSize = first ? 26 : 23; nm.horizontalOverflow = HorizontalWrapMode.Wrap; nm.verticalOverflow = VerticalWrapMode.Truncate; Ui.Drop(nm, 0.5f);
            Ui.TextWithCountry(_stage, "Level " + r.Level, r.Country, "Regular", 16, new Color(0.8f, 0.83f, 0.88f), ax, y + 114, w - (ax - x) - 16, 22);
            var vt = Ui.Label(_stage, LeaderboardRules.FormatValue(_lbMetric, r.Value), "ExtraBold", first ? 36 : 31, mc, x + 20, y + h - 62, w - 40, 46, TextAnchor.MiddleLeft); vt.resizeTextForBestFit = true; vt.resizeTextMinSize = 18; vt.resizeTextMaxSize = first ? 36 : 31; vt.horizontalOverflow = HorizontalWrapMode.Wrap; vt.verticalOverflow = VerticalWrapMode.Truncate;
            if (LeaderboardRules.IsMe(r, MyId())) { Ui.Rounded(_stage, "YouChip", new Color(Gold.r, Gold.g, Gold.b, 0.9f), x + w - 76, y + h - 52, 58, 26, 13); Ui.Label(_stage, "You", "Bold", 15, Ink, x + w - 76, y + h - 50, 58, 22, TextAnchor.UpperCenter); }
            Ui.Click(_stage, "Btn_LbPodium_" + place, x, y, w, h, () => LbOpenProfile(r));
        }

        void LbOpenProfile(BoardRow r) { Debug.Log($"[Leaderboards] profile {r.Username}"); FriOpenProfile(new PersonCard { Id = r.Id, Name = r.Name, Username = r.Username, Level = r.Level, Country = r.Country, Status = LeaderboardRules.IsMe(r, MyId()) ? "self" : "none" }); }

        // ---------------------------------------------------------------- the table
        void BuildLbTable()
        {
            Ui.Outlined(_stage, "LbPanel", PanelFill, PanelLine, LbLX, LbTableY, 1090, LbTableH, 22, 1.5f);
            bool searching = _lbSearch.Length >= 2 || _lbSearchState == LoadState.Loading;
            if (searching) { BuildLbSearchView(); return; }
            var ld = _lbScope == BoardScope.Friends ? _svc.Boards.Friends : _svc.Boards.Board;
            string title = _lbScope == BoardScope.Friends ? "My Friends Leaderboard" : "Leaderboard";
            LbTableHeader(title, LeaderboardRules.ColumnTitle(_lbMetric), null);
            if (!LbHasData(ld)) { if (ld.State == LoadState.Unavailable) LbError("Unable to load leaderboard.", ld.Error, "Btn_LbRetry", () => LoadBoards(false)); else LbLoading("Loading leaderboard..."); return; }
            var page = ld.Value; var all = Rows(page); string myId = MyId();
            List<BoardRow> rows; int total, offset;
            if (_lbScope == BoardScope.Friends) { rows = all; total = all.Count; offset = 0; }
            else { rows = all; total = page.total; offset = page.offset; }
            if (_lbScope == BoardScope.Global && _lbPage > 1 && offset == 0 && total > 0 && rows.Count > 0 && ld.State == LoadState.Loading) { }
            string note = LeaderboardRules.PeriodNote(_lbMetric, _lbPeriod); if (note != null) { Ui.Label(_stage, note, "SemiBold", 15, Gold, LbLX + 1090 - 24 - 560, LbTableY + 10, 560, 22, TextAnchor.UpperRight); }
            if (rows.Count == 0 || (_lbScope == BoardScope.Friends && all.Count <= 1 && all.All(r => r.Value <= 0)))
            {
                if (_lbScope == BoardScope.Friends) LbEmpty("ic_people", "You don't have any ranked friends yet.", "Add friends, then complete jobs: you will see how you compare.", "Find friends", () => OpenFriends(), "Btn_LbFindFriends");
                else LbEmpty("ic_trophy", "No drivers have been ranked yet.", "Complete a job to be the first on this board.", null, null, null);
                return;
            }
            for (int i = 0; i < rows.Count; i++) LbRow(rows[i], LbTableY + 54 + i * LbRowPitch, myId); LbLogFlags("table", rows);
            if (_lbScope == BoardScope.Global) LbPager(page.total, page.offset, rows.Count);
            else Ui.Label(_stage, "Showing " + rows.Count + (rows.Count == 1 ? " player" : " players") + " - you and your friends", "Regular", 16, Soft, LbLX + 1090 - 24 - 520, LbTableY + LbTableH - 40, 520, 24, TextAnchor.UpperRight);
        }

        void LbTableHeader(string title, string valueTitle, string clearName)
        {
            Ui.Label(_stage, title, "ExtraBold", 22, White, LbLX + 24, LbTableY + 4, 560, 30);
            float y = LbTableY + 52; Color hc = new Color(0.62f, 0.66f, 0.74f);
            Ui.Label(_stage, "#", "SemiBold", 15, hc, LbLX + 28, y - 18, 60, 20); Ui.Label(_stage, "Player", "SemiBold", 15, hc, LbLX + 92, y - 18, 200, 20); Ui.Label(_stage, "Level", "SemiBold", 15, hc, LbLX + 470, y - 18, 100, 20); Ui.Label(_stage, "Country", "SemiBold", 15, hc, LbLX + 600, y - 18, 140, 20);
            Ui.Label(_stage, valueTitle + " >", "Bold", 15, Gold, LbLX + 1090 - 70 - 260, y - 18, 260, 20, TextAnchor.UpperRight);
        }

        /// <summary>One log line per table build: how many rows drew a flag, a neutral placeholder for a missing country, or the placeholder for a code with no artwork (the browser test reads it).</summary>
        void LbLogFlags(string view, List<BoardRow> rows) { var c = CountryFlags.Count(rows.Select(r => r.Country)); Debug.Log($"[Leaderboards] flags view={view} rows={rows.Count} known={c.Known} noartwork={c.NoArtwork} notset={c.NotSet} drawn={c.Drawn}"); }

        void LbRow(BoardRow r, float y, string myId)
        {
            bool me = LeaderboardRules.IsMe(r, myId); int medal = LeaderboardRules.MedalOf(r.Rank); float x = LbLX + 12, w = 1090 - 24;
            if (me) Ui.Outlined(_stage, "MeRow", new Color(Gold.r, Gold.g, Gold.b, 0.10f), new Color(Gold.r, Gold.g, Gold.b, 0.85f), x, y, w, LbRowPitch - 3, 10, 1.8f); else Ui.Flat(_stage, "RowLine", new Color(1, 1, 1, 0.045f), x + 8, y + LbRowPitch - 2, w - 16, 1.2f);
            Ui.Label(_stage, r.Rank > 0 ? r.Rank.ToString("N0") : "-", "Bold", 19, medal > 0 ? Medal(medal) : White, x + 16, y + 6, 70, 26);
            var card = new PersonCard { Id = r.Id, Name = r.Name }; FriAvatar(_stage, card, x + 82, y + 3, 30, false);
            var nm = Ui.Label(_stage, r.Name, me ? "Bold" : "SemiBold", 18, White, x + 124, y + 6, 280, 26); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 12; nm.resizeTextMaxSize = 18; nm.horizontalOverflow = HorizontalWrapMode.Wrap; nm.verticalOverflow = VerticalWrapMode.Truncate;
            if (me) { Ui.Rounded(_stage, "YouTag", Gold, x + 124 + Mathf.Min(nm.preferredWidth, 270f) + 8, y + 9, 40, 20, 10); Ui.Label(_stage, "You", "Bold", 12, Ink, x + 124 + Mathf.Min(nm.preferredWidth, 270f) + 8, y + 10, 40, 18, TextAnchor.UpperCenter); }
            Ui.Label(_stage, r.Level.ToString(), "Regular", 18, new Color(0.84f, 0.86f, 0.9f), x + 458, y + 6, 100, 26); Ui.CountryTag(_stage, r.Country, x + 588, y + 7, 28, 18, new Color(0.84f, 0.86f, 0.9f), "Regular", "Not set");
            Ui.Label(_stage, LeaderboardRules.FormatBare(_lbMetric, r.Value), "Bold", 19, me ? Gold : White, x + w - 70 - 260, y + 6, 260, 26, TextAnchor.UpperRight); Ui.Icon(_stage, "ic_chevron_right", new Color(1, 1, 1, 0.35f), x + w - 40, y + 8, 20);
            Ui.Click(_stage, "Btn_LbRow_" + r.Username, x, y, w, LbRowPitch - 3, () => LbOpenProfile(r));
        }

        void LbPager(int total, int offset, int shown)
        {
            int pages = LeaderboardRules.PageCount(total); float y = LbTableY + LbTableH - 54; if (pages <= 0) return;
            var win = LeaderboardRules.PageWindow(_lbPage, pages); float bw = 38, gap = 8, totalW = (win.Count + 2) * (bw + gap) - gap, x = LbLX + (1090 - totalW) / 2f - 120;
            Action<string, string, bool, Action> btn = (label, name, on, click) =>
            {
                if (on) Ui.Rounded(_stage, "PgOn", Gold, x, y, bw, 40, 10); else Ui.Outlined(_stage, "Pg", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.14f), x, y, bw, 40, 10, 1.2f);
                Ui.Label(_stage, label, "Bold", 17, on ? Ink : White, x, y + 8, bw, 26, TextAnchor.UpperCenter); if (click != null) Ui.Click(_stage, name, x, y, bw, 40, click); x += bw + gap;
            };
            btn("<", "Btn_LbPrev", false, _lbPage > 1 ? (Action)(() => LbGoto(_lbPage - 1)) : null);
            foreach (int p in win) { int pp = p; if (p < 0) { Ui.Label(_stage, "...", "Bold", 17, Muted, x, y + 8, bw, 26, TextAnchor.UpperCenter); x += bw + gap; } else btn(p.ToString(), "Btn_LbPage_" + p, p == _lbPage, p == _lbPage ? null : (Action)(() => LbGoto(pp))); }
            btn(">", "Btn_LbNext", false, _lbPage < pages ? (Action)(() => LbGoto(_lbPage + 1)) : null);
            Ui.Label(_stage, LeaderboardRules.ShowingText(offset, shown, total), "Regular", 16, Soft, LbLX + 1090 - 24 - 330, y + 9, 330, 24, TextAnchor.UpperRight);
        }

        void LbGoto(int page) { int pages = LeaderboardRules.PageCount(_svc.Boards.Board.Value != null ? _svc.Boards.Board.Value.total : 0); _lbPage = Mathf.Clamp(page, 1, Math.Max(1, pages)); Debug.Log("[Leaderboards] page=" + _lbPage); _ = LoadBoardPage(false); }

        void LbLoading(string text)
        {
            for (int i = 0; i < 8; i++) { float y = LbTableY + 64 + i * 44; Ui.Rounded(_stage, "Skel", new Color(1, 1, 1, 0.05f + (i % 2) * 0.02f), LbLX + 24, y, 1090 - 48, 30, 10); }
            Ui.Label(_stage, text, "SemiBold", 21, Muted, LbLX, LbTableY + 200, 1090, 30, TextAnchor.UpperCenter);
        }
        void LbError(string title, string detail, string retryName, Action retry)
        {
            Ui.Label(_stage, title, "Bold", 26, Brand.Bad, LbLX + 40, LbTableY + 190, 1090 - 80, 36, TextAnchor.UpperCenter);
            Ui.Paragraph(_stage, string.IsNullOrEmpty(detail) ? "Please try again in a moment." : detail, "Regular", 18, Soft, LbLX + 200, LbTableY + 234, 1090 - 400, 60, TextAnchor.UpperCenter);
            FriButton(_stage, retryName, LbLX + 1090 / 2 - 90, LbTableY + 304, 180, 50, "Retry", Gold, Ink, retry, true, null, 20);
        }
        void LbEmpty(string icon, string title, string text, string cta, Action go, string ctaName)
        {
            Ui.Icon(_stage, icon, new Color(1, 1, 1, 0.16f), LbLX + 1090 / 2 - 40, LbTableY + 130, 80);
            Ui.Label(_stage, title, "Bold", 26, White, LbLX + 40, LbTableY + 232, 1090 - 80, 36, TextAnchor.UpperCenter); Ui.Paragraph(_stage, text, "Regular", 18, Soft, LbLX + 200, LbTableY + 276, 1090 - 400, 50, TextAnchor.UpperCenter);
            if (cta != null) FriButton(_stage, ctaName, LbLX + 1090 / 2 - 110, LbTableY + 350, 220, 50, cta, Gold, Ink, go, true, null, 19);
        }

        void BuildLbSearchView()
        {
            string q = _lbSearch.Length >= 2 ? _lbSearch : _lbSearchPending.Trim();
            Ui.Label(_stage, "Search results", "ExtraBold", 24, White, LbLX + 24, LbTableY + 12, 400, 32);
            var clearProbe = Ui.Label(_stage, "Search results", "ExtraBold", 24, White, 0, 0, 10, 10); float cw = clearProbe.preferredWidth; Destroy(clearProbe.gameObject);
            Ui.Label(_stage, "for \"" + q + "\"", "Regular", 19, Soft, LbLX + 24 + cw + 10, LbTableY + 16, 400, 28);
            FriButton(_stage, "Btn_LbSearchClear", LbLX + 1090 - 24 - 150, LbTableY + 10, 150, 38, "Clear search", Color.clear, White, () => { _lbSearch = ""; _lbSearchPending = ""; _lbResults = null; _lbSearchState = LoadState.Idle; if (_lbSearchField != null) _lbSearchField.text = ""; RefreshBoards(); }, true, null, 15);
            Ui.Label(_stage, "#", "SemiBold", 15, new Color(0.62f, 0.66f, 0.74f), LbLX + 28, LbTableY + 30, 60, 20);
            if (_lbSearchState == LoadState.Loading) { LbLoading("Searching..."); return; }
            if (_lbSearchState == LoadState.Unavailable) { LbError("The search did not work.", _lbSearchErr, "Btn_LbSearchRetry", () => _ = RunLbSearch(q)); return; }
            if (_lbSearchState == LoadState.Empty) { LbEmpty("ic_people", "No driver found.", "Only ranked drivers appear here: a player needs some " + LeaderboardRules.MetricLabel(_lbMetric) + " on this board. Check the spelling.", null, null, null); return; }
            if (_lbSearchState != LoadState.Ready || _lbResults == null) { LbLoading("Searching..."); return; }
            var rows = Rows(_lbResults); string myId = MyId();
            for (int i = 0; i < rows.Count; i++) LbRow(rows[i], LbTableY + 54 + i * LbRowPitch, myId); LbLogFlags("table", rows);
            Ui.Label(_stage, _lbResults.total > rows.Count ? "Showing the top " + rows.Count + " of " + _lbResults.total.ToString("N0") + " matches. Type more of the name to narrow it down." : _lbResults.total + (_lbResults.total == 1 ? " match" : " matches") + ". Ranks are on the " + LeaderboardRules.PeriodLabel(EffPeriod).ToLowerInvariant() + " " + LeaderboardRules.MetricLabel(_lbMetric) + " board.", "Regular", 16, Soft, LbLX + 24, LbTableY + LbTableH - 38, 1000, 24);
        }

        // ---------------------------------------------------------------- the side column: your rank, friends, this week
        void BuildLbSide() { BuildLbMe(); BuildLbFriendsPanel(); BuildLbWeeklyPanel(); }

        void BuildLbMe()
        {
            const float y = LbLY, h = 250; var pr = _svc.Profile; var ld = _svc.Boards.Board; var fr = _svc.Boards.Friends;
            Ui.Outlined(_stage, "RankCard", PanelFill, PanelLine, LbRX, y, LbRW, h, 22, 1.5f);
            Ui.Label(_stage, "Your Rank", "ExtraBold", 26, White, LbRX + 22, y + 16, 240, 36);
            var tex = Brand.Texture("truck_photo"); if (tex != null) Ui.Photo(_stage, "RankPhoto", tex, Ui.Cover(tex, 0f, 0f, 1f, LbRW / 110f), LbRX + 2, y + 62, LbRW - 4, 110, 0, new Color(1, 1, 1, 0.30f));
            Ui.Gradient(_stage, "RankFade", new Color(0.062f, 0.07f, 0.092f, 0f), new Color(0.062f, 0.07f, 0.092f, 1f), false, LbRX + 2, y + 118, LbRW - 4, 54);
            if (pr == null) { Ui.Label(_stage, "Your profile is not loaded.", "SemiBold", 19, Muted, LbRX, y + 110, LbRW, 28, TextAnchor.UpperCenter); return; }
            if (!LbHasData(ld))
            {
                if (ld.State == LoadState.Unavailable) { Ui.Label(_stage, "Unable to load your ranking.", "Bold", 20, Brand.Bad, LbRX + 22, y + 84, LbRW - 44, 28); FriButton(_stage, "Btn_LbRetryMe", LbRX + 22, y + 124, 130, 40, "Retry", Gold, Ink, () => LoadBoards(false), true, null, 17); }
                else Ui.Label(_stage, "Loading your rank...", "SemiBold", 19, Muted, LbRX, y + 110, LbRW, 28, TextAnchor.UpperCenter);
                return;
            }
            var me = ld.Value != null ? ld.Value.me : null; bool ranked = me != null && me.rank > 0;
            var rt = Ui.Label(_stage, ranked ? LeaderboardRules.RankText(me.rank) : "Unranked", "ExtraBold", ranked ? 40 : 28, ranked ? Gold : Muted, LbRX + LbRW - 22 - 240, y + 10, 240, 48, TextAnchor.MiddleRight); Ui.Drop(rt, 0.5f);
            var card = new PersonCard { Id = pr.id ?? "", Name = pr.display_name ?? "Driver", Username = Sum != null && Sum.profile != null ? Sum.profile.username ?? "" : "", Level = pr.level, Status = "self" };
            FriAvatar(_stage, card, LbRX + 20, y + 78, 76, false);
            var nm = Ui.Label(_stage, card.Name, "ExtraBold", 24, White, LbRX + 112, y + 82, LbRW - 134, 32); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 14; nm.resizeTextMaxSize = 24; nm.horizontalOverflow = HorizontalWrapMode.Wrap; nm.verticalOverflow = VerticalWrapMode.Truncate; Ui.Drop(nm, 0.6f);
            string myCountry = Me != null ? Me.country_code : Sum != null && Sum.profile != null ? Sum.profile.country_code : null; bool noCountry = string.IsNullOrEmpty(myCountry);
            Debug.Log("[Leaderboards] me flag=" + (CountryFlags.AssetName(myCountry) ?? "none"));
            var lvl = Ui.Label(_stage, "Level " + pr.level, "SemiBold", 17, Soft, LbRX + 112, y + 116, 200, 24);
            // with a country: the flag and code follow "Level N". Without one: the neutral placeholder and "Add your country" sit at the RIGHT end of that row, so the middle of the card still opens the profile
            float tagW, tagX;
            if (noCountry) { var probe = Ui.Label(_stage, "Add your country", "SemiBold", 15, Gold, 0, 0, 10, 10); float tw = probe.preferredWidth; Destroy(probe.gameObject); tagW = 24 + 8 + tw; tagX = LbRX + LbRW - 22 - tagW; Ui.CountryTag(_stage, myCountry, tagX, y + 117, 24, 15, Gold, "SemiBold", "Add your country"); }
            else { tagX = LbRX + 112 + lvl.preferredWidth + 14; tagW = Ui.CountryTag(_stage, myCountry, tagX, y + 117, 24, 15, Soft, "SemiBold", ""); }
            Ui.Label(_stage, ranked ? LeaderboardRules.FormatValue(_lbMetric, me.value) + (_lbPeriod == BoardPeriod.Week && EffPeriod == BoardPeriod.Week ? " this week" : "") : LeaderboardRules.UnrankedText(_lbMetric, EffPeriod), "SemiBold", 15, ranked ? Gold : Soft, LbRX + 112, y + 142, LbRW - 130, 22);
            // xp progress: the server's progression
            var prog = Sum != null ? Sum.progression : null;
            if (prog != null && prog.xp_next_level > prog.xp_level_start)
            {
                long span = Math.Max(1, prog.xp_next_level - prog.xp_level_start); Ui.Bar(_stage, LbRX + 22, y + 176, LbRW - 44 - 210, 10, Mathf.Clamp01((float)prog.level_progress), Gold);
                Ui.Label(_stage, prog.xp_into_level.ToString("N0") + " / " + span.ToString("N0") + " XP", "SemiBold", 15, White, LbRX + LbRW - 20 - 200, y + 170, 200, 22, TextAnchor.UpperRight);
            }
            int? friendRank = fr.Value != null && fr.Value.me != null && fr.Value.items != null && fr.Value.items.Length > 1 ? (int?)fr.Value.me.rank : null;
            var stats = new[] { (ranked ? me.rank.ToString("N0") : "-", "Global rank", "ic_trophy"), (friendRank.HasValue ? "#" + friendRank.Value : "-", "Among friends", "ic_people"), (pr.jobs_completed.ToString("N0"), "Jobs done", "ic_jobs"), (pr.distance_km.ToString("N0") + " km", "Driven", "ic_road") };
            float sw = (LbRW - 24) / 4f;
            for (int i = 0; i < 4; i++) { float sx = LbRX + 12 + i * sw; Ui.Icon(_stage, stats[i].Item3, Gold, sx + sw / 2 - 10, y + 198, 20); Ui.Label(_stage, stats[i].Item1, "ExtraBold", 17, White, sx, y + 217, sw, 20, TextAnchor.UpperCenter); Ui.Label(_stage, stats[i].Item2, "Regular", 12, Muted, sx, y + 235, sw, 16, TextAnchor.UpperCenter); }
            Ui.Click(_stage, "Btn_LbMeProfile", LbRX + 10, y + 70, LbRW - 20, 96, () => FriOpenProfile(card));
            if (noCountry) Ui.Click(_stage, "Btn_LbAddCountry", tagX - 4, y + 112, tagW + 12, 30, () => { Debug.Log("[Leaderboards] add country"); ShowPage(Page.Profile); OpenCountryPicker(); });     // existing players with no country: straight to the profile's country picker, never forced through onboarding
        }

        void BuildLbFriendsPanel()
        {
            const float y = LbLY + 262, h = 298; var ld = _svc.Boards.Friends;
            Ui.Outlined(_stage, "FrPanel", PanelFill, PanelLine, LbRX, y, LbRW, h, 22, 1.5f);
            Ui.Label(_stage, "Friends Leaderboard", "ExtraBold", 24, White, LbRX + 20, y + 14, 300, 34);
            Ui.Label(_stage, "View All  >", "SemiBold", 15, Soft, LbRX + LbRW - 110, y + 20, 90, 22, TextAnchor.UpperRight); Ui.Click(_stage, "Btn_LbSideViewFriends", LbRX + LbRW - 122, y + 12, 112, 36, () => { _lbScope = BoardScope.Friends; _lbPage = 1; Debug.Log("[Leaderboards] scope=friends"); RefreshBoards(); });
            Ui.Flat(_stage, "Div", new Color(1, 1, 1, 0.06f), LbRX + 16, y + 54, LbRW - 32, 1.5f);
            if (!LbHasData(ld)) { if (ld.State == LoadState.Unavailable) { Ui.Label(_stage, "Unable to load friends ranking.", "SemiBold", 17, Brand.Bad, LbRX + 20, y + 90, LbRW - 40, 24); FriButton(_stage, "Btn_LbRetryFriends", LbRX + 20, y + 128, 130, 38, "Retry", Gold, Ink, () => _ = LoadFriendsBoard(false), true, null, 16); } else Ui.Label(_stage, "Loading rankings...", "SemiBold", 17, Muted, LbRX, y + 110, LbRW, 24, TextAnchor.UpperCenter); return; }
            var rows = Rows(ld.Value);
            if (rows.Count <= 1) { Ui.Label(_stage, "You don't have any ranked friends yet.", "SemiBold", 17, Muted, LbRX + 20, y + 100, LbRW - 40, 48, TextAnchor.UpperCenter); FriButton(_stage, "Btn_LbSideFindFriends", LbRX + LbRW / 2 - 80, y + 170, 160, 40, "Find friends", Gold, Ink, () => OpenFriends(), true, null, 16); return; }
            string myId = MyId(); var shown = rows.Take(5).ToList(); if (!shown.Any(r => LeaderboardRules.IsMe(r, myId))) { var mine = rows.FirstOrDefault(r => LeaderboardRules.IsMe(r, myId)); if (mine != null) { shown.RemoveAt(shown.Count - 1); shown.Add(mine); } }
            for (int i = 0; i < shown.Count; i++)
            {
                var r = shown[i]; float ry = y + 62 + i * 44; bool me = LeaderboardRules.IsMe(r, myId); int medal = LeaderboardRules.MedalOf(r.Rank);
                if (me) Ui.Outlined(_stage, "MeRow", new Color(Gold.r, Gold.g, Gold.b, 0.10f), new Color(Gold.r, Gold.g, Gold.b, 0.85f), LbRX + 10, ry, LbRW - 20, 40, 10, 1.6f);
                Ui.Label(_stage, r.Rank.ToString(), "Bold", 18, medal > 0 ? Medal(medal) : White, LbRX + 22, ry + 8, 30, 24); FriAvatar(_stage, new PersonCard { Id = r.Id, Name = r.Name }, LbRX + 54, ry + 4, 32, false);
                var nm = Ui.Label(_stage, r.Name, me ? "Bold" : "SemiBold", 17, White, LbRX + 96, ry + 8, 180, 24); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 11; nm.resizeTextMaxSize = 17; nm.horizontalOverflow = HorizontalWrapMode.Wrap; nm.verticalOverflow = VerticalWrapMode.Truncate;
                Ui.Label(_stage, LeaderboardRules.FormatBare(_lbMetric, r.Value), "Bold", 17, me ? Gold : White, LbRX + LbRW - 20 - 150, ry + 8, 150, 24, TextAnchor.UpperRight);
                Ui.Click(_stage, "Btn_LbFriend_" + r.Username, LbRX + 10, ry, LbRW - 20, 40, () => LbOpenProfile(r));
            }
        }

        void BuildLbWeeklyPanel()
        {
            const float y = LbLY + 572, h = 790 - 572 - 0; var ld = _svc.Boards.Weekly;
            Ui.Outlined(_stage, "WkPanel", PanelFill, PanelLine, LbRX, y, LbRW, h, 22, 1.5f);
            Ui.Label(_stage, "Top Drivers This Week", "ExtraBold", 24, White, LbRX + 20, y + 14, 420, 34); Ui.Flat(_stage, "Div", new Color(1, 1, 1, 0.06f), LbRX + 16, y + 54, LbRW - 32, 1.5f);
            if (!LbHasData(ld)) { if (ld.State == LoadState.Unavailable) { Ui.Label(_stage, "Unable to load this week.", "SemiBold", 17, Brand.Bad, LbRX + 20, y + 76, LbRW - 40, 24); FriButton(_stage, "Btn_LbRetryWeekly", LbRX + 20, y + 112, 130, 38, "Retry", Gold, Ink, () => _ = LoadWeeklyTop(false), true, null, 16); } else Ui.Label(_stage, "Loading...", "SemiBold", 17, Muted, LbRX, y + 90, LbRW, 24, TextAnchor.UpperCenter); return; }
            var rows = Rows(ld.Value); if (rows.Count == 0) { Ui.Label(_stage, "No jobs completed yet this week.", "SemiBold", 17, Muted, LbRX, y + 84, LbRW, 24, TextAnchor.UpperCenter); Ui.Label(_stage, "Be the first on the board.", "Regular", 15, Muted, LbRX, y + 110, LbRW, 22, TextAnchor.UpperCenter); return; }
            for (int i = 0; i < Math.Min(3, rows.Count); i++)
            {
                var r = rows[i]; float ry = y + 60 + i * 56; Color mc = Medal(Math.Min(3, Math.Max(1, r.Rank)));
                Ui.Disc(_stage, "Medal", mc, LbRX + 18, ry + 8, 34); Ui.Label(_stage, r.Rank.ToString(), "ExtraBold", 18, Ink, LbRX + 18, ry + 8, 34, 34, TextAnchor.MiddleCenter);
                FriAvatar(_stage, new PersonCard { Id = r.Id, Name = r.Name }, LbRX + 62, ry + 4, 42, false);
                var nm = Ui.Label(_stage, r.Name, "Bold", 17, White, LbRX + 114, ry + 4, 170, 24); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 11; nm.resizeTextMaxSize = 17; nm.horizontalOverflow = HorizontalWrapMode.Wrap; nm.verticalOverflow = VerticalWrapMode.Truncate;
                Ui.TextWithCountry(_stage, "Level " + r.Level, r.Country, "Regular", 13, Muted, LbRX + 114, ry + 28, 200, 18, 20);
                Ui.Label(_stage, LeaderboardRules.FormatValue(BoardMetric.Jobs, r.Value), "Bold", 17, Gold, LbRX + LbRW - 20 - 110, ry + 12, 110, 24, TextAnchor.UpperRight);
                Ui.Click(_stage, "Btn_LbWeekly_" + r.Username, LbRX + 10, ry, LbRW - 20, 52, () => LbOpenProfile(r));
            }
        }

        // ---------------------------------------------------------------- the period menu
        void BuildLbPop()
        {
            if (!_lbPeriodPop) return;
            Ui.Click(_stage, "Btn_LbPopClose", 0, 0, 1920, 1080, () => { _lbPeriodPop = false; Refill("LbPop", BuildLbPop); Refill("LbTabs", BuildLbTabs); }).GetComponent<HoverGlow>().enabled = false;
            const float w = 200, ih = 44; float x = _lbPeriodX, y = 207 + 50, h = 2 * ih + 8;
            Ui.Outlined(_stage, "PeriodList", new Color(0.07f, 0.078f, 0.10f, 1f), new Color(Gold.r, Gold.g, Gold.b, 0.6f), x, y, w, h, 11, 1.5f);
            int i = 0;
            foreach (BoardPeriod p in new[] { BoardPeriod.AllTime, BoardPeriod.Week })
            {
                float iy = y + 4 + i * ih; bool on = p == _lbPeriod; bool ok = LeaderboardRules.PeriodSupported(_lbMetric, p); if (on) Ui.Rounded(_stage, "Cur", new Color(Gold.r, Gold.g, Gold.b, 0.22f), x + 4, iy, w - 8, ih - 2, 8);
                Ui.Label(_stage, LeaderboardRules.PeriodLabel(p) + (ok ? "" : "  (all time)"), on ? "Bold" : "Regular", 17, on ? Gold : White, x + 16, iy + 9, w - 30, 26); var pp = p;
                Ui.Click(_stage, "Btn_LbPeriodOpt_" + p, x + 4, iy, w - 8, ih - 2, () => { _lbPeriodPop = false; if (_lbPeriod != pp) { _lbPeriod = pp; Debug.Log("[Leaderboards] period=" + LeaderboardRules.PeriodKey(EffPeriod) + (LeaderboardRules.PeriodSupported(_lbMetric, pp) ? "" : " (weekly is not supported for this board, showing all time)")); LbBoardChanged(); } else RefreshBoards(); }); i++;
            }
        }

        // ================================================================================================================ update
        void LbUpdate(float now)
        {
            if (_lbSearchDue > 0f && now > _lbSearchDue) { _lbSearchDue = -1f; string q = (_lbSearchPending ?? "").Trim(); if (q != _lbSearch) _ = RunLbSearch(q); }
            if (now > _nextLb) { _nextLb = now + 30f; if (_modal == null && _lbSearch.Length < 2) LoadBoards(true); }
            bool typing = _lbSearchField != null && _lbSearchField.isFocused;
            try
            {
                if (typing && (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter))) { _lbSearchDue = -1f; _ = RunLbSearch(_lbSearchPending); }
                if (!typing && UnityEngine.Input.GetKeyDown(KeyCode.Escape)) { if (_modal != null) CloseModal(); else if (_lbPeriodPop) { _lbPeriodPop = false; RefreshBoards(); } }
            }
            catch (InvalidOperationException) { }
        }
    }
}
