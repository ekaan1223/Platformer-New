using UnityEngine;             // gives us access to Unity classes (MonoBehaviour, Collider, AudioSource, etc.)
using UnityEngine.Events;      // gives us UnityEvent, for the Inspector event list
using UnityEngine.InputSystem; // gives us InputAction, for the interact button

// INTERACTABLE
// Put this on any object the player should be able to interact with (a sign, a radio, a statue...).
// Stand close to it and press the interact button (E / gamepad X by default): its interaction plays.
// WHAT happens is not in this script: drag an InteractionData asset into "Interaction" (it holds the sound and the text).
// The texts are shown by the scene's DialoguePlayer, so the scene needs one (it already does if dialogue works there).
//
// "CLOSE" = the player's body touches the Zone collider. Any collider works (Box, Sphere, Capsule...).
// Easiest setup: add a Box Collider, tick "Is Trigger" (so the player can walk into it) and make it a bit
// bigger than the object. Leave "Is Trigger" off if the object's own solid collider should be the zone
// (then the player has to walk right up against it).

public class Interactable : MonoBehaviour // MonoBehaviour = a script Unity can put on an object and call every frame
{
    public InteractionData interaction;   // what happens when the player interacts (drag an InteractionData asset here)
    public Collider zone;                 // how close the player must be. Empty = the first collider on this object
    public bool interactOnlyOnce = false; // ON = works the first time only. OFF = works every time the button is pressed

    [Header("Prompt (optional)")]
    public DialogueData prompt; // a conversation shown when the player comes close, e.g. "Press E to read". Empty = no prompt
    public bool promptShowsBorder = false; // OFF = the prompt is shown without the border. ON = with the border, like normal dialogue

    [Header("Walking away")]
    public bool stopWhenPlayerLeaves = true; // ON = the text and the sound stop when the player walks away. OFF = they keep playing
    [Min(0f)]                                // the Inspector never lets this go below 0
    public float leaveDistance = 0.5f;       // how far OUTSIDE the zone the player must go before they stop (meters). 0 = right at the edge

    [Header("Input (empty = E / gamepad X)")]
    public InputAction interactAction = new InputAction("Interact", InputActionType.Button); // press to interact

    [Header("Events (hook animations / VFX here)")]
    public UnityEvent onInteracted; // fires every time the player interacts. E.g. open a chest lid, turn on a light

    private CharacterController player; // the player's body (found in Start)
    private bool playerInRange;         // true while the player touches the zone
    private bool hasInteracted;         // true after the first interaction (used by "Interact Only Once")
    private AudioSource speaker;        // plays the interaction's sound (made in Awake)

    // Awake runs once, when the object is created.
    private void Awake()
    {
        if (zone == null) zone = GetComponent<Collider>(); // no zone dragged in: use the collider on this object
        if (zone == null) Debug.LogError(name + ": Interactable needs a collider (Box, Sphere...) to know how close the player is.", this);

        speaker = gameObject.AddComponent<AudioSource>(); // make a speaker on this object for the sound
        speaker.playOnAwake = false;        // silent until the player interacts
        speaker.ignoreListenerPause = true; // keep playing even when a conversation pauses the other sounds

        if (interactAction.bindings.Count == 0) // no keys set in the Inspector: give default ones
        {
            interactAction.AddBinding("<Keyboard>/e");         // E on the keyboard
            interactAction.AddBinding("<Gamepad>/buttonWest"); // X on an Xbox pad, Square on a PlayStation pad
        }
    }

    // Start runs once, after every object's Awake, so the player surely exists by now.
    private void Start()
    {
        PlayerController playerController = FindFirstObjectByType<PlayerController>(); // find the player in the scene
        if (playerController != null) player = playerController.GetComponent<CharacterController>(); // we need its body
        else Debug.LogWarning(name + ": there is no player in the scene, so it can't be interacted with.", this);
    }

    private void OnEnable() => interactAction.Enable();   // start listening to the interact button
    private void OnDisable() => interactAction.Disable(); // stop listening (the object was turned off or destroyed)

    // Update runs every frame.
    private void Update()
    {
        if (zone == null || player == null) return; // no zone or no player (e.g. it died): nothing to do

        Bounds body = player.bounds;         // the box around the player's body
        body.Expand(player.skinWidth * 4f);  // a little bigger: the player always stops a hair away from solid colliders (its "skin")
        bool inRangeNow = zone.bounds.Intersects(body); // does the player's body touch the zone this frame?
        if (inRangeNow && !playerInRange) PlayerCameClose();     // just walked in
        if (!inRangeNow && playerInRange) PlayerWentAway();      // just walked out
        playerInRange = inRangeNow;                              // remember it for the next frame

        body.Expand(leaveDistance * 2f); // grow the body box by Leave Distance on every side (Expand takes the total, so x2)
        bool farAway = !zone.bounds.Intersects(body);            // even the bigger box misses the zone: the player is far away
        if (farAway && stopWhenPlayerLeaves) StopInteraction();  // stop the text and the sound (does nothing if they are not playing)

        if (!playerInRange) return;                        // the player is not close: the button does nothing
        if (!interactAction.WasPressedThisFrame()) return; // the button was not pressed this frame: nothing to do
        if (IsUsedUp()) return;                            // one-time interaction that was already used: do nothing
        if (OtherTextIsShowing()) return;                  // the real text is on screen: that press is for skipping it

        Interact(); // all checks passed: do the interaction
    }

    // Plays the interaction: the sound, the text and the event. Public so a UnityEvent (a button, a lever) can call it too.
    public void Interact()
    {
        if (interaction == null) // no InteractionData asset was dragged in
        {
            Debug.LogWarning(name + ": there is no Interaction asset in the slot, so nothing happens.", this);
            return; // stop here
        }

        hasInteracted = true; // remember that it was used (for "Interact Only Once")

        if (PromptIsShowing()) DialoguePlayer.Current.Stop(); // close the "Press E" prompt to make room for the real text

        if (interaction.sound != null) speaker.PlayOneShot(interaction.sound, interaction.volume); // play the sound if there is one

        if (interaction.dialogue != null) // there is text to show
        {
            if (DialoguePlayer.Current != null) DialoguePlayer.Current.Play(interaction.dialogue, interaction.pauseGame); // show it
            else Debug.LogWarning(name + ": there is no DialoguePlayer in the scene, so the text can't show.", this); // no dialogue UI
        }

        onInteracted?.Invoke(); // tell anything hooked up in the Inspector ("?." = only if the list exists)
    }

    // The player just came close: show the prompt (if there is one and it can still be used).
    private void PlayerCameClose()
    {
        if (prompt == null || IsUsedUp()) return;             // no prompt, or nothing left to interact with
        if (DialoguePlayer.Current == null) return;           // no dialogue UI in the scene: nowhere to show it
        DialoguePlayer.Current.Play(prompt, false, promptShowsBorder); // show it (false = never freeze the game for a prompt)
    }

    // Stops this object's sound and closes its text, if they are still going. Used when the player walks away.
    private void StopInteraction()
    {
        if (speaker.isPlaying) speaker.Stop(); // cut the interaction's sound

        if (interaction == null || interaction.dialogue == null) return; // this object has no text: nothing more to stop
        if (DialoguePlayer.Current == null) return;                       // no dialogue UI in the scene: nothing on screen
        if (DialoguePlayer.Current.CurrentDialogue == interaction.dialogue) DialoguePlayer.Current.Stop(); // only OUR text is closed
    }

    // The player just went away: close the prompt if it is still on screen.
    private void PlayerWentAway()
    {
        if (PromptIsShowing()) DialoguePlayer.Current.Stop(); // only OUR prompt is closed, never another conversation
    }

    // True if this was a one-time interaction and it was already used.
    private bool IsUsedUp() => interactOnlyOnce && hasInteracted;

    // True if OUR prompt is the conversation on screen right now.
    private bool PromptIsShowing() => prompt != null && DialoguePlayer.Current != null && DialoguePlayer.Current.CurrentDialogue == prompt;

    // True if some OTHER conversation (not our prompt) is on screen right now.
    private bool OtherTextIsShowing() => DialoguePlayer.Current != null && DialoguePlayer.Current.IsPlaying && !PromptIsShowing();
}
