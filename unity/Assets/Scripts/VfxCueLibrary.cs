using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The vocabulary of GAMEPLAY EVENTS that can trigger a visual — the "verbs" and status lifecycle beats. This is
/// the reverse direction of a <see cref="SkillDef"/>: a skill's VFX drives gameplay (ticks → damage), whereas a
/// cue is gameplay driving VFX (the event already happened in code; we just play a look at the actor).
///
/// APPEND-ONLY (like <see cref="Ailment"/> / Biome): the int index is stored in the <see cref="VfxCueLibrary"/>
/// asset, so add new ids at the END and never reorder.
/// </summary>
public enum VfxCueId
{
    // --- Active Verbs (attacker-side; NOT built yet) ---
    // OnAttack fires on the ACTOR when a swing starts. Currently DORMANT — its call site is commented out; it's
    // reserved for future per-weapon swing vfx/sfx. Kept here at index 0 so every stored cue index below stays put
    // (the int is serialized in VfxCueLibrary.asset; this enum is APPEND-ONLY / never-reorder). When Active Verbs
    // are built, this is their first member.
    OnAttack,

    // --- Passive Verbs (receiver-side, one-shot) ---
    // Every cue below fires on the UNIT THAT RECEIVES the event, with no attacker and no direction (Origin ==
    // Target == receiver), exactly like OnReceiveHeal. That receiver-only rule is the whole point of the "passive"
    // grouping — it keeps the mental model simple: these are things that happen TO a unit.
    OnHitByNormalAttack,   // took a basic (non-crit) auto-attack hit
    OnHitByCriticalAttack, // took a critical auto-attack hit
    OnReceiveHeal,         // HP restored (potion, heal skill, regen)
    OnLevelUp,             // gained a level
    OnDeath,               // died

    // --- status lifecycle ---
    // *Start = a PERSISTENT aura begun on apply and stopped on expiry (author as an Attach/looping sequence).
    // *Tick  = a one-shot puff each damage-over-time tick.
    OnPoisonStart, OnPoisonTick,
    OnBleedStart,  OnBleedTick,
    OnBurnStart,   OnBurnTick,

    // control / debuff applied (one-shot on apply)
    OnStun, OnFreeze, OnSlow, OnHaste, OnSilence, OnBlind,

    // --- skill lifecycle ---
    OnCastStart,  // a skill begins casting (fired once at the caster the instant a cast is confirmed)

    // Passive Verb (receiver-side): fired at the VICTIM when any damage-dealing skill lands on it (physical skill
    // strike or magic — NOT a basic auto-attack, which is OnHitByNormalAttack / OnHitByCriticalAttack). Grouped
    // with the Passive Verbs in the composer UI, but the enum is append-only so the int index lives at the end.
    // See Health's skill-damage choke point.
    OnHitBySkill,

    // Active Verb (actor-side, one-shot): fired ONCE on the player the moment V-sprint engages. Grouped under
    // "Movement" in the composer UI; appended here to keep the enum APPEND-ONLY (the int index is serialized).
    OnSprint,
}

/// <summary>Binds ONE <see cref="VfxCueId"/> to the composed look it should play — the status/verb analog of a
/// <see cref="SkillTickBinding"/>, but with no gameplay (the event already fired). Authored in the Vfx Cue
/// Composer. A cue with a null <see cref="vfx"/> is a silent no-op.</summary>
[System.Serializable]
public class VfxCueBinding
{
    public VfxCueId id;
    [Tooltip("The composed look from the Vfx Composer. *Start cues should be an Attach/looping sequence so the " +
             "aura lives for the ailment's duration; everything else is one-shot.")]
    public VfxSequence vfx;
    [Tooltip("World-space nudge applied to both reference points (one-shot cues only; attached auras use the sequence's own step offset).")]
    public Vector3 offset;
    [Tooltip("Uniform scale multiplier on the whole sequence (1 = authored).")]
    public float scale = 1f;
}

/// <summary>
/// The single asset that maps every gameplay <see cref="VfxCueId"/> to a composed <see cref="VfxSequence"/>. The
/// data sibling of <see cref="EffectLibrary"/> — resolved once at runtime by <see cref="VfxCueCatalog"/> and edited
/// in the <b>Vfx Cue Composer</b> window. Lives at <c>Resources/VfxCueLibrary.asset</c> so the game loads it by name.
/// </summary>
[CreateAssetMenu(menuName = "unwritten/Vfx Cue Library", fileName = "VfxCueLibrary")]
public class VfxCueLibrary : ScriptableObject
{
    public List<VfxCueBinding> cues = new List<VfxCueBinding>();
}
