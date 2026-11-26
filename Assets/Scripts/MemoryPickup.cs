using UnityEngine;        // gives us access to Unity classes (MonoBehaviour, Collider, AudioSource, etc.)
using UnityEngine.Events; // gives us UnityEvent (the event lists you can fill in the Inspector)

// MEMORY PICKUP
// The shared behaviour of every memory collectible: notice the player touching it, fade its sound out, hide it,
// and remove it. WHAT collecting means is left to the two child scripts, MemorySmall and MemoryMain.
// That is "inheritance": one parent class, two children that fill in the blanks ("abstract" = this one is only
// the parent, it can't be put on an object by itself).
// Needs a trigger collider (a SphereCollider is added automatically and set to Is Trigger).
// The player is recognised through PlayerController.IsPlayer, so no tag is needed.

[RequireComponent(typeof(SphereCollider))] // Unity adds a SphereCollider automatically if the object has none
public abstract class MemoryPickup : MonoBehaviour // MonoBehaviour = a script Unity can put on an object
{
    [Header("References (found automatically if empty)")]
    [SerializeField] private MemoryAudio memoryAudio;

    [Header("Collecting")]
    [Tooltip("The looping sound fades out over this long when collected; the object disappears afterwards.")]
    [SerializeField, Min(0f)] private float audioFadeOutSeconds = 0.6f;
    [Tooltip("Played once when collected (a chime, a whoosh...). Empty = silent. Each prefab can have its own.")]
    [SerializeField] private AudioClip collectSound;
    [SerializeField, Range(0f, 1f)] private float collectVolume = 1f;
    [Tooltip("The conversation shown when collected (this memory's lines). Empty = no text. Needs a DialoguePlayer in the scene.")]
    [SerializeField] private DialogueData collectDialogue;

    [Header("Events (hook VFX / sound / UI here)")]
    [SerializeField] private UnityEvent onCollected;

    private Collider[] colliders; // our own colliders, switched off when collected
    private bool collected;       // true once collected, so it can't be collected twice

    // The level's memory counter (the children use it).
    protected LevelMemoryTracker Tracker
    {
        get { return LevelMemoryTracker.Current; }
    }

    // The children fill these in:
    protected abstract bool CanBeCollected { get; }    // may it be collected right now?
    protected abstract void HandleCollected();         // what collecting does (count a small one, finish the level...)
    protected virtual void HandleCollectBlocked() { }  // touched while it can't be collected (empty = nothing happens)

    // Awake runs once, when the object is created. "virtual" = MemoryMain adds its own steps to it.
    protected virtual void Awake()
    {
        colliders = GetComponents<Collider>();
        for (int i = 0; i < colliders.Length; i++) colliders[i].isTrigger = true; // a pickup must never block the player

        if (memoryAudio == null) memoryAudio = GetComponentInChildren<MemoryAudio>();
    }

    // Start runs once, after every Awake (so the tracker exists by now).
    protected virtual void Start()
    {
        if (Tracker == null) Debug.LogError(name + ": there is no LevelMemoryTracker in the scene. Add one to an empty object.", this);
    }

    // Unity calls this when something walks into the trigger.
    private void OnTriggerEnter(Collider other)
    {
        if (collected || !PlayerController.IsPlayer(other)) return; // already collected, or not the player
        if (!TryCollect()) HandleCollectBlocked();                  // locked: let the child react (a hint)
    }

    // Stay matters for the locked main memory: if you are standing inside it when it unlocks, Enter will not fire
    // again, but Stay still does. (IsPlayer is cheap, so checking every physics step is fine.)
    private void OnTriggerStay(Collider other)
    {
        if (collected || !PlayerController.IsPlayer(other)) return;
        TryCollect();
    }

    // Collects the memory if it is allowed. Returns false if it is still locked.
    private bool TryCollect()
    {
        if (collected || !CanBeCollected) return false;

        collected = true;
        for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = false; // no more touches

        Renderer[] renderers = GetComponentsInChildren<Renderer>(); // once, at the moment of collecting
        for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = false; // hide the model

        Light[] lights = GetComponentsInChildren<Light>();
        for (int i = 0; i < lights.Length; i++) lights[i].enabled = false; // a glow placed on the memory goes out with it

        if (memoryAudio != null) memoryAudio.FadeOutAndStop(audioFadeOutSeconds);

        // PlayClipAtPoint makes its own temporary speaker, so the sound keeps playing after this object is destroyed.
        // Played at the camera, so it is always full volume (the camera has the AudioListener).
        if (collectSound != null)
        {
            Vector3 where = transform.position;
            Camera mainCamera = Camera.main; // a search, but only once per collected memory
            if (mainCamera != null) where = mainCamera.transform.position;
            AudioSource.PlayClipAtPoint(collectSound, where, collectVolume);
        }

        HandleCollected();
        ShowCollectDialogue();
        if (onCollected != null) onCollected.Invoke();

        Destroy(gameObject, audioFadeOutSeconds); // wait for the sound to finish fading before the object disappears
        return true;
    }

    // The conversation lives on DialoguePlayer, so it keeps playing after this object is destroyed.
    private void ShowCollectDialogue()
    {
        if (collectDialogue == null) return;

        if (DialoguePlayer.Current == null)
        {
            Debug.LogWarning(name + ": there is no DialoguePlayer in the scene, so its text can't show.", this);
            return;
        }

        DialoguePlayer.Current.Stop();                       // a memory's lines replace whatever is on screen (a "Press E" prompt, a hint...)
        DialoguePlayer.Current.Play(collectDialogue, false); // false = don't freeze the game
    }

    // Reset() runs in the editor when the script is first added: sensible trigger defaults.
    private void Reset()
    {
        SphereCollider sphere = GetComponent<SphereCollider>();
        if (sphere != null)
        {
            sphere.isTrigger = true;
            sphere.radius = 0.25f;
        }
    }
}
