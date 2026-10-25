using UnityEngine;                 // gives us access to Unity classes (MonoBehaviour, CanvasGroup, etc.)
using UnityEngine.EventSystems;    // gives us EventTrigger, which reports "the mouse is over me" / "the mouse left"
using UnityEngine.UI;              // gives us Button

// BUTTON HOVER FADE
// Put this ONCE on a Canvas (or any parent object). Every Button under it turns a little see-through while the mouse
// is over it, and back to normal when the mouse leaves. Text, icons and images of the button all fade together.
// New buttons you add under the Canvas get it automatically; nothing to set up on the buttons themselves.

public class ButtonHoverFade : MonoBehaviour // MonoBehaviour = a script Unity can put on an object
{
    [Range(0f, 1f)]
    public float hoverAlpha = 0.6f; // how see-through a hovered button is (1 = not at all, 0 = invisible)

    // Awake runs once, when the object is created.
    private void Awake()
    {
        foreach (Button button in GetComponentsInChildren<Button>(true)) // every button under us (true = also hidden ones, like closed menus)
        {
            CanvasGroup group = button.GetComponent<CanvasGroup>();               // a CanvasGroup fades the button AND everything inside it
            if (group == null) group = button.gameObject.AddComponent<CanvasGroup>(); // add one if it has none

            EventTrigger trigger = button.GetComponent<EventTrigger>();               // listens for the mouse
            if (trigger == null) trigger = button.gameObject.AddComponent<EventTrigger>(); // add one if it has none

            AddListener(trigger, EventTriggerType.PointerEnter, () => group.alpha = hoverAlpha); // mouse over: fade
            AddListener(trigger, EventTriggerType.PointerExit, () => group.alpha = 1f);          // mouse gone: back to normal
        }
    }

    // Tells the EventTrigger: "when this happens, run this code". Used by Awake.
    private void AddListener(EventTrigger trigger, EventTriggerType type, System.Action action)
    {
        EventTrigger.Entry entry = new EventTrigger.Entry(); // one "when X happens" line
        entry.eventID = type;                               // what to listen for (mouse enter / exit)
        entry.callback.AddListener(eventData => action());  // what to do then
        trigger.triggers.Add(entry);                        // add it to the trigger's list
    }
}
