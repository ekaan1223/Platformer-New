using UnityEngine; // gives us access to Unity classes (ScriptableObject, Color, Sprite, etc.)

// KEY TYPE
// One KIND of key, saved as an asset: right-click in the Project window > Create > Keys > Key Type.
// Make one asset per key: PurpleKey, GoldKey, BossKey... A KeyPickup and a LockedDoor match when you drag the
// SAME asset into both. A new key is just a new asset, so you never have to edit code to add one.

[CreateAssetMenu(menuName = "Keys/Key Type", fileName = "PurpleKey")] // adds the "Create > Keys > Key Type" menu entry
public class KeyType : ScriptableObject // ScriptableObject = a script whose data is saved as its own asset file
{
    [Tooltip("The name shown to the player (messages, UI).")]
    [SerializeField] private string displayName = "Purple Key";
    [Tooltip("Tints the key and the door that uses it, and colours the little marker you see in the Scene view.")]
    [SerializeField] private Color color = new Color(0.6f, 0.2f, 0.9f);
    [Tooltip("For your UI later (a key counter). Not used by gameplay.")]
    [SerializeField] private Sprite icon;

    // Read-only access for other scripts (they can look, but only the Inspector changes these).
    public string DisplayName
    {
        get { return displayName; }
    }

    public Color Color
    {
        get { return color; }
    }

    public Sprite Icon
    {
        get { return icon; }
    }
}
