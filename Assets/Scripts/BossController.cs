using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The boss's brain. It runs the fight but contains no attack code and no numbers: those live in the
/// BossPhaseData assets (what each phase does) and the BossMove assets (how each attack works).
///
/// The fight, as set up in the phase assets:
///  Phase 1 and 2 (by health): the boss attacks and spawns waves of enemies.
///  Phase 3 (by health): the ARENA. The boss stops, the player is moved into your pre-made arena level.
///     When all memories are collected the arena despawns and the player returns to the boss area.
///  Phase 4 (starts when the arena ends): the FINALE. The boss stands still and throws projectiles and
///     enemies. One sword slash or stomp defeats it.
///
/// What it does:
///  1) Watches BossHealth and switches phase when health crosses a phase's threshold.
///  2) In a loop: picks a move from the current phase, performs it, rests, repeats.
///  3) Spawns waves of enemies (placed, or thrown at the player) if the current phase asks for it.
///  4) Runs the arena: spawns it, moves the player in, and moves them out when the memories are done.
///
/// On the boss object you also need: CharacterController and BossHealth (added automatically).
/// Optional: a Stompable component. It is switched on only during the finale.
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
    [Tooltip("Optional. Put this on the boss. It can only be stomped during the finale.")]
    [SerializeField] private Stompable stompable;

    [Header("Phases (in order; the first one starts the fight)")]
    [SerializeField] private BossPhaseData[] phases;

    [Header("Fight")]
    [SerializeField, Min(0f)] private float startDelaySeconds = 2f;
    [SerializeField, Min(0f)] private float turnSpeedDegreesPerSecond = 180f;
    [SerializeField, Min(0f)] private float gravity = 20f;

    [Header("Arena")]
    [Tooltip("Where the arena level appears. Put it FAR from the boss area so it feels like a different place " +
             "(e.g. 200 meters away). Empty = at the boss's position.")]
    [SerializeField] private Transform arenaSpawnPoint;
    [Tooltip("Where the player appears when the arena is finished (e.g. at the start of the path to the boss). " +
             "Empty = back where they were before they entered the arena.")]
    [SerializeField] private Transform returnFromArenaPoint;
    [Tooltip("After the last memory is collected, wait this long before the arena despawns (so its sound and effects can play).")]
    [SerializeField, Min(0f)] private float leaveArenaDelaySeconds = 1.5f;

    [Header("Enemy Spawning")]
    [Tooltip("Optional. Enemies appear at one of these points at random. Empty = in a ring around the boss. " +
             "Ignored when a phase throws its enemies.")]
    [SerializeField] private Transform[] enemySpawnPoints;
    [SerializeField, Min(0f)] private float ringSpawnRadiusMeters = 2f;

    [Header("Defeat")]
    [SerializeField] private bool destroyEnemiesOnDefeat = true;
    [Tooltip("Seconds the boss stays around after being defeated (for a death animation or effect).")]
    [SerializeField, Min(0f)] private float destroyBossAfterSeconds = 3f;

    [Header("Events (hook environment / UI / sound / fades here)")]
    [Tooltip("Element 0 fires when the first phase starts, element 1 for the second phase, and so on. " +
             "Use them to change the level per phase (lights, environment, music, spawn points...).")]
    [SerializeField] private UnityEvent[] onPhaseEntered;
    [Tooltip("Fires right after the player is moved into the arena. Later: fade in.")]
    [SerializeField] private UnityEvent onArenaEntered;
    [Tooltip("Fires right after the arena despawned and the player is back. Later: fade in / a comic.")]
    [SerializeField] private UnityEvent onArenaCompleted;
    [Tooltip("Fires when the boss dies. Hook up the boss level's Memory Main here (SetActive true).")]
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
    private bool arenaFinishing;

    private bool hasPositionBeforeArena;
    private Vector3 positionBeforeArena;
    private Quaternion rotationBeforeArena;

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
        if (stompable == null) TryGetComponent(out stompable);
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
            // First by the 'Player' tag; if that fails, by the object that has PlayerMotor.
            GameObject tagged = GameObject.FindGameObjectWithTag("Player");
            if (tagged != null)
            {
                player = tagged.transform;
            }
            else
            {
                PlayerMotor motor = FindFirstObjectByType<PlayerMotor>();
                if (motor != null) player = motor.transform;
                else Debug.LogWarning($"{name}: can't find the player. Is it in this scene (in the Hierarchy) and active?", this);
            }
        }

        health.HealthFractionChanged += OnHealthFractionChanged;

        EnterPhase(0);
        StartCoroutine(MoveLoop());
    }

    private void OnDestroy()
    {
        if (health != null) health.HealthFractionChanged -= OnHealthFractionChanged;
        StopListeningToMemories();
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

        // Find the LAST health-triggered phase whose threshold we have reached. Phases only move forward.
        // Phases that are not started by health (the finale) are skipped: they start another way.
        int target = phaseIndex;
        for (int i = phases.Length - 1; i > phaseIndex; i--)
        {
            if (!phases[i].StartedByHealth) continue;

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

        // The sword can't push health below the next HEALTH-triggered phase's threshold, so none is skipped.
        health.IsInvulnerable = currentPhase.Invulnerable;
        health.OneHitKill = currentPhase.IsFinisher;
        health.DamageFloorFraction = NextHealthThreshold(index);

        // Only the finale can be stomped.
        if (stompable != null) stompable.enabled = currentPhase.IsFinisher;

        pendingSpawns = 0;
        spawnSecondsRemaining = 0f;
        waveSecondsRemaining = currentPhase.Waves.FirstWaveDelaySeconds;

        if (currentPhase.ClearEnemiesOnStart) ClearEnemies();
        if (currentPhase.ArenaLevelPrefab != null && spawnedArena == null) EnterArena(currentPhase);

        if (onPhaseEntered != null && index < onPhaseEntered.Length) onPhaseEntered[index]?.Invoke();
        PhaseChanged?.Invoke(index);
    }

    /// <summary>The health fraction of the next phase that starts from health (0 if there is none).</summary>
    private float NextHealthThreshold(int index)
    {
        for (int i = index + 1; i < phases.Length; i++)
        {
            if (phases[i].StartedByHealth) return phases[i].StartsAtHealthFraction;
        }
        return 0f;
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
                yield return null;   // this phase has no moves (the arena phase): look again next frame
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

        bool thrown = waves.SpawnStyle == EnemySpawnStyle.ThrownTowardPlayer && player != null;

        Vector3 position;
        if (thrown)
        {
            position = transform.TransformPoint(waves.ThrowOriginLocalOffset);
        }
        else if (enemySpawnPoints != null && enemySpawnPoints.Length > 0)
        {
            Transform point = enemySpawnPoints[Random.Range(0, enemySpawnPoints.Length)];
            position = point != null ? point.position : transform.position;
            position += Vector3.up * 0.05f;   // a tiny lift so it doesn't start inside the floor
        }
        else
        {
            Quaternion around = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            position = transform.position + around * Vector3.forward * ringSpawnRadiusMeters + Vector3.up * 0.05f;
        }

        Quaternion rotation = Quaternion.LookRotation(DirectionToPlayerFlat(), Vector3.up);
        GameObject enemy = Instantiate(prefab, position, rotation);
        spawnedEnemies.Add(enemy);

        if (thrown) StartCoroutine(ThrowEnemy(enemy, PickLandingPoint(waves), waves.ThrowFlightSeconds));
    }

    /// <summary>A random point on the ground around the player (between the min and max distance).</summary>
    private Vector3 PickLandingPoint(BossWaveSettings waves)
    {
        Vector2 direction = Random.insideUnitCircle.normalized;
        float distance = Random.Range(waves.LandMinDistanceFromPlayerMeters, waves.LandMaxDistanceFromPlayerMeters);
        return player.position + new Vector3(direction.x, 0f, direction.y) * distance;
    }

    /// <summary>
    /// Lobs an enemy along a curved path (a "ballistic arc") so it lands on 'target' after 'flightSeconds'.
    /// While it flies its EnemyAI is switched off; when it lands the AI turns on and treats the landing spot as home.
    /// The maths: with gravity g, to cover a distance d in time T you launch with velocity d / T horizontally
    /// and (height difference / T + g * T / 2) vertically.
    /// </summary>
    private IEnumerator ThrowEnemy(GameObject enemy, Vector3 target, float flightSeconds)
    {
        if (enemy == null) yield break;

        EnemyAI ai = enemy.GetComponent<EnemyAI>();
        CharacterController enemyController = enemy.GetComponent<CharacterController>();
        if (ai != null) ai.enabled = false;

        Vector3 velocity = (target - enemy.transform.position) / flightSeconds
                           + Vector3.up * (0.5f * gravity * flightSeconds);
        float elapsed = 0f;

        while (enemy != null && elapsed < flightSeconds)
        {
            float deltaTime = Time.deltaTime;
            velocity.y -= gravity * deltaTime;

            if (enemyController != null) enemyController.Move(velocity * deltaTime);
            else enemy.transform.position += velocity * deltaTime;

            elapsed += deltaTime;

            // Touched a floor on the way down (a platform, a ledge): land early.
            if (enemyController != null && enemyController.isGrounded && velocity.y < 0f) break;

            yield return null;
        }

        if (enemy == null || ai == null) yield break;

        ai.SetHomePosition(enemy.transform.position);   // patrol and chase from where it landed
        ai.enabled = true;
    }

    private void ClearEnemies()
    {
        foreach (GameObject enemy in spawnedEnemies)
        {
            if (enemy != null) Destroy(enemy);
        }
        spawnedEnemies.Clear();
    }

    // ---- Arena ---------------------------------------------------------

    private void EnterArena(BossPhaseData phase)
    {
        Vector3 position = arenaSpawnPoint != null ? arenaSpawnPoint.position : transform.position;
        Quaternion rotation = arenaSpawnPoint != null ? arenaSpawnPoint.rotation : Quaternion.identity;

        // Remember where the player was, in case no "return" point is set.
        if (player != null)
        {
            Transform root = PlayerRoot();
            positionBeforeArena = root.position;
            rotationBeforeArena = root.rotation;
            hasPositionBeforeArena = true;
        }

        spawnedArena = Instantiate(phase.ArenaLevelPrefab, position, rotation);

        ArenaLevel arena = spawnedArena.GetComponentInChildren<ArenaLevel>();
        if (arena != null && arena.PlayerSpawnPoint != null)
            TeleportPlayer(arena.PlayerSpawnPoint.position, arena.PlayerSpawnPoint.rotation);
        else
            Debug.LogWarning($"{name}: the arena prefab has no ArenaLevel component with a Player Spawn Point, " +
                             "so the player was not moved into it.", this);

        if (phase.EndsWhenMemoriesComplete) ListenToMemories();
        onArenaEntered?.Invoke();
    }

    private void ListenToMemories()
    {
        LevelMemoryTracker tracker = LevelMemoryTracker.Current;
        if (tracker == null)
        {
            Debug.LogWarning($"{name}: the arena waits for memories but there is no LevelMemoryTracker in the scene.", this);
            return;
        }

        if (listeningTracker == tracker) return;

        StopListeningToMemories();
        listeningTracker = tracker;
        listeningTracker.SmallCountChanged += OnMemoryCountChanged;
    }

    private void StopListeningToMemories()
    {
        if (listeningTracker == null) return;

        listeningTracker.SmallCountChanged -= OnMemoryCountChanged;
        listeningTracker = null;
    }

    private void OnMemoryCountChanged(int collected, int required)
    {
        if (defeated || arenaFinishing || currentPhase == null || !currentPhase.EndsWhenMemoriesComplete) return;
        if (collected < required) return;

        StartCoroutine(FinishArena());
    }

    private IEnumerator FinishArena()
    {
        arenaFinishing = true;
        StopListeningToMemories();

        // A short pause so the last memory's sound and effects can play out.
        yield return new WaitForSeconds(leaveArenaDelaySeconds);
        if (defeated) yield break;

        if (spawnedArena != null) Destroy(spawnedArena);
        spawnedArena = null;

        if (returnFromArenaPoint != null)
            TeleportPlayer(returnFromArenaPoint.position, returnFromArenaPoint.rotation);
        else if (hasPositionBeforeArena)
            TeleportPlayer(positionBeforeArena, rotationBeforeArena);

        onArenaCompleted?.Invoke();
        arenaFinishing = false;

        if (phaseIndex + 1 < phases.Length)
            EnterPhase(phaseIndex + 1);   // the finale
        else
            Debug.LogWarning($"{name}: the arena is done but there is no phase after it. Add a finale phase.", this);
    }

    // ---- Moving the player ---------------------------------------------

    /// <summary>The object that actually moves (the one with PlayerMotor), in case the "Player" tag is on a child.</summary>
    private Transform PlayerRoot()
    {
        PlayerMotor motor = player.GetComponentInParent<PlayerMotor>();
        return motor != null ? motor.transform : player;
    }

    /// <summary>
    /// Instantly moves the player. A CharacterController fights direct position changes, so it is switched off
    /// for a moment, and the motor's leftover speed is cleared so the player doesn't keep sliding or falling.
    /// </summary>
    private void TeleportPlayer(Vector3 position, Quaternion rotation)
    {
        if (player == null) return;

        Transform root = PlayerRoot();
        CharacterController playerController = root.GetComponent<CharacterController>();

        if (playerController != null) playerController.enabled = false;
        root.SetPositionAndRotation(position, rotation);
        if (playerController != null) playerController.enabled = true;

        if (root.TryGetComponent(out PlayerMotor motor))
        {
            motor.HorizontalVelocity = Vector3.zero;
            motor.VerticalVelocity = 0f;
        }
    }

    // ---- Defeat --------------------------------------------------------

    private void Defeat()
    {
        if (defeated) return;

        defeated = true;
        StopAllCoroutines();
        StopListeningToMemories();
        pendingSpawns = 0;
        health.IsInvulnerable = true;
        if (stompable != null) stompable.enabled = false;
        Debug.Log($"{name}: defeated!", this);

        if (destroyEnemiesOnDefeat) ClearEnemies();
        if (spawnedArena != null) Destroy(spawnedArena);

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

        if (returnFromArenaPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(returnFromArenaPoint.position, 0.12f);
        }
    }
}
