using UnityEngine;

/// <summary>
/// The mutable per-strike context a basic attack carries into <see cref="Player3DAttacker"/>'s hit pipeline —
/// damage/element/on-hit shaping seeded from the swing's own tuned numbers via <see cref="Default"/>. A struct
/// passed by <c>in</c>/<c>ref</c> so a strike allocates nothing on the hot path.
///
/// <para>Historically this was ALSO the output of a six-phase "SkillMod" modifier system (SkillModifier /
/// ModifierCatalog / SkillModBook), which was authored + unit-tested but never wired into a live cast — that dead
/// vessel was removed 2026-07-20. The ring's real behavioral layer now lives in <see cref="RingVoices"/> (driven
/// off ring-stat investment through the live combat sinks). This struct stays because basic attacks still use it
/// as their by-value carrier — every current strike simply passes <see cref="Default"/> (no modifiers).</para>
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

    /// <summary>Seed the context from the base swing's own tuned numbers.</summary>
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
}
