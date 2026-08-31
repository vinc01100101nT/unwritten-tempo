#if UNITY_ANDROID
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The on-screen move stick. Writes <see cref="TouchControls.MoveAxis"/> and nothing else — the player controller
/// reads that axis exactly where it reads WASD, so the walking, sprinting, facing and footstep code below it is
/// the same code the desktop runs.
///
/// <para>It is a uGUI element, which is what keeps it out of the world gestures for free: a finger on the stick
/// is a finger over UI, so <see cref="TouchControls"/> never sees it as a tap or a camera drag. The two input
/// paths are separated by WHERE the finger is, not by guessing at what it meant.</para>
///
/// <para>Analog, not eight-way: the axis keeps its magnitude, so a half-pushed stick walks at half speed. The
/// dead zone is remapped rather than clipped, so the very first movement past it starts from a standstill
/// instead of snapping to a third of full speed.</para>
/// </summary>
public class TouchStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    /// <summary>Fraction of the ring radius that reads as no input, so a resting thumb doesn't creep.</summary>
    const float DeadZone = 0.18f;

    public RectTransform knob;

    RectTransform rt;

    void Awake() => rt = (RectTransform)transform;

    public void OnPointerDown(PointerEventData e) => Track(e);
    public void OnDrag(PointerEventData e) => Track(e);

    public void OnPointerUp(PointerEventData e)
    {
        TouchControls.MoveAxis = Vector2.zero;
        if (knob != null) knob.anchoredPosition = Vector2.zero;
    }

    void Track(PointerEventData e)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var local))
            return;

        float radius = rt.rect.width * 0.5f;
        if (radius <= 0.01f) return;

        Vector2 v = local / radius;
        float mag = v.magnitude;
        if (mag <= DeadZone) v = Vector2.zero;
        else
        {
            // Remap [DeadZone..1] onto [0..1] so speed ramps from zero at the edge of the dead zone.
            Vector2 dir = v / mag;
            v = dir * Mathf.Min((mag - DeadZone) / (1f - DeadZone), 1f);
        }

        TouchControls.MoveAxis = v;
        if (knob != null) knob.anchoredPosition = Vector2.ClampMagnitude(local, radius);
    }

    void OnDisable()
    {
        // Never leave the player walking because the stick was hidden mid-push.
        TouchControls.MoveAxis = Vector2.zero;
    }
}
#endif
