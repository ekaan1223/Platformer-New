using TMPro;          // gives us TextMeshPro (TMP_Text, TextMeshProUGUI, TMP_FontAsset)
using UnityEngine;    // gives us access to Unity classes (MonoBehaviour, GameObject, Canvas, etc.)
using UnityEngine.UI; // gives us CanvasScaler

// MEMORY COUNTER UI
// The "Memories 1/3" counter in the corner of the screen. Put it next to the LevelMemoryTracker.
// It builds its own small Canvas when the level starts, so a level needs no UI set up for it.
// It only listens to the tracker: the tracker knows nothing about this script.
// It disappears once the main memory is collected, and in a level that needs no small memories (the boss).

[RequireComponent(typeof(LevelMemoryTracker))] // it reads the tracker on the same object
public class MemoryCounterUI : MonoBehaviour // MonoBehaviour = a script Unity can put on an object
{
    private const int CanvasSortingOrder = 5;                        // drawn above the game, below the pause menu
    private static Vector2 ReferenceResolution = new Vector2(1920f, 1080f); // the UI is laid out as if the screen were this size

    [SerializeField] private string label = "Memories";
    [Tooltip("Empty = TextMeshPro's default font.")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField, Min(1f)] private float fontSize = 40f;
    [SerializeField] private Color color = Color.white;
    [Tooltip("Distance from the top right corner of the screen, in pixels at 1920 x 1080.")]
    [SerializeField] private Vector2 marginFromCorner = new Vector2(40f, 30f);

    private LevelMemoryTracker tracker; // the counter we show (found in Start)
    private GameObject canvasObject;    // the canvas we built
    private TMP_Text text;              // the text on it

    // Start runs once, after every Awake.
    private void Start()
    {
        tracker = GetComponent<LevelMemoryTracker>();
        if (tracker.SmallRequired <= 0) return; // the boss level: nothing to count

        BuildUI();
        ShowCount(tracker.SmallCollected, tracker.SmallRequired);

        tracker.SmallCountChanged += ShowCount;          // "when the count changes, call ShowCount"
        tracker.MainCollected += HandleMainCollected;    // "when the level is done, call HandleMainCollected"
    }

    // OnDestroy runs when the level unloads: stop listening.
    private void OnDestroy()
    {
        if (tracker == null) return;

        tracker.SmallCountChanged -= ShowCount;
        tracker.MainCollected -= HandleMainCollected;
    }

    // Writes "Memories 1/3".
    private void ShowCount(int collected, int required)
    {
        text.text = label + " " + collected + "/" + required;
    }

    // The level's memories are done: hide the counter.
    private void HandleMainCollected()
    {
        canvasObject.SetActive(false);
    }

    // Builds the canvas and the text in the top right corner.
    private void BuildUI()
    {
        canvasObject = new GameObject("MemoryCounterCanvas");
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; // drawn on top of the game, on the screen
        canvas.sortingOrder = CanvasSortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; // the same size on every screen
        scaler.referenceResolution = ReferenceResolution;

        GameObject textObject = new GameObject("MemoryCounterText");
        textObject.transform.SetParent(canvasObject.transform, false);

        text = textObject.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAlignmentOptions.TopRight;
        text.raycastTarget = false; // the mouse goes through it

        RectTransform rect = text.rectTransform;
        rect.anchorMin = Vector2.one; // stuck to the top right corner
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = -marginFromCorner;
        rect.sizeDelta = new Vector2(600f, 80f);
    }
}
