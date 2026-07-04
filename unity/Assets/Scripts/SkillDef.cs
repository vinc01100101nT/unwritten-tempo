using System.Collections.Generic;
using UnityEngine;

/// <summary>How a skill picks WHERE it plays — decides how the <see cref="SkillRunner"/> builds the
/// <see cref="VfxContext"/> before playing the composed VFX.</summary>
public enum SkillAim
{
    /// <summary>Cast on the caster (self buffs, nova-from-me). origin == target == the player.</summary>
    Self,
    /// <summary>Cast on the current locked/nearest mob. origin = player, target = that mob (also the Single-scope unit).</summary>
    Target,
    /// <summary>Ground-targeted: a reticle follows the cursor (clamped to <see cref="castRange"/>); confirm drops it.</summary>
    Ground,
}

/// <summary>Per-level growth for one binding. Kept to a couple of linear knobs for now (swap to a curve later if
/// non-linear scaling is wanted). Level 1 = the authored values; each level over 1 adds these.</summary>
[System.Serializable]
public class SkillLevelScale
{
    [Tooltip("Added to every Damage effect's × multiplier per level over 1 (e.g. 0.2 → +0.2× per level).")]
    public float damageMultPerLevel = 0.2f;
    [Tooltip("Flat damage added per level over 1 (before element/DEF). 0 = pure multiplier scaling.")]
    public int flatBonusPerLevel = 0;
}

/// <summary>Binds ONE exposed <see cref="VfxStep.tickId"/> to the gameplay it should do. The tickId matches a
/// step in the skill's <see cref="SkillDef.vfx"/>; every time that component ticks, the runner applies these
/// effects to the targets in <see cref="radius"/> at the tick point.</summary>
[System.Serializable]
public class SkillTickBinding
{
    [Tooltip("MUST match a VfxStep.tickId in the referenced VfxSequence (the Skill Composer lists them for you).")]
    public string tickId;
    [Tooltip("Who this tick hits.")]
    public SkillTargets targets = SkillTargets.Mobs;
    [Tooltip("Area = everyone in the radius. Single = only the cast's designated target (always lands, radius ignored).")]
    public SkillScope scope = SkillScope.Area;
    [Tooltip("AoE radius (world units) centred on the tick point. Ignored in Single scope.")]
    public float radius = 1.5f;
    [Tooltip("What each tick does — applied through the shared SkillEffectApplier (the one damage path).")]
    public List<SkillEffect> effects = new List<SkillEffect>();
    [Tooltip("Per-level growth applied to this binding's damage effects.")]
    public SkillLevelScale scale = new SkillLevelScale();
}

/// <summary>
/// A data-authored SKILL — the output of the Skill Composer. It references a composed <see cref="VfxSequence"/>
/// (the look, authored in the Vfx Composer) and binds each of its exposed <b>ticks</b> to gameplay
/// (<see cref="SkillTickBinding"/>: damage/heal/status, element, multiplier, radius). The FINAL numbers are still
/// owned by code — the runner rolls them off the live caster stats through <see cref="SkillEffectApplier"/> — so
/// this asset only stores the design intent, never a baked damage value.
///
/// <para>Create via <c>Assets ▸ Create ▸ unwritten ▸ Skill</c> or the <b>Skill Composer</b> window; drop it under
/// <c>Resources/Skills/</c> so the game can load it by <see cref="id"/> via <see cref="SkillCatalog"/>.</para>
/// </summary>
[CreateAssetMenu(menuName = "unwritten/Skill", fileName = "Skill")]
public class SkillDef : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Stable kebab id, e.g. \"skill.meteor\". Keyed by SkillCatalog; don't rename once shipped.")]
    public string id;
    public string displayName = "New Skill";
    [Tooltip("Hotbar icon (Skill Icon pack). Null → the bar falls back to the short label.")]
    public Sprite icon;

    [Header("Cost / cadence")]
    public int manaCost = 10;
    public float cooldown = 5f;
    [Tooltip("Base cast wind-up in seconds BEFORE the skill fires (the cast bar fills over this). 0 = instant. " +
             "The caster's DEX shortens it at runtime via Character.CastTimeMult (instacast at 150 DEX).")]
    [Min(0f)] public float castTime = 0f;
    [Range(1, 20)] public int maxLevel = 10;
    [Tooltip("How the skill picks where it lands.")]
    public SkillAim aim = SkillAim.Ground;
    [Tooltip("Ground/Target aim: max distance from the caster the impact point can be.")]
    public float castRange = 11f;

    [Header("Cast circle (shown ONLY during the wind-up, needs castTime > 0)")]
    [Tooltip("EffectLibrary id of the ground casting circle shown WHILE this skill winds up. Empty = no per-skill " +
             "circle (the global OnCastStart cue plays instead). It's a wind-up visual, separate from the release " +
             "vfx below. Author a LOOPING circle prefab; add packs via Art ▸ Rebuild Effect Library.")]
    public string castCircleId = "";
    [Tooltip("Tint for the casting circle (white = the prefab's own colours). Recolour per element without a new prefab.")]
    public Color castCircleTint = Color.white;
    [Tooltip("Calibration on the AUTO radius: the circle sizes to the skill's largest AoE radius (LargestAreaRadius) × " +
             "this. 1 = exact for a 1-unit-radius prefab; tweak until the ring matches the ground reticle. Radius itself " +
             "is NOT stored here — it's read live from the bindings (single source of truth).")]
    [Min(0.01f)] public float castCircleScaleMult = 1f;

    [Header("Presentation (referenced, never copied)")]
    [Tooltip("The composed look from the Vfx Composer. Its exposed ticks are what the bindings below map to.")]
    public VfxSequence vfx;

    [Tooltip("How many times to replay the WHOLE composed cycle per cast (e.g. 5 = a 5-meteor shower). Each cycle " +
             "is an independent play at the same cast point — its own carrier falls and multi-hits via the VFX's " +
             "per-component tickCount. 1 = play once. Repetition of the whole effect lives HERE (the gameplay layer), " +
             "not in the Vfx Composer — the composer stays WYSIWYG (one cycle = one meteor).")]
    [Min(1)] public int repeatCount = 1;
    [Tooltip("Seconds between the start of each cycle when repeatCount > 1. 0 = fall back to the VFX sequence's " +
             "RestartInterval (its loop-end marker). Cycles overlap freely; a still-playing cycle keeps rendering.")]
    [Min(0f)] public float repeatInterval = 0f;

    [Header("Skill type (release animation)")]
    [Tooltip("Play a skill-release stance when this skill FIRES (after any cast wind-up). false = no release pose.")]
    public bool hasReleaseStance = false;
    [Tooltip("Which release cue to play — the caster's chosen 'skill type'. Only the SkillReleaseStances subset is " +
             "valid; assign each cue a clip in the Stance Composer. Ignored when hasReleaseStance is false.")]
    public StanceId releaseStance = StanceId.AoeSpell1;

    [Header("Gameplay — one binding per tickId you want to matter")]
    public List<SkillTickBinding> bindings = new List<SkillTickBinding>();

    [Header("Hotbar")]
    public string shortLabel = "Skill";
    public string keyLabel = "";
    public int slotOrder = 10;
    public Color tint = Color.white;

    /// <summary>The binding for <paramref name="tickId"/>, or null if this tick is purely visual (unbound).</summary>
    public SkillTickBinding BindingFor(string tickId)
    {
        if (string.IsNullOrEmpty(tickId) || bindings == null) return null;
        for (int i = 0; i < bindings.Count; i++)
            if (bindings[i] != null && bindings[i].tickId == tickId) return bindings[i];
        return null;
    }

    /// <summary>The largest Area-scope binding radius — the skill's real ground footprint. The SINGLE OWNER of that
    /// value: the ground reticle (<see cref="SkillRunner.PreviewRadius"/>) and the cast circle both READ it, so a
    /// radius change in one place can never drift from the visual. 1.5 fallback when no Area binding exists.</summary>
    public float LargestAreaRadius
    {
        get
        {
            float r = 0f;
            if (bindings != null)
                for (int i = 0; i < bindings.Count; i++)
                    if (bindings[i] != null && bindings[i].scope == SkillScope.Area) r = Mathf.Max(r, bindings[i].radius);
            return r > 0f ? r : 1.5f;
        }
    }

    /// <summary>True if ANY binding is Single scope — the skill must land on ONE chosen unit, so it can't be
    /// ground-cast (a ground point isn't a valid target). Drives the single-target aim path in <see cref="SkillRunner"/>.</summary>
    public bool IsSingleTarget
    {
        get
        {
            if (bindings == null) return false;
            for (int i = 0; i < bindings.Count; i++)
                if (bindings[i] != null && bindings[i].scope == SkillScope.Single) return true;
            return false;
        }
    }

    /// <summary>The union of every Single-scope binding's <see cref="SkillTickBinding.targets"/> — the set of unit
    /// KINDS a single-target cast may legally land on. The reticle validates the hovered unit against this; the
    /// bindings are the single source of truth for who a skill can hit (no separate "target type" field).</summary>
    public SkillTargets SingleTargetMask
    {
        get
        {
            SkillTargets mask = SkillTargets.None;
            if (bindings != null)
                for (int i = 0; i < bindings.Count; i++)
                    if (bindings[i] != null && bindings[i].scope == SkillScope.Single) mask |= bindings[i].targets;
            return mask;
        }
    }
}
