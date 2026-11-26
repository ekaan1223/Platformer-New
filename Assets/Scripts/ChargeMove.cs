using System.Collections; // gives us IEnumerator, for coroutines
using UnityEngine;        // gives us access to Unity classes (Vector3, Mathf, Time, etc.)

// CHARGE MOVE
// The boss winds up, locks onto the player's position, then dashes in a straight line.
// The direction is locked at the start of the dash, so the player can dodge sideways.
// It stops early if it runs into a wall.

[CreateAssetMenu(menuName = "Boss/Moves/Charge", fileName = "Charge")] // adds the "Create > Boss > Moves > Charge" menu entry
public class ChargeMove : BossMove // built on BossMove (gets its timing and Telegraph)
{
    [Header("Charge")]
    [SerializeField, Min(0f)] private float damage = 1f;
    [SerializeField, Min(0f)] private float dashSpeedMetersPerSecond = 4f;
    [SerializeField, Min(0.1f)] private float dashDistanceMeters = 4f;
    [Tooltip("How far beyond the boss's body the player can be and still get hit.")]
    [SerializeField, Min(0f)] private float hitPaddingMeters = 0.1f;

    public override IEnumerator Perform(BossController boss)
    {
        yield return Telegraph(boss); // wind up first (the boss turns toward the player)

        Vector3 direction = boss.DirectionToPlayerFlat(); // locked in NOW, so the player can dodge sideways
        float travelled = 0f;                             // how far the boss has dashed so far
        bool hasHitPlayer = false;                        // a local variable on purpose: this asset is shared (see BossMove)

        while (travelled < dashDistanceMeters)
        {
            float step = Mathf.Min(dashSpeedMetersPerSecond * Time.deltaTime, dashDistanceMeters - travelled); // this frame's distance, never past the end

            Vector3 before = boss.transform.position; // where we were before moving
            boss.Move(direction * step);              // move (walls stop it)

            Vector3 moved = boss.transform.position - before; // how far we really got
            moved.y = 0f;                                     // only sideways counts
            if (moved.magnitude < step * 0.25f) break;        // we hardly moved: a wall is in the way, stop the charge
            travelled += step;

            if (!hasHitPlayer && boss.IsPlayerTouching(hitPaddingMeters)) // ran into the player (only hurts once per charge)
            {
                hasHitPlayer = true;
                boss.DamagePlayer(damage, direction);
            }

            yield return null; // wait one frame
        }
    }
}
