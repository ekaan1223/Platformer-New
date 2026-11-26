using System.Collections.Generic; // gives us List
using UnityEngine;                // gives us access to Unity classes (MonoBehaviour, Physics, etc.)
using UnityEngine.Events;         // gives us UnityEvent (the event lists you can fill in the Inspector)

// PLAYER SWORD ATTACK
// One sword swing, in three parts:
//   1. Windup   : the arm pulls back. Nothing can be hurt yet.
//   2. Active   : the blade can hurt.
//   3. Recovery : the arm comes back. Nothing can be hurt any more.
//
// WHAT HURTS:
//   - If "Weapon" is filled in (a WeaponHitbox on the sword in Caleb's hand), ONLY the sword's box hurts,
//     and only what it really touches during the Active part. Swing at nothing = hit nothing.
//   - If "Weapon" is empty, the old invisible ball in front of the player is used instead (the Hitbox settings below).
//
// This script only does the timing and the damage. Slowing the player down, aiming and the attack animation
// are done by PlayerController (BeginAttack / EndAttack).

[DefaultExecutionOrder(-10)] // runs before PlayerController, so a swing started this frame already slows the player
public class PlayerSwordAttack : MonoBehaviour // MonoBehaviour = a script Unity can put on an object
{
    // The three parts of a swing, plus Idle (no swing).
    private enum AttackPhase { Idle, Windup, Active, Recovery }

    [Header("References")]
    [SerializeField] private PlayerController player; // the PlayerController on this object; found in Awake if empty
    [Tooltip("The sword in Caleb's hand (a WeaponHitbox). Filled in = the sword's box does the hitting. Empty = the old ball in front of the player.")]
    [SerializeField] private WeaponHitbox weapon;

    [Header("Damage")]
    [SerializeField, Min(0f)] private float damage = 1f; // damage of one hit (copied onto the weapon in Awake)
    [Tooltip("What the old ball can hit. Do NOT include the Player layer. (Not used when a Weapon is set.)")]
    [SerializeField] private LayerMask hitMask = ~0;

    [Header("Old hitbox ball (only used when Weapon is empty; local space, origin at feet)")]
    [SerializeField] private Vector3 hitboxLocalOffset = new Vector3(0f, 0.2f, 0.3f);
    [SerializeField, Min(0.01f)] private float hitboxRadiusMeters = 0.25f;

    [Header("Timing")]
    [SerializeField, Min(0f)] private float windupSeconds = 0.06f;    // part 1: arm pulls back
    [SerializeField, Min(0.01f)] private float activeSeconds = 0.12f; // part 2: blade can hurt
    [SerializeField, Min(0f)] private float recoverySeconds = 0.15f;  // part 3: arm comes back
    [SerializeField, Min(0f)] private float attackBufferSeconds = 0.15f; // a click this early before the swing can start still counts

    [Header("Lock until the animation is done")]
    [Tooltip("How long one whole attack lasts, in seconds, counting from the click. Set it to the length of your attack animation " +
             "(clip length divided by the animation speed). Nothing can start a new swing before this time is over. " +
             "0 = just use Windup + Active + Recovery above.")]
    [SerializeField, Min(0f)] private float totalAttackSeconds = 0f;
    [Tooltip("ON = clicks made during an attack are thrown away, so you can't mash the button to restart the animation " +
             "or to queue another swing. OFF = a click near the end is remembered (Attack Buffer Seconds) and swings right after.")]
    [SerializeField] private bool ignoreClicksDuringAttack = true;

    [Header("Movement While Attacking")]
    [Tooltip("1 = full speed, 0 = rooted in place.")]
    [SerializeField, Range(0f, 1f)] private float moveSpeedMultiplier = 0.4f;
    [SerializeField] private bool lockRotationDuringSwing = true;
    [SerializeField] private bool allowAirAttack = true;

    [Header("Getting Hit")]
    [Tooltip("After being hit the player can't attack for this long (the length of the Hit animation). Hook OnHit to Damageable > On Damaged.")]
    [SerializeField, Min(0f)] private float hitStunSeconds = 0.75f;

    [Header("Events (hook sound / VFX)")]
    [SerializeField] private UnityEvent onAttackStarted; // the click started a swing
    [SerializeField] private UnityEvent onSwingActive;   // the blade starts to hurt
    [SerializeField] private UnityEvent onHitLanded;     // the old ball hit something (the weapon has its own hits)
    [SerializeField] private UnityEvent onAttackEnded;   // the whole swing is over

    private AttackPhase phase = AttackPhase.Idle; // which part of the swing we are in
    private float phaseSecondsRemaining;          // how long the current part still lasts
    private List<IDamageable> alreadyHit = new List<IDamageable>(); // old ball only: who was hurt in this swing
    private float hitStunRemaining;               // time left of the Hit animation: no attacking until it is 0

    // True during the whole swing. PlayerController and others read it.
    public bool IsAttacking
    {
        get { return phase != AttackPhase.Idle; }
    }

    // Awake runs once, when the object is created.
    private void Awake()
    {
        if (player == null) player = GetComponent<PlayerController>(); // not dragged in: take the one on this object

        if (player == null) // still nothing: this script can't work
        {
            Debug.LogError("PlayerSwordAttack needs a PlayerController on the same object.", this);
            enabled = false; // switch this script off so it doesn't throw errors every frame
            return;
        }

        if (weapon != null) weapon.damage = damage; // the sword hurts as much as set here
    }

    // OnDisable runs when the script is switched off or destroyed.
    private void OnDisable()
    {
        if (IsAttacking) EndAttack(); // never leave the player slowed or locked, or the blade switched on
    }

    // Update runs every frame.
    private void Update()
    {
        if (hitStunRemaining > 0f) hitStunRemaining -= Time.deltaTime; // count down the Hit animation

        if (phase == AttackPhase.Idle) // no swing yet: see if the player clicked
        {
            if (hitStunRemaining > 0f) // still playing the Hit animation: no attacking
            {
                player.ClearAttackPress(); // throw the click away, so it doesn't swing the moment the Hit animation ends
                return;
            }
            TryStartAttack();
            return;
        }

        phaseSecondsRemaining -= Time.deltaTime;                            // count down the current part
        if (phase == AttackPhase.Active && weapon == null) DetectHitsWithBall(); // the weapon checks its own hits
        if (phaseSecondsRemaining <= 0f) AdvancePhase();                    // this part is over: go to the next
    }

    // Call this when the player gets hurt (Damageable > On Damaged). It stops the swing in progress
    // and no new swing can start until the Hit animation is over.
    public void OnHit()
    {
        if (IsAttacking) EndAttack();      // a hit interrupts the swing: weapon off, control back to PlayerController
        hitStunRemaining = hitStunSeconds; // no attacks while the Hit animation plays
    }

    // Starts a swing if the player clicked and is allowed to attack.
    private void TryStartAttack()
    {
        if (!allowAirAttack && !player.IsGrounded) return;                            // no attacks in the air
        if (!player.TryConsumeAttackPress(attackBufferSeconds)) return;               // no click
        if (!player.BeginAttack(moveSpeedMultiplier, lockRotationDuringSwing)) return; // PlayerController said no (dead, climbing...)

        alreadyHit.Clear();
        EnterPhase(AttackPhase.Windup, windupSeconds);
        if (onAttackStarted != null) onAttackStarted.Invoke();
    }

    // Goes from one part of the swing to the next.
    private void AdvancePhase()
    {
        if (phase == AttackPhase.Windup) // windup over: the blade starts to hurt
        {
            EnterPhase(AttackPhase.Active, activeSeconds);
            if (onSwingActive != null) onSwingActive.Invoke();

            if (weapon != null) weapon.StartSwing(transform); // the sword's box hurts from now on (never us)
            else DetectHitsWithBall();                        // old ball: hit on the very first active frame
        }
        else if (phase == AttackPhase.Active) // active over: the blade stops hurting
        {
            if (weapon != null) weapon.StopSwing();

            // Recovery stretches so the whole attack lasts at least Total Attack Seconds (the animation length).
            float recovery = Mathf.Max(recoverySeconds, totalAttackSeconds - windupSeconds - activeSeconds);
            EnterPhase(AttackPhase.Recovery, recovery);
        }
        else // recovery over: the whole swing is done
        {
            EndAttack();
        }
    }

    // Switches to a part of the swing and sets how long it lasts.
    private void EnterPhase(AttackPhase newPhase, float durationSeconds)
    {
        phase = newPhase;
        phaseSecondsRemaining = durationSeconds;
    }

    // Ends the swing and gives control back to the player.
    private void EndAttack()
    {
        phase = AttackPhase.Idle;
        if (weapon != null) weapon.StopSwing();                    // make sure the blade is off
        if (ignoreClicksDuringAttack) player.ClearAttackPress();   // forget any clicks made while the attack was running
        player.EndAttack();
        if (onAttackEnded != null) onAttackEnded.Invoke();
    }

    // The OLD way (only when no Weapon is set): an invisible ball in front of the player hurts everything inside it.
    private void DetectHitsWithBall()
    {
        Vector3 center = transform.TransformPoint(hitboxLocalOffset);
        Collider[] touching = Physics.OverlapSphere(center, hitboxRadiusMeters, hitMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < touching.Length; i++)
        {
            Collider other = touching[i];
            if (other.transform.IsChildOf(transform)) continue; // don't hit ourselves

            IDamageable target = other.GetComponentInParent<IDamageable>();
            if (target == null) continue;               // nothing with health
            if (alreadyHit.Contains(target)) continue;  // already hurt in this swing
            alreadyHit.Add(target);

            Vector3 hitDirection = other.bounds.center - transform.position;
            hitDirection.y = 0f;
            if (hitDirection.sqrMagnitude < 0.0001f) hitDirection = transform.forward;
            hitDirection.Normalize();

            target.TakeDamage(damage, hitDirection);
            if (onHitLanded != null) onHitLanded.Invoke();
        }
    }

    // Scene view only: the old ball (only drawn when no Weapon is set; the weapon draws its own box).
    private void OnDrawGizmosSelected()
    {
        if (weapon != null) return;
        Gizmos.color = phase == AttackPhase.Active ? Color.red : new Color(1f, 0.9f, 0f, 0.6f);
        Gizmos.DrawWireSphere(transform.TransformPoint(hitboxLocalOffset), hitboxRadiusMeters);
    }
}
