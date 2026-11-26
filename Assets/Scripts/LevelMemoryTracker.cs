using UnityEngine;        // gives us access to Unity classes (MonoBehaviour, Debug, etc.)
using UnityEngine.Events; // gives us UnityEvent (the event lists you can fill in the Inspector)

// LEVEL MEMORY TRACKER
// One per level. Counts the small memories collected, unlocks the main memory when enough are collected, and
// announces what happened. It does not know what a memory looks like or what should happen next: hook that up
// with the events (environment change, comic, next level...).
// Boss level: set Small Memories Required to 0 and the main memory is unlocked from the start.

[DefaultExecutionOrder(-50)] // runs before the pickups, so they can find it
public class LevelMemoryTracker : MonoBehaviour // MonoBehaviour = a script Unity can put on an object
{
    // The tracker of the level that is loaded right now (pickups, the counter UI and the boss find it through this).
    public static LevelMemoryTracker Current;

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

    // "Shouts" for scripts (MemoryCounterUI shows "1 / 3", BossController waits for the arena memories).
    public event System.Action<int, int> SmallCountChanged; // (collected, required)
    public event System.Action MainUnlocked;
    public event System.Action MainCollected;

    private int smallCollected;   // how many small memories were collected so far
    private bool mainCollected;   // true once the main memory was collected

    // Read-only values for other scripts.
    public string LevelId
    {
        get { return levelId; }
    }

    public int SmallCollected
    {
        get { return smallCollected; }
    }

    public int SmallRequired
    {
        get { return smallMemoriesRequired; }
    }

    public bool IsMainUnlocked
    {
        get { return smallCollected >= smallMemoriesRequired; }
    }

    public bool IsMainCollected
    {
        get { return mainCollected; }
    }

    // Awake runs once, when the object is created: become the level's tracker.
    private void Awake()
    {
        if (Current != null && Current != this)
            Debug.LogWarning("There is more than one LevelMemoryTracker in the scene. Only one is allowed.", this);

        Current = this;
    }

    // OnDestroy runs when the level unloads.
    private void OnDestroy()
    {
        if (Current == this) Current = null;
    }

    // MemorySmall calls this when it is collected.
    public void RegisterSmallCollected()
    {
        if (IsMainUnlocked) return; // already have enough; extra small memories change nothing

        smallCollected++;
        if (Application.isEditor) Debug.Log("[" + levelId + "] Small memory " + smallCollected + "/" + smallMemoriesRequired, this);

        if (SmallCountChanged != null) SmallCountChanged(smallCollected, smallMemoriesRequired);
        if (onSmallMemoryCollected != null) onSmallMemoryCollected.Invoke();

        if (smallCollected == smallMemoriesRequired) // that was the last one needed
        {
            if (Application.isEditor) Debug.Log("[" + levelId + "] Main memory unlocked!", this);
            if (MainUnlocked != null) MainUnlocked();
            if (onMainUnlocked != null) onMainUnlocked.Invoke();
        }
    }

    // MemoryMain calls this when it is collected.
    public void RegisterMainCollected()
    {
        if (mainCollected) return;

        mainCollected = true;
        MemoryProgressStore.MarkMainCollected(levelId); // remembered even after this scene is gone
        if (Application.isEditor) Debug.Log("[" + levelId + "] Main memory collected. Level memories complete.", this);

        if (MainCollected != null) MainCollected();
        if (onMainCollected != null) onMainCollected.Invoke();
    }
}
