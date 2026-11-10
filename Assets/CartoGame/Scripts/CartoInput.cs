#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
#define CARTO_NEW_INPUT
#endif

using UnityEngine;
#if CARTO_NEW_INPUT
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Envoltorio de entrada. Funciona con el Input Manager clasico y tambien
/// si el proyecto solo tiene activo el nuevo Input System.
/// </summary>
public static class CartoInput
{
#if CARTO_NEW_INPUT
    static Keyboard K { get { return Keyboard.current; } }
    static Mouse M { get { return Mouse.current; } }

    public static Vector2 Move
    {
        get
        {
            if (K == null) return Vector2.zero;
            float x = 0f, y = 0f;
            if (K.aKey.isPressed || K.leftArrowKey.isPressed) x -= 1f;
            if (K.dKey.isPressed || K.rightArrowKey.isPressed) x += 1f;
            if (K.sKey.isPressed || K.downArrowKey.isPressed) y -= 1f;
            if (K.wKey.isPressed || K.upArrowKey.isPressed) y += 1f;
            return new Vector2(x, y);
        }
    }

    public static Vector2 Look
    {
        get { return M == null ? Vector2.zero : M.delta.ReadValue() * 0.1f; }
    }

    public static float Scroll
    {
        get { return M == null ? 0f : Mathf.Clamp(M.scroll.ReadValue().y, -1f, 1f); }
    }

    public static bool Sprint { get { return K != null && K.leftShiftKey.isPressed; } }
    public static bool AttackDown { get { return M != null && M.leftButton.wasPressedThisFrame; } }
    public static bool ShootDown { get { return M != null && M.rightButton.wasPressedThisFrame; } }
    public static bool RollDown { get { return K != null && K.spaceKey.wasPressedThisFrame; } }
    public static bool InteractDown { get { return K != null && K.eKey.wasPressedThisFrame; } }
    public static bool NextMeleeDown { get { return K != null && K.qKey.wasPressedThisFrame; } }
    public static bool NextRangedDown { get { return K != null && K.rKey.wasPressedThisFrame; } }
    public static bool PauseDown { get { return K != null && K.escapeKey.wasPressedThisFrame; } }
    public static bool ConfirmDown { get { return K != null && (K.enterKey.wasPressedThisFrame || K.numpadEnterKey.wasPressedThisFrame); } }
    public static bool NewGameDown { get { return K != null && K.nKey.wasPressedThisFrame; } }

    public static bool NumberDown(int n)
    {
        if (K == null) return false;
        switch (n)
        {
            case 1: return K.digit1Key.wasPressedThisFrame || K.numpad1Key.wasPressedThisFrame;
            case 2: return K.digit2Key.wasPressedThisFrame || K.numpad2Key.wasPressedThisFrame;
            case 3: return K.digit3Key.wasPressedThisFrame || K.numpad3Key.wasPressedThisFrame;
            case 4: return K.digit4Key.wasPressedThisFrame || K.numpad4Key.wasPressedThisFrame;
        }
        return false;
    }
#else
    public static Vector2 Move
    {
        get
        {
            float x = 0f, y = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) x -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) x += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) y -= 1f;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) y += 1f;
            return new Vector2(x, y);
        }
    }

    public static Vector2 Look
    {
        get { return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")); }
    }

    public static float Scroll
    {
        get { return Mathf.Clamp(Input.mouseScrollDelta.y, -1f, 1f); }
    }

    public static bool Sprint { get { return Input.GetKey(KeyCode.LeftShift); } }
    public static bool AttackDown { get { return Input.GetMouseButtonDown(0); } }
    public static bool ShootDown { get { return Input.GetMouseButtonDown(1); } }
    public static bool RollDown { get { return Input.GetKeyDown(KeyCode.Space); } }
    public static bool InteractDown { get { return Input.GetKeyDown(KeyCode.E); } }
    public static bool NextMeleeDown { get { return Input.GetKeyDown(KeyCode.Q); } }
    public static bool NextRangedDown { get { return Input.GetKeyDown(KeyCode.R); } }
    public static bool PauseDown { get { return Input.GetKeyDown(KeyCode.Escape); } }
    public static bool ConfirmDown { get { return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter); } }
    public static bool NewGameDown { get { return Input.GetKeyDown(KeyCode.N); } }

    public static bool NumberDown(int n)
    {
        switch (n)
        {
            case 1: return Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1);
            case 2: return Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2);
            case 3: return Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3);
            case 4: return Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4);
        }
        return false;
    }
#endif
}
