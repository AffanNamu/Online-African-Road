using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ARO.Game
{
    /// <summary>Code-built uGUI helpers with one dark, high-contrast theme (no prefab dependencies).</summary>
    public static class UIKit
    {
        public static readonly Color Bg = new Color(0.06f, 0.07f, 0.09f, 0.94f);
        public static readonly Color Panel = new Color(0.11f, 0.13f, 0.16f, 0.96f);
        public static readonly Color Accent = new Color(0.98f, 0.70f, 0.13f, 1f);   // road-marking amber
        public static readonly Color Good = new Color(0.30f, 0.80f, 0.45f, 1f);
        public static readonly Color Bad = new Color(0.95f, 0.35f, 0.30f, 1f);
        public static readonly Color TextCol = new Color(0.93f, 0.94f, 0.96f, 1f);
        public static readonly Color Muted = new Color(0.60f, 0.64f, 0.70f, 1f);
        static Font _font;
        public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static Canvas CreateCanvas(string name, int order)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = order;
            var s = go.GetComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; s.referenceResolution = new Vector2(1920, 1080); s.matchWidthOrHeight = 0.5f;
            if (Object.FindFirstObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            return c;
        }

        public static RectTransform Box(Transform parent, string name, Color col, Vector2 anchorMin, Vector2 anchorMax, Vector2 offMin, Vector2 offMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.offsetMin = offMin; rt.offsetMax = offMax;
            go.GetComponent<Image>().color = col; return rt;
        }

        public static Text Label(Transform parent, string text, int size, Color col, TextAnchor anchor, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
            var t = go.GetComponent<Text>(); t.font = Font; t.text = text; t.fontSize = size; t.color = col; t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false;
            return t;
        }

        public static Button Btn(Transform parent, string text, System.Action onClick, bool primary = true, float h = 56f)
        {
            var go = new GameObject("Btn_" + text, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().preferredHeight = h;
            var img = go.GetComponent<Image>(); img.color = primary ? Accent : new Color(0.2f, 0.23f, 0.28f, 1f);
            var b = go.GetComponent<Button>(); b.targetGraphic = img;
            var cb = b.colors; cb.highlightedColor = new Color(1f, 1f, 1f, 0.85f); cb.pressedColor = new Color(0.8f, 0.8f, 0.8f); b.colors = cb;
            b.onClick.AddListener(() => onClick());
            Label(go.transform, text, 24, primary ? Color.black : TextCol, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return b;
        }

        public static InputField Input(Transform parent, string placeholder, bool password = false)
        {
            var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
            go.transform.SetParent(parent, false); go.GetComponent<LayoutElement>().preferredHeight = 52f;
            go.GetComponent<Image>().color = new Color(0.16f, 0.19f, 0.23f, 1f);
            var f = go.GetComponent<InputField>();
            var txt = Label(go.transform, "", 24, TextCol, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, new Vector2(14, 0), new Vector2(-14, 0));
            var ph = Label(go.transform, placeholder, 24, Muted, TextAnchor.MiddleLeft, Vector2.zero, Vector2.one, new Vector2(14, 0), new Vector2(-14, 0));
            f.textComponent = txt; f.placeholder = ph;
            if (password) f.contentType = InputField.ContentType.Password;
            return f;
        }

        public static RectTransform VStack(Transform parent, string name, Vector2 size, float spacing = 12f, int pad = 0)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup)); go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform; rt.sizeDelta = size; rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            var v = go.GetComponent<VerticalLayoutGroup>(); v.spacing = spacing; v.padding = new RectOffset(pad, pad, pad, pad);
            v.childControlHeight = true; v.childControlWidth = true; v.childForceExpandHeight = false; v.childForceExpandWidth = true;
            return rt;
        }

        public static void Clear(Transform t) { for (int i = t.childCount - 1; i >= 0; i--) Object.Destroy(t.GetChild(i).gameObject); }
    }
}
