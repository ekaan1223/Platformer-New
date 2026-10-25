using UnityEngine; // gives us access to Unity classes (MonoBehaviour, BoxCollider, Bounds, etc.)

// STOMPABLE
// Everything about being stomped lives here. Put it on anything the player should be able to stomp
// (the Enemy prefab has one; remove it to make an enemy that can't be stomped).
//   - The boss has one too, but BossController switches it OFF except during the finale.
// HEAD ZONE: in Awake it builds an invisible trigger box on top of the body, a bit WIDER than the body and reaching
// a bit above and below the top. Falling into that box counts as a stomp, even if you only clip the edge of the head.
// Bigger box = more forgiving. Select the enemy in the Scene view to see it (orange box).
// Who reads it:
//   - PlayerStomp : stomps when the falling player enters the head zone (or lands on the body's top), using stompDamage.
//   - EnemyAI     : asks IsBeingStomped before hurting the player on touch. It knows nothing else about stomping.

public class Stompable : MonoBehaviour // MonoBehaviour = a script Unity can put on an object
{
    public float stompDamage = 3f; // damage one stomp deals to this enemy's health (Damageable). Enemy health is 3, so 3 = dies in one stomp; 1 = needs three stomps

    [Header("Head zone (bigger = more forgiving)")]
    [Range(1f, 3f)]
    public float headWidthMultiplier = 1.6f; // how wide the head zone is, compared to the body (1 = exactly as wide, 2 = twice as wide)
    public float headHeightMeters = 0.15f;   // how far the head zone reaches above AND below the top of the body (meters)
    public float topContactToleranceMeters = 0.1f; // a player whose feet are within this distance of the top (or higher) is landing on the head

    [System.NonSerialized] public BoxCollider HeadZone; // the trigger box built in Awake. PlayerStomp checks it

    private Collider body; // the body the head sits on (found in Awake)

    // Awake runs once, when the object is created.
    private void Awake()
    {
        body = GetComponent<CharacterController>();  // the enemy's body: its CharacterController (it is a Collider too)...
        if (body == null) body = GetComponent<Collider>(); // ...or any other collider
        if (body == null) return;                    // no body: nothing to put a head on

        Bounds bounds = body.bounds; // the body's box in the world

        GameObject zone = new GameObject("HeadZone");   // a new empty child object for the trigger
        zone.layer = gameObject.layer;                  // same layer as the enemy
        zone.transform.SetParent(transform, false);     // stick to the enemy (moves and turns with it)
        zone.transform.position = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z); // centred on the top of the head

        HeadZone = zone.AddComponent<BoxCollider>(); // the box itself
        HeadZone.isTrigger = true;                   // a zone, not a wall: the player passes into it

        Vector3 scale = transform.lossyScale; // the box size is in LOCAL units, so undo the enemy's scale
        HeadZone.size = new Vector3(
            bounds.size.x * headWidthMultiplier / scale.x, // wider than the body
            headHeightMeters * 2f / scale.y,               // above AND below the top
            bounds.size.z * headWidthMultiplier / scale.z);
    }

    /// <summary>
    /// True if the player (their body's box) is stomping this enemy: feet on or above the head, or inside the head zone.
    /// EnemyAI asks this before it hurts the player on touch, so a stomp never also hurts the player.
    /// </summary>
    public bool IsBeingStomped(Bounds player)
    {
        if (!isActiveAndEnabled || body == null) return false;                       // switched off (the boss before its finale): no stomps
        if (player.min.y >= body.bounds.max.y - topContactToleranceMeters) return true; // feet on top of the head
        return HeadZone != null && HeadZone.bounds.Intersects(player);               // inside the head zone
    }

    // Scene view: the head zone as an orange box (before Play it shows where the zone WILL be).
    private void OnDrawGizmosSelected()
    {
        Collider body = GetComponent<CharacterController>();  // same body as Awake picks
        if (body == null) body = GetComponent<Collider>();
        if (body == null) return;

        Bounds bounds = body.bounds;
        Gizmos.color = new Color(1f, 0.5f, 0f); // orange
        Gizmos.DrawWireCube(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z),
            new Vector3(bounds.size.x * headWidthMultiplier, headHeightMeters * 2f, bounds.size.z * headWidthMultiplier));
    }
}
