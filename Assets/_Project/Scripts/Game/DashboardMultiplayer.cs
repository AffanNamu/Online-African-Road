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
    /// The Multiplayer page of the dashboard: find or create a convoy, pick the route, invite and join players, wait in the lobby, ready check, start driving.
    /// Everything shown is the server's: convoys (list_open_convoys), the player's own convoy (get_my_convoy: members, ready flags, route, phase),
    /// chat (list_convoy_messages), online players (list_active_players), friends and server status. Every action (create, join, route, ready, start,
    /// kick, invite, post) is a server call whose refusal is shown to the player; the client never assumes it succeeded.
    /// Not available and said so on screen: voice chat, global chat (needs moderation), server selection.
    /// </summary>
    public sealed partial class DashboardScreen
    {
        enum MpTab { Convoys, Players, Friends, Voice, Servers }
        static readonly (MpTab tab, string label, string icon)[] MpTabs =
        {
            (MpTab.Convoys, "Find Convoys", "ic_box"), (MpTab.Players, "Active Players", "ic_people"), (MpTab.Friends, "Friends", "ic_user_circle"),
            (MpTab.Voice, "Voice Chat", "ic_bell"), (MpTab.Servers, "Server List", "ic_gauge"),
        };
        static readonly (string key, string label)[] ConvoyTags =
        {
            ("jobs", "Jobs"), ("freight", "Freight"), ("chill_drive", "Chill Drive"), ("realistic_driving", "Realistic Driving"), ("training", "Training"),
            ("help_support", "Help & Support"), ("friendly", "Friendly"), ("long_haul", "Long Haul"), ("events", "Events"),
        };
        static readonly Color GoGreen = new Color(0.20f, 0.78f, 0.38f, 1f);

        // ---- state (all of it a copy of what the server last said)
        MpTab _mpTab = MpTab.Convoys; string _mpSearch = "", _mpSearchPending = "", _mpTag; float _mpSearchDue = -1f;
        OpenConvoy[] _mpConvoys = new OpenConvoy[0]; int _mpConvoyTotal; LoadState _mpConvoyState = LoadState.Idle; string _mpConvoyErr;
        ActivePlayer[] _mpPlayers = new ActivePlayer[0]; int _mpPlayerTotal; LoadState _mpPlayerState = LoadState.Idle; string _mpPlayerErr;
        LoadState _mpFriendState = LoadState.Idle; string _mpFriendErr;
        ServerStatus _mpStatus; double _mpPing = -1;
        ConvoyInfo _mpMine; LoadState _mpMineState = LoadState.Idle; string _mpMineErr;
        ConvoyInviteItem[] _mpInvites = new ConvoyInviteItem[0];
        readonly List<ChatLine> _chat = new List<ChatLine>(); long _chatLast; string _chatTab = "convoy"; bool _chatStick = true; string _chatConvoy;
        bool _mpBusy, _mpLoaded; float _nextMine, _nextChat, _nextLists; int _mpToken, _mpFriendToken;
        InputField _mpSearchField, _chatField; RectTransform _modal; string _phaseSeen;
        readonly List<ScrollArea> _areas = new List<ScrollArea>(); readonly Dictionary<string, float> _scrollPos = new Dictionary<string, float>();

        sealed class ScrollArea { public string Name; public RectTransform View, Content; public float Height, ContentH, Pos; public Image Thumb; public float X, Y; public float Max => Mathf.Max(0f, ContentH - Height); }

        public void OpenMultiplayer() => ShowPage(Page.Multiplayer);

        void MpOpened()
        {
            if (!_mpLoaded || Time.unscaledTime - _nextLists > 30f) { _mpLoaded = true; _ = MpReloadAll(); }
            _nextMine = Time.unscaledTime + 3f; _nextChat = Time.unscaledTime + 2.5f; _nextLists = Time.unscaledTime + 15f;
        }

        /// <summary>GameFlow tells the page when the player leaves a convoy or a drive ends elsewhere.</summary>
        public void ReloadMultiplayer() { if (_page == Page.Multiplayer) _ = MpReloadAll(); else _mpLoaded = false; }

        // ================================================================================================================ build
        void BuildMultiplayerPage()
        {
            Region("MpTop", 0.02f); BuildMpTop();
            Region("MpTabs", 0.05f); BuildMpTabs();
            Region("MpMain", 0.08f); BuildMpMain();
            Region("MpConvoy", 0.12f); BuildMpConvoy();
            Region("MpChat", 0.16f); BuildMpChat();
            Region("MpChatLines", 0.16f); BuildChatLines();
        }

        void BuildMpTop()
        {
            Ui.Icon(_stage, "ic_people", Gold, 322, 138, 56);
            var t = Ui.Label(_stage, "Multiplayer", "ExtraBold", 52, White, 396, 128, 600, 64); Ui.Drop(t, 0.5f);
            Ui.Label(_stage, "Drive together, join convoys, meet other drivers and explore Africa as a community.", "Regular", 20, new Color(0.84f, 0.86f, 0.9f), 396, 186, 1000, 28);
            const float sx = 1542, sy = 134, sw = 355, sh = 50;
            Ui.Outlined(_stage, "MpSearchBox", new Color(0.05f, 0.055f, 0.075f, 0.92f), new Color(1, 1, 1, 0.16f), sx, sy, sw, sh, 14);
            var c = new Color(0.82f, 0.84f, 0.88f);
            Ui.Disc(_stage, "SearchRing", c, sx + 16, sy + 12, 19); Ui.Disc(_stage, "SearchHole", new Color(0.05f, 0.055f, 0.075f, 1f), sx + 18, sy + 14, 15); Ui.Segment(_stage, new Vector2(sx + 32, sy + 28), new Vector2(sx + 41, sy + 37), 3.5f, c);
            var areaGo = new GameObject("MpSearchArea", typeof(RectTransform), typeof(RectMask2D));
            var area = Ui.Place(areaGo, _stage, Ui.Anchor.TopLeft, sx + 52, sy, sw - 66, sh);
            var txt = Ui.Label(area, "", "Regular", 19, White, 0, 0, sw - 66, sh, TextAnchor.MiddleLeft);
            var ph = Ui.Label(area, "Search players, convoys or routes...", "Regular", 19, new Color(1, 1, 1, 0.5f), 0, 0, sw - 66, sh, TextAnchor.MiddleLeft);
            var hit = areaGo.AddComponent<Image>(); hit.color = new Color(1, 1, 1, 0.001f); areaGo.name = "Mp_Search_Input";
            var f = areaGo.AddComponent<InputField>(); f.targetGraphic = hit;
            f.textComponent = txt; f.placeholder = ph; f.lineType = InputField.LineType.SingleLine; f.characterLimit = 40; f.text = _mpSearch; f.transition = Selectable.Transition.None;
            f.customCaretColor = true; f.caretColor = Gold; f.selectionColor = new Color(Gold.r, Gold.g, Gold.b, 0.35f);
            f.onValueChanged.AddListener(v => { _mpSearchPending = v ?? ""; _mpSearchDue = Time.unscaledTime + 0.45f; });
            _mpSearchField = f;
        }

        void BuildMpTabs()
        {
            float x = 308;
            foreach (var t in MpTabs)
            {
                bool on = t.tab == _mpTab;
                var probe = Ui.Label(_stage, t.label, on ? "Bold" : "SemiBold", 19, White, 0, 0, 10, 10); float tw = probe.preferredWidth; Destroy(probe.gameObject);
                float w = 22 + 26 + 10 + tw + 22, y = 207, h = 46;
                if (on) Ui.Rounded(_stage, "TabOn", Gold, x, y, w, h, 12); else Ui.Outlined(_stage, "Tab", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.14f), x, y, w, h, 12);
                bool soon = t.tab == MpTab.Voice || t.tab == MpTab.Servers;
                Ui.Icon(_stage, t.icon, on ? Ink : soon ? new Color(0.6f, 0.62f, 0.68f) : new Color(0.86f, 0.88f, 0.92f), x + 18, y + 10, 26);
                Ui.Label(_stage, t.label, on ? "Bold" : "SemiBold", 19, on ? Ink : soon ? new Color(0.7f, 0.72f, 0.78f) : White, x + 54, y + 10, tw + 6, 28);
                var tab = t.tab; Ui.Click(_stage, "Btn_MpTab_" + tab, x, y, w, h, () => SetMpTab(tab));
                x += w + 12;
            }
        }

        void SetMpTab(MpTab tab)
        {
            if (_mpTab == tab) return;
            _mpTab = tab; _mpSearch = ""; _mpSearchPending = ""; if (_mpSearchField != null) _mpSearchField.text = "";
            Refill("MpTabs", BuildMpTabs); Refill("MpMain", BuildMpMain);
            if (tab == MpTab.Players) _ = LoadPlayers(); else if (tab == MpTab.Friends) _ = LoadFriendsList(); else if (tab == MpTab.Convoys) _ = LoadConvoys();
            Debug.Log("[Multiplayer] tab=" + tab.ToString().ToLowerInvariant());
        }

        void BuildMpMain()
        {
            _areas.RemoveAll(a => a.Name.StartsWith("mp_main") || a.View == null);
            switch (_mpTab)
            {
                case MpTab.Convoys: BuildFindConvoys(); break;
                case MpTab.Players: BuildPlayersTab(); break;
                case MpTab.Friends: BuildFriendsTab(); break;
                case MpTab.Voice: BuildSoon("Voice chat is not available yet", "Talking to your convoy by voice needs a voice service that is not connected to the game yet. Use the convoy chat in the meantime.", "ic_bell"); break;
                case MpTab.Servers: BuildSoon("Server selection is not available yet", "There is one game server for everyone right now; you can see how many players and convoys it holds in Server Status on the Find Convoys tab.", "ic_gauge"); break;
            }
        }

        void BuildSoon(string title, string body, string icon)
        {
            Ui.Outlined(_stage, "SoonPanel", PanelFill, PanelLine, 308, 276, 1120, 786, 20);
            Ui.Icon(_stage, icon, new Color(1, 1, 1, 0.18f), 808, 480, 120);
            Ui.Label(_stage, title, "Bold", 30, White, 308, 640, 1120, 40, TextAnchor.UpperCenter);
            Ui.Paragraph(_stage, body, "Regular", 20, Soft, 508, 690, 720, 90, TextAnchor.UpperCenter);
        }

        // ---- Find Convoys: banner, available convoys, live players, server status
        void BuildFindConvoys()
        {
            BuildBanner();
            BuildAvailableConvoys();
            BuildLivePlayers();
            BuildServerStatus();
        }

        void BuildBanner()
        {
            const float X = 308, Y = 263, W = 1120, H = 260;
            var tex = Brand.Scene();
            Ui.Outlined(_stage, "BannerFrame", new Color(0, 0, 0, 1), new Color(Gold.r, Gold.g, Gold.b, 0.55f), X, Y, W, H, 20, 2f);
            Ui.Photo(_stage, "BannerPhoto", tex, Ui.Cover(tex, 0.18f, 0.18f, 0.72f, (W - 4f) / (H - 4f)), X + 2, Y + 2, W - 4, H - 4, 18);
            Ui.Gradient(_stage, "BannerShade", new Color(0.02f, 0.025f, 0.04f, 0.9f), new Color(0.02f, 0.025f, 0.04f, 0.05f), true, X + 2, Y + 2, W * 0.62f, H - 4);
            var d = Ui.Label(_stage, "DRIVE", "ExtraBold", 66, White, X + 30, Y + 12, 500, 76); Ui.Drop(d, 0.6f);
            var t = Ui.Label(_stage, "TOGETHER", "ExtraBold", 66, Gold, X + 30, Y + 78, 600, 76); Ui.Drop(t, 0.6f);
            Ui.Paragraph(_stage, "Join convoys, complete jobs together and explore the highways of Africa with real players.", "Regular", 20, new Color(0.92f, 0.94f, 0.97f), X + 30, Y + 150, 480, 60);
            Ui.Rounded(_stage, "CreateBtn", Gold, X + 30, Y + 202, 230, 46, 12);
            Ui.Label(_stage, "Create Convoy", "Bold", 21, Ink, X + 30, Y + 211, 190, 30, TextAnchor.UpperCenter); Ui.Icon(_stage, "icon_arrow", Ink, X + 224, Y + 212, 26);
            Ui.Click(_stage, "Btn_CreateConvoy", X + 30, Y + 202, 230, 46, OpenCreateDialog);
            Ui.Rounded(_stage, "BannerFeatures", new Color(0.02f, 0.025f, 0.04f, 0.72f), X + W - 290, Y + 150, 262, 96, 14);
            var feats = new (string icon, string label)[] { ("ic_pin", "Safer Routes"), ("ic_coin", "More Rewards"), ("ic_people", "Real Community") };
            for (int i = 0; i < 3; i++) { Ui.Icon(_stage, feats[i].icon, Gold, X + W - 272, Y + 156 + i * 29, 22); Ui.Label(_stage, feats[i].label, "SemiBold", 18, White, X + W - 240, Y + 156 + i * 29, 210, 24); }
        }

        void Pill2(float x, float y, string text, Color c, int size = 14, float? w = null)
        {
            var probe = Ui.Label(_stage, text, "SemiBold", size, c, 0, 0, 10, 10); float tw = probe.preferredWidth; Destroy(probe.gameObject);
            float bw = w ?? tw + 18;
            Ui.Outlined(_stage, "Chip", new Color(c.r * 0.2f, c.g * 0.2f, c.b * 0.2f, 0.9f), new Color(c.r, c.g, c.b, 0.45f), x, y, bw, size + 10, 7, 1.2f);
            Ui.Label(_stage, text, "SemiBold", size, c, x, y + 4, bw, size + 4, TextAnchor.UpperCenter);
        }

        static string TagLabel(string key) { foreach (var t in ConvoyTags) if (t.key == key) return t.label; return key; }
        static string LangLabel(string code) { switch (code) { case "en": return "English"; case "fr": return "French"; case "ha": return "Hausa"; case "yo": return "Yoruba"; case "ig": return "Igbo"; case "pt": return "Portuguese"; case "ar": return "Arabic"; case "sw": return "Swahili"; default: return (code ?? "").ToUpperInvariant(); } }

        void BuildAvailableConvoys()
        {
            const float X = 308, Y = 536, W = 660, H = 526;
            Ui.Outlined(_stage, "AvailPanel", PanelFill, PanelLine, X, Y, W, H, 20);
            Ui.Label(_stage, "Available Convoys", "Bold", 26, White, X + 20, Y + 12, 300, 34);
            string count = _mpConvoyState == LoadState.Ready || _mpConvoyState == LoadState.Empty ? _mpConvoyTotal.ToString() : "-";
            var c = Ui.Label(_stage, "Available Convoys", "Bold", 26, White, 0, 0, 10, 10); float cw = c.preferredWidth; Destroy(c.gameObject);
            Ui.Rounded(_stage, "CountPill", new Color(1, 1, 1, 0.12f), X + 30 + cw, Y + 15, 40, 28, 14); Ui.Label(_stage, count, "SemiBold", 17, White, X + 30 + cw, Y + 18, 40, 24, TextAnchor.UpperCenter);
            SmallPillIn(_stage, _mpTag == null ? "Filters" : TagLabel(_mpTag) + "  x", X + W - 132, Y + 14, 112, 30, OpenFilterDialog);
            float ry = Y + 56;
            if (_mpConvoyState == LoadState.Loading || _mpConvoyState == LoadState.Idle) { Ui.Label(_stage, "Loading convoys...", "Regular", 18, Muted, X + 24, ry + 12, 400, 26); return; }
            if (_mpConvoyState == LoadState.Unavailable) { MpMessage(X + 24, ry + 10, W - 48, "ic_road", "We could not load the convoys", _mpConvoyErr ?? "Please try again.", Brand.Bad, "Try again", "Btn_RetryConvoys", () => _ = LoadConvoys()); return; }
            if (_mpConvoys.Length == 0)
            {
                bool filtered = _mpTag != null || !string.IsNullOrEmpty(_mpSearch);
                MpMessage(X + 24, ry + 10, W - 48, "ic_people", filtered ? "No convoys match" : "No open convoys right now", filtered ? "Try another filter or search." : "Be the first: create a convoy and invite your friends.", White,
                    filtered ? "Clear filters" : "Create Convoy", "Btn_ConvoysEmptyAction", () => { if (filtered) { _mpTag = null; _mpSearch = ""; _mpSearchPending = ""; if (_mpSearchField != null) _mpSearchField.text = ""; _ = LoadConvoys(); } else OpenCreateDialog(); });
                return;
            }
            const float rowH = 112, pitch = 118;
            var area = MakeScroll("mp_main_convoys", X + 10, ry, W - 20, H - 66, _mpConvoys.Length * pitch + 6);
            var tex = Brand.Scene();
            for (int i = 0; i < _mpConvoys.Length; i++)
            {
                var cv = _mpConvoys[i]; float y = 2 + i * pitch, x = 4, w = W - 40;
                var host = area.Content;
                Ui.Outlined(host, "ConvoyRow", CardFill, new Color(1, 1, 1, 0.10f), x, y, w, rowH, 14, 1.5f);
                float u = (i * 0.13f) % 0.5f;
                Ui.Photo(host, "ConvoyPhoto", tex, Ui.Cover(tex, u, 0.30f, 0.28f, 172f / (rowH - 4f)), x + 2, y + 2, 172, rowH - 4, 12);
                float tx = x + 190;
                var nm = Ui.Label(host, cv.name, "Bold", 21, White, tx, y + 8, 300, 28); nm.horizontalOverflow = HorizontalWrapMode.Wrap; nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 14; nm.resizeTextMaxSize = 21; nm.verticalOverflow = VerticalWrapMode.Truncate;
                bool driving = cv.phase == "driving", full = !cv.joinable;
                string badge = driving ? "In Progress" : full ? "Full" : "Open"; Color bc = driving ? Blue : full ? Muted : Brand.Good;
                var bt = Ui.Label(host, cv.name, "Bold", 21, White, 0, 0, 10, 10); float nw = Mathf.Min(nm.preferredWidth, 300f); Destroy(bt.gameObject);
                MiniChip(host, tx + nw + 12, y + 10, badge, bc);
                Ui.Label(host, "led by " + (cv.leader ?? "?"), "Regular", 14, Muted, tx, y + 36, 300, 20);
                Ui.Icon(host, "ic_people", Gold, tx, y + 58, 20); Ui.Label(host, cv.member_count + "/" + cv.capacity, "SemiBold", 16, White, tx + 26, y + 58, 60, 22);
                var rt = cv.route != null && cv.route.Exists ? cv.route : null;
                Ui.Icon(host, "ic_road", Gold, tx + 88, y + 58, 20); Ui.Label(host, rt != null ? (rt.distance_km > 0 ? $"{rt.distance_km:0.#} km" : "Route set") : "No route yet", "SemiBold", 16, White, tx + 114, y + 58, 110, 22);
                if (rt != null && rt.est_minutes > 0) { Ui.Icon(host, "ic_clock", Gold, tx + 232, y + 58, 20); Ui.Label(host, JobMath.FormatDuration(rt.est_minutes), "SemiBold", 16, White, tx + 258, y + 58, 90, 22); }
                float cx = tx;
                foreach (var tg in (cv.tags ?? new string[0]).Take(2)) { cx += MiniChip(host, cx, y + 84, TagLabel(tg), new Color(0.85f, 0.88f, 0.92f)) + 6; }
                if (!string.IsNullOrEmpty(cv.language)) MiniChip(host, cx, y + 84, LangLabel(cv.language), new Color(0.85f, 0.88f, 0.92f));
                bool can = cv.joinable && _mpMine == null;
                Ui.Rounded(host, "JoinBg", can ? Gold : new Color(0.2f, 0.22f, 0.27f), x + w - 118, y + 36, 100, 42, 11);
                Ui.Label(host, "Join", "Bold", 20, can ? Ink : Muted, x + w - 118, y + 44, 100, 28, TextAnchor.UpperCenter);
                var jc = cv; Ui.Click(host, "Btn_JoinConvoy_" + SafeName(cv.name), x + w - 118, y + 36, 100, 42, () => { if (_mpMine != null) Toast("Leave your current convoy first."); else if (!jc.joinable) Toast("That convoy is full."); else _ = JoinConvoy(jc); });
            }
            RestoreScroll(area);
        }

        static string SafeName(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray());

        float MiniChip(Transform host, float x, float y, string text, Color c)
        {
            var probe = Ui.Label(host, text, "SemiBold", 13, c, 0, 0, 10, 10); float tw = probe.preferredWidth; Destroy(probe.gameObject);
            float w = tw + 16;
            Ui.Outlined(host, "MiniChip", new Color(c.r * 0.18f, c.g * 0.18f, c.b * 0.18f, 0.9f), new Color(c.r, c.g, c.b, 0.4f), x, y, w, 22, 6, 1.1f);
            Ui.Label(host, text, "SemiBold", 13, c, x, y + 3, w, 18, TextAnchor.UpperCenter);
            return w;
        }

        void MpMessage(float x, float y, float w, string icon, string title, string body, Color titleColor, string button, string buttonName, Action onClick)
        {
            Ui.Icon(_stage, icon, new Color(1, 1, 1, 0.22f), x + 14, y + 40, 64);
            Ui.Label(_stage, title, "Bold", 24, titleColor, x + 96, y + 36, w - 100, 32);
            Ui.Paragraph(_stage, body, "Regular", 17, Soft, x + 96, y + 70, w - 110, 50);
            Ui.Rounded(_stage, "MsgBtn", Gold, x + 96, y + 130, 190, 44, 11); Ui.Label(_stage, button, "Bold", 18, Ink, x + 96, y + 139, 190, 28, TextAnchor.UpperCenter);
            Ui.Click(_stage, buttonName, x + 96, y + 130, 190, 44, onClick);
        }

        void BuildLivePlayers()
        {
            const float X = 984, Y = 536, W = 444, H = 380;
            Ui.Outlined(_stage, "LivePanel", PanelFill, PanelLine, X, Y, W, H, 20);
            Ui.Label(_stage, "Live Players", "Bold", 26, White, X + 20, Y + 12, 240, 34);
            string count = _mpPlayerState == LoadState.Ready || _mpPlayerState == LoadState.Empty ? _mpPlayerTotal.ToString() : "-";
            Ui.Rounded(_stage, "LiveCount", new Color(1, 1, 1, 0.12f), X + 176, Y + 15, 40, 28, 14); Ui.Label(_stage, count, "SemiBold", 17, White, X + 176, Y + 18, 40, 24, TextAnchor.UpperCenter);
            SmallPillIn(_stage, "View All", X + W - 112, Y + 14, 92, 30, () => SetMpTab(MpTab.Players));
            Ui.Label(_stage, "Player", "Regular", 14, Muted, X + 56, Y + 58, 120, 18); Ui.Label(_stage, "Level", "Regular", 14, Muted, X + 214, Y + 58, 60, 18);
            Ui.Label(_stage, "Truck", "Regular", 14, Muted, X + 262, Y + 58, 90, 18); Ui.Label(_stage, "Status", "Regular", 14, Muted, X + 372, Y + 58, 60, 18);
            if (_mpPlayerState == LoadState.Idle || _mpPlayerState == LoadState.Loading) { Ui.Label(_stage, "Loading players...", "Regular", 17, Muted, X + 22, Y + 100, 300, 24); return; }
            if (_mpPlayerState == LoadState.Unavailable) { Ui.Label(_stage, "Players are unavailable right now.", "SemiBold", 17, Brand.Bad, X + 22, Y + 100, 400, 24); Ui.Rounded(_stage, "RetryBg", Gold, X + 22, Y + 136, 150, 38, 10); Ui.Label(_stage, "Try again", "Bold", 17, Ink, X + 22, Y + 143, 150, 24, TextAnchor.UpperCenter); Ui.Click(_stage, "Btn_RetryPlayers2", X + 22, Y + 136, 150, 38, () => _ = LoadPlayers()); return; }
            if (_mpPlayers.Length == 0) { Ui.Label(_stage, "No other players are online right now.", "SemiBold", 17, Muted, X + 22, Y + 100, 400, 24); return; }
            for (int i = 0; i < Mathf.Min(8, _mpPlayers.Length); i++)
            {
                var p = _mpPlayers[i]; float y = Y + 82 + i * 36;
                if (i > 0) Ui.Flat(_stage, "RowLine", new Color(1, 1, 1, 0.05f), X + 16, y - 3, W - 32, 1);
                Ui.Disc(_stage, "PDot", Brand.Good, X + 20, y + 11, 10); Ui.Disc(_stage, "PAv", new Color(0.16f, 0.18f, 0.24f), X + 36, y + 2, 26); Ui.Icon(_stage, "avatar_default", White, X + 36, y + 2, 26);
                var n = Ui.Label(_stage, p.display_name, "Regular", 15, White, X + 70, y + 6, 142, 22); n.horizontalOverflow = HorizontalWrapMode.Wrap; n.verticalOverflow = VerticalWrapMode.Truncate; n.resizeTextForBestFit = true; n.resizeTextMinSize = 11; n.resizeTextMaxSize = 15;
                Ui.Label(_stage, p.level.ToString(), "Regular", 15, White, X + 216, y + 6, 40, 22);
                var v = Ui.Label(_stage, p.vehicle ?? "-", "Regular", 14, Soft, X + 262, y + 7, 106, 22); v.horizontalOverflow = HorizontalWrapMode.Wrap; v.verticalOverflow = VerticalWrapMode.Truncate; v.resizeTextForBestFit = true; v.resizeTextMinSize = 10; v.resizeTextMaxSize = 14;
                Ui.Label(_stage, StatusOf(p), "Regular", 14, p.in_convoy ? Blue : Brand.Good, X + 372, y + 7, 70, 22);
            }
        }

        static string StatusOf(ActivePlayer p) => p.in_convoy ? "Convoy" : string.IsNullOrEmpty(p.activity) ? "Online" : char.ToUpperInvariant(p.activity[0]) + p.activity.Substring(1);

        void BuildServerStatus()
        {
            const float X = 984, Y = 930, W = 444, H = 132;
            Ui.Outlined(_stage, "StatusPanel", PanelFill, PanelLine, X, Y, W, H, 20);
            Ui.Label(_stage, "Server Status", "Bold", 24, White, X + 20, Y + 12, 240, 32);
            bool ok = _svc.Api.IsSignedIn && _svc.Presence.Connected; Color sc = ok ? Brand.Good : Brand.Bad;
            Ui.Disc(_stage, "StDot", sc, X + W - 106, Y + 20, 14); Ui.Label(_stage, ok ? "Online" : "Offline", "SemiBold", 19, sc, X + W - 86, Y + 14, 80, 28);
            var cells = new (string icon, string value, string label)[]
            {
                ("ic_people", _mpStatus != null ? Money.Format(_mpStatus.players_online) : "-", "Players Online"),
                ("ic_bus", _mpStatus != null ? Money.Format(_mpStatus.active_convoys) : "-", "Active Convoys"),
                ("ic_bars", _mpPing >= 0 ? Mathf.RoundToInt((float)_mpPing) + " ms" : "-", "Your Ping"),
            };
            float cw = (W - 20) / 3f;
            for (int i = 0; i < 3; i++)
            {
                float cx = X + 10 + i * cw;
                Ui.Icon(_stage, cells[i].icon, Brand.Good, cx + 10, Y + 58, 34);
                Ui.Label(_stage, cells[i].value, "Bold", 24, White, cx + 52, Y + 52, cw - 54, 30); Ui.Label(_stage, cells[i].label, "Regular", 14, Muted, cx + 52, Y + 84, cw - 54, 20);
            }
        }

        // ================================================================================================================ the other tabs
        void BuildPlayersTab()
        {
            const float X = 308, Y = 276, W = 1120, H = 786;
            Ui.Outlined(_stage, "PlayersPanel", PanelFill, PanelLine, X, Y, W, H, 20);
            Ui.Label(_stage, "Active Players", "Bold", 26, White, X + 24, Y + 14, 300, 34);
            string count = _mpPlayerState == LoadState.Ready || _mpPlayerState == LoadState.Empty ? _mpPlayerTotal.ToString() : "-";
            Ui.Rounded(_stage, "PCount", new Color(1, 1, 1, 0.12f), X + 222, Y + 17, 44, 28, 14); Ui.Label(_stage, count, "SemiBold", 17, White, X + 222, Y + 20, 44, 24, TextAnchor.UpperCenter);
            if (_mpPlayerState == LoadState.Idle || _mpPlayerState == LoadState.Loading) { Ui.Label(_stage, "Loading players...", "Regular", 19, Muted, X + 28, Y + 80, 400, 26); return; }
            if (_mpPlayerState == LoadState.Unavailable) { MpMessage(X + 24, Y + 50, W - 48, "ic_people", "We could not load the players", _mpPlayerErr ?? "Please try again.", Brand.Bad, "Try again", "Btn_RetryPlayers", () => _ = LoadPlayers()); return; }
            if (_mpPlayers.Length == 0) { MpMessage(X + 24, Y + 50, W - 48, "ic_people", string.IsNullOrEmpty(_mpSearch) ? "No other players are online" : "No players match", string.IsNullOrEmpty(_mpSearch) ? "Invite a friend, or check back in a little while." : "Try a different name.", White, "Refresh", "Btn_PlayersRefresh", () => _ = LoadPlayers()); return; }
            Ui.Label(_stage, "Player", "Regular", 15, Muted, X + 96, Y + 64, 200, 20); Ui.Label(_stage, "Level", "Regular", 15, Muted, X + 420, Y + 64, 80, 20); Ui.Label(_stage, "Truck", "Regular", 15, Muted, X + 520, Y + 64, 200, 20); Ui.Label(_stage, "Status", "Regular", 15, Muted, X + 760, Y + 64, 120, 20);
            const float rowH = 62;
            var area = MakeScroll("mp_main_players", X + 12, Y + 88, W - 24, H - 100, _mpPlayers.Length * rowH + 6);
            for (int i = 0; i < _mpPlayers.Length; i++)
            {
                var p = _mpPlayers[i]; float y = 2 + i * rowH; var host = area.Content;
                Ui.Outlined(host, "PRow", CardFill, new Color(1, 1, 1, 0.08f), 4, y, W - 56, rowH - 6, 12, 1.2f);
                Ui.Disc(host, "PDot", Brand.Good, 18, y + 21, 10); Ui.Disc(host, "PAv", new Color(0.16f, 0.18f, 0.24f), 36, y + 7, 40); Ui.Icon(host, "avatar_default", White, 36, y + 7, 40);
                Ui.Label(host, p.display_name, "Bold", 19, White, 88, y + 14, 300, 26);
                Ui.Label(host, p.level.ToString(), "SemiBold", 18, White, 408, y + 14, 70, 26); Ui.Label(host, p.vehicle ?? "-", "Regular", 17, Soft, 508, y + 15, 230, 26);
                Ui.Label(host, StatusOf(p), "SemiBold", 17, p.in_convoy ? Blue : Brand.Good, 748, y + 15, 120, 26);
                bool isFriend = p.friend_status == "friends", pending = p.friend_status == "request_sent" || p.friend_status == "request_received";
                if (_mpMine != null && _mpMine.is_leader && !p.in_convoy)
                {
                    Ui.Rounded(host, "InvBg", Gold, W - 300, y + 10, 110, 36, 10); Ui.Label(host, "Invite", "Bold", 17, Ink, W - 300, y + 16, 110, 26, TextAnchor.UpperCenter);
                    var pl = p; Ui.Click(host, "Btn_InvitePlayer_" + SafeName(p.display_name), W - 300, y + 10, 110, 36, () => _ = InvitePlayer(pl.player_id, pl.display_name));
                }
                if (!isFriend && !pending)
                {
                    Ui.Outlined(host, "AddBg", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.2f), W - 180, y + 10, 118, 36, 10, 1.2f); Ui.Label(host, "Add Friend", "SemiBold", 16, White, W - 180, y + 17, 118, 24, TextAnchor.UpperCenter);
                    var pl = p; Ui.Click(host, "Btn_AddFriend_" + SafeName(p.display_name), W - 180, y + 10, 118, 36, () => _ = AddFriend(pl.player_id, pl.display_name));
                }
                else Ui.Label(host, isFriend ? "Friends" : "Request sent", "SemiBold", 16, Brand.Good, W - 180, y + 17, 118, 24, TextAnchor.UpperCenter);
            }
            RestoreScroll(area);
        }

        void BuildFriendsTab()
        {
            const float X = 308, Y = 276, W = 1120, H = 786;
            Ui.Outlined(_stage, "FriendsPanel", PanelFill, PanelLine, X, Y, W, H, 20);
            Ui.Label(_stage, "Friends", "Bold", 26, White, X + 24, Y + 14, 300, 34);
            var f = _svc.Friends.Friends;
            if (_mpFriendState == LoadState.Idle || _mpFriendState == LoadState.Loading) { Ui.Label(_stage, "Loading friends...", "Regular", 19, Muted, X + 28, Y + 80, 400, 26); return; }
            if (_mpFriendState == LoadState.Unavailable) { MpMessage(X + 24, Y + 50, W - 48, "ic_people", "We could not load your friends", _mpFriendErr ?? "Please try again.", Brand.Bad, "Try again", "Btn_RetryFriends", () => _ = LoadFriendsList()); return; }
            var items = (f.Value != null && f.Value.items != null ? f.Value.items : new PersonDto[0]).Where(p => string.IsNullOrEmpty(_mpSearch) || p.display_name.IndexOf(_mpSearch, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            if (items.Length == 0) { MpMessage(X + 24, Y + 50, W - 48, "ic_people", string.IsNullOrEmpty(_mpSearch) ? "No friends yet" : "No friends match", string.IsNullOrEmpty(_mpSearch) ? "Add players you meet from the Active Players tab." : "Try a different name.", White, "Active Players", "Btn_FriendsToPlayers", () => SetMpTab(MpTab.Players)); return; }
            const float rowH = 62;
            var area = MakeScroll("mp_main_friends", X + 12, Y + 70, W - 24, H - 82, items.Length * rowH + 6);
            for (int i = 0; i < items.Length; i++)
            {
                var p = items[i]; float y = 2 + i * rowH; var host = area.Content;
                bool online = p.presence != null && p.presence.online;
                Ui.Outlined(host, "FRow", CardFill, new Color(1, 1, 1, 0.08f), 4, y, W - 56, rowH - 6, 12, 1.2f);
                Ui.Disc(host, "FDot", online ? Brand.Good : Muted, 18, y + 21, 10); Ui.Disc(host, "FAv", new Color(0.16f, 0.18f, 0.24f), 36, y + 7, 40); Ui.Icon(host, "avatar_default", White, 36, y + 7, 40);
                Ui.Label(host, p.display_name, "Bold", 19, White, 88, y + 14, 300, 26); Ui.Label(host, "Level " + p.level, "Regular", 17, Soft, 408, y + 15, 120, 26);
                string st = online ? (p.presence != null && !string.IsNullOrEmpty(p.presence.activity) ? char.ToUpperInvariant(p.presence.activity[0]) + p.presence.activity.Substring(1) : "Online") : "Offline";
                Ui.Label(host, st, "SemiBold", 17, online ? Brand.Good : Muted, 560, y + 15, 200, 26);
                if (_mpMine != null && _mpMine.is_leader)
                {
                    Ui.Rounded(host, "InvBg", Gold, W - 190, y + 10, 130, 36, 10); Ui.Label(host, "Invite", "Bold", 17, Ink, W - 190, y + 16, 130, 26, TextAnchor.UpperCenter);
                    var pl = p; Ui.Click(host, "Btn_InviteFriend_" + SafeName(p.display_name), W - 190, y + 10, 130, 36, () => _ = InvitePlayer(pl.player_id, pl.display_name));
                }
            }
            RestoreScroll(area);
        }

        // ================================================================================================================ your convoy
        void BuildMpConvoy()
        {
            const float X = 1442, Y = 263, W = 455, H = 481;
            Ui.Outlined(_stage, "YourConvoyPanel", PanelFill, PanelLine, X, Y, W, H, 20);
            Ui.Label(_stage, "Your Convoy", "Bold", 26, White, X + 20, Y + 12, 240, 34);
            _areas.RemoveAll(a => a.Name == "mp_members" || a.View == null);
            if (_mpMineState == LoadState.Idle || _mpMineState == LoadState.Loading) { Ui.Label(_stage, "Loading your convoy...", "Regular", 18, Muted, X + 22, Y + 70, 380, 26); return; }
            if (_mpMineState == LoadState.Unavailable) { Ui.Label(_stage, "Your convoy is unavailable.", "SemiBold", 19, Brand.Bad, X + 22, Y + 70, 400, 26); Ui.Paragraph(_stage, _mpMineErr ?? "Please try again.", "Regular", 16, Soft, X + 22, Y + 100, 400, 44); Ui.Rounded(_stage, "RetryBg", Gold, X + 22, Y + 160, 150, 40, 10); Ui.Label(_stage, "Try again", "Bold", 17, Ink, X + 22, Y + 168, 150, 24, TextAnchor.UpperCenter); Ui.Click(_stage, "Btn_RetryMine", X + 22, Y + 160, 150, 40, () => _ = LoadMine()); return; }
            if (_mpMine == null) { BuildNoConvoy(X, Y, W, H); return; }
            var c = _mpMine; bool leader = c.is_leader, driving = c.Driving;
            Color sc = driving ? GoGreen : Blue; string st = driving ? "On the Road" : "In Lobby";
            Ui.Disc(_stage, "PhaseDot", sc, X + 190, Y + 22, 12); Ui.Label(_stage, st, "SemiBold", 17, sc, X + 208, Y + 17, 120, 24);
            Ui.Label(_stage, c.member_count + "/" + c.capacity, "SemiBold", 18, White, X + W - 112, Y + 17, 60, 24, TextAnchor.UpperRight);
            Ui.Rounded(_stage, "GearBg", new Color(1, 1, 1, 0.10f), X + W - 46, Y + 12, 32, 32, 9); Ui.Icon(_stage, "ic_gear", White, X + W - 42, Y + 16, 24);
            Ui.Click(_stage, "Btn_ConvoyManage", X + W - 46, Y + 12, 32, 32, OpenManageDialog);
            // the one big action
            int waiting = c.members.Count(m => !m.is_leader && !m.ready); bool hasRoute = c.route != null && c.route.Exists;
            string label; Color bg; Action act; string btnName;
            if (driving) { label = leader ? "Rejoin the Road" : "Join the Road"; bg = GoGreen; btnName = "Btn_JoinRoad"; act = () => _act.ConvoyDrive(c); }
            else if (leader)
            {
                bool can = hasRoute && waiting == 0;
                label = !hasRoute ? "Choose a route to start" : waiting > 0 ? $"Waiting for {waiting} player{(waiting == 1 ? "" : "s")}..." : "Start Driving";
                bg = can ? GoGreen : new Color(0.20f, 0.22f, 0.27f); btnName = can ? "Btn_StartDriving" : "Btn_StartBlocked";
                act = () => { if (!hasRoute) { Toast("Choose a route first."); OpenRouteDialog(); } else if (waiting > 0) Toast("Everyone must be ready before you can start."); else _act.ConvoyDrive(c); };
            }
            else
            {
                var me = c.members.FirstOrDefault(m => m.player_id == _svc.Profile.id); bool ready = me != null && me.ready;
                label = ready ? "Ready - tap to cancel" : "I'm Ready"; bg = ready ? new Color(0.20f, 0.5f, 0.3f) : GoGreen; btnName = "Btn_Ready";
                act = () => { if (!hasRoute) Toast("The leader has not chosen a route yet."); else _ = SetReady(!ready); };
            }
            Ui.Rounded(_stage, "PrimaryBg", bg, X + 16, Y + 56, W - 32, 46, 12);
            Ui.Label(_stage, label, "Bold", 20, bg == GoGreen || bg.g > 0.4f ? Ink : Muted, X + 16, Y + 66, W - 32, 28, TextAnchor.UpperCenter);
            Ui.Click(_stage, btnName, X + 16, Y + 56, W - 32, 46, act);
            // route
            Ui.Label(_stage, "Route:", "Regular", 17, Soft, X + 20, Y + 116, 60, 24);
            string rn = hasRoute ? c.route.from + "  →  " + c.route.to : "Not chosen yet";
            var rl = Ui.Label(_stage, rn, "Bold", 17, hasRoute ? White : Muted, X + 76, Y + 115, 250, 24);
            Ui.Label(_stage, hasRoute && c.route.distance_km > 0 ? $"Distance: {c.route.distance_km:0.#} km  ·  {JobMath.FormatDuration(c.route.est_minutes)}" : "Distance: -", "Regular", 16, Soft, X + 20, Y + 142, 330, 22);
            if (leader && !driving) { SmallPillIn(_stage, hasRoute ? "Change" : "Choose", X + 330, Y + 114, 100, 28, OpenRouteDialog); }
            Pill2(X + W - 118, Y + 140, c.is_private ? "Invite only" : "Public", Gold, 15, 98);
            float tagX = X + 20; foreach (var tg in (c.tags ?? new string[0])) tagX += MiniChip(_stage, tagX, Y + 172, TagLabel(tg), new Color(0.85f, 0.88f, 0.92f)) + 6;
            // members
            Ui.Label(_stage, $"Members ({c.member_count}/{c.capacity})", "Bold", 20, White, X + 20, Y + 202, 250, 28);
            const float my = 234, mh = 240, rowH = 46;
            int slots = leader && !driving ? Mathf.Min(2, c.capacity - c.member_count) : 0;
            var area = MakeScroll("mp_members", X + 10, Y + my, W - 20, mh, c.members.Length * rowH + slots * 36 + 4);
            for (int i = 0; i < c.members.Length; i++)
            {
                var m = c.members[i]; float y = 2 + i * rowH; var host = area.Content;
                if (i > 0) Ui.Flat(host, "MLine", new Color(1, 1, 1, 0.06f), 6, y - 1, W - 52, 1);
                Ui.Disc(host, "MAv", new Color(0.16f, 0.18f, 0.24f), 8, y + 3, 40); Ui.Icon(host, "avatar_default", White, 8, y + 3, 40);
                var nm = Ui.Label(host, m.display_name, "Bold", 17, White, 58, y + 2, 170, 22); if (m.is_leader) { Ui.Icon(host, "ic_crown", Gold, 58 + Mathf.Min(nm.preferredWidth, 150f) + 6, y + 3, 18); }
                Ui.Label(host, "Level " + m.level, "Regular", 14, Muted, 58, y + 24, 100, 18);
                Ui.Icon(host, "ic_bus", Soft, 190, y + 11, 22); var vl = Ui.Label(host, m.vehicle ?? "-", "Regular", 14, Soft, 216, y + 6, 120, 34); vl.horizontalOverflow = HorizontalWrapMode.Wrap; vl.verticalOverflow = VerticalWrapMode.Truncate;
                string ms = m.is_leader ? "Leader" : driving ? "Driving" : m.ready ? "Ready" : "Not ready"; Color mc = m.is_leader ? White : (driving || m.ready) ? Brand.Good : Muted;
                Ui.Label(host, ms, "SemiBold", 15, mc, W - 120, y + 11, 90, 22, TextAnchor.UpperRight);
            }
            for (int k = 0; k < slots; k++)
            {
                float y = 2 + c.members.Length * rowH + k * 36; var host = area.Content;
                Ui.Outlined(host, "InviteSlot", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.12f), 6, y + 2, W - 52, 30, 8, 1.2f);
                Ui.Label(host, "+", "Bold", 22, White, 18, y + 1, 24, 30); Ui.Label(host, "Invite Player", "Regular", 16, Soft, 52, y + 6, 200, 22);
                Ui.Click(host, "Btn_InviteSlot" + k, 6, y + 2, W - 52, 30, OpenInviteDialog);
            }
            RestoreScroll(area);
        }

        void BuildNoConvoy(float X, float Y, float W, float H)
        {
            Ui.Icon(_stage, "ic_people", new Color(1, 1, 1, 0.2f), X + 20, Y + 64, 64);
            Ui.Label(_stage, "You are not in a convoy", "Bold", 21, White, X + 96, Y + 66, 340, 28);
            Ui.Paragraph(_stage, "Create one, join one from the list, or accept an invite.", "Regular", 16, Soft, X + 96, Y + 96, 340, 44);
            Ui.Rounded(_stage, "CreateBg", Gold, X + 20, Y + 156, W - 40, 44, 12); Ui.Label(_stage, "Create Convoy", "Bold", 19, Ink, X + 20, Y + 165, W - 40, 28, TextAnchor.UpperCenter);
            Ui.Click(_stage, "Btn_CreateConvoy2", X + 20, Y + 156, W - 40, 44, OpenCreateDialog);
            Ui.Label(_stage, _mpInvites.Length > 0 ? $"Invites ({_mpInvites.Length})" : "No pending invites", "Bold", 19, _mpInvites.Length > 0 ? White : Muted, X + 20, Y + 222, 300, 26);
            for (int i = 0; i < Mathf.Min(3, _mpInvites.Length); i++)
            {
                var inv = _mpInvites[i]; float y = Y + 254 + i * 72;
                Ui.Outlined(_stage, "InvRow", CardFill, new Color(1, 1, 1, 0.10f), X + 16, y, W - 32, 64, 12, 1.2f);
                Ui.Label(_stage, inv.convoy_name, "Bold", 17, White, X + 28, y + 6, 240, 24);
                Ui.Label(_stage, "from " + (inv.from != null ? inv.from.display_name : "?"), "Regular", 14, Muted, X + 28, y + 34, 240, 20);
                Ui.Rounded(_stage, "AccBg", GoGreen, X + W - 168, y + 14, 70, 36, 9); Ui.Label(_stage, "Accept", "Bold", 15, Ink, X + W - 168, y + 21, 70, 24, TextAnchor.UpperCenter);
                var iv = inv; Ui.Click(_stage, "Btn_AcceptInvite_" + i, X + W - 168, y + 14, 70, 36, () => _ = RespondInvite(iv, true));
                Ui.Outlined(_stage, "DecBg", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.2f), X + W - 90, y + 14, 66, 36, 9, 1.2f); Ui.Label(_stage, "Decline", "SemiBold", 14, White, X + W - 90, y + 22, 66, 24, TextAnchor.UpperCenter);
                Ui.Click(_stage, "Btn_DeclineInvite_" + i, X + W - 90, y + 14, 66, 36, () => _ = RespondInvite(iv, false));
            }
        }

        // ================================================================================================================ chat
        void BuildMpChat()
        {
            const float X = 1442, Y = 752, W = 455, H = 310;
            Ui.Outlined(_stage, "ChatPanel", PanelFill, PanelLine, X, Y, W, H, 20);
            var tabs = new (string key, string label)[] { ("convoy", "Convoy Chat"), ("global", "Global Chat"), ("system", "System") };
            float tx = X + 12;
            foreach (var t in tabs)
            {
                bool on = _chatTab == t.key; float tw = t.key == "convoy" ? 130 : t.key == "global" ? 130 : 100;
                if (on) Ui.Rounded(_stage, "ChatTabOn", Gold, tx, Y + 12, tw, 34, 10);
                Ui.Label(_stage, t.label, on ? "Bold" : "SemiBold", 16, on ? Ink : t.key == "global" ? new Color(0.6f, 0.62f, 0.68f) : White, tx, Y + 19, tw, 24, TextAnchor.UpperCenter);
                var key = t.key; Ui.Click(_stage, "Btn_ChatTab_" + key, tx, Y + 12, tw, 34, () =>
                {
                    if (key == "global") { Toast("Global chat is not available yet: it needs moderation tools first."); return; }
                    if (_chatTab == key) return; _chatTab = key; _chatStick = true; Refill("MpChat", BuildMpChat); Refill("MpChatLines", BuildChatLines);
                });
                tx += tw + 8;
            }
            bool can = _mpMine != null;
            const float iy = 262;
            Ui.Outlined(_stage, "ChatInputBox", new Color(0.05f, 0.055f, 0.075f, 0.95f), new Color(1, 1, 1, 0.14f), X + 14, Y + iy, W - 74, 40, 12, 1.2f);
            var areaGo = new GameObject("ChatArea", typeof(RectTransform), typeof(RectMask2D));
            var area = Ui.Place(areaGo, _stage, Ui.Anchor.TopLeft, X + 26, Y + iy, W - 98, 40);
            var txt = Ui.Label(area, "", "Regular", 16, White, 0, 0, W - 98, 40, TextAnchor.MiddleLeft);
            var ph = Ui.Label(area, can ? "Type a message..." : "Join a convoy to chat", "Regular", 16, new Color(1, 1, 1, 0.5f), 0, 0, W - 98, 40, TextAnchor.MiddleLeft);
            var hit = areaGo.AddComponent<Image>(); hit.color = new Color(1, 1, 1, 0.001f); areaGo.name = "Chat_Input";
            var f = areaGo.AddComponent<InputField>(); f.targetGraphic = hit; f.textComponent = txt; f.placeholder = ph; f.lineType = InputField.LineType.SingleLine; f.characterLimit = 200; f.transition = Selectable.Transition.None;
            f.customCaretColor = true; f.caretColor = Gold; f.interactable = can; f.selectionColor = new Color(Gold.r, Gold.g, Gold.b, 0.35f);
            _chatField = f;
            Ui.Rounded(_stage, "SendBg", can ? Gold : new Color(0.2f, 0.22f, 0.27f), X + W - 52, Y + iy, 40, 40, 11); Ui.Icon(_stage, "icon_arrow", can ? Ink : Muted, X + W - 45, Y + iy + 7, 26);
            Ui.Click(_stage, "Btn_SendChat", X + W - 52, Y + iy, 40, 40, () => SendChat());
        }

        void SendChat()
        {
            if (_mpMine == null) { Toast("Join a convoy to chat."); return; }
            if (_chatField == null || string.IsNullOrWhiteSpace(_chatField.text)) return;
            string body = _chatField.text.Trim(); _chatField.text = "";
            _ = PostChat(body);
        }

        void BuildChatLines()
        {
            const float X = 1442, Y = 752, W = 455;
            _areas.RemoveAll(a => a.Name == "mp_chat" || a.View == null);
            if (_mpMine == null) { Ui.Label(_stage, "Join or create a convoy to chat with your crew.", "Regular", 16, Muted, X + 24, Y + 120, W - 48, 24, TextAnchor.UpperCenter); return; }
            var lines = _chat.Where(l => _chatTab == "system" ? l.System : !l.System).ToList();
            if (lines.Count == 0) { Ui.Label(_stage, _chatTab == "system" ? "Nothing yet." : "No messages yet. Say hello!", "Regular", 16, Muted, X + 24, Y + 120, W - 48, 24, TextAnchor.UpperCenter); return; }
            var heights = new List<float>(); float total = 4f;
            foreach (var l in lines)
            {
                var probe = Ui.Paragraph(_stage, l.body, "Regular", 15, White, 0, 0, W - 94, 20); float ph = probe.preferredHeight; Destroy(probe.gameObject);
                float h = l.System ? Mathf.Max(24f, ph + 6f) : Mathf.Max(48f, 24f + ph + 8f); heights.Add(h); total += h;
            }
            const float vh = 198;
            var area = MakeScroll("mp_chat", X + 10, Y + 54, W - 20, vh, total);
            float y = 2f; var host = area.Content;
            for (int i = 0; i < lines.Count; i++)
            {
                var l = lines[i];
                if (l.System) { Ui.Paragraph(host, l.body, "Regular", 15, new Color(0.62f, 0.66f, 0.72f), 50, y + 2, W - 94, heights[i]); }
                else
                {
                    Ui.Disc(host, "CAv", new Color(0.16f, 0.18f, 0.24f), 6, y + 4, 34); Ui.Icon(host, "avatar_default", White, 6, y + 4, 34);
                    string name = l.sender != null ? l.sender.display_name : "?"; var nl = Ui.Label(host, name, "Bold", 15, White, 50, y + 2, 220, 20);
                    string when = TimeText.TryParse(l.created_at, out var t) ? t.LocalDateTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) : "";
                    Ui.Label(host, when, "Regular", 12, Muted, 50 + Mathf.Min(nl.preferredWidth, 200f) + 8, y + 5, 60, 16);
                    var body = Ui.Paragraph(host, l.body, "Regular", 15, new Color(0.9f, 0.92f, 0.95f), 50, y + 22, W - 94, heights[i] - 22); body.supportRichText = false;
                }
                y += heights[i];
            }
            if (_chatStick) { area.Pos = area.Max; _scrollPos["mp_chat"] = area.Pos; }
            RestoreScroll(area);
        }

        // ================================================================================================================ scroll areas (mouse wheel)
        ScrollArea MakeScroll(string name, float x, float y, float w, float h, float contentH)
        {
            var view = Ui.Box(_stage, name + "_View", x, y, w, h); view.gameObject.AddComponent<RectMask2D>();
            var content = Ui.Box(view, "Content", 0, 0, w, Mathf.Max(contentH, 1f));
            var a = new ScrollArea { Name = name, View = view, Content = content, Height = h, ContentH = contentH, X = x, Y = y };
            a.Thumb = Ui.Rounded(_stage, name + "_Thumb", new Color(1, 1, 1, 0.3f), x + w - 5, y, 4, 40, 2);
            _areas.Add(a); return a;
        }

        void RestoreScroll(ScrollArea a) { float p; if (!_scrollPos.TryGetValue(a.Name, out p)) p = 0f; ApplyScroll(a, p); }

        void ApplyScroll(ScrollArea a, float v)
        {
            a.Pos = Mathf.Clamp(v, 0f, a.Max); _scrollPos[a.Name] = a.Pos;
            if (a.Content != null) a.Content.anchoredPosition = new Vector2(0, a.Pos);
            if (a.Thumb != null)
            {
                float th = Mathf.Max(36f, a.Height * a.Height / Mathf.Max(a.ContentH, a.Height)), travel = a.Height - th;
                a.Thumb.rectTransform.sizeDelta = new Vector2(4, th);
                a.Thumb.rectTransform.anchoredPosition = new Vector2(a.Thumb.rectTransform.anchoredPosition.x, -(a.Y + (a.Max > 0f ? a.Pos / a.Max * travel : 0f)));
                a.Thumb.gameObject.SetActive(a.Max > 0f);
            }
        }

        // ================================================================================================================ update
        void MpUpdate(float now)
        {
            if (_mpSearchDue > 0f && now > _mpSearchDue)
            {
                _mpSearchDue = -1f; string q = (_mpSearchPending ?? "").Trim();
                if (q != _mpSearch) { _mpSearch = q; if (_mpTab == MpTab.Convoys) _ = LoadConvoys(); else if (_mpTab == MpTab.Players) _ = LoadPlayers(); else if (_mpTab == MpTab.Friends) Refill("MpMain", BuildMpMain); }
            }
            if (now > _nextMine) { _nextMine = now + 3f; _ = LoadMine(true); }
            if (now > _nextChat) { _nextChat = now + 2.5f; if (_mpMine != null) _ = LoadChat(false); }
            if (now > _nextLists) { _nextLists = now + 15f; if (_mpTab == MpTab.Convoys) { _ = LoadConvoys(true); _ = LoadPlayers(true); _ = LoadStatus(); } else if (_mpTab == MpTab.Players) _ = LoadPlayers(true); }
            float dy = 0f; Vector2 mp = Vector2.zero;
            try { dy = UnityEngine.Input.mouseScrollDelta.y; mp = UnityEngine.Input.mousePosition; } catch (InvalidOperationException) { }
            if (dy != 0f)
            {
                _areas.RemoveAll(a => a.View == null);
                foreach (var a in _areas) if (a.View.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(a.View, mp, null))
                { ApplyScroll(a, a.Pos - dy * 60f); if (a.Name == "mp_chat") _chatStick = a.Pos >= a.Max - 4f; break; }
            }
            bool typing = (_mpSearchField != null && _mpSearchField.isFocused) || (_chatField != null && _chatField.isFocused);
            try
            {
                if (_chatField != null && _chatField.isFocused && (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter))) SendChat();
                if (!typing && UnityEngine.Input.GetKeyDown(KeyCode.Escape)) { if (_modal != null) CloseModal(); else ShowPage(Page.Home); }
            }
            catch (InvalidOperationException) { }
        }
    }
}
