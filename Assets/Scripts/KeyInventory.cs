using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>One line of the inventory: "2 x PurpleKey". Shows up as a list in the Inspector.</summary>
[Serializable]
public class KeyStack
{
    [SerializeField] private KeyType keyType;
    [SerializeField, Min(0)] private int count;

    public KeyStack() { }

    public KeyStack(KeyType keyType, int count)
    {
        this.keyType = keyType;
        this.count = count;
    }

    public KeyType KeyType => keyType;

    public int Count
    {
        get => count;
        set => count = value;
    }
}

/// <summary>
/// Put this on the PLAYER. It remembers which keys the player is carrying.
/// It only stores keys: pickups call Add(), doors call Has() and TryUse(). It knows nothing about either,
/// which is what keeps keys, doors and the player independent of each other.
/// The list in the Inspector is live while you play, and anything you put in it BEFORE playing is what
/// the player starts with (handy for testing a door without fetching the key).
/// </summary>
public class KeyInventory : MonoBehaviour
{
    [Tooltip("The keys the player has. Add entries here to START with keys. Counts change while playing.")]
    [SerializeField] private List<KeyStack> keys = new List<KeyStack>();

    [Header("Events (hook sound / UI here)")]
    [SerializeField] private UnityEvent onKeyCollected;
    [SerializeField] private UnityEvent onKeyUsed;

    /// <summary>Raised with the key type and its NEW count whenever a count changes. A UI counter can listen to this.</summary>
    public event Action<KeyType, int> KeyCountChanged;

    /// <summary>Read-only view of everything carried (for building UI).</summary>
    public IReadOnlyList<KeyStack> Keys => keys;

    private void Awake() => TidyList();

    public int GetCount(KeyType keyType)
    {
        KeyStack stack = keyType != null ? Find(keys, keyType) : null;
        return stack != null ? stack.Count : 0;
    }

    public bool Has(KeyType keyType, int amount = 1) => keyType != null && GetCount(keyType) >= amount;

    public void Add(KeyType keyType, int amount = 1)
    {
        if (keyType == null || amount <= 0) return;

        KeyStack stack = Find(keys, keyType);
        if (stack == null)
        {
            stack = new KeyStack(keyType, 0);
            keys.Add(stack);
        }

        stack.Count += amount;
        KeyCountChanged?.Invoke(keyType, stack.Count);
        onKeyCollected?.Invoke();
    }

    /// <summary>Spend keys. Returns false (and changes nothing) if the player doesn't have enough.</summary>
    public bool TryUse(KeyType keyType, int amount = 1)
    {
        if (!Has(keyType, amount)) return false;

        KeyStack stack = Find(keys, keyType);
        stack.Count -= amount;
        int remaining = stack.Count;
        if (remaining <= 0) keys.Remove(stack);

        KeyCountChanged?.Invoke(keyType, remaining);
        onKeyUsed?.Invoke();
        return true;
    }

    public void Clear()
    {
        keys.Clear();
    }

    /// <summary>Merges duplicate lines and drops empty ones, so there is exactly one line per key type.</summary>
    private void TidyList()
    {
        List<KeyStack> tidy = new List<KeyStack>();

        foreach (KeyStack stack in keys)
        {
            if (stack == null || stack.KeyType == null || stack.Count <= 0) continue;

            KeyStack existing = Find(tidy, stack.KeyType);
            if (existing != null) existing.Count += stack.Count;
            else tidy.Add(new KeyStack(stack.KeyType, stack.Count));
        }

        keys = tidy;
    }

    private static KeyStack Find(List<KeyStack> list, KeyType keyType)
    {
        foreach (KeyStack stack in list)
        {
            if (stack != null && stack.KeyType == keyType) return stack;
        }
        return null;
    }
}
