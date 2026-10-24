using UnityEngine; // gives us access to Unity classes (MonoBehaviour, Collider, BoxCollider, etc.)

// DIALOGUE TRIGGER
// An invisible box: when the player walks into it, a conversation plays.
// Put it on an empty object, drag a conversation asset (DialogueData) into "Dialogue", and size the Box Collider.
// The scene also needs ONE DialoguePlayer (the dialogue UI).

[RequireComponent(typeof(BoxCollider))] // Unity adds a Box Collider automatically if the object has none
public class DialogueTrigger : MonoBehaviour
{
    public DialogueData dialogue;   // the conversation to play
    public bool pauseGame = true;   // ON = the game freezes while the conversation plays. OFF = the player keeps playing
    public bool playOnlyOnce = true; // ON = plays the first time only. OFF = plays every time the player walks in

    private bool hasPlayed; // true after the conversation played once

    // Awake runs once, when the object is created.
    private void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true; // the box must never block the player, only notice them
    }

    // Unity calls this when something walks into the box.
    private void OnTriggerEnter(Collider other)
    {
        if (playOnlyOnce && hasPlayed) return;                          // already played once: do nothing
        if (other.GetComponentInParent<PlayerController>() == null) return; // not the player: ignore

        if (DialoguePlayer.Current == null) // no dialogue UI in the scene
        {
            Debug.LogWarning(name + ": there is no DialoguePlayer in the scene, so the conversation can't play.", this);
            return;
        }

        if (DialoguePlayer.Current.Play(dialogue, pauseGame)) hasPlayed = true; // started: remember it (false = another one was playing)
    }

    // Reset runs in the editor when the script is first added: make the box a trigger right away.
    private void Reset()
    {
        GetComponent<BoxCollider>().isTrigger = true; // so you can see in the Inspector that it is a trigger
    }
}
