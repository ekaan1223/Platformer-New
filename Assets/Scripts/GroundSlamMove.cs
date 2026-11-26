using System.Collections; // gives us IEnumerator, for coroutines
using UnityEngine;        // gives us access to Unity classes (GameObject, Vector3, etc.)

// GROUND SLAM MOVE
// The boss winds up, then a shockwave hits everything within a radius around it.
// The shockwave only hurts things near the ground, so the player can JUMP over it.

[CreateAssetMenu(menuName = "Boss/Moves/Ground Slam", fileName = "GroundSlam")] // adds the "Create > Boss > Moves > Ground Slam" menu entry
public class GroundSlamMove : BossMove // built on BossMove (gets its timing and Telegraph)
{
    [Header("Slam")]
    [SerializeField, Min(0f)] private float damage = 1f;
    [Tooltip("How far the shockwave reaches from the boss, in meters.")]
    [SerializeField, Min(0.1f)] private float radiusMeters = 2f;
    [Tooltip("The player is only hit if their feet are lower than this above the boss's feet. Jump higher to dodge.")]
    [SerializeField, Min(0f)] private float hitHeightMeters = 0.25f;

    [Header("Effects (optional prefabs)")]
    [Tooltip("Shown during the wind-up so the player can see the danger zone.")]
    [SerializeField] private GameObject telegraphEffect;
    [Tooltip("Shown when the shockwave lands.")]
    [SerializeField] private GameObject impactEffect;
    [SerializeField, Min(0f)] private float impactEffectLifetimeSeconds = 2f;
    [Tooltip("Stretch the effects to match the radius. Make them a flat disc that is 1 meter wide.")]
    [SerializeField] private bool scaleEffectsToRadius = true;

    public override IEnumerator Perform(BossController boss)
    {
        SpawnEffect(boss, telegraphEffect, TelegraphSeconds + 0.05f); // show the danger zone during the wind-up

        yield return Telegraph(boss); // wind up (the boss turns toward the player)

        SpawnEffect(boss, impactEffect, impactEffectLifetimeSeconds); // the shockwave effect

        bool playerInRange = boss.FlatDistanceToPlayer() <= radiusMeters;           // close enough?
        bool playerOnGround = boss.PlayerFeetHeightAboveBoss <= hitHeightMeters;    // not jumping over it?
        if (playerInRange && playerOnGround)
        {
            boss.DamagePlayer(damage, boss.DirectionToPlayerFlat());
        }
    }

    // Spawns an effect at the boss's feet and, if wanted, stretches it to the radius.
    private void SpawnEffect(BossController boss, GameObject prefab, float lifetimeSeconds)
    {
        GameObject effect = boss.SpawnEffect(prefab, boss.FeetPosition, lifetimeSeconds); // null if no prefab was set
        if (effect == null || !scaleEffectsToRadius) return;

        Vector3 scale = effect.transform.localScale;
        effect.transform.localScale = new Vector3(radiusMeters * 2f, scale.y, radiusMeters * 2f); // a 1 m disc becomes as wide as the shockwave
    }
}
