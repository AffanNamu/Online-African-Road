using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;

namespace ARO.Editor
{
    /// <summary>
    /// Reads, from the real Unity project and the real importers, how textures are configured and what they became: per texture and per target (WebGL, Windows/Standalone, Android, iOS) the
    /// overrides, the maximum size, the compression and the format Unity would pick automatically, plus the format, size and memory of what was actually imported for the ACTIVE build target.
    /// Also dumps the player, quality and build settings that concern textures. Nothing is changed and nothing is built. Run in CI by .github/workflows/texture-report.yml:
    ///   -executeMethod ARO.Editor.TextureReport.Run   (writes Builds/TextureReport/texture_report.json and prints it between TEXTURE_REPORT_BEGIN / TEXTURE_REPORT_END)
    /// </summary>
    public static class TextureReport
    {
        static readonly string[] Targets = { "WebGL", "Standalone", "Android", "iPhone" };

        [Serializable] public class PlatformInfo { public string platform; public bool overridden; public int maxTextureSize; public string format, compression, automaticFormat; public int compressionQuality; public bool crunched; }
        [Serializable] public class TextureInfo
        {
            public string path, textureType, importedFormat; public int importedWidth, importedHeight, importedMips; public long runtimeBytesInEditor;
            public bool sRGB, mipmaps, readable, streamingMips, alphaIsTransparency; public string defaultCompression, wrap; public int defaultMaxSize, aniso;
            public List<PlatformInfo> platforms = new List<PlatformInfo>();
        }
        [Serializable] public class KeyValue { public string key, value; }
        [Serializable] public class QualityLevel { public string name; public int globalTextureMipmapLimit, anisotropicFiltering, streamingMipmapsActive; }
        [Serializable] public class Report
        {
            public string unityVersion, activeBuildTarget, activeBuildTargetGroup, colorSpace;
            public List<KeyValue> playerSettingsTextureRelated = new List<KeyValue>(), webglPlayerSettings = new List<KeyValue>(), editorUserBuildSettings = new List<KeyValue>();
            public List<QualityLevel> qualityLevels = new List<QualityLevel>();
            public List<TextureInfo> textures = new List<TextureInfo>();
            public long totalRuntimeBytesInEditor, textureCount;
            public List<string> notes = new List<string>();
        }

        public static void Run()
        {
            try
            {
                var json = JsonUtility.ToJson(Build(), true);
                Directory.CreateDirectory("Builds/TextureReport");
                File.WriteAllText("Builds/TextureReport/texture_report.json", json);
                Console.WriteLine("TEXTURE_REPORT_BEGIN"); Console.WriteLine(json); Console.WriteLine("TEXTURE_REPORT_END");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Console.Error.WriteLine("[TextureReport] failed: " + e); EditorApplication.Exit(1); }
        }

        public static Report Build()
        {
            var r = new Report
            {
                unityVersion = Application.unityVersion, activeBuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                activeBuildTargetGroup = BuildPipelineGroup().ToString(), colorSpace = PlayerSettings.colorSpace.ToString()
            };
            DumpPlayerSettings(r); DumpStatics(typeof(PlayerSettings.WebGL), r.webglPlayerSettings); DumpStatics(typeof(EditorUserBuildSettings), r.editorUserBuildSettings, "texture", "compress", "webgl", "codeoptimization", "development");
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                r.qualityLevels.Add(new QualityLevel { name = QualitySettings.names[i], globalTextureMipmapLimit = QualitySettings.globalTextureMipmapLimit, anisotropicFiltering = (int)QualitySettings.anisotropicFiltering,
                    streamingMipmapsActive = QualitySettings.streamingMipmapsActive ? 1 : 0 });
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Texture", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Assets/ThirdParty/")) continue;
                var imp = AssetImporter.GetAtPath(path) as TextureImporter; if (imp == null) continue;
                var t = new TextureInfo { path = path, textureType = imp.textureType.ToString(), sRGB = imp.sRGBTexture, mipmaps = imp.mipmapEnabled, readable = imp.isReadable, streamingMips = imp.streamingMipmaps,
                    alphaIsTransparency = imp.alphaIsTransparency, wrap = imp.wrapMode.ToString(), aniso = imp.anisoLevel };
                var def = imp.GetDefaultPlatformTextureSettings(); t.defaultCompression = imp.textureCompression.ToString(); t.defaultMaxSize = def.maxTextureSize;
                foreach (var p in Targets)
                {
                    var s = imp.GetPlatformTextureSettings(p);
                    string auto; try { auto = imp.GetAutomaticFormat(p).ToString(); } catch (Exception e) { auto = "n/a (" + e.GetType().Name + ")"; }
                    t.platforms.Add(new PlatformInfo { platform = p, overridden = s.overridden, maxTextureSize = s.maxTextureSize, format = s.format.ToString(), compression = s.textureCompression.ToString(), compressionQuality = s.compressionQuality, crunched = s.crunchedCompression, automaticFormat = auto });
                }
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex != null) { t.importedFormat = tex.format.ToString(); t.importedWidth = tex.width; t.importedHeight = tex.height; t.importedMips = tex.mipmapCount; t.runtimeBytesInEditor = Profiler.GetRuntimeMemorySizeLong(tex); r.totalRuntimeBytesInEditor += t.runtimeBytesInEditor; }
                r.textures.Add(t);
            }
            r.textureCount = r.textures.Count;
            r.notes.Add("imported* and runtimeBytesInEditor describe the texture as imported for the ACTIVE build target (" + r.activeBuildTarget + "); other targets show their overrides and the format Unity would choose automatically (automaticFormat).");
            r.notes.Add("Textures created at runtime in code (WorldMaterials.ProcTex) are not assets and are not listed.");
            return r;
        }

        static BuildTargetGroup BuildPipelineGroup() => BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);

        static void DumpPlayerSettings(Report r)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) { r.notes.Add("ProjectSettings.asset could not be loaded"); return; }
            var so = new SerializedObject(assets[0]); var it = so.GetIterator(); bool enter = true;
            string[] keys = { "texture", "compress", "astc", "etc2", "dxt", "colorspace", "mipstripping", "webgl", "gpuskinning", "streaming", "splash" };
            while (it.Next(enter))
            {
                enter = it.propertyType == SerializedPropertyType.Generic;
                var name = it.propertyPath.ToLowerInvariant();
                if (!keys.Any(k => name.Contains(k))) continue;
                string v;
                switch (it.propertyType)
                {
                    case SerializedPropertyType.Integer: case SerializedPropertyType.Enum: case SerializedPropertyType.LayerMask: v = it.intValue.ToString(); break;
                    case SerializedPropertyType.Boolean: v = it.boolValue.ToString(); break;
                    case SerializedPropertyType.Float: v = it.floatValue.ToString("0.###"); break;
                    case SerializedPropertyType.String: v = it.stringValue; break;
                    default: continue;
                }
                r.playerSettingsTextureRelated.Add(new KeyValue { key = it.propertyPath, value = v });
            }
        }

        static void DumpStatics(Type type, List<KeyValue> into, params string[] filters)
        {
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (filters.Length > 0 && !filters.Any(f => p.Name.ToLowerInvariant().Contains(f))) continue;
                try { var v = p.GetValue(null); into.Add(new KeyValue { key = type.Name + "." + p.Name, value = v == null ? "null" : v.ToString() }); }
                catch (Exception) { into.Add(new KeyValue { key = type.Name + "." + p.Name, value = "(not readable)" }); }
            }
        }
    }
}
