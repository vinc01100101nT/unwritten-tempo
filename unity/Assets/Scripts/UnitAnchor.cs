using UnityEngine;

/// <summary>
/// Single source of truth for a unit's vertical anchor points (player, mobs, NPCs — any grounded billboard).
///
/// INVARIANT: a unit's <c>transform.position</c> is its FEET, seated on the ground (pivot = base anchor —
/// the one anchor that's size-invariant: base == position whether the unit is a rat or a giant). Nothing
/// else in the world should hand-add <c>Vector3.up * magicNumber</c> to reach a unit's centre or head —
/// route through here so the offset lives in ONE place and can't drift between call sites.
///
/// <see cref="HalfHeight"/> owns the old scattered <c>0.4f * lossyScale.y</c> constant (0.5 cell − ~0.1 foot
/// padding; see Build25DSpike.SpriteFoot). It was typed independently in MobNameplate, UnitTargeter, etc.;
/// now they read it here.
///
/// Cheap value math only (multiply + Vector3 add) — no alloc / GetComponent — so it's safe in the per-frame
/// aim/targeter paths, which already read lossyScale today.
/// </summary>
public static class UnitAnchor
{
    // Feet→centre distance in world units. A billboard is ~one world cell tall at its uniform scale; the
    // usable body is ~0.8 of that (the rest is transparent foot/head padding), so half-height ≈ 0.4 × scale.
    // lossyScale.y already folds in Build25DSpike.BillboardScale, so this tracks per-mob scale automatically.
    public const float HalfHeightFactor = 0.4f;

    // Feet→centre distance (world units) for this unit, scaled to its size.
    public static float HalfHeight(Transform t) => HalfHeightFactor * Mathf.Max(0.0001f, t.lossyScale.y);

    // The unit's feet on the ground. With the feet-pivot invariant this IS transform.position; kept as a
    // named accessor so callers read intent, not a raw field.
    public static Vector3 Feet(Transform t) => t.position;

    // Mid-body — the visual centre of the sprite. Aim, projectile homing and ground-marker math target this.
    public static Vector3 Center(Transform t) => t.position + Vector3.up * HalfHeight(t);

    // Top of the sprite — where floating labels/popups sit so they clear the head.
    public static Vector3 Head(Transform t) => t.position + Vector3.up * (2f * HalfHeight(t));
}
