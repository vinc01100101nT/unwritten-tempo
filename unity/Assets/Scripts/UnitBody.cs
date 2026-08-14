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
/// single non-allocating <c>TryGetComponent</c>; a billboard mob carries no <see cref="UnitBody"/> at all, so it
/// takes the fast miss and keeps the scale rule it has always used.</para>
/// </summary>
[DisallowMultipleComponent]
public class UnitBody : MonoBehaviour
{
    [Tooltip("Feet → mid-body distance in world units. Half the body's real rendered height.")]
    public float halfHeight;

    /// <summary>Declare <paramref name="root"/>'s real body height (feet to crown). Idempotent — call it again after
    /// a rebuild and it just updates the number.</summary>
    public static void Set(GameObject root, float bodyHeight)
    {
        if (root == null || bodyHeight <= 0f) return;
        var b = root.GetComponent<UnitBody>() ?? root.AddComponent<UnitBody>();
        b.halfHeight = 0.5f * bodyHeight;
    }
}
