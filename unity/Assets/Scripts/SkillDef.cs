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
    [Range(1, 20)] public int maxLevel = 10;
    [Tooltip("How the skill picks where it lands.")]
    public SkillAim aim = SkillAim.Ground;
    [Tooltip("Ground/Target aim: max distance from the caster the impact point can be.")]
    public float castRange = 11f;

    [Header("Presentation (referenced, never copied)")]
    [Tooltip("The composed look from the Vfx Composer. Its exposed ticks are what the bindings below map to.")]
    public VfxSequence vfx;

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
}
