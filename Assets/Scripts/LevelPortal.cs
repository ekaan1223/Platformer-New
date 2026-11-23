using System.Collections;          // gives us IEnumerator, for the optional delay
using UnityEngine;                 // gives us access to Unity classes (MonoBehaviour, BoxCollider, Gizmos, etc.)
using UnityEngine.Events;          // gives us UnityEvent, for the Inspector event list
using UnityEngine.SceneManagement; // gives us SceneManager, to load the level

// LEVEL PORTAL
// Put this on any object (a door, a glowing ring, an empty object...). It adds a Box Collider set to "Is Trigger":
// when the player walks into that box, the level in "Scene Name" loads.
// The level must be in File > Build Profiles (the scene list), or it can't be loaded.
// Make the box as big as the area that should send the player away. Select the object to see it (cyan box).

[RequireComponent(typeof(BoxCollider))] // Unity adds a Box Collider automatically if the object has none
public class LevelPortal : MonoBehaviour // MonoBehaviour = a script Unity can put on an object
{
    public string sceneName = "Level 2"; // the level to load: its exact name in the Project window, without ".unity"
    [Min(0f)]                            // the Inspector never lets this go below 0
    public float delaySeconds = 0f;      // wait this long before loading (0 = right away). Time for a sound or a fade

    [Header("Events (hook sound / VFX / a fade here)")]
    public UnityEvent onEntered; // fires the moment the player walks in, before the level loads

    private bool used; // true once the player walked in, so touching it twice can't load the level twice

    // Awake runs once, when the object is created.
    private void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true; // the box must never block the player, only notice them

        if (!Application.CanStreamedLevelBeLoaded(sceneName)) // the name is wrong, or the level is not in the scene list
            Debug.LogError(name + ": can't load '" + sceneName + "'. Check the spelling and add it to File > Build Profiles.", this);
    }

    // Unity calls this when something walks into the trigger box.
    private void OnTriggerEnter(Collider other)
    {
        if (used) return;                                                 // already on the way to the next level
        if (other.GetComponentInParent<PlayerController>() == null) return; // not the player (an enemy, a pickup...): ignore

        used = true;            // remember it, so it only happens once
        onEntered?.Invoke();    // play the sounds / effects hooked up in the Inspector
        StartCoroutine(LoadAfterDelay());
    }

    // Waits for the delay (if any), then loads the level.
    private IEnumerator LoadAfterDelay()
    {
        if (delaySeconds > 0f) yield return new WaitForSecondsRealtime(delaySeconds); // "Realtime" still counts if a dialogue froze the game

        Time.timeScale = 1f;               // the next level must not start frozen (a dialogue or pause may have frozen this one)
        SceneManager.LoadScene(sceneName); // load the level
    }

    // Reset runs in the editor when the script is first added: make the box a trigger right away.
    private void Reset()
    {
        GetComponent<BoxCollider>().isTrigger = true; // so you can see in the Inspector that it is a trigger
    }

    // Scene view (select the object): the trigger box in cyan, so you can see where the player has to walk.
    private void OnDrawGizmosSelected()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null) return;

        Gizmos.color = Color.cyan;
        Gizmos.matrix = transform.localToWorldMatrix; // draw in the object's own space, so the box turns and scales with it
        Gizmos.DrawWireCube(box.center, box.size);
    }
}
