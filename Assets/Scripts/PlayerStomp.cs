using UnityEngine;        // gives us access to Unity classes (MonoBehaviour, ControllerColliderHit, Collider, etc.)
using UnityEngine.Events; // gives us UnityEvent (the event lists you can fill in the Inspector)

// PLAYER STOMP
// Land on an enemy's head while falling -> the enemy takes damage and the player bounces up as high as a normal jump.
// Bounce from enemy to enemy without touching the ground and the combo counter goes up; touching the ground resets it.
//
// You don't have to add this script yourself: PlayerController adds it to the player in Awake if it's missing.
//
// How it connects to the other scripts:
//   - PlayerController : moves the player. While it moves (controller.Move), Unity calls OnControllerColliderHit below for
//                        everything the player bumps into. We read its VerticalVelocity and jumpHeight, and ask it to Launch (the bounce).
//                        Its Landed event tells us when the player touches the ground (ends the combo).
//   - Stompable        : sits on every enemy that CAN be stomped (EnemyAI adds it automatically). Holds how much damage a stomp does.
//   - Damageable       : the enemy's health. We call its TakeDamage (through the IDamageable interface).
//   - EnemyAI          : sees that the player's feet are above its head and does NOT hurt the player in that case.

public class PlayerStomp : MonoBehaviour // MonoBehaviour = a script Unity can put on an object
{
    [Header("References (empty = found automatically)")]
    public PlayerController player; // the PlayerController on the same object; found in Awake if empty

    [Header("Detection")]
    [Range(0f, 1f)]
    public float minTopNormalY = 0.5f;          // how flat the spot we land on must be. 1 = perfectly flat only, 0.5 = up to about 60 degrees
    public float stompCooldownSeconds = 0.08f;  // ignore new stomps for this long, so one landing can't count twice

    [Header("Events (hook sound / VFX / score here in the Inspector)")]
    public UnityEvent onStomp;       // called on every successful stomp
    public UnityEvent onComboEnded;  // called when the player touches the ground after one or more stomps

    [System.NonSerialized] public int ComboCount; // stomps in a row since the last time the player touched the ground (only this script changes it)

    private float lastStompTime = float.NegativeInfinity; // when (Time.time) the last stomp happened; NegativeInfinity = never

    // SPEED TRICK: when standing, the player "hits" the floor EVERY frame, and searching that floor for a Stompable every
    // frame would waste time. So we remember the last thing that turned out NOT to be stompable and skip it next time.
    private Collider lastNonStompableCollider; // the last collider we checked that had no Stompable (usually the floor)

    // Awake runs once, when the object is created (or right away when PlayerController adds this script).
    private void Awake()
    {
        if (player == null) player = GetComponent<PlayerController>(); // not dragged in: take the one on this object

        if (player == null) // still nothing: this script can't work
        {
            Debug.LogError("PlayerStomp needs a PlayerController on the same object.", this);
            enabled = false; // switch this script off so it doesn't throw errors every frame
        }
    }

    // OnEnable runs when the script is switched on: start listening for "the player landed".
    private void OnEnable()
    {
        if (player != null) player.Landed += EndCombo; // PlayerController.UpdateGrounded raises Landed -> EndCombo runs
    }

    // OnDisable runs when the script is switched off or destroyed: stop listening (otherwise the event would point at a dead script).
    private void OnDisable()
    {
        if (player != null) player.Landed -= EndCombo;
    }

    // Unity calls this on the CharacterController's object every time it bumps into something while moving
    // (during controller.Move in PlayerController.MoveCharacter). hit.normal points OUT of the surface we touched:
    // pointing up means we are on TOP of the thing.
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.collider == lastNonStompableCollider) return;             // already know this is not an enemy (the floor): skip the search
        if (Time.time - lastStompTime < stompCooldownSeconds) return;      // just stomped: ignore this extra touch
        if (hit.normal.y < minTopNormalY) return;                          // we hit its side, not its top
        if (player.VerticalVelocity > 0f) return;                          // still going up (jumping into it from below/side): not a stomp

        Stompable stompable = hit.collider.GetComponentInParent<Stompable>(); // look on the thing we hit, and its parents, for a Stompable
        if (stompable == null)                                             // not stompable (floor, box, wall top...)
        {
            lastNonStompableCollider = hit.collider;                       // remember it so we don't search it again next frame
            return;
        }
        if (!stompable.isActiveAndEnabled) return;                         // a switched-off Stompable (the boss before its finale) can't be stomped

        lastStompTime = Time.time;                                         // start the cooldown

        IDamageable target = hit.collider.GetComponentInParent<IDamageable>(); // the enemy's health (Damageable / BossHealth)
        if (target != null) target.TakeDamage(stompable.stompDamage, Vector3.down); // hurt it; the hit comes from above, so the direction is down

        player.Launch(player.jumpHeight);                                  // bounce exactly as high as a normal jump (Launch always reaches full height)

        ComboCount++;                                                      // one more stomp in this chain
        if (onStomp != null) onStomp.Invoke();                             // play the sounds / effects hooked up in the Inspector
    }

    // Called by PlayerController's Landed event: the player touched the ground, so the chain is over.
    private void EndCombo()
    {
        if (ComboCount == 0) return;                                 // no stomps happened: nothing to end

        ComboCount = 0;                                              // reset for the next chain
        if (onComboEnded != null) onComboEnded.Invoke();             // let the Inspector events know (score popup, sound...)
    }
}
