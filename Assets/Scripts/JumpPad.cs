using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>How the pad decides to launch the player.</summary>
public enum BounceMode
{
    Automatic,     // touch the pad and you are thrown up (classic Mario spring)
    OnJumpPress    // you must press JUMP while on the pad to bounce (the press IS the bounce, no normal jump happens)
}

/// <summary>
/// A bounce pad. Step on it and the player is launched into the air.
///
/// BUILD IT LIKE THIS:
///   JumpPad          (empty object)  <- this script. It adds a TRIGGER box: the part that launches the player.
///     Visual         (child, the model)  <- optional: drag into the "Visual" slot and it squashes when used.
///
/// HOW HIGH IT THROWS YOU is the first thing in the Inspector, as sliders:
///   Launch Height Meters           - how high the player flies above the pad.  Type any number: there is no maximum.
///   Launch Height Holding Jump     - how high they fly if they HOLD the jump button while bouncing (Mario style).
/// The character is 0.4 m tall and a normal jump is 0.8 m, so 2 = a big bounce, 5 = way up high.
/// Select the pad in the Scene view: a green arc shows exactly where the player will fly, and it updates
/// live while you drag the sliders. You can even drag the sliders while the game is running to tune it.
/// </summary>
[DefaultExecutionOrder(-20)] // Runs before PlayerController, so a manual bounce takes the jump press first.
public class JumpPad : MonoBehaviour
{
    [Header("HOW IT TRIGGERS")]
    [Tooltip("Automatic = touching the pad launches you.\n" +
             "OnJumpPress = you have to press the JUMP button while standing on the pad. The press is used up by the pad, " +
             "so you get the pad's bounce INSTEAD of a normal jump.")]
    [SerializeField] private BounceMode bounceMode = BounceMode.Automatic;
    [Tooltip("OnJumpPress: a jump pressed this long BEFORE reaching the pad still counts (forgiveness).")]
    [SerializeField, Min(0f)] private float jumpPressBufferSeconds = 0.12f;

    [Header("HOW HIGH (the main knobs)")]
    [Tooltip("How high the player flies above the pad, in meters.")]
    [SerializeField, Min(0f)] private float launchHeightMeters = 2f;
    [Tooltip("Automatic mode only: ON = holding the jump button while bouncing sends the player higher (use the next slider).")]
    [SerializeField] private bool holdJumpToBounceHigher = true;
    [Tooltip("How high the player flies if they are HOLDING jump when they hit the pad, in meters.")]
    [SerializeField, Min(0f)] private float launchHeightHoldingJumpMeters = 3.5f;

    [Header("Sideways push (cannon pad)")]
    [Tooltip("Sideways speed in the pad's forward direction (the blue arrow), in meters/second. 0 = straight up.")]
    [SerializeField, Min(0f)] private float forwardPushMetersPerSecond = 0f;
    [Tooltip("ON = the player's own sideways speed is kept and the push is added on top. " +
             "OFF = the pad decides the sideways speed (straight up = the player stops moving sideways).")]
    [SerializeField] private bool keepPlayerMomentum = true;

    [Header("Rules")]
    [Tooltip("Seconds before THIS pad can launch again. Stops it from firing twice in a row.")]
    [SerializeField, Min(0f)] private float cooldownSeconds = 0.3f;
    [Tooltip("ON = only a player who is falling or standing launches. A player already flying up through the pad is left alone.")]
    [SerializeField] private bool ignoreRisingPlayer = true;

    [Header("Look")]
    [Tooltip("The model. It squashes flat and springs back on each launch. Empty = no animation.")]
    [SerializeField] private Transform visual;
    [Tooltip("How flat it squashes (0.5 = half height).")]
    [SerializeField, Range(0.1f, 1f)] private float squashScale = 0.5f;
    [SerializeField, Min(0.01f)] private float squashSeconds = 0.25f;

    [Header("Events (hook sound / particles here)")]
    [SerializeField] private UnityEvent onLaunch;

    private float nextAllowedTime;
    private PlayerController playerOnPad;
    private float playerSeenTime = float.NegativeInfinity;
    private Vector3 visualStartScale;
    private Coroutine squashRoutine;

    /// <summary>Change the height from other scripts (a switch, a boss phase...).</summary>
    public float LaunchHeightMeters
    {
        get => launchHeightMeters;
        set => launchHeightMeters = Mathf.Max(0f, value);
    }

    private void Awake()
    {
        if (visual != null) visualStartScale = visual.localScale;

        Collider zone = GetComponent<Collider>();
        if (zone == null || !zone.isTrigger)
            Debug.LogWarning($"{name}: the pad needs a Collider with 'Is Trigger' ON (add a Box Collider and tick Is Trigger).", this);
    }

    private void OnTriggerEnter(Collider other) => HandleTouch(other);

    // Stay: if the player lands exactly on the pad during its cooldown, it still launches as soon as it is ready.
    private void OnTriggerStay(Collider other) => HandleTouch(other);

    private void OnTriggerExit(Collider other)
    {
        if (playerOnPad != null && other.GetComponentInParent<PlayerController>() == playerOnPad) playerOnPad = null;
    }

    private void HandleTouch(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null) return;

        if (bounceMode == BounceMode.OnJumpPress)
        {
            // Just remember who is standing on the pad; Update() watches for the jump press.
            playerOnPad = player;
            playerSeenTime = Time.time;
            return;
        }

        if (CanLaunch(player)) Launch(player, allowHoldBonus: true);
    }

    private void Update()
    {
        if (bounceMode != BounceMode.OnJumpPress || playerOnPad == null) return;

        // The trigger reports every physics step; if it stopped reporting, the player is gone.
        if (Time.time - playerSeenTime > 0.2f)
        {
            playerOnPad = null;
            return;
        }

        // The pad takes this press, so the player doesn't also do a normal jump.
        if (!CanLaunch(playerOnPad) || !playerOnPad.TryConsumeJumpPress(jumpPressBufferSeconds)) return;

        Launch(playerOnPad, allowHoldBonus: false);
    }

    private bool CanLaunch(PlayerController player)
    {
        if (Time.time < nextAllowedTime) return false;
        if (ignoreRisingPlayer && player.VerticalVelocity > 0.5f) return false;
        return true;
    }

    private void Launch(PlayerController player, bool allowHoldBonus)
    {
        nextAllowedTime = Time.time + cooldownSeconds;

        // Holding jump = the higher bounce.
        bool holding = allowHoldBonus && holdJumpToBounceHigher && player.JumpHeld;
        float height = holding ? launchHeightHoldingJumpMeters : launchHeightMeters;

        Vector3 push = transform.forward;
        push.y = 0f;
        push = push.sqrMagnitude > 0.0001f ? push.normalized * forwardPushMetersPerSecond : Vector3.zero;
        player.Launch(height, keepPlayerMomentum ? player.HorizontalVelocity + push : push);

        onLaunch?.Invoke();

        if (visual != null)
        {
            if (squashRoutine != null) StopCoroutine(squashRoutine);
            squashRoutine = StartCoroutine(SquashRoutine());
        }
    }

    private IEnumerator SquashRoutine()
    {
        Vector3 squashed = new Vector3(visualStartScale.x, visualStartScale.y * squashScale, visualStartScale.z);

        for (float t = 0f; t < squashSeconds; t += Time.deltaTime)
        {
            // Smooth there-and-back: 0 -> 1 -> 0 over the duration.
            float wave = Mathf.Sin(t / squashSeconds * Mathf.PI);
            visual.localScale = Vector3.Lerp(visualStartScale, squashed, wave);
            yield return null;
        }

        visual.localScale = visualStartScale;
        squashRoutine = null;
    }

    // Reset() runs in the editor when the script is first added: a flat trigger box on top of the pad.
    private void Reset()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null) box = gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = new Vector3(0f, 0.05f, 0f);
        box.size = new Vector3(0.5f, 0.1f, 0.5f);
    }

    // Scene view (select the pad): the green arc is the path UP to the highest point, for the normal bounce.
    // The yellow ring marks the higher "holding jump" bounce.
    private void OnDrawGizmosSelected()
    {
        float gravity = 20f;
        PlayerController player = FindFirstObjectByType<PlayerController>();
        if (player != null) gravity = player.gravity;

        Vector3 flat = transform.forward;
        flat.y = 0f;
        Vector3 push = flat.sqrMagnitude > 0.0001f ? flat.normalized * forwardPushMetersPerSecond : Vector3.zero;

        DrawArc(gravity, launchHeightMeters, push, Color.green);

        if (holdJumpToBounceHigher && bounceMode == BounceMode.Automatic)
        {
            Vector3 top = transform.position + Vector3.up * launchHeightHoldingJumpMeters
                          + push * (Mathf.Sqrt(2f * launchHeightHoldingJumpMeters / gravity));
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(top, 0.06f);
        }
    }

    private void DrawArc(float gravity, float height, Vector3 push, Color color)
    {
        float speed = Mathf.Sqrt(2f * gravity * height);
        float timeToTop = speed / gravity;

        Gizmos.color = color;
        Vector3 previous = transform.position;
        const int Steps = 20;

        for (int i = 1; i <= Steps; i++)
        {
            float t = timeToTop * i / Steps;
            Vector3 point = transform.position + push * t + Vector3.up * (speed * t - 0.5f * gravity * t * t);
            Gizmos.DrawLine(previous, point);
            previous = point;
        }

        Gizmos.DrawWireSphere(previous, 0.05f);
    }
}
