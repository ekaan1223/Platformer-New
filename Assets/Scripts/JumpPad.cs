using System.Collections;  // gives us IEnumerator, for the squash animation (a coroutine)
using UnityEngine;         // gives us access to Unity classes (MonoBehaviour, Collider, AudioSource, Gizmos, etc.)
using UnityEngine.Events;  // gives us UnityEvent (the event lists you can fill in the Inspector)

// How the pad decides to launch the player.
public enum BounceMode
{
    Automatic,     // touch the pad and you are thrown up (classic spring)
    OnJumpPress    // you must press JUMP while on the pad to bounce (the press IS the bounce, no normal jump happens)
}

// JUMP PAD
// A bounce pad. Step on it and the player is launched into the air.
//
// BUILD IT LIKE THIS:
//   JumpPad          (empty object)  <- this script. It adds a TRIGGER box: the part that launches the player.
//     Visual         (child, the model)  <- optional: drag into the "Visual" slot and it squashes when used.
//
// HOW HIGH IT THROWS YOU is the first thing in the Inspector:
//   Launch Height Meters           - how high the player flies above the pad. Type any number: there is no maximum.
//   Launch Height Holding Jump     - how high they fly if they HOLD the jump button while bouncing.
// Select the pad in the Scene view: a green arc shows exactly where the player will fly, and it updates
// live while you change the numbers. You can even change them while the game is running to tune it.

[DefaultExecutionOrder(-20)] // runs before PlayerController, so a manual bounce takes the jump press first
public class JumpPad : MonoBehaviour // MonoBehaviour = a script Unity can put on an object and call every frame
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
    [Tooltip("Automatic mode only: ON = holding the jump button while bouncing sends the player higher (use the next value).")]
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

    [Header("Sound")]
    [Tooltip("Played once on every launch. Empty = silent.")]
    [SerializeField] private AudioClip launchSound;
    [SerializeField, Range(0f, 1f)] private float launchVolume = 0.8f;
    [Tooltip("Each launch picks a random pitch in this range, so repeated bounces don't sound identical. 1-1 = always the same.")]
    [SerializeField] private Vector2 launchPitchRange = new Vector2(0.95f, 1.05f);
    [Tooltip("1 = 3D (quieter when the camera is far away). 0 = always the same volume.")]
    [SerializeField, Range(0f, 1f)] private float launchSpatialBlend = 1f;

    [Header("Events (hook sound / particles here)")]
    [SerializeField] private UnityEvent onLaunch;

    private float nextAllowedTime;                         // the pad can't launch again before this time (cooldown)
    private bool playerOnPad;                              // OnJumpPress: true while the player stands on the pad
    private float playerSeenTime = float.NegativeInfinity; // OnJumpPress: when the trigger last reported the player
    private Vector3 visualStartScale;                      // the model's normal size, to spring back to
    private Coroutine squashRoutine;                       // the squash animation that is running (null = none)
    private AudioSource audioSource;                       // plays the launch sound (made in Awake if needed)

    // Change the height from other scripts (a switch, a boss phase...). It never goes below 0.
    public float LaunchHeightMeters
    {
        get { return launchHeightMeters; }
        set { launchHeightMeters = Mathf.Max(0f, value); }
    }

    // Awake runs once, when the object is created.
    private void Awake()
    {
        if (visual != null) visualStartScale = visual.localScale; // remember the model's normal size

        Collider zone = GetComponent<Collider>();
        if (zone == null || !zone.isTrigger)
            Debug.LogWarning(name + ": the pad needs a Collider with 'Is Trigger' ON (add a Box Collider and tick Is Trigger).", this);

        // Uses an AudioSource already on the pad (for your own mixer / range settings), or adds one.
        if (launchSound != null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = launchSpatialBlend;
        }
    }

    // Unity calls these when something is inside the trigger box.
    private void OnTriggerEnter(Collider other)
    {
        HandleTouch(other);
    }

    // Stay: if the player lands on the pad during its cooldown, it still launches as soon as it is ready.
    private void OnTriggerStay(Collider other)
    {
        HandleTouch(other);
    }

    private void OnTriggerExit(Collider other)
    {
        if (PlayerController.IsPlayer(other)) playerOnPad = false; // the player stepped off
    }

    // The player (or something else) touches the pad.
    private void HandleTouch(Collider other)
    {
        if (!PlayerController.IsPlayer(other)) return; // not the player (cheap check, no searching)

        if (bounceMode == BounceMode.OnJumpPress)
        {
            // Just remember that the player is standing on the pad; Update watches for the jump press.
            playerOnPad = true;
            playerSeenTime = Time.time;
            return;
        }

        PlayerController player = PlayerController.Current;
        if (CanLaunch(player)) Launch(player, true); // true = holding jump may send the player higher
    }

    // Update runs every frame. Only does something in OnJumpPress mode while the player is on the pad.
    private void Update()
    {
        if (bounceMode != BounceMode.OnJumpPress || !playerOnPad) return;

        PlayerController player = PlayerController.Current;
        if (player == null) // the player is gone
        {
            playerOnPad = false;
            return;
        }

        // The trigger reports every physics step; if it stopped reporting, the player is gone.
        if (Time.time - playerSeenTime > 0.2f)
        {
            playerOnPad = false;
            return;
        }

        // The pad takes this press, so the player doesn't also do a normal jump.
        if (!CanLaunch(player) || !player.TryConsumeJumpPress(jumpPressBufferSeconds)) return;

        Launch(player, false); // false = the press is the bounce, no "holding" bonus
    }

    // May the pad launch the player right now?
    private bool CanLaunch(PlayerController player)
    {
        if (player == null) return false;
        if (Time.time < nextAllowedTime) return false;                                // still cooling down
        if (ignoreRisingPlayer && player.VerticalVelocity > 0.5f) return false;       // already flying up through it
        return true;
    }

    // Throws the player up, plays the sound and squashes the model.
    private void Launch(PlayerController player, bool allowHoldBonus)
    {
        nextAllowedTime = Time.time + cooldownSeconds;

        // Holding jump = the higher bounce. Max: holding must never give LESS than the normal height.
        bool holding = allowHoldBonus && holdJumpToBounceHigher && player.JumpHeld;
        float height = launchHeightMeters;
        if (holding) height = Mathf.Max(launchHeightHoldingJumpMeters, launchHeightMeters);

        Vector3 push = transform.forward; // the pad's forward direction, flat
        push.y = 0f;
        if (push.sqrMagnitude > 0.0001f) push = push.normalized * forwardPushMetersPerSecond;
        else push = Vector3.zero;

        Vector3 newHorizontalVelocity = push;                                    // the pad decides the sideways speed...
        if (keepPlayerMomentum) newHorizontalVelocity = player.HorizontalVelocity + push; // ...or adds to the player's own
        player.Launch(height, newHorizontalVelocity);

        if (audioSource != null)
        {
            audioSource.pitch = Random.Range(launchPitchRange.x, launchPitchRange.y);
            audioSource.PlayOneShot(launchSound, launchVolume);
        }

        if (onLaunch != null) onLaunch.Invoke();

        if (visual != null)
        {
            if (squashRoutine != null) StopCoroutine(squashRoutine); // restart the squash if it was still running
            squashRoutine = StartCoroutine(SquashRoutine());
        }
    }

    // Squashes the model flat and lets it spring back.
    private IEnumerator SquashRoutine()
    {
        Vector3 squashed = new Vector3(visualStartScale.x, visualStartScale.y * squashScale, visualStartScale.z);

        for (float t = 0f; t < squashSeconds; t += Time.deltaTime)
        {
            float wave = Mathf.Sin(t / squashSeconds * Mathf.PI); // smooth there-and-back: 0 -> 1 -> 0 over the duration
            visual.localScale = Vector3.Lerp(visualStartScale, squashed, wave);
            yield return null; // wait one frame
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
        float gravity = 20f;                              // the player's default gravity...
        PlayerController player = PlayerController.Current; // ...or the real one while playing
        if (player == null) player = FindFirstObjectByType<PlayerController>(); // Scene view only, before Play: a search is fine here
        if (player != null) gravity = player.gravity;

        Vector3 flat = transform.forward;
        flat.y = 0f;
        Vector3 push = Vector3.zero;
        if (flat.sqrMagnitude > 0.0001f) push = flat.normalized * forwardPushMetersPerSecond;

        DrawArc(gravity, launchHeightMeters, push, Color.green);

        if (holdJumpToBounceHigher && bounceMode == BounceMode.Automatic)
        {
            Vector3 top = transform.position + Vector3.up * launchHeightHoldingJumpMeters
                          + push * (Mathf.Sqrt(2f * launchHeightHoldingJumpMeters / gravity));
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(top, 0.06f);
        }
    }

    // Draws the flight path from the pad up to the highest point.
    private void DrawArc(float gravity, float height, Vector3 push, Color color)
    {
        float speed = Mathf.Sqrt(2f * gravity * height); // the launch speed for that height
        float timeToTop = speed / gravity;                // how long until the highest point

        Gizmos.color = color;
        Vector3 previous = transform.position;
        int steps = 20; // how many little lines make up the arc

        for (int i = 1; i <= steps; i++)
        {
            float t = timeToTop * i / steps;
            Vector3 point = transform.position + push * t + Vector3.up * (speed * t - 0.5f * gravity * t * t);
            Gizmos.DrawLine(previous, point);
            previous = point;
        }

        Gizmos.DrawWireSphere(previous, 0.05f);
    }
}
