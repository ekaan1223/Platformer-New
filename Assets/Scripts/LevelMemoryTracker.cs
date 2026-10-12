using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// One per level. Counts the small memories collected, unlocks the main memory when enough are
/// collected, and announces what happened. It does not know what a memory looks like or what should
/// happen next: hook that up with the events (environment change, comic, next level...).
/// Boss level: set Small Memories Required to 0 and the main memory is unlocked from the start.
/// </summary>
[DefaultExecutionOrder(-50)] // Runs before the pickups so they can find it.
public class LevelMemoryTracker : MonoBehaviour
{
    /// <summary>The tracker of the level that is currently loaded (pickups find it through this).</summary>
    public static LevelMemoryTracker Current { get; private set; }

    [Header("Level")]
    [Tooltip("A unique name for this level, e.g. Level1, Level2, Level3, Boss. " +
             "It is how the game remembers which main memories you collected.")]
    [SerializeField] private string levelId = "Level1";
    [Tooltip("How many small memories unlock the main memory. Boss level: 0.")]
    [SerializeField, Min(0)] private int smallMemoriesRequired = 3;

    [Header("Events (hook environment change / comic / next level here)")]
    [SerializeField] private UnityEvent onSmallMemoryCollected;
    [SerializeField] private UnityEvent onMainUnlocked;
    [Tooltip("The big one: fires when the main memory is collected, i.e. ALL memories of this level are done.")]
    [SerializeField] private UnityEvent onMainCollected;

    // C# events for scripts (a UI counter later: "1 / 3").
    public event Action<int, int> SmallCountChanged;   // (collected, required)
    public event Action MainUnlocked;
    public event Action MainCollected;

    public string LevelId => levelId;
    public int SmallCollected { get; private set; }
    public int SmallRequired => smallMemoriesRequired;
    public bool IsMainUnlocked => SmallCollected >= smallMemoriesRequired;
    public bool IsMainCollected { get; private set; }

    private void Awake()
    {
        if (Current != null && Current != this)
            Debug.LogWarning("There is more than one LevelMemoryTracker in the scene. Only one is allowed.", this);

        Current = this;
    }

    private void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    public void RegisterSmallCollected()
    {
        if (IsMainUnlocked) return;   // already have enough; extra small memories change nothing

        SmallCollected++;
        Debug.Log($"[{levelId}] Small memory {SmallCollected}/{smallMemoriesRequired}", this);

        SmallCountChanged?.Invoke(SmallCollected, smallMemoriesRequired);
        onSmallMemoryCollected?.Invoke();

        if (SmallCollected == smallMemoriesRequired)
        {
            Debug.Log($"[{levelId}] Main memory unlocked!", this);
            MainUnlocked?.Invoke();
            onMainUnlocked?.Invoke();
        }
    }

    public void RegisterMainCollected()
    {
        if (IsMainCollected) return;

        IsMainCollected = true;
        MemoryProgressStore.MarkMainCollected(levelId);   // remembered even after this scene is gone
        Debug.Log($"[{levelId}] Main memory collected. Level memories complete.", this);

        MainCollected?.Invoke();
        onMainCollected?.Invoke();
    }
}
