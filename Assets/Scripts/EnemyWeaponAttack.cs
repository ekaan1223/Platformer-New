using UnityEngine; // gives us access to Unity classes (MonoBehaviour, Transform, Collider, etc.)

// ENEMY WEAPON ATTACK
// Put this on an enemy next to EnemyAI. When the player comes close, the enemy stops, plays its attack animation
// and swings the weapon in its hand. Only the weapon's box (WeaponHitbox) hurts, and only if it really touches the player.
// So if the player runs away during the windup, the swing misses.
//
// One attack, in three parts (the same idea as the player's sword):
//   1. Windup   : the animation starts, the weapon can't hurt yet.
//   2. Active   : the weapon's box can hurt.
//   3. Recovery : the rest of the animation. Can't hurt.
// Then it waits "Cooldown Seconds" before it may attack again.
//
// While attacking, EnemyAI stands still and turns to the player (EnemyAI.HoldStill).
// When the enemy dies (Damageable) it never attacks again.

[RequireComponent(typeof(EnemyAI))] // it tells EnemyAI to stand still while swinging
public class EnemyWeaponAttack : MonoBehaviour // MonoBehaviour = a script Unity can put on an object
{
    // The parts of one attack, plus Ready (not attacking).
    private enum AttackPhase { Ready, Windup, Active, Recovery }

    [Header("References (empty = found automatically)")]
    [Tooltip("The weapon in the enemy's hand. Empty = the first WeaponHitbox in this enemy's children.")]
    public WeaponHitbox weapon;
    [Tooltip("Plays the attack animation. Empty = the EnemyAnimation on this object.")]
    public EnemyAnimation enemyAnimation;

    [Header("When to attack")]
    [Tooltip("Starts a swing when the player is this close (meters, middle of the enemy to middle of the player, flat).")]
    public float attackRangeMeters = 0.9f;
    [Tooltip("The player must also be within this height difference (meters), so it doesn't swing at a player on a ledge far above.")]
    public float attackHeightMeters = 1f;

    [Header("Timing (match these to the attack animation)")]
    [Tooltip("From the start of the animation until the weapon can hurt.")]
    public float windupSeconds = 0.3f;
    [Tooltip("How long the weapon can hurt.")]
    public float activeSeconds = 0.3f;
    [Tooltip("The rest of the animation after that. Total = Windup + Active + Recovery = animation length / speed.")]
    public float recoverySeconds = 0.2f;
    [Tooltip("Waits this long after an attack before it may attack again.")]
    public float cooldownSeconds = 0.8f;
    [Tooltip("After being hit it can't attack for this long (the length of its Hit animation). Hook OnHit to Damageable > On Damaged.")]
    public float hitStunSeconds = 0.75f;

    private EnemyAI ai;                 // the enemy's movement (found in Awake)
    private Damageable health;          // the enemy's health (found in Awake). Dead enemies don't attack
    private Transform player;           // the player (found when needed)
    private AttackPhase phase = AttackPhase.Ready; // which part of the attack we are in
    private float phaseSecondsRemaining;           // how long the current part still lasts
    private float cooldownRemaining;               // time left before the next attack is allowed
    private float hitStunRemaining;                // time left of the Hit animation: no attacking until it is 0

    // Awake runs once, when the object is created.
    private void Awake()
    {
        ai = GetComponent<EnemyAI>();
        health = GetComponent<Damageable>();
        if (weapon == null) weapon = GetComponentInChildren<WeaponHitbox>();          // not dragged in: the weapon in our hand
        if (enemyAnimation == null) enemyAnimation = GetComponent<EnemyAnimation>(); // not dragged in: the one on this object

        if (weapon == null) Debug.LogWarning(name + ": EnemyWeaponAttack found no WeaponHitbox, so this enemy can't hurt anyone.", this);
    }

    // OnDisable runs when the script is switched off (or the enemy dies and is destroyed).
    private void OnDisable()
    {
        StopAttack(); // never leave the weapon switched on or the enemy frozen
    }

    // Update runs every frame.
    private void Update()
    {
        if (health != null && health.IsDead) // dead: stop everything
        {
            StopAttack();
            return;
        }

        if (cooldownRemaining > 0f) cooldownRemaining -= Time.deltaTime; // count down the wait between attacks
        if (hitStunRemaining > 0f) hitStunRemaining -= Time.deltaTime;   // count down the Hit animation

        if (phase == AttackPhase.Ready) // not attacking: see if the player is close enough
        {
            if (cooldownRemaining <= 0f && hitStunRemaining <= 0f && IsPlayerInRange()) StartAttack();
            return;
        }

        phaseSecondsRemaining -= Time.deltaTime;            // count down the current part
        if (phaseSecondsRemaining <= 0f) NextPhase();       // this part is over: go to the next
    }

    // Call this when the enemy gets hurt (Damageable > On Damaged). It stops the swing it was doing
    // and it can't start a new one until its Hit animation is over.
    public void OnHit()
    {
        StopAttack();                       // a hit interrupts the swing: weapon off
        hitStunRemaining = hitStunSeconds;  // no attacks while the Hit animation plays
    }

    // True if the player is close enough to swing at.
    private bool IsPlayerInRange()
    {
        if (player == null) FindPlayer();
        if (player == null) return false; // no player in the scene

        Vector3 toPlayer = player.position - transform.position;
        if (Mathf.Abs(toPlayer.y) > attackHeightMeters) return false; // too far above or below

        toPlayer.y = 0f;                                    // flat distance only
        return toPlayer.magnitude <= attackRangeMeters;
    }

    // Finds the player: the object with the PlayerController.
    private void FindPlayer()
    {
        PlayerController found = FindFirstObjectByType<PlayerController>();
        if (found != null) player = found.transform;
    }

    // Starts one attack: stand still, play the animation, wait for the windup.
    private void StartAttack()
    {
        phase = AttackPhase.Windup;
        phaseSecondsRemaining = windupSeconds;
        ai.HoldStill = true;                                   // EnemyAI stops walking and faces the player
        if (enemyAnimation != null) enemyAnimation.PlayAttack(); // the Attack trigger in the Animator
    }

    // Goes from one part of the attack to the next.
    private void NextPhase()
    {
        if (phase == AttackPhase.Windup) // windup over: the weapon starts to hurt
        {
            phase = AttackPhase.Active;
            phaseSecondsRemaining = activeSeconds;
            if (weapon != null) weapon.StartSwing(transform); // the box hurts from now on (never this enemy)
        }
        else if (phase == AttackPhase.Active) // active over: the weapon stops hurting
        {
            phase = AttackPhase.Recovery;
            phaseSecondsRemaining = recoverySeconds;
            if (weapon != null) weapon.StopSwing();
        }
        else // recovery over: the attack is done
        {
            StopAttack();
            cooldownRemaining = cooldownSeconds; // wait a bit before the next one
        }
    }

    // Ends the attack (finished, cancelled or dead): weapon off, enemy may walk again.
    private void StopAttack()
    {
        phase = AttackPhase.Ready;
        if (weapon != null) weapon.StopSwing();
        if (ai != null) ai.HoldStill = false;
    }

    // Scene view only: the attack range as a purple circle.
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.7f, 0.2f, 1f);
        Gizmos.DrawWireSphere(transform.position, attackRangeMeters);
    }
}
