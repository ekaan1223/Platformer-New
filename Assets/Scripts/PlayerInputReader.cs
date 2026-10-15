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
    [Tooltip("Hold to sprint. If this is empty (a player made before sprint existed), Left Shift and " +
             "Left Stick Press are added automatically.")]
    [SerializeField] private InputAction sprintAction;

    [Header("Shift: tap = dash, hold = sprint")]
    [Tooltip("Hold the sprint button this long before sprinting starts. A press shorter than this is a TAP.")]
    [SerializeField, Min(0.05f)] private float holdToSprintSeconds = 0.25f;
    [Tooltip("ON = dash happens when you RELEASE a quick tap, so the same button can be held to sprint. " +
             "OFF = dash happens the instant you press (use this if dash and sprint are on different buttons).")]
    [SerializeField] private bool dashOnTapRelease = true;

    private float lastJumpPressedTime = float.NegativeInfinity;
    private float lastDashPressedTime = float.NegativeInfinity;
    private float lastAttackPressedTime = float.NegativeInfinity;
    private float dashPressStartTime = float.NegativeInfinity;
    private float sprintPressStartTime = float.NegativeInfinity;
    private bool dashFiredOnPress;
    private PlayerMotor motor;

    /// <summary>Raised the instant jump is pressed (used for things like dash-cancel).</summary>
    public event Action JumpPressed;

    public Vector2 Move { get; private set; }
    public bool JumpHeld => jumpAction.IsPressed();

    /// <summary>True while the sprint button has been held longer than Hold To Sprint Seconds.</summary>
    public bool SprintHeld { get; private set; }

    public float SecondsSinceJumpPressed => Time.time - lastJumpPressedTime;

    public bool IsJumpBuffered(float windowSeconds) => Time.time - lastJumpPressedTime <= windowSeconds;
    public bool IsDashBuffered(float windowSeconds) => Time.time - lastDashPressedTime <= windowSeconds;
    public bool IsAttackBuffered(float windowSeconds) => Time.time - lastAttackPressedTime <= windowSeconds;

    // Consuming = "this press has been used, forget it".
    public void ConsumeJump() => lastJumpPressedTime = float.NegativeInfinity;
    public void ConsumeDash() => lastDashPressedTime = float.NegativeInfinity;
    public void ConsumeAttack() => lastAttackPressedTime = float.NegativeInfinity;

    private void Awake() => TryGetComponent(out motor);

    private void OnEnable()
    {
        EnsureSprintBindings();

        jumpAction.performed += OnJumpPerformed;
        dashAction.started += OnDashStarted;
        dashAction.performed += OnDashPerformed;
        dashAction.canceled += OnDashCanceled;
        sprintAction.started += OnSprintStarted;
        attackAction.performed += OnAttackPerformed;

        moveAction.Enable();
        jumpAction.Enable();
        dashAction.Enable();
        sprintAction.Enable();
        attackAction.Enable();
    }

    private void OnDisable()
    {
        jumpAction.performed -= OnJumpPerformed;
        dashAction.started -= OnDashStarted;
        dashAction.performed -= OnDashPerformed;
        dashAction.canceled -= OnDashCanceled;
        sprintAction.started -= OnSprintStarted;
        attackAction.performed -= OnAttackPerformed;

        moveAction.Disable();
        jumpAction.Disable();
        dashAction.Disable();
        sprintAction.Disable();
        attackAction.Disable();

        SprintHeld = false;
    }

    private void Update()
    {
        Move = moveAction.ReadValue<Vector2>();

        // Sprinting = the button is down AND has been down long enough to not be a tap.
        SprintHeld = sprintAction.IsPressed() && Time.time - sprintPressStartTime >= holdToSprintSeconds;
    }

    /// <summary>
    /// A player that was set up before sprint existed has an empty sprint action (Reset() only runs when the
    /// component is first added). Give it the default bindings so sprint works without any manual setup.
    /// Must happen BEFORE the action is enabled: bindings can't be added to a running action.
    /// </summary>
    private void EnsureSprintBindings()
    {
        if (sprintAction == null || sprintAction.bindings.Count > 0) return;

        sprintAction.AddBinding("<Keyboard>/leftShift");
        sprintAction.AddBinding("<Gamepad>/leftStickPress");
    }

    private void OnJumpPerformed(InputAction.CallbackContext context)
    {
        lastJumpPressedTime = Time.time;
        JumpPressed?.Invoke();
    }

    // Pressing dash in the AIR (or with the instant-dash option) fires right away: there is no sprint to wait for,
    // because sprint can only start on the ground. Only a ground press has to wait to see if it is a tap or a hold.
    private void OnDashStarted(InputAction.CallbackContext context)
    {
        dashPressStartTime = Time.time;
        dashFiredOnPress = false;

        if (dashOnTapRelease && motor != null && !motor.IsGrounded)
        {
            lastDashPressedTime = Time.time;
            dashFiredOnPress = true;   // so the release doesn't dash a second time
        }
    }

    // Instant dash (the old behaviour), only when "Dash On Tap Release" is OFF.
    private void OnDashPerformed(InputAction.CallbackContext context)
    {
        if (!dashOnTapRelease) lastDashPressedTime = Time.time;
    }

    // Tap-to-dash: the button was released. A quick press is a dash; a long press was a sprint, so no dash.
    private void OnDashCanceled(InputAction.CallbackContext context)
    {
        if (dashFiredOnPress)
        {
            dashFiredOnPress = false;
            return;
        }

        if (dashOnTapRelease && Time.time - dashPressStartTime < holdToSprintSeconds)
            lastDashPressedTime = Time.time;
    }

    private void OnSprintStarted(InputAction.CallbackContext context) => sprintPressStartTime = Time.time;
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

        sprintAction = new InputAction("Sprint", InputActionType.Button);
        sprintAction.AddBinding("<Keyboard>/leftShift");
        sprintAction.AddBinding("<Gamepad>/leftStickPress");

        attackAction = new InputAction("Attack", InputActionType.Button);
        attackAction.AddBinding("<Mouse>/leftButton");
        attackAction.AddBinding("<Keyboard>/j");
        attackAction.AddBinding("<Gamepad>/buttonWest");
    }
}
