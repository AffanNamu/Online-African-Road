using System.IO;
using UnityEditor;
using UnityEngine;

namespace ARO.Editor
{
    /// <summary>
    /// Links the repository's Unity project to the Unity Cloud project (needed for Relay / Multiplayer Sessions) without
    /// anyone clicking through the Editor, so CI builds are linked too. Reads ProjectSettings/ARO_UnityProject.json.
    /// The IDs are identifiers, not credentials. Runs on editor load and before every scripted build.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectLinker
    {
        const string ConfigPath = "ProjectSettings/ARO_UnityProject.json";
        [System.Serializable] class Cfg { public string unityProjectName, cloudProjectId, productName; }

        static ProjectLinker() { EditorApplication.delayCall += () => { try { Apply(); } catch (System.Exception e) { Debug.LogWarning("[ARO] ProjectLinker: " + e.Message); } }; }

        [MenuItem("African Roads/Link Unity Cloud Project")]
        public static void Apply()
        {
            if (!File.Exists(ConfigPath)) return;
            var cfg = JsonUtility.FromJson<Cfg>(File.ReadAllText(ConfigPath));
            if (cfg == null || string.IsNullOrEmpty(cfg.cloudProjectId)) return;

            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) { Debug.LogWarning("[ARO] ProjectSettings.asset not found; cannot link cloud project yet."); return; }
            var so = new SerializedObject(assets[0]);
            bool changed = false;
            changed |= Set(so, "cloudProjectId", cfg.cloudProjectId);
            if (changed) { so.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets(); Debug.Log("[ARO] Linked Unity Cloud project " + cfg.cloudProjectId); }
            if (!string.IsNullOrEmpty(cfg.productName)) PlayerSettings.productName = cfg.productName;
        }

        static bool Set(SerializedObject so, string prop, string value)
        {
            var p = so.FindProperty(prop);
            if (p == null) { Debug.LogWarning($"[ARO] PlayerSettings has no '{prop}' property in this Unity version."); return false; }
            if (p.stringValue == value) return false;
            p.stringValue = value; return true;
        }
    }
}
