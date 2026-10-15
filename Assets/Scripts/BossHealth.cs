using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The boss's health. It is an IDamageable, so your sword hurts it like any other target.
/// It does NOT decide what happens at which health: BossController watches HealthFractionChanged
/// and switches phases. BossController also sets two things here:
///  - IsInvulnerable: the sword does nothing (used for the memory phase).
///  - DamageFloorFraction: sword damage can never push health below this fraction, so one big hit
///    can't skip a whole phase or kill the boss before the final phase.
/// </summary>
public class BossHealth : MonoBehaviour, IDamageable
{
    [SerializeField, Min(1f)] private float maxHealth = 100f;

    [Header("Debug")]
    [SerializeField] private bool logDamage = true;

    [Header("Runtime (watch this drop while playing)")]
    [SerializeField] private float currentHealth;

    [Header("Events (hook hit flash / sound here)")]
    [SerializeField] private UnityEvent onDamaged;

    /// <summary>Raised with the new health as a fraction (1 = full, 0 = empty). A health bar can listen to this.</summary>
    public event System.Action<float> HealthFractionChanged;

    public float MaxHealth => maxHealth;
    public float CurrentHealth => currentHealth;
    public float HealthFraction => currentHealth / maxHealth;
    public bool IsInvulnerable { get; set; }
    public float DamageFloorFraction { get; set; }

    /// <summary>ON during the finale: ANY hit (sword or stomp) takes all the boss's health.</summary>
    public bool OneHitKill { get; set; }

    private void Awake() => currentHealth = maxHealth;

    public void TakeDamage(float amount, Vector3 hitDirection)
    {
        if (IsInvulnerable || amount <= 0f) return;

        float floor = maxHealth * DamageFloorFraction;
        float newHealth = OneHitKill ? 0f : Mathf.Max(currentHealth - amount, floor);
        if (Mathf.Approximately(newHealth, currentHealth)) return;

        SetHealth(newHealth);
        if (logDamage) Debug.Log($"{name} took {amount} damage. Health: {currentHealth:0.#}/{maxHealth}", this);
        onDamaged?.Invoke();
    }

    /// <summary>Set health directly, bypassing invulnerability (the memory phase drains it this way).</summary>
    public void SetHealthFraction(float fraction) => SetHealth(maxHealth * Mathf.Clamp01(fraction));

    private void SetHealth(float value)
    {
        currentHealth = value;
        HealthFractionChanged?.Invoke(HealthFraction);
    }
}
