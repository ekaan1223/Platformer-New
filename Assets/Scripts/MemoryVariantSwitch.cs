using UnityEngine;        // gives us access to Unity classes (MonoBehaviour, GameObject, etc.)
using UnityEngine.Events; // gives us UnityEvent (the event lists you can fill in the Inspector)

// MEMORY VARIANT SWITCH
// For the boss level's memory: checks MemoryProgressStore for the main memories of the listed levels and shows
// ONE of two versions. Put the two versions in as child objects (different model, glow, text...) and drag them
// into the two slots. Other scripts (e.g. the comic player later) can read AllCollected to pick the matching ending.

public class MemoryVariantSwitch : MonoBehaviour // MonoBehaviour = a script Unity can put on an object
{
    [Header("Which main memories count (must match each level's Level Id)")]
    [SerializeField] private string[] requiredLevelIds = { "Level1", "Level2", "Level3" };

    [Header("What it shows")]
    [Tooltip("Shown when EVERY level above has its main memory collected.")]
    [SerializeField] private GameObject allCollectedVersion;
    [Tooltip("Shown when at least one is missing.")]
    [SerializeField] private GameObject missingVersion;

    [Header("Events")]
    [SerializeField] private UnityEvent onAllCollected;
    [SerializeField] private UnityEvent onSomeMissing;

    private bool allCollected; // the answer worked out in Awake

    // True if every listed level's main memory was collected. Other scripts can read it.
    public bool AllCollected
    {
        get { return allCollected; }
    }

    // Awake runs once, when the object is created: pick the version to show.
    private void Awake()
    {
        allCollected = MemoryProgressStore.AreAllCollected(requiredLevelIds);

        if (allCollectedVersion != null) allCollectedVersion.SetActive(allCollected);
        if (missingVersion != null) missingVersion.SetActive(!allCollected);

        if (allCollected)
        {
            if (onAllCollected != null) onAllCollected.Invoke();
        }
        else
        {
            if (onSomeMissing != null) onSomeMissing.Invoke();
        }
    }
}
