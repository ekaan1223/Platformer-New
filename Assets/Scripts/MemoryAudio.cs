using UnityEngine; // gives us access to Unity classes (MonoBehaviour, AudioSource, AudioClip, etc.)

// MEMORY AUDIO
// Plays your sound file in a loop as a 3D sound: faint when the listener (the camera) is far away, louder as it
// gets close. Every setting is here, and because they are applied in OnValidate you can drag the sliders while
// the game is running and hear the result immediately.
// Select the object in the Scene view to see the range: GREEN sphere = full volume inside,
// RED sphere = the farthest it can be heard.
// The scene needs an AudioListener (the Main Camera has one by default).

[RequireComponent(typeof(AudioSource))] // Unity adds an AudioSource automatically if the object has none
public class MemoryAudio : MonoBehaviour // MonoBehaviour = a script Unity can put on an object and call every frame
{
    [Header("Sound")]
    [Tooltip("Drop your own sound file here. It loops forever.")]
    [SerializeField] private AudioClip loopClip;
    [SerializeField, Range(0f, 1f)] private float volume = 0.6f;
    [Tooltip("Start at a random point of the clip, so three memories don't pulse in sync.")]
    [SerializeField] private bool randomizeStartTime = true;

    [Header("3D Hearing (distances in meters)")]
    [Tooltip("Inside this distance the sound is at full volume.")]
    [SerializeField, Min(0.01f)] private float fullVolumeDistance = 0.5f;
    [Tooltip("The farthest distance the sound can be heard from.")]
    [SerializeField, Min(0.02f)] private float maxHearingDistance = 8f;
    [Tooltip("Linear: fades evenly and is silent at Max Distance. Logarithmic: natural, loud up close, " +
             "but it never fully goes silent. Custom: draw your own curve on the AudioSource component.")]
    [SerializeField] private AudioRolloffMode rolloff = AudioRolloffMode.Linear;
    [Tooltip("1 = fully 3D (direction + distance). 0 = plain 2D sound with no distance effect.")]
    [SerializeField, Range(0f, 1f)] private float spatialBlend = 1f;
    [Tooltip("Pitch shift when you move past quickly. 0 = off.")]
    [SerializeField, Range(0f, 5f)] private float dopplerLevel = 0f;

    private AudioSource audioSource;   // the speaker on this object (found in Awake)
    private bool fading;               // true while fading out (FadeOutAndStop)
    private float fadeStartVolume;     // the volume when the fade started
    private float fadeSecondsTotal;    // how long the whole fade takes
    private float fadeSecondsRemaining; // how long the fade still lasts

    // Awake runs once, when the object is created.
    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        ApplySettings();
    }

    // Start runs once, before the first Update: start the loop.
    private void Start()
    {
        if (loopClip == null)
        {
            Debug.LogWarning(name + ": no Loop Clip assigned, so this memory is silent.", this);
            return;
        }

        if (randomizeStartTime) audioSource.time = Random.Range(0f, loopClip.length * 0.99f); // a random starting point
        audioSource.Play();
    }

    // Update runs every frame, but only does something while fading out.
    private void Update()
    {
        if (!fading) return;

        fadeSecondsRemaining -= Time.deltaTime;

        float t = 1f; // 0 = start of the fade, 1 = silent
        if (fadeSecondsTotal > 0f) t = Mathf.Clamp01(1f - fadeSecondsRemaining / fadeSecondsTotal);
        audioSource.volume = Mathf.Lerp(fadeStartVolume, 0f, t);

        if (t >= 1f) // the fade is over
        {
            fading = false;
            audioSource.Stop();
        }
    }

    // Fade the sound to silence over 'seconds', then stop it. MemoryPickup calls this when collected.
    public void FadeOutAndStop(float seconds)
    {
        fadeStartVolume = audioSource.volume;
        fadeSecondsTotal = seconds;
        fadeSecondsRemaining = seconds;
        fading = true;
    }

    // OnValidate runs in the editor whenever you change a value in the Inspector (also during Play).
    private void OnValidate()
    {
        if (fading) return; // don't undo a fade that is running
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) return;
        ApplySettings();
    }

    // Copies the settings above onto the AudioSource.
    private void ApplySettings()
    {
        audioSource.clip = loopClip;
        audioSource.loop = true;
        audioSource.playOnAwake = false;
        audioSource.volume = volume;
        audioSource.spatialBlend = spatialBlend;
        audioSource.rolloffMode = rolloff;
        audioSource.minDistance = fullVolumeDistance;
        audioSource.maxDistance = Mathf.Max(maxHearingDistance, fullVolumeDistance + 0.01f); // max must be bigger than min
        audioSource.dopplerLevel = dopplerLevel;
    }

    // Scene view: green = full volume inside, red = the farthest it can be heard.
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, fullVolumeDistance);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, maxHearingDistance);
    }
}
