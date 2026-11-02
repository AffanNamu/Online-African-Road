using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARO.World
{
    /// <summary>
    /// The Kano residential street, shown by the RUNNING GAME for visual review ("?smoke=kanostreet&amp;view=ref&amp;hour=15.5"): the review prefab (the placements of the Blender review scene, built from the
    /// private assets by ARO.Editor.BlenderImport.KanoReviewBuilder) is instantiated far from the route, and the game's own camera is put on one of the review cameras, so the picture has the game's lighting,
    /// sky, tone mapping and quality tier. Test hook for CI and review only: it is never reached from the UI, it changes no game system, and without the private assets in the build it logs that and does nothing.
    /// The cameras are the Blender review cameras (tools/blender/kano_kit/render_scene.py), in Blender axes (street along +Y, right +X, up +Z) converted to Unity axes: (x, y, z) becomes (-x, z, -y).
    /// </summary>
    public sealed class KanoReviewScene : MonoBehaviour
    {
        public const string PrefabPath = "KanoReview/kano_residential_section_a";
        /// <summary>The same street with each plot's wall panels, piers and gate-pier lanterns merged into one frontage mesh (content-side draw-call prototype, "&amp;variant=merged"); built only when the merged placements exist.</summary>
        public const string MergedPrefabPath = "KanoReview/kano_residential_section_a_merged";
        /// <summary>Far from the route so the chunk streamer's terrain never overlaps the review ground.</summary>
        public static readonly Vector3 Origin = new Vector3(6000f, 0f, 6000f);

        struct View { public Vector3 Loc, Look; public float LensMm; public View(float lx, float ly, float lz, float tx, float ty, float tz, float lens) { Loc = new Vector3(lx, ly, lz); Look = new Vector3(tx, ty, tz); LensMm = lens; } }

        static readonly Dictionary<string, View> Views = new Dictionary<string, View>
        {
            { "ref", new View(-3.2f, -45f, 1.7f, 0.6f, 60f, 2.6f, 28f) },          // the reference camera of concept R-006: eye level 1.7 m at the left kerb line, 28 mm
            { "front", new View(-5.2f, 10f, 2.1f, 16f, 10f, 3.6f, 30f) },          // from the left sidewalk, facing the right-hand frontages
            { "side", new View(0f, -62f, 4.6f, 0f, 0f, 4.0f, 24f) },               // along the street from beyond the section start
            { "street1", new View(4.3f, -30f, 1.6f, -6f, -4f, 1.6f, 28f) },        // pedestrian on the right sidewalk
            { "street2", new View(-1.7f, -20f, 1.2f, 5f, 10f, 1.5f, 28f) },        // driver's eye in the left lane
            { "top", new View(0f, 0f, 95f, 0f, 0.5f, 0f, 50f) },
        };

        Camera _cam; Vector3 _pos; Quaternion _rot; float _lens; string _view = "ref";

        static Vector3 ToUnity(Vector3 b) => new Vector3(-b.x, b.z, -b.y);

        /// <summary>Starts the scene when the page asked for it. Returns false (and logs why) when the review prefab is not in this build.</summary>
        public static bool Begin(string url)
        {
            string variant = Query(url, "variant", ""), path = variant == "merged" ? MergedPrefabPath : PrefabPath;
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null) { Debug.Log("[KanoReview] no review prefab at Resources/" + path + ": the private assets are not part of this build, nothing to show"); return false; }
            var s = new GameObject("KanoReviewScene").AddComponent<KanoReviewScene>(); s._view = Query(url, "view", "ref");
            if (!Views.ContainsKey(s._view)) { Debug.Log("[KanoReview] unknown view '" + s._view + "', using ref"); s._view = "ref"; }
            var inst = Instantiate(prefab, Origin, Quaternion.identity); inst.name = "kano_residential_section_a";
            s.gameObject.AddComponent<KanoReviewStats>().Root = inst.transform;
            s._cam = Camera.main; var v = Views[s._view];
            s._pos = Origin + ToUnity(v.Loc); s._rot = Quaternion.LookRotation(ToUnity(v.Look) - ToUnity(v.Loc), Vector3.up); s._lens = v.LensMm;
            s.Apply();
            Debug.Log("[KanoReview] ready view=" + s._view + " variant=" + (variant == "" ? "standard" : variant) + " camera=" + s._pos + " lens=" + s._lens + " mm");
            return true;
        }

        static string Query(string url, string key, string fallback)
        {
            if (string.IsNullOrEmpty(url)) return fallback; int q = url.IndexOf('?'); if (q < 0) return fallback;
            foreach (var part in url.Substring(q + 1).Split('&')) { int eq = part.IndexOf('='); if (eq > 0 && part.Substring(0, eq) == key) { string v = part.Substring(eq + 1); int h = v.IndexOf('#'); return h >= 0 ? v.Substring(0, h) : v; } }
            return fallback;
        }

        void Apply()
        {
            if (_cam == null) _cam = Camera.main; if (_cam == null) return;
            _cam.transform.SetPositionAndRotation(_pos, _rot);
            // Blender's 36 mm sensor fits the width: horizontal field of view 2 atan(18 / lens); Unity wants the vertical one for the current aspect.
            _cam.fieldOfView = VerticalFov(_lens, _cam.aspect > 0.1f ? _cam.aspect : 16f / 9f);
        }

        /// <summary>The vertical field of view in degrees that reproduces a Blender camera with a 36 mm sensor (which fits the width) and this focal length, for this aspect ratio: 28 mm at 16:9 is 39.76 degrees.</summary>
        public static float VerticalFov(float lensMm, float aspect)
        {
            float hfov = 2f * Mathf.Atan(18f / lensMm);
            return 2f * Mathf.Atan(Mathf.Tan(hfov * 0.5f) / aspect) * Mathf.Rad2Deg;
        }

        void LateUpdate() { Apply(); }
    }
}
