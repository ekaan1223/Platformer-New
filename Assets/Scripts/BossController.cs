using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The boss's brain. It runs the fight but contains no attack code and no numbers: those live in the
/// BossPhaseData assets (what each phase does) and the BossMove assets (how each attack works).
///
/// What it does:
///  1) Watches BossHealth and switches phase when health crosses a phase's threshold.
///  2) In a loop: picks a move from the current phase, performs it, rests, repeats.
///  3) Spawns waves of enemies if the current phase asks for it.
///  4) Spawns the arena level if the current phase has one, and can make the boss's health follow
///     the memories collected there. Reaching 0 health defeats the boss.
///
/// On the boss object you also need: CharacterController and BossHealth (added automatically).
/// The boss's pivot should be at its feet, like your player's.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(BossHealth))]
public class BossController : MonoBehaviour
{
    private const float PhaseThresholdEpsilon = 0.0001f;
    private const float GroundStickSpeed = 1f;
    private const float MaxFallSpeedMetersPerSecond = 10f;
    private const float DirectionEpsilonSqr = 0.0001f;
    private const float WaveRecheckSeconds = 0.5f;

    [Header("References")]
    [SerializeField] private BossHealth health;
    [Tooltip("Who to fight. Empty = finds the object tagged 'Player'.")]
    [SerializeField] private Transform player;
    [Tooltip("Where the arena level appears. Empty = at the boss's position.")]
    [SerializeField] private Transform arenaSpawnPoint;

    [Header("Phases (in order; the first one starts the fight)")]
    [SerializeField] private BossPhaseData[] phases;

    [Header("Fight")]
    [SerializeField, Min(0f)] private float startDelaySeconds = 2f;
    [SerializeField, Min(0f)] private float turnSpeedDegreesPerSecond = 180f;
    [SerializeField, Min(0f)] private float gravity = 20f;

    [Header("Enemy Spawning")]
    [Tooltip("Optional. Enemies appear at one of these points at random. Empty = in a ring around the boss.")]
    [SerializeField] private Transform[] enemySpawnPoints;
    [SerializeField, Min(0f)] private float ringSpawnRadiusMeters = 2f;

    [Header("Defeat")]
    [SerializeField] private bool destroyEnemiesOnDefeat = true;
    [SerializeField] private bool destroyArenaOnDefeat;
    [Tooltip("Seconds the boss stays around after being defeated (for a death animation or effect).")]
    [SerializeField, Min(0f)] private float destroyBossAfterSeconds = 3f;

    [Header("Events (hook environment / UI / sound here)")]
    [Tooltip("Element 0 fires when the first phase starts, element 1 for the second phase, and so on. " +
             "Use them to change the level per phase (lights, environment, music, spawn points...).")]
    [SerializeField] private UnityEvent[] onPhaseEntered;
    [SerializeField] private UnityEvent onDefeated;

    private readonly List<GameObject> spawnedEnemies = new List<GameObject>();

    private CharacterController controller;
    private BossPhaseData currentPhase;
    private LevelMemoryTracker listeningTracker;
    private GameObject spawnedArena;
    private BossMove lastMove;

    private int phaseIndex = -1;
    private int moveIndex = -1;
    private int pendingSpawns;
    private float waveSecondsRemaining;
    private float spawnSecondsRemaining;
    private float verticalVelocity;
    private bool defeated;

    /// <summary>Raised with the new phase index when a phase starts (a UI could show the phase name).</summary>
    public event System.Action<int> PhaseChanged;

    public Transform Player => player;
    public bool IsDefeated => defeated;
    public int PhaseIndex => phaseIndex;
    public BossPhaseData CurrentPhase => currentPhase;

    // ---- Helpers the moves use -----------------------------------------

    public Vector3 FeetPosition => new Vector3(transform.position.x, controller.bounds.min.y, transform.position.z);
    public float BodyRadius => Mathf.Max(controller.bounds.extents.x, controller.bounds.extents.z);
    public float PlayerFeetHeightAboveBoss => player != null ? player.position.y - controller.bounds.min.y : 0f;

    public float FlatDistanceToPlayer()
    {
        if (player == null) return float.MaxValue;

        Vector3 offset = player.position - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    public Vector3 DirectionToPlayerFlat()
    {
        if (player == null) return transform.forward;

        Vector3 offset = player.position - transform.position;
        offset.y = 0f;
        return offset.sqrMagnitude > DirectionEpsilonSqr ? offset.normalized : transform.forward;
    }

    public void TurnTowardsPlayer()
    {
        Vector3 direction = DirectionToPlayerFlat();
        Quaternion target = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target,
            turnSpeedDegreesPerSecond * Time.deltaTime);
    }

    /// <summary>Move the boss by this much this frame, with collisions (walls stop it).</summary>
    public void Move(Vector3 displacement) => controller.Move(displacement);

    /// <summary>True if the player's body is touching the boss's body (plus a little padding).</summary>
    public bool IsPlayerTouching(float paddingMeters)
    {
        if (player == null) return false;

        bool closeEnough = FlatDistanceToPlayer() <= BodyRadius + paddingMeters;
        bool belowBossTop = PlayerFeetHeightAboveBoss < controller.bounds.size.y;
        return closeEnough && belowBossTop;
    }

    public void DamagePlayer(float amount, Vector3 direction)
    {
        if (player == null) return;

        IDamageable target = player.GetComponentInChildren<IDamageable>();
        if (target != null) target.TakeDamage(amount, direction);
    }

    /// <summary>Spawn an effect prefab (does nothing if the prefab is empty) and remove it after 'lifetimeSeconds'.</summary>
    public GameObject SpawnEffect(GameObject prefab, Vector3 position, float lifetimeSeconds)
    {
        if (prefab == null) return null;

        GameObject effect = Instantiate(prefab, position, Quaternion.identity);
        if (lifetimeSeconds > 0f) Destroy(effect, lifetimeSeconds);
        return effect;
    }

    // ---- Setup ---------------------------------------------------------

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (health == null) TryGetComponent(out health);
    }

    private void Start()
    {
        if (phases == null || phases.Length == 0)
        {
            Debug.LogError($"{name}: no phases assigned. Create BossPhaseData assets and add them to Phases.", this);
            enabled = false;
            return;
        }

        if (player == null)
        {
            GameObject found = GameObject.FindGameObjectWithTag("Player");
            if (found != null) player = found.transform;
            else Debug.LogWarning($"{name}: no object tagged 'Player' found.", this);
        }

        health.HealthFractionChanged += OnHealthFractionChanged;

        EnterPhase(0);
        StartCoroutine(MoveLoop());
    }

    private void OnDestroy()
    {
        if (health != null) health.HealthFractionChanged -= OnHealthFractionChanged;
        if (listeningTracker != null) listeningTracker.SmallCountChanged -= OnMemoryCountChanged;
    }

    private void Update()
    {
        if (defeated) return;

        float deltaTime = Time.deltaTime;
        ApplyGravity(deltaTime);
        UpdateWaves(deltaTime);
    }

    private void ApplyGravity(float deltaTime)
    {
        if (controller.isGrounded && verticalVelocity <= 0f)
            verticalVelocity = -GroundStickSpeed;
        else
            verticalVelocity = Mathf.Max(verticalVelocity - gravity * deltaTime, -MaxFallSpeedMetersPerSecond);

        controller.Move(Vector3.up * (verticalVelocity * deltaTime));
    }

    // ---- Phases --------------------------------------------------------

    private void OnHealthFractionChanged(float fraction)
    {
        if (defeated) return;

        if (fraction <= 0f)
        {
            Defeat();
            return;
        }

        // Find the LAST phase whose threshold we have reached. Phases only ever move forward.
        int target = phaseIndex;
        for (int i = phases.Length - 1; i > phaseIndex; i--)
        {
            if (fraction <= phases[i].StartsAtHealthFraction + PhaseThresholdEpsilon)
            {
                target = i;
                break;
            }
        }

        if (target != phaseIndex) EnterPhase(target);
    }

    private void EnterPhase(int index)
    {
        phaseIndex = index;
        currentPhase = phases[index];
        moveIndex = -1;
        Debug.Log($"{name}: phase {index + 1} ({currentPhase.name}) started.", this);

        // The sword can't push health below the NEXT phase's threshold, so no phase can be skipped.
        health.IsInvulnerable = currentPhase.Invulnerable;
        health.DamageFloorFraction = index + 1 < phases.Length ? phases[index + 1].StartsAtHealthFraction : 0f;

        pendingSpawns = 0;
        spawnSecondsRemaining = 0f;
        waveSecondsRemaining = currentPhase.Waves.FirstWaveDelaySeconds;

        if (currentPhase.ArenaLevelPrefab != null && spawnedArena == null) SpawnArena(currentPhase.ArenaLevelPrefab);
        if (currentPhase.HealthFollowsMemories) ListenToMemories();

        if (onPhaseEntered != null && index < onPhaseEntered.Length) onPhaseEntered[index]?.Invoke();
        PhaseChanged?.Invoke(index);
    }

    // ---- Moves ---------------------------------------------------------

    private IEnumerator MoveLoop()
    {
        yield return new WaitForSeconds(startDelaySeconds);

        while (!defeated)
        {
            BossMove move = PickMove();
            if (move == null)
            {
                yield return null;   // this phase has no moves right now: look again next frame
                continue;
            }

            yield return StartCoroutine(move.Perform(this));
            yield return new WaitForSeconds(move.RecoverySeconds + currentPhase.SecondsBetweenMoves);
        }
    }

    private BossMove PickMove()
    {
        BossMove[] moves = currentPhase.Moves;
        if (moves == null || moves.Length == 0) return null;

        BossMove chosen;
        if (currentPhase.RandomOrder)
        {
            chosen = moves[Random.Range(0, moves.Length)];
            for (int tries = 0; tries < 4 && chosen == lastMove && moves.Length > 1; tries++)
                chosen = moves[Random.Range(0, moves.Length)];   // avoid the same move twice in a row
        }
        else
        {
            moveIndex = (moveIndex + 1) % moves.Length;
            chosen = moves[moveIndex];
        }

        lastMove = chosen;
        return chosen;
    }

    // ---- Enemy waves ---------------------------------------------------

    private void UpdateWaves(float deltaTime)
    {
        BossWaveSettings waves = currentPhase.Waves;
        if (!waves.SpawnWaves || waves.EnemyPrefabs == null || waves.EnemyPrefabs.Length == 0) return;

        // Currently spawning a wave: one enemy at a time, a short gap apart.
        if (pendingSpawns > 0)
        {
            spawnSecondsRemaining -= deltaTime;
            if (spawnSecondsRemaining <= 0f)
            {
                SpawnEnemy(waves);
                pendingSpawns--;
                spawnSecondsRemaining = waves.SpawnStaggerSeconds;
            }
            return;
        }

        waveSecondsRemaining -= deltaTime;
        if (waveSecondsRemaining > 0f) return;

        spawnedEnemies.RemoveAll(enemy => enemy == null);   // forget the ones that died
        int room = waves.MaxAliveEnemies - spawnedEnemies.Count;
        if (room <= 0)
        {
            waveSecondsRemaining = WaveRecheckSeconds;       // too many alive, check again soon
            return;
        }

        pendingSpawns = Mathf.Min(waves.EnemiesPerWave, room);
        spawnSecondsRemaining = 0f;
        waveSecondsRemaining = waves.SecondsBetweenWaves;
    }

    private void SpawnEnemy(BossWaveSettings waves)
    {
        GameObject prefab = waves.EnemyPrefabs[Random.Range(0, waves.EnemyPrefabs.Length)];
        if (prefab == null) return;

        Vector3 position;
        if (enemySpawnPoints != null && enemySpawnPoints.Length > 0)
        {
            Transform point = enemySpawnPoints[Random.Range(0, enemySpawnPoints.Length)];
            position = point != null ? point.position : transform.position;
        }
        else
        {
            Quaternion around = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            position = transform.position + around * Vector3.forward * ringSpawnRadiusMeters;
        }

        position += Vector3.up * 0.05f;   // a tiny lift so it doesn't start inside the floor
        Quaternion rotation = Quaternion.LookRotation(DirectionToPlayerFlat(), Vector3.up);
        spawnedEnemies.Add(Instantiate(prefab, position, rotation));
    }

    // ---- Arena level and memories --------------------------------------

    private void SpawnArena(GameObject prefab)
    {
        Vector3 position = arenaSpawnPoint != null ? arenaSpawnPoint.position : transform.position;
        Quaternion rotation = arenaSpawnPoint != null ? arenaSpawnPoint.rotation : Quaternion.identity;
        spawnedArena = Instantiate(prefab, position, rotation);
    }

    private void ListenToMemories()
    {
        LevelMemoryTracker tracker = LevelMemoryTracker.Current;
        if (tracker == null)
        {
            Debug.LogWarning($"{name}: this phase follows memories but there is no LevelMemoryTracker in the scene.", this);
            return;
        }

        if (listeningTracker == tracker) return;

        listeningTracker = tracker;
        listeningTracker.SmallCountChanged += OnMemoryCountChanged;
    }

    private void OnMemoryCountChanged(int collected, int required)
    {
        if (defeated || currentPhase == null || !currentPhase.HealthFollowsMemories) return;

        // Full health when the phase starts, empty when the last memory is collected.
        float remaining = required > 0 ? 1f - (float)collected / required : 0f;
        health.SetHealthFraction(currentPhase.StartsAtHealthFraction * remaining);   // 0 triggers Defeat()
    }

    // ---- Defeat --------------------------------------------------------

    private void Defeat()
    {
        if (defeated) return;

        defeated = true;
        StopAllCoroutines();
        pendingSpawns = 0;
        health.IsInvulnerable = true;
        Debug.Log($"{name}: defeated!", this);

        if (destroyEnemiesOnDefeat)
        {
            foreach (GameObject enemy in spawnedEnemies)
            {
                if (enemy != null) Destroy(enemy);
            }
            spawnedEnemies.Clear();
        }

        if (destroyArenaOnDefeat && spawnedArena != null) Destroy(spawnedArena);

        onDefeated?.Invoke();
        Destroy(gameObject, destroyBossAfterSeconds);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        if (enemySpawnPoints != null && enemySpawnPoints.Length > 0)
        {
            foreach (Transform point in enemySpawnPoints)
            {
                if (point != null) Gizmos.DrawWireSphere(point.position, 0.1f);
            }
        }
        else
        {
            Gizmos.DrawWireSphere(transform.position, ringSpawnRadiusMeters);
        }

        if (arenaSpawnPoint != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireCube(arenaSpawnPoint.position, Vector3.one * 0.3f);
        }
    }
}
