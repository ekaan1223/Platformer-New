using System.Collections;
using UnityEngine;

/// <summary>
/// The boss winds up, locks onto the player's position, then dashes in a straight line.
/// The direction is locked at the start of the dash, so the player can dodge sideways.
/// It stops early if it runs into a wall.
/// </summary>
[CreateAssetMenu(menuName = "Boss/Moves/Charge", fileName = "Charge")]
public class ChargeMove : BossMove
{
    [Header("Charge")]
    [SerializeField, Min(0f)] private float damage = 1f;
    [SerializeField, Min(0f)] private float dashSpeedMetersPerSecond = 4f;
    [SerializeField, Min(0.1f)] private float dashDistanceMeters = 4f;
    [Tooltip("How far beyond the boss's body the player can be and still get hit.")]
    [SerializeField, Min(0f)] private float hitPaddingMeters = 0.1f;

    public override IEnumerator Perform(BossController boss)
    {
        yield return Telegraph(boss);

        Vector3 direction = boss.DirectionToPlayerFlat();   // locked in NOW
        float travelled = 0f;
        bool hasHitPlayer = false;   // local variable on purpose: this asset is shared (see BossMove)

        while (travelled < dashDistanceMeters)
        {
            float step = Mathf.Min(dashSpeedMetersPerSecond * Time.deltaTime, dashDistanceMeters - travelled);

            Vector3 before = boss.transform.position;
            boss.Move(direction * step);

            Vector3 moved = boss.transform.position - before;
            moved.y = 0f;
            if (moved.magnitude < step * 0.25f) break;   // blocked by a wall, stop the charge
            travelled += step;

            if (!hasHitPlayer && boss.IsPlayerTouching(hitPaddingMeters))
            {
                hasHitPlayer = true;
                boss.DamagePlayer(damage, direction);
            }

            yield return null;
        }
    }
}
