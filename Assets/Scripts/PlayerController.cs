using UnityEngine;                // gives us access to Unity classes (MonoBehaviour, Vector3, Physics, Animator, etc.)
using UnityEngine.InputSystem;    // gives us access to the new Input System (InputAction, keyboard/gamepad bindings)

// PLAYER CONTROLLER
// Put this script on the Player object (the one with the CharacterController). The character model with the
// Animator is a CHILD of that object.
//
// This ONE script does the whole player:
//   1. reads the buttons (move, jump, sprint, dash, attack)
//   2. moves the CharacterController (walk, sprint, jump, double jump, dash, gravity)
//   3. turns the player
//   4. picks which animation plays
//
// Other scripts NEVER move the player or touch the Animator themselves. They call the public functions at the
// bottom of this file instead:
//   - PlayerSwordAttack -> TryConsumeAttackPress, BeginAttack, EndAttack, ClearAttackPress, IsGrounded
//   - PlayerStomp       -> Launch (the bounce), jumpHeight, VerticalVelocity, the Landed event (added automatically in Awake)
//   - JumpPad           -> Launch, TryConsumeJumpPress, JumpHeld, VerticalVelocity, HorizontalVelocity, gravity
//   - BossController    -> Teleport
//   - Ladder            -> StartClimbing, StopClimbing
//   - Damageable (Inspector events) -> PlayHit, Die
// So there is exactly ONE place that owns the speed and ONE place that decides the animation.
//
// WHAT HAPPENS EVERY FRAME (see Update):
//   read input -> dash -> jump -> sprint -> sideways speed -> up/down speed -> move -> ground check -> turn -> animation
//
// ANIMATOR SETUP: the Animator Controller only needs states with these exact names:
//   Idle, Walk, Run, Jump, DoubleJump, Fall, Dash, Attack, Hit, Death, Climb
// No parameters and no transitions are needed: this script blends between the states itself.

[RequireComponent(typeof(CharacterController))] // Unity adds a CharacterController automatically if the object has none
public class PlayerController : MonoBehaviour   // MonoBehaviour = a script Unity can put on an object and call every frame
{
    // =====================================================================================
    // SETTINGS (shown in the Inspector)
    // IMPORTANT: these names must stay exactly the same, because Unity saves your Inspector values by name.
    // =====================================================================================

    [Header("References (empty = found automatically)")]
    public Transform cameraTransform;                       // movement is relative to this camera; empty = Camera.main (found in Awake)
    public Animator animator;                               // the Animator on the character model; empty = searched for in SetUpAnimator
    public RuntimeAnimatorController animatorController;    // if set, it is put on the Animator at start; empty = keep the Animator's own controller

    [Header("Input (empty bindings get default keys in AddDefaultBindings)")]
    public InputAction moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2"); // WASD / arrows / left stick, gives an (x, y) value
    public InputAction jumpAction = new InputAction("Jump", InputActionType.Button);     // space / A button
    public InputAction sprintAction = new InputAction("Sprint", InputActionType.Button); // left shift / left stick click
    public InputAction dashAction = new InputAction("Dash", InputActionType.Button);     // Q / B button
    public InputAction attackAction = new InputAction("Attack", InputActionType.Button); // left mouse / J / X button

    [Header("Walk & Sprint")]
    public float walkSpeed = 5f;            // top speed while walking (meters per second)
    public float sprintSpeed = 8f;          // top speed while the sprint button is held
    // SNAPPY vs SLIPPERY: these 4 values decide it. Time to reach full speed = speed / acceleration,
    // so walking at 5 m/s with 70 takes 5 / 70 = 0.07 seconds. Lower numbers = slower to start/stop = more slippery.
    public float groundAcceleration = 70f;  // how fast we speed up on the ground (m/s gained per second)
    public float groundDeceleration = 90f;  // how fast we slow down on the ground when no key is held (higher than acceleration = stops feel crisp)
    public float airAcceleration = 40f;     // how fast we speed up in the air (lower = less air control)
    public float airDeceleration = 25f;     // how fast we slow down in the air when no key is held (low = drift and keep momentum)

    [Header("Rotation")]
    public bool faceCameraDirection = true; // ON = always face where the camera looks (S walks backwards). OFF = turn toward the walking direction
    public float turnSpeed = 720f;          // how fast the player turns (degrees per second)

    [Header("Jump")]
    public float jumpHeight = 1f;           // how high a normal jump goes (meters)
    public int maxAirJumps = 1;             // how many extra jumps in the air (1 = double jump)
    public float airJumpHeight = 1f;        // how high the double jump goes (meters)
    public float jumpBufferSeconds = 0.15f; // a jump pressed this long BEFORE landing still counts
    public float coyoteSeconds = 0.12f;     // you can still jump this long AFTER walking off a ledge
    [Range(0f, 1f)]
    public float jumpReleaseMultiplier = 0.4f; // upward speed is multiplied by this when jump is let go early (short hop). 1 = always full height

    [Header("Dash")]
    public float dashSpeed = 10f;            // speed during the dash (m/s)
    public float dashSeconds = 0.15f;        // how long one dash lasts
    public float dashCooldownSeconds = 0.35f; // wait time after a dash before the next one
    public float dashBufferSeconds = 0.1f;   // a dash pressed this long too early (during cooldown) still happens
    public int maxAirDashes = 1;             // how many dashes allowed before touching the ground again
    [Range(0f, 1f)]
    public float dashExitSpeedFraction = 0.5f; // how much of the dash speed is kept when the dash ends

    [Header("Gravity")]
    public float gravity = 20f;              // how strongly we are pulled down (m/s per second). JumpPad reads this too, for its preview arc
    public float fallGravityMultiplier = 1.5f; // falling is this many times heavier than rising: snappier, less floaty
    public float maxFallSpeed = 10f;         // we never fall faster than this (m/s)
    public float groundStickSpeed = 1f;      // a small downward push while standing, so we stay glued to slopes

    [Header("Ground Check")]
    public LayerMask groundMask = ~0;        // which layers count as ground (~0 = all). Do NOT include the Player layer
    public float groundCheckDistance = 0.03f; // how far below the feet we still count as "on the ground" (meters)
    public float edgeSlideSpeed = 2f;        // when hanging on the very edge of something (touching it, but too far out to stand on it), push off it at this speed (m/s)

    [Header("Animation")]
    public float crossFadeSeconds = 0.1f;    // how long the blend between two animations takes
    public float idleSpeedThreshold = 0.1f;  // below this speed (m/s) on the ground the Idle animation plays
    public float hitAnimationSeconds = 0.4f; // how long the Hit animation plays after taking damage

    // =====================================================================================
    // STATE OTHER SCRIPTS CAN READ
    // [System.NonSerialized] = not shown in the Inspector and not saved: these are live values, rebuilt while playing.
    // Only THIS script should change them; other scripts just look at them.
    // =====================================================================================

    [System.NonSerialized] public bool IsGrounded;            // true while standing on the ground (set in UpdateGrounded). Read by PlayerSwordAttack
    [System.NonSerialized] public bool IsDashing;             // true during a dash (set in StartDash / EndDash)
    [System.NonSerialized] public bool IsSprinting;           // true while sprinting (set in UpdateSprint)
    [System.NonSerialized] public bool IsDead;                // true after Die() was called; stops all control
    [System.NonSerialized] public bool IsClimbing;            // true while holding on to a ladder (set in StartClimbing / StopClimbing)
    [System.NonSerialized] public bool JumpHeld;              // true while the jump button is held down (set in Update). Read by JumpPad
    [System.NonSerialized] public float VerticalVelocity;     // up/down speed in m/s (+ = up). Read by PlayerStomp and JumpPad
    [System.NonSerialized] public Vector3 HorizontalVelocity; // sideways speed (no up/down part). Read by JumpPad

    public event System.Action Landed; // a "shout" sent the frame we touch the ground again. PlayerStomp listens to it to end the stomp combo

    // =====================================================================================
    // PRIVATE VARIABLES (only this script uses them)
    // =====================================================================================

    private CharacterController controller; // the Unity component that actually moves us and handles walls/floors (found in Awake)

    // Camera directions, worked out ONCE per frame in ReadMoveInput and reused by Rotate and BeginAttack
    private Vector3 cameraForward = Vector3.forward; // where the camera looks, flattened onto the ground (no up/down part)
    private Vector3 cameraRight = Vector3.right;     // the camera's right side, flattened onto the ground

    // Movement
    private Vector2 moveInput;                             // the raw keys / stick this frame (x = A/D, y = W/S), NOT turned by the camera. Used by Climb (set in ReadMoveInput)
    private Vector3 moveDirection;                         // where the player wants to walk, in world space, length 0..1 (set in ReadMoveInput)
    private bool hasMoveInput;                             // true if a move key / the stick is pressed this frame (set in ReadMoveInput)
    private float lastGroundedTime = float.NegativeInfinity; // the last time (Time.time) we were on the ground, for coyote time and the fall animation

    // Edges. Filled in by OnControllerColliderHit while controller.Move runs, then used by UpdateGrounded.
    private float normalStepOffset;       // the CharacterController's Step Offset from the Inspector (saved in Awake). It is switched OFF in the air so we don't snag on ledges
    private bool touchedBelow;            // did the last Move touch something under us? (set in MoveCharacter)
    private Vector3 floorNormal;          // the most upward-facing surface direction we touched below during the last Move (zero = none)
    private bool floorIsGround;           // is that surface on a groundMask layer?
    private bool hitCeiling;              // did the last Move hit something flat ABOVE us (a real ceiling, not just a corner)?
    private bool isOnEdge;                // true while hanging on an edge (touching something below but not standing on it)
    private Vector3 edgePushDirection;    // which way to slide off that edge (flat, length 1)

    // Jump
    private bool groundJumpAvailable;  // true after touching the ground; used up by the first jump
    private int airJumpsUsed;          // how many double jumps were used since the last landing
    private bool jumpCutAvailable;     // true while a jump can still be made shorter by letting go of the button
    private bool lastJumpWasAirJump;   // true if the last jump was a double jump, so the DoubleJump animation plays

    // Dash
    private Vector3 dashDirection;     // the direction of the current dash (flat)
    private float dashTimeLeft;        // seconds left in the current dash
    private float dashCooldownLeft;    // seconds left before another dash is allowed
    private int airDashesUsed;         // dashes used since the last landing

    // Input buffer: WHEN each button was last pressed. NegativeInfinity = "no press waiting".
    // Storing the time (not just true/false) lets a press made slightly too early still count a moment later.
    private float jumpPressedTime = float.NegativeInfinity;   // set in OnJumpPressed, used up in Jump / TryConsumeJumpPress
    private float dashPressedTime = float.NegativeInfinity;   // set in OnDashPressed, used up in StartDash
    private float attackPressedTime = float.NegativeInfinity; // set in OnAttackPressed, used up by PlayerSwordAttack through TryConsumeAttackPress

    // Things other scripts switched on
    private bool isAttacking;                 // true between BeginAttack and EndAttack (called by PlayerSwordAttack)
    private float attackSpeedMultiplier = 1f; // walking speed is multiplied by this while attacking
    private bool rotationLocked;              // true = don't turn (during a sword swing)
    private float hitTimeLeft;                // seconds left of the Hit animation (set in PlayHit)
    private Ladder currentLadder;             // the ladder we are holding on to (null = none). Set by StartClimbing, cleared by StopClimbing

    // Animation. Unity finds Animator states fastest by a number ("hash") instead of by name,
    // so every name is turned into a number ONCE in Start and the numbers are reused every frame.
    private int idleHash;
    private int walkHash;
    private int runHash;
    private int jumpHash;
    private int doubleJumpHash;
    private int fallHash;
    private int dashHash;
    private int attackHash;
    private int hitHash;
    private int deathHash;
    private int climbHash;
    private int currentAnimation;     // the hash of the animation playing now, so we only tell the Animator when it CHANGES
    private bool restartAnimation;    // true = play the animation from the start even if it is the same one (second swing, second jump)

    // =====================================================================================
    // UNITY FUNCTIONS (Unity calls these by itself)
    // =====================================================================================

    // Awake runs once, when the object is created, before any Update.
    private void Awake()
    {
        controller = GetComponent<CharacterController>(); // find our CharacterController once and keep it (searching every frame would be slow)

        if (!controller.enabled) // a switched-off CharacterController can't move at all
        {
            Debug.LogWarning(name + ": the CharacterController was switched off, so the player could not move. Switching it on.", this);
            controller.enabled = true; // switch it back on
        }

        normalStepOffset = controller.stepOffset; // remember the step height set in the Inspector; UpdateGrounded switches it off in the air and back on when landing

        if (cameraTransform == null && Camera.main != null) // no camera dragged in, but the scene has a Main Camera
        {
            cameraTransform = Camera.main.transform; // use it (Camera.main is a search, so we do it only once, here)
        }

        if (GetComponent<PlayerStomp>() == null) gameObject.AddComponent<PlayerStomp>(); // stomping lives in its own script (PlayerStomp); add it if the player doesn't have it yet

        SetUpAnimator();      // find the Animator and prepare it (see the Helpers section)
        AddDefaultBindings(); // give every button default keys if none were set in the Inspector

        // Tell the Input System: "when this button is pressed, call this function". The functions only remember the time.
        jumpAction.performed += OnJumpPressed;
        dashAction.performed += OnDashPressed;
        attackAction.performed += OnAttackPressed;
    }

    // Start runs once, after every Awake, right before the first Update.
    private void Start()
    {
        // Turn every animation name into its number, and warn if the Animator Controller is missing that state.
        idleHash = GetStateHash("Idle");
        walkHash = GetStateHash("Walk");
        runHash = GetStateHash("Run");
        jumpHash = GetStateHash("Jump");
        doubleJumpHash = GetStateHash("DoubleJump");
        fallHash = GetStateHash("Fall");
        dashHash = GetStateHash("Dash");
        attackHash = GetStateHash("Attack");
        hitHash = GetStateHash("Hit");
        deathHash = GetStateHash("Death");
        climbHash = GetStateHash("Climb");
    }

    // OnEnable runs when the script is switched on. Input actions only listen to keys while enabled.
    private void OnEnable()
    {
        moveAction.Enable();
        jumpAction.Enable();
        sprintAction.Enable();
        dashAction.Enable();
        attackAction.Enable();
    }

    // OnDisable runs when the script is switched off (or the object is destroyed): stop listening to keys.
    private void OnDisable()
    {
        moveAction.Disable();
        jumpAction.Disable();
        sprintAction.Disable();
        dashAction.Disable();
        attackAction.Disable();
    }

    // Update runs once every frame. The ORDER matters: each step uses what the step before it decided.
    private void Update()
    {
        float deltaTime = Time.deltaTime; // seconds since the last frame; multiplying by it makes speeds the same at any frame rate

        JumpHeld = jumpAction.IsPressed();       // remember if jump is held, for the short-hop check and for JumpPad
        ReadMoveInput();                         // 1. which way do we want to walk? (also saves the camera directions)
        if (hitTimeLeft > 0f) hitTimeLeft -= deltaTime; // count down the Hit animation timer

        if (IsClimbing) // on a ladder: climbing replaces steps 2-9 below
        {
            Climb(deltaTime);  // move up/down the ladder
            UpdateAnimation(); // still pick the animation (Climb)
            return;            // skip normal walking, jumping and gravity this frame
        }

        if (!IsDead) // a dead player can't start dashes or jumps
        {
            UpdateDash(deltaTime); // 2. start / continue / end a dash
            UpdateJump();          // 3. start a jump, or cut it short
        }

        UpdateSprint();                // 4. are we sprinting? (needs IsGrounded from last frame and isAttacking)
        ApplyHorizontal(deltaTime);    // 5. change the sideways speed toward the wanted speed
        ApplyVertical(deltaTime);      // 6. apply gravity to the up/down speed
        MoveCharacter(deltaTime);      // 7. actually move, using the speeds from steps 5 and 6
        UpdateGrounded();              // 8. after moving: are we on the ground now?
        Rotate(deltaTime);             // 9. turn the player
        UpdateAnimation();             // 10. pick the animation from everything above
    }

    // =====================================================================================
    // INPUT CALLBACKS (the Input System calls these the moment a button is pressed; hooked up in Awake)
    // =====================================================================================

    private void OnJumpPressed(InputAction.CallbackContext context)
    {
        jumpPressedTime = Time.time; // remember WHEN; UpdateJump checks if this press is still recent enough
    }

    private void OnDashPressed(InputAction.CallbackContext context)
    {
        dashPressedTime = Time.time; // remember WHEN; UpdateDash checks if this press is still recent enough
    }

    private void OnAttackPressed(InputAction.CallbackContext context)
    {
        attackPressedTime = Time.time; // remember WHEN; PlayerSwordAttack asks for it through TryConsumeAttackPress
    }

    // =====================================================================================
    // MOVEMENT
    // =====================================================================================

    // Turns the keys / stick into a direction in the world, relative to the camera.
    private void ReadMoveInput()
    {
        Vector2 input = Vector2.zero;                   // (x = left/right, y = forward/back), zero if dead
        if (!IsDead) input = moveAction.ReadValue<Vector2>(); // read the keys / stick
        moveInput = input;                              // keep the raw keys too, for climbing (W/S = up/down, A/D = sideways)

        if (cameraTransform != null) // with a camera: "forward" means "where the camera looks"
        {
            cameraForward = Flatten(cameraTransform.forward); // flattened so looking down doesn't make us walk into the floor
            cameraRight = Flatten(cameraTransform.right);
        }

        moveDirection = cameraForward * input.y + cameraRight * input.x; // mix forward and right by how much each key is pressed
        moveDirection = Vector3.ClampMagnitude(moveDirection, 1f);      // diagonals would be longer than 1 (faster): cut them back to 1
        hasMoveInput = moveDirection.sqrMagnitude > 0.0001f;            // sqrMagnitude = length squared; skips a square root, which is cheaper
    }

    // Decides if we sprint. Only decided on the ground, so a sprinting jump keeps its sprint speed in the air.
    private void UpdateSprint()
    {
        if (!IsGrounded) return; // in the air: keep whatever we had when we left the ground

        IsSprinting = sprintAction.IsPressed() && hasMoveInput && !isAttacking && !IsDashing && !IsDead; // sprint only if ALL of these are true
    }

    // Moves the sideways speed a little toward the speed we want. This gives smooth speeding up and slowing down.
    private void ApplyHorizontal(float deltaTime)
    {
        if (IsDashing) // during a dash the speed is fixed
        {
            HorizontalVelocity = dashDirection * dashSpeed; // full dash speed in the dash direction
            return;                                         // skip the normal walking code below
        }

        float topSpeed = walkSpeed;                   // normally walk speed...
        if (IsSprinting) topSpeed = sprintSpeed;      // ...or sprint speed
        if (isAttacking) topSpeed *= attackSpeedMultiplier; // slower while swinging the sword (set by BeginAttack)

        Vector3 targetVelocity = moveDirection * topSpeed; // the speed we WANT (zero when no key is held)

        float rate;                                   // how fast we may change speed this frame
        if (IsGrounded)
        {
            if (hasMoveInput) rate = groundAcceleration; // on ground, key held: speed up
            else rate = groundDeceleration;              // on ground, no key: brake
        }
        else
        {
            if (hasMoveInput) rate = airAcceleration;    // in air, key held: steer a bit
            else rate = airDeceleration;                 // in air, no key: drift (keep most momentum)
        }

        HorizontalVelocity = Vector3.MoveTowards(HorizontalVelocity, targetVelocity, rate * deltaTime); // step toward the wanted speed, never overshooting
    }

    // Applies gravity to the up/down speed.
    private void ApplyVertical(float deltaTime)
    {
        if (IsDashing) // dashes fly in a straight line
        {
            VerticalVelocity = 0f; // no falling during a dash
            return;
        }

        if (IsGrounded && VerticalVelocity <= 0f) // standing on the ground and not jumping
        {
            VerticalVelocity = -groundStickSpeed; // a small push down keeps us glued to slopes and steps
            return;
        }

        float appliedGravity = gravity;                                  // normal gravity while rising...
        if (VerticalVelocity < 0f) appliedGravity *= fallGravityMultiplier; // ...heavier while falling

        VerticalVelocity -= appliedGravity * deltaTime;                  // gravity pulls the speed down a bit every frame
        if (VerticalVelocity < -maxFallSpeed) VerticalVelocity = -maxFallSpeed; // but never faster than the max fall speed
    }

    // Moves the CharacterController. It slides along walls and stops at floors by itself.
    private void MoveCharacter(float deltaTime)
    {
        Vector3 velocity = HorizontalVelocity;   // start with the sideways speed (from ApplyHorizontal)
        velocity.y = VerticalVelocity;           // add the up/down speed (from ApplyVertical)

        if (isOnEdge) velocity += edgePushDirection * edgeSlideSpeed; // hanging on an edge (found by UpdateGrounded last frame): slide off it

        floorNormal = Vector3.zero; // forget last frame's contacts; OnControllerColliderHit fills these in again during Move
        floorIsGround = false;
        hitCeiling = false;

        CollisionFlags flags = controller.Move(velocity * deltaTime); // speed x time = distance to move this frame. Returns what we bumped into
        // NOTE: while Move runs, Unity calls OnControllerColliderHit (below, and in PlayerStomp) for everything we touch.

        touchedBelow = (flags & CollisionFlags.Below) != 0; // something is under us (real ground OR just an edge); UpdateGrounded decides which

        if (hitCeiling && VerticalVelocity > 0f) VerticalVelocity = 0f; // bonked our head on a real ceiling: stop going up so we fall right away
        // (Only a FLAT ceiling counts. Brushing a corner with our head used to kill the whole jump; now we just slide past it.)
    }

    // Unity calls this for EVERY surface the CharacterController touches during controller.Move (in MoveCharacter).
    // hit.normal = the direction pointing out of the surface toward us. Up = floor, down = ceiling, sideways = wall,
    // tilted = a slope or the EDGE of a box (edges give a tilted direction, pointing away from the edge).
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.normal.y < -0.7f) hitCeiling = true; // pointing mostly down = a flat ceiling above us (MoveCharacter uses this)

        if (hit.normal.y > floorNormal.y) // pointing more upward than anything else we touched this Move = our best "floor"
        {
            floorNormal = hit.normal;                                              // remember its direction (UpdateGrounded checks how steep it is)
            floorIsGround = (groundMask.value & (1 << hit.gameObject.layer)) != 0; // is its layer ticked in groundMask? (1 << layer = that layer's bit)
        }
    }

    // Checks if we are standing on the ground, and resets jumps / dashes when we are.
    private void UpdateGrounded()
    {
        bool wasGrounded = IsGrounded; // remember last frame's answer, to find the exact frame we land

        // Standing on what we touched: something under us, on a ground layer, and flat enough (not steeper than the slope limit).
        bool standingOnContact = touchedBelow && floorIsGround && Vector3.Angle(floorNormal, Vector3.up) <= controller.slopeLimit;

        // While moving upward we are never grounded (otherwise we could jump again right after jumping).
        // || stops early: if the contact already says "ground", the extra ground probe (CastGround) is skipped. && does the same for the first part.
        IsGrounded = VerticalVelocity <= 0.01f && (standingOnContact || CastGround(0f));

        // EDGE CHECK: we touch something below, but it doesn't count as ground -> we're hanging on a corner.
        // The CharacterController won't slide off by itself, so without this the player would get stuck there.
        isOnEdge = false;                                // assume not, until proven below
        if (touchedBelow && !IsGrounded && VerticalVelocity <= 0f)
        {
            if (VerticalVelocity < -groundStickSpeed) VerticalVelocity = -groundStickSpeed; // something holds us up, so don't build up fall speed while hanging

            edgePushDirection = floorNormal; // the edge's direction points away from the edge and up...
            edgePushDirection.y = 0f;        // ...keep only the sideways part = "away from the edge"
            if (edgePushDirection.sqrMagnitude > 0.0001f) // a perfectly flat surface has no sideways part: nothing to slide off
            {
                edgePushDirection.Normalize(); // length 1, so edgeSlideSpeed is the real speed
                isOnEdge = true;               // MoveCharacter adds the push next frame
            }
        }

        // Step Offset lets the CharacterController walk up small steps. In the air it makes us catch on ledges we
        // jump past, so it is only on while grounded. (Only changed when needed.)
        float wantedStepOffset = 0f;                          // in the air: off
        if (IsGrounded) wantedStepOffset = normalStepOffset;  // on the ground: the Inspector value
        if (controller.stepOffset != wantedStepOffset) controller.stepOffset = wantedStepOffset;

        if (IsGrounded) // on the ground: refill everything
        {
            lastGroundedTime = Time.time;       // for coyote time and the fall animation delay
            groundJumpAvailable = true;         // the normal jump is available again
            airJumpsUsed = 0;                   // double jumps are refilled
            if (!IsDashing) airDashesUsed = 0;  // air dashes are refilled (not during a dash along the ground)
        }

        if (IsGrounded && !wasGrounded) // this is the frame we touched down
        {
            if (Landed != null) Landed(); // tell everyone listening (PlayerStomp ends its combo)
        }
    }

    // Turns the player smoothly toward where it should face.
    private void Rotate(float deltaTime)
    {
        if (rotationLocked || IsDead) return; // no turning during a locked sword swing or when dead

        Vector3 lookDirection;                                               // where we want to face
        if (IsDashing) lookDirection = dashDirection;                        // face the dash
        else if (faceCameraDirection && cameraTransform != null) lookDirection = cameraForward; // face where the camera looks (worked out in ReadMoveInput)
        else lookDirection = moveDirection;                                  // face where we walk

        if (lookDirection.sqrMagnitude < 0.0001f) return; // no direction (standing still): keep the current rotation

        Quaternion target = Quaternion.LookRotation(lookDirection.normalized, Vector3.up); // the rotation that faces lookDirection
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * deltaTime); // turn toward it, at most turnSpeed degrees per second
    }

    // =====================================================================================
    // JUMP
    // =====================================================================================

    private void UpdateJump()
    {
        if (IsPressRecent(jumpPressedTime, jumpBufferSeconds)) // jump was pressed recently and not used yet
        {
            bool withinCoyoteTime = Time.time - lastGroundedTime <= coyoteSeconds; // left the ground only a moment ago?

            if (groundJumpAvailable && (IsGrounded || withinCoyoteTime)) // normal jump: on the ground, or just walked off a ledge
            {
                groundJumpAvailable = false;     // used up until we land again (UpdateGrounded refills it)
                Jump(jumpHeight, false);         // false = not a double jump
            }
            else if (airJumpsUsed < maxAirJumps && !WillLandBeforeBufferEnds()) // double jump, unless we're about to land anyway
            {
                airJumpsUsed++;                  // count it (UpdateGrounded resets it)
                Jump(airJumpHeight, true);       // true = double jump (plays the DoubleJump animation)
            }
        }

        // Variable jump height: letting go of jump while still going up makes the jump shorter.
        if (!jumpCutAvailable) return;  // no jump to cut (or already cut)

        if (VerticalVelocity <= 0f)     // already at the top / falling
        {
            jumpCutAvailable = false;   // too late to cut now
        }
        else if (!JumpHeld)             // still going up but the button was let go
        {
            VerticalVelocity *= jumpReleaseMultiplier; // cut the upward speed -> short hop
            jumpCutAvailable = false;   // only cut once per jump
        }
    }

    // Does the jump itself. Called by UpdateJump.
    private void Jump(float height, bool airJump)
    {
        if (IsDashing) EndDash(); // jumping cancels a dash

        VerticalVelocity = JumpSpeed(height);   // the upward speed needed to reach that height
        jumpPressedTime = float.NegativeInfinity; // the press is used up, so it can't jump twice
        jumpCutAvailable = true;                // letting go of the button may now shorten this jump
        lastJumpWasAirJump = airJump;           // so ChooseAnimation picks Jump or DoubleJump
        restartAnimation = true;                // play the jump animation from the start, even if it was already playing
    }

    // If we will touch the ground before the jump press expires, keep the press for a real ground jump
    // instead of wasting the double jump a few centimeters above the floor.
    private bool WillLandBeforeBufferEnds()
    {
        if (VerticalVelocity >= 0f) return false; // going up: we won't land soon

        float bufferTimeLeft = jumpBufferSeconds - (Time.time - jumpPressedTime); // how long the press stays valid
        float fallDistance = -VerticalVelocity * bufferTimeLeft;                    // roughly how far we fall in that time
        return CastGround(fallDistance);                                            // is there ground within that distance?
    }

    // =====================================================================================
    // LADDER
    // =====================================================================================

    // Moves us up and down the ladder. Called by Update instead of the normal movement while IsClimbing.
    private void Climb(float deltaTime)
    {
        HorizontalVelocity = cameraRight * moveInput.x * currentLadder.climbSpeed; // A = left, D = right (as the camera sees it). Leaving the ladder's box sideways lets go (Ladder.OnTriggerExit), so we fall
        VerticalVelocity = moveInput.y * currentLadder.climbSpeed; // W = up (+), S = down (-), nothing = hang still (0)
        Vector3 velocity = HorizontalVelocity + Vector3.up * VerticalVelocity; // sideways and up/down together
        CollisionFlags flags = controller.Move(velocity * deltaTime); // speed x time = distance; returns what we bumped into

        bool reachedFloor = (flags & CollisionFlags.Below) != 0;      // our feet touched the floor while climbing down
        if (reachedFloor && VerticalVelocity < 0f) StopClimbing(currentLadder); // at the bottom: let go, so we can walk away
    }

    // =====================================================================================
    // DASH
    // =====================================================================================

    private void UpdateDash(float deltaTime)
    {
        if (IsDashing) // a dash is running
        {
            dashTimeLeft -= deltaTime;           // count it down
            if (dashTimeLeft <= 0f) EndDash();   // time is up
            return;                              // can't start a new dash during one
        }

        if (dashCooldownLeft > 0f) dashCooldownLeft -= deltaTime; // count down the cooldown

        if (!IsPressRecent(dashPressedTime, dashBufferSeconds)) return; // dash wasn't pressed recently
        if (dashCooldownLeft > 0f) return;                              // still cooling down (the press stays buffered for a moment)
        if (!IsGrounded && airDashesUsed >= maxAirDashes) return;       // no air dashes left

        StartDash();
    }

    private void StartDash()
    {
        if (hasMoveInput) dashDirection = moveDirection.normalized; // dash where the stick points...
        else dashDirection = transform.forward;                     // ...or forward if nothing is pressed
        dashDirection.y = 0f;                                       // keep it flat

        if (!IsGrounded) airDashesUsed++; // count air dashes (UpdateGrounded resets the count)

        IsDashing = true;                          // ApplyHorizontal / ApplyVertical / Rotate / ChooseAnimation all check this
        dashTimeLeft = dashSeconds;                // how long it lasts (counted down in UpdateDash)
        dashPressedTime = float.NegativeInfinity;  // the press is used up
        VerticalVelocity = 0f;                     // stop falling / rising
        jumpCutAvailable = false;                  // the dash replaced the jump, nothing to cut
        restartAnimation = true;                   // play the dash animation from the start
    }

    private void EndDash()
    {
        IsDashing = false;                       // back to normal movement
        dashCooldownLeft = dashCooldownSeconds;  // start the cooldown
        HorizontalVelocity = Vector3.ClampMagnitude(HorizontalVelocity, dashSpeed * dashExitSpeedFraction); // keep only part of the dash speed
    }

    // =====================================================================================
    // ANIMATION (the ONE place that decides what the model plays)
    // =====================================================================================

    private void UpdateAnimation()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return; // nothing to animate

        int next = ChooseAnimation(); // which animation should play now?

        if (next == currentAnimation && !restartAnimation) return; // same as before: do NOTHING (this is what happens on most frames, so it's cheap)
        if (!animator.HasState(0, next)) return; // the Animator Controller doesn't have this state: skip it instead of filling the Console with errors

        if (next == currentAnimation) animator.Play(next, 0, 0f);          // same move again (second swing, stomp bounce): restart it from 0
        else animator.CrossFadeInFixedTime(next, crossFadeSeconds, 0);     // a different move: blend into it smoothly

        currentAnimation = next;   // remember what plays now
        restartAnimation = false;  // the restart request is handled
    }

    // Returns the animation to play. Checked from most important to least important: the first match wins.
    private int ChooseAnimation()
    {
        if (IsDead) return deathHash;          // dead beats everything
        if (hitTimeLeft > 0f) return hitHash;  // just got hit (PlayHit)
        if (IsClimbing) return climbHash;      // holding on to a ladder
        if (isAttacking) return attackHash;    // swinging the sword (BeginAttack)
        if (IsDashing) return dashHash;        // dashing

        if (!IsGrounded) // in the air
        {
            if (VerticalVelocity > 0f) // going up
            {
                if (lastJumpWasAirJump) return doubleJumpHash;
                return jumpHash;
            }

            bool justLeftGround = Time.time - lastGroundedTime < 0.1f; // small drops (steps, slopes) last less than 0.1 s
            if (!justLeftGround) return fallHash; // really falling: play Fall. (Tiny drops fall through to Idle/Walk below so Fall doesn't flash)
        }

        float minSpeedSqr = idleSpeedThreshold * idleSpeedThreshold; // compare squared lengths: no square root needed
        if (HorizontalVelocity.sqrMagnitude < minSpeedSqr) return idleHash; // almost not moving
        if (IsSprinting) return runHash;
        return walkHash;
    }

    // =====================================================================================
    // PUBLIC FUNCTIONS (the ONLY way other scripts change the player)
    // =====================================================================================

    // Uses up a recent jump press. JumpPad calls this so the press bounces off the pad instead of doing a normal jump.
    public bool TryConsumeJumpPress(float bufferSeconds)
    {
        if (!IsPressRecent(jumpPressedTime, bufferSeconds)) return false; // no recent press
        jumpPressedTime = float.NegativeInfinity;                         // used up, so UpdateJump won't also use it
        return true;
    }

    // Uses up a recent attack press. PlayerSwordAttack calls this every frame to know when to swing.
    public bool TryConsumeAttackPress(float bufferSeconds)
    {
        if (!IsPressRecent(attackPressedTime, bufferSeconds)) return false; // no recent press
        attackPressedTime = float.NegativeInfinity;                         // used up
        return true;
    }

    // Forgets any attack press made so far. PlayerSwordAttack calls this so clicks during a swing are ignored.
    public void ClearAttackPress()
    {
        attackPressedTime = float.NegativeInfinity;
    }

    // Throws the player up to the given height and keeps the sideways speed. Used by PlayerStomp (bounce) and JumpPad.
    public void Launch(float heightMeters)
    {
        DoLaunch(heightMeters, false, Vector3.zero); // false = don't change the sideways speed
    }

    // Throws the player up AND sets a new sideways speed. Used by JumpPad (a pad that pushes you forward).
    public void Launch(float heightMeters, Vector3 newHorizontalVelocity)
    {
        DoLaunch(heightMeters, true, newHorizontalVelocity); // true = replace the sideways speed
    }

    // Grabs on to a ladder. Called by Ladder when we walk into it.
    public void StartClimbing(Ladder ladder)
    {
        if (IsDead) return; // the dead don't climb

        if (IsDashing) EndDash();      // grabbing a ladder stops a dash
        currentLadder = ladder;        // remember which ladder (Climb reads its speed)
        IsClimbing = true;             // Update now calls Climb instead of the normal movement
        VerticalVelocity = 0f;         // stop falling / rising the moment we grab it
        HorizontalVelocity = Vector3.zero; // and stop sliding sideways
        jumpCutAvailable = false;      // a jump that was going on is over
    }

    // Lets go of a ladder. Called by Ladder when we leave it, by Climb at the bottom, and by Die.
    public void StopClimbing(Ladder ladder)
    {
        if (ladder != currentLadder) return; // that's not the ladder we're on (or we're on none): ignore

        currentLadder = null;                    // not holding any ladder now
        IsClimbing = false;                      // Update goes back to normal movement and gravity
    }

    // Starts a sword swing: aims, slows the player and plays the attack animation. Called by PlayerSwordAttack.
    // Returns false if the swing isn't allowed (dead).
    public bool BeginAttack(float moveSpeedMultiplier, bool lockRotation)
    {
        if (IsDead) return false;

        Vector3 aim = moveDirection;                                          // aim where the stick points...
        if (faceCameraDirection && cameraTransform != null) aim = cameraForward; // ...or where the camera looks in camera-facing mode
        if (aim.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(aim.normalized, Vector3.up); // snap to face it instantly

        isAttacking = true;                                       // ApplyHorizontal, UpdateSprint and ChooseAnimation check this
        attackSpeedMultiplier = Mathf.Clamp01(moveSpeedMultiplier); // 0..1, used in ApplyHorizontal
        rotationLocked = lockRotation;                            // Rotate checks this
        restartAnimation = true;                                  // restart the attack animation (for a second swing in a row)
        return true;
    }

    // Ends the sword swing. Called by PlayerSwordAttack, and by Die.
    public void EndAttack()
    {
        isAttacking = false;
        attackSpeedMultiplier = 1f; // full speed again
        rotationLocked = false;     // can turn again
    }

    // Plays the Hit animation. Hook it to Damageable -> On Damaged in the Inspector.
    public void PlayHit()
    {
        if (IsDead) return;
        hitTimeLeft = hitAnimationSeconds; // ChooseAnimation plays Hit while this is above 0 (counted down in Update)
        restartAnimation = true;           // restart it if we get hit twice quickly
    }

    // Stops all control and plays the Death animation. Hook it to Damageable -> On Died in the Inspector.
    public void Die()
    {
        if (IsDead) return; // already dead

        if (IsDashing) EndDash();
        if (IsClimbing) StopClimbing(currentLadder); // let go of the ladder, so the Death animation plays and we fall
        EndAttack();
        IsSprinting = false;
        IsDead = true; // ReadMoveInput, Update, Rotate, ChooseAnimation and the public functions all check this
    }

    // Instantly moves the player and clears its speed. Called by BossController (arena / respawn).
    public void Teleport(Vector3 position, Quaternion rotation)
    {
        // A CharacterController puts the player back where IT thinks it is, so it has to be off while we move.
        controller.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        controller.enabled = true;

        if (IsDashing) EndDash();
        HorizontalVelocity = Vector3.zero; // no leftover speed after the teleport
        VerticalVelocity = 0f;
        isOnEdge = false;                  // don't carry an edge push over to the new place
    }

    // =====================================================================================
    // HELPERS (small tools used by the code above)
    // =====================================================================================

    // True if a button was pressed less than windowSeconds ago. Used by UpdateJump, UpdateDash and the TryConsume functions.
    private bool IsPressRecent(float pressedTime, float windowSeconds)
    {
        return Time.time - pressedTime <= windowSeconds; // NegativeInfinity (no press) gives a huge number, so it's always false
    }

    // The upward speed needed to reach a height. From physics: v = square root of (2 x gravity x height).
    // Used by Jump and DoLaunch.
    private float JumpSpeed(float heightMeters)
    {
        return Mathf.Sqrt(2f * gravity * heightMeters);
    }

    // Removes the up/down part of a direction and makes it length 1 (or zero if nothing is left).
    // Used by ReadMoveInput for the camera directions.
    private Vector3 Flatten(Vector3 direction)
    {
        direction.y = 0f;                                    // remove up/down
        if (direction.sqrMagnitude < 0.0001f) return Vector3.zero; // straight up/down: no flat direction
        return direction.normalized;                         // length 1
    }

    // Shared code of both Launch functions.
    private void DoLaunch(float heightMeters, bool setHorizontal, Vector3 newHorizontalVelocity)
    {
        if (IsDead) return;

        if (IsDashing) EndDash();                 // a launch cancels a dash
        VerticalVelocity = JumpSpeed(heightMeters); // throw upward

        if (setHorizontal) // the two-value Launch also replaces the sideways speed
        {
            newHorizontalVelocity.y = 0f;               // sideways only
            HorizontalVelocity = newHorizontalVelocity;
        }

        jumpCutAvailable = false;   // letting go of jump must NOT shorten a launch: it always reaches full height
        lastJumpWasAirJump = false; // play the normal Jump animation
        restartAnimation = true;    // restart it (each stomp bounce plays it again)
    }

    // Throws an invisible ball down from our feet and returns true if it hits flat-enough ground.
    // extraDistance = how much further than normal to look (WillLandBeforeBufferEnds looks further ahead).
    // This is the only physics query this script does each frame.
    private bool CastGround(float extraDistance)
    {
        // The CharacterController's size grows with the object's scale, so the ball must grow too.
        float scale = transform.lossyScale.y;
        float radius = controller.radius * scale;   // the capsule's real radius
        float castRadius = radius * 0.9f;           // the ball is slightly thinner than the capsule, so walls beside us don't count as ground

        Vector3 capsuleCenter = transform.TransformPoint(controller.center);              // the middle of the capsule, in the world
        float centerToBottomSphere = controller.height * 0.5f * scale - radius;          // from the middle down to the center of the round bottom
        Vector3 start = capsuleCenter + Vector3.down * centerToBottomSphere;             // the ball starts at the capsule's round bottom

        float distance = (radius - castRadius) + controller.skinWidth * scale + groundCheckDistance + extraDistance; // how far down to look

        RaycastHit hit; // filled with what the ball hit
        bool hitSomething = Physics.SphereCast(start, castRadius, Vector3.down, out hit, distance, groundMask, QueryTriggerInteraction.Ignore); // triggers (pickups, zones) are ignored
        if (!hitSomething) return false; // nothing below us

        return Vector3.Angle(hit.normal, Vector3.up) <= controller.slopeLimit; // only ground flatter than the CharacterController's slope limit counts
    }

    // Turns an animation name into its number, and warns once if the Animator Controller doesn't have it. Called in Start.
    private int GetStateHash(string stateName)
    {
        int hash = Animator.StringToHash(stateName); // name -> number (done once, so Update never works with strings)

        if (animator != null && animator.runtimeAnimatorController != null && !animator.HasState(0, hash))
        {
            Debug.LogWarning(name + ": the Animator Controller has no state called '" + stateName + "', so that animation won't play.", this);
        }

        return hash;
    }

    // Finds the model's Animator and prepares it. Called in Awake.
    private void SetUpAnimator()
    {
        if (animator == null) // not dragged in: search for it
        {
            Animator[] found = GetComponentsInChildren<Animator>(); // every Animator on us and our children (done once, here)
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i].gameObject == gameObject) continue; // prefer the model (a child) over this object
                animator = found[i];
                break;                                           // first child Animator is enough
            }
            if (animator == null) animator = GetComponent<Animator>(); // no child had one: try this object
        }

        if (animator == null) // still nothing
        {
            Debug.LogWarning(name + ": no Animator found on the model, so the player moves without animations.", this);
            return;
        }

        if (animatorController != null) animator.runtimeAnimatorController = animatorController; // put our controller on it, if one was given

        animator.applyRootMotion = false; // this script moves the player; if the animation moved it too, the two would fight
    }

    // Gives each button default keys, but only if none were set in the Inspector. Called in Awake.
    private void AddDefaultBindings()
    {
        if (moveAction.bindings.Count == 0) // no move keys set
        {
            moveAction.AddCompositeBinding("2DVector") // 4 keys combined into one (x, y) value
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            moveAction.AddCompositeBinding("2DVector") // the arrow keys too
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            moveAction.AddBinding("<Gamepad>/leftStick"); // and the gamepad stick
        }

        if (jumpAction.bindings.Count == 0)
        {
            jumpAction.AddBinding("<Keyboard>/space");
            jumpAction.AddBinding("<Gamepad>/buttonSouth");
        }

        if (sprintAction.bindings.Count == 0)
        {
            sprintAction.AddBinding("<Keyboard>/leftShift");
            sprintAction.AddBinding("<Gamepad>/leftStickPress");
        }

        if (dashAction.bindings.Count == 0)
        {
            dashAction.AddBinding("<Keyboard>/q");
            dashAction.AddBinding("<Gamepad>/buttonEast");
        }

        if (attackAction.bindings.Count == 0)
        {
            attackAction.AddBinding("<Mouse>/leftButton");
            attackAction.AddBinding("<Keyboard>/j");
            attackAction.AddBinding("<Gamepad>/buttonWest");
        }
    }
}
