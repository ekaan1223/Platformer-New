using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Ground jump, coyote time, jump buffering, variable jump height and air jumps (double jump).
/// </summary>
[DefaultExecutionOrder(-10)] // Runs before PlayerMotor so the motor sees our velocity this frame.
public class PlayerJump : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerInputReader input;
    [SerializeField] private PlayerMotor motor;

    [Header("Ground Jump")]
    [SerializeField, Min(0f)] private float jumpHeightMeters = 0.8f;

    [Header("Variable Jump Height")]
    [SerializeField] private bool allowVariableHeight = true;
    [Tooltip("Upward speed is multiplied by this when jump is released early. Lower = shorter hop.")]
    [SerializeField, Range(0f, 1f)] private float releaseVelocityMultiplier = 0.4f;

    [Header("Forgiveness")]
    [Tooltip("Jump pressed this long BEFORE landing still counts.")]
    [SerializeField, Min(0f)] private float jumpBufferSeconds = 0.15f;
    [Tooltip("Can still jump this long AFTER walking off a ledge.")]
    [SerializeField, Min(0f)] private float coyoteSeconds = 0.12f;

    [Header("Air Jumps (double jump)")]
    [SerializeField, Min(0)] private int maxAirJumps = 1;
    [SerializeField, Min(0f)] private float airJumpHeightMeters = 0.6f;

    [Header("Events (hook animation / sound / VFX)")]
    [SerializeField] private UnityEvent onGroundJump;
    [SerializeField] private UnityEvent onAirJump;

    private bool groundJumpAvailable;
    private int airJumpsUsed;
    private bool releaseCutPending;

    public int AirJumpsRemaining => Mathf.Max(0, maxAirJumps - airJumpsUsed);

    private void Awake()
    {
        if (input == null) TryGetComponent(out input);
        if (motor == null) TryGetComponent(out motor);

        if (input == null || motor == null)
        {
            Debug.LogError($"{nameof(PlayerJump)} needs {nameof(PlayerInputReader)} and {nameof(PlayerMotor)}.", this);
            enabled = false;
        }
    }

    private void Update()
    {
        if (motor.IsGrounded)
        {
            groundJumpAvailable = true;
            airJumpsUsed = 0;
        }

        TryConsumeBufferedJump();
        ApplyReleaseCut();
    }

    private void TryConsumeBufferedJump()
    {
        if (!input.IsJumpBuffered(jumpBufferSeconds)) return;

        if (CanGroundJump())
        {
            PerformJump(jumpHeightMeters);
            groundJumpAvailable = false;
            onGroundJump?.Invoke();
        }
        else if (CanAirJump())
        {
            PerformJump(airJumpHeightMeters);
            airJumpsUsed++;
            onAirJump?.Invoke();
        }
    }

    private bool CanGroundJump()
    {
        if (!groundJumpAvailable) return false;
        bool withinCoyoteTime = Time.time - motor.LastGroundedTime <= coyoteSeconds;
        return motor.IsGrounded || withinCoyoteTime;
    }

    private bool CanAirJump()
    {
        if (airJumpsUsed >= maxAirJumps) return false;

        // If we will touch the ground before this press expires, keep the press buffered so it
        // becomes a proper ground jump instead of wasting the double jump right before landing.
        float bufferRemaining = jumpBufferSeconds - input.SecondsSinceJumpPressed;
        return !motor.WillLandWithin(bufferRemaining);
    }

    private void PerformJump(float heightMeters)
    {
        // From v^2 = 2 * g * h  ->  v = sqrt(2 * g * h): "how fast to leave the ground to reach height h".
        motor.VerticalVelocity = Mathf.Sqrt(2f * motor.Gravity * heightMeters);
        releaseCutPending = true;
        input.ConsumeJump();
    }

    private void ApplyReleaseCut()
    {
        if (!allowVariableHeight || !releaseCutPending) return;

        if (motor.VerticalVelocity <= 0f)
        {
            releaseCutPending = false; // already past the apex, nothing to cut
        }
        else if (!input.JumpHeld)
        {
            motor.VerticalVelocity *= releaseVelocityMultiplier;
            releaseCutPending = false;
        }
    }
}
