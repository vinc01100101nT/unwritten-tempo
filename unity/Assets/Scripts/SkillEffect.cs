using UnityEngine;

/// <summary>Which entity categories a skill tick applies to. Absolute (RO-faction-free): the author picks who can
/// be hit; the caster (the player) is who deals it. A <c>[Flags]</c> mask so one tick can hit several at once.
/// In the solo slice <see cref="Player"/> == "self" (buffs/heals on the caster).</summary>
[System.Flags]
public enum SkillTargets
{
    None   = 0,
    Player = 1 << 0,
    Mobs   = 1 << 1,
    Npc    = 1 << 2,   // reserved: wired once NPCs become damageable
}

/// <summary>How a tick picks its victims.</summary>
public enum SkillScope
{
    /// <summary>Hit EVERY enabled entity inside the radius (the AoE contact zone).</summary>
    Area,
    /// <summary>Hit ONLY the cast's designated target (<see cref="VfxContext.targetUnit"/>) — always lands,
    /// range-independent (radius ignored). Falls back to <see cref="Area"/> if the cast set no target.</summary>
    Single,
}

/// <summary>What a single skill effect does to a contacted entity.</summary>
public enum SkillEffectKind
{
    /// <summary>Roll the caster's ATK/MATK × <see cref="SkillEffect.damageMult"/>, element-aware → the existing
    /// <c>Health</c>/<c>Character</c> damage sinks.</summary>
    Damage,
    /// <summary>Restore <see cref="SkillEffect.amount"/> flat HP → <c>Health.Heal</c>/<c>Character.Heal</c>.</summary>
    Heal,
    /// <summary>Inflict an <see cref="Ailment"/> (DoT / stun / slow / …) for <see cref="SkillEffect.seconds"/> at
    /// <see cref="SkillEffect.magnitude"/> → the shared <c>Status.Apply</c> front door.</summary>
    Status,
    /// <summary>Timed move-speed buff ×<see cref="SkillEffect.magnitude"/> for <see cref="SkillEffect.seconds"/> →
    /// <c>StatusController.Haste</c>.</summary>
    Haste,
}

/// <summary>One gameplay OUTCOME a skill tick applies. Presentation-neutral INTENT — the central
/// <see cref="SkillEffectApplier"/> maps it onto the SAME combat sinks skills already call (no second damage
/// path). Damage scales off the caster so a tick tracks the caster's stats/crit like every other skill. This is
/// the relocated old <c>VfxEffect</c>: gameplay is now owned by the Skill layer, not the VFX asset.</summary>
[System.Serializable]
public class SkillEffect
{
    public SkillEffectKind kind = SkillEffectKind.Damage;

    [Header("Damage")]
    [Tooltip("Magic (MATK vs MDEF, unmissable) vs physical (ATK vs DEF, as an always-lands skill hit).")]
    public bool magic = true;
    [Tooltip("Damage = the caster's rolled ATK/MATK × this. 1 = one normal hit. (Level scaling is added on top.)")]
    public float damageMult = 1f;
    [Tooltip("Attack element (RO property), checked against the target's armor element.")]
    public Element element = Element.Neutral;

    [Header("Heal")]
    [Tooltip("Flat HP restored (Heal kind).")]
    public int amount = 10;

    [Header("Status / Haste")]
    [Tooltip("Which ailment to inflict (Status kind).")]
    public Ailment ailment = Ailment.Burn;
    [Tooltip("Duration in seconds (Status / Haste).")]
    public float seconds = 4f;
    [Tooltip("Status: DoT damage-per-tick, Slow factor (0..1), or Blind HIT loss. Haste: move-speed multiplier (>1).")]
    public float magnitude = 3f;
}
