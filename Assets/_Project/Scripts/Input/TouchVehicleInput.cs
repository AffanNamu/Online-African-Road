using UnityEngine;

namespace ARO.Input
{
    /// <summary>
    /// Touch driver input. On-screen controls (UI buttons/sliders) call the setters; this component
    /// turns them into the same VehicleInputState the keyboard produces.
    /// </summary>
    public sealed class TouchVehicleInput : MonoBehaviour, IVehicleInput
    {
        float _steer, _throttle, _brake;
        bool _horn, _handbrake, _lights, _left, _right, _camera;

        public void SetSteer(float v) => _steer = Mathf.Clamp(v, -1f, 1f);
        public void SetThrottle(float v) => _throttle = Mathf.Clamp01(v);
        public void SetBrake(float v) => _brake = Mathf.Clamp01(v);
        public void SetHorn(bool held) => _horn = held;
        public void SetHandbrake(bool held) => _handbrake = held;
        public void PressLights() => _lights = true;
        public void PressIndicatorLeft() => _left = true;
        public void PressIndicatorRight() => _right = true;
        public void PressCamera() => _camera = true;

        public VehicleInputState Read()
        {
            var s = new VehicleInputState
            {
                Steer = _steer, Throttle = _throttle, Brake = _brake,
                Handbrake = _handbrake, HornHeld = _horn,
                LightsToggled = _lights, IndicatorLeftToggled = _left,
                IndicatorRightToggled = _right, CameraToggled = _camera
            };
            _lights = _left = _right = _camera = false; // consume edges
            return s;
        }
    }
}
