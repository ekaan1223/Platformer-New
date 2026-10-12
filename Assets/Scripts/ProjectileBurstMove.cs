using System.Collections;
using UnityEngine;

/// <summary>
/// The boss winds up, then fires a fan of projectiles toward the player.
/// Needs a projectile prefab with the BossProjectile script (a small sphere is fine to start).
/// </summary>
[CreateAssetMenu(menuName = "Boss/Moves/Projectile Burst", fileName = "ProjectileBurst")]
public class ProjectileBurstMove : BossMove
{
    [Header("Projectiles")]
    [SerializeField] private BossProjectile projectilePrefab;
    [SerializeField, Min(1)] private int projectileCount = 5;
    [Tooltip("Total width of the fan, in degrees. 0 = all in one line.")]
    [SerializeField, Range(0f, 180f)] private float spreadDegrees = 50f;
    [SerializeField, Min(0f)] private float projectileSpeedMetersPerSecond = 2f;
    [SerializeField, Min(0f)] private float damage = 1f;
    [Tooltip("Seconds between each shot (0 = all at once).")]
    [SerializeField, Min(0f)] private float secondsBetweenShots = 0f;
    [Tooltip("Where shots come out, in the boss's local space (x right, y up, z forward).")]
    [SerializeField] private Vector3 muzzleLocalOffset = new Vector3(0f, 0.3f, 0.5f);

    public override IEnumerator Perform(BossController boss)
    {
        if (projectilePrefab == null)
        {
            Debug.LogWarning($"{name}: no projectile prefab assigned.", this);
            yield break;
        }

        yield return Telegraph(boss);

        Vector3 aim = boss.DirectionToPlayerFlat();   // aim is locked when the volley starts

        for (int i = 0; i < projectileCount; i++)
        {
            float t = projectileCount == 1 ? 0.5f : (float)i / (projectileCount - 1);
            float angle = Mathf.Lerp(-spreadDegrees * 0.5f, spreadDegrees * 0.5f, t);
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * aim;

            Vector3 origin = boss.transform.TransformPoint(muzzleLocalOffset);
            BossProjectile shot = Instantiate(projectilePrefab, origin, Quaternion.LookRotation(direction));
            shot.Launch(direction, projectileSpeedMetersPerSecond, damage);

            if (secondsBetweenShots > 0f) yield return new WaitForSeconds(secondsBetweenShots);
        }
    }
}
