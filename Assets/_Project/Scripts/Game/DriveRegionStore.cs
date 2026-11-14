using ARO.NetCore;
using UnityEngine;

namespace ARO.Game
{
    /// <summary>
    /// Where the player drives (owner decision of 2026-11-14: Kano in the route selection), kept on this device (PlayerPrefs: the browser's storage in WebGL). The game loads the chosen route at start;
    /// choosing another one reloads the world (the streamer, traffic, lights and adverts are all built for one route). Test pages ignore the stored choice, so CI always drives what its URL says.
    /// </summary>
    public static class DriveRegionStore
    {
        const string Key = "aro.driveRoute", AfterLoadKey = "aro.driveAfterLoad";

        /// <summary>The region this run of the game loaded (set once by GameBootstrap).</summary>
        public static DriveRegion Current { get; private set; } = DriveRegions.Get(null);

        /// <summary>The stored choice (the shipped road when nothing valid is stored).</summary>
        public static DriveRegion Stored() { try { return DriveRegions.Get(PlayerPrefs.GetString(Key, "")); } catch { return DriveRegions.Get(null); } }

        public static void SetCurrent(DriveRegion r) => Current = r ?? DriveRegions.Get(null);

        /// <summary>Remember a new choice and ask the next start to drive straight away (the player chose to drive there).</summary>
        public static void Choose(string routeId)
        {
            try { PlayerPrefs.SetString(Key, DriveRegions.Get(routeId).RouteId); PlayerPrefs.SetInt(AfterLoadKey, 1); PlayerPrefs.Save(); } catch { }
        }

        /// <summary>True once after a reload the player asked for (then cleared).</summary>
        public static bool TakeDriveAfterLoad()
        {
            try { if (PlayerPrefs.GetInt(AfterLoadKey, 0) != 1) return false; PlayerPrefs.DeleteKey(AfterLoadKey); PlayerPrefs.Save(); return true; } catch { return false; }
        }
    }
}
