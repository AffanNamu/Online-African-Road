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
    /// First-run onboarding: a full-screen page above the dashboard that a player sees once, when the server says the account has not been onboarded
    /// (profile.onboarded = false). Three steps: driver name, country + language, how you want to drive (plus speed unit and nearby visibility).
    /// The country and language lists are the server's reference data; the browser's locale only suggests a starting point. Nothing is stored until the last
    /// step, then one server call (complete_onboarding) validates and stores it. Players who played before onboarding existed are never asked.
    /// </summary>
    public sealed partial class DashboardScreen
    {
        const float ObX = 260, ObY = 90, ObW = 1400, ObH = 900, ObCY = ObY + 250, ObCB = ObY + 770;

        OnboardingStep _obStep = OnboardingStep.Name; string _obName, _obCountry, _obLang, _obCountryQuery = "", _obCountryPending = "", _obError, _obLocale = "";
        PlayStyle? _obStyle; bool _obSpeedMph, _obNearby = true, _obBusy, _obDone, _obOpen, _obGuessed, _obGuess; float _obCountryDue = -1f;
        InputField _obNameField, _obSearchField; RectTransform _obRoot;

        List<CountryOption> ObCountries() => (_svc.Player.Countries.Value ?? new CountryRow[0]).Select(c => new CountryOption { Code = c.code, Name = c.name }).ToList();
        List<LanguageOption> ObLanguages() => OnboardingRules.OrderLanguages((_svc.Player.Languages.Value ?? new LanguageRow[0]).Select(l => new LanguageOption { Code = l.code, Name = l.name, NativeName = l.native_name, UiReady = l.ui_ready }));
        bool ObListsReady => _svc.Player.Countries.State == LoadState.Ready && _svc.Player.Languages.State == LoadState.Ready;

        /// <summary>True while the first-run page is up: nothing else may be opened until it is finished.</summary>
        bool Onboarding => _page == Page.Onboarding;

        void ObOpened()
        {
            _obOpen = true; if (string.IsNullOrEmpty(_obName)) _obName = _svc.Profile != null ? _svc.Profile.display_name : "";
            Debug.Log("[Onboarding] opened"); _ = ObLoadLists(); RefreshOb();
        }

        async Task ObLoadLists()
        {
            var c = _svc.Player.LoadCountries(); var l = _svc.Player.LoadLanguages(); RefreshOb(); var cr = await c; var lr = await l; if (this == null) return;
            Debug.Log($"[Onboarding] lists countries={(cr.Ok ? cr.Value.Length.ToString() : "unavailable")} languages={(lr.Ok ? lr.Value.Length.ToString() : "unavailable")}" + (cr.Ok && lr.Ok ? "" : " error=" + (cr.Ok ? lr.ErrorCode : cr.ErrorCode)));
            if (cr.Ok && lr.Ok && !_obGuessed)
            {
                _obGuessed = true; _obLocale = WebBridge.BrowserLocale(); OnboardingRules.Suggest(_obLocale, ObCountries(), ObLanguages(), out var cc, out var lc);
                if (string.IsNullOrEmpty(_obCountry) && cc != null) _obCountry = cc;
                if (string.IsNullOrEmpty(_obLang)) _obLang = lc ?? (ObLanguages().Any(x => x.Code == "en") ? "en" : null);
                _obGuess = cc != null || lc != null;
                Debug.Log($"[Onboarding] suggested locale=\"{_obLocale}\" country={cc ?? "none"} language={_obLang ?? "none"}");
            }
            RefreshOb();
        }

        void RefreshOb() { if (_obRoot == null || !_obOpen) return; Refill("ObMain", BuildObMain); Refill("ObList", BuildObList); Refill("ObPreview", BuildObPreview); Refill("ObFooter", BuildObFooter); }

        // ================================================================================================================ build
        void BuildOnboardingPage()
        {
            Region("ObBg", 0f); BuildObBg();
            Region("ObMain", 0.04f); BuildObMain();
            Region("ObList", 0.04f); BuildObList();
            Region("ObPreview", 0.08f); BuildObPreview();
            Region("ObFooter", 0.06f); BuildObFooter();
        }

        void BuildObBg()
        {
            Ui.Flat(_stage, "ObOpaque", new Color(0.028f, 0.033f, 0.048f, 1f), 0, 0, 1920, 1080);
            var tex = Brand.Texture("login_bg");
            if (tex != null) Ui.Photo(_stage, "ObSky", tex, Ui.Cover(tex, 0f, 0f, 1f, 1920f / 1080f), 0, 0, 1920, 1080, 0);
            Ui.Flat(_stage, "ObDim", new Color(0.02f, 0.025f, 0.04f, 0.62f), 0, 0, 1920, 1080);
            Ui.Gradient(_stage, "ObFloor", new Color(0.02f, 0.025f, 0.04f, 0f), new Color(0.02f, 0.025f, 0.04f, 0.9f), false, 0, 540, 1920, 540);
            Ui.Click(_stage, "Btn_ObBlocker", 0, 0, 1920, 1080, () => { }).GetComponent<HoverGlow>().enabled = false;           // nothing underneath can be clicked
            Ui.Outlined(_stage, "ObCard", new Color(0.062f, 0.070f, 0.092f, 0.95f), new Color(1, 1, 1, 0.12f), ObX, ObY, ObW, ObH, 26, 1.5f);
            Ui.Icon(_stage, "logo_mark", Color.white, ObX + 44, ObY + 30, 64);
            Ui.Label(_stage, "AFRICAN ROADS ONLINE", "ExtraBold", 22, Gold, ObX + 124, ObY + 36, 520, 28);
            Ui.Label(_stage, "Welcome aboard", "Regular", 16, Muted, ObX + 124, ObY + 64, 420, 22);
            // sign out stays available: this may be the wrong account
            Ui.Icon(_stage, "ic_logout", Muted, 1700, 34, 22); Ui.Label(_stage, "Sign out", "SemiBold", 17, Muted, 1730, 33, 120, 24); Ui.Click(_stage, "Btn_ObSignOut", 1690, 26, 170, 40, () => _act.SignOut());
        }

        void BuildObMain()
        {
            int n = OnboardingRules.Number(_obStep);
            Ui.Label(_stage, $"Step {n} of {OnboardingRules.Steps}", "SemiBold", 18, Muted, ObX + ObW - 60 - 220, ObY + 34, 220, 24, TextAnchor.UpperRight);
            for (int i = 0; i < OnboardingRules.Steps; i++) Ui.Rounded(_stage, i < n ? "StepOn" : "StepOff", i < n ? Gold : new Color(1, 1, 1, 0.16f), ObX + ObW - 60 - (OnboardingRules.Steps - i) * 72 + 8, ObY + 68, 64, 8, 4);
            Ui.Label(_stage, OnboardingRules.Title(_obStep), "ExtraBold", 42, White, ObX + 60, ObY + 118, ObW - 120, 54);
            Ui.Label(_stage, OnboardingRules.Subtitle(_obStep), "Regular", 20, Soft, ObX + 60, ObY + 176, ObW - 120, 28);
            Ui.Flat(_stage, "ObDiv", new Color(1, 1, 1, 0.08f), ObX + 60, ObY + 222, ObW - 120, 1.5f);
            if (_obStep == OnboardingStep.Name) BuildObName(); else if (_obStep == OnboardingStep.Where) BuildObWhere(); else BuildObStyle();
        }

        /// <summary>A text field in the page's style; returns it so the caller can read and focus it.</summary>
        InputField ObInput(Transform host, string name, float x, float y, float w, float h, string placeholder, string initial, int limit, int size, Action<string> changed)
        {
            Ui.Outlined(host, name + "Bg", new Color(0.05f, 0.055f, 0.075f, 0.95f), new Color(1, 1, 1, 0.2f), x, y, w, h, 14, 1.4f);
            var areaGo = new GameObject(name + "Area", typeof(RectTransform), typeof(RectMask2D)); var area = Ui.Place(areaGo, host, Ui.Anchor.TopLeft, x + 20, y, w - 40, h);
            var txt = Ui.Label(area, "", "SemiBold", size, White, 0, 0, w - 40, h, TextAnchor.MiddleLeft); var ph = Ui.Label(area, placeholder, "Regular", size - 2, new Color(1, 1, 1, 0.45f), 0, 0, w - 40, h, TextAnchor.MiddleLeft);
            var hit = areaGo.AddComponent<Image>(); hit.color = new Color(1, 1, 1, 0.001f); areaGo.name = name;
            var f = areaGo.AddComponent<InputField>(); f.targetGraphic = hit; f.textComponent = txt; f.placeholder = ph; f.lineType = InputField.LineType.SingleLine; f.characterLimit = limit; f.text = initial ?? ""; f.transition = Selectable.Transition.None;
            f.customCaretColor = true; f.caretColor = Gold; f.selectionColor = new Color(Gold.r, Gold.g, Gold.b, 0.35f);
            f.onValueChanged.AddListener(v => changed(v ?? "")); return f;
        }

        // ---------------------------------------------------------------- step 1: the driver name
        void BuildObName()
        {
            Ui.Label(_stage, "Driver name", "Bold", 21, White, ObX + 60, ObCY, 400, 28);
            _obNameField = ObInput(_stage, "Ob_Name_Input", ObX + 60, ObCY + 38, 700, 72, "Your driver name", _obName, 24, 28, v => { _obName = v; _obError = null; Refill("ObPreview", BuildObPreview); Refill("ObFooter", BuildObFooter); });
            Ui.Paragraph(_stage, "3 to 24 letters, numbers, spaces, - and _. Pick something other drivers will remember.", "Regular", 17, Muted, ObX + 60, ObCY + 122, 700, 48);
            string handle = _svc.Me != null && !string.IsNullOrEmpty(_svc.Me.username) ? "@" + _svc.Me.username : "";
            if (handle.Length > 0) { Ui.Icon(_stage, "ic_info", Muted, ObX + 60, ObCY + 190, 22); Ui.Paragraph(_stage, "Your handle is " + handle + ". Friends find you by it. You can change both later on your profile.", "Regular", 17, Soft, ObX + 90, ObCY + 189, 660, 52); }
        }

        void BuildObPreview()
        {
            if (_obStep != OnboardingStep.Name) return;
            float x = ObX + 840, y = ObCY, w = 500, h = 300; string name = OnboardingRules.CleanName(_obName); bool ok = OnboardingRules.NameProblem(_obName) == null;
            Ui.Label(_stage, "How other drivers see you", "SemiBold", 17, Muted, x, y, w, 22);
            Ui.Outlined(_stage, "PrevCard", new Color(0.075f, 0.085f, 0.11f, 1f), new Color(1, 1, 1, 0.12f), x, y + 34, w, 190, 20, 1.4f);
            var show = new PersonCard { Id = _svc.Profile != null ? _svc.Profile.id : "", Name = name.Length > 0 ? name : "Your name" };
            FriAvatar(_stage, show, x + 28, y + 34 + 46, 98, false);
            var nm = Ui.Label(_stage, show.Name, "ExtraBold", 30, ok ? White : Muted, x + 146, y + 34 + 52, w - 170, 40); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 16; nm.resizeTextMaxSize = 30;
            Ui.Label(_stage, "Level 1", "SemiBold", 19, Gold, x + 146, y + 34 + 98, 200, 26);
            if (!ok && name.Length > 0) Ui.Label(_stage, OnboardingRules.NameProblem(_obName), "Regular", 16, Brand.Bad, x, y + 240, w, 44);
            else Ui.Label(_stage, "Looks good.", "SemiBold", 17, Brand.Good, x, y + 240, w, 24);
        }

        // ---------------------------------------------------------------- step 2: country and language
        void BuildObWhere()
        {
            var pl = _svc.Player;
            if (pl.Countries.State == LoadState.Loading || pl.Languages.State == LoadState.Loading || (pl.Countries.State == LoadState.Idle && pl.Languages.State == LoadState.Idle)) { Ui.Label(_stage, "Loading countries and languages...", "SemiBold", 21, Muted, ObX, ObCY + 200, ObW, 30, TextAnchor.UpperCenter); return; }
            if (!ObListsReady)
            {
                Ui.Label(_stage, "Unable to load the lists.", "Bold", 24, Brand.Bad, ObX, ObCY + 150, ObW, 32, TextAnchor.UpperCenter);
                Ui.Paragraph(_stage, pl.Countries.State == LoadState.Unavailable ? pl.Countries.Error ?? "Please check your connection." : pl.Languages.Error ?? "Please check your connection.", "Regular", 18, Soft, ObX + 400, ObCY + 192, ObW - 800, 50, TextAnchor.UpperCenter);
                FriButton(_stage, "Btn_ObRetryLists", ObX + ObW / 2 - 90, ObCY + 262, 180, 50, "Try again", Gold, Ink, () => _ = ObLoadLists(), true, null, 19); return;
            }
            // countries (left)
            float lx = ObX + 60, lw = 640, rx = ObX + 740, rw = 600;
            Ui.Label(_stage, "Country", "Bold", 21, White, lx, ObCY, 200, 28);
            if (_obGuess) Ui.Label(_stage, "Suggested from your browser", "SemiBold", 15, Gold, lx + 120, ObCY + 4, 300, 22);
            _obSearchField = ObInput(_stage, "Ob_Country_Search", lx, ObCY + 34, lw, 52, "Search countries...", _obCountryQuery, 30, 20, v => { _obCountryPending = v; _obCountryDue = Time.unscaledTime + 0.2f; });
            // languages (right)
            Ui.Label(_stage, "Language", "Bold", 21, White, rx, ObCY, 200, 28);
            var langs = ObLanguages(); const float lp = 52; var larea = MakeScroll("ob_langs", rx, ObCY + 34, rw, 320, langs.Count * lp + 4); var lh = larea.Content;
            for (int i = 0; i < langs.Count; i++)
            {
                var l = langs[i]; bool on = l.Code == _obLang; float y = i * lp + 2;
                if (on) Ui.Outlined(lh, "LRowOn", new Color(Gold.r, Gold.g, Gold.b, 0.16f), Gold, 2, y, rw - 16, lp - 4, 11, 1.6f); else Ui.Rounded(lh, "LRow", new Color(1, 1, 1, i % 2 == 0 ? 0.04f : 0.02f), 2, y, rw - 16, lp - 4, 11);
                Ui.Label(lh, OnboardingRules.LanguageLabel(l), on ? "Bold" : "Regular", 19, on ? Gold : White, 20, y + 9, rw - 250, 28);
                Ui.Label(lh, OnboardingRules.LanguageTag(l), "SemiBold", 14, l.UiReady ? Brand.Good : Muted, rw - 220, y + 14, 160, 22, TextAnchor.UpperRight);
                if (on) Ui.Icon(lh, "icon_check", Gold, rw - 42, y + 12, 22);
                var ll = l; Ui.Click(lh, "Btn_ObLang_" + l.Code, 2, y, rw - 16, lp - 4, () => { _obLang = ll.Code; _obError = null; Debug.Log("[Onboarding] language=" + ll.Code); RefreshOb(); });
            }
            RestoreScroll(larea);
            var chosen = langs.FirstOrDefault(x => x.Code == _obLang);
            Ui.Icon(_stage, "ic_info", chosen != null && !chosen.UiReady ? Gold : Muted, rx, ObCY + 372, 22);
            Ui.Paragraph(_stage, chosen != null ? OnboardingRules.LanguageNote(chosen) : "Choose the language you would like the game in.", "Regular", 17, Soft, rx + 30, ObCY + 371, rw - 30, 60);
        }

        /// <summary>The country rows on their own, so typing in the search box never rebuilds (and never unfocuses) the box itself.</summary>
        void BuildObList()
        {
            if (_obStep != OnboardingStep.Where || !ObListsReady) return; float lx = ObX + 60, lw = 640;
            var list = OnboardingRules.FilterCountries(ObCountries(), _obCountryQuery); const float pitch = 46; float vh = ObCB - (ObCY + 98);
            if (list.Count == 0) { Ui.Label(_stage, "No country matches \"" + _obCountryQuery + "\".", "SemiBold", 18, Muted, lx, ObCY + 130, lw, 28, TextAnchor.UpperCenter); }
            else
            {
                if (!_scrollPos.ContainsKey("ob_countries") && !string.IsNullOrEmpty(_obCountry)) { int at = list.FindIndex(x => x.Code == _obCountry); if (at > 1) _scrollPos["ob_countries"] = (at - 1) * pitch; }      // open on the chosen (or suggested) country
                var area = MakeScroll("ob_countries", lx, ObCY + 98, lw, vh, list.Count * pitch + 4); var host = area.Content;
                for (int i = 0; i < list.Count; i++)
                {
                    var c = list[i]; bool on = c.Code == _obCountry; float y = i * pitch + 2;
                    if (on) Ui.Outlined(host, "CRowOn", new Color(Gold.r, Gold.g, Gold.b, 0.16f), Gold, 2, y, lw - 16, pitch - 4, 11, 1.6f); else Ui.Rounded(host, "CRow", new Color(1, 1, 1, i % 2 == 0 ? 0.04f : 0.02f), 2, y, lw - 16, pitch - 4, 11);
                    Ui.Label(host, c.Name, on ? "Bold" : "Regular", 19, on ? Gold : White, 20, y + 8, lw - 100, 28); Ui.Label(host, c.Code, "SemiBold", 14, Muted, lw - 76, y + 12, 40, 22, TextAnchor.UpperRight);
                    if (on) Ui.Icon(host, "icon_check", Gold, lw - 60 - 30, y + 10, 22);
                    var cc = c; Ui.Click(host, "Btn_ObCountry_" + c.Code, 2, y, lw - 16, pitch - 4, () => { _obCountry = cc.Code; _obError = null; Debug.Log("[Onboarding] country=" + cc.Code); RefreshOb(); });
                }
                RestoreScroll(area);
            }
        }

        // ---------------------------------------------------------------- step 3: how to drive, units, visibility
        void BuildObStyle()
        {
            const float cw = 299, gap = 28, ch = 288; float x = ObX + 60, y = ObCY;
            foreach (var s in OnboardingRules.AllStyles)
            {
                bool on = _obStyle == s; var ss = s;
                if (on) Ui.Outlined(_stage, "StyleOn", new Color(Gold.r, Gold.g, Gold.b, 0.14f), Gold, x, y, cw, ch, 20, 2f); else Ui.Outlined(_stage, "Style", new Color(0.075f, 0.085f, 0.11f, 1f), new Color(1, 1, 1, 0.14f), x, y, cw, ch, 20, 1.4f);
                Ui.Disc(_stage, "StyleIconBg", on ? Gold : new Color(1, 1, 1, 0.09f), x + 24, y + 24, 68); Ui.Icon(_stage, OnboardingRules.StyleIcon(s), on ? Ink : White, x + 40, y + 40, 36);
                if (on) Ui.Icon(_stage, "ic_check_circle", Gold, x + cw - 56, y + 22, 34);
                Ui.Label(_stage, OnboardingRules.StyleTitle(s), "ExtraBold", 25, on ? Gold : White, x + 24, y + 108, cw - 48, 34);
                Ui.Paragraph(_stage, OnboardingRules.StyleText(s), "Regular", 17, Soft, x + 24, y + 150, cw - 48, 96);
                string land = OnboardingRules.LandingPage(s); string where = land == "jobs" ? "Jobs" : land == "vehicles" ? "Trucks & Buses" : land == "multiplayer" ? "Multiplayer" : "Home";
                Ui.Label(_stage, "Opens: " + where, "SemiBold", 15, Muted, x + 24, y + ch - 36, cw - 48, 22);
                Ui.Click(_stage, "Btn_ObStyle_" + OnboardingRules.StyleKey(s), x, y, cw, ch, () => { _obStyle = ss; _obError = null; Debug.Log("[Onboarding] style=" + OnboardingRules.StyleKey(ss)); RefreshOb(); });
                x += cw + gap;
            }
            float ry = y + ch + 24;
            Ui.Outlined(_stage, "UnitRow", new Color(0.075f, 0.085f, 0.11f, 1f), new Color(1, 1, 1, 0.12f), ObX + 60, ry, ObW - 120, 72, 16, 1.3f);
            Ui.Icon(_stage, "ic_gauge", Soft, ObX + 84, ry + 22, 28); Ui.Label(_stage, "Speed unit", "Bold", 20, White, ObX + 126, ry + 10, 300, 26); Ui.Label(_stage, "How your speedometer shows speed. Change it any time in Settings.", "Regular", 16, Muted, ObX + 126, ry + 38, 700, 22);
            float sx = ObX + ObW - 60 - 24 - 290;
            foreach (var u in new[] { (false, "km/h"), (true, "mph") })
            {
                bool on = _obSpeedMph == u.Item1; var uu = u;
                if (on) Ui.Rounded(_stage, "UnitOn", Gold, sx, ry + 14, 140, 44, 12); else Ui.Outlined(_stage, "Unit", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.2f), sx, ry + 14, 140, 44, 12, 1.3f);
                Ui.Label(_stage, u.Item2, on ? "Bold" : "SemiBold", 19, on ? Ink : White, sx, ry + 24, 140, 26, TextAnchor.UpperCenter);
                Ui.Click(_stage, "Btn_ObSpeed_" + (u.Item1 ? "mph" : "kmh"), sx, ry + 14, 140, 44, () => { _obSpeedMph = uu.Item1; Debug.Log("[Onboarding] speed=" + (uu.Item1 ? "mph" : "kmh")); RefreshOb(); });
                sx += 150;
            }
            float ny = ry + 84;
            Ui.Outlined(_stage, "NearRow", new Color(0.075f, 0.085f, 0.11f, 1f), new Color(1, 1, 1, 0.12f), ObX + 60, ny, ObW - 120, 72, 16, 1.3f);
            Ui.Icon(_stage, "ic_people", Soft, ObX + 84, ny + 22, 28); Ui.Label(_stage, "Appear in Players Nearby", "Bold", 20, White, ObX + 126, ny + 10, 400, 26); Ui.Label(_stage, "Other drivers can find you in the Players Nearby list and invite you to convoys. Change it any time in Settings.", "Regular", 16, Muted, ObX + 126, ny + 38, 900, 22);
            float tx = ObX + ObW - 60 - 24 - 140;
            if (_obNearby) Ui.Rounded(_stage, "NearOn", Brand.Good, tx, ny + 14, 140, 44, 12); else Ui.Outlined(_stage, "NearOff", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.2f), tx, ny + 14, 140, 44, 12, 1.3f);
            Ui.Label(_stage, _obNearby ? "Enabled" : "Disabled", "Bold", 19, _obNearby ? Ink : White, tx, ny + 24, 140, 26, TextAnchor.UpperCenter);
            Ui.Click(_stage, "Btn_ObNearby", tx, ny + 14, 140, 44, () => { _obNearby = !_obNearby; Debug.Log("[Onboarding] nearby=" + _obNearby.ToString().ToLowerInvariant()); RefreshOb(); });
        }

        // ---------------------------------------------------------------- footer: why Continue is off, Back, Continue / Start driving
        void BuildObFooter()
        {
            string why = OnboardingRules.BlockedReason(_obStep, _obName, _obCountry, _obLang, _obStyle); bool can = why == null && !_obBusy && (_obStep != OnboardingStep.Where || ObListsReady);
            if (_obError != null) Ui.Label(_stage, _obError, "SemiBold", 18, Brand.Bad, ObX + 60, ObY + 776, ObW - 120, 28);
            else if (why != null && _obStep != OnboardingStep.Name && (_obStep != OnboardingStep.Where || ObListsReady)) Ui.Label(_stage, why, "Regular", 17, Muted, ObX + 60, ObY + 776, ObW - 120, 28);
            if (_obStep != OnboardingStep.Name) FriButton(_stage, "Btn_ObBack", ObX + 60, ObY + 820, 170, 58, "Back", Color.clear, White, () => { if (_obBusy) return; _obStep = OnboardingRules.Back(_obStep); _obError = null; _scrollPos.Remove("ob_countries"); _scrollPos.Remove("ob_langs"); Debug.Log("[Onboarding] step=" + _obStep.ToString().ToLowerInvariant()); RefreshOb(); }, !_obBusy, null, 20);
            bool last = OnboardingRules.IsLast(_obStep); string label = _obBusy ? "Saving..." : last ? "Start driving" : "Continue";
            FriButton(_stage, last ? "Btn_ObFinish" : "Btn_ObContinue", ObX + ObW - 60 - 280, ObY + 820, 280, 58, label, Gold, Ink, ObContinue, can, null, 21);
        }

        void ObContinue()
        {
            if (_obBusy) return;
            if (!OnboardingRules.CanContinue(_obStep, _obName, _obCountry, _obLang, _obStyle)) { Toast(OnboardingRules.BlockedReason(_obStep, _obName, _obCountry, _obLang, _obStyle), 3f); return; }
            if (!OnboardingRules.IsLast(_obStep)) { _obStep = OnboardingRules.Next(_obStep); _obError = null; Debug.Log("[Onboarding] step=" + _obStep.ToString().ToLowerInvariant()); RefreshOb(); return; }
            _ = ObFinish();
        }

        async Task ObFinish()
        {
            _obBusy = true; _obError = null; RefreshOb();
            string name = OnboardingRules.CleanName(_obName); bool renamed = _svc.Profile == null || !string.Equals(name, _svc.Profile.display_name, StringComparison.Ordinal);
            var r = await _svc.Player.CompleteOnboarding(_obCountry, _obLang, OnboardingRules.StyleKey(_obStyle.Value), renamed ? name : null); if (this == null) return;
            _obBusy = false;
            Debug.Log($"[Onboarding] complete ok={r.Ok} country={_obCountry} language={_obLang} style={OnboardingRules.StyleKey(_obStyle.Value)} renamed={renamed}" + (r.Ok ? "" : " error=" + r.ErrorCode));
            if (!r.Ok)
            {
                _obError = OnboardingRules.ErrorText(r.ErrorCode, r.UserMessage);
                if (r.ErrorCode == "display_name_taken" || r.ErrorCode == "invalid_display_name") _obStep = OnboardingStep.Name;
                else if (r.ErrorCode == "unknown_country" || r.ErrorCode == "unknown_language") _obStep = OnboardingStep.Where;
                RefreshOb(); return;
            }
            _svc.Me = r.Value; if (_svc.Profile != null) _svc.Profile.display_name = r.Value.display_name;
            // device choice (applied at once) and the one account choice that is not the default
            var gs = SettingsStore.Current.Clone(); string unit = _obSpeedMph ? "mph" : "kmh";
            if (gs.Get("speed_unit") != unit) { gs.TrySet("speed_unit", unit); SettingsStore.Save(gs); }
            if (!_obNearby) { var v = await _svc.Convoys.SetNearbyVisible(false); if (this == null) return; Debug.Log("[Onboarding] nearby visibility off ok=" + v.Ok); if (!v.Ok) Toast("Could not hide you from Players Nearby. You can change it in Settings.", 5f); }
            _obDone = true; _obOpen = false; string land = OnboardingRules.LandingPage(_obStyle);
            Debug.Log($"[Onboarding] finished name={r.Value.display_name} onboarded={r.Value.onboarded.ToString().ToLowerInvariant()} landing={land}");
            Refill("Header", BuildHeader);
            ShowPage(land == "jobs" ? Page.Jobs : land == "vehicles" ? Page.Vehicles : land == "multiplayer" ? Page.Multiplayer : Page.Home);
            Toast("Welcome aboard, " + r.Value.display_name + "!", 4f);
        }

        // ================================================================================================================ update
        void ObUpdate(float now)
        {
            if (_obCountryDue > 0f && now > _obCountryDue) { _obCountryDue = -1f; string q = (_obCountryPending ?? "").Trim(); if (q != _obCountryQuery) { _obCountryQuery = q; _scrollPos.Remove("ob_countries"); Debug.Log($"[Onboarding] country search=\"{q}\" shown={OnboardingRules.FilterCountries(ObCountries(), q).Count}"); Refill("ObList", BuildObList); } }
            float dy = 0f; Vector2 mp = Vector2.zero;
            try { dy = UnityEngine.Input.mouseScrollDelta.y; mp = UnityEngine.Input.mousePosition; } catch (InvalidOperationException) { }
            if (dy != 0f) { _areas.RemoveAll(a => a.View == null); foreach (var a in _areas) if (a.View.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(a.View, mp, null)) { ApplyScroll(a, a.Pos - dy * 60f); break; } }
            try
            {
                bool searching = _obSearchField != null && _obSearchField.isFocused;
                if (!searching && (UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter))) ObContinue();
            }
            catch (InvalidOperationException) { }
        }
    }
}
