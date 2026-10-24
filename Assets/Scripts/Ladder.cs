using UnityEngine; // gives us access to Unity classes (MonoBehaviour, Collider, BoxCollider, etc.)

// LADDER
// Put this script on the ladder. It adds a Box Collider set to "Is Trigger": that box is the climbable zone.
// Make the box a little thicker than the ladder model, so the player can walk INTO it.
//
// HOW IT PLAYS:
//   touch the ladder -> you hold on to it
//   W = climb up, S = climb down, A / D = move sideways, nothing = hang still
//   climb out of the top or the sides of the box, or climb down onto the floor = let go (off the side = you fall)
// The climbing itself is done by PlayerController (it owns all player movement); this script only says
// "the player touched me" and "the player left me".

[RequireComponent(typeof(BoxCollider))] // Unity adds a Box Collider automatically if the object has none
public class Ladder : MonoBehaviour      // MonoBehaviour = a script Unity can put on an object
{
    public float climbSpeed = 2f; // how fast the player climbs up and down (meters per second). PlayerController reads this

    // Awake runs once, when the object is created.
    private void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true; // the zone must never block the player, only notice them
    }

    // Unity calls this when something walks into the trigger box.
    private void OnTriggerEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>(); // is it the player? (null if not)
        if (player != null) player.StartClimbing(this);                           // yes: grab on to this ladder
    }

    // Unity calls this when something leaves the trigger box (climbed off the top, bottom or sides).
    private void OnTriggerExit(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>(); // is it the player? (null if not)
        if (player != null) player.StopClimbing(this);                            // yes: let go of this ladder
    }

    // Reset runs in the editor when the script is first added: make the box a trigger right away.
    private void Reset()
    {
        GetComponent<BoxCollider>().isTrigger = true; // so you can see in the Inspector that it is a trigger
    }
}
