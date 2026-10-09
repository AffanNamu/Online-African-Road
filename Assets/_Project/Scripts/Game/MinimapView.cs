using ARO.NetCore;
using UnityEngine;
using UnityEngine.UI;

namespace ARO.Game
{
    /// <summary>
    /// Circular heading-up minimap: a small orthographic top-down camera rendered to a texture at a few Hz (cheap),
    /// masked to a disc, with the destination marker projected using the unit-tested MinimapMath.
    /// </summary>
    public class MinimapView : MonoBehaviour
    {
        public float radiusMetres = 220f;
        public float refreshHz = 8f;

        Camera _cam; RenderTexture _rt; RectTransform _marker; Image _markerImg; Transform _target; float _next;
        bool _enabled = true;

        public void Build(Transform parent, Vector2 centre, float size)
        {
            // Circular mask + render texture + player arrow + destination marker.
            var root = UIGfx.Shape(parent, "Minimap", UIGfx.Disc, new Color(0.04f, 0.05f, 0.06f, 0.92f), centre, size);
            var mask = root.gameObject.AddComponent<Mask>(); mask.showMaskGraphic = true;

            _rt = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32) { name = "MinimapRT", antiAliasing = 1 };
            var raw = new GameObject("Map", typeof(RectTransform), typeof(RawImage)); raw.transform.SetParent(root.transform, false);
            var rt = (RectTransform)raw.transform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var ri = raw.GetComponent<RawImage>(); ri.texture = _rt; ri.raycastTarget = false; ri.color = new Color(1, 1, 1, 0.95f);

            var ring = UIGfx.Shape(root.transform, "Rim", UIGfx.Ring, new Color(1f, 1f, 1f, 0.22f), Vector2.zero, size);
            ring.transform.SetAsLastSibling();
            var arrow = UIGfx.Shape(root.transform, "You", UIGfx.Disc, UIKit.Accent, Vector2.zero, size * 0.07f);
            arrow.transform.SetAsLastSibling();
            _markerImg = UIGfx.Shape(root.transform, "Dest", UIGfx.Disc, UIKit.Good, Vector2.zero, size * 0.075f);
            _marker = (RectTransform)_markerImg.transform; _markerImg.gameObject.SetActive(false);

            var camGo = new GameObject("MinimapCamera"); camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic = true; _cam.orthographicSize = radiusMetres; _cam.targetTexture = _rt; _cam.enabled = false;
            _cam.clearFlags = CameraClearFlags.SolidColor; _cam.backgroundColor = new Color(0.08f, 0.10f, 0.09f);
            _cam.allowHDR = false; _cam.allowMSAA = false; _cam.cullingMask = ~(1 << 5); _cam.farClipPlane = 800f;
            camGo.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        }

        public void SetTarget(Transform t) => _target = t;
        public void SetVisible(bool v) { _enabled = v; if (_cam != null) { _cam.gameObject.SetActive(v); } }

        /// <summary>Update the camera at refreshHz and place the destination marker. destination==null hides the marker.</summary>
        public void Tick(Vector3? destination)
        {
            if (!_enabled || _cam == null || _target == null) return;
            if (Time.unscaledTime >= _next)
            {
                _next = Time.unscaledTime + 1f / refreshHz;
                var p = _target.position;
                _cam.transform.position = new Vector3(p.x, p.y + 300f, p.z);
                _cam.transform.rotation = Quaternion.Euler(90f, _target.eulerAngles.y, 0f);   // heading-up
                _cam.Render();
            }
            if (destination.HasValue)
            {
                var d = destination.Value; var p = _target.position;
                bool inside = MinimapMath.ToMap(d.x, d.z, p.x, p.z, _target.eulerAngles.y, radiusMetres, true, out float mx, out float my);
                if (!inside) MinimapMath.ClampToEdge(ref mx, ref my);
                var half = ((RectTransform)_marker.parent).sizeDelta.x * 0.5f;
                _marker.anchoredPosition = new Vector2(mx * half, my * half);
                _markerImg.color = inside ? UIKit.Good : UIKit.Accent;
                _markerImg.gameObject.SetActive(true);
            }
            else _markerImg.gameObject.SetActive(false);
        }

        void OnDestroy() { if (_rt != null) _rt.Release(); }
    }
}
