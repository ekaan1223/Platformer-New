using System.Linq;                     // gives us Where, OrderBy and FirstOrDefault
using TMPro;                           // gives us TMP_FontAsset
using UnityEditor;                     // gives us AssetDatabase, PrefabUtility and the menu item
using UnityEditor.SceneManagement;     // gives us EditorSceneManager, to open and save the levels
using UnityEngine;                     // gives us access to Unity classes (Material, SphereCollider, Renderer, etc.)
using UnityEngine.SceneManagement;     // gives us Scene

// MEMORY SETUP
// One click (Tools > Set Up Memories) applies the memory fixes to the prefabs and both levels. Safe to click again.
//
// Prefabs:
//   - MainMemory: uses the see-through Ghost material while locked, and says a hint when touched while locked.
//   - MemoryTracker: gets the on-screen counter (MemoryCounterUI).
//   - SmallMemory: its pickup zone is measured, and shrunk only if it is much bigger than the ball.
// Level 1.1: each memory plays its lines when collected, and the old dialogue boxes beside the memories are switched off.
// Level 2: its tracker gets its own Level Id, so its main memory no longer counts as Level 1's.

public static class MemorySetup
{
    private const string Level1Path = "Assets/Scenes/Level 1.1.unity";
    private const string Level2Path = "Assets/Scenes/Level 2.unity";
    private const string Level2Id = "Level2";

    private const string MainMemoryPrefabPath = "Assets/Prefabs Player/Memories/MainMemory.prefab";
    private const string SmallMemoryPrefabPath = "Assets/Memories/Prefabs/SmallMemory.prefab";
    private const string TrackerPrefabPath = "Assets/Prefabs Player/MemoryTracker.prefab";
    private const string GhostMaterialPath = "Assets/Ghost.mat";
    private const string CounterFontPath = "Assets/level1assets/VisualNovel/Font/Victorian Supremacy Demo SDF.asset";

    private const string DialogueFolder = "Assets/Dialouge/Dialogues/";
    private const string LockedHintPath = DialogueFolder + "MainMemoryLocked.asset";
    private const string LockedHintText = "\"I can't reach this memory yet... I need to find the other 3 first.\"";
    private static readonly string[] MemoryDialogues = { "FirstMemoryD", "SecondMemoryD", "ThirdMemoryD", "MainMemory" };

    private const float MaxDialogueBoxDistanceMeters = 3f; // a dialogue box further than this from every memory is not that memory's box
    private const float ZoneTooBigByMeters = 0.15f;        // the pickup zone may be this much bigger than the ball before it counts as too big
    private const float ZoneMarginMeters = 0.1f;           // a shrunk zone is this much bigger than the ball

    [MenuItem("Tools/Set Up Memories")]
    private static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[MemorySetup] Stop Play mode first.");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return; // the player pressed Cancel

        DialogueData lockedHint = GetOrCreateLockedHint();
        SetUpMainMemoryPrefab(lockedHint);
        SetUpTrackerPrefab();
        CheckSmallMemoryZone();
        AssetDatabase.SaveAssets();

        SetUpLevel1();
        SetUpLevel2();

        EditorSceneManager.OpenScene(Level1Path);
        Debug.Log("[MemorySetup] Done.");
    }

    // ---- Assets and prefabs ----------------------------------------------

    // Copies Ellis's portrait and border from the main memory's lines, so the hint looks like the rest of the dialogue.
    private static DialogueData GetOrCreateLockedHint()
    {
        DialogueData hint = AssetDatabase.LoadAssetAtPath<DialogueData>(LockedHintPath);
        if (hint != null) return hint;

        DialogueData mainLines = AssetDatabase.LoadAssetAtPath<DialogueData>(DialogueFolder + "MainMemory.asset");
        DialogueLine ellis = mainLines.lines.FirstOrDefault(line => line.speakerName == "Ellis");

        hint = ScriptableObject.CreateInstance<DialogueData>();
        hint.lines = new[]
        {
            new DialogueLine
            {
                speakerName = "Ellis",
                text = LockedHintText,
                portrait = ellis != null ? ellis.portrait : null,
                border = ellis != null ? ellis.border : null
            }
        };

        AssetDatabase.CreateAsset(hint, LockedHintPath);
        return hint;
    }

    private static void SetUpMainMemoryPrefab(DialogueData lockedHint)
    {
        Material ghost = AssetDatabase.LoadAssetAtPath<Material>(GhostMaterialPath);

        using (PrefabUtility.EditPrefabContentsScope prefab = new PrefabUtility.EditPrefabContentsScope(MainMemoryPrefabPath))
        {
            MemoryMain main = prefab.prefabContentsRoot.GetComponentInChildren<MemoryMain>();
            SerializedObject fields = new SerializedObject(main);
            fields.FindProperty("lockedMaterial").objectReferenceValue = ghost;
            fields.FindProperty("lockedHint").objectReferenceValue = lockedHint;
            fields.ApplyModifiedPropertiesWithoutUndo();
        }

        Debug.Log("[MemorySetup] MainMemory prefab: locked material = " + ghost.name + ", hint = " + lockedHint.name + ".");
    }

    private static void SetUpTrackerPrefab()
    {
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(CounterFontPath);

        using (PrefabUtility.EditPrefabContentsScope prefab = new PrefabUtility.EditPrefabContentsScope(TrackerPrefabPath))
        {
            LevelMemoryTracker tracker = prefab.prefabContentsRoot.GetComponentInChildren<LevelMemoryTracker>();
            MemoryCounterUI counter = tracker.GetComponent<MemoryCounterUI>();
            if (counter == null) counter = tracker.gameObject.AddComponent<MemoryCounterUI>();

            SerializedObject fields = new SerializedObject(counter);
            fields.FindProperty("font").objectReferenceValue = font;
            fields.ApplyModifiedPropertiesWithoutUndo();
        }

        Debug.Log("[MemorySetup] MemoryTracker prefab: counter added, font = " + (font != null ? font.name : "default") + ".");
    }

    // A Sphere Collider's radius is multiplied by the object's scale, so the Inspector number alone says little.
    private static void CheckSmallMemoryZone()
    {
        using (PrefabUtility.EditPrefabContentsScope prefab = new PrefabUtility.EditPrefabContentsScope(SmallMemoryPrefabPath))
        {
            SphereCollider zone = prefab.prefabContentsRoot.GetComponentInChildren<SphereCollider>();
            Renderer ball = prefab.prefabContentsRoot.GetComponentInChildren<Renderer>();
            float scale = zone.transform.lossyScale.x;
            float zoneRadius = zone.radius * scale;
            float ballRadius = ball.bounds.extents.x;

            if (zoneRadius > ballRadius + ZoneTooBigByMeters)
            {
                zone.radius = (ballRadius + ZoneMarginMeters) / scale;
                Debug.Log("[MemorySetup] SmallMemory zone shrunk: " + zoneRadius.ToString("0.00") + " m -> " + (zone.radius * scale).ToString("0.00") +
                          " m (ball " + ballRadius.ToString("0.00") + " m).");
            }
            else
            {
                Debug.Log("[MemorySetup] SmallMemory zone kept: " + zoneRadius.ToString("0.00") + " m (ball " + ballRadius.ToString("0.00") + " m).");
            }
        }
    }

    // ---- Levels ------------------------------------------------------------

    private static void SetUpLevel1()
    {
        Scene scene = EditorSceneManager.OpenScene(Level1Path);

        // The level had its own locked material, which would hide the prefab's Ghost material.
        foreach (MemoryMain main in Object.FindObjectsByType<MemoryMain>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SerializedProperty lockedMaterial = new SerializedObject(main).FindProperty("lockedMaterial");
            if (lockedMaterial.prefabOverride) PrefabUtility.RevertPropertyOverride(lockedMaterial, InteractionMode.AutomatedAction);
        }

        MemoryPickup[] pickups = Object.FindObjectsByType<MemoryPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        DialogueTrigger[] triggers = Object.FindObjectsByType<DialogueTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (string dialogueName in MemoryDialogues)
        {
            DialogueData dialogue = AssetDatabase.LoadAssetAtPath<DialogueData>(DialogueFolder + dialogueName + ".asset");
            DialogueTrigger box = triggers.FirstOrDefault(trigger => trigger.dialogue == dialogue);
            if (box == null)
            {
                Debug.LogWarning("[MemorySetup] No dialogue box plays " + dialogueName + " in Level 1.1. Skipped.");
                continue;
            }

            bool isMainLine = dialogueName == "MainMemory";
            MemoryPickup nearest = pickups
                .Where(pickup => (pickup is MemoryMain) == isMainLine)
                .OrderBy(pickup => Vector3.Distance(pickup.transform.position, box.transform.position))
                .FirstOrDefault();

            float distance = nearest != null ? Vector3.Distance(nearest.transform.position, box.transform.position) : float.MaxValue;
            if (distance > MaxDialogueBoxDistanceMeters)
            {
                Debug.LogWarning("[MemorySetup] " + box.name + " is not beside any memory. Skipped, and its box stays on.");
                continue;
            }

            SerializedObject fields = new SerializedObject(nearest);
            fields.FindProperty("collectDialogue").objectReferenceValue = dialogue;
            fields.ApplyModifiedPropertiesWithoutUndo();

            box.gameObject.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(box.gameObject);

            Debug.Log("[MemorySetup] " + nearest.name + " at " + nearest.transform.position + " now plays " + dialogueName +
                      " (" + distance.ToString("0.00") + " m from its old box, which is now off).");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void SetUpLevel2()
    {
        Scene scene = EditorSceneManager.OpenScene(Level2Path);

        LevelMemoryTracker tracker = Object.FindFirstObjectByType<LevelMemoryTracker>();
        if (tracker == null)
        {
            Debug.LogError("[MemorySetup] No LevelMemoryTracker in Level 2.");
            return;
        }

        SerializedObject fields = new SerializedObject(tracker);
        fields.FindProperty("levelId").stringValue = Level2Id;
        fields.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[MemorySetup] Level 2: Level Id = " + Level2Id + ".");
    }
}
