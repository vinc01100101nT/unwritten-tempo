using System.Collections.Generic;
using UnityEngine;

/// <summary>One stat's pull on an effect channel — the atom of an <see cref="SkillEffect"/>'s AFFINITY. The
/// weight is relative (the list is normalised by its own sum), so <c>{MAtk 70, CastSpeed 20, MP 10}</c> just
/// means "mostly MAtk." A channel's affinity multiplier = <c>1 + Σ(normWeight × the caster's ring % in that
/// stat)</c>, so an effect grows as you steer the ring into its home region — the skill IS a readout of your
/// ring position. Empty list ⇒ ×1 (no scaling), so a skill that authors no affinity behaves exactly as before.</summary>
[System.Serializable]
public struct StatWeight
{
    public StatId stat;
    public float weight;
}

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

/// <summary>What a single skill effect does to a contacted entity. The first four are the original set; the rest
/// are the MVP active-skill verbs (each maps onto an EXISTING combat sink where one exists — Guard→BeginGuard,
/// Dash→the roll primitive, Slow/Stun stay <see cref="Status"/>).</summary>
public enum SkillEffectKind
{
    /// <summary>Roll the caster's ATK/MATK × <see cref="SkillEffect.damageMult"/>, element-aware → the existing
    /// <c>Health</c>/<c>Character</c> damage sinks. Owns only <see cref="SkillEffect.hitCount"/> (multi-hit) — each
    /// strike is the trigger point where the binding's PROC effects (Status / Execute) roll. Does ONE thing.</summary>
    Damage,
    /// <summary>Restore <see cref="SkillEffect.amount"/> flat HP → <c>Health.Heal</c>/<c>Character.Heal</c>.</summary>
    Heal,
    /// <summary>Inflict an <see cref="Ailment"/> (DoT / stun / slow / …) for <see cref="SkillEffect.seconds"/> at
    /// <see cref="SkillEffect.magnitude"/> → the shared <c>Status.Apply</c> front door.</summary>
    Status,
    /// <summary>Timed move-speed buff ×<see cref="SkillEffect.magnitude"/> for <see cref="SkillEffect.seconds"/> →
    /// <c>StatusController.Haste</c>.</summary>
    Haste,

    /// <summary>Self incoming-damage cut for <see cref="SkillEffect.seconds"/> at <see cref="SkillEffect.magnitude"/>
    /// (0..1 reduction) → reuses <c>Character.BeginGuard</c>. (Bulwark.)</summary>
    Guard,
    /// <summary>Force a mob's aggro onto the caster for <see cref="SkillEffect.seconds"/> → <c>MonsterAI3D.Taunt</c>.
    /// (Bulwark.)</summary>
    Taunt,
    /// <summary>Instantly TELEPORT the caster to the cast point (no roll, no travel) → <c>Player3DController.Teleport</c>.
    /// Applied to the caster regardless of the tick radius (the destination IS the point). (Blink Step.)</summary>
    Teleport,
    /// <summary>Timed self stat-buff (which stat = <see cref="SkillEffect.buffStat"/>) ×<see cref="SkillEffect.magnitude"/>
    /// for <see cref="SkillEffect.seconds"/>. Damage / AttackSpeed / CastSpeed ride <c>Character</c>; MoveSpeed reuses
    /// Haste. (Blink Step haste, War Banner damage-up.)</summary>
    Buff,
    /// <summary>Heal-over-time on the caster: <see cref="SkillEffect.magnitude"/> HP per tick for
    /// <see cref="SkillEffect.seconds"/> → <c>Character.BeginRegen</c>. (Mending Tide.)</summary>
    Regen,
    /// <summary>Remove up to <see cref="SkillEffect.amount"/> active debuffs from the target → <c>StatusController.Cleanse</c>.
    /// (Mending Tide.)</summary>
    Cleanse,
    /// <summary>Mark a mob to TAKE +<see cref="SkillEffect.magnitude"/> fraction more damage for
    /// <see cref="SkillEffect.seconds"/> → <c>Health.BeginVulnerable</c>. (Hex Field.)</summary>
    Vulnerability,
    /// <summary>Bonus damage vs a LOW-HP target — a PROC (rolled per hit via <see cref="SkillEffect.chance"/>):
    /// deals <c>roll × magnitude × (1 − target HP fraction)</c>, hitting hardest as the target nears death. Its own
    /// self-contained effect, sitting alongside the Damage effect it triggers off. (Meteor.)</summary>
    Execute,
}

/// <summary>Which self-stat a <see cref="SkillEffectKind.Buff"/> effect lifts for its window.</summary>
public enum BuffStat { Damage, AttackSpeed, CastSpeed, MoveSpeed }

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
    [Tooltip("Status: DoT damage-per-tick, Slow factor (0..1), or Blind HIT loss. Haste/Buff: multiplier (>1). " +
             "Guard: damage-cut fraction (0..1). Regen: HP per tick. Vulnerability: extra-damage fraction (0.3 = +30%).")]
    public float magnitude = 3f;

    [Header("Multi-hit (Damage kind)")]
    [Tooltip("How many times a Damage effect strikes per landing. Each strike is the TRIGGER POINT for the binding's " +
             "PROC effects (a Status or Execute with a chance) — they roll once per hit. 1 = single hit.")]
    [Min(1)] public int hitCount = 1;

    [Header("Proc")]
    [Tooltip("PROC chance (0..1), rolled PER HIT of the binding's multi-hit Damage. Used by Status (e.g. a stun proc) " +
             "and Execute. 0 = NOT a proc — the effect applies ONCE per landing instead.")]
    [Range(0f, 1f)] public float chance = 0f;

    [Header("Buff (Buff kind)")]
    [Tooltip("Which self-stat the Buff kind lifts for its window (magnitude = multiplier, seconds = duration).")]
    public BuffStat buffStat = BuffStat.Damage;

    [Header("Affinity (ring-investment scaling)")]
    [Tooltip("Scales this effect's main OUTPUT by ring investment — its damage / heal amount / DoT damage / buff " +
             "amount / slow amount, or (for a proc) its CHANCE. Weights are relative; empty = no scaling (×1).")]
    public List<StatWeight> affinity = new List<StatWeight>();
    [Tooltip("Scales this effect's DURATION (its seconds) by ring investment. Empty = duration unscaled (×1).")]
    public List<StatWeight> durationAffinity = new List<StatWeight>();
    [Tooltip("Damage kind only: scales the number of STRIKES (hitCount) by ring investment — more investment = more " +
             "hits in the flurry. hits = round(hitCount × mult). Empty = hit count unscaled (×1).")]
    public List<StatWeight> hitCountAffinity = new List<StatWeight>();
}
