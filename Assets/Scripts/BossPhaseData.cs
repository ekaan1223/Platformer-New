using UnityEngine;

/// <summary>How the boss gets its enemies into the fight.</summary>
public enum EnemySpawnStyle
{
    AtSpawnPoints,       // they just appear at the spawn points (or in a ring around the boss)
    ThrownTowardPlayer   // the boss lobs them in an arc so they land at / close to the player
}

/// <summary>How a phase spawns waves of enemies. Lives inside a BossPhaseData asset.</summary>
[System.Serializable]
public class BossWaveSettings
{
    [Tooltip("ON = the boss spawns waves of enemies during this phase.")]
    [SerializeField] private bool spawnWaves;
    [Tooltip("Enemy prefabs to spawn (one picked at random for each enemy). They need EnemyAI + Damageable. " +
             "For a horde that hunts you right away, set their Sight Angle to 360 and give them a big Max Chase Distance.")]
    [SerializeField] private GameObject[] enemyPrefabs;
    [SerializeField, Min(1)] private int enemiesPerWave = 3;
    [SerializeField, Min(0f)] private float firstWaveDelaySeconds = 2f;
    [SerializeField, Min(0.5f)] private float secondsBetweenWaves = 12f;
    [Tooltip("No new wave appears while this many of the boss's enemies are still alive.")]
    [SerializeField, Min(1)] private int maxAliveEnemies = 6;
    [Tooltip("Seconds between each enemy of a wave appearing (0 = all at once).")]
    [SerializeField, Min(0f)] private float spawnStaggerSeconds = 0.25f;

    [Header("Throwing (only used when Spawn Style is Thrown Toward Player)")]
    [SerializeField] private EnemySpawnStyle spawnStyle = EnemySpawnStyle.AtSpawnPoints;
    [Tooltip("Where the enemy leaves the boss, in the boss's local space (x right, y up, z forward).")]
    [SerializeField] private Vector3 throwOriginLocalOffset = new Vector3(0f, 0.6f, 0.4f);
    [Tooltip("How long each enemy is in the air. Longer = a higher, slower lob.")]
    [SerializeField, Min(0.2f)] private float throwFlightSeconds = 1.2f;
    [Tooltip("Enemies land at least this far from the player (so they don't land on top of you).")]
    [SerializeField, Min(0f)] private float landMinDistanceFromPlayerMeters = 0.4f;
    [Tooltip("...and at most this far. Small = lands right next to you, big = lands in the general area.")]
    [SerializeField, Min(0f)] private float landMaxDistanceFromPlayerMeters = 1.2f;

    public bool SpawnWaves => spawnWaves;
    public GameObject[] EnemyPrefabs => enemyPrefabs;
    public int EnemiesPerWave => enemiesPerWave;
    public float FirstWaveDelaySeconds => firstWaveDelaySeconds;
    public float SecondsBetweenWaves => secondsBetweenWaves;
    public int MaxAliveEnemies => maxAliveEnemies;
    public float SpawnStaggerSeconds => spawnStaggerSeconds;
    public EnemySpawnStyle SpawnStyle => spawnStyle;
    public Vector3 ThrowOriginLocalOffset => throwOriginLocalOffset;
    public float ThrowFlightSeconds => throwFlightSeconds;
    public float LandMinDistanceFromPlayerMeters => landMinDistanceFromPlayerMeters;
    public float LandMaxDistanceFromPlayerMeters => Mathf.Max(landMaxDistanceFromPlayerMeters, landMinDistanceFromPlayerMeters);
}

/// <summary>
/// One phase of the boss fight as a data asset (Create > Boss > Phase). Make one asset per phase and
/// drag them into the BossController in order. Everything about a phase is tuned here, so you can
/// swap moves, waves and the arena level without touching code.
/// </summary>
[CreateAssetMenu(menuName = "Boss/Phase", fileName = "BossPhase")]
public class BossPhaseData : ScriptableObject
{
    [Header("When it starts")]
    [Tooltip("ON = starts when the boss's health drops to the fraction below. " +
             "OFF = never starts from health; it starts when the previous phase FINISHES " +
             "(e.g. the finale, which starts when the arena is completed).")]
    [SerializeField] private bool startedByHealth = true;
    [Tooltip("The phase starts when the boss's health drops to this fraction. " +
             "1 = full health, 0.65 = 65%, 0.3 = 30%. The first phase should be 1.")]
    [SerializeField, Range(0f, 1f)] private float startsAtHealthFraction = 1f;

    [Header("Defence")]
    [Tooltip("ON = the sword and stomps can't hurt the boss during this phase.")]
    [SerializeField] private bool invulnerable;
    [Tooltip("ON = FINALE: any single hit (sword slash or stomp) defeats the boss, and the boss can be stomped.")]
    [SerializeField] private bool isFinisher;

    [Header("Moves")]
    [Tooltip("The attacks the boss picks from during this phase. Drag Boss Move assets in here. " +
             "Empty = the boss does nothing (the arena phase). " +
             "A boss that must stand still should not have a Charge move.")]
    [SerializeField] private BossMove[] moves;
    [Tooltip("ON = random move each time (never the same one twice in a row). OFF = in list order.")]
    [SerializeField] private bool randomOrder = true;
    [Tooltip("Extra pause between moves, on top of each move's own recovery time.")]
    [SerializeField, Min(0f)] private float secondsBetweenMoves = 1f;

    [Header("Enemy Waves")]
    [Tooltip("ON = enemies the boss spawned in earlier phases disappear when this phase starts.")]
    [SerializeField] private bool clearEnemiesOnStart;
    [SerializeField] private BossWaveSettings waves = new BossWaveSettings();

    [Header("Arena Level (puzzle / platformer phase)")]
    [Tooltip("Optional. A level prefab you made (with an ArenaLevel component on its root). " +
             "It is spawned when this phase starts and the player is moved into it.")]
    [SerializeField] private GameObject arenaLevelPrefab;
    [Tooltip("ON = when every memory is collected: the arena despawns, the player goes back to the boss area, " +
             "and the NEXT phase starts. Needs a LevelMemoryTracker whose Small Memories Required is the number of memories.")]
    [SerializeField] private bool endsWhenMemoriesComplete;

    public bool StartedByHealth => startedByHealth;
    public float StartsAtHealthFraction => startsAtHealthFraction;
    public bool Invulnerable => invulnerable;
    public bool IsFinisher => isFinisher;
    public BossMove[] Moves => moves;
    public bool RandomOrder => randomOrder;
    public float SecondsBetweenMoves => secondsBetweenMoves;
    public bool ClearEnemiesOnStart => clearEnemiesOnStart;
    public BossWaveSettings Waves => waves;
    public GameObject ArenaLevelPrefab => arenaLevelPrefab;
    public bool EndsWhenMemoriesComplete => endsWhenMemoriesComplete;
}
