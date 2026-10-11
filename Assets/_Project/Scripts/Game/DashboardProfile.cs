using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ARO.Backend;
using ARO.Core;
using ARO.NetCore;
using UnityEngine;
using UnityEngine.UI;

namespace ARO.Game
{
    /// <summary>
    /// The player's own profile page (opened from the avatar in the header, or after onboarding). Every figure is the server's: the profile (level, XP window,
    /// jobs, distance, earnings, vehicles, friends), the four leaderboard ranks, the achievements and the stored preferences (country, language, play style).
    /// Name and username are edited in a dialog; country, language and play style change in place; each change is one server call and the page shows what the server answered.
    /// </summary>
    public sealed partial class DashboardScreen
    {
        const float PrX = 330, PrW = 1550;
        RectTransform _prRoot; bool _prOpen; float _nextPr;
        LoadState _prRankState = LoadState.Idle, _prAchState = LoadState.Idle; int[] _prRank = new int[4]; double[] _prValue = new double[4]; int[] _prTotal = new int[4]; AchievementItem[] _prAch = new AchievementItem[0]; string _prAchErr, _prRankErr;
        bool _prBusy; InputField _prNameField, _prUserField, _prCountrySearch; RectTransform _prCountryHost; Text _prEditErr; string _prCountryQuery = "", _prCountryPending = ""; float _prCountryDue = -1f;
        static readonly BoardMetric[] PrMetrics = { BoardMetric.Xp, BoardMetric.Earnings, BoardMetric.Distance, BoardMetric.Jobs };

        public void OpenProfile() => ShowPage(Page.Profile);
        ProfileFull Me => _svc.Me;

        void PrOpened() { _prOpen = true; _nextPr = Time.unscaledTime + 30f; Debug.Log("[Profile] opened"); _ = PrLoad(false); RefreshPr(); }
        void PrClosed() { _prOpen = false; if (_modal != null) CloseModal(); }
        void RefreshPr() { if (_prRoot == null || !_prOpen) return; Refill("PrHero", BuildPrHero); Refill("PrStats", BuildPrStats); Refill("PrRanks", BuildPrRanks); Refill("PrAch", BuildPrAch); Refill("PrPrefs", BuildPrPrefs); }

        async Task PrLoad(bool quiet)
        {
            if (!quiet) { _prRankState = LoadState.Loading; _prAchState = LoadState.Loading; RefreshPr(); }
            var me = _svc.Player.GetProfile(); var ach = _svc.Progress.Achievements(); var ranks = PrMetrics.Select(m => _svc.Boards.Get(LeaderboardRules.MetricKey(m), "all", 1, 0)).ToArray();
            var mr = await me; var ar = await ach; var rr = new List<Result<LeaderboardPage>>(); foreach (var t in ranks) rr.Add(await t); if (this == null) return;
            if (mr.Ok) { _svc.Me = mr.Value; if (_svc.Profile != null) _svc.Profile.display_name = mr.Value.display_name; }
            if (ar.Ok) { _prAch = ar.Value.items ?? new AchievementItem[0]; _prAchState = _prAch.Length == 0 ? LoadState.Empty : LoadState.Ready; _prAchErr = null; } else if (_prAchState != LoadState.Ready) { _prAchState = LoadState.Unavailable; _prAchErr = ar.UserMessage; }
            if (rr.All(x => x.Ok)) { for (int i = 0; i < 4; i++) { var me2 = rr[i].Value.me; _prRank[i] = me2 != null ? me2.rank : 0; _prValue[i] = me2 != null ? me2.value : 0; _prTotal[i] = rr[i].Value.total; } _prRankState = LoadState.Ready; _prRankErr = null; }
            else if (_prRankState != LoadState.Ready) { _prRankState = LoadState.Unavailable; _prRankErr = rr.First(x => !x.Ok).UserMessage; }
            Debug.Log($"[Profile] loaded profile={(mr.Ok ? "ok" : "unavailable")} ranks={_prRankState.ToString().ToLowerInvariant()} xp_rank={_prRank[0]} achievements={_prAchState.ToString().ToLowerInvariant()} unlocked={_prAch.Count(a => a.Unlocked)}/{_prAch.Length}");
            RefreshPr();
        }

        // ================================================================================================================ build
        void BuildProfilePage()
        {
            Region("PrHero", 0.02f); BuildPrHero();
            Region("PrStats", 0.06f); BuildPrStats();
            Region("PrRanks", 0.10f); BuildPrRanks();
            Region("PrAch", 0.12f); BuildPrAch();
            Region("PrPrefs", 0.14f); BuildPrPrefs();
        }

        void PrCard(float x, float y, float w, float h) => Ui.Outlined(_stage, "PrCard", PanelFill, PanelLine, x, y, w, h, 20, 1.4f);

        // ---------------------------------------------------------------- hero: who you are, your level, edit
        void BuildPrHero()
        {
            var me = Me; if (me == null) return;
            PrCard(PrX, 140, PrW, 200);
            var card = new PersonCard { Id = me.id, Name = me.display_name }; FriAvatar(_stage, card, PrX + 32, 170, 140, false);
            var nm = Ui.Label(_stage, me.display_name, "ExtraBold", 44, White, PrX + 200, 160, 640, 56); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 24; nm.resizeTextMaxSize = 44;
            Ui.Label(_stage, "@" + me.username, "SemiBold", 21, Soft, PrX + 202, 218, 400, 28);
            float cx = PrX + 202, cy = 262;
            cx += PrChip(cx, cy, "icon_globe", string.IsNullOrEmpty(me.country_name) ? "Country not set" : me.country_name, !string.IsNullOrEmpty(me.country_name)) + 12;
            cx += PrChip(cx, cy, "ic_chat", string.IsNullOrEmpty(me.language_name) ? "Language not set" : me.language_name, !string.IsNullOrEmpty(me.language_name)) + 12;
            string since = ProfileRules.MemberSince(me.created_at); if (since.Length > 0) PrChip(cx, cy, "ic_clock", since, true);
            // level and XP window (the server's)
            float lx = PrX + 900; int lvl = Math.Max(1, me.level); float frac = ProfileRules.LevelProgress(me.experience, me.xp_level_start, me.xp_next_level);
            Ui.Label(_stage, "Level " + lvl, "ExtraBold", 36, Gold, lx, 158, 300, 46); Ui.Icon(_stage, "ic_crown", Gold, lx + 196, 164, 30);
            var fill = Ui.Bar(_stage, lx, 214, 330, 14, frac, Gold); BarGrow.Run(fill, 14, Mathf.Max(14f, 330f * frac), 0.35f, 0.6f);
            Ui.Label(_stage, ProfileRules.Count(me.experience) + " XP", "Bold", 19, White, lx, 238, 200, 26);
            Ui.Label(_stage, ProfileRules.Count(ProfileRules.XpToNext(me.experience, me.xp_next_level)) + " XP to Level " + (lvl + 1), "Regular", 17, Muted, lx, 266, 330, 24);
            FriButton(_stage, "Btn_PrEdit", PrX + PrW - 290, 162, 260, 54, "Edit profile", Gold, Ink, OpenEditProfile, true, null, 20);
            FriButton(_stage, "Btn_PrPublic", PrX + PrW - 290, 228, 260, 54, "How others see me", Color.clear, White, () => FriOpenProfile(new PersonCard { Id = me.id, Name = me.display_name, Username = me.username, Level = me.level, Country = me.country_code, Status = "self" }), true, null, 18);
        }

        float PrChip(float x, float y, string icon, string text, bool set)
        {
            var probe = Ui.Label(_stage, text, "SemiBold", 17, White, 0, 0, 10, 10); float tw = probe.preferredWidth; Destroy(probe.gameObject); float w = tw + 56;
            Ui.Outlined(_stage, "PrChip", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.14f), x, y, w, 40, 12, 1.2f);
            Ui.Icon(_stage, icon, set ? Soft : Muted, x + 14, y + 10, 20); Ui.Label(_stage, text, "SemiBold", 17, set ? White : Muted, x + 42, y + 8, tw + 8, 26);
            return w;
        }

        // ---------------------------------------------------------------- the five numbers
        void BuildPrStats()
        {
            var me = Me; if (me == null) return;
            const float w = 294, gap = 20, h = 120; float y = 360, x = PrX;
            var tiles = new List<(string icon, string value, string label, string sub)>
            {
                ("ic_jobs", ProfileRules.Count(me.jobs_completed), "Jobs completed", ProfileRules.JobsRate(me.jobs_completed, me.jobs_total)),
                ("ic_road", ProfileRules.Distance(me.distance_km), "Distance driven", ""),
                ("ic_coin", ProfileRules.Money(me.lifetime_earnings), "Lifetime earnings", "Balance " + ProfileRules.Money(me.coins)),
                ("ic_car", ProfileRules.Count(me.vehicle_count), me.vehicle_count == 1 ? "Vehicle owned" : "Vehicles owned", ""),
                ("ic_people", ProfileRules.Count(me.friend_count), me.friend_count == 1 ? "Friend" : "Friends", ""),
            };
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i]; PrCard(x, y, w, h);
                Ui.Disc(_stage, "TileIcon", new Color(Gold.r, Gold.g, Gold.b, 0.16f), x + 20, y + 20, 56); Ui.Icon(_stage, t.icon, Gold, x + 33, y + 33, 30);
                var v = Ui.Label(_stage, t.value, "ExtraBold", 31, White, x + 92, y + 16, w - 104, 40); v.resizeTextForBestFit = true; v.resizeTextMinSize = 16; v.resizeTextMaxSize = 31;
                Ui.Label(_stage, t.label, "SemiBold", 17, Soft, x + 92, y + 58, w - 104, 24);
                if (t.sub.Length > 0) Ui.Label(_stage, t.sub, "Regular", 15, Muted, x + 92, y + 84, w - 104, 22);
                x += w + gap;
            }
        }

        // ---------------------------------------------------------------- ranks (the server's boards)
        void BuildPrRanks()
        {
            const float x = PrX, y = 500, w = 470, h = 550; PrCard(x, y, w, h);
            Ui.Label(_stage, "Leaderboard rankings", "Bold", 24, White, x + 26, y + 20, w - 52, 32); Ui.Label(_stage, "All time, among every ranked player", "Regular", 15, Muted, x + 26, y + 52, w - 52, 22);
            if (_prRankState == LoadState.Loading || _prRankState == LoadState.Idle) { Ui.Label(_stage, "Loading your rankings...", "SemiBold", 18, Muted, x, y + 220, w, 26, TextAnchor.UpperCenter); return; }
            if (_prRankState == LoadState.Unavailable) { Ui.Label(_stage, "Unable to load your rankings.", "Bold", 19, Brand.Bad, x, y + 190, w, 28, TextAnchor.UpperCenter); Ui.Paragraph(_stage, _prRankErr ?? "Please try again.", "Regular", 16, Soft, x + 40, y + 226, w - 80, 48, TextAnchor.UpperCenter); FriButton(_stage, "Btn_PrRetryRanks", x + w / 2 - 80, y + 290, 160, 46, "Try again", Gold, Ink, () => _ = PrLoad(false), true, null, 17); return; }
            string[] icons = { "ic_crown", "ic_coin", "ic_road", "ic_jobs" };
            for (int i = 0; i < 4; i++)
            {
                var m = PrMetrics[i]; float ry = y + 92 + i * 86; int rank = _prRank[i]; Ui.Outlined(_stage, "RankRow", new Color(0.075f, 0.085f, 0.11f, 1f), new Color(1, 1, 1, 0.10f), x + 20, ry, w - 40, 76, 14, 1.2f);
                Ui.Icon(_stage, icons[i], Gold, x + 38, ry + 22, 32);
                Ui.Label(_stage, LeaderboardRules.MetricLabel(m), "Bold", 19, White, x + 86, ry + 10, 200, 26);
                Ui.Label(_stage, rank > 0 ? LeaderboardRules.FormatValue(m, _prValue[i]) : LeaderboardRules.UnrankedText(m, BoardPeriod.AllTime), "Regular", rank > 0 ? 16 : 14, Muted, x + 86, ry + 40, rank > 0 ? 220 : 270, rank > 0 ? 24 : 32);
                Ui.Label(_stage, ProfileRules.RankText(rank), "ExtraBold", rank > 0 ? 30 : 20, rank > 0 ? (rank <= 3 ? Gold : White) : Muted, x + w - 20 - 160, ry + 18, 140, 40, TextAnchor.UpperRight);
                if (rank > 0 && _prTotal[i] > 0) Ui.Label(_stage, "of " + ProfileRules.Count(_prTotal[i]), "Regular", 13, Muted, x + w - 20 - 160, ry + 52, 140, 18, TextAnchor.UpperRight);
                var mm = m; Ui.Click(_stage, "Btn_PrRank_" + LeaderboardRules.MetricKey(m), x + 20, ry, w - 40, 76, () => { _lbMetric = mm; _lbPage = 1; OpenLeaderboards(); });
            }
            FriButton(_stage, "Btn_PrOpenBoards", x + 20, y + h - 70, w - 40, 50, "Open Leaderboards", Color.clear, White, OpenLeaderboards, true, "ic_trophy", 18);
        }

        // ---------------------------------------------------------------- achievements (the server's)
        void BuildPrAch()
        {
            const float x = PrX + 490, y = 500, w = 590, h = 550; PrCard(x, y, w, h); int done = _prAch.Count(a => a.Unlocked);
            Ui.Label(_stage, "Achievements", "Bold", 24, White, x + 26, y + 20, 300, 32);
            if (_prAchState == LoadState.Ready) Ui.Label(_stage, done + " of " + _prAch.Length + " unlocked", "SemiBold", 17, Gold, x + w - 26 - 220, y + 26, 220, 24, TextAnchor.UpperRight);
            if (_prAchState == LoadState.Loading || _prAchState == LoadState.Idle) { Ui.Label(_stage, "Loading achievements...", "SemiBold", 18, Muted, x, y + 220, w, 26, TextAnchor.UpperCenter); return; }
            if (_prAchState == LoadState.Unavailable) { Ui.Label(_stage, "Unable to load achievements.", "Bold", 19, Brand.Bad, x, y + 190, w, 28, TextAnchor.UpperCenter); Ui.Paragraph(_stage, _prAchErr ?? "Please try again.", "Regular", 16, Soft, x + 60, y + 226, w - 120, 48, TextAnchor.UpperCenter); FriButton(_stage, "Btn_PrRetryAch", x + w / 2 - 80, y + 290, 160, 46, "Try again", Gold, Ink, () => _ = PrLoad(false), true, null, 17); return; }
            if (_prAchState == LoadState.Empty) { Ui.Label(_stage, "No achievements yet.", "Bold", 20, White, x, y + 220, w, 28, TextAnchor.UpperCenter); return; }
            var items = _prAch.OrderByDescending(a => a.Unlocked).ThenByDescending(a => a.threshold > 0 ? a.current / a.threshold : 0).ToList(); const float pitch = 86;
            var area = MakeScroll("pr_ach", x + 12, y + 68, w - 24, h - 82, items.Count * pitch + 4); var host = area.Content;
            for (int i = 0; i < items.Count; i++)
            {
                var a = items[i]; float ry = i * pitch + 2; bool on = a.Unlocked;
                Ui.Outlined(host, on ? "AchOn" : "Ach", on ? new Color(Gold.r, Gold.g, Gold.b, 0.10f) : new Color(0.075f, 0.085f, 0.11f, 1f), on ? new Color(Gold.r, Gold.g, Gold.b, 0.5f) : new Color(1, 1, 1, 0.10f), 2, ry, w - 44, pitch - 8, 14, 1.2f);
                Ui.Disc(host, "AchIcon", on ? Gold : new Color(1, 1, 1, 0.08f), 18, ry + 14, 50); Ui.Icon(host, on ? "ic_check_circle" : "ic_lock", on ? Ink : Muted, 28, ry + 24, 30);
                Ui.Label(host, a.title, "Bold", 19, on ? Gold : White, 84, ry + 8, w - 190, 26); Ui.Label(host, a.description, "Regular", 15, Soft, 84, ry + 34, w - 190, 22);
                if (on) { string d = TimeText.TryParse(a.unlocked_at, out var t) ? t.UtcDateTime.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) : ""; Ui.Label(host, d, "SemiBold", 14, Muted, 84, ry + 56, 200, 18); }
                else if (a.threshold > 0) { float f = Mathf.Clamp01((float)(a.current / a.threshold)); Ui.Bar(host, 84, ry + 60, w - 250, 8, f, Gold); Ui.Label(host, ProfileRules.Count((long)Math.Min(a.current, a.threshold)) + " / " + ProfileRules.Count((long)a.threshold), "SemiBold", 14, Muted, w - 150, ry + 52, 100, 20, TextAnchor.UpperRight); }
            }
            RestoreScroll(area);
        }

        // ---------------------------------------------------------------- preferences (country, language, play style) and what you are doing now
        void BuildPrPrefs()
        {
            var me = Me; if (me == null) return; const float x = PrX + 1100, y = 500, w = 450, h = 550; PrCard(x, y, w, h);
            Ui.Label(_stage, "Preferences", "Bold", 24, White, x + 26, y + 20, w - 52, 32);
            PrPrefRow(x + 20, y + 68, w - 40, "Country", string.IsNullOrEmpty(me.country_name) ? "Not set" : me.country_name, !string.IsNullOrEmpty(me.country_name), "Btn_PrCountry", OpenCountryPicker, "icon_globe");
            PrPrefRow(x + 20, y + 158, w - 40, "Language", string.IsNullOrEmpty(me.language_name) ? "Not set" : me.language_name, !string.IsNullOrEmpty(me.language_name), "Btn_PrLanguage", OpenLanguagePicker, "ic_chat");
            Ui.Label(_stage, "How I like to drive", "SemiBold", 17, Soft, x + 26, y + 258, w - 52, 24);
            var cur = OnboardingRules.StyleFromKey(me.play_style); float cx = x + 20, cy = y + 290; int k = 0;
            foreach (var s in OnboardingRules.AllStyles)
            {
                bool on = cur == s; var ss = s; float cw = (w - 40 - 10) / 2f, chy = cy + (k / 2) * 58, chx = cx + (k % 2) * (cw + 10);
                if (on) Ui.Rounded(_stage, "StyleOn", Gold, chx, chy, cw, 48, 12); else Ui.Outlined(_stage, "Style", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.18f), chx, chy, cw, 48, 12, 1.2f);
                Ui.Icon(_stage, OnboardingRules.StyleIcon(s), on ? Ink : Soft, chx + 12, chy + 13, 22); var l = Ui.Label(_stage, OnboardingRules.StyleTitle(s), on ? "Bold" : "SemiBold", 15, on ? Ink : White, chx + 40, chy + 12, cw - 46, 24); l.resizeTextForBestFit = true; l.resizeTextMinSize = 11; l.resizeTextMaxSize = 15;
                Ui.Click(_stage, "Btn_PrStyle_" + OnboardingRules.StyleKey(s), chx, chy, cw, 48, () => _ = PrSetStyle(ss)); k++;
            }
            if (cur.HasValue) Ui.Paragraph(_stage, OnboardingRules.StyleText(cur.Value), "Regular", 15, Muted, x + 26, cy + 122, w - 52, 44);
            // an active job or bus route can be abandoned from here (as the old profile panel allowed)
            bool job = _drive != null && _drive.Job != null, bus = _bus != null && _bus.Active;
            if (job || bus)
            {
                float ay = y + h - 96; Ui.Outlined(_stage, "ActCard", new Color(0.075f, 0.085f, 0.11f, 1f), new Color(Brand.Bad.r, Brand.Bad.g, Brand.Bad.b, 0.5f), x + 20, ay, w - 40, 76, 14, 1.3f);
                Ui.Label(_stage, job ? "A job is in progress" : "A bus route is in progress", "Bold", 17, White, x + 38, ay + 10, w - 200, 24); Ui.Label(_stage, "Abandoning pays nothing.", "Regular", 14, Muted, x + 38, ay + 38, w - 200, 20);
                FriButton(_stage, "Btn_PrAbandon", x + w - 20 - 150, ay + 16, 130, 44, "Abandon", Color.clear, Brand.Bad, () => OpenAbandonConfirm(job), true, null, 16);
            }
        }

        void PrPrefRow(float x, float y, float w, string label, string value, bool set, string btn, Action change, string icon)
        {
            Ui.Outlined(_stage, "PrefRow", new Color(0.075f, 0.085f, 0.11f, 1f), new Color(1, 1, 1, 0.10f), x, y, w, 80, 14, 1.2f);
            Ui.Icon(_stage, icon, Soft, x + 16, y + 26, 28); Ui.Label(_stage, label, "Regular", 14, Muted, x + 58, y + 12, 200, 20);
            var v = Ui.Label(_stage, value, "Bold", 20, set ? White : Muted, x + 58, y + 34, w - 190, 28); v.resizeTextForBestFit = true; v.resizeTextMinSize = 13; v.resizeTextMaxSize = 20;
            FriButton(_stage, btn, x + w - 16 - 100, y + 20, 100, 40, "Change", Color.clear, White, change, true, null, 15);
        }

        // ================================================================================================================ actions
        async Task PrSetStyle(PlayStyle s)
        {
            if (_prBusy || Me == null || Me.play_style == OnboardingRules.StyleKey(s)) return; _prBusy = true;
            var r = await _svc.Player.SetPreferences(null, OnboardingRules.StyleKey(s)); if (this == null) return; _prBusy = false;
            Debug.Log($"[Profile] play style={OnboardingRules.StyleKey(s)} ok={r.Ok}" + (r.Ok ? "" : " error=" + r.ErrorCode));
            if (r.Ok) { _svc.Me = r.Value; Toast("Saved. You like to " + OnboardingRules.StyleTitle(s).ToLowerInvariant() + ".", 3f); } else Toast(OnboardingRules.ErrorText(r.ErrorCode, r.UserMessage), 4f);
            RefreshPr();
        }

        // ---- name and username
        void OpenEditProfile()
        {
            var me = Me; if (me == null) return; var m = OpenModal("Edit profile", 760, 560); const float x0 = 580, y0 = 260;
            Ui.Label(m, "Driver name", "Bold", 19, White, x0 + 32, y0 + 84, 300, 26);
            _prNameField = ObInput(m, "Pr_Name_Input", x0 + 32, y0 + 114, 696, 60, "Your driver name", me.display_name, 24, 24, v => { });
            Ui.Label(m, "Username", "Bold", 19, White, x0 + 32, y0 + 200, 300, 26);
            _prUserField = ObInput(m, "Pr_Username_Input", x0 + 32, y0 + 230, 696, 60, "username", me.username, 24, 24, v => { });
            Ui.Paragraph(m, "Friends find you by your username (lower-case letters, numbers and _). Changing it does not change your friends or your stats.", "Regular", 16, Muted, x0 + 32, y0 + 300, 696, 48);
            _prEditErr = Ui.Label(m, "", "SemiBold", 17, Brand.Bad, x0 + 32, y0 + 360, 696, 26);
            ModalButton(m, "Btn_PrEditCancel", x0 + 32, y0 + 440, 200, "Cancel", new Color(1, 1, 1, 0.12f), White, CloseModal);
            ModalButton(m, "Btn_PrEditSave", x0 + 728 - 280, y0 + 440, 280, _prBusy ? "Saving..." : "Save", Gold, Ink, () => _ = PrSaveProfile());
        }

        async Task PrSaveProfile()
        {
            if (_prBusy || Me == null) return; string name = OnboardingRules.CleanName(_prNameField != null ? _prNameField.text : Me.display_name), user = OnboardingRules.CleanUsername(_prUserField != null ? _prUserField.text : Me.username);
            string problem = OnboardingRules.NameProblem(name) ?? OnboardingRules.UsernameProblem(user);
            if (problem != null) { PrEditFail(problem); return; }
            string newName = name != Me.display_name ? name : null, newUser = user != Me.username ? user : null;
            if (newName == null && newUser == null) { CloseModal(); return; }
            _prBusy = true; var r = await _svc.Player.UpdateProfile(newName, newUser); if (this == null) return; _prBusy = false;
            Debug.Log($"[Profile] edit name={(newName != null)} username={(newUser != null)} ok={r.Ok}" + (r.Ok ? "" : " error=" + r.ErrorCode));
            if (!r.Ok) { PrEditFail(OnboardingRules.ErrorText(r.ErrorCode, r.UserMessage)); return; }
            _svc.Me = r.Value; if (_svc.Profile != null) _svc.Profile.display_name = r.Value.display_name; CloseModal(); Refill("Header", BuildHeader); RefreshPr(); Toast("Profile saved.", 3f);
        }

        void PrEditFail(string text) { if (_prEditErr != null) _prEditErr.text = text; Debug.Log("[Profile] edit refused: " + text); }

        // ---- country (a long list: search + scroll) and language (twelve rows)
        void OpenCountryPicker()
        {
            _prCountryQuery = ""; _prCountryPending = ""; _scrollPos.Remove("pr_countries"); BuildCountryPicker();
            if (_svc.Player.Countries.State != LoadState.Ready) _ = PrLoadCountries();
        }
        async Task PrLoadCountries() { var r = await _svc.Player.LoadCountries(); if (this == null) return; Debug.Log("[Profile] countries ok=" + r.Ok); if (_modal != null) BuildCountryPicker(); }

        void BuildCountryPicker()
        {
            var m = OpenModal("Choose your country", 760, 780); const float x0 = 580, y0 = 150; var pl = _svc.Player;
            if (pl.Countries.State == LoadState.Loading || pl.Countries.State == LoadState.Idle) { Ui.Label(m, "Loading countries...", "SemiBold", 20, Muted, x0, y0 + 340, 760, 28, TextAnchor.UpperCenter); return; }
            if (pl.Countries.State != LoadState.Ready) { Ui.Label(m, "Unable to load countries.", "Bold", 22, Brand.Bad, x0, y0 + 300, 760, 30, TextAnchor.UpperCenter); ModalButton(m, "Btn_PrRetryCountries", x0 + 280, y0 + 360, 200, "Try again", Gold, Ink, () => _ = PrLoadCountries()); return; }
            _prCountrySearch = ObInput(m, "Pr_Country_Search", x0 + 28, y0 + 84, 704, 54, "Search countries...", _prCountryQuery, 30, 20, v => { _prCountryPending = v; _prCountryDue = Time.unscaledTime + 0.2f; });
            _prCountryHost = Ui.Box(m, "CountryRows", 0, 0, 1920, 1080); BuildCountryRows();
        }

        void BuildCountryRows()
        {
            var m = _prCountryHost; if (m == null) return; const float x0 = 580, y0 = 150; var pl = _svc.Player;
            for (int i = m.childCount - 1; i >= 0; i--) Destroy(m.GetChild(i).gameObject);
            _areas.RemoveAll(a => a.Name == "pr_countries" || a.View == null);
            var all = pl.Countries.Value.Select(c => new CountryOption { Code = c.code, Name = c.name }).ToList(); var list = OnboardingRules.FilterCountries(all, _prCountryQuery); const float pitch = 46;
            if (list.Count == 0) { Ui.Label(m, "No country matches \"" + _prCountryQuery + "\".", "SemiBold", 18, Muted, x0, y0 + 180, 760, 28, TextAnchor.UpperCenter); return; }
            var prev = _stage; _stage = m; var area = MakeScroll("pr_countries", x0 + 28, y0 + 150, 704, 600, list.Count * pitch + 4); _stage = prev; var host = area.Content;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i]; bool on = Me != null && c.Code == Me.country_code; float y = i * pitch + 2;
                if (on) Ui.Outlined(host, "CRowOn", new Color(Gold.r, Gold.g, Gold.b, 0.16f), Gold, 2, y, 690, pitch - 4, 11, 1.6f); else Ui.Rounded(host, "CRow", new Color(1, 1, 1, i % 2 == 0 ? 0.04f : 0.02f), 2, y, 690, pitch - 4, 11);
                Ui.Label(host, c.Name, on ? "Bold" : "Regular", 19, on ? Gold : White, 20, y + 8, 540, 28); Ui.Label(host, c.Code, "SemiBold", 14, Muted, 600, y + 12, 40, 22, TextAnchor.UpperRight); if (on) Ui.Icon(host, "icon_check", Gold, 650, y + 10, 22);
                var cc = c; Ui.Click(host, "Btn_PrCountry_" + c.Code, 2, y, 690, pitch - 4, () => _ = PrSetCountry(cc));
            }
            RestoreScroll(area);
        }


        async Task PrSetCountry(CountryOption c)
        {
            if (_prBusy) return; _prBusy = true; var r = await _svc.Player.UpdateProfile(null, null, null, c.Code); if (this == null) return; _prBusy = false;
            Debug.Log($"[Profile] country={c.Code} ok={r.Ok}" + (r.Ok ? "" : " error=" + r.ErrorCode));
            if (r.Ok) { _svc.Me = r.Value; CloseModal(); RefreshPr(); Toast("Country set to " + c.Name + ".", 3f); } else Toast(OnboardingRules.ErrorText(r.ErrorCode, r.UserMessage), 4f);
        }

        void OpenLanguagePicker()
        {
            if (_svc.Player.Languages.State != LoadState.Ready) { _ = PrLoadLanguages(); }
            BuildLanguagePicker();
        }
        async Task PrLoadLanguages() { var r = await _svc.Player.LoadLanguages(); if (this == null) return; Debug.Log("[Profile] languages ok=" + r.Ok); if (_modal != null) BuildLanguagePicker(); }

        void BuildLanguagePicker()
        {
            var m = OpenModal("Choose your language", 760, 780); const float x0 = 580, y0 = 150; var pl = _svc.Player;
            if (pl.Languages.State == LoadState.Loading || pl.Languages.State == LoadState.Idle) { Ui.Label(m, "Loading languages...", "SemiBold", 20, Muted, x0, y0 + 340, 760, 28, TextAnchor.UpperCenter); return; }
            if (pl.Languages.State != LoadState.Ready) { Ui.Label(m, "Unable to load languages.", "Bold", 22, Brand.Bad, x0, y0 + 300, 760, 30, TextAnchor.UpperCenter); ModalButton(m, "Btn_PrRetryLanguages", x0 + 280, y0 + 360, 200, "Try again", Gold, Ink, () => _ = PrLoadLanguages()); return; }
            var langs = ObLanguages(); const float pitch = 52; string cur = Me != null ? Me.language_code : null;
            for (int i = 0; i < langs.Count; i++)
            {
                var l = langs[i]; bool on = l.Code == cur; float y = y0 + 84 + i * pitch;
                if (on) Ui.Outlined(m, "LRowOn", new Color(Gold.r, Gold.g, Gold.b, 0.16f), Gold, x0 + 28, y, 704, pitch - 4, 11, 1.6f); else Ui.Rounded(m, "LRow", new Color(1, 1, 1, i % 2 == 0 ? 0.04f : 0.02f), x0 + 28, y, 704, pitch - 4, 11);
                Ui.Label(m, OnboardingRules.LanguageLabel(l), on ? "Bold" : "Regular", 19, on ? Gold : White, x0 + 48, y + 9, 420, 28); Ui.Label(m, OnboardingRules.LanguageTag(l), "SemiBold", 14, l.UiReady ? Brand.Good : Muted, x0 + 440, y + 14, 240, 22, TextAnchor.UpperRight);
                if (on) Ui.Icon(m, "icon_check", Gold, x0 + 696, y + 12, 22);
                var ll = l; Ui.Click(m, "Btn_PrLang_" + l.Code, x0 + 28, y, 704, pitch - 4, () => _ = PrSetLanguage(ll));
            }
            Ui.Icon(m, "ic_info", Muted, x0 + 30, y0 + 84 + langs.Count * pitch + 12, 22); Ui.Label(m, "The game is in English for now. Other languages are saved for when they are translated.", "Regular", 15, Muted, x0 + 60, y0 + 84 + langs.Count * pitch + 12, 670, 24);
        }

        async Task PrSetLanguage(LanguageOption l)
        {
            if (_prBusy) return; _prBusy = true; var r = await _svc.Player.SetPreferences(l.Code, null); if (this == null) return; _prBusy = false;
            Debug.Log($"[Profile] language={l.Code} ok={r.Ok}" + (r.Ok ? "" : " error=" + r.ErrorCode));
            if (r.Ok) { _svc.Me = r.Value; CloseModal(); RefreshPr(); Toast(OnboardingRules.LanguageNote(l), 4f); } else Toast(OnboardingRules.ErrorText(r.ErrorCode, r.UserMessage), 4f);
        }

        // ---- abandon (with a confirmation: it pays nothing)
        void OpenAbandonConfirm(bool job)
        {
            var m = OpenModal(job ? "Abandon this job?" : "Abandon this bus route?", 640, 300); const float x0 = 640, y0 = 390;
            Ui.Paragraph(m, "You will not be paid for the work done so far.", "Regular", 19, Soft, x0 + 32, y0 + 84, 576, 60);
            ModalButton(m, "Btn_PrAbandonNo", x0 + 32, y0 + 200, 250, "Keep going", new Color(1, 1, 1, 0.12f), White, CloseModal);
            ModalButton(m, "Btn_PrAbandonYes", x0 + 640 - 32 - 250, y0 + 200, 250, "Abandon", new Color(0.62f, 0.14f, 0.14f), White, () => { CloseModal(); _ = PrAbandon(job); });
        }
        async Task PrAbandon(bool job)
        {
            Debug.Log("[Profile] abandon " + (job ? "job" : "bus route"));
            if (job) _drive.AbandonJob(); else await _bus.Abandon();
            if (this == null) return; RefreshPr(); _ = RefreshSnapshot();
        }

        // ================================================================================================================ update
        void PrUpdate(float now)
        {
            if (_prCountryDue > 0f && now > _prCountryDue) { _prCountryDue = -1f; string q = (_prCountryPending ?? "").Trim(); if (q != _prCountryQuery && _modal != null) { _prCountryQuery = q; _scrollPos.Remove("pr_countries"); Debug.Log($"[Profile] country search=\"{q}\""); BuildCountryRows(); } }
            if (now > _nextPr) { _nextPr = now + 30f; if (_modal == null && !_prBusy) _ = PrLoad(true); }
            float dy = 0f; Vector2 mp = Vector2.zero;
            try { dy = UnityEngine.Input.mouseScrollDelta.y; mp = UnityEngine.Input.mousePosition; } catch (InvalidOperationException) { }
            if (dy != 0f) { _areas.RemoveAll(a => a.View == null); foreach (var a in _areas) if (a.View.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(a.View, mp, null)) { ApplyScroll(a, a.Pos - dy * 60f); break; } }
            try { if (UnityEngine.Input.GetKeyDown(KeyCode.Escape) && !(_prNameField != null && _prNameField.isFocused)) { if (_modal != null) CloseModal(); else ShowPage(Page.Home); } }
            catch (InvalidOperationException) { }
        }
    }
}
