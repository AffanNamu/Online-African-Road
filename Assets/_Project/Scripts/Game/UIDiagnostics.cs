using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ARO.Game
{
    /// <summary>
    /// Logs why UI input might be dead (visible in the browser console and in the CI browser smoke test).
    /// Prints the screen position of every button/input field once, then the EventSystem state and what a mouse press hits.
    /// </summary>
    public sealed class UIDiagnostics : MonoBehaviour
    {
        float _nextStatus = 3f, _started;
        bool _targetsLogged;
        readonly List<RaycastResult> _hits = new List<RaycastResult>();

        void Start() { _started = Time.realtimeSinceStartup; }

        void Update()
        {
            float t = Time.realtimeSinceStartup - _started;
            if (!_targetsLogged && t > 4f) { _targetsLogged = true; LogTargets(); }
            if (t > _nextStatus && t < 40f) { _nextStatus += 6f; LogStatus(); }
            try
            {
                if (UnityEngine.Input.GetMouseButtonDown(0)) LogPress(UnityEngine.Input.mousePosition);
                if (UnityEngine.Input.anyKeyDown)
                {
                    var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                    Debug.Log($"[UIDiag] key pressed; selected={(sel != null ? sel.name : "none")}");
                }
            }
            catch (InvalidOperationException) { /* legacy Input disabled by the project's input setting */ }
        }

        static void LogTargets()
        {
            var sb = new StringBuilder("[UIDiag] targets (fraction of screen, origin top-left): screen=" + Screen.width + "x" + Screen.height);
            var corners = new Vector3[4];
            foreach (var sel in FindObjectsByType<Selectable>(FindObjectsSortMode.None))
            {
                if (!sel.isActiveAndEnabled) continue;
                ((RectTransform)sel.transform).GetWorldCorners(corners);   // overlay canvas: world == screen pixels
                float cx = (corners[0].x + corners[2].x) * 0.5f / Screen.width;
                float cy = 1f - (corners[0].y + corners[2].y) * 0.5f / Screen.height;
                sb.Append($"\n  TARGET {sel.GetType().Name} '{sel.name}' x={cx:0.000} y={cy:0.000}");
            }
            Debug.Log(sb.ToString());
        }

        static void LogStatus()
        {
            var es = EventSystem.current;
            var mod = es != null ? es.currentInputModule : null;
            string mouse;
            try { mouse = UnityEngine.Input.mousePresent.ToString(); } catch (InvalidOperationException) { mouse = "legacy-input-disabled"; }
            string backends = "";
#if ENABLE_INPUT_SYSTEM
            backends += "InputSystem ";
            backends += $"(mouse={UnityEngine.InputSystem.Mouse.current != null}, keyboard={UnityEngine.InputSystem.Keyboard.current != null}) ";
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            backends += "LegacyInput";
#endif
            Debug.Log($"[UIDiag] backends compiled: {backends}");
            Debug.Log($"[UIDiag] status: eventSystems={FindObjectsByType<EventSystem>(FindObjectsSortMode.None).Length} " +
                      $"module={(mod != null ? mod.GetType().Name : "none")} mousePresent={mouse} " +
                      $"selected={(es != null && es.currentSelectedGameObject != null ? es.currentSelectedGameObject.name : "none")} " +
                      $"fps={(1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f)):0} focused={Application.isFocused}");
        }

        void LogPress(Vector2 pos)
        {
            _hits.Clear();
            if (EventSystem.current != null)
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = pos }, _hits);
            var sb = new StringBuilder($"[UIDiag] mouse pressed at {pos.x:0},{pos.y:0} of {Screen.width}x{Screen.height}; ui hits=[");
            foreach (var h in _hits) sb.Append(h.gameObject.name).Append(' ');
            sb.Append("]");
            Debug.Log(sb.ToString());
        }
    }
}
