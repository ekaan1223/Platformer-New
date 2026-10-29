using UnityEngine; // gives us access to Unity classes (ScriptableObject, Sprite, AudioClip, etc.)

// DIALOGUE DATA
// One conversation, saved as an asset file in the Project (not in a scene), so it can be reused by many triggers.
// MAKE ONE: right-click in the Project window > Create > Dialogue > Conversation, then fill in the lines.
// Each line has its own portrait, border, name, text, voice line and how long it stays on screen.
// DialogueTrigger points to one of these; DialoguePlayer shows it.

[CreateAssetMenu(fileName = "NewConversation", menuName = "Dialogue/Conversation")] // adds the "Create > Dialogue > Conversation" menu entry
public class DialogueData : ScriptableObject // ScriptableObject = a script whose data is saved as its own asset file
{
    public DialogueLine[] lines; // the lines, played from top to bottom. Drag them to reorder, + to add one
}

// One line of a conversation. [System.Serializable] makes Unity show it (and save it) in the Inspector.
[System.Serializable]
public class DialogueLine
{
    public Sprite portrait;     // the face shown next to the text. Empty = the portrait is hidden for this line
    public Sprite border;       // the background border/frame behind the text. Empty = the border is hidden for this line
    public string speakerName;  // the name shown above the text. Empty = no name
    [TextArea(2, 5)]
    public string text;         // what is said (the box in the Inspector grows for long lines)
    public AudioClip voiceLine; // the spoken version of this line. Empty = silent
    [Tooltip("How long this line stays on screen, in seconds. 0 = as long as the voice line (3 seconds if there is none).")]
    public float secondsOnScreen = 0f;
}
