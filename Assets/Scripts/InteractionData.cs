using UnityEngine; // gives us access to Unity classes (ScriptableObject, AudioClip, etc.)

// INTERACTION DATA
// What happens when the player interacts with something, saved as an asset file in the Project (not in a scene),
// so many objects can share the same interaction (every bookshelf can use one "Bookshelf" interaction).
// MAKE ONE: right-click in the Project window > Create > Interaction > Interaction, then fill it in.
// It can play a sound, show a conversation (the same DialogueData the dialogue triggers use), or both.
// Interactable points to one of these; DialoguePlayer shows the conversation.

[CreateAssetMenu(fileName = "NewInteraction", menuName = "Interaction/Interaction")] // adds the "Create > Interaction > Interaction" menu entry
public class InteractionData : ScriptableObject // ScriptableObject = a script whose data is saved as its own asset file
{
    [Header("Sound")]
    public AudioClip sound; // played once when the player interacts. Empty = silent
    [Range(0f, 1f)]         // shows the volume as a slider from 0 to 1 in the Inspector
    public float volume = 1f; // how loud the sound is (1 = full volume)

    [Header("Text")]
    public DialogueData dialogue;  // the conversation to show (made with Create > Dialogue > Conversation). Empty = no text
    public bool pauseGame = false; // ON = the game freezes while the text is on screen. OFF = the player keeps playing
}
