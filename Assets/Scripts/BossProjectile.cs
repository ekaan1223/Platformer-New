using UnityEngine;

/// <summary>
/// A shot fired by the boss. It flies in a straight line, damages the player if it hits them,
/// disappears when it hits a wall, and flies through the boss and other enemies.
/// It needs no Rigidbody or collider: each frame it sweeps a small sphere forward to see what is in its way.
/// </summary>
public class BossProjectile : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float lifetimeSeconds = 6f;
    [Tooltip("Size of the shot for hit detection, in meters.")]
    [SerializeField, Min(0.01f)] private float radiusMeters = 0.08f;
    [Tooltip("Which layers can stop the shot. Leave on Everything unless you have a reason to change it.")]
    [SerializeField] private LayerMask hitMask = ~0;

    private Vector3 velocity;
    private float damage;
    private float secondsLeft;

    private void Awake() => secondsLeft = lifetimeSeconds;

    public void Launch(Vector3 direction, float speed, float damageAmount)
    {
        velocity = direction.normalized * speed;
        damage = damageAmount;
        secondsLeft = lifetimeSeconds;
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;

        secondsLeft -= deltaTime;
        if (secondsLeft <= 0f)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 step = velocity * deltaTime;
        float distance = step.magnitude;

        if (distance > 0f && Physics.SphereCast(transform.position, radiusMeters, step / distance,
                out RaycastHit hit, distance, hitMask, QueryTriggerInteraction.Ignore))
        {
            // Friendly things (the boss, its enemies): fly straight through them.
            bool friendly = hit.collider.GetComponentInParent<BossController>() != null
                            || hit.collider.GetComponentInParent<EnemyAI>() != null;

            if (!friendly)
            {
                HitSomething(hit.collider);
                return;
            }
        }

        transform.position += step;
    }

    private void HitSomething(Collider other)
    {
        if (other.GetComponentInParent<PlayerController>() != null)
        {
            IDamageable target = other.GetComponentInParent<IDamageable>();
            if (target != null) target.TakeDamage(damage, velocity.normalized);
        }

        Destroy(gameObject);
    }
}
