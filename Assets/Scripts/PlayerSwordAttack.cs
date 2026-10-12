using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Basic sword swing: windup -> active (hitbox on) -> recovery. Each target is hit once per swing.
/// Owns the motor's SpeedMultiplier and RotationLocked while attacking.
/// </summary>
[DefaultExecutionOrder(-10)]
public class PlayerSwordAttack : MonoBehaviour
{
    private const int MaxHitsPerFrame = 16;
    private const float DirectionEpsilonSqr = 0.0001f;

    private enum AttackPhase { Idle, Windup, Active, Recovery }

    [Header("References")]
    [SerializeField] private PlayerInputReader input;
    [SerializeField] private PlayerMotor motor;

    [Header("Damage")]
    [SerializeField, Min(0f)] private float damage = 1f;
    [Tooltip("What the sword can hit. Do NOT include the Player layer.")]
    [SerializeField] private LayerMask hitMask = ~0;

    [Header("Hitbox (local space, origin at feet)")]
    [SerializeField] private Vector3 hitboxLocalOffset = new Vector3(0f, 0.2f, 0.3f);
    [SerializeField, Min(0.01f)] private float hitboxRadiusMeters = 0.25f;

    [Header("Timing")]
    [SerializeField, Min(0f)] private float windupSeconds = 0.06f;
    [SerializeField, Min(0.01f)] private float activeSeconds = 0.12f;
    [SerializeField, Min(0f)] private float recoverySeconds = 0.15f;
    [SerializeField, Min(0f)] private float attackBufferSeconds = 0.15f;

    [Header("Movement While Attacking")]
    [Tooltip("1 = full speed, 0 = rooted in place.")]
    [SerializeField, Range(0f, 1f)] private float moveSpeedMultiplier = 0.4f;
    [SerializeField] private bool lockRotationDuringSwing = true;
    [SerializeField] private bool allowAirAttack = true;

    [Header("Events (hook animation / sound / VFX)")]
    [SerializeField] private UnityEvent onAttackStarted;
    [SerializeField] private UnityEvent onSwingActive;
    [SerializeField] private UnityEvent onHitLanded;
    [SerializeField] private UnityEvent onAttackEnded;

    private readonly Collider[] hitBuffer = new Collider[MaxHitsPerFrame];
    private readonly HashSet<IDamageable> alreadyHit = new HashSet<IDamageable>(MaxHitsPerFrame);

    private AttackPhase phase = AttackPhase.Idle;
    private float phaseSecondsRemaining;

    public bool IsAttacking => phase != AttackPhase.Idle;

    private void Awake()
    {
        if (input == null) TryGetComponent(out input);
        if (motor == null) TryGetComponent(out motor);

        if (input == null || motor == null)
        {
            Debug.LogError($"{nameof(PlayerSwordAttack)} needs {nameof(PlayerInputReader)} and {nameof(PlayerMotor)}.", this);
            enabled = false;
        }
    }

    private void OnDisable()
    {
        if (IsAttacking) EndAttack(); // never leave the motor slowed or locked
    }

    private void Update()
    {
        if (phase == AttackPhase.Idle)
        {
            TryStartAttack();
            return;
        }

        phaseSecondsRemaining -= Time.deltaTime;
        if (phase == AttackPhase.Active) DetectHits();
        if (phaseSecondsRemaining <= 0f) AdvancePhase();
    }

    private void TryStartAttack()
    {
        if (!input.IsAttackBuffered(attackBufferSeconds)) return;
        if (!allowAirAttack && !motor.IsGrounded) return;

        input.ConsumeAttack();
        alreadyHit.Clear();

        // Snap toward the stick so you can swing exactly where you're steering.
        if (motor.WorldInputDirection.sqrMagnitude > DirectionEpsilonSqr)
        {
            motor.FaceDirection(motor.WorldInputDirection);
        }

        motor.SpeedMultiplier = moveSpeedMultiplier;
        if (lockRotationDuringSwing) motor.RotationLocked = true;

        EnterPhase(AttackPhase.Windup, windupSeconds);
        onAttackStarted?.Invoke();
    }

    private void AdvancePhase()
    {
        switch (phase)
        {
            case AttackPhase.Windup:
                EnterPhase(AttackPhase.Active, activeSeconds);
                onSwingActive?.Invoke();
                DetectHits(); // hit on the very first active frame
                break;
            case AttackPhase.Active:
                EnterPhase(AttackPhase.Recovery, recoverySeconds);
                break;
            default:
                EndAttack();
                break;
        }
    }

    private void EnterPhase(AttackPhase newPhase, float durationSeconds)
    {
        phase = newPhase;
        phaseSecondsRemaining = durationSeconds;
    }

    private void EndAttack()
    {
        phase = AttackPhase.Idle;
        motor.SpeedMultiplier = 1f;
        motor.RotationLocked = false;
        onAttackEnded?.Invoke();
    }

    // Complexity: O(k), k = colliders overlapping the sphere, capped at MaxHitsPerFrame. No allocations.
    private void DetectHits()
    {
        Vector3 center = transform.TransformPoint(hitboxLocalOffset);
        int count = Physics.OverlapSphereNonAlloc(center, hitboxRadiusMeters, hitBuffer, hitMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider other = hitBuffer[i];
            if (other.transform.IsChildOf(transform)) continue; // don't hit ourselves

            IDamageable target = other.GetComponentInParent<IDamageable>();
            if (target == null || !alreadyHit.Add(target)) continue; // Add() is false if already hit

            Vector3 hitDirection = other.bounds.center - transform.position;
            hitDirection.y = 0f;
            hitDirection = hitDirection.sqrMagnitude > DirectionEpsilonSqr ? hitDirection.normalized : transform.forward;

            target.TakeDamage(damage, hitDirection);
            onHitLanded?.Invoke();
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = phase == AttackPhase.Active ? Color.red : new Color(1f, 0.9f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.TransformPoint(hitboxLocalOffset), hitboxRadiusMeters);
    }
}
