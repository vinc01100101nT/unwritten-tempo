using UnityEngine;

/// <summary>
/// Puts the game's "ears" (<see cref="AudioListener"/>) on the PLAYER instead of the camera, so 3D
/// positional sounds (<see cref="Sfx.PlayAt"/>) attenuate by distance from the player — not from the
/// camera, which orbits ~10 units above/behind in this 2.5D rig and would make everything sound far and
/// mis-panned.
///
/// The listener lives on a dedicated <c>Ears</c> child (created here) so we can freely rotate it to match
/// the camera's ground-facing — screen-right ⇒ right ear — WITHOUT spinning the player's billboard/root.
/// The basis comes from <see cref="CameraRig"/> (computed once per frame, shared by all the billboards),
/// so this adds no <c>Camera.main</c> lookup and allocates nothing per frame.
///
/// Invariant: exactly one active listener. <see cref="Awake"/> silences every other one (the camera's,
/// any left over from an additively-loaded map) — a graceful migration even on scenes still carrying an
/// old camera listener.
/// </summary>
[DisallowMultipleComponent]
public class AudioListenerRig : MonoBehaviour
{
    Transform ears;

    void Awake()
    {
        var go = new GameObject("Ears");
        ears = go.transform;
        ears.SetParent(transform, false);   // local zero → sits exactly at the player
        go.AddComponent<AudioListener>();

        // Exactly one active listener in the scene — disable every other (camera, map leftovers).
        foreach (var other in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            if (other.gameObject != go) other.enabled = false;
    }

    void LateUpdate()
    {
        // Face the ears along the camera's ground-forward so left/right panning matches the screen
        // under the orbiting camera. LookRotation is a value type → no allocation.
        Vector3 fwd = CameraRig.ForwardXZ;
        if (fwd.sqrMagnitude > 1e-6f)
            ears.rotation = Quaternion.LookRotation(fwd, Vector3.up);
    }
}
