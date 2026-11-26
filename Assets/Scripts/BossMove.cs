using System.Collections; // gives us IEnumerator, for coroutines (code that runs over several frames)
using UnityEngine;        // gives us access to Unity classes (ScriptableObject, Time, etc.)

// BOSS MOVE
// The base of every boss attack (ChargeMove, GroundSlamMove). Each attack is an ASSET
// (right-click > Create > Boss > Moves), so you can make several versions of the same attack (a slow slam, a huge
// slam...) and choose which ones each phase uses, all from the Inspector.
//
// IMPORTANT: an asset is SHARED by everything that uses it. Never store "this attack's" state in fields here
// (like "has the player been hit yet"); use local variables inside Perform instead.
//
// Perform is a COROUTINE: a function that can pause with "yield return" and carry on later, which is perfect for
// "wind up, wait, strike". BossController runs it and waits until it finishes.
// "abstract" = this class is only a base; the real attacks fill in Perform.

public abstract class BossMove : ScriptableObject // ScriptableObject = a script whose data is saved as its own asset file
{
    [Header("Timing")]
    [Tooltip("Warning time before the attack lands. The boss turns to face the player during it.")]
    [SerializeField, Min(0f)] private float telegraphSeconds = 0.8f;
    [Tooltip("How long the boss is helpless after the attack: the player's chance to hit back.")]
    [SerializeField, Min(0f)] private float recoverySeconds = 1.2f;

    // "protected" = only the attacks built on this base can read it.
    protected float TelegraphSeconds
    {
        get { return telegraphSeconds; }
    }

    // BossController reads this to know how long to wait after the attack.
    public float RecoverySeconds
    {
        get { return recoverySeconds; }
    }

    // The attack itself. Every attack writes its own version of this.
    public abstract IEnumerator Perform(BossController boss);

    // The wind-up: the boss turns toward the player for TelegraphSeconds. The attacks call it first.
    protected IEnumerator Telegraph(BossController boss)
    {
        float elapsed = 0f; // how long the wind-up has lasted so far
        while (elapsed < telegraphSeconds)
        {
            boss.TurnTowardsPlayer();  // keep facing the player
            elapsed += Time.deltaTime; // count the time
            yield return null;         // wait one frame
        }
    }
}
