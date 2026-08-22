using System.Text;
using UnityEngine;

/// <summary>
/// Builds the rich-text body of a skill's hover card from its <see cref="SkillDef"/> — title, tag/category/tier chips,
/// cost line, description, and a human-readable line per effect with its VARIABLE NUMBERS highlighted. Pure function
/// (no UI dependency), so the hotbar, grimoire, or any hover can share one description. Numbers are estimated off the
/// live caster stats (<see cref="Character.Instance"/>) when available, else shown as the raw multiplier.
/// </summary>
public static class SkillTooltip
{
    const string Num = "#FFD27F";   // highlighted variable numbers (warm gold)
    const string Desc = "#B4B4BE";  // description grey
    const string Chip = "#C8C8D2";  // category text

    static readonly StringBuilder sb = new StringBuilder(512);

    static string Hl(string s) => "<color=" + Num + "><b>" + s + "</b></color>";
    static string Pct(float f01) => Hl(Mathf.RoundToInt(f01 * 100f) + "%");

    public static string ArchetypeName(SkillArchetype a)
    {
        switch (a)
        {
            case SkillArchetype.Burst:    return "Burst";
            case SkillArchetype.Tempo:    return "Tempo";
            case SkillArchetype.Fortress: return "Fortress";
            case SkillArchetype.Buff:     return "Buff";
            case SkillArchetype.Curse:    return "Curse";
            default:                      return a.ToString();
        }
    }

    public static Color ArchetypeColor(SkillArchetype a)
    {
        switch (a)
        {
            case SkillArchetype.Burst:    return new Color(0.95f, 0.48f, 0.29f);
            case SkillArchetype.Tempo:    return new Color(0.95f, 0.82f, 0.29f);
            case SkillArchetype.Fortress: return new Color(0.56f, 0.70f, 1f);
            case SkillArchetype.Buff:     return new Color(0.37f, 0.88f, 0.55f);
            case SkillArchetype.Curse:    return new Color(0.70f, 0.48f, 0.85f);
            default:                      return Color.white;
        }
    }

    static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

    // Damage-resolution word for the card (Physical / Magic / Crit — the RPS-triangle axis).
    static string DmgWord(DamageType t) => t == DamageType.Magic ? "magic" : t == DamageType.Crit ? "crit" : "physical";

    /// <summary>The short "…and it also answers to the pile" note for ONE number. Every number that can pull says it
    /// the same way, so a reader learns the phrasing once instead of per effect kind.</summary>
    static string StackNote(ScaledNumber n)
    {
        if (!n.useStacks) return "";
        string op = n.op == StackOp.Subtract ? "−" : n.op == StackOp.Multiply ? "×" : n.op == StackOp.Divide ? "÷" : "+";
        string what = n.read == StackReading.CarriedTotal ? "banked total" : "stacks";
        // The scaler is part of what the pile is WORTH, so it is said with it — "+ 2.5 per stack" rather than a bare
        // "+ stacks" that hides the number doing the work. A scaler of 1 adds nothing to read.
        if (Mathf.Abs(n.scaler - 1f) > 0.0001f) what = n.scaler.ToString("0.####") + " × " + what;
        return " " + Hl(op + " " + what);
    }

    /// <summary>Plain-language "what makes this run", read off the skill's authored triggers. Shared by every surface
    /// that lists them (the Grimoire rows, the weapon skill-details panel) so they can never word it differently.
    ///
    /// <para>A skill can carry several triggers, so this joins every NON-OnCast one. OnCast is skipped: "you press it"
    /// is what a hotbar slot already says, so listing it would only be noise on a skill that has both.</para></summary>
    public static string TriggerLine(SkillDef def)
    {
        if (def == null || def.triggers == null) return "";
        string line = "";
        for (int i = 0; i < def.triggers.Count; i++)
        {
            var e = def.triggers[i];
            if (e == null || e.trigger == SkillTrigger.OnCast) continue;
            if (line.Length > 0) line += ", and ";
            line += MomentLine(e);
        }
        return line.Length > 0 ? line : (def.IsPassive ? "Passive" : "");
    }

    static string MomentLine(SkillTriggerEntry e)
    {
        switch (e.trigger)
        {
            case SkillTrigger.OnSkillCastComplete: return "After casting " + NameOf(e.triggerSkillId);
            case SkillTrigger.OnBasicAttackStart:  return "When you start a basic attack";
            case SkillTrigger.OnBasicAttackDealt:  return "When a basic attack lands";
            case SkillTrigger.OnKill:              return "When you kill a mob";   // the "killed by" filter is named by the caller's line
            case SkillTrigger.OnLowHP:             return "When your HP drops low";
            case SkillTrigger.OnDamageDealt:       return "When you deal damage";
            case SkillTrigger.OnParry:             return "When you parry";
            case SkillTrigger.OnDamageReceived:    return "When you take damage";
            default:                               return "Passive";
        }
    }

    static string NameOf(string skillId)
    {
        var d = SkillCatalog.Get(skillId);
        return d != null ? d.displayName : (string.IsNullOrEmpty(skillId) ? "(nothing)" : skillId);
    }

    /// <summary>The full rich-text card body for <paramref name="def"/> (title through effects).</summary>
    public static string Build(SkillDef def)
    {
        if (def == null) return "";
        sb.Length = 0;

        // Title + tier.
        sb.Append("<size=120%><b>").Append(def.displayName).Append("</b></size>");
        if (def.IsUltimate) sb.Append("  <color=#FF8CE0><b>ULTIMATE</b></color>");
        sb.Append('\n');

        // Tag (archetype) + category chips.
        sb.Append("<color=").Append(Hex(ArchetypeColor(def.archetype))).Append("><b>")
          .Append(ArchetypeName(def.archetype)).Append("</b></color>")
          .Append("  <color=").Append(Chip).Append('>').Append(def.family).Append("</color>\n");

        // Cost / cadence line.
        sb.Append("<color=").Append(Chip).Append(">MP </color>").Append(Hl(def.manaCost.ToString()));
        sb.Append("  <color=").Append(Chip).Append(">CD </color>").Append(Hl(def.cooldown.ToString("0.#") + "s"));
        if (def.castTime > 0f) sb.Append("  <color=").Append(Chip).Append(">Cast </color>").Append(Hl(def.castTime.ToString("0.#") + "s"));
        else                   sb.Append("  <color=").Append(Chip).Append(">Instant</color>");
        if (def.Reticle != CastReticle.Instant) sb.Append("  <color=").Append(Chip).Append(">Range </color>").Append(Hl(def.castRange.ToString("0.#")));
        sb.Append('\n');

        // Description.
        if (!string.IsNullOrEmpty(def.description))
            sb.Append("<color=").Append(Desc).Append("><i>").Append(def.description).Append("</i></color>\n");

        // Effects — one line each, numbers highlighted.
        if (def.bindings != null)
            for (int i = 0; i < def.bindings.Count; i++)
            {
                var b = def.bindings[i];
                if (b == null || b.effects == null) continue;
                for (int k = 0; k < b.effects.Count; k++)
                {
                    string line = DescribeEffect(b.effects[k]);
                    if (!string.IsNullOrEmpty(line)) sb.Append("\n• ").Append(line);
                }
            }

        return sb.ToString();
    }

    /// <summary>One human-readable line for a single effect ("Reduces damage taken 30% for 6s", "+20% move speed
    /// for 5s"), with its variable numbers highlighted. Shared by the skill card AND the buff-bar hover card.</summary>
    public static string DescribeEffect(SkillEffect e)
    {
        if (e == null) return null;
        string line = DescribeEffectBody(e);
        // The odds are said ONCE, here, for every kind that has them — so a new effect never has to remember to
        // mention its own chance. A certain effect (the default) says nothing, because "100%" is just noise.
        if (line == null || !SkillEffect.RollsChance(e.kind) || e.chance.value >= 1f) return line;
        if (e.kind == SkillEffectKind.Status || e.kind == SkillEffectKind.Execute) return line;   // these word it themselves
        return Pct(Mathf.Clamp01(e.chance.value)) + " per hit: " + char.ToLower(line[0]) + line.Substring(1);
    }

    static string DescribeEffectBody(SkillEffect e)
    {

        switch (e.kind)
        {
            case SkillEffectKind.Damage:
            {
                var ch = Character.Instance;
                float mult = e.damageMult.value <= 0f ? 1f : e.damageMult.value;
                string s = "Deals " + Hl(mult.ToString("0.##") + "×") + " " + DmgWord(e.damageType) + " damage";
                if (ch != null)
                {
                    // Mirror the runtime roll: base attack × mult × the damage-affinity scaling (ring investment).
                    float aff = SkillEffectApplier.AffinityMult(e.affinity);
                    int est = Mathf.RoundToInt((e.damageType == DamageType.Magic ? ch.MaxMagicAttack : ch.MaxAttack) * mult * aff);
                    if (est > 0) s += " (~" + Hl(est.ToString()) + ")";
                }
                int hits = SkillEffectApplier.EffectiveHits(e);   // affinity-scaled, matches what actually lands
                if (hits > 1) s += ", " + Hl(hits + "×") + " hits";
                // Any number may answer to the pile now, so the note belongs to the NUMBER, said the same way
                // wherever it appears rather than spelled out per kind.
                s += StackNote(e.damageMult);
                return s;
            }
            case SkillEffectKind.Status:
            {
                string body;
                switch (e.ailment)
                {
                    case Ailment.Slow:  body = "slow " + Pct(1f - Mathf.Clamp01(e.magnitude.value)) + " for " + Hl(e.seconds.value.ToString("0.#") + "s"); break;
                    case Ailment.Stun:  body = "stun for " + Hl(e.seconds.value.ToString("0.#") + "s"); break;
                    case Ailment.Poison:
                    case Ailment.Bleed:
                    case Ailment.Burn:  body = e.ailment + " " + Hl(e.magnitude.value.ToString("0.#")) + "/tick for " + Hl(e.seconds.value.ToString("0.#") + "s"); break;
                    default:            body = e.ailment + " for " + Hl(e.seconds.value.ToString("0.#") + "s"); break;
                }
                // Below certain it reads as the gamble it is; certain (the default) just states what happens.
                return e.chance.value < 1f ? Pct(e.chance.value) + " per hit: " + body : char.ToUpper(body[0]) + body.Substring(1);
            }
            case SkillEffectKind.Execute:
                return (e.chance.value < 1f ? Pct(e.chance.value) + " per hit: " : "")
                     + "execute — bonus " + DmgWord(e.damageType) + " damage scaling with the target's missing HP (×" + Hl(e.magnitude.value.ToString("0.##")) + ")";
            case SkillEffectKind.Heal:          return "Heals " + Hl(e.amount.value.ToString()) + " HP";
            case SkillEffectKind.Guard:         return "Reduces damage taken " + Pct(Mathf.Clamp01(e.magnitude.value)) + " for " + Hl(e.seconds.value.ToString("0.#") + "s");
            case SkillEffectKind.Taunt:         return "Taunts nearby enemies for " + Hl(e.seconds.value.ToString("0.#") + "s");
            case SkillEffectKind.Teleport:      return "Blinks to the target location";
            case SkillEffectKind.Dash:          return "Dashes to the target location";
            // Magnitude is the amount itself; on a stacking one it is the PER-STACK amount, so say so.
            case SkillEffectKind.SubStatBuff:   return SubStatLine(e, worse: false);
            case SkillEffectKind.SubStatDebuff: return SubStatLine(e, worse: true);
            case SkillEffectKind.Cleanse:       return "Removes " + Hl(Mathf.Max(1, e.amount.value).ToString()) + " debuff(s)";
            case SkillEffectKind.Vulnerability: return Subject(e, "You take", "Enemies take") + " +" + Pct(e.magnitude.value) + " damage for " + Hl(e.seconds.value.ToString("0.#") + "s");
            case SkillEffectKind.Knockback:     return "Knocks enemies back";
            case SkillEffectKind.Pull:          return "Pulls enemies in";
            case SkillEffectKind.Shield:        return "Absorbs " + Hl(e.amount.value.ToString()) + " damage for " + Hl(e.seconds.value.ToString("0.#") + "s");
            case SkillEffectKind.BoostOutgoingDamage:
            {
                // Charges ARE stacks, read from the same knob the runtime uses — never a second number to keep in sync.
                int charges = e.stacking ? Mathf.Max(1, Mathf.RoundToInt(e.stacksPerApply.value)) : 1;
                string window = " within " + Hl(e.seconds.value.ToString("0.#") + "s");
                string amount = "+" + Pct(e.magnitude.value) + " damage";
                return charges > 1
                    ? "Your next " + Hl(charges.ToString()) + " attacks deal " + amount + window
                    : "Your next attack deals " + amount + window;
            }
            case SkillEffectKind.ImmuneToDisables:
                return "Nothing stops you for " + Hl(e.seconds.value.ToString("0.#") + "s") +
                       " — no flinch, stun, freeze, silence, knockback or pull, and hits can't cancel your casts";
            case SkillEffectKind.ReduceOutgoingDamage:
            {
                // Same shape as the boost's line — charges read off the one knob the runtime uses — but the subject is
                // whoever the effect lands on, since this one is authored on either side.
                int charges = e.stacking ? Mathf.Max(1, Mathf.RoundToInt(e.stacksPerApply.value)) : 1;
                string window = " within " + Hl(e.seconds.value.ToString("0.#") + "s");
                string amount = "−" + Pct(Mathf.Clamp01(e.magnitude.value)) + " damage";
                string who = Subject(e, "Your", "Their");
                return charges > 1
                    ? who + " next " + Hl(charges.ToString()) + " attacks deal " + amount + window
                    : who + " next attack deals " + amount + window;
            }
            case SkillEffectKind.Parry:
            {
                // Charges ARE stacks (the store's rule), so the count the player reads comes from the same knob the
                // runtime hands to Apply — never a second number to keep in sync.
                int charges = e.stacking ? Mathf.Max(1, Mathf.RoundToInt(e.stacksPerApply.value)) : 1;
                string window = " within " + Hl(e.seconds.value.ToString("0.#") + "s");
                return charges > 1
                    ? "Parries the next " + Hl(charges.ToString()) + " hits" + window + " — no damage taken"
                    : "Parries the next hit" + window + " — no damage taken";
            }
            case SkillEffectKind.Brace:
            {
                // Charges ARE stacks — the same read Parry uses, off the same knob, so the two can't word it differently.
                int held = e.stacking ? Mathf.Max(1, Mathf.RoundToInt(e.stacksPerApply.value)) : 1;
                return (held > 1 ? "Delays the next " + Hl(held.ToString()) + " hits" : "Delays the next hit")
                     + " within " + Hl(e.seconds.value.ToString("0.#") + "s")
                     + " — the delayed damage all lands at once when the window ends";
            }
            // Named by the skill they edit, not by the pile's internal name: the player knows "Blade Dance", never
            // "DamageBuff". A modifier with no skill picked describes nothing, so it says nothing.
            case SkillEffectKind.ExtendDuration:
            {
                var of = SkillCatalog.Get(e.modifierSkillId);
                return of == null ? null
                     : "Extends " + Hl(of.displayName) + " by " + Hl(e.modifierSeconds.value.ToString("0.#") + "s");
            }
            case SkillEffectKind.AddStacks:
            {
                var of = SkillCatalog.Get(e.modifierSkillId);
                return of == null ? null
                     : "Adds " + Hl(Mathf.Max(1, e.modifierStacks.value).ToString()) + " to " + Hl(of.displayName);
            }
            default:                            return null;
        }
    }

    // WHO a line is about comes from the same predicate the runtime partitions on (SkillEffect.LandsOnCaster), never
    // from an assumption baked into the kind: a Vulnerability authored on the caster is a self-drawback (Overclock's
    // fragile window), not a debuff on enemies, and saying "enemies" there tells the player the opposite of the truth.
    static string Subject(SkillEffect e, string caster, string target) => SkillEffect.LandsOnCaster(e) ? caster : target;

    /// <summary>The sub-stat's name in ordinary words. Every stat has a row, so a new one cannot silently fall
    /// through to a run-together enum name on the player's screen.</summary>
    static string SubStatName(SubStat s)
    {
        switch (s)
        {
            case SubStat.Damage:        return "damage";
            case SubStat.AttackSpeed:   return "attack speed";
            case SubStat.CastSpeed:     return "cast speed";
            case SubStat.MovementSpeed: return "movement speed";
            case SubStat.HPRegen:       return "HP regen";
            case SubStat.MPRegen:       return "MP regen";
            case SubStat.ManaLeech:     return "mana leech";
            case SubStat.Reflect:       return "reflect";
            case SubStat.LifeSteal:     return "life steal";
            case SubStat.Attack:        return "attack";
            case SubStat.MagicAttack:   return "magic attack";
            case SubStat.Defense:       return "defense";
            case SubStat.MagicDefense:  return "magic defense";
            case SubStat.Accuracy:      return "accuracy";
            case SubStat.Evasion:       return "evasion";
            case SubStat.CritChance:    return "crit chance";
            case SubStat.CritDamage:    return "crit damage";
            case SubStat.MaxHP:         return "max HP";
            default:                    return "max MP";
        }
    }

    /// <summary>One line for a sub-stat buff or debuff: which way, how much, which stat, how long. The amount reads as
    /// a percentage or as a plain number depending on what the stat's number MEANS, so a regen never claims to be
    /// "+6%" of a trickle that is usually zero.</summary>
    static string SubStatLine(SkillEffect e, bool worse)
    {
        float v = Mathf.Max(0f, e.magnitude.value);
        string amount = SubStats.IsPercent(e.subStat) ? Pct(v) : v.ToString("0.##");
        return (worse ? "−" : "+") + amount + " " + SubStatName(e.subStat)
             + (e.stacking ? " per stack" : "")
             + " for " + Hl(e.seconds.value.ToString("0.#") + "s");
    }
}
