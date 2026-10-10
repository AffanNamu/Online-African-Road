using System.Collections;
using System.Collections.Generic;
using ARO.NetCore;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ARO.World
{
    /// <summary>
    /// Streams the world in square chunks around a tracked target. Work is split into small stages (collision, road, terrain, props) that run
    /// under a per-frame time budget, nearest chunk first; far chunks unload, and chunks refresh their level of detail as the player moves.
    ///
    /// With route data (RouteDefinition.Model) every chunk gets conforming terrain, the generated road, roadside props merged into one mesh and,
    /// near the player only, physics colliders. Without route data the old flat-ground/ribbon/cube behaviour is used (LEGACY fallback).
    /// Hierarchy mapping: World > Region > Country > Zone > Sector > Chunk (this class owns the Chunk level; higher levels are backend data).
    /// </summary>
    public class ChunkStreamer : MonoBehaviour
    {
        public RouteDefinition route;
        public Transform target;
        public float chunkSize = 200f;
        public int loadRadius = 3;        // chunks (legacy path; the model path uses the quality tier)
        public int unloadRadius = 5;
        public float frameBudgetMs = 3f;
        public Material roadGood, roadWorn, roadDamaged, groundMat;   // legacy materials
        public Material[] buildingMats;
        public WorldMaterials materials;
        public QualityTier tier = QualityTiers.Medium;

        public int LoadedChunkCount => _chunks.Count;
        public int PooledProps => _pool.Count;
        public int PendingWork => _work.Count + _loadQueue.Count;
        public bool UsesRouteModel => route != null && route.Model != null && materials != null;

        public struct StreamStats { public int ChunksBuilt, StagesRun, Hitches; public float BuildMsTotal, WorstStageMs; }
        public StreamStats Stats;
        public int TerrainTriangles { get { int n = 0; foreach (var c in _chunks.Values) n += c.terrainTris; return n; } }
        public int RoadTriangles { get { int n = 0; foreach (var c in _chunks.Values) n += c.roadTris; return n; } }
        public int PropTriangles { get { int n = 0; foreach (var c in _chunks.Values) n += c.propTris; return n; } }
        public int PropCount { get { int n = 0; foreach (var c in _chunks.Values) n += c.instances != null ? c.instances.Count : 0; return n; } }

        enum Stage { Collision, Road, Terrain, Props }
        struct Work : System.IEquatable<Work>
        {
            public Vector2Int c; public Stage s;
            public bool Equals(Work o) => c == o.c && s == o.s;
            public override bool Equals(object o) => o is Work w && Equals(w);
            public override int GetHashCode() => c.GetHashCode() * 31 + (int)s;
        }

        class Chunk
        {
            public GameObject root; public Vector2Int id;
            public GameObject terrain, terrainCollision, props, propColliders, labels;
            public readonly List<GameObject> roads = new List<GameObject>();
            public readonly List<GameObject> productionProps = new List<GameObject>();
            public readonly List<Object> owned = new List<Object>();
            public List<PropInstance> instances;
            public int terrainCells, propLod = -1, terrainTris, roadTris, propTris;
            public bool hasCollision;
            public readonly List<GameObject> props_legacy = new List<GameObject>();   // legacy cubes
        }

        readonly Dictionary<Vector2Int, Chunk> _chunks = new Dictionary<Vector2Int, Chunk>();
        readonly Queue<Work> _work = new Queue<Work>();
        readonly HashSet<Work> _pending = new HashSet<Work>();
        // legacy path
        readonly HashSet<Vector2Int> _queued = new HashSet<Vector2Int>();
        readonly Queue<Vector2Int> _loadQueue = new Queue<Vector2Int>();
        readonly Stack<GameObject> _pool = new Stack<GameObject>();
        Vector2Int _lastCentre = new Vector2Int(int.MinValue, 0);

        void Start() { StartCoroutine(Pump()); }

        /// <summary>True when something solid (other than `ignore`'s own colliders) lies under p; y receives the highest such surface.</summary>
        public bool GroundBelow(Vector3 p, Transform ignore, out float y)
        {
            y = 0f; bool found = false;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 300f, Vector3.down, 600f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (ignore != null && h.collider.transform.IsChildOf(ignore)) continue;
                if (!found || h.point.y > y) { y = h.point.y; found = true; }
            }
            return found;
        }

        Vector2Int ToChunk(Vector3 p) => new Vector2Int(Mathf.FloorToInt(p.x / chunkSize), Mathf.FloorToInt(p.z / chunkSize));
        Vector3 Origin(Vector2Int c) => new Vector3(c.x * chunkSize, 0, c.y * chunkSize);
        int RingOf(Vector2Int c) => LodPolicy.Ring(c.x, c.y, _lastCentre.x, _lastCentre.y);

        void Update()
        {
            if (target == null || route == null || route.nodes.Length < 2) return;
            var centre = ToChunk(target.position);
            if (centre == _lastCentre) return;
            _lastCentre = centre;
            if (UsesRouteModel) UpdateModel(centre); else UpdateLegacy(centre);
        }

        // ============================================================================================= route-model path
        void UpdateModel(Vector2Int centre)
        {
            int load = Mathf.Max(1, tier.LoadRadiusChunks), unload = Mathf.Max(load + 1, tier.UnloadRadiusChunks);
            var wanted = new List<Vector2Int>();
            for (int x = -load; x <= load; x++)
                for (int z = -load; z <= load; z++) { var c = centre + new Vector2Int(x, z); if (!_chunks.ContainsKey(c)) wanted.Add(c); }
            wanted.Sort((a, b) => (a - centre).sqrMagnitude.CompareTo((b - centre).sqrMagnitude));
            foreach (var c in wanted) CreateChunk(c);

            var existing = new List<KeyValuePair<Vector2Int, Chunk>>(_chunks);
            var toUnload = new List<Vector2Int>();
            foreach (var kv in existing)
            {
                int ring = LodPolicy.Ring(kv.Key.x, kv.Key.y, centre.x, centre.y);
                if (ring > unload) { toUnload.Add(kv.Key); continue; }
                RefreshLod(kv.Value, ring);
            }
            foreach (var c in toUnload) Unload(c);
        }

        void CreateChunk(Vector2Int c)
        {
            var root = new GameObject($"Chunk_{c.x}_{c.y}"); root.transform.SetParent(transform, false); root.transform.position = Origin(c);
            var ch = new Chunk { root = root, id = c }; _chunks[c] = ch;
            int ring = RingOf(c);
            if (ring <= 2) Enqueue(c, Stage.Collision);      // ground first: the vehicle waits for it
            if (ChunkHasRoad(c)) Enqueue(c, Stage.Road);
            Enqueue(c, Stage.Terrain);
            Enqueue(c, Stage.Props);
            Stats.ChunksBuilt++;
        }

        void Enqueue(Vector2Int c, Stage s) { var w = new Work { c = c, s = s }; if (_pending.Add(w)) _work.Enqueue(w); }

        /// <summary>Does any road (main or side) pass within 20 m of the chunk? Uses the global ring tables, so it is cheap and exact enough.</summary>
        bool ChunkHasRoad(Vector2Int c)
        {
            float x0 = c.x * chunkSize - 20f, x1 = (c.x + 1) * chunkSize + 20f, z0 = c.y * chunkSize - 20f, z1 = (c.y + 1) * chunkSize + 20f;
            foreach (var cor in route.Model.AllCorridors)
                for (int i = 0; i < cor.RingX.Length; i++)
                    if (cor.RingX[i] >= x0 && cor.RingX[i] < x1 && cor.RingZ[i] >= z0 && cor.RingZ[i] < z1) return true;
            return false;
        }

        void RefreshLod(Chunk ch, int ring)
        {
            if (ch.terrain != null)
            {
                int cells = LodPolicy.TerrainCells(ring, tier);
                if (ch.terrainCells != cells) Enqueue(ch.id, Stage.Terrain);
                ch.terrain.GetComponent<MeshRenderer>().shadowCastingMode = LodPolicy.ShadowsEnabled(ring, tier) ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }
            if (ring <= 2 && !ch.hasCollision) Enqueue(ch.id, Stage.Collision);
            else if (ring >= 4 && ch.hasCollision) DropCollision(ch);
            foreach (var r in ch.roads) { if (r == null) continue; var mc = r.GetComponent<MeshCollider>(); if (mc != null) mc.enabled = ring <= 3; r.GetComponent<MeshRenderer>().shadowCastingMode = ring <= 1 ? ShadowCastingMode.On : ShadowCastingMode.Off; }
            if (ch.instances != null)
            {
                int lod = ring <= 1 ? 0 : ring == 2 ? 1 : 2;
                if (ch.propLod != lod) Enqueue(ch.id, Stage.Props);
            }
        }

        void DropCollision(Chunk ch)
        {
            if (ch.terrainCollision != null) { var mc = ch.terrainCollision.GetComponent<MeshCollider>(); if (mc != null && mc.sharedMesh != null) { ch.owned.Remove(mc.sharedMesh); Destroy(mc.sharedMesh); } Destroy(ch.terrainCollision); ch.terrainCollision = null; }
            ch.hasCollision = false;
        }

        IEnumerator Pump()
        {
            var sw = new System.Diagnostics.Stopwatch();
            while (true)
            {
                sw.Restart();
                while ((_work.Count > 0 || _loadQueue.Count > 0) && sw.Elapsed.TotalMilliseconds < frameBudgetMs)
                {
                    if (_work.Count > 0)
                    {
                        var w = _work.Dequeue(); _pending.Remove(w);
                        if (!_chunks.TryGetValue(w.c, out var ch)) continue;
                        double t0 = sw.Elapsed.TotalMilliseconds;
                        // One bad stage must never kill the pump: the world would silently stop loading (the truck then falls through nothing).
                        try { RunStage(ch, w.s); } catch (System.Exception e) { Debug.LogError($"[World] {w.s} stage of chunk {w.c} failed: {e}"); }
                        float ms = (float)(sw.Elapsed.TotalMilliseconds - t0);
                        Stats.StagesRun++; Stats.BuildMsTotal += ms; if (ms > Stats.WorstStageMs) Stats.WorstStageMs = ms;
                        if (ms > 60f) { Stats.Hitches++; if (Stats.Hitches <= 5) Debug.LogWarning($"[World] slow stage: {w.s} of chunk {w.c} took {ms:0} ms"); }
                    }
                    else
                    {
                        var c = _loadQueue.Dequeue(); _queued.Remove(c);
                        if (!_chunks.ContainsKey(c)) { try { BuildLegacy(c); } catch (System.Exception e) { Debug.LogError($"[World] chunk {c} failed to build: {e}"); } }
                    }
                }
                yield return null;
            }
        }

        void RunStage(Chunk ch, Stage s)
        {
            switch (s)
            {
                case Stage.Collision: StageCollision(ch); break;
                case Stage.Road: StageRoad(ch); break;
                case Stage.Terrain: StageTerrain(ch); break;
                case Stage.Props: StageProps(ch); break;
            }
        }

        void StageCollision(Chunk ch)
        {
            if (ch.hasCollision || RingOf(ch.id) > 3) return;
            var m = route.Model; var o = Origin(ch.id);
            var mb = TerrainGeometry.BuildCollision(m.Terrain, o.x, o.z, chunkSize, LodPolicy.CollisionCells, new V3(o.x, 0, o.z));
            var mesh = MeshUtil.ToCollisionMesh(mb, "TerrainCollision", TerrainSub.Ground);
            var go = new GameObject("TerrainCollision"); go.transform.SetParent(ch.root.transform, false);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            ch.owned.Add(mesh); ch.terrainCollision = go; ch.hasCollision = true;
        }

        void StageRoad(Chunk ch)
        {
            if (ch.roads.Count > 0) return;
            var m = route.Model; var o = Origin(ch.id); var org = new V3(o.x, 0, o.z); int ring = RingOf(ch.id);
            foreach (var cor in m.AllCorridors)
            {
                var mb = RoadGeometry.BuildChunk(cor, o.x, o.z, o.x + chunkSize, o.z + chunkSize, org, (x, z) => m.Terrain.HeightAt(x, z));
                if (mb.IsEmpty) continue;
                var mesh = MeshUtil.ToMesh(mb, "Road_" + cor.Id, out var used);
                var go = new GameObject("Road_" + cor.Id, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(ch.root.transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterials = MeshUtil.Pick(materials.Road, used);
                mr.shadowCastingMode = ring <= 1 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                var cm = MeshUtil.ToCollisionMesh(mb, "RoadCollision_" + cor.Id, RoadSub.Asphalt, RoadSub.Shoulder, RoadSub.Verge, RoadSub.Concrete, RoadSub.Metal);
                var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = cm; mc.enabled = ring <= 3;
                go.isStatic = true;
                ch.roads.Add(go); ch.owned.Add(mesh); ch.owned.Add(cm); ch.roadTris += MeshUtil.Triangles(mesh);
            }
        }

        Material TerrainMaterial(Chunk ch)
        {
            var o = Origin(ch.id); float cx = o.x + chunkSize * 0.5f, cz = o.z + chunkSize * 0.5f;
            if (route.Model.Main.Spline.Nearest(cx, cz, 4000f, out float s, out _, out _))
            {
                var z = route.Model.Main.ZoneAt(s);
                if (z == Zone.Urban || z == Zone.Commercial || z == Zone.Industrial) return materials.TerrainDry;
            }
            return materials.TerrainLush;
        }

        void StageTerrain(Chunk ch)
        {
            var m = route.Model; var o = Origin(ch.id); int ring = RingOf(ch.id); int cells = LodPolicy.TerrainCells(ring, tier);
            if (ch.terrain != null && ch.terrainCells == cells) return;
            var mb = TerrainGeometry.BuildTile(m.Terrain, o.x, o.z, chunkSize, cells, new V3(o.x, 0, o.z), true, true);
            var mesh = MeshUtil.ToMesh(mb, "Terrain", out _);
            if (ch.terrain == null)
            {
                ch.terrain = new GameObject("Terrain", typeof(MeshFilter), typeof(MeshRenderer)); ch.terrain.transform.SetParent(ch.root.transform, false);
                ch.terrain.GetComponent<MeshRenderer>().sharedMaterial = TerrainMaterial(ch); ch.terrain.isStatic = true;
            }
            var mf = ch.terrain.GetComponent<MeshFilter>();
            if (mf.sharedMesh != null) { ch.owned.Remove(mf.sharedMesh); Destroy(mf.sharedMesh); }
            mf.sharedMesh = mesh; ch.owned.Add(mesh);
            ch.terrain.GetComponent<MeshRenderer>().shadowCastingMode = LodPolicy.ShadowsEnabled(ring, tier) ? ShadowCastingMode.On : ShadowCastingMode.Off;
            ch.terrainCells = cells; ch.terrainTris = MeshUtil.Triangles(mesh);
        }

        void StageProps(Chunk ch)
        {
            var m = route.Model; var o = Origin(ch.id); var org = new V3(o.x, 0, o.z); int ring = RingOf(ch.id);
            if (ch.instances == null) ch.instances = PropScatter.PlaceChunk(m, o.x, o.z, o.x + chunkSize, o.z + chunkSize, tier.PropDensity);
            int lod = ring <= 1 ? 0 : ring == 2 ? 1 : 2;
            if (ch.propLod == lod) return;
            ClearProps(ch);

            float minDist = Mathf.Max(0, ring - 1) * chunkSize;
            var dev = new List<PropInstance>(); var prod = new List<PropInstance>();
            foreach (var inst in ch.instances) { if (PropLibrary.ProductionPrefab(inst.Prop) != null) prod.Add(inst); else dev.Add(inst); }
            System.Func<PropInstance, bool> inRange = inst => { var d = PropLibrary.Def(inst.Prop); return d != null && LodPolicy.PropCullDistance(d, tier) >= minDist; };

            var mb = PropBatcher.Merge(dev, PropLibrary.Geometry, lod, org, inRange);
            if (!mb.IsEmpty)
            {
                var mesh = MeshUtil.ToMesh(mb, "Props", out var used);
                ch.props = new GameObject("Props", typeof(MeshFilter), typeof(MeshRenderer)); ch.props.transform.SetParent(ch.root.transform, false);
                ch.props.GetComponent<MeshFilter>().sharedMesh = mesh;
                var mr = ch.props.GetComponent<MeshRenderer>(); mr.sharedMaterials = MeshUtil.Pick(materials.Prop, used);
                mr.shadowCastingMode = ring <= 1 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                ch.props.isStatic = true; ch.owned.Add(mesh); ch.propTris = MeshUtil.Triangles(mesh);
            }
            else ch.propTris = 0;

            if (ring <= 1)
            {
                var boxes = PropBatcher.BoxColliders(dev, PropLibrary.Def, inRange);
                if (boxes.Count > 0)
                {
                    ch.propColliders = new GameObject("PropColliders"); ch.propColliders.transform.SetParent(ch.root.transform, false);
                    foreach (var b in boxes)
                    {
                        var g = new GameObject("Box_" + b.Prop); g.transform.SetParent(ch.propColliders.transform, false);
                        g.transform.localPosition = new Vector3(b.Centre.X - o.x, b.Centre.Y, b.Centre.Z - o.z); g.transform.localRotation = Quaternion.Euler(0f, b.YawDeg, 0f);
                        g.AddComponent<BoxCollider>().size = new Vector3(b.Size.X, b.Size.Y, b.Size.Z);
                    }
                }
            }
            if (ring <= 2)
            {
                foreach (var inst in dev) if (!string.IsNullOrEmpty(inst.Text) && inRange(inst)) MakeLabel(ch, inst, o);
                foreach (var inst in prod)
                {
                    var go = Instantiate(PropLibrary.ProductionPrefab(inst.Prop), ch.root.transform);
                    go.transform.localPosition = new Vector3(inst.Pos.X - o.x, inst.Pos.Y, inst.Pos.Z - o.z); go.transform.localRotation = Quaternion.Euler(0f, inst.YawDeg, 0f); go.transform.localScale *= inst.Scale;
                    ch.productionProps.Add(go);
                }
            }
            ch.propLod = lod;
        }

        void ClearProps(Chunk ch)
        {
            if (ch.props != null) { var mf = ch.props.GetComponent<MeshFilter>(); if (mf.sharedMesh != null) { ch.owned.Remove(mf.sharedMesh); Destroy(mf.sharedMesh); } Destroy(ch.props); ch.props = null; }
            if (ch.propColliders != null) { Destroy(ch.propColliders); ch.propColliders = null; }
            if (ch.labels != null) { Destroy(ch.labels); ch.labels = null; }
            foreach (var g in ch.productionProps) if (g != null) Destroy(g);
            ch.productionProps.Clear();
        }

        /// <summary>Text on a sign face (a world-space UI canvas, only for the few hand-placed signs, billboards and fuel stations).</summary>
        void MakeLabel(Chunk ch, PropInstance inst, Vector3 o)
        {
            var d = PropLibrary.Def(inst.Prop); if (d == null) return;
            V3 local; float w, h;
            switch (inst.Prop)
            {
                case "road_sign": local = new V3(0f, d.heightM - 0.6f, 0.08f + d.depthM * 0.5f + 0.02f); w = d.widthM * 0.92f; h = 1.25f; break;
                case "billboard": local = new V3(0f, d.heightM - 2.1f, d.depthM * 0.5f + 0.03f); w = d.widthM * 0.9f; h = 3.6f; break;
                case "fuel_station": local = new V3(d.widthM * 0.1f, 5.5f, d.depthM * 0.375f + 0.03f); w = d.widthM * 0.55f; h = 0.5f; break;
                case "bus_stop": local = new V3(0f, d.heightM + 0.08f, d.depthM * 0.5f + 0.02f); w = d.widthM; h = 0.14f; break;
                default: return;
            }
            if (ch.labels == null) { ch.labels = new GameObject("Labels"); ch.labels.transform.SetParent(ch.root.transform, false); }
            var wp = PropBatcher.Place(local, inst.Pos, inst.YawDeg, inst.Scale);
            var go = new GameObject("Label_" + inst.Prop, typeof(RectTransform), typeof(Canvas)); go.transform.SetParent(ch.labels.transform, false);
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)go.transform; rt.sizeDelta = new Vector2(w * 100f, h * 100f); rt.localScale = Vector3.one * 0.01f * inst.Scale;
            go.transform.localPosition = new Vector3(wp.X - o.x, wp.Y, wp.Z - o.z);
            go.transform.localRotation = Quaternion.Euler(0f, inst.YawDeg + 180f, 0f);        // a world canvas reads from its -z side; the sign face looks along +z
            var tgo = new GameObject("Text", typeof(RectTransform), typeof(Text)); tgo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)tgo.transform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            var t = tgo.GetComponent<Text>(); t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); t.text = inst.Text; t.color = Color.white; t.raycastTarget = false;
            t.alignment = TextAnchor.MiddleCenter; t.resizeTextForBestFit = true; t.resizeTextMinSize = 8; t.resizeTextMaxSize = Mathf.Max(10, (int)(h * 100f * 0.5f)); t.fontStyle = FontStyle.Bold;
        }

        void Unload(Vector2Int c)
        {
            if (!_chunks.TryGetValue(c, out var ch)) return;
            _chunks.Remove(c);
            if (UsesRouteModel)
            {
                foreach (var o in ch.owned) if (o != null) Destroy(o);
                foreach (var g in ch.productionProps) if (g != null) Destroy(g);
                Destroy(ch.root); return;
            }
            UnloadLegacy(ch);
        }

        // ============================================================================================= LEGACY (no route data)
        void UpdateLegacy(Vector2Int centre)
        {
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

        void BuildLegacy(Vector2Int c)
        {
            var origin = new Vector3(c.x * chunkSize, 0, c.y * chunkSize);
            var root = new GameObject($"Chunk_{c.x}_{c.y}");
            root.transform.SetParent(transform, false); root.transform.position = origin;
            var chunk = new Chunk { root = root, id = c };

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.transform.SetParent(root.transform, false);
            ground.transform.localPosition = new Vector3(chunkSize / 2f, -0.05f, chunkSize / 2f);
            ground.transform.localScale = new Vector3(chunkSize / 10f, 1f, chunkSize / 10f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;
            ground.isStatic = true;
            // The plane's own mesh collider is a zero-thickness sheet; a heavy vehicle landing on it can tunnel through. Use a thick box.
            Destroy(ground.GetComponent<MeshCollider>());
            var box = ground.AddComponent<BoxCollider>(); box.center = new Vector3(0f, -1f, 0f); box.size = new Vector3(10f, 2f, 10f);

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
                chunk.props_legacy.Add(go);   // road meshes are not pooled (unique geometry); destroyed on unload
                PlaceRoadside(chunk, a, b, origin, rng);
            }
            _chunks[c] = chunk; Stats.ChunksBuilt++;
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
                    chunk.props_legacy.Add(go);
                }
            }
        }

        // LEGACY props are pooled cubes reshaped per zone. Replaced by catalog props when route data is present.
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

        void UnloadLegacy(Chunk chunk)
        {
            foreach (var p in chunk.props_legacy)
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
