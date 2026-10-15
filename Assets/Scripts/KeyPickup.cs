using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A collectible key. Walk into it and it goes into the player's KeyInventory.
/// Which key it is = the Key Type asset you drag in. Same prefab, different asset = a different key.
/// Needs a trigger collider (a SphereCollider is added and set to Is Trigger automatically).
/// The player is recognised by having a KeyInventory, so no tag is needed.
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public class KeyPickup : MonoBehaviour
{
    [Header("Key")]
    [Tooltip("Which key this is. Drag a Key Type asset here.")]
    [SerializeField] private KeyType keyType;
    [Tooltip("How many keys it gives.")]
    [SerializeField, Min(1)] private int amount = 1;

    [Header("Look")]
    [Tooltip("Paint the model with the key's colour. Turn OFF if your own model/material already has the right colour.")]
    [SerializeField] private bool tintWithKeyColor = true;
    [Tooltip("Which renderers to paint. Empty = every model under this object.")]
    [SerializeField] private Renderer[] tintRenderers;
    [Tooltip("The part that spins and floats. Empty = this whole object.")]
    [SerializeField] private Transform visual;
    [SerializeField] private float spinDegreesPerSecond = 90f;
    [Tooltip("How far it floats up and down, in meters. 0 = off.")]
    [SerializeField, Min(0f)] private float bobHeightMeters = 0.03f;
    [SerializeField, Min(0f)] private float bobSpeed = 2f;

    [Header("Collecting")]
    [Tooltip("The key disappears at once, but the object stays this long so a sound can finish playing.")]
    [SerializeField, Min(0f)] private float destroyDelaySeconds = 0.5f;
    [SerializeField] private bool logMessages = true;

    [Header("Events (hook sound / VFX / UI here)")]
    [SerializeField] private UnityEvent onCollected;

    private Collider[] colliders;
    private Vector3 visualStartLocalPosition;
    private float bobTimeOffset;
    private bool collected;

    private void Awake()
    {
        colliders = GetComponents<Collider>();
        foreach (Collider c in colliders) c.isTrigger = true;   // a pickup must never block the player

        if (visual == null) visual = transform;
        visualStartLocalPosition = visual.localPosition;
        bobTimeOffset = Random.value * Mathf.PI * 2f;           // keys placed side by side don't bob in sync

        if (keyType == null)
            Debug.LogWarning($"{name}: no Key Type assigned, so this key does nothing.", this);
        else if (tintWithKeyColor)
            KeyColorTint.Apply(tintRenderers, gameObject, keyType.Color);
    }

    private void Update()
    {
        if (collected) return;

        if (spinDegreesPerSecond != 0f)
            visual.Rotate(0f, spinDegreesPerSecond * Time.deltaTime, 0f, Space.Self);

        if (bobHeightMeters > 0f)
        {
            float offset = Mathf.Sin(Time.time * bobSpeed + bobTimeOffset) * bobHeightMeters;
            visual.localPosition = visualStartLocalPosition + Vector3.up * offset;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (collected || keyType == null) return;

        KeyInventory inventory = other.GetComponentInParent<KeyInventory>();
        if (inventory == null) return;   // not the player (an enemy walked over it, for example)

        collected = true;
        foreach (Collider c in colliders) c.enabled = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;

        inventory.Add(keyType, amount);
        if (logMessages) Debug.Log($"Picked up {amount} x {keyType.DisplayName}. Now carrying {inventory.GetCount(keyType)}.", this);

        onCollected?.Invoke();
        Destroy(gameObject, destroyDelaySeconds);
    }

    // Reset() runs in the editor when the script is first added: sensible trigger defaults.
    private void Reset()
    {
        if (TryGetComponent(out SphereCollider sphere))
        {
            sphere.isTrigger = true;
            sphere.radius = 0.25f;
        }
    }

    // A small marker in the Scene view in the key's colour, so you can see which key is which while building a level.
    private void OnDrawGizmos()
    {
        if (keyType == null) return;

        Gizmos.color = keyType.Color;
        Gizmos.DrawWireSphere(transform.position, 0.1f);
    }
}
