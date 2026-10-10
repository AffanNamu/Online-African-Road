using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ARO.Game
{
    /// <summary>
    /// Code-built UI primitives on a 1920x1080 reference "stage". Positions are top-left based (y grows downward) unless an Anchor says otherwise.
    /// Shared by the dashboard (and any new screen); the login screen has its own private copy from before this existed.
    /// </summary>
    public static class Ui
    {
        public enum Anchor { TopLeft, TopRight, BottomLeft, CenterRight }
        static readonly Dictionary<string, Sprite> _grads = new Dictionary<string, Sprite>();

        /// <summary>UV rect from top-left coordinates (RawImage UVs start at the bottom-left).</summary>
        public static Rect Top(float x, float yTop, float w, float h) => new Rect(x, 1f - yTop - h, w, h);

        /// <summary>
        /// UV rect for a crop of `t`: x and yTop (fractions from the top-left), width as a fraction of the texture, and the aspect ratio (w/h) of the frame it will fill,
        /// so the crop is never stretched. The crop is kept inside the texture.
        /// </summary>
        public static Rect Cover(Texture t, float x, float yTop, float w, float frameAspect)
        {
            if (t == null || t.height <= 0 || frameAspect <= 0f) return new Rect(0, 0, 1, 1);
            w = Mathf.Clamp(w, 0.05f, 1f); float h = Mathf.Min(1f, w * t.width / frameAspect / t.height);
            return Top(Mathf.Clamp(x, 0f, 1f - w), Mathf.Clamp(yTop, 0f, 1f - h), w, h);
        }

        public static RectTransform Place(GameObject go, Transform parent, Anchor a, float x, float y, float w, float h)
        {
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            Vector2 anc = a == Anchor.TopLeft ? new Vector2(0, 1) : a == Anchor.TopRight ? new Vector2(1, 1) : a == Anchor.BottomLeft ? new Vector2(0, 0) : new Vector2(1, 0.5f);
            rt.anchorMin = rt.anchorMax = rt.pivot = anc;
            float sx = (a == Anchor.TopRight || a == Anchor.CenterRight) ? -1f : 1f, sy = a == Anchor.BottomLeft ? 1f : (a == Anchor.CenterRight ? 0f : -1f);
            rt.anchoredPosition = new Vector2(x * sx, y * sy); rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static RectTransform Box(Transform parent, string name, float x, float y, float w, float h)
        { var go = new GameObject(name, typeof(RectTransform)); return Place(go, parent, Anchor.TopLeft, x, y, w, h); }

        public static Image Rounded(Transform parent, string name, Color c, float x, float y, float w, float h, float radius, Anchor a = Anchor.TopLeft)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); Place(go, parent, a, x, y, w, h);
            var img = go.GetComponent<Image>(); img.sprite = UIGfx.Glass; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 18f / Mathf.Max(radius, 1f); img.color = c; img.raycastTarget = false;
            return img;
        }

        /// <summary>Rounded box with a border: returns the OUTER (border) image; the inner fill is its first child.</summary>
        public static Image Outlined(Transform parent, string name, Color fill, Color border, float x, float y, float w, float h, float radius, float bw = 1.5f)
        {
            var outer = Rounded(parent, name, border, x, y, w, h, radius);
            Rounded(outer.transform, "Fill", fill, bw, bw, w - bw * 2f, h - bw * 2f, Mathf.Max(radius - bw, 1f));
            return outer;
        }

        public static Image Flat(Transform parent, string name, Color c, float x, float y, float w, float h, Anchor a = Anchor.TopLeft)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); Place(go, parent, a, x, y, w, h);
            var img = go.GetComponent<Image>(); img.color = c; img.raycastTarget = false; return img;
        }

        public static Image Disc(Transform parent, string name, Color c, float x, float y, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); Place(go, parent, Anchor.TopLeft, x, y, size, size);
            var img = go.GetComponent<Image>(); img.sprite = UIGfx.Disc; img.color = c; img.raycastTarget = false; return img;
        }

        public static Image Icon(Transform parent, string icon, Color tint, float x, float y, float size)
        {
            var go = new GameObject("Icon_" + icon, typeof(RectTransform), typeof(Image)); Place(go, parent, Anchor.TopLeft, x, y, size, size);
            var img = go.GetComponent<Image>(); img.sprite = Brand.Icon(icon); img.color = tint; img.preserveAspect = true; img.raycastTarget = false; img.enabled = img.sprite != null; return img;
        }

        public static Text Label(Transform parent, string s, string weight, int size, Color c, float x, float y, float w, float h, TextAnchor al = TextAnchor.UpperLeft)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text)); Place(go, parent, Anchor.TopLeft, x, y, w, h);
            var t = go.GetComponent<Text>(); t.font = Brand.FontOf(weight); t.fontSize = size; t.color = c; t.alignment = al; t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false; t.text = s; return t;
        }

        /// <summary>Wrapping paragraph (fixed width).</summary>
        public static Text Paragraph(Transform parent, string s, string weight, int size, Color c, float x, float y, float w, float h, TextAnchor al = TextAnchor.UpperLeft)
        { var t = Label(parent, s, weight, size, c, x, y, w, h, al); t.horizontalOverflow = HorizontalWrapMode.Wrap; return t; }

        public static void Drop(Text t, float a = 0.55f)
        { var sh = t.gameObject.AddComponent<UnityEngine.UI.Shadow>(); sh.effectColor = new Color(0, 0, 0, a); sh.effectDistance = new Vector2(2f, -2f); }

        /// <summary>Invisible click area. Optionally tints `target` on hover/press (target is darkened on press).</summary>
        public static Button Click(Transform parent, string name, float x, float y, float w, float h, Action onClick)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); Place(go, parent, Anchor.TopLeft, x, y, w, h);
            var img = go.GetComponent<Image>(); img.color = new Color(1, 1, 1, 0.001f);
            var b = go.GetComponent<Button>(); b.targetGraphic = img; b.transition = Selectable.Transition.None; b.onClick.AddListener(() => onClick());
            go.AddComponent<HoverGlow>();
            return b;
        }

        public static Sprite GradientSprite(Color a, Color b, bool horizontal)
        {
            string key = $"{a}|{b}|{horizontal}";
            if (_grads.TryGetValue(key, out var s) && s != null) return s;
            const int n = 128;
            var tex = new Texture2D(horizontal ? n : 1, horizontal ? 1 : n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int i = 0; i < n; i++) { var c = Color.Lerp(a, b, i / (float)(n - 1)); if (horizontal) tex.SetPixel(i, 0, c); else tex.SetPixel(0, n - 1 - i, c); }
            tex.Apply(false, true);
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f); _grads[key] = s; return s;
        }

        public static Image Gradient(Transform parent, string name, Color a, Color b, bool horizontal, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); Place(go, parent, Anchor.TopLeft, x, y, w, h);
            var img = go.GetComponent<Image>(); img.sprite = GradientSprite(a, b, horizontal); img.raycastTarget = false; return img;
        }

        /// <summary>A photo (or crop of one, via uv) clipped to a rounded rectangle. Returns the RawImage.</summary>
        public static RawImage Photo(Transform parent, string name, Texture tex, Rect uv, float x, float y, float w, float h, float radius, Color? tint = null)
        {
            var mask = Rounded(parent, name, Color.white, x, y, w, h, radius);
            mask.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var go = new GameObject("Photo", typeof(RectTransform), typeof(RawImage)); go.transform.SetParent(mask.transform, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var raw = go.GetComponent<RawImage>(); raw.texture = tex; raw.uvRect = uv; raw.color = tint ?? Color.white; raw.raycastTarget = false;
            if (tex == null) raw.color = new Color(0.1f, 0.11f, 0.14f);
            return raw;
        }

        /// <summary>Horizontal progress bar; returns the fill so callers can resize it later via SetBar.</summary>
        public static Image Bar(Transform parent, float x, float y, float w, float h, float fraction, Color fill, Color? track = null)
        {
            Rounded(parent, "BarTrack", track ?? new Color(1, 1, 1, 0.16f), x, y, w, h, h / 2f);
            var f = Rounded(parent, "BarFill", fill, x, y, Mathf.Max(h, w * Mathf.Clamp01(fraction)), h, h / 2f);
            return f;
        }

        /// <summary>A straight line between two points of `parent` (top-left coordinates), drawn as a rotated rectangle.</summary>
        public static Image Segment(Transform parent, Vector2 a, Vector2 b, float thickness, Color c)
        {
            var d = b - a; float len = d.magnitude; if (len < 0.5f) return null;
            var img = Flat(parent, "Seg", c, 0, 0, len, thickness);
            var rt = img.rectTransform; rt.pivot = new Vector2(0f, 0.5f); rt.anchoredPosition = new Vector2(a.x, -a.y);
            rt.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            return img;
        }
    }

    /// <summary>Subtle hover feedback for click areas: brightens the sibling-visible card by raising its parent's CanvasGroup-free tint.</summary>
    public sealed class HoverGlow : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler, UnityEngine.EventSystems.IPointerDownHandler, UnityEngine.EventSystems.IPointerUpHandler
    {
        Image _glow; bool _over;
        void Awake()
        {
            var go = new GameObject("Glow", typeof(RectTransform), typeof(Image)); go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            _glow = go.GetComponent<Image>(); _glow.sprite = UIGfx.Glass; _glow.type = Image.Type.Sliced; _glow.pixelsPerUnitMultiplier = 1.2f; _glow.color = new Color(1, 1, 1, 0f); _glow.raycastTarget = false;
        }
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) { _over = true; if (_glow != null) _glow.color = new Color(1f, 0.85f, 0.4f, 0.10f); }
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) { _over = false; if (_glow != null) _glow.color = new Color(1, 1, 1, 0f); }
        // press feedback: a stronger flash that settles back to the hover state
        public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e) { if (_glow != null) _glow.color = new Color(1f, 0.85f, 0.4f, 0.26f); }
        public void OnPointerUp(UnityEngine.EventSystems.PointerEventData e) { if (_glow != null) _glow.color = _over ? new Color(1f, 0.85f, 0.4f, 0.10f) : new Color(1, 1, 1, 0f); }
        void OnDisable() { if (_glow != null) _glow.color = new Color(1, 1, 1, 0f); }
    }

    /// <summary>Keeps a 1920x1080 stage letterboxed inside whatever the canvas size is (any window shape).</summary>
    public sealed class StageFitter : MonoBehaviour
    {
        RectTransform _stage, _parent; Vector2 _last;
        void LateUpdate()
        {
            // The parent is resolved lazily: Awake runs while the stage is being created, before it has been parented (that was a per-frame NullReferenceException).
            if (_parent == null) { _stage = (RectTransform)transform; _parent = transform.parent as RectTransform; if (_parent == null) return; }
            var size = _parent.rect.size; if (size == _last || size.x < 1f) return; _last = size;
            _stage.localScale = Vector3.one * Mathf.Min(size.x / 1920f, size.y / 1080f);
        }
    }
}
