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
        /// <summary>"?smoke=kanostreet&view=ref|front|side|street1|street2|top": no sign-in, the Kano residential street review scene (only with the private assets in the build; see ARO.World.KanoReviewScene).</summary>
        public static bool KanoStreet => Url != null && Url.Contains("smoke=kanostreet");
        /// <summary>"?smoke=drive&hold=1": once the truck has settled on the road it is frozen there, so screenshots from every camera are taken from the same place.</summary>
        public static bool Hold => Drive && Url.Contains("hold=1");
        /// <summary>"?smoke=drive&hold=1&probe=1": when the truck is frozen, log what is around it (renderers, ground heights) so a visual defect can be traced to a system instead of guessed at.</summary>
        public static bool Probe => Hold && Url.Contains("probe=1");
        /// <summary>"?smoke=drive&start=12000": the truck starts that many metres along the route (night and weather checks in the village, on the expressway, at a junction). -1 when absent.</summary>
        public static float StartMetres => Drive ? LightingHour.QueryNumber(Url, "start", 1f, 60000f) : -1f;
        /// <summary>"?smoke=drive&weather=clear|cloudy|rain|wet": a named test weather. Null when absent or unknown.</summary>
        public static string Weather => Drive && WeatherLook.TryPreset(NightLighting.QueryValue(Url, "weather"), out var p) ? p.Name : null;
        /// <summary>"?smoke=drive&highbeam=1" and "&hazard=1": the truck starts with its high beam or hazard lights on (CI renders them without a key press).</summary>
        /// <summary>"?smoke=drive&hour=16.8&hourrate=0.04": the clock runs from that hour at 0.04 game hours per real second once the truck is frozen (a dusk filmed in about a minute). -1 when absent.</summary>
        public static float HourRate => Drive ? LightingHour.QueryNumber(Url, "hourrate", 0.001f, 2f) : -1f;
        public static bool HighBeam => Drive && Url.Contains("highbeam=1");
        public static bool Hazard => Drive && Url.Contains("hazard=1");
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
