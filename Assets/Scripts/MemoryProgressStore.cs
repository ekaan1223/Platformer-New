using System.Collections.Generic; // gives us List
using UnityEngine;                // gives us RuntimeInitializeOnLoadMethod

// MEMORY PROGRESS STORE
// Remembers which levels' MAIN memories the player has collected, across scene loads.
// "static" means there is exactly one of it, living outside any scene, so it survives LoadScene
// (a normal MonoBehaviour is destroyed with its scene). The boss level reads it (MemoryVariantSwitch) to decide
// which version of its memory to show.
// It is cleared when the game closes. When you add saving to disk, THIS is the one place to change.

public static class MemoryProgressStore
{
    private static List<string> collectedMainLevels = new List<string>(); // the Level Ids whose main memory was collected

    // How many main memories were collected.
    public static int MainCollectedCount
    {
        get { return collectedMainLevels.Count; }
    }

    // LevelMemoryTracker calls this when a main memory is collected.
    public static void MarkMainCollected(string levelId)
    {
        if (string.IsNullOrEmpty(levelId)) return;                         // no name: nothing to remember
        if (!collectedMainLevels.Contains(levelId)) collectedMainLevels.Add(levelId); // each level only once
    }

    // True if this level's main memory was collected.
    public static bool IsMainCollected(string levelId)
    {
        return collectedMainLevels.Contains(levelId);
    }

    // True only if EVERY level id in the list has its main memory collected.
    public static bool AreAllCollected(string[] levelIds)
    {
        for (int i = 0; i < levelIds.Length; i++)
        {
            if (!collectedMainLevels.Contains(levelIds[i])) return false; // one is missing
        }
        return true;
    }

    // Forget everything (the "New Game" button).
    public static void ResetAll()
    {
        collectedMainLevels.Clear();
    }

    // Static data normally resets when Unity reloads scripts on entering Play mode. If you ever turn that off
    // (Project Settings > Editor > Enter Play Mode Options), this keeps each Play session clean.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearOnPlay()
    {
        collectedMainLevels.Clear();
    }
}
