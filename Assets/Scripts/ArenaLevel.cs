using UnityEngine;

/// <summary>
/// Put this on the ROOT of your arena prefab (the pre-made puzzle/platformer level).
/// Create an empty child object where the player should start, name it "PlayerSpawn",
/// and drag it into the slot below. When the boss spawns the arena it moves the player to that point.
/// </summary>
public class ArenaLevel : MonoBehaviour
{
    [Tooltip("Where the player appears when they enter the arena. The player faces the same way as this object's blue arrow.")]
    [SerializeField] private Transform playerSpawnPoint;

    public Transform PlayerSpawnPoint => playerSpawnPoint;

    private void OnDrawGizmos()
    {
        if (playerSpawnPoint == null) return;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(playerSpawnPoint.position, 0.1f);
        Gizmos.DrawLine(playerSpawnPoint.position, playerSpawnPoint.position + playerSpawnPoint.forward * 0.3f);
    }
}
