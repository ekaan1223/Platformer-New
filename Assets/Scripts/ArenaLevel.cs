using UnityEngine; // gives us access to Unity classes (MonoBehaviour, Transform, Gizmos, etc.)

// ARENA LEVEL
// Put this on the ROOT of your arena prefab (the pre-made puzzle / platformer level of boss phase 3).
// Make an empty child object where the player should start, name it "PlayerSpawn", and drag it into the slot below.
// When the boss spawns the arena (BossController.EnterArena), it moves the player to that point.

public class ArenaLevel : MonoBehaviour // MonoBehaviour = a script Unity can put on an object
{
    [Tooltip("Where the player appears when they enter the arena. The player faces the same way as this object's blue arrow.")]
    [SerializeField] private Transform playerSpawnPoint;

    // BossController reads this.
    public Transform PlayerSpawnPoint
    {
        get { return playerSpawnPoint; }
    }

    // Scene view: a green ball where the player appears, and a line showing which way they will face.
    private void OnDrawGizmos()
    {
        if (playerSpawnPoint == null) return; // no spawn point dragged in: nothing to draw

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(playerSpawnPoint.position, 0.1f);
        Gizmos.DrawLine(playerSpawnPoint.position, playerSpawnPoint.position + playerSpawnPoint.forward * 0.3f);
    }
}
