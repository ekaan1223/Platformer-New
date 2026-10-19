using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The names of the Animator's parameters. The Animator Controller and this script must agree on them,
/// so they live in ONE place. (The "Build Player Animator Controller" wizard uses these too.)
/// </summary>
public static class PlayerAnimParams
{
    public const string Speed = "Speed";                 // float: how fast the player moves sideways, meters/second
    public const string MoveX = "MoveX";                 // float: speed to the player's right (negative = left)
    public const string MoveZ = "MoveZ";                 // float: speed forward (negative = backwards)
    public const string Grounded = "Grounded";           // bool
    public const string VerticalSpeed = "VerticalSpeed"; // float: up (+) / down (-)
    public const string Sprinting = "Sprinting";         // bool
    public const string Dashing = "Dashing";             // bool
    public const string Jump = "Jump";                   // trigger: any upward launch (jump, double jump, jump pad, stomp bounce)
    public const string Attack = "Attack";               // trigger
    public const string Hit = "Hit";                     // trigger
    public const string Die = "Die";                     // trigger
}

/// <summary>
/// Tells the character's Animator what the player is doing, so the right animation plays.
/// It only READS the player (motor, dash, sword) and never changes how they move.
///
/// SETUP: put this on the PLAYER ROOT (the object with PlayerMotor). The Animator lives on the MODEL, a child of the player.
/// Hook two Damageable events to it (Inspector):  On Damaged -> PlayHit(),  On Died -> PlayDeath().
/// </summary>
[DefaultExecutionOrder(100)] // after the motor, so we see this frame's final values
public class PlayerAnimator : MonoBehaviour
{
    [Header("References (empty = found automatically)")]
    [Tooltip("The Animator on the character model (a child of the player).")]
    [SerializeField] private Animator animator;
    [SerializeField] private PlayerMotor motor;
    [SerializeField] private PlayerDash dash;
    [SerializeField] private PlayerSwordAttack sword;

    [Header("Feel")]
    [Tooltip("Smooths the walk/run blend so the legs don't snap between speeds. 0 = no smoothing.")]
    [SerializeField, Min(0f)] private float speedSmoothSeconds = 0.08f;
    [Tooltip("A sudden speed-up in the upward direction bigger than this (meters/second in one frame) counts as a jump.")]
    [SerializeField, Min(0f)] private float jumpDetectThreshold = 2f;
    [Tooltip("...and it must end up going up at least this fast.")]
    [SerializeField, Min(0f)] private float jumpMinUpSpeed = 1f;

    private static readonly int SpeedId = Animator.StringToHash(PlayerAnimParams.Speed);
    private static readonly int MoveXId = Animator.StringToHash(PlayerAnimParams.MoveX);
    private static readonly int MoveZId = Animator.StringToHash(PlayerAnimParams.MoveZ);
    private static readonly int GroundedId = Animator.StringToHash(PlayerAnimParams.Grounded);
    private static readonly int VerticalSpeedId = Animator.StringToHash(PlayerAnimParams.VerticalSpeed);
    private static readonly int SprintingId = Animator.StringToHash(PlayerAnimParams.Sprinting);
    private static readonly int DashingId = Animator.StringToHash(PlayerAnimParams.Dashing);
    private static readonly int JumpId = Animator.StringToHash(PlayerAnimParams.Jump);
    private static readonly int AttackId = Animator.StringToHash(PlayerAnimParams.Attack);
    private static readonly int HitId = Animator.StringToHash(PlayerAnimParams.Hit);
    private static readonly int DieId = Animator.StringToHash(PlayerAnimParams.Die);

    // Only the parameters the controller really has are touched, so a simple controller never throws warnings.
    private readonly HashSet<int> available = new HashSet<int>();

    private float smoothedSpeed;
    private float speedVelocity;
    private float lastVerticalSpeed;
    private bool wasAttacking;
    private bool dead;

    private void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (motor == null) TryGetComponent(out motor);
        if (dash == null) TryGetComponent(out dash);
        if (sword == null) TryGetComponent(out sword);

        if (animator == null || motor == null)
        {
            Debug.LogError($"{nameof(PlayerAnimator)} needs an Animator (on the model, a child of the player) and a PlayerMotor.", this);
            enabled = false;
            return;
        }

        // The motor moves the player. If the animation also moved it (root motion) the two would fight.
        animator.applyRootMotion = false;

        if (animator.runtimeAnimatorController == null)
            Debug.LogWarning($"{name}: the Animator has no Controller assigned.", this);
        else if (!animator.isHuman)
            Debug.LogWarning($"{name}: the model's rig is not Humanoid. In the FBX's Rig tab set Animation Type = Humanoid and Apply.", this);

        foreach (AnimatorControllerParameter parameter in animator.parameters) available.Add(parameter.nameHash);
    }

    private void LateUpdate()
    {
        if (dead) return;

        Vector3 velocity = motor.HorizontalVelocity;
        smoothedSpeed = speedSmoothSeconds > 0f
            ? Mathf.SmoothDamp(smoothedSpeed, velocity.magnitude, ref speedVelocity, speedSmoothSeconds)
            : velocity.magnitude;

        // Velocity relative to where the character FACES: lets a blend tree play strafe / backpedal animations later.
        Vector3 local = transform.InverseTransformDirection(velocity);

        SetFloat(SpeedId, smoothedSpeed);
        SetFloat(MoveXId, local.x);
        SetFloat(MoveZId, local.z);
        SetBool(GroundedId, motor.IsGrounded);
        SetFloat(VerticalSpeedId, motor.VerticalVelocity);
        SetBool(SprintingId, motor.IsSprinting);

        bool dashing = dash != null && dash.IsDashing;
        SetBool(DashingId, dashing);

        // Jump: the up-speed suddenly jumps (jump, double jump, jump pad, stomp bounce). A dash zeroes it, so skip dashes.
        float vertical = motor.VerticalVelocity;
        if (!dashing && vertical > jumpMinUpSpeed && vertical - lastVerticalSpeed > jumpDetectThreshold)
            SetTrigger(JumpId);
        lastVerticalSpeed = vertical;

        bool attacking = sword != null && sword.IsAttacking;
        if (attacking && !wasAttacking) SetTrigger(AttackId);
        wasAttacking = attacking;
    }

    /// <summary>Hook to Damageable -> On Damaged.</summary>
    public void PlayHit()
    {
        if (!dead) SetTrigger(HitId);
    }

    /// <summary>Hook to Damageable -> On Died.</summary>
    public void PlayDeath()
    {
        if (dead) return;
        dead = true;

        // A lethal hit fires "damaged" and "died" together: drop the hit / attack / jump reactions so death plays cleanly.
        if (available.Contains(HitId)) animator.ResetTrigger(HitId);
        if (available.Contains(AttackId)) animator.ResetTrigger(AttackId);
        if (available.Contains(JumpId)) animator.ResetTrigger(JumpId);

        SetTrigger(DieId);
    }

    /// <summary>Call after a respawn so the character can move and animate again.</summary>
    public void ResetAfterRespawn()
    {
        dead = false;
        animator.Rebind();
    }

    // ---- Safe setters ----------------------------------------------------

    private void SetFloat(int id, float value)
    {
        if (available.Contains(id)) animator.SetFloat(id, value);
    }

    private void SetBool(int id, bool value)
    {
        if (available.Contains(id)) animator.SetBool(id, value);
    }

    private void SetTrigger(int id)
    {
        if (available.Contains(id)) animator.SetTrigger(id);
    }

    // ---- Editor helper ---------------------------------------------------

    /// <summary>
    /// Mixamo characters are about 1.8 m tall; yours is 0.4 m. This scales the model to the CharacterController's height
    /// and puts its feet and middle on the controller. Right-click this component's header > "Fit Model To Character".
    /// </summary>
    [ContextMenu("Fit Model To Character")]
    private void FitModelToCharacter()
    {
        CharacterController controller = GetComponent<CharacterController>();
        Animator model = animator != null ? animator : GetComponentInChildren<Animator>();

        if (controller == null || model == null)
        {
            Debug.LogWarning("Needs a CharacterController on the player and an Animator on the model.", this);
            return;
        }

        SkinnedMeshRenderer[] renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>();
        if (renderers.Length == 0)
        {
            Debug.LogWarning("The model has no SkinnedMeshRenderer, so its height can't be measured.", this);
            return;
        }

#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(model.transform, "Fit Model To Character");
#endif

        Bounds bounds = CombinedBounds(renderers);
        if (bounds.size.y <= 0.0001f) return;

        model.transform.localScale *= controller.height / bounds.size.y;

        bounds = CombinedBounds(renderers);
        Vector3 feetTarget = transform.TransformPoint(controller.center) + Vector3.down * (controller.height * 0.5f);
        Vector3 shift = new Vector3(feetTarget.x - bounds.center.x, feetTarget.y - bounds.min.y, feetTarget.z - bounds.center.z);
        model.transform.position += shift;

        Debug.Log($"{name}: model scaled to {controller.height} m and placed on the character controller.", this);
    }

    private static Bounds CombinedBounds(SkinnedMeshRenderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}
