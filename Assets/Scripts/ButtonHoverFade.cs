using UnityEngine;    // gives us access to Unity classes (MonoBehaviour, CanvasGroup, etc.)
using UnityEngine.UI; // gives us Button

// BUTTON HOVER FADE
// Put this ONCE on a Canvas (or any parent object). Every Button under it turns a little see-through while the mouse
// is over it, and back to normal when the mouse leaves. Text, icons and images of the button all fade together.
// It gives every button a small ButtonHoverFadeTarget script (see that file), which does the fading itself.

public class ButtonHoverFade : MonoBehaviour // MonoBehaviour = a script Unity can put on an object
{
    [Range(0f, 1f)]
    public float hoverAlpha = 0.6f; // how see-through a hovered button is (1 = not at all, 0 = invisible)

    // Awake runs once, when the object is created.
    private void Awake()
    {
        Button[] buttons = GetComponentsInChildren<Button>(true); // every button under us (true = also hidden ones, like closed menus)

        for (int i = 0; i < buttons.Length; i++)
        {
            GameObject button = buttons[i].gameObject;

            CanvasGroup group = button.GetComponent<CanvasGroup>();        // a CanvasGroup fades the button AND everything inside it
            if (group == null) group = button.AddComponent<CanvasGroup>(); // add one if it has none

            ButtonHoverFadeTarget target = button.GetComponent<ButtonHoverFadeTarget>();
            if (target == null) target = button.AddComponent<ButtonHoverFadeTarget>(); // listens for the mouse on this button
            target.group = group;
            target.hoverAlpha = hoverAlpha;
        }
    }
}
