using System.Collections.Generic; // gives us List
using UnityEngine;                // gives us access to Unity classes (Renderer, Material, etc.)
using UnityEngine.Events;         // gives us UnityEvent (the event lists you can fill in the Inspector)

// MEMORY MAIN
// "Memory Main": can't be collected until the level's small memories are all collected.
// While locked it shows a semi-transparent "ghost" material; when unlocked, the model's own materials come back.
// Swapping MATERIALS (not the model) means this works with a placeholder sphere today and your Blender model
// later, with no code changes.

public class MemoryMain : MemoryPickup // built on MemoryPickup (touching, sound, fade, removal)
{
    [Header("Locked look")]
    [Tooltip("A semi-transparent material shown while locked. EVERY material slot of the model is replaced " +
             "with it. Make it with Surface Type = Transparent (URP) or Rendering Mode = Fade (Standard).")]
    [SerializeField] private Material lockedMaterial;
    [Tooltip("The conversation shown when the player touches it while it is locked (e.g. 'I need the other memories first'). Empty = no text.")]
    [SerializeField] private DialogueData lockedHint;

    [Header("Events (hook VFX / sound / UI here)")]
    [SerializeField] private UnityEvent onUnlocked;
    [Tooltip("Fires when the player touches it while it is still locked. Later: a 'collect 3 small memories' hint.")]
    [SerializeField] private UnityEvent onTouchedWhileLocked;

    private List<Renderer> renderers = new List<Renderer>();             // the model's parts
    private List<Material[]> unlockedMaterials = new List<Material[]>(); // each part's real materials, to put back on unlock

    // It can be collected once the tracker says the main memory is unlocked.
    protected override bool CanBeCollected
    {
        get { return Tracker != null && Tracker.IsMainUnlocked; }
    }

    // Awake runs once, when the object is created.
    protected override void Awake()
    {
        base.Awake(); // MemoryPickup's own Awake first (colliders, sound)

        // Remember the model's real materials, so we can put them back when it unlocks.
        Renderer[] found = GetComponentsInChildren<Renderer>();
        for (int i = 0; i < found.Length; i++)
        {
            Renderer r = found[i];
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue; // skip particles, trails...
            renderers.Add(r);
            unlockedMaterials.Add(r.sharedMaterials);
        }
    }

    // Start runs once, after every Awake.
    protected override void Start()
    {
        base.Start();
        if (Tracker == null) return;

        Tracker.MainUnlocked += HandleMainUnlocked; // "when the main memory unlocks, call HandleMainUnlocked"
        ApplyLockedLook(!Tracker.IsMainUnlocked);   // boss level: already unlocked, so it starts solid
    }

    // OnDestroy runs when the memory goes away: stop listening.
    private void OnDestroy()
    {
        if (Tracker != null) Tracker.MainUnlocked -= HandleMainUnlocked;
    }

    // Collecting it: this level's memories are complete.
    protected override void HandleCollected()
    {
        Tracker.RegisterMainCollected();
    }

    // Touched while still locked: show the hint.
    protected override void HandleCollectBlocked()
    {
        if (lockedHint != null && DialoguePlayer.Current != null) DialoguePlayer.Current.Play(lockedHint, false);
        if (onTouchedWhileLocked != null) onTouchedWhileLocked.Invoke();
    }

    // The tracker calls this when enough small memories are collected.
    private void HandleMainUnlocked()
    {
        ApplyLockedLook(false);
        if (onUnlocked != null) onUnlocked.Invoke();
    }

    // locked = true: every material slot shows the ghost material. false: the real materials come back.
    private void ApplyLockedLook(bool locked)
    {
        if (lockedMaterial == null)
        {
            if (locked) Debug.LogWarning(name + ": no Locked Material assigned, so it won't look semi-transparent.", this);
            return;
        }

        for (int i = 0; i < renderers.Count; i++)
        {
            if (!locked)
            {
                renderers[i].sharedMaterials = unlockedMaterials[i]; // the real materials
                continue;
            }

            Material[] ghost = new Material[unlockedMaterials[i].Length]; // as many slots as the real model has
            for (int slot = 0; slot < ghost.Length; slot++) ghost[slot] = lockedMaterial;
            renderers[i].sharedMaterials = ghost;
        }
    }
}
