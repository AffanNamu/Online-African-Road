using System.Threading.Tasks;
using UnityEngine;

namespace ARO.Backend
{
    /// <summary>Waits without threads (WebGL has none): yields once per frame until the unscaled clock has advanced.</summary>
    public static class Wait
    {
        public static async Task Seconds(float seconds)
        {
            if (seconds <= 0f) { await Task.Yield(); return; }
            float until = Time.unscaledTime + seconds;
            while (Time.unscaledTime < until) await Task.Yield();
        }
    }
}
