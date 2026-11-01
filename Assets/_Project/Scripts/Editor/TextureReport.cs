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
            public string path, textureType, importedFormat; public int importedWidth, importedHeight, importedMips; public long runtimeBytesInEditor, gpuBytes;
            public bool sRGB, mipmaps, readable, streamingMips, alphaIsTransparency; public string defaultCompression, wrap; public int defaultMaxSize, aniso;
            public List<PlatformInfo> platforms = new List<PlatformInfo>();
        }
        [Serializable] public class KeyValue { public string key, value; }
        [Serializable] public class Experiment { public string name, note, format; public int width, height, mips; public long gpuBytes; public bool sRGB; public string textureType, npotScale, compression; }
        [Serializable] public class Header { public string unityVersion, activeBuildTarget, colorSpace; public long textureCount, totalRuntimeBytesInEditor, totalGpuBytes; }
        [Serializable] public class Group { public string folder; public int count; public string formats; public long runtimeBytes; }
        [Serializable] public class QualityLevel { public string name; public int globalTextureMipmapLimit, anisotropicFiltering, streamingMipmapsActive; }
        [Serializable] public class Report
        {
            public string unityVersion, activeBuildTarget, activeBuildTargetGroup, colorSpace;
            public List<KeyValue> playerSettingsTextureRelated = new List<KeyValue>(), webglPlayerSettings = new List<KeyValue>(), editorUserBuildSettings = new List<KeyValue>();
            public List<QualityLevel> qualityLevels = new List<QualityLevel>();
            public List<TextureInfo> textures = new List<TextureInfo>(); public List<Group> groups = new List<Group>(); public List<Experiment> experiments = new List<Experiment>();
            public long totalRuntimeBytesInEditor, totalGpuBytes, textureCount;
            public List<string> notes = new List<string>();
        }

        public static void Run()
        {
            try
            {
                var r = Build();
                Directory.CreateDirectory("Builds/TextureReport");
                File.WriteAllText("Builds/TextureReport/texture_report.json", JsonUtility.ToJson(r, true));
                // One compact line per item: the pretty-printed JSON was cut off in the CI log after a few dozen textures.
                Console.WriteLine("TEXTURE_REPORT_BEGIN");
                Console.WriteLine("TR_HEADER " + JsonUtility.ToJson(new Header { unityVersion = r.unityVersion, activeBuildTarget = r.activeBuildTarget, colorSpace = r.colorSpace, textureCount = r.textureCount, totalRuntimeBytesInEditor = r.totalRuntimeBytesInEditor, totalGpuBytes = r.totalGpuBytes }));
                foreach (var x in r.playerSettingsTextureRelated) Console.WriteLine("TR_PLAYER " + x.key + "=" + x.value);
                foreach (var x in r.editorUserBuildSettings) Console.WriteLine("TR_EDITORBUILD " + x.key + "=" + x.value);
                foreach (var t in r.textures) Console.WriteLine("TR_TEXTURE " + JsonUtility.ToJson(t));
                foreach (var g in r.groups) Console.WriteLine("TR_GROUP " + JsonUtility.ToJson(g));
                foreach (var x in r.experiments) Console.WriteLine("TR_EXPERIMENT " + JsonUtility.ToJson(x));
                Console.WriteLine("TEXTURE_REPORT_END"); Console.Out.Flush();
                System.Threading.Thread.Sleep(3000);
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
                if (tex != null) { t.importedFormat = tex.format.ToString(); t.importedWidth = tex.width; t.importedHeight = tex.height; t.importedMips = tex.mipmapCount; t.runtimeBytesInEditor = Profiler.GetRuntimeMemorySizeLong(tex); t.gpuBytes = GpuBytes(tex); r.totalRuntimeBytesInEditor += t.runtimeBytesInEditor; r.totalGpuBytes += t.gpuBytes; }
                if (path.StartsWith("Assets/_Project/Resources/Flags/")) { var gr = r.groups.FirstOrDefault(g => g.folder == "Resources/Flags") ?? NewGroup(r, "Resources/Flags"); gr.count++; gr.runtimeBytes += t.runtimeBytesInEditor; if (!gr.formats.Contains(t.importedFormat + " ")) gr.formats += t.importedFormat + " "; continue; }
                r.textures.Add(t);
            }
            r.textureCount = r.textures.Count;
            try { RunExperiments(r); } catch (Exception e) { r.notes.Add("experiments failed: " + e.Message); }
            r.notes.Add("imported* and runtimeBytesInEditor describe the texture as imported for the ACTIVE build target (" + r.activeBuildTarget + "); other targets show their overrides and the format Unity would choose automatically (automaticFormat).");
            r.notes.Add("Textures created at runtime in code (WorldMaterials.ProcTex) are not assets and are not listed.");
            return r;
        }

        // --- GPU bytes of a texture from its format and mip chain (block formats counted per block): independent of the editor's doubled CPU+GPU figure
        static long GpuBytes(Texture2D t)
        {
            int bw = 1, bh = 1, bb; var f = t.format;
            switch (f)
            {
                case TextureFormat.DXT1: case TextureFormat.DXT1Crunched: case TextureFormat.BC4: case TextureFormat.ETC_RGB4: case TextureFormat.ETC2_RGB: case TextureFormat.ETC2_RGBA1: case TextureFormat.EAC_R: bw = bh = 4; bb = 8; break;
                case TextureFormat.DXT5: case TextureFormat.DXT5Crunched: case TextureFormat.BC5: case TextureFormat.BC6H: case TextureFormat.BC7: case TextureFormat.ETC2_RGBA8: case TextureFormat.EAC_RG: bw = bh = 4; bb = 16; break;
                case TextureFormat.ASTC_4x4: bw = bh = 4; bb = 16; break; case TextureFormat.ASTC_5x5: bw = bh = 5; bb = 16; break; case TextureFormat.ASTC_6x6: bw = bh = 6; bb = 16; break;
                case TextureFormat.ASTC_8x8: bw = bh = 8; bb = 16; break; case TextureFormat.ASTC_10x10: bw = bh = 10; bb = 16; break; case TextureFormat.ASTC_12x12: bw = bh = 12; bb = 16; break;
                case TextureFormat.RGB24: bb = 3; break; case TextureFormat.R8: case TextureFormat.Alpha8: bb = 1; break; case TextureFormat.RG16: bb = 2; break; case TextureFormat.RGBAHalf: bb = 8; break;
                default: bb = 4; break;
            }
            long sum = 0; int w = t.width, h = t.height;
            for (int m = 0; m < Math.Max(1, t.mipmapCount); m++) { sum += (long)((Math.Max(1, w) + bw - 1) / bw) * ((Math.Max(1, h) + bh - 1) / bh) * bb; w >>= 1; h >>= 1; }
            return sum;
        }

        static Experiment Describe(string name, string note, string path)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path); var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            return new Experiment { name = name, note = note, format = tex.format.ToString(), width = tex.width, height = tex.height, mips = tex.mipmapCount, gpuBytes = GpuBytes(tex), sRGB = imp.sRGBTexture,
                                    textureType = imp.textureType.ToString(), npotScale = imp.npotScale.ToString(), compression = imp.textureCompression.ToString() };
        }

        static void RunExperiments(Report r)
        {
            // 1. Is the world map's missing compression an import-order effect? Force a reimport now that every editor script is compiled and loaded, and look again.
            const string wm = "Assets/_Project/Resources/WorldMap/west_africa_world_map.jpg";
            if (AssetImporter.GetAtPath(wm) is TextureImporter)
            {
                r.experiments.Add(Describe("world_map_as_imported", "format after the cold import, before any reimport", wm));
                AssetDatabase.ImportAsset(wm, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                r.experiments.Add(Describe("world_map_after_forced_reimport", "same asset after ImportAsset(ForceUpdate), all postprocessors loaded", wm));
            }
            // 2. What do NEW textures with default importer settings become on this target? (what Blender-made PBR textures will be)
            const string dir = "Assets/_TextureProbe";
            try
            {
                Directory.CreateDirectory(dir);
                void Make(string file, int w, int h, bool alpha)
                {
                    var t = new Texture2D(w, h, alpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false); var px = new Color32[w * h];
                    for (int i = 0; i < px.Length; i++) { byte v = (byte)(((i % w) * 7 + (i / w) * 13) & 255); px[i] = new Color32(v, (byte)(255 - v), (byte)(v / 2), alpha ? (byte)(i % 3 == 0 ? 128 : 255) : (byte)255); }
                    t.SetPixels32(px); File.WriteAllBytes(dir + "/" + file, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
                }
                Make("albedo_2048.png", 2048, 2048, false); Make("albedo_alpha_1024.png", 1024, 1024, true); Make("normal_1024.png", 1024, 1024, false); Make("orm_1024.png", 1024, 1024, false);
                Make("npot_default_1672x944.png", 1672, 944, false); Make("npot_none_1672x944.png", 1672, 944, false); Make("po2_none_1024x512.png", 1024, 512, false);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                void Tweak(string file, Action<TextureImporter> f) { var p = dir + "/" + file; var imp = (TextureImporter)AssetImporter.GetAtPath(p); f(imp); imp.SaveAndReimport(); }
                Tweak("normal_1024.png", i => { i.textureType = TextureImporterType.NormalMap; });
                Tweak("orm_1024.png", i => { i.sRGBTexture = false; });
                Tweak("npot_none_1672x944.png", i => { i.npotScale = TextureImporterNPOTScale.None; });
                Tweak("po2_none_1024x512.png", i => { i.npotScale = TextureImporterNPOTScale.None; });
                foreach (var f in new[] { "albedo_2048.png", "albedo_alpha_1024.png", "normal_1024.png", "orm_1024.png", "npot_default_1672x944.png", "npot_none_1672x944.png", "po2_none_1024x512.png" })
                    r.experiments.Add(Describe("probe_" + Path.GetFileNameWithoutExtension(f), "new texture, default importer settings except as named", dir + "/" + f));
            }
            finally { AssetDatabase.DeleteAsset(dir); }
        }

        static Group NewGroup(Report r, string folder) { var g = new Group { folder = folder, formats = "" }; r.groups.Add(g); return g; }
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
