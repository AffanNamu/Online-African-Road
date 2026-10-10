using System;

namespace ARO.NetCore
{
    /// <summary>Mirror of the server's level curve (supabase level_for_xp: level = 1 + floor(sqrt(xp / 100))). Display only; the server is the authority.</summary>
    public static class LevelMath
    {
        public static int LevelFor(long xp) => 1 + (int)Math.Floor(Math.Sqrt(Math.Max(0, xp) / 100.0));
        /// <summary>XP at which `level` begins (level 1 = 0, level 2 = 100, level 3 = 400, level 5 = 1,600).</summary>
        public static long XpAtLevel(int level) { long n = Math.Max(1, level) - 1; return 100L * n * n; }
        /// <summary>Progress inside the current level: xp earned since it began, xp the level spans, 0..1 fraction.</summary>
        public static (long into, long span, float fraction) Progress(long xp)
        {
            int lvl = LevelFor(xp); long start = XpAtLevel(lvl), next = XpAtLevel(lvl + 1);
            long into = Math.Max(0, xp) - start, span = next - start;
            return (into, span, span <= 0 ? 0f : Math.Min(1f, (float)into / span));
        }
    }
}
