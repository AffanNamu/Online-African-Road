using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ARO.NetCore;
using UnityEditor;
using UnityEngine;

namespace ARO.Editor.BlenderImport
{
    /// <summary>
    /// Builds, from the exports of the Kano residential street kit (made by tools/blender/kano_kit and kept in the private asset repository), everything the game needs to show the street:
    ///  1. a production prefab per asset id at Assets/ThirdParty/ARO/Resources/Props/{id}.prefab (identity root, LODGroup, BoxCollider, kit materials with textures and cutout), by BlenderAssetBuilder;
    ///  2. the review prefab Resources/KanoReview/kano_residential_section_a.prefab: every placement of the Blender review scene (placements/kano_residential_section_a.json), converted from Blender axes
    ///     to Unity axes, on a plain ground slab. The running game shows it with its own lighting, camera and post-processing (ARO.World.KanoReviewScene, "?smoke=kanostreet").
    /// Files appear under Assets/ThirdParty/ARO at CI time (copied from the private repository's unity/ folder) and are never part of the public repository. Editor only; it changes no game system.
    /// Blender axes (street along +Y, right +X, up +Z, asset front -Y) to Unity axes: position (x, y, z) becomes (-x, z, -y) and a yaw of t degrees becomes -t degrees, which is what the importers do to the models
    /// (measured by the calibration import test: the Blender front -Y arrives as Unity +Z and the Blender right -X as Unity +X).
    /// </summary>
    public static class KanoReviewBuilder
    {
        public const string Root = "Assets/ThirdParty/ARO";
        public const string PrefabFolder = Root + "/Resources/Props";
        public const string ReviewFolder = Root + "/Resources/KanoReview";
        public const string ReviewPrefabPath = ReviewFolder + "/kano_residential_section_a.prefab";
        public const string PlacementsPath = Root + "/placements/kano_residential_section_a.json";
        /// <summary>Optional: the same street with each plot's wall panels, piers and gate-pier lanterns replaced by one merged frontage asset (a content-side draw-call prototype; see kano_kit/frontage.py).</summary>
        public const string MergedPlacementsPath = Root + "/placements/kano_residential_section_a_merged.json";
        public const string MergedReviewPrefabPath = ReviewFolder + "/kano_residential_section_a_merged.prefab";
        public const string KitPath = Root + "/Materials/aro_materials.json";

        [Serializable] class Placement { public string asset; public float x, y, z, yawDeg, scale = 1f; public string tag; }
        [Serializable] class ViewDef { public string name; public float[] loc; public float[] look; public float lens; }
        [Serializable] class ViewsOut { public ViewDef[] views; }
        [Serializable] class PlacementFile { public string scene; public string kind; public string ground; public ViewDef[] views; public Placement[] items; }
        [Serializable] class ManifestWrap { public PropDef catalog_entry; }

        public static Vector3 FromBlender(float x, float y, float z) => new Vector3(-x, z, -y);

        /// <summary>The newest exported FBX of an asset: Models/{id}/vNNN/{id}.fbx with the highest NNN, or Models/{id}/{id}.fbx.</summary>
        public static string LatestFbx(string id)
        {
            string dir = Root + "/Models/" + id; if (!Directory.Exists(dir)) return null;
            var versions = Directory.GetDirectories(dir).Select(d => Path.GetFileName(d)).Where(n => n.Length == 4 && n[0] == 'v').OrderBy(n => n, StringComparer.Ordinal).ToList();
            for (int i = versions.Count - 1; i >= 0; i--) { string p = dir + "/" + versions[i] + "/" + id + ".fbx"; if (File.Exists(p)) return p; }
            string flat = dir + "/" + id + ".fbx"; return File.Exists(flat) ? flat : null;
        }

        static PropDef DefFor(string fbxPath, string id)
        {
            string manifest = Path.ChangeExtension(fbxPath, null) + ".manifest.json";
            if (File.Exists(manifest)) { var w = JsonUtility.FromJson<ManifestWrap>(File.ReadAllText(manifest)); if (w != null && w.catalog_entry != null && !string.IsNullOrEmpty(w.catalog_entry.id)) return w.catalog_entry; }
            // roads and other assets without a prop-catalogue entry: seen from far, never a shadow caster
            return new PropDef { id = id, category = "environment", status = "production", cullDistance = 600f, castShadow = false, collider = "none" };
        }

        /// <summary>Imports one asset's FBX, builds its production prefab and saves it under Resources/Props. Returns the saved prefab, or null with a message.</summary>
        public static GameObject BuildPrefab(string id, MaterialKitFile kit, List<string> notes)
        {
            string fbx = LatestFbx(id); if (fbx == null) { notes.Add(id + ": no FBX under " + Root + "/Models"); return null; }
            AssetDatabase.ImportAsset(fbx, ImportAssetOptions.ForceSynchronousImport);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx); if (model == null) { notes.Add(id + ": " + fbx + " did not import as a GameObject"); return null; }
            var built = BlenderAssetBuilder.Build(model, DefFor(fbx, id), kit, Root + "/Materials");
            foreach (var n in built.Notes) notes.Add(id + ": " + n);
            foreach (var m in built.UnmatchedMaterials) notes.Add(id + ": material '" + m + "' is not in the kit");
            Directory.CreateDirectory(PrefabFolder); string path = PrefabFolder + "/" + id + ".prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(built.Root, path); UnityEngine.Object.DestroyImmediate(built.Root);
            if (saved == null) notes.Add(id + ": the prefab was not saved");
            else notes.Add(id + ": prefab " + path + " LOD triangles [" + string.Join(", ", built.Measure.LodTriangles) + "] materials " + built.Measure.MaterialCount + " collider " + built.Measure.HasBoxCollider);
            return saved;
        }

        /// <summary>Builds the prefabs for every asset the placement files use, then the review prefab (and the merged variant when its placements exist). Returns the log lines.</summary>
        public static List<string> BuildReview()
        {
            var notes = new List<string>();
            if (!File.Exists(PlacementsPath)) { notes.Add("no placements file at " + PlacementsPath + ": nothing to build (the private assets are not in this checkout)"); return notes; }
            var kit = File.Exists(KitPath) ? BlenderAssetBuilder.LoadKit(KitPath) : null; if (kit == null) notes.Add("no material kit at " + KitPath + ": the imported materials are kept");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var variants = new List<(string placements, string prefab)> { (PlacementsPath, ReviewPrefabPath) };
            if (File.Exists(MergedPlacementsPath)) variants.Add((MergedPlacementsPath, MergedReviewPrefabPath));
            foreach (var envFile in Directory.GetFiles(Root + "/placements", "env_*.json").OrderBy(f => f, StringComparer.Ordinal))                  // complete environments (env_kit.scenes): one review prefab each, with its own cameras
                variants.Add((envFile.Replace('\\', '/'), ReviewFolder + "/" + Path.GetFileNameWithoutExtension(envFile) + ".prefab"));
            var prefabs = new Dictionary<string, GameObject>();
            foreach (var (placements, prefabPath) in variants)
            {
                var file = JsonUtility.FromJson<PlacementFile>(File.ReadAllText(placements));
                foreach (var id in file.items.Select(i => i.asset).Distinct()) if (!prefabs.ContainsKey(id)) prefabs[id] = BuildPrefab(id, kit, notes);
                BuildReviewPrefab(file, prefabPath, prefabs, kit, notes);
            }
            return notes;
        }

        static void BuildReviewPrefab(PlacementFile file, string prefabPath, Dictionary<string, GameObject> prefabs, MaterialKitFile kit, List<string> notes)
        {
            var root = new GameObject("kano_residential_section_a");
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube); ground.name = "review_ground"; ground.transform.SetParent(root.transform, false);
            ground.transform.localScale = new Vector3(3000f, 0.2f, 3000f); ground.transform.localPosition = new Vector3(0f, -0.1f - 0.03f, 300f);
            string groundKey = string.IsNullOrEmpty(file.ground) ? "grass_green" : file.ground;
            if (kit != null) { var e = kit.materials.FirstOrDefault(x => x.name == "M_" + groundKey); if (e != null) ground.GetComponent<MeshRenderer>().sharedMaterial = BlenderAssetBuilder.KitMaterial(e, Root + "/Materials"); }
            int placed = 0, missing = 0; Bounds trench = default; bool haveTrench = false;
            foreach (var it in file.items)
            {
                if (!prefabs.TryGetValue(it.asset, out var prefab) || prefab == null) { missing++; continue; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform); go.name = it.asset;
                go.transform.localPosition = FromBlender(it.x, it.y, it.z); go.transform.localRotation = Quaternion.Euler(0f, -it.yawDeg, 0f); go.transform.localScale = Vector3.one * (it.scale > 0f ? it.scale : 1f); placed++;
                if (file.kind == "environment" && it.asset.StartsWith("aro_road_"))                                                              // the footprint of the road patches (ground surfaces whose ruts, potholes and banks lie below the nominal level)
                    foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (!haveTrench) { trench = r.bounds; haveTrench = true; } else trench.Encapsulate(r.bounds); }
            }
            if (haveTrench)                                                                                                                      // a ground slab flush with the nominal road level would hide every rut floor and pothole under grass: sink the slab below the road patches and keep the verge at its level around them
            {
                const float vergeTop = -0.03f, baseTop = -0.60f; var groundMat = ground.GetComponent<MeshRenderer>().sharedMaterial;
                ground.transform.localPosition = new Vector3(0f, baseTop - 0.1f, 300f);
                void Verge(float x0, float x1, float z0, float z1)
                {
                    var c = GameObject.CreatePrimitive(PrimitiveType.Cube); c.name = "review_ground"; c.transform.SetParent(root.transform, false);
                    c.transform.localScale = new Vector3(x1 - x0, vergeTop - baseTop, z1 - z0); c.transform.localPosition = new Vector3((x0 + x1) / 2f, (vergeTop + baseTop) / 2f, (z0 + z1) / 2f);
                    c.GetComponent<MeshRenderer>().sharedMaterial = groundMat;
                }
                Verge(-1500f, trench.min.x, -1200f, 1800f); Verge(trench.max.x, 1500f, -1200f, 1800f); Verge(trench.min.x, trench.max.x, -1200f, trench.min.z); Verge(trench.min.x, trench.max.x, trench.max.z, 1800f);
                notes.Add("review ground: road trench x " + trench.min.x.ToString("0.0") + " to " + trench.max.x.ToString("0.0") + ", z " + trench.min.z.ToString("0.0") + " to " + trench.max.z.ToString("0.0") + ", verge at " + vergeTop + " m, base at " + baseTop + " m");
            }
            Directory.CreateDirectory(ReviewFolder);
            var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath); UnityEngine.Object.DestroyImmediate(root); AssetDatabase.SaveAssets();
            if (file.views != null && file.views.Length > 0)                                                                                     // the cameras of an environment scene travel with its prefab as a Resources text file
            {
                string vp = prefabPath.Substring(0, prefabPath.Length - ".prefab".Length) + "_views.json";
                File.WriteAllText(vp, JsonUtility.ToJson(new ViewsOut { views = file.views })); AssetDatabase.ImportAsset(vp, ImportAssetOptions.ForceSynchronousImport);
            }
            notes.Add("review prefab " + prefabPath + ": " + placed + " placements, " + missing + " skipped (no prefab), saved " + (saved != null) + (file.views != null ? ", " + file.views.Length + " views" : ""));
        }

        /// <summary>Entry point for CI: builds the prefabs and the review prefab and prints what happened (the log lines start with [KanoReviewBuilder]).</summary>
        public static void BuildReviewAndLog()
        {
            foreach (var n in BuildReview()) Console.WriteLine("[KanoReviewBuilder] " + n);
        }
    }
}
