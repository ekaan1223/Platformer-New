using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The "Memories 1/3" counter in the corner of the screen. Put it next to the LevelMemoryTracker.
/// It builds its own small Canvas when the level starts, so a level needs no UI set up for it.
/// It only listens to the tracker: the tracker knows nothing about this script.
/// It disappears once the main memory is collected, and in a level that needs no small memories (the boss).
/// </summary>
[RequireComponent(typeof(LevelMemoryTracker))]
public class MemoryCounterUI : MonoBehaviour
{
    private const int CanvasSortingOrder = 5;
    private static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);

    [SerializeField] private string label = "Memories";
    [Tooltip("Empty = TextMeshPro's default font.")]
    [SerializeField] private TMP_FontAsset font;
    [SerializeField, Min(1f)] private float fontSize = 40f;
    [SerializeField] private Color color = Color.white;
    [Tooltip("Distance from the top right corner of the screen, in pixels at 1920 x 1080.")]
    [SerializeField] private Vector2 marginFromCorner = new Vector2(40f, 30f);

    private LevelMemoryTracker tracker;
    private GameObject canvasObject;
    private TMP_Text text;

    private void Start()
    {
        tracker = GetComponent<LevelMemoryTracker>();
        if (tracker.SmallRequired <= 0) return;

        BuildUI();
        ShowCount(tracker.SmallCollected, tracker.SmallRequired);

        tracker.SmallCountChanged += ShowCount;
        tracker.MainCollected += HandleMainCollected;
    }

    private void OnDestroy()
    {
        if (tracker == null) return;

        tracker.SmallCountChanged -= ShowCount;
        tracker.MainCollected -= HandleMainCollected;
    }

    private void ShowCount(int collected, int required) => text.text = $"{label} {collected}/{required}";

    private void HandleMainCollected() => canvasObject.SetActive(false);

    private void BuildUI()
    {
        canvasObject = new GameObject("MemoryCounterCanvas");
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CanvasSortingOrder;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;

        GameObject textObject = new GameObject("MemoryCounterText");
        textObject.transform.SetParent(canvasObject.transform, false);

        text = textObject.AddComponent<TextMeshProUGUI>();
        if (font != null) text.font = font;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAlignmentOptions.TopRight;
        text.raycastTarget = false;

        RectTransform rect = text.rectTransform;
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = -marginFromCorner;
        rect.sizeDelta = new Vector2(600f, 80f);
    }
}
