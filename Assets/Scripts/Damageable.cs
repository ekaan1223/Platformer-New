using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Simple health you can drop on a test dummy or enemy. Wire reactions up with UnityEvents.
/// The enemy also needs a (non-trigger) Collider, or the sword can never detect it.
/// </summary>
public class Damageable : MonoBehaviour, IDamageable
{
    [SerializeField, Min(0.01f)] private float maxHealth = 3f;

    [Header("Debug / Test Behaviour")]
    [Tooltip("Print a message to the Console every time this takes damage.")]
    [SerializeField] private bool logDamage = true;
    [Tooltip("Destroy this GameObject when health reaches 0.")]
    [SerializeField] private bool destroyOnDeath = true;

    [Header("Runtime (watch this drop while playing)")]
    [SerializeField] private float currentHealth;

    [Header("Events")]
    [SerializeField] private UnityEvent onDamaged;
    [SerializeField] private UnityEvent onDied;

    public bool IsDead => currentHealth <= 0f;

    private void Awake() => currentHealth = maxHealth;

    public void TakeDamage(float amount, Vector3 hitDirection)
    {
        if (IsDead) return;

        currentHealth -= amount;
        if (logDamage) Debug.Log($"{name} took {amount} damage. Health: {currentHealth}/{maxHealth}", this);

        onDamaged?.Invoke();

        if (!IsDead) return;

        onDied?.Invoke();
        if (destroyOnDeath) Destroy(gameObject);
    }
}
