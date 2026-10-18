using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Builds the Animator Controller for you: drop your animation clips into the slots, press Build.
/// Open it with the menu:  Tools > Build Player Animator Controller.
/// (This file MUST sit inside a folder called "Editor" - it is a tool for the Unity editor, not part of the game.)
/// Only "Idle" is required; any slot you leave empty simply skips that state.
/// </summary>
public class PlayerAnimatorControllerBuilder : ScriptableWizard
{
    [Header("Walking / running (blended by speed)")]
    public AnimationClip idle;
    public AnimationClip walk;
    public AnimationClip run;
    [Tooltip("The player's walk speed (Max Speed Meters Per Second on PlayerMotor).")]
    public float walkSpeed = 2.5f;
    [Tooltip("The player's sprint speed (Sprint Speed Meters Per Second on PlayerMotor).")]
    public float runSpeed = 4f;

    [Header("In the air")]
    [Tooltip("Plays when the player jumps / is launched.")]
    public AnimationClip jump;
    [Tooltip("Plays while falling. Empty = the jump clip is reused.")]
    public AnimationClip fall;

    [Header("Actions")]
    public AnimationClip dash;
    public AnimationClip attack;
    [Tooltip("1 = normal. Raise it so the attack animation finishes as fast as your sword swing.")]
    public float attackAnimationSpeed = 1f;
    public AnimationClip hit;
    public AnimationClip death;

    [Header("Where to save")]
    public string folder = "Assets";
    public string controllerName = "PlayerAnimator";

    [MenuItem("Tools/Build Player Animator Controller")]
    private static void Open() =>
        DisplayWizard<PlayerAnimatorControllerBuilder>("Build Player Animator Controller", "Build");

    private void OnWizardUpdate()
    {
        isValid = idle != null;
        helpString = isValid
            ? "Drag each animation clip into its slot. (For a clip inside an FBX, expand the FBX and drag the clip itself.)"
            : "Drop at least an Idle clip.";
    }

    private void OnWizardCreate()
    {
        string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{controllerName}.controller");
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);

        controller.AddParameter(PlayerAnimParams.Speed, AnimatorControllerParameterType.Float);
        controller.AddParameter(PlayerAnimParams.MoveX, AnimatorControllerParameterType.Float);
        controller.AddParameter(PlayerAnimParams.MoveZ, AnimatorControllerParameterType.Float);
        controller.AddParameter(PlayerAnimParams.Grounded, AnimatorControllerParameterType.Bool);
        controller.AddParameter(PlayerAnimParams.VerticalSpeed, AnimatorControllerParameterType.Float);
        controller.AddParameter(PlayerAnimParams.Sprinting, AnimatorControllerParameterType.Bool);
        controller.AddParameter(PlayerAnimParams.Dashing, AnimatorControllerParameterType.Bool);
        controller.AddParameter(PlayerAnimParams.Jump, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(PlayerAnimParams.Attack, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(PlayerAnimParams.Hit, AnimatorControllerParameterType.Trigger);
        controller.AddParameter(PlayerAnimParams.Die, AnimatorControllerParameterType.Trigger);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        // ---- Locomotion: idle -> walk -> run, blended by Speed ----
        BlendTree tree;
        AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out tree);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = PlayerAnimParams.Speed;
        tree.useAutomaticThresholds = false;
        tree.AddChild(idle, 0f);
        if (walk != null) tree.AddChild(walk, walkSpeed);
        if (run != null) tree.AddChild(run, runSpeed);
        machine.defaultState = locomotion;

        // ---- Air ----
        AnimationClip fallClip = fall != null ? fall : jump;

        AnimatorState jumpState = jump != null ? NewState(machine, "Jump", jump, 1f) : null;
        AnimatorState fallState = fallClip != null ? NewState(machine, "Fall", fallClip, 1f) : null;

        if (jumpState != null)
        {
            AnimatorStateTransition toJump = machine.AddAnyStateTransition(jumpState);
            Setup(toJump, false, 0f, 0.05f, canSelf: true);
            toJump.AddCondition(AnimatorConditionMode.If, 0f, PlayerAnimParams.Jump);

            if (fallState != null)
            {
                AnimatorStateTransition jumpToFall = jumpState.AddTransition(fallState);
                Setup(jumpToFall, false, 0f, 0.15f);
                jumpToFall.AddCondition(AnimatorConditionMode.Less, 0f, PlayerAnimParams.VerticalSpeed);
            }

            AnimatorStateTransition jumpToGround = jumpState.AddTransition(locomotion);
            Setup(jumpToGround, false, 0f, 0.1f);
            jumpToGround.AddCondition(AnimatorConditionMode.If, 0f, PlayerAnimParams.Grounded);
        }

        if (fallState != null)
        {
            // Walking off a ledge.
            AnimatorStateTransition ledge = locomotion.AddTransition(fallState);
            Setup(ledge, false, 0f, 0.1f);
            ledge.AddCondition(AnimatorConditionMode.IfNot, 0f, PlayerAnimParams.Grounded);

            AnimatorStateTransition landing = fallState.AddTransition(locomotion);
            Setup(landing, false, 0f, 0.1f);
            landing.AddCondition(AnimatorConditionMode.If, 0f, PlayerAnimParams.Grounded);
        }

        // ---- Dash: plays while the Dashing bool is on ----
        if (dash != null)
        {
            AnimatorState dashState = NewState(machine, "Dash", dash, 1f);

            AnimatorStateTransition toDash = machine.AddAnyStateTransition(dashState);
            Setup(toDash, false, 0f, 0.03f);
            toDash.AddCondition(AnimatorConditionMode.If, 0f, PlayerAnimParams.Dashing);

            AnimatorStateTransition fromDash = dashState.AddTransition(locomotion);
            Setup(fromDash, false, 0f, 0.1f);
            fromDash.AddCondition(AnimatorConditionMode.IfNot, 0f, PlayerAnimParams.Dashing);
        }

        // ---- One-shot actions: play once, then return to locomotion ----
        AddOneShot(machine, locomotion, "Attack", attack, PlayerAnimParams.Attack, attackAnimationSpeed);
        AddOneShot(machine, locomotion, "Hit", hit, PlayerAnimParams.Hit, 1f);

        // ---- Death: plays once and stays there ----
        if (death != null)
        {
            AnimatorState deathState = NewState(machine, "Death", death, 1f);
            AnimatorStateTransition toDeath = machine.AddAnyStateTransition(deathState);
            Setup(toDeath, false, 0f, 0.1f);
            toDeath.AddCondition(AnimatorConditionMode.If, 0f, PlayerAnimParams.Die);
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Selection.activeObject = controller;
        EditorGUIUtility.PingObject(controller);
        Debug.Log($"Animator Controller created at {path}. Drag it into the Animator's Controller slot on your character model.", controller);
    }

    private static AnimatorState NewState(AnimatorStateMachine machine, string stateName, AnimationClip clip, float speed)
    {
        AnimatorState state = machine.AddState(stateName);
        state.motion = clip;
        state.speed = speed;
        return state;
    }

    private static void AddOneShot(AnimatorStateMachine machine, AnimatorState locomotion, string stateName,
        AnimationClip clip, string trigger, float speed)
    {
        if (clip == null) return;

        AnimatorState state = NewState(machine, stateName, clip, speed);

        AnimatorStateTransition enter = machine.AddAnyStateTransition(state);
        Setup(enter, false, 0f, 0.05f, canSelf: true);
        enter.AddCondition(AnimatorConditionMode.If, 0f, trigger);

        AnimatorStateTransition leave = state.AddTransition(locomotion);
        Setup(leave, true, 0.9f, 0.1f);
    }

    private static void Setup(AnimatorStateTransition transition, bool hasExitTime, float exitTime, float duration, bool canSelf = false)
    {
        transition.hasExitTime = hasExitTime;
        transition.exitTime = exitTime;
        transition.hasFixedDuration = true;
        transition.duration = duration;
        transition.canTransitionToSelf = canSelf;
    }
}
