using System.Collections;
using UnityEngine;

/// <summary>
/// Base class for every boss attack. Each attack is an ASSET (right-click > Create > Boss > Moves),
/// so you can make several versions of the same attack (a slow slam, a huge slam...) and choose which
/// ones each phase uses, all from the Inspector.
///
/// IMPORTANT: a ScriptableObject is shared by everything that uses it. Never store per-attack state in
/// fields here (like "has the player been hit yet"); use local variables inside Perform instead.
///
/// Perform is a COROUTINE: a method that can pause with 'yield return' and carry on later, which is
/// perfect for "wind up, wait, strike". BossController runs it and waits until it finishes.
/// </summary>
public abstract class BossMove : ScriptableObject
{
    [Header("Timing")]
    [Tooltip("Warning time before the attack lands. The boss turns to face the player during it.")]
    [SerializeField, Min(0f)] private float telegraphSeconds = 0.8f;
    [Tooltip("How long the boss is helpless after the attack: the player's chance to hit back.")]
    [SerializeField, Min(0f)] private float recoverySeconds = 1.2f;

    protected float TelegraphSeconds => telegraphSeconds;
    public float RecoverySeconds => recoverySeconds;

    public abstract IEnumerator Perform(BossController boss);

    /// <summary>The wind-up: the boss tracks the player for TelegraphSeconds.</summary>
    protected IEnumerator Telegraph(BossController boss)
    {
        float elapsed = 0f;
        while (elapsed < telegraphSeconds)
        {
            boss.TurnTowardsPlayer();
            elapsed += Time.deltaTime;
            yield return null;
        }
    }
}
