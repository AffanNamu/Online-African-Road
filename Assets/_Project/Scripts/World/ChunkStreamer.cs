using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ARO.World
{
    /// <summary>
    /// Streams the world in square chunks around a tracked target. Chunks load across frames under a
    /// time budget, far chunks unload, and roadside props come from a pool. Content is generated from
    /// the RouteDefinition, so a scene never holds the whole world.
    /// Hierarchy mapping: World > Region > Country > Zone > Sector > Chunk (this class owns Chunk level;
    /// higher levels are data in the backend `countries/cities/locations` tables).
    /// </summary>
    public class ChunkStreamer : MonoBehaviour
    {
        public RouteDefinition route;
        public Transform target;
        public float chunkSize = 200f;
        public int loadRadius = 3;        // chunks
        public int unloadRadius = 5;
        public float frameBudgetMs = 3f;
        public Material roadGood, roadWorn, roadDamaged, groundMat;
        public Material[] buildingMats;

        public int LoadedChunkCount => _chunks.Count;
        public int PooledProps => _pool.Count;

        readonly Dictionary<Vector2Int, Chunk> _chunks = new Dictionary<Vector2Int, Chunk>();
        readonly HashSet<Vector2Int> _queued = new HashSet<Vector2Int>();
        readonly Queue<Vector2Int> _loadQueue = new Queue<Vector2Int>();
        readonly Stack<GameObject> _pool = new Stack<GameObject>();
        Vector2Int _lastCentre = new Vector2Int(int.MinValue, 0);

        class Chunk { public GameObject root; public List<GameObject> props = new List<GameObject>(); }

        void Start() { StartCoroutine(Pump()); }

        Vector2Int ToChunk(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / chunkSize), Mathf.FloorToInt(p.z / chunkSize));

        void Update()
        {
            if (target == null || route == null || route.nodes.Length < 2) return;
            var centre = ToChunk(target.position);
            if (centre == _lastCentre) return;
            _lastCentre = centre;
            // Queue nearest-first so the road under the player appears first.
            var wanted = new List<Vector2Int>();
            for (int x = -loadRadius; x <= loadRadius; x++)
                for (int z = -loadRadius; z <= loadRadius; z++)
                {
                    var c = centre + new Vector2Int(x, z);
                    if (!_chunks.ContainsKey(c) && !_queued.Contains(c) && ChunkTouchesRoute(c)) wanted.Add(c);
                }
            wanted.Sort((a, b) => (a - centre).sqrMagnitude.CompareTo((b - centre).sqrMagnitude));
            foreach (var c in wanted) { _queued.Add(c); _loadQueue.Enqueue(c); }

            var toUnload = new List<Vector2Int>();
            foreach (var kv in _chunks)
                if (Mathf.Max(Mathf.Abs(kv.Key.x - centre.x), Mathf.Abs(kv.Key.y - centre.y)) > unloadRadius) toUnload.Add(kv.Key);
            foreach (var c in toUnload) Unload(c);
        }

        IEnumerator Pump()
        {
            var sw = new System.Diagnostics.Stopwatch();
            while (true)
            {
                sw.Restart();
                while (_loadQueue.Count > 0 && sw.Elapsed.TotalMilliseconds < frameBudgetMs)
                {
                    var c = _loadQueue.Dequeue(); _queued.Remove(c);
                    if (!_chunks.ContainsKey(c)) Build(c);
                }
                yield return null;
            }
        }

        bool ChunkTouchesRoute(Vector2Int c)
        {
            // Keep a margin so roadside content (60 m) is included.
            var min = new Vector3(c.x * chunkSize - 60f, 0, c.y * chunkSize - 60f);
            var max = new Vector3((c.x + 1) * chunkSize + 60f, 0, (c.y + 1) * chunkSize + 60f);
            for (int i = 1; i < route.nodes.Length; i++)
                if (SegmentIntersectsBox(route.nodes[i - 1].position, route.nodes[i].position, min, max)) return true;
            return false;
        }

        static bool SegmentIntersectsBox(Vector3 a, Vector3 b, Vector3 min, Vector3 max)
        {
            float t0 = 0f, t1 = 1f;
            return Clip(a.x, b.x - a.x, min.x, max.x, ref t0, ref t1) && Clip(a.z, b.z - a.z, min.z, max.z, ref t0, ref t1);
        }
        static bool Clip(float p0, float d, float lo, float hi, ref float t0, ref float t1)
        {
            if (Mathf.Abs(d) < 1e-6f) return p0 >= lo && p0 <= hi;
            float ta = (lo - p0) / d, tb = (hi - p0) / d;
            if (ta > tb) { var s = ta; ta = tb; tb = s; }
            t0 = Mathf.Max(t0, ta); t1 = Mathf.Min(t1, tb);
            return t0 <= t1;
        }

        void Build(Vector2Int c)
        {
            var origin = new Vector3(c.x * chunkSize, 0, c.y * chunkSize);
            var root = new GameObject($"Chunk_{c.x}_{c.y}");
            root.transform.SetParent(transform, false); root.transform.position = origin;
            var chunk = new Chunk { root = root };

            // Ground tile
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.SetParent(root.transform, false);
            ground.transform.localPosition = new Vector3(chunkSize / 2f, -0.05f, chunkSize / 2f);
            ground.transform.localScale = new Vector3(chunkSize / 10f, 1f, chunkSize / 10f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;
            ground.isStatic = true;

            var rng = new System.Random(route.seed ^ (c.x * 73856093) ^ (c.y * 19349663));
            for (int i = 1; i < route.nodes.Length; i++)
            {
                var a = route.nodes[i - 1]; var b = route.nodes[i];
                if (!SegmentIntersectsBox(a.position, b.position, origin, origin + new Vector3(chunkSize, 0, chunkSize))) continue;
                var mesh = RoadMeshBuilder.BuildSegment(a, b, origin);
                if (mesh == null) continue;
                var go = new GameObject("Road_" + i, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                go.transform.SetParent(root.transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                go.GetComponent<MeshCollider>().sharedMesh = mesh;
                go.GetComponent<MeshRenderer>().sharedMaterial =
                    a.surface == RoadSurface.Good ? roadGood : a.surface == RoadSurface.Worn ? roadWorn : roadDamaged;
                go.isStatic = true;
                chunk.props.Add(go);   // road meshes are not pooled (unique geometry); destroyed on unload
                PlaceRoadside(chunk, a, b, origin, rng);
            }
            _chunks[c] = chunk;
        }

        void PlaceRoadside(Chunk chunk, RouteNode a, RouteNode b, Vector3 origin, System.Random rng)
        {
            float len = Vector3.Distance(a.position, b.position);
            Vector3 dir = (b.position - a.position).normalized, right = Vector3.Cross(Vector3.up, dir);
            float density = a.zone switch { ZoneType.Urban => 22f, ZoneType.Commercial => 28f, ZoneType.Industrial => 40f, ZoneType.Rural => 45f, _ => 90f };
            for (float d = 0f; d < len; d += density * (0.7f + (float)rng.NextDouble() * 0.6f))
            {
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 p = a.position + dir * d + right * side * (a.width * 0.5f + 6f + (float)rng.NextDouble() * 14f);
                    var local = p - origin;
                    if (local.x < 0 || local.z < 0 || local.x >= chunkSize || local.z >= chunkSize) continue; // owned by neighbour
                    var go = RentProp();
                    go.transform.SetParent(chunk.root.transform, false);
                    go.transform.localPosition = new Vector3(local.x, 0f, local.z);
                    go.transform.localRotation = Quaternion.LookRotation(-right * side);
                    ShapeProp(go, a.zone, rng);
                    chunk.props.Add(go);
                }
            }
        }

        // Props are pooled cubes reshaped per zone (placeholder art; replace by prefab variants via Addressables).
        void ShapeProp(GameObject go, ZoneType zone, System.Random rng)
        {
            float h, w, dpt;
            switch (zone)
            {
                case ZoneType.Urban: h = 6f + (float)rng.NextDouble() * 18f; w = 8f + (float)rng.NextDouble() * 8f; dpt = 10f; break;
                case ZoneType.Commercial: h = 4f + (float)rng.NextDouble() * 6f; w = 10f + (float)rng.NextDouble() * 8f; dpt = 12f; break;
                case ZoneType.Industrial: h = 8f + (float)rng.NextDouble() * 6f; w = 20f + (float)rng.NextDouble() * 20f; dpt = 25f; break;
                case ZoneType.Rural: h = 2.5f + (float)rng.NextDouble() * 2f; w = 5f + (float)rng.NextDouble() * 3f; dpt = 5f; break;
                default: h = 0.4f; w = 0.4f; dpt = 0.4f; break; // highway: markers only
            }
            go.transform.localScale = new Vector3(w, h, dpt);
            go.transform.localPosition += Vector3.up * (h / 2f);
            if (buildingMats != null && buildingMats.Length > 0)
                go.GetComponent<Renderer>().sharedMaterial = buildingMats[rng.Next(buildingMats.Length)];
        }

        GameObject RentProp()
        {
            if (_pool.Count > 0) { var p = _pool.Pop(); p.SetActive(true); return p; }
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.isStatic = true; go.name = "Prop";
            return go;
        }

        void Unload(Vector2Int c)
        {
            var chunk = _chunks[c]; _chunks.Remove(c);
            foreach (var p in chunk.props)
            {
                if (p == null) continue;
                if (p.name == "Prop") { p.SetActive(false); p.transform.SetParent(transform, false); _pool.Push(p); }
                else
                {
                    var mf = p.GetComponent<MeshFilter>(); if (mf != null) Destroy(mf.sharedMesh);
                    Destroy(p);
                }
            }
            Destroy(chunk.root);
        }
    }
}
