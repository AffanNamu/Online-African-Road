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
        readonly HashSet<int> _hooked = new HashSet<int>();
        readonly Dictionary<int, int> _seen = new Dictionary<int, int>();   // button instance id -> frame it was first seen
        int FirstSeen(Button b) => _seen.TryGetValue(b.GetInstanceID(), out int f) ? f : Time.frameCount;   // buttons that already log their onClick (browser end-to-end runs only)

        void Start() { _started = Time.realtimeSinceStartup; }

        void Update()
        {
            float t = Time.realtimeSinceStartup - _started;
            if (!_targetsLogged && t > 4f) { _targetsLogged = true; LogTargets(); }
            if (t > _nextStatus && t < 40f) { _nextStatus += 6f; LogStatus(); }
            // The project uses the Input System only (the legacy Input below throws and is ignored), so presses are read from the Input System. Browser end-to-end runs only: every press and release the
            // game actually receives, where it was, which UI elements were under it, and the frame time (a press lost between two frames never shows up here, which is the evidence the tests need).
            if (SmokeMode.E2E)
            {
                HookButtons();
                try
                {
                    var ms = UnityEngine.InputSystem.Mouse.current;
                    if (ms != null)
                    {
                        if (ms.leftButton.wasPressedThisFrame) LogPointer("pressed", ms.position.ReadValue());
                        if (ms.leftButton.wasReleasedThisFrame) LogPointer("released", ms.position.ReadValue());
                    }
                }
                catch (System.Exception) { /* diagnostics must never break the game */ }
            }
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

        /// <summary>
        /// Browser end-to-end runs only: every button logs "[UIDiag] onClick name" when Unity UI actually invokes it. With the press line this separates "the press never reached the button" (press logged, no onClick)
        /// from "the button fired but its handler did nothing" (onClick logged, no effect). The listener is added after the game's own, so it never changes what a click does.
        /// </summary>
        void HookButtons()
        {
            foreach (var b in FindObjectsByType<Button>(FindObjectsSortMode.None))
            {
                if (!_hooked.Add(b.GetInstanceID())) continue;
                _seen[b.GetInstanceID()] = Time.frameCount;
                string n = b.name;
                b.onClick.AddListener(() => Debug.Log($"[UIDiag] onClick {n} interactable=true frame={(Time.unscaledDeltaTime * 1000f):0}ms t={Time.realtimeSinceStartup:0.00}"));
            }
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

        void LogPointer(string what, Vector2 pos)
        {
            _hits.Clear();
            if (EventSystem.current != null) EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = pos }, _hits);
            var sb = new StringBuilder($"[UIDiag] pointer {what} at {pos.x:0},{pos.y:0} of {Screen.width}x{Screen.height} frame={(Time.unscaledDeltaTime * 1000f):0}ms t={Time.realtimeSinceStartup:0.00}; ui hits=[");
            foreach (var h in _hits) sb.Append(h.gameObject.name).Append(' ');
            sb.Append("]");
            if (what == "pressed")
            {   // which buttons EXIST under the pointer, whether or not anything raycasts to them: "hits empty and no button here" means the UI was not there (a loading state or a rebuild), "button here but no hit" means something blocks it
                sb.Append("; buttons under pointer=[");
                int total = 0;
                foreach (var b in FindObjectsByType<Button>(FindObjectsSortMode.None))
                {
                    if (!b.gameObject.activeInHierarchy) continue; total++;
                    if (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)b.transform, pos, null)) sb.Append(b.name).Append(b.interactable ? "" : "(not interactable)").Append(' ');
                }
                sb.Append("] active buttons=").Append(total);
                // A button under the pointer that nothing raycast to: say why (a graphic that has not been drawn yet has depth -1 and is never hit; a CanvasGroup can switch raycasts off; a canvas or its raycaster can be disabled)
                foreach (var b in FindObjectsByType<Button>(FindObjectsSortMode.None))
                {
                    if (!b.gameObject.activeInHierarchy || !RectTransformUtility.RectangleContainsScreenPoint((RectTransform)b.transform, pos, null)) continue;
                    bool hit = false; foreach (var h in _hits) if (h.gameObject == b.gameObject) { hit = true; break; }
                    if (hit) continue;
                    var g = b.targetGraphic; var cv = b.GetComponentInParent<Canvas>(); var gr = cv != null ? cv.GetComponent<GraphicRaycaster>() : null;
                    sb.Append($"; WHY-NOT-HIT {b.name}: graphic={(g != null ? g.GetType().Name : "none")} raycastTarget={(g != null && g.raycastTarget)} depth={(g != null ? g.depth : -99)} cull={(g != null && g.canvasRenderer.cull)} alpha={(g != null ? g.canvasRenderer.GetInheritedAlpha() : -1f):0.000} canvasEnabled={(cv != null && cv.enabled)} raycasterEnabled={(gr != null && gr.enabled)} groups=[");
                    foreach (var cg in b.GetComponentsInParent<CanvasGroup>()) sb.Append(cg.name).Append("(blocks=").Append(cg.blocksRaycasts).Append(",alpha=").Append(cg.alpha.ToString("0.00")).Append(") ");
                    sb.Append("] age=").Append(((Time.frameCount - FirstSeen(b)) ).ToString()).Append(" frames");
                }
            }
            Debug.Log(sb.ToString());
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
