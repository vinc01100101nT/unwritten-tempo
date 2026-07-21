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
        if (def.IsUltimate) sb.Append("  <color=").Append(Chip).Append(">Charge </color>").Append(Hl(def.chargeCost.ToString("0")));
        else                sb.Append("  <color=").Append(Chip).Append(">CD </color>").Append(Hl(def.cooldown.ToString("0.#") + "s"));
        if (def.castTime > 0f) sb.Append("  <color=").Append(Chip).Append(">Cast </color>").Append(Hl(def.castTime.ToString("0.#") + "s"));
        else                   sb.Append("  <color=").Append(Chip).Append(">Instant</color>");
        if (def.aim != SkillAim.Self) sb.Append("  <color=").Append(Chip).Append(">Range </color>").Append(Hl(def.castRange.ToString("0.#")));
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

        // Live ring/weapon reshapers — reads current investment + equipped weapon, so the card is always up to
        // date. The section header is dropped entirely when nothing is active (no empty "Ring & Weapon" heading).
        int mark = sb.Length;
        sb.Append("\n\n<color=").Append(Chip).Append("><b>Ring & Weapon</b></color>");
        int afterHeader = sb.Length;
        WeaponAffixes.AppendSummary(Character.Instance, sb, def);
        RingVoices.AppendSummary(Character.Instance, sb, def);
        if (sb.Length == afterHeader) sb.Length = mark;

        return sb.ToString();
    }

    /// <summary>One human-readable line for a single effect ("Reduces damage taken 30% for 6s", "+20% move speed
    /// for 5s"), with its variable numbers highlighted. Shared by the skill card AND the buff-bar hover card.</summary>
    public static string DescribeEffect(SkillEffect e)
    {
        if (e == null) return null;
        switch (e.kind)
        {
            case SkillEffectKind.Damage:
            {
                var ch = Character.Instance;
                float mult = e.damageMult <= 0f ? 1f : e.damageMult;
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
                return s;
            }
            case SkillEffectKind.Status:
            {
                string body;
                switch (e.ailment)
                {
                    case Ailment.Slow:  body = "slow " + Pct(1f - Mathf.Clamp01(e.magnitude)) + " for " + Hl(e.seconds.ToString("0.#") + "s"); break;
                    case Ailment.Stun:  body = "stun for " + Hl(e.seconds.ToString("0.#") + "s"); break;
                    case Ailment.Poison:
                    case Ailment.Bleed:
                    case Ailment.Burn:  body = e.ailment + " " + Hl(e.magnitude.ToString("0.#")) + "/tick for " + Hl(e.seconds.ToString("0.#") + "s"); break;
                    default:            body = e.ailment + " for " + Hl(e.seconds.ToString("0.#") + "s"); break;
                }
                // A proc (chance > 0) rolls per hit; else it's a guaranteed apply.
                return e.chance > 0f ? Pct(e.chance) + " per hit: " + body : char.ToUpper(body[0]) + body.Substring(1);
            }
            case SkillEffectKind.Execute:
                return (e.chance > 0f ? Pct(e.chance) + " per hit: " : "")
                     + "execute — bonus " + DmgWord(e.damageType) + " damage scaling with the target's missing HP (×" + Hl(e.magnitude.ToString("0.##")) + ")";
            case SkillEffectKind.Heal:          return "Heals " + Hl(e.amount.ToString()) + " HP";
            case SkillEffectKind.Guard:         return "Reduces damage taken " + Pct(Mathf.Clamp01(e.magnitude)) + " for " + Hl(e.seconds.ToString("0.#") + "s");
            case SkillEffectKind.Taunt:         return "Taunts nearby enemies for " + Hl(e.seconds.ToString("0.#") + "s");
            case SkillEffectKind.Teleport:      return "Blinks to the target location";
            case SkillEffectKind.Dash:          return "Dashes to the target location";
            case SkillEffectKind.Buff:          return "+" + Pct(Mathf.Max(0f, e.magnitude - 1f)) + " " + BuffStatName(e.buffStat) + " for " + Hl(e.seconds.ToString("0.#") + "s");
            case SkillEffectKind.Regen:         return "Restores " + Hl(e.magnitude.ToString("0.#")) + " HP/sec for " + Hl(e.seconds.ToString("0.#") + "s");
            case SkillEffectKind.Cleanse:       return "Removes " + Hl(Mathf.Max(1, e.amount).ToString()) + " debuff(s)";
            case SkillEffectKind.Vulnerability: return "Enemies take +" + Pct(e.magnitude) + " damage for " + Hl(e.seconds.ToString("0.#") + "s");
            case SkillEffectKind.Haste:         return "+" + Pct(Mathf.Max(0f, e.magnitude - 1f)) + " move speed for " + Hl(e.seconds.ToString("0.#") + "s");
            default:                            return null;
        }
    }

    static string BuffStatName(BuffStat s)
    {
        switch (s)
        {
            case BuffStat.Damage:      return "damage";
            case BuffStat.AttackSpeed: return "attack speed";
            case BuffStat.CastSpeed:   return "cast speed";
            default:                   return s.ToString();
        }
    }
}
