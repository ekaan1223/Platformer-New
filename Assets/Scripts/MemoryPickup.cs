using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The shared behaviour of every memory collectible: notice the player touching it, fade its sound out,
/// hide it, and remove it. WHAT collecting means is left to the two child scripts, MemorySmall and
/// MemoryMain. That is "inheritance": one parent class, two children that fill in the blanks.
/// Needs a trigger collider (a SphereCollider is added automatically and set to Is Trigger).
/// The player is recognised by having a PlayerController, so no tag is needed.
/// </summary>
[RequireComponent(typeof(SphereCollider))]
public abstract class MemoryPickup : MonoBehaviour
{
    [Header("References (found automatically if empty)")]
    [SerializeField] private MemoryAudio memoryAudio;

    [Header("Collecting")]
    [Tooltip("The looping sound fades out over this long when collected; the object disappears afterwards.")]
    [SerializeField, Min(0f)] private float audioFadeOutSeconds = 0.6f;
    [Tooltip("Played once when collected (a chime, a whoosh...). Empty = silent. Each prefab can have its own.")]
    [SerializeField] private AudioClip collectSound;
    [SerializeField, Range(0f, 1f)] private float collectVolume = 1f;

    [Header("Events (hook VFX / sound / UI here)")]
    [SerializeField] private UnityEvent onCollected;

    private Collider[] colliders;
    private bool collected;

    protected LevelMemoryTracker Tracker => LevelMemoryTracker.Current;

    // The children fill these in:
    protected abstract bool CanBeCollected { get; }
    protected abstract void HandleCollected();
    protected virtual void HandleCollectBlocked() { }

    protected virtual void Awake()
    {
        colliders = GetComponents<Collider>();
        foreach (Collider c in colliders) c.isTrigger = true;   // a pickup must never block the player

        if (memoryAudio == null) memoryAudio = GetComponentInChildren<MemoryAudio>();
    }

    protected virtual void Start()
    {
        if (Tracker == null)
            Debug.LogError($"{name}: there is no LevelMemoryTracker in the scene. Add one to an empty object.", this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (collected || !IsPlayer(other)) return;
        if (!TryCollect()) HandleCollectBlocked();
    }

    // Stay matters for the locked main memory: if you are standing inside it when it unlocks,
    // Enter will not fire again, but Stay still does.
    private void OnTriggerStay(Collider other)
    {
        if (collected || !IsPlayer(other)) return;
        TryCollect();
    }

    private static bool IsPlayer(Collider other) => other.GetComponentInParent<PlayerController>() != null;

    private bool TryCollect()
    {
        if (collected || !CanBeCollected) return false;

        collected = true;
        foreach (Collider c in colliders) c.enabled = false;
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) r.enabled = false;

        if (memoryAudio != null) memoryAudio.FadeOutAndStop(audioFadeOutSeconds);

        // PlayClipAtPoint makes its own temporary speaker, so the sound keeps playing after this object is destroyed.
        // Played at the camera, so it is always full volume (the camera has the AudioListener).
        if (collectSound != null)
        {
            Vector3 where = Camera.main != null ? Camera.main.transform.position : transform.position;
            AudioSource.PlayClipAtPoint(collectSound, where, collectVolume);
        }

        HandleCollected();
        onCollected?.Invoke();

        // Wait for the sound to finish fading before the object (and its AudioSource) disappears.
        Destroy(gameObject, audioFadeOutSeconds);
        return true;
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
}
