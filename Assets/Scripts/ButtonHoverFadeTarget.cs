using UnityEngine;              // gives us access to Unity classes (MonoBehaviour, CanvasGroup, etc.)
using UnityEngine.EventSystems; // gives us IPointerEnterHandler / IPointerExitHandler ("the mouse is over me" / "the mouse left")

// BUTTON HOVER FADE TARGET
// You don't add this yourself: ButtonHoverFade puts it on every button under its Canvas.
// The UI system calls OnPointerEnter when the mouse moves onto the button and OnPointerExit when it leaves,
// because this script says it "handles" those two events (the ": IPointerEnterHandler, IPointerExitHandler" part).

public class ButtonHoverFadeTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public CanvasGroup group;       // fades the button and everything inside it (set by ButtonHoverFade)
    public float hoverAlpha = 0.6f; // how see-through the button is while hovered (set by ButtonHoverFade)

    // The mouse moved onto the button: fade it.
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (group != null) group.alpha = hoverAlpha;
    }

    // The mouse left the button: back to normal.
    public void OnPointerExit(PointerEventData eventData)
    {
        if (group != null) group.alpha = 1f;
    }
}
