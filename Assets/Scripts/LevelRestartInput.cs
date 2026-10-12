using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Listens for the restart button (R by default) and asks the GameManager to restart.
/// Put it on the GameManager object, NOT on the player: the player can be destroyed when it dies,
/// and the restart key still has to work after that.
/// When your UI is finished you can keep this as a shortcut or simply disable the component:
/// UI Buttons call GameManager.RestartLevel() directly and never need this script.
/// </summary>
public class LevelRestartInput : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameManager gameManager;

    [Header("Input (edit bindings directly in the Inspector)")]
    [SerializeField] private InputAction restartAction;

    private void Awake()
    {
        if (gameManager == null) TryGetComponent(out gameManager);

        if (gameManager == null)
        {
            Debug.LogError($"{nameof(LevelRestartInput)} needs a {nameof(GameManager)}.", this);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        restartAction.performed += OnRestartPerformed;
        restartAction.Enable();
    }

    private void OnDisable()
    {
        restartAction.performed -= OnRestartPerformed;
        restartAction.Disable();
    }

    private void OnRestartPerformed(InputAction.CallbackContext context) => gameManager.RestartLevel();

    // Reset() runs in the editor when the component is first added (or via the "Reset" menu)
    // and fills in default bindings, same as PlayerInputReader.
    private void Reset()
    {
        restartAction = new InputAction("Restart", InputActionType.Button);
        restartAction.AddBinding("<Keyboard>/r");
        restartAction.AddBinding("<Gamepad>/select");
    }
}
