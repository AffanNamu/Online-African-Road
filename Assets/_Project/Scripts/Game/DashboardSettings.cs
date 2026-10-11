using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ARO.Backend;
using ARO.Core;
using ARO.NetCore;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ARO.Game
{
    /// <summary>
    /// The Settings page. The rows come from <see cref="SettingsCatalog"/> (pure, tested): each one is Live (applied on Save), OnReload (stored now, applied when
    /// the game next loads), Soon (the game has no system for it yet: shown, disabled, with the reason) or Fixed (a real value that cannot change).
    /// Edits are a draft until "Save Settings"; "Reset to Default Settings" resets the draft. Device settings are stored by <see cref="SettingsStore"/>; the two
    /// privacy items (Appear in Nearby Players, Blocked Players) are saved to the account at once through the Multiplayer backend calls.
    /// </summary>
    public sealed partial class DashboardScreen
    {
        SettingsTab _sTab = SettingsTab.Game; GameSettings _sDraft = new GameSettings(), _sBase = new GameSettings();
        string _sDrop; readonly Dictionary<string, Rect> _sCtl = new Dictionary<string, Rect>();     // each dropdown's rectangle, so its list opens under it
        bool _sNearbyKnown, _sNearby = false, _sBusy; LoadState _sBlockState = LoadState.Idle; BlockedPlayer[] _sBlocked = new BlockedPlayer[0]; string _sBlockErr;
        static readonly Color SetDim = new Color(1, 1, 1, 0.42f), SetTrack = new Color(1, 1, 1, 0.20f), SetRowLine = new Color(1, 1, 1, 0.05f), SetField = new Color(0.045f, 0.05f, 0.07f, 0.96f);

        static readonly (SettingsTab t, string icon)[] SetTabs =
        {
            (SettingsTab.Game, "ic_gamepad"), (SettingsTab.Graphics, "ic_monitor"), (SettingsTab.Controls, "ic_steering"), (SettingsTab.Audio, "ic_speaker"),
            (SettingsTab.Gameplay, "ic_car"), (SettingsTab.Account, "ic_user_circle"), (SettingsTab.Language, "icon_globe"),
        };

        public void OpenSettings() => ShowPage(Page.Settings);
        bool SDirty => !_sDraft.SameAs(_sBase);

        // ================================================================================================================ lifecycle
        void SetOpened()
        {
            _sDraft = SettingsStore.Current.Clone(); _sDraft.TrySet("display_mode", SettingsStore.IsFullScreen ? "fullscreen" : "windowed"); _sBase = _sDraft.Clone(); _sDrop = null;
            Debug.Log($"[Settings] opened tab={_sTab.ToString().ToLowerInvariant()} dirty=False");
            RefreshSettings(); _ = LoadPrivacy();
        }
        void SetClosed()
        {
            bool dirty = SDirty; _sDrop = null; _sDraft = _sBase.Clone();
            Debug.Log("[Settings] closed" + (dirty ? " (unsaved changes discarded)" : ""));
            if (dirty) Toast("Unsaved settings were discarded.");
        }

        void RefreshSettings() { if (SettingSlider.Dragging) return; Refill("SetTabs", BuildSetTabs); Refill("SetMain", BuildSetMain); Refill("SetPop", BuildSetPop); }

        async Task LoadPrivacy()
        {
            _sBlockState = LoadState.Loading; _sBlockErr = null; if (_sTab == SettingsTab.Gameplay) RefreshSettings();
            var n = _svc.Convoys.Nearby(1); var b = _svc.Convoys.Blocked(); var nr = await n; var br = await b; if (this == null) return;
            _sNearbyKnown = nr.Ok; if (nr.Ok) _sNearby = nr.Value.visible;
            if (br.Ok) { _sBlocked = br.Value.items ?? new BlockedPlayer[0]; _sBlockState = _sBlocked.Length == 0 ? LoadState.Empty : LoadState.Ready; }
            else { _sBlockState = LoadState.Unavailable; _sBlockErr = br.UserMessage; }
            Debug.Log($"[Settings] privacy nearby={(nr.Ok ? _sNearby.ToString().ToLowerInvariant() : "unknown")} blocked={(br.Ok ? _sBlocked.Length.ToString() : "unavailable")}");
            RefreshSettings();
        }

        async Task ToggleNearby()
        {
            if (_sBusy || !_sNearbyKnown) { if (!_sNearbyKnown) Toast("Your visibility could not be read. Try again in a moment."); return; }
            _sBusy = true; bool want = !_sNearby; var r = await _svc.Convoys.SetNearbyVisible(want); if (this == null) return; _sBusy = false;
            if (r.Ok) _sNearby = want;
            Debug.Log($"[Settings] nearby visible={want.ToString().ToLowerInvariant()} ok={r.Ok} error={r.ErrorCode}");
            Toast(r.Ok ? (want ? "You appear in nearby lists." : "You are hidden from nearby lists.") : "Could not change this. " + (r.UserMessage ?? "Try again."), 3f); RefreshSettings();
        }

        async Task Unblock(string id, string name)
        {
            if (_sBusy) return; _sBusy = true; var r = await _svc.Convoys.Unblock(id); if (this == null) return; _sBusy = false;
            Debug.Log($"[Settings] unblock ok={r.Ok} error={r.ErrorCode}");
            Toast(r.Ok ? name + " is unblocked." : "Could not unblock. " + (r.UserMessage ?? "Try again."), 3f); await LoadPrivacy();
        }

        // ================================================================================================================ actions
        void SetTab(SettingsTab t) { if (_sTab == t) return; _sTab = t; _sDrop = null; Debug.Log($"[Settings] tab={t.ToString().ToLowerInvariant()}"); if (t == SettingsTab.Gameplay) _ = LoadPrivacy(); RefreshSettings(); }

        void SetChange(string id, string value)
        {
            if (!_sDraft.TrySet(id, value)) { Debug.Log($"[Settings] refused id={id} value={value}"); return; }
            Debug.Log($"[Settings] set {id}={_sDraft.Get(id)}"); RefreshSettings();
        }

        void SaveSettings()
        {
            if (!SDirty) { Toast("There is nothing new to save."); Debug.Log("[Settings] save nothing"); return; }
            var changed = _sDraft.ChangedFrom(_sBase).ToList(); bool stored = SettingsStore.Save(_sDraft); _sBase = _sDraft.Clone();
            bool later = changed.Any(i => SettingsCatalog.Find(i).Status == SettingStatus.OnReload);
            Debug.Log($"[Settings] saved changed={string.Join(",", changed)} stored={stored.ToString().ToLowerInvariant()} reload={later.ToString().ToLowerInvariant()}");
            Toast(!stored ? "Applied for this visit only: this browser would not store the settings." : later ? "Settings saved. Graphics quality and traffic apply the next time the game loads." : "Settings saved.", 4f);
            RefreshSettings();
        }

        void ResetSettings()
        {
            if (_sDraft.IsDefault) { Toast("These are already the default settings."); Debug.Log("[Settings] reset nothing"); return; }
            _sDraft.ResetToDefaults(); Debug.Log("[Settings] reset to defaults (not saved yet)"); Toast("Defaults restored. Press Save Settings to keep them.", 4f); RefreshSettings();
        }

        void SetNote(SettingDef d) { Debug.Log($"[Settings] note id={d.Id}"); if (!string.IsNullOrEmpty(d.Note)) Toast(d.Label + ": " + d.Note, 4f); }

        // ================================================================================================================ build
        void BuildSettingsPage()
        {
            Region("SetTop", 0.02f); BuildSetTop();
            Region("SetTabs", 0.05f); BuildSetTabs();
            Region("SetMain", 0.08f); BuildSetMain();
            Region("SetPop", 0.30f); BuildSetPop();
        }

        void BuildSetTop()
        {
            Ui.Icon(_stage, "ic_gear", Gold, 322, 138, 56);
            var t = Ui.Label(_stage, "Settings", "ExtraBold", 52, White, 396, 128, 600, 64); Ui.Drop(t, 0.5f);
            Ui.Label(_stage, "Customize your game experience, controls, graphics and account settings.", "Regular", 20, new Color(0.84f, 0.86f, 0.9f), 396, 186, 1200, 28);
        }

        void BuildSetTabs()
        {
            float x = 308;
            foreach (var tab in SetTabs)
            {
                bool on = tab.t == _sTab; string label = SettingsCatalog.TabLabel(tab.t);
                var probe = Ui.Label(_stage, label, on ? "Bold" : "SemiBold", 19, White, 0, 0, 10, 10); float tw = probe.preferredWidth; Destroy(probe.gameObject);
                float w = 18 + 26 + 10 + tw + 18, y = 207, h = 46;
                if (on) Ui.Rounded(_stage, "TabOn", Gold, x, y, w, h, 12); else Ui.Outlined(_stage, "Tab", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.14f), x, y, w, h, 12);
                Ui.Icon(_stage, tab.icon, on ? Ink : new Color(0.86f, 0.88f, 0.92f), x + 16, y + 10, 26); Ui.Label(_stage, label, on ? "Bold" : "SemiBold", 19, on ? Ink : White, x + 50, y + 10, tw + 6, 28);
                var tt = tab.t; Ui.Click(_stage, "Btn_SetTab_" + tt, x, y, w, h, () => SetTab(tt)); x += w + 10;
            }
        }

        void BuildSetMain()
        {
            _sCtl.Clear();
            switch (_sTab)
            {
                case SettingsTab.Game: BuildSetOverview(); break;
                case SettingsTab.Graphics: DrawPanel(SettingsPanel.Graphics, 308, 272, 760, 40); DrawSaveBar(308, 272 + PanelHeight(SettingsPanel.Graphics, 40) + 18, 760, false); BuildQualityCard(1096, 272, 794); break;
                case SettingsTab.Controls: DrawPanel(SettingsPanel.Input, 308, 272, 760, 46); DrawSaveBar(308, 272 + PanelHeight(SettingsPanel.Input, 46) + 18, 760, false); BuildBindingsCard(1096, 272, 794); break;
                case SettingsTab.Audio: DrawPanel(SettingsPanel.Audio, 308, 272, 760, 46); DrawSaveBar(308, 272 + PanelHeight(SettingsPanel.Audio, 46) + 18, 760, false);
                    NoteCard(1096, 272, 794, 300, "ic_speaker", "About the sound", "Master Volume scales everything you hear. The engine is the only sound in the game today, so Engine Sound is the only other slider that works.", "Environment, Radio Music, UI Sounds and Voice Chat are shown for when those sounds exist; they are marked Soon and cannot be changed."); break;
                case SettingsTab.Gameplay: DrawPanel(SettingsPanel.Gameplay, 308, 272, 760, 46); DrawSaveBar(308, 272 + PanelHeight(SettingsPanel.Gameplay, 46) + 18, 760, false); BuildBlockedCard(1096, 272, 794, 540); break;
                case SettingsTab.Account: BuildAccountCard(308, 272, 760); NoteCard(1096, 272, 794, 330, "ic_info", "Where your settings are kept",
                    "Graphics, sound, controls and display choices are saved on this device, in this browser.", "Appear in Nearby Players and your blocked players are saved to your account and follow you to another device.", "Language, notification choices, voice and account deletion are not available yet."); break;
                case SettingsTab.Language: DrawPanel(SettingsPanel.Language, 308, 272, 760, 46); NoteCard(1096, 272, 794, 300, "icon_globe", "Language and region",
                    "English is the only language in the game today. The other languages in the list will appear when translations exist.", "The world, the clock and the money are West Africa and the Nigerian Naira, so Region, Distance Units and Currency are shown as facts, not choices."); break;
            }
        }

        void BuildSetOverview()
        {
            const float c1 = 308, c2 = 843, c3 = 1378, w = 511, y0 = 266, pitch = 34, gap = 14;
            float h1 = DrawPanel(SettingsPanel.Game, c1, y0, w, pitch); DrawPanel(SettingsPanel.Language, c1, y0 + h1 + gap, w, pitch);
            float h2 = DrawPanel(SettingsPanel.Graphics, c2, y0, w, pitch); DrawPanel(SettingsPanel.Gameplay, c2, y0 + h2 + gap, w, pitch);
            float h3 = DrawPanel(SettingsPanel.Audio, c3, y0, w, pitch); float y = y0 + h3 + gap; float h4 = DrawPanel(SettingsPanel.Input, c3, y, w, pitch, 50);
            float by = y + h4 - 46;
            Ui.Outlined(_stage, "AdvBtn", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.16f), c3 + 12, by, w - 24, 38, 10, 1.2f);
            Ui.Label(_stage, "Advanced Controls", "SemiBold", 17, White, c3 + 12, by + 7, w - 24, 26, TextAnchor.UpperCenter); Ui.Icon(_stage, "ic_chevron_right", Gold, c3 + w - 46, by + 9, 20);
            Ui.Click(_stage, "Btn_SetAdvanced", c3 + 12, by, w - 24, 38, () => SetTab(SettingsTab.Controls));
            DrawSaveBar(c3, y + h4 + 16, w, true);
        }

        // ---------------------------------------------------------------- panels and rows
        static string PanelIcon(SettingsPanel p) => p == SettingsPanel.Game ? "ic_gamepad" : p == SettingsPanel.Language ? "icon_globe" : p == SettingsPanel.Graphics ? "ic_monitor" : p == SettingsPanel.Gameplay ? "ic_car" : p == SettingsPanel.Audio ? "ic_speaker" : "ic_steering";
        float PanelHeight(SettingsPanel p, float pitch, float extra = 0f) => 54 + SettingsCatalog.Of(p).Count() * pitch + 12 + extra;

        static string RowIcon(string id)
        {
            switch (id)
            {
                case "game_mode": case "force_feedback": return "ic_gamepad";
                case "speed_unit": case "vsync": case "brake_sens": case "contrast": case "show_fps": return "ic_gauge";
                case "time_of_day": return "ic_clock";
                case "traffic": case "engine_volume": return "ic_car";
                case "weather": return "ic_cloud";
                case "hints": return "ic_info";
                case "nav_assist": case "region": return "ic_pin";
                case "auto_parking": return "ic_parking";
                case "minimap": return "ic_map";
                case "camera_shake": return "ic_camera";
                case "language": return "icon_globe";
                case "distance_units": case "view_distance": return "ic_road";
                case "currency": return "ic_dollar";
                case "quality": case "aa": return "ic_gear";
                case "resolution": case "display_mode": return "ic_monitor";
                case "shadows": case "reflections": case "brightness": return "ic_sun";
                case "textures": return "ic_box";
                case "post": case "accel_sens": return "ic_lightning";
                case "saturation": case "environment_volume": return "ic_leaf";
                case "show_names": case "appear_nearby": return "ic_people";
                case "notifications": case "ui_volume": return "ic_bell";
                case "chat_messages": return "ic_chat";
                case "profanity": return "ic_shield";
                case "master_volume": return "ic_speaker";
                case "radio_volume": return "ic_music";
                case "voice_volume": return "ic_mic";
                case "steering": return "ic_steering";
                case "invert_y": return "ic_arrow_up";
                default: return "ic_gear";
            }
        }

        string InfoText(SettingDef d)
        {
            if (d.FixedText != null) return d.FixedText;
            string s = d.Id == "aa" ? RuntimeSettings.AntiAliasing : d.Id == "view_distance" ? RuntimeSettings.ViewDistance : "";
            return string.IsNullOrEmpty(s) ? "Set when the game loads" : s;
        }

        float DrawPanel(SettingsPanel p, float x, float y, float w, float pitch, float extra = 0f)
        {
            var defs = SettingsCatalog.Of(p).ToList(); float h = PanelHeight(p, pitch, extra);
            Ui.Outlined(_stage, "Panel_" + p, PanelFill, PanelLine, x, y, w, h, 18, 1.5f);
            Ui.Icon(_stage, PanelIcon(p), Gold, x + 18, y + 12, 30); Ui.Label(_stage, SettingsCatalog.PanelTitle(p), "Bold", 24, White, x + 58, y + 11, w - 80, 32);
            Ui.Flat(_stage, "Div", SetRowLine, x + 16, y + 50, w - 32, 1.5f);
            for (int i = 0; i < defs.Count; i++) DrawRow(defs[i], x + 8, y + 54 + i * pitch, w - 16, pitch);
            return h;
        }

        void DrawRow(SettingDef d, float x, float y, float w, float pitch)
        {
            bool soon = d.Status == SettingStatus.Soon; Color lab = soon ? SetDim : White; float ih = 22, cx = x + w * 0.45f, cw = w * 0.55f - 6;
            Ui.Icon(_stage, RowIcon(d.Id), soon ? SetDim : new Color(0.9f, 0.92f, 0.96f), x + 10, y + (pitch - ih) / 2, ih);
            var lbl = Ui.Label(_stage, d.Label, "SemiBold", 17, lab, x + 44, y + (pitch - 24) / 2, w * 0.45f - 48, 24); lbl.resizeTextForBestFit = true; lbl.resizeTextMinSize = 12; lbl.resizeTextMaxSize = 17; lbl.horizontalOverflow = HorizontalWrapMode.Wrap; lbl.verticalOverflow = VerticalWrapMode.Truncate;   // best fit shrinks the text until it is ONE line in the box (a wrapped label ran into the next row)
            Ui.Click(_stage, "Btn_SetNote_" + d.Id, x, y, cx - x - 4, pitch, () => SetNote(d));
            switch (d.Kind)
            {
                case SettingKind.Choice: DrawChoice(d, cx, y, cw, pitch); break;
                case SettingKind.Toggle: DrawToggle(d, cx, y, cw, pitch); break;
                case SettingKind.Slider: DrawSlider(d, cx, y, cw, pitch); break;
                default: var t = Ui.Label(_stage, InfoText(d), "Regular", 16, new Color(0.84f, 0.86f, 0.9f), cx + 12, y + (pitch - 22) / 2, cw - 12, 22); t.resizeTextForBestFit = true; t.resizeTextMinSize = 11; t.resizeTextMaxSize = 16; break;
            }
        }

        void SoonChip(float right, float y, float pitch)
        {
            Ui.Rounded(_stage, "SoonChip", new Color(Gold.r, Gold.g, Gold.b, 0.18f), right - 50, y + (pitch - 20) / 2, 46, 20, 10);
            Ui.Label(_stage, "SOON", "Bold", 11, Gold, right - 50, y + (pitch - 20) / 2 + 2, 46, 16, TextAnchor.UpperCenter);
        }

        void DrawChoice(SettingDef d, float cx, float y, float cw, float pitch)
        {
            bool edit = d.Editable; float bh = pitch - 8, by = y + 4; var rect = new Rect(cx, by, cw, bh); string val = d.LabelOf(_sDraft.Get(d.Id));
            Ui.Outlined(_stage, "Drop_" + d.Id, SetField, new Color(1, 1, 1, edit ? (_sDrop == d.Id ? 0.6f : 0.20f) : 0.08f), cx, by, cw, bh, 9, _sDrop == d.Id ? 2f : 1.2f);
            var t = Ui.Label(_stage, val, "Regular", 16, edit ? White : SetDim, cx + 12, by + (bh - 22) / 2, cw - 52, 22); t.resizeTextForBestFit = true; t.resizeTextMinSize = 11; t.resizeTextMaxSize = 16;
            if (edit)
            {
                Ui.Icon(_stage, "ic_chevron_down", new Color(0.86f, 0.88f, 0.92f), cx + cw - 28, by + (bh - 16) / 2, 16); _sCtl[d.Id] = rect;
                var id = d.Id; Ui.Click(_stage, "Btn_SetDrop_" + id, cx, by, cw, bh, () => { _sDrop = _sDrop == id ? null : id; Refill("SetMain", BuildSetMain); Refill("SetPop", BuildSetPop); });
            }
            else SoonChip(cx + cw, y, pitch);
        }

        void DrawToggle(SettingDef d, float cx, float y, float cw, float pitch)
        {
            bool edit = d.Editable || d.Server, on = d.Server ? (_sNearbyKnown && _sNearby) : _sDraft.Bool(d.Id), soon = d.Status == SettingStatus.Soon; float sy = y + (pitch - 24) / 2;
            float a = soon ? 0.4f : 1f;
            Ui.Rounded(_stage, "Switch_" + d.Id, on ? new Color(Gold.r, Gold.g, Gold.b, a) : new Color(1, 1, 1, 0.22f * (soon ? 0.6f : 1f)), cx, sy, 46, 24, 12);
            Ui.Disc(_stage, "Knob", new Color(1, 1, 1, soon ? 0.6f : 1f), on ? cx + 25 : cx + 3, sy + 3, 18);
            string text = d.Server && !_sNearbyKnown ? "Unavailable" : on ? "Enabled" : "Disabled";
            Ui.Label(_stage, text, "Regular", 16, soon ? SetDim : new Color(0.9f, 0.92f, 0.96f), cx + 58, y + (pitch - 22) / 2, 120, 22);
            if (soon) { SoonChip(cx + cw, y, pitch); return; }
            if (!edit) return; var id = d.Id; bool server = d.Server;
            Ui.Click(_stage, "Btn_SetToggle_" + id, cx, y, 150, pitch, () => { if (server) _ = ToggleNearby(); else SetChange(id, _sDraft.Bool(id) ? "0" : "1"); });
            if (server) Ui.Label(_stage, "saved now", "Regular", 12, Muted, cx + cw - 80, y + (pitch - 18) / 2, 76, 18, TextAnchor.UpperRight);
        }

        void DrawSlider(SettingDef d, float cx, float y, float cw, float pitch)
        {
            bool edit = d.Editable; int v = _sDraft.Int(d.Id); float tw = cw - 62, h = 28, sy = y + (pitch - h) / 2;
            if (!edit)
            {
                float f = (v - d.Min) / (float)Math.Max(1, d.Max - d.Min);
                Ui.Rounded(_stage, "TrackDim", new Color(1, 1, 1, 0.10f), cx, sy + h / 2 - 3, tw, 6, 3); Ui.Rounded(_stage, "FillDim", new Color(1, 1, 1, 0.22f), cx, sy + h / 2 - 3, Mathf.Max(6f, tw * f), 6, 3);
                SoonChip(cx + cw, y, pitch); return;
            }
            var go = new GameObject("Slider_" + d.Id, typeof(RectTransform), typeof(Image), typeof(Button), typeof(SettingSlider)); var rt = Ui.Place(go, _stage, Ui.Anchor.TopLeft, cx, sy, tw, h);
            var img = go.GetComponent<Image>(); img.color = new Color(1, 1, 1, 0.001f); var btn = go.GetComponent<Button>(); btn.targetGraphic = img; btn.transition = Selectable.Transition.None;
            Ui.Rounded(go.transform, "Track", SetTrack, 0, h / 2 - 3, tw, 6, 3);
            var fill = Ui.Rounded(go.transform, "Fill", Gold, 0, h / 2 - 3, 6, 6, 3); var knob = Ui.Disc(go.transform, "Knob", White, 0, h / 2 - 9, 18);
            var val = Ui.Label(_stage, v + "%", "Regular", 16, White, cx + tw + 16, y + (pitch - 22) / 2, 46, 22);
            var s = go.GetComponent<SettingSlider>(); string id = d.Id; s.Rect = rt; s.Fill = fill; s.Knob = (RectTransform)knob.transform; s.Value = val; s.Min = d.Min; s.Max = d.Max; s.Step = d.Step;
            s.Changed = n => _sDraft.SetInt(id, n); s.Ended = () => { Debug.Log($"[Settings] set {id}={_sDraft.Get(id)}"); RefreshSettings(); }; s.Init(v);
        }

        // ---------------------------------------------------------------- save bar
        void DrawSaveBar(float x, float y, float w, bool stacked)
        {
            bool dirty = SDirty;
            if (stacked)
            {
                Ui.Disc(_stage, "StateDot", dirty ? Gold : Brand.Good, x + 4, y - 20, 10); Ui.Label(_stage, dirty ? "Unsaved changes" : "All changes saved", "SemiBold", 15, dirty ? Gold : Muted, x + 22, y - 25, 300, 20);
                Ui.Outlined(_stage, "ResetBtn", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.22f), x, y, w, 48, 12, 1.3f);
                Ui.Icon(_stage, "ic_reset", White, x + w / 2 - 120, y + 12, 24); Ui.Label(_stage, "Reset to Default Settings", "SemiBold", 18, White, x + w / 2 - 88, y + 11, 240, 26);
                Ui.Click(_stage, "Btn_SetReset", x, y, w, 48, ResetSettings);
                Ui.Rounded(_stage, "SaveBtn", dirty ? Gold : new Color(Gold.r, Gold.g, Gold.b, 0.55f), x, y + 58, w, 56, 14);
                Ui.Icon(_stage, "ic_save", Ink, x + w / 2 - 92, y + 74, 26); Ui.Label(_stage, "Save Settings", "ExtraBold", 21, Ink, x + w / 2 - 58, y + 72, 200, 30);
                Ui.Click(_stage, "Btn_SetSave", x, y + 58, w, 56, SaveSettings);
                return;
            }
            float half = (w - 14) / 2;
            Ui.Disc(_stage, "StateDot", dirty ? Gold : Brand.Good, x + 4, y + 4, 10); Ui.Label(_stage, dirty ? "Unsaved changes" : "All changes saved", "SemiBold", 15, dirty ? Gold : Muted, x + 22, y - 1, 300, 20);
            y += 30;
            Ui.Outlined(_stage, "ResetBtn", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.22f), x, y, half, 50, 12, 1.3f);
            Ui.Icon(_stage, "ic_reset", White, x + half / 2 - 112, y + 13, 24); Ui.Label(_stage, "Reset to Defaults", "SemiBold", 18, White, x + half / 2 - 80, y + 12, 220, 26); Ui.Click(_stage, "Btn_SetReset", x, y, half, 50, ResetSettings);
            Ui.Rounded(_stage, "SaveBtn", dirty ? Gold : new Color(Gold.r, Gold.g, Gold.b, 0.55f), x + half + 14, y, half, 50, 12);
            Ui.Icon(_stage, "ic_save", Ink, x + half + 14 + half / 2 - 84, y + 12, 26); Ui.Label(_stage, "Save Settings", "ExtraBold", 20, Ink, x + half + 14 + half / 2 - 50, y + 10, 200, 30); Ui.Click(_stage, "Btn_SetSave", x + half + 14, y, half, 50, SaveSettings);
        }

        // ---------------------------------------------------------------- the dropdown list
        void BuildSetPop()
        {
            if (_sDrop == null || !_sCtl.TryGetValue(_sDrop, out var r)) return; var d = SettingsCatalog.Find(_sDrop); if (d == null) return;
            Ui.Click(_stage, "Btn_SetDropClose", 0, 0, 1920, 1080, () => { _sDrop = null; Refill("SetMain", BuildSetMain); Refill("SetPop", BuildSetPop); }).GetComponent<HoverGlow>().enabled = false;
            const float ih = 34; float h = d.Options.Length * ih + 8, y = r.yMax + 4; if (y + h > 1070) y = r.y - h - 4;
            Ui.Outlined(_stage, "DropList", new Color(0.07f, 0.078f, 0.10f, 1f), new Color(Gold.r, Gold.g, Gold.b, 0.6f), r.x, y, r.width, h, 10, 1.5f);
            string cur = _sDraft.Get(d.Id);
            for (int i = 0; i < d.Options.Length; i++)
            {
                string opt = d.Options[i]; bool on = opt == cur; float iy = y + 4 + i * ih;
                if (on) Ui.Rounded(_stage, "Cur", new Color(Gold.r, Gold.g, Gold.b, 0.22f), r.x + 4, iy, r.width - 8, ih - 2, 7);
                Ui.Label(_stage, d.OptionLabels[i], on ? "Bold" : "Regular", 16, on ? Gold : White, r.x + 14, iy + 5, r.width - 40, 24);
                string id = d.Id; Ui.Click(_stage, "Btn_SetOpt_" + id + "_" + opt, r.x + 4, iy, r.width - 8, ih - 2, () => { _sDrop = null; SetChange(id, opt); });
            }
        }

        // ---------------------------------------------------------------- cards
        void NoteCard(float x, float y, float w, float h, string icon, string title, params string[] paras)
        {
            Ui.Outlined(_stage, "NoteCard", PanelFill, PanelLine, x, y, w, h, 18, 1.5f); Ui.Icon(_stage, icon, Gold, x + 20, y + 14, 30); Ui.Label(_stage, title, "Bold", 24, White, x + 62, y + 13, w - 90, 32);
            Ui.Flat(_stage, "Div", SetRowLine, x + 16, y + 52, w - 32, 1.5f); float py = y + 68;
            foreach (var p in paras) { var t = Ui.Paragraph(_stage, p, "Regular", 18, new Color(0.86f, 0.88f, 0.92f), x + 24, py, w - 48, 80); py += Mathf.Max(34f, t.preferredHeight + 14f); }
        }

        void BuildQualityCard(float x, float y, float w)
        {
            string pick = _sDraft.Get("quality"), now = string.IsNullOrEmpty(RuntimeSettings.TierName) ? "not built yet" : RuntimeSettings.TierName;
            NoteCard(x, y, w, 420, "ic_monitor", "What the game is using now", "Running now: " + now + (string.IsNullOrEmpty(RuntimeSettings.TierSummary) ? "" : "\n" + RuntimeSettings.TierSummary),
                "Your choice: " + SettingsCatalog.Find("quality").LabelOf(pick) + (pick != "auto" && !string.Equals(pick, RuntimeSettings.TierName, StringComparison.OrdinalIgnoreCase) ? "  (applies the next time the game loads)" : ""),
                "Graphics Quality and Traffic Density are decided when the world is built, so they apply the next time the game loads. Shadows, Post Processing, the FPS counter and Display Mode apply when you press Save.",
                "A web address with ?quality=low, medium or high always wins over this setting.");
        }

        void BuildBindingsCard(float x, float y, float w)
        {
            Ui.Outlined(_stage, "BindCard", PanelFill, PanelLine, x, y, w, 520, 18, 1.5f); Ui.Icon(_stage, "ic_keyboard", Gold, x + 20, y + 14, 30); Ui.Label(_stage, "How you drive", "Bold", 24, White, x + 62, y + 13, w - 90, 32);
            Ui.Flat(_stage, "Div", SetRowLine, x + 16, y + 52, w - 32, 1.5f);
            float colW = (w - 48) / 2, ky = y + 66, gy = y + 66; bool left;
            foreach (var group in new[] { "Keyboard", "Gamepad", "Mouse" })
            {
                left = group == "Keyboard"; float cx = left ? x + 20 : x + 28 + colW; float cy = left ? ky : gy;
                Ui.Label(_stage, group.ToUpperInvariant(), "Bold", 14, Gold, cx, cy, colW, 20); cy += 26;
                foreach (var b in ControlBindings.All.Where(b => b.Device == group))
                {
                    Ui.Label(_stage, b.Action, "Regular", 16, new Color(0.86f, 0.88f, 0.92f), cx, cy, colW * 0.55f, 22);
                    Ui.Rounded(_stage, "KeyChip", new Color(1, 1, 1, 0.10f), cx + colW * 0.52f, cy - 2, colW * 0.48f - 8, 26, 8); Ui.Label(_stage, b.Keys, "SemiBold", 15, White, cx + colW * 0.52f, cy + 1, colW * 0.48f - 8, 22, TextAnchor.UpperCenter);
                    cy += 32;
                }
                if (left) ky = cy + 8; else gy = cy + 14;
            }
            Ui.Paragraph(_stage, "These are the keys the game reads today. Changing them is not available yet.", "Regular", 15, Muted, x + 20, y + 470, w - 40, 40);
        }

        void BuildBlockedCard(float x, float y, float w, float h)
        {
            Ui.Outlined(_stage, "BlockCard", PanelFill, PanelLine, x, y, w, h, 18, 1.5f); Ui.Icon(_stage, "ic_shield", Gold, x + 20, y + 14, 30); Ui.Label(_stage, "Blocked Players", "Bold", 24, White, x + 62, y + 13, 400, 32);
            Ui.Label(_stage, "saved to your account", "Regular", 14, Muted, x + w - 220, y + 20, 200, 20, TextAnchor.UpperRight); Ui.Flat(_stage, "Div", SetRowLine, x + 16, y + 52, w - 32, 1.5f);
            if (_sBlockState == LoadState.Idle || _sBlockState == LoadState.Loading) { Ui.Label(_stage, "Loading blocked players...", "SemiBold", 19, Muted, x, y + 120, w, 30, TextAnchor.UpperCenter); return; }
            if (_sBlockState == LoadState.Unavailable)
            {
                Ui.Label(_stage, "Could not load your blocked players.", "Bold", 20, Brand.Bad, x + 24, y + 80, w - 48, 28); Ui.Paragraph(_stage, _sBlockErr ?? "Please try again.", "Regular", 16, Soft, x + 24, y + 112, w - 48, 40);
                Ui.Rounded(_stage, "RetryBtn", Gold, x + 24, y + 160, 150, 42, 11); Ui.Label(_stage, "Retry", "Bold", 18, Ink, x + 24, y + 168, 150, 26, TextAnchor.UpperCenter); Ui.Click(_stage, "Btn_SetRetryBlocked", x + 24, y + 160, 150, 42, () => _ = LoadPrivacy()); return;
            }
            if (_sBlocked.Length == 0)
            {
                Ui.Icon(_stage, "ic_shield", new Color(1, 1, 1, 0.2f), x + 24, y + 80, 48); Ui.Label(_stage, "You have not blocked anyone.", "Bold", 19, White, x + 84, y + 82, w - 110, 26);
                Ui.Paragraph(_stage, "Players you block from a chat line or the Players Nearby list appear here, and you can unblock them.", "Regular", 16, Soft, x + 84, y + 112, w - 110, 60); return;
            }
            const float rowH = 66, pitch = 72; var area = MakeScroll("set_blocked", x + 10, y + 62, w - 20, h - 76, _sBlocked.Length * pitch + 4);
            for (int i = 0; i < _sBlocked.Length; i++)
            {
                var p = _sBlocked[i]; float ry = 2 + i * pitch; Ui.Outlined(area.Content, "BlockRow", CardFill, new Color(1, 1, 1, 0.10f), 2, ry, w - 44, rowH, 12, 1.2f);
                Ui.Icon(area.Content, "ic_user_circle", new Color(1, 1, 1, 0.5f), 16, ry + 15, 36); Ui.Label(area.Content, p.display_name ?? "Player", "Bold", 19, White, 66, ry + 8, w - 300, 26);
                Ui.Label(area.Content, $"Level {p.level}" + (string.IsNullOrEmpty(p.since) ? "" : "  -  blocked " + p.since.Substring(0, Math.Min(10, p.since.Length))), "Regular", 14, Muted, 66, ry + 36, w - 300, 20);
                Ui.Outlined(area.Content, "UnblockBtn", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.25f), w - 190, ry + 14, 130, 38, 10, 1.3f); Ui.Label(area.Content, "Unblock", "Bold", 17, White, w - 190, ry + 21, 130, 24, TextAnchor.UpperCenter);
                var pid = p.player_id; var pn = p.display_name ?? "Player"; Ui.Click(area.Content, "Btn_SetUnblock_" + i, w - 190, ry + 14, 130, 38, () => _ = Unblock(pid, pn));
            }
            RestoreScroll(area);
        }

        void BuildAccountCard(float x, float y, float w)
        {
            var pr = _svc.Profile; Ui.Outlined(_stage, "AccountCard", PanelFill, PanelLine, x, y, w, 640, 18, 1.5f);
            Ui.Icon(_stage, "ic_user_circle", Gold, x + 20, y + 14, 30); Ui.Label(_stage, "Account", "Bold", 24, White, x + 62, y + 13, 400, 32); Ui.Flat(_stage, "Div", SetRowLine, x + 16, y + 52, w - 32, 1.5f);
            if (pr == null) { Ui.Label(_stage, "Your profile is not loaded.", "SemiBold", 19, Muted, x, y + 120, w, 30, TextAnchor.UpperCenter); return; }
            var nm = Ui.Label(_stage, pr.display_name ?? "Driver", "ExtraBold", 34, White, x + 28, y + 68, w - 56, 44); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 20; nm.resizeTextMaxSize = 34;
            var rows = new[] { ("Level", pr.level.ToString()), ("Experience", pr.experience.ToString("N0") + " XP"), ("Jobs completed", pr.jobs_completed.ToString("N0")), ("Distance driven", pr.distance_km.ToString("N1") + " km"),
                               ("Coins", Sum != null ? "₦ " + Sum.coins.ToString("N0") : "-"), ("Vehicles owned", Sum != null ? Sum.vehicle_count.ToString() : "-") };
            for (int i = 0; i < rows.Length; i++)
            {
                float ry = y + 126 + i * 44; Ui.Flat(_stage, "RowLine", SetRowLine, x + 24, ry + 41, w - 48, 1.5f);
                Ui.Label(_stage, rows[i].Item1, "SemiBold", 18, new Color(0.84f, 0.86f, 0.9f), x + 28, ry + 7, 300, 26); Ui.Label(_stage, rows[i].Item2, "Bold", 19, White, x + w - 328, ry + 6, 300, 28, TextAnchor.UpperRight);
            }
            float by = y + 126 + rows.Length * 44 + 26;
            Ui.Outlined(_stage, "SignOutBtn", new Color(0.30f, 0.07f, 0.07f, 0.9f), new Color(0.97f, 0.42f, 0.38f, 0.8f), x + 24, by, w - 48, 54, 13, 1.5f);
            Ui.Icon(_stage, "ic_logout", Brand.Bad, x + w / 2 - 90, by + 14, 26); Ui.Label(_stage, "Sign Out", "Bold", 20, White, x + w / 2 - 54, by + 12, 200, 30); Ui.Click(_stage, "Btn_SetSignOut", x + 24, by, w - 48, 54, () => _act.SignOut());
            var later = new[] { ("Change Password", "ic_lock", "Password changes are not available in the game yet."), ("Delete Account", "ic_shield", "Account deletion is not available in the game yet.") };
            for (int i = 0; i < later.Length; i++)
            {
                float ly = by + 68 + i * 54; Ui.Outlined(_stage, "LaterBtn", new Color(0.05f, 0.055f, 0.075f, 0.6f), new Color(1, 1, 1, 0.08f), x + 24, ly, w - 48, 46, 12, 1.2f);
                Ui.Icon(_stage, later[i].Item2, SetDim, x + 40, ly + 11, 24); Ui.Label(_stage, later[i].Item1, "SemiBold", 18, SetDim, x + 78, ly + 10, 300, 26); SoonChip(x + w - 36, ly, 46);
                var msg = later[i].Item3; var key = later[i].Item1.Replace(" ", ""); Ui.Click(_stage, "Btn_SetSoon_" + key, x + 24, ly, w - 48, 46, () => { Debug.Log("[Settings] note id=" + key); Toast(msg, 4f); });
            }
        }
    }

    /// <summary>A draggable slider for the Settings page. While one is being dragged the page does not rebuild (that would destroy the slider under the pointer).</summary>
    public sealed class SettingSlider : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public static bool Dragging;
        public RectTransform Rect, Knob; public Image Fill; public Text Value; public int Min, Max = 100, Step = 1; public Action<int> Changed; public Action Ended;
        int _v; bool _mine;

        public void Init(int v) { _v = v; Show(); }
        void Show()
        {
            float f = Max > Min ? (float)(_v - Min) / (Max - Min) : 0f, w = Rect.rect.width;
            Fill.rectTransform.sizeDelta = new Vector2(Mathf.Max(6f, w * f), 6f); Knob.anchoredPosition = new Vector2(w * f - 9f, Knob.anchoredPosition.y); Value.text = _v + "%";
        }
        void Set(PointerEventData e)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, e.position, e.pressEventCamera, out Vector2 lp)) return;
            var r = Rect.rect; float f = Mathf.Clamp01((lp.x - r.xMin) / Mathf.Max(1f, r.width)); int raw = Min + Mathf.RoundToInt(f * (Max - Min));
            int n = Mathf.Clamp(Min + Mathf.RoundToInt((raw - Min) / (float)Mathf.Max(1, Step)) * Mathf.Max(1, Step), Min, Max);
            if (n != _v) { _v = n; Show(); Changed?.Invoke(_v); }
        }
        public void OnPointerDown(PointerEventData e) { _mine = true; Dragging = true; Set(e); }
        public void OnDrag(PointerEventData e) { Set(e); }
        public void OnPointerUp(PointerEventData e) { if (!_mine) return; _mine = false; Dragging = false; Ended?.Invoke(); }
        void OnDestroy() { if (_mine) { _mine = false; Dragging = false; } }
    }
}
