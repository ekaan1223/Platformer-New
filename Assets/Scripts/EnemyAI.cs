using System.Collections.Generic; // gives us List
using UnityEngine;                // gives us access to Unity classes (MonoBehaviour, CharacterController, Physics, etc.)
using UnityEngine.Events;         // gives us UnityEvent, for the Inspector events

/// <summary>
/// Goomba-style enemy: patrols a path, chases the player when it SEES them (view cone, walls block the view),
/// gives up after a while, and hurts the player on touch through IDamageable.
/// Moves itself with a CharacterController, the same way PlayerController moves the player.
///
/// HOW IT THINKS (every "Steer Interval Seconds", not every frame, to save time):
///   1. Can I see the player?  (in range, in the view cone, and no wall between us)
///   2. Where do I want to go? (the next patrol point, or the player)
///   3. Which way is safe?     It tries straight ahead first, then 30, 60 and 90 degrees to each side, and takes
///                             the first direction with no wall, no gap and no damaging ground. That makes it slide
///                             along walls instead of walking into them, and never walk off a platform.
///   It always tries its last turning side first, so it follows a wall instead of zig-zagging.
///   If it still makes no progress it tries the other side, and finally gives up (chase) or skips the point (patrol).
///
/// Select the enemy in the Scene view to see the gizmos:
///   GREEN = patrol path   YELLOW = view cone   RED sphere = chase leash   RED box = touch zone
///   WHITE line (while playing) = the direction it chose.   CYAN line = it can see the player right now.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class EnemyAI : MonoBehaviour
{
    private const float ArrivalDistanceMeters = 0.08f;    // "close enough" to a patrol point
    private const float GroundStickSpeed = 1f;            // small push down while standing, so it stays on slopes
    private const float MaxFallSpeedMetersPerSecond = 10f; // never falls faster than this
    private const float StuckProgressFraction = 0.25f;    // moved less than this part of the expected distance = stuck
    private const int ConeArcSegments = 12;               // how smooth the view cone gizmo is

    // Every enemy in the scene (static = ONE list shared by all enemies). Used to pass through and push apart.
    private static List<EnemyAI> allEnemies = new List<EnemyAI>();

    // Static data survives into the next Play session if "Enter Play Mode Options" skips domain reload.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => allEnemies.Clear();

    // The angles it tries, in order, around the way it wants to go (multiplied by its preferred side).
    private static float[] tryAngles = { 0f, 30f, -30f, 60f, -60f, 90f, -90f };

    [Header("References")]
    [Tooltip("Who to hunt. Empty = finds the object tagged 'Player' (or the one with PlayerController).")]
    [SerializeField] private Transform player;

    [Header("Patrol Path")]
    [Tooltip("Points the enemy walks to, as OFFSETS (meters) from where you placed it. Not rotated with the enemy.")]
    [SerializeField] private Vector3[] patrolPoints = { new Vector3(1.5f, 0f, 0f), new Vector3(-1.5f, 0f, 0f) };
    [Tooltip("ON = walk there and back. OFF = loop through the points in order.")]
    [SerializeField] private bool pingPong = true;
    [SerializeField, Min(0f)] private float patrolSpeedMetersPerSecond = 0.8f;
    [SerializeField, Min(0f)] private float waitAtPointSeconds = 0.5f;

    [Header("Line of Sight")]
    [Tooltip("How far the enemy can see.")]
    [SerializeField, Min(0f)] private float sightRangeMeters = 2.5f;
    [Tooltip("Width of the view cone. 360 = sees all around.")]
    [SerializeField, Range(1f, 360f)] private float sightAngleDegrees = 120f;
    [Tooltip("Inside this distance it notices you even from behind (0 = off).")]
    [SerializeField, Min(0f)] private float closeAwarenessMeters = 0.35f;
    [Tooltip("Height of the eyes above the enemy's feet. It looks at the MIDDLE of the player's body.")]
    [SerializeField, Min(0f)] private float eyeHeightMeters = 0.3f;

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
    [Tooltip("Gives up if every direction toward the player is blocked for this long (a wall, a gap...).")]
    [SerializeField, Min(0.1f)] private float giveUpWhenBlockedSeconds = 1.5f;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float turnSpeedDegreesPerSecond = 540f;
    [SerializeField, Min(0f)] private float gravity = 20f;
    [Tooltip("How quickly it changes direction once it has decided. Higher = snappier.")]
    [SerializeField, Min(0.1f)] private float steerResponsiveness = 10f;

    [Header("Seeing the world (walls, ledges, hazards)")]
    [Tooltip("How often it looks around and decides (seconds). Lower = reacts faster but costs a little more.")]
    [SerializeField, Range(0.02f, 0.5f)] private float steerIntervalSeconds = 0.1f;
    [Tooltip("How far in front of its body it checks for a wall. (It adds the distance it walks until the next check.)")]
    [SerializeField, Min(0.01f)] private float wallCheckDistanceMeters = 0.12f;
    [Tooltip("ON = never walks off a platform edge or into a gap.")]
    [SerializeField] private bool avoidLedges = true;
    [Tooltip("How far ahead of its feet it checks for ground.")]
    [SerializeField, Min(0f)] private float ledgeCheckForwardMeters = 0.2f;
    [Tooltip("A drop deeper than this counts as a ledge.")]
    [SerializeField, Min(0f)] private float ledgeCheckDepthMeters = 0.3f;
    [Tooltip("ON = never walks onto ground that has a GroundHazard component (lava, spikes, poison...).")]
    [SerializeField] private bool avoidHazards = true;
    [Tooltip("Safety net: if it falls this far below where it started, it is removed.")]
    [SerializeField, Min(1f)] private float fallDeathDepthMeters = 20f;

    [Header("Groups (many enemies at once)")]
    [Tooltip("Enemies closer than this get gently pushed apart so they don't overlap.")]
    [SerializeField, Min(0f)] private float separationRadiusMeters = 0.4f;
    [Tooltip("How hard enemies push each other apart (0 = off).")]
    [SerializeField, Min(0f)] private float separationStrengthMetersPerSecond = 0.6f;

    [Header("Getting Unstuck")]
    [Tooltip("Every this many seconds it checks whether it actually moved.")]
    [SerializeField, Min(0.1f)] private float stuckCheckSeconds = 0.5f;
    [Tooltip("After this many stuck checks in a row it gives up (chasing) or skips to the next patrol point.")]
    [SerializeField, Min(1)] private int giveUpAfterStuckCount = 3;

    [Header("Touch Damage")]
    [SerializeField, Min(0f)] private float contactDamage = 1f;
    [Tooltip("Seconds between hits, so one touch can't drain all health in a single frame.")]
    [SerializeField, Min(0f)] private float damageCooldownSeconds = 1f;
    [Tooltip("The player is 'touched' when their body is within this distance of the enemy's body.")]
    [SerializeField, Min(0f)] private float contactPaddingMeters = 0.08f;

    [Header("Events (hook animation / sound / VFX)")]
    [SerializeField] private UnityEvent onPlayerSpotted;
    [SerializeField] private UnityEvent onPlayerLost;
    [SerializeField] private UnityEvent onPlayerHit;

    private RaycastHit[] hitBuffer = new RaycastHit[8];   // reused for every ray, so checking creates no garbage
    private Collider[] overlapBuffer = new Collider[8];   // reused for the hazard-zone check

    private CharacterController controller;
    private Collider playerCollider;      // the player's body: where it looks, and what it touches
    private IDamageable playerHealth;     // what it hurts on touch
    private Stompable stompable;          // optional: does all the stomping work. Asked before hurting the player
    private Vector3 homePosition;         // where the enemy was placed; the patrol path and leash hang off this

    private bool isChasing;
    private bool canSeePlayer;            // the result of the last sight check

    private int patrolIndex;              // the patrol point it is walking to
    private int patrolStep = 1;           // +1 or -1 (ping-pong direction)
    private float waitSecondsRemaining;   // waiting at a patrol point

    private float chaseSecondsElapsed;
    private float secondsSinceSeen;
    private float giveUpCooldownRemaining;
    private float damageCooldownRemaining;
    private float blockedSeconds;         // how long every direction toward the player was blocked
    private float verticalVelocity;

    private float thinkTimer;             // counts down to the next look-around
    private Vector3 chosenDirection;      // the safe direction picked by the last look-around (zero = none)
    private Vector3 moveDirection;        // chosenDirection, smoothed, so turns aren't instant
    private float preferredSide = 1f;     // +1 = tries left turns first, -1 = right turns first

    private float stuckTimer;
    private Vector3 lastCheckPosition;
    private int stuckCount;

    public bool IsChasing => isChasing;

    /// <summary>
    /// Move this enemy's "home" (where its patrol path and chase leash are measured from).
    /// The boss calls this after throwing an enemy, so it patrols and chases from where it LANDED.
    /// </summary>
    public void SetHomePosition(Vector3 position) => homePosition = position;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        stompable = GetComponent<Stompable>(); // none = this enemy can't be stomped, it just hurts on touch

        homePosition = transform.position;
        lastCheckPosition = transform.position;
        thinkTimer = Random.value * steerIntervalSeconds; // enemies don't all think on the same frame

        // Enemies pass through each other (two solid enemies pushing each other is how groups get stuck).
        // The soft separation push keeps them from standing inside each other.
        allEnemies.RemoveAll(enemy => enemy == null);
        foreach (EnemyAI other in allEnemies) Physics.IgnoreCollision(controller, other.controller, true);
        allEnemies.Add(this);
    }

    private void OnDestroy() => allEnemies.Remove(this);

    private void Update()
    {
        float deltaTime = Time.deltaTime;

        if (transform.position.y < homePosition.y - fallDeathDepthMeters) // fell out of the world
        {
            Destroy(gameObject);
            return;
        }

        if (giveUpCooldownRemaining > 0f) giveUpCooldownRemaining -= deltaTime;
        if (damageCooldownRemaining > 0f) damageCooldownRemaining -= deltaTime;

        Vector3 goal = isChasing ? GetChaseGoal() : GetPatrolGoal(deltaTime); // where it WANTS to go (zero = stand still)
        float speed = isChasing ? chaseSpeedMetersPerSecond : patrolSpeedMetersPerSecond;

        thinkTimer -= deltaTime;
        if (thinkTimer <= 0f) // time to look around
        {
            thinkTimer = steerIntervalSeconds;
            Think(goal, speed);
        }

        UpdateChaseTimers(deltaTime);

        moveDirection = Vector3.MoveTowards(moveDirection, chosenDirection, steerResponsiveness * deltaTime); // smooth turns
        Vector3 look = isChasing && player != null ? player.position - transform.position : moveDirection; // chasing: face the player
        Move(moveDirection * speed + GetSeparation(), look, deltaTime);

        TryDamagePlayer();
        CheckStuck(goal, speed, deltaTime);
    }

    // ---- Finding the player ----------------------------------------------

    private void FindPlayer()
    {
        GameObject tagged = GameObject.FindGameObjectWithTag("Player"); // first try the tag...
        if (tagged != null) player = tagged.transform;
        else
        {
            PlayerController found = FindFirstObjectByType<PlayerController>(); // ...then the PlayerController
            if (found != null) player = found.transform;
        }

        if (player == null) return; // still not in the scene

        playerCollider = player.GetComponentInChildren<Collider>();
        playerHealth = player.GetComponentInChildren<IDamageable>();
        if (playerHealth == null) Debug.LogWarning($"{name}: the player has no Damageable, so touching it does nothing.", this);
    }

    private void Start()
    {
        if (player != null) // dragged in by hand: still needs the collider and health
        {
            playerCollider = player.GetComponentInChildren<Collider>();
            playerHealth = player.GetComponentInChildren<IDamageable>();
        }
        else FindPlayer();
    }

    // ---- Thinking (every Steer Interval Seconds) --------------------------

    private void Think(Vector3 goal, float speed)
    {
        if (player == null) FindPlayer(); // not found yet (spawns later?): keep looking, but only when thinking
        canSeePlayer = giveUpCooldownRemaining <= 0f && CanSeePlayer();

        if (!isChasing && canSeePlayer && !IsTooFarFromHome()) // spotted: start chasing
        {
            isChasing = true;
            chaseSecondsElapsed = 0f;
            secondsSinceSeen = 0f;
            blockedSeconds = 0f;
            stuckCount = 0;
            onPlayerSpotted?.Invoke();
            goal = GetChaseGoal();
            speed = chaseSpeedMetersPerSecond;
        }

        if (goal == Vector3.zero) // nothing to do: stand still
        {
            chosenDirection = Vector3.zero;
            return;
        }

        float lookAhead = wallCheckDistanceMeters + speed * steerIntervalSeconds; // also covers the distance walked until the next check
        chosenDirection = Vector3.zero;

        foreach (float angle in tryAngles) // straight first, then wider and wider turns
        {
            Vector3 direction = Quaternion.AngleAxis(angle * preferredSide, Vector3.up) * goal;
            if (!IsSafe(direction, lookAhead)) continue;

            chosenDirection = direction;
            if (angle != 0f) preferredSide = Mathf.Sign(angle * preferredSide); // turned: try that side first next time (follows walls)
            break;
        }

        if (chosenDirection != Vector3.zero) // found a way
        {
            blockedSeconds = 0f;
            return;
        }

        // Every direction is blocked.
        if (isChasing) blockedSeconds += steerIntervalSeconds; // chasing: give up if this lasts (UpdateChaseTimers)
        else NextPatrolPoint();                                // patrolling: the path ends here, turn around
    }

    private void UpdateChaseTimers(float deltaTime)
    {
        if (!isChasing) return;

        chaseSecondsElapsed += deltaTime;
        secondsSinceSeen = canSeePlayer ? 0f : secondsSinceSeen + deltaTime;

        if (player == null || secondsSinceSeen >= loseSightSeconds || chaseSecondsElapsed >= maxChaseSeconds ||
            blockedSeconds >= giveUpWhenBlockedSeconds || IsTooFarFromHome())
        {
            GiveUp();
        }
    }

    private void GiveUp()
    {
        isChasing = false; // patrol resumes from the point it was heading to
        giveUpCooldownRemaining = giveUpCooldownSeconds;
        waitSecondsRemaining = 0f;
        blockedSeconds = 0f;
        stuckCount = 0;
        thinkTimer = 0f; // decide the new direction right away
        onPlayerLost?.Invoke();
    }

    private bool IsTooFarFromHome()
    {
        Vector3 offset = transform.position - homePosition;
        offset.y = 0f;
        return offset.magnitude > maxChaseDistanceMeters;
    }

    // ---- Line of sight -----------------------------------------------------

    /// <summary>
    /// 1) close enough?  2) inside the view cone (or very close)?  3) a ray from the eyes to the MIDDLE of the
    /// player's body hits no wall on the way? Only real world objects block it (not enemies, not triggers).
    /// </summary>
    private bool CanSeePlayer()
    {
        if (player == null) return false;

        Vector3 eye = transform.position + Vector3.up * eyeHeightMeters;
        Vector3 target = playerCollider != null ? playerCollider.bounds.center : player.position; // the middle of the body
        Vector3 toTarget = target - eye;
        float distance = toTarget.magnitude;

        if (distance > sightRangeMeters) return false; // too far
        if (distance < 0.001f) return true;            // right on top of us

        Vector3 flat = new Vector3(toTarget.x, 0f, toTarget.z);
        bool inCone = Vector3.Angle(transform.forward, flat) <= sightAngleDegrees * 0.5f;
        if (!inCone && distance > closeAwarenessMeters) return false; // behind us and not very close

        return !IsBlocked(eye, toTarget / distance, distance); // a wall in between?
    }

    // ---- Goals: where does it WANT to go? ----------------------------------

    private Vector3 GetPatrolGoal(float deltaTime)
    {
        if (patrolPoints == null || patrolPoints.Length == 0) return Vector3.zero;
        if (patrolIndex >= patrolPoints.Length) patrolIndex = 0;

        if (waitSecondsRemaining > 0f) // resting at a point
        {
            waitSecondsRemaining -= deltaTime;
            return Vector3.zero;
        }

        Vector3 toPoint = homePosition + patrolPoints[patrolIndex] - transform.position;
        toPoint.y = 0f;

        if (toPoint.magnitude <= ArrivalDistanceMeters) // arrived
        {
            NextPatrolPoint();
            return Vector3.zero;
        }

        return toPoint.normalized;
    }

    private void NextPatrolPoint()
    {
        waitSecondsRemaining = waitAtPointSeconds;
        thinkTimer = 0f; // decide the new direction right away
        if (patrolPoints == null || patrolPoints.Length <= 1) return;

        if (pingPong)
        {
            int next = patrolIndex + patrolStep;
            if (next < 0 || next >= patrolPoints.Length) patrolStep = -patrolStep; // bounce at the ends
            patrolIndex += patrolStep;
        }
        else patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
    }

    private Vector3 GetChaseGoal()
    {
        if (player == null) return Vector3.zero;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;

        if (toPlayer.magnitude <= stopDistanceMeters || IsTouchingPlayer()) return Vector3.zero; // already on them
        return toPlayer.normalized;
    }

    // ---- Checking the world ------------------------------------------------

    /// <summary>Can it walk this way? No wall within 'distance', ground ahead (unless ledges are allowed), no hazard.</summary>
    private bool IsSafe(Vector3 direction, float distance)
    {
        Bounds body = controller.bounds;
        float radius = Mathf.Min(body.extents.x, body.extents.z) * 0.8f; // a bit thinner than the body, so brushing a wall is fine

        // Wall: a ball the size of the body, swept forward from the middle of the body (its bottom stays just above
        // the feet, so the floor itself never counts). Walkable slopes don't count as walls.
        if (IsBlocked(body.center, direction, distance, radius)) return false;

        if (!avoidLedges && !avoidHazards) return true;

        // Ground: a ray straight down, just past the front of the body.
        Vector3 ahead = transform.position + direction * (body.extents.x + ledgeCheckForwardMeters);
        float startHeight = body.extents.y; // start at half the body's height above the ground
        RaycastHit ground;
        bool hasGround = FirstHit(ahead + Vector3.up * startHeight, Vector3.down, startHeight + ledgeCheckDepthMeters, 0f, out ground);

        if (!hasGround) return !avoidLedges;                                  // a gap or a deep drop
        if (avoidHazards && IsHazard(ground.collider, ground.point)) return false; // lava, spikes...
        return true;
    }

    /// <summary>
    /// Is something solid in the way? A ray (radius 0) or a ball sweep from 'origin' along 'direction'.
    /// Slopes it can walk up are not "in the way".
    /// </summary>
    private bool IsBlocked(Vector3 origin, Vector3 direction, float distance, float radius = 0f)
    {
        RaycastHit hit;
        if (!FirstHit(origin, direction, distance, radius, out hit)) return false; // nothing at all
        return radius <= 0f || Vector3.Angle(hit.normal, Vector3.up) > controller.slopeLimit; // a sweep ignores ramps
    }

    /// <summary>
    /// The nearest solid world object along a ray / ball sweep. Skips this enemy, other enemies, the player and
    /// triggers, on EVERY layer, so walls work no matter which layer they are on.
    /// </summary>
    private bool FirstHit(Vector3 origin, Vector3 direction, float distance, float radius, out RaycastHit nearest)
    {
        int count = radius > 0f
            ? Physics.SphereCastNonAlloc(origin, radius, direction, hitBuffer, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(origin, direction, hitBuffer, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        nearest = default;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hitBuffer[i];
            if (hit.distance <= 0f && radius > 0f) continue;            // already overlapping at the start (the floor we stand on)
            if (IsIgnored(hit.collider)) continue;                      // ourselves, enemies, the player
            if (found && hit.distance >= nearest.distance) continue;    // not the nearest
            nearest = hit;
            found = true;
        }
        return found;
    }

    /// <summary>Colliders that are not "the world": this enemy, other enemies and the player.</summary>
    private bool IsIgnored(Collider other)
    {
        if (other.transform.IsChildOf(transform)) return true;
        if (player != null && other.transform.IsChildOf(player)) return true;
        return other.GetComponentInParent<EnemyAI>() != null;
    }

    /// <summary>
    /// Is this ground damaging? Either the ground itself has a GroundHazard, or a GroundHazard TRIGGER zone
    /// sits on it (rays skip triggers, so those are searched for directly).
    /// </summary>
    private bool IsHazard(Collider groundCollider, Vector3 point)
    {
        GroundHazard hazard = groundCollider.GetComponentInParent<GroundHazard>();
        if (hazard != null && hazard.EnemiesAvoid) return true;

        int count = Physics.OverlapSphereNonAlloc(point, 0.08f, overlapBuffer, Physics.AllLayers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            GroundHazard zone = overlapBuffer[i].GetComponentInParent<GroundHazard>();
            if (zone != null && zone.EnemiesAvoid) return true;
        }
        return false;
    }

    // ---- Moving ------------------------------------------------------------

    /// <summary>Pushes this enemy away from enemies that are too close, but never off a ledge or into a wall.</summary>
    private Vector3 GetSeparation()
    {
        if (separationStrengthMetersPerSecond <= 0f || separationRadiusMeters <= 0f) return Vector3.zero;

        Vector3 push = Vector3.zero;
        foreach (EnemyAI other in allEnemies)
        {
            if (other == null || other == this || !other.isActiveAndEnabled) continue;

            Vector3 away = transform.position - other.transform.position;
            away.y = 0f;
            float distance = away.magnitude;
            if (distance >= separationRadiusMeters || distance < 0.001f) continue;

            push += away / distance * (1f - distance / separationRadiusMeters); // closer = harder push
        }

        if (push == Vector3.zero || !IsSafe(push.normalized, wallCheckDistanceMeters)) return Vector3.zero;
        return push * separationStrengthMetersPerSecond;
    }

    private void Move(Vector3 horizontalVelocity, Vector3 lookDirection, float deltaTime)
    {
        if (controller.isGrounded && verticalVelocity <= 0f) verticalVelocity = -GroundStickSpeed; // standing
        else verticalVelocity = Mathf.Max(verticalVelocity - gravity * deltaTime, -MaxFallSpeedMetersPerSecond); // falling

        horizontalVelocity.y = 0f;
        controller.Move((horizontalVelocity + Vector3.up * verticalVelocity) * deltaTime);

        lookDirection.y = 0f;
        if (lookDirection.sqrMagnitude < 0.0001f) return; // no direction: keep facing the same way

        Quaternion targetRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeedDegreesPerSecond * deltaTime);
    }

    /// <summary>
    /// "Stuck" = it wants to move but covered much less distance than it should. First it switches the side it
    /// prefers to turn to; if that keeps failing it gives up the chase, or skips to the next patrol point.
    /// </summary>
    private void CheckStuck(Vector3 goal, float speed, float deltaTime)
    {
        stuckTimer += deltaTime;
        if (stuckTimer < stuckCheckSeconds) return;

        Vector3 moved = transform.position - lastCheckPosition;
        moved.y = 0f;
        float expected = speed * stuckTimer;

        lastCheckPosition = transform.position;
        stuckTimer = 0f;

        if (goal == Vector3.zero || speed <= 0f || moved.magnitude >= expected * StuckProgressFraction) // fine
        {
            stuckCount = 0;
            return;
        }

        stuckCount++;
        preferredSide = -preferredSide; // try going around the other way
        thinkTimer = 0f;

        if (stuckCount < giveUpAfterStuckCount) return;

        stuckCount = 0;
        if (isChasing) GiveUp();
        else NextPatrolPoint();
    }

    // ---- Touch damage ------------------------------------------------------

    /// <summary>"Touching" = the enemy's body, grown by a small padding, overlaps the player's body.</summary>
    private bool IsTouchingPlayer()
    {
        if (playerCollider == null) return false;

        Bounds touchZone = controller.bounds;
        touchZone.Expand(contactPaddingMeters * 2f); // Expand grows the full size, so x2 = padding on each side
        return touchZone.Intersects(playerCollider.bounds);
    }

    private void TryDamagePlayer()
    {
        if (playerHealth == null || damageCooldownRemaining > 0f || !IsTouchingPlayer()) return;

        if (stompable != null && stompable.IsBeingStomped(playerCollider.bounds)) return; // a stomp, not a touch (Stompable decides)

        Vector3 hitDirection = player.position - transform.position;
        hitDirection.y = 0f;
        hitDirection = hitDirection.sqrMagnitude > 0.0001f ? hitDirection.normalized : transform.forward;

        playerHealth.TakeDamage(contactDamage, hitDirection);
        damageCooldownRemaining = damageCooldownSeconds;
        onPlayerHit?.Invoke();
    }

    // ---- Gizmos (Scene view only) ------------------------------------------

    private void OnDrawGizmosSelected()
    {
        Vector3 home = Application.isPlaying ? homePosition : transform.position;

        // Patrol path (green)
        Gizmos.color = Color.green;
        if (patrolPoints != null)
        {
            for (int i = 0; i < patrolPoints.Length; i++)
            {
                Vector3 point = home + patrolPoints[i];
                Gizmos.DrawWireSphere(point, 0.06f);
                if (i < patrolPoints.Length - 1) Gizmos.DrawLine(point, home + patrolPoints[i + 1]);
                else if (!pingPong && patrolPoints.Length > 1) Gizmos.DrawLine(point, home + patrolPoints[0]);
            }
        }

        // Chase leash (red)
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(home, maxChaseDistanceMeters);

        // View cone (yellow) + close awareness
        Vector3 eye = transform.position + Vector3.up * eyeHeightMeters;
        float half = sightAngleDegrees * 0.5f;
        Gizmos.color = Color.yellow;
        Vector3 previous = eye + Quaternion.Euler(0f, -half, 0f) * transform.forward * sightRangeMeters;
        Gizmos.DrawLine(eye, previous);
        for (int i = 1; i <= ConeArcSegments; i++)
        {
            Vector3 point = eye + Quaternion.Euler(0f, -half + sightAngleDegrees * i / ConeArcSegments, 0f) * transform.forward * sightRangeMeters;
            Gizmos.DrawLine(previous, point);
            previous = point;
        }
        Gizmos.DrawLine(eye, previous);
        Gizmos.color = new Color(1f, 1f, 0f, 0.35f);
        Gizmos.DrawWireSphere(eye, closeAwarenessMeters);

        // Touch zone (red box)
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(cc.bounds.center, cc.bounds.size + Vector3.one * (contactPaddingMeters * 2f));
        }

        if (!Application.isPlaying) return;

        // What it is thinking right now
        Gizmos.color = Color.white;
        Gizmos.DrawLine(transform.position, transform.position + chosenDirection * 0.5f); // the direction it chose
        if (canSeePlayer && playerCollider != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(eye, playerCollider.bounds.center); // it sees the player
        }
    }
}
