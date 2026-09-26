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
/// padding; see RigBuilder25D.SpriteFoot). It was typed independently in MobNameplate, UnitTargeter, etc.;
/// now they read it here.
///
/// Cheap value math only (multiply + Vector3 add) — no alloc / GetComponent — so it's safe in the per-frame
/// aim/targeter paths, which already read lossyScale today.
/// </summary>
public static class UnitAnchor
{
    // Feet→centre distance in world units, for a billboard that states no measured body. A billboard is ~one world
    // cell tall at its uniform scale; the usable body is ~0.8 of that (the rest is transparent foot/head padding), so
    // half-height ≈ 0.4 × scale. A baked mob states its measured body (UnitBody, from its size), so this is the fallback.
    public const float HalfHeightFactor = 0.4f;

    /// <summary>Feet→centre distance (world units) for this unit.
    ///
    /// <para>A unit carrying a <see cref="UnitBody"/> states its real half-height outright. That is every LIVE-3D
    /// character (player, townsfolk, shadow mobs), whose root scale is deliberately NOT its size — the body is a
    /// separately-fitted child, so the scale rule below reads the wrong number for them (measured: a shadow's centre
    /// landed at 0.4 on a 0.327-tall body; the player's at 0.28 on that same body) — and every baked billboard mob,
    /// whose art is measured and fitted to its size's height (<see cref="MobBody"/>), so padding in the picture
    /// never lifts its nameplate.</para>
    ///
    /// <para>A billboard that states no body keeps the scale rule. The lookup is one non-allocating
    /// TryGetComponent.</para></summary>
    public static float HalfHeight(Transform t)
        => t.TryGetComponent<UnitBody>(out var b) && b.halfHeight > 0f
            ? b.halfHeight
            : HalfHeightFactor * Mathf.Max(0.0001f, t.lossyScale.y);

    /// <summary>How wide this unit's body is, from its middle out to one side, in world units.
    ///
    /// <para>Same two answers as <see cref="HalfHeight"/>, for the same reason. A LIVE-3D character states its
    /// measured width outright, because its root scale is deliberately not its size; a baked billboard mob states the
    /// painted width of its art at its fitted scale. A billboard that states no width keeps the scale rule: the sprite
    /// IS the root, so the horizontal scale is the body's width.</para>
    ///
    /// <para>The factor is shared with the height on purpose — a billboard's art sits in the same one-cell box
    /// with the same transparent padding on every side, so the usable body is the same fraction across as it is
    /// tall. One number, one place, no second constant to drift.</para></summary>
    public static float HalfWidth(Transform t)
        => t.TryGetComponent<UnitBody>(out var b) && b.halfWidth > 0f
            ? b.halfWidth
            : HalfHeightFactor * Mathf.Max(0.0001f, t.lossyScale.x);

    // The unit's feet on the ground. With the feet-pivot invariant this IS transform.position; kept as a
    // named accessor so callers read intent, not a raw field.
    public static Vector3 Feet(Transform t) => t.position;

    // Mid-body — the visual centre of the sprite. Aim, projectile homing and ground-marker math target this.
    public static Vector3 Center(Transform t) => t.position + Vector3.up * HalfHeight(t);

    // Top of the sprite / crown of the body.
    public static Vector3 Head(Transform t) => t.position + Vector3.up * (2f * HalfHeight(t));

    // Gap above the crown for floating text, as a fraction of HALF-height — so it scales with the unit instead of
    // being a fixed world distance that swamps a small mob and hugs a big one.
    public const float OverheadClearanceFactor = 0.35f;

    /// <summary>Where floating TEXT sits: just clear of the crown, scaled to the unit.
    ///
    /// <para>THE one answer for every popup — damage numbers, MISS, heals, status announcements, level-ups. They used
    /// to use four different rules (the raw feet position, a hand-added +1.0, a hand-added +1.1, or the crown) on top
    /// of a fixed +0.5 buried in the popup pool itself, which is a world distance and therefore size-blind: measured,
    /// status text floated 8.4x a median mob's body height above its feet, and 11.2x on the smallest. Routing every
    /// caller here is what this class was written for — its own summary already forbade hand-added offsets.</para></summary>
    public static Vector3 Overhead(Transform t)
    {
        float h = HalfHeight(t);
        return t.position + Vector3.up * (2f * h + h * OverheadClearanceFactor);
    }
}
