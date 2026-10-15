using UnityEngine;

/// <summary>
/// One KIND of key, as an asset: right-click in the Project window > Create > Keys > Key Type.
/// Make one asset per key: PurpleKey, GoldKey, BossKey... A KeyPickup and a LockedDoor match when you drag the
/// SAME asset into both. Because a new key is just a new asset, you never have to edit code to add one.
/// </summary>
[CreateAssetMenu(menuName = "Keys/Key Type", fileName = "PurpleKey")]
public class KeyType : ScriptableObject
{
    [Tooltip("The name shown to the player (messages, UI).")]
    [SerializeField] private string displayName = "Purple Key";
    [Tooltip("Tints the key and the door that uses it, and colours the little marker you see in the Scene view.")]
    [SerializeField] private Color color = new Color(0.6f, 0.2f, 0.9f);
    [Tooltip("For your UI later (a key counter). Not used by gameplay.")]
    [SerializeField] private Sprite icon;

    public string DisplayName => displayName;
    public Color Color => color;
    public Sprite Icon => icon;
}
