using ARO.Vehicles;
using ARO.World;
using UnityEngine;

namespace ARO.Game
{
    /// <summary>
    /// Test pages only (?smoke=drive&amp;people=1&amp;peoplecam=1): parks the camera in front of the people so the review pictures show them (the chase camera looks at the truck and rarely
    /// frames anyone). Once people are drawn it picks, among the 20 nearest, a standing person with the most others within 6 m, stops the follow camera, and takes two views: a close one
    /// (4.5 m in front of that person at eye height) and a group one (11 m away, a little higher). Each view is logged as "[PeopleCam] shot N ..." for the browser check to photograph.
    /// It changes nothing else: the truck, the crowd and the world run as usual.
    /// </summary>
    public class PeopleCam : MonoBehaviour
    {
        public CrowdRenderer crowd;
        float _seen = -1f, _next; int _shot; Vector3 _subject, _front, _centre;

        void LateUpdate()
        {
            if (crowd == null || crowd.Shown.Count == 0) return;
            if (_seen < 0f) { _seen = Time.unscaledTime; _next = _seen + 4f; return; }                  // let the chunks and the crowd settle first
            if (Time.unscaledTime < _next || _shot >= 2) { if (_shot > 0) Hold(); return; }
            if (_shot == 0) Pick();
            _shot++; _next = Time.unscaledTime + 5f; Hold();
            Debug.Log($"[PeopleCam] shot {_shot} {(_shot == 1 ? "close" : "group")}: subject at ({_subject.x:F1}, {_subject.z:F1}), {crowd.Shown.Count} people drawn, people={(crowd.Real ? "real" : "code-built")}");
        }

        void Pick()
        {
            int best = 0, bestN = -1, n = Mathf.Min(20, crowd.Shown.Count);
            for (int i = 0; i < n; i++)
            {
                var (p, _, standing) = crowd.Shown[i]; int k = 0;
                for (int j = 0; j < crowd.Shown.Count; j++) if (j != i && (crowd.Shown[j].pos - p).sqrMagnitude < 36f) k++;
                int score = k * 2 + (standing ? 1 : 0);
                if (score > bestN) { bestN = score; best = i; }
            }
            var s = crowd.Shown[best]; _subject = s.pos; float r = s.yaw * Mathf.Deg2Rad; _front = new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
            _centre = Vector3.zero; int c = 0;
            foreach (var o in crowd.Shown) if ((o.pos - _subject).sqrMagnitude < 36f) { _centre += o.pos; c++; }
            _centre /= Mathf.Max(1, c);
            var follow = FindFirstObjectByType<FollowCamera>(); if (follow != null) follow.enabled = false;
        }

        void Hold()
        {
            var cam = Camera.main; if (cam == null) return;
            if (_shot <= 1) { cam.transform.position = _subject + _front * 4.5f + Vector3.up * 1.55f; cam.transform.LookAt(_subject + Vector3.up * 1.2f); }
            else { cam.transform.position = _centre + _front * 11f + Vector3.up * 3.2f; cam.transform.LookAt(_centre + Vector3.up * 1.0f); }
        }
    }
}
