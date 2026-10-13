using UnityEngine;
using UnityEngine.InputSystem;

namespace ARO.Input
{
    /// <summary>Keyboard + gamepad driver input via the Input System package.</summary>
    public sealed class KeyboardVehicleInput : MonoBehaviour, IVehicleInput
    {
        const float SteerSmoothing = 8f;
        float _steer; static bool _padLogged;

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
                // Camera keys come from CameraBindings (C next, Shift+C previous, 1..8 direct) so they can be rebound in one place.
                bool shift = kb[CameraBindings.PreviousModifierLeft].isPressed || kb[CameraBindings.PreviousModifierRight].isPressed;
                if (kb[CameraBindings.Next].wasPressedThisFrame) { if (shift) s.CameraPrevious = true; else s.CameraToggled = true; }
                for (int i = 0; i < CameraBindings.Direct.Length; i++) if (kb[CameraBindings.Direct[i]].wasPressedThisFrame) { s.CameraSelect = i + 1; break; }
            }
            if (pad != null)
            {
                if (!_padLogged) { _padLogged = true; Debug.Log("[Input] gamepad detected: " + pad.displayName); }
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
            _steer = Mathf.MoveTowards(_steer, steerTarget, SteerSmoothing * ARO.Core.RuntimeSettings.SteeringGain * Time.deltaTime);   // Steering Sensitivity: how quickly it answers
            s.Steer = _steer;
            s.Throttle = throttle;
            s.Brake = brake;
            return s;
        }
    }
}
