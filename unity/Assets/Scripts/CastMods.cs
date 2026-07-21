using System.Collections.Generic;

/// <summary>
/// The composed OUTGOING reshapers for one cast — the runtime end-state of the design's "mutation fold"
/// (design doc §Collision rule): the weapon's baked affixes are folded first, then the ring's voices on top,
/// into this one value. It's a plain struct (stack, zero-alloc) passed by <c>in</c> into
/// <see cref="SkillEffectApplier.ApplyBurst"/>, which honours each field at the existing combat sinks — so
/// there's no separate mutation engine and no per-cast allocation. Zero/none = identity, so
/// <c>default(CastMods)</c> is "no change" and every existing caller is untouched.
///
/// <para>The two sources own DIFFERENT dials so they never duplicate: the RING feeds the stat voices
/// (<see cref="executePower"/> = Crit, <see cref="extraHits"/> = AttackSpeed, <see cref="radiusBonus"/> = MAtk);
/// the WEAPON feeds control + raw damage (<see cref="slowFactor"/>/<see cref="rootSecs"/>/<see cref="stunSecs"/>/
/// <see cref="pushSpeed"/>/<see cref="pullSpeed"/>/<see cref="rawDamageMult"/>) — the loot pool ring voices don't cover.</para>
/// </summary>
public struct CastMods
{
    // ---- ring voices ----
    /// <summary>MATK · Expand — area radius ×= <c>(1 + radiusBonus)</c>. 0 = unchanged.</summary>
    public float radiusBonus;
    /// <summary>AttackSpeed · Repeat — extra damage echoes on the hit targets. 0 = none.</summary>
    public int extraHits;
    /// <summary>Critical · Execute — finisher power; bonus/target = roll × this × missing-HP fraction. 0 = none.</summary>
    public float executePower;

    // ---- weapon affixes: control (applied to every mob the hit reached) ----
    /// <summary>Slow — the hit slows mobs to this × speed (e.g. 0.5). 0 = none.</summary>
    public float slowFactor;
    public float slowSecs;
    /// <summary>Root — the hit pins mobs (hard stop) for this long. 0 = none.</summary>
    public float rootSecs;
    /// <summary>Stun — the hit stuns mobs for this long. 0 = none.</summary>
    public float stunSecs;
    /// <summary>Push — knock mobs back (away from the cast point) at this speed. 0 = none.</summary>
    public float pushSpeed;
    public float pushSecs;
    /// <summary>Pull — drag mobs toward the cast point at this speed. 0 = none.</summary>
    public float pullSpeed;
    public float pullSecs;

    // ---- weapon affixes: damage flavor ----
    /// <summary>Raw damage — a flat fraction added to the skill's damage multiplier (weapon-baked). 0 = none.</summary>
    public float rawDamageMult;

    /// <summary>True if any reshaper is active — lets the applier skip the whole post-pass on a plain cast.</summary>
    public bool Any =>
        radiusBonus != 0f || extraHits != 0 || executePower > 0f ||
        (slowFactor > 0f && slowSecs > 0f) || rootSecs > 0f || stunSecs > 0f ||
        (pushSpeed > 0f && pushSecs > 0f) || (pullSpeed > 0f && pullSecs > 0f);
    // (rawDamageMult rides the pre-hit damage mult, not the post-pass, so it isn't part of Any.)
}

/// <summary>
/// Per-skill ELIGIBILITY for the outgoing reshapers — the simple form of the design's tag-eligibility. A voice
/// only applies where it can DO something: Execute/Repeat/Raw need a damaging hit; Expand needs an area gather;
/// control needs the hit to reach mobs at all. Both the runtime (gates a cast's <see cref="CastMods"/>) and the
/// skill tooltip (shows only the reshapers that matter for that skill) read this ONE place.
/// </summary>
public static class SkillReshape
{
    static bool HasDamage(List<SkillEffect> fx)
    {
        if (fx == null) return false;
        for (int i = 0; i < fx.Count; i++)
            if (fx[i] != null && fx[i].kind == SkillEffectKind.Damage) return true;
        return false;
    }

    public static bool BindingHitsMobs(SkillTickBinding b)
        => b != null && (b.targets & SkillTargets.Mobs) != 0;

    public static bool BindingDamagesMobs(SkillTickBinding b)
        => BindingHitsMobs(b) && HasDamage(b.effects);

    public static bool BindingIsMobArea(SkillTickBinding b)
        => BindingDamagesMobs(b) && b.scope == SkillScope.Area && b.radius > 0f;

    /// <summary>Does ANY binding reach mobs at all (so a control affix can land)?</summary>
    public static bool HitsMobs(SkillDef def)
    {
        if (def == null || def.bindings == null) return false;
        for (int i = 0; i < def.bindings.Count; i++)
            if (BindingHitsMobs(def.bindings[i])) return true;
        return false;
    }

    /// <summary>Does ANY binding deal damage to mobs (for Execute/Repeat/Raw)?</summary>
    public static bool DamagesMobs(SkillDef def)
    {
        if (def == null || def.bindings == null) return false;
        for (int i = 0; i < def.bindings.Count; i++)
            if (BindingDamagesMobs(def.bindings[i])) return true;
        return false;
    }

    /// <summary>Does ANY binding gather mobs in an area (for Expand)?</summary>
    public static bool HasMobArea(SkillDef def)
    {
        if (def == null || def.bindings == null) return false;
        for (int i = 0; i < def.bindings.Count; i++)
            if (BindingIsMobArea(def.bindings[i])) return true;
        return false;
    }

    /// <summary>Zero out the reshapers THIS binding can't use, so a reshaper that would do nothing here simply
    /// doesn't apply. Applied to the folded weapon+ring result — masks BOTH sources to what the skill can host.</summary>
    public static void GateTo(SkillTickBinding b, ref CastMods mods)
    {
        if (!BindingDamagesMobs(b)) { mods.executePower = 0f; mods.extraHits = 0; mods.rawDamageMult = 0f; }
        if (!BindingIsMobArea(b)) mods.radiusBonus = 0f;
        if (!BindingHitsMobs(b))
        {
            mods.slowFactor = 0f; mods.slowSecs = 0f; mods.rootSecs = 0f; mods.stunSecs = 0f;
            mods.pushSpeed = 0f; mods.pushSecs = 0f; mods.pullSpeed = 0f; mods.pullSecs = 0f;
        }
    }
}
