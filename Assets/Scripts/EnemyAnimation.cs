using UnityEngine; // gives us access to Unity classes (MonoBehaviour, Animator, CharacterController, etc.)

// ENEMY ANIMATION
// Put this on an enemy, next to EnemyAI. It tells the enemy's Animator what the enemy is doing; the arrows in the
// Animator Controller decide what plays (the same idea as the player's). It never moves the enemy: EnemyAI does that.
//
// The Animator Controller needs these parameters (names must match exactly):
//   Speed  (Float)   - how fast the enemy is walking, in m/s. Read from its CharacterController every frame
//   Attack (Trigger) - PlayAttack() fires it. Hook it to EnemyAI > On Player Hit
//   Hit    (Trigger) - PlayHit() fires it. Hook it to Damageable > On Damaged
//   Dead   (Bool)    - PlayDeath() turns it on. Hook it to Damageable > On Died (and give Damageable a Destroy Delay,
//                      or the enemy vanishes before the death animation can play)

[RequireComponent(typeof(CharacterController))] // the speed comes from the CharacterController EnemyAI moves
public class EnemyAnimation : MonoBehaviour   // MonoBehaviour = a script Unity can put on an object
{
    public Animator animator; // the Animator on the enemy's model; empty = the first one on this object or its children

    private static readonly int SpeedParameter = Animator.StringToHash("Speed");   // names turned into numbers once, so Update never works with strings
    private static readonly int AttackParameter = Animator.StringToHash("Attack");
    private static readonly int HitParameter = Animator.StringToHash("Hit");
    private static readonly int DeadParameter = Animator.StringToHash("Dead");

    private CharacterController controller; // the enemy's body (found in Awake)

    // Awake runs once, when the object is created.
    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>(); // not dragged in: use the model's Animator

        if (animator == null)
        {
            Debug.LogWarning(name + ": EnemyAnimation found no Animator on the model, so the enemy moves without animations.", this);
            return;
        }

        animator.applyRootMotion = false; // EnemyAI moves the enemy; if the animation moved it too, the two would fight
    }

    // Update runs every frame.
    private void Update()
    {
        if (animator == null) return; // nothing to animate

        Vector3 velocity = controller.velocity; // how fast the CharacterController really moved last frame
        velocity.y = 0f;                        // only walking counts, not falling
        animator.SetFloat(SpeedParameter, velocity.magnitude);
    }

    // Plays the attack. Public with no parameters, so EnemyAI's On Player Hit event can call it from the Inspector.
    public void PlayAttack()
    {
        if (animator != null) animator.SetTrigger(AttackParameter);
    }

    // Plays the flinch. Public with no parameters, so Damageable's On Damaged event can call it from the Inspector.
    public void PlayHit()
    {
        if (animator != null) animator.SetTrigger(HitParameter);
    }

    // Plays the death. Public with no parameters, so Damageable's On Died event can call it from the Inspector.
    // Dead is a Bool (not a Trigger) so the Animator stays dead: the Attack and Hit arrows check "Dead = false".
    public void PlayDeath()
    {
        if (animator == null) return;
        animator.ResetTrigger(HitParameter);    // the killing blow also fired Hit: forget it, death wins
        animator.ResetTrigger(AttackParameter);
        animator.SetBool(DeadParameter, true);
    }
}
