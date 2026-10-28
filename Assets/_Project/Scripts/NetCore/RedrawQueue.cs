using System;
using System.Collections.Generic;

namespace ARO.NetCore
{
    /// <summary>
    /// Collects the redraws that background work asks for during a frame so they can all run at the END of that frame, after the event system has handled the frame's input.
    /// Why: a redraw destroys a region's buttons and creates new ones, and Unity UI only raycasts to a graphic once its canvas has been drawn once (its depth is -1 until then). Server replies
    /// (async continuations) run BEFORE the event system in the frame's update; a press that arrives in that same frame is processed against buttons that were created a moment earlier and are
    /// not hit-testable yet, so the click is lost. Built at the end of the frame, the new buttons are drawn before the next frame's input is processed. Per key the newest request wins (the redraw
    /// builds from the current data when it runs), and keys run oldest first.
    /// </summary>
    public sealed class RedrawQueue
    {
        readonly List<string> _order = new List<string>();
        readonly Dictionary<string, Action> _pending = new Dictionary<string, Action>();

        public int Count => _pending.Count;

        public void Add(string key, Action redraw)
        {
            if (!_pending.ContainsKey(key)) _order.Add(key);
            _pending[key] = redraw;
        }

        /// <summary>A redraw for this key is happening now for another reason (a click's own redraw): the queued one would only repeat it.</summary>
        public void Cancel(string key) { if (_pending.Remove(key)) _order.Remove(key); }

        public void Clear() { _pending.Clear(); _order.Clear(); }

        /// <summary>Returns the queued redraws, oldest key first, and empties the queue. Redraws queued while the returned ones run belong to the next frame.</summary>
        public Action[] Take()
        {
            var run = new Action[_order.Count];
            for (int i = 0; i < run.Length; i++) run[i] = _pending[_order[i]];
            Clear();
            return run;
        }
    }
}
