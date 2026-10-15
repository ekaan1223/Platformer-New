using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>How the door panel opens.</summary>
public enum DoorOpenStyle
{
    Slide,       // glides by "Open Offset" (up into the ceiling, sideways into the wall...)
    Rotate,      // swings around its pivot (a normal hinged door)
    Disappear    // simply vanishes (good for gates, force fields, rubble)
}

/// <summary>
/// A door that needs a key. Walk up to it with the right key and it opens.
///
/// BUILD IT LIKE THIS:
///   Door              (empty object)  <- this script. It also gets a TRIGGER collider: the "unlock zone".
///                                        Make it a bit bigger than the door so you don't have to press into it.
///     Panel           (child object)  <- the model + a SOLID collider (Is Trigger OFF). This is the part that moves.
/// Drag Panel into the "Panel" slot and a Key Type asset into the "Key Type" slot. Done.
///
/// Open() is public with no parameters, so a lever, a button, a boss dying... can open the door too:
/// hook it up to that thing's UnityEvent.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class LockedDoor : MonoBehaviour
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
    [SerializeField] private bool logMessages = true;

    [Header("Events (hook sound / VFX / UI here)")]
    [Tooltip("The right key was used and the door STARTS opening.")]
    [SerializeField] private UnityEvent onUnlocked;
    [Tooltip("The door finished opening.")]
    [SerializeField] private UnityEvent onOpened;
    [Tooltip("The player touched the door WITHOUT the right key. Later: a 'you need the purple key' message.")]
    [SerializeField] private UnityEvent onTouchedWhileLocked;

    private Collider[] panelColliders;
    private float nextFeedbackTime;
    private bool isOpening;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        // Slot left empty? Look for a child called "Panel", then for any child, so a normal setup just works.
        if (panel == null) panel = transform.Find("Panel");
        if (panel == null && transform.childCount > 0) panel = transform.GetChild(0);

        if (panel == null)
        {
            Debug.LogError($"{name}: no Panel found. Make a child object with the door model and a solid collider " +
                           "(name it 'Panel') and drag it into the Panel slot.", this);
            enabled = false;
            return;
        }

        // Everything on THIS object is the unlock zone, so it must be a trigger. The solid collider belongs on the Panel.
        foreach (Collider c in GetComponents<Collider>()) c.isTrigger = true;

        panelColliders = panel.GetComponentsInChildren<Collider>();

        if (keyType != null && tintWithKeyColor)
            KeyColorTint.Apply(tintRenderers, panel.gameObject, keyType.Color);
    }

    private void OnTriggerEnter(Collider other) => TryOpenFor(other, giveFeedback: true);

    // Stay: if the player is already standing at the door when they get the key (or a second one), it still opens.
    private void OnTriggerStay(Collider other) => TryOpenFor(other, giveFeedback: false);

    private void TryOpenFor(Collider other, bool giveFeedback)
    {
        if (IsOpen || isOpening) return;

        KeyInventory inventory = other.GetComponentInParent<KeyInventory>();
        if (inventory == null) return;   // only the player carries keys

        if (keyType != null)
        {
            if (!inventory.Has(keyType, keysRequired))
            {
                if (giveFeedback) ReportLocked(inventory);
                return;
            }

            if (consumeKeys) inventory.TryUse(keyType, keysRequired);
        }

        if (logMessages) Debug.Log($"{name}: unlocked" + (keyType != null ? $" with {keysRequired} x {keyType.DisplayName}." : "."), this);
        Open();
    }

    private void ReportLocked(KeyInventory inventory)
    {
        if (Time.time < nextFeedbackTime) return;
        nextFeedbackTime = Time.time + lockedFeedbackCooldownSeconds;

        if (logMessages)
            Debug.Log($"{name}: locked. Needs {keysRequired} x {keyType.DisplayName} (carrying {inventory.GetCount(keyType)}).", this);

        onTouchedWhileLocked?.Invoke();
    }

    /// <summary>
    /// Opens the door no matter what. Public with no parameters on purpose, so a lever, a button, a dead boss
    /// (any UnityEvent) can call it from the Inspector.
    /// </summary>
    public void Open()
    {
        if (!isActiveAndEnabled || IsOpen || isOpening) return;

        isOpening = true;
        foreach (Collider c in panelColliders)
        {
            if (c != null) c.enabled = false;   // the player can walk through straight away
        }

        onUnlocked?.Invoke();
        StartCoroutine(OpenRoutine());
    }

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
                float t = openCurve.Evaluate(Mathf.Clamp01(elapsed / openSeconds));

                if (openStyle == DoorOpenStyle.Slide) panel.localPosition = Vector3.LerpUnclamped(startPosition, endPosition, t);
                else panel.localRotation = Quaternion.SlerpUnclamped(startRotation, endRotation, t);

                yield return null;
            }

            // Land exactly on the end position, whatever the curve did.
            if (openStyle == DoorOpenStyle.Slide) panel.localPosition = endPosition;
            else panel.localRotation = endRotation;
        }

        isOpening = false;
        IsOpen = true;
        onOpened?.Invoke();
    }

    // Reset() runs in the editor when the script is first added: makes the unlock zone a trigger box.
    // Resize it in the Inspector (Box Collider > Size / Center) so it reaches a little in front of and behind the door.
    private void Reset()
    {
        if (TryGetComponent(out BoxCollider zone))
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
