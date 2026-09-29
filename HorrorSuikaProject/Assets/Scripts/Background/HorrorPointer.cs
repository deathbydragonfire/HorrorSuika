using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Reads the current pointer (mouse, pen, or touch) screen position for background effects.</summary>
public static class HorrorPointer
{
    /// <summary>Returns false when no pointer device exists.</summary>
    public static bool TryGetScreenPosition(out Vector2 screen)
    {
        screen = default;
        Pointer pointer = Pointer.current;
        if (pointer != null)
        {
            screen = pointer.position.ReadValue();
            return true;
        }

        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            screen = mouse.position.ReadValue();
            return true;
        }

        return false;
    }
}
