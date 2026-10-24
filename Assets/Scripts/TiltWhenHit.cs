using UnityEngine; // gives us access to Unity classes (MonoBehaviour, Quaternion, BoxCollider, Renderer, etc.)

// TILT WHEN HIT
// Put this on an object (the Level1Sign). When the player hits it with the sword, it smoothly moves AND turns from
// where it stands at the start of the game (its placement in the scene) to "End Position" / "End Angles", and stays there.
//
// How it connects to the sword (PlayerSwordAttack):
//   - The sword looks for SOLID colliders (not triggers) on the layers in its Hit Mask, then asks each one for an
//     IDamageable script and calls its TakeDamage. It never knows what it hit: enemy, crate, or this sign.
//   - So this script IS an IDamageable (": IDamageable" below), and Awake gives the sign a solid box collider.
//   - The sign's layer must be ticked in the sword's Hit Mask (Player prefab), or the sword can't see it.
//     To make the sign WALKABLE, put it on the Ground layer (PlayerController only counts Ground-layer things as floor).
//     The sword's Hit Mask has Enemy + Default + Ground ticked, so it can hit the sign either way.
//   - Set the layer on the TOP object (Level1Sign itself): the box collider below is added to that object.
//
// How it works:
//   1. Awake remembers the starting position and rotation and, if the sign has no collider, builds a box around the model.
//   2. The sword hits the box -> PlayerSwordAttack calls TakeDamage below.
//   3. TakeDamage switches Update on, and Update moves the sign a little each frame.
//   4. When the move is finished, Update is switched off again, so the sign costs nothing for the rest of the game.

public class TiltWhenHit : MonoBehaviour, IDamageable // MonoBehaviour = a script Unity can put on an object; IDamageable = "the sword can hit me"
{
    public Vector3 endPosition = new Vector3(-11.330491f, 24.93f, 0.47f); // where the sign ends up (like the Inspector's Position)
    public Vector3 endAngles = new Vector3(25.609f, 0f, 0f);              // how the sign ends up turned (X, Y, Z degrees, like the Inspector's Rotation)
    public float moveSeconds = 0.6f;                                      // how long the whole move takes

    private Vector3 startPosition;       // the position at the start of the game (saved in Awake)
    private Quaternion startRotation;    // the rotation at the start of the game (saved in Awake)
    private Quaternion endRotation;      // endAngles turned into a Quaternion (Unity's rotation type), worked out once in Awake
    private float timePassed;            // seconds since the move started (counted up in Update)
    private bool hasMoved;               // true once the sign was hit, so it only moves once

    // Awake runs once, when the object is created.
    private void Awake()
    {
        startPosition = transform.localPosition;           // remember where we start (the position set in the scene)
        startRotation = transform.localRotation;           // remember how we start turned (the rotation set in the scene)
        endRotation = Quaternion.Euler(endAngles);         // degrees -> Quaternion, done once instead of every frame

        if (GetComponent<Collider>() == null) AddHitBox(); // no collider of our own: build one so the sword has something to hit

        enabled = false; // switch Update OFF until the sign is hit (an idle sign shouldn't run code every frame)
    }

    // The sword (PlayerSwordAttack.DetectHits) calls this when its swing touches our box.
    // amount and hitDirection are part of IDamageable; a sign has no health, so we don't need them.
    public void TakeDamage(float amount, Vector3 hitDirection)
    {
        if (hasMoved) return;  // already knocked down: ignore more hits

        hasMoved = true;       // never start again
        timePassed = 0f;       // start the timer from zero
        enabled = true;        // switch Update ON, it does the actual moving
    }

    // Update runs every frame, but ONLY while the move is happening (see enabled above).
    private void Update()
    {
        timePassed += Time.deltaTime;                         // count the time since the move started

        float progress = 1f;                                  // 0 = start placement, 1 = end placement
        if (moveSeconds > 0f) progress = timePassed / moveSeconds; // how far through the move we are
        if (progress > 1f) progress = 1f;                     // never go past the end

        float eased = Mathf.SmoothStep(0f, 1f, progress);     // starts slow, speeds up, slows down at the end: looks more natural than a constant speed
        transform.localPosition = Vector3.Lerp(startPosition, endPosition, eased);     // Lerp = the point "eased" of the way from start to end
        transform.localRotation = Quaternion.Slerp(startRotation, endRotation, eased); // Slerp = the same idea for rotations

        if (progress >= 1f) enabled = false;                  // finished: switch Update off again for good
    }

    // Builds a solid box collider that fits around the model. Called once from Awake.
    private void AddHitBox()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(); // every visible part of the model (done once, here)
        if (renderers.Length == 0)                                   // nothing visible: we can't know how big the box should be
        {
            Debug.LogWarning(name + ": TiltWhenHit found no model to size its hit box. Add a Box Collider yourself.", this);
            return;
        }

        // Renderer bounds are boxes lined up with the WORLD, so measuring a turned sign would give a wrong (too big,
        // swapped) box. So we turn the sign straight for a moment, measure it, and turn it back right after.
        transform.localRotation = Quaternion.identity;               // identity = no rotation at all

        Bounds bounds = renderers[0].bounds;                          // start with the first part's world-space box...
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds); // ...and grow it to cover every part

        // The box lives on this object, so its center and size must be in this object's own (local) space.
        // The sign is straight right now, so turning world -> local is just "undo the position and the scale".
        Vector3 scale = transform.lossyScale;                         // our real scale in the world (parents included)
        Vector3 localSize = new Vector3(bounds.size.x / scale.x, bounds.size.y / scale.y, bounds.size.z / scale.z); // world size -> local size
        Vector3 localCenter = transform.InverseTransformPoint(bounds.center); // the middle of the model, in local space

        transform.localRotation = startRotation;                      // turn the sign back to its starting rotation

        BoxCollider box = gameObject.AddComponent<BoxCollider>();     // add the box to this object (solid: the sword ignores triggers, and the player can't walk through the sign)
        box.center = localCenter;                                     // put it around the model; it turns along with the sign from now on
        box.size = localSize;                                         // exactly the model's size
    }
}
