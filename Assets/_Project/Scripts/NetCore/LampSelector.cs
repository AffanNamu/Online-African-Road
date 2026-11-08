using System;
using System.Collections.Generic;

namespace ARO.NetCore
{
    /// <summary>
    /// Chooses which few lamps of all the loaded ones get a real light. The look of the street does not depend on this (halos, pools and emissive heads cover every lamp); real lights add
    /// light on vehicles and nearby props, and each one costs a per-pixel light, so there are only a handful. Pure and tested.
    /// Rules: nearest first; lamps ahead of the player count as nearer than lamps behind (the road in front is what the headlights and the eye work on); a lamp that already has a light keeps
    /// it unless another is clearly nearer (hysteresis), so lights do not swap back and forth as the player moves; nothing beyond the radius.
    /// </summary>
    public sealed class LampSelector
    {
        public readonly int Budget; public readonly float Radius; readonly float _keep;
        readonly List<long> _selected = new List<long>();
        public IReadOnlyList<long> Selected => _selected;

        /// <param name="keep">A selected lamp's distance is multiplied by this (below 1) before comparing, so a challenger must be clearly nearer.</param>
        public LampSelector(int budget, float radius, float keep = 0.8f) { Budget = Math.Max(0, budget); Radius = radius; _keep = keep; }

        struct Scored { public long Key; public float Score; }

        /// <summary>Re-selects. <paramref name="forwardX"/>/<paramref name="forwardZ"/> is the direction the player looks or drives (need not be normalised). Returns the keys now selected (also in <see cref="Selected"/>).</summary>
        public IReadOnlyList<long> Update(IList<Lamp> lamps, V3 player, float forwardX, float forwardZ)
        {
            float fl = (float)Math.Sqrt(forwardX * forwardX + forwardZ * forwardZ); if (fl > 1e-4f) { forwardX /= fl; forwardZ /= fl; } else { forwardX = 0f; forwardZ = 0f; }
            var had = new HashSet<long>(_selected); var scored = new List<Scored>();
            float r2 = Radius * Radius;
            foreach (var l in lamps)
            {
                if (!l.Real) continue;
                float dx = l.Pos.X - player.X, dz = l.Pos.Z - player.Z, d2 = dx * dx + dz * dz;
                if (d2 > r2) continue;
                float d = (float)Math.Sqrt(d2), ahead = d > 1e-3f ? (dx * forwardX + dz * forwardZ) / d : 0f;     // +1 straight ahead, -1 straight behind
                float score = d * (1.15f - 0.3f * ahead);                                                         // ahead counts up to 30% nearer, behind up to 15% farther
                if (had.Contains(l.Key)) score *= _keep;
                scored.Add(new Scored { Key = l.Key, Score = score });
            }
            scored.Sort((a, b) => { int c = a.Score.CompareTo(b.Score); return c != 0 ? c : a.Key.CompareTo(b.Key); });
            _selected.Clear();
            var seen = new HashSet<long>();
            foreach (var s in scored) { if (_selected.Count >= Budget) break; if (seen.Add(s.Key)) _selected.Add(s.Key); }
            return _selected;
        }
    }
}
