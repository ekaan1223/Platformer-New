using System.Collections.Generic; // gives us List
using UnityEngine;                // gives us access to Unity classes (MonoBehaviour, BoxCollider, Physics, etc.)

// WEAPON HITBOX
// Put this on a weapon that sits in a hand (the player's sword, the Demon Doll's weapon).
// The weapon needs a BoxCollider on the same object: that box is the part of the weapon that hurts.
// The box is only used as a shape. It is switched to a trigger in Awake, so it never pushes anything around.
//
// How it is used:
//   - An attack script (PlayerSwordAttack or EnemyWeaponAttack) calls StartSwing when the blade should hurt,
//     and StopSwing when the swing is over.
//   - While the swing is on, every frame this script looks for bodies INSIDE the box. Only what the blade really
//     touches gets hurt. If the target ran away, the blade misses.
//   - Each target is hurt only once per swing, even if the blade stays inside it for many frames.
//
// Select the weapon in the Scene view to see the box: YELLOW = off, RED = swinging (can hurt).

[RequireComponent(typeof(BoxCollider))] // the box shape is required, Unity adds one if it is missing
public class WeaponHitbox : MonoBehaviour // MonoBehaviour = a script Unity can put on an object
{
    [Tooltip("How much damage one hit does.")]
    public float damage = 1f;

    [Tooltip("ON = this weapon only hurts the player (use it for enemy weapons, so enemies don't hurt each other).")]
    public bool onlyHitPlayer = false;

    private BoxCollider box;                // the hurting part of the weapon (found in Awake)
    private bool isSwinging;                // true between StartSwing and StopSwing
    private Transform owner;                // who holds the weapon. Never hurt by their own weapon
    private List<IDamageable> hitThisSwing = new List<IDamageable>(); // who was already hurt in this swing
    private Collider[] touchingBuffer = new Collider[16];             // reused for every check, so swinging creates no garbage

    // True while the blade can hurt. Other scripts can read it (for example to play a sound).
    public bool IsSwinging
    {
        get { return isSwinging; }
    }

    // Awake runs once, when the object is created.
    private void Awake()
    {
        box = GetComponent<BoxCollider>();
        box.isTrigger = true; // a shape only: the weapon must never push the player or the enemy
    }

    // The attack script calls this when the blade starts to hurt.
    // whoHoldsIt = the root of the character holding the weapon (it is never hurt by it).
    public void StartSwing(Transform whoHoldsIt)
    {
        owner = whoHoldsIt;
        hitThisSwing.Clear(); // a new swing: everybody can be hit again
        isSwinging = true;
        CheckHits();          // hit on the very first frame too
    }

    // The attack script calls this when the swing is over (or the attack was cancelled).
    public void StopSwing()
    {
        isSwinging = false;
    }

    // Update runs every frame.
    private void Update()
    {
        if (isSwinging) CheckHits(); // only look for targets while the blade can hurt
    }

    // Finds every body inside the box right now and hurts the ones that were not hurt yet in this swing.
    private void CheckHits()
    {
        // The box in world space: its middle, half of its size (scaled like the weapon) and its rotation.
        Vector3 center = transform.TransformPoint(box.center);
        Vector3 halfSize = Vector3.Scale(box.size, transform.lossyScale) * 0.5f;
        halfSize = new Vector3(Mathf.Abs(halfSize.x), Mathf.Abs(halfSize.y), Mathf.Abs(halfSize.z)); // a negative scale must not make the box inside-out

        // Every solid collider inside the box. Triggers (like an enemy's head zone) are skipped.
        // "NonAlloc" fills our ready-made list instead of making a new one every frame. count = how many it found.
        int count = Physics.OverlapBoxNonAlloc(center, halfSize, touchingBuffer, transform.rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            Collider other = touchingBuffer[i];

            if (owner != null && other.transform.IsChildOf(owner)) continue; // our own body: skip it
            if (onlyHitPlayer && !PlayerController.IsPlayer(other)) continue; // enemy weapon: only the player counts (checked first, it's the cheapest)

            IDamageable target = other.GetComponentInParent<IDamageable>(); // the health script of what we touched
            if (target == null) continue;                                    // a wall, the floor...: nothing to hurt
            if (hitThisSwing.Contains(target)) continue;                     // already hurt in this swing

            // Push direction: from the weapon's holder toward the target, flat on the ground.
            Vector3 from = transform.position;      // no holder: from the weapon itself
            if (owner != null) from = owner.position; // from the holder
            Vector3 hitDirection = other.bounds.center - from;
            hitDirection.y = 0f;
            if (hitDirection.sqrMagnitude < 0.0001f) hitDirection = transform.forward; // right on top of each other: use the blade's direction
            hitDirection.Normalize();

            hitThisSwing.Add(target); // remember it, so this swing can't hurt it again
            target.TakeDamage(damage, hitDirection);
        }
    }

    // Scene view only: draws the box (red while swinging).
    private void OnDrawGizmosSelected()
    {
        BoxCollider shape = GetComponent<BoxCollider>();
        if (shape == null) return;

        Gizmos.color = Color.yellow;               // yellow: off
        if (isSwinging) Gizmos.color = Color.red;  // red: swinging, can hurt
        Gizmos.matrix = transform.localToWorldMatrix; // draw in the weapon's own space, so the box turns with it
        Gizmos.DrawWireCube(shape.center, shape.size);
    }
}
