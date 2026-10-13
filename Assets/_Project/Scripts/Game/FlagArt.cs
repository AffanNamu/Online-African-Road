using System.Collections.Generic;
using ARO.NetCore;
using UnityEngine;

namespace ARO.Game
{
    /// <summary>
    /// Loads the national-flag sprites (Resources/Flags/{iso2 lower case}.png, 96x72, flag-icons, MIT). The code-to-file mapping is ARO.NetCore.CountryFlags, the only one:
    /// this class never looks at anything but the stored country code. Null means "draw the neutral placeholder" (no country, a malformed value, or no artwork shipped).
    /// </summary>
    public static class FlagArt
    {
        static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();

        public static Sprite Get(string code)
        {
            string path = CountryFlags.ResourcePath(code); if (path == null) return null;
            if (_sprites.TryGetValue(path, out var s) && s != null) return s;
            var tex = Resources.Load<Texture2D>(path);
            if (tex == null) { Debug.LogWarning("[Flags] missing " + path); return null; }
            tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            _sprites[path] = s; return s;
        }
    }
}
