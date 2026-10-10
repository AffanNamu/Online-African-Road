using System;
using System.IO;
using System.Linq;
using ARO.Backend;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace ARO.Editor
{
    /// <summary>Headless build entry points for CI: -executeMethod ARO.Editor.BuildScript.BuildWebGL</summary>
    public static class BuildScript
    {
        public static void BuildWebGL() => Build(BuildTarget.WebGL, "Builds/WebGL");
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/AfricanRoads.exe");

        /// <summary>
        /// CI passes `-supabaseUrl <url> -supabaseAnonKey <anon key>` (GitHub variable/secret) so the player is built already pointing at the
        /// backend, with nothing committed to git. Only the PUBLIC anon key is ever accepted here. Without the args the game runs unconfigured.
        /// </summary>
        static void ApplyBackendConfigFromArgs()
        {
            string url = Arg("-supabaseUrl"), key = Arg("-supabaseAnonKey");
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(key)) { Console.WriteLine("[ARO] No backend args: building unconfigured."); return; }
            if (key.Count(c => c == '.') != 2) { Console.Error.WriteLine("[ARO] -supabaseAnonKey is not a JWT; refusing to bake it into the build."); EditorApplication.Exit(3); return; }
            Directory.CreateDirectory("Assets/_Project/Resources");
            const string path = "Assets/_Project/Resources/BackendConfig.asset";
            var cfg = AssetDatabase.LoadAssetAtPath<BackendConfig>(path);
            if (cfg == null) { cfg = UnityEngine.ScriptableObject.CreateInstance<BackendConfig>(); AssetDatabase.CreateAsset(cfg, path); }
            cfg.supabaseUrl = url.TrimEnd('/'); cfg.anonKey = key;
            EditorUtility.SetDirty(cfg); AssetDatabase.SaveAssets();
            Console.WriteLine("[ARO] BackendConfig written for " + cfg.supabaseUrl);
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        static void Build(BuildTarget target, string output)
        {
            // Headless CI has no human to click the menu: generate the scene, materials and URP asset first.
            ProjectLinker.Apply();
            SceneBuilder.Build();
            ApplyBackendConfigFromArgs();
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) { Console.Error.WriteLine("Scene generation produced no scenes."); EditorApplication.Exit(2); return; }
            // Static hosts (GitHub Pages, S3, itch.io) rarely send Content-Encoding for .gz files; the fallback loader decompresses in JS instead.
            if (target == BuildTarget.WebGL) PlayerSettings.WebGL.decompressionFallback = true;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes, locationPathName = output, target = target, options = BuildOptions.None
            });
            Console.WriteLine($"Build {report.summary.result}: {report.summary.totalSize / 1048576} MB in {report.summary.totalTime}");
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
