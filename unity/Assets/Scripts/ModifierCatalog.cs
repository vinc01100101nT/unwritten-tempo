using System.Collections.Generic;

/// <summary>
/// The authored pool of skill modifiers — the CONTENT layer of the modifier system. Every entry is a
/// loose data row reusable across all base skills; nothing here is combined or enumerated per skill
/// (Charter §8). Each phase carries a few options, tagged so the eligibility filter draws the right
/// subset per skill: a melee skill and a magic skill end up with DIFFERENT pools from the same catalog.
/// "Mending" is the deliberate exclusion proof — support-only, so it can never roll onto an attack skill.
///
/// Pure data + filtering, no Unity objects — so the editor self-test exercises it with no Play mode.
/// </summary>
public static class ModifierCatalog
{
    // The authored pool (Active Skill Roster v1). All 14 original ids are preserved for save compatibility;
    // damage / targeting / on-hit / lifesteal mods now require Offense so a self-buff (no Offense tag) can
    // never roll a no-op effect (Charter §12: tags compute legality, no mod should be a silent dud).
    public static readonly SkillModifier[] All =
    {
        // ---- Cast (how it goes off) ----
        new SkillModifier("quickened",   "Quickened",     SkillPhase.Cast,       ModEffect.QuickenCooldown, p0: 0.7f,
                          requires: new[] { SkillTag.Offense }),
        new SkillModifier("overcharged", "Overcharged",   SkillPhase.Cast,       ModEffect.QuickenCooldown, p0: 0.55f,
                          requires: new[] { SkillTag.Offense }),
        new SkillModifier("relentless",  "Relentless",    SkillPhase.Cast,       ModEffect.QuickenCooldown, p0: 0.8f,
                          requires: new[] { SkillTag.Melee }),
        new SkillModifier("overflow",    "Overflow",      SkillPhase.Cast,       ModEffect.QuickenCooldown, p0: 0.6f,
                          requires: new[] { SkillTag.Magic }),

        // ---- Targeting (where it lands) ----
        new SkillModifier("long_reach",  "Long Reach",    SkillPhase.Targeting,  ModEffect.ExtendReach,     p0: 1.5f,
                          requires: new[] { SkillTag.Melee, SkillTag.Offense }),
        new SkillModifier("piercing",    "Piercing",      SkillPhase.Targeting,  ModEffect.ExtendReach,     p0: 1.7f,
                          requires: new[] { SkillTag.Melee, SkillTag.Offense }),
        new SkillModifier("cleaving",    "Cleaving",      SkillPhase.Targeting,  ModEffect.Cleave,          p0: 2f, p1: 2.2f,
                          requires: new[] { SkillTag.Melee, SkillTag.Offense }, excludes: new[] { SkillTag.Aoe }),
        new SkillModifier("wide_arc",    "Wide Arc",      SkillPhase.Targeting,  ModEffect.Cleave,          p0: 1f, p1: 3.0f,
                          requires: new[] { SkillTag.Melee, SkillTag.Offense }, excludes: new[] { SkillTag.Aoe }),
        new SkillModifier("far_cast",    "Far Cast",      SkillPhase.Targeting,  ModEffect.ExtendReach,     p0: 1.4f,
                          requires: new[] { SkillTag.Ranged, SkillTag.Offense }),

        // ---- Impact (the hit math) ----
        new SkillModifier("crushing",    "Crushing Blow", SkillPhase.Impact,     ModEffect.AmplifyDamage,   p0: 1.4f,
                          requires: new[] { SkillTag.Offense }),
        new SkillModifier("heavy",       "Heavy",         SkillPhase.Impact,     ModEffect.AmplifyDamage,   p0: 1.7f,
                          requires: new[] { SkillTag.Melee, SkillTag.Offense }),
        new SkillModifier("flaming",     "Flaming",       SkillPhase.Impact,     ModEffect.ConvertElement,  element: Element.Fire,
                          requires: new[] { SkillTag.Offense }),
        new SkillModifier("freezing",    "Freezing",      SkillPhase.Impact,     ModEffect.ConvertElement,  element: Element.Water,
                          requires: new[] { SkillTag.Offense }),
        new SkillModifier("gale",        "Gale",          SkillPhase.Impact,     ModEffect.ConvertElement,  element: Element.Wind,
                          requires: new[] { SkillTag.Offense }),
        new SkillModifier("precise",     "Precise",       SkillPhase.Impact,     ModEffect.GuaranteedCrit,
                          requires: new[] { SkillTag.Melee, SkillTag.Offense }),
        new SkillModifier("flurry",      "Flurry",        SkillPhase.Impact,     ModEffect.MultiHit,        p0: 2f,
                          requires: new[] { SkillTag.Melee, SkillTag.Offense }, excludes: new[] { SkillTag.Aoe }),

        // ---- OnHit (what it does to the target) ----
        new SkillModifier("concussive",  "Concussive",    SkillPhase.OnHit,      ModEffect.ProlongStagger,  p0: 0.7f,
                          requires: new[] { SkillTag.Offense }),
        new SkillModifier("shattering",  "Shattering",    SkillPhase.OnHit,      ModEffect.ProlongStagger,  p0: 1.0f,
                          requires: new[] { SkillTag.Melee, SkillTag.Offense }),
        new SkillModifier("chilling",    "Chilling",      SkillPhase.OnHit,      ModEffect.ApplySlow,       p0: 0.5f, p1: 2.5f,
                          requires: new[] { SkillTag.Offense }),
        new SkillModifier("withering",   "Withering",     SkillPhase.OnHit,      ModEffect.ApplySlow,       p0: 0.3f, p1: 3.0f,
                          requires: new[] { SkillTag.Offense }),
        new SkillModifier("repelling",   "Repelling",     SkillPhase.OnHit,      ModEffect.Knockback,       p0: 13f, p1: 0.22f,
                          requires: new[] { SkillTag.Melee, SkillTag.Offense }),
        new SkillModifier("entangling",  "Entangling",    SkillPhase.OnHit,      ModEffect.Root,            p0: 1.5f,
                          requires: new[] { SkillTag.Melee, SkillTag.Offense }),

        // ---- SelfEffect (what it does to YOU) ----
        new SkillModifier("bloodthirst", "Bloodthirst",   SkillPhase.SelfEffect, ModEffect.LifestealOnHit,  p0: 0.25f,
                          requires: new[] { SkillTag.Melee, SkillTag.Offense }),
        new SkillModifier("vampiric",    "Vampiric",      SkillPhase.SelfEffect, ModEffect.LifestealOnHit,  p0: 0.35f,
                          requires: new[] { SkillTag.Melee, SkillTag.Offense }),
        new SkillModifier("siphon",      "Siphon",        SkillPhase.SelfEffect, ModEffect.LifestealOnHit,  p0: 0.2f,
                          requires: new[] { SkillTag.Magic, SkillTag.Offense }),
        new SkillModifier("bulwark",     "Bulwark",       SkillPhase.SelfEffect, ModEffect.SelfShield,      p0: 0.4f, p1: 2.5f,
                          requires: new[] { SkillTag.Melee }),
        new SkillModifier("mending",     "Mending",       SkillPhase.SelfEffect, ModEffect.LifeOnCast,      p0: 0.1f,
                          requires: new[] { SkillTag.Support }),   // the exclusion proof — never rolls onto an attack skill

        // ---- Proc (conditional riders) ----
        new SkillModifier("momentum",    "Momentum",      SkillPhase.Proc,       ModEffect.EveryNthFree,    p0: 3f,
                          requires: new[] { SkillTag.Offense }),
        new SkillModifier("second_wind", "Second Wind",   SkillPhase.Proc,       ModEffect.EveryNthFree,    p0: 5f,
                          requires: new[] { SkillTag.Offense }),
        new SkillModifier("rhythm",      "Rhythm",        SkillPhase.Proc,       ModEffect.EveryNthFree,    p0: 4f,
                          requires: new[] { SkillTag.Melee }),
    };

    // Reused scratch — the roll runs only on level-up (not a hot path), but keeping it alloc-free is cheap.
    static readonly List<SkillModifier> scratch = new List<SkillModifier>(8);

    /// <summary>Look a modifier up by id (for restoring a saved loadout). Null if not found.</summary>
    public static SkillModifier ById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < All.Length; i++) if (All[i].Id == id) return All[i];
        return null;
    }

    /// <summary>Does the skill have at least one legal modifier for this phase? (A phase with none can
    /// never fill, so the loadout must skip it instead of looping forever trying.)</summary>
    public static bool HasEligible(IReadOnlyList<string> skillTags, SkillPhase phase)
    {
        for (int i = 0; i < All.Length; i++)
        {
            var m = All[i];
            if (m.Phase == phase && m.EligibleFor(skillTags)) return true;
        }
        return false;
    }

    /// <summary>Pick a uniformly-random legal modifier for <paramref name="phase"/> on a skill carrying
    /// <paramref name="skillTags"/>. Returns null only if the phase has no eligible modifier.</summary>
    public static SkillModifier RollFor(IReadOnlyList<string> skillTags, SkillPhase phase, System.Random rng)
    {
        scratch.Clear();
        for (int i = 0; i < All.Length; i++)
        {
            var m = All[i];
            if (m.Phase == phase && m.EligibleFor(skillTags)) scratch.Add(m);
        }
        if (scratch.Count == 0) return null;
        return scratch[rng.Next(scratch.Count)];
    }
}
