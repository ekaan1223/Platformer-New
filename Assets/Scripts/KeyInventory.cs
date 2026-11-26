using System.Collections.Generic; // gives us List
using UnityEngine;                // gives us access to Unity classes (MonoBehaviour, etc.)
using UnityEngine.Events;         // gives us UnityEvent (the event lists you can fill in the Inspector)

// One line of the inventory: "2 x PurpleKey". Shows up as a list in the Inspector.
[System.Serializable] // makes Unity show it (and save it) in the Inspector
public class KeyStack
{
    [SerializeField] private KeyType keyType; // which key
    [SerializeField, Min(0)] private int count; // how many of it

    public KeyStack() { } // an empty line (Unity needs this one to build the list)

    public KeyStack(KeyType keyType, int count)
    {
        this.keyType = keyType; // "this." = our field, not the parameter with the same name
        this.count = count;
    }

    public KeyType KeyType
    {
        get { return keyType; }
    }

    public int Count
    {
        get { return count; }
        set { count = value; }
    }
}

// KEY INVENTORY
// Put this on the PLAYER. It remembers which keys the player is carrying.
// It only stores keys: pickups call Add, doors call Has and TryUse. It knows nothing about either, which is what
// keeps keys, doors and the player independent of each other.
// The list in the Inspector is live while you play, and anything you put in it BEFORE playing is what the player
// starts with (handy for testing a door without fetching the key).

public class KeyInventory : MonoBehaviour // MonoBehaviour = a script Unity can put on an object
{
    [Tooltip("The keys the player has. Add entries here to START with keys. Counts change while playing.")]
    [SerializeField] private List<KeyStack> keys = new List<KeyStack>();

    [Header("Events (hook sound / UI here)")]
    [SerializeField] private UnityEvent onKeyCollected;
    [SerializeField] private UnityEvent onKeyUsed;

    // A "shout" with the key type and its NEW count whenever a count changes. A UI counter can listen to this.
    public event System.Action<KeyType, int> KeyCountChanged;

    // Everything carried (for building UI). Please only read it; change keys with Add / TryUse.
    public List<KeyStack> Keys
    {
        get { return keys; }
    }

    // Awake runs once, when the object is created: clean up the starting list.
    private void Awake()
    {
        TidyList();
    }

    // How many of this key the player carries.
    public int GetCount(KeyType keyType)
    {
        if (keyType == null) return 0;
        KeyStack stack = Find(keys, keyType);
        if (stack == null) return 0;
        return stack.Count;
    }

    // True if the player carries at least 'amount' of this key.
    public bool Has(KeyType keyType, int amount = 1)
    {
        return keyType != null && GetCount(keyType) >= amount;
    }

    // Adds keys. KeyPickup calls this.
    public void Add(KeyType keyType, int amount = 1)
    {
        if (keyType == null || amount <= 0) return;

        KeyStack stack = Find(keys, keyType);
        if (stack == null) // the first key of this kind: a new line
        {
            stack = new KeyStack(keyType, 0);
            keys.Add(stack);
        }

        stack.Count += amount;
        if (KeyCountChanged != null) KeyCountChanged(keyType, stack.Count);
        if (onKeyCollected != null) onKeyCollected.Invoke();
    }

    // Spends keys. Returns false (and changes nothing) if the player doesn't have enough. LockedDoor calls this.
    public bool TryUse(KeyType keyType, int amount = 1)
    {
        if (!Has(keyType, amount)) return false;

        KeyStack stack = Find(keys, keyType);
        stack.Count -= amount;
        int remaining = stack.Count;
        if (remaining <= 0) keys.Remove(stack); // none left: remove the line

        if (KeyCountChanged != null) KeyCountChanged(keyType, remaining);
        if (onKeyUsed != null) onKeyUsed.Invoke();
        return true;
    }

    // Forgets every key.
    public void Clear()
    {
        keys.Clear();
    }

    // Merges duplicate lines and drops empty ones, so there is exactly one line per key type.
    private void TidyList()
    {
        List<KeyStack> tidy = new List<KeyStack>();

        for (int i = 0; i < keys.Count; i++)
        {
            KeyStack stack = keys[i];
            if (stack == null || stack.KeyType == null || stack.Count <= 0) continue; // an empty line: drop it

            KeyStack existing = Find(tidy, stack.KeyType);
            if (existing != null) existing.Count += stack.Count;           // a second line of the same key: merge
            else tidy.Add(new KeyStack(stack.KeyType, stack.Count));
        }

        keys = tidy;
    }

    // The line for this key in a list (null = none).
    private static KeyStack Find(List<KeyStack> list, KeyType keyType)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null && list[i].KeyType == keyType) return list[i];
        }
        return null;
    }
}
