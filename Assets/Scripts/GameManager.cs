using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

/// <summary>
/// Knows "is the game over?" and knows how to restart the level.
/// It does NOT care who asks for a restart: the R key (LevelRestartInput), a UI Button, a Game Over
/// screen, or the player dying can all call the same public methods. That is what lets you swap the
/// key for buttons later without touching this script.
/// Not a singleton on purpose: wire it up in the Inspector with UnityEvents, like Damageable.
/// Restarting reloads the scene, so the player, enemies and everything else go back to how you placed them.
/// </summary>
public class GameManager : MonoBehaviour
{
    [Header("Restart")]
    [Tooltip("ON = restart works any time (R mid-level, a Restart button in a pause menu). " +
             "OFF = only after the player has died.")]
    [SerializeField] private bool allowRestartWhileAlive = true;

    [Header("Events (hook UI / sound / fades here later)")]
    [Tooltip("Fires once when the player dies. Later: show the Game Over panel here.")]
    [SerializeField] private UnityEvent onPlayerDied;
    [Tooltip("Fires right before the scene reloads. Later: a fade-out or a sound.")]
    [SerializeField] private UnityEvent onRestarting;

    public bool IsGameOver { get; private set; }

    /// <summary>Hook this to the player's Damageable -> On Died event.</summary>
    public void HandlePlayerDied()
    {
        if (IsGameOver) return;

        IsGameOver = true;
        Debug.Log("Player died. Press R to restart.", this);
        onPlayerDied?.Invoke();
    }

    /// <summary>
    /// Public and with NO parameters on purpose: that is what lets a UI Button's OnClick list
    /// (and any UnityEvent) pick it from the dropdown.
    /// </summary>
    public void RestartLevel()
    {
        if (!allowRestartWhileAlive && !IsGameOver) return;

        onRestarting?.Invoke();

        // If a pause menu ever sets timeScale to 0, the reloaded level must not start frozen.
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
