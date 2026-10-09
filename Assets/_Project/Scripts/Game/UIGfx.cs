using UnityEngine;
using UnityEngine.UI;

namespace ARO.Game
{
    /// <summary>
    /// Procedural UI sprites (anti-aliased signed-distance shapes) so the HUD needs no art assets and scales crisply.
    /// Sprites are cached for the process lifetime.
    /// </summary>
    public static class UIGfx
    {
        static Sprite _glass, _ring, _disc, _pill;

        static Sprite Make(int size, System.Func<float, float, float> sdf, Vector4 border)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = sdf(x + 0.5f, y + 0.5f);                        // signed distance in pixels, <0 inside
                    byte a = (byte)(Mathf.Clamp01(0.5f - d) * 255f);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }

        static float RoundedRectSdf(float x, float y, float size, float radius)
        {
            float c = size * 0.5f, qx = Mathf.Abs(x - c) - (c - radius), qy = Mathf.Abs(y - c) - (c - radius);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        /// <summary>9-sliced rounded rectangle (use Image.Type.Sliced).</summary>
        public static Sprite Glass => _glass != null ? _glass : (_glass = Make(64, (x, y) => RoundedRectSdf(x, y, 64f, 18f), new Vector4(20, 20, 20, 20)));
        public static Sprite Pill => _pill != null ? _pill : (_pill = Make(64, (x, y) => RoundedRectSdf(x, y, 64f, 31f), new Vector4(31, 31, 31, 31)));
        public static Sprite Disc => _disc != null ? _disc : (_disc = Make(128, (x, y) => new Vector2(x - 64f, y - 64f).magnitude - 63f, Vector4.zero));
        /// <summary>Thin ring for radial-filled gauges (Image.Type.Filled).</summary>
        public static Sprite Ring => _ring != null ? _ring : (_ring = Make(256, (x, y) =>
            Mathf.Abs(new Vector2(x - 128f, y - 128f).magnitude - 118f) - 10f, Vector4.zero));

        public static Image Panel(Transform parent, string name, Color tint, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax, bool pill = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
            var img = go.GetComponent<Image>(); img.sprite = pill ? Pill : Glass; img.type = Image.Type.Sliced; img.color = tint; img.raycastTarget = false;
            return img;
        }

        public static Image Shape(Transform parent, string name, Sprite sprite, Color tint, Vector2 centre, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.anchoredPosition = centre; rt.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<Image>(); img.sprite = sprite; img.color = tint; img.raycastTarget = false; return img;
        }
    }
}
