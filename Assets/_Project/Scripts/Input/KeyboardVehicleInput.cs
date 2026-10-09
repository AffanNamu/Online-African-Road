using UnityEngine;
using UnityEngine.InputSystem;

namespace ARO.Input
{
    /// <summary>Keyboard + gamepad driver input via the Input System package.</summary>
    public sealed class KeyboardVehicleInput : MonoBehaviour, IVehicleInput
    {
        const float SteerSmoothing = 8f;
        float _steer;

        public VehicleInputState Read()
        {
            var s = new VehicleInputState();
            var kb = Keyboard.current;
            var pad = Gamepad.current;

            float steerTarget = 0f, throttle = 0f, brake = 0f;
            if (kb != null)
            {
                steerTarget = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f)
                            - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f);
                throttle = kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f;
                brake = kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f;
                s.Handbrake = kb.spaceKey.isPressed;
                s.HornHeld = kb.hKey.isPressed;
                s.LightsToggled = kb.lKey.wasPressedThisFrame;
                s.IndicatorLeftToggled = kb.qKey.wasPressedThisFrame;
                s.IndicatorRightToggled = kb.eKey.wasPressedThisFrame;
                s.CameraToggled = kb.cKey.wasPressedThisFrame;
            }
            if (pad != null)
            {
                float stick = pad.leftStick.x.ReadValue();
                if (Mathf.Abs(stick) > Mathf.Abs(steerTarget)) steerTarget = stick;
                throttle = Mathf.Max(throttle, pad.rightTrigger.ReadValue());
                brake = Mathf.Max(brake, pad.leftTrigger.ReadValue());
                s.Handbrake |= pad.buttonSouth.isPressed;
                s.HornHeld |= pad.buttonWest.isPressed;
                s.LightsToggled |= pad.dpad.up.wasPressedThisFrame;
                s.CameraToggled |= pad.buttonNorth.wasPressedThisFrame;
            }

            // Keyboard is digital; smooth it so steering is not a snap.
            _steer = Mathf.MoveTowards(_steer, steerTarget, SteerSmoothing * Time.deltaTime);
            s.Steer = _steer;
            s.Throttle = throttle;
            s.Brake = brake;
            return s;
        }
    }
}
