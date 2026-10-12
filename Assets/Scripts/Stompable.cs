using UnityEngine;

/// <summary>
/// Put this on anything the player is allowed to stomp (normal enemies, YES; the boss, NO).
/// PlayerStomp only reacts to objects that have it, and EnemyAI treats the player landing on the head
/// of a Stompable enemy as a stomp instead of a touch.
/// </summary>
public class Stompable : MonoBehaviour
{
    [Tooltip("Damage one stomp deals to this enemy's health (its Damageable). Damageable's default health is 3, " +
             "so 3 = dies in one stomp. Use 1 for an enemy that needs three stomps.")]
    [SerializeField, Min(0f)] private float stompDamage = 3f;

    public float StompDamage => stompDamage;
}
