using System.Collections.Generic;

namespace ARO.NetCore
{
    /// <summary>
    /// Decides whether a background reload needs to redraw a page. The dashboard redraws a region by destroying its buttons and creating new ones, and Unity UI only sends a click when the button
    /// that took the press is still the one under the pointer at release, so a redraw that lands between press and release loses the click. A reload that brought nothing new must therefore not
    /// redraw. Each page keeps a key describing everything its data lists contributed to the last drawing; a reload redraws only when the key differs.
    /// An unknown key (null) always means "redraw", so a key that cannot be built can never leave a stale screen.
    /// </summary>
    public sealed class RefreshGate
    {
        readonly Dictionary<string, string> _drawn = new Dictionary<string, string>();

        /// <summary>True when the page must be redrawn for this key; the key is remembered as drawn. False when it is the key already on screen.</summary>
        public bool Changed(string scope, string key)
        {
            if (key == null) { _drawn.Remove(scope); return true; }
            if (_drawn.TryGetValue(scope, out var last) && last == key) return false;
            _drawn[scope] = key; return true;
        }

        /// <summary>Records what a redraw (made for another reason, such as a user action) has just put on screen.</summary>
        public void Drew(string scope, string key) { if (key == null) _drawn.Remove(scope); else _drawn[scope] = key; }

        /// <summary>Forgets a page (it was closed or rebuilt from scratch): the next reload redraws.</summary>
        public void Forget(string scope) => _drawn.Remove(scope);

        /// <summary>Joins the parts of a key; any null part makes the whole key unknown.</summary>
        public static string Key(params string[] parts)
        {
            foreach (var p in parts) if (p == null) return null;
            return string.Join("\u001f", parts);
        }
    }
}
