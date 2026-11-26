using UnityEngine;        // gives us access to Unity classes (MonoBehaviour, Mathf, Debug, etc.)
using UnityEngine.Events; // gives us UnityEvent (the event lists you can fill in the Inspector)

// BOSS HEALTH
// The boss's health. It is an IDamageable, so the sword and stomps hurt it like any other target.
// It does NOT decide what happens at which health: BossController listens to HealthFractionChanged and switches
// phases. BossController also sets these here:
//   - IsInvulnerable      : hits do nothing (the arena phase).
//   - DamageFloorFraction : hits can never push health below this fraction, so one big hit can't skip a whole
//                           phase or kill the boss before the finale.
//   - OneHitKill          : the finale. Any hit takes all the boss's health.

public class BossHealth : MonoBehaviour, IDamageable // IDamageable = "the sword and stomps can hurt me"
{
    [SerializeField, Min(1f)] private float maxHealth = 100f; // health at the start

    [Header("Debug")]
    [Tooltip("Print a message to the Console every time the boss takes damage (only in the Editor, never in the built game).")]
    [SerializeField] private bool logDamage = true;

    [Header("Runtime (watch this drop while playing)")]
    [SerializeField] private float currentHealth;

    [Header("Events (hook hit flash / sound here)")]
    [SerializeField] private UnityEvent onDamaged;

    // A "shout" with the new health as a fraction (1 = full, 0 = empty). BossController listens; a health bar could too.
    public event System.Action<float> HealthFractionChanged;

    // Read-only values for other scripts.
    public float MaxHealth
    {
        get { return maxHealth; }
    }

    public float CurrentHealth
    {
        get { return currentHealth; }
    }

    public float HealthFraction
    {
        get { return currentHealth / maxHealth; }
    }

    // Set by BossController when a phase starts (see the top of this file).
    [System.NonSerialized] public bool IsInvulnerable;
    [System.NonSerialized] public float DamageFloorFraction;
    [System.NonSerialized] public bool OneHitKill;

    // Awake runs once, when the object is created: start at full health.
    private void Awake()
    {
        currentHealth = maxHealth;
    }

    // Called by the sword and stomps (through IDamageable).
    public void TakeDamage(float amount, Vector3 hitDirection)
    {
        if (IsInvulnerable || amount <= 0f) return; // can't be hurt right now

        float floor = maxHealth * DamageFloorFraction; // the lowest health this phase allows

        float newHealth;
        if (OneHitKill) newHealth = 0f;                                 // finale: one hit is enough
        else newHealth = Mathf.Max(currentHealth - amount, floor);      // normal hit, but never below the floor
        if (Mathf.Approximately(newHealth, currentHealth)) return;      // already at the floor: nothing changes

        SetHealth(newHealth);

        // Application.isEditor = only while testing in Unity. Building the message costs time, so the game never does it.
        if (logDamage && Application.isEditor)
        {
            Debug.Log(name + " took " + amount + " damage. Health: " + currentHealth.ToString("0.#") + "/" + maxHealth, this);
        }

        if (onDamaged != null) onDamaged.Invoke();
    }

    // Sets health directly, ignoring invulnerability. Public, so a UnityEvent (a cutscene, a test button) can use it.
    public void SetHealthFraction(float fraction)
    {
        SetHealth(maxHealth * Mathf.Clamp01(fraction));
    }

    // Changes the health and tells everyone listening.
    private void SetHealth(float value)
    {
        currentHealth = value;
        if (HealthFractionChanged != null) HealthFractionChanged(HealthFraction);
    }
}
