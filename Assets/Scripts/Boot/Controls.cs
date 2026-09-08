// -----------------------------------------------------------------------------
// Controls — every key and mouse read in the game, in one place.
//
// Uses the Input System's DEVICE API (Keyboard.current, Mouse.current) rather
// than an .inputactions asset. That matters here: the device API needs no
// authored asset at all, so the project keeps its "clone and press Play"
// property while still being on the supported, non-deprecated input path.
//
// The trade against an actions asset is rebinding — a shipping game wants an
// InputActionAsset so players can remap keys and so gamepads work without a
// second code path. That is the upgrade, and it is a small one: the call sites
// all go through this file, so only this file changes.
//
// Every accessor is null-safe. Keyboard.current is null when no keyboard is
// attached and, more commonly, for a frame or two while the Editor takes focus.
// -----------------------------------------------------------------------------

using UnityEngine;
using UnityEngine.InputSystem;

namespace LochNess.Boot
{
    public static class Controls
    {
        /// <summary>
        /// Mouse delta arrives in raw pixels, where the legacy axis was pre-scaled.
        /// This constant restores roughly the old feel at the same sensitivity
        /// setting, so the value stored in preferences still means what it used to.
        /// </summary>
        private const float LookScale = 0.06f;

        public static bool Available => Keyboard.current != null;

        /// <summary>Forward/back, -1..1. W ahead, S astern.</summary>
        public static float Throttle
        {
            get
            {
                Keyboard k = Keyboard.current;
                if (k == null) return 0f;
                return (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
            }
        }

        /// <summary>Left/right, -1..1. Strafe on foot, rudder at the helm.</summary>
        public static float Strafe
        {
            get
            {
                Keyboard k = Keyboard.current;
                if (k == null) return 0f;
                return (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
            }
        }

        public static bool Sprint => Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;

        /// <summary>Mouse movement this frame, already scaled for look sensitivity.</summary>
        public static Vector2 LookDelta
        {
            get
            {
                Mouse m = Mouse.current;
                if (m == null) return Vector2.zero;
                return m.delta.ReadValue() * LookScale;
            }
        }

        /// <summary>E — take or leave a station.</summary>
        public static bool InteractPressed => Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;

        /// <summary>Q — leave a station without having to look at it.</summary>
        public static bool StandDownPressed => Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame;

        /// <summary>Space — ping at the sonar, log a sighting at the bow watch.</summary>
        public static bool ActionPressed => Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;

        /// <summary>Escape — hand the cursor back.</summary>
        public static bool CancelPressed => Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
    }
}
