using System.Collections;
using UnityEngine;

/// <summary>
/// The boss winds up, then a shockwave hits everything within a radius around it.
/// The shockwave only hurts things near the ground, so the player can JUMP over it.
/// </summary>
[CreateAssetMenu(menuName = "Boss/Moves/Ground Slam", fileName = "GroundSlam")]
public class GroundSlamMove : BossMove
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
        SpawnEffect(boss, telegraphEffect, TelegraphSeconds + 0.05f);

        yield return Telegraph(boss);

        SpawnEffect(boss, impactEffect, impactEffectLifetimeSeconds);

        bool playerInRange = boss.FlatDistanceToPlayer() <= radiusMeters;
        bool playerOnGround = boss.PlayerFeetHeightAboveBoss <= hitHeightMeters;
        if (playerInRange && playerOnGround)
            boss.DamagePlayer(damage, boss.DirectionToPlayerFlat());
    }

    private void SpawnEffect(BossController boss, GameObject prefab, float lifetimeSeconds)
    {
        GameObject effect = boss.SpawnEffect(prefab, boss.FeetPosition, lifetimeSeconds);
        if (effect == null || !scaleEffectsToRadius) return;

        Vector3 scale = effect.transform.localScale;
        effect.transform.localScale = new Vector3(radiusMeters * 2f, scale.y, radiusMeters * 2f);
    }
}
