using System.Collections.Generic;
using UnityEngine;

/// <summary>What one damage line on a skill card shows (<see cref="SkillCardNumbers"/>).</summary>
public enum SkillCardShape
{
    /// <summary>Not a damage line: the card draws the growing number the level view already carries.</summary>
    Level = 0,
    /// <summary>The damage a strike lands, as the band its roll falls in.</summary>
    Band = 1,
    /// <summary>A pay-off part: the damage it adds for each mark on the target, as a band. With no mark it lands nothing.</summary>
    BandPerMark = 2,
    /// <summary>A strike that always crits: one number.</summary>
    CritOnly = 3,
    /// <summary>A burn put out: the burn's remaining damage times the number, which reads no attack at all.</summary>
    BurnPayoff = 4,
}

/// <summary>
/// THE SKILL CARD'S FINISHED NUMBERS, worked out on the server for the skills of the weapon in hand, so the card shows
/// what this character lands right now (CLAUDE.md, RULE NOCLIENTMATH): every damage line as the band the fight rolls
/// it in, its crit, the same band at the next level and with no timed effects on the caster, and the cast time with
/// what each source cut from it. A number that depends on the TARGET or on the fight (marks carried, metres, hit chance
/// over the target's evasion, a pile) is not folded in — the card states those as the rules they are.
///
/// <para>Per skill: <see cref="CastFloats"/> numbers — the wind-up now, without timed effects, then what DEX, the ring
/// and weapon mastery, and the timed effects each changed it by (negative = shorter). Per growing number, in
/// <see cref="SkillLevelNumbers"/>' order, <see cref="LineInts"/> numbers — see <see cref="Field"/>.</para>
/// </summary>
public static class SkillCardNumbers
{
    public const int CastFloats = 5;
    public const int LineInts = 11;

    /// <summary>The place of each number in one line.</summary>
    public static class Field
    {
        public const int Shape = 0, Element = 1, Min = 2, Max = 3, NextMin = 4, NextMax = 5,
                         PlainMin = 6, PlainMax = 7, TimedMin = 8, TimedMax = 9, Crit = 10;
    }

    static readonly float[] orbLifts = new float[5];

    /// <summary>SERVER: append the card numbers of every skill of <paramref name="owner"/>'s weapon in hand.
    /// <paramref name="now"/> is the owner's stats with their timed effects (<see cref="Character.DerivedWithStates"/>),
    /// <paramref name="plain"/> without (<see cref="Character.BaseDerived"/>). Allocation-free into the caller's lists.</summary>
    public static void Capture(Character owner, in DerivedSnapshot now, in DerivedSnapshot plain,
                               List<string> ids, List<float> cast, List<int> counts, List<int> lines)
    {
        if (owner == null) return;
        var held = owner.HeldWeapon;
        if (held == WeaponType.None) return;
        bool magicShot = WeaponTypes.ShootsMagic(held);
        var mastery = owner.Mastery;
        var all = SkillCatalog.All;
        for (int i = 0; i < all.Count; i++)
        {
            var def = all[i];
            if (def == null || string.IsNullOrEmpty(def.id) || def.weaponType != held || def.IsCompanion) continue;
            ids.Add(def.id);

            float a0 = Mathf.Max(0f, def.castTime), fixedT = Mathf.Max(0f, def.fixedCastTime);
            owner.CastSteps(def.castTime, out float a1, out float a2, out float a3);
            cast.Add(a3 + fixedT);
            cast.Add(a2 + fixedT);
            cast.Add(a1 - a0);
            cast.Add(a2 - a1);
            cast.Add(a3 - a2);

            int bought = mastery.LevelOf(def.id);
            float trademark = owner.TrademarkLift(def.id);
            float liftNow = MasteryMath.LevelLift(def, bought) + trademark;
            float liftNext = MasteryMath.LevelLift(def, Mathf.Min(bought + 1, def.maxLevel)) + trademark;
            float[] orbs = def.IsOrbCombo ? mastery.OrbLifts(owner, true, orbLifts) : null;

            int before = lines.Count;
            for (int b = 0; def.bindings != null && b < def.bindings.Count; b++)
            {
                var fx = def.bindings[b]?.effects;
                if (fx == null) continue;
                for (int k = 0; k < fx.Count; k++)
                {
                    var e = fx[k];
                    if (!SkillLevelNumbers.MainNumber(e, out var number, out _)) continue;
                    bool byOrb = orbs != null && e.orb != OrbElement.None && e.kind != SkillEffectKind.AddOrb;
                    AddLine(e, number, owner, magicShot, now, plain,
                            byOrb ? orbs[(int)e.orb] : liftNow, byOrb ? orbs[(int)e.orb] : liftNext, lines);
                }
            }
            counts.Add((lines.Count - before) / LineInts);
        }
    }

    static void AddLine(SkillEffect e, ScaledNumber number, Character owner, bool magicShot,
                        in DerivedSnapshot now, in DerivedSnapshot plain, float liftNow, float liftNext, List<int> lines)
    {
        var shape = SkillCardShape.Level;
        int element = 0, min = 0, max = 0, nextMin = 0, nextMax = 0, plainMin = 0, plainMax = 0, crit = 0;
        if (e.kind == SkillEffectKind.Damage)
        {
            bool payoffOnly = e.cashMark != MarkKind.None && e.damageMult.value <= 0f;
            if (payoffOnly && e.cashMark == MarkKind.Burn && e.damageMultPerMark.value <= 0f)
                shape = SkillCardShape.BurnPayoff;
            else
            {
                bool basic = e.resolveAsBasicAttack;
                bool physical = basic ? !magicShot : e.damageType != DamageType.Magic;
                element = (int)(basic ? owner.BasicElement : e.element);
                // What a strike's own multiplier grows by from the CASTER alone — max MP — is folded in, exactly as the
                // fight adds it before the roll meets the multiplier. Growth that reads the target is not.
                float mult = number.value + (payoffOnly ? 0f : MarkMath.MaxManaGrowth(e.damageMultPerMaxMana.value, owner.MaxMP));
                int hits = e.kind == SkillEffectKind.Damage ? SkillEffectApplier.EffectiveHits(e, owner) : 1;
                float scaleNow = SkillEffectApplier.OutputScale(e, owner, liftNow);
                float scaleNext = SkillEffectApplier.OutputScale(e, owner, liftNext);
                int lo = physical ? now.atkMin : now.matkMin, hi = physical ? now.atkMax : now.matkMax;
                int plo = physical ? plain.atkMin : plain.matkMin, phi = physical ? plain.atkMax : plain.matkMax;
                min = Strike(lo, mult, scaleNow, hits);
                max = Strike(hi, mult, scaleNow, hits);
                nextMin = Strike(lo, mult, scaleNext, hits);
                nextMax = Strike(hi, mult, scaleNext, hits);
                plainMin = Strike(plo, mult, scaleNow, hits);
                plainMax = Strike(phi, mult, scaleNow, hits);
                // A physical crit lands the top of the roll times the crit multiplier, past the band.
                if (physical) crit = Strike(Mathf.RoundToInt(hi * now.critMult), mult, scaleNow, hits);
                shape = payoffOnly ? SkillCardShape.BandPerMark
                      : !basic && e.damageType == DamageType.Crit ? SkillCardShape.CritOnly
                      : SkillCardShape.Band;
            }
        }
        lines.Add((int)shape); lines.Add(element);
        lines.Add(min); lines.Add(max); lines.Add(nextMin); lines.Add(nextMax);
        lines.Add(plainMin); lines.Add(plainMax); lines.Add(min - plainMin); lines.Add(max - plainMax);
        lines.Add(crit);
    }

    /// <summary>One strike's damage from one roll: the roll times the multiplier times the ring-and-level scale, rounded as
    /// the fight rounds a hit, times the hits a multi-hit is shown as (the fight lands the whole blow at once).</summary>
    public static int Strike(int roll, float mult, float scale, int hits) =>
        Mathf.Max(0, Mathf.RoundToInt(roll * mult * scale)) * Mathf.Max(1, hits);
}
