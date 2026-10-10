using ARO.NetCore;
using UnityEngine;
using UnityEngine.UI;

namespace ARO.Game
{
    /// <summary>
    /// Small, allocation-free UI animations driven by unscaled time (they keep working while the game is paused behind a menu). All easing and timing
    /// maths lives in NetCore (Ease, Tween) and is unit-tested; these components only apply it. Append ?reduce_motion=1 to the URL to switch motion off.
    /// </summary>
    public static class UiMotion
    {
        public static bool Enabled { get { var u = Application.absoluteURL; return u == null || !u.Contains("reduce_motion=1"); } }
    }

    /// <summary>Fades a region in while it rises into place. Starts hidden so there is no flash before the delay.</summary>
    public sealed class Entrance : MonoBehaviour
    {
        CanvasGroup _cg; RectTransform _rt; Vector2 _home; float _t0, _delay, _dur, _rise; bool _done;

        public static Entrance On(RectTransform rt, float delay, float duration = 0.55f, float rise = 22f)
        {
            if (!UiMotion.Enabled) return null;
            var e = rt.gameObject.AddComponent<Entrance>(); e._rt = rt; e._home = rt.anchoredPosition; e._delay = delay; e._dur = duration; e._rise = rise; e._t0 = Time.unscaledTime;
            e._cg = rt.GetComponent<CanvasGroup>() ?? rt.gameObject.AddComponent<CanvasGroup>(); e._cg.alpha = 0f; e.Apply(0f);
            return e;
        }

        void Apply(float p) { float k = Ease.OutCubic(p); _cg.alpha = Mathf.Clamp01(p * 1.4f); _rt.anchoredPosition = _home + new Vector2(0f, -_rise * (1f - k)); }

        void Update()
        {
            if (_done) return;
            float p = Tween.Progress(Time.unscaledTime - _t0, _delay, _dur); Apply(p);
            if (p >= 1f) { _done = true; _cg.alpha = 1f; _rt.anchoredPosition = _home; Destroy(this); }
        }
    }

    /// <summary>Animates a number from one value to another (balance, XP, distance).</summary>
    public sealed class CountText : MonoBehaviour
    {
        Text _t; long _from, _to; float _t0, _delay, _dur; string _pre, _post; bool _done;
        public static void Run(Text t, long from, long to, string prefix = "", string suffix = "", float delay = 0.25f, float duration = 0.9f)
        {
            string fin = prefix + Money.Format(to) + suffix;
            if (!UiMotion.Enabled || from == to) { t.text = fin; return; }
            var c = t.gameObject.AddComponent<CountText>(); c._t = t; c._from = from; c._to = to; c._pre = prefix; c._post = suffix; c._delay = delay; c._dur = duration; c._t0 = Time.unscaledTime; t.text = prefix + Money.Format(from) + suffix;
        }
        void Update()
        {
            if (_done || _t == null) return;
            float p = Tween.Progress(Time.unscaledTime - _t0, _delay, _dur);
            _t.text = _pre + Money.Format(Tween.CountUp(_from, _to, p)) + _post;
            if (p >= 1f) { _done = true; Destroy(this); }
        }
    }

    /// <summary>Grows a progress-bar fill to its target width.</summary>
    public sealed class BarGrow : MonoBehaviour
    {
        RectTransform _rt; float _min, _target, _t0, _delay, _dur; bool _done;
        public static void Run(Image fill, float minWidth, float targetWidth, float delay = 0.3f, float duration = 0.9f)
        {
            var rt = fill.rectTransform;
            if (!UiMotion.Enabled) { rt.sizeDelta = new Vector2(targetWidth, rt.sizeDelta.y); return; }
            var b = fill.gameObject.AddComponent<BarGrow>(); b._rt = rt; b._min = minWidth; b._target = targetWidth; b._delay = delay; b._dur = duration; b._t0 = Time.unscaledTime;
            rt.sizeDelta = new Vector2(minWidth, rt.sizeDelta.y);
        }
        void Update()
        {
            if (_done) return;
            float p = Ease.OutCubic(Tween.Progress(Time.unscaledTime - _t0, _delay, _dur)); _rt.sizeDelta = new Vector2(Mathf.Lerp(_min, _target, p), _rt.sizeDelta.y);
            if (p >= 1f) { _done = true; Destroy(this); }
        }
    }

    /// <summary>Loading placeholder: the image's alpha breathes, each neighbour slightly out of phase.</summary>
    public sealed class Breath : MonoBehaviour
    {
        Image _img; int _index; Color _c;
        public static void On(Image img, int index) { var b = img.gameObject.AddComponent<Breath>(); b._img = img; b._index = index; b._c = img.color; }
        void Update() { if (_img == null) return; var c = _c; c.a = UiMotion.Enabled ? Tween.SkeletonAlpha(Time.unscaledTime, _index) : 0.4f; _img.color = c; }
    }

    /// <summary>Looping scale/alpha pulse (live dot, notification badge, "you are here" ring).</summary>
    public sealed class PulseImage : MonoBehaviour
    {
        Image _img; RectTransform _rt; float _period, _minScale, _maxScale, _minA, _maxA; Color _c;
        public static void On(Image img, float period = 1.6f, float minScale = 0.9f, float maxScale = 1.25f, float minAlpha = 0.45f, float maxAlpha = 1f)
        {
            if (!UiMotion.Enabled) return;
            var rt = img.rectTransform; var size = rt.sizeDelta;                                          // pulse about the centre, not the top-left pivot the layout helpers use
            rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition += new Vector2(size.x * 0.5f, -size.y * 0.5f);
            var p = img.gameObject.AddComponent<PulseImage>(); p._img = img; p._rt = rt; p._period = period; p._minScale = minScale; p._maxScale = maxScale; p._minA = minAlpha; p._maxA = maxAlpha; p._c = img.color;
        }
        void Update()
        {
            float k = Tween.Pulse(Time.unscaledTime, _period); _rt.localScale = Vector3.one * Mathf.Lerp(_minScale, _maxScale, k);
            var c = _c; c.a = _c.a * Mathf.Lerp(_minA, _maxA, k); _img.color = c;
        }
    }

    /// <summary>Slow zoom-and-drift over a photo. `baseUv` is the crop at rest (top-left based, like Ui.Top); mirror flips it horizontally.</summary>
    public sealed class KenBurnsView : MonoBehaviour
    {
        RawImage _raw; Rect _base; float _duration, _zoom, _dx, _dy, _t0;
        public static KenBurnsView On(RawImage raw, Rect baseUv, float duration = 14f, float zoomEnd = 1.08f, float driftX = 0.03f, float driftY = 0.015f)
        {
            var k = raw.gameObject.AddComponent<KenBurnsView>(); k.Set(raw, baseUv, duration, zoomEnd, driftX, driftY); return k;
        }
        public void Set(RawImage raw, Rect baseUv, float duration, float zoomEnd, float driftX, float driftY) { _raw = raw; _base = baseUv; _duration = duration; _zoom = zoomEnd; _dx = driftX; _dy = driftY; _t0 = Time.unscaledTime; _raw.uvRect = baseUv; }
        public void Rebase(Rect baseUv) { _base = baseUv; _t0 = Time.unscaledTime; _raw.uvRect = baseUv; }
        void Update()
        {
            if (_raw == null || !UiMotion.Enabled) return;
            Tween.KenBurns(Time.unscaledTime - _t0, _duration, _base.x, _base.y, _base.width, _base.height, _zoom, _dx, _dy, out float x, out float y, out float w, out float h);
            _raw.uvRect = new Rect(x, y, w, h);
        }
    }

    /// <summary>Quick fade of a CanvasGroup (+ optional slide) used when the hero changes slide.</summary>
    public sealed class GroupFade : MonoBehaviour
    {
        CanvasGroup _cg; RectTransform _rt; Vector2 _home; float _t0, _dur, _slide; bool _active;
        public static GroupFade On(RectTransform rt) { var g = rt.gameObject.AddComponent<GroupFade>(); g._rt = rt; g._home = rt.anchoredPosition; g._cg = rt.GetComponent<CanvasGroup>() ?? rt.gameObject.AddComponent<CanvasGroup>(); return g; }
        public void SetHome(Vector2 home) { _home = home; if (!_active) _rt.anchoredPosition = home; }
        public void Play(float duration = 0.5f, float slide = 28f) { if (!UiMotion.Enabled) { _cg.alpha = 1f; _rt.anchoredPosition = _home; return; } _t0 = Time.unscaledTime; _dur = duration; _slide = slide; _active = true; Apply(0f); }
        void Apply(float p) { float k = Ease.OutCubic(p); _cg.alpha = Mathf.Clamp01(p * 1.6f); _rt.anchoredPosition = _home + new Vector2(-_slide * (1f - k), 0f); }
        void Update() { if (!_active) return; float p = Tween.Progress(Time.unscaledTime - _t0, 0f, _dur); Apply(p); if (p >= 1f) { _active = false; _cg.alpha = 1f; _rt.anchoredPosition = _home; } }
    }
}
