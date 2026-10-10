using System.Collections.Generic;
using UnityEngine;

namespace ARO.Game
{
    /// <summary>Brand colours, the bundled Montserrat font weights and the icon sprites (Resources/UI). Falls back gracefully if an asset is missing.</summary>
    public static class Brand
    {
        public static readonly Color Gold = new Color(0.976f, 0.710f, 0.129f, 1f);
        public static readonly Color Ink = new Color(0.07f, 0.065f, 0.06f, 1f);
        public static readonly Color Good = new Color(0.36f, 0.82f, 0.52f, 1f);
        public static readonly Color Bad = new Color(0.97f, 0.42f, 0.38f, 1f);
        public static readonly Color Muted = new Color(0.72f, 0.74f, 0.78f, 1f);

        static readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();
        static readonly Dictionary<string, Sprite> _icons = new Dictionary<string, Sprite>();

        /// <summary>weight: Regular, SemiBold, Bold, ExtraBold.</summary>
        public static Font FontOf(string weight)
        {
            if (_fonts.TryGetValue(weight, out var f) && f != null) return f;
            f = Resources.Load<Font>("UI/Fonts/Montserrat-" + weight) ?? UIKit.Font;
            _fonts[weight] = f; return f;
        }

        /// <summary>Icon from Resources/UI/{name}.png (white glyphs are tinted via Image.color). Null if absent.</summary>
        public static Sprite Icon(string name)
        {
            if (_icons.TryGetValue(name, out var s) && s != null) return s;
            var tex = Resources.Load<Texture2D>("UI/" + name);
            if (tex == null) { Debug.LogWarning("[Brand] missing icon UI/" + name); return null; }
            tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            _icons[name] = s; return s;
        }

        public static Texture2D Texture(string name) => Resources.Load<Texture2D>("UI/" + name);
    }
}
