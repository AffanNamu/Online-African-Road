using ARO.NetCore;
using UnityEngine;

namespace ARO.Game
{
    /// <summary>
    /// Test hooks for CI. "?smoke=drive" skips sign-in and drops a free-drive truck onto the road; "?smoke=worldmap" opens the map with fixture data.
    /// The backend scenarios use the REAL game against a throw-away backend: "?backend=...&anon=...&autologin=email|password" (loopback pages only,
    /// see BackendOverride) and "?fault=..." to inject request failures. None of this is reachable from the UI, and none of it works on a public page.
    /// </summary>
    public static class SmokeMode
    {
        static string Url => Application.absoluteURL;
        public static bool WorldMap => Url != null && Url.Contains("smoke=worldmap");
        public static bool Dashboard => Url != null && Url.Contains("smoke=dashboard");
        public static bool Drive => Url != null && Url.Contains("smoke=drive");
        /// <summary>"?smoke=drive&hold=1": once the truck has settled on the road it is frozen there, so screenshots from every camera are taken from the same place.</summary>
        public static bool Hold => Drive && Url.Contains("hold=1");
        /// <summary>The page is a loopback test page that asked for a scripted sign-in on a throw-away backend.</summary>
        public static bool E2E => BackendOverride.PageIsLoopback(Url) && !string.IsNullOrEmpty(BackendOverride.Query(Url, "autologin"));
        /// <summary>The browser tests need the on-screen click positions of the current screen.</summary>
        public static bool LogTargets => WorldMap || Dashboard || E2E;

        /// <summary>"autologin=email|password" on a loopback page.</summary>
        public static bool AutoLogin(out string email, out string password)
        {
            email = password = null;
            if (!BackendOverride.PageIsLoopback(Url)) return false;
            string v = BackendOverride.Query(Url, "autologin"); if (string.IsNullOrEmpty(v)) return false;
            int bar = v.IndexOf('|'); if (bar <= 0) return false;
            email = v.Substring(0, bar); password = v.Substring(bar + 1); return true;
        }
    }
}
