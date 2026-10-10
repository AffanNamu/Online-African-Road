using System.Collections.Generic;
using ARO.NetCore;
using UnityEngine;

namespace ARO.World
{
    /// <summary>
    /// The prop catalog at runtime. Props are DEVELOPMENT PLACEHOLDERS (generated geometry) until a production prefab with the same id exists at
    /// Resources/Props/{id}; the streamer then instantiates that prefab instead and the placeholder is no longer used for that id.
    /// </summary>
    public static class PropLibrary
    {
        static PropCatalogSpec _catalog; static Dictionary<string, PropDef> _defs;
        static readonly Dictionary<string, MeshBuffers> _geo = new Dictionary<string, MeshBuffers>();
        static readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>();

        public static void EnsureLoaded()
        {
            if (_defs != null) return;
            _defs = new Dictionary<string, PropDef>();
            var ta = Resources.Load<TextAsset>("Props/prop_catalog");
            if (ta == null) { Debug.LogError("[Props] Resources/Props/prop_catalog.json is missing; no roadside props will be placed"); return; }
            _catalog = JsonUtility.FromJson<PropCatalogSpec>(ta.text);
            var errors = PropCatalogValidator.Validate(_catalog);
            if (errors.Count > 0) { Debug.LogError("[Props] invalid prop catalog: " + string.Join("; ", errors)); return; }
            foreach (var p in _catalog.props) _defs[p.id] = p;
            Debug.Log($"[Props] catalog loaded: {_defs.Count} props ({CountStatus("dev")} development placeholders, {CountStatus("production")} production)");
        }

        static int CountStatus(string status) { int n = 0; foreach (var d in _defs.Values) if (d.status == status) n++; return n; }

        public static ISet<string> KnownIds() { EnsureLoaded(); return new HashSet<string>(_defs.Keys); }
        public static PropDef Def(string id) { EnsureLoaded(); return id != null && _defs.TryGetValue(id, out var d) ? d : null; }
        public static IEnumerable<PropDef> All { get { EnsureLoaded(); return _defs.Values; } }

        /// <summary>Generated development geometry for (id, lod), cached.</summary>
        public static MeshBuffers Geometry(string id, int lod)
        {
            var def = Def(id); if (def == null) return null;
            string key = id + "#" + lod;
            if (!_geo.TryGetValue(key, out var g)) _geo[key] = g = DevPropGeometry.Build(def, lod);
            return g;
        }

        /// <summary>A production prefab for this id (Resources/Props/{id}), or null while only the placeholder exists.</summary>
        public static GameObject ProductionPrefab(string id)
        {
            if (_prefabs.TryGetValue(id, out var p)) return p;
            return _prefabs[id] = Resources.Load<GameObject>("Props/" + id);
        }

        /// <summary>Test hook: registers a prefab-like template for an id (a production asset standing in).</summary>
        public static void OverrideProduction(string id, GameObject template) => _prefabs[id] = template;
        public static void ClearOverrides() => _prefabs.Clear();
    }
}
