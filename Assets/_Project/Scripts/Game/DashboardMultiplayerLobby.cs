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
    /// <summary>Multiplayer page, part 3: the convoy lobby, players nearby, the server list, voice, and the dialogs that go with them.</summary>
    public sealed partial class DashboardScreen
    {
        // ================================================================================================================ loading
        async Task LoadNearby(bool quiet = false)
        {
            if (!quiet) { _mpNearbyState = LoadState.Loading; if (_mpTab == MpTab.Convoys) Refill("MpMain", BuildMpMain); }
            var r = await _svc.Convoys.Nearby(10);
            if (this == null) return;
            if (!r.Ok) { if (quiet && _mpNearbyState == LoadState.Ready) return; _mpNearbyState = LoadState.Unavailable; _mpNearbyErr = r.UserMessage; }
            else
            {
                string before = _mpNearby == null ? "" : _mpNearby.scope + "|" + _mpNearby.visible + "|" + string.Join(",", (_mpNearby.items ?? new NearbyPlayer[0]).Select(p => p.player_id + p.InConvoy));
                _mpNearby = r.Value; _mpNearbyState = (r.Value.items == null || r.Value.items.Length == 0) ? LoadState.Empty : LoadState.Ready;
                string after = r.Value.scope + "|" + r.Value.visible + "|" + string.Join(",", (r.Value.items ?? new NearbyPlayer[0]).Select(p => p.player_id + p.InConvoy));
                if (quiet && before == after) return;
            }
            Debug.Log($"[Multiplayer] nearby state={_mpNearbyState.ToString().ToLowerInvariant()} scope={(_mpNearby != null ? _mpNearby.scope : "-")} shown={(_mpNearby != null && _mpNearby.items != null ? _mpNearby.items.Length : 0)}" + (_mpNearbyState == LoadState.Unavailable ? " error=" + _mpNearbyErr : ""));
            if (_mpTab == MpTab.Convoys) Refill("MpMain", BuildMpMain);
        }

        async Task LoadServers(bool quiet = false)
        {
            if (!quiet) { _mpServerState = LoadState.Loading; if (_mpTab == MpTab.Servers) Refill("MpMain", BuildMpMain); }
            var r = await _svc.Convoys.Servers();
            if (this == null) return;
            if (!r.Ok) { if (quiet && _mpServerState == LoadState.Ready) return; _mpServerState = LoadState.Unavailable; _mpServerErr = r.UserMessage; }
            else { _mpServers = r.Value.items ?? new ServerInfo[0]; _mpServerState = _mpServers.Length == 0 ? LoadState.Empty : LoadState.Ready; }
            Debug.Log($"[Multiplayer] servers state={_mpServerState.ToString().ToLowerInvariant()} count={_mpServers.Length}" + (_mpServers.Length > 0 ? $" first={_mpServers[0].status} population={_mpServers[0].population}" : "") + (_mpServerState == LoadState.Unavailable ? " error=" + _mpServerErr : ""));
            if (_mpTab == MpTab.Servers) Refill("MpMain", BuildMpMain);
        }

        async Task SetNearbyVisible(bool visible) => await Guard(async () =>
        {
            var r = await _svc.Convoys.SetNearbyVisible(visible);
            if (this == null) return;
            Toast(r.Ok ? (visible ? "You appear in nearby lists." : "You are hidden from nearby lists.") : r.UserMessage, 3f);
            await LoadNearby();
        });

        public void SetStarting(bool starting) { _pending = starting ? PendingOp.Starting : PendingOp.None; if (_page == Page.Multiplayer) { Refill("MpConvoy", BuildMpConvoy); if (_mpTab == MpTab.Lobby) Refill("MpMain", BuildMpMain); } }

        // ================================================================================================================ players nearby
        void BuildNearby()
        {
            const float X = 984, Y = 536, W = 444, H = 380;
            Ui.Outlined(_stage, "NearbyPanel", PanelFill, PanelLine, X, Y, W, H, 20);
            Ui.Label(_stage, "Players Nearby", "Bold", 26, White, X + 20, Y + 12, 260, 34);
            var items = _mpNearby != null && _mpNearby.items != null ? _mpNearby.items : new NearbyPlayer[0];
            string count = _mpNearbyState == LoadState.Ready || _mpNearbyState == LoadState.Empty ? items.Length.ToString() : "-";
            var t = Ui.Label(_stage, "Players Nearby", "Bold", 26, White, 0, 0, 10, 10); float tw = t.preferredWidth; Destroy(t.gameObject);
            Ui.Rounded(_stage, "NearbyCount", new Color(1, 1, 1, 0.12f), X + 32 + tw, Y + 15, 40, 28, 14); Ui.Label(_stage, count, "SemiBold", 17, White, X + 32 + tw, Y + 18, 40, 24, TextAnchor.UpperCenter);
            SmallPillIn(_stage, "View All", X + W - 112, Y + 14, 92, 30, () => SetMpTab(MpTab.Players));
            if (_mpNearbyState == LoadState.Idle || _mpNearbyState == LoadState.Loading) { Ui.Label(_stage, "Looking for drivers near you...", "Regular", 17, Muted, X + 22, Y + 80, 400, 24); return; }
            if (_mpNearbyState == LoadState.Unavailable) { Ui.Label(_stage, "Nearby players are unavailable.", "SemiBold", 17, Brand.Bad, X + 22, Y + 80, 400, 24); Ui.Rounded(_stage, "RetryBg", Gold, X + 22, Y + 116, 150, 38, 10); Ui.Label(_stage, "Try again", "Bold", 17, Ink, X + 22, Y + 123, 150, 24, TextAnchor.UpperCenter); Ui.Click(_stage, "Btn_RetryNearby", X + 22, Y + 116, 150, 38, () => _ = LoadNearby()); return; }
            string scope = _mpNearby != null ? _mpNearby.scope : "none";
            string sub = scope == "route" ? "On your route: " + _mpNearby.scope_name : scope == "city" ? "In your city: " + _mpNearby.scope_name : "Start driving to see drivers near you.";
            Ui.Label(_stage, sub, "Regular", 15, scope == "none" ? Muted : Soft, X + 22, Y + 52, 400, 20);
            if (scope != "none" && items.Length == 0) Ui.Label(_stage, "Nobody else is here right now.", "SemiBold", 17, Muted, X + 22, Y + 100, 400, 24);
            for (int i = 0; i < Mathf.Min(6, items.Length); i++)
            {
                var p = items[i]; float y = Y + 82 + i * 38;
                if (i > 0) Ui.Flat(_stage, "RowLine", new Color(1, 1, 1, 0.05f), X + 16, y - 3, W - 32, 1);
                Ui.Disc(_stage, "NDot", Brand.Good, X + 20, y + 12, 10); Ui.Disc(_stage, "NAv", new Color(0.16f, 0.18f, 0.24f), X + 36, y + 3, 28); Ui.Icon(_stage, "avatar_default", White, X + 36, y + 3, 28);
                var n = Ui.Label(_stage, p.display_name, "SemiBold", 15, White, X + 72, y + 7, 120, 22); n.horizontalOverflow = HorizontalWrapMode.Wrap; n.verticalOverflow = VerticalWrapMode.Truncate; n.resizeTextForBestFit = true; n.resizeTextMinSize = 11; n.resizeTextMaxSize = 15;
                Ui.Label(_stage, "Lv " + p.level, "Regular", 14, Soft, X + 196, y + 8, 44, 22);
                var v = Ui.Label(_stage, p.vehicle ?? "-", "Regular", 13, Soft, X + 244, y + 8, 104, 22); v.horizontalOverflow = HorizontalWrapMode.Wrap; v.verticalOverflow = VerticalWrapMode.Truncate; v.resizeTextForBestFit = true; v.resizeTextMinSize = 10; v.resizeTextMaxSize = 13;
                Ui.Label(_stage, p.InConvoy ? "In convoy" : "Online", "SemiBold", 13, p.InConvoy ? Blue : Brand.Good, X + 354, y + 8, 80, 22);
                var pl = p; Ui.Click(_stage, "Btn_Nearby_" + SafeName(p.display_name), X + 12, y, W - 24, 36, () => OpenPlayerDialog(pl));
            }
            bool vis = _mpNearby == null || _mpNearby.visible;
            Ui.Flat(_stage, "NearbyLine", new Color(1, 1, 1, 0.08f), X + 16, Y + H - 44, W - 32, 1);
            Ui.Rounded(_stage, "VisTrack", vis ? GoGreen : new Color(0.3f, 0.32f, 0.38f), X + 22, Y + H - 33, 40, 22, 11); Ui.Disc(_stage, "VisKnob", White, vis ? X + 44 : X + 24, Y + H - 31, 18);
            Ui.Label(_stage, vis ? "You appear in nearby lists" : "You are hidden from nearby lists", "Regular", 14, Soft, X + 72, Y + H - 32, 340, 20);
            Ui.Click(_stage, "Btn_NearbyVisibility", X + 16, Y + H - 40, W - 32, 34, () => _ = SetNearbyVisible(!vis));
        }

        // ================================================================================================================ the lobby tab
        void BuildLobby()
        {
            const float X = 308, Y = 276, W = 1120, H = 786;
            Ui.Outlined(_stage, "LobbyPanel", PanelFill, PanelLine, X, Y, W, H, 20);
            _areas.RemoveAll(a => a.Name == "mp_main_lobby" || a.View == null);
            var c = _mpMine;
            if (c == null) { Ui.Label(_stage, "You are not in a convoy.", "Bold", 26, Muted, X + 30, Y + 40, 600, 34); Ui.Rounded(_stage, "BackBg", Gold, X + 30, Y + 90, 240, 46, 12); Ui.Label(_stage, "Find Convoys", "Bold", 19, Ink, X + 30, Y + 99, 240, 28, TextAnchor.UpperCenter); Ui.Click(_stage, "Btn_LobbyToFind", X + 30, Y + 90, 240, 46, () => SetMpTab(MpTab.Convoys)); return; }
            bool leader = c.is_leader, driving = c.Driving; var ui = MpState(); Color sc = StateColor(ui);
            // header: name, state, rules
            var nm = Ui.Label(_stage, c.name, "ExtraBold", 32, White, X + 28, Y + 16, 560, 42); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 20; nm.resizeTextMaxSize = 32; nm.horizontalOverflow = HorizontalWrapMode.Wrap; nm.verticalOverflow = VerticalWrapMode.Truncate;
            float nw = Mathf.Min(nm.preferredWidth, 560f);
            Ui.Outlined(_stage, "StatePill", new Color(sc.r * 0.2f, sc.g * 0.2f, sc.b * 0.2f, 0.95f), new Color(sc.r, sc.g, sc.b, 0.6f), X + 44 + nw, Y + 20, 168, 34, 10, 1.4f);
            Ui.Disc(_stage, "StateDot", sc, X + 56 + nw, Y + 31, 12); Ui.Label(_stage, ConvoyStates.Label(ui), "Bold", 17, sc, X + 74 + nw, Y + 26, 140, 24);
            float cx = X + 28;
            cx += MiniChip(_stage, cx, Y + 66, c.is_private ? "Invite only" : "Public", Gold) + 6;
            if (c.has_password) cx += MiniChip(_stage, cx, Y + 66, "Password", Gold) + 6;
            cx += MiniChip(_stage, cx, Y + 66, c.driving_mode == "realistic" ? "Realistic driving" : "Relaxed driving", Blue) + 6;
            cx += MiniChip(_stage, cx, Y + 66, c.min_level > 1 ? "Level " + c.min_level + "+" : "Any level", new Color(0.85f, 0.88f, 0.92f)) + 6;
            cx += MiniChip(_stage, cx, Y + 66, LangLabel(c.language), new Color(0.85f, 0.88f, 0.92f)) + 6;
            foreach (var tg in c.tags ?? new string[0]) cx += MiniChip(_stage, cx, Y + 66, TagLabel(tg), new Color(0.85f, 0.88f, 0.92f)) + 6;
            // route block
            bool hasRoute = c.route != null && c.route.Exists;
            Ui.Outlined(_stage, "RouteBlock", CardFill, new Color(1, 1, 1, 0.10f), X + 20, Y + 104, W - 40, 92, 16, 1.4f);
            Ui.Icon(_stage, "ic_road", Gold, X + 40, Y + 126, 44);
            Ui.Label(_stage, "ROUTE", "SemiBold", 13, Muted, X + 100, Y + 114, 100, 18);
            Ui.Label(_stage, hasRoute ? c.route.from + "  →  " + c.route.to : "No route chosen yet", "ExtraBold", 28, hasRoute ? White : Muted, X + 100, Y + 130, 520, 36);
            var facts = new (string icon, string v, string l)[]
            {
                ("ic_road", hasRoute && c.route.distance_km > 0 ? $"{c.route.distance_km:0.#} km" : "-", "Distance"),
                ("ic_clock", hasRoute && c.route.est_minutes > 0 ? JobMath.FormatDuration(c.route.est_minutes) : "-", "Estimated time"),
            };
            for (int i = 0; i < facts.Length; i++) { float fx = X + 640 + i * 180; Ui.Icon(_stage, facts[i].icon, Gold, fx, Y + 126, 32); Ui.Label(_stage, facts[i].v, "Bold", 22, White, fx + 42, Y + 118, 130, 28); Ui.Label(_stage, facts[i].l, "Regular", 14, Muted, fx + 42, Y + 148, 130, 20); }
            if (leader && !driving) { Ui.Outlined(_stage, "RouteChange", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.22f), X + W - 164, Y + 150, 124, 32, 9, 1.2f); Ui.Label(_stage, hasRoute ? "Change route" : "Choose route", "SemiBold", 14, White, X + W - 164, Y + 157, 124, 22, TextAnchor.UpperCenter); Ui.Click(_stage, "Btn_LobbyRoute", X + W - 164, Y + 150, 124, 32, OpenRouteDialog); }
            // members table
            const float ty = 214;
            string[] cols = { "Player", "Level", "Truck", "State", "Connection", "Voice" }; float[] cxs = { 74, 372, 450, 690, 850, 990 };
            for (int i = 0; i < cols.Length; i++) Ui.Label(_stage, cols[i], "SemiBold", 14, Muted, X + cxs[i], Y + ty, 140, 20);
            const float rowH = 56; var invited = c.invited ?? new InvitedDto[0]; int rows = c.members.Length + invited.Length;
            var area = MakeScroll("mp_main_lobby", X + 12, Y + ty + 26, W - 24, 380, rows * rowH + 6);
            for (int i = 0; i < c.members.Length; i++)
            {
                var m = c.members[i]; float y = 2 + i * rowH; var host = area.Content; bool me = m.player_id == _svc.Profile.id; var ms = MpMemberState(m);
                Ui.Outlined(host, "MRow", CardFill, new Color(1, 1, 1, 0.08f), 4, y, W - 44, rowH - 6, 12, 1.2f);
                Ui.Disc(host, "MAv", new Color(0.16f, 0.18f, 0.24f), 18, y + 5, 40); Ui.Icon(host, "avatar_default", White, 18, y + 5, 40);
                var n = Ui.Label(host, m.display_name + (me ? " (you)" : ""), "Bold", 18, White, cxs[0] - 4 + 8, y + 13, 240, 26); if (m.is_leader) Ui.Icon(host, "ic_crown", Gold, cxs[0] + 8 + Mathf.Min(n.preferredWidth, 230f) + 6, y + 14, 20);
                Ui.Label(host, m.level.ToString(), "SemiBold", 17, White, cxs[1] - 8, y + 14, 60, 24);
                var v = Ui.Label(host, m.vehicle ?? "-", "Regular", 15, Soft, cxs[2] - 8, y + 14, 220, 24); v.horizontalOverflow = HorizontalWrapMode.Wrap; v.verticalOverflow = VerticalWrapMode.Truncate; v.resizeTextForBestFit = true; v.resizeTextMinSize = 11; v.resizeTextMaxSize = 15;
                Ui.Label(host, m.is_leader && ms == MemberUiState.Ready ? "Leader" : ConvoyStates.Label(ms), "Bold", 16, m.is_leader && ms == MemberUiState.Ready ? White : MemberColor(ms), cxs[3] - 8, y + 14, 150, 24);
                Ui.Disc(host, "ConnDot", m.connected ? Brand.Good : Brand.Bad, cxs[4] - 8, y + 20, 11);
                string conn = !m.connected ? "Offline" : me && _mpPing >= 0 ? "Online · " + Mathf.RoundToInt((float)_mpPing) + " ms" : "Online";
                Ui.Label(host, conn, "Regular", 15, m.connected ? Soft : Brand.Bad, cxs[4] + 10, y + 14, 140, 24);
                var vs = me ? _voice.Local : _voice.StateOf(m.player_id);
                Ui.Label(host, VoiceText.Label(vs), "Regular", 14, vs == VoiceState.Unavailable ? Muted : Brand.Good, cxs[5] - 8, y + 15, 100, 22);
                if (leader && !me && !driving)
                {
                    Ui.Outlined(host, "LdBg", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.2f), W - 150, y + 10, 52, 30, 8, 1.2f); Ui.Label(host, "Lead", "SemiBold", 13, White, W - 150, y + 16, 52, 20, TextAnchor.UpperCenter);
                    var mm = m; Ui.Click(host, "Btn_LobbyLead_" + SafeName(m.display_name), W - 150, y + 10, 52, 30, () => _ = MakeLeader(mm));
                    Ui.Rounded(host, "KickBg", new Color(0.6f, 0.15f, 0.15f), W - 92, y + 10, 48, 30, 8); Ui.Label(host, "Kick", "Bold", 13, White, W - 92, y + 16, 48, 20, TextAnchor.UpperCenter);
                    Ui.Click(host, "Btn_LobbyKick_" + SafeName(m.display_name), W - 92, y + 10, 48, 30, () => _ = KickMember(mm));
                }
            }
            for (int k = 0; k < invited.Length; k++)
            {
                float y = 2 + (c.members.Length + k) * rowH; var host = area.Content;
                Ui.Outlined(host, "IRow", new Color(CardFill.r, CardFill.g, CardFill.b, 0.6f), new Color(1, 1, 1, 0.06f), 4, y, W - 44, rowH - 6, 12, 1.2f);
                Ui.Disc(host, "IAv", new Color(0.16f, 0.18f, 0.24f, 0.6f), 18, y + 5, 40); Ui.Icon(host, "avatar_default", new Color(1, 1, 1, 0.5f), 18, y + 5, 40);
                Ui.Label(host, invited[k].display_name, "Bold", 18, new Color(1, 1, 1, 0.7f), cxs[0] + 4, y + 13, 240, 26); Ui.Label(host, invited[k].level.ToString(), "SemiBold", 17, Soft, cxs[1] - 8, y + 14, 60, 24);
                Ui.Label(host, ConvoyStates.Label(MemberUiState.Invited), "Bold", 16, MemberColor(MemberUiState.Invited), cxs[3] - 8, y + 14, 150, 24); Ui.Label(host, "Waiting for an answer", "Regular", 14, Muted, cxs[4] + 10, y + 15, 200, 22);
            }
            RestoreScroll(area);
            // controls
            var gate = MpStartCheck(); const float by = 692;
            if (driving)
            {
                BigButton("Btn_LobbyJoinRoad", X + 28, Y + by, 300, leader ? "Rejoin the Road" : "Join the Road", GoGreen, Ink, () => _act.ConvoyDrive(c));
                if (leader) BigButton("Btn_LobbyBack", X + 340, Y + by, 220, "Back to lobby", new Color(1, 1, 1, 0.12f), White, () => _ = BackToLobby());
            }
            else if (leader)
            {
                BigButton(gate.Allowed ? "Btn_LobbyStart" : "Btn_LobbyStartBlocked", X + 28, Y + by, 330, gate.Allowed ? "Start Convoy" : "Start Convoy", gate.Allowed ? GoGreen : new Color(0.20f, 0.22f, 0.27f), gate.Allowed ? Ink : Muted, () => { if (gate.Allowed) _act.ConvoyDrive(c); else Toast(gate.Reason); });
                if (!gate.Allowed) Ui.Label(_stage, gate.Reason, "Regular", 14, Soft, X + 28, Y + by + 52, 560, 20);
                BigButton("Btn_LobbyInvite", X + 372, Y + by, 130, "Invite", new Color(1, 1, 1, 0.12f), White, OpenInviteDialog);
                BigButton("Btn_LobbySettings", X + 514, Y + by, 150, "Settings", new Color(1, 1, 1, 0.12f), White, OpenSettingsDialog);
                BigButton("Btn_LobbyDisband", X + W - 200, Y + by, 172, "Disband", new Color(0.6f, 0.15f, 0.15f), White, OpenDisbandDialog);
            }
            else
            {
                var me = c.members.FirstOrDefault(m => m.player_id == _svc.Profile.id); bool ready = me != null && me.ready;
                BigButton("Btn_LobbyReady", X + 28, Y + by, 300, ready ? "Ready - tap to cancel" : "I'm Ready", ready ? new Color(0.20f, 0.5f, 0.3f) : GoGreen, Ink, () => { if (!hasRoute) Toast("The leader has not chosen a route yet."); else _ = SetReady(!ready); });
                BigButton("Btn_LobbyInviteFriend", X + 340, Y + by, 190, "Invite Friend", new Color(1, 1, 1, 0.12f), White, OpenInviteDialog);
                BigButton("Btn_LobbyLeave", X + W - 220, Y + by, 192, "Leave Convoy", new Color(0.6f, 0.15f, 0.15f), White, () => _ = LeaveConvoy());
            }
        }

        void BigButton(string name, float x, float y, float w, string text, Color bg, Color fg, Action click)
        {
            Ui.Rounded(_stage, "BigBg", bg, x, y, w, 48, 12); Ui.Label(_stage, text, "Bold", 19, fg, x, y + 10, w, 28, TextAnchor.UpperCenter); Ui.Click(_stage, name, x, y, w, 48, click);
        }

        // ================================================================================================================ voice and servers
        void BuildVoiceTab()
        {
            Ui.Outlined(_stage, "VoicePanel", PanelFill, PanelLine, 308, 276, 1120, 786, 20);
            Ui.Icon(_stage, "ic_bell", new Color(1, 1, 1, 0.18f), 808, 330, 110);
            Ui.Label(_stage, _voice.IsAvailable ? "Voice chat" : "Voice chat is not available", "Bold", 30, White, 308, 460, 1120, 40, TextAnchor.UpperCenter);
            Ui.Paragraph(_stage, _voice.IsAvailable ? "Your voice status is shown below." : "No voice system is installed in this build, so nothing here claims a connection. When a voice service is added, each member's status (connected, connecting, muted, speaking, disconnected) appears here and in the lobby.", "Regular", 20, Soft, 508, 510, 720, 100, TextAnchor.UpperCenter);
            Ui.Label(_stage, "Your status: " + VoiceText.Label(_voice.Local), "SemiBold", 20, _voice.IsAvailable ? Brand.Good : Muted, 308, 640, 1120, 30, TextAnchor.UpperCenter);
        }

        void BuildServersTab()
        {
            const float X = 308, Y = 276, W = 1120, H = 786;
            Ui.Outlined(_stage, "ServersPanel", PanelFill, PanelLine, X, Y, W, H, 20);
            Ui.Label(_stage, "Server List", "Bold", 26, White, X + 24, Y + 14, 300, 34);
            if (_mpServerState == LoadState.Idle || _mpServerState == LoadState.Loading) { Ui.Label(_stage, "Loading servers...", "Regular", 19, Muted, X + 28, Y + 80, 400, 26); return; }
            if (_mpServerState == LoadState.Unavailable)
            {
                MpMessage(X + 24, Y + 50, W - 48, "ic_gauge", "Servers are unavailable", (_mpServerErr ?? "The game server could not be reached.") + "  Status: UNAVAILABLE", Brand.Bad, "Try again", "Btn_RetryServers", () => _ = LoadServers()); return;
            }
            if (_mpServers.Length == 0) { MpMessage(X + 24, Y + 50, W - 48, "ic_gauge", "No servers are registered", "An operator has not registered any game server yet.", White, "Refresh", "Btn_ServersRefresh", () => _ = LoadServers()); return; }
            string[] cols = { "Server", "Region", "Population", "Capacity", "Ping", "Status" }; float[] cxs = { 36, 380, 560, 720, 860, 980 };
            for (int i = 0; i < cols.Length; i++) Ui.Label(_stage, cols[i], "SemiBold", 15, Muted, X + cxs[i], Y + 70, 160, 20);
            for (int i = 0; i < _mpServers.Length; i++)
            {
                var s = _mpServers[i]; float y = Y + 98 + i * 64;
                Ui.Outlined(_stage, "SRow", CardFill, new Color(1, 1, 1, 0.10f), X + 16, y, W - 32, 58, 12, 1.2f);
                Ui.Label(_stage, s.name, "Bold", 19, White, X + cxs[0], y + 15, 330, 28);
                Ui.Label(_stage, string.IsNullOrEmpty(s.region) ? "-" : s.region, "Regular", 17, Soft, X + cxs[1], y + 16, 170, 26);
                Ui.Label(_stage, Money.Format(s.population), "SemiBold", 18, White, X + cxs[2], y + 15, 150, 26);
                Ui.Label(_stage, s.capacity > 0 ? Money.Format(s.capacity) : "-", "Regular", 17, Soft, X + cxs[3], y + 16, 130, 26);
                Ui.Label(_stage, _mpPing >= 0 ? Mathf.RoundToInt((float)_mpPing) + " ms" : "-", "Regular", 17, Soft, X + cxs[4], y + 16, 110, 26);
                Color c = s.status == "ONLINE" ? Brand.Good : s.status == "FULL" ? Gold : s.status == "MAINTENANCE" ? Blue : Brand.Bad;
                MiniChip(_stage, X + cxs[5], y + 18, s.status, c);
            }
            Ui.Paragraph(_stage, "Population is the number of players with a live session. Capacity and region are shown only when an operator has set them. Ping is your measured round trip to the server.", "Regular", 15, Muted, X + 24, Y + H - 70, W - 48, 44);
        }

        // ================================================================================================================ dialogs: create
        string _cName = "", _cRoute, _cLang = "en", _cDriving = "relaxed", _cPw = ""; bool _cPrivate; int _cCap = 8, _cRank = 1; readonly HashSet<string> _cTags = new HashSet<string>(); InputField _cField, _cPwField;
        static readonly (string code, string label)[] Languages = { ("en", "English"), ("fr", "French"), ("ha", "Hausa"), ("yo", "Yoruba"), ("ig", "Igbo"), ("pt", "Portuguese") };

        void OpenCreateDialog()
        {
            if (_mpMine != null) { Toast("Leave your current convoy before creating a new one."); return; }
            _cName = (_svc.Profile != null ? _svc.Profile.display_name + "'s Convoy" : "My Convoy"); _cRoute = null; _cPrivate = false; _cCap = 8; _cTags.Clear(); _cLang = "en"; _cDriving = "relaxed"; _cRank = 1; _cPw = "";
            if (_svc.World.State.State == LoadState.Idle || _svc.World.State.State == LoadState.Unavailable) _ = ReloadWorldForCreate();
            BuildCreateDialog();
        }

        async Task ReloadWorldForCreate() { await _svc.World.Load(WorldMapScreen.Model()); if (this != null && _modal != null && _cField != null) BuildCreateDialog(); }

        InputField DialogInput(Transform m, string name, float x, float y, float w, string placeholder, string text, int limit, bool password = false)
        {
            Ui.Outlined(m, name + "Box", new Color(0.04f, 0.045f, 0.065f, 1f), new Color(1, 1, 1, 0.2f), x, y, w, 46, 12, 1.4f);
            var areaGo = new GameObject(name + "Area", typeof(RectTransform), typeof(RectMask2D));
            var area = Ui.Place(areaGo, m, Ui.Anchor.TopLeft, x + 16, y, w - 32, 46);
            var txt = Ui.Label(area, "", "Regular", 20, White, 0, 0, w - 32, 46, TextAnchor.MiddleLeft); var ph = Ui.Label(area, placeholder, "Regular", 20, new Color(1, 1, 1, 0.45f), 0, 0, w - 32, 46, TextAnchor.MiddleLeft);
            var hit = areaGo.AddComponent<Image>(); hit.color = new Color(1, 1, 1, 0.001f); areaGo.name = name;
            var f = areaGo.AddComponent<InputField>(); f.targetGraphic = hit; f.textComponent = txt; f.placeholder = ph; f.lineType = InputField.LineType.SingleLine; f.characterLimit = limit; f.transition = Selectable.Transition.None; f.text = text ?? "";
            if (password) f.contentType = InputField.ContentType.Password;
            f.customCaretColor = true; f.caretColor = Gold; return f;
        }

        void Stepper(Transform m, string name, float x, float y, int value, Action minus, Action plus)
        {
            ModalChip(m, name + "Minus", x, y, "-", false, minus, 46, 22);
            Ui.Label(m, value.ToString(), "Bold", 22, White, x + 50, y + 6, 54, 30, TextAnchor.UpperCenter);
            ModalChip(m, name + "Plus", x + 108, y, "+", false, plus, 46, 22);
        }

        void BuildCreateDialog()
        {
            if (_cField != null) _cName = _cField.text; if (_cPwField != null) _cPw = _cPwField.text;
            var m = OpenModal("Create Convoy", 1160, 800); const float x0 = 380, y0 = 140;
            // ---- left column: name, route, tags
            const float lx = x0 + 28, lw = 520;
            Ui.Label(m, "Name", "SemiBold", 17, Soft, lx, y0 + 74, 200, 24);
            _cField = DialogInput(m, "Create_Name_Input", lx, y0 + 100, lw, "Convoy name (3 to 32 characters)", _cName, 32);
            Ui.Label(m, "Route", "SemiBold", 17, Soft, lx, y0 + 166, 200, 24);
            var routes = OpenRoutes(); float cx = lx, cy = y0 + 194;
            cx += ModalChip(m, "Btn_CreateRoute_none", cx, cy, "Decide later", _cRoute == null, () => { _cRoute = null; BuildCreateDialog(); }) + 8;
            foreach (var r in routes.Take(5))
            {
                string label = $"{r.from} → {r.to}  {(r.distance_km > 0 ? r.distance_km.ToString("0") + " km" : "")}";
                var probe = Ui.Label(m, label, "SemiBold", 16, White, 0, 0, 10, 10); float w = probe.preferredWidth + 28; Destroy(probe.gameObject);
                if (cx + w > lx + lw) { cx = lx; cy += 46; }
                string code = r.code; cx += ModalChip(m, "Btn_CreateRoute_" + code, cx, cy, label, _cRoute == code, () => { _cRoute = code; BuildCreateDialog(); }) + 8;
            }
            if (routes.Length == 0) Ui.Label(m, _svc.World.State.State == LoadState.Loading ? "Loading routes..." : "No route is open to you yet.", "Regular", 15, Muted, cx + 4, cy + 9, 400, 22);
            Ui.Label(m, "Tags (up to 3)", "SemiBold", 17, Soft, lx, y0 + 330, 300, 24);
            cx = lx; cy = y0 + 358;
            foreach (var t in ConvoyTags)
            {
                var probe = Ui.Label(m, t.label, "SemiBold", 16, White, 0, 0, 10, 10); float w = probe.preferredWidth + 28; Destroy(probe.gameObject);
                if (cx + w > lx + lw) { cx = lx; cy += 46; }
                string key = t.key; cx += ModalChip(m, "Btn_CreateTag_" + key, cx, cy, t.label, _cTags.Contains(key), () => { if (_cTags.Contains(key)) _cTags.Remove(key); else if (_cTags.Count < 3) _cTags.Add(key); else Toast("Pick up to three tags."); BuildCreateDialog(); }) + 8;
            }
            // ---- right column: rules
            const float rx = x0 + 600;
            Ui.Label(m, "Who can join", "SemiBold", 17, Soft, rx, y0 + 74, 200, 24);
            float vx = rx; vx += ModalChip(m, "Btn_CreatePublic", vx, y0 + 100, "Public", !_cPrivate, () => { _cPrivate = false; BuildCreateDialog(); }, 120) + 8;
            ModalChip(m, "Btn_CreatePrivate", vx, y0 + 100, "Invite only", _cPrivate, () => { _cPrivate = true; BuildCreateDialog(); }, 150);
            Ui.Label(m, "Players", "SemiBold", 17, Soft, rx + 300, y0 + 74, 120, 24);
            Stepper(m, "Btn_CreateCap", rx + 300, y0 + 100, _cCap, () => { _cCap = Mathf.Max(2, _cCap - 1); BuildCreateDialog(); }, () => { _cCap = Mathf.Min(8, _cCap + 1); BuildCreateDialog(); });
            Ui.Label(m, "Driving", "SemiBold", 17, Soft, rx, y0 + 160, 200, 24);
            vx = rx; vx += ModalChip(m, "Btn_CreateRelaxed", vx, y0 + 186, "Relaxed", _cDriving == "relaxed", () => { _cDriving = "relaxed"; BuildCreateDialog(); }, 120) + 8;
            ModalChip(m, "Btn_CreateRealistic", vx, y0 + 186, "Realistic", _cDriving == "realistic", () => { _cDriving = "realistic"; BuildCreateDialog(); }, 130);
            int myLevel = _svc.Profile != null ? Mathf.Max(1, _svc.Profile.level) : 1; _cRank = Mathf.Clamp(_cRank, 1, Mathf.Min(50, myLevel));
            Ui.Label(m, "Required level", "SemiBold", 17, Soft, rx + 300, y0 + 160, 200, 24);
            Stepper(m, "Btn_CreateRank", rx + 300, y0 + 186, _cRank, () => { _cRank = Mathf.Max(1, _cRank - 1); BuildCreateDialog(); }, () => { _cRank = Mathf.Min(Mathf.Min(50, myLevel), _cRank + 1); BuildCreateDialog(); });
            Ui.Label(m, "Language", "SemiBold", 17, Soft, rx, y0 + 246, 200, 24);
            cx = rx; cy = y0 + 272;
            foreach (var l in Languages)
            {
                var probe = Ui.Label(m, l.label, "SemiBold", 16, White, 0, 0, 10, 10); float w = probe.preferredWidth + 28; Destroy(probe.gameObject);
                if (cx + w > rx + 520) { cx = rx; cy += 46; }
                string code = l.code; cx += ModalChip(m, "Btn_CreateLang_" + code, cx, cy, l.label, _cLang == code, () => { _cLang = code; BuildCreateDialog(); }) + 8;
            }
            Ui.Label(m, "Password (optional)", "SemiBold", 17, Soft, rx, y0 + 370, 300, 24);
            _cPwField = DialogInput(m, "Create_Password_Input", rx, y0 + 396, 520, "Leave empty for no password", _cPw, 32, true);
            DisabledOption(m, rx, y0 + 462, "Voice chat", "Not available yet: no voice system is installed");
            DisabledOption(m, rx, y0 + 520, "Shared jobs", "Not available yet: jobs are still done one driver at a time");
            ModalButton(m, "Btn_CreateCancel", x0 + 28, y0 + 718, 200, "Cancel", new Color(1, 1, 1, 0.1f), White, CloseModal);
            ModalButton(m, "Btn_CreateConfirm", x0 + 1160 - 28 - 320, y0 + 718, 320, "Create Convoy", Gold, Ink, () => _ = CreateConvoyFromDialog());
        }

        void DisabledOption(Transform m, float x, float y, string title, string reason)
        {
            Ui.Outlined(m, "OptOff", new Color(0.05f, 0.055f, 0.075f, 0.6f), new Color(1, 1, 1, 0.08f), x, y, 520, 48, 12, 1.2f);
            Ui.Label(m, title, "SemiBold", 17, new Color(0.7f, 0.72f, 0.78f), x + 16, y + 6, 160, 24); Ui.Label(m, reason, "Regular", 14, Muted, x + 16, y + 26, 490, 20);
            Ui.Rounded(m, "OptTrack", new Color(0.25f, 0.27f, 0.32f), x + 462, y + 13, 40, 22, 11); Ui.Disc(m, "OptKnob", new Color(0.55f, 0.57f, 0.62f), x + 464, y + 15, 18);
        }

        async Task CreateConvoyFromDialog() => await Guard(async () =>
        {
            string name = _cField != null ? _cField.text : _cName, pw = _cPwField != null ? _cPwField.text : _cPw;
            _pending = PendingOp.Creating; Refill("MpConvoy", BuildMpConvoy);
            var spec = new ConvoyService.NewConvoy { Name = name, Route = _cRoute, Private = _cPrivate, Capacity = _cCap, DrivingMode = _cDriving, MinRank = _cRank, Language = _cLang, Password = pw, Tags = _cTags.ToArray() };
            var r = await _svc.Convoys.CreateLobby(spec);
            _pending = PendingOp.None;
            if (this == null) return;
            Debug.Log($"[Multiplayer] created convoy=\"{(name ?? "").Trim()}\" route={_cRoute ?? "none"} private={_cPrivate} cap={_cCap} mode={_cDriving} level={_cRank} lang={_cLang} password={!string.IsNullOrEmpty(pw)} tags={_cTags.Count} ok={r.Ok}" + (r.Ok ? "" : " error=" + r.ErrorCode));
            if (!r.Ok) { Toast(r.UserMessage, 5f); Refill("MpConvoy", BuildMpConvoy); return; }
            Toast("Convoy created. Invite your friends and get ready.", 4f);
            _leftOnPurpose = false; CloseModal(); _chat.Clear(); _chatLast = 0; await LoadMine(); await LoadConvoys(true);
        });

        // ================================================================================================================ dialogs: join with a password
        void JoinConvoyFlow(OpenConvoy c)
        {
            if (c.has_password) OpenPasswordDialog(c); else _ = JoinConvoy(c);
        }

        InputField _pwField;
        void OpenPasswordDialog(OpenConvoy c)
        {
            var m = OpenModal("This convoy needs a password", 640, 340); const float x0 = 640, y0 = 370;
            Ui.Label(m, c.name, "Bold", 22, White, x0 + 28, y0 + 76, 580, 30);
            _pwField = DialogInput(m, "Join_Password_Input", x0 + 28, y0 + 122, 584, "Password", "", 32, true);
            ModalButton(m, "Btn_PwCancel", x0 + 28, y0 + 250, 200, "Cancel", new Color(1, 1, 1, 0.1f), White, CloseModal);
            ModalButton(m, "Btn_PwJoin", x0 + 312, y0 + 250, 300, "Join Convoy", Gold, Ink, () => _ = JoinWithPassword(c, _pwField != null ? _pwField.text : ""));
        }

        async Task JoinWithPassword(OpenConvoy c, string pw) => await Guard(async () =>
        {
            _pending = PendingOp.Joining; Refill("MpConvoy", BuildMpConvoy);
            var r = await _svc.Convoys.JoinLobbyWithPassword(c.id, c.name, pw);
            _pending = PendingOp.None;
            if (this == null) return;
            Debug.Log($"[Multiplayer] join convoy=\"{c.name}\" password ok={r.Ok}" + (r.Ok ? "" : " error=" + r.ErrorCode));
            Toast(r.Ok ? "You joined " + c.name + "." : r.UserMessage, r.Ok ? 3f : 5f);
            if (r.Ok) { _leftOnPurpose = false; CloseModal(); }
            await LoadMine(); await LoadConvoys(true);
        });

        // ================================================================================================================ dialogs: settings and disband
        string _sPw = ""; InputField _sPwField;
        void OpenSettingsDialog()
        {
            var c = _mpMine; if (c == null || !c.is_leader) { Toast("Only the leader can change the settings."); return; }
            if (_sPwField != null) _sPw = _sPwField.text;
            var m = OpenModal("Convoy settings", 780, 640); const float x0 = 570, y0 = 220;
            bool lobby = !c.Driving;
            Ui.Label(m, "Driving", "SemiBold", 17, Soft, x0 + 28, y0 + 74, 200, 24);
            float vx = x0 + 28; vx += ModalChip(m, "Btn_SetRelaxed", vx, y0 + 100, "Relaxed", c.driving_mode != "realistic", () => { if (lobby) _ = ApplySettings(drivingMode: "relaxed"); }, 130) + 8;
            ModalChip(m, "Btn_SetRealistic", vx, y0 + 100, "Realistic", c.driving_mode == "realistic", () => { if (lobby) _ = ApplySettings(drivingMode: "realistic"); }, 140);
            int myLevel = _svc.Profile != null ? Mathf.Max(1, _svc.Profile.level) : 1;
            Ui.Label(m, "Required level", "SemiBold", 17, Soft, x0 + 420, y0 + 74, 200, 24);
            Stepper(m, "Btn_SetRank", x0 + 420, y0 + 100, c.min_level, () => { if (lobby && c.min_level > 1) _ = ApplySettings(minRank: c.min_level - 1); }, () => { if (lobby && c.min_level < Mathf.Min(50, myLevel)) _ = ApplySettings(minRank: c.min_level + 1); else Toast("The required level cannot be above your own."); });
            Ui.Label(m, "Language", "SemiBold", 17, Soft, x0 + 28, y0 + 160, 200, 24);
            float cx = x0 + 28, cy = y0 + 186;
            foreach (var l in Languages)
            {
                var probe = Ui.Label(m, l.label, "SemiBold", 16, White, 0, 0, 10, 10); float w = probe.preferredWidth + 28; Destroy(probe.gameObject);
                if (cx + w > x0 + 752) { cx = x0 + 28; cy += 46; }
                string code = l.code; cx += ModalChip(m, "Btn_SetLang_" + code, cx, cy, l.label, c.language == code, () => _ = ApplySettings(language: code)) + 8;
            }
            Ui.Label(m, c.has_password ? "Password (set)" : "Password (none)", "SemiBold", 17, Soft, x0 + 28, y0 + 290, 300, 24);
            _sPwField = DialogInput(m, "Settings_Password_Input", x0 + 28, y0 + 318, 440, "New password (4 to 32 characters)", _sPw, 32, true);
            ModalButton(m, "Btn_SetPassword", x0 + 484, y0 + 316, 130, "Set", Gold, Ink, () => { string pw = _sPwField != null ? _sPwField.text : ""; if (lobby) _ = ApplySettings(password: pw); });
            if (c.has_password) ModalButton(m, "Btn_ClearPassword", x0 + 626, y0 + 316, 130, "Remove", new Color(1, 1, 1, 0.12f), White, () => { if (lobby) _ = ApplySettings(clearPassword: true); });
            if (!lobby) Ui.Label(m, "Driving mode, level and password can only be changed in the lobby.", "Regular", 15, Muted, x0 + 28, y0 + 380, 700, 22);
            ModalButton(m, "Btn_SettingsDone", x0 + 28, y0 + 556, 200, "Done", Gold, Ink, CloseModal);
        }

        async Task ApplySettings(string drivingMode = null, int? minRank = null, string language = null, string password = null, bool clearPassword = false) => await Guard(async () =>
        {
            var r = await _svc.Convoys.Settings(drivingMode, minRank, language, password, clearPassword);
            if (this == null) return;
            Debug.Log($"[Multiplayer] settings mode={drivingMode ?? "-"} level={(minRank.HasValue ? minRank.Value.ToString() : "-")} lang={language ?? "-"} password={(password != null ? "set" : clearPassword ? "cleared" : "-")} ok={r.Ok}" + (r.Ok ? "" : " error=" + r.ErrorCode));
            if (!r.Ok) Toast(r.UserMessage, 5f); else if (password != null || clearPassword) _sPw = "";
            await LoadMine(); if (_modal != null) { _sPw = ""; OpenSettingsDialog(); }
        });

        void OpenDisbandDialog()
        {
            var m = OpenModal("Disband this convoy?", 640, 320); const float x0 = 640, y0 = 380;
            Ui.Paragraph(m, "Every member is removed and the lobby closes. This cannot be undone.", "Regular", 20, Soft, x0 + 28, y0 + 84, 584, 70);
            ModalButton(m, "Btn_DisbandCancel", x0 + 28, y0 + 230, 200, "Keep it", new Color(1, 1, 1, 0.1f), White, CloseModal);
            ModalButton(m, "Btn_DisbandConfirm", x0 + 312, y0 + 230, 300, "Disband convoy", new Color(0.6f, 0.15f, 0.15f), White, () => _ = DisbandConvoy());
        }

        async Task DisbandConvoy() => await Guard(async () =>
        {
            if (_svc.Session.State == ARO.NetCore.SessionState.InSession) await _svc.Convoys.Session.LeaveAsync();
            var r = await _svc.Convoys.Disband();
            if (this == null) return;
            Toast(r.Ok ? "The convoy was disbanded." : r.UserMessage, r.Ok ? 3f : 5f);
            Debug.Log("[Multiplayer] disband ok=" + r.Ok + (r.Ok ? "" : " error=" + r.ErrorCode));
            if (r.Ok) { _leftOnPurpose = true; Debug.Log("[Multiplayer] convoy=none (left)"); _mpMine = null; _mpMineState = LoadState.Empty; _chat.Clear(); _chatLast = 0; _svc.Convoys.Mine.Reset(); }
            CloseModal(); if (_mpTab == MpTab.Lobby) { _mpTab = MpTab.Convoys; Refill("MpTabs", BuildMpTabs); Refill("MpMain", BuildMpMain); }
            Refill("MpConvoy", BuildMpConvoy); Refill("MpChat", BuildMpChat); Refill("MpChatLines", BuildChatLines); await LoadMine(); await LoadConvoys(true);
        });

        // ================================================================================================================ dialogs: other players
        void OpenPlayerDialog(NearbyPlayer p)
        {
            var m = OpenModal(p.display_name, 640, 460); const float x0 = 640, y0 = 310;
            Ui.Disc(m, "PAv", new Color(0.16f, 0.18f, 0.24f), x0 + 28, y0 + 76, 64); Ui.Icon(m, "avatar_default", White, x0 + 28, y0 + 76, 64);
            Ui.Label(m, "Level " + p.level + "  ·  " + (p.vehicle ?? "no vehicle"), "SemiBold", 20, White, x0 + 112, y0 + 80, 500, 28);
            Ui.Label(m, p.InConvoy ? "In convoy: " + p.convoy.name + $" ({p.convoy.member_count}/{p.convoy.capacity})" : "Online, not in a convoy", "Regular", 17, p.InConvoy ? Blue : Brand.Good, x0 + 112, y0 + 112, 500, 24);
            float y = y0 + 170;
            ModalButton(m, "Btn_PlayerProfile", x0 + 28, y, 280, "View Profile", new Color(1, 1, 1, 0.12f), White, () => _ = OpenProfileDialog(p.player_id));
            bool canInvite = _mpMine != null && !p.InConvoy && (_mpMine.is_leader || p.friend_status == "friends");
            if (canInvite) ModalButton(m, "Btn_PlayerInvite", x0 + 332, y, 280, _mpMine.is_leader ? "Invite" : "Invite Friend", Gold, Ink, () => { var pl = p; _ = InvitePlayer(pl.player_id, pl.display_name); });
            bool canJoin = p.InConvoy && p.convoy.joinable && _mpMine == null;
            y += 62;
            if (canJoin) ModalButton(m, "Btn_PlayerJoin", x0 + 28, y, 584, p.convoy.has_password ? "Join Convoy (password)" : "Join Convoy", GoGreen, Ink, () =>
            {
                var oc = new OpenConvoy { id = p.convoy.id, name = p.convoy.name, has_password = p.convoy.has_password, joinable = true, member_count = p.convoy.member_count, capacity = p.convoy.capacity };
                if (oc.has_password) OpenPasswordDialog(oc); else { CloseModal(); _ = JoinConvoy(oc); }
            });
            else if (p.InConvoy && _mpMine == null) { Ui.Label(m, p.convoy.joinable ? "" : (p.convoy.member_count >= p.convoy.capacity ? "That convoy is full." : "Your level is too low for that convoy."), "Regular", 16, Muted, x0 + 28, y + 12, 584, 24); }
            y += 62;
            if (p.friend_status != "friends" && p.friend_status != "request_sent") ModalButton(m, "Btn_PlayerAddFriend", x0 + 28, y, 280, "Add Friend", new Color(1, 1, 1, 0.12f), White, () => { var pl = p; _ = AddFriend(pl.player_id, pl.display_name); });
            ModalButton(m, "Btn_PlayerBlock", x0 + 332, y, 280, "Block", new Color(0.6f, 0.15f, 0.15f), White, () => { var pl = p; _ = BlockPlayer(pl.player_id, pl.display_name); });
        }

        async Task OpenProfileDialog(string playerId)
        {
            var r = await _svc.Player.GetPublic(playerId);
            if (this == null) return;
            if (!r.Ok) { Toast(r.UserMessage, 4f); return; }
            var p = r.Value;
            var m = OpenModal(p.display_name, 640, 460); const float x0 = 640, y0 = 310;
            Ui.Label(m, "Level " + p.level, "Bold", 26, Gold, x0 + 28, y0 + 76, 300, 34);
            var cells = new (string label, string value)[] { ("Jobs completed", Money.Format(p.jobs_completed)), ("Distance driven", $"{p.distance_km:0.#} km"), ("Achievements", p.achievements.ToString()), ("Experience", Money.Format(p.experience) + " XP") };
            for (int i = 0; i < cells.Length; i++) { float cx = x0 + 28 + (i % 2) * 300, cy = y0 + 136 + (i / 2) * 84; Ui.Outlined(m, "PCell", CardFill, new Color(1, 1, 1, 0.1f), cx, cy, 280, 70, 12, 1.2f); Ui.Label(m, cells[i].value, "Bold", 24, White, cx + 16, cy + 8, 250, 32); Ui.Label(m, cells[i].label, "Regular", 15, Muted, cx + 16, cy + 42, 250, 20); }
            if (TimeText.TryParse(p.member_since, out var since)) Ui.Label(m, "Member since " + since.ToString("MMM yyyy", System.Globalization.CultureInfo.InvariantCulture), "Regular", 16, Soft, x0 + 28, y0 + 316, 500, 22);
            Ui.Label(m, p.presence != null && p.presence.online ? "Online now" : "Offline", "SemiBold", 16, p.presence != null && p.presence.online ? Brand.Good : Muted, x0 + 28, y0 + 346, 300, 22);
            ModalButton(m, "Btn_ProfileClose", x0 + 328, y0 + 384, 284, "Close", new Color(1, 1, 1, 0.12f), White, CloseModal);
        }

        async Task BlockPlayer(string id, string name) => await Guard(async () =>
        {
            var r = await _svc.Convoys.Block(id);
            if (this == null) return;
            Toast(r.Ok ? name + " is blocked. Their chat lines are hidden from you." : r.UserMessage, 4f);
            Debug.Log($"[Multiplayer] block player=\"{name}\" ok={r.Ok}" + (r.Ok ? "" : " error=" + r.ErrorCode));
            CloseModal(); if (r.Ok) { _chat.Clear(); _chatLast = 0; if (_mpMine != null) await LoadChat(true); await LoadNearby(); await LoadPlayers(); }
        });

        // ================================================================================================================ dialogs: a chat line (report / block)
        void OpenChatLineDialog(ChatLine l)
        {
            if (l.sender == null) return;
            var m = OpenModal("Message from " + l.sender.display_name, 700, 440); const float x0 = 610, y0 = 320;
            Ui.Outlined(m, "LineBox", CardFill, new Color(1, 1, 1, 0.1f), x0 + 28, y0 + 74, 644, 90, 12, 1.2f);
            var t = Ui.Paragraph(m, ChatText.Display(l.body, l.moderation), "Regular", 18, new Color(0.9f, 0.92f, 0.95f), x0 + 44, y0 + 86, 612, 70); t.supportRichText = false; t.verticalOverflow = VerticalWrapMode.Truncate;
            Ui.Label(m, "Report this message for:", "SemiBold", 17, Soft, x0 + 28, y0 + 184, 400, 24);
            float cx = x0 + 28;
            foreach (var reason in new[] { ("spam", "Spam"), ("abuse", "Abuse"), ("harassment", "Harassment"), ("other", "Other") })
            { string code = reason.Item1; cx += ModalChip(m, "Btn_Report_" + code, cx, y0 + 212, reason.Item2, false, () => _ = ReportLine(l, code), 140) + 8; }
            ModalButton(m, "Btn_LineBlock", x0 + 28, y0 + 290, 300, "Block " + l.sender.display_name, new Color(0.6f, 0.15f, 0.15f), White, () => _ = BlockPlayer(l.sender.player_id, l.sender.display_name));
            ModalButton(m, "Btn_LineClose", x0 + 372, y0 + 290, 300, "Close", new Color(1, 1, 1, 0.12f), White, CloseModal);
        }

        async Task ReportLine(ChatLine l, string reason) => await Guard(async () =>
        {
            var r = await _svc.Convoys.Report(l.id, reason);
            if (this == null) return;
            Toast(r.Ok ? (r.Value.moderation == "hidden" ? "Report received. The message is now hidden." : "Report received. Thank you.") : r.UserMessage, 4f);
            Debug.Log($"[Multiplayer] report line={l.id} reason={reason} ok={r.Ok}" + (r.Ok ? " moderation=" + r.Value.moderation : " error=" + r.ErrorCode));
            CloseModal(); if (r.Ok) await LoadChat(true);
        });
    }
}
