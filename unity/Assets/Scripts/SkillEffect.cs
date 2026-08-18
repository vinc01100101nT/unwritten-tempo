using System.Collections.Generic;
using UnityEngine;

/// <summary>One stat's pull on an effect channel — the atom of an <see cref="SkillEffect"/>'s AFFINITY. The
/// weight is relative (the list is normalised by its own sum), so <c>{MAtk 70, CastSpeed 20, MP 10}</c> just
/// means "mostly MAtk." A channel's affinity multiplier = <c>1 + Σ(normWeight × the caster's ring % in that
/// stat)</c>, so an effect grows as you steer the ring into its home region — the skill IS a readout of your
/// ring position. Empty list ⇒ ×1 (no scaling), so a skill that authors no affinity behaves exactly as before.</summary>
[System.Serializable]
public struct StatWeight
{
    public StatId stat;
    public float weight;
}

/// <summary>Which entity categories a skill tick applies to. Absolute (RO-faction-free): the author picks who can
/// be hit; the caster (the player) is who deals it. A <c>[Flags]</c> mask so one tick can hit several at once.
/// In the solo slice <see cref="Player"/> == "self" (buffs/heals on the caster).</summary>
[System.Flags]
public enum SkillTargets
{
    None   = 0,
    Player = 1 << 0,   // the caster (self). TODO(party): ALLY targeting for support cross-buffing needs a party
                       // system first — until allies exist, buffs land on the caster only (design "Support Scaling").
    Mobs   = 1 << 1,
    Npc    = 1 << 2,   // reserved: wired once NPCs become damageable
}

/// <summary>How a tick picks its victims.</summary>
public enum SkillScope
{
    /// <summary>Hit EVERY enabled entity inside the radius (the AoE contact zone).</summary>
    Area,
    /// <summary>Hit ONLY the cast's designated target (<see cref="VfxContext.targetUnit"/>) — always lands,
    /// range-independent (radius ignored). Falls back to <see cref="Area"/> if the cast set no target.</summary>
    Single,
}

/// <summary>What a single skill effect does to a contacted entity. The first four are the original set; the rest
/// are the MVP active-skill verbs (each maps onto an EXISTING combat sink where one exists — Guard→BeginGuard,
/// Dash→the roll primitive, Slow/Stun stay <see cref="Status"/>).</summary>
public enum SkillEffectKind
{
    /// <summary>Roll the caster's ATK/MATK × <see cref="SkillEffect.damageMult"/>, element-aware → the existing
    /// <c>Health</c>/<c>Character</c> damage sinks. Owns only <see cref="SkillEffect.hitCount"/> (multi-hit) — each
    /// strike is the trigger point where the binding's PROC effects (Status / Execute) roll. Does ONE thing.</summary>
    Damage,
    /// <summary>Restore <see cref="SkillEffect.amount"/> flat HP → <c>Health.Heal</c>/<c>Character.Heal</c>.</summary>
    Heal,
    /// <summary>Inflict an <see cref="Ailment"/> (DoT / stun / slow / …) for <see cref="SkillEffect.seconds"/> at
    /// <see cref="SkillEffect.magnitude"/> → the shared <c>Status.Apply</c> front door.</summary>
    Status,
    /// <summary>Timed move-speed buff ×<see cref="SkillEffect.magnitude"/> for <see cref="SkillEffect.seconds"/> →
    /// <c>StatusController.Haste</c>.</summary>
    Haste,

    /// <summary>Self incoming-damage cut for <see cref="SkillEffect.seconds"/> at <see cref="SkillEffect.magnitude"/>
    /// (0..1 reduction) → reuses <c>Character.BeginGuard</c>. (Bulwark.)</summary>
    Guard,
    /// <summary>Force a mob's aggro onto the caster for <see cref="SkillEffect.seconds"/> → <c>MonsterAI3D.Taunt</c>.
    /// (Bulwark.)</summary>
    Taunt,
    /// <summary>Instantly TELEPORT the caster to the cast point (no roll, no travel) → <c>Player3DController.Teleport</c>.
    /// Applied to the caster regardless of the tick radius (the destination IS the point). (Blink Step.)</summary>
    Teleport,
    /// <summary>Timed self stat-buff (which stat = <see cref="SkillEffect.buffStat"/>) ×<see cref="SkillEffect.magnitude"/>
    /// for <see cref="SkillEffect.seconds"/>. Damage / AttackSpeed / CastSpeed ride <c>Character</c>. Move speed is NOT
    /// here — use the <see cref="Haste"/> kind (the single move-speed path; it can also debuff mobs). (War Banner damage-up.)</summary>
    Buff,
    /// <summary>Heal-over-time on the caster: <see cref="SkillEffect.magnitude"/> HP per tick for
    /// <see cref="SkillEffect.seconds"/> → <c>Character.BeginRegen</c>. (Mending Tide.)</summary>
    Regen,
    /// <summary>Remove up to <see cref="SkillEffect.amount"/> active debuffs from the target → <c>StatusController.Cleanse</c>.
    /// (Mending Tide.)</summary>
    Cleanse,
    /// <summary>Mark a mob to TAKE +<see cref="SkillEffect.magnitude"/> fraction more damage for
    /// <see cref="SkillEffect.seconds"/> → <c>Health.BeginVulnerable</c>. (Hex Field.)</summary>
    Vulnerability,
    /// <summary>Bonus damage vs a LOW-HP target — a PROC (rolled per hit via <see cref="SkillEffect.chance"/>):
    /// deals <c>roll × magnitude × (1 − target HP fraction)</c>, hitting hardest as the target nears death. Its own
    /// self-contained effect, sitting alongside the Damage effect it triggers off. (Meteor.)</summary>
    Execute,
    /// <summary>Rush the caster to the cast point — same aimed, range-clamped destination as <see cref="Teleport"/>,
    /// but the player VISIBLY travels there fast (a gap-close glide) instead of blinking instantly →
    /// <c>Player3DController.BeginGlideTo</c> — a pure A→B glide with NO character animation (not a roll or run).
    /// Speed = <see cref="SkillEffect.dashSpeedMult"/> × current run speed; blocked by geometry (unlike Blink). The
    /// skill's range is the max distance. (Dash.)</summary>
    Dash,

    /// <summary>Knock a mob back — shove it AWAY from the caster at <see cref="SkillEffect.magnitude"/> speed for
    /// <see cref="SkillEffect.seconds"/> → <c>MonsterAI3D.Knockback</c>. (The old weapon Push affix, rehomed onto
    /// the skill — V3: control is authored on the skill, not a weapon affix.)</summary>
    Knockback,
    /// <summary>Drag a mob TOWARD the caster at <see cref="SkillEffect.magnitude"/> speed for
    /// <see cref="SkillEffect.seconds"/> → <c>MonsterAI3D.Knockback</c> from a mirrored origin. (The old Pull affix.)</summary>
    Pull,
    /// <summary>Self, timed window: for <see cref="SkillEffect.seconds"/>, restore <see cref="SkillEffect.magnitude"/>
    /// fraction (0..1) of the damage you deal as MP → <c>Character.BeginManaLeech</c>. (The old weapon Mana-Leech affix.)</summary>
    ManaLeech,
    /// <summary>Self, timed window: for <see cref="SkillEffect.seconds"/>, reflect <see cref="SkillEffect.magnitude"/>
    /// fraction (0..1) of the damage you TAKE back at the attacker → <c>Character.BeginReflect</c>. (The old Reflect affix.)</summary>
    Reflect,

    /// <summary>Raise a timed absorbing BARRIER on the caster: soaks <see cref="SkillEffect.amount"/> HP of incoming HIT
    /// damage for <see cref="SkillEffect.seconds"/> → <c>Character.BeginShield</c>. Distinct from <see cref="Guard"/> (a %
    /// damage CUT): Shield is a finite HP pool that depletes as it eats hits, then lapses. Bypassed by DoT, like Guard.
    /// Barrier HP scales off sustain stats via <see cref="SkillEffect.affinity"/> (kept off offense to stay a support
    /// tool).</summary>
    Shield,
    /// <summary>Self, timed READ: for <see cref="SkillEffect.seconds"/> the caster holds parry CHARGES — the next
    /// incoming hit is negated OUTRIGHT (0 damage, no defence math) and spends one charge → <c>Character.BeginParry</c>.
    /// One charge per <see cref="SkillEffect.stacksPerApply"/>, so "blocks the next hit" is simply 1.
    ///
    /// <para>Distinct from <see cref="Guard"/> on purpose: Guard is a standing PERCENTAGE cut that every hit in the
    /// window is reduced by and that ends on its clock; Parry is a DISCRETE charge that fully erases ONE hit and is
    /// spent by it (the duration is only how long you wait for that hit). Binary by design — there is no partial
    /// parry, hence no magnitude. A spent charge fires <see cref="SkillTrigger.OnParry"/>.</para>
    ///
    /// <para>Distinct from <see cref="Brace"/>: a parry ERASES the hit, a brace only POSTPONES it.</para></summary>
    Parry,
    /// <summary>Arm BRACE charges for <see cref="SkillEffect.seconds"/>. Each charge holds back ONE incoming hit:
    /// instead of costing HP, that hit's damage — what it would really have cost after every other mitigation — is
    /// banked, and the whole debt lands as one true-damage hit the moment the window ends. Out of charges, the next
    /// hit lands normally.
    ///
    /// <para>Charges come from <see cref="SkillEffect.stacksPerApply"/>, the same shared knob <see cref="Parry"/>
    /// uses — this kind has NO multiplicity of its own, so stacking it is the only way to hold more hits and stacking
    /// it always does something real. Binary per hit, so no magnitude.</para>
    ///
    /// <para>The twin of <see cref="Parry"/>, one word apart: a parry ERASES the hit, a brace POSTPONES it. Surviving
    /// is the player's job — a consumer must cash the debt before it comes due.</para></summary>
    Brace,

    // ---- Modifiers: the target is a SKILL's live pile, not a body ------------------------------------------------
    // These two are the first effects whose subject is another skill. Everything above asks "what happens to whoever
    // this reaches"; these ask "what happens to the pile that skill left on me". Both edit an EXISTING pile and never
    // create one — a pile's stacks are the granter's to define, so an editor that could conjure them would be a second
    // definition of the same charge (see UnitStates.AddStacksTo).

    /// <summary>Give every stack of the named skill's pile <see cref="SkillEffect.modifierSeconds"/> more life →
    /// <c>UnitStates.ExtendFrom</c>. Does nothing if that pile isn't running.</summary>
    ExtendDuration,
    /// <summary>Add <see cref="SkillEffect.modifierStacks"/> stacks to the named skill's pile, each a copy of its
    /// newest one → <c>UnitStates.AddStacksTo</c>. THE way one skill feeds another's pile: a normal grant always files
    /// under the skill that ran it, so a kill can only lengthen a storm through this.
    ///
    /// <para>Works whether or not that pile is running: a live pile gains copies of its newest stack, and a lapsed one
    /// is STARTED from the granting skill's own authored size and duration. Either way the stack is defined by the
    /// granter, never invented here — which is the rule this kind exists to keep, not a limit on when it fires.</para>
    ///
    /// <para>Appending is the only safe add — Unity serializes the kind by int.</para></summary>
    AddStacks,

    /// <summary>Hand the unit CHARGES that make its attacks hit harder: one charge multiplies one outgoing attack by
    /// 1 + <see cref="SkillEffect.magnitude"/>, and that attack spends it — whether it hits or misses, because the
    /// charge paid for making the attack, not for landing it → <c>UnitOffense.Boosted</c>.
    ///
    /// <para>Charges come from <see cref="SkillEffect.stacksPerApply"/>, the same knob <see cref="Parry"/> and
    /// <see cref="Brace"/> use, and each carries its own bonus. The kind owns no multiplicity of its own — one charge,
    /// one attack — so stacking it always buys exactly N boosted attacks.</para>
    ///
    /// <para>It catches every attack the unit sends: a basic, a skill strike, an execute, a mob's swing, and the flat
    /// banked kind that never rolls. Damage-over-time is the one exclusion, and it comes free: ailment ticks are true
    /// damage straight to HP, so they can neither gain the boost nor eat a charge.</para>
    ///
    /// <para>It multiplies BEFORE the target's armour, so the flat part of armour is paid once out of a bigger number
    /// and the boost is worth more the tankier the target. Distinct from a <see cref="Buff"/> on Damage, which is a
    /// standing percentage for a window; this is a counted resource, spent per attack.</para>
    ///
    /// <para>MUST stay last in the enum — Unity serializes the kind by int, so appending is the only safe add.</para></summary>
    BoostOutgoingDamage,
}

/// <summary>Which self-stat a <see cref="SkillEffectKind.Buff"/> effect lifts for its window. Move speed is
/// deliberately absent — the <see cref="SkillEffectKind.Haste"/> kind is the one authoring path for move speed
/// (it also targets mobs), so Buff owns only the character-combat stats Haste can't do.</summary>
public enum BuffStat { Damage, AttackSpeed, CastSpeed }

/// <summary>How a Damage/Execute effect RESOLVES — the RPS-triangle axis (design doc "Combat Ontology"). Serialized
/// by int, so order is load-bearing: <c>Physical=0, Magic=1, Crit=2</c> (a legacy <c>magic:1</c> bool migrates to
/// Magic for free — same int, so old assets keep their behaviour with no data patch). Physical → ATK vs armour;
/// Magic → MATK, unmissable, glances vs Evasion, cannot crit; Crit → physical that PIERCES armour and always crits.
/// NOTE: the full pierce/glance math lands in a later combat pass (it touches <c>Health</c>); for now Physical/Magic
/// behave exactly as the old bool and Crit routes the physical roll with a forced crit.</summary>
public enum DamageType { Physical, Magic, Crit }

/// <summary>One gameplay OUTCOME a skill tick applies. Presentation-neutral INTENT — the central
/// <see cref="SkillEffectApplier"/> maps it onto the SAME combat sinks skills already call (no second damage
/// path). Damage scales off the caster so a tick tracks the caster's stats/crit like every other skill. This is
/// the relocated old <c>VfxEffect</c>: gameplay is now owned by the Skill layer, not the VFX asset.</summary>
[System.Serializable]
public class SkillEffect
{
    public SkillEffectKind kind = SkillEffectKind.Damage;

    [Header("Damage")]
    [Tooltip("ON = this strike IS an ordinary attack: the caster swings by its own rules — its crit, its accuracy " +
             "against the target's dodge (so it CAN miss), its weapon's element — and a landed strike announces a " +
             "basic attack, so it feeds Momentum and every on-basic passive. Base power still scales it (1 = one " +
             "normal swing, 2 = twice one). Damage type and element are the weapon's then, not this effect's.")]
    public bool resolveAsBasicAttack = false;
    [Tooltip("How the hit resolves: Physical (ATK vs armour) / Magic (MATK, unmissable, no crit) / Crit (physical " +
             "that pierces armour & always crits). Default Magic — matches every pre-migration bool-magic skill.")]
    public DamageType damageType = DamageType.Magic;
    [Tooltip("BASE POWER — the skill's inherent strength (1 = one normal hit; a heavy nuke is higher). Affinity picks " +
             "WHICH stats scale it; this sets HOW BIG. Final = caster ATK/MATK × base power × affinity. No skill levels.")]
    public float damageMult = 1f;
    [Tooltip("Attack element (RO property), checked against the target's armor element.")]
    public Element element = Element.Neutral;

    [Header("Heal")]
    [Tooltip("Flat HP restored (Heal kind).")]
    public int amount = 10;

    [Header("Status / Haste")]
    [Tooltip("Which ailment to inflict (Status kind).")]
    public Ailment ailment = Ailment.Burn;
    [Tooltip("Duration in seconds — 0 for an instant/action effect. Only durational STATE kinds (Status/Haste/Guard/" +
             "Buff/Regen/…) use it; the composer keeps it 0 for everything else. The buff tray / nameplate shows an icon " +
             "iff this is > 0 — i.e. only for ongoing CONDITIONS, never for movement actions.")]
    public float seconds = 0f;
    [Tooltip("Knockback / Pull ONLY: how long the shove MOVEMENT lasts (seconds). This is movement time, NOT a state " +
             "duration — a shove isn't a tracked condition, so it never shows a buff/nameplate icon.")]
    public float shoveSeconds = 0.25f;
    [Tooltip("Status: DoT damage-per-tick, Slow factor (0..1), or Blind HIT loss. Haste/Buff: the BONUS fraction, " +
             "not a multiplier (0.3 = +30%). " +
             "Guard: damage-cut fraction (0..1). Regen: HP per tick. Vulnerability: extra-damage fraction (0.3 = +30%). " +
             "Knockback/Pull: shove speed. ManaLeech/Reflect: fraction of damage dealt/taken (0..1).")]
    public float magnitude = 3f;

    [Header("Stacking (STATE effects only)")]
    [Tooltip("OFF = one instance; re-applying REPLACES it (newest wins) — today's behaviour. ON = each application ADDS " +
             "stacks, every stack carrying its own duration and its own magnitude; the total is their SUM.\n\n" +
             "IMPORTANT: a stacking effect's magnitude is the PER-STACK amount. On Haste / Stat Buff it is the BONUS, " +
             "never a multiplier — stacking or not: author 0.03 for '+3% per stack', so 10 stacks read as +30%.")]
    public bool stacking = false;
    [Tooltip("Max stacks held at once. Past it, the OLDEST stack drops. Hard-capped by the store's safety ceiling.")]
    [Min(1)] public int stackLimit = 5;
    [Tooltip("How many stacks ONE application grants — this is the 'charges' knob. 3 = a cast hands out 3 charges that " +
             "share one window and are spent one at a time.")]
    [Min(1)] public int stacksPerApply = 1;

    [Tooltip("ON = this effect's stack count is NOT authored — it is worked out at cast time from how many times the " +
             "caster would swing during this effect's own duration (duration / seconds-per-swing). Faster attack " +
             "speed therefore means more stacks in the SAME window, never a longer one. Max stacks and stacks per " +
             "use no longer apply: the count is whatever the caster's speed earns.")]
    public bool stacksFromAttackSpeed = false;
    [Tooltip("This effect's SHARE of that count. 1 = the full count; 0.5 = half of it, rounded up. Two effects on one " +
             "skill sharing a duration stay in exact proportion for free — half is always half, at any attack speed, " +
             "with nothing to re-author when the duration changes.")]
    [Min(0f)] public float stackShare = 1f;

    [Header("Dash")]
    [Tooltip("Dash travel speed as a multiple of the caster's CURRENT run speed (8 = 8× run speed) — the visible fast " +
             "glide to the aimed point. Higher = snappier gap-close. Only the Dash kind uses this.")]
    public float dashSpeedMult = 8f;
    [Tooltip("Dash: ease IN — accelerate from a slow start up to full speed over the ease-window seconds. Off = full speed instantly.")]
    public bool dashEaseIn = false;
    [Tooltip("Dash: ease OUT — decelerate to a gentle stop over the ease-window seconds before arrival. Off = full speed " +
             "until arrival. (Both on = slow-fast-slow.)")]
    public bool dashEaseOut = false;
    [Tooltip("Dash: how long (seconds) each ease ramp lasts — the accelerate / decelerate window. TIME-based, so the " +
             "ramp reads the same at any distance; on a dash shorter than the window the ramps fill the whole trip. " +
             "Only used when ease in / out is on.")]
    [Min(0.05f)] public float dashEaseSeconds = 0.3f;

    [Tooltip("Dash / Blink: leave a FROZEN COPY of the mover where it stood, fading out behind it — so a fast chain " +
             "of hops reads as several of you at once. 0 = off.")]
    [Min(0f)] public float afterimageSeconds = 0f;
    [Tooltip("Colour of that frozen copy. Alpha = how solid it starts before fading to nothing.")]
    public Color afterimageTint = new Color(0.6f, 0.85f, 1f, 0.5f);
    [Tooltip("Dash: how see-through the MOVER's own body is WHILE IT TRAVELS — solid again the instant it lands, so " +
             "the fade is always exactly one hop long. 1 = solid (no fade). Lower = more see-through, so a hop reads " +
             "as a blur rather than a person sliding. Blink arrives instantly, so it has no travel to fade.")]
    [Range(0f, 1f)] public float casterOpacity = 1f;

    [Tooltip("Dash / Blink: how many CORNERS the repeated landings trace. Each hop crosses to the far side of the " +
             "target and steps on, so this many hops in a row draw a star THROUGH it rather than walking a ring " +
             "around it. 5 = the classic five-pointed star. Must be ODD or the figure never closes — an even value " +
             "is nudged up by one.")]
    [Min(3)] public int starCorners = 5;

    [Header("Modifiers (which skill's pile this edits)")]
    [Tooltip("WHOSE pile this edits — the id of the skill that granted it. Its own pile is a valid answer (a skill " +
             "that lengthens itself). Set it via the Skill Composer's dropdown, not by hand.")]
    public string modifierSkillId = "";
    [Tooltip("WHICH of that skill's piles. Same two-part answer the stack gate needs: a skill can grant more than one, " +
             "and editing the wrong one is silent.")]
    public StateId modifierStateId = StateId.Poison;
    // Every StateId is legal, so the field alone can't say "nobody picked". Same companion flag, same reason, as the
    // stack gate's — an unauthored pile must be VISIBLY unauthored instead of reading as the first enum member.
    [HideInInspector] public bool modifierStateIdSet = false;
    [Tooltip("Seconds added to every stack of that pile. Extend duration only.")]
    [Min(0f)] public float modifierSeconds = 1f;
    [Tooltip("How many stacks are added to that pile, each a copy of its newest. Add stacks/charges only.")]
    [Min(0)] public int modifierStacks = 1;

    [Header("Multi-hit (Damage kind)")]
    [Tooltip("How many times a Damage effect strikes per landing. Each strike is the TRIGGER POINT for the binding's " +
             "PROC effects (a Status or Execute with a chance) — they roll once per hit. 1 = single hit.")]
    [Min(1)] public int hitCount = 1;

    [Header("Proc")]
    [Tooltip("PROC chance (0..1), rolled PER HIT of the binding's multi-hit Damage. Used by Status (e.g. a stun proc) " +
             "and Execute. 0 = NOT a proc — the effect applies ONCE per landing instead.")]
    [Range(0f, 1f)] public float chance = 0f;

    [Header("Pull from the cast's stacks")]
    [Tooltip("ON = the pile this cast is cashing decides this effect's SIZE. HOW it pays depends on the reading below: " +
             "a carried total is dealt FLAT (it is already real damage), while a COUNT adds to this effect's base " +
             "power and rolls through your Attack like any other hit. Several effects on one skill can each pull from " +
             "the SAME pile — that's how one cast both hits for the banked damage and heals off it.")]
    public bool useStackValue = false;
    [Tooltip("WHICH reading this effect pulls: Count = how many stacks; Carried total = the sum of what they carry " +
             "(a percentage pile reads as its decimal, +30% = 0.3).")]
    public StackReading stackValueSource = StackReading.Count;
    [Tooltip("What the reading is multiplied by. On a carried total it is a FLAT scale: 2.5 on a total of 400 = 1000 " +
             "damage, 0.5 = heal for half of it. On a COUNT of Damage it is the extra power PER STACK: 0.4 = +40% of " +
             "your Attack per stack, added to this effect's base power.")]
    [Min(0f)] public float stackValueMult = 1f;

    /// <summary>This effect's FLAT output when the pile simply IS its size, or -1 when it isn't driven that way. The
    /// number is dealt/healed as-is, with no Attack roll behind it — a carried total is already real, earned damage,
    /// so re-rolling it through the caster's Attack would scale a number that was never the caster's to scale.
    ///
    /// <para>The ONE exception is a DAMAGE effect reading the COUNT: a tally is not an amount, so it pays through
    /// <see cref="StackDamageBonus"/> (which lifts this effect's own power and still rolls) instead. The two are exact
    /// complements — every stack-driven effect goes down one path or the other, never both, never neither. A HEAL takes
    /// the flat path on either reading, because a heal has no Attack roll for a multiplier to lift.</para></summary>
    public int StackOutput(in StackVars vars) =>
        !useStackValue || (kind == SkillEffectKind.Damage && stackValueSource == StackReading.Count)
            ? -1
            : Mathf.Max(0, Mathf.RoundToInt(vars.Read(stackValueSource) * Mathf.Max(0f, stackValueMult)));

    /// <summary>The extra damage multiplier the COUNT reading buys THIS effect — ADDED to its own base power, so the
    /// hit still rolls through the caster's Attack, still scales with the ring, and can still crit. Zero for any effect
    /// the pile doesn't drive that way.
    ///
    /// <para>Adds up, never compounds: a 6× effect on 10 stacks at 0.4 lands at 6 + 4 = 10× Attack.</para></summary>
    public float StackDamageBonus(in StackVars vars) =>
        useStackValue && kind == SkillEffectKind.Damage && stackValueSource == StackReading.Count
            ? Mathf.Max(0f, stackValueMult) * vars.Count
            : 0f;

    /// <summary>WHICH state entry this effect creates, or false when it creates none. THE one answer to "what pile
    /// does this effect leave behind", so a consumer's authoring list can never offer a pile that will not exist.
    ///
    /// <para>It must agree with <c>SkillEffectApplier.ApplyOne</c>, which is where each kind actually lands — every
    /// line below names the site it mirrors. Kinds absent from the switch make no state at all: the instant ones
    /// (Damage, Heal, Dash…) obviously, but also <see cref="SkillEffectKind.Taunt"/>, which has a duration yet routes
    /// to <c>ForceAggro</c> — <c>StateId.Taunt</c>, <c>Knockback</c> and <c>Pull</c> are declared but never written by
    /// anything, so offering them would be offering an empty pile.</para></summary>
    public static bool TryStateFor(SkillEffect e, out StateId id)
    {
        id = default;
        if (e == null) return false;
        switch (e.kind)
        {
            // Ailment i ↔ StateId i, matching enum order (StatusController.Apply).
            case SkillEffectKind.Status:        id = (StateId)(int)e.ailment; return true;
            // UnitStates.ApplyStatBuff picks the channel from the stat.
            case SkillEffectKind.Buff:
                id = e.buffStat == BuffStat.AttackSpeed ? StateId.AttackSpeedBuff
                   : e.buffStat == BuffStat.CastSpeed   ? StateId.CastSpeedBuff
                                                        : StateId.DamageBuff;
                return true;
            case SkillEffectKind.Guard:         id = StateId.Guard;         return true;
            case SkillEffectKind.Parry:         id = StateId.Parry;         return true;
            case SkillEffectKind.BoostOutgoingDamage: id = StateId.OutgoingDamageBoost; return true;
            // The ARMED charges. Its debt (StateId.DeferredDamage) is created by incoming hits, not by this apply,
            // so it is not something this effect hands out.
            case SkillEffectKind.Brace:         id = StateId.Brace;         return true;
            case SkillEffectKind.Shield:        id = StateId.Shield;        return true;
            case SkillEffectKind.Regen:         id = StateId.Regen;         return true;
            case SkillEffectKind.Vulnerability: id = StateId.Vulnerability; return true;
            case SkillEffectKind.Haste:         id = StateId.Haste;         return true;
            case SkillEffectKind.ManaLeech:     id = StateId.ManaLeech;     return true;
            case SkillEffectKind.Reflect:       id = StateId.Reflect;       return true;
            default: return false;
        }
    }

    /// <summary>EVERY pile this effect can leave on the caster — what its apply creates, plus any pile that apply
    /// causes LATER. The two differ for exactly one kind today: a Brace arms charges now, and each charge that catches
    /// a hit turns into delayed damage, which is a second pile with its own lifetime. That later pile is filed under
    /// this same skill, so a consumer must be able to name it — Retribution cashes the delayed damage, never the armed
    /// charges. THE one answer to "what can a consumer point at", so an authoring list can neither offer a pile that
    /// will never exist nor hide one that will.</summary>
    public static void CollectPiles(SkillEffect e, List<StateId> into)
    {
        if (into == null || !TryStateFor(e, out var id)) return;
        if (!into.Contains(id)) into.Add(id);
        if (e.kind == SkillEffectKind.Brace && !into.Contains(StateId.DeferredDamage))
            into.Add(StateId.DeferredDamage);
    }

    /// <summary>Which kinds can be driven by the cast's pile. Deliberately the two whose output is a plain SIZE — a
    /// number the pile can BE or add to. A duration or a proc chance is not a size, so pulling a pile into one would
    /// mean inventing a second conversion rule per kind; those keep their own fields.</summary>
    public static bool SupportsStackValue(SkillEffectKind k) =>
        k == SkillEffectKind.Damage || k == SkillEffectKind.Heal;

    [Header("Who it lands on")]
    [Tooltip("WHO this effect is applied to. Auto = this kind's traditional side (guard/shield/buff on the caster, " +
             "damage/ailments on the target). Caster = always you. Target = always whoever the tick reaches, so a " +
             "shield, a regen or a stat buff can be placed on ANY unit — including a mob.")]
    public EffectSide side = EffectSide.Auto;

    [Header("Buff (Buff kind)")]
    [Tooltip("Which self-stat the Buff kind lifts for its window. magnitude = the FLAT bonus fraction, 0.3 = +30% (AMPLIFY buffs " +
             "are NOT affinity-scaled — support value comes from uptime, not a bigger number); seconds = duration " +
             "(this DOES scale off sustain stats via durationAffinity).")]
    public BuffStat buffStat = BuffStat.Damage;

    // A buff that GREW while it was up (a delay, a step size, an interval, a ceiling — four knobs on one effect) was
    // removed 2026-08-17: no shipped skill ever set them, and a buff whose value depends on how long it has been
    // running can't be read off the screen or balanced against a flat one. A buff is worth what it says it is worth;
    // uptime is the support's contribution. Don't reintroduce it as an effect — it would be a standing change with a
    // clock, which the Modifiers rule already forbids.

    [Header("Affinity (ring-investment scaling)")]
    [Tooltip("Scales this effect's main OUTPUT by ring investment — its damage / heal amount / DoT damage / buff " +
             "amount / slow amount, or (for a proc) its CHANCE. Weights are relative; empty = no scaling (×1).")]
    public List<StatWeight> affinity = new List<StatWeight>();
    [Tooltip("Scales this effect's DURATION (its seconds) by ring investment. Empty = duration unscaled (×1).")]
    public List<StatWeight> durationAffinity = new List<StatWeight>();
    [Tooltip("Damage kind only: scales the number of STRIKES (hitCount) by ring investment — more investment = more " +
             "hits in the flurry. hits = round(hitCount × mult). Empty = hit count unscaled (×1).")]
    public List<StatWeight> hitCountAffinity = new List<StatWeight>();

    /// <summary>WHO an authored effect lands on. The author's choice, not a property of the effect kind — every kind
    /// works on every unit now (see <see cref="SkillEffectApplier"/>'s single apply switch), so "a shield can only be
    /// cast on yourself" is a DESIGN decision made per skill, not a limit of the engine.
    ///
    /// <para><see cref="Auto"/> is the default and means "whatever this kind traditionally did" — see
    /// <see cref="SkillEffect.IsSelfVerb"/>. It exists so every skill asset authored before the side was choosable keeps
    /// its exact behaviour with no data migration: Unity serializes an enum by int, and 0 = Auto.</para></summary>
    public enum EffectSide
    {
        /// <summary>Traditional behaviour for this kind (self verbs on the caster, everything else on the target).</summary>
        Auto,
        /// <summary>Always the acting caster, whatever the tick is aimed at.</summary>
        Caster,
        /// <summary>Always whoever the tick reaches — including a mob, for kinds that used to be caster-only.</summary>
        Target,
    }

    /// <summary>Resolve where this effect actually lands. THE predicate the runtime partitions on and the composer
    /// groups by, so what an author sees can't drift from what runs.</summary>
    public static bool LandsOnCaster(SkillEffect e)
        => e != null && (e.side == EffectSide.Auto ? IsSelfVerb(e.kind) : e.side == EffectSide.Caster);

    /// <summary>The TRADITIONAL side of each kind — what <see cref="EffectSide.Auto"/> resolves to, and nothing else.
    /// It is no longer a capability limit: every kind works on every unit (the applier has ONE apply switch), so this
    /// is purely the default an asset gets when its author never picked a side.
    ///
    /// <para><b>Do not add new rules here.</b> A new kind's side is the author's choice via <see cref="side"/>; this
    /// list only exists to keep pre-existing assets behaving exactly as they did before the side was authorable. Heal
    /// is deliberately absent: it could always target others.</para></summary>
    public static bool IsSelfVerb(SkillEffectKind k) =>
        k == SkillEffectKind.Guard || k == SkillEffectKind.Buff || k == SkillEffectKind.Regen ||
        k == SkillEffectKind.Cleanse || k == SkillEffectKind.ManaLeech || k == SkillEffectKind.Reflect ||
        k == SkillEffectKind.Shield || k == SkillEffectKind.Parry || k == SkillEffectKind.Brace ||
        k == SkillEffectKind.BoostOutgoingDamage ||
        IsModifier(k) || NeedsPoint(k);

    /// <summary>An effect that changes ANOTHER SKILL's live pile instead of doing something to a body. Its target is
    /// named by <see cref="modifierSkillId"/> + <see cref="modifierStateId"/>, never by the tick's aim, and the pile it
    /// edits sits on the caster — so these always land caster-side no matter what the tick catches.</summary>
    public static bool IsModifier(SkillEffectKind k) =>
        k == SkillEffectKind.ExtendDuration || k == SkillEffectKind.AddStacks;

    /// <summary>An effect that must be told WHERE: it RELOCATES whoever it lands on, so the point is part of its
    /// meaning, not part of its targeting. This is the ONE thing that makes a cast ask for a ground point on top of
    /// (or instead of) picking an entity — see <see cref="SkillDef.NeedsAimPoint"/>. A future "move X to here" verb
    /// joins this list and inherits the two-step aim for free.
    ///
    /// <para>Aim vs side are INDEPENDENT questions: on the caster side the entity is known (you), so only the point
    /// is asked; on the target side the cast asks for the entity FIRST, then the point it's sent to.</para></summary>
    public static bool NeedsPoint(SkillEffectKind k) =>
        k == SkillEffectKind.Dash || k == SkillEffectKind.Teleport;
}
