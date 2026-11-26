using UnityEngine;        // gives us access to Unity classes (MonoBehaviour, Light, RenderSettings, Shader, etc.)
using UnityEngine.Events; // gives us UnityEvent (the event lists you can fill in the Inspector)

// ENVIRONMENT CHANGER
// Changes the look of the level without loading anything. Call ChangeEnvironment() from an event
// (e.g. LevelMemoryTracker -> On Main Collected). It does three kinds of change:
//   1. Swaps objects on/off and the skybox instantly when the change starts.
//   2. Blends the sun and fog over Transition Seconds.
//   3. Moves a GLOBAL shader number from 0 to 1 over the same time. Your 3D models' shaders can read it to fade
//      from the "before" look to the "after" look (see the tooltip on that field).

public class EnvironmentChanger : MonoBehaviour // MonoBehaviour = a script Unity can put on an object and call every frame
{
    [Header("Transition")]
    [SerializeField, Min(0f)] private float transitionSeconds = 3f;

    [Header("Objects (swapped instantly when the change starts)")]
    [Tooltip("e.g. dead trees -> blooming trees, a locked door, new lights.")]
    [SerializeField] private GameObject[] enableOnChange;
    [SerializeField] private GameObject[] disableOnChange;

    [Header("Sun (blended over the transition)")]
    [Tooltip("Leave empty to skip.")]
    [SerializeField] private Light sun;
    [SerializeField] private Color sunColorAfter = new Color(1f, 0.65f, 0.45f);
    [SerializeField, Min(0f)] private float sunIntensityAfter = 1f;

    [Header("Fog (blended over the transition)")]
    [SerializeField] private bool changeFog;
    [SerializeField] private Color fogColorAfter = new Color(0.8f, 0.6f, 0.5f);
    [SerializeField, Min(0f)] private float fogDensityAfter = 0.02f;

    [Header("Skybox (swapped instantly when the change starts)")]
    [Tooltip("Leave empty to skip.")]
    [SerializeField] private Material skyboxAfter;

    [Header("Shader blend (for your 3D models)")]
    [Tooltip("Name of a GLOBAL float shader property that goes 0 -> 1 during the change. In Shader Graph: " +
             "add a Float property, set its Reference to this name, untick Exposed, set Scope to Global, " +
             "then use it as the T of a Lerp between the old and new look. Leave empty to skip.")]
    [SerializeField] private string globalBlendProperty = "_MemoryBlend";

    [Header("Events")]
    [SerializeField] private UnityEvent onChangeStarted;
    [Tooltip("Fires when the transition is complete. Later: start the comic segment here.")]
    [SerializeField] private UnityEvent onChangeFinished;

    private bool changing;        // true while the blend runs
    private float elapsedSeconds; // how long the blend has run

    private Color sunColorBefore;     // the "before" values, remembered when the change starts
    private float sunIntensityBefore;
    private Color fogColorBefore;
    private float fogDensityBefore;

    private bool hasChanged; // true once the change is finished

    // True once the change is finished. Other scripts can read it.
    public bool HasChanged
    {
        get { return hasChanged; }
    }

    // Awake runs once, when the object is created: start from the "before" look.
    private void Awake()
    {
        SetBlend(0f);
    }

    // Global shader values outlive scenes, so put it back to "before" when this level goes away.
    private void OnDestroy()
    {
        SetBlend(0f);
    }

    // Starts the change. Public with no parameters so a UnityEvent can call it.
    public void ChangeEnvironment()
    {
        if (changing || hasChanged) return; // only once

        changing = true;
        elapsedSeconds = 0f;

        // Remember the "before" values so we can blend away from them.
        if (sun != null)
        {
            sunColorBefore = sun.color;
            sunIntensityBefore = sun.intensity;
        }
        fogColorBefore = RenderSettings.fogColor;
        fogDensityBefore = RenderSettings.fogDensity;

        if (enableOnChange != null)
        {
            for (int i = 0; i < enableOnChange.Length; i++)
            {
                if (enableOnChange[i] != null) enableOnChange[i].SetActive(true);
            }
        }

        if (disableOnChange != null)
        {
            for (int i = 0; i < disableOnChange.Length; i++)
            {
                if (disableOnChange[i] != null) disableOnChange[i].SetActive(false);
            }
        }

        if (skyboxAfter != null)
        {
            RenderSettings.skybox = skyboxAfter;
            DynamicGI.UpdateEnvironment(); // refresh the ambient light that comes from the sky
        }

        if (changeFog) RenderSettings.fog = true;

        if (onChangeStarted != null) onChangeStarted.Invoke();
    }

    // Update runs every frame, but only does something while the blend runs.
    private void Update()
    {
        if (!changing) return;

        elapsedSeconds += Time.deltaTime;

        float t = 1f; // 0 = before, 1 = after
        if (transitionSeconds > 0f) t = Mathf.Clamp01(elapsedSeconds / transitionSeconds);
        float eased = Mathf.SmoothStep(0f, 1f, t); // starts and ends gently

        SetBlend(eased);

        if (sun != null)
        {
            sun.color = Color.Lerp(sunColorBefore, sunColorAfter, eased);
            sun.intensity = Mathf.Lerp(sunIntensityBefore, sunIntensityAfter, eased);
        }

        if (changeFog)
        {
            RenderSettings.fogColor = Color.Lerp(fogColorBefore, fogColorAfter, eased);
            RenderSettings.fogDensity = Mathf.Lerp(fogDensityBefore, fogDensityAfter, eased);
        }

        if (t >= 1f) // finished
        {
            changing = false;
            hasChanged = true;
            if (onChangeFinished != null) onChangeFinished.Invoke();
        }
    }

    // Sets the global shader number (if a name was given).
    private void SetBlend(float value)
    {
        if (!string.IsNullOrEmpty(globalBlendProperty)) Shader.SetGlobalFloat(globalBlendProperty, value);
    }
}
