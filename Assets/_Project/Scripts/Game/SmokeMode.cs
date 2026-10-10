using UnityEngine;

namespace ARO.Game
{
    /// <summary>
    /// Test hook for CI: opening the WebGL build with "?smoke=drive" skips sign-in and drops a free-drive truck onto the road,
    /// so the browser test can check the world, physics and controls without credentials. Never reachable from the UI.
    /// </summary>
    public static class SmokeMode
    {
        public static bool Drive => Application.absoluteURL != null && Application.absoluteURL.Contains("smoke=drive");
    }
}
