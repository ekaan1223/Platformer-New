using System;
using UnityEngine;

/// <summary>
/// The ONLY script that moves the character. Owns velocity, gravity, ground detection and rotation.
/// Abilities (jump, dash, attack) never call CharacterController.Move themselves; they change
/// velocity or set flags on this motor. That keeps them modular and conflict-free.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerMotor : MonoBehaviour
{
    private const float GroundCheckRadiusScale = 0.9f;
    private const float AscendingThreshold = 0.01f;       // m/s; above this we are "going up", never grounded
    private const float InputDeadzoneSqr = 0.0001f;
    private const float MinFacingSpeedSqr = 0.01f;

    [Header("References")]
    [SerializeField] private PlayerInputReader input;
    [Tooltip("Movement is relative to this camera. Empty = Camera.main.")]
    [SerializeField] private Transform cameraTransform;

    [Header("Ground Movement")]
    [SerializeField, Min(0f)] private float maxSpeedMetersPerSecond = 2.5f;
    [SerializeField, Min(0f)] private float groundAcceleration = 18f;
    [SerializeField, Min(0f)] private float groundDeceleration = 24f;

    [Header("Air Movement")]
    [SerializeField, Min(0f)] private float airAcceleration = 10f;
    [SerializeField, Min(0f)] private float airDeceleration = 3f;

    [Header("Rotation")]
    [SerializeField, Min(0f)] private float turnSpeedDegreesPerSecond = 720f;

    [Header("Gravity")]
    [SerializeField, Min(0f)] private float gravity = 20f;
    [Tooltip("Falling is heavier than rising: snappier, less floaty.")]
    [SerializeField, Min(1f)] private float fallGravityMultiplier = 1.5f;
    [SerializeField, Min(0f)] private float maxFallSpeedMetersPerSecond = 10f;
    [Tooltip("Small downward push while grounded so we stay glued to slopes.")]
    [SerializeField, Min(0f)] private float groundStickSpeed = 1f;

    [Header("Ground Check")]
    [Tooltip("Do NOT include the Player layer here.")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField, Min(0f)] private float groundCheckDistanceMeters = 0.03f;

    private CharacterController controller;

    // ---- Public state (read by abilities) ----
    public bool IsGrounded { get; private set; }
    public float LastGroundedTime { get; private set; } = float.NegativeInfinity;
    public Vector3 WorldInputDirection { get; private set; }
    public Vector3 Facing => transform.forward;
    public float Gravity => gravity;

    // ---- Public controls (written by abilities) ----
    // Each flag should have ONE owner ability (dash owns the first two, attack owns the last two).
    public Vector3 HorizontalVelocity { get; set; }
    public float VerticalVelocity { get; set; }
    public bool HorizontalControlLocked { get; set; }
    public bool GravityEnabled { get; set; } = true;
    public bool RotationLocked { get; set; }
    public float SpeedMultiplier { get; set; } = 1f;

    public event Action Landed;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (input == null) TryGetComponent(out input);
        if (input == null)
        {
            Debug.LogError($"{nameof(PlayerMotor)} needs a {nameof(PlayerInputReader)}.", this);
            enabled = false;
            return;
        }

        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;

        UpdateWorldInputDirection();
        ApplyHorizontal(deltaTime);
        ApplyVertical(deltaTime);
        MoveController(deltaTime);
        UpdateGrounded();
        Rotate(deltaTime);
    }

    /// <summary>True if we would touch ground within 'seconds' at the current fall speed.</summary>
    public bool WillLandWithin(float seconds)
    {
        if (VerticalVelocity >= 0f) return false;
        return CastGround(-VerticalVelocity * seconds);
    }

    /// <summary>Instantly turn to face a direction (ignores height).</summary>
    public void FaceDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < InputDeadzoneSqr) return;
        transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    // ------------------------------------------------------------------

    private void UpdateWorldInputDirection()
    {
        Vector2 raw = input.Move;
        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;

        if (cameraTransform != null)
        {
            forward = cameraTransform.forward;
            right = cameraTransform.right;
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();
        }

        WorldInputDirection = Vector3.ClampMagnitude(forward * raw.y + right * raw.x, 1f);
    }

    private void ApplyHorizontal(float deltaTime)
    {
        if (HorizontalControlLocked) return;

        Vector3 targetVelocity = WorldInputDirection * (maxSpeedMetersPerSecond * SpeedMultiplier);
        bool hasInput = WorldInputDirection.sqrMagnitude > InputDeadzoneSqr;

        float rate;
        if (IsGrounded) rate = hasInput ? groundAcceleration : groundDeceleration;
        else rate = hasInput ? airAcceleration : airDeceleration;

        HorizontalVelocity = Vector3.MoveTowards(HorizontalVelocity, targetVelocity, rate * deltaTime);
    }

    private void ApplyVertical(float deltaTime)
    {
        if (IsGrounded && VerticalVelocity <= 0f)
        {
            VerticalVelocity = -groundStickSpeed;
            return;
        }

        if (!GravityEnabled) return;

        float appliedGravity = gravity * (VerticalVelocity < 0f ? fallGravityMultiplier : 1f);
        VerticalVelocity = Mathf.Max(VerticalVelocity - appliedGravity * deltaTime, -maxFallSpeedMetersPerSecond);
    }

    private void MoveController(float deltaTime)
    {
        Vector3 velocity = HorizontalVelocity + Vector3.up * VerticalVelocity;
        CollisionFlags flags = controller.Move(velocity * deltaTime);

        // Bonked our head: stop rising.
        if ((flags & CollisionFlags.Above) != 0 && VerticalVelocity > 0f) VerticalVelocity = 0f;
    }

    private void UpdateGrounded()
    {
        bool wasGrounded = IsGrounded;

        // While moving upward we are never "grounded" (prevents a second ground jump mid-air).
        IsGrounded = VerticalVelocity <= AscendingThreshold && CastGround(0f);

        if (IsGrounded) LastGroundedTime = Time.time;
        if (IsGrounded && !wasGrounded) Landed?.Invoke();
    }

    private bool CastGround(float extraDistanceMeters)
    {
        // Sphere at the bottom of the capsule, slightly smaller than the capsule so we don't catch walls.
        float castRadius = controller.radius * GroundCheckRadiusScale;
        Vector3 bottomSphereCenter = transform.TransformPoint(controller.center)
                                     + Vector3.down * (controller.height * 0.5f - controller.radius);

        float distance = (controller.radius - castRadius) + controller.skinWidth
                         + groundCheckDistanceMeters + extraDistanceMeters;

        if (!Physics.SphereCast(bottomSphereCenter, castRadius, Vector3.down, out RaycastHit hit,
                distance, groundMask, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        return Vector3.Angle(hit.normal, Vector3.up) <= controller.slopeLimit;
    }

    private void Rotate(float deltaTime)
    {
        if (RotationLocked) return;

        // Face where the player is steering; if locked (dash) or no input, face travel direction.
        bool steering = !HorizontalControlLocked && WorldInputDirection.sqrMagnitude > InputDeadzoneSqr;
        Vector3 lookDirection = steering ? WorldInputDirection : HorizontalVelocity;
        lookDirection.y = 0f;

        if (lookDirection.sqrMagnitude < MinFacingSpeedSqr) return;

        Quaternion target = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeedDegreesPerSecond * deltaTime);
    }

    // ---- Editor helpers --------------------------------------------------

    // Runs when the component is first added: sizes the CharacterController for a 40 cm character.
    private void Reset() => ApplySmallCharacterSettings();

    [ContextMenu("Apply 40cm Character Controller Settings")]
    private void ApplySmallCharacterSettings()
    {
        CharacterController c = GetComponent<CharacterController>();
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(c, "Apply 40cm Character Controller Settings");
#endif
        c.height = 0.4f;                       // 40 cm tall
        c.radius = 0.12f;
        c.center = new Vector3(0f, 0.2f, 0f);  // pivot (transform origin) sits at the feet
        c.skinWidth = 0.01f;                   // default 0.08 would be 20% of our height!
        c.stepOffset = 0.1f;
        c.slopeLimit = 50f;
        c.minMoveDistance = 0f;
    }
}
