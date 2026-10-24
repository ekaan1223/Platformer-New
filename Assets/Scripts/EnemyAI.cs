using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Goomba-style enemy: patrols a path, chases the player when it SEES them (view cone + walls block
/// the view), gives up after a while, and hurts the player on touch through IDamageable.
/// Moves itself with a CharacterController, the same way PlayerController moves the player.
///
/// HOW IT MOVES (the "smart" part):
///  Every 0.1 s the enemy looks in 12 directions around the way it WANTS to go and scores each one:
///    - a wall in the way?        -> that direction is out
///    - no ground / a ledge?      -> out
///    - damaging ground ahead?    -> out (see GroundHazard)
///    - another enemy that way?   -> lower score
///  Then it walks in the best direction left. That is why it slides around walls and crates instead of
///  pushing into them, and why it never walks off a platform or into lava.
///  Groups: enemies pass through each other (they can never block one another), push apart gently,
///  and spread out around the player instead of queueing in one line.
///  Stuck: if it is trying to move but isn't getting anywhere, it sidesteps, and gives up if that keeps failing.
///
/// Select the enemy in the Scene view to see the gizmos:
///   GREEN = patrol path        YELLOW = vision cone        RED sphere = chase leash    RED box = touch zone
///   While playing, one short line per direction it is considering:
///     green = free, red = wall, blue = no ground, magenta = damaging ground, WHITE = the one it picked.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class EnemyAI : MonoBehaviour
{
    private const int MaxBufferHits = 8;
    private const int MaxNeighbors = 8;
    private const int DirectionCount = 12;
    private const float DirectionStepDegrees = 360f / DirectionCount;
    private const float MinCandidateAlignment = -0.05f;      // never consider walking away from the goal (90 degrees is the limit)
    private const float MinUsefulAlignment = 0.3f;           // best direction pointing less toward the goal than this = "blocked"
    private const float ArrivalDistanceMeters = 0.08f;       // "close enough" to a patrol point
    private const float DirectionEpsilonSqr = 0.0001f;
    private const float GroundStickSpeed = 1f;
    private const float MaxFallSpeedMetersPerSecond = 10f;
    private const float GroundProbeExtraHeightMeters = 0.05f;
    private const float HazardProbeRadiusMeters = 0.08f;
    private const float WallCheckStepClearanceMeters = 0.02f;
    private const float FanEngageMarginMeters = 0.2f;
    private const float HazardEscapePenalty = 0.5f;
    private const int ConeArcSegments = 12;
    private const float PlayerSearchIntervalSeconds = 0.5f;

    // The enemy is ALWAYS in exactly one of these states.
    private enum State { Patrol, Chase }

    // What the ground looks like at a spot.
    private enum Surface { Safe, NoGround, Hazard }

    // Result of checking one direction (only used to draw the debug gizmos).
    private enum DirectionStatus { NotChecked, Free, Wall, NoGround, Hazard }

    // Shared by ALL enemies in the scene (static = one copy, not one per enemy).
    private static readonly List<EnemyAI> allEnemies = new List<EnemyAI>();
    private static readonly List<EnemyAI> chasers = new List<EnemyAI>();

    // Static data survives into the next Play session if "Enter Play Mode Options" skips domain reload.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        allEnemies.Clear();
        chasers.Clear();
    }

    [Header("References")]
    [Tooltip("Who to hunt. Empty = finds the object tagged 'Player'.")]
    [SerializeField] private Transform player;

    [Header("Patrol Path")]
    [Tooltip("Points the enemy walks to, as OFFSETS (meters) from where you placed it. Not rotated with the enemy.")]
    [SerializeField] private Vector3[] patrolPoints = { new Vector3(1.5f, 0f, 0f), new Vector3(-1.5f, 0f, 0f) };
    [Tooltip("ON = walk there and back. OFF = loop through the points in order.")]
    [SerializeField] private bool pingPong = true;
    [SerializeField, Min(0f)] private float patrolSpeedMetersPerSecond = 0.8f;
    [SerializeField, Min(0f)] private float waitAtPointSeconds = 0.5f;

    [Header("Line of Sight")]
    [Tooltip("How far ahead the enemy can see.")]
    [SerializeField, Min(0f)] private float sightRangeMeters = 2.5f;
    [Tooltip("Width of the view cone. 360 = sees all around.")]
    [SerializeField, Range(1f, 360f)] private float sightAngleDegrees = 120f;
    [Tooltip("Inside this distance it notices you even from behind (0 = off).")]
    [SerializeField, Min(0f)] private float closeAwarenessMeters = 0.35f;
    [Tooltip("Height of the eyes above the enemy's feet.")]
    [SerializeField, Min(0f)] private float eyeHeightMeters = 0.3f;
    [Tooltip("Height of the point on the player it looks at (chest).")]
    [SerializeField, Min(0f)] private float targetHeightMeters = 0.2f;
    [Tooltip("Everything that blocks the view (walls, ground). The Player's layer is added automatically.")]
    [SerializeField] private LayerMask sightBlockers = ~0;

    [Header("Chase (and giving up)")]
    [SerializeField, Min(0f)] private float chaseSpeedMetersPerSecond = 1.8f;
    [Tooltip("Gives up if it hasn't SEEN the player for this long.")]
    [SerializeField, Min(0.1f)] private float loseSightSeconds = 3f;
    [Tooltip("Gives up if it gets this far from where you placed it. Make it bigger than the patrol path.")]
    [SerializeField, Min(0.1f)] private float maxChaseDistanceMeters = 5f;
    [Tooltip("Hard limit: gives up after chasing this long, even if it still sees the player.")]
    [SerializeField, Min(0.1f)] private float maxChaseSeconds = 8f;
    [Tooltip("After giving up it ignores the player for this long, so it can't instantly re-chase.")]
    [SerializeField, Min(0f)] private float giveUpCooldownSeconds = 2f;
    [Tooltip("Stops this close to the player instead of pushing into them. (It also stops as soon as it touches the player.)")]
    [SerializeField, Min(0f)] private float stopDistanceMeters = 0.1f;
    [Tooltip("Gives up if it can't find a way toward the player for this long (for example the player is across a gap).")]
    [SerializeField, Min(0.1f)] private float giveUpWhenBlockedSeconds = 1.5f;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float turnSpeedDegreesPerSecond = 540f;
    [SerializeField, Min(0f)] private float gravity = 20f;

    [Header("Ground & Hazards (what it refuses to walk onto)")]
    [Tooltip("Layers that count as solid ground and walls. The Player and Enemy are ignored even if ticked.")]
    [SerializeField] private LayerMask groundMask = ~0;
    [Tooltip("ON = never walks off a platform edge or into a gap.")]
    [SerializeField] private bool avoidLedges = true;
    [Tooltip("How far ahead of its feet it checks for ground. (It looks a bit further when it is moving fast.)")]
    [SerializeField, Min(0f)] private float ledgeCheckForwardMeters = 0.2f;
    [Tooltip("A drop deeper than this counts as a ledge.")]
    [SerializeField, Min(0f)] private float ledgeCheckDepthMeters = 0.3f;
    [Tooltip("ON = never walks onto ground that has a GroundHazard component (lava, spikes, poison...).")]
    [SerializeField] private bool avoidHazards = true;
    [Tooltip("Layers to search for hazard trigger zones. Leave on Everything unless you have a reason.")]
    [SerializeField] private LayerMask hazardMask = ~0;
    [Tooltip("Safety net: if it falls this far below where it started, it is removed.")]
    [SerializeField, Min(1f)] private float fallDeathDepthMeters = 20f;

    [Header("Smart Steering")]
    [Tooltip("How often it re-checks its surroundings. Lower = smarter but a little slower.")]
    [SerializeField, Range(0.02f, 0.5f)] private float steerIntervalSeconds = 0.1f;
    [Tooltip("How quickly it changes direction once it has decided. Higher = snappier.")]
    [SerializeField, Min(0.1f)] private float steerResponsiveness = 10f;
    [Tooltip("How far ahead of its body it checks for a wall.")]
    [SerializeField, Min(0.01f)] private float wallCheckDistanceMeters = 0.12f;
    [Tooltip("Prefers to keep walking the way it already was. Higher = less zig-zagging.")]
    [SerializeField, Range(0f, 1f)] private float directionStickiness = 0.15f;

    [Header("Groups (many enemies at once)")]
    [Tooltip("Enemies closer than this are noticed and steered around.")]
    [SerializeField, Min(0f)] private float crowdAwarenessRadiusMeters = 0.8f;
    [Tooltip("How strongly it avoids walking into other enemies. 0 = ignores them.")]
    [SerializeField, Min(0f)] private float crowdAvoidanceWeight = 0.8f;
    [Tooltip("Enemies closer than this get gently pushed apart so they don't overlap.")]
    [SerializeField, Min(0f)] private float separationRadiusMeters = 0.4f;
    [Tooltip("How hard enemies push each other apart (0 = off).")]
    [SerializeField, Min(0f)] private float separationStrengthMetersPerSecond = 0.6f;
    [Tooltip("ON = chasing enemies fan out and come at the player from different sides instead of in one line.")]
    [SerializeField] private bool spreadAroundPlayer = true;
    [Tooltip("The ring around the player that enemies aim for before closing in.")]
    [SerializeField, Min(0.1f)] private float fanRadiusMeters = 0.9f;
    [Tooltip("Two chasers within this angle (as seen from the player) push each other apart sideways.")]
    [SerializeField, Range(10f, 180f)] private float fanSpreadAngleDegrees = 70f;
    [Tooltip("The most it will swing sideways around the player to make room for others.")]
    [SerializeField, Range(0f, 90f)] private float fanMaxShiftDegrees = 50f;

    [Header("Getting Unstuck")]
    [Tooltip("Every this many seconds it checks whether it actually moved.")]
    [SerializeField, Min(0.1f)] private float stuckCheckSeconds = 0.5f;
    [Tooltip("It counts as stuck if it moved less than this fraction of the distance it should have.")]
    [SerializeField, Range(0.05f, 0.9f)] private float stuckProgressFraction = 0.25f;
    [Tooltip("How long it sidesteps when it is stuck.")]
    [SerializeField, Min(0.1f)] private float unstickSeconds = 0.5f;
    [Tooltip("After this many stuck checks in a row it gives up (chasing) or skips to the next patrol point.")]
    [SerializeField, Min(1)] private int giveUpAfterStuckCount = 3;

    [Header("Touch Damage")]
    [SerializeField, Min(0f)] private float contactDamage = 1f;
    [Tooltip("Seconds between hits, so one touch can't drain all health in a single frame.")]
    [SerializeField, Min(0f)] private float damageCooldownSeconds = 1f;
    [Tooltip("The player is 'touched' when their body is within this distance of the enemy's body. " +
             "Works for any enemy size. Raise it if hits feel hard to land.")]
    [SerializeField, Min(0f)] private float contactPaddingMeters = 0.08f;
    [Tooltip("Only used if this enemy has a Stompable component. A player whose feet are within this distance " +
             "of the enemy's top (or higher) is landing on its head, so it is a stomp, not a touch.")]
    [SerializeField, Min(0f)] private float topContactToleranceMeters = 0.1f;

    [Header("Events (hook animation / sound / VFX)")]
    [SerializeField] private UnityEvent onPlayerSpotted;
    [SerializeField] private UnityEvent onPlayerLost;
    [SerializeField] private UnityEvent onPlayerHit;

    // Reused buffers (so checking every 0.1 s creates no garbage for the memory cleaner to collect).
    private readonly RaycastHit[] rayBuffer = new RaycastHit[MaxBufferHits];
    private readonly Collider[] hazardBuffer = new Collider[MaxBufferHits];
    private readonly Vector3[] neighborDirections = new Vector3[MaxNeighbors];
    private readonly float[] neighborWeights = new float[MaxNeighbors];
    private readonly Vector3[] candidateDirections = new Vector3[DirectionCount];
    private readonly DirectionStatus[] candidateStatus = new DirectionStatus[DirectionCount];

    private CharacterController controller;
    private Collider playerCollider;
    private Stompable stompable;          // optional: if present, landing on this enemy's head is safe (a stomp)
    private State state = State.Patrol;
    private Vector3 homePosition;         // where the enemy was placed; the patrol path hangs off this
    private int playerMask;
    private int sightMask;

    private int patrolIndex;
    private int patrolStep = 1;
    private float waitSecondsRemaining;

    private float chaseSecondsElapsed;
    private float secondsSinceSeen;
    private float giveUpCooldownRemaining;
    private float damageCooldownRemaining;
    private float verticalVelocity;
    private float playerSearchSecondsRemaining;

    // steering
    private float steerTimer;
    private Vector3 chosenDirection;      // what the last check decided
    private Vector3 currentMoveDirection; // chosenDirection, smoothed
    private Vector3 lastGoalDirection;
    private bool steerBlocked;
    private int chosenIndex = -1;
    private float currentSpeed;
    private float blockedSeconds;

    // getting unstuck
    private float stuckTimer;
    private Vector3 lastCheckPosition;
    private int stuckCount;
    private float unstickSecondsRemaining;
    private Vector3 unstickDirection;

    public bool IsChasing => state == State.Chase;

    /// <summary>
    /// Move this enemy's "home" (where its patrol path and chase leash are measured from).
    /// The boss calls this after throwing an enemy, so it patrols and chases from where it LANDED.
    /// </summary>
    public void SetHomePosition(Vector3 position) => homePosition = position;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        stompable = GetComponent<Stompable>();
        if (stompable == null) stompable = gameObject.AddComponent<Stompable>(); // every enemy can be stomped; PlayerStomp needs this to recognise it
        homePosition = transform.position;
        lastCheckPosition = transform.position;
        steerTimer = Random.value * steerIntervalSeconds;   // enemies don't all do their checks on the same frame

        RegisterEnemy();
    }

    private void Start()
    {
        if (player != null) SetUpPlayerReferences();   // assigned by hand in the Inspector
        else if (!TryFindPlayer())
        {
            Debug.LogWarning($"{name}: can't find the player yet (looked for the 'Player' tag, then for an object with " +
                             "PlayerController). It only patrols for now and keeps looking. Is the player IN THIS SCENE " +
                             "(in the Hierarchy) and active?", this);
        }
    }

    /// <summary>
    /// Finds the player in the scene. First by the 'Player' tag; if that fails, by looking for the object that
    /// has PlayerController. So a wrong or missing tag no longer breaks the enemy. Only ACTIVE objects can be found.
    /// </summary>
    private bool TryFindPlayer()
    {
        Transform found = null;

        GameObject tagged = GameObject.FindGameObjectWithTag("Player");
        if (tagged != null)
        {
            found = tagged.transform;
        }
        else
        {
            PlayerController playerController = FindFirstObjectByType<PlayerController>();
            if (playerController != null) found = playerController.transform;
        }

        if (found == null) return false;

        player = found;
        SetUpPlayerReferences();
        return true;
    }

    /// <summary>Everything that depends on knowing who the player is.</summary>
    private void SetUpPlayerReferences()
    {
        playerMask = 1 << player.gameObject.layer;
        sightMask = sightBlockers | playerMask;

        playerCollider = player.GetComponentInChildren<Collider>();
        if (playerCollider == null)
            Debug.LogWarning($"{name}: the player has no Collider/CharacterController, so it can't be touched.", this);

        if (player.GetComponentInChildren<IDamageable>() == null)
            Debug.LogWarning($"{name}: the player has no Damageable (IDamageable), so touching it will do nothing.", this);
    }

    private void OnDisable() => chasers.Remove(this);

    private void OnDestroy()
    {
        allEnemies.Remove(this);
        chasers.Remove(this);
    }

    /// <summary>
    /// Joins the group list and makes this enemy's body pass through every other enemy's body.
    /// Two solid enemies pushing against each other is how groups get stuck, so they never block each other;
    /// the soft separation push below keeps them from standing inside each other.
    /// (If they still bump: put enemies on their own layer and untick Enemy x Enemy in
    ///  Project Settings > Physics > Layer Collision Matrix.)
    /// </summary>
    private void RegisterEnemy()
    {
        allEnemies.RemoveAll(enemy => enemy == null);

        foreach (EnemyAI other in allEnemies)
            Physics.IgnoreCollision(controller, other.controller, true);

        allEnemies.Add(this);
    }

    // ------------------------------------------------------------------
    // Each frame: 1) decide the state  2) pick a GOAL direction  3) steer safely toward it
    //             4) move  5) hurt the player if touching  6) check we aren't stuck
    // ------------------------------------------------------------------
    private void Update()
    {
        float deltaTime = Time.deltaTime;

        // Safety net: fell out of the world (through a gap, off the map).
        if (transform.position.y < homePosition.y - fallDeathDepthMeters)
        {
            Destroy(gameObject);
            return;
        }

        if (giveUpCooldownRemaining > 0f) giveUpCooldownRemaining -= deltaTime;
        if (damageCooldownRemaining > 0f) damageCooldownRemaining -= deltaTime;

        // No player yet (it spawns later, or was inactive)? Look again twice a second.
        if (player == null)
        {
            playerSearchSecondsRemaining -= deltaTime;
            if (playerSearchSecondsRemaining <= 0f)
            {
                playerSearchSecondsRemaining = PlayerSearchIntervalSeconds;
                TryFindPlayer();
            }
        }

        UpdateState(deltaTime);

        Vector3 goal;          // where it WANTS to go (zero = stand still)
        Vector3 lookDirection; // where its face points
        float speed;

        if (state == State.Chase)
        {
            speed = chaseSpeedMetersPerSecond;
            GetChaseGoal(out goal, out lookDirection);
        }
        else
        {
            speed = patrolSpeedMetersPerSecond;
            goal = GetPatrolGoal(deltaTime);
            lookDirection = Vector3.zero;
        }
        currentSpeed = speed;

        Vector3 moveDirection = Steer(goal, deltaTime);   // where it SAFELY goes
        HandleBlocked(goal, deltaTime);

        if (state == State.Patrol) lookDirection = moveDirection;   // patrolling: face where we walk

        Move(moveDirection * speed + ComputeSeparation(), lookDirection, deltaTime);
        TryDamagePlayer();
        UpdateStuckDetection(goal, speed, deltaTime);
    }

    // ---- 1) State machine ----------------------------------------------

    private void UpdateState(float deltaTime)
    {
        if (state == State.Chase && player == null)   // player got destroyed
        {
            GiveUp();
            return;
        }

        bool canSee = giveUpCooldownRemaining <= 0f && CanSeePlayer();

        if (state == State.Patrol)
        {
            if (canSee && !IsTooFarFromHome()) StartChase();
            return;
        }

        // State.Chase: count how long we've been at it and how long since we last saw them.
        chaseSecondsElapsed += deltaTime;
        secondsSinceSeen = canSee ? 0f : secondsSinceSeen + deltaTime;

        if (secondsSinceSeen >= loseSightSeconds ||
            chaseSecondsElapsed >= maxChaseSeconds ||
            IsTooFarFromHome())
        {
            GiveUp();
        }
    }

    private void StartChase()
    {
        state = State.Chase;
        chaseSecondsElapsed = 0f;
        secondsSinceSeen = 0f;
        blockedSeconds = 0f;
        stuckCount = 0;
        if (!chasers.Contains(this)) chasers.Add(this);
        RequestSteerNow();
        onPlayerSpotted?.Invoke();
    }

    private void GiveUp()
    {
        state = State.Patrol;               // patrol resumes from the point it was heading to
        chasers.Remove(this);
        giveUpCooldownRemaining = giveUpCooldownSeconds;
        waitSecondsRemaining = 0f;
        blockedSeconds = 0f;
        stuckCount = 0;
        RequestSteerNow();
        onPlayerLost?.Invoke();
    }

    private bool IsTooFarFromHome()
    {
        Vector3 offset = transform.position - homePosition;
        offset.y = 0f;
        return offset.magnitude > maxChaseDistanceMeters;
    }

    // ---- Line of sight -------------------------------------------------

    /// <summary>
    /// Three tests, cheapest first:
    /// 1) is the player close enough?  2) are they inside the view cone (or very close)?
    /// 3) shoot a ray at them; the FIRST thing it hits must be the player (a wall in between blocks it).
    /// </summary>
    private bool CanSeePlayer()
    {
        if (player == null) return false;

        Vector3 eye = transform.position + Vector3.up * eyeHeightMeters;
        Vector3 target = player.position + Vector3.up * targetHeightMeters;
        Vector3 toTarget = target - eye;
        float distance = toTarget.magnitude;

        if (distance > sightRangeMeters) return false;
        if (distance < 0.001f) return true;

        bool veryClose = distance <= closeAwarenessMeters;
        if (!veryClose)
        {
            Vector3 flat = toTarget;
            flat.y = 0f;
            if (flat.sqrMagnitude > DirectionEpsilonSqr &&
                Vector3.Angle(transform.forward, flat) > sightAngleDegrees * 0.5f)
            {
                return false;
            }
        }

        int count = Physics.RaycastNonAlloc(eye, toTarget / distance, rayBuffer, distance, sightMask,
            QueryTriggerInteraction.Ignore);

        // Find the closest thing the ray hit, ignoring this enemy's own colliders.
        float nearest = float.PositiveInfinity;
        Collider nearestCollider = null;
        for (int i = 0; i < count; i++)
        {
            Collider hit = rayBuffer[i].collider;
            if (hit.transform.IsChildOf(transform)) continue;
            if (rayBuffer[i].distance < nearest)
            {
                nearest = rayBuffer[i].distance;
                nearestCollider = hit;
            }
        }

        return nearestCollider != null && nearestCollider.transform.IsChildOf(player);
    }

    // ---- 2) Goals: where does it WANT to go? ---------------------------

    private Vector3 GetPatrolGoal(float deltaTime)
    {
        if (patrolPoints == null || patrolPoints.Length == 0) return Vector3.zero;
        if (patrolIndex >= patrolPoints.Length) patrolIndex = 0;

        if (waitSecondsRemaining > 0f)
        {
            waitSecondsRemaining -= deltaTime;
            return Vector3.zero;
        }

        Vector3 toPoint = homePosition + patrolPoints[patrolIndex] - transform.position;
        toPoint.y = 0f;

        if (toPoint.magnitude <= ArrivalDistanceMeters)
        {
            ArriveAtPatrolPoint();
            return Vector3.zero;
        }

        return toPoint.normalized;
    }

    private void ArriveAtPatrolPoint()
    {
        waitSecondsRemaining = waitAtPointSeconds;
        RequestSteerNow();
        if (patrolPoints == null || patrolPoints.Length <= 1) return;

        if (pingPong)
        {
            int next = patrolIndex + patrolStep;
            if (next < 0 || next >= patrolPoints.Length) patrolStep = -patrolStep;   // bounce at the ends
            patrolIndex += patrolStep;
        }
        else
        {
            patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
        }
    }

    private void GetChaseGoal(out Vector3 goal, out Vector3 lookDirection)
    {
        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;

        lookDirection = toPlayer;
        goal = Vector3.zero;

        float distance = toPlayer.magnitude;
        if (distance <= stopDistanceMeters || IsTouchingPlayer()) return;   // already on them: stand and fight

        Vector3 target = player.position;
        if (spreadAroundPlayer && distance > fanRadiusMeters + FanEngageMarginMeters)
            target = GetFannedTarget();

        Vector3 toTarget = target - transform.position;
        toTarget.y = 0f;
        goal = toTarget.sqrMagnitude > DirectionEpsilonSqr ? toTarget.normalized : toPlayer / distance;
    }

    /// <summary>
    /// A spot on a ring around the player, on the side that is LEAST crowded by other chasers.
    /// Each chaser looks at the other chasers' angles around the player and swings the other way, so five
    /// enemies arrive from five different directions instead of one line. Once close, they go straight in.
    /// </summary>
    private Vector3 GetFannedTarget()
    {
        Vector3 fromPlayer = transform.position - player.position;
        fromPlayer.y = 0f;
        if (fromPlayer.sqrMagnitude < DirectionEpsilonSqr) return player.position;

        float shift = 0f;
        for (int i = 0; i < chasers.Count; i++)
        {
            EnemyAI other = chasers[i];
            if (other == null || other == this) continue;

            Vector3 theirs = other.transform.position - player.position;
            theirs.y = 0f;
            if (theirs.sqrMagnitude < DirectionEpsilonSqr) continue;

            float angle = Vector3.SignedAngle(fromPlayer, theirs, Vector3.up);   // where they are, relative to me
            float absAngle = Mathf.Abs(angle);
            if (absAngle >= fanSpreadAngleDegrees) continue;

            // Swing AWAY from them. If we are exactly on top of each other, the instance ID decides who goes which way.
            float away = absAngle < 1f
                ? (GetInstanceID() < other.GetInstanceID() ? 1f : -1f)
                : -Mathf.Sign(angle);

            shift += away * (1f - absAngle / fanSpreadAngleDegrees);   // closer neighbours push harder
        }

        float shiftDegrees = Mathf.Clamp(shift * fanMaxShiftDegrees, -90f, 90f);
        Vector3 fanDirection = Quaternion.AngleAxis(shiftDegrees, Vector3.up) * fromPlayer.normalized;
        return player.position + fanDirection * fanRadiusMeters;
    }

    // ---- 3) Steering: how does it get there SAFELY? --------------------

    private void RequestSteerNow() => steerTimer = 0f;

    private Vector3 Steer(Vector3 goal, float deltaTime)
    {
        goal.y = 0f;

        // Nothing to do: ease to a stop.
        if (goal.sqrMagnitude < DirectionEpsilonSqr)
        {
            chosenDirection = Vector3.zero;
            steerBlocked = false;
            currentMoveDirection = Vector3.MoveTowards(currentMoveDirection, Vector3.zero, steerResponsiveness * deltaTime);
            return currentMoveDirection;
        }

        goal.Normalize();
        lastGoalDirection = goal;

        // Stuck? Sidestep for a moment instead of pushing the same way.
        if (unstickSecondsRemaining > 0f)
        {
            unstickSecondsRemaining -= deltaTime;
            goal = unstickDirection;
        }

        steerTimer -= deltaTime;
        if (steerTimer <= 0f)
        {
            steerTimer = steerIntervalSeconds;
            chosenDirection = ChooseDirection(goal, out steerBlocked);
        }

        currentMoveDirection = Vector3.MoveTowards(currentMoveDirection, chosenDirection, steerResponsiveness * deltaTime);
        return currentMoveDirection;
    }

    /// <summary>
    /// Looks in 12 directions around the goal (direction 0 is straight at it), throws out the unsafe ones
    /// and scores the rest. 'blocked' is true when even the best safe direction points mostly AWAY from the goal.
    /// </summary>
    private Vector3 ChooseDirection(Vector3 goal, out bool blocked)
    {
        int neighborCount = GatherNeighbors();
        bool standingOnHazard = avoidHazards && SampleSurface(transform.position) == Surface.Hazard;
        float probeDistance = ledgeCheckForwardMeters + currentSpeed * steerIntervalSeconds;

        float bestScore = float.NegativeInfinity;
        float bestAlignment = 0f;
        int bestIndex = -1;

        for (int k = 0; k < DirectionCount; k++)
        {
            Vector3 direction = Quaternion.AngleAxis(k * DirectionStepDegrees, Vector3.up) * goal;
            candidateDirections[k] = direction;
            candidateStatus[k] = DirectionStatus.NotChecked;

            // 1 = straight at the goal, 0 = sideways. We never even look at "away from the goal".
            float alignment = Vector3.Dot(direction, goal);
            if (alignment < MinCandidateAlignment) continue;

            if (IsBlockedByWall(direction))
            {
                candidateStatus[k] = DirectionStatus.Wall;
                continue;
            }

            float penalty = 0f;
            if (avoidLedges || avoidHazards)
            {
                Surface surface = SampleSurface(transform.position + direction * probeDistance);

                if (surface == Surface.NoGround && avoidLedges)
                {
                    candidateStatus[k] = DirectionStatus.NoGround;
                    continue;
                }

                if (surface == Surface.Hazard && avoidHazards)
                {
                    if (!standingOnHazard)
                    {
                        candidateStatus[k] = DirectionStatus.Hazard;
                        continue;
                    }

                    penalty += HazardEscapePenalty;   // already standing in it: allowed, but get out the shortest way
                }
            }

            candidateStatus[k] = DirectionStatus.Free;

            float score = alignment - penalty;

            // Other enemies in that direction make it less attractive.
            for (int n = 0; n < neighborCount; n++)
            {
                float toward = Vector3.Dot(direction, neighborDirections[n]);
                if (toward > 0f) score -= toward * neighborWeights[n] * crowdAvoidanceWeight;
            }

            // A small bonus for continuing the way we were already going (less zig-zag).
            if (chosenDirection.sqrMagnitude > DirectionEpsilonSqr)
                score += directionStickiness * Vector3.Dot(direction, chosenDirection);

            if (score > bestScore)
            {
                bestScore = score;
                bestAlignment = alignment;
                bestIndex = k;
            }
        }

        chosenIndex = bestIndex;

        if (bestIndex < 0)
        {
            blocked = true;
            return Vector3.zero;
        }

        blocked = bestAlignment < MinUsefulAlignment;
        return candidateDirections[bestIndex];
    }

    /// <summary>Patrol: a blocked path counts as "reached the end". Chase: give up if it stays blocked.</summary>
    private void HandleBlocked(Vector3 goal, float deltaTime)
    {
        bool wantsToMove = goal.sqrMagnitude > DirectionEpsilonSqr;
        if (!wantsToMove || !steerBlocked)
        {
            blockedSeconds = 0f;
            return;
        }

        if (state == State.Patrol)
        {
            steerBlocked = false;     // used up, so we don't react to the same result twice
            ArriveAtPatrolPoint();
            return;
        }

        blockedSeconds += deltaTime;
        if (blockedSeconds >= giveUpWhenBlockedSeconds) GiveUp();
    }

    /// <summary>Other active enemies close to this one, as directions and "how close" weights (1 = touching).</summary>
    private int GatherNeighbors()
    {
        if (crowdAvoidanceWeight <= 0f || crowdAwarenessRadiusMeters <= 0f) return 0;

        int count = 0;
        Vector3 myPosition = transform.position;

        for (int i = 0; i < allEnemies.Count && count < MaxNeighbors; i++)
        {
            EnemyAI other = allEnemies[i];
            if (other == null || other == this || !other.isActiveAndEnabled) continue;

            Vector3 offset = other.transform.position - myPosition;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (distance >= crowdAwarenessRadiusMeters || distance < 0.0001f) continue;

            neighborDirections[count] = offset / distance;
            neighborWeights[count] = 1f - distance / crowdAwarenessRadiusMeters;
            count++;
        }

        return count;
    }

    /// <summary>
    /// Pushes this enemy away from enemies that are too close. Never pushes it off a ledge,
    /// into a wall or onto a hazard: a crowd must not be able to shove an enemy to its doom.
    /// </summary>
    private Vector3 ComputeSeparation()
    {
        if (separationStrengthMetersPerSecond <= 0f || separationRadiusMeters <= 0f) return Vector3.zero;

        Vector3 push = Vector3.zero;
        for (int i = 0; i < allEnemies.Count; i++)
        {
            EnemyAI other = allEnemies[i];
            if (other == null || other == this || !other.isActiveAndEnabled) continue;

            Vector3 away = transform.position - other.transform.position;
            away.y = 0f;
            float distance = away.magnitude;
            if (distance >= separationRadiusMeters) continue;

            // Exactly on top of each other: pick a random direction to break the tie.
            if (distance < 0.001f) away = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            else away /= distance;

            push += away * (1f - Mathf.Clamp01(distance / separationRadiusMeters));   // closer = harder push
        }

        if (push.sqrMagnitude < DirectionEpsilonSqr) return Vector3.zero;

        Vector3 pushDirection = push.normalized;
        if (!IsSafeToStep(pushDirection)) return Vector3.zero;

        return push * separationStrengthMetersPerSecond;
    }

    // ---- Checking the world --------------------------------------------

    /// <summary>Is there a wall just ahead in this direction? (Slopes you can walk up don't count.)</summary>
    private bool IsBlockedByWall(Vector3 direction)
    {
        Bounds bounds = controller.bounds;
        float castRadius = Mathf.Max(0.01f, Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.9f);

        // Start just above the height of a step the enemy can climb, so small steps aren't mistaken for walls.
        Vector3 origin = new Vector3(bounds.center.x,
            bounds.min.y + controller.stepOffset + castRadius + WallCheckStepClearanceMeters,
            bounds.center.z);

        int count = Physics.SphereCastNonAlloc(origin, castRadius, direction, rayBuffer, wallCheckDistanceMeters,
            groundMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = rayBuffer[i];
            if (IsIgnoredCollider(hit.collider)) continue;
            if (Vector3.Angle(hit.normal, Vector3.up) <= controller.slopeLimit) continue;   // a ramp, not a wall
            return true;
        }

        return false;
    }

    /// <summary>
    /// What is the ground like at this spot? Shoots a ray down from just above it.
    /// NoGround = a gap or a drop deeper than Ledge Check Depth. Hazard = ground marked with GroundHazard.
    /// </summary>
    private Surface SampleSurface(Vector3 feetPoint)
    {
        float startHeight = controller.stepOffset + GroundProbeExtraHeightMeters;
        Vector3 origin = feetPoint + Vector3.up * startHeight;
        float distance = startHeight + ledgeCheckDepthMeters;

        int count = Physics.RaycastNonAlloc(origin, Vector3.down, rayBuffer, distance, groundMask,
            QueryTriggerInteraction.Ignore);

        float nearest = float.PositiveInfinity;
        bool found = false;
        RaycastHit ground = default;
        for (int i = 0; i < count; i++)
        {
            if (IsIgnoredCollider(rayBuffer[i].collider)) continue;
            if (rayBuffer[i].distance < nearest)
            {
                nearest = rayBuffer[i].distance;
                ground = rayBuffer[i];
                found = true;
            }
        }

        if (!found) return Surface.NoGround;
        if (avoidHazards && IsHazard(ground.collider, ground.point)) return Surface.Hazard;
        return Surface.Safe;
    }

    /// <summary>
    /// Is this ground damaging? Two ways a hazard can exist, and both are checked:
    /// 1) the ground collider itself (or its parent) has a GroundHazard component;
    /// 2) a TRIGGER volume with a GroundHazard sits over the ground. Rays ignore triggers, so we look for those directly.
    /// </summary>
    private bool IsHazard(Collider groundCollider, Vector3 point)
    {
        GroundHazard hazard = groundCollider.GetComponentInParent<GroundHazard>();
        if (hazard != null && hazard.EnemiesAvoid) return true;

        int count = Physics.OverlapSphereNonAlloc(point, HazardProbeRadiusMeters, hazardBuffer, hazardMask,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < count; i++)
        {
            GroundHazard zone = hazardBuffer[i].GetComponentInParent<GroundHazard>();
            if (zone != null && zone.EnemiesAvoid) return true;
        }

        return false;
    }

    /// <summary>Wall, gap and hazard check for stepping in one direction (used by the separation push).</summary>
    private bool IsSafeToStep(Vector3 direction)
    {
        if (IsBlockedByWall(direction)) return false;
        if (!avoidLedges && !avoidHazards) return true;

        Surface surface = SampleSurface(transform.position + direction * ledgeCheckForwardMeters);
        if (surface == Surface.NoGround && avoidLedges) return false;
        if (surface == Surface.Hazard && avoidHazards) return false;
        return true;
    }

    /// <summary>Colliders that are not "the world": this enemy, other enemies and the player.</summary>
    private bool IsIgnoredCollider(Collider other)
    {
        if (other.transform.IsChildOf(transform)) return true;
        if (player != null && other.transform.IsChildOf(player)) return true;
        return other.GetComponentInParent<EnemyAI>() != null;
    }

    // ---- 4) Moving -----------------------------------------------------

    private void Move(Vector3 horizontalVelocity, Vector3 lookDirection, float deltaTime)
    {
        horizontalVelocity.y = 0f;

        if (controller.isGrounded && verticalVelocity <= 0f)
            verticalVelocity = -GroundStickSpeed;
        else
            verticalVelocity = Mathf.Max(verticalVelocity - gravity * deltaTime, -MaxFallSpeedMetersPerSecond);

        controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * deltaTime);

        lookDirection.y = 0f;
        if (lookDirection.sqrMagnitude < DirectionEpsilonSqr) return;

        Quaternion targetRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation,
            turnSpeedDegreesPerSecond * deltaTime);
    }

    // ---- 5) Getting unstuck --------------------------------------------

    /// <summary>
    /// "Stuck" = it WANTS to move but covered much less distance than it should have.
    /// First it sidesteps (random side, 60 to 120 degrees off). If that keeps failing it gives up the chase,
    /// or skips to the next patrol point. This is the safety net for every situation the steering didn't foresee.
    /// </summary>
    private void UpdateStuckDetection(Vector3 goal, float speed, float deltaTime)
    {
        stuckTimer += deltaTime;
        if (stuckTimer < stuckCheckSeconds) return;

        Vector3 moved = transform.position - lastCheckPosition;
        moved.y = 0f;

        float expectedDistance = speed * stuckTimer;
        bool wantedToMove = goal.sqrMagnitude > DirectionEpsilonSqr && speed > 0f;

        lastCheckPosition = transform.position;
        stuckTimer = 0f;

        if (!wantedToMove || moved.magnitude >= expectedDistance * stuckProgressFraction)
        {
            stuckCount = 0;
            return;
        }

        stuckCount++;

        if (stuckCount >= giveUpAfterStuckCount)
        {
            stuckCount = 0;
            if (state == State.Chase) GiveUp();
            else ArriveAtPatrolPoint();
            return;
        }

        float side = Random.value < 0.5f ? 1f : -1f;
        unstickDirection = Quaternion.AngleAxis(side * Random.Range(60f, 120f), Vector3.up) * lastGoalDirection;
        unstickSecondsRemaining = unstickSeconds;
        RequestSteerNow();
    }

    // ---- 6) Touch damage -----------------------------------------------

    /// <summary>"Touching" = the enemy's body, grown by a small padding, overlaps the player's body.</summary>
    private bool IsTouchingPlayer()
    {
        if (playerCollider == null) return false;

        Bounds touchZone = controller.bounds;
        touchZone.Expand(contactPaddingMeters * 2f);   // Expand adds the amount to the full size, so x2 = padding on each side
        return touchZone.Intersects(playerCollider.bounds);
    }

    /// <summary>
    /// Find the player's IDamageable and call TakeDamage (the enemy never needs to know what kind of
    /// health the player has). Comparing bodies directly means it works for any size of enemy and
    /// doesn't depend on layers.
    /// </summary>
    private void TryDamagePlayer()
    {
        if (player == null || playerCollider == null || damageCooldownRemaining > 0f) return;
        if (!IsTouchingPlayer()) return;

        // Landing on the head of a Stompable enemy is a stomp (PlayerStomp handles it), not a touch.
        // Checking positions (not timing) means it works no matter which script runs first this frame.
        if (stompable != null &&
            playerCollider.bounds.min.y >= controller.bounds.max.y - topContactToleranceMeters) return;

        IDamageable target = player.GetComponentInChildren<IDamageable>();
        if (target == null) return;

        Vector3 hitDirection = player.position - transform.position;
        hitDirection.y = 0f;
        hitDirection = hitDirection.sqrMagnitude > DirectionEpsilonSqr ? hitDirection.normalized : transform.forward;

        target.TakeDamage(contactDamage, hitDirection);
        damageCooldownRemaining = damageCooldownSeconds;
        onPlayerHit?.Invoke();
    }

    // ---- Gizmos (Scene view only) --------------------------------------

    private void OnDrawGizmosSelected()
    {
        Vector3 home = Application.isPlaying ? homePosition : transform.position;

        // Patrol path (green) + home (blue)
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(home, 0.04f);
        Gizmos.color = Color.green;
        if (patrolPoints != null)
        {
            for (int i = 0; i < patrolPoints.Length; i++)
            {
                Vector3 point = home + patrolPoints[i];
                Gizmos.DrawWireSphere(point, 0.06f);

                bool isLast = i == patrolPoints.Length - 1;
                if (!isLast) Gizmos.DrawLine(point, home + patrolPoints[i + 1]);
                else if (!pingPong && patrolPoints.Length > 1) Gizmos.DrawLine(point, home + patrolPoints[0]);
            }
        }

        // Chase leash (red, big)
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(home, maxChaseDistanceMeters);

        // Vision cone (yellow) + close awareness
        Vector3 eye = transform.position + Vector3.up * eyeHeightMeters;
        float half = sightAngleDegrees * 0.5f;
        Gizmos.color = Color.yellow;

        Vector3 previous = eye + Quaternion.Euler(0f, -half, 0f) * transform.forward * sightRangeMeters;
        Gizmos.DrawLine(eye, previous);
        for (int i = 1; i <= ConeArcSegments; i++)
        {
            float angle = -half + sightAngleDegrees * i / ConeArcSegments;
            Vector3 point = eye + Quaternion.Euler(0f, angle, 0f) * transform.forward * sightRangeMeters;
            Gizmos.DrawLine(previous, point);
            previous = point;
        }
        Gizmos.DrawLine(eye, previous);

        Gizmos.color = new Color(1f, 1f, 0f, 0.35f);
        Gizmos.DrawWireSphere(eye, closeAwarenessMeters);

        CharacterController cc = GetComponent<CharacterController>();

        // Touch-damage zone (red box around the body)
        if (cc != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(cc.bounds.center, cc.bounds.size + Vector3.one * (contactPaddingMeters * 2f));
        }

        // What it is thinking right now: one line per direction it considered (only while playing).
        if (!Application.isPlaying) return;

        Vector3 feet = transform.position + Vector3.up * 0.03f;
        float lineLength = ledgeCheckForwardMeters + wallCheckDistanceMeters + 0.1f;

        for (int k = 0; k < DirectionCount; k++)
        {
            switch (candidateStatus[k])
            {
                case DirectionStatus.Free: Gizmos.color = Color.green; break;
                case DirectionStatus.Wall: Gizmos.color = Color.red; break;
                case DirectionStatus.NoGround: Gizmos.color = Color.blue; break;
                case DirectionStatus.Hazard: Gizmos.color = Color.magenta; break;
                default: continue;
            }

            Gizmos.DrawLine(feet, feet + candidateDirections[k] * lineLength);
        }

        if (chosenIndex >= 0)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(feet, feet + candidateDirections[chosenIndex] * (lineLength * 1.3f));
        }
    }
}
