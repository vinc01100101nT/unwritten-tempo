using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// THE NUMBERS A SKILL LEVEL MOVES — for one skill, every number the fight raises, worked out for one caster at a
/// given lift. The hover card lists them "now" and "at the next level"; the server works both out here and sends the
/// finished values (CLAUDE.md, RULE NOCLIENTMATH).
///
/// <para>Which numbers are raised is <see cref="EffectScaling.Boostable"/> — the same test the fight asks — and
/// what each raised number IS follows the applier: a strike's (and an execute's) damage multiplier, a heal's or a
/// shield's amount, the magnitude of an ailment's damage, a sub-stat change and an outgoing-damage charge, and the
/// charges added to another skill. Each is multiplied by the fight's own term (<see cref="SkillEffectApplier.OutputScale"/>:
/// the caster's ring investment plus the lift), so the card shows what an ordinary cast lands. The one thing it
/// leaves out is the rare EFFICIENT cast (<see cref="SkillEfficiency"/>), which is a roll, not a level.</para>
/// </summary>
public static class SkillLevelNumbers
{
    /// <summary>Append to <paramref name="into"/> one number per raised effect of <paramref name="def"/>, in the
    /// order its bindings and effects are authored, as <paramref name="caster"/> would land it with
    /// <paramref name="lift"/> (its skill level's and its weapon trademark's lift, added together as the fight adds them).</summary>
    public static void Collect(SkillDef def, ICombatUnit caster, float lift, List<float> into)
    {
        if (def == null || def.bindings == null || into == null) return;
        for (int b = 0; b < def.bindings.Count; b++)
        {
            var binding = def.bindings[b];
            if (binding == null || binding.effects == null) continue;
            for (int k = 0; k < binding.effects.Count; k++)
            {
                var e = binding.effects[k];
                if (MainNumber(e, out var number, out bool whole))
                    into.Add(Scaled(number, whole, SkillEffectApplier.OutputScale(e, caster, lift)));
            }
        }
    }

    /// <summary>The authored number the fight raises on <paramref name="e"/>, and whether the fight uses it as a whole
    /// number; false when the effect has no raised number.</summary>
    public static bool MainNumber(SkillEffect e, out ScaledNumber number, out bool whole)
    {
        number = default;
        whole = false;
        if (!EffectScaling.Boostable(e)) return false;
        switch (e.kind)
        {
            case SkillEffectKind.Damage:
            case SkillEffectKind.Execute:
                number = e.damageMult;
                return true;
            case SkillEffectKind.Heal:
            case SkillEffectKind.Shield:
                number = e.amount;
                whole = true;
                return true;
            case SkillEffectKind.AddStacks:
                number = e.modifierStacks;
                whole = true;
                return true;
            default:
                number = e.magnitude;
                return true;
        }
    }

    // A whole number that reads no pile is rounded the way the fight rounds it (ScaledNumber.ResolveInt), so a charge
    // count shows the jump it really makes rather than a fraction nobody gets. One that reads a pile is a RATE on that
    // pile, so it stays a fraction — the fight rounds only the finished amount.
    static float Scaled(ScaledNumber n, bool whole, float scale)
    {
        float v = n.value * scale;
        return whole && !n.useStacks ? Mathf.Max(0, Mathf.RoundToInt(v)) : v;
    }
}
