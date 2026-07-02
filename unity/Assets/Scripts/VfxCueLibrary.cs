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
    // --- verbs (one-shot) ---
    OnAttack,     // a swing starts
    OnHit,        // a basic hit lands
    OnCrit,       // a basic hit lands as a critical
    OnHeal,       // HP restored (potion, heal skill, regen)
    OnLevelUp,    // character gained a level
    OnDeath,      // the unit died

    // --- status lifecycle ---
    // *Start = a PERSISTENT aura begun on apply and stopped on expiry (author as an Attach/looping sequence).
    // *Tick  = a one-shot puff each damage-over-time tick.
    OnPoisonStart, OnPoisonTick,
    OnBleedStart,  OnBleedTick,
    OnBurnStart,   OnBurnTick,

    // control / debuff applied (one-shot on apply)
    OnStun, OnFreeze, OnSlow, OnHaste, OnSilence, OnBlind,
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
