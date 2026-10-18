using System.Globalization;
using ARO.NetCore;
using UnityEngine;

namespace ARO.Game
{
    /// <summary>
    /// Whether the player has a pointer pressed right now (mouse button or finger), read from the Input System, which is updated before any Update runs in the frame. The dashboard holds back a needed
    /// background redraw while this is true (see RefreshDeferral), because destroying the button under a pressed pointer loses the click.
    /// Browser end-to-end pages (loopback with autologin) may switch the guard off ("pointerguard=off", to show what happens without it) or lengthen its cap ("pointerhold=seconds"); nothing else can.
    /// </summary>
    public static class PointerGuard
    {
        static string _off, _hold; static bool _read;
        static void ReadOnce()
        {
            if (_read) return; _read = true;
            if (SmokeMode.E2E) { _off = BackendOverride.Query(Application.absoluteURL, "pointerguard"); _hold = BackendOverride.Query(Application.absoluteURL, "pointerhold"); }
        }

        public static bool Enabled { get { ReadOnce(); return _off != "off"; } }

        /// <summary>How long one press can hold redraws back; a press reported down for longer than this stops holding (the screen must not stay stale).</summary>
        public static float MaxHold { get { ReadOnce(); return float.TryParse(_hold, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && f > 0f ? f : 3f; } }

        static int _frame = -1; static bool _down;
        public static bool Down
        {
            get
            {
                if (_frame == Time.frameCount) return _down;
                _frame = Time.frameCount; _down = false;
                try
                {
                    var m = UnityEngine.InputSystem.Mouse.current; if (m != null && m.leftButton.isPressed) _down = true;
                    var t = UnityEngine.InputSystem.Touchscreen.current; if (t != null && t.primaryTouch.press.isPressed) _down = true;
                }
                catch (System.Exception) { /* no pointer device: nothing is pressed */ }
                return _down;
            }
        }
    }
}
