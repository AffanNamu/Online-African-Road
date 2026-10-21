using System;
using System.Collections.Generic;

namespace ARO.NetCore
{
    /// <summary>
    /// Holds back a redraw that a background reload asks for while the player has a pointer pressed. The dashboard redraws a region by destroying its buttons and creating new ones, and Unity UI only
    /// sends a click when the button that took the press is still the one under the pointer at release, so a redraw between press and release loses the click (see RefreshGate for the redraws that
    /// are avoided altogether; this is for the ones that are needed). A held redraw is applied as soon as the pointer is released. A press that lasts longer than the cap (a pointer that never reports
    /// its release, a very long drag) stops holding, so a screen can never stay stale. Per key the newest request wins: the redraw builds from the current data when it finally runs.
    /// </summary>
    public sealed class RefreshDeferral
    {
        readonly float _maxHold;
        float _downSince = -1f;
        readonly List<string> _order = new List<string>();
        readonly Dictionary<string, Action> _pending = new Dictionary<string, Action>();

        public RefreshDeferral(float maxHoldSeconds = 3f) { _maxHold = maxHoldSeconds; }

        public int PendingCount => _pending.Count;

        /// <summary>True while a pointer is down and has been for less than the cap.</summary>
        public bool Holding(bool pointerDown, float now)
        {
            if (!pointerDown) _downSince = -1f; else if (_downSince < 0f) _downSince = now;
            return pointerDown && now - _downSince < _maxHold;
        }

        /// <summary>True when the redraw was held back (it runs later from <see cref="Flush"/>); false when it must run now.</summary>
        public bool Defer(string key, Action redraw, bool pointerDown, float now)
        {
            if (!Holding(pointerDown, now)) return false;
            if (!_pending.ContainsKey(key)) _order.Add(key);
            _pending[key] = redraw; return true;
        }

        /// <summary>A redraw for this key is happening now for another reason: the held one would only repeat it.</summary>
        public void Cancel(string key) { if (_pending.Remove(key)) _order.Remove(key); }

        public void Clear() { _pending.Clear(); _order.Clear(); }

        /// <summary>Runs the held redraws (oldest key first) once nothing is holding any more. Returns how many ran.</summary>
        public int Flush(bool pointerDown, float now)
        {
            if (_pending.Count == 0 || Holding(pointerDown, now)) return 0;
            var keys = _order.ToArray(); var run = new Action[keys.Length];
            for (int i = 0; i < keys.Length; i++) run[i] = _pending[keys[i]];
            Clear();
            foreach (var a in run) a();
            return run.Length;
        }
    }
}
