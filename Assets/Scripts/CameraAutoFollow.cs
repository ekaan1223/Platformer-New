using Unity.Cinemachine; // gives us access to Cinemachine classes (CinemachineOrbitalFollow, etc.)
using UnityEngine;       // gives us access to Unity classes (MonoBehaviour, Mathf, Vector3, Time, etc.)

// CAMERA AUTO FOLLOW
// Put this script on the "FreeLook Camera" object (the one with CinemachineOrbitalFollow), next to CameraCollision.
// It gives the camera two moods, like the camera in Mario Odyssey:
//
//   STANDING STILL -> a free camera. Move the mouse / right stick and it orbits all the way around the player.
//                     This script does nothing then, and the player does not turn with the camera.
//   MOVING         -> the camera slowly goes back to its normal height and gently swings to follow where the
//                     player runs (run sideways and it slowly turns to look that way).
//
// It NEVER fights the player: the moment the player moves the camera themselves, it stops helping and waits
// "Wait After Look Input" seconds before it gently starts again.
// It only nudges the orbit's two sliders (Horizontal = around, Vertical = height), so CameraCollision still
// stops the camera from going through walls, exactly like before.

[DefaultExecutionOrder(100)] // run after Cinemachine has read the mouse this frame, so we can tell if the player moved the camera
public class CameraAutoFollow : MonoBehaviour // MonoBehaviour = a script Unity can put on an object and call every frame
{
    [Header("References (empty = found automatically)")]
    public PlayerController player;         // the player to follow; empty = the PlayerController this camera sits under
    public CinemachineOrbitalFollow orbit;  // the orbit to nudge; empty = the one on this object

    [Header("When does it help?")]
    public float movingSpeed = 0.5f;            // the player counts as "moving" above this speed (meters per second)
    public float waitAfterLookInput = 1f;       // after the player moves the camera themselves, wait this long before helping again (seconds)

    [Header("How strongly it helps")]
    public float heightReturnSpeed = 1.5f;      // how fast the camera goes back to its normal height while moving (bigger = faster, 0 = never)
    public float followDegreesPerSecond = 90f;  // how fast it swings to follow a player running sideways (0 = never swings)

    private float lastLookInputTime = -999f; // when the player last moved the camera themselves (-999 = long ago)
    private float lastHorizontal;            // the Horizontal slider as we left it last frame
    private float lastVertical;              // the Vertical slider as we left it last frame
    private bool hasLastValues;              // false on the very first frame, when there is nothing to compare with yet

    // Awake runs once, when the object is created.
    private void Awake()
    {
        if (player == null) player = GetComponentInParent<PlayerController>(); // the camera is a child of the player: look up the hierarchy
        if (player == null) player = FindFirstObjectByType<PlayerController>(); // not under the player: search the whole scene
        if (orbit == null) orbit = GetComponent<CinemachineOrbitalFollow>();    // the orbit on this same object

        if (orbit == null) Debug.LogError(name + ": CameraAutoFollow needs a CinemachineOrbitalFollow on the same object.", this);
    }

    // Update runs every frame.
    private void Update()
    {
        if (orbit == null || player == null) return; // missing pieces (or the player died and was removed): do nothing

        float deltaTime = Time.deltaTime; // seconds since the last frame
        if (deltaTime <= 0f) return;      // the game is paused (e.g. a dialogue): nothing moves, so nothing to do

        float horizontal = orbit.HorizontalAxis.Value; // where the camera is AROUND the player right now (degrees)
        float vertical = orbit.VerticalAxis.Value;     // how HIGH the camera is right now (degrees)

        // 1. DID THE PLAYER MOVE THE CAMERA? If the sliders changed since we left them last frame, the mouse/stick did it.
        if (hasLastValues)
        {
            bool turnedAround = Mathf.Abs(Mathf.DeltaAngle(lastHorizontal, horizontal)) > 0.01f; // DeltaAngle = smallest difference, even across 180/-180
            bool movedUpDown = Mathf.Abs(vertical - lastVertical) > 0.01f;                       // the height slider changed
            if (turnedAround || movedUpDown) lastLookInputTime = Time.time;                       // remember WHEN the player last touched the camera
        }

        // 2. SHOULD WE HELP? Only while the player is moving AND has not touched the camera for a moment.
        Vector3 velocity = player.HorizontalVelocity;                          // the player's sideways speed (no up/down part)
        float speed = velocity.magnitude;                                      // how fast that is (meters per second)
        bool isMoving = speed > movingSpeed && !player.IsDead;                 // standing still (or dead) = free camera, no help
        bool handsOff = Time.time - lastLookInputTime >= waitAfterLookInput;   // the player left the camera alone long enough

        if (isMoving && handsOff)
        {
            // 3. HEIGHT: ease back toward the normal height (the orbit's Vertical "Center" value, set in the Inspector).
            float ease = 1f - Mathf.Exp(-heightReturnSpeed * deltaTime);                // a smooth step that is the same at any frame rate
            vertical = Mathf.Lerp(vertical, orbit.VerticalAxis.Center, ease);           // move a little of the way back each frame

            // 4. AROUND: swing a little toward where the player runs, but only for SIDEWAYS running.
            //    Running straight away from the camera needs no swing, and running TOWARD the camera must not spin it around.
            Vector3 cameraForward = transform.forward; // where the camera looks right now
            cameraForward.y = 0f;                      // flatten it: only the direction around the player matters
            if (cameraForward.sqrMagnitude > 0.0001f)  // looking straight down has no "around" direction: skip
            {
                float cameraYaw = Mathf.Atan2(cameraForward.x, cameraForward.z) * Mathf.Rad2Deg; // the camera's compass direction (degrees)
                float moveYaw = Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg;             // the player's running direction (degrees)
                float difference = Mathf.DeltaAngle(cameraYaw, moveYaw);                        // how far the run turns away from the camera's view

                float sideways = Mathf.Sin(difference * Mathf.Deg2Rad); // 0 = straight ahead or straight back, 1 / -1 = fully right / left
                float speedFraction = Mathf.Clamp01(speed / player.walkSpeed); // walking = full help, creeping slowly = less help

                horizontal += sideways * followDegreesPerSecond * speedFraction * deltaTime; // turn the camera a little toward the run
            }

            orbit.HorizontalAxis.Value = orbit.HorizontalAxis.ClampValue(horizontal); // put it back (ClampValue wraps 180 -> -180 for us)
            orbit.VerticalAxis.Value = orbit.VerticalAxis.ClampValue(vertical);       // put it back (ClampValue keeps it inside its range)
        }

        // 5. Remember the sliders as WE left them, so next frame any change means the player moved the camera.
        lastHorizontal = orbit.HorizontalAxis.Value;
        lastVertical = orbit.VerticalAxis.Value;
        hasLastValues = true;
    }
}
