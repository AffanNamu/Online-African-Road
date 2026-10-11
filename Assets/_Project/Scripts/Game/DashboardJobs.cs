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
    /// The Jobs page of the dashboard (sidebar and header are shared with Home). Layout follows the approved mockup: title and search, category chips,
    /// a scrolling list of job cards on the left, and the selected job on the right (hero, route preview, cargo information, requirements,
    /// Accept Job, earnings strip).
    /// EVERY value is the server's: the board page, filter and search come from list_available_jobs; cargo details, fuel and profit estimates and the
    /// requirement checks come from the same reply; eligibility is the server's verdict. Nothing is invented, and a failed load shows Unavailable + Retry.
    /// </summary>
    public sealed partial class DashboardScreen
    {
        enum BoardState { Loading, Ready, Empty, Error }
        static readonly (string key, string label, string icon)[] Cats =
        {
            (null, "All Jobs", "ic_jobs"), ("freight", "Freight", "ic_box"), ("construction", "Construction", "ic_wrench"), ("fuel_chemicals", "Fuel & Chemicals", "ic_fuel"),
            ("agriculture", "Agriculture", "ic_leaf"), ("manufacturing", "Manufacturing", "ic_gear"), ("vehicles", "Vehicles", "ic_car"),
        };
        static readonly Color Lime = new Color(0.80f, 0.87f, 0.26f), CardFill = new Color(0.055f, 0.062f, 0.082f, 0.96f);
        const float ListX = 305, ListY = 277, ListW = 742, ListH = 803, RowH = 131, RowPitch = 142, PageSize = 30;

        BoardState _bs = BoardState.Loading; string _bsError, _cat, _search = "", _selId, _searchPending = "";
        readonly List<JobDto> _board = new List<JobDto>(); int _boardTotal, _boardToken; bool _boardLoaded, _moreLoading; float _boardAt, _scroll, _searchDue = -1f;
        RectTransform _listView, _listContent; Image _scrollThumb; readonly Dictionary<string, Image> _rowBorders = new Dictionary<string, Image>(); InputField _searchField;

        // ================================================================================================================ pages
        public void OpenJobs() => ShowPage(Page.Jobs);

        void ShowPage(Page p)
        {
            if (_page == Page.Onboarding && p != Page.Onboarding && !_obDone) return;                     // the first-run page is finished before anything else opens
            if (_page == p && _jobsRoot != null && _jobsRoot.gameObject.activeSelf == (p == Page.Jobs) && _mpRoot.gameObject.activeSelf == (p == Page.Multiplayer) && _vehRoot.gameObject.activeSelf == (p == Page.Vehicles) && _garRoot.gameObject.activeSelf == (p == Page.Garage) && _setRoot.gameObject.activeSelf == (p == Page.Settings) && _friRoot.gameObject.activeSelf == (p == Page.Friends) && _lbRoot.gameObject.activeSelf == (p == Page.Leaderboards) && _obRoot.gameObject.activeSelf == (p == Page.Onboarding) && _prRoot.gameObject.activeSelf == (p == Page.Profile)) return;
            var was = _page; _page = p; ApplyPage(); if (was == Page.Garage && p != Page.Garage) GarClosed(); if (was == Page.Settings && p != Page.Settings) SetClosed(); if (was == Page.Friends && p != Page.Friends) FriClosed(); if (was == Page.Leaderboards && p != Page.Leaderboards) LbClosed(); if (was == Page.Profile && p != Page.Profile) PrClosed(); Refill("Sidebar", BuildSidebar); MarkTargetsDirty();
            Debug.Log("[Dashboard] page=" + p.ToString().ToLowerInvariant());
            if (p == Page.Jobs && (!_boardLoaded || Time.unscaledTime - _boardAt > 60f)) _ = LoadBoard(true);
            if (p == Page.Multiplayer) MpOpened(); else if (_modal != null) CloseModal();
            if (p == Page.Vehicles) VehOpened();
            if (p == Page.Garage) GarOpened();
            if (p == Page.Settings) SetOpened();
            if (p == Page.Friends) FriOpened();
            if (p == Page.Leaderboards) LbOpened();
            if (p == Page.Profile) PrOpened();
        }

        void ApplyPage()
        {
            if (_homeRoot != null) _homeRoot.gameObject.SetActive(_page == Page.Home);
            if (_jobsRoot != null) _jobsRoot.gameObject.SetActive(_page == Page.Jobs);
            if (_mpRoot != null) _mpRoot.gameObject.SetActive(_page == Page.Multiplayer);
            if (_vehRoot != null) _vehRoot.gameObject.SetActive(_page == Page.Vehicles);
            if (_garRoot != null) _garRoot.gameObject.SetActive(_page == Page.Garage);
            if (_setRoot != null) _setRoot.gameObject.SetActive(_page == Page.Settings);
            if (_friRoot != null) _friRoot.gameObject.SetActive(_page == Page.Friends);
            if (_lbRoot != null) _lbRoot.gameObject.SetActive(_page == Page.Leaderboards);
            if (_obRoot != null) _obRoot.gameObject.SetActive(_page == Page.Onboarding);
            if (_prRoot != null) _prRoot.gameObject.SetActive(_page == Page.Profile);
        }

        /// <summary>GameFlow calls this after a refused accept (someone took the job, route closed...): the board is re-read so the page tells the truth.</summary>
        public void ReloadJobsPage() { if (_page == Page.Jobs) _ = LoadBoard(true); else _boardLoaded = false; }

        // ================================================================================================================ build
        void BuildJobsPage()
        {
            Region("JobsTop", 0.02f); BuildJobsTop();
            Region("JobsChips", 0.05f); BuildChips();
            Region("JobsList", 0.08f); BuildJobsList();
            Region("JobsDetail", 0.12f); BuildJobsDetail();
        }

        void BuildJobsTop()
        {
            Ui.Icon(_stage, "ic_jobs", Gold, 322, 138, 52);
            var t = Ui.Label(_stage, "Jobs", "ExtraBold", 52, White, 392, 128, 400, 64); Ui.Drop(t, 0.5f);
            Ui.Label(_stage, "Take real jobs, deliver cargo, earn money and grow your trucking career.", "Regular", 20, new Color(0.84f, 0.86f, 0.9f), 392, 186, 900, 28);
            // search: the server filters (cargo, place or city); typing is debounced so one request is sent per pause
            const float sx = 1566, sy = 134, sw = 324, sh = 50;
            Ui.Outlined(_stage, "SearchBox", new Color(0.05f, 0.055f, 0.075f, 0.92f), new Color(1, 1, 1, 0.16f), sx, sy, sw, sh, 14);
            DrawSearchGlyph(sx + 16, sy + 12, 26);
            var areaGo = new GameObject("SearchArea", typeof(RectTransform), typeof(RectMask2D));
            var area = Ui.Place(areaGo, _stage, Ui.Anchor.TopLeft, sx + 52, sy, sw - 66, sh);
            var txt = Ui.Label(area, "", "Regular", 19, White, 0, 0, sw - 66, sh, TextAnchor.MiddleLeft);
            var ph = Ui.Label(area, "Search jobs, cities or cargo...", "Regular", 19, new Color(1, 1, 1, 0.5f), 0, 0, sw - 66, sh, TextAnchor.MiddleLeft);
            var hit = areaGo.AddComponent<Image>(); hit.color = new Color(1, 1, 1, 0.001f); areaGo.name = "Search_Input";
            var f = areaGo.AddComponent<InputField>(); f.targetGraphic = hit;
            f.textComponent = txt; f.placeholder = ph; f.lineType = InputField.LineType.SingleLine; f.characterLimit = 40; f.text = _search;
            f.transition = Selectable.Transition.None; f.customCaretColor = true; f.caretColor = Gold; f.selectionColor = new Color(Gold.r, Gold.g, Gold.b, 0.35f);
                        f.onValueChanged.AddListener(v => { _searchPending = v ?? ""; _searchDue = Time.unscaledTime + 0.45f; });
            _searchField = f;
        }

        /// <summary>A magnifier drawn from primitives (a ring and a handle), so no extra icon file is needed.</summary>
        void DrawSearchGlyph(float x, float y, float s)
        {
            var c = new Color(0.82f, 0.84f, 0.88f);
            Ui.Disc(_stage, "SearchRing", c, x, y, s * 0.74f); Ui.Disc(_stage, "SearchHole", new Color(0.05f, 0.055f, 0.075f, 1f), x + s * 0.09f, y + s * 0.09f, s * 0.56f);
            Ui.Segment(_stage, new Vector2(x + s * 0.62f, y + s * 0.62f), new Vector2(x + s * 0.95f, y + s * 0.95f), 3.5f, c);
        }

        void BuildChips()
        {
            float x = 319;
            foreach (var cat in Cats)
            {
                bool on = cat.key == _cat;
                var probe = Ui.Label(_stage, cat.label, on ? "Bold" : "SemiBold", 19, on ? Ink : White, 0, 0, 10, 10); float tw = probe.preferredWidth; Destroy(probe.gameObject);
                float w = 20 + 26 + 10 + tw + 22, y = 210, h = 46;
                if (on) Ui.Rounded(_stage, "ChipOn", Gold, x, y, w, h, 12);
                else Ui.Outlined(_stage, "Chip", new Color(0.05f, 0.055f, 0.075f, 0.9f), new Color(1, 1, 1, 0.14f), x, y, w, h, 12);
                Ui.Icon(_stage, cat.icon, on ? Ink : new Color(0.86f, 0.88f, 0.92f), x + 18, y + 10, 26);
                Ui.Label(_stage, cat.label, on ? "Bold" : "SemiBold", 19, on ? Ink : White, x + 54, y + 10, tw + 6, 28);
                string key = cat.key; Ui.Click(_stage, "Btn_Cat_" + (key ?? "all"), x, y, w, h, () => { if (_cat == key) return; _cat = key; Refill("JobsChips", BuildChips); _ = LoadBoard(true); });
                x += w + 12;
            }
        }

        // ================================================================================================================ data
        async Task LoadBoard(bool reset)
        {
            int token = ++_boardToken;
            if (reset) { _board.Clear(); _boardTotal = 0; _bs = BoardState.Loading; _scroll = 0; _moreLoading = false; Refill("JobsList", BuildJobsList); Refill("JobsDetail", BuildJobsDetail); }
            else _moreLoading = true;
            var r = await _svc.Jobs.Page((int)PageSize, reset ? 0 : _board.Count, _cat, _search);
            if (this == null || token != _boardToken) return;                          // a newer request superseded this one
            _moreLoading = false; _boardLoaded = true; _boardAt = Time.unscaledTime;
            if (!r.Ok)
            {
                if (reset) { _bs = BoardState.Error; _bsError = r.UserMessage; } else Toast(r.UserMessage);
            }
            else
            {
                _board.AddRange(r.Value.Jobs); _boardTotal = r.Value.Total; _bs = _board.Count == 0 ? BoardState.Empty : BoardState.Ready;
                if (_board.Count > 0 && (_selId == null || _board.All(j => j.id != _selId))) _selId = _board[0].id;
                if (_board.Count == 0) _selId = null;
                SetJobsBadge(_cat == null && string.IsNullOrEmpty(_search) ? _boardTotal : _badgeCount);
            }
            var sel = SelectedJob();
            Debug.Log($"[Jobs] page state={_bs.ToString().ToLowerInvariant()} total={_boardTotal} shown={_board.Count} cat={_cat ?? "all"} search=\"{_search}\" selected={(sel != null ? sel.code : "none")}" + (_bsError != null && _bs == BoardState.Error ? " error=" + _bsError : ""));
            Refill("JobsList", BuildJobsList); Refill("JobsDetail", BuildJobsDetail);
        }

        JobDto SelectedJob() => _selId == null ? null : _board.FirstOrDefault(j => j.id == _selId);

        void SelectJob(JobDto j)
        {
            if (j == null || _selId == j.id) return;
            _selId = j.id; MarkRows(); Refill("JobsDetail", BuildJobsDetail);
            Debug.Log("[Jobs] selected=" + j.code);
        }

        void MarkRows()
        {
            foreach (var kv in _rowBorders) if (kv.Value != null) kv.Value.color = kv.Key == _selId ? Gold : new Color(1, 1, 1, 0.09f);
        }

        // ================================================================================================================ update: scroll, search debounce, escape
        void JobsUpdate(float now)
        {
            if (_searchDue > 0f && now > _searchDue)
            {
                _searchDue = -1f; string q = (_searchPending ?? "").Trim();
                if (q != _search) { _search = q; _ = LoadBoard(true); }
            }
            if (_listView != null && _listContent != null)
            {
                float dy = 0f; bool over = false;
                try { dy = UnityEngine.Input.mouseScrollDelta.y; over = RectTransformUtility.RectangleContainsScreenPoint(_listView, UnityEngine.Input.mousePosition, null); } catch (InvalidOperationException) { }
                if (dy != 0f && over) SetScroll(_scroll - dy * 70f);
                float max = MaxScroll();
                if (_bs == BoardState.Ready && !_moreLoading && _board.Count < _boardTotal && _scroll > max - 260f) _ = LoadBoard(false);
            }
            bool typing = _searchField != null && _searchField.isFocused;
            try { if (!typing && UnityEngine.Input.GetKeyDown(KeyCode.Escape)) ShowPage(Page.Home); } catch (InvalidOperationException) { }
        }

        float MaxScroll() => Mathf.Max(0f, (_listContent != null ? _listContent.sizeDelta.y : 0f) - ListH + 8f);

        void SetScroll(float v)
        {
            _scroll = Mathf.Clamp(v, 0f, MaxScroll());
            if (_listContent != null) _listContent.anchoredPosition = new Vector2(0, _scroll);
            if (_scrollThumb != null)
            {
                float max = MaxScroll(), content = _listContent.sizeDelta.y, th = Mathf.Max(50f, ListH * ListH / Mathf.Max(content, ListH)), travel = ListH - th;
                _scrollThumb.rectTransform.anchoredPosition = new Vector2(_scrollThumb.rectTransform.anchoredPosition.x, -(ListY + (max > 0 ? _scroll / max * travel : 0)));
                _scrollThumb.rectTransform.sizeDelta = new Vector2(4, th); _scrollThumb.gameObject.SetActive(max > 0);
            }
        }

        // ================================================================================================================ list
        void BuildJobsList()
        {
            _rowBorders.Clear(); _listView = null; _listContent = null; _scrollThumb = null;
            if (_bs == BoardState.Loading) { BuildListSkeleton(); return; }
            if (_bs == BoardState.Error) { ListMessage("ic_road", "We could not load the job board", _bsError ?? "Please try again.", Brand.Bad, "Try again", "Btn_RetryJobsPage", () => _ = LoadBoard(true)); return; }
            if (_bs == BoardState.Empty)
            {
                bool filtered = _cat != null || !string.IsNullOrEmpty(_search);
                ListMessage("ic_box", filtered ? "No jobs match" : "No open jobs right now", filtered ? "Try another category or a different search." : "The server posts new jobs regularly. Check back soon, or run a bus route meanwhile.", White,
                    filtered ? "Clear filters" : "Refresh", "Btn_JobsEmptyAction", () => { _cat = null; _search = ""; _searchPending = ""; if (_searchField != null) _searchField.text = ""; Refill("JobsChips", BuildChips); _ = LoadBoard(true); });
                return;
            }
            var view = Ui.Box(_stage, "ListView", ListX, ListY, ListW, ListH); view.gameObject.AddComponent<RectMask2D>(); _listView = view;
            float contentH = _board.Count * RowPitch + (_board.Count < _boardTotal ? 44f : 0f) + 8f;
            _listContent = Ui.Box(view, "ListContent", 0, 0, ListW, contentH);
            for (int i = 0; i < _board.Count; i++) JobRow(_listContent, _board[i], 6, 6 + i * RowPitch, ListW - 22);
            if (_board.Count < _boardTotal) Ui.Label(_listContent, _moreLoading ? "Loading more jobs..." : $"Showing {_board.Count} of {_boardTotal}. Scroll for more.", "Regular", 16, Muted, 0, 6 + _board.Count * RowPitch + 4, ListW - 22, 24, TextAnchor.UpperCenter);
            Ui.Rounded(_stage, "ScrollTrack", new Color(1, 1, 1, 0.07f), ListX + ListW - 8, ListY, 4, ListH, 2);
            _scrollThumb = Ui.Rounded(_stage, "ScrollThumb", new Color(1, 1, 1, 0.35f), ListX + ListW - 8, ListY, 4, 80, 2);
            SetScroll(_scroll); MarkRows();
        }

        void BuildListSkeleton()
        {
            for (int i = 0; i < 5; i++)
            {
                float y = ListY + 6 + i * RowPitch;
                Ui.Outlined(_stage, "SkRow", CardFill, new Color(1, 1, 1, 0.07f), ListX + 6, y, ListW - 22, RowH, 16);
                var blocks = new[] { Ui.Rounded(_stage, "SkPhoto", new Color(1, 1, 1, 0.14f), ListX + 8, y + 2, 224, RowH - 4, 13), Ui.Rounded(_stage, "SkA", new Color(1, 1, 1, 0.14f), ListX + 250, y + 16, 240, 22, 8),
                    Ui.Rounded(_stage, "SkB", new Color(1, 1, 1, 0.14f), ListX + 250, y + 56, 320, 16, 7), Ui.Rounded(_stage, "SkC", new Color(1, 1, 1, 0.14f), ListX + 250, y + 90, 160, 16, 7), Ui.Rounded(_stage, "SkD", new Color(1, 1, 1, 0.14f), ListX + 570, y + 90, 110, 20, 7) };
                for (int k = 0; k < blocks.Length; k++) Breath.On(blocks[k], i * 3 + k);
            }
            Ui.Label(_stage, "Loading jobs...", "Regular", 17, Muted, ListX + 8, ListY - 22, 300, 22);
        }

        void ListMessage(string icon, string title, string body, Color titleColor, string button, string buttonName, Action onClick)
        {
            Ui.Outlined(_stage, "MsgPanel", CardFill, new Color(1, 1, 1, 0.09f), ListX + 6, ListY + 6, ListW - 22, 300, 18);
            Ui.Icon(_stage, icon, new Color(1, 1, 1, 0.22f), ListX + 40, ListY + 70, 84);
            Ui.Label(_stage, title, "Bold", 28, titleColor, ListX + 150, ListY + 72, 540, 36);
            Ui.Paragraph(_stage, body, "Regular", 19, Soft, ListX + 150, ListY + 116, 540, 66);
            Ui.Rounded(_stage, "MsgBtn", Gold, ListX + 150, ListY + 196, 230, 50, 12); Ui.Label(_stage, button, "Bold", 20, Ink, ListX + 150, ListY + 207, 230, 30, TextAnchor.UpperCenter);
            Ui.Click(_stage, buttonName, ListX + 150, ListY + 196, 230, 50, onClick);
        }

        static string CargoKey(JobDto j) => string.IsNullOrEmpty(j.cargo_image) ? "general" : j.cargo_image;
        /// <summary>Card / row picture: a real photograph cut from the approved mockups (tools/crop_ref_art.py); replace the file to change it.</summary>
        static Texture2D CargoPic(JobDto j) => Brand.Texture("job_" + CargoKey(j)) ?? Brand.Texture("job_general");
        static Texture2D HeroPic(JobDto j) => Brand.Texture("job_" + CargoKey(j) + "_hero") ?? CargoPic(j);
        static string Naira(long v) => "₦ " + Money.Format(v);
        static string CargoClass(string c)
        {
            switch (c) { case "freight": return "General Freight"; case "construction": return "Construction"; case "fuel_chemicals": return "Fuel & Chemicals"; case "agriculture": return "Agriculture"; case "manufacturing": return "Manufacturing"; case "vehicles": return "Vehicles"; default: return "General Freight"; }
        }
        static Color DifficultyColor(int d) => d <= 2 ? Brand.Good : d == 3 ? Gold : Brand.Bad;

        /// <summary>"from  ->  to" on one line, shrinking the type until it fits `maxW` (long depot names must never run into the card next to it).</summary>
        void RouteText(Transform host, string from, string to, int size, float x, float y, float maxW, string weight, Color fromCol, Color toCol)
        {
            var a = Ui.Label(host, from, weight, size, fromCol, x, y, 10, size + 8); var b = Ui.Label(host, to, weight, size, toCol, x, y, 10, size + 8);
            float arrow = size + 10f, total;
            while ((total = a.preferredWidth + arrow + b.preferredWidth + 16f) > maxW && size > 13) { size--; a.fontSize = size; b.fontSize = size; arrow = size + 10f; }
            a.rectTransform.sizeDelta = new Vector2(a.preferredWidth + 4, size + 8);
            float ax = x + a.preferredWidth + 8f;
            Ui.Icon(host, "icon_arrow", Gold, ax, y + (size + 6 - size) / 2f - 1f, size + 4);
            b.rectTransform.anchoredPosition = new Vector2(ax + arrow + 4f, -y); b.rectTransform.sizeDelta = new Vector2(b.preferredWidth + 4, size + 8);
        }

        void Tag(Transform host, int difficulty, float x, float y, float w, float h, int size)
        {
            var c = DifficultyColor(difficulty);
            Ui.Outlined(host, "Tag", new Color(c.r * 0.22f, c.g * 0.22f, c.b * 0.22f, 0.95f), new Color(c.r, c.g, c.b, 0.85f), x, y, w, h, 8, 1.4f);
            Ui.Icon(host, "ic_bars", c, x + 9, y + (h - size - 2) / 2f, size + 2);
            Ui.Label(host, JobMath.Difficulty(difficulty), "SemiBold", size, c, x + 12 + size, y + (h - size - 6) / 2f, w - 14 - size, size + 8);
        }

        void JobRow(Transform host, JobDto j, float x, float y, float w)
        {
            bool sel = j.id == _selId;
            if (sel) Ui.Rounded(host, "RowGlow", new Color(Gold.r, Gold.g, Gold.b, 0.12f), x - 4, y - 4, w + 8, RowH + 8, 20);
            var border = Ui.Outlined(host, "JobRow", CardFill, sel ? Gold : new Color(1, 1, 1, 0.09f), x, y, w, RowH, 16, sel ? 2.5f : 1.5f); _rowBorders[j.id] = border;
            var tex = CargoPic(j);
            Ui.Photo(host, "RowPhoto", tex, Ui.Cover(tex, 0f, 0.04f, 1f, 224f / (RowH - 5f)), x + 2.5f, y + 2.5f, 224, RowH - 5, 13, j.eligible ? Color.white : new Color(0.6f, 0.6f, 0.65f));
            Ui.Gradient(host, "RowPhotoFade", new Color(CardFill.r, CardFill.g, CardFill.b, 0f), new Color(CardFill.r, CardFill.g, CardFill.b, 1f), true, x + 190, y + 3, 36, RowH - 6);
            float tx = x + 246;
            var title = Ui.Label(host, j.cargo_type, "Bold", 28, White, tx, y + 10, w - 246 - 150, 36); title.resizeTextForBestFit = true; title.resizeTextMinSize = 18; title.resizeTextMaxSize = 28; title.horizontalOverflow = HorizontalWrapMode.Wrap; title.verticalOverflow = VerticalWrapMode.Truncate;
            Tag(host, j.difficulty, x + w - 124, y + 14, 100, 30, 15);
            RouteText(host, j.origin != null ? j.origin.name : "?", j.destination != null ? j.destination.name : "?", 19, tx, y + 52, w - 246 - 24, "Regular", new Color(0.86f, 0.88f, 0.92f), new Color(0.86f, 0.88f, 0.92f));
            Ui.Icon(host, "ic_road", Gold, tx, y + 84, 22); Ui.Label(host, $"{j.distance_km:0.#} km", "Regular", 18, new Color(0.86f, 0.88f, 0.92f), tx + 28, y + 85, 90, 24);
            Ui.Icon(host, "ic_clock", new Color(0.86f, 0.88f, 0.92f), tx + 130, y + 84, 22); Ui.Label(host, JobMath.FormatDuration(j.est_minutes > 0 ? j.est_minutes : JobMath.EtaMinutes(j.distance_km)), "Regular", 18, new Color(0.86f, 0.88f, 0.92f), tx + 158, y + 85, 100, 24);
            Ui.Icon(host, "ic_weight", Brand.Good, tx, y + 107, 20); Ui.Label(host, $"{j.cargo_weight_kg:N0} kg", "Regular", 17, Muted, tx + 28, y + 107, 140, 22);
            var rw = Ui.Label(host, Naira(j.reward), "Bold", 30, Lime, x + w - 76 - 300, y + 82, 300, 38, TextAnchor.UpperRight);
            Ui.Icon(host, "ic_chevron_right", Gold, x + w - 52, y + (RowH - 30) / 2f, 30);
            if (!j.eligible) Ui.Label(host, BlockedText(j), "SemiBold", 15, Brand.Bad, x + w - 260 - 24, y + 52, 260, 22, TextAnchor.UpperRight);
            var job = j; Ui.Click(host, "Btn_JobRow_" + j.code, x, y, w, RowH, () => SelectJob(job));
        }

        // ================================================================================================================ detail
        void BuildJobsDetail()
        {
            const float X = 1066, W = 825;
            if (_bs == BoardState.Loading) { BuildDetailSkeleton(X, W); return; }
            var j = SelectedJob();
            if (j == null)
            {
                Ui.Outlined(_stage, "DetailEmpty", PanelFill, PanelLine, X, 278, W, 790, 20);
                Ui.Icon(_stage, "ic_jobs", new Color(1, 1, 1, 0.16f), X + W / 2 - 50, 520, 100);
                Ui.Label(_stage, _bs == BoardState.Error ? "Job details are unavailable" : "Select a job to see its details", "SemiBold", 24, Muted, X, 640, W, 32, TextAnchor.UpperCenter);
                return;
            }
            BuildHeroCard(j, X, 278, W, 302);
            BuildRoutePreview(j, X, 587, 416, 387);
            BuildCargoCard(j, 1491, 587, 400, 190);
            BuildRequirements(j, 1491, 785, 400, 134);
            BuildAccept(j, 1491, 928, 400, 50);
            BuildEarnings(j, X, 988, W, 82);
        }

        void BuildDetailSkeleton(float X, float W)
        {
            var blocks = new List<Image>
            {
                Ui.Rounded(_stage, "SkHero", new Color(1, 1, 1, 0.10f), X, 278, W, 302, 20), Ui.Rounded(_stage, "SkRoute", new Color(1, 1, 1, 0.08f), X, 587, 416, 387, 20),
                Ui.Rounded(_stage, "SkCargo", new Color(1, 1, 1, 0.08f), 1491, 587, 400, 190, 20), Ui.Rounded(_stage, "SkReq", new Color(1, 1, 1, 0.08f), 1491, 785, 400, 134, 20),
                Ui.Rounded(_stage, "SkBtn", new Color(1, 1, 1, 0.12f), 1491, 928, 400, 50, 14), Ui.Rounded(_stage, "SkEarn", new Color(1, 1, 1, 0.08f), X, 988, W, 82, 20)
            };
            for (int i = 0; i < blocks.Count; i++) Breath.On(blocks[i], i);
        }

        void BuildHeroCard(JobDto j, float X, float Y, float W, float H)
        {
            var tex = HeroPic(j);
            Ui.Outlined(_stage, "HeroFrame", new Color(0, 0, 0, 1), new Color(1, 1, 1, 0.12f), X, Y, W, H, 20);
            Ui.Photo(_stage, "HeroPhoto", tex, Ui.Cover(tex, 0f, 0f, 1f, (W - 3f) / (H - 3f)), X + 1.5f, Y + 1.5f, W - 3, H - 3, 19);
            Ui.Gradient(_stage, "HeroShadeL", new Color(0.02f, 0.025f, 0.04f, 0.88f), new Color(0.02f, 0.025f, 0.04f, 0f), true, X + 2, Y + 2, W * 0.62f, H - 4);
            Ui.Gradient(_stage, "HeroShadeB", new Color(0.02f, 0.025f, 0.04f, 0f), new Color(0.02f, 0.025f, 0.04f, 0.92f), false, X + 2, Y + H - 130, W - 4, 128);
            Ui.Label(_stage, (CargoClass(j.cargo_category) + " transport").ToUpperInvariant(), "SemiBold", 18, new Color(0.88f, 0.9f, 0.94f), X + 24, Y + 22, 520, 24);
            var t = Ui.Label(_stage, j.cargo_type, "ExtraBold", 54, White, X + 24, Y + 46, 560, 66); Ui.Drop(t, 0.6f); t.resizeTextForBestFit = true; t.resizeTextMinSize = 28; t.resizeTextMaxSize = 54; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            RouteText(_stage, j.origin != null ? j.origin.name : "?", j.destination != null ? j.destination.name : "?", 26, X + 24, Y + 118, 520, "SemiBold", White, White);
            Tag(_stage, j.difficulty, X + W - 150, Y + 22, 126, 36, 18);
            // the four numbers under the picture, all from the job record
            var cells = new (string icon, Color tint, string value, string label)[]
            {
                ("ic_road", Gold, $"{j.distance_km:0.#} km", "Distance"),
                ("ic_clock", Gold, JobMath.FormatDuration(j.est_minutes > 0 ? j.est_minutes : JobMath.EtaMinutes(j.distance_km)), "Estimated Time"),
                ("ic_weight", Gold, $"{j.cargo_weight_kg:N0} kg", "Cargo Weight"),
                ("ic_coin", Gold, Naira(j.reward), "Base Payment"),
            };
            float cw = (W - 4f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                float cx = X + 2 + i * cw, cy = Y + H - 76;
                if (i > 0) Ui.Flat(_stage, "StatDivider", new Color(1, 1, 1, 0.16f), cx, cy + 12, 1, 50);
                Ui.Icon(_stage, cells[i].icon, cells[i].tint, cx + 22, cy + 18, 38);
                Ui.Label(_stage, cells[i].value, "Bold", 24, White, cx + 70, cy + 10, cw - 74, 32);
                Ui.Label(_stage, cells[i].label, "Regular", 17, new Color(0.82f, 0.85f, 0.9f), cx + 70, cy + 40, cw - 74, 24);
            }
        }

        // ---- route preview: the real artwork cropped around the cities involved, the validated route polyline, and the job's own endpoints
        static MapCity CityOf(WorldMapModel m, LocationDto l)
        {
            if (m == null || l == null || string.IsNullOrEmpty(l.slug)) return null;
            return m.Cities.FirstOrDefault(c => !string.IsNullOrEmpty(c.locationSlugPrefix) && l.slug.StartsWith(c.locationSlugPrefix, StringComparison.Ordinal));
        }

        void BuildRoutePreview(JobDto j, float X, float Y, float W, float H)
        {
            Ui.Outlined(_stage, "RouteCard", PanelFill, PanelLine, X, Y, W, H, 20, 1.5f);
            Ui.Label(_stage, "Route Preview", "Bold", 28, White, X + 22, Y + 16, 260, 36);
            SmallPillIn(_stage, "View on Map", X + W - 138, Y + 18, 118, 34, _act.WorldMap != null ? _act.WorldMap : () => Toast("The map is unavailable."));
            float mx = X + 14, my = Y + 66, mw = W - 28, mh = 236;
            var model = WorldMapScreen.Model(); var art = WorldMapScreen.Artwork(model);
            var from = CityOf(model, j.origin); var to = CityOf(model, j.destination);
            if (model == null || art == null || from == null || to == null)
            {
                Ui.Rounded(_stage, "MapNone", new Color(0.07f, 0.08f, 0.1f), mx, my, mw, mh, 14);
                Ui.Icon(_stage, "ic_map", new Color(1, 1, 1, 0.2f), mx + mw / 2 - 36, my + 70, 72);
                Ui.Label(_stage, "The map is unavailable right now.", "SemiBold", 18, Muted, mx, my + 156, mw, 26, TextAnchor.UpperCenter);
            }
            else DrawRouteMap(j, model, art, from, to, mx, my, mw, mh);
            // three numbers under the map (all server values; the fuel figure is for the vehicle this player would drive)
            float by = Y + H - 70, cw = (W - 28f) / 3f;
            var items = new (string icon, string text)[] { ("ic_road", $"{j.distance_km:0.#} km"), ("ic_clock", JobMath.FormatDuration(j.est_minutes > 0 ? j.est_minutes : JobMath.EtaMinutes(j.distance_km))), ("ic_fuel", $"{j.est_fuel_l:0.#} L fuel") };
            for (int i = 0; i < 3; i++)
            {
                float cx = X + 14 + i * cw;
                if (i > 0) Ui.Flat(_stage, "RouteDivider", new Color(1, 1, 1, 0.14f), cx, by + 6, 1, 44);
                Ui.Icon(_stage, items[i].icon, Gold, cx + 14, by + 12, 30);
                Ui.Label(_stage, items[i].text, "SemiBold", 19, White, cx + 52, by + 16, cw - 56, 26);
            }
        }

        void DrawRouteMap(JobDto j, WorldMapModel model, Texture2D art, MapCity from, MapCity to, float mx, float my, float mw, float mh)
        {
            float tw = model.Data.image.textureWidth, th = model.Data.image.textureHeight, aspect = mw / mh;
            bool local = from.id == to.id;
            float cx0, cy0, cw, ch;
            Vector2 pFrom, pTo;                                     // artwork pixels
            MapRoute route = model.Routes.FirstOrDefault(r => r.via.Contains(from.id) && r.via.Contains(to.id));
            if (!local)
            {
                float minX = Mathf.Min(from.mapX, to.mapX), maxX = Mathf.Max(from.mapX, to.mapX), minY = Mathf.Min(from.mapY, to.mapY), maxY = Mathf.Max(from.mapY, to.mapY);
                cw = Mathf.Max((maxX - minX) * 1.9f, 220f); ch = cw / aspect; if (ch < (maxY - minY) * 1.9f) { ch = (maxY - minY) * 1.9f; cw = ch * aspect; }
                cx0 = (minX + maxX) / 2f - cw / 2f; cy0 = (minY + maxY) / 2f - ch / 2f; pFrom = new Vector2(from.mapX, from.mapY); pTo = new Vector2(to.mapX, to.mapY);
            }
            else
            {   // both ends are inside one city: zoom to the city and place the two depots by their real world coordinates (a schematic, labelled as such)
                cw = 150f; ch = cw / aspect; cx0 = from.mapX - cw / 2f; cy0 = from.mapY - ch / 2f;
                float ox = j.origin != null ? (float)j.origin.world_x : 0f, oz = j.origin != null ? (float)j.origin.world_z : 0f, dx = j.destination != null ? (float)j.destination.world_x : 0f, dz = j.destination != null ? (float)j.destination.world_z : 0f;
                float span = Mathf.Max(Mathf.Abs(dx - ox), Mathf.Abs(dz - oz), 1f), k = Mathf.Min(cw, ch) * 0.55f / span;
                var mid = new Vector2((ox + dx) / 2f, (oz + dz) / 2f);
                pFrom = new Vector2(from.mapX + (ox - mid.x) * k, from.mapY - (oz - mid.y) * k); pTo = new Vector2(from.mapX + (dx - mid.x) * k, from.mapY - (dz - mid.y) * k);
            }
            cx0 = Mathf.Clamp(cx0, 0f, Mathf.Max(0f, model.Data.image.contentWidth - cw)); cy0 = Mathf.Clamp(cy0, 0f, Mathf.Max(0f, model.Data.image.contentHeight - ch));
            Ui.Photo(_stage, "RouteMapPhoto", art, new Rect(cx0 / tw, (th - (cy0 + ch)) / th, cw / tw, ch / th), mx, my, mw, mh, 14, new Color(0.9f, 0.92f, 0.95f));
            Ui.Gradient(_stage, "RouteMapShade", new Color(0, 0, 0, 0f), new Color(0, 0, 0, 0.35f), false, mx, my + mh * 0.5f, mw, mh * 0.5f);
            var host = Ui.Box(_stage, "RouteMapHost", mx, my, mw, mh);
            Func<Vector2, Vector2> at = p => new Vector2((p.x - cx0) / cw * mw, (p.y - cy0) / ch * mh);
            var pts = new List<Vector2>();
            if (!local && route != null)
            {   // the validated route polyline between the two cities (the route may continue past them; only the part between is drawn)
                int a = Array.IndexOf(route.via, from.id), b = Array.IndexOf(route.via, to.id); int step = b >= a ? 1 : -1;
                for (int i = a; i != b + step; i += step) { var c = model.City(route.via[i]); pts.Add(at(new Vector2(c.mapX, c.mapY))); }
            }
            else { pts.Add(at(pFrom)); pts.Add(at(pTo)); }
            for (int i = 1; i < pts.Count; i++) { Ui.Segment(host, pts[i - 1], pts[i], 9f, new Color(Gold.r, Gold.g, Gold.b, 0.25f)); Ui.Segment(host, pts[i - 1], pts[i], 4f, Gold); }
            Pin(host, at(pFrom), false, j.origin != null ? j.origin.name : from.name, mw);
            Pin(host, at(pTo), true, j.destination != null ? j.destination.name : to.name, mw);
            if (local) Ui.Label(host, "Local route in " + from.name + " (schematic)", "Regular", 13, new Color(1, 1, 1, 0.7f), 12, mh - 24, mw - 24, 18);
        }

        void Pin(Transform host, Vector2 p, bool destination, string label, float mw)
        {
            Color c = destination ? Brand.Bad : Brand.Good;
            Ui.Disc(host, "PinGlow", new Color(c.r, c.g, c.b, 0.28f), p.x - 22, p.y - 22, 44);
            Ui.Disc(host, "PinRing", White, p.x - 14, p.y - 14, 28); Ui.Disc(host, "PinDot", c, p.x - 10, p.y - 10, 20);
            if (destination) Ui.Icon(host, "ic_pin", White, p.x - 7, p.y - 8, 14);
            var t = Ui.Label(host, label, "Bold", 16, White, 0, 0, 10, 22); float w = t.preferredWidth + 20f; Destroy(t.gameObject);
            float lx = Mathf.Clamp(p.x + 20, 6, mw - w - 6), ly = p.y - 34;
            if (ly < 6) ly = p.y + 18;
            Ui.Rounded(host, "PinChip", new Color(0.03f, 0.035f, 0.05f, 0.9f), lx, ly, w, 26, 8);
            Ui.Label(host, label, "Bold", 16, White, lx + 10, ly + 3, w, 22);
        }

        // ---- cargo information
        void BuildCargoCard(JobDto j, float X, float Y, float W, float H)
        {
            Ui.Outlined(_stage, "CargoCard", PanelFill, PanelLine, X, Y, W, H, 20, 1.5f);
            Ui.Label(_stage, "Cargo Information", "Bold", 26, White, X + 20, Y + 12, 340, 34);
            var tex = CargoPic(j);
            Ui.Photo(_stage, "CargoPhoto", tex, Ui.Cover(tex, 0.04f, 0.05f, 0.92f, 104f / 70f), X + 20, Y + 54, 104, 70, 10);
            var nm = Ui.Label(_stage, j.cargo_type, "Bold", 22, White, X + 138, Y + 54, W - 154, 28); nm.resizeTextForBestFit = true; nm.resizeTextMinSize = 15; nm.resizeTextMaxSize = 22; nm.horizontalOverflow = HorizontalWrapMode.Wrap; nm.verticalOverflow = VerticalWrapMode.Truncate;
            var ds = Ui.Paragraph(_stage, string.IsNullOrEmpty(j.cargo_description) ? "General cargo." : j.cargo_description, "Regular", 15, new Color(0.8f, 0.83f, 0.88f), X + 138, Y + 82, W - 154, 46); ds.verticalOverflow = VerticalWrapMode.Truncate;
            const float ly = 136;
            Attr(X + 20, Y + ly, "Cargo Weight", $"{j.cargo_weight_kg:N0} kg", 100, White);
            Attr(X + 124, Y + ly, "Cargo Type", CargoClass(j.cargo_category), 130, White);
            Attr(X + 262, Y + ly, "Fragile", j.fragile ? "Yes" : "No", 50, j.fragile ? Gold : White);
            Attr(X + 322, Y + ly, "Special Handling", j.special_handling ? "Yes" : "No", 70, j.special_handling ? Gold : White, true);
        }

        void Attr(float x, float y, string label, string value, float w, Color vc, bool twoLine = false)
        {
            var l = Ui.Label(_stage, twoLine ? "Special\nHandling" : label, "Regular", 13, Muted, x, y - (twoLine ? 8 : 0), w + 20, twoLine ? 34 : 18);
            var v = Ui.Label(_stage, value, "SemiBold", 16, vc, x, y + 20, w + 24, 22); v.resizeTextForBestFit = true; v.resizeTextMinSize = 12; v.resizeTextMaxSize = 16; v.horizontalOverflow = HorizontalWrapMode.Wrap; v.verticalOverflow = VerticalWrapMode.Truncate;
        }

        // ---- requirements: each one is checked against the player's own state (level, vehicles) or the server's verdict (route)
        void BuildRequirements(JobDto j, float X, float Y, float W, float H)
        {
            Ui.Outlined(_stage, "ReqCard", PanelFill, PanelLine, X, Y, W, H, 20, 1.5f);
            Ui.Label(_stage, "Requirements", "Bold", 26, White, X + 20, Y + 8, 340, 34);
            bool routeOk = j.blocked_reason != "route_locked" && j.blocked_reason != "route_unavailable";
            var rows = new (string icon, string label, string value, bool ok)[]
            {
                ("ic_gauge", "Minimum Level", "Level " + Mathf.Max(1, j.min_level), PlayerLevel() >= j.min_level),
                ("ic_bus", "Required Vehicle", VehicleLabel(j.required_category), j.has_vehicle),
                ("ic_road", "Route Access", routeOk ? "Open" : (j.blocked_reason == "route_locked" ? "Locked" : "Not open yet"), routeOk),
            };
            for (int i = 0; i < rows.Length; i++)
            {
                float y = Y + 44 + i * 29;
                if (i > 0) Ui.Flat(_stage, "ReqLine", new Color(1, 1, 1, 0.08f), X + 18, y - 1, W - 36, 1);
                Ui.Icon(_stage, rows[i].icon, Gold, X + 20, y + 3, 22);
                Ui.Label(_stage, rows[i].label, "Regular", 15, Muted, X + 54, y + 5, 170, 20);
                Ui.Label(_stage, rows[i].value, "SemiBold", 17, White, X + 220, y + 3, 130, 24);
                if (rows[i].ok) Ui.Icon(_stage, "ic_check_circle", Brand.Good, X + W - 46, y + 1, 26); else Ui.Icon(_stage, "ic_lock", Brand.Bad, X + W - 44, y + 2, 24);
            }
        }

        static string VehicleLabel(string category) => string.IsNullOrEmpty(category) ? "Any" : char.ToUpperInvariant(category[0]) + category.Substring(1);

        void BuildAccept(JobDto j, float X, float Y, float W, float H)
        {
            bool can = j.eligible;
            Ui.Rounded(_stage, "AcceptBg", can ? Gold : new Color(0.20f, 0.22f, 0.27f, 1f), X, Y, W, H, 14);
            string text = can ? "Accept Job" : AcceptBlocked(j);
            var t = Ui.Label(_stage, text, "Bold", can ? 26 : 20, can ? Ink : Muted, X, Y + (can ? 9 : 12), W, 34, TextAnchor.UpperCenter);
            if (can) Ui.Icon(_stage, "icon_arrow", Ink, X + W / 2 + t.preferredWidth / 2 + 14, Y + 11, 28);
            var job = j;
            Ui.Click(_stage, can ? "Btn_AcceptJob" : "Btn_AcceptBlocked", X, Y, W, H, () => { if (job.eligible) _act.AcceptJob(job); else Toast(BlockedExplain(job)); });
        }

        static string AcceptBlocked(JobDto j)
        {
            switch (j.blocked_reason)
            {
                case "level_too_low": return "Requires level " + j.min_level;
                case "route_locked": case "route_unavailable": return "Route not available";
                case "activity_in_progress": return "Finish your current job first";
                case "no_matching_vehicle": return "You need a " + j.required_category;
                case "job_full": return "Job is full";
                default: return "Unavailable";
            }
        }

        // ---- earnings strip: the server's estimate for THIS player (reward, fuel cost at the shop price, XP, profit)
        void BuildEarnings(JobDto j, float X, float Y, float W, float H)
        {
            Ui.Outlined(_stage, "EarnCard", PanelFill, PanelLine, X, Y, W, H, 20, 1.5f);
            var cells = new (string label, string value, Color c)[]
            {
                ("Potential Earnings", Naira(j.reward), Lime), ("Est. Fuel Cost", Naira(j.est_fuel_cost), White),
                ("XP Reward", "+" + Money.Format(j.xp_reward) + " XP", new Color(0.55f, 0.78f, 1f)), ("Estimated Profit", Naira(j.est_profit), j.est_profit >= 0 ? Brand.Good : Brand.Bad),
            };
            float cw = W / 4f;
            for (int i = 0; i < 4; i++)
            {
                float cx = X + i * cw;
                if (i > 0) Ui.Flat(_stage, "EarnDivider", new Color(1, 1, 1, 0.14f), cx, Y + 14, 1, H - 28);
                Ui.Label(_stage, cells[i].label, "Regular", 17, new Color(0.84f, 0.86f, 0.9f), cx + 24, Y + 10, cw - 30, 22);
                Ui.Label(_stage, cells[i].value, "Bold", 32, cells[i].c, cx + 24, Y + 34, cw - 30, 42);
            }
        }
    }
}
