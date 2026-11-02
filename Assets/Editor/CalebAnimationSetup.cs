using System.Collections.Generic;      // gives us HashSet, for the files whose clips should loop
using System.Linq;                     // gives us OfType, Where and OrderByDescending
using UnityEditor;                     // gives us AssetDatabase, ModelImporter and the menu item
using UnityEditor.Animations;          // gives us AnimatorController
using UnityEditor.SceneManagement;     // gives us EditorSceneManager, to save the scene
using UnityEngine;                     // gives us access to Unity classes (Avatar, AnimationClip, Animator, etc.)

// CALEB ANIMATION SETUP
// One click (Tools > Set Up Caleb Animations) makes the Mixamo clips play on Caleb. Safe to click again.
//
// What it does:
//   1. Imports the Mixamo clips as Humanoid, copying their Avatar from the Caleb model.
//   2. Makes a separate controller (playerCaleb.controller) with those clips, so Puppet's player.controller stays as it is.
//   3. In the open scene: puts that controller on the player and moves Caleb's model onto the player's collider.
//
// Clips can only be picked through Unity's own API: their IDs inside the FBX files are made by the importer.

public static class CalebAnimationSetup
{
    private const string CalebPath = "Assets/NEWCHARS/SM_CharacterCaleb 1.fbx";
    private const string ClipFolder = "Assets/NEWCHARS/Denenecek animasyonlar";
    private const string Tried = ClipFolder + "/";
    private const string WalkPath = "Assets/Animation/Walking.fbx"; // already Humanoid with its own Avatar: only its loop setting changes
    private const string PuppetControllerPath = "Assets/Animation/player.controller";
    private const string CalebControllerPath = "Assets/Animation/playerCaleb.controller";
    private const float DashStateSpeed = 1.5f; // there is no dash clip, so Dash is the run played faster

    // Which file each Animator state plays. The state names are the ones PlayerController asks for.
    private static readonly (string state, string path, bool loop)[] StateClips =
    {
        ("Idle", Tried + "Idle.fbx", true),
        ("Walk", WalkPath, true),
        ("Run", Tried + "Fast Run.fbx", true),
        ("Jump", Tried + "Jumping.fbx", false),
        ("DoubleJump", Tried + "Sword And Shield Jump.fbx", false),
        ("Fall", Tried + "Falling To Landing.fbx", false),
        ("Dash", Tried + "Fast Run.fbx", true),
        ("Attack", Tried + "Sword And Shield Slash.fbx", false),
        ("Hit", Tried + "Sword And Shield Impact.fbx", false),
        ("Death", Tried + "Sword And Shield Death.fbx", false),
    };

    [MenuItem("Tools/Set Up Caleb Animations")]
    private static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[CalebSetup] Stop Play mode first.");
            return;
        }

        Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(CalebPath).OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError("[CalebSetup] " + CalebPath + " has no valid Humanoid Avatar. Check its Rig tab. Nothing was changed.");
            return;
        }

        ImportClips(avatar);

        AnimatorController controller = BuildController();
        if (controller == null) return;

        SetUpScene(controller);
    }

    // Step 1: every clip file becomes Humanoid with Caleb's Avatar, and the movement stays on the player (not in the clip).
    private static void ImportClips(Avatar avatar)
    {
        HashSet<string> loopingFiles = new HashSet<string>(StateClips.Where(entry => entry.loop).Select(entry => entry.path));

        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ClipFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null) continue;

            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = avatar;
            ApplyClipSettings(importer, loopingFiles.Contains(path));
            importer.SaveAndReimport();
        }

        ModelImporter walkImporter = AssetImporter.GetAtPath(WalkPath) as ModelImporter;
        if (walkImporter != null)
        {
            ApplyClipSettings(walkImporter, true);
            walkImporter.SaveAndReimport();
        }
    }

    // PlayerController moves the player (applyRootMotion is off), so each clip keeps its body where the clip has it.
    private static void ApplyClipSettings(ModelImporter importer, bool loop)
    {
        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;

        foreach (ModelImporterClipAnimation clip in clips)
        {
            clip.loopTime = loop;
            clip.lockRootRotation = true;
            clip.keepOriginalOrientation = true;
            clip.lockRootHeightY = true;
            clip.keepOriginalPositionY = true;
            clip.lockRootPositionXZ = true;
            clip.keepOriginalPositionXZ = true;
        }

        importer.clipAnimations = clips;
    }

    // Step 2: a copy of Puppet's controller, so the state names stay the same, with Caleb's clips in its states.
    private static AnimatorController BuildController()
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(CalebControllerPath) == null &&
            !AssetDatabase.CopyAsset(PuppetControllerPath, CalebControllerPath))
        {
            Debug.LogError("[CalebSetup] Could not copy " + PuppetControllerPath + ".");
            return null;
        }

        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(CalebControllerPath);
        ChildAnimatorState[] states = controller.layers[0].stateMachine.states;

        foreach ((string stateName, string path, bool loop) in StateClips)
        {
            AnimatorState state = states.Select(child => child.state).FirstOrDefault(candidate => candidate.name == stateName);
            AnimationClip clip = FindClip(path);

            if (state == null || clip == null)
            {
                Debug.LogError("[CalebSetup] Skipped '" + stateName + "': state or clip not found (" + path + ").");
                continue;
            }

            state.motion = clip;
            state.speed = stateName == "Dash" ? DashStateSpeed : 1f;
            Debug.Log("[CalebSetup] " + stateName + " -> " + clip.name + " from " + path + " (" + clip.length.ToString("0.00") +
                      " s, human=" + clip.isHumanMotion + ", loop=" + clip.isLooping + ")");
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    // The real clip in a file: the longest one, skipping Unity's hidden preview copies.
    private static AnimationClip FindClip(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .Where(clip => !clip.name.StartsWith("__preview__"))
            .OrderByDescending(clip => clip.length)
            .FirstOrDefault();
    }

    // Step 3: the player in the open scene gets the controller, and Caleb's model goes onto the collider.
    private static void SetUpScene(AnimatorController controller)
    {
        PlayerController player = Object.FindFirstObjectByType<PlayerController>();
        if (player == null)
        {
            Debug.LogError("[CalebSetup] No PlayerController in the open scene. Open Level 1.1 and click again.");
            return;
        }

        Avatar calebAvatar = AssetDatabase.LoadAllAssetsAtPath(CalebPath).OfType<Avatar>().First();
        Animator animator = player.GetComponentsInChildren<Animator>().FirstOrDefault(candidate => candidate.avatar == calebAvatar);
        if (animator == null)
        {
            Debug.LogError("[CalebSetup] Found no Animator with Caleb's Avatar under " + player.name + ". Is the model under the player?");
            return;
        }

        // An Animator added to the player itself would fight the model's own one. Only one added in the scene is removed, never a prefab's.
        Animator extraAnimator = player.GetComponent<Animator>();
        if (extraAnimator != null && extraAnimator != animator && PrefabUtility.IsAddedComponentOverride(extraAnimator))
        {
            Object.DestroyImmediate(extraAnimator);
            Debug.Log("[CalebSetup] Removed the extra Animator from " + player.name + ".");
        }

        player.animatorController = controller;
        PrefabUtility.RecordPrefabInstancePropertyModifications(player);

        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        PrefabUtility.RecordPrefabInstancePropertyModifications(animator);

        Transform model = animator.transform;
        model.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity); // the scale is left as it was set in the scene
        PrefabUtility.RecordPrefabInstancePropertyModifications(model);

        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        EditorSceneManager.SaveScene(player.gameObject.scene);

        Debug.Log("[CalebSetup] Done. " + player.name + " uses " + CalebControllerPath + ", " + model.name + " scale " + model.localScale.x + ".");
    }
}
