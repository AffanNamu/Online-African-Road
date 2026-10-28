using System.Globalization;
using ARO.NetCore;
using UnityEngine;

namespace ARO.Game
{
    /// <summary>
    /// Whether the player has a pointer pressed right now or pressed it during this frame (mouse button or finger), read from the Input System, which is updated before any Update runs in the frame. The dashboard holds back a needed
    /// background redraw while this is true (see RefreshDeferral), because destroying the button under a pressed pointer loses the click.
    /// Browser end-to-end pages (loopback with autologin) may switch the guard off ("pointerguard=off", to show what happens without it) or lengthen its cap ("pointerhold=seconds"); nothing else can.
    /// </summary>
    public static class PointerGuard
    {
        static string _off, _hold, _frameEnd; static bool _read;
        static void ReadOnce()
        {
            if (_read) return; _read = true;
            if (SmokeMode.E2E) { _off = BackendOverride.Query(Application.absoluteURL, "pointerguard"); _hold = BackendOverride.Query(Application.absoluteURL, "pointerhold"); _frameEnd = BackendOverride.Query(Application.absoluteURL, "frameend"); }
        }

        public static bool Enabled { get { ReadOnce(); return _off != "off"; } }

        /// <summary>Background redraws run at the end of the frame (RedrawQueue). Browser end-to-end pages may switch it off ("frameend=off") to show what happens without it; nothing else can.</summary>
        public static bool QueueAtFrameEnd { get { ReadOnce(); return _frameEnd != "off"; } }

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
                    // pressed now, or pressed during this frame (a quick click can press and release inside one input update, and the event system may only finish it next frame)
                    var m = UnityEngine.InputSystem.Mouse.current; if (m != null && (m.leftButton.isPressed || m.leftButton.wasPressedThisFrame)) _down = true;
                    var t = UnityEngine.InputSystem.Touchscreen.current; if (t != null && (t.primaryTouch.press.isPressed || t.primaryTouch.press.wasPressedThisFrame)) _down = true;
                }
                catch (System.Exception) { /* no pointer device: nothing is pressed */ }
                return _down;
            }
        }
    }
}
