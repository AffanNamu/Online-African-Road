using System;
using System.Collections.Generic;
using System.Linq;

namespace ARO.NetCore
{
    /// <summary>
    /// Splits the visible draw-call estimate into named groups so a budget overrun can be traced to what causes it (review and diagnostics only; no game system uses it).
    /// The estimate has the same definition as PerfProbe's: one draw per material slot of every visible mesh renderer, nothing batched. Besides the draws each group also records how many DISTINCT
    /// materials, meshes and (mesh, material) pairs it uses: the number of pairs is the floor that perfect GPU instancing of identical meshes could reach, the number of materials the floor
    /// that perfect merging of everything with the same material could reach. Both are bounds for judging an optimisation, not predictions.
    /// </summary>
    public sealed class DrawBreakdown
    {
        public sealed class Group
        {
            public string Name; public int Renderers, Draws; public long Triangles;
            public readonly HashSet<int> Materials = new HashSet<int>(), Meshes = new HashSet<int>();
            public readonly HashSet<long> Pairs = new HashSet<long>();
        }

        readonly Dictionary<string, Group> _groups = new Dictionary<string, Group>(StringComparer.Ordinal);

        /// <summary>Adds one visible renderer to a group. `materialIds` holds the identity of each material slot that counts as a draw (its length is `draws`).</summary>
        public void Add(string group, int meshId, int[] materialIds, int draws, long triangles)
        {
            if (!_groups.TryGetValue(group, out var g)) _groups[group] = g = new Group { Name = group };
            g.Renderers++; g.Draws += draws; g.Triangles += triangles; g.Meshes.Add(meshId);
            if (materialIds == null) return;
            foreach (var m in materialIds) { g.Materials.Add(m); g.Pairs.Add(((long)meshId << 32) ^ (uint)m); }
        }

        public Group Get(string name) => _groups.TryGetValue(name, out var g) ? g : new Group { Name = name };

        /// <summary>One line: "name renderers=.. draws=.. tris=.. materials=.. meshes=.. pairs=..". An empty group reports zeros.</summary>
        public string Summary(string name)
        {
            var g = Get(name);
            return $"{name} renderers={g.Renderers} draws={g.Draws} tris={g.Triangles} distinctMaterials={g.Materials.Count} distinctMeshes={g.Meshes.Count} distinctMeshMaterialPairs={g.Pairs.Count}";
        }

        /// <summary>The groups whose name starts with `prefix`, most draws first (ties by name), as "name=draws/renderers/tris" separated by commas, with the prefix removed from the names.</summary>
        public string Ranked(string prefix)
        {
            var rows = _groups.Values.Where(g => g.Name.StartsWith(prefix, StringComparison.Ordinal)).OrderByDescending(g => g.Draws).ThenBy(g => g.Name, StringComparer.Ordinal);
            return string.Join(", ", rows.Select(g => $"{g.Name.Substring(prefix.Length)}={g.Draws}/{g.Renderers}/{g.Triangles}"));
        }

        /// <summary>The sum of the draws of the groups with the given names (a group that does not exist counts as zero).</summary>
        public int Draws(params string[] names) => names.Sum(n => Get(n).Draws);
    }
}
