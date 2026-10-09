using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace ARO.Editor
{
    /// <summary>Headless build entry points for CI: -executeMethod ARO.Editor.BuildScript.BuildWebGL</summary>
    public static class BuildScript
    {
        public static void BuildWebGL() => Build(BuildTarget.WebGL, "Builds/WebGL");
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/AfricanRoads.exe");

        static void Build(BuildTarget target, string output)
        {
            // Headless CI has no human to click the menu: generate the scene, materials and URP asset first.
            ProjectLinker.Apply();
            SceneBuilder.Build();
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) { Console.Error.WriteLine("Scene generation produced no scenes."); EditorApplication.Exit(2); return; }
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes, locationPathName = output, target = target, options = BuildOptions.None
            });
            Console.WriteLine($"Build {report.summary.result}: {report.summary.totalSize / 1048576} MB in {report.summary.totalTime}");
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
