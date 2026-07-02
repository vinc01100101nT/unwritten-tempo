using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ONE place a data-authored <see cref="SkillEffect"/> (fired by a <see cref="SkillRunner"/> tick) becomes
/// real gameplay. It maps each intent onto the SAME combat sinks skills already call — a mob's <see cref="Health"/>,
/// the player's <see cref="Character"/>, the shared <see cref="Status"/> front door — so a data skill is just a
/// new CALLER of the existing damage/heal/status paths, never a second combat engine. That keeps ONE damage path
/// (the "one choke point" house rule): this class does no DEF/element/accuracy math of its own; it hands the roll
/// to the sinks that own it.
///
/// <para>Damage scales off the CASTER (<see cref="Character.Instance"/>) so a tick tracks the caster's stats +
/// crit like every other skill. Per-level growth (from <see cref="SkillLevelScale"/>) rides in as an extra
/// multiplier + flat bonus resolved by the caller — never a baked number in the asset.</para>
/// </summary>
public static class SkillEffectApplier
{
    /// <summary>Apply every effect ONCE to whoever a tick reaches: an AoE scan of the entity registries at
    /// <paramref name="point"/> (Area), or straight onto the cast's designated target (Single). This is the
    /// instantaneous "one burst per tick" — the repeat-over-time cadence lives in the VFX tick schedule, not here.
    /// Zero-alloc: a squared-distance sweep over the small Active list, no physics / Find / GetComponent.</summary>
    public static void ApplyBurst(Vector3 point, float radius, SkillScope scope, SkillTargets targets,
                                  List<SkillEffect> effects, Transform singleTarget,
                                  float extraMult = 0f, int extraFlat = 0)
    {
        if (targets == SkillTargets.None || effects == null || effects.Count == 0) return;

        // SINGLE scope: ignore the radius, land straight on the cast's designated unit (always lands). Falls back
        // to the Area scan if no target was set for the cast.
        if (scope == SkillScope.Single && singleTarget != null)
        {
            var mob = singleTarget.GetComponentInParent<MonsterAI3D>();
            if (mob != null)
            {
                if ((targets & SkillTargets.Mobs) != 0 && mob.Health != null && !mob.Health.IsDead)
                    for (int k = 0; k < effects.Count; k++) ApplyToMob(effects[k], mob, extraMult, extraFlat);
            }
            else if ((targets & SkillTargets.Player) != 0)
                for (int k = 0; k < effects.Count; k++) ApplyToPlayer(effects[k], extraMult, extraFlat);
            return;
        }

        float r2 = Mathf.Max(0.05f, radius); r2 *= r2;

        if ((targets & SkillTargets.Mobs) != 0)
        {
            var list = MonsterAI3D.Active;
            for (int i = 0; i < list.Count; i++)
            {
                var ai = list[i];
                if (ai == null || ai.Health == null || ai.Health.IsDead) continue;
                Vector3 d = ai.transform.position - point; d.y = 0f;
                if (d.sqrMagnitude > r2) continue;
                for (int k = 0; k < effects.Count; k++) ApplyToMob(effects[k], ai, extraMult, extraFlat);
            }
        }

        if ((targets & SkillTargets.Player) != 0)
        {
            var p = StatusController.Player;
            if (p != null && Character.Instance != null && !Character.Instance.IsDead)
            {
                Vector3 d = p.transform.position - point; d.y = 0f;
                if (d.sqrMagnitude <= r2)
                    for (int k = 0; k < effects.Count; k++) ApplyToPlayer(effects[k], extraMult, extraFlat);
            }
        }
    }

    /// <summary>Apply <paramref name="e"/> to a MOB via its cached <see cref="Health"/>.</summary>
    public static void ApplyToMob(SkillEffect e, MonsterAI3D ai, float extraMult = 0f, int extraFlat = 0)
    {
        if (e == null || ai == null) return;
        var h = ai.Health;
        if (h == null) return;
        switch (e.kind)
        {
            case SkillEffectKind.Damage:
                int power = RollPower(e, extraMult, extraFlat, out bool crit);
                if (e.magic) h.TakeMagicDamage(power, crit, e.element);
                else h.TakeDamage(power, crit, -1, e.element);   // −1 HIT = always-lands skill hit (no accuracy roll)
                break;
            case SkillEffectKind.Heal:
                h.Heal(Mathf.Max(0, e.amount));
                break;
            case SkillEffectKind.Status:
                Status.Apply(ai.gameObject, e.ailment, e.seconds, e.magnitude);
                break;
            case SkillEffectKind.Haste:
                var s = Status.Of(ai.gameObject);
                if (s != null) s.Haste(e.magnitude, e.seconds);
                break;
        }
    }

    /// <summary>Apply <paramref name="e"/> to the PLAYER (<see cref="Character.Instance"/>).</summary>
    public static void ApplyToPlayer(SkillEffect e, float extraMult = 0f, int extraFlat = 0)
    {
        var ch = Character.Instance;
        if (e == null || ch == null) return;
        switch (e.kind)
        {
            case SkillEffectKind.Damage:
                int power = RollPower(e, extraMult, extraFlat, out _);
                if (e.magic) ch.TakeMagicDamage(power, e.element);
                else ch.TakeDamage(power, -1, e.element);        // −1 HIT = always-lands (skill/environmental hit)
                break;
            case SkillEffectKind.Heal:
                ch.Heal(Mathf.Max(0, e.amount));
                break;
            case SkillEffectKind.Status:
                if (StatusController.Player != null) StatusController.Player.Apply(e.ailment, e.seconds, e.magnitude);
                break;
            case SkillEffectKind.Haste:
                if (StatusController.Player != null) StatusController.Player.Haste(e.magnitude, e.seconds);
                break;
        }
    }

    // Damage value = the caster's rolled hit × (the effect's multiplier + per-level growth) + a per-level flat
    // bonus. Sourced from the player (the only caster in the current slice); the crit-aware rolls mean a tick crits
    // like any other skill. No allocation.
    static int RollPower(SkillEffect e, float extraMult, int extraFlat, out bool crit)
    {
        crit = false;
        var ch = Character.Instance;
        if (ch == null) return 1;
        float mult = (e.damageMult <= 0f ? 1f : e.damageMult) + Mathf.Max(0f, extraMult);
        int roll = e.magic ? ch.RollMagicDamage(out crit) : ch.RollDamage(out crit);
        return Mathf.Max(1, Mathf.RoundToInt(roll * mult) + Mathf.Max(0, extraFlat));
    }
}
