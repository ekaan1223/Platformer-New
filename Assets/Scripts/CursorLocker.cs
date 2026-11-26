using UnityEngine;             // gives us access to Unity classes (MonoBehaviour, Cursor, etc.)
using UnityEngine.InputSystem; // gives us Keyboard and Mouse (the new Input System)

// CURSOR LOCKER
// Hides and locks the mouse cursor so mouse-look works. Esc frees it, clicking locks it again.
// For menus (Game Over screen, pause menu): UnlockForUI() frees the cursor AND turns off click-to-relock,
// otherwise your first click on a button would lock the cursor again.
// LockForGameplay() puts everything back. Both are public with no parameters, so UnityEvents
// (e.g. GameManager's On Player Died) can call them from the Inspector.
// Put this on an object that survives the player's death (the GameManager object), NOT on the player.

public class CursorLocker : MonoBehaviour // MonoBehaviour = a script Unity can put on an object and call every frame
{
    [SerializeField] private bool lockOnStart = true; // ON = the cursor is locked when the level starts

    private bool clickToRelockEnabled = true; // false while a menu is open

    // Start runs once, before the first Update.
    private void Start()
    {
        if (lockOnStart) SetLocked(true);
    }

    // Update runs every frame: Esc frees the cursor, a click locks it again.
    private void Update()
    {
        Keyboard keyboard = Keyboard.current; // null if no keyboard is plugged in
        Mouse mouse = Mouse.current;          // null if no mouse is plugged in

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            SetLocked(false);
        }
        else if (clickToRelockEnabled && mouse != null && mouse.leftButton.wasPressedThisFrame
                 && Cursor.lockState != CursorLockMode.Locked)
        {
            SetLocked(true);
        }
    }

    // Free the cursor for menus and stop clicks from locking it again.
    public void UnlockForUI()
    {
        clickToRelockEnabled = false;
        SetLocked(false);
    }

    // Back to gameplay: lock the cursor and allow click-to-relock again.
    public void LockForGameplay()
    {
        clickToRelockEnabled = true;
        SetLocked(true);
    }

    // locked = true: hidden and stuck in the middle of the screen. false: visible and free.
    private static void SetLocked(bool locked)
    {
        if (locked) Cursor.lockState = CursorLockMode.Locked;
        else Cursor.lockState = CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
