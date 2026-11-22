using System.Collections;
using System.Collections.Generic;
using ARO.NetCore;
using UnityEngine;
using UnityEngine.Networking;

namespace ARO.World
{
    /// <summary>
    /// Real surfaces for the roads (owner, 2026-11-22: "only highly realistic things"): photographs of real asphalt, gravel, dry soil, concrete and pavers from Poly Haven, every one CC0 1.0,
    /// replace the procedural textures of the road slots they name. The game repository holds no binaries: CI downloads and checks them (tools/textures/fetch_scanned.py,
    /// tools/textures/scanned_textures.json) into StreamingAssets/Scanned before the build. They are streamed after start-up, so the initial download (the 6 MB Resources budget) does not grow;
    /// until they arrive, and in a build without them, the procedural textures stay. Each picture's brightness tint (its mean brightness scaled to the old surface's, within 0.6 to 1.5, so the
    /// lighting checks keep their meaning) is baked into its pixels: the weather system rewrites the road materials' base colour every frame. One picture covers its real size on the ground
    /// (the road UVs repeat every RoadGeometry.TileMetres). The Low tier halves each picture (512 px) to save memory on phones.
    /// </summary>
    public static class ScannedTextures
    {
        [System.Serializable] class Entry { public string slot, asset; public float metres = 2f, tint = 1f; }
        [System.Serializable] class Manifest { public Entry[] textures; }

        public static int Applied { get; private set; }

        static int SlotOf(string s) => s == "Asphalt" ? RoadSub.Asphalt : s == "Shoulder" ? RoadSub.Shoulder : s == "Verge" ? RoadSub.Verge : s == "Concrete" ? RoadSub.Concrete
                                      : s == "Kerb" ? RoadSub.Kerb : s == "Sidewalk" ? RoadSub.Sidewalk : -1;

        public static IEnumerator Load(WorldMaterials w, bool half)
        {
            if (w == null) yield break;
            string root = Application.streamingAssetsPath + "/Scanned/"; if (!root.Contains("://")) root = "file://" + root;
            Manifest man = null;
            using (var r = UnityWebRequest.Get(root + "manifest.json"))
            {
                yield return r.SendWebRequest();
                if (r.result != UnityWebRequest.Result.Success) { Debug.Log("[Materials] no scanned road textures in this build (" + r.error + "): the procedural textures stay"); yield break; }
                try { man = JsonUtility.FromJson<Manifest>(r.downloadHandler.text); } catch (System.Exception e) { Debug.LogWarning("[Materials] scanned texture manifest unreadable: " + e.Message); yield break; }
            }
            if (man == null || man.textures == null) yield break;
            var byAsset = new Dictionary<string, Texture2D>(); int n = 0; var t0 = Time.realtimeSinceStartup;
            foreach (var e in man.textures)
            {
                int slot = SlotOf(e.slot); var m = slot >= 0 ? w.Road[slot] : null; if (m == null) continue;
                if (!byAsset.TryGetValue(e.asset ?? e.slot, out var tex))            // a picture shared by two slots (kerb and concrete) is decoded once, with the first slot's tint
                {
                    using (var r = UnityWebRequestTexture.GetTexture(root + e.slot + ".jpg", false))      // readable: its pixels are re-baked below (the decoder of the unitywebrequesttexture module, as AdService uses)
                    {
                        yield return r.SendWebRequest();
                        if (r.result != UnityWebRequest.Result.Success) { Debug.LogWarning("[Materials] scanned texture " + e.slot + " not loaded: " + r.error); continue; }
                        var src = DownloadHandlerTexture.GetContent(r);
                        tex = src != null ? Bake(src, e.tint, half, e.asset ?? e.slot) : null;
                        if (src != null) Object.Destroy(src);
                    }
                    if (tex == null) continue;
                    byAsset[e.asset ?? e.slot] = tex;
                }
                var old = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : m.mainTexture;
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex); m.mainTexture = tex;
                float sc = RoadGeometry.TileMetres / Mathf.Max(0.5f, e.metres);
                if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", new Vector2(sc, sc)); m.mainTextureScale = new Vector2(sc, sc);
                if (old != null && old != tex && !byAsset.ContainsValue(old as Texture2D)) Object.Destroy(old);
                n++;
            }
            Applied = n;
            Debug.Log($"[Materials] scanned road textures: {n} of {man.textures.Length} slots draw real photographs (Poly Haven, CC0), {byAsset.Count} pictures at {(half ? 512 : 1024)} px, {(Time.realtimeSinceStartup - t0) * 1000f:0} ms");
        }

        /// <summary>A decoded picture to a mipmapped, repeating texture with the brightness tint baked in (a linear-light scale, through a 256-entry table), optionally halved.</summary>
        static Texture2D Bake(Texture2D src, float tint, bool half, string name)
        {
            int w = src.width, h = src.height; var px = src.GetPixels32();
            var lut = new byte[256]; float k = Mathf.Clamp(tint, 0.3f, 2f);
            for (int i = 0; i < 256; i++) lut[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.LinearToGammaSpace(Mathf.GammaToLinearSpace(i / 255f) * k) * 255f), 0, 255);
            if (half && w >= 4 && h >= 4)
            {
                int hw = w / 2, hh = h / 2; var q = new Color32[hw * hh];
                for (int y = 0; y < hh; y++)
                    for (int x = 0; x < hw; x++)
                    {
                        var a = px[(2 * y) * w + 2 * x]; var b = px[(2 * y) * w + 2 * x + 1]; var c = px[(2 * y + 1) * w + 2 * x]; var d = px[(2 * y + 1) * w + 2 * x + 1];
                        q[y * hw + x] = new Color32((byte)((a.r + b.r + c.r + d.r + 2) >> 2), (byte)((a.g + b.g + c.g + d.g + 2) >> 2), (byte)((a.b + b.b + c.b + d.b + 2) >> 2), 255);
                    }
                px = q; w = hw; h = hh;
            }
            for (int i = 0; i < px.Length; i++) { var c = px[i]; px[i] = new Color32(lut[c.r], lut[c.g], lut[c.b], 255); }
            var tex = new Texture2D(w, h, TextureFormat.RGB24, true, false) { name = "Scanned_" + name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            tex.SetPixels32(px); tex.Apply(true, true);                // mipmaps (no shimmer on the road ahead), then the CPU copy is released
            return tex;
        }
    }
}
