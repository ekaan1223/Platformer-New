// Auto-configures Ellis.fbx the FIRST time Unity imports it:
//  - Humanoid rig (avatar created from the model's T-pose)
//  - correct scale/axes from Blender
//  - clean clip names (Idle, Walk, ...) instead of "Ellis_Rig|Idle"
//  - Loop Time ON for Idle / Walk / Run / Fall
//  - root motion baked into pose (animations stay in place; your scripts move the player)
// After the first import it does nothing, so any change you make in the Inspector is kept.
// Put this file in any folder named "Editor".
using System.Linq;
using UnityEditor;
using UnityEngine;

public class EllisImportSettings : AssetPostprocessor
{
    static readonly string[] LoopingClips = { "Idle", "Walk", "Run", "Fall" };

    bool IsEllis => assetPath.Replace('\\', '/').EndsWith("/Ellis.fbx");

    void OnPreprocessModel()
    {
        if (!IsEllis || !assetImporter.importSettingsMissing) return;
        var mi = (ModelImporter)assetImporter;
        mi.globalScale = 1f;
        mi.useFileScale = true;
        mi.bakeAxisConversion = true;
        mi.importAnimation = true;
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
    }

    void OnPreprocessAnimation()
    {
        if (!IsEllis || !assetImporter.importSettingsMissing) return;
        var mi = (ModelImporter)assetImporter;
        var clips = mi.defaultClipAnimations;
        foreach (var clip in clips)
        {
            clip.name = clip.name.Split('|').Last();
            clip.loopTime = LoopingClips.Contains(clip.name);
            clip.lockRootRotation = true;      clip.keepOriginalOrientation = true;
            clip.lockRootHeightY = true;       clip.keepOriginalPositionY = true;
            clip.lockRootPositionXZ = true;    clip.keepOriginalPositionXZ = true;
        }
        mi.clipAnimations = clips;
        Debug.Log($"[Ellis] Imported {clips.Length} clips: {string.Join(", ", clips.Select(c => c.name))}");
    }
}
