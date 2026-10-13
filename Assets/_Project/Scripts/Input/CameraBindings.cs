using UnityEngine.InputSystem;

namespace ARO.Input
{
    /// <summary>
    /// The keyboard keys that change the camera, declared in one place so a future controls screen can rebind them without touching the input code.
    /// Defaults: C = next view, Shift+C = previous view, 1..8 = view by number. None of them is used by driving
    /// (W A S D arrows, Space, H, L, Q, E) or by the menu (Escape). Digits are reserved for the camera; planned driving controls (gears,
    /// high beams, hazards, wipers) must pick other keys: see docs/CAMERA.md.
    /// </summary>
    public static class CameraBindings
    {
        public static Key Next = Key.C;
        public static Key PreviousModifierLeft = Key.LeftShift, PreviousModifierRight = Key.RightShift;
        /// <summary>Index 0 selects view 1 (cockpit) ... index 7 selects view 8 (front bumper).</summary>
        public static readonly Key[] Direct = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8 };

        public static void ResetDefaults()
        {
            Next = Key.C; PreviousModifierLeft = Key.LeftShift; PreviousModifierRight = Key.RightShift;
            Direct[0] = Key.Digit1; Direct[1] = Key.Digit2; Direct[2] = Key.Digit3; Direct[3] = Key.Digit4;
            Direct[4] = Key.Digit5; Direct[5] = Key.Digit6; Direct[6] = Key.Digit7; Direct[7] = Key.Digit8;
        }
    }
}
