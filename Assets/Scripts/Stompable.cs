using UnityEngine; // gives us access to Unity classes (MonoBehaviour, etc.)

// STOMPABLE
// A "tag" component: anything that has it can be stomped by the player (landed on from above).
//   - EnemyAI adds it to every enemy automatically in Awake, so normal enemies are always stompable.
//   - The boss has one too, but BossController switches it OFF except during the finale.
// Who reads it:
//   - PlayerStomp : only stomps things that have an ENABLED Stompable, and uses stompDamage as the damage.
//   - EnemyAI     : if it has a Stompable, the player landing on its head is a stomp, so the enemy does NOT hurt the player.

public class Stompable : MonoBehaviour // MonoBehaviour = a script Unity can put on an object
{
    public float stompDamage = 3f; // damage one stomp deals to this enemy's health (Damageable). Enemy health is 3, so 3 = dies in one stomp; 1 = needs three stomps
}
