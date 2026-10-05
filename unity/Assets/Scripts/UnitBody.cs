using UnityEngine;

/// <summary>
/// A unit's REAL rendered half-height (feet to mid-body), for units whose root scale is not their size.
///
/// <para><b>Why it exists.</b> <see cref="UnitAnchor"/> normally derives "how tall is this unit" from its root scale,
/// which is correct for a billboard mob: the sprite IS the root, so scaling the root scales the body. It is wrong for
/// every LIVE-3D character, because their root deliberately carries a scale that has nothing to do with their size —
/// a 3D character's root must stay unscaled (a CharacterController silently refuses to move at character scale), and
/// the body rides it as a separately-fitted child.
///
/// <para>Measured, before this existed: a shadow mob's root sat at scale 1 while its body was 0.327 tall, so the
/// anchor put its mid-body at 0.4 and its head at 0.8 — about two and a half body-lengths up. The player's root sits
/// at 0.7 against the same 0.327 body, putting its centre at 0.28 instead of 0.164. Every floating number, nameplate,
/// cast bar, target marker and — worse — every projectile homing point and cursor snap point inherited that error.</para>
///
/// <para>PERFORMANCE: a plain field written ONCE when the body is built. <see cref="UnitAnchor"/> reads it with a
/// single non-allocating <c>TryGetComponent</c>. A baked billboard mob carries one too, written when its size is
/// fitted (<see cref="BillboardAnimator3D.FitToHeight"/>), so its anchors sit on its measured art; a billboard with
/// none keeps the scale rule.</para>
/// </summary>
[DisallowMultipleComponent]
public class UnitBody : MonoBehaviour
{
    [Tooltip("Feet → mid-body distance in world units. Half the body's real rendered height.")]
    public float halfHeight;

    [Tooltip("Half the body's real rendered width in world units. ZERO means nobody measured it — readers fall " +
             "back to the scale rule rather than trusting a made-up number.")]
    public float halfWidth;

    [Tooltip("How far above the feet the body is DRAWN, in world units — a flying creature's. Zero for everything " +
             "that stands on the ground. Only the picture and the points read off it rise; the walking body stays down.")]
    public float lift;

    /// <summary>Declare <paramref name="root"/>'s real body height (feet to crown), and its width when the caller
    /// has measured one. Idempotent — call it again after a rebuild and it just updates the numbers.
    ///
    /// <para>Width is optional because only the callers that build a body from a model have a bounds union to read
    /// it from; the ones working from an authored height do not, and a width invented for them would be a wrong
    /// number wearing a measured one's clothes. Left at zero it stays distinguishable from a real width, and
    /// <see cref="UnitAnchor.HalfWidth"/> falls back to the same scale rule it uses for a billboard.</para></summary>
    public static void Set(GameObject root, float bodyHeight, float bodyWidth = 0f)
    {
        if (root == null || bodyHeight <= 0f) return;
        var b = root.GetComponent<UnitBody>() ?? root.AddComponent<UnitBody>();
        b.halfHeight = 0.5f * bodyHeight;
        if (bodyWidth > 0f) b.halfWidth = 0.5f * bodyWidth;
    }

    /// <summary>Declare how far above its feet <paramref name="root"/>'s body is drawn (<see cref="lift"/>). Zero on a
    /// body with no <see cref="UnitBody"/> adds nothing, since a missing one already reads as zero.</summary>
    public static void SetLift(GameObject root, float lift)
    {
        if (root == null) return;
        var b = root.GetComponent<UnitBody>();
        if (b == null)
        {
            if (lift <= 0f) return;
            b = root.AddComponent<UnitBody>();
        }
        b.lift = Mathf.Max(0f, lift);
    }
}
