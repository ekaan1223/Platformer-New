using UnityEngine;

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

    public bool SpawnWaves => spawnWaves;
    public GameObject[] EnemyPrefabs => enemyPrefabs;
    public int EnemiesPerWave => enemiesPerWave;
    public float FirstWaveDelaySeconds => firstWaveDelaySeconds;
    public float SecondsBetweenWaves => secondsBetweenWaves;
    public int MaxAliveEnemies => maxAliveEnemies;
    public float SpawnStaggerSeconds => spawnStaggerSeconds;
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
    [Tooltip("The phase starts when the boss's health drops to this fraction. " +
             "1 = full health, 0.65 = 65%, 0.3 = 30%. The first phase should be 1.")]
    [SerializeField, Range(0f, 1f)] private float startsAtHealthFraction = 1f;

    [Header("Defence")]
    [Tooltip("ON = the sword can't hurt the boss during this phase.")]
    [SerializeField] private bool invulnerable;

    [Header("Moves")]
    [Tooltip("The attacks the boss picks from during this phase. Drag Boss Move assets in here.")]
    [SerializeField] private BossMove[] moves;
    [Tooltip("ON = random move each time (never the same one twice in a row). OFF = in list order.")]
    [SerializeField] private bool randomOrder = true;
    [Tooltip("Extra pause between moves, on top of each move's own recovery time.")]
    [SerializeField, Min(0f)] private float secondsBetweenMoves = 1f;

    [Header("Enemy Waves")]
    [SerializeField] private BossWaveSettings waves = new BossWaveSettings();

    [Header("Arena Level (puzzle / platformer phase)")]
    [Tooltip("Optional. A level prefab you made; it is spawned when this phase starts. " +
             "Put the memories (MemorySmall) and your hand-placed enemies inside it.")]
    [SerializeField] private GameObject arenaLevelPrefab;
    [Tooltip("ON = the boss's health follows the memories you collect (full at the start of this phase, " +
             "empty when the last one is collected) and the boss dies at 0. " +
             "Needs a LevelMemoryTracker whose Small Memories Required is the number of memories.")]
    [SerializeField] private bool healthFollowsMemories;

    public float StartsAtHealthFraction => startsAtHealthFraction;
    public bool Invulnerable => invulnerable;
    public BossMove[] Moves => moves;
    public bool RandomOrder => randomOrder;
    public float SecondsBetweenMoves => secondsBetweenMoves;
    public BossWaveSettings Waves => waves;
    public GameObject ArenaLevelPrefab => arenaLevelPrefab;
    public bool HealthFollowsMemories => healthFollowsMemories;
}
