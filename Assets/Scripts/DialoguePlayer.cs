using System.Collections;      // gives us IEnumerator, for coroutines (code that runs over several frames)
using TMPro;                   // gives us TMP_Text, the TextMeshPro text component
using UnityEngine;             // gives us access to Unity classes (MonoBehaviour, AudioSource, Time, etc.)
using UnityEngine.InputSystem; // gives us InputAction, for the skip button
using UnityEngine.UI;          // gives us Image, for the portrait

// DIALOGUE PLAYER
// Shows a conversation (a DialogueData asset) on screen, one line after another. Put ONE in the scene, on your
// dialogue UI, and drag the UI pieces into its slots. DialogueTrigger finds it by itself.
//
// Each line stays for its "Seconds On Screen" (or as long as its voice line), then the next one shows.
// The skip button (E / Enter / gamepad Y by default) jumps to the next line right away and stops its voice line.
// When the game is paused for a conversation, the voice line still plays; all other sounds pause.

public class DialoguePlayer : MonoBehaviour // MonoBehaviour = a script Unity can put on an object
{
    public static DialoguePlayer Current; // the dialogue player of this scene, so triggers can find it (set in Awake)

    [Header("UI (drag your own UI pieces in here)")]
    public GameObject dialogueBox; // the whole dialogue UI: hidden when no conversation is playing
    public Image portraitImage;    // where the portrait goes
    public Image borderImage;      // where the background border goes (optional)
    public TMP_Text nameText;      // where the speaker's name goes (optional)
    public TMP_Text lineText;      // where the line's text goes

    [Header("Sound")]
    public AudioSource voiceSource; // plays the voice lines. Empty = one is added automatically

    [Header("Input (empty = E / Enter / gamepad Y)")]
    public InputAction skipAction = new InputAction("Skip", InputActionType.Button); // press to go to the next line

    [Header("Timing")]
    public float defaultSecondsOnScreen = 3f; // used by lines with Seconds On Screen = 0 and no voice line

    public bool IsPlaying { get; private set; } // true while a conversation is on screen
    public DialogueData CurrentDialogue { get; private set; } // the conversation on screen right now (null = none). Interactable reads it

    private bool pausedTheGame;   // true if the conversation on screen froze the game (so Stop knows to unfreeze it)
    private float timeScaleBefore; // the game speed before the conversation froze it, to put it back afterwards
    private bool showBorder = true; // false = the border stays hidden for the whole conversation (set in Play)

    // Awake runs once, when the object is created.
    private void Awake()
    {
        Current = this; // let the triggers find us

        if (voiceSource == null) voiceSource = gameObject.AddComponent<AudioSource>(); // no speaker given: make one
        voiceSource.playOnAwake = false;       // silent until a line plays
        voiceSource.ignoreListenerPause = true; // keep talking while the game's other sounds are paused

        if (skipAction.bindings.Count == 0) // no keys set in the Inspector: give default ones
        {
            skipAction.AddBinding("<Keyboard>/e");
            skipAction.AddBinding("<Keyboard>/enter");
            skipAction.AddBinding("<Gamepad>/buttonNorth");
        }

        if (dialogueBox != null) dialogueBox.SetActive(false); // hidden until a conversation starts
    }

    private void OnEnable() => skipAction.Enable();   // listen to the skip button
    private void OnDisable() => skipAction.Disable(); // stop listening

    // OnDestroy runs when the object goes away (e.g. the level restarts in the middle of a conversation).
    private void OnDestroy()
    {
        if (Current == this) Current = null; // the next scene's dialogue player takes over
        if (!IsPlaying) return;              // no conversation was playing: nothing to clean up
        Time.timeScale = 1f;                 // never leave the game frozen
        AudioListener.pause = false;         // never leave the sounds paused
    }

    // Starts a conversation. Called by DialogueTrigger and Interactable. Returns false if another one is already playing.
    // showBorder = false hides the border for the whole conversation (left out = true, the border shows as usual).
    public bool Play(DialogueData dialogue, bool pauseGame, bool showBorder = true)
    {
        if (IsPlaying || dialogue == null) return false; // busy, or nothing to play

        this.showBorder = showBorder; // remember it for ShowLine ("this." = our field, not the parameter with the same name)

        StartCoroutine(PlayRoutine(dialogue, pauseGame)); // play the lines over the next frames
        return true;
    }

    // Ends the conversation on screen right away. Called by Interactable (to replace its prompt with the real text).
    public void Stop()
    {
        if (!IsPlaying) return; // nothing on screen: nothing to stop

        StopAllCoroutines(); // stop PlayRoutine where it is, so no more lines show
        Finish();            // hide the UI and unfreeze the game, just like a conversation that ended normally
    }

    // Plays every line of the conversation, one after another.
    private IEnumerator PlayRoutine(DialogueData dialogue, bool pauseGame)
    {
        IsPlaying = true;                // no other conversation can start now
        CurrentDialogue = dialogue;      // remember which conversation is on screen
        pausedTheGame = pauseGame;       // remember if we freeze the game, so Finish knows to unfreeze it
        timeScaleBefore = Time.timeScale; // remember the game speed, to put it back afterwards

        if (pauseGame)
        {
            Time.timeScale = 0f;        // freeze the game (movement, physics, animations)
            AudioListener.pause = true; // pause every sound except the voice line (ignoreListenerPause)
        }

        if (dialogueBox != null) dialogueBox.SetActive(true); // show the dialogue UI

        foreach (DialogueLine line in dialogue.lines) // each line, top to bottom
        {
            ShowLine(line); // put the portrait, border, name and text on screen and start the voice line

            float seconds = line.secondsOnScreen;                                   // how long this line stays...
            if (seconds <= 0f && line.voiceLine != null) seconds = line.voiceLine.length; // ...0 = as long as its voice line...
            if (seconds <= 0f) seconds = defaultSecondsOnScreen;                    // ...or the default if it has none

            float timer = 0f; // how long this line has been on screen
            while (timer < seconds)
            {
                yield return null;              // wait one frame (first, so the press that skipped the last line can't skip this one too)
                timer += Time.unscaledDeltaTime; // "unscaled" keeps counting while the game is paused
                if (skipAction.WasPressedThisFrame()) break; // skip pressed: go to the next line now
            }

            voiceSource.Stop(); // the line is over (or skipped): its voice line stops too
        }

        Finish(); // every line was shown: close the conversation
    }

    // Closes the conversation: hides the UI and unfreezes the game. Used by PlayRoutine and Stop.
    private void Finish()
    {
        voiceSource.Stop(); // no voice line keeps talking after the box is gone

        if (dialogueBox != null) dialogueBox.SetActive(false); // hide the dialogue UI

        if (pausedTheGame)
        {
            Time.timeScale = timeScaleBefore; // unfreeze the game
            AudioListener.pause = false;      // un-pause the other sounds
        }

        IsPlaying = false;      // ready for the next conversation
        CurrentDialogue = null; // nothing is on screen anymore
    }

    // Puts one line on screen and starts its voice line. Used by PlayRoutine.
    private void ShowLine(DialogueLine line)
    {
        if (portraitImage != null)
        {
            portraitImage.sprite = line.portrait;         // the face for this line
            portraitImage.enabled = line.portrait != null; // no portrait: hide the image instead of showing a white box
        }

        if (borderImage != null)
        {
            borderImage.sprite = line.border;          // the frame for this line
            borderImage.enabled = showBorder && line.border != null; // border turned off, or none for this line: hide the image
        }

        if (nameText != null) nameText.text = line.speakerName; // who is talking
        if (lineText != null) lineText.text = line.text;        // what they say

        voiceSource.Stop();            // stop the last voice line, just in case
        voiceSource.clip = line.voiceLine; // this line's voice line (may be empty)
        if (line.voiceLine != null) voiceSource.Play(); // play it if there is one
    }
}
