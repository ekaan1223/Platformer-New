using UnityEngine;        // gives us access to Unity classes (MonoBehaviour, Vector3, Debug, etc.)
using UnityEngine.Events; // gives us UnityEvent (the event lists you can fill in the Inspector)

// DAMAGEABLE
// Simple health for the player, enemies and test dummies. Hook up the reactions (animation, sound, death) with the
// events in the Inspector: On Damaged fires on every hit, On Died once when health reaches 0.
// The object also needs a solid (not trigger) Collider, or the sword can never find it.
//
// THE PLAYER: leave "Destroy On Death" OFF. The player plays its Death animation (PlayerController.Die) and
// GameManager handles the restart. A destroyed player would also take the camera with it.

public class Damageable : MonoBehaviour, IDamageable // IDamageable = "the sword, stomps and weapons can hurt me"
{
    [SerializeField, Min(0.01f)] private float maxHealth = 3f; // health at the start

    [Header("Debug / Test Behaviour")]
    [Tooltip("Print a message to the Console every time this takes damage (only in the Editor, never in the built game).")]
    [SerializeField] private bool logDamage = true;
    [Tooltip("Destroy this GameObject when health reaches 0. Leave it OFF on the player.")]
    [SerializeField] private bool destroyOnDeath = true;
    [Tooltip("Seconds to wait before destroying it, so a death animation can play. 0 = right away.")]
    [SerializeField, Min(0f)] private float destroyDelaySeconds = 0f;

    [Header("Runtime (watch this drop while playing)")]
    [SerializeField] private float currentHealth;

    [Header("Events")]
    [SerializeField] private UnityEvent onDamaged; // every hit
    [SerializeField] private UnityEvent onDied;    // once, when health reaches 0

    // True once health has reached 0. EnemyWeaponAttack reads it.
    public bool IsDead
    {
        get { return currentHealth <= 0f; }
    }

    // Awake runs once, when the object is created: start at full health.
    private void Awake()
    {
        currentHealth = maxHealth;
    }

    // Called by the sword, stomps and enemy weapons (through IDamageable).
    public void TakeDamage(float amount, Vector3 hitDirection)
    {
        if (IsDead) return; // already dead: more hits change nothing

        currentHealth -= amount;

        // Application.isEditor = only while testing in Unity. Building the message costs time, so the game never does it.
        if (logDamage && Application.isEditor)
        {
            Debug.Log(name + " took " + amount + " damage. Health: " + currentHealth + "/" + maxHealth, this);
        }

        if (onDamaged != null) onDamaged.Invoke(); // hit reaction (animation, sound...)

        if (!IsDead) return; // still alive: done

        if (onDied != null) onDied.Invoke();                          // death reaction
        if (destroyOnDeath) Destroy(gameObject, destroyDelaySeconds); // remove the object (after the delay)
    }
}
