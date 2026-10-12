using System.Text;
using UnityEngine;

/// <summary>
/// Builds the rich-text body of a skill's hover card from its <see cref="SkillDef"/> — title, tag/category/tier chips,
/// cost line, description, and a human-readable line per effect with its VARIABLE NUMBERS highlighted. Pure function
/// (no UI dependency), so the hotbar, the Skills window, or any hover can share one description. A Damage effect shows its
/// authored multiplier and no damage estimate.
/// </summary>
public static class SkillTooltip
{
    // Rich-text tints from the interface theme: highlighted variable numbers, the description, category text.
    static readonly string Num = UiKit.Hex(UiKit.Accent);
    static readonly string Desc = UiKit.Hex(UiKit.TextMain);
    static readonly string Chip = UiKit.Hex(UiKit.TextDim);
    // The ultimate's pink, darkened only as far as it must be to read on a window.
    static readonly string UltimateHex = UiKit.Hex(UiKit.Readable(new Color(1f, 0.549f, 0.878f)));

    static readonly StringBuilder sb = new StringBuilder(512);

    /// <summary>Which dialect the describer writes its emphasis in. The WORDS and the NUMBERS are identical in every
    /// dialect — only the markup around them changes — so the wiki and the design docs can print exactly what the
    /// in-game card says without a second describer that would drift from this one.</summary>
    public enum Dialect
    {
        /// <summary>Unity rich text (<c>&lt;color&gt;</c>/<c>&lt;b&gt;</c>) — what the in-game hover cards want.</summary>
        RichText = 0,
        /// <summary>Markdown (<c>**bold**</c>) — what the wiki renderer and the design docs want.</summary>
        Markdown,
        /// <summary>No markup at all, for anything that just needs the sentence.</summary>
        Plain,
    }

    // The dialect in force for the call being built. Set only by the public entry points below, which always restore
    // it, so a caller can never leave the describer speaking the wrong dialect to the next one.
    static Dialect dialect = Dialect.RichText;

    static string Hl(string s)
    {
        switch (dialect)
        {
            case Dialect.Markdown: return "**" + s + "**";
            case Dialect.Plain:    return s;
            default:               return "<color=" + Num + "><b>" + s + "</b></color>";
        }
    }

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
    /// that lists them (the Skills window rows, the weapon skill-details panel) so they can never word it differently.
    ///
    /// <para>A skill can carry several triggers, so this joins every NON-OnCast one. OnCast is skipped: "you press it"
    /// is what a hotbar slot already says, so listing it would only be noise on a skill that has both.</para></summary>
    public static string TriggerLine(SkillDef def, Dialect d)
    {
        var prev = dialect; dialect = d;
        try { return TriggerLine(def); }
        finally { dialect = prev; }
    }

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
            case SkillTrigger.OnWeaponDrawn:       return "When you draw your weapon";
            default:                               return "Passive";
        }
    }

    static string NameOf(string skillId)
    {
        var d = SkillCatalog.Get(skillId);
        return d != null ? d.displayName : (string.IsNullOrEmpty(skillId) ? "(nothing)" : skillId);
    }

    // A quiet label ("MP", "CD", the family chip). Grey in the game; plain everywhere else, because the wiki and the
    // docs colour their own text and a stray hex code in a markdown file is noise.
    static string Chipped(string s) => dialect == Dialect.RichText ? "<color=" + Chip + ">" + s + "</color>" : s;

    /// <summary>The ONE cost/cadence line — mana, cooldown, cast, range — in whatever dialect is in force. Every
    /// surface that states a skill's price reads it from here, so the hover card, the wiki and the docs can never
    /// quote different numbers for the same skill.</summary>
    public static string CostLine(SkillDef def, Dialect d)
    {
        var prev = dialect; dialect = d;
        try { return CostLine(def); }
        finally { dialect = prev; }
    }

    public static string CostLine(SkillDef def)
    {
        if (def == null) return "";
        var t = new StringBuilder(96);
        t.Append(Chipped("MP ")).Append(Hl(def.manaCost.ToString()));
        t.Append("  ").Append(Chipped("CD ")).Append(Hl(def.cooldown.ToString("0.#") + "s"));
        if (def.castTime > 0f) t.Append("  ").Append(Chipped("Cast ")).Append(Hl(def.castTime.ToString("0.#") + "s"));
        else                   t.Append("  ").Append(Chipped("Instant"));
        if (def.Reticle != CastReticle.Instant) t.Append("  ").Append(Chipped("Range ")).Append(Hl(def.castRange.ToString("0.#")));
        return t.ToString();
    }

    /// <summary>The full card body for <paramref name="def"/> (title through effects), in the game's rich text.</summary>
    public static string Build(SkillDef def) => Build(def, Dialect.RichText);

    /// <summary>The same card body in a chosen dialect — the wiki and the design docs ask for
    /// <see cref="Dialect.Markdown"/> so they print exactly what the player is told, word for word.</summary>
    public static string Build(SkillDef def, Dialect d)
    {
        if (def == null) return "";
        var prev = dialect; dialect = d;
        try
        {
            sb.Length = 0;

            // Title + tier.
            if (d == Dialect.RichText)
            {
                sb.Append("<size=120%><b>").Append(def.displayName).Append("</b></size>");
                if (def.IsUltimate) sb.Append("  <color=").Append(UltimateHex).Append("><b>ULTIMATE</b></color>");
                else if (KindTag(def) != null) sb.Append("  <color=").Append(Chip).Append("><b>").Append(KindTag(def)).Append("</b></color>");
            }
            else
            {
                sb.Append(Hl(def.displayName));
                if (def.IsUltimate) sb.Append("  ").Append(Hl("ULTIMATE"));
                else if (KindTag(def) != null) sb.Append("  ").Append(KindTag(def));
            }
            sb.Append('\n');

            // Which form of its weapon's style this is — only for a weapon whose style is in the game.
            string form = SkillForms.Line(def);
            if (form.Length > 0)
            {
                if (d == Dialect.RichText) sb.Append("<color=").Append(Desc).Append('>').Append(form).Append("</color>\n");
                else sb.Append(form).Append('\n');
            }

            // Tag (archetype) + category chips.
            if (d == Dialect.RichText)
                sb.Append("<color=").Append(Hex(UiKit.Readable(ArchetypeColor(def.archetype)))).Append("><b>")
                  .Append(ArchetypeName(def.archetype)).Append("</b></color>");
            else
                sb.Append(ArchetypeName(def.archetype));
            sb.Append("  ").Append(Chipped(def.family.ToString())).Append('\n');

            // Cost / cadence line.
            sb.Append(CostLine(def)).Append('\n');

            // Description.
            if (!string.IsNullOrEmpty(def.description))
            {
                if (d == Dialect.RichText) sb.Append("<color=").Append(Desc).Append("><i>").Append(def.description).Append("</i></color>\n");
                else if (d == Dialect.Markdown) sb.Append('*').Append(def.description).Append("*\n");
                else sb.Append(def.description).Append('\n');
            }

            // Effects — one line each, numbers highlighted.
            var lines = EffectLines(def);
            for (int i = 0; i < lines.Count; i++) sb.Append("\n• ").Append(lines[i]);

            // What it takes to learn it and what its levels move — only a skill that belongs to a weapon has either.
            if (def.weaponType != WeaponType.None) LevelSection(def, d);

            return sb.ToString();
        }
        finally { dialect = prev; }
    }

    // Reused across calls so listing a skill's effects allocates nothing per call on the game's side.
    static readonly System.Collections.Generic.List<string> effectLines = new System.Collections.Generic.List<string>(8);

    /// <summary>Every effect line this skill would show, in order — the same lines the hover card bullets. Returns a
    /// SHARED list: read it or copy it before the next call. Assumes the dialect is already set by the caller.
    ///
    /// <para>Each line states the effect's BASE number. What the character's weapon trademark adds on top is said
    /// underneath, on every effect that has a main number — the same effects the fight raises
    /// (<see cref="EffectScaling.Boostable"/>), so the card and the fight cannot disagree about which. What the skill's
    /// own LEVEL adds is the card's level section, number by number (<see cref="Build(SkillDef, Dialect)"/>).</para></summary>
    public static System.Collections.Generic.List<string> EffectLines(SkillDef def)
    {
        effectLines.Clear();
        if (def == null || def.bindings == null) return effectLines;
        var ch = Character.Instance;
        float trademark = ch != null ? ch.TrademarkLift(def.id) : 0f;
        for (int i = 0; i < def.bindings.Count; i++)
        {
            var b = def.bindings[i];
            if (b == null || b.effects == null) continue;
            for (int k = 0; k < b.effects.Count; k++)
            {
                var e = b.effects[k];
                string line = DescribeEffect(e);
                if (string.IsNullOrEmpty(line)) continue;
                if (EffectScaling.Boostable(e) && trademark > 0f)
                    line += "\n   Trademark skill bonus " + MainNumberWord(e) + ": +" + Pct(trademark);
                effectLines.Add(line);
            }
        }
        return effectLines;
    }

    /// <summary>The main number of <paramref name="e"/> in a player's words — "damage", "damage buff", "bleed
    /// damage" — for the lines that say what raises it. Null for an effect with no main number.</summary>
    public static string MainNumberWord(SkillEffect e)
    {
        if (!EffectScaling.Boostable(e)) return null;
        switch (e.kind)
        {
            case SkillEffectKind.Damage:               return "damage";
            case SkillEffectKind.Execute:              return "execute damage";
            case SkillEffectKind.Heal:                 return "heal";
            case SkillEffectKind.Shield:               return "shield";
            case SkillEffectKind.Status:               return e.ailment.ToString().ToLowerInvariant() + " damage";
            case SkillEffectKind.SubStatBuff:          return SubStatName(e.subStat) + " buff";
            case SkillEffectKind.SubStatDebuff:        return SubStatName(e.subStat) + " debuff";
            case SkillEffectKind.BoostOutgoingDamage:  return "damage bonus";
            case SkillEffectKind.ReduceOutgoingDamage: return "damage cut";
            case SkillEffectKind.AddStacks:
            {
                var of = SkillCatalog.Get(e.modifierSkillId);
                return of != null ? "stacks added to " + of.displayName : "stacks added";
            }
            default:                                   return null;
        }
    }

    // What a live cast produces from an authored OUTPUT number BEFORE practice and trademark: the ring's affinity
    // term, from the fight's own multiplier with no lift passed in — never a copy of its formula. The lifts are
    // said on their own lines (EffectLines), so the base number here stays the base.
    static float LiveMult(SkillEffect e) => SkillEffectApplier.OutputScale(e, Character.Instance, 0f);

    /// <summary>Every effect line in a chosen dialect. Copies into <paramref name="into"/> so the caller owns the
    /// result (the no-alloc list above is only safe until the next call).</summary>
    public static void EffectLines(SkillDef def, Dialect d, System.Collections.Generic.List<string> into)
    {
        if (into == null) return;
        into.Clear();
        var prev = dialect; dialect = d;
        try { into.AddRange(EffectLines(def)); }
        finally { dialect = prev; }
    }

    /// <summary>One human-readable line for a single effect ("Reduces damage taken 30% for 6s", "+20% move speed
    /// for 5s"), with its variable numbers highlighted. Shared by the skill card AND the buff-bar hover card.</summary>
    public static string DescribeEffect(SkillEffect e, Dialect d)
    {
        var prev = dialect; dialect = d;
        try { return DescribeEffect(e); }
        finally { dialect = prev; }
    }

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
                float mult = e.damageMult.value <= 0f ? 1f : e.damageMult.value;
                string s = "Deals " + Hl(mult.ToString("0.##") + "×") + " " + DmgWord(e.damageType) + " damage";
                int hits = SkillEffectApplier.EffectiveHits(e, Character.Instance);   // affinity-scaled, matches what actually lands
                if (hits > 1) s += ", " + Hl(hits + "×") + " hits";
                // Any number may answer to the pile now, so the note belongs to the NUMBER, said the same way
                // wherever it appears rather than spelled out per kind.
                s += StackNote(e.damageMult);
                if (e.damageMultPerPointGivenUp.value > 0f)
                    s += ", +" + Hl(e.damageMultPerPointGivenUp.value.ToString("0.###") + "×") + " per point of MP or HP given up";
                if (e.damageMultPerMetre.value > 0f)
                    s += ", +" + Hl(e.damageMultPerMetre.value.ToString("0.###") + "×") + " per metre to the target";
                return s;
            }
            case SkillEffectKind.Status:
            {
                string body;
                switch (e.ailment)
                {
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
            case SkillEffectKind.Heal:          return "Heals " + Hl(Mathf.RoundToInt(e.amount.value * LiveMult(e)).ToString()) + " HP";
            case SkillEffectKind.DrainMana:     return "Drains up to " + Hl(Mathf.RoundToInt(e.amount.value).ToString()) + " MP and gives it to you";
            case SkillEffectKind.SpendAllMana:  return "Spends all your MP";
            case SkillEffectKind.DropHealthToOne: return "Drops your HP to 1";
            case SkillEffectKind.Taunt:         return "Taunts nearby enemies for " + Hl(e.seconds.value.ToString("0.#") + "s");
            case SkillEffectKind.Teleport:      return "Blinks to the target location";
            case SkillEffectKind.Dash:          return "Dashes to the target location";
            case SkillEffectKind.ReturnToSavePoint:    return "Returns you to your save point";
            case SkillEffectKind.TeleportToRandomSpot: return "Sets you down somewhere random on this map";
            // Magnitude is the amount itself; on a stacking one it is the PER-STACK amount, so say so.
            case SkillEffectKind.SubStatBuff:   return SubStatLine(e, worse: false);
            case SkillEffectKind.SubStatDebuff: return SubStatLine(e, worse: true);
            case SkillEffectKind.Cleanse:       return "Removes " + Hl(Mathf.Max(1, e.amount.value).ToString()) + " debuff(s)";
            case SkillEffectKind.Summon:        return "Calls up its summons";
            case SkillEffectKind.Knockback:     return "Knocks enemies back";
            case SkillEffectKind.Pull:          return "Pulls enemies in";
            case SkillEffectKind.Shield:        return "Absorbs " + Hl(Mathf.RoundToInt(e.amount.value * LiveMult(e)).ToString()) + " damage for " + Hl(e.seconds.value.ToString("0.#") + "s");
            case SkillEffectKind.BoostOutgoingDamage:
            {
                // Charges ARE stacks, read from the same knob the runtime uses — never a second number to keep in sync.
                int charges = e.stacking ? Mathf.Max(1, Mathf.RoundToInt(e.stacksPerApply.value)) : 1;
                string window = " within " + Hl(e.seconds.value.ToString("0.#") + "s");
                string amount = "+" + Pct(e.magnitude.value * LiveMult(e)) + " damage";
                return charges > 1
                    ? "Your next " + Hl(charges.ToString()) + " attacks deal " + amount + window
                    : "Your next attack deals " + amount + window;
            }
            case SkillEffectKind.ElementConverter:
            {
                int secs = Mathf.Max(0, Mathf.RoundToInt(e.seconds.value));
                string clock = Hl((secs / 60) + ":" + (secs % 60).ToString("00"));
                string element = Hl(Elements.Name(e.element));
                return e.converts == EquipSlotType.Armor
                    ? Subject(e, "Your", "Their") + " armor is " + element + " (level " + Elements.ConverterLevel + ") for " + clock
                    : Subject(e, "Your", "Their") + " weapon strikes as " + element + " for " + clock;
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
                string amount = "−" + Pct(Mathf.Clamp01(e.magnitude.value * LiveMult(e))) + " damage";
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
    // from an assumption baked into the kind: a damage-reduction DEBUFF authored on the caster is a self-drawback
    // (Overclock's fragile window), not a debuff on enemies, and saying "enemies" tells the player the opposite.
    static string Subject(SkillEffect e, string caster, string target) => SkillEffect.LandsOnCaster(e) ? caster : target;

    /// <summary>The sub-stat's name in ordinary words. Every stat has its OWN row and the catch-all answers
    /// "undefined", so a stat added without a row here says so on screen instead of wearing another stat's name.</summary>
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
            case SubStat.DamageReduction: return "damage reduction";
            case SubStat.BasicAttackRange: return "basic attack range";
            case SubStat.BlockChance:   return "block chance";
            case SubStat.ExtraBounceJumps: return "extra bounce jumps";
            case SubStat.MaxMP:         return "max MP";
            // NOT A REAL STAT'S NAME. A catch-all that answers with a plausible stat makes a missing row look
            // like a working one: a new stat read "max MP" on the player's screen and nothing anywhere said it
            // had no row. A word that is obviously not a stat is the whole point.
            default:                    return "undefined";
        }
    }

    /// <summary>One line for a sub-stat buff or debuff: which way, how much, which stat, how long. The amount reads as
    /// a percentage or as a plain number depending on what the stat's number MEANS, so a regen never claims to be
    /// "+6%" of a trickle that is usually zero.</summary>
    static string SubStatLine(SkillEffect e, bool worse)
    {
        float v = Mathf.Max(0f, e.magnitude.value * LiveMult(e));
        string amount = SubStats.IsPercent(e.subStat) ? Pct(v) : v.ToString("0.##");
        return (worse ? "−" : "+") + amount + " " + SubStatName(e.subStat)
             + (e.stacking ? " per stack" : "")
             + " for " + Hl(e.seconds.value.ToString("0.#") + "s");
    }

    // PASSIVE or ACTIVE beside the name (an ultimate already says ULTIMATE). Nothing for what an item runs: it is
    // neither pressed from the bar nor fired by a moment of the fight.
    static string KindTag(SkillDef def)
        => def.ItemUseEntry != null ? null : def.IsPassive ? "PASSIVE" : "ACTIVE";

    // ---- learning and levels ----------------------------------------------------------------------------------

    // What it takes to learn a weapon skill, how far it can go, and every number its level moves. In the game the
    // numbers are the server's finished values (MasteryProgress.SentView) and the "met" colours compare what the
    // server sent; with no character (the wiki, an editor tool) only the fixed facts are written.
    static void LevelSection(SkillDef def, Dialect d)
    {
        var ch = Character.Instance;
        var mastery = ch != null ? ch.Mastery : null;
        int opensAt = Mathf.Max(1, def.masteryLevel);
        sb.Append('\n');
        sb.Append("\nRequires ").Append(Met(WeaponTypes.Label(def.weaponType) + " Mastery " + opensAt,
                                            mastery != null ? mastery.SentLevel(def.weaponType) >= opensAt : (bool?)null, d));
        if (!string.IsNullOrEmpty(def.needsSkillId))
            sb.Append("\nRequires ").Append(Met(NameOf(def.needsSkillId),
                                                mastery != null ? mastery.HasLearned(def.needsSkillId) : (bool?)null, d));

        int top = Mathf.Max(1, def.maxLevel);
        int level = mastery != null ? mastery.LevelOf(def.id) : 0;
        bool hasView = false;
        SkillLevelView view = default;
        if (mastery == null) sb.Append("\nMax level ").Append(Hl(top.ToString()));
        else
        {
            hasView = mastery.SentView(def.id, out view);
            sb.Append("\nSkill level ").Append(Hl(level.ToString())).Append(" / ").Append(top);
        }

        bool any = false;
        int channel = 0;
        for (int b = 0; def.bindings != null && b < def.bindings.Count; b++)
        {
            var binding = def.bindings[b];
            if (binding == null || binding.effects == null) continue;
            for (int k = 0; k < binding.effects.Count; k++)
            {
                var e = binding.effects[k];
                if (!SkillLevelNumbers.MainNumber(e, out var number, out bool whole)) continue;
                if (!any)
                {
                    sb.Append("\nEach level: ").Append(Hl("+" + Mathf.RoundToInt(MasteryMath.SkillLevelPercent * 100f) + "%"))
                      .Append(" to");
                    any = true;
                }
                string word = MainNumberWord(e) ?? "";
                if (word.Length > 0) word = char.ToUpper(word[0]) + word.Substring(1);
                bool perStack = e.stacking && (e.kind == SkillEffectKind.SubStatBuff || e.kind == SkillEffectKind.SubStatDebuff);
                sb.Append("\n   ").Append(word).Append(perStack ? " per stack" : "");
                if (hasView && channel < view.now.Length && channel < view.next.Length)
                {
                    string now = level >= 1 ? Hl(LevelNumber(e, number, whole, view.now[channel])) : Chipped("—");
                    sb.Append(": ").Append(now);
                    if (level < top) sb.Append(" → ").Append(Hl(LevelNumber(e, number, whole, view.next[channel])));
                    else sb.Append("  ").Append(Chipped("max"));
                }
                channel++;
            }
        }
        if (!any) sb.Append('\n').Append(Chipped("No number on this skill grows with its level."));
    }

    // A requirement, coloured met or not when there is a character to judge it against.
    static string Met(string what, bool? met, Dialect d)
    {
        if (met == null || d != Dialect.RichText) return Hl(what);
        string hex = UiKit.Hex(met.Value ? UiKit.Good : UiKit.Bad);
        return "<color=" + hex + "><b>" + what + "</b></color>";
    }

    // One finished number in the units its effect is written in everywhere else on the card.
    static string LevelNumber(SkillEffect e, ScaledNumber authored, bool whole, float v)
    {
        switch (e.kind)
        {
            case SkillEffectKind.Damage:
            case SkillEffectKind.Execute:
                return v.ToString("0.##") + "×";
            case SkillEffectKind.Status:
                return v.ToString("0.#") + "/tick";
            case SkillEffectKind.SubStatBuff:
            case SkillEffectKind.SubStatDebuff:
                return SubStats.IsPercent(e.subStat) ? FinePct(v) : v.ToString("0.##");
            case SkillEffectKind.BoostOutgoingDamage:
            case SkillEffectKind.ReduceOutgoingDamage:
                return FinePct(v);
            default:
                return whole && !authored.useStacks ? Mathf.RoundToInt(v).ToString() : v.ToString("0.##") + "×";
        }
    }

    // A percentage to one decimal — a level moves a 3% buff to 3.2%, which a whole-number percent would hide.
    static string FinePct(float f01) => (f01 * 100f).ToString("0.#") + "%";
}
