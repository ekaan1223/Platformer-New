using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Remembers which levels' MAIN memories the player has collected, across scene loads.
/// "static" means there is exactly one of it, living outside any scene, so it survives LoadScene
/// (a normal MonoBehaviour is destroyed with its scene). The boss level reads it to decide
/// which version of its memory to show.
/// It is cleared when the game closes. When you add saving to disk, THIS is the one place to change.
/// </summary>
public static class MemoryProgressStore
{
    private static readonly HashSet<string> collectedMainLevels = new HashSet<string>();

    public static int MainCollectedCount => collectedMainLevels.Count;

    public static void MarkMainCollected(string levelId)
    {
        if (!string.IsNullOrEmpty(levelId)) collectedMainLevels.Add(levelId);
    }

    public static bool IsMainCollected(string levelId) => collectedMainLevels.Contains(levelId);

    /// <summary>True only if EVERY level id in the list has its main memory collected.</summary>
    public static bool AreAllCollected(IEnumerable<string> levelIds)
    {
        foreach (string levelId in levelIds)
        {
            if (!collectedMainLevels.Contains(levelId)) return false;
        }
        return true;
    }

    /// <summary>Forget everything (e.g. a "New Game" button).</summary>
    public static void ResetAll() => collectedMainLevels.Clear();

    // Static data normally resets when Unity reloads scripts on entering Play mode. If you ever turn
    // that off (Project Settings > Editor > Enter Play Mode Options), this keeps each Play session clean.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearOnPlay() => collectedMainLevels.Clear();
}
