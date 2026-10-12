using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// For the boss level's memory: checks MemoryProgressStore for the main memories of the listed levels
/// and shows ONE of two versions. Put the two versions in as child objects (different model, glow,
/// text...) and drag them into the two slots. Other scripts (e.g. the comic player later) can read
/// AllCollected to pick the matching comic / ending.
/// </summary>
public class MemoryVariantSwitch : MonoBehaviour
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

    public bool AllCollected { get; private set; }

    private void Awake()
    {
        AllCollected = MemoryProgressStore.AreAllCollected(requiredLevelIds);

        if (allCollectedVersion != null) allCollectedVersion.SetActive(AllCollected);
        if (missingVersion != null) missingVersion.SetActive(!AllCollected);

        if (AllCollected) onAllCollected?.Invoke();
        else onSomeMissing?.Invoke();
    }
}
