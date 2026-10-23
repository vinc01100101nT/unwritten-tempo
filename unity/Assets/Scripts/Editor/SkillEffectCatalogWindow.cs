using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Read-only reference for the skill authoring palette (design doc "Active Skill System — V3"). Two tables:
/// the CHANNELS catalog (the numeric outputs, split into effect-output vs skill-delivery tiers) and the EFFECTS
/// catalog (every <see cref="SkillEffectKind"/> with the channels it owns, whether the ring scales them, its combat
/// sink, and details). The effect list is driven off the real enum via <see cref="Enum.GetValues(Type)"/>, so a newly
/// added kind that lacks a catalog row is flagged (⚠) instead of silently missing — the table can't drift from code.
/// NOTE: until a formal Channels-catalog data type exists (see V3 "Implementation Notes"), the channel + per-effect
/// rows below are the interim editor-side catalog, hand-verified against SkillEffect.cs / SkillEffectApplier.cs.</summary>
public class SkillEffectCatalogWindow : EditorWindow
{
    const string MenuPath = "Tools/unwritten/Catalogs/Skill Channels & Effects";

    [MenuItem(MenuPath)]
    static void Open() => GetWindow<SkillEffectCatalogWindow>("Skill Catalogs").Show();

    Vector2 scroll;
    GUIStyle wrap, head;

    // ---- CHANNELS catalog (the numeric outputs). Tier A = effect-output (attaches per-effect, what the ring scales);
    //      Tier B = skill/delivery (owned once per skill or tick). "ring?" = is it affinity-scalable today. --------------
    struct Chan { public string id, label, unit, tier, ring, notes; }
    static readonly Chan[] Channels =
    {
        C("damage",         "Damage",           "mult",   "effect-output", "yes",  "ATK/MATK multiplier; final = the caster's ATK/MATK roll × this × affinity"),
        C("hits",           "Hits",             "int",    "effect-output", "yes",  "strikes per landing; each is a proc trigger (hitCountAffinity)"),
        C("heal_amount",    "Heal Amount",      "HP (or a range)","effect-output", "yes",  "Heal amount (sustain: HP/MP/Def)"),
        C("heal_per_tick",  "Heal / sec",       "HP",     "effect-output", "yes",  "Regen HoT, every 0.8s"),
        C("shield_amount",  "Barrier HP",       "flat HP","effect-output", "yes",  "Shield absorb pool (sustain)"),
        C("dmg_reduction",  "Damage Reduction", "0..1",   "effect-output", "yes",  "Guard cut fraction (clamped to 1)"),
        C("dmg_taken_frac", "Damage Taken",     "frac",   "effect-output", "yes",  "Vulnerability extra dmg"),
        C("dot_damage",     "Damage / Tick",    "HP",     "effect-output", "yes",  "Status DoT (Poison/Bleed/Burn)"),
        C("slow_amount",    "Slow Amount",      "0..1",   "effect-output", "yes",  "Status Slow speed factor"),
        C("accuracy_loss",  "Accuracy Loss",    "HIT",    "effect-output", "yes",  "Status Blind"),
        C("move_mult",      "Move Speed",       "mult",   "effect-output", "yes",  "Haste (>1 faster; mobs debuff <1)"),
        C("shove_speed",    "Shove Speed",      "u/s",    "effect-output", "no",   "Knockback / Pull — magnitude is raw (no channel shown)"),
        C("leech_frac",     "MP Leech",         "frac",   "effect-output", "no",   "ManaLeech — raw"),
        C("reflect_frac",   "Reflect",          "frac",   "effect-output", "no",   "Reflect — raw"),
        C("execute_scale",  "Execute Power",    "mult",   "effect-output", "part", "Execute; the base roll scales, this multiplier is raw"),
        C("buff_mult",      "Buff Amount",      "mult",   "effect-output", "NO",   "Buff — FLAT by design (support value = uptime, not size)"),
        C("proc_chance",    "Proc Chance",      "0..1",   "effect-output", "no",   "per-hit roll for a Status/Execute proc — never scaled"),
        C("duration",       "Duration",         "sec",    "effect-output", "yes",  "window length (durationAffinity, separate list)"),
        C("cost",           "Mana Cost",        "MP",     "skill-delivery","—",    "SkillDef.manaCost"),
        C("cooldown",       "Cooldown",         "sec",    "skill-delivery","—",    "SkillDef.cooldown"),
        C("cast_time",      "Cast Time",        "sec",    "skill-delivery","—",    "SkillDef.castTime (DEX shortens at runtime)"),
        C("fixed_cast_time","Fixed Cast Time",  "sec",    "skill-delivery","—",    "SkillDef.fixedCastTime (nothing shortens it)"),
        C("range",          "Range",            "u",      "skill-delivery","—",    "SkillDef.castRange"),
        C("radius",         "Radius",           "u",      "skill-delivery","—",    "SkillTickBinding.radius (AoE / hit width)"),
        C("projectiles",    "Projectiles / Spread","int/°","skill-delivery","—",   "SkillTickBinding.projectileCount / projectileSpread"),
        C("cycle",          "Repeat",           "int/sec","skill-delivery","—",    "SkillDef.repeatCount / repeatInterval"),
    };
    static Chan C(string id, string label, string unit, string tier, string ring, string notes)
        => new Chan { id = id, label = label, unit = unit, tier = tier, ring = ring, notes = notes };

    // ---- EFFECTS catalog. Keyed by the real enum; "scales" = what the RING actually grows (verified against the
    //      applier, NOT just the composer label). ⚠ marks an effect whose composer shows a scaling channel the runtime
    //      ignores (a dead knob to resolve). ---------------------------------------------------------------------------
    struct Eff { public string target, dmg, channels, scales, sink, details; }
    static readonly Dictionary<SkillEffectKind, Eff> Effects = new Dictionary<SkillEffectKind, Eff>
    {
        [SkillEffectKind.Damage]        = E("mobs/any", "yes", "damage, hits", "damage, hits", "CombatAuthority.Apply", "The caster's ATK/MATK roll × the effect's ATK/MATK multiplier × affinity; each hit triggers the binding's procs."),
        [SkillEffectKind.DrainMana]     = E("mobs/any", "no", "mp_drained (amount), proc_chance", "mp_drained", "ICombatUnit.SpendMana + GainMana", "Takes up to N MP from the unit it lands on (never more than it holds); the caster gains exactly what was taken."),
        [SkillEffectKind.SpendAllMana]  = E("self", "no", "—", "—", "SkillEffectApplier.PayAllInCosts", "The caster spends every point of MP held. Paid once per cast; a Damage effect later in the cast grows by its 'damage mult per point given up'."),
        [SkillEffectKind.DropHealthToOne] = E("self", "no", "—", "—", "ICombatUnit.GiveUpHealthDownToOne", "The caster's HP drops to 1 (never 0); not a hit. Paid once per cast; a Damage effect later in the cast grows by its 'damage mult per point given up'."),
        [SkillEffectKind.Heal]          = E("self/others","no", "heal_amount", "heal_amount", "Health/Character.Heal", "HP restored, fixed or a range rolled per heal; landing follows the tick's TargetMode."),
        [SkillEffectKind.Status]        = E("mobs/self", "no", "ailment; dot_damage, duration, proc_chance", "output + duration (proc_chance never)", "StatusController.Apply", "Only the DoTs carry a number now; Stun/Freeze/Silence are duration-only. Slow and Blind moved to the sub-stat debuff."),
        [SkillEffectKind.Taunt]         = E("mobs", "no", "duration", "duration", "MonsterAI3D.Taunt", "Forces mob aggro onto the caster."),
        [SkillEffectKind.Teleport]      = E("self (point)","no", "—", "—", "Player3DController.Teleport", "Instant blink to the aimed, range-clamped point."),
        [SkillEffectKind.SubStatBuff]   = E("self/mobs", "no", "amount (FLAT), duration", "duration only", "UnitStates.ApplySubStat", "subStat picks WHICH sub-stat rises. THE one path for every timed sub-stat change — move speed, both regens, leech, reflect, evasion, all of them."),
        [SkillEffectKind.SubStatDebuff] = E("self/mobs", "no", "amount (FLAT), duration", "duration only", "UnitStates.ApplySubStat", "The mirror of SubStatBuff: same stat list, authored as a positive amount, applied as a drop."),
        [SkillEffectKind.Cleanse]       = E("self", "no", "debuffs_removed (amount)", "—", "StatusController.Cleanse", "Strips up to N active debuffs."),
        [SkillEffectKind.Summon]        = E("self", "no", "—", "—", "CreatureSummons.SummonNow", "Tops the caster's summons back up to its kind's maximum. Who and how many belong to the caster."),
        [SkillEffectKind.ElementConverter] = E("self/mobs", "no", "converts (Weapon/Armor), element, duration", "duration only", "UnitStates.BeginElement", "Weapon: ordinary attacks land as the element. Armor: hit as the element at level 1. A new one on the same slot replaces the old; never stacks."),
        [SkillEffectKind.Execute]       = E("mobs", "yes", "execute_scale, proc_chance", "base roll (execute_scale raw)", "CombatAuthority.Apply", "Proc per hit: bonus = roll × power × (target's missing HP). Huge near death."),
        [SkillEffectKind.ReturnToSavePoint]    = E("self/mobs", "no", "—", "—", "NetPlayer.OweTripToSavePoint / Health.TeleportTo", "A person goes to their save point (the default one when none is set), on whichever map it is; a creature to the home it spawned at. Server only; a dead unit goes nowhere. What a Butterfly Wing runs."),
        [SkillEffectKind.TeleportToRandomSpot] = E("self/mobs", "no", "—", "—", "Pathfinder3D.RandomSpotInArea + ICombatUnit.TeleportTo", "Sets the unit down on a random walkable spot of its own connected stretch of ground, so it can always walk back. Nowhere found = nothing happens, and an item running it is not spent. What a Fly Wing runs."),
        [SkillEffectKind.Dash]          = E("self (point)","no", "dash_speed, ease", "—", "Player3DController.BeginGlideTo", "Visible fast glide to the aimed point; blocked by geometry (unlike Teleport)."),
        [SkillEffectKind.Knockback]     = E("mobs", "no", "shove_speed, duration", "duration only", "MonsterAI3D.Knockback", "Shoves the mob AWAY from the caster (speed = raw magnitude)."),
        [SkillEffectKind.Pull]          = E("mobs", "no", "shove_speed, duration", "duration only", "MonsterAI3D.Knockback", "Drags the mob TOWARD the caster (mirrored origin)."),
        [SkillEffectKind.Shield]        = E("self", "no", "shield_amount, duration", "shield_amount, duration", "Character.BeginShield", "NEW. A finite HP barrier that soaks HITS until drained; DoT bypasses it (like Guard)."),
        [SkillEffectKind.BoostBasicAttackDamage] = E("self", "no", "damage_bonus, duration, charges (stacks-per-apply)", "damage_bonus, duration", "UnitStates.BeginBasicAttackBoost", "Charges each worth +bonus on ONE basic attack (a swing, a shot, or a skill shot resolving as one), spent hit or miss in BasicSwing. Skill strikes never spend it."),
        [SkillEffectKind.SureHit]       = E("self", "no", "duration, charges (stacks-per-apply)", "duration only", "UnitStates.BeginSureHit", "Charges that make ONE basic attack skip its accuracy roll; spent by the next basic attack whether or not it would have missed."),
        [SkillEffectKind.ResetCooldown] = E("self", "no", "of skill", "—", "SkillRunner.ResetCooldown", "Modifier: the named skill of the unit it lands on is ready again at once. Fires once, stores nothing; the shared ultimate cooldown keeps running."),
        [SkillEffectKind.Charges]       = E("self", "no", "duration, charges (stacks-per-apply)", "duration only", "UnitStates.BeginCharges", "Charges that change no stat; another skill's trigger spends them through its stack gate."),
        [SkillEffectKind.Parry]         = E("self", "no", "duration, charges (stacks-per-apply)", "duration only", "Character.BeginParry", "NEW. Charges that ERASE one incoming hit each (0 damage, pre-defence) and are spent by it — not a % cut like Guard. Binary, so no magnitude. Fires OnParry."),
    };
    static Eff E(string target, string dmg, string channels, string scales, string sink, string details)
        => new Eff { target = target, dmg = dmg, channels = channels, scales = scales, sink = sink, details = details };

    void OnGUI()
    {
        if (wrap == null)
        {
            wrap = new GUIStyle(EditorStyles.label) { wordWrap = true };
            head = new GUIStyle(EditorStyles.boldLabel) { wordWrap = true };
        }

        EditorGUILayout.HelpBox("Read-only reference for skill authoring (design: \"Active Skill System — V3\"). " +
            "The Effects table is generated from the SkillEffectKind enum, so any new effect without a catalog row " +
            "shows a ⚠. 'ring scales' is verified against the applier — a ⚠ there means the composer shows a scaling " +
            "channel the runtime ignores (a dead knob to fix).", MessageType.Info);

        scroll = EditorGUILayout.BeginScrollView(scroll);

        // ---- Channels ----
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("CHANNELS CATALOG — the numeric outputs (\"how much?\")", EditorStyles.boldLabel);
        Row(head, ("id", 140), ("label", 130), ("unit", 60), ("tier", 105), ("ring?", 45), ("notes", Flex(480)));
        Line();
        foreach (var c in Channels)
            Row(wrap, (c.id, 140), (c.label, 130), (c.unit, 60), (c.tier, 105), (c.ring, 45), (c.notes, Flex(480)));

        EditorGUILayout.Space(14);

        // ---- Effects ----
        EditorGUILayout.LabelField("EFFECTS CATALOG — the behaviors (\"what happens?\")", EditorStyles.boldLabel);
        Row(head, ("effect", 105), ("target", 80), ("dmg-type", 60), ("channels", 165), ("ring scales", 150), ("combat sink", 150), ("details", Flex(710)));
        Line();
        foreach (SkillEffectKind k in Enum.GetValues(typeof(SkillEffectKind)))
        {
            if (Effects.TryGetValue(k, out var e))
                Row(wrap, (k.ToString(), 105), (e.target, 80), (e.dmg, 60), (e.channels, 165), (e.scales, 150), (e.sink, 150), (e.details, Flex(710)));
            else
                Row(wrap, (k.ToString(), 105), ("⚠ no catalog entry — add one", Flex(105)));
        }

        EditorGUILayout.EndScrollView();
    }

    // Remaining width for the last (details/notes) column, min-clamped so it stays readable when the window is narrow.
    float Flex(float usedBefore) => Mathf.Max(220f, position.width - usedBefore - 30f);

    void Row(GUIStyle style, params (string text, float w)[] cols)
    {
        using (new EditorGUILayout.HorizontalScope())
            foreach (var (text, w) in cols)
                GUILayout.Label(text, style, GUILayout.Width(w));
    }

    void Line()
    {
        var r = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(r, new Color(1f, 1f, 1f, 0.12f));
    }
}
