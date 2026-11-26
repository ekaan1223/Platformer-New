using UnityEngine;                 // gives us access to Unity classes (MonoBehaviour, Debug, etc.)
using UnityEngine.Events;          // gives us UnityEvent (the event lists you can fill in the Inspector)
using UnityEngine.InputSystem;     // gives us InputAction, for the restart key
using UnityEngine.SceneManagement; // gives us SceneManager, to reload the level

// GAME MANAGER
// Knows "is the game over?" and knows how to restart the level.
// It does NOT care who asks for a restart: the restart key (R by default), a UI Button, a Game Over screen or the
// player dying can all call the same public functions. So you can swap the key for buttons later without touching this script.
// It is not a "singleton" on purpose: hook it up in the Inspector with UnityEvents, like Damageable.
// Restarting reloads the scene, so the player, enemies and everything else go back to how you placed them.
// Keep it on its own object (the GameManager prefab), NOT on the player.

public class GameManager : MonoBehaviour // MonoBehaviour = a script Unity can put on an object
{
    [Header("Restart")]
    [Tooltip("ON = restart works any time (R mid-level, a Restart button in a pause menu). " +
             "OFF = only after the player has died.")]
    [SerializeField] private bool allowRestartWhileAlive = true;

    [Header("Input (edit bindings directly in the Inspector, empty = R / gamepad Select)")]
    [SerializeField] private InputAction restartAction = new InputAction("Restart", InputActionType.Button);

    [Header("Events (hook UI / sound / fades here later)")]
    [Tooltip("Fires once when the player dies. Later: show the Game Over panel here.")]
    [SerializeField] private UnityEvent onPlayerDied;
    [Tooltip("Fires right before the scene reloads. Later: a fade-out or a sound.")]
    [SerializeField] private UnityEvent onRestarting;

    private bool isGameOver; // true after the player died

    // True after the player died. Other scripts (a Game Over screen) can read it.
    public bool IsGameOver
    {
        get { return isGameOver; }
    }

    // Awake runs once, when the object is created: give the restart key its default buttons if none were set.
    private void Awake()
    {
        if (restartAction.bindings.Count == 0)
        {
            restartAction.AddBinding("<Keyboard>/r");
            restartAction.AddBinding("<Gamepad>/select");
        }
    }

    // OnEnable runs when the script is switched on: start listening to the restart key.
    private void OnEnable()
    {
        restartAction.performed += OnRestartPerformed; // "when the key is pressed, call OnRestartPerformed"
        restartAction.Enable();
    }

    // OnDisable runs when the script is switched off (or destroyed): stop listening.
    private void OnDisable()
    {
        restartAction.performed -= OnRestartPerformed;
        restartAction.Disable();
    }

    // The Input System calls this when the restart key is pressed.
    private void OnRestartPerformed(InputAction.CallbackContext context)
    {
        RestartLevel();
    }

    // Hook this to the player's Damageable -> On Died event.
    public void HandlePlayerDied()
    {
        if (isGameOver) return; // already over

        isGameOver = true;
        if (Application.isEditor) Debug.Log("Player died. Press R to restart.", this);
        if (onPlayerDied != null) onPlayerDied.Invoke();
    }

    // Public and with NO parameters on purpose: that is what lets a UI Button's On Click list (and any UnityEvent)
    // pick it from the dropdown.
    public void RestartLevel()
    {
        if (!allowRestartWhileAlive && !isGameOver) return; // restarting is only allowed after dying

        if (onRestarting != null) onRestarting.Invoke();

        GamePause.ResetAll(); // the reloaded level must not start frozen or silent (pause menu, conversation)
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex); // load this same level again
    }
}
