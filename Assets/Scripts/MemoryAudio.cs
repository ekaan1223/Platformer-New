using UnityEngine;

/// <summary>
/// Plays your sound file in a loop as a 3D sound: faint when the listener (the camera) is far away,
/// louder as it gets close. Every setting is exposed here, and because they are applied in OnValidate
/// you can drag the sliders while the game is running and hear the result immediately.
/// Select the object in the Scene view to see the range: GREEN sphere = full volume inside,
/// RED sphere = the farthest it can be heard.
/// The scene needs an AudioListener (the Main Camera has one by default).
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class MemoryAudio : MonoBehaviour
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

    private AudioSource audioSource;
    private bool fading;
    private float fadeStartVolume;
    private float fadeSecondsTotal;
    private float fadeSecondsRemaining;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        ApplySettings();
    }

    private void Start()
    {
        if (loopClip == null)
        {
            Debug.LogWarning($"{name}: no Loop Clip assigned, so this memory is silent.", this);
            return;
        }

        if (randomizeStartTime) audioSource.time = Random.Range(0f, loopClip.length * 0.99f);
        audioSource.Play();
    }

    private void Update()
    {
        if (!fading) return;

        fadeSecondsRemaining -= Time.deltaTime;
        float t = fadeSecondsTotal > 0f ? Mathf.Clamp01(1f - fadeSecondsRemaining / fadeSecondsTotal) : 1f;
        audioSource.volume = Mathf.Lerp(fadeStartVolume, 0f, t);

        if (t >= 1f)
        {
            fading = false;
            audioSource.Stop();
        }
    }

    /// <summary>Fade the sound to silence over 'seconds', then stop it.</summary>
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
        if (fading) return;
        if (!TryGetComponent(out audioSource)) return;
        ApplySettings();
    }

    private void ApplySettings()
    {
        audioSource.clip = loopClip;
        audioSource.loop = true;
        audioSource.playOnAwake = false;
        audioSource.volume = volume;
        audioSource.spatialBlend = spatialBlend;
        audioSource.rolloffMode = rolloff;
        audioSource.minDistance = fullVolumeDistance;
        audioSource.maxDistance = Mathf.Max(maxHearingDistance, fullVolumeDistance + 0.01f);
        audioSource.dopplerLevel = dopplerLevel;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, fullVolumeDistance);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, maxHearingDistance);
    }
}
