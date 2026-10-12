using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// "Memory Main": cannot be collected until the level's small memories are all collected.
/// While locked it shows a semi-transparent "ghost" material; when unlocked, the model's own
/// materials come back. Swapping MATERIALS (not the model) means this works with a placeholder sphere
/// today and your Blender model later, with no code changes.
/// </summary>
public class MemoryMain : MemoryPickup
{
    [Header("Locked look")]
    [Tooltip("A semi-transparent material shown while locked. EVERY material slot of the model is replaced " +
             "with it. Make it with Surface Type = Transparent (URP) or Rendering Mode = Fade (Standard).")]
    [SerializeField] private Material lockedMaterial;

    [Header("Events (hook VFX / sound / UI here)")]
    [SerializeField] private UnityEvent onUnlocked;
    [Tooltip("Fires when the player touches it while it is still locked. Later: a 'collect 3 small memories' hint.")]
    [SerializeField] private UnityEvent onTouchedWhileLocked;

    private readonly List<Renderer> renderers = new List<Renderer>();
    private readonly List<Material[]> unlockedMaterials = new List<Material[]>();

    protected override bool CanBeCollected => Tracker != null && Tracker.IsMainUnlocked;

    protected override void Awake()
    {
        base.Awake();

        // Remember the model's real materials, so we can put them back when it unlocks.
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;   // skip particles, trails...
            renderers.Add(r);
            unlockedMaterials.Add(r.sharedMaterials);
        }
    }

    protected override void Start()
    {
        base.Start();
        if (Tracker == null) return;

        Tracker.MainUnlocked += HandleMainUnlocked;
        ApplyLockedLook(!Tracker.IsMainUnlocked);   // boss level: already unlocked, so it starts solid
    }

    private void OnDestroy()
    {
        if (Tracker != null) Tracker.MainUnlocked -= HandleMainUnlocked;
    }

    protected override void HandleCollected() => Tracker.RegisterMainCollected();

    protected override void HandleCollectBlocked() => onTouchedWhileLocked?.Invoke();

    private void HandleMainUnlocked()
    {
        ApplyLockedLook(false);
        onUnlocked?.Invoke();
    }

    private void ApplyLockedLook(bool locked)
    {
        if (lockedMaterial == null)
        {
            if (locked) Debug.LogWarning($"{name}: no Locked Material assigned, so it won't look semi-transparent.", this);
            return;
        }

        for (int i = 0; i < renderers.Count; i++)
        {
            if (!locked)
            {
                renderers[i].sharedMaterials = unlockedMaterials[i];
                continue;
            }

            Material[] ghost = new Material[unlockedMaterials[i].Length];
            for (int slot = 0; slot < ghost.Length; slot++) ghost[slot] = lockedMaterial;
            renderers[i].sharedMaterials = ghost;
        }
    }
}
