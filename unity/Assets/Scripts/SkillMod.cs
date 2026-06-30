using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The six structural hook-points every active skill runs through (Active Skill Charter §8).
/// A modifier plugs into exactly ONE phase, and a skill holds at most one modifier per phase —
/// that "≤1 per phase" rule is what gives us legal, readable builds for free (no degenerate
/// stacking). The enum order doubles as the slot index (0..5) in a loadout, so keep it stable.
///
/// (JS analogy: think of a skill as a request pipeline with six middleware slots; a modifier is
/// one middleware that mutates the shared context as it passes through.)
/// </summary>
public enum SkillPhase
{
    Cast,        // how it goes off: cooldown, charges, cast time, channel
    Targeting,   // where it lands: range, shape, cleave, projectile vs melee
    Impact,      // the hit math: damage shaping, element convert, multi-hit
    OnHit,       // what it does to the target: stun/burn/slow, knockback, mark
    SelfEffect,  // what it does to YOU: heal, shield, lifesteal, dash
    Proc,        // conditional riders: every Nth cast, on-crit, on-kill, below-30%
}

/// <summary>What a modifier actually does when its phase runs. The pipeline interprets these in
/// <see cref="SkillContext.Apply"/>, so a new modifier is a DATA row (id + effect + params + tags),
/// not a new code file. Add an enum case + a line in the switch and every legal skill can wear it.</summary>
public enum ModEffect
{
    QuickenCooldown,   // Cast:       cooldown ×= P0   (P0 < 1 = faster)
    ExtendReach,       // Targeting:  range    ×= P0
    Cleave,            // Targeting:  hit P0 extra nearby targets within P1 radius
    AmplifyDamage,     // Impact:     damageMult ×= P0
    ConvertElement,    // Impact:     the strike's element becomes ElementValue
    ProlongStagger,    // OnHit:      staggerSecs += P0
    ApplySlow,         // OnHit:      chill the target to P0× speed for P1 s
    LifestealOnHit,    // SelfEffect: heal P0 × damage actually dealt
    LifeOnCast,        // SelfEffect: heal P0 × MaxHP on cast (no hit needed)
    SelfShield,        // SelfEffect: on cast, gain a Guard stance — P0 damage cut for P1 s
    EveryNthFree,      // Proc:       every P0-th cast skips its cooldown

    // ---- v1 melee/physical additions (consumed only on the Strike path; see Active Skill Roster v1) ----
    GuaranteedCrit,    // Impact:     this strike always crits (real crit math, not a cosmetic flag)
    MultiHit,          // Impact:     split the strike into P0 separate hits (total damage unchanged)
    Knockback,         // OnHit:      shove the target away at P0 speed for P1 s
    Root,              // OnHit:      pin the target in place for P0 s (a hard slow to ~0)
}

/// <summary>The tag vocabulary used for modifier eligibility. A base skill carries a set of these;
/// a modifier declares <c>Requires</c> / <c>Excludes</c> against them. This is the GENERAL fix
/// (Charter §12): we never hand-list which mods fit which skill — the tag match computes it, so one
/// authored modifier auto-slots into every skill it legally fits and auto-excludes from the rest.</summary>
public static class SkillTag
{
    public const string Melee = "melee";
    public const string Ranged = "ranged";
    public const string Physical = "physical";
    public const string Magic = "magic";
    public const string Offense = "offense";
    public const string Support = "support";
    public const string SingleTarget = "single_target";
    public const string Aoe = "aoe";
}

/// <summary>One authored modifier definition — pure data. Immutable + shared (the catalog owns a
/// single instance of each; a loadout just references it). <see cref="EligibleFor"/> is the whole
/// legality rule: the skill must carry ALL <see cref="Requires"/> tags and NONE of the
/// <see cref="Excludes"/> tags.</summary>
public sealed class SkillModifier
{
    public readonly string Id;
    public readonly string DisplayName;
    public readonly SkillPhase Phase;
    public readonly ModEffect Effect;
    public readonly float P0, P1;
    public readonly Element ElementValue;   // only meaningful for ConvertElement
    public readonly string[] Requires;      // null = no requirement
    public readonly string[] Excludes;      // null = nothing excluded

    public SkillModifier(string id, string displayName, SkillPhase phase, ModEffect effect,
                         float p0 = 0f, float p1 = 0f, Element element = Element.Neutral,
                         string[] requires = null, string[] excludes = null)
    {
        Id = id; DisplayName = displayName; Phase = phase; Effect = effect;
        P0 = p0; P1 = p1; ElementValue = element; Requires = requires; Excludes = excludes;
    }

    /// <summary>True if this modifier is legal on a skill carrying <paramref name="skillTags"/>.</summary>
    public bool EligibleFor(IReadOnlyList<string> skillTags)
    {
        if (Requires != null)
            for (int i = 0; i < Requires.Length; i++)
                if (!Has(skillTags, Requires[i])) return false;
        if (Excludes != null)
            for (int i = 0; i < Excludes.Length; i++)
                if (Has(skillTags, Excludes[i])) return false;
        return true;
    }

    static bool Has(IReadOnlyList<string> tags, string t)
    {
        for (int i = 0; i < tags.Count; i++) if (tags[i] == t) return true;
        return false;
    }
}

/// <summary>
/// The mutable bag a skill builds with its own defaults and then lets each equipped modifier mutate,
/// in phase order, before it executes. This is the "request context" the six middleware slots write to.
/// A struct passed by <c>ref</c>/<c>in</c> so applying a full loadout allocates nothing on the cast path.
/// </summary>
public struct SkillContext
{
    public float DamageMult;          // multiple of a normal swing/cast
    public float FxScale;             // hit-FX size
    public float CooldownMult;        // 1 = unchanged
    public float RangeMult;           // 1 = unchanged
    public float StaggerSecs;         // freeze applied on hit
    public float LifestealFracDealt;  // heal this fraction of damage actually dealt
    public float LifeFracMaxHP;       // heal this fraction of MaxHP on cast (no hit needed)
    public float SlowFactor;          // 1 = no slow; <1 chills the target to this × speed
    public float SlowSecs;            // duration of the slow (0 = none)
    public int   ExtraTargets;        // cleave: extra nearby targets to also hit
    public float CleaveRadius;        // radius to gather cleave targets
    public int   EveryNthFree;        // 0 = never; else every Nth cast is free
    public bool  ConvertElement;      // true = override the strike's element with NewElement
    public Element NewElement;
    public bool  ForceCrit;           // true = this strike always crits
    public int   HitCount;            // 1 = single hit; >1 splits the strike into N hits (total damage unchanged)
    public float KnockbackSpeed;      // 0 = no shove; else shove the target away at this speed
    public float KnockbackSecs;       // how long the shove lasts
    public float RootSecs;            // 0 = none; else pin the target (hard slow to ~0) for this long
    public float ShieldCut;           // 0 = no self-shield; else fraction of incoming damage cut on cast
    public float ShieldSecs;          // how long the self-shield lasts

    /// <summary>Seed the context from the base skill's own tuned numbers (before any modifier).</summary>
    public static SkillContext Default(float damageMult, float fxScale, float staggerSecs)
    {
        return new SkillContext
        {
            DamageMult = damageMult,
            FxScale = fxScale,
            CooldownMult = 1f,
            RangeMult = 1f,
            StaggerSecs = staggerSecs,
            LifestealFracDealt = 0f,
            LifeFracMaxHP = 0f,
            SlowFactor = 1f,
            SlowSecs = 0f,
            ExtraTargets = 0,
            CleaveRadius = 0f,
            EveryNthFree = 0,
            ConvertElement = false,
            NewElement = Element.Neutral,
            ForceCrit = false,
            HitCount = 1,
            KnockbackSpeed = 0f,
            KnockbackSecs = 0f,
            RootSecs = 0f,
            ShieldCut = 0f,
            ShieldSecs = 0f,
        };
    }

    /// <summary>Fold one modifier's effect into the context. The whole interpreter lives here, so
    /// modifiers stay data — see <see cref="ModEffect"/>.</summary>
    public void Apply(SkillModifier m)
    {
        switch (m.Effect)
        {
            case ModEffect.QuickenCooldown: CooldownMult *= m.P0; break;
            case ModEffect.ExtendReach:     RangeMult *= m.P0; break;
            case ModEffect.Cleave:          ExtraTargets += (int)m.P0; CleaveRadius = Mathf.Max(CleaveRadius, m.P1); break;
            case ModEffect.AmplifyDamage:   DamageMult *= m.P0; break;
            case ModEffect.ConvertElement:  ConvertElement = true; NewElement = m.ElementValue; break;
            case ModEffect.ProlongStagger:  StaggerSecs += m.P0; break;
            case ModEffect.ApplySlow:       SlowFactor = Mathf.Min(SlowFactor, Mathf.Clamp01(m.P0)); SlowSecs = Mathf.Max(SlowSecs, m.P1); break;
            case ModEffect.LifestealOnHit:  LifestealFracDealt += m.P0; break;
            case ModEffect.LifeOnCast:      LifeFracMaxHP += m.P0; break;
            case ModEffect.SelfShield:      ShieldCut = Mathf.Max(ShieldCut, m.P0); ShieldSecs = Mathf.Max(ShieldSecs, m.P1); break;
            case ModEffect.EveryNthFree:    EveryNthFree = EveryNthFree == 0 ? (int)m.P0 : Mathf.Min(EveryNthFree, (int)m.P0); break;
            case ModEffect.GuaranteedCrit:  ForceCrit = true; break;
            case ModEffect.MultiHit:        HitCount = Mathf.Max(Mathf.Max(1, HitCount), (int)m.P0); break;
            case ModEffect.Knockback:       KnockbackSpeed = Mathf.Max(KnockbackSpeed, m.P0); KnockbackSecs = Mathf.Max(KnockbackSecs, m.P1); break;
            case ModEffect.Root:            RootSecs = Mathf.Max(RootSecs, m.P0); break;
        }
    }
}
