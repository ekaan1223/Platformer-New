using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Goomba stomp: land on top of a Stompable enemy while falling and you damage it and get a small
/// bounce. The bounce is short enough that you can chain stomps from enemy to enemy without touching
/// the ground; the number of stomps in a row is the combo. Touching the ground ends the combo.
/// Like the other abilities it never moves the character itself: it just sets motor.VerticalVelocity.
/// Put it on the Player (same object as PlayerMotor).
/// </summary>
public class PlayerStomp : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerMotor motor;

    [Header("Stomp Bounce")]
    [Tooltip("How high the hop after a stomp goes, in meters. Your normal jump height is 0.8.")]
    [SerializeField, Min(0f)] private float bounceHeightMeters = 0.45f;
    [Tooltip("Extra bounce height added for every stomp already in the combo (0 = every stomp bounces the same).")]
    [SerializeField, Min(0f)] private float bonusHeightPerComboMeters = 0f;
    [Tooltip("The extra height from combos never goes above this.")]
    [SerializeField, Min(0f)] private float maxBonusHeightMeters = 0.4f;

    [Header("Detection")]
    [Tooltip("How flat the surface you land on must be. 1 = perfectly flat only, 0.5 = up to about 60 degrees.")]
    [SerializeField, Range(0f, 1f)] private float minTopNormalY = 0.5f;
    [Tooltip("Ignore further stomps for this long, so one landing can't count twice.")]
    [SerializeField, Min(0f)] private float stompCooldownSeconds = 0.08f;

    [Header("Events (hook sound / VFX / score here)")]
    [SerializeField] private UnityEvent onStomp;
    [SerializeField] private UnityEvent onComboEnded;

    private float lastStompTime = float.NegativeInfinity;

    /// <summary>Stomps in a row since the last time the player touched the ground.</summary>
    public int ComboCount { get; private set; }

    /// <summary>Raised whenever the combo changes (a UI counter later). 0 = the combo ended.</summary>
    public event System.Action<int> ComboChanged;

    private void Awake()
    {
        if (motor == null) TryGetComponent(out motor);

        if (motor == null)
        {
            Debug.LogError($"{nameof(PlayerStomp)} needs a {nameof(PlayerMotor)}.", this);
            enabled = false;
        }
    }

    private void OnEnable()
    {
        if (motor != null) motor.Landed += EndCombo;
    }

    private void OnDisable()
    {
        if (motor != null) motor.Landed -= EndCombo;
    }

    // Unity calls this on the CharacterController's object every time it bumps into something
    // while moving. hit.normal points OUT of the surface we touched: pointing up = we are on top of it.
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (Time.time - lastStompTime < stompCooldownSeconds) return;
        if (hit.normal.y < minTopNormalY) return;      // we hit its side, not its top
        if (motor.VerticalVelocity > 0f) return;       // still going up, can't stomp

        Stompable stompable = hit.collider.GetComponentInParent<Stompable>();
        if (stompable == null) return;                 // only Stompable things can be stomped

        lastStompTime = Time.time;

        IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
        if (target != null) target.TakeDamage(stompable.StompDamage, Vector3.down);

        Bounce();
        ComboCount++;
        ComboChanged?.Invoke(ComboCount);
        onStomp?.Invoke();
    }

    private void Bounce()
    {
        float bonus = Mathf.Min(ComboCount * bonusHeightPerComboMeters, maxBonusHeightMeters);
        float height = bounceHeightMeters + bonus;

        // Same maths as PlayerJump: v = sqrt(2 * g * h) is "how fast to leave the ground to reach height h".
        motor.VerticalVelocity = Mathf.Sqrt(2f * motor.Gravity * height);
    }

    private void EndCombo()
    {
        if (ComboCount == 0) return;

        Debug.Log($"Stomp combo ended at {ComboCount}", this);
        ComboCount = 0;
        ComboChanged?.Invoke(0);
        onComboEnded?.Invoke();
    }
}
