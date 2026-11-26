using System.Collections;         // gives us IEnumerator, for coroutines (code that runs over several frames)
using System.Collections.Generic; // gives us List
using UnityEngine;                // gives us access to Unity classes (MonoBehaviour, CharacterController, etc.)
using UnityEngine.Events;         // gives us UnityEvent (the event lists you can fill in the Inspector)

// BOSS CONTROLLER
// The boss's brain. It runs the fight but contains no attack code and no numbers: those live in the
// BossPhaseData assets (what each phase does) and the BossMove assets (how each attack works).
//
// The fight, as set up in the phase assets:
//   Phase 1 and 2 (by health): the boss attacks and spawns waves of enemies.
//   Phase 3 (by health): the ARENA. The boss stops, the player is moved into your pre-made arena level.
//      When all memories are collected the arena despawns and the player returns to the boss area.
//   Phase 4 (starts when the arena ends): the FINALE. The boss stands still. One sword slash or stomp defeats it.
//
// What it does:
//   1. Listens to BossHealth and switches phase when health crosses a phase's threshold.
//   2. In a loop: picks a move from the current phase, performs it, rests, repeats.
//   3. Spawns waves of enemies (placed, or thrown at the player) if the current phase asks for it.
//   4. Runs the arena: spawns it, moves the player in, and moves them out when the memories are done.
//
// On the boss object you also need: CharacterController and BossHealth (added automatically).
// Optional: a Stompable component. It is switched on only during the finale.
// The boss's pivot should be at its feet, like the player's.

[RequireComponent(typeof(CharacterController))] // Unity adds these automatically if the object has none
[RequireComponent(typeof(BossHealth))]
public class BossController : MonoBehaviour // MonoBehaviour = a script Unity can put on an object and call every frame
{
    private const float PhaseThresholdEpsilon = 0.0001f;   // a tiny extra, so 0.65 health counts as "reached 0.65"
    private const float GroundStickSpeed = 1f;             // small push down while standing, so it stays on slopes
    private const float MaxFallSpeedMetersPerSecond = 10f; // never falls faster than this
    private const float DirectionEpsilonSqr = 0.0001f;     // shorter than this (squared) = no direction
    private const float WaveRecheckSeconds = 0.5f;         // too many enemies alive: look again after this long

    [Header("References")]
    [SerializeField] private BossHealth health;
    [Tooltip("Who to fight. Empty = the player of the scene (PlayerController).")]
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

    private List<GameObject> spawnedEnemies = new List<GameObject>(); // the enemies this boss spawned (some may be dead = null)

    private CharacterController controller;   // the boss's body (found in Awake)
    private IDamageable playerHealth;         // what the boss hurts (found once in Start)
    private BossPhaseData currentPhase;       // the phase running now
    private LevelMemoryTracker listeningTracker; // the memory tracker we listen to during the arena (null = none)
    private GameObject spawnedArena;          // the arena level while it exists
    private BossMove lastMove;                // so a random pick doesn't repeat the same move twice

    private int phaseIndex = -1;          // which phase is running (-1 = none yet)
    private int moveIndex = -1;           // in-order moves: the last one used
    private int pendingSpawns;            // enemies of the current wave still to spawn
    private float waveSecondsRemaining;   // time until the next wave
    private float spawnSecondsRemaining;  // time until the next enemy of this wave
    private float verticalVelocity;       // up/down speed (gravity)
    private bool defeated;                // true after Defeat()
    private bool arenaFinishing;          // true while FinishArena is waiting to send the player back

    private bool hasPositionBeforeArena;  // true once we saved where the player was before the arena
    private Vector3 positionBeforeArena;
    private Quaternion rotationBeforeArena;

    // A "shout" with the new phase index when a phase starts (a UI could show the phase name).
    public event System.Action<int> PhaseChanged;

    // ---- Read-only values the moves and other scripts use -------------------

    public Transform Player { get { return player; } }
    public bool IsDefeated { get { return defeated; } }
    public int PhaseIndex { get { return phaseIndex; } }
    public BossPhaseData CurrentPhase { get { return currentPhase; } }

    // The point under the boss's feet.
    public Vector3 FeetPosition
    {
        get { return new Vector3(transform.position.x, controller.bounds.min.y, transform.position.z); }
    }

    // How wide the boss is (from its middle to its side).
    public float BodyRadius
    {
        get { return Mathf.Max(controller.bounds.extents.x, controller.bounds.extents.z); }
    }

    // How high the player's feet are above the boss's feet (0 if there is no player).
    public float PlayerFeetHeightAboveBoss
    {
        get
        {
            if (player == null) return 0f;
            return player.position.y - controller.bounds.min.y;
        }
    }

    // ---- Helpers the moves use ------------------------------------------------

    // The distance to the player, ignoring height.
    public float FlatDistanceToPlayer()
    {
        if (player == null) return float.MaxValue; // no player: "infinitely far"

        Vector3 offset = player.position - transform.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    // The flat direction (length 1) toward the player. No player, or standing on them: the boss's own forward.
    public Vector3 DirectionToPlayerFlat()
    {
        if (player == null) return transform.forward;

        Vector3 offset = player.position - transform.position;
        offset.y = 0f;
        if (offset.sqrMagnitude > DirectionEpsilonSqr) return offset.normalized;
        return transform.forward;
    }

    // Turns the boss a little toward the player (called every frame during a wind-up).
    public void TurnTowardsPlayer()
    {
        Vector3 direction = DirectionToPlayerFlat();
        Quaternion target = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeedDegreesPerSecond * Time.deltaTime);
    }

    // Moves the boss by this much this frame, with collisions (walls stop it).
    public void Move(Vector3 displacement)
    {
        controller.Move(displacement);
    }

    // True if the player's body is touching the boss's body (plus a little padding).
    public bool IsPlayerTouching(float paddingMeters)
    {
        if (player == null) return false;

        bool closeEnough = FlatDistanceToPlayer() <= BodyRadius + paddingMeters;
        bool belowBossTop = PlayerFeetHeightAboveBoss < controller.bounds.size.y; // not standing on top of it
        return closeEnough && belowBossTop;
    }

    // Hurts the player (used by the moves).
    public void DamagePlayer(float amount, Vector3 direction)
    {
        if (playerHealth != null) playerHealth.TakeDamage(amount, direction);
    }

    // Spawns an effect prefab (does nothing if the prefab is empty) and removes it after 'lifetimeSeconds'.
    public GameObject SpawnEffect(GameObject prefab, Vector3 position, float lifetimeSeconds)
    {
        if (prefab == null) return null;

        GameObject effect = Instantiate(prefab, position, Quaternion.identity);
        if (lifetimeSeconds > 0f) Destroy(effect, lifetimeSeconds);
        return effect;
    }

    // ---- Setup --------------------------------------------------------------

    // Awake runs once, when the object is created.
    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (health == null) health = GetComponent<BossHealth>();       // not dragged in: the one on this object
        if (stompable == null) stompable = GetComponent<Stompable>(); // optional, may stay empty
    }

    // Start runs once, after every Awake (so the player has set PlayerController.Current by now).
    private void Start()
    {
        if (phases == null || phases.Length == 0)
        {
            Debug.LogError(name + ": no phases assigned. Create BossPhaseData assets and add them to Phases.", this);
            enabled = false; // nothing to run
            return;
        }

        if (player == null && PlayerController.Current != null) player = PlayerController.Current.transform; // not dragged in: the scene's player
        if (player != null)
        {
            PlayerController root = player.GetComponentInParent<PlayerController>(); // a child was dragged in? use the object that really moves
            if (root != null) player = root.transform;
        }
        if (player == null) Debug.LogWarning(name + ": can't find the player. Is it in this scene (in the Hierarchy) and active?", this);
        else playerHealth = player.GetComponentInChildren<IDamageable>(); // found once here, used by DamagePlayer

        health.HealthFractionChanged += OnHealthFractionChanged; // listen to the boss's health

        EnterPhase(0);
        StartCoroutine(MoveLoop());
    }

    // OnDestroy runs when the boss goes away: stop listening, so the events don't point at a destroyed boss.
    private void OnDestroy()
    {
        if (health != null) health.HealthFractionChanged -= OnHealthFractionChanged;
        StopListeningToMemories();
    }

    // Update runs every frame: gravity and enemy waves. The attacks run in MoveLoop.
    private void Update()
    {
        if (defeated) return;

        float deltaTime = Time.deltaTime;
        ApplyGravity(deltaTime);
        UpdateWaves(deltaTime);
    }

    // Keeps the boss on the ground.
    private void ApplyGravity(float deltaTime)
    {
        if (controller.isGrounded && verticalVelocity <= 0f)
        {
            verticalVelocity = -GroundStickSpeed; // standing: a small push down
        }
        else
        {
            verticalVelocity = Mathf.Max(verticalVelocity - gravity * deltaTime, -MaxFallSpeedMetersPerSecond); // falling
        }

        controller.Move(Vector3.up * (verticalVelocity * deltaTime));
    }

    // ---- Phases ---------------------------------------------------------------

    // BossHealth calls this every time the health changes.
    private void OnHealthFractionChanged(float fraction)
    {
        if (defeated) return;

        if (fraction <= 0f) // no health left
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

    // Starts a phase: sets the boss's defence, the waves and the arena, and fires the phase's event.
    private void EnterPhase(int index)
    {
        phaseIndex = index;
        currentPhase = phases[index];
        moveIndex = -1;
        if (Application.isEditor) Debug.Log(name + ": phase " + (index + 1) + " (" + currentPhase.name + ") started.", this);

        // Hits can't push health below the next HEALTH-triggered phase's threshold, so none is skipped.
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

        if (onPhaseEntered != null && index < onPhaseEntered.Length && onPhaseEntered[index] != null) onPhaseEntered[index].Invoke();
        if (PhaseChanged != null) PhaseChanged(index);
    }

    // The health fraction of the next phase that starts from health (0 if there is none).
    private float NextHealthThreshold(int index)
    {
        for (int i = index + 1; i < phases.Length; i++)
        {
            if (phases[i].StartedByHealth) return phases[i].StartsAtHealthFraction;
        }
        return 0f;
    }

    // ---- Moves ----------------------------------------------------------------

    // The attack loop: wait, pick a move, do it, rest, repeat. Runs as a coroutine until the boss is defeated.
    private IEnumerator MoveLoop()
    {
        yield return new WaitForSeconds(startDelaySeconds);

        while (!defeated)
        {
            BossMove move = PickMove();
            if (move == null)
            {
                yield return null; // this phase has no moves (the arena, the finale): look again next frame
                continue;
            }

            yield return StartCoroutine(move.Perform(this)); // do the move and wait until it is finished
            yield return new WaitForSeconds(move.RecoverySeconds + currentPhase.SecondsBetweenMoves); // the player's chance to hit back
        }
    }

    // Picks the next move of the current phase (null = the phase has none).
    private BossMove PickMove()
    {
        BossMove[] moves = currentPhase.Moves;
        if (moves == null || moves.Length == 0) return null;

        BossMove chosen;
        if (currentPhase.RandomOrder)
        {
            chosen = moves[Random.Range(0, moves.Length)];
            for (int tries = 0; tries < 4 && chosen == lastMove && moves.Length > 1; tries++)
            {
                chosen = moves[Random.Range(0, moves.Length)]; // avoid the same move twice in a row
            }
        }
        else
        {
            moveIndex = (moveIndex + 1) % moves.Length; // the next one in the list, back to the start after the last
            chosen = moves[moveIndex];
        }

        lastMove = chosen;
        return chosen;
    }

    // ---- Enemy waves ----------------------------------------------------------

    // Counts down to the next wave and spawns its enemies one by one.
    private void UpdateWaves(float deltaTime)
    {
        BossWaveSettings waves = currentPhase.Waves;
        if (!waves.SpawnWaves || waves.EnemyPrefabs == null || waves.EnemyPrefabs.Length == 0) return; // this phase has no waves

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
        if (waveSecondsRemaining > 0f) return; // not time for a wave yet

        RemoveDeadEnemies();
        int room = waves.MaxAliveEnemies - spawnedEnemies.Count; // how many more may be alive at once
        if (room <= 0)
        {
            waveSecondsRemaining = WaveRecheckSeconds; // too many alive, check again soon
            return;
        }

        pendingSpawns = Mathf.Min(waves.EnemiesPerWave, room);
        spawnSecondsRemaining = 0f;
        waveSecondsRemaining = waves.SecondsBetweenWaves;
    }

    // Forgets the enemies that died (a destroyed object counts as null).
    private void RemoveDeadEnemies()
    {
        for (int i = spawnedEnemies.Count - 1; i >= 0; i--) // backwards, so removing doesn't skip the next one
        {
            if (spawnedEnemies[i] == null) spawnedEnemies.RemoveAt(i);
        }
    }

    // Spawns one enemy: at a spawn point, in a ring around the boss, or thrown toward the player.
    private void SpawnEnemy(BossWaveSettings waves)
    {
        GameObject prefab = waves.EnemyPrefabs[Random.Range(0, waves.EnemyPrefabs.Length)];
        if (prefab == null) return;

        bool thrown = waves.SpawnStyle == EnemySpawnStyle.ThrownTowardPlayer && player != null;

        Vector3 position;
        if (thrown)
        {
            position = transform.TransformPoint(waves.ThrowOriginLocalOffset); // from the boss's hands
        }
        else if (enemySpawnPoints != null && enemySpawnPoints.Length > 0)
        {
            Transform point = enemySpawnPoints[Random.Range(0, enemySpawnPoints.Length)];
            if (point != null) position = point.position;
            else position = transform.position;
            position += Vector3.up * 0.05f; // a tiny lift so it doesn't start inside the floor
        }
        else
        {
            Quaternion around = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f); // a random direction around the boss
            position = transform.position + around * Vector3.forward * ringSpawnRadiusMeters + Vector3.up * 0.05f;
        }

        Quaternion rotation = Quaternion.LookRotation(DirectionToPlayerFlat(), Vector3.up);
        GameObject enemy = Instantiate(prefab, position, rotation);
        spawnedEnemies.Add(enemy);

        if (thrown) StartCoroutine(ThrowEnemy(enemy, PickLandingPoint(waves), waves.ThrowFlightSeconds));
    }

    // A random point on the ground around the player (between the min and max distance).
    private Vector3 PickLandingPoint(BossWaveSettings waves)
    {
        Vector2 direction = Random.insideUnitCircle.normalized;
        float distance = Random.Range(waves.LandMinDistanceFromPlayerMeters, waves.LandMaxDistanceFromPlayerMeters);
        return player.position + new Vector3(direction.x, 0f, direction.y) * distance;
    }

    // Lobs an enemy along a curved path (a "ballistic arc") so it lands on 'target' after 'flightSeconds'.
    // While it flies its EnemyAI is switched off; when it lands the AI turns on and treats the landing spot as home.
    // The maths: with gravity g, to cover a distance d in time T you launch with velocity d / T sideways
    // and (height difference / T + g * T / 2) upward.
    private IEnumerator ThrowEnemy(GameObject enemy, Vector3 target, float flightSeconds)
    {
        if (enemy == null) yield break;

        EnemyAI ai = enemy.GetComponent<EnemyAI>();
        CharacterController enemyController = enemy.GetComponent<CharacterController>();
        if (ai != null) ai.enabled = false; // no walking while flying

        Vector3 velocity = (target - enemy.transform.position) / flightSeconds + Vector3.up * (0.5f * gravity * flightSeconds);
        float elapsed = 0f;

        while (enemy != null && elapsed < flightSeconds)
        {
            float deltaTime = Time.deltaTime;
            velocity.y -= gravity * deltaTime; // gravity bends the path

            if (enemyController != null) enemyController.Move(velocity * deltaTime);
            else enemy.transform.position += velocity * deltaTime;

            elapsed += deltaTime;

            // Touched a floor on the way down (a platform, a ledge): land early.
            if (enemyController != null && enemyController.isGrounded && velocity.y < 0f) break;

            yield return null;
        }

        if (enemy == null || ai == null) yield break; // it died in the air, or has no AI

        ai.SetHomePosition(enemy.transform.position); // patrol and chase from where it landed
        ai.enabled = true;
    }

    // Removes every enemy this boss spawned.
    private void ClearEnemies()
    {
        for (int i = 0; i < spawnedEnemies.Count; i++)
        {
            if (spawnedEnemies[i] != null) Destroy(spawnedEnemies[i]);
        }
        spawnedEnemies.Clear();
    }

    // ---- Arena ----------------------------------------------------------------

    // Spawns the arena level and moves the player into it.
    private void EnterArena(BossPhaseData phase)
    {
        Vector3 position = transform.position;
        Quaternion rotation = Quaternion.identity;
        if (arenaSpawnPoint != null)
        {
            position = arenaSpawnPoint.position;
            rotation = arenaSpawnPoint.rotation;
        }

        // Remember where the player was, in case no "return" point is set.
        if (player != null)
        {
            positionBeforeArena = player.position;
            rotationBeforeArena = player.rotation;
            hasPositionBeforeArena = true;
        }

        spawnedArena = Instantiate(phase.ArenaLevelPrefab, position, rotation);

        ArenaLevel arena = spawnedArena.GetComponentInChildren<ArenaLevel>();
        if (arena != null && arena.PlayerSpawnPoint != null)
        {
            TeleportPlayer(arena.PlayerSpawnPoint.position, arena.PlayerSpawnPoint.rotation);
        }
        else
        {
            Debug.LogWarning(name + ": the arena prefab has no ArenaLevel component with a Player Spawn Point, " +
                             "so the player was not moved into it.", this);
        }

        if (phase.EndsWhenMemoriesComplete) ListenToMemories();
        if (onArenaEntered != null) onArenaEntered.Invoke();
    }

    // Starts listening to the memory counter, to know when the arena is done.
    private void ListenToMemories()
    {
        LevelMemoryTracker tracker = LevelMemoryTracker.Current;
        if (tracker == null)
        {
            Debug.LogWarning(name + ": the arena waits for memories but there is no LevelMemoryTracker in the scene.", this);
            return;
        }

        if (listeningTracker == tracker) return; // already listening

        StopListeningToMemories();
        listeningTracker = tracker;
        listeningTracker.SmallCountChanged += OnMemoryCountChanged;
    }

    // Stops listening to the memory counter.
    private void StopListeningToMemories()
    {
        if (listeningTracker == null) return;

        listeningTracker.SmallCountChanged -= OnMemoryCountChanged;
        listeningTracker = null;
    }

    // The tracker calls this every time a memory is collected.
    private void OnMemoryCountChanged(int collected, int required)
    {
        if (defeated || arenaFinishing || currentPhase == null || !currentPhase.EndsWhenMemoriesComplete) return;
        if (collected < required) return; // not all of them yet

        StartCoroutine(FinishArena());
    }

    // Waits a moment, removes the arena, sends the player back and starts the next phase (the finale).
    private IEnumerator FinishArena()
    {
        arenaFinishing = true;
        StopListeningToMemories();

        yield return new WaitForSeconds(leaveArenaDelaySeconds); // so the last memory's sound and effects can play out
        if (defeated) yield break;

        if (spawnedArena != null) Destroy(spawnedArena);
        spawnedArena = null;

        if (returnFromArenaPoint != null) TeleportPlayer(returnFromArenaPoint.position, returnFromArenaPoint.rotation);
        else if (hasPositionBeforeArena) TeleportPlayer(positionBeforeArena, rotationBeforeArena);

        if (onArenaCompleted != null) onArenaCompleted.Invoke();
        arenaFinishing = false;

        if (phaseIndex + 1 < phases.Length) EnterPhase(phaseIndex + 1); // the finale
        else Debug.LogWarning(name + ": the arena is done but there is no phase after it. Add a finale phase.", this);
    }

    // ---- Moving the player ----------------------------------------------------

    // Instantly moves the player (PlayerController also clears its leftover speed).
    private void TeleportPlayer(Vector3 position, Quaternion rotation)
    {
        if (player == null) return;

        PlayerController playerController = player.GetComponentInParent<PlayerController>(); // once per teleport, so a search is fine
        if (playerController != null) playerController.Teleport(position, rotation);
        else player.SetPositionAndRotation(position, rotation);
    }

    // ---- Defeat ---------------------------------------------------------------

    // The boss is beaten: stop everything, clean up and fire On Defeated.
    private void Defeat()
    {
        if (defeated) return;

        defeated = true;
        StopAllCoroutines();
        StopListeningToMemories();
        pendingSpawns = 0;
        health.IsInvulnerable = true;
        if (stompable != null) stompable.enabled = false;
        if (Application.isEditor) Debug.Log(name + ": defeated!", this);

        if (destroyEnemiesOnDefeat) ClearEnemies();
        if (spawnedArena != null) Destroy(spawnedArena);

        if (onDefeated != null) onDefeated.Invoke();
        Destroy(gameObject, destroyBossAfterSeconds);
    }

    // Scene view (select the boss): cyan = enemy spawn points (or the ring), magenta = arena, green = return point.
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        if (enemySpawnPoints != null && enemySpawnPoints.Length > 0)
        {
            for (int i = 0; i < enemySpawnPoints.Length; i++)
            {
                if (enemySpawnPoints[i] != null) Gizmos.DrawWireSphere(enemySpawnPoints[i].position, 0.1f);
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
