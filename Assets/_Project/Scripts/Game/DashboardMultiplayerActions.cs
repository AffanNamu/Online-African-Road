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
    /// <summary>Multiplayer page: loading, server actions and dialogs. Every action reports the server's answer; nothing is assumed to have worked.</summary>
    public sealed partial class DashboardScreen
    {
        // ================================================================================================================ loading
        async Task MpReloadAll()
        {
            var a = LoadMine(); var b = LoadConvoys(); var c = LoadPlayers(); var d = LoadStatus(); var e = LoadInvites();
            await a; await b; await c; await d; await e;
            if (this == null) return;
            if (_mpMine != null) await LoadChat(true);
        }

        static string ConvoyKey(ConvoyInfo c)
        {
            if (c == null) return "none";
            return $"{c.id}|{c.phase}|{c.route?.code}|{c.capacity}|{c.is_private}|{c.leader_id}|{string.Join(",", c.tags ?? new string[0])}|" +
                   string.Join(";", (c.members ?? new ConvoyMemberDto[0]).Select(m => m.player_id + ":" + m.ready + ":" + m.level + ":" + m.vehicle));
        }

        async Task LoadMine(bool quiet = false)
        {
            float t0 = Time.realtimeSinceStartup;
            var r = await _svc.Convoys.LoadMine();
            if (this == null) return;
            if (r.Ok) { double ms = (Time.realtimeSinceStartup - t0) * 1000.0; _mpPing = _mpPing < 0 ? ms : _mpPing * 0.6 + ms * 0.4; }
            if (!r.Ok)
            {
                if (_mpMineState == LoadState.Ready || quiet && _mpMineState == LoadState.Empty) return;           // keep what we know; the next poll retries
                _mpMineState = LoadState.Unavailable; _mpMineErr = r.UserMessage; Refill("MpConvoy", BuildMpConvoy); return;
            }
            var now = r.Value.convoy != null && r.Value.convoy.Exists ? r.Value.convoy : null;
            string before = ConvoyKey(_mpMine), after = ConvoyKey(now);
            string oldPhase = _phaseSeen; _phaseSeen = now != null ? now.phase : null;
            bool convoyChanged = (_mpMine == null ? null : _mpMine.id) != (now == null ? null : now.id);
            _mpMine = now; _mpMineState = now != null ? LoadState.Ready : LoadState.Empty;
            if (before != after || _mpMineState != LoadState.Ready && before == "none" && !quiet)
            {
                Refill("MpConvoy", BuildMpConvoy);
                if (_mpTab == MpTab.Convoys) Refill("MpMain", BuildMpMain);
                if (convoyChanged) { _chat.Clear(); _chatLast = 0; _chatStick = true; Refill("MpChat", BuildMpChat); Refill("MpChatLines", BuildChatLines); if (now != null) _ = LoadChat(true); else _ = LoadInvites(); }
                Debug.Log($"[Multiplayer] convoy={(now != null ? now.name : "none")} phase={(now != null ? now.phase : "-")} members={(now != null ? now.member_count : 0)} route={(now != null && now.route != null ? now.route.code : "-")} ready={(now != null ? now.members.Count(m => m.ready) : 0)} leader={(now != null && now.is_leader)}");
            }
            if (now != null && oldPhase == "lobby" && now.phase == "driving" && !now.is_leader) Toast("Your convoy is on the road. Tap Join the Road.", 5f);
        }

        async Task LoadConvoys(bool quiet = false)
        {
            int token = ++_mpToken;
            if (!quiet) { _mpConvoyState = LoadState.Loading; if (_mpTab == MpTab.Convoys) Refill("MpMain", BuildMpMain); }
            var r = await _svc.Convoys.ListOpen(30, _mpSearch, _mpTag);
            if (this == null || token != _mpToken) return;
            if (!r.Ok) { if (quiet && _mpConvoyState == LoadState.Ready) return; _mpConvoyState = LoadState.Unavailable; _mpConvoyErr = r.UserMessage; }
            else
            {
                string before = string.Join("|", _mpConvoys.Select(c => c.id + c.member_count + c.phase + c.route?.code));
                _mpConvoys = r.Value.items ?? new OpenConvoy[0]; _mpConvoyTotal = r.Value.total; _mpConvoyState = _mpConvoys.Length == 0 ? LoadState.Empty : LoadState.Ready;
                if (quiet && before == string.Join("|", _mpConvoys.Select(c => c.id + c.member_count + c.phase + c.route?.code)) && _mpConvoyState != LoadState.Empty) return;
            }
            Debug.Log($"[Multiplayer] convoys state={_mpConvoyState.ToString().ToLowerInvariant()} total={_mpConvoyTotal} shown={_mpConvoys.Length} tag={_mpTag ?? "all"} search=\"{_mpSearch}\"" + (_mpConvoyState == LoadState.Unavailable ? " error=" + _mpConvoyErr : ""));
            if (_mpTab == MpTab.Convoys) Refill("MpMain", BuildMpMain);
        }

        async Task LoadPlayers(bool quiet = false)
        {
            if (!quiet) { _mpPlayerState = LoadState.Loading; if (_mpTab == MpTab.Convoys || _mpTab == MpTab.Players) Refill("MpMain", BuildMpMain); }
            var r = await _svc.Convoys.ActivePlayers(_mpTab == MpTab.Players ? 50 : 10, _mpTab == MpTab.Players ? _mpSearch : null);
            if (this == null) return;
            if (!r.Ok) { if (quiet && _mpPlayerState == LoadState.Ready) return; _mpPlayerState = LoadState.Unavailable; _mpPlayerErr = r.UserMessage; }
            else
            {
                string before = string.Join("|", _mpPlayers.Select(p => p.player_id + p.in_convoy + p.activity));
                _mpPlayers = r.Value.items ?? new ActivePlayer[0]; _mpPlayerTotal = r.Value.total; _mpPlayerState = _mpPlayers.Length == 0 ? LoadState.Empty : LoadState.Ready;
                if (quiet && before == string.Join("|", _mpPlayers.Select(p => p.player_id + p.in_convoy + p.activity)) && _mpPlayerState != LoadState.Empty) return;
            }
            Debug.Log($"[Multiplayer] players state={_mpPlayerState.ToString().ToLowerInvariant()} total={_mpPlayerTotal} shown={_mpPlayers.Length}" + (_mpPlayerState == LoadState.Unavailable ? " error=" + _mpPlayerErr : ""));
            if (_mpTab == MpTab.Convoys || _mpTab == MpTab.Players) Refill("MpMain", BuildMpMain);
        }

        async Task LoadFriendsList()
        {
            int token = ++_mpFriendToken; _mpFriendState = LoadState.Loading; if (_mpTab == MpTab.Friends) Refill("MpMain", BuildMpMain);
            var r = await _svc.Friends.LoadFriends();
            if (this == null || token != _mpFriendToken) return;
            _mpFriendState = r.Ok ? (r.Value.items == null || r.Value.items.Length == 0 ? LoadState.Empty : LoadState.Ready) : LoadState.Unavailable; _mpFriendErr = r.Ok ? null : r.UserMessage;
            Debug.Log($"[Multiplayer] friends state={_mpFriendState.ToString().ToLowerInvariant()} count={(r.Ok && r.Value.items != null ? r.Value.items.Length : 0)}");
            if (_mpTab == MpTab.Friends) Refill("MpMain", BuildMpMain);
        }

        async Task LoadStatus()
        {
            var r = await _svc.Convoys.Status();
            if (this == null || !r.Ok) return;
            bool changed = _mpStatus == null || _mpStatus.players_online != r.Value.players_online || _mpStatus.active_convoys != r.Value.active_convoys;
            _mpStatus = r.Value;
            if (changed && _mpTab == MpTab.Convoys) Refill("MpMain", BuildMpMain);
        }

        async Task LoadInvites()
        {
            var r = await _svc.Convoys.Invites();
            if (this == null || !r.Ok) return;
            var items = r.Value.items ?? new ConvoyInviteItem[0];
            if (string.Join("|", items.Select(i => i.invite_id)) != string.Join("|", _mpInvites.Select(i => i.invite_id))) { _mpInvites = items; if (_mpMine == null) Refill("MpConvoy", BuildMpConvoy); }
            else _mpInvites = items;
        }

        async Task LoadChat(bool initial)
        {
            if (_mpMine == null) return;
            string convoy = _mpMine.id;
            var r = await _svc.Convoys.Messages(initial ? 0 : _chatLast, 50);
            if (this == null || _mpMine == null || _mpMine.id != convoy || !r.Ok) return;
            var items = r.Value.items ?? new ChatLine[0];
            if (initial) { _chat.Clear(); _chatLast = 0; }
            bool added = false;
            foreach (var l in items) if (l.id > _chatLast) { _chat.Add(l); _chatLast = l.id; added = true; }
            while (_chat.Count > 120) _chat.RemoveAt(0);
            if (added || initial) { Debug.Log($"[Multiplayer] chat lines={_chat.Count} last={_chatLast}"); Refill("MpChatLines", BuildChatLines); }
        }

        // ================================================================================================================ actions
        async Task<bool> Guard(Func<Task> work)
        {
            if (_mpBusy) return false; _mpBusy = true;
            try { await work(); return true; } finally { _mpBusy = false; }
        }

        async Task JoinConvoy(OpenConvoy c) => await Guard(async () =>
        {
            var r = await _svc.Convoys.JoinLobby(c.id, c.name);
            if (this == null) return;
            Toast(r.Ok ? "You joined " + c.name + "." : r.UserMessage, r.Ok ? 3f : 5f);
            Debug.Log($"[Multiplayer] join convoy=\"{c.name}\" ok={r.Ok}" + (r.Ok ? "" : " error=" + r.ErrorCode));
            await LoadMine(); await LoadConvoys(true);
        });

        async Task SetReady(bool ready) => await Guard(async () =>
        {
            var r = await _svc.Convoys.SetReady(ready);
            if (this == null) return;
            if (!r.Ok) Toast(r.UserMessage, 5f); Debug.Log($"[Multiplayer] ready={ready} ok={r.Ok}" + (r.Ok ? "" : " error=" + r.ErrorCode));
            await LoadMine();
        });

        async Task ChooseRoute(string code) => await Guard(async () =>
        {
            var r = await _svc.Convoys.SetRoute(code);
            if (this == null) return;
            Toast(r.Ok ? (code == null ? "Route cleared." : "Route set.") : r.UserMessage, r.Ok ? 2.5f : 5f);
            Debug.Log($"[Multiplayer] route={code ?? "none"} ok={r.Ok}" + (r.Ok ? "" : " error=" + r.ErrorCode));
            if (r.Ok) CloseModal();
            await LoadMine();
        });

        async Task InvitePlayer(string id, string name) => await Guard(async () =>
        {
            var r = await _svc.Convoys.Invite(id);
            if (this == null) return;
            Toast(r.Ok ? "Invite sent to " + name + "." : r.UserMessage, r.Ok ? 3f : 5f);
            Debug.Log($"[Multiplayer] invite player=\"{name}\" ok={r.Ok}" + (r.Ok ? "" : " error=" + r.ErrorCode));
        });

        async Task AddFriend(string id, string name) => await Guard(async () =>
        {
            var r = await _svc.Friends.Send(id);
            if (this == null) return;
            Toast(r.Ok ? (r.Value != null && r.Value.status == "friends" ? "You are now friends with " + name + "." : "Friend request sent to " + name + ".") : r.UserMessage, r.Ok ? 3f : 5f);
            Debug.Log($"[Multiplayer] add friend player=\"{name}\" ok={r.Ok}" + (r.Ok ? "" : " error=" + r.ErrorCode));
            await LoadPlayers();
        });

        async Task RespondInvite(ConvoyInviteItem inv, bool accept) => await Guard(async () =>
        {
            var r = await _svc.Convoys.RespondToInvite(inv.invite_id, accept);
            if (this == null) return;
            Toast(r.Ok ? (accept ? "You joined " + inv.convoy_name + "." : "Invite declined.") : r.UserMessage, r.Ok ? 3f : 5f);
            Debug.Log($"[Multiplayer] invite {(accept ? "accepted" : "declined")} ok={r.Ok}" + (r.Ok ? "" : " error=" + r.ErrorCode));
            await LoadInvites(); await LoadMine(); await LoadConvoys(true);
        });

        async Task PostChat(string body)
        {
            var r = await _svc.Convoys.Post(body);
            if (this == null) return;
            if (!r.Ok) { Toast(r.UserMessage, 4f); return; }
            _chatStick = true; await LoadChat(false);
        }

        async Task LeaveConvoy() => await Guard(async () =>
        {
            await _svc.Convoys.Leave();
            if (this == null) return;
            Toast("You left the convoy."); Debug.Log("[Multiplayer] convoy=none (left)"); CloseModal(); _mpMine = null; _mpMineState = LoadState.Empty; _chat.Clear(); _chatLast = 0;
            Refill("MpConvoy", BuildMpConvoy); Refill("MpChat", BuildMpChat); Refill("MpChatLines", BuildChatLines);
            await LoadMine(); await LoadConvoys(true); await LoadInvites();
        });

        async Task BackToLobby() => await Guard(async () =>
        {
            if (_svc.Session.State == ARO.NetCore.SessionState.InSession) await _svc.Convoys.Session.LeaveAsync();
            var r = await _svc.Convoys.EndDriving();
            if (this == null) return;
            Toast(r.Ok ? "The convoy is back in the lobby." : r.UserMessage, r.Ok ? 3f : 5f); CloseModal(); await LoadMine();
        });

        async Task KickMember(ConvoyMemberDto m) => await Guard(async () =>
        {
            var r = await _svc.Convoys.Kick(m.player_id);
            if (this == null) return;
            Toast(r.Ok ? m.display_name + " was removed." : r.UserMessage, r.Ok ? 3f : 5f); await LoadMine(); if (_modal != null) OpenManageDialog();
        });

        async Task MakeLeader(ConvoyMemberDto m) => await Guard(async () =>
        {
            var r = await _svc.Convoys.TransferLeader(m.player_id);
            if (this == null) return;
            Toast(r.Ok ? m.display_name + " now leads the convoy." : r.UserMessage, r.Ok ? 3f : 5f); await LoadMine(); CloseModal();
        });

        async Task ApplyOptions(int? capacity, bool? isPrivate) => await Guard(async () =>
        {
            var r = await _svc.Convoys.SetOptions(capacity, isPrivate);
            if (this == null) return;
            if (!r.Ok) Toast(r.UserMessage, 5f); await LoadMine(); if (_modal != null) OpenManageDialog();
        });

        // ================================================================================================================ dialogs
        void CloseModal() { if (_modal != null) { Destroy(_modal.gameObject); _modal = null; } _areas.RemoveAll(a => a.View == null); }

        /// <summary>A centred dialog over a dimmed page. Returns the dialog's container (top-left coordinates, 1920x1080 stage).</summary>
        RectTransform OpenModal(string title, float w, float h)
        {
            if (_modal != null) Destroy(_modal.gameObject);
            _modal = Ui.Box(_root, "MpModal", 0, 0, 1920, 1080);
            Ui.Flat(_modal, "Dim", new Color(0, 0, 0, 0.62f), 0, 0, 1920, 1080);
            Ui.Click(_modal, "Btn_ModalDim", 0, 0, 1920, 1080, CloseModal).GetComponent<HoverGlow>().enabled = false;
            float x = 960 - w / 2, y = 540 - h / 2;
            Ui.Outlined(_modal, "ModalPanel", new Color(0.055f, 0.062f, 0.085f, 1f), new Color(Gold.r, Gold.g, Gold.b, 0.5f), x, y, w, h, 22, 2f);
            Ui.Click(_modal, "ModalBlock", x, y, w, h, () => { }).GetComponent<HoverGlow>().enabled = false;
            Ui.Label(_modal, title, "ExtraBold", 30, White, x + 28, y + 18, w - 120, 40);
            Ui.Rounded(_modal, "CloseBg", new Color(1, 1, 1, 0.1f), x + w - 62, y + 18, 38, 38, 10); Ui.Label(_modal, "x", "Bold", 24, White, x + w - 62, y + 22, 38, 30, TextAnchor.UpperCenter);
            Ui.Click(_modal, "Btn_CloseDialog", x + w - 62, y + 18, 38, 38, CloseModal);
            if (_toast != null) _toast.transform.SetAsLastSibling();
            MarkTargetsDirty();
            return _modal;
        }

        float ModalChip(Transform m, string name, float x, float y, string text, bool on, Action click, float? width = null, int size = 16)
        {
            var probe = Ui.Label(m, text, "SemiBold", size, White, 0, 0, 10, 10); float tw = probe.preferredWidth; Destroy(probe.gameObject);
            float w = width ?? tw + 28;
            if (on) Ui.Rounded(m, "ChipOn", Gold, x, y, w, 38, 10); else Ui.Outlined(m, "Chip", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.16f), x, y, w, 38, 10, 1.2f);
            Ui.Label(m, text, on ? "Bold" : "SemiBold", size, on ? Ink : White, x, y + 8, w, 26, TextAnchor.UpperCenter);
            Ui.Click(m, name, x, y, w, 38, click);
            return w;
        }

        void ModalButton(Transform m, string name, float x, float y, float w, string text, Color bg, Color fg, Action click)
        {
            Ui.Rounded(m, "BtnBg", bg, x, y, w, 46, 12); Ui.Label(m, text, "Bold", 19, fg, x, y + 10, w, 28, TextAnchor.UpperCenter); Ui.Click(m, name, x, y, w, 46, click);
        }

        // ---- filters
        void OpenFilterDialog()
        {
            var m = OpenModal("Filter convoys", 640, 360); const float x0 = 640, y0 = 360;
            Ui.Label(m, "Show convoys with this tag", "Regular", 18, Soft, x0 + 28, y0 + 78, 500, 26);
            float cx = x0 + 28, cy = y0 + 116;
            var all = new List<(string key, string label)> { (null, "All") }; all.AddRange(ConvoyTags);
            foreach (var t in all)
            {
                var probe = Ui.Label(m, t.label, "SemiBold", 16, White, 0, 0, 10, 10); float w = probe.preferredWidth + 28; Destroy(probe.gameObject);
                if (cx + w > x0 + 612) { cx = x0 + 28; cy += 48; }
                string key = t.key; cx += ModalChip(m, "Btn_FilterTag_" + (key ?? "all"), cx, cy, t.label, _mpTag == key, () => { _mpTag = key; CloseModal(); _ = LoadConvoys(); }) + 8;
            }
        }

        // ---- create
        string _cName = "", _cRoute; bool _cPrivate; int _cCap = 8; readonly HashSet<string> _cTags = new HashSet<string>(); InputField _cField;

        void OpenCreateDialog()
        {
            if (_mpMine != null) { Toast("Leave your current convoy before creating a new one."); return; }
            _cName = (_svc.Profile != null ? _svc.Profile.display_name + "'s Convoy" : "My Convoy"); _cRoute = null; _cPrivate = false; _cCap = 8; _cTags.Clear();
            if (_svc.World.State.State == LoadState.Idle || _svc.World.State.State == LoadState.Unavailable) _ = _svc.World.Load(WorldMapScreen.Model());
            BuildCreateDialog();
        }

        void BuildCreateDialog()
        {
            if (_cField != null) _cName = _cField.text;
            var m = OpenModal("Create Convoy", 800, 700); const float x0 = 560, y0 = 190;
            Ui.Label(m, "Name", "SemiBold", 17, Soft, x0 + 28, y0 + 74, 200, 24);
            Ui.Outlined(m, "NameBox", new Color(0.04f, 0.045f, 0.065f, 1f), new Color(1, 1, 1, 0.2f), x0 + 28, y0 + 100, 744, 46, 12, 1.4f);
            var areaGo = new GameObject("NameArea", typeof(RectTransform), typeof(RectMask2D));
            var area = Ui.Place(areaGo, m, Ui.Anchor.TopLeft, x0 + 44, y0 + 100, 712, 46);
            var txt = Ui.Label(area, "", "Regular", 20, White, 0, 0, 712, 46, TextAnchor.MiddleLeft); var ph = Ui.Label(area, "Convoy name (3 to 32 characters)", "Regular", 20, new Color(1, 1, 1, 0.45f), 0, 0, 712, 46, TextAnchor.MiddleLeft);
            var hit = areaGo.AddComponent<Image>(); hit.color = new Color(1, 1, 1, 0.001f); areaGo.name = "Create_Name_Input";
            var f = areaGo.AddComponent<InputField>(); f.targetGraphic = hit; f.textComponent = txt; f.placeholder = ph; f.lineType = InputField.LineType.SingleLine; f.characterLimit = 32; f.transition = Selectable.Transition.None; f.text = _cName;
            f.customCaretColor = true; f.caretColor = Gold; _cField = f;

            Ui.Label(m, "Route", "SemiBold", 17, Soft, x0 + 28, y0 + 162, 200, 24);
            var routes = OpenRoutes(); float cx = x0 + 28, cy = y0 + 190;
            cx += ModalChip(m, "Btn_CreateRoute_none", cx, cy, "Decide later", _cRoute == null, () => { _cRoute = null; BuildCreateDialog(); }) + 8;
            foreach (var r in routes.Take(4))
            {
                string label = $"{r.from} → {r.to}  {(r.distance_km > 0 ? r.distance_km.ToString("0") + " km" : "")}";
                var probe = Ui.Label(m, label, "SemiBold", 16, White, 0, 0, 10, 10); float w = probe.preferredWidth + 28; Destroy(probe.gameObject);
                if (cx + w > x0 + 772) { cx = x0 + 28; cy += 46; }
                string code = r.code; cx += ModalChip(m, "Btn_CreateRoute_" + code, cx, cy, label, _cRoute == code, () => { _cRoute = code; BuildCreateDialog(); }) + 8;
            }
            if (routes.Length == 0) Ui.Label(m, _svc.World.State.State == LoadState.Loading ? "Loading routes..." : "No route is open to you yet.", "Regular", 15, Muted, cx + 4, cy + 9, 400, 22);

            Ui.Label(m, "Who can join", "SemiBold", 17, Soft, x0 + 28, y0 + 296, 200, 24);
            float vx = x0 + 28; vx += ModalChip(m, "Btn_CreatePublic", vx, y0 + 324, "Public", !_cPrivate, () => { _cPrivate = false; BuildCreateDialog(); }, 130) + 8;
            ModalChip(m, "Btn_CreatePrivate", vx, y0 + 324, "Invite only", _cPrivate, () => { _cPrivate = true; BuildCreateDialog(); }, 150);
            Ui.Label(m, "Players", "SemiBold", 17, Soft, x0 + 470, y0 + 296, 200, 24);
            ModalChip(m, "Btn_CreateCapMinus", x0 + 470, y0 + 324, "-", false, () => { _cCap = Mathf.Max(2, _cCap - 1); BuildCreateDialog(); }, 46, 22);
            Ui.Label(m, _cCap.ToString(), "Bold", 22, White, x0 + 520, y0 + 330, 50, 30, TextAnchor.UpperCenter);
            ModalChip(m, "Btn_CreateCapPlus", x0 + 574, y0 + 324, "+", false, () => { _cCap = Mathf.Min(8, _cCap + 1); BuildCreateDialog(); }, 46, 22);

            Ui.Label(m, "Tags (up to 3)", "SemiBold", 17, Soft, x0 + 28, y0 + 380, 300, 24);
            cx = x0 + 28; cy = y0 + 408;
            foreach (var t in ConvoyTags)
            {
                var probe = Ui.Label(m, t.label, "SemiBold", 16, White, 0, 0, 10, 10); float w = probe.preferredWidth + 28; Destroy(probe.gameObject);
                if (cx + w > x0 + 772) { cx = x0 + 28; cy += 46; }
                string key = t.key; cx += ModalChip(m, "Btn_CreateTag_" + key, cx, cy, t.label, _cTags.Contains(key), () => { if (_cTags.Contains(key)) _cTags.Remove(key); else if (_cTags.Count < 3) _cTags.Add(key); else Toast("Pick up to three tags."); BuildCreateDialog(); }) + 8;
            }
            ModalButton(m, "Btn_CreateCancel", x0 + 28, y0 + 624, 200, "Cancel", new Color(1, 1, 1, 0.1f), White, CloseModal);
            ModalButton(m, "Btn_CreateConfirm", x0 + 472, y0 + 624, 300, "Create Convoy", Gold, Ink, () => _ = CreateConvoyFromDialog());
        }

        WorldRoute[] OpenRoutes()
        {
            var w = _svc.World.State.Value; if (w == null || w.routes == null) return new WorldRoute[0];
            return w.routes.Where(r => r.effective_status == "OPEN").OrderBy(r => r.unlock_level).ThenBy(r => r.name).ToArray();
        }

        async Task CreateConvoyFromDialog() => await Guard(async () =>
        {
            string name = _cField != null ? _cField.text : _cName;
            var r = await _svc.Convoys.CreateLobby(name);
            if (this == null) return;
            if (!r.Ok) { Toast(r.UserMessage, 5f); return; }
            var problems = new List<string>();
            if (_cRoute != null) { var x = await _svc.Convoys.SetRoute(_cRoute); if (!x.Ok) problems.Add(x.UserMessage); }
            if (_cCap != 8 || _cPrivate) { var x = await _svc.Convoys.SetOptions(_cCap != 8 ? (int?)_cCap : null, _cPrivate ? (bool?)true : null); if (!x.Ok) problems.Add(x.UserMessage); }
            if (_cTags.Count > 0) { var x = await _svc.Convoys.SetTags(_cTags.ToArray()); if (!x.Ok) problems.Add(x.UserMessage); }
            Debug.Log($"[Multiplayer] created convoy=\"{name.Trim()}\" route={_cRoute ?? "none"} private={_cPrivate} cap={_cCap} tags={_cTags.Count} problems={problems.Count}");
            Toast(problems.Count == 0 ? "Convoy created. Invite your friends and choose a route." : "Convoy created, but: " + problems[0], 5f);
            CloseModal(); _chat.Clear(); _chatLast = 0; await LoadMine(); await LoadConvoys(true);
        });

        // ---- route
        void OpenRouteDialog()
        {
            if (_mpMine == null || !_mpMine.is_leader) { Toast("Only the leader can choose the route."); return; }
            BuildRouteDialog();
            if (_svc.World.State.State != LoadState.Ready) _ = ReloadRouteDialog();
        }

        async Task ReloadRouteDialog()
        {
            await _svc.World.Load(WorldMapScreen.Model());
            if (this != null && _modal != null) BuildRouteDialog();
        }

        void BuildRouteDialog()
        {
            var m = OpenModal("Choose a route", 780, 640); const float x0 = 570, y0 = 220;
            var w = _svc.World.State.Value; var routes = (w != null && w.routes != null ? w.routes : new WorldRoute[0]).OrderBy(r => r.effective_status == "OPEN" ? 0 : r.effective_status == "LOCKED" ? 1 : 2).ThenBy(r => r.unlock_level).ThenBy(r => r.name).ToArray();
            if (_svc.World.State.State == LoadState.Loading) { Ui.Label(m, "Loading routes...", "Regular", 19, Muted, x0 + 28, y0 + 90, 400, 26); return; }
            if (_svc.World.State.State == LoadState.Unavailable) { Ui.Label(m, "Routes are unavailable right now.", "SemiBold", 19, Brand.Bad, x0 + 28, y0 + 90, 600, 26); ModalButton(m, "Btn_RetryRoutes", x0 + 28, y0 + 130, 200, "Try again", Gold, Ink, () => OpenRouteDialog()); return; }
            const float rowH = 62;
            var area = new ScrollArea { Name = "mp_modal_routes", Height = 500, ContentH = (routes.Length + 1) * rowH + 6 };
            area.View = Ui.Box(m, "RouteView", x0 + 14, y0 + 78, 752, 500); area.View.gameObject.AddComponent<RectMask2D>(); area.Content = Ui.Box(area.View, "Content", 0, 0, 752, area.ContentH); area.X = x0 + 14; area.Y = y0 + 78;
            area.Thumb = Ui.Rounded(m, "RouteThumb", new Color(1, 1, 1, 0.3f), x0 + 760, y0 + 78, 4, 40, 2); _areas.Add(area);
            string cur = _mpMine.route != null ? _mpMine.route.code : null;
            RouteRow(area.Content, 0, "No route", "Decide later", true, cur == null, "Btn_Route_none", () => _ = ChooseRoute(null));
            for (int i = 0; i < routes.Length; i++)
            {
                var r = routes[i]; bool open = r.effective_status == "OPEN";
                string sub = (r.distance_km > 0 ? $"{r.distance_km:0.#} km  ·  {JobMath.FormatDuration(r.est_minutes)}" : "Distance not measured") + (open ? "" : r.effective_status == "LOCKED" ? $"  ·  unlocks at level {r.unlock_level}" : "  ·  planned, not open yet");
                string code = r.code; RouteRow(area.Content, i + 1, r.from + "  →  " + r.to, sub, open, cur == code, "Btn_Route_" + code, () => { if (open) _ = ChooseRoute(code); else Toast(r.effective_status == "LOCKED" ? "That route unlocks at level " + r.unlock_level + "." : "That route is planned and not open yet."); });
            }
            ApplyScroll(area, 0);
        }

        void RouteRow(Transform host, int i, string title, string sub, bool enabled, bool current, string click, Action act)
        {
            float y = 2 + i * 62;
            Ui.Outlined(host, "RRow", CardFill, current ? Gold : new Color(1, 1, 1, 0.1f), 6, y, 730, 56, 12, current ? 2.2f : 1.2f);
            Ui.Label(host, title, "Bold", 19, enabled ? White : new Color(0.6f, 0.62f, 0.68f), 22, y + 6, 520, 26); Ui.Label(host, sub, "Regular", 15, enabled ? Soft : Muted, 22, y + 31, 600, 20);
            if (current) Ui.Icon(host, "ic_check_circle", Brand.Good, 690, y + 14, 28); else if (!enabled) Ui.Icon(host, "ic_lock", Muted, 692, y + 15, 24);
            Ui.Click(host, click, 6, y, 730, 56, act);
        }

        // ---- invite
        string _invTab = "friends";
        void OpenInviteDialog()
        {
            if (_mpMine == null || !_mpMine.is_leader) { Toast("Only the leader can invite players."); return; }
            BuildInviteDialog();
            _ = ReloadInviteLists();
        }

        async Task ReloadInviteLists()
        {
            if (_svc.Friends.Friends.NeedsLoad(Time.unscaledTime, 60)) { await _svc.Friends.LoadFriends(); if (this != null && _modal != null && _invTab == "friends") BuildInviteDialog(); }
            if (_svc.Friends.Recent.NeedsLoad(Time.unscaledTime, 60)) { await _svc.Friends.LoadRecent(20); if (this != null && _modal != null && _invTab == "recent") BuildInviteDialog(); }
        }

        void BuildInviteDialog()
        {
            var m = OpenModal("Invite players", 780, 640); const float x0 = 570, y0 = 220;
            float vx = x0 + 28; vx += ModalChip(m, "Btn_InvTab_friends", vx, y0 + 74, "Friends", _invTab == "friends", () => { _invTab = "friends"; BuildInviteDialog(); }, 140) + 8;
            ModalChip(m, "Btn_InvTab_recent", vx, y0 + 74, "Recent players", _invTab == "recent", () => { _invTab = "recent"; BuildInviteDialog(); }, 190);
            var src = _invTab == "friends" ? _svc.Friends.Friends : _svc.Friends.Recent;
            if (src.State == LoadState.Loading || src.State == LoadState.Idle) { Ui.Label(m, "Loading...", "Regular", 19, Muted, x0 + 28, y0 + 140, 400, 26); return; }
            if (src.State == LoadState.Unavailable) { Ui.Label(m, src.Error ?? "Unavailable.", "SemiBold", 18, Brand.Bad, x0 + 28, y0 + 140, 700, 26); return; }
            var items = src.Value != null && src.Value.items != null ? src.Value.items : new PersonDto[0];
            if (items.Length == 0) { Ui.Label(m, _invTab == "friends" ? "You have no friends yet. Add players from the Active Players tab." : "You have not driven with anyone yet.", "Regular", 18, Muted, x0 + 28, y0 + 140, 700, 26); return; }
            const float rowH = 62;
            var area = new ScrollArea { Name = "mp_modal_invite", Height = 500, ContentH = items.Length * rowH + 6 };
            area.View = Ui.Box(m, "InvView", x0 + 14, y0 + 126, 752, 450); area.Height = 450; area.View.gameObject.AddComponent<RectMask2D>(); area.Content = Ui.Box(area.View, "Content", 0, 0, 752, area.ContentH); area.X = x0 + 14; area.Y = y0 + 126;
            area.Thumb = Ui.Rounded(m, "InvThumb", new Color(1, 1, 1, 0.3f), x0 + 760, y0 + 126, 4, 40, 2); _areas.Add(area);
            for (int i = 0; i < items.Length; i++)
            {
                var p = items[i]; float y = 2 + i * rowH; var host = area.Content; bool online = p.presence != null && p.presence.online;
                Ui.Outlined(host, "IRow", CardFill, new Color(1, 1, 1, 0.1f), 6, y, 730, 56, 12, 1.2f);
                Ui.Disc(host, "IDot", online ? Brand.Good : Muted, 18, y + 23, 10); Ui.Label(host, p.display_name, "Bold", 19, White, 40, y + 6, 360, 26); Ui.Label(host, "Level " + p.level + (online ? "  ·  online" : ""), "Regular", 15, Soft, 40, y + 31, 360, 20);
                Ui.Rounded(host, "InvBg", Gold, 600, y + 9, 124, 38, 10); Ui.Label(host, "Invite", "Bold", 17, Ink, 600, y + 16, 124, 26, TextAnchor.UpperCenter);
                var pl = p; Ui.Click(host, "Btn_InviteRow_" + SafeName(p.display_name), 600, y + 9, 124, 38, () => _ = InvitePlayer(pl.player_id, pl.display_name));
            }
            ApplyScroll(area, 0);
        }

        // ---- manage
        void OpenManageDialog()
        {
            if (_mpMine == null) return;
            var c = _mpMine; bool leader = c.is_leader;
            var m = OpenModal(leader ? "Manage convoy" : "Convoy", 780, leader ? 700 : 360); const float x0 = 570; float y0 = leader ? 190 : 360;
            Ui.Label(m, c.name, "Bold", 22, White, x0 + 28, y0 + 74, 600, 30);
            float y = y0 + 116;
            if (leader && !c.Driving)
            {
                Ui.Label(m, "Who can join", "SemiBold", 17, Soft, x0 + 28, y, 200, 24);
                float vx = x0 + 28; vx += ModalChip(m, "Btn_ManagePublic", vx, y + 28, "Public", !c.is_private, () => _ = ApplyOptions(null, false), 130) + 8;
                ModalChip(m, "Btn_ManagePrivate", vx, y + 28, "Invite only", c.is_private, () => _ = ApplyOptions(null, true), 150);
                Ui.Label(m, "Players", "SemiBold", 17, Soft, x0 + 470, y, 200, 24);
                ModalChip(m, "Btn_ManageCapMinus", x0 + 470, y + 28, "-", false, () => _ = ApplyOptions(Mathf.Max(Mathf.Max(2, c.member_count), c.capacity - 1), null), 46, 22);
                Ui.Label(m, c.capacity.ToString(), "Bold", 22, White, x0 + 520, y + 34, 50, 30, TextAnchor.UpperCenter);
                ModalChip(m, "Btn_ManageCapPlus", x0 + 574, y + 28, "+", false, () => _ = ApplyOptions(Mathf.Min(8, c.capacity + 1), null), 46, 22);
                y += 90;
            }
            if (leader)
            {
                Ui.Label(m, $"Members ({c.member_count}/{c.capacity})", "SemiBold", 17, Soft, x0 + 28, y, 300, 24);
                var area = new ScrollArea { Name = "mp_modal_members", Height = 230, ContentH = c.members.Length * 52 + 6 };
                area.View = Ui.Box(m, "MemView", x0 + 14, y + 28, 752, 230); area.View.gameObject.AddComponent<RectMask2D>(); area.Content = Ui.Box(area.View, "Content", 0, 0, 752, area.ContentH); area.X = x0 + 14; area.Y = y + 28;
                area.Thumb = Ui.Rounded(m, "MemThumb", new Color(1, 1, 1, 0.3f), x0 + 760, y + 28, 4, 40, 2); _areas.Add(area);
                for (int i = 0; i < c.members.Length; i++)
                {
                    var mb = c.members[i]; float ry = 2 + i * 52; var host = area.Content; bool me = mb.player_id == _svc.Profile.id;
                    Ui.Outlined(host, "MRow", CardFill, new Color(1, 1, 1, 0.1f), 6, ry, 730, 46, 10, 1.2f);
                    Ui.Label(host, mb.display_name + (mb.is_leader ? "  (leader)" : me ? "  (you)" : ""), "Bold", 18, White, 20, ry + 10, 380, 26); Ui.Label(host, "Level " + mb.level, "Regular", 15, Soft, 420, ry + 12, 100, 22);
                    if (!me)
                    {
                        Ui.Outlined(host, "LdBg", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.2f), 506, ry + 6, 124, 34, 9, 1.2f); Ui.Label(host, "Make leader", "SemiBold", 14, White, 506, ry + 13, 124, 22, TextAnchor.UpperCenter);
                        var mm = mb; Ui.Click(host, "Btn_MakeLeader_" + SafeName(mb.display_name), 506, ry + 6, 124, 34, () => _ = MakeLeader(mm));
                        Ui.Rounded(host, "KickBg", new Color(0.6f, 0.15f, 0.15f), 640, ry + 6, 84, 34, 9); Ui.Label(host, "Kick", "Bold", 15, White, 640, ry + 13, 84, 22, TextAnchor.UpperCenter);
                        Ui.Click(host, "Btn_Kick_" + SafeName(mb.display_name), 640, ry + 6, 84, 34, () => _ = KickMember(mm));
                    }
                }
                ApplyScroll(area, 0); y += 270;
            }
            if (leader && c.Driving) { ModalButton(m, "Btn_BackToLobby", x0 + 28, y, 330, "Back to lobby", new Color(1, 1, 1, 0.12f), White, () => _ = BackToLobby()); }
            ModalButton(m, "Btn_LeaveConvoy", x0 + (leader && c.Driving ? 380 : 28), leader ? y + (c.Driving ? 0 : 0) : y0 + 150, leader && c.Driving ? 372 : 330, "Leave convoy", new Color(0.6f, 0.15f, 0.15f), White, () => _ = LeaveConvoy());
        }
    }
}
