using System.Collections.Generic;
using ARO.Backend;
using ARO.Vehicles;
using ARO.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ARO.Game
{
    public enum JobPhase { None, ToPickup, Loading, ToDestination }

    /// <summary>
    /// Owns the in-world driving experience: spawns the player's vehicle, runs the job phases
    /// (pickup -> load -> deliver) and reports to the server. Money/XP only ever change by server reply.
    /// </summary>
    public class DrivingSession : MonoBehaviour
    {
        public const float PickupRadius = 40f, DeliverRadius = 100f, StopSpeedKmh = 6f;

        GameServices _svc; RouteDefinition _route; Material _body, _wheel, _marker;
        Dictionary<string, VehicleDefinition> _defs;
        public VehicleController Vehicle { get; private set; }
        public FollowCamera Cam { get; private set; }
        public ChunkStreamer Streamer { get; private set; }
        public bool Active { get; private set; }

        // job state
        public JobDto Job { get; private set; }
        public string AssignmentId { get; private set; }
        public JobPhase Phase { get; private set; }
        public string Message { get; private set; }
        public float MessageUntil;
        GameObject _markerGo; bool _busy; float _startOdo;
        string _vehicleId;
        public System.Action<CompleteJobResult> JobCompleted;

        public void Init(GameServices svc, RouteDefinition route, Dictionary<string, VehicleDefinition> defs,
                         Material body, Material wheel, Material marker, ChunkStreamer streamer)
        {
            _svc = svc; _route = route; _defs = defs; _body = body; _wheel = wheel; _marker = marker; Streamer = streamer;
            if (!Camera.main.TryGetComponent(out FollowCamera cam)) cam = Camera.main.gameObject.AddComponent<FollowCamera>();
            Cam = cam;
        }

        public void Enter(OwnedVehicleDto owned)
        {
            if (Vehicle != null) Destroy(Vehicle.gameObject);
            var def = _defs[owned.definition_id];
            var n0 = _route.nodes[0].position;
            var dir = (_route.nodes[1].position - n0).normalized;
            Vehicle = TruckFactory.Create(def, n0 + Vector3.up * 1.5f, Quaternion.LookRotation(dir), _body, _wheel);
            Vehicle.fuelL = (float)owned.fuel_l; Vehicle.damagePct = (float)owned.damage_pct;
            _vehicleId = owned.id;
            Cam.target = Vehicle.transform; Streamer.target = Vehicle.transform; Streamer.route = _route;
            Active = true; Time.timeScale = 1f;
        }

        public void Pause(bool paused) { Active = !paused; Time.timeScale = paused ? 0f : 1f; }
        public string VehicleId => _vehicleId;

        /// <summary>Called after the server accepted the job.</summary>
        public void BeginJob(JobDto job, string assignmentId)
        {
            Job = job; AssignmentId = assignmentId; Phase = JobPhase.ToPickup;
            PlaceMarker(new Vector3((float)job.origin.world_x, 0, (float)job.origin.world_z));
            Say($"Job {job.code}: drive to {job.origin.name} to load.");
        }

        Vector3 TargetPos => Phase == JobPhase.ToDestination
            ? new Vector3((float)Job.destination.world_x, 0, (float)Job.destination.world_z)
            : new Vector3((float)Job.origin.world_x, 0, (float)Job.origin.world_z);

        public float DistanceToTarget => Job == null || Vehicle == null ? 0f
            : Vector2.Distance(new Vector2(Vehicle.transform.position.x, Vehicle.transform.position.z), new Vector2(TargetPos.x, TargetPos.z));

        /// <summary>Signed bearing (degrees, -180..180) to the target relative to the vehicle heading, for the HUD arrow.</summary>
        public float BearingToTarget()
        {
            if (Job == null || Vehicle == null) return 0f;
            var to = TargetPos - Vehicle.transform.position; to.y = 0f;
            return Vector3.SignedAngle(Vehicle.transform.forward, to, Vector3.up);
        }

        void Update()
        {
            if (!Active || Vehicle == null) return;
            if (Keyboard.current != null && Keyboard.current.cKey.wasPressedThisFrame) Cam.ToggleCockpit();
            if (Job == null || _busy) return;

            bool stopped = Vehicle.SpeedKmh < StopSpeedKmh;
            float d = DistanceToTarget;
            if (Phase == JobPhase.ToPickup && d < PickupRadius && stopped) _ = LoadCargo();
            else if (Phase == JobPhase.ToDestination && d < DeliverRadius && stopped) _ = Deliver();
            if (_markerGo != null) _markerGo.transform.Rotate(0, 60f * Time.deltaTime, 0);
        }

        async System.Threading.Tasks.Task LoadCargo()
        {
            _busy = true; Say("Loading cargo...");
            var r = await _svc.Jobs.Start(AssignmentId);   // server stamps started_at: the clock for delivery validation
            _busy = false;
            if (!r.Ok) { Say(r.UserMessage, 6f); if (r.ErrorCode == "invalid_state") Phase = JobPhase.ToDestination; return; }
            _startOdo = Vehicle.odometerKm; Phase = JobPhase.ToDestination;
            PlaceMarker(TargetPos);
            Say($"Cargo loaded ({Job.cargo_type}). Deliver to {Job.destination.name}.");
        }

        async System.Threading.Tasks.Task Deliver()
        {
            _busy = true; Say("Delivering... waiting for server validation.");
            float dist = Vehicle.odometerKm - _startOdo;
            var r = await _svc.Jobs.Complete(AssignmentId, dist, Vehicle.transform.position, Vehicle.fuelL, Vehicle.damagePct);
            _busy = false;
            if (!r.Ok)
            {
                Say(r.UserMessage, 8f);
                if (r.ErrorCode == "already_completed") Clear();
                return;   // other rejections: player can keep driving and retry (e.g. not_at_destination)
            }
            Say($"Delivered! +{r.Value.reward + r.Value.bonus} coins, +{r.Value.xp} XP", 8f);
            Clear();
            JobCompleted?.Invoke(r.Value);
        }

        public async void AbandonJob()
        {
            if (AssignmentId == null) return;
            await _svc.Jobs.Abandon(AssignmentId); Clear(); Say("Job abandoned.");
        }

        void Clear() { Job = null; AssignmentId = null; Phase = JobPhase.None; if (_markerGo) Destroy(_markerGo); }

        void PlaceMarker(Vector3 p)
        {
            if (_markerGo) Destroy(_markerGo);
            _markerGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(_markerGo.GetComponent<Collider>());
            _markerGo.transform.position = new Vector3(p.x, 40f, p.z); _markerGo.transform.localScale = new Vector3(8f, 40f, 8f);
            if (_marker != null) _markerGo.GetComponent<Renderer>().sharedMaterial = _marker;
        }

        public void Say(string m, float seconds = 4f) { Message = m; MessageUntil = Time.unscaledTime + seconds; }
    }
}
