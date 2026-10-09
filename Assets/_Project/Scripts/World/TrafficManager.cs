using System.Collections.Generic;
using ARO.NetCore;
using UnityEngine;

namespace ARO.World
{
    /// <summary>
    /// Drives ambient traffic along a RouteDefinition using the unit-tested TrafficSim (IDM car following, two-way lanes,
    /// spawn/despawn windows around the player). Vehicles are pooled kinematic bodies: the player can collide with them
    /// (and takes damage via VehicleController), and traffic brakes for the player in the same lane.
    /// Intersections and traffic lights are NOT implemented (route is a single corridor).
    /// </summary>
    public class TrafficManager : MonoBehaviour
    {
        public RouteDefinition route;
        public Transform player;
        public Material bodyTemplate;
        public float laneOffset = 3.2f;
        [Range(0, 2)] public int quality = 1;          // 0 low, 1 medium, 2 high (set from QualitySettings by ApplyQuality)

        public int ActiveCount => _views.Count;
        public int PooledCount => _pool.Count;

        TrafficSim _sim;
        readonly Dictionary<int, View> _views = new Dictionary<int, View>();
        readonly Stack<View> _pool = new Stack<View>();
        float[] _limits;           // per segment speed limit (m/s)
        float[] _cum;
        MaterialPropertyBlock _mpb;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color");

        class View { public GameObject go; public Rigidbody rb; public Renderer[] renders; public TrafficKind kind; }

        static readonly Color[] CarColours = { new Color(.75f,.75f,.78f), new Color(.1f,.1f,.12f), new Color(.7f,.12f,.1f), new Color(.15f,.3f,.6f), new Color(.9f,.9f,.9f), new Color(.25f,.45f,.3f) };

        public void ApplyQuality()
        {
            int q = QualitySettings.GetQualityLevel(), n = QualitySettings.names.Length;
            quality = n <= 1 ? 1 : Mathf.Clamp(Mathf.RoundToInt(2f * q / (n - 1)), 0, 2);
            if (_sim == null) return;
            _sim.Cfg.DensityPerKmPerDir = quality == 0 ? 3f : quality == 1 ? 5f : 8f;
            _sim.Cfg.MaxAgents = quality == 0 ? 20 : quality == 1 ? 40 : 80;
        }

        void Start()
        {
            if (route == null || route.nodes.Length < 2) { enabled = false; return; }
            route.Rebuild(); _mpb = new MaterialPropertyBlock();
            _cum = new float[route.nodes.Length];
            for (int i = 1; i < route.nodes.Length; i++) _cum[i] = _cum[i - 1] + Vector3.Distance(route.nodes[i - 1].position, route.nodes[i].position);
            _limits = new float[route.nodes.Length - 1];
            for (int i = 0; i < _limits.Length; i++) _limits[i] = LimitFor(route.nodes[i]);
            _sim = new TrafficSim(route.TotalLength, new TrafficConfig { Seed = route.seed });
            _sim.SpeedLimit = s => { route.SegmentAt(s, out _); return _limits[Mathf.Clamp(SegIndex(s), 0, _limits.Length - 1)]; };
            ApplyQuality();
        }

        int SegIndex(float s) { int i = 0; while (i < _cum.Length - 2 && s > _cum[i + 1]) i++; return i; }

        /// <summary>Speed limit in m/s: zone sets the base, surface condition scales it (damaged roads are slower).</summary>
        public static float LimitFor(RouteNode n)
        {
            float v;
            switch (n.zone)
            {
                case ZoneType.Urban: v = 11f; break;          // ~40 km/h
                case ZoneType.Commercial: v = 9f; break;      // ~32 km/h, markets and stalls
                case ZoneType.Industrial: v = 14f; break;
                case ZoneType.Rural: v = 20f; break;
                default: v = 25f; break;                      // highway ~90 km/h
            }
            return v * (n.surface == RoadSurface.Damaged ? 0.55f : n.surface == RoadSurface.Worn ? 0.85f : 1f);
        }

        void FixedUpdate()
        {
            if (_sim == null) return;
            float lateral; float ps = player != null ? route.Project(player.position, out lateral) : 0f;
            if (player != null)
            {
                var tan = route.TangentAt(ps);
                int dir = Vector3.Dot(player.forward, tan) >= 0f ? 1 : -1;
                float speed = player.TryGetComponent(out Rigidbody prb) ? prb.linearVelocity.magnitude : 0f;
                _sim.SetPlayer(ps, speed, dir, 8f);
            }
            _sim.Step(Time.fixedDeltaTime, ps);
            Sync();
        }

        void Sync()
        {
            var alive = new HashSet<int>();
            foreach (var a in _sim.Agents)
            {
                alive.Add(a.Id);
                if (!_views.TryGetValue(a.Id, out var v)) { v = Rent(a); _views[a.Id] = v; }
                float centre = a.S - a.Dir * a.P.Length * 0.5f;                  // agent S is the front bumper
                Vector3 tan = route.TangentAt(centre) * a.Dir;
                Vector3 right = Vector3.Cross(Vector3.up, tan);
                Vector3 pos = route.PositionAt(centre) + right * laneOffset;     // drive on the right of own direction of travel
                pos.y += v.go.transform.localScale.y * 0.5f + 0.35f;
                v.rb.MovePosition(pos); v.rb.MoveRotation(Quaternion.LookRotation(tan, Vector3.up));
            }
            List<int> gone = null;
            foreach (var id in _views.Keys) if (!alive.Contains(id)) (gone ?? (gone = new List<int>())).Add(id);
            if (gone != null) foreach (var id in gone) { Return(_views[id]); _views.Remove(id); }
        }

        View Rent(TrafficAgent a)
        {
            View v = null;
            while (_pool.Count > 0 && v == null) { var c = _pool.Pop(); if (c.go != null && c.kind == a.Kind) v = c; else if (c.go != null) Destroy(c.go); }
            if (v == null) v = Build(a.Kind);
            v.go.SetActive(true);
            var col = a.Kind == TrafficKind.Taxi || a.Kind == TrafficKind.Minibus ? new Color(0.95f, 0.75f, 0.1f) : CarColours[a.Id % CarColours.Length];
            Tint(v, col);
            // teleport so the kinematic body does not sweep across the world from its old position
            var centre = a.S - a.Dir * a.P.Length * 0.5f; var tan = route.TangentAt(centre) * a.Dir;
            v.go.transform.SetPositionAndRotation(route.PositionAt(centre), Quaternion.LookRotation(tan, Vector3.up));
            return v;
        }

        void Return(View v) { v.go.SetActive(false); _pool.Push(v); }

        void Tint(View v, Color c)
        {
            _mpb.SetColor(BaseColor, c); _mpb.SetColor(ColorId, c);
            foreach (var r in v.renders) r.SetPropertyBlock(_mpb);
        }

        View Build(TrafficKind kind)
        {
            Vector3 size = kind == TrafficKind.Truck ? new Vector3(2.5f, 3.0f, 12f) : kind == TrafficKind.Minibus ? new Vector3(2.0f, 2.4f, 7f)
                         : kind == TrafficKind.Motorcycle ? new Vector3(0.7f, 1.3f, 2f) : new Vector3(1.8f, 1.45f, 4.5f);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = "Traffic_" + kind; go.transform.SetParent(transform, false);
            go.transform.localScale = size;
            var rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.mass = kind == TrafficKind.Truck ? 9000f : kind == TrafficKind.Minibus ? 3500f : kind == TrafficKind.Motorcycle ? 200f : 1400f;
            var r = go.GetComponent<Renderer>(); if (bodyTemplate != null) r.sharedMaterial = bodyTemplate;
            return new View { go = go, rb = rb, renders = new[] { r }, kind = kind };
        }
    }
}
