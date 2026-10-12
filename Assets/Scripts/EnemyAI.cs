using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Goomba-style enemy: patrols a path, chases the player when it SEES them (view cone + walls block
/// the view), gives up after a while, and hurts the player on touch through IDamageable.
/// Moves itself with a CharacterController, the same way PlayerMotor moves the player.
/// Needs on the same object: CharacterController (added automatically). Damageable is optional here,
/// but you need it if you want the sword to be able to kill this enemy.
/// Select the enemy in the Scene view to see the gizmos:
///   GREEN = patrol path, YELLOW = vision cone, RED (big) = chase leash, RED (small) = touch-damage zone.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class EnemyAI : MonoBehaviour
{
    private const int MaxBufferHits = 8;
    private const float ArrivalDistanceMeters = 0.08f;   // "close enough" to a patrol point
    private const float DirectionEpsilonSqr = 0.0001f;
    private const float GroundStickSpeed = 1f;
    private const float MaxFallSpeedMetersPerSecond = 10f;
    private const int ConeArcSegments = 12;

    // The enemy is ALWAYS in exactly one of these states.
    private enum State { Patrol, Chase }

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
    [Tooltip("Stops this close to the player instead of pushing into them.")]
    [SerializeField, Min(0f)] private float stopDistanceMeters = 0.1f;

    [Header("Movement")]
    [SerializeField, Min(0f)] private float turnSpeedDegreesPerSecond = 540f;
    [SerializeField, Min(0f)] private float gravity = 20f;
    [Tooltip("Do NOT include the Player or Enemy layers.")]
    [SerializeField] private LayerMask groundMask = ~0;
    [Tooltip("ON = turns around / stops at platform edges instead of walking off.")]
    [SerializeField] private bool avoidLedges = true;
    [SerializeField, Min(0f)] private float ledgeCheckForwardMeters = 0.2f;
    [Tooltip("If there is no ground within this distance below the check point, it counts as a ledge.")]
    [SerializeField, Min(0f)] private float ledgeCheckDepthMeters = 0.3f;

    [Header("No Stacking (enemies push each other apart)")]
    [Tooltip("Tick the layer your enemies are on. Leave on Everything if unsure.")]
    [SerializeField] private LayerMask enemyMask = ~0;
    [SerializeField, Min(0f)] private float separationRadiusMeters = 0.4f;
    [Tooltip("How hard enemies push each other apart (0 = off).")]
    [SerializeField, Min(0f)] private float separationStrengthMetersPerSecond = 0.6f;

    [Header("Touch Damage")]
    [SerializeField, Min(0f)] private float contactDamage = 1f;
    [Tooltip("Seconds between hits, so one touch can't drain all health in a single frame.")]
    [SerializeField, Min(0f)] private float damageCooldownSeconds = 1f;
    [Tooltip("Centre of the touch zone, in the enemy's local space (origin at feet).")]
    [SerializeField] private Vector3 contactLocalOffset = new Vector3(0f, 0.2f, 0f);
    [SerializeField, Min(0.01f)] private float contactRadiusMeters = 0.22f;

    [Header("Events (hook animation / sound / VFX)")]
    [SerializeField] private UnityEvent onPlayerSpotted;
    [SerializeField] private UnityEvent onPlayerLost;
    [SerializeField] private UnityEvent onPlayerHit;

    private readonly RaycastHit[] rayBuffer = new RaycastHit[MaxBufferHits];
    private readonly Collider[] overlapBuffer = new Collider[MaxBufferHits];

    private CharacterController controller;
    private State state = State.Patrol;
    private Vector3 homePosition;          // where the enemy was placed; the patrol path hangs off this
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

    public bool IsChasing => state == State.Chase;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        homePosition = transform.position;
    }

    private void Start()
    {
        if (player == null)
        {
            GameObject found = GameObject.FindGameObjectWithTag("Player");
            if (found != null) player = found.transform;
        }

        if (player == null)
        {
            Debug.LogWarning($"{name}: no object tagged 'Player' found, so this enemy will only patrol.", this);
            return;
        }

        playerMask = 1 << player.gameObject.layer;
        sightMask = sightBlockers | playerMask;

        if (player.GetComponentInParent<IDamageable>() == null)
            Debug.LogWarning($"{name}: the player has no Damageable (IDamageable), so touching it will do nothing.", this);
    }

    // ------------------------------------------------------------------
    // Each frame: 1) decide the state  2) pick where to go  3) move  4) hurt the player if touching
    // ------------------------------------------------------------------
    private void Update()
    {
        float deltaTime = Time.deltaTime;
        if (giveUpCooldownRemaining > 0f) giveUpCooldownRemaining -= deltaTime;
        if (damageCooldownRemaining > 0f) damageCooldownRemaining -= deltaTime;

        UpdateState(deltaTime);

        Vector3 moveDirection;
        Vector3 lookDirection;
        float speed;

        if (state == State.Chase)
        {
            GetChaseSteering(out moveDirection, out lookDirection);
            speed = chaseSpeedMetersPerSecond;
        }
        else
        {
            moveDirection = GetPatrolDirection(deltaTime);
            lookDirection = moveDirection;
            speed = patrolSpeedMetersPerSecond;
        }

        Move(moveDirection * speed + ComputeSeparation(), lookDirection, deltaTime);
        TryDamagePlayer();
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
        onPlayerSpotted?.Invoke();
    }

    private void GiveUp()
    {
        state = State.Patrol;               // patrol resumes from the point it was heading to
        giveUpCooldownRemaining = giveUpCooldownSeconds;
        waitSecondsRemaining = 0f;
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

    // ---- 2) Steering ---------------------------------------------------

    private Vector3 GetPatrolDirection(float deltaTime)
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

        Vector3 direction = toPoint.normalized;
        if (LedgeAhead(direction))
        {
            ArriveAtPatrolPoint();          // can't go further this way: treat it as the end of the path
            return Vector3.zero;
        }

        return direction;
    }

    private void ArriveAtPatrolPoint()
    {
        waitSecondsRemaining = waitAtPointSeconds;
        if (patrolPoints.Length <= 1) return;

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

    private void GetChaseSteering(out Vector3 moveDirection, out Vector3 lookDirection)
    {
        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;

        lookDirection = toPlayer;
        moveDirection = Vector3.zero;

        if (toPlayer.magnitude <= stopDistanceMeters) return;

        Vector3 direction = toPlayer.normalized;
        if (!LedgeAhead(direction)) moveDirection = direction;
    }

    /// <summary>True if there is NO ground just in front of the enemy's feet.</summary>
    private bool LedgeAhead(Vector3 direction)
    {
        if (!avoidLedges) return false;

        Bounds bounds = controller.bounds;
        Vector3 origin = new Vector3(bounds.center.x, bounds.min.y + 0.05f, bounds.center.z)
                         + direction * ledgeCheckForwardMeters;

        return !Physics.Raycast(origin, Vector3.down, ledgeCheckDepthMeters + 0.05f, groundMask,
            QueryTriggerInteraction.Ignore);
    }

    /// <summary>Pushes this enemy away from other enemies that are too close, so they don't clump in one spot.</summary>
    private Vector3 ComputeSeparation()
    {
        if (separationStrengthMetersPerSecond <= 0f || separationRadiusMeters <= 0f) return Vector3.zero;

        int count = Physics.OverlapSphereNonAlloc(transform.position, separationRadiusMeters, overlapBuffer,
            enemyMask, QueryTriggerInteraction.Ignore);

        Vector3 push = Vector3.zero;
        for (int i = 0; i < count; i++)
        {
            Collider other = overlapBuffer[i];
            if (other.transform.IsChildOf(transform)) continue;

            EnemyAI otherEnemy = other.GetComponentInParent<EnemyAI>();
            if (otherEnemy == null) continue;

            Vector3 away = transform.position - otherEnemy.transform.position;
            away.y = 0f;
            float distance = away.magnitude;

            // Exactly on top of each other: pick a random direction to break the tie.
            if (distance < 0.001f) away = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            else away /= distance;

            // The closer they are, the harder the push.
            push += away * (1f - Mathf.Clamp01(distance / separationRadiusMeters));
        }

        return push * separationStrengthMetersPerSecond;
    }

    // ---- 3) Moving -----------------------------------------------------

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

    // ---- 4) Touch damage -----------------------------------------------

    /// <summary>
    /// Same idea as the sword: overlap a sphere, find an IDamageable, call TakeDamage.
    /// The enemy never needs to know what kind of health the player has.
    /// </summary>
    private void TryDamagePlayer()
    {
        if (player == null || damageCooldownRemaining > 0f) return;

        Vector3 center = transform.TransformPoint(contactLocalOffset);
        int count = Physics.OverlapSphereNonAlloc(center, contactRadiusMeters, overlapBuffer, playerMask,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider other = overlapBuffer[i];
            if (!other.transform.IsChildOf(player)) continue;

            IDamageable target = other.GetComponentInParent<IDamageable>();
            if (target == null) continue;

            Vector3 hitDirection = player.position - transform.position;
            hitDirection.y = 0f;
            hitDirection = hitDirection.sqrMagnitude > DirectionEpsilonSqr ? hitDirection.normalized : transform.forward;

            target.TakeDamage(contactDamage, hitDirection);
            damageCooldownRemaining = damageCooldownSeconds;
            onPlayerHit?.Invoke();
            return;
        }
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

        // Touch-damage zone (red, small)
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.TransformPoint(contactLocalOffset), contactRadiusMeters);

        // Ledge check (cyan)
        CharacterController cc = GetComponent<CharacterController>();
        if (avoidLedges && cc != null)
        {
            Bounds b = cc.bounds;
            Vector3 origin = new Vector3(b.center.x, b.min.y + 0.05f, b.center.z) + transform.forward * ledgeCheckForwardMeters;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(origin, origin + Vector3.down * (ledgeCheckDepthMeters + 0.05f));
        }
    }
}
