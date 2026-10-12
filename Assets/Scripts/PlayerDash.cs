using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Ground dash and air dash. Direction = movement input, or facing direction if no input.
/// Owns the motor's HorizontalControlLocked and GravityEnabled flags while dashing.
/// </summary>
[DefaultExecutionOrder(-10)]
public class PlayerDash : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerInputReader input;
    [SerializeField] private PlayerMotor motor;

    [Header("Dash")]
    [SerializeField, Min(0f)] private float dashSpeedMetersPerSecond = 6f;
    [SerializeField, Min(0.01f)] private float dashDurationSeconds = 0.15f;
    [SerializeField, Min(0f)] private float cooldownSeconds = 0.35f;
    [SerializeField, Min(0f)] private float dashBufferSeconds = 0.1f;

    [Header("Air Dash")]
    [SerializeField, Min(0)] private int maxAirDashes = 1;
    [Tooltip("Hover in a straight line while dashing (Celeste style).")]
    [SerializeField] private bool freezeGravityWhileDashing = true;

    [Header("Feel")]
    [Tooltip("Fraction of dash speed kept when the dash ends (momentum carry-over).")]
    [SerializeField, Range(0f, 1f)] private float exitSpeedFraction = 0.5f;
    [Tooltip("Pressing jump during a dash ends it so the jump can happen immediately.")]
    [SerializeField] private bool cancelOnJump = true;

    [Header("Events")]
    [SerializeField] private UnityEvent onDashStarted;
    [SerializeField] private UnityEvent onDashEnded;

    private Vector3 dashDirection;
    private float dashSecondsRemaining;
    private float cooldownSecondsRemaining;
    private int airDashesUsed;

    public bool IsDashing { get; private set; }

    private void Awake()
    {
        if (input == null) TryGetComponent(out input);
        if (motor == null) TryGetComponent(out motor);

        if (input == null || motor == null)
        {
            Debug.LogError($"{nameof(PlayerDash)} needs {nameof(PlayerInputReader)} and {nameof(PlayerMotor)}.", this);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        if (input != null) input.JumpPressed += OnJumpPressed;
    }

    private void OnDisable()
    {
        if (input != null) input.JumpPressed -= OnJumpPressed;
        if (IsDashing) EndDash(); // never leave the motor locked
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        cooldownSecondsRemaining -= deltaTime;

        if (motor.IsGrounded && !IsDashing) airDashesUsed = 0;

        if (IsDashing)
        {
            TickDash(deltaTime);
            return;
        }

        if (input.IsDashBuffered(dashBufferSeconds) && CanDash()) StartDash();
    }

    private bool CanDash()
    {
        if (cooldownSecondsRemaining > 0f) return false;
        return motor.IsGrounded || airDashesUsed < maxAirDashes;
    }

    private void StartDash()
    {
        bool hasInput = motor.WorldInputDirection.sqrMagnitude > 0.01f;
        dashDirection = hasInput ? motor.WorldInputDirection.normalized : motor.Facing;
        dashDirection.y = 0f;

        if (!motor.IsGrounded) airDashesUsed++;

        IsDashing = true;
        dashSecondsRemaining = dashDurationSeconds;
        input.ConsumeDash();

        motor.HorizontalControlLocked = true;
        if (freezeGravityWhileDashing)
        {
            motor.GravityEnabled = false;
            motor.VerticalVelocity = 0f;
        }

        motor.HorizontalVelocity = dashDirection * dashSpeedMetersPerSecond;
        onDashStarted?.Invoke();
    }

    private void TickDash(float deltaTime)
    {
        dashSecondsRemaining -= deltaTime;
        motor.HorizontalVelocity = dashDirection * dashSpeedMetersPerSecond;
        if (freezeGravityWhileDashing) motor.VerticalVelocity = 0f;

        if (dashSecondsRemaining <= 0f) EndDash();
    }

    private void EndDash()
    {
        IsDashing = false;
        cooldownSecondsRemaining = cooldownSeconds;

        motor.HorizontalControlLocked = false;
        motor.GravityEnabled = true;
        motor.HorizontalVelocity = Vector3.ClampMagnitude(motor.HorizontalVelocity,
            dashSpeedMetersPerSecond * exitSpeedFraction);

        onDashEnded?.Invoke();
    }

    private void OnJumpPressed()
    {
        if (IsDashing && cancelOnJump) EndDash();
    }
}
