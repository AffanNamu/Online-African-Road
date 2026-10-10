using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ARO.Backend;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ARO.Game
{
    /// <summary>
    /// The sign-in / create-account screen: full-bleed sunset backdrop, brand column on the left, glass card on the right.
    /// Everything is built in code from Resources/UI (background, logo mark, icons, Montserrat) so art can be swapped by replacing files.
    /// Reference layout is 1920x1080; left items anchor to the top-left/bottom-left, the card and language pill to the right edge.
    /// </summary>
    public sealed class LoginScreen : MonoBehaviour
    {
        enum Anchor { TopLeft, TopRight, BottomLeft, CenterRight }
        /// <summary>The key art has its truck on the right, where the card sits; mirror it so the truck is on the left (as in the design). Set false to show it as shot.</summary>
        const bool MirrorBackdrop = true;
        const float CardW = 724f, CardH = 824f, Pad = 44f, FieldW = CardW - Pad * 2f;
        static readonly Color CardBg = new Color(0.075f, 0.082f, 0.10f, 0.90f);
        static readonly Color Line = new Color(1f, 1f, 1f, 0.14f);
        static readonly Color FieldBorder = new Color(1f, 1f, 1f, 0.16f);
        static readonly Color FieldBg = new Color(0.055f, 0.06f, 0.075f, 0.92f);

        GameServices _svc; Func<Task> _onSuccess;
        Canvas _canvas; RectTransform _root, _card, _content; RawImage _bg;
        Text _status, _langNote; float _langNoteUntil;
        InputField _email, _pass, _name;
        bool _busy, _create, _remember = true, _showPass;
        string _pendingInfo; Color _pendingInfoColor;
        readonly List<KeyValuePair<Image, InputField>> _fields = new List<KeyValuePair<Image, InputField>>();

        public static LoginScreen Create(GameServices svc, Func<Task> onSuccess, string info)
        {
            var go = new GameObject("LoginScreen");
            var ls = go.AddComponent<LoginScreen>(); ls.Build(svc, onSuccess, info);
            return ls;
        }

        // ------------------------------------------------------------------ layout helpers
        static RectTransform Place(GameObject go, Transform parent, Anchor a, float x, float y, float w, float h)
        {
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Vector2 anc = a == Anchor.TopLeft ? new Vector2(0, 1) : a == Anchor.TopRight ? new Vector2(1, 1) : a == Anchor.BottomLeft ? new Vector2(0, 0) : new Vector2(1, 0.5f);
            rt.anchorMin = rt.anchorMax = rt.pivot = anc;
            float sx = (a == Anchor.TopRight || a == Anchor.CenterRight) ? -1f : 1f, sy = (a == Anchor.BottomLeft) ? 1f : (a == Anchor.CenterRight ? 0f : -1f);
            rt.anchoredPosition = new Vector2(x * sx, y * sy); rt.sizeDelta = new Vector2(w, h);
            return rt;
        }
        static RectTransform Local(GameObject go, Transform parent, float x, float y, float w, float h) => Place(go, parent, Anchor.TopLeft, x, y, w, h);

        static Image Rounded(Transform parent, string name, Color c, float x, float y, float w, float h, float radius, Anchor a = Anchor.TopLeft)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            Place(go, parent, a, x, y, w, h);
            var img = go.GetComponent<Image>(); img.sprite = UIGfx.Glass; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 18f / Mathf.Max(radius, 1f); img.color = c;
            return img;
        }
        static Image Flat(Transform parent, string name, Color c, float x, float y, float w, float h, Anchor a = Anchor.TopLeft)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); Place(go, parent, a, x, y, w, h);
            var img = go.GetComponent<Image>(); img.color = c; img.raycastTarget = false; return img;
        }
        static Image Icon(Transform parent, string icon, Color tint, float x, float y, float size, Anchor a = Anchor.TopLeft)
        {
            var go = new GameObject("Icon_" + icon, typeof(RectTransform), typeof(Image)); Place(go, parent, a, x, y, size, size);
            var img = go.GetComponent<Image>(); img.sprite = Brand.Icon(icon); img.color = tint; img.preserveAspect = true; img.raycastTarget = false; img.enabled = img.sprite != null; return img;
        }
        static Text Txt(Transform parent, string s, string weight, int size, Color c, float x, float y, float w, float h, TextAnchor al, Anchor a = Anchor.TopLeft)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text)); Place(go, parent, a, x, y, w, h);
            var t = go.GetComponent<Text>(); t.font = Brand.FontOf(weight); t.fontSize = size; t.color = c; t.alignment = al; t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false; t.text = s; return t;
        }
        static void Shadow(Text t, float a = 0.55f) { var sh = t.gameObject.AddComponent<UnityEngine.UI.Shadow>(); sh.effectColor = new Color(0, 0, 0, a); sh.effectDistance = new Vector2(2f, -2f); }
        static Button Hit(Transform parent, string name, float x, float y, float w, float h, Action onClick, Anchor a = Anchor.TopLeft)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); Place(go, parent, a, x, y, w, h);
            var img = go.GetComponent<Image>(); img.color = new Color(1, 1, 1, 0.001f);
            var b = go.GetComponent<Button>(); b.targetGraphic = img; b.transition = Selectable.Transition.None; b.onClick.AddListener(() => onClick());
            return b;
        }

        // ------------------------------------------------------------------ build
        void Build(GameServices svc, Func<Task> onSuccess, string info)
        {
            _svc = svc; _onSuccess = onSuccess; _pendingInfo = info; _pendingInfoColor = Brand.Gold;
            _remember = PlayerPrefs.GetInt("aro.remember", 1) == 1;
            _canvas = UIKit.CreateCanvas("LoginCanvas", 30);
            _canvas.transform.SetParent(transform, false);
            _root = (RectTransform)_canvas.transform;

            var bgGo = new GameObject("Backdrop", typeof(RectTransform), typeof(RawImage)); bgGo.transform.SetParent(_root, false);
            var brt = (RectTransform)bgGo.transform; brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one; brt.offsetMin = brt.offsetMax = Vector2.zero;
            _bg = bgGo.GetComponent<RawImage>(); _bg.texture = Brand.Texture("login_bg"); _bg.color = _bg.texture != null ? Color.white : new Color(0.08f, 0.09f, 0.12f); _bg.raycastTarget = false;

            BuildBrandColumn(); BuildLanguagePill();
            _card = Rounded(_root, "CardBorder", new Color(1f, 1f, 1f, 0.16f), 127f, 0f, CardW, CardH, 30f, Anchor.CenterRight).rectTransform;
            var fill = Rounded(_card, "Card", CardBg, 1.5f, 1.5f, CardW - 3f, CardH - 3f, 29f);
            _card = fill.rectTransform;
            BuildCard();
        }

        void BuildBrandColumn()
        {
            Icon(_root, "logo_mark", Color.white, 108f, 34f, 156f);
            var a = Txt(_root, "AFRICAN", "ExtraBold", 98, Color.white, 292f, 30f, 700f, 120f, TextAnchor.UpperLeft); Shadow(a);
            var b = Txt(_root, "R O A D S   O N L I N E", "Bold", 41, Brand.Gold, 297f, 138f, 700f, 56f, TextAnchor.UpperLeft); Shadow(b);
            var tag = Txt(_root, "D R I V E   <color=#F9B521>•</color>   D E L I V E R   <color=#F9B521>•</color>   E X P L O R E   <color=#F9B521>•</color>   T O G E T H E R", "SemiBold", 22,
                new Color(0.92f, 0.93f, 0.95f), 143f, 198f, 900f, 34f, TextAnchor.UpperLeft); Shadow(tag);

            var h1 = Txt(_root, "REAL ROADS.", "ExtraBold", 62, Color.white, 76f, 288f, 900f, 80f, TextAnchor.LowerLeft, Anchor.BottomLeft); Shadow(h1);
            var h2 = Txt(_root, "REAL AFRICA.", "ExtraBold", 62, Brand.Gold, 76f, 227f, 900f, 80f, TextAnchor.LowerLeft, Anchor.BottomLeft); Shadow(h2);
            var sub = Txt(_root, "Multiplayer truck and bus simulator across Africa.", "Regular", 25, new Color(0.9f, 0.91f, 0.94f), 78f, 198f, 900f, 36f, TextAnchor.LowerLeft, Anchor.BottomLeft); Shadow(sub);

            // feature strip
            var strip = Rounded(_root, "Features", new Color(0.05f, 0.055f, 0.07f, 0.72f), 62f, 36f, 962f, 138f, 20f, Anchor.BottomLeft);
            var items = new[] {
                ("feat_world", "Open World", "Explore African cities"), ("feat_people", "Multiplayer", "Drive together"),
                ("feat_vehicles", "Trucks & Buses", "Multiple vehicles"), ("feat_jobs", "Real Jobs", "Deliver and earn"), ("feat_weather", "Dynamic Weather", "Day & night cycle") };
            float colW = 962f / items.Length;
            for (int i = 0; i < items.Length; i++)
            {
                float cx = i * colW;
                Icon(strip.transform, items[i].Item1, Color.white, cx + colW / 2f - 25f, 18f, 50f);
                Txt(strip.transform, items[i].Item2, "Bold", 19, Color.white, cx + colW / 2f, 76f, colW, 26f, TextAnchor.UpperCenter).rectTransform.pivot = new Vector2(0.5f, 1f);
                Txt(strip.transform, items[i].Item3, "Regular", 15, new Color(0.7f, 0.72f, 0.76f), cx + colW / 2f, 102f, colW, 22f, TextAnchor.UpperCenter).rectTransform.pivot = new Vector2(0.5f, 1f);
                if (i > 0) Flat(strip.transform, "Divider", Line, cx, 22f, 1f, 94f);
            }
        }

        void BuildLanguagePill()
        {
            var pill = Rounded(_root, "Language", new Color(0.06f, 0.065f, 0.08f, 0.78f), 46f, 44f, 212f, 58f, 29f, Anchor.TopRight);
            Icon(pill.transform, "icon_globe", Color.white, 18f, 13f, 32f);
            Txt(pill.transform, "English (UK)", "SemiBold", 19, Color.white, 60f, 15f, 120f, 28f, TextAnchor.MiddleLeft);
            Icon(pill.transform, "icon_chevron", Color.white, 182f, 17f, 22f);
            Hit(pill.transform, "Btn_Language", 0f, 0f, 212f, 58f, () => { _langNote.text = "More languages are coming soon."; _langNoteUntil = Time.unscaledTime + 2.6f; });
            _langNote = Txt(_root, "", "Regular", 18, new Color(0.95f, 0.95f, 0.97f), 46f, 110f, 300f, 26f, TextAnchor.UpperRight, Anchor.TopRight);
            _langNote.horizontalOverflow = HorizontalWrapMode.Overflow;
        }

        // ------------------------------------------------------------------ card
        void BuildCard()
        {
            if (_content != null) Destroy(_content.gameObject);
            _fields.RemoveAll(kv => kv.Key == null);
            var holder = new GameObject("Content", typeof(RectTransform));
            _content = Local(holder, _card, 0, 0, CardW - 3f, CardH - 3f);
            _status = null;

            // tabs
            TabButton("Sign In", Pad, !_create, () => { if (_create && !_busy) { _create = false; BuildCard(); } });
            TabButton("Create Account", Pad + FieldW / 2f, _create, () => { if (!_create && !_busy) { _create = true; BuildCard(); } });
            Flat(_content, "TabLine", Line, Pad, 86f, FieldW, 1f);
            Flat(_content, "TabActive", Brand.Gold, Pad + (_create ? FieldW / 2f : 0f), 84f, FieldW / 2f, 3f);

            if (!_create) BuildSignIn(); else BuildCreate();

            // divider + social + legal
            float orY = _create ? 600f : 612f, socY = _create ? 648f : 665f;
            Flat(_content, "OrL", Line, Pad, orY + 11f, FieldW / 2f - 28f, 1f); Flat(_content, "OrR", Line, Pad + FieldW / 2f + 28f, orY + 11f, FieldW / 2f - 28f, 1f);
            Txt(_content, "OR", "Regular", 19, new Color(0.7f, 0.72f, 0.76f), Pad + FieldW / 2f, orY, 60f, 24f, TextAnchor.UpperCenter).rectTransform.pivot = new Vector2(0.5f, 1f);
            float sw = (FieldW - 20f) / 2f;
            Social("Btn_Google", "icon_google", "Continue with Google", Pad, socY, sw, () => Oauth("google", "Google"));
            Social("Btn_Apple", "icon_apple", "Continue with Apple", Pad + sw + 20f, socY, sw, () => Oauth("apple", "Apple"));
            Txt(_content, "By continuing, you agree to our <color=#F9B521>Terms of Service</color> and <color=#F9B521>Privacy Policy</color>.", "Regular", 18,
                new Color(0.7f, 0.72f, 0.76f), CardW / 2f - 1.5f, 770f, FieldW + 40f, 26f, TextAnchor.UpperCenter).rectTransform.pivot = new Vector2(0.5f, 1f);

            if (!string.IsNullOrEmpty(_pendingInfo)) { SetStatus(_pendingInfo, _pendingInfoColor); _pendingInfo = null; }
        }

        void TabButton(string label, float x, bool active, Action onClick)
        {
            var t = Txt(_content, label, active ? "Bold" : "SemiBold", 26, active ? Brand.Gold : new Color(0.62f, 0.64f, 0.68f), x + FieldW / 4f, 30f, FieldW / 2f, 40f, TextAnchor.UpperCenter);
            t.rectTransform.pivot = new Vector2(0.5f, 1f);
            Hit(_content, "Tab_" + label.Replace(" ", ""), x, 20f, FieldW / 2f, 62f, onClick);
        }

        void BuildSignIn()
        {
            Txt(_content, "Welcome Back", "ExtraBold", 44, Color.white, Pad, 116f, FieldW, 56f, TextAnchor.UpperLeft);
            Txt(_content, "Sign in to continue your journey", "Regular", 24, new Color(0.72f, 0.74f, 0.78f), Pad, 176f, FieldW, 34f, TextAnchor.UpperLeft);
            _email = Field("Email address", "icon_mail", 249f, 72f, false); _pass = Field("Password", "icon_lock", 341f, 72f, true); _name = null;
            _email.text = PlayerPrefs.GetString("aro.last_email", "");
            _email.onEndEdit.AddListener(_ => { if (EnterPressed()) _pass.Select(); });
            _pass.onEndEdit.AddListener(_ => { if (EnterPressed()) DoSignIn(); });

            // remember me + forgot password
            var box = Rounded(_content, "Checkbox", _remember ? Brand.Gold : new Color(1, 1, 1, 0.35f), Pad, 441f, 30f, 30f, 8f);
            var inner = Rounded(box.transform, "Inner", new Color(0.06f, 0.065f, 0.08f, 1f), 2f, 2f, 26f, 26f, 6f); inner.enabled = !_remember;
            var tick = Icon(box.transform, "icon_check", Brand.Ink, 3f, 3f, 24f); tick.enabled = _remember && tick.sprite != null;
            Txt(_content, "Remember me", "Regular", 22, Color.white, Pad + 44f, 440f, 300f, 32f, TextAnchor.MiddleLeft);
            Hit(_content, "Btn_Remember", Pad, 432f, 230f, 48f, () =>
            {
                _remember = !_remember; box.color = _remember ? Brand.Gold : new Color(1, 1, 1, 0.35f); inner.enabled = !_remember; tick.enabled = _remember && tick.sprite != null;
                PlayerPrefs.SetInt("aro.remember", _remember ? 1 : 0);
            });
            var forgot = Txt(_content, "Forgot password?", "SemiBold", 21, Brand.Gold, CardW - 3f - Pad, 440f, 260f, 32f, TextAnchor.MiddleRight);
            forgot.rectTransform.pivot = new Vector2(1f, 1f); forgot.rectTransform.anchorMin = forgot.rectTransform.anchorMax = new Vector2(0, 1); forgot.rectTransform.anchoredPosition = new Vector2(CardW - 3f - Pad, -440f);
            Flat(_content, "ForgotUnderline", Brand.Gold, CardW - 3f - Pad - forgot.preferredWidth, 468f, forgot.preferredWidth, 1.5f);
            Hit(_content, "Btn_Forgot", CardW - 3f - Pad - 230f, 432f, 230f, 48f, () => DoForgot());

            Primary("Btn_SIGNIN", "Sign In", 505f, () => DoSignIn());
            _status = Txt(_content, "", "SemiBold", 19, Brand.Bad, Pad, 586f, FieldW, 26f, TextAnchor.UpperCenter); _status.rectTransform.pivot = new Vector2(0.5f, 1f);
            _status.rectTransform.anchoredPosition = new Vector2(Pad + FieldW / 2f, -586f);
        }

        void BuildCreate()
        {
            Txt(_content, "Join the Convoy", "ExtraBold", 44, Color.white, Pad, 116f, FieldW, 56f, TextAnchor.UpperLeft);
            Txt(_content, "Create your driver account to start earning", "Regular", 24, new Color(0.72f, 0.74f, 0.78f), Pad, 176f, FieldW, 34f, TextAnchor.UpperLeft);
            _name = Field("Display name", "icon_user", 232f, 68f, false); _email = Field("Email address", "icon_mail", 312f, 68f, false); _pass = Field("Password (8+ characters)", "icon_lock", 392f, 68f, true);
            _name.onEndEdit.AddListener(_ => { if (EnterPressed()) _email.Select(); });
            _email.onEndEdit.AddListener(_ => { if (EnterPressed()) _pass.Select(); });
            _pass.onEndEdit.AddListener(_ => { if (EnterPressed()) DoCreate(); });
            Primary("Btn_CREATEACCOUNT", "Create Account", 484f, () => DoCreate(), 72f);
            _status = Txt(_content, "", "SemiBold", 19, Brand.Bad, Pad, 566f, FieldW, 26f, TextAnchor.UpperCenter); _status.rectTransform.pivot = new Vector2(0.5f, 1f);
            _status.rectTransform.anchoredPosition = new Vector2(Pad + FieldW / 2f, -566f);
        }

        InputField Field(string placeholder, string icon, float y, float h, bool password)
        {
            var border = Rounded(_content, "FieldBorder", FieldBorder, Pad, y, FieldW, h, 13f);
            var inner = Rounded(border.transform, "Field", FieldBg, 1.5f, 1.5f, FieldW - 3f, h - 3f, 12f);
            Icon(inner.transform, icon, new Color(0.78f, 0.8f, 0.84f), 24f, (h - 3f - 32f) / 2f, 32f);
            var areaGo = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            float right = password ? 76f : 24f;
            var area = Local(areaGo, inner.transform, 76f, 0f, FieldW - 3f - 76f - right, h - 3f);
            var txt = UIKit.Label(area, "", 26, Color.white, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero); txt.font = Brand.FontOf("Regular");
            var ph = UIKit.Label(area, placeholder, 26, new Color(1, 1, 1, 0.45f), TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero); ph.font = Brand.FontOf("Regular");
            var f = inner.gameObject.AddComponent<InputField>();
            f.textComponent = txt; f.placeholder = ph; f.lineType = InputField.LineType.SingleLine; f.characterLimit = 120;
            f.customCaretColor = true; f.caretColor = Brand.Gold; f.selectionColor = new Color(Brand.Gold.r, Brand.Gold.g, Brand.Gold.b, 0.35f);
            if (password)
            {
                f.contentType = InputField.ContentType.Password; _showPass = false;
                var eye = Icon(inner.transform, "icon_eye_off", new Color(0.78f, 0.8f, 0.84f), FieldW - 3f - 60f, (h - 3f - 32f) / 2f, 32f);
                Hit(inner.transform, "Btn_ShowPassword", FieldW - 3f - 72f, 0f, 72f, h - 3f, () =>
                {
                    _showPass = !_showPass; f.contentType = _showPass ? InputField.ContentType.Standard : InputField.ContentType.Password;
                    f.ForceLabelUpdate(); eye.sprite = Brand.Icon(_showPass ? "icon_eye" : "icon_eye_off");
                });
            }
            _fields.Add(new KeyValuePair<Image, InputField>(border, f));
            return f;
        }

        void Primary(string name, string label, float y, Action onClick, float h = 72f)
        {
            var b = Rounded(_content, name, Brand.Gold, Pad, y, FieldW, h, 13f);
            var btn = b.gameObject.AddComponent<Button>(); btn.targetGraphic = b;
            var cb = btn.colors; cb.highlightedColor = new Color(1f, 0.96f, 0.85f); cb.pressedColor = new Color(0.85f, 0.85f, 0.85f); cb.selectedColor = Color.white; cb.fadeDuration = 0.08f; btn.colors = cb;
            btn.onClick.AddListener(() => onClick());
            Txt(b.transform, label, "Bold", 28, Brand.Ink, FieldW / 2f, (h - 36f) / 2f, FieldW, 36f, TextAnchor.UpperCenter).rectTransform.pivot = new Vector2(0.5f, 1f);
            Icon(b.transform, "icon_arrow", Brand.Ink, FieldW - 66f, (h - 34f) / 2f, 34f);
        }

        void Social(string name, string icon, string label, float x, float y, float w, Action onClick)
        {
            var border = Rounded(_content, name, FieldBorder, x, y, w, 72f, 13f);
            var inner = Rounded(border.transform, "Fill", new Color(0.06f, 0.065f, 0.08f, 0.9f), 1.5f, 1.5f, w - 3f, 69f, 12f); inner.raycastTarget = false;
            var btn = border.gameObject.AddComponent<Button>(); btn.targetGraphic = border;
            var cb = btn.colors; cb.highlightedColor = new Color(1.2f, 1.2f, 1.2f); cb.pressedColor = new Color(0.8f, 0.8f, 0.8f); btn.colors = cb;
            btn.onClick.AddListener(() => onClick());
            Icon(inner.transform, icon, Color.white, 22f, 16f, 36f);
            Txt(inner.transform, label, "SemiBold", 20, Color.white, 70f, 22f, w - 80f, 28f, TextAnchor.UpperLeft);
        }

        // ------------------------------------------------------------------ behaviour
        void Update()
        {
            // cover-fit the backdrop for any screen shape
            if (_bg != null && _bg.texture != null && Screen.height > 0)
            {
                float texA = _bg.texture.width / (float)_bg.texture.height, scrA = Screen.width / (float)Screen.height;
                var uv = scrA > texA ? new Rect(0f, (1f - texA / scrA) * 0.5f, 1f, texA / scrA) : new Rect((1f - scrA / texA) * 0.5f, 0f, scrA / texA, 1f);
                if (MirrorBackdrop) uv = new Rect(uv.x + uv.width, uv.y, -uv.width, uv.height);   // negative width flips horizontally
                _bg.uvRect = uv;
            }
            var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            foreach (var kv in _fields)
                if (kv.Key != null && kv.Value != null) kv.Key.color = sel == kv.Value.gameObject ? Brand.Gold : FieldBorder;
            if (_langNote != null && _langNote.text.Length > 0 && Time.unscaledTime > _langNoteUntil) _langNote.text = "";
        }

        static bool EnterPressed() { try { return UnityEngine.Input.GetKeyDown(KeyCode.Return) || UnityEngine.Input.GetKeyDown(KeyCode.KeypadEnter); } catch (InvalidOperationException) { return false; } }

        void SetStatus(string msg, Color c) { if (_status != null) { _status.text = msg; _status.color = c; } else { _pendingInfo = msg; _pendingInfoColor = c; } }

        async void DoSignIn()
        {
            if (_busy) return;
            string e = _email.text.Trim(), p = _pass.text;
            if (e.Length == 0 || p.Length == 0) { SetStatus("Enter your email and password.", Brand.Bad); return; }
            _busy = true; SetStatus("Signing in...", Brand.Gold);
            try
            {
                _svc.Api.RememberSession = _remember;
                var r = await _svc.Api.SignIn(e, p);
                if (!r.Ok) { _busy = false; SetStatus(r.UserMessage, Brand.Bad); return; }
                if (_remember) PlayerPrefs.SetString("aro.last_email", e); else PlayerPrefs.DeleteKey("aro.last_email");
                _busy = false; await _onSuccess();
            }
            catch (Exception ex) { _busy = false; Debug.LogError("[Login] " + ex); SetStatus("Something went wrong. Please try again.", Brand.Bad); }
        }

        async void DoCreate()
        {
            if (_busy) return;
            string n = _name.text.Trim(), e = _email.text.Trim(), p = _pass.text;
            if (n.Length < 2) { SetStatus("Pick a display name (2+ characters).", Brand.Bad); return; }
            if (!e.Contains("@") || !e.Contains(".")) { SetStatus("Enter a valid email address.", Brand.Bad); return; }
            if (p.Length < 8) { SetStatus("Password must be at least 8 characters.", Brand.Bad); return; }
            _busy = true; SetStatus("Creating your account...", Brand.Gold);
            try
            {
                _svc.Api.RememberSession = _remember;
                var r = await _svc.Api.SignUp(e, p, n);
                _busy = false;
                if (r.Ok) { PlayerPrefs.SetString("aro.last_email", e); await _onSuccess(); return; }
                if (r.ErrorCode == "no_session")
                {   // email confirmation is on: the account exists, the player must confirm before signing in
                    PlayerPrefs.SetString("aro.last_email", e);
                    _create = false; _pendingInfo = "Account created. Check your email to confirm it, then sign in."; _pendingInfoColor = Brand.Good; BuildCard(); return;
                }
                SetStatus(r.UserMessage, Brand.Bad);
            }
            catch (Exception ex) { _busy = false; Debug.LogError("[Login] " + ex); SetStatus("Something went wrong. Please try again.", Brand.Bad); }
        }

        async void DoForgot()
        {
            if (_busy) return;
            string e = _email != null ? _email.text.Trim() : "";
            if (e.Length == 0) { SetStatus("Enter your email first, then tap Forgot password.", Brand.Bad); return; }
            _busy = true; SetStatus("Sending recovery email...", Brand.Gold);
            try { var r = await _svc.Api.RecoverPassword(e); _busy = false; SetStatus(r.Ok ? "Recovery email sent. Check your inbox." : r.UserMessage, r.Ok ? Brand.Good : Brand.Bad); }
            catch (Exception ex) { _busy = false; Debug.LogError("[Login] " + ex); SetStatus("Something went wrong. Please try again.", Brand.Bad); }
        }

        async void Oauth(string provider, string title)
        {
            if (_busy) return;
            if (!WebBridge.IsWeb) { SetStatus(title + " sign-in works in the web version of the game.", Brand.Gold); return; }
            _busy = true; SetStatus("Contacting " + title + "...", Brand.Gold);
            try
            {
                bool enabled = await _svc.Api.ProviderEnabled(provider);
                if (!enabled) { _busy = false; SetStatus(title + " sign-in is not enabled for this game yet.", Brand.Bad); return; }
                _svc.Api.RememberSession = _remember;
                WebBridge.Redirect(_svc.Api.OAuthUrl(provider, WebBridge.PageBase));   // the page reloads; Start-up picks the session out of the URL
            }
            catch (Exception ex) { _busy = false; Debug.LogError("[Login] " + ex); SetStatus("Could not reach " + title + ". Please try again.", Brand.Bad); }
        }
    }
}
