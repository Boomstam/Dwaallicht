using UnityEngine;
using UnityEngine.InputSystem;

namespace Dwaallicht.Input
{
    public static class DwaallichtInput
    {
        private enum PrimaryPointerSource
        {
            None,
            Mouse,
            Touch,
        }

        private static PrimaryPointerSource activePrimaryPointerSource;
        private static PrimaryPointerSource lastPrimaryPointerSource;
        private static Vector2 lastPrimaryPointerPosition;

        public static bool TryGetPrimaryPointerDown(out Vector2 position)
        {
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                position = Touchscreen.current.primaryTouch.position.ReadValue();
                RememberPrimaryPointer(PrimaryPointerSource.Touch, position);
                activePrimaryPointerSource = PrimaryPointerSource.Touch;
                return true;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                position = Mouse.current.position.ReadValue();
                RememberPrimaryPointer(PrimaryPointerSource.Mouse, position);
                activePrimaryPointerSource = PrimaryPointerSource.Mouse;
                return true;
            }

            position = default;
            return false;
        }

        public static bool TryGetPrimaryPointer(out Vector2 position)
        {
            if (activePrimaryPointerSource == PrimaryPointerSource.Touch
                && Touchscreen.current != null
                && Touchscreen.current.primaryTouch.press.isPressed)
            {
                position = Touchscreen.current.primaryTouch.position.ReadValue();
                RememberPrimaryPointer(PrimaryPointerSource.Touch, position);
                return true;
            }

            if (activePrimaryPointerSource == PrimaryPointerSource.Mouse
                && Mouse.current != null
                && Mouse.current.leftButton.isPressed)
            {
                position = Mouse.current.position.ReadValue();
                RememberPrimaryPointer(PrimaryPointerSource.Mouse, position);
                return true;
            }

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            {
                position = Touchscreen.current.primaryTouch.position.ReadValue();
                RememberPrimaryPointer(PrimaryPointerSource.Touch, position);
                activePrimaryPointerSource = PrimaryPointerSource.Touch;
                return true;
            }

            if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            {
                position = Mouse.current.position.ReadValue();
                RememberPrimaryPointer(PrimaryPointerSource.Mouse, position);
                activePrimaryPointerSource = PrimaryPointerSource.Mouse;
                return true;
            }

            position = default;
            return false;
        }

        public static bool TryGetPrimaryPointerReleasedThisFrame(out Vector2 position, out bool wasTouch)
        {
            if (activePrimaryPointerSource == PrimaryPointerSource.Touch
                && Touchscreen.current != null
                && Touchscreen.current.primaryTouch.press.wasReleasedThisFrame)
            {
                position = Touchscreen.current.primaryTouch.position.ReadValue();
                RememberPrimaryPointer(PrimaryPointerSource.Touch, position);
                activePrimaryPointerSource = PrimaryPointerSource.None;
                wasTouch = true;
                return true;
            }

            if (activePrimaryPointerSource == PrimaryPointerSource.Mouse
                && Mouse.current != null
                && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                position = Mouse.current.position.ReadValue();
                RememberPrimaryPointer(PrimaryPointerSource.Mouse, position);
                activePrimaryPointerSource = PrimaryPointerSource.None;
                wasTouch = false;
                return true;
            }

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasReleasedThisFrame)
            {
                position = Touchscreen.current.primaryTouch.position.ReadValue();
                RememberPrimaryPointer(PrimaryPointerSource.Touch, position);
                activePrimaryPointerSource = PrimaryPointerSource.None;
                wasTouch = true;
                return true;
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                position = Mouse.current.position.ReadValue();
                RememberPrimaryPointer(PrimaryPointerSource.Mouse, position);
                activePrimaryPointerSource = PrimaryPointerSource.None;
                wasTouch = false;
                return true;
            }

            position = lastPrimaryPointerPosition;
            wasTouch = lastPrimaryPointerSource == PrimaryPointerSource.Touch;
            return false;
        }

        public static bool PrimaryPointerReleasedThisFrame()
        {
            return TryGetPrimaryPointerReleasedThisFrame(out _, out _);
        }

        public static float ReadScrollSteps()
        {
            if (Mouse.current == null)
            {
                return 0f;
            }

            var scrollY = Mouse.current.scroll.ReadValue().y;
            return Mathf.Abs(scrollY) > 1f ? scrollY / 120f : scrollY;
        }

        public static bool TryGetPinch(out float distance, out Vector2 center)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null || touchscreen.touches.Count < 2)
            {
                distance = 0f;
                center = default;
                return false;
            }

            var firstTouch = touchscreen.touches[0];
            var secondTouch = touchscreen.touches[1];
            if (!firstTouch.press.isPressed || !secondTouch.press.isPressed)
            {
                distance = 0f;
                center = default;
                return false;
            }

            var firstPosition = firstTouch.position.ReadValue();
            var secondPosition = secondTouch.position.ReadValue();
            distance = Vector2.Distance(firstPosition, secondPosition);
            center = (firstPosition + secondPosition) * 0.5f;
            return distance > 1f;
        }

        public static bool IsAnyKeyPressed(params Key[] keys)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            foreach (var key in keys)
            {
                if (keyboard[key].isPressed)
                {
                    return true;
                }
            }

            return false;
        }

        private static void RememberPrimaryPointer(PrimaryPointerSource source, Vector2 position)
        {
            lastPrimaryPointerSource = source;
            lastPrimaryPointerPosition = position;
        }
    }
}
