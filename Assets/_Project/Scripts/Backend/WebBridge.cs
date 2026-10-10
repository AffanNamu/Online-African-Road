using System.Runtime.InteropServices;
using UnityEngine;

namespace ARO.Backend
{
    /// <summary>Thin bridge to the browser (WebGL only; harmless elsewhere). Implemented in Plugins/WebGL/ARO_Web.jslib.</summary>
    public static class WebBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void ARO_Redirect(string url);
        [DllImport("__Internal")] static extern void ARO_ClearUrlHash();
        public const bool IsWeb = true;
        public static void Redirect(string url) => ARO_Redirect(url);
        public static void ClearUrlHash() => ARO_ClearUrlHash();
#else
        public const bool IsWeb = false;
        public static void Redirect(string url) => Application.OpenURL(url);
        public static void ClearUrlHash() { }
#endif
        /// <summary>The page address without query or fragment: where OAuth providers should send the player back to.</summary>
        public static string PageBase
        {
            get { string u = Application.absoluteURL ?? ""; int i = u.IndexOfAny(new[] { '#', '?' }); return i >= 0 ? u.Substring(0, i) : u; }
        }
    }
}
