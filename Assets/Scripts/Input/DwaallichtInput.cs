using UnityEngine;
using UnityEngine.InputSystem;

namespace Dwaallicht.Input
{
    public static class DwaallichtInput
    {
        public static bool TryGetPrimaryPointerDown(out Vector2 position)
        {
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                position = Mouse.current.position.ReadValue();
                return true;
            }

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                position = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }

            position = default;
            return false;
        }

        public static bool TryGetPrimaryPointer(out Vector2 position)
        {
            if (Mouse.current != null && Mouse.current.leftButton.isPressed)
            {
                position = Mouse.current.position.ReadValue();
                return true;
            }

            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            {
                position = Touchscreen.current.primaryTouch.position.ReadValue();
                return true;
            }

            position = default;
            return false;
        }

        public static bool PrimaryPointerReleasedThisFrame()
        {
            return (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
                || (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasReleasedThisFrame);
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
    }
}
