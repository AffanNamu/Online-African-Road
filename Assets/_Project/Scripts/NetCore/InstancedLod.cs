using System;

namespace ARO.NetCore
{
    /// <summary>
    /// RB-15 (owner approval of 2026-11-16): the level-of-detail choice the instanced building renderer (World/PropInstancer) makes for each building, the same choice Unity's LODGroup makes
    /// for a prefab instance, so batching never changes which detail a building shows. Unity's rule (perspective camera): relative height = size x lodBias / (2 x distance x tan(fov / 2)),
    /// where size is the LODGroup's world size and distance is from the camera to its reference point; the shown LOD is the first whose screenRelativeTransitionHeight it reaches; below the
    /// last threshold the object is culled.
    /// </summary>
    public static class InstancedLod
    {
        /// <summary>Most instances one instanced draw carries (WebGL 2 keeps per-instance matrices in a uniform buffer; 511 fits the 64 KB the shader library assumes on every platform).</summary>
        public const int MaxPerDraw = 511;

        public static float RelativeHeight(float worldSize, float distance, float fovDeg, float lodBias)
        {
            if (distance < 1e-4f) return float.PositiveInfinity;
            double half = Math.Tan(fovDeg * Math.PI / 360.0);
            return (float)(worldSize * lodBias / (2.0 * distance * half));
        }

        /// <summary>The LOD shown at a relative height: the first i with height at or above thresholds[i] (thresholds descend, as in a LODGroup), -1 when culled.</summary>
        public static int Select(float relativeHeight, float[] thresholds)
        {
            if (thresholds == null) return -1;
            for (int i = 0; i < thresholds.Length; i++) if (relativeHeight >= thresholds[i]) return i;
            return -1;
        }

        /// <summary>The LOD of an instance, or -1 when it is culled by its LODs or beyond its cull distance.</summary>
        public static int Choose(float worldSize, float distance, float fovDeg, float lodBias, float[] thresholds, float cullDistance)
        {
            if (distance > cullDistance) return -1;
            return Select(RelativeHeight(worldSize, distance, fovDeg, lodBias), thresholds);
        }

        /// <summary>Instanced draws needed for `count` instances of one (mesh, material) pair.</summary>
        public static int Draws(int count) => count <= 0 ? 0 : (count + MaxPerDraw - 1) / MaxPerDraw;

        /// <summary>True when thresholds are a valid LODGroup set: in (0, 1], strictly descending.</summary>
        public static bool ValidThresholds(float[] t)
        {
            if (t == null || t.Length == 0) return false;
            for (int i = 0; i < t.Length; i++) { if (!(t[i] > 0f && t[i] <= 1f)) return false; if (i > 0 && !(t[i] < t[i - 1])) return false; }
            return true;
        }
    }
}
