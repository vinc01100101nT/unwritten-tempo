using UnityEngine;

/// <summary>
/// The RING's behavioral layer — "each ring stat speaks through your skills with its own verb" (design doc
/// "Active Skill System — V2", the Voice grammar). This is the ONE place a ring stat maps to a behavioral
/// twist, so the voice table lives in a single readable spot instead of scattered across the cast path.
///
/// <para>Slice 1 — three SELF voices, applied once per cast, reusing the LIVE combat sinks (no dead SkillMod
/// vessel, zero allocation):
///   • <b>DEF → Fortify</b>      — each cast briefly shields the caster (<see cref="Character.BeginGuard"/>).
///   • <b>HP → Leech</b>         — each cast starts a short heal-over-time (<see cref="Character.BeginRegen"/>).
///   • <b>CastSpeed → Accelerate</b> — each cast briefly quickens your casting (<see cref="Character.BuffCastSpeed"/>).
/// A voice stays silent until you invest past <see cref="ActivateAt"/> in its stat, and ALL voices go quiet with
/// no ring worn (<see cref="Character.RingBonus"/> returns 0 unequipped). The OUTGOING-attack reshapers (Critical→
/// Execute finisher, AttackSpeed→Repeat extra hits, MATK→Expand) inject effects into the hit and land in Slice 2,
/// where they need a zero-alloc injection into <see cref="SkillEffectApplier"/> — not a per-cast list.</para>
/// </summary>
public static class RingVoices
{
    // A voice stays silent below this much ring investment in its stat (fraction, 0.05 = 5%). Low so it's easy
    // to cross in a playtest; tune once it feels right.
    const float ActivateAt = 0.05f;

    // DEF · Fortify — guard strength = your Def investment (capped), for this long after a cast.
    const float FortifyMaxReduction = 0.5f;
    const float FortifySeconds = 3f;

    // HP · Leech — heal this fraction of Max HP per regen tick, for this long, after a cast.
    const float LeechMaxHpPerTick = 0.02f;
    const float LeechSeconds = 4f;

    // CastSpeed · Accelerate — cast-speed multiplier (>1 = faster) = 1 + your CastSpeed investment, for this long.
    const float AccelerateSeconds = 4f;

    // Critical · Execute — finisher power added per point of Crit investment (folds into CastMods.executePower).
    const float ExecutePerCrit = 1f;

    /// <summary>Fire the self-voices for one cast. Called from the single cast choke (<see cref="SkillRunner"/>).
    /// Safe to call on every skill: each voice self-gates on its own ring investment, so nothing happens for a
    /// stat you haven't invested in (or with no ring worn). Zero allocation — all direct calls into live sinks.</summary>
    public static void ApplyCast(Character ch)
    {
        if (ch == null) return;

        // DEF · Fortify — a brief damage-cut shield whose strength scales with Def investment.
        float def = ch.RingBonus(StatId.Def);
        if (def >= ActivateAt)
            ch.BeginGuard(FortifySeconds, Mathf.Min(FortifyMaxReduction, def));

        // HP · Leech — a short heal-over-time sized off Max HP.
        float hp = ch.RingBonus(StatId.HP);
        if (hp >= ActivateAt)
            ch.BeginRegen(Mathf.Max(1, Mathf.RoundToInt(ch.MaxHP * LeechMaxHpPerTick)), LeechSeconds);

        // CastSpeed · Accelerate — quicken subsequent casts, deeper investment = faster.
        float cast = ch.RingBonus(StatId.CastSpeed);
        if (cast >= ActivateAt)
            ch.BuffCastSpeed(1f + cast, AccelerateSeconds);
    }

    /// <summary>Fold the ring's OUTGOING voices into a cast's reshapers, layered ON TOP of whatever the weapon's
    /// affixes already added (call weapon first, then this — the design's fold order). These reshape the hit
    /// itself; the self-voices above buff the caster. Each self-gates on its stat; zero-alloc.</summary>
    public static void AddOutgoing(Character ch, ref CastMods mods)
    {
        if (ch == null) return;

        // Critical · Execute — a low-HP finisher, its power scaling with Crit investment.
        float crit = ch.RingBonus(StatId.Critical);
        if (crit >= ActivateAt) mods.executePower += ExecutePerCrit * crit;

        // MATK · Expand — a wider area, scaling with MAtk investment.
        float matk = ch.RingBonus(StatId.MAtk);
        if (matk >= ActivateAt) mods.radiusBonus += matk;

        // AttackSpeed · Repeat — one extra damage echo once you've invested.
        float aspd = ch.RingBonus(StatId.AttackSpeed);
        if (aspd >= ActivateAt) mods.extraHits += 1;
    }

    /// <summary>One read-only reference row per ring voice (stat, verb name, scope, human effect, activation %) — the
    /// data source for the editor Affix &amp; Ring Voice Table. Effect text is built from the SAME tuning consts the
    /// live voices use, so the reference view can't drift from the real numbers. Editor/author-time only (allocs).</summary>
    public readonly struct VoiceRow
    {
        public readonly string Stat, Name, Scope, Effect;
        public readonly float ActivatePercent;
        public VoiceRow(string stat, string name, string scope, string effect, float activate)
        { Stat = stat; Name = name; Scope = scope; Effect = effect; ActivatePercent = activate; }
    }

    public static VoiceRow[] AllVoices()
    {
        float a = ActivateAt * 100f;
        return new[]
        {
            new VoiceRow("DEF", "Fortify", "Self · every cast",
                $"Shield up to {FortifyMaxReduction * 100f:0}% damage-cut for {FortifySeconds:0.#}s (strength = Def invest)", a),
            new VoiceRow("HP", "Leech", "Self · every cast",
                $"Regen {LeechMaxHpPerTick * 100f:0}% of Max HP per tick for {LeechSeconds:0.#}s", a),
            new VoiceRow("CastSpeed", "Accelerate", "Self · every cast",
                $"+(CastSpeed invest)% cast speed for {AccelerateSeconds:0.#}s", a),
            new VoiceRow("Critical", "Execute", "Outgoing · damage skills only",
                $"Low-HP finisher, power = {ExecutePerCrit:0.#} × Crit invest", a),
            new VoiceRow("MATK", "Expand", "Outgoing · mob-area skills only",
                "+(MAtk invest)% area radius", a),
            new VoiceRow("AttackSpeed", "Repeat", "Outgoing · damage skills only",
                "+1 extra damage echo (hit) — hidden on no-damage skills like Blink", a),
        };
    }

    /// <summary>Append a live, human-readable line per ACTIVE ring voice (reads current investment, so a tooltip
    /// that calls this is always up to date). Self-gated: only voices past their threshold show. Rich-text bold
    /// on the numbers; the caller owns the surrounding section header.</summary>
    public static void AppendSummary(Character ch, System.Text.StringBuilder sb, SkillDef def)
    {
        if (ch == null) return;
        bool damagesMobs = SkillReshape.DamagesMobs(def);
        bool hasMobArea = SkillReshape.HasMobArea(def);
        // Self-voices fire on ANY cast, so they always show.
        float defRing = ch.RingBonus(StatId.Def);
        if (defRing >= ActivateAt)
            sb.Append("\n• <b>DEF · Fortify</b> — shield <b>").Append(Mathf.RoundToInt(Mathf.Min(FortifyMaxReduction, defRing) * 100f))
              .Append("%</b> for <b>").Append(FortifySeconds.ToString("0.#")).Append("s</b> on cast");
        float hp = ch.RingBonus(StatId.HP);
        if (hp >= ActivateAt)
            sb.Append("\n• <b>HP · Leech</b> — regen <b>").Append(Mathf.Max(1, Mathf.RoundToInt(ch.MaxHP * LeechMaxHpPerTick)))
              .Append("</b> HP/tick for <b>").Append(LeechSeconds.ToString("0.#")).Append("s</b> on cast");
        float cast = ch.RingBonus(StatId.CastSpeed);
        if (cast >= ActivateAt)
            sb.Append("\n• <b>CastSpeed · Accelerate</b> — <b>+").Append(Mathf.RoundToInt(cast * 100f)).Append("%</b> cast speed on cast");
        // Outgoing voices only show when THIS skill can actually use them.
        float crit = ch.RingBonus(StatId.Critical);
        if (crit >= ActivateAt && damagesMobs)
            sb.Append("\n• <b>Crit · Execute</b> — low-HP finisher, power <b>").Append((ExecutePerCrit * crit).ToString("0.##")).Append("</b>");
        float matk = ch.RingBonus(StatId.MAtk);
        if (matk >= ActivateAt && hasMobArea)
            sb.Append("\n• <b>MATK · Expand</b> — <b>+").Append(Mathf.RoundToInt(matk * 100f)).Append("%</b> area");
        float aspd = ch.RingBonus(StatId.AttackSpeed);
        if (aspd >= ActivateAt && damagesMobs)
            sb.Append("\n• <b>AttackSpeed · Repeat</b> — <b>+1</b> hit echo");
    }
}
