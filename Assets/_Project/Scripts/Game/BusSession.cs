using System.Linq;
using System.Threading.Tasks;
using ARO.Backend;
using ARO.NetCore;
using ARO.Vehicles;
using UnityEngine;

namespace ARO.Game
{
    /// <summary>
    /// A bus route in progress. Drives the stop-to-stop loop and reports to the server:
    /// position samples every <see cref="SampleInterval"/> s, and a serve request when the bus is halted at the next stop.
    /// Passengers, fares and XP only ever change from server replies (<see cref="BusRunState.Apply"/>).
    /// </summary>
    public class BusSession : MonoBehaviour
    {
        const float SampleInterval = 2f;

        GameServices _svc; DrivingSession _drive;
        StopPoint[] _stops = new StopPoint[0];
        bool _busy, _sampling; float _nextSample, _retryAt; Vector3 _markerTarget;

        public BusRouteDto Route { get; private set; }
        public string RunId { get; private set; }
        public readonly BusRunState State = new BusRunState();
        public bool Active => RunId != null && Route != null;
        public System.Action<ServeStopResult> RunCompleted;

        public int NextIndex => State.NextIndex;
        public BusStopDto NextStop => Route.stops[Mathf.Clamp(State.NextIndex, 0, Route.stops.Length - 1)];
        public int Capacity => _drive.Vehicle != null && _drive.Vehicle.definition != null ? _drive.Vehicle.definition.passengerCapacity : 0;
        public bool IsFirstStop => State.NextIndex == 0;
        public bool IsLastStop => Route != null && State.NextIndex >= Route.stops.Length - 1;
        public Vector3 NextStopPosition => new Vector3((float)NextStop.location.world_x, 0f, (float)NextStop.location.world_z);

        public float DistanceToNext
        {
            get
            {
                if (!Active || _drive.Vehicle == null) return 0f;
                var p = _drive.Vehicle.transform.position; var t = NextStopPosition;
                return BusRules.Distance(p.x, p.z, t.x, t.z);
            }
        }

        public float Progress
        {
            get
            {
                if (!Active || _drive.Vehicle == null) return 0f;
                var p = _drive.Vehicle.transform.position; return BusRules.Progress(_stops, State.NextIndex, p.x, p.z);
            }
        }

        public void Init(GameServices svc, DrivingSession drive) { _svc = svc; _drive = drive; }

        /// <summary>Ask the server to start a run, then spawn the bus just before the first stop. Returns an error message or null.</summary>
        public async Task<string> Begin(BusRouteDto route, OwnedVehicleDto bus)
        {
            if (Active) return "You already have a bus route in progress.";
            var first = route.stops[0].location; var second = route.stops[1].location;
            var firstPos = new Vector3((float)first.world_x, 0f, (float)first.world_z);
            var dir = (new Vector3((float)second.world_x, 0f, (float)second.world_z) - firstPos).normalized;
            var spawn = firstPos - dir * 30f;   // inside the server's 120 m start radius, short of the stop so the player pulls in

            var r = await _svc.Bus.Start(route.id, bus.id, spawn);
            if (!r.Ok) return r.UserMessage;

            Route = route; RunId = r.Value; State.Reset(); _busy = false; _sampling = false; _retryAt = 0f; _nextSample = Time.time + SampleInterval;
            _stops = route.stops.Select(s => new StopPoint(s.location.name, (float)s.location.world_x, (float)s.location.world_z)).ToArray();
            _drive.Enter(bus, spawn, Quaternion.LookRotation(dir));
            if (_drive.Vehicle == null) { await Abandon(); return "Vehicle model failed to load."; }
            ShowNextMarker();
            _drive.Say($"Route {route.code}: pull in at {NextStop.location.name} to board passengers.");
            return null;
        }

        void ShowNextMarker() { _markerTarget = NextStopPosition; _drive.SetMarker(_markerTarget); }

        void Update()
        {
            if (!Active || _drive.Vehicle == null || !_drive.Active) return;
            if (!_sampling && Time.time >= _nextSample) _ = Sample();
            if (_busy || Time.time < _retryAt) return;
            if (BusRules.CanServe(DistanceToNext, _drive.Vehicle.SpeedKmh)) _ = Serve();
        }

        async Task Sample()
        {
            _sampling = true; _nextSample = Time.time + SampleInterval;
            string run = RunId;
            var r = await _svc.Bus.SubmitTelemetry(run, _drive.Vehicle.transform.position);
            _sampling = false;
            if (run != RunId) return;   // run ended while the request was in flight
            if (!r.Ok) { if (r.ErrorCode == "run_not_active" || r.ErrorCode == "run_not_found") EndLocal(); return; }
            if (!r.Value.accepted && r.Value.reason == "flagged")
            {
                _drive.Say("Your movement data was rejected. This route cannot be completed.", 8f, Severity.Error);
                await Abandon();
            }
            else if (!r.Value.accepted && r.Value.reason == "too_fast") Debug.LogWarning("[Bus] telemetry sample rejected by server (too fast)");
        }

        async Task Serve()
        {
            _busy = true;
            string run = RunId;
            var stop = NextStop;
            while (_sampling) await Task.Yield();
            if (run != RunId) { _busy = false; return; }
            await _svc.Bus.SubmitTelemetry(run, _drive.Vehicle.transform.position);   // fresh sample at the stop (server demands <= 30 s old)
            var r = await _svc.Bus.ServeStop(run);
            _busy = false;
            if (run != RunId) return;
            if (!r.Ok)
            {
                _drive.Say(r.UserMessage, 6f, Severity.Error);
                _retryAt = Time.time + (r.ErrorCode == "telemetry_stale" ? 3f : 6f);
                if (r.ErrorCode == "run_not_active" || r.ErrorCode == "run_not_found") EndLocal();
                else if (r.ErrorCode == "telemetry_flagged") await Abandon();
                return;
            }

            var v = r.Value;
            if (v.completed)
            {
                _drive.Say($"Route complete! {v.passengers_carried} passengers carried. +{v.revenue:N0} coins, +{v.xp} XP", 10f, Severity.Success);
                EndLocal();
                RunCompleted?.Invoke(v);
                return;
            }
            State.Apply(v.aboard, v.fare);
            _drive.Say(BusRules.StopSummary(stop.location.name, v.alighted, v.boarded, v.aboard, v.fare), 7f, v.fare > 0 ? Severity.Success : Severity.Info);
            if (v.fare > 0) _ = _svc.RefreshWallet();
            ShowNextMarker();
            _retryAt = Time.time + 4f;   // never re-serve the stop we are still standing in
        }

        public async Task Abandon()
        {
            if (!Active) return;
            string run = RunId; EndLocal();
            await _svc.Bus.Abandon(run);
            _drive.Say("Bus route abandoned.");
        }

        void EndLocal()
        {
            RunId = null; Route = null; State.Reset(); _drive.ClearMarker();
        }
    }
}
