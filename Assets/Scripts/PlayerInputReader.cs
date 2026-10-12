using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Reads raw input and remembers WHEN each button was pressed, so other scripts can
/// implement input buffering. Contains no gameplay logic.
/// Requires the "Input System" package (Package Manager).
/// </summary>
[DefaultExecutionOrder(-100)] // Runs before every other player script each frame.
public class PlayerInputReader : MonoBehaviour
{
    [Header("Input Actions (edit bindings directly in the Inspector)")]
    [SerializeField] private InputAction moveAction;
    [SerializeField] private InputAction jumpAction;
    [SerializeField] private InputAction dashAction;
    [SerializeField] private InputAction attackAction;

    private float lastJumpPressedTime = float.NegativeInfinity;
    private float lastDashPressedTime = float.NegativeInfinity;
    private float lastAttackPressedTime = float.NegativeInfinity;

    /// <summary>Raised the instant jump is pressed (used for things like dash-cancel).</summary>
    public event Action JumpPressed;

    public Vector2 Move { get; private set; }
    public bool JumpHeld => jumpAction.IsPressed();

    public float SecondsSinceJumpPressed => Time.time - lastJumpPressedTime;

    public bool IsJumpBuffered(float windowSeconds) => Time.time - lastJumpPressedTime <= windowSeconds;
    public bool IsDashBuffered(float windowSeconds) => Time.time - lastDashPressedTime <= windowSeconds;
    public bool IsAttackBuffered(float windowSeconds) => Time.time - lastAttackPressedTime <= windowSeconds;

    // Consuming = "this press has been used, forget it".
    public void ConsumeJump() => lastJumpPressedTime = float.NegativeInfinity;
    public void ConsumeDash() => lastDashPressedTime = float.NegativeInfinity;
    public void ConsumeAttack() => lastAttackPressedTime = float.NegativeInfinity;

    private void OnEnable()
    {
        jumpAction.performed += OnJumpPerformed;
        dashAction.performed += OnDashPerformed;
        attackAction.performed += OnAttackPerformed;

        moveAction.Enable();
        jumpAction.Enable();
        dashAction.Enable();
        attackAction.Enable();
    }

    private void OnDisable()
    {
        jumpAction.performed -= OnJumpPerformed;
        dashAction.performed -= OnDashPerformed;
        attackAction.performed -= OnAttackPerformed;

        moveAction.Disable();
        jumpAction.Disable();
        dashAction.Disable();
        attackAction.Disable();
    }

    private void Update()
    {
        Move = moveAction.ReadValue<Vector2>();
    }

    private void OnJumpPerformed(InputAction.CallbackContext context)
    {
        lastJumpPressedTime = Time.time;
        JumpPressed?.Invoke();
    }

    private void OnDashPerformed(InputAction.CallbackContext context) => lastDashPressedTime = Time.time;
    private void OnAttackPerformed(InputAction.CallbackContext context) => lastAttackPressedTime = Time.time;

    // Reset() runs in the editor when the component is first added (or via the "Reset" menu).
    // It fills in sensible default bindings so it works out of the box.
    private void Reset()
    {
        moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
        moveAction.AddBinding("<Gamepad>/leftStick");

        jumpAction = new InputAction("Jump", InputActionType.Button);
        jumpAction.AddBinding("<Keyboard>/space");
        jumpAction.AddBinding("<Gamepad>/buttonSouth");

        dashAction = new InputAction("Dash", InputActionType.Button);
        dashAction.AddBinding("<Keyboard>/leftShift");
        dashAction.AddBinding("<Gamepad>/buttonEast");

        attackAction = new InputAction("Attack", InputActionType.Button);
        attackAction.AddBinding("<Mouse>/leftButton");
        attackAction.AddBinding("<Keyboard>/j");
        attackAction.AddBinding("<Gamepad>/buttonWest");
    }
}
