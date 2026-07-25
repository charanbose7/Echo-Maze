using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Thin input abstraction so the rest of the game never touches a specific input
/// API. Compiles against the new Input System when the package is present
/// (ENABLE_INPUT_SYSTEM), and falls back to the legacy Input Manager otherwise.
/// Supports mouse + touch pointer and WASD / arrow keys.
/// </summary>
public static class EchoInput
{
    /// <summary>Space pressed this frame (used to emit a ping).</summary>
    public static bool PingKeyDown
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Space);
#endif
        }
    }

    /// <summary>True while a mouse button or touch is held down.</summary>
    public static bool PointerHeld
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed) return true;
            return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
            if (Input.touchCount > 0) return true;
            return Input.GetMouseButton(0);
#endif
        }
    }

    /// <summary>Pointer went down this frame.</summary>
    public static bool PointerDown
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame) return true;
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began) return true;
            return Input.GetMouseButtonDown(0);
#endif
        }
    }

    /// <summary>Pointer released this frame.</summary>
    public static bool PointerUp
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasReleasedThisFrame) return true;
            return Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
#else
            if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Ended) return true;
            return Input.GetMouseButtonUp(0);
#endif
        }
    }

    /// <summary>Current pointer position in screen pixels.</summary>
    public static Vector2 PointerScreen
    {
        get
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
                return Touchscreen.current.primaryTouch.position.ReadValue();
            return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            if (Input.touchCount > 0) return Input.GetTouch(0).position;
            return (Vector2)Input.mousePosition;
#endif
        }
    }

    /// <summary>WASD / arrow keys as a normalized direction (zero if untouched).</summary>
    public static Vector2 MoveAxis
    {
        get
        {
            float x = 0f, y = 0f;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k != null)
            {
                if (k.aKey.isPressed || k.leftArrowKey.isPressed)  x -= 1f;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) x += 1f;
                if (k.sKey.isPressed || k.downArrowKey.isPressed)  y -= 1f;
                if (k.wKey.isPressed || k.upArrowKey.isPressed)    y += 1f;
            }
#else
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))  x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))  y -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))    y += 1f;
#endif
            var v = new Vector2(x, y);
            return v.sqrMagnitude > 1f ? v.normalized : v;
        }
    }
}
