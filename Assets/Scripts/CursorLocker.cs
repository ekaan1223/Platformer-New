using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Hides and locks the mouse cursor so mouse-look works. Esc releases it, clicking re-locks it.
/// For menus (Game Over screen, pause menu): UnlockForUI() frees the cursor AND turns off
/// click-to-relock, otherwise your first click on a button would lock the cursor again.
/// LockForGameplay() puts everything back. Both are public with no parameters, so UnityEvents
/// (e.g. GameManager's On Player Died) can call them from the Inspector.
/// Put this on an object that survives the player's death (e.g. the GameManager object),
/// NOT on the player, because the player can be destroyed when it dies.
/// </summary>
public class CursorLocker : MonoBehaviour
{
    [SerializeField] private bool lockOnStart = true;

    private bool clickToRelockEnabled = true;

    private void Start()
    {
        if (lockOnStart) SetLocked(true);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) SetLocked(false);
        else if (clickToRelockEnabled && mouse != null && mouse.leftButton.wasPressedThisFrame
                 && Cursor.lockState != CursorLockMode.Locked)
        {
            SetLocked(true);
        }
    }

    /// <summary>Free the cursor for menus and stop clicks from re-locking it.</summary>
    public void UnlockForUI()
    {
        clickToRelockEnabled = false;
        SetLocked(false);
    }

    /// <summary>Back to gameplay: lock the cursor and allow click-to-relock again.</summary>
    public void LockForGameplay()
    {
        clickToRelockEnabled = true;
        SetLocked(true);
    }

    private static void SetLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
