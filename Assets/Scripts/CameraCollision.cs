using Unity.Cinemachine; // gives us access to Cinemachine classes (CinemachineExtension, CameraState, etc.)
using UnityEngine;       // gives us access to Unity classes (Transform, Vector3, Physics, LayerMask, etc.)

// CAMERA COLLISION
// Put this script on the "FreeLook Camera" object (the one with CinemachineCamera + CinemachineOrbitalFollow).
// Then drag the player's "Camera Target" object into the "Target" slot in the Inspector.
//
// What it does every frame:
//   1. Cinemachine's Orbital Follow decides where the camera WANTS to be (behind the player).
//   2. This script throws an invisible ball (SphereCast) from the target toward that spot.
//   3. If the ball hits a wall/floor/object, the camera is moved forward to just before the hit point.
//   4. When nothing is in the way anymore, the camera slides back out to its normal distance.

public class CameraCollision : CinemachineExtension // a CinemachineExtension is a script Cinemachine calls itself while it moves the camera
{
    public Transform target; // the point the camera protects (drag the player's "Camera Target" here)

    public LayerMask obstacleLayers; // which layers the camera can NOT go through (tick everything except Player)

    public float cameraRadius = 0.1f; // size of the invisible ball; bigger = camera stays further away from walls (keep it smaller than the player, the player is only 0.4 m tall)

    public float minDistance = 0.2f; // the camera never comes closer to the target than this (meters)

    public float returnSpeed = 4f; // how fast (meters per second) the camera slides back out after a wall is gone

    private float currentDistance = -1f; // the camera distance we used last frame (-1 means "first frame, nothing saved yet")

    // Cinemachine calls this function several times every frame, once after each step ("stage") of moving the camera.
    // The stages run in this order: Body (position) -> Aim (rotation) -> Noise (shake) -> Finalize.
    protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Body) return; // only run right after the position is decided, so the aim step afterwards looks at the player from our fixed spot

        if (target == null) return; // no target dragged in yet, so there is nothing to protect: do nothing

        Vector3 pivot = target.position; // the start point of the ball: the player's camera target

        Vector3 wanted = state.RawPosition; // the position the orbit wants the camera to be at

        Vector3 toCamera = wanted - pivot; // an arrow pointing from the target to the wanted camera position

        float wantedDistance = toCamera.magnitude; // the length of that arrow = how far the camera wants to be

        if (wantedDistance < 0.001f) return; // camera is basically on top of the target: no direction to check, so stop

        Vector3 direction = toCamera / wantedDistance; // the same arrow shortened to length 1 (only the direction)

        float allowedDistance = wantedDistance; // start by assuming nothing is in the way, so the full distance is allowed

        RaycastHit hit; // this will hold information about what the ball hit (if it hits anything)

        bool hitSomething = Physics.SphereCast(pivot, cameraRadius, direction, out hit, wantedDistance, obstacleLayers, QueryTriggerInteraction.Ignore); // throw the ball from the target toward the camera; triggers (pickups, zones) are ignored

        if (hitSomething) // if the ball hit a wall/floor/object on the way
        {
            allowedDistance = hit.distance; // the camera may only go as far as the ball travelled before touching it
        }

        // EXTRA SAFETY: a SphereCast IGNORES any wall the ball is already touching at the start.
        // When the player presses against a wall, the ball (at the target) already touches that wall, so the
        // SphereCast misses it and the camera slips through. A thin line (Raycast) does not have this problem,
        // so we also shoot a line and use whichever result is closer.
        RaycastHit lineHit; // this will hold information about what the thin line hit

        bool lineHitSomething = Physics.Raycast(pivot, direction, out lineHit, wantedDistance, obstacleLayers, QueryTriggerInteraction.Ignore); // shoot a thin line from the target toward the camera; triggers are ignored

        if (lineHitSomething) // if the line hit a wall/floor/object on the way
        {
            float lineDistance = lineHit.distance - cameraRadius; // stop the camera "cameraRadius" before the hit point, so it keeps the same gap from the wall as the ball does

            if (lineDistance < allowedDistance) // if the line found something closer than the ball did
            {
                allowedDistance = lineDistance; // use the closer one, so the camera stays in front of BOTH results
            }
        }

        if (allowedDistance < minDistance) // if that is closer than our minimum distance
        {
            allowedDistance = minDistance; // push it back out to the minimum so the camera doesn't go inside the player's head
        }

        if (allowedDistance > wantedDistance) // if the minimum pushed it further than the orbit wanted (very short orbit)
        {
            allowedDistance = wantedDistance; // never go further than the orbit wanted
        }

        if (currentDistance < 0f || deltaTime < 0f) // first frame, or Cinemachine asks for an instant update (camera cut / teleport)
        {
            currentDistance = allowedDistance; // jump straight to the allowed distance, no smoothing
        }
        else if (allowedDistance < currentDistance) // a wall came between the camera and the target
        {
            currentDistance = allowedDistance; // move in IMMEDIATELY so the camera is never inside the wall, even for one frame
        }
        else // the way is clear (or clearer than before)
        {
            currentDistance = Mathf.MoveTowards(currentDistance, allowedDistance, returnSpeed * deltaTime); // slide back out a little bit each frame
        }

        state.RawPosition = pivot + direction * currentDistance; // place the camera: start at the target, go along the direction by the final distance
    }
}
