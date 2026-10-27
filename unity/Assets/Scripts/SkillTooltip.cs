using System.Text;
using UnityEngine;

/// <summary>
/// Builds the rich-text body of a skill's hover card from its <see cref="SkillDef"/> — title, tag/category/tier chips,
/// cost line, description, and a human-readable line per effect with its VARIABLE NUMBERS highlighted. Pure function
/// (no UI dependency), so the hotbar, the Skills window, or any hover can share one description. The card
/// (<see cref="Build(SkillDef, Dialect)"/>) shows, for a skill of the weapon in hand, the damage each strike lands and the
/// cast time as the server finished them (<see cref="SkillCardNumbers"/>); every effect line elsewhere states its
/// authored multiplier.
/// </summary>
public static class SkillTooltip
{
    // Rich-text tints from the interface theme: highlighted variable numbers, the description, category text.
    static string Num => UiKit.Hex(UiKit.Accent);
    static string Desc => UiKit.Hex(UiKit.TextMain);
    static string Chip => UiKit.Hex(UiKit.TextDim);
    // The ultimate's pink, moved only as far as it must be to read on a window — worked out once per theme.
    static string ultimateLight, ultimateDark;
    static string UltimateHex => UiKit.IsDark ? ultimateDark ??= UltimateHexNow() : ultimateLight ??= UltimateHexNow();
    static string UltimateHexNow() => UiKit.Hex(UiKit.Readable(new Color(1f, 0.549f, 0.878f)));

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

    // The stat an orb's element raises, in the words the character sheet uses.
    static string OrbStatName(OrbElement orb)
    {
        switch (orb)
        {
            case OrbElement.Fire:  return "magic attack";
            case OrbElement.Earth: return "defence";
            case OrbElement.Wind:  return "cast speed";
            default:               return "max MP";
        }
    }

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
            case SkillTrigger.OnHealDealt:         return "When you heal someone";
            case SkillTrigger.OnDodge:             return "When you dodge an attack";
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
        if (def.HasWindup) t.Append("  ").Append(Chipped("Cast ")).Append(Hl(UnitStatMath.CastWindupSeconds(def.castTime, 1f, def.fixedCastTime).ToString("0.#") + "s"));
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
            var ch = Character.Instance;
            var mastery = ch != null ? ch.Mastery : null;
            bool hasCard = false, hasView = false;
            SkillCardView card = default;
            SkillLevelView view = default;
            if (mastery != null)
            {
                hasCard = mastery.SentCard(def.id, out card);
                hasView = mastery.SentView(def.id, out view);
            }
            int level = mastery != null ? mastery.LevelOf(def.id) : 0;
            int top = Mathf.Max(1, def.maxLevel);
            bool grows = !def.IsOrbCombo && def.weaponType != WeaponType.None && mastery != null && level < top;

            // NAME, then what kind of skill it is.
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

            // THE FACTS: every element its strikes land as, where it is aimed, and how far.
            string facts = ElementsLine(def, hasCard, card);
            string aim = def.Reticle == CastReticle.Ground ? "Ground" : def.Reticle == CastReticle.Unit ? "Enemy" : null;
            if (aim != null)
                facts += (facts.Length > 0 ? Chipped(" · ") : "") + Chipped(aim + " · " + def.castRange.ToString("0.#") + " m");
            if (facts.Length > 0) sb.Append('\n').Append(facts);

            // WHAT IT COSTS — or, for a passive, the moment it answers.
            if (def.IsPassive)
            {
                string when = TriggerLine(def);
                if (when.Length > 0) sb.Append('\n').Append(Hl(when));
            }
            else sb.Append('\n').Append(CostRow(def, hasCard, card));

            // WHAT HAPPENS, in order, with no numbers — every amount is on a line below.
            SplitDescription(def.description, out string flavour, out string how);
            if (how.Length > 0) sb.Append('\n').Append(how);

            // ONE LINE PER THING IT DOES, the number first.
            sb.Append('\n');
            int channel = 0;
            for (int b = 0; def.bindings != null && b < def.bindings.Count; b++)
            {
                var fx = def.bindings[b]?.effects;
                if (fx == null) continue;
                for (int k = 0; k < fx.Count; k++)
                {
                    var e = fx[k];
                    bool main = SkillLevelNumbers.MainNumber(e, out var number, out bool whole);
                    int c = main ? channel++ : -1;
                    string line = CardLine(def, e, number, whole, c, hasCard, card, hasView, view, level, grows);
                    if (!string.IsNullOrEmpty(line)) sb.Append('\n').Append(line);
                }
            }

            // WHAT IT TAKES, and how far it has come. (The orbs' own section opens with its blank line.)
            if (def.IsOrbCombo) OrbSection(def, d);
            else if (def.weaponType != WeaponType.None) { sb.Append('\n'); LevelFooter(def, d, mastery, level, top); }

            // FLAVOUR LAST, small and grey, with the form it is.
            string form = SkillForms.Line(def);
            if (flavour.Length > 0 || form.Length > 0) sb.Append('\n');
            if (flavour.Length > 0)
                sb.Append('\n').Append(d == Dialect.RichText ? "<size=90%><color=" + Chip + "><i>" + flavour + "</i></color></size>"
                                                             : d == Dialect.Markdown ? "*" + flavour + "*" : flavour);
            if (form.Length > 0) sb.Append('\n').Append(Chipped(form));

            // WHERE EACH CHANGED NUMBER COMES FROM — only the numbers something on you is changing right now.
            if (hasCard) SourcesSection(def, card);

            return sb.ToString();
        }
        finally { dialect = prev; }
    }

    // A description is its flavour line first, then what the skill does; a text with no line break is all "what it does".
    static void SplitDescription(string text, out string flavour, out string how)
    {
        text = text ?? "";
        int nl = text.IndexOf('\n');
        if (nl < 0) { flavour = ""; how = text.Trim(); return; }
        flavour = text.Substring(0, nl).Trim();
        how = text.Substring(nl + 1).Trim();
    }

    // Each element the skill's strikes land as, once, in that element's colour. A strike that is a basic attack lands as
    // the weapon's element, which only the server's numbers know; the rest is the skill's own.
    static string ElementsLine(SkillDef def, bool hasCard, SkillCardView card)
    {
        string line = "";
        int seen = 0, channel = 0;
        for (int b = 0; def.bindings != null && b < def.bindings.Count; b++)
        {
            var fx = def.bindings[b]?.effects;
            if (fx == null) continue;
            for (int k = 0; k < fx.Count; k++)
            {
                var e = fx[k];
                bool main = SkillLevelNumbers.MainNumber(e, out _, out _);
                int c = main ? channel++ : -1;
                if (e.kind != SkillEffectKind.Damage) continue;
                // A burn put out lands what the burn had left, as it is — no element of its own to name.
                if (e.cashMark == MarkKind.Burn && e.damageMult.value <= 0f && e.damageMultPerMark.value <= 0f) continue;
                Element el;
                if (hasCard && c >= 0 && c < card.LineCount && card.At(c, SkillCardNumbers.Field.Shape) != (int)SkillCardShape.Level)
                    el = (Element)card.At(c, SkillCardNumbers.Field.Element);
                else if (e.resolveAsBasicAttack) continue;
                else el = e.element;
                int bit = 1 << (int)el;
                if ((seen & bit) != 0) continue;
                seen |= bit;
                if (line.Length > 0) line += Chipped(" · ");
                line += ElementWord(el);
            }
        }
        return line;
    }

    static string ElementWord(Element el) => dialect == Dialect.RichText
        ? "<color=" + Hex(UiKit.Readable(Elements.Tint(el))) + "><b>" + Elements.Name(el) + "</b></color>"
        : Hl(Elements.Name(el));

    // MP · CD · the cast time this character waits right now. When something timed on them is changing it, the time
    // without it is struck through before it, coloured by whether the change helps.
    static string CostRow(SkillDef def, bool hasCard, SkillCardView card)
    {
        var t = new StringBuilder(96);
        t.Append(Chipped("MP ")).Append(Hl(def.manaCost.ToString()));
        t.Append("  ").Append(Chipped("CD ")).Append(Hl(def.cooldown.ToString("0.#") + "s"));
        if (!def.HasWindup) { t.Append("  ").Append(Chipped("Instant")); return t.ToString(); }
        t.Append("  ").Append(Chipped("Cast "));
        float fixedT = Mathf.Max(0f, def.fixedCastTime);
        if (hasCard && card.cast != null && card.cast.Length >= SkillCardNumbers.CastFloats)
        {
            float now = card.cast[0], plain = card.cast[1];
            t.Append(Changed(Seconds(plain), Seconds(now), now < plain));
        }
        else t.Append(Hl(Seconds(Mathf.Max(0f, def.castTime) + fixedT)));
        if (fixedT > 0f) t.Append(Chipped(" (" + Seconds(fixedT) + " fixed)"));
        return t.ToString();
    }

    static string Seconds(float s) => s.ToString("0.##") + "s";

    // A number something timed is changing: the value without it struck through, then the value now, green when the
    // change helps and red when it hurts. Unchanged, it is just the number.
    static string Changed(string plain, string now, bool helps)
    {
        if (plain == now) return Hl(now);
        switch (dialect)
        {
            case Dialect.Markdown: return "~~" + plain + "~~ **" + now + "**";
            case Dialect.Plain:    return plain + " → " + now;
            default:
                return "<color=" + Chip + "><s>" + plain + "</s></color> <color=" + UiKit.Hex(helps ? UiKit.Good : UiKit.Bad)
                       + "><b>" + now + "</b></color>";
        }
    }

    static string Band(int min, int max) => min == max ? min.ToString() : min + "–" + max;

    // ONE EFFECT'S LINE. A strike with the server's numbers: the damage it lands, then its element and type, then what
    // it grows by from the target; its crit; its value at the next level. Any other effect with a growing number: the
    // sentence it always reads, then its value now and at the next level. The rest: its sentence.
    static string CardLine(SkillDef def, SkillEffect e, ScaledNumber number, bool whole, int channel, bool hasCard,
                           SkillCardView card, bool hasView, SkillLevelView view, int level, bool grows)
    {
        string sentence = DescribeEffect(e);
        if (string.IsNullOrEmpty(sentence)) return null;
        if (channel >= 0 && hasCard && channel < card.LineCount)
        {
            var shape = (SkillCardShape)card.At(channel, SkillCardNumbers.Field.Shape);
            if (shape == SkillCardShape.Band || shape == SkillCardShape.BandPerMark || shape == SkillCardShape.CritOnly)
            {
                int min = card.At(channel, SkillCardNumbers.Field.Min), max = card.At(channel, SkillCardNumbers.Field.Max);
                int pMin = card.At(channel, SkillCardNumbers.Field.PlainMin), pMax = card.At(channel, SkillCardNumbers.Field.PlainMax);
                int crit = card.At(channel, SkillCardNumbers.Field.Crit);
                var el = (Element)card.At(channel, SkillCardNumbers.Field.Element);
                string num = shape == SkillCardShape.CritOnly ? Changed(crit.ToString(), crit.ToString(), true)
                           : Changed(Band(pMin, pMax), Band(min, max), max >= pMax);
                bool basic = e.resolveAsBasicAttack;
                string what = (basic ? "" : ElementWord(el) + " ") + (shape == SkillCardShape.CritOnly ? "crit" : DmgWord(e.damageType))
                            + " damage";
                if (basic) what = ElementWord(el) + " damage, as a basic attack";
                if (shape == SkillCardShape.BandPerMark) what += " per " + MarkName(e.cashMark) + " on the target";
                int hits = SkillEffectApplier.EffectiveHits(e, Character.Instance);
                if (hits > 1) what += " in " + Hl(hits.ToString()) + " hits";
                what += GrowthNotes(e, false);
                if (shape != SkillCardShape.BandPerMark) what += MarkClause(e, what);
                if (shape == SkillCardShape.Band && crit > 0) what += Chipped(" · crit ") + Hl(crit.ToString());
                if (grows && shape != SkillCardShape.CritOnly)
                    what += Chipped("  → " + Band(card.At(channel, SkillCardNumbers.Field.NextMin),
                                                  card.At(channel, SkillCardNumbers.Field.NextMax)));
                return num + " " + what;
            }
        }
        if (channel >= 0 && hasView && channel < view.now.Length && channel < view.next.Length && level >= 1)
        {
            string now = LevelNumber(e, number, whole, view.now[channel]);
            string line = sentence + Chipped("  · now ") + Hl(now);
            if (grows) line += Chipped("  → " + LevelNumber(e, number, whole, view.next[channel]));
            return line;
        }
        return sentence;
    }

    // What it takes to learn it — said only while it is not yet met — and how far it has come. Without a character
    // (an editor tool, the docs) the fixed facts, and what each level adds.
    static void LevelFooter(SkillDef def, Dialect d, MasteryProgress mastery, int level, int top)
    {
        int opensAt = Mathf.Max(1, def.masteryLevel);
        string weapon = WeaponTypes.Label(def.weaponType);
        if (mastery == null)
        {
            sb.Append("\nOpens at ").Append(Hl(weapon + " Mastery " + opensAt));
            if (!string.IsNullOrEmpty(def.needsSkillId)) sb.Append("\nNeeds ").Append(Hl(NameOf(def.needsSkillId)));
            sb.Append("\nMax level ").Append(Hl(top.ToString())).Append(Chipped(" · each level +"
                      + Mathf.RoundToInt(MasteryMath.SkillLevelPercent * 100f) + "% to every growing number"));
            return;
        }
        int have = mastery.SentLevel(def.weaponType);
        if (have < opensAt)
            sb.Append('\n').Append(Met("Needs " + weapon + " Mastery " + opensAt, false, d)).Append(Chipped("  you: " + have));
        if (!string.IsNullOrEmpty(def.needsSkillId) && !mastery.HasLearned(def.needsSkillId))
            sb.Append('\n').Append(Met("Needs " + NameOf(def.needsSkillId), false, d)).Append(Chipped("  not learned"));
        sb.Append('\n').Append(Chipped("Level ")).Append(Hl(level + " / " + top));
    }

    // THE BREAKDOWN, for the numbers something is changing: one line per source under each, every amount the server's.
    static void SourcesSection(SkillDef def, SkillCardView card)
    {
        int start = sb.Length;
        sb.Append("\n\n").Append(Chipped("Where each number comes from"));
        int opened = sb.Length;
        if (card.cast != null && card.cast.Length >= SkillCardNumbers.CastFloats && def.HasWindup && def.castTime > 0f)
        {
            float dex = card.cast[2], ring = card.cast[3], timed = card.cast[4];
            if (Mathf.Abs(dex) > 0.005f || Mathf.Abs(ring) > 0.005f || Mathf.Abs(timed) > 0.005f)
            {
                sb.Append('\n').Append(Hl("Cast " + Seconds(card.cast[0])));
                SourceLine(Seconds(def.castTime), "the skill");
                if (Mathf.Abs(dex) > 0.005f) SourceLine(Signed(dex, "s"), "DEX");
                if (Mathf.Abs(ring) > 0.005f) SourceLine(Signed(ring, "s"), "cast speed from the ring and weapon mastery");
                if (Mathf.Abs(timed) > 0.005f) SourceLine(Signed(timed, "s"), "timed effects on you");
                if (def.fixedCastTime > 0f) SourceLine("+" + Seconds(def.fixedCastTime), "fixed, nothing shortens it");
            }
        }
        for (int c = 0; c < card.LineCount; c++)
        {
            var shape = (SkillCardShape)card.At(c, SkillCardNumbers.Field.Shape);
            if (shape != SkillCardShape.Band && shape != SkillCardShape.BandPerMark) continue;
            int tMin = card.At(c, SkillCardNumbers.Field.TimedMin), tMax = card.At(c, SkillCardNumbers.Field.TimedMax);
            if (tMin == 0 && tMax == 0) continue;
            sb.Append('\n').Append(Hl(Band(card.At(c, SkillCardNumbers.Field.Min), card.At(c, SkillCardNumbers.Field.Max))
                                      + (shape == SkillCardShape.BandPerMark ? " damage per mark" : " damage")));
            SourceLine(Band(card.At(c, SkillCardNumbers.Field.PlainMin), card.At(c, SkillCardNumbers.Field.PlainMax)), "the skill, with your attack");
            SourceLine((tMax >= 0 ? "+" : "") + Band(tMin, tMax), "timed effects on you");
        }
        if (sb.Length == opened) sb.Length = start;
    }

    static void SourceLine(string amount, string source) =>
        sb.Append("\n   ").Append(Hl(amount)).Append(' ').Append(Chipped(source));

    static string Signed(float v, string unit) => (v > 0f ? "+" : v < 0f ? "−" : "") + Mathf.Abs(v).ToString("0.##") + unit;

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
            case SkillEffectKind.BoostBasicAttackDamage: return "basic attack damage bonus";
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
        if (line != null && e.onlyIfMarked != MarkKind.None)
            line += e.stackPerMark ? " — one for every " + MarkName(e.onlyIfMarked) + " the target carried, only on one that carried any"
                                   : " — only on a target that carried " + MarkName(e.onlyIfMarked);
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
                bool payoffOnly = e.damageMult.value <= 0f && e.cashMark != MarkKind.None;
                float mult = e.damageMult.value <= 0f ? 1f : e.damageMult.value;
                string s = payoffOnly ? "" : "Deals " + Hl(mult.ToString("0.##") + "×") + " " + DmgWord(e.damageType) + " damage";
                int hits = SkillEffectApplier.EffectiveHits(e, Character.Instance);   // affinity-scaled, matches what actually lands
                if (hits > 1) s += ", " + Hl(hits + "×") + " hits";
                // Any number may answer to the pile now, so the note belongs to the NUMBER, said the same way
                // wherever it appears rather than spelled out per kind.
                s += GrowthNotes(e, true);
                return s + MarkClause(e, s);
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
            case SkillEffectKind.Heal:
            {
                float mult = LiveMult(e);
                if (e.healShareOfMaxHP)
                {
                    string lowPct = Mathf.RoundToInt(e.amount.value * mult * 100f) + "%";
                    return e.healAmountMaxSet && e.healAmountMax.value > e.amount.value
                        ? "Heals " + Hl(lowPct + "–" + Mathf.RoundToInt(e.healAmountMax.value * mult * 100f) + "%") + " of max HP"
                        : "Heals " + Hl(lowPct) + " of max HP";
                }
                string low = Mathf.RoundToInt(e.amount.value * mult).ToString();
                return e.healAmountMaxSet && e.healAmountMax.value > e.amount.value
                    ? "Heals " + Hl(low + "–" + Mathf.RoundToInt(e.healAmountMax.value * mult)) + " HP"
                    : "Heals " + Hl(low) + " HP";
            }
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
            case SkillEffectKind.BoostBasicAttackDamage:
            {
                int charges = e.stacking ? Mathf.Max(1, Mathf.RoundToInt(e.stacksPerApply.value)) : 1;
                string window = " within " + Hl(e.seconds.value.ToString("0.#") + "s");
                string amount = "+" + Pct(e.magnitude.value * LiveMult(e)) + " damage";
                return charges > 1
                    ? "Your next " + Hl(charges.ToString()) + " basic attacks deal " + amount + window
                    : "Your next basic attack deals " + amount + window;
            }
            case SkillEffectKind.SureHit:
            {
                int charges = e.stacking ? Mathf.Max(1, Mathf.RoundToInt(e.stacksPerApply.value)) : 1;
                string window = " within " + Hl(e.seconds.value.ToString("0.#") + "s");
                return charges > 1
                    ? "Your next " + Hl(charges.ToString()) + " basic attacks can't miss" + window
                    : "Your next basic attack can't miss" + window;
            }
            case SkillEffectKind.ResetCooldown:
            {
                if (e.resetsLastCombination) return "The last combination you cast is ready again at once, ultimates included";
                var of = SkillCatalog.Get(e.modifierSkillId);
                return (of != null ? Hl(of.displayName) : "A skill") + " is ready again at once";
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
            case SkillEffectKind.Charges:
            {
                int held = e.stacking ? Mathf.Max(1, Mathf.RoundToInt(e.stacksPerApply.value)) : 1;
                return "Gives " + Hl(held.ToString()) + (held > 1 ? " charges" : " charge")
                     + " for " + Hl(e.seconds.value.ToString("0.#") + "s");
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
            // ---- the Staff ----
            case SkillEffectKind.AddOrb:
                return e.orb == OrbElement.None ? null
                     : "Holds one " + Hl(e.orb.ToString()) + " orb: +" + Pct(e.magnitude.value * LiveMult(e)) + " "
                       + OrbStatName(e.orb) + " while a Staff is in hand (three orbs at most, the oldest drops)";
            case SkillEffectKind.Chill:
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(e.stacksPerApply.value));
                int ceiling = Mathf.RoundToInt(e.stackLimit.value);
                string cap = ceiling > 0 && ceiling < MarkMath.FreezeAt ? " (never past " + Hl(ceiling.ToString()) + ", so never a freeze)" : "";
                return "Adds " + Hl(n.ToString()) + " Chill for " + Hl(e.seconds.value.ToString("0.#") + "s") + cap
                       + " — each slows " + Pct(e.magnitude.value * LiveMult(e)) + ", " + Hl(MarkMath.FreezeAt.ToString())
                       + " freeze for " + Hl(MarkMath.FreezeSeconds.ToString("0") + "s") + ". Not on a burning target";
            }
            case SkillEffectKind.Static:
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(e.stacksPerApply.value));
                return "Adds " + Hl(n.ToString()) + " Static for " + Hl(e.seconds.value.ToString("0.#") + "s")
                       + " — each cuts attack speed " + Pct(e.magnitude.value * LiveMult(e)) + ", up to " + Hl(MarkMath.MaxStatic.ToString());
            }
            case SkillEffectKind.CopyMark:
                return e.markKind == MarkKind.Chill
                    ? "Carries the target's Chill to every enemy within " + Hl(e.copyRadius.value.ToString("0.#") + "m")
                      + ", never past its own count — each slows " + Pct(e.magnitude.value * LiveMult(e))
                    : "Copies the target's " + MarkName(e.markKind) + " to every enemy within " + Hl(e.copyRadius.value.ToString("0.#") + "m");
            case SkillEffectKind.ExtendMarks:
                return "Every " + MarkName(e.markKind) + " on the target lasts " + Hl(e.seconds.value.ToString("0.#") + "s")
                       + " longer" + (e.magnitude.value > 0f ? " and grows " + Pct(e.magnitude.value * LiveMult(e)) + " stronger" : "");
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
    /// <summary>What a strike's damage GROWS by, as the rules they are — ", +0.05× per metre to the target" and the
    /// rest — each said once, here, for the effect line and the skill card alike. <paramref name="withMaxMana"/> is
    /// false where the card has already folded the caster's max MP into the number it shows.</summary>
    static string GrowthNotes(SkillEffect e, bool withMaxMana)
    {
        string s = "";
        s += StackNote(e.damageMult);
        if (e.damageMultPerPointGivenUp.value > 0f)
            s += ", +" + Hl(e.damageMultPerPointGivenUp.value.ToString("0.###") + "×") + " per point of MP or HP given up";
        if (e.damageMultPerMetre.value > 0f)
            s += ", +" + Hl(e.damageMultPerMetre.value.ToString("0.###") + "×") + " per metre to the target";
        if (e.damageMultPerAccuracyOver.value > 0f)
            s += ", +" + Hl(e.damageMultPerAccuracyOver.value.ToString("0.###") + "×") + " per point of hit chance above 100% (up to " + StrikeMath.MaxAccuracyOverPoints + ")";
        if (e.critChanceBonus.value > 0f)
            s += ", +" + Pct(e.critChanceBonus.value) + " crit chance";
        if (e.critChanceBonusVsSlowed.value > 0f)
            s += ", +" + Pct(e.critChanceBonusVsSlowed.value) + " more crit chance against a slowed target";
        if (e.damageMultPerSpeedBonus.value > 0f)
            s += ", +" + Hl(e.damageMultPerSpeedBonus.value.ToString("0.###") + "×") + " per 1% movement speed bonus";
        if (e.damageMultPerBurnSecond.value > 0f)
            s += ", +" + Hl(e.damageMultPerBurnSecond.value.ToString("0.###") + "×") + " per second the target has been burning (up to "
                 + MarkMath.BurnAgeCap.ToString("0") + "s)";
        if (withMaxMana && e.damageMultPerMaxMana.value > 0f)
            s += ", +" + Hl(e.damageMultPerMaxMana.value.ToString("0.####") + "×") + " per point of your max MP (up to +"
                 + MarkMath.MaxManaGrowthCap.ToString("0") + "×)";
        return s;
    }

    /// <summary>What a strike does with a mark on its target — puts out a burn, scatters Chill, grounds Static, melts
    /// Stone, or counts one left in place — joined onto <paramref name="sofar"/>, the line it ends.</summary>
    static string MarkClause(SkillEffect e, string sofar)
    {
        string s = "";
        string per = Hl("+" + e.damageMultPerMark.value.ToString("0.##") + "×") + " " + DmgWord(e.damageType) + " damage";
        if (e.keepsMark && e.cashMark != MarkKind.None)
            s += ", " + per + " for every " + MarkName(e.cashMark) + " on the target (left in place)";
        else switch (e.cashMark)
        {
            case MarkKind.Burn:
                s += Joined(sofar + s, "Puts out the target's burn");
                if (e.damageMultPerMark.value > 0f) s += ": " + per + " per burn stack";
                if (e.burnPayoff.value > 0f) s += ", and the burn damage it had left lands at once, ×" + Hl(e.burnPayoff.value.ToString("0.##"));
                break;
            case MarkKind.Chill:
                s += Joined(sofar + s, "Scatters the target's Chill") + ": " + per + " per stack, a frozen target shatters as "
                     + Hl(MarkMath.FrozenCountsAs.ToString());
                break;
            case MarkKind.Static:
                s += Joined(sofar + s, "Grounds the target's Static") + ": " + per + " per stack";
                break;
            case MarkKind.Stone:
                s += Joined(sofar + s, "Melts the target's Stone") + ": " + per + " per charge";
                break;
        }
        return s;
    }

    static string KindTag(SkillDef def)
        => def.ItemUseEntry != null ? null : def.IsPassive ? "PASSIVE" : "ACTIVE";

    // A mark's name as a player reads it.
    static string MarkName(MarkKind m) => m == MarkKind.None ? "mark" : m.ToString();

    // A payoff part with no base of its own starts its own sentence; one that follows a hit is joined to it.
    static string Joined(string before, string clause) => before.Length == 0 ? clause : ". " + clause;

    // A STAFF COMBINATION has no levels of its own: every part grows with one of its orbs. Said per orb in the recipe —
    // the level the server sent, what each level adds, and each part it grows at the number the server worked out.
    static void OrbSection(SkillDef def, Dialect d)
    {
        var mastery = Character.Instance != null ? Character.Instance.Mastery : null;
        SkillLevelView view = default;
        bool hasView = mastery != null && mastery.SentView(def.id, out view);
        sb.Append('\n');
        int opensAt = Mathf.Max(1, def.masteryLevel);
        if (opensAt > 1)
            sb.Append("\nRequires ").Append(Met(WeaponTypes.Label(def.weaponType) + " Mastery " + opensAt,
                                                mastery != null ? mastery.SentLevel(def.weaponType) >= opensAt : (bool?)null, d));
        for (int o = 1; o <= 4; o++)
        {
            var orb = (OrbElement)o;
            if (def.orbCombo.IndexOf(OrbLetter(orb)) < 0) continue;
            string parts = OrbParts(def, orb, hasView ? view.now : null);
            if (parts.Length == 0) continue;
            sb.Append('\n').Append(Hl(orb + " Orb"));
            if (mastery != null) sb.Append(" level ").Append(Hl(mastery.LevelOf(OrbMath.SkillIdOf(orb)).ToString()));
            sb.Append(", ").Append(Hl("+" + (OrbMath.LevelPercent * 100f).ToString("0.##") + "%"))
              .Append(" a level to: ").Append(parts);
        }
    }

    static char OrbLetter(OrbElement orb)
    {
        switch (orb)
        {
            case OrbElement.Fire:  return 'F';
            case OrbElement.Earth: return 'E';
            case OrbElement.Wind:  return 'W';
            default:               return 'A';
        }
    }

    // The parts of a combination one orb grows, in a player's words: "damage, bonus per Stone".
    // Each part carries the number the server sent for it ("damage 2.56×") — one per raised part, in effect order
    // (SkillLevelNumbers.Collect).
    static string OrbParts(SkillDef def, OrbElement orb, float[] sentNow)
    {
        var parts = new System.Text.StringBuilder(48);
        int channel = 0;
        for (int b = 0; def.bindings != null && b < def.bindings.Count; b++)
        {
            var effects = def.bindings[b] != null ? def.bindings[b].effects : null;
            for (int k = 0; effects != null && k < effects.Count; k++)
            {
                var e = effects[k];
                if (e == null || !SkillLevelNumbers.MainNumber(e, out var number, out bool whole)) continue;
                int at = channel++;
                if (e.orb != orb) continue;
                string word = OrbPartWord(e);
                if (word == null) continue;
                if (parts.Length > 0) parts.Append(", ");
                parts.Append(word);
                if (sentNow != null && at < sentNow.Length) parts.Append(' ').Append(Hl(LevelNumber(e, number, whole, sentNow[at])));
            }
        }
        return parts.ToString();
    }

    static string OrbPartWord(SkillEffect e)
    {
        switch (e.kind)
        {
            case SkillEffectKind.Damage:
                if (e.cashMark == MarkKind.None) return "damage";
                if (e.keepsMark) return "damage, and the bonus per " + MarkName(e.cashMark);
                return e.damageMult.value > 0f ? "damage, and the bonus per " + MarkName(e.cashMark) + " cashed"
                                               : "the bonus per " + MarkName(e.cashMark) + " cashed";
            case SkillEffectKind.Chill:              return "how much its Chill slows";
            case SkillEffectKind.Static:             return "how much its Static cuts attack speed";
            case SkillEffectKind.CopyMark:           return "how much the carried Chill slows";
            case SkillEffectKind.ReduceOutgoingDamage: return "how much its Stone weakens blows";
            case SkillEffectKind.ExtendMarks:        return "how much stronger the lengthened " + MarkName(e.markKind) + " grows";
            case SkillEffectKind.DrainMana:          return "MP drained";
            default:                                 return MainNumberWord(e);
        }
    }

    // ---- learning and levels ----------------------------------------------------------------------------------

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
            case SkillEffectKind.BoostBasicAttackDamage:
            case SkillEffectKind.ReduceOutgoingDamage:
            case SkillEffectKind.Chill:
            case SkillEffectKind.Static:
            case SkillEffectKind.CopyMark:
            case SkillEffectKind.ExtendMarks:
                return FinePct(v);
            default:
                return whole && !authored.useStacks ? Mathf.RoundToInt(v).ToString() : v.ToString("0.##") + "×";
        }
    }

    // A percentage to one decimal — a level moves a 3% buff to 3.2%, which a whole-number percent would hide.
    static string FinePct(float f01) => (f01 * 100f).ToString("0.#") + "%";
}
