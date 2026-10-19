using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The whole player in ONE script: reads input, moves the CharacterController (walk, sprint, jump,
/// double jump, dash, gravity) and plays the matching animation.
///
/// Other scripts never move the player or touch the Animator themselves. They ask through the public
/// methods at the bottom (Launch, BeginAttack / EndAttack, PlayHit, Die, Teleport). So there is exactly one
/// place that owns the velocity and one place that decides which animation plays.
///
/// ANIMATOR SETUP: the Animator sits on the model (a child of this object). Its controller only needs states
/// with the names in <see cref="AnimState"/>. No parameters and no transitions: this script cross-fades between them.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    private const float InputDeadzoneSqr = 0.0001f;
    private const float GroundCheckRadiusScale = 0.9f;  // ground probe slightly thinner than the capsule, so walls don't count
    private const float RisingThreshold = 0.01f;        // m/s; moving up faster than this is never "grounded"
    private const float FallAnimationDelaySeconds = 0.1f; // tiny drops (steps, slopes) don't flash the fall animation

    /// <summary>Animator state names. The Animator Controller must have states with exactly these names.</summary>
    public static class AnimState
    {
        public const string Idle = "Idle";
        public const string Walk = "Walk";
        public const string Run = "Run";
        public const string Jump = "Jump";
        public const string DoubleJump = "DoubleJump";
        public const string Fall = "Fall";
        public const string Dash = "Dash";
        public const string Attack = "Attack";
        public const string Hit = "Hit";
        public const string Death = "Death";

        public static readonly string[] All = { Idle, Walk, Run, Jump, DoubleJump, Fall, Dash, Attack, Hit, Death };
    }

    private static readonly int IdleHash = Animator.StringToHash(AnimState.Idle);
    private static readonly int WalkHash = Animator.StringToHash(AnimState.Walk);
    private static readonly int RunHash = Animator.StringToHash(AnimState.Run);
    private static readonly int JumpHash = Animator.StringToHash(AnimState.Jump);
    private static readonly int DoubleJumpHash = Animator.StringToHash(AnimState.DoubleJump);
    private static readonly int FallHash = Animator.StringToHash(AnimState.Fall);
    private static readonly int DashHash = Animator.StringToHash(AnimState.Dash);
    private static readonly int AttackHash = Animator.StringToHash(AnimState.Attack);
    private static readonly int HitHash = Animator.StringToHash(AnimState.Hit);
    private static readonly int DeathHash = Animator.StringToHash(AnimState.Death);

    [Header("References (empty = found automatically)")]
    [Tooltip("Movement is relative to this camera. Empty = Camera.main.")]
    [SerializeField] private Transform cameraTransform;
    [Tooltip("The Animator on the character model (a child of the player).")]
    [SerializeField] private Animator animator;
    [Tooltip("Put on the model's Animator at start. Empty = keep whatever controller the Animator already has.")]
    [SerializeField] private RuntimeAnimatorController animatorController;

    [Header("Input (empty bindings get sensible defaults)")]
    [SerializeField] private InputAction moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
    [SerializeField] private InputAction jumpAction = new InputAction("Jump", InputActionType.Button);
    [SerializeField] private InputAction sprintAction = new InputAction("Sprint", InputActionType.Button);
    [SerializeField] private InputAction dashAction = new InputAction("Dash", InputActionType.Button);
    [SerializeField] private InputAction attackAction = new InputAction("Attack", InputActionType.Button);

    [Header("Walk & Sprint")]
    [SerializeField, Min(0f)] private float walkSpeed = 5f;
    [Tooltip("Top speed while the sprint button is held.")]
    [SerializeField, Min(0f)] private float sprintSpeed = 8f;
    [SerializeField, Min(0f)] private float groundAcceleration = 18f;
    [SerializeField, Min(0f)] private float groundDeceleration = 24f;
    [SerializeField, Min(0f)] private float airAcceleration = 10f;
    [SerializeField, Min(0f)] private float airDeceleration = 3f;

    [Header("Rotation")]
    [Tooltip("ON = always face where the camera looks (S walks backwards, A/D strafe). OFF = turn to face the walking direction.")]
    [SerializeField] private bool faceCameraDirection = true;
    [SerializeField, Min(0f)] private float turnSpeed = 720f;

    [Header("Jump")]
    [SerializeField, Min(0f)] private float jumpHeight = 1f;
    [Tooltip("How many extra jumps in the air (1 = double jump).")]
    [SerializeField, Min(0)] private int maxAirJumps = 1;
    [SerializeField, Min(0f)] private float airJumpHeight = 1f;
    [Tooltip("A jump pressed this long BEFORE landing still counts.")]
    [SerializeField, Min(0f)] private float jumpBufferSeconds = 0.15f;
    [Tooltip("You can still jump this long AFTER walking off a ledge.")]
    [SerializeField, Min(0f)] private float coyoteSeconds = 0.12f;
    [Tooltip("Upward speed is multiplied by this when jump is released early (short hop). 1 = always full height.")]
    [SerializeField, Range(0f, 1f)] private float jumpReleaseMultiplier = 0.4f;

    [Header("Dash")]
    [SerializeField, Min(0f)] private float dashSpeed = 10f;
    [SerializeField, Min(0.01f)] private float dashSeconds = 0.15f;
    [SerializeField, Min(0f)] private float dashCooldownSeconds = 0.35f;
    [SerializeField, Min(0f)] private float dashBufferSeconds = 0.1f;
    [SerializeField, Min(0)] private int maxAirDashes = 1;
    [Tooltip("Fraction of dash speed kept when the dash ends.")]
    [SerializeField, Range(0f, 1f)] private float dashExitSpeedFraction = 0.5f;

    [Header("Gravity")]
    [SerializeField, Min(0f)] private float gravity = 20f;
    [Tooltip("Falling is heavier than rising: snappier, less floaty.")]
    [SerializeField, Min(1f)] private float fallGravityMultiplier = 1.5f;
    [SerializeField, Min(0f)] private float maxFallSpeed = 10f;
    [Tooltip("Small downward push while grounded so we stay glued to slopes.")]
    [SerializeField, Min(0f)] private float groundStickSpeed = 1f;

    [Header("Ground Check")]
    [Tooltip("What counts as ground. Do NOT include the Player layer.")]
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField, Min(0f)] private float groundCheckDistance = 0.03f;

    [Header("Animation")]
    [SerializeField, Min(0f)] private float crossFadeSeconds = 0.1f;
    [Tooltip("Below this speed (m/s) on the ground the Idle animation plays.")]
    [SerializeField, Min(0f)] private float idleSpeedThreshold = 0.1f;
    [SerializeField, Min(0f)] private float hitAnimationSeconds = 0.4f;

    private CharacterController controller;

    // Movement
    private Vector3 moveDirection;          // input, in world space, relative to the camera
    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private bool isGrounded;
    private float lastGroundedTime = float.NegativeInfinity;
    private bool isSprinting;

    // Jump
    private bool groundJumpAvailable;
    private int airJumpsUsed;
    private bool jumpCutAvailable;
    private bool lastJumpWasAirJump;

    // Dash
    private bool isDashing;
    private Vector3 dashDirection;
    private float dashTimeLeft;
    private float dashCooldownLeft;
    private int airDashesUsed;

    // Input buffer: WHEN each button was last pressed (NegativeInfinity = no press waiting)
    private float jumpPressedTime = float.NegativeInfinity;
    private float dashPressedTime = float.NegativeInfinity;
    private float attackPressedTime = float.NegativeInfinity;

    // Actions requested by other scripts
    private bool isAttacking;
    private float attackSpeedMultiplier = 1f;
    private bool rotationLocked;
    private float hitTimeLeft;
    private bool isDead;

    // Animation
    private int currentAnimation;
    private bool restartAnimation;

    // ---- Read-only state for other scripts ------------------------------------------------
    public bool IsGrounded => isGrounded;
    public bool IsDashing => isDashing;
    public bool IsSprinting => isSprinting;
    public bool IsDead => isDead;
    public bool JumpHeld => jumpAction.IsPressed();
    public float Gravity => gravity;
    public float VerticalVelocity => verticalVelocity;
    public Vector3 HorizontalVelocity => horizontalVelocity;

    /// <summary>Raised the frame the player touches the ground again.</summary>
    public event Action Landed;

    // =====================================================================================
    // Unity
    // =====================================================================================

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (!controller.enabled)
        {
            Debug.LogWarning($"{name}: the CharacterController was switched off, so the player could not move. Switching it on.", this);
            controller.enabled = true;
        }

        if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;

        SetUpAnimator();
        AddDefaultBindings();

        // Remember the press time, so a press slightly too early still counts (input buffering).
        jumpAction.performed += _ => jumpPressedTime = Time.time;
        dashAction.performed += _ => dashPressedTime = Time.time;
        attackAction.performed += _ => attackPressedTime = Time.time;
    }

    private void Start()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        foreach (string state in AnimState.All)
        {
            if (!animator.HasState(0, Animator.StringToHash(state)))
                Debug.LogWarning($"{name}: the Animator Controller has no state called '{state}', so that animation won't play.", this);
        }
    }

    private void OnEnable()
    {
        moveAction.Enable();
        jumpAction.Enable();
        sprintAction.Enable();
        dashAction.Enable();
        attackAction.Enable();
    }

    private void OnDisable()
    {
        moveAction.Disable();
        jumpAction.Disable();
        sprintAction.Disable();
        dashAction.Disable();
        attackAction.Disable();
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;

        ReadMoveInput();
        if (hitTimeLeft > 0f) hitTimeLeft -= deltaTime;

        if (!isDead)
        {
            UpdateDash(deltaTime);
            UpdateJump();
        }

        UpdateSprint();
        ApplyHorizontal(deltaTime);
        ApplyVertical(deltaTime);
        MoveCharacter(deltaTime);
        UpdateGrounded();
        Rotate(deltaTime);
        UpdateAnimation();
    }

    // =====================================================================================
    // Movement
    // =====================================================================================

    private void ReadMoveInput()
    {
        Vector2 raw = isDead ? Vector2.zero : moveAction.ReadValue<Vector2>();

        Vector3 forward = Vector3.forward;
        Vector3 right = Vector3.right;
        if (cameraTransform != null)
        {
            forward = Flatten(cameraTransform.forward);
            right = Flatten(cameraTransform.right);
        }

        moveDirection = Vector3.ClampMagnitude(forward * raw.y + right * raw.x, 1f);
    }

    /// <summary>Sprint is decided on the ground only, so a sprinting jump keeps its speed in the air.</summary>
    private void UpdateSprint()
    {
        if (!isGrounded) return;

        isSprinting = sprintAction.IsPressed() && HasMoveInput && !isAttacking && !isDashing && !isDead;
    }

    private void ApplyHorizontal(float deltaTime)
    {
        if (isDashing)
        {
            horizontalVelocity = dashDirection * dashSpeed;
            return;
        }

        float topSpeed = (isSprinting ? sprintSpeed : walkSpeed) * (isAttacking ? attackSpeedMultiplier : 1f);
        Vector3 targetVelocity = moveDirection * topSpeed;

        float rate;
        if (isGrounded) rate = HasMoveInput ? groundAcceleration : groundDeceleration;
        else rate = HasMoveInput ? airAcceleration : airDeceleration;

        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, rate * deltaTime);
    }

    private void ApplyVertical(float deltaTime)
    {
        if (isDashing)
        {
            verticalVelocity = 0f; // dash flies in a straight line
            return;
        }

        if (isGrounded && verticalVelocity <= 0f)
        {
            verticalVelocity = -groundStickSpeed;
            return;
        }

        float appliedGravity = gravity * (verticalVelocity < 0f ? fallGravityMultiplier : 1f);
        verticalVelocity = Mathf.Max(verticalVelocity - appliedGravity * deltaTime, -maxFallSpeed);
    }

    private void MoveCharacter(float deltaTime)
    {
        Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity;
        CollisionFlags flags = controller.Move(velocity * deltaTime);

        // Bonked our head: stop rising.
        if ((flags & CollisionFlags.Above) != 0 && verticalVelocity > 0f) verticalVelocity = 0f;
    }

    private void UpdateGrounded()
    {
        bool wasGrounded = isGrounded;

        // While moving upward we are never grounded (prevents a second ground jump right after jumping).
        isGrounded = verticalVelocity <= RisingThreshold && CastGround(0f);

        if (isGrounded)
        {
            lastGroundedTime = Time.time;
            groundJumpAvailable = true;
            airJumpsUsed = 0;
            if (!isDashing) airDashesUsed = 0;
        }

        if (isGrounded && !wasGrounded) Landed?.Invoke();
    }

    private void Rotate(float deltaTime)
    {
        if (rotationLocked || isDead) return;

        Vector3 lookDirection;
        if (isDashing) lookDirection = dashDirection;
        else if (faceCameraDirection && cameraTransform != null) lookDirection = Flatten(cameraTransform.forward);
        else lookDirection = moveDirection;

        lookDirection.y = 0f;
        if (lookDirection.sqrMagnitude < InputDeadzoneSqr) return;

        Quaternion target = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * deltaTime);
    }

    // =====================================================================================
    // Jump
    // =====================================================================================

    private void UpdateJump()
    {
        if (IsBuffered(jumpPressedTime, jumpBufferSeconds))
        {
            bool withinCoyoteTime = Time.time - lastGroundedTime <= coyoteSeconds;

            if (groundJumpAvailable && (isGrounded || withinCoyoteTime))
            {
                groundJumpAvailable = false;
                Jump(jumpHeight, airJump: false);
            }
            else if (airJumpsUsed < maxAirJumps && !WillLandBeforeBufferEnds())
            {
                airJumpsUsed++;
                Jump(airJumpHeight, airJump: true);
            }
        }

        // Variable height: letting go of jump while still rising cuts the jump short.
        if (!jumpCutAvailable) return;

        if (verticalVelocity <= 0f)
        {
            jumpCutAvailable = false;
        }
        else if (!jumpAction.IsPressed())
        {
            verticalVelocity *= jumpReleaseMultiplier;
            jumpCutAvailable = false;
        }
    }

    private void Jump(float height, bool airJump)
    {
        if (isDashing) EndDash(); // jumping cancels a dash

        verticalVelocity = JumpSpeed(height);
        jumpPressedTime = float.NegativeInfinity;
        jumpCutAvailable = true;
        lastJumpWasAirJump = airJump;
        restartAnimation = true;
    }

    /// <summary>
    /// If we will touch the ground before the buffered press expires, keep it for a proper ground jump
    /// instead of wasting the double jump right before landing.
    /// </summary>
    private bool WillLandBeforeBufferEnds()
    {
        if (verticalVelocity >= 0f) return false;

        float bufferLeft = jumpBufferSeconds - (Time.time - jumpPressedTime);
        return CastGround(-verticalVelocity * bufferLeft);
    }

    // =====================================================================================
    // Dash
    // =====================================================================================

    private void UpdateDash(float deltaTime)
    {
        if (isDashing)
        {
            dashTimeLeft -= deltaTime;
            if (dashTimeLeft <= 0f) EndDash();
            return;
        }

        if (dashCooldownLeft > 0f) dashCooldownLeft -= deltaTime;

        if (!IsBuffered(dashPressedTime, dashBufferSeconds) || dashCooldownLeft > 0f) return;
        if (!isGrounded && airDashesUsed >= maxAirDashes) return;

        StartDash();
    }

    private void StartDash()
    {
        dashDirection = HasMoveInput ? moveDirection.normalized : transform.forward;
        dashDirection.y = 0f;

        if (!isGrounded) airDashesUsed++;

        isDashing = true;
        dashTimeLeft = dashSeconds;
        dashPressedTime = float.NegativeInfinity;
        verticalVelocity = 0f;
        jumpCutAvailable = false;
        restartAnimation = true;
    }

    private void EndDash()
    {
        isDashing = false;
        dashCooldownLeft = dashCooldownSeconds;
        horizontalVelocity = Vector3.ClampMagnitude(horizontalVelocity, dashSpeed * dashExitSpeedFraction);
    }

    // =====================================================================================
    // Animation: the ONE place that decides what the model plays
    // =====================================================================================

    private void UpdateAnimation()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        int next = ChooseAnimation();
        if (next == currentAnimation && !restartAnimation) return;
        if (!animator.HasState(0, next)) return;

        if (next == currentAnimation) animator.Play(next, 0, 0f);   // same move again (second swing, stomp bounce): restart it
        else animator.CrossFadeInFixedTime(next, crossFadeSeconds, 0);

        currentAnimation = next;
        restartAnimation = false;
    }

    /// <summary>Highest priority first.</summary>
    private int ChooseAnimation()
    {
        if (isDead) return DeathHash;
        if (hitTimeLeft > 0f) return HitHash;
        if (isAttacking) return AttackHash;
        if (isDashing) return DashHash;

        bool justLeftGround = Time.time - lastGroundedTime < FallAnimationDelaySeconds;
        if (!isGrounded && (verticalVelocity > 0f || !justLeftGround))
        {
            if (verticalVelocity > 0f) return lastJumpWasAirJump ? DoubleJumpHash : JumpHash;
            return FallHash;
        }

        if (horizontalVelocity.magnitude < idleSpeedThreshold) return IdleHash;
        return isSprinting ? RunHash : WalkHash;
    }

    // =====================================================================================
    // Public actions: the ONLY way other scripts change the player
    // =====================================================================================

    /// <summary>Uses up a buffered jump press (a jump pad that takes the press instead of a normal jump).</summary>
    public bool TryConsumeJumpPress(float bufferSeconds)
    {
        if (!IsBuffered(jumpPressedTime, bufferSeconds)) return false;
        jumpPressedTime = float.NegativeInfinity;
        return true;
    }

    /// <summary>Uses up a buffered attack press. The sword calls this to know when to swing.</summary>
    public bool TryConsumeAttackPress(float bufferSeconds)
    {
        if (!IsBuffered(attackPressedTime, bufferSeconds)) return false;
        attackPressedTime = float.NegativeInfinity;
        return true;
    }

    /// <summary>Forget any attack press made so far (clicks during a swing).</summary>
    public void ClearAttackPress() => attackPressedTime = float.NegativeInfinity;

    /// <summary>Throws the player up to the given height (stomp bounce, jump pad). Keeps the sideways speed.</summary>
    public void Launch(float heightMeters) => LaunchInternal(heightMeters, null);

    /// <summary>Throws the player up and sets the sideways speed (a cannon pad).</summary>
    public void Launch(float heightMeters, Vector3 newHorizontalVelocity) => LaunchInternal(heightMeters, newHorizontalVelocity);

    /// <summary>Starts a sword swing: aims, slows the player down and plays the attack animation. False if it can't.</summary>
    public bool BeginAttack(float moveSpeedMultiplier, bool lockRotation)
    {
        if (isDead) return false;

        // Aim: where the camera looks in camera-facing mode, otherwise toward the stick.
        Vector3 aim = faceCameraDirection && cameraTransform != null ? Flatten(cameraTransform.forward) : moveDirection;
        if (aim.sqrMagnitude > InputDeadzoneSqr) transform.rotation = Quaternion.LookRotation(aim.normalized, Vector3.up);

        isAttacking = true;
        attackSpeedMultiplier = Mathf.Clamp01(moveSpeedMultiplier);
        rotationLocked = lockRotation;
        restartAnimation = true;
        return true;
    }

    public void EndAttack()
    {
        isAttacking = false;
        attackSpeedMultiplier = 1f;
        rotationLocked = false;
    }

    /// <summary>Hook to Damageable -> On Damaged.</summary>
    public void PlayHit()
    {
        if (isDead) return;
        hitTimeLeft = hitAnimationSeconds;
        restartAnimation = true;
    }

    /// <summary>Hook to Damageable -> On Died. Stops all control and plays the death animation.</summary>
    public void Die()
    {
        if (isDead) return;

        if (isDashing) EndDash();
        EndAttack();
        isSprinting = false;
        isDead = true;
    }

    /// <summary>Instantly moves the player and clears its speed (boss arena, respawn).</summary>
    public void Teleport(Vector3 position, Quaternion rotation)
    {
        // A CharacterController overrides direct position changes, so switch it off for the move.
        controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        controller.enabled = true;

        if (isDashing) EndDash();
        horizontalVelocity = Vector3.zero;
        verticalVelocity = 0f;
    }

    // =====================================================================================
    // Helpers
    // =====================================================================================

    private bool HasMoveInput => moveDirection.sqrMagnitude > InputDeadzoneSqr;

    private static bool IsBuffered(float pressedTime, float windowSeconds) => Time.time - pressedTime <= windowSeconds;

    // From v^2 = 2 * g * h  ->  v = sqrt(2 * g * h): "how fast to leave the ground to reach height h".
    private float JumpSpeed(float heightMeters) => Mathf.Sqrt(2f * gravity * heightMeters);

    private static Vector3 Flatten(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > InputDeadzoneSqr ? direction.normalized : Vector3.zero;
    }

    private void LaunchInternal(float heightMeters, Vector3? newHorizontalVelocity)
    {
        if (isDead) return;

        if (isDashing) EndDash();
        verticalVelocity = JumpSpeed(heightMeters);

        if (newHorizontalVelocity.HasValue)
        {
            Vector3 horizontal = newHorizontalVelocity.Value;
            horizontal.y = 0f;
            horizontalVelocity = horizontal;
        }

        jumpCutAvailable = false;   // a launch always reaches its full height
        lastJumpWasAirJump = false;
        restartAnimation = true;
    }

    private bool CastGround(float extraDistance)
    {
        float castRadius = controller.radius * GroundCheckRadiusScale;
        Vector3 bottomSphereCenter = transform.TransformPoint(controller.center)
                                     + Vector3.down * (controller.height * 0.5f - controller.radius);
        float distance = (controller.radius - castRadius) + controller.skinWidth + groundCheckDistance + extraDistance;

        if (!Physics.SphereCast(bottomSphereCenter, castRadius, Vector3.down, out RaycastHit hit,
                distance, groundMask, QueryTriggerInteraction.Ignore))
        {
            return false;
        }

        return Vector3.Angle(hit.normal, Vector3.up) <= controller.slopeLimit;
    }

    private void SetUpAnimator()
    {
        if (animator == null)
        {
            // Prefer the model's Animator (a child); fall back to one on this object.
            foreach (Animator candidate in GetComponentsInChildren<Animator>())
            {
                if (candidate.gameObject == gameObject) continue;
                animator = candidate;
                break;
            }
            if (animator == null) animator = GetComponent<Animator>();
        }

        if (animator == null)
        {
            Debug.LogWarning($"{name}: no Animator found on the model, so the player moves without animations.", this);
            return;
        }

        if (animatorController != null) animator.runtimeAnimatorController = animatorController;

        // This script moves the player. If the animation moved it too (root motion), the two would fight.
        animator.applyRootMotion = false;
    }

    private void AddDefaultBindings()
    {
        if (moveAction.bindings.Count == 0)
        {
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            moveAction.AddBinding("<Gamepad>/leftStick");
        }

        AddBindingsIfEmpty(jumpAction, "<Keyboard>/space", "<Gamepad>/buttonSouth");
        AddBindingsIfEmpty(sprintAction, "<Keyboard>/leftShift", "<Gamepad>/leftStickPress");
        AddBindingsIfEmpty(dashAction, "<Keyboard>/q", "<Gamepad>/buttonEast");
        AddBindingsIfEmpty(attackAction, "<Mouse>/leftButton", "<Keyboard>/j", "<Gamepad>/buttonWest");
    }

    private static void AddBindingsIfEmpty(InputAction action, params string[] paths)
    {
        if (action.bindings.Count > 0) return;
        foreach (string path in paths) action.AddBinding(path);
    }
}
