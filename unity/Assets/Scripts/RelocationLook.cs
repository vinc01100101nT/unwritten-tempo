using UnityEngine;

/// <summary>
/// WHICH AUTHORED EFFECT moved a body — a skill, the binding and the effect inside it, and the pile readings its cast
/// resolved against. It carries no picture of its own: every screen that draws the move reads the afterimage and the
/// see-through fade out of ITS OWN copy of that skill, so the server never decides or sends anything about how a move
/// looks. <see cref="None"/> for a move no effect authored (a shove, a correction, a GM command).
/// </summary>
public struct RelocationLook
{
    public string skillId;
    public byte binding;
    public byte effect;
    // The cast's pile readings, so a look authored to scale with stacks resolves to the same number on every screen.
    public int stacks;
    public float carried;
    public int givenUp;

    public static readonly RelocationLook None = default;

    public bool Has => !string.IsNullOrEmpty(skillId);

    /// <summary>The reference to <paramref name="e"/> inside skill <paramref name="skillId"/>, found by identity in
    /// the skill's own bindings. <see cref="None"/> when the effect is not one of that skill's — nothing is drawn
    /// rather than the wrong effect's look.</summary>
    public static RelocationLook Of(string skillId, SkillEffect e, in StackVars vars)
    {
        var def = string.IsNullOrEmpty(skillId) || e == null ? null : SkillCatalog.Get(skillId);
        if (def == null || def.bindings == null) return None;
        for (int b = 0; b < def.bindings.Count && b <= byte.MaxValue; b++)
        {
            var effects = def.bindings[b] != null ? def.bindings[b].effects : null;
            if (effects == null) continue;
            int i = effects.IndexOf(e);
            if (i < 0 || i > byte.MaxValue) continue;
            return new RelocationLook
            {
                skillId = skillId, binding = (byte)b, effect = (byte)i,
                stacks = vars.Count, carried = vars.Carried, givenUp = vars.GivenUp,
            };
        }
        return None;
    }

    /// <summary>Read this move's look off this machine's copy of the skill: how long its afterimage fades, in what
    /// colour, and how see-through the mover is while it travels (1 = solid; only a Dash fades its mover). FALSE when
    /// there is no look, or the skill or effect is not here.</summary>
    public bool TryRead(out float afterimageSeconds, out Color afterimageTint, out float opacity)
    {
        afterimageSeconds = 0f; afterimageTint = default; opacity = 1f;
        if (!Has) return false;
        var def = SkillCatalog.Get(skillId);
        if (def == null || def.bindings == null || binding >= def.bindings.Count || def.bindings[binding] == null) return false;
        var effects = def.bindings[binding].effects;
        if (effects == null || effect >= effects.Count || effects[effect] == null) return false;

        var e = effects[effect];
        var vars = new StackVars(stacks, carried).WithGivenUp(givenUp);
        afterimageSeconds = e.afterimageSeconds.Resolve(vars);
        afterimageTint = e.afterimageTint;
        if (e.kind == SkillEffectKind.Dash) opacity = Mathf.Clamp01(e.casterOpacity.Resolve(vars));
        return true;
    }
}
