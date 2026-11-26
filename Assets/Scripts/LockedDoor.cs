using System.Collections;  // gives us IEnumerator, for the opening animation (a coroutine)
using UnityEngine;         // gives us access to Unity classes (MonoBehaviour, Collider, Transform, etc.)
using UnityEngine.Events;  // gives us UnityEvent (the event lists you can fill in the Inspector)

// How the door panel opens.
public enum DoorOpenStyle
{
    Slide,       // glides by "Open Offset" (up into the ceiling, sideways into the wall...)
    Rotate,      // swings around its pivot (a normal hinged door)
    Disappear    // simply vanishes (good for gates, force fields, rubble)
}

// LOCKED DOOR
// A door that needs a key. Walk up to it with the right key and it opens.
//
// BUILD IT LIKE THIS:
//   Door              (empty object)  <- this script. It also gets a TRIGGER collider: the "unlock zone".
//                                        Make it a bit bigger than the door so you don't have to press into it.
//     Panel           (child object)  <- the model + a SOLID collider (Is Trigger OFF). This is the part that moves.
// Drag Panel into the "Panel" slot and a Key Type asset into the "Key Type" slot. Done.
//
// Open() is public with no parameters, so a lever, a button, a boss dying... can open the door too:
// hook it up to that thing's UnityEvent.

[RequireComponent(typeof(BoxCollider))] // Unity adds a Box Collider automatically if the object has none
public class LockedDoor : MonoBehaviour // MonoBehaviour = a script Unity can put on an object
{
    [Header("Lock")]
    [Tooltip("Which key opens this door. Drag a Key Type asset here. EMPTY = no key needed, it opens for the player.")]
    [SerializeField] private KeyType keyType;
    [Tooltip("How many of that key it takes. 3 = a door that needs three purple keys.")]
    [SerializeField, Min(1)] private int keysRequired = 1;
    [Tooltip("ON = the key is used up. OFF = the player keeps it and can open other doors of the same colour.")]
    [SerializeField] private bool consumeKeys = true;

    [Header("Door Panel (the part that moves)")]
    [Tooltip("The child object with the model and the SOLID collider.")]
    [SerializeField] private Transform panel;
    [SerializeField] private DoorOpenStyle openStyle = DoorOpenStyle.Slide;
    [Tooltip("Slide: how far it moves, in the door's own space (x right, y up, z forward). The default slides up 0.7 m.")]
    [SerializeField] private Vector3 openOffset = new Vector3(0f, 0.7f, 0f);
    [Tooltip("Rotate: how far it swings, in degrees. Negative = the other way.")]
    [SerializeField] private float openAngleDegrees = 90f;
    [Tooltip("Rotate: the axis it swings around, in the panel's own space. Up (0,1,0) = a normal hinge.")]
    [SerializeField] private Vector3 rotateAxis = Vector3.up;
    [SerializeField, Min(0.01f)] private float openSeconds = 0.8f;
    [Tooltip("The opening speed over time: slow-fast-slow by default. 0 = closed, 1 = fully open.")]
    [SerializeField] private AnimationCurve openCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Look")]
    [Tooltip("Paint the panel with the key's colour. Turn OFF if your own model/material already has the right colour.")]
    [SerializeField] private bool tintWithKeyColor = true;
    [Tooltip("Which renderers to paint. Empty = every model under the panel.")]
    [SerializeField] private Renderer[] tintRenderers;

    [Header("Feedback")]
    [Tooltip("The 'locked' event can fire at most this often while the player stays at the door.")]
    [SerializeField, Min(0f)] private float lockedFeedbackCooldownSeconds = 1.5f;
    [Tooltip("Print messages to the Console (only in the Editor, never in the built game).")]
    [SerializeField] private bool logMessages = true;

    [Header("Events (hook sound / VFX / UI here)")]
    [Tooltip("The right key was used and the door STARTS opening.")]
    [SerializeField] private UnityEvent onUnlocked;
    [Tooltip("The door finished opening.")]
    [SerializeField] private UnityEvent onOpened;
    [Tooltip("The player touched the door WITHOUT the right key. Later: a 'you need the purple key' message.")]
    [SerializeField] private UnityEvent onTouchedWhileLocked;

    private Collider[] panelColliders;  // the panel's solid colliders, switched off when the door opens
    private float nextFeedbackTime;     // the "locked" event can't fire again before this time
    private bool isOpening;             // true while the opening animation runs
    private bool isOpen;                // true once fully open
    private KeyInventory playerInventory; // the player's keys, found the first time the player touches the door

    // True once the door is fully open. Other scripts can read it.
    public bool IsOpen
    {
        get { return isOpen; }
    }

    // Awake runs once, when the object is created.
    private void Awake()
    {
        // Slot left empty? Look for a child called "Panel", then for any child, so a normal setup just works.
        if (panel == null) panel = transform.Find("Panel");
        if (panel == null && transform.childCount > 0) panel = transform.GetChild(0);

        if (panel == null)
        {
            Debug.LogError(name + ": no Panel found. Make a child object with the door model and a solid collider " +
                           "(name it 'Panel') and drag it into the Panel slot.", this);
            enabled = false;
            return;
        }

        // Everything on THIS object is the unlock zone, so it must be a trigger. The solid collider belongs on the Panel.
        Collider[] zoneColliders = GetComponents<Collider>();
        for (int i = 0; i < zoneColliders.Length; i++) zoneColliders[i].isTrigger = true;

        panelColliders = panel.GetComponentsInChildren<Collider>();

        if (keyType != null && tintWithKeyColor) KeyColorTint.Apply(tintRenderers, panel.gameObject, keyType.Color);
    }

    // Unity calls this when something walks into the unlock zone.
    private void OnTriggerEnter(Collider other)
    {
        TryOpenFor(other, true); // true = say "locked" if the player has no key
    }

    // Stay: if the player is already standing at the door when they get the key (or a second one), it still opens.
    private void OnTriggerStay(Collider other)
    {
        TryOpenFor(other, false); // false = don't repeat the "locked" message every physics step
    }

    // Opens the door if the player has the right key.
    private void TryOpenFor(Collider other, bool giveFeedback)
    {
        if (isOpen || isOpening) return;
        if (!PlayerController.IsPlayer(other)) return; // only the player carries keys (cheap check, no searching)

        if (playerInventory == null) playerInventory = other.GetComponentInParent<KeyInventory>(); // searched once, then remembered
        if (playerInventory == null) return; // the player has no KeyInventory

        if (keyType != null)
        {
            if (!playerInventory.Has(keyType, keysRequired))
            {
                if (giveFeedback) ReportLocked(playerInventory);
                return;
            }

            if (consumeKeys) playerInventory.TryUse(keyType, keysRequired);
        }

        if (logMessages && Application.isEditor)
        {
            if (keyType != null) Debug.Log(name + ": unlocked with " + keysRequired + " x " + keyType.DisplayName + ".", this);
            else Debug.Log(name + ": unlocked.", this);
        }
        Open();
    }

    // The player touched the door without the key.
    private void ReportLocked(KeyInventory inventory)
    {
        if (Time.time < nextFeedbackTime) return; // told them a moment ago
        nextFeedbackTime = Time.time + lockedFeedbackCooldownSeconds;

        if (logMessages && Application.isEditor)
        {
            Debug.Log(name + ": locked. Needs " + keysRequired + " x " + keyType.DisplayName + " (carrying " + inventory.GetCount(keyType) + ").", this);
        }

        if (onTouchedWhileLocked != null) onTouchedWhileLocked.Invoke();
    }

    // Opens the door no matter what. Public with no parameters on purpose, so a lever, a button, a dead boss
    // (any UnityEvent) can call it from the Inspector.
    public void Open()
    {
        if (!isActiveAndEnabled || isOpen || isOpening) return;

        isOpening = true;
        for (int i = 0; i < panelColliders.Length; i++)
        {
            if (panelColliders[i] != null) panelColliders[i].enabled = false; // the player can walk through straight away
        }

        if (onUnlocked != null) onUnlocked.Invoke();
        StartCoroutine(OpenRoutine());
    }

    // Moves the panel open over Open Seconds (or makes it vanish).
    private IEnumerator OpenRoutine()
    {
        if (openStyle == DoorOpenStyle.Disappear)
        {
            panel.gameObject.SetActive(false);
        }
        else
        {
            Vector3 startPosition = panel.localPosition;
            Quaternion startRotation = panel.localRotation;
            Vector3 endPosition = startPosition + openOffset;
            Quaternion endRotation = startRotation * Quaternion.AngleAxis(openAngleDegrees, rotateAxis);

            float elapsed = 0f;
            while (elapsed < openSeconds)
            {
                elapsed += Time.deltaTime;
                float t = openCurve.Evaluate(Mathf.Clamp01(elapsed / openSeconds)); // 0 = closed, 1 = open, shaped by the curve

                if (openStyle == DoorOpenStyle.Slide) panel.localPosition = Vector3.LerpUnclamped(startPosition, endPosition, t);
                else panel.localRotation = Quaternion.SlerpUnclamped(startRotation, endRotation, t);

                yield return null; // wait one frame
            }

            // Land exactly on the end position, whatever the curve did.
            if (openStyle == DoorOpenStyle.Slide) panel.localPosition = endPosition;
            else panel.localRotation = endRotation;
        }

        isOpening = false;
        isOpen = true;
        if (onOpened != null) onOpened.Invoke();
    }

    // Reset() runs in the editor when the script is first added: makes the unlock zone a trigger box.
    // Resize it in the Inspector (Box Collider > Size / Center) so it reaches a little in front of and behind the door.
    private void Reset()
    {
        BoxCollider zone = GetComponent<BoxCollider>();
        if (zone != null)
        {
            zone.isTrigger = true;
            zone.center = new Vector3(0f, 0.4f, 0f);
            zone.size = new Vector3(0.8f, 0.8f, 0.6f);
        }
    }

    // A marker above the door in the key's colour, so in the Scene view you can see which door wants which key.
    private void OnDrawGizmos()
    {
        if (keyType == null) return;

        Gizmos.color = keyType.Color;
        Gizmos.DrawSphere(transform.position + Vector3.up * 0.1f, 0.05f);
    }
}
