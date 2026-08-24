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

    /// <summary>Force a mob's aggro onto the caster for <see cref="SkillEffect.seconds"/> → <c>MonsterAI3D.Taunt</c>.
    /// (Bulwark.)</summary>
    Taunt,
    /// <summary>Instantly TELEPORT the caster to the cast point (no roll, no travel) → <c>Player3DController.Teleport</c>.
    /// Applied to the caster regardless of the tick radius (the destination IS the point). (Blink Step.)</summary>
    Teleport,
    /// <summary>Timed RISE in one sub-stat (which one = <see cref="SkillEffect.subStat"/>) by
    /// <see cref="SkillEffect.magnitude"/> for <see cref="SkillEffect.seconds"/>.
    ///
    /// <para>THE single path for every timed sub-stat change there is — move speed, both regens, leech, reflect,
    /// accuracy, evasion, the lot. Each used to be its own verb; an author no longer has to know which ones happened
    /// to get one. Whether the number is a percentage of the stat or an amount added to it is the stat's own answer
    /// (<see cref="SubStats.IsPercent"/>).</para></summary>
    SubStatBuff,
    /// <summary>Remove up to <see cref="SkillEffect.amount"/> active debuffs from the target → <c>StatusController.Cleanse</c>.
    /// (Mending Tide.)</summary>
    Cleanse,
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

    /// <summary>Raise a timed absorbing BARRIER on the caster: soaks <see cref="SkillEffect.amount"/> HP of incoming HIT
    /// damage for <see cref="SkillEffect.seconds"/> → <c>Character.BeginShield</c>. Distinct from a damage-reduction
    /// sub-stat buff (a % damage CUT): Shield is a finite HP pool that depletes as it eats hits, then lapses. Bypassed
    /// by DoT, as the damage-reduction cut is.
    /// Barrier HP scales off sustain stats via <see cref="SkillEffect.affinity"/> (kept off offense to stay a support
    /// tool).</summary>
    Shield,
    /// <summary>Self, timed READ: for <see cref="SkillEffect.seconds"/> the caster holds parry CHARGES — the next
    /// incoming hit is negated OUTRIGHT (0 damage, no defence math) and spends one charge → <c>Character.BeginParry</c>.
    /// One charge per <see cref="SkillEffect.stacksPerApply"/>, so "blocks the next hit" is simply 1.
    ///
    /// <para>Distinct from a damage-reduction sub-stat buff on purpose: that is a standing PERCENTAGE cut that every hit
    /// in the window is reduced by and that ends on its clock; Parry is a DISCRETE charge that fully erases ONE hit and is
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
    /// charge paid for making the attack, not for landing it → <c>UnitOffense.SpendCharges</c>.
    ///
    /// <para>Charges come from <see cref="SkillEffect.stacksPerApply"/>, the same knob <see cref="Parry"/> and
    /// <see cref="Brace"/> use, and each carries its own bonus. The kind owns no multiplicity of its own — one charge,
    /// one attack — so stacking it always buys exactly N boosted attacks.</para>
    ///
    /// <para>It catches every attack the unit sends: a basic, a skill strike, an execute, a mob's swing.
    /// Damage-over-time is the one exclusion, and it comes free: ailment ticks are true
    /// damage straight to HP, so they can neither gain the boost nor eat a charge.</para>
    ///
    /// <para>It multiplies BEFORE the target's armour, so the flat part of armour is paid once out of a bigger number
    /// and the boost is worth more the tankier the target. Distinct from a <see cref="SubStatBuff"/> on the Damage
    /// sub-stat, which is a standing percentage for a window; this is a counted resource, spent per attack.</para></summary>
    BoostOutgoingDamage,

    /// <summary>The mirror of <see cref="BoostOutgoingDamage"/>: charges that make the holder's attacks land SOFTER.
    /// One charge multiplies one outgoing attack by 1 − <see cref="SkillEffect.magnitude"/> and is spent by it, hit or
    /// miss, for the same reason — the charge pays for making the attack. Authored as the CUT (0.4 = the next attack
    /// comes out 40% weaker) so the number reads as what it does, and it lands on whoever the effect's side names: on
    /// a target it blunts their swing, on the caster it is a self-inflicted cost.
    ///
    /// <para><b>Its own pile, deliberately.</b> A boost charge and a penalty charge never share one, so an enemy
    /// debuff cannot spend the boost you were saving, and the two simply multiply when a unit holds both.</para>
    ///
    /// <para>MUST stay last in the enum — Unity serializes the kind by int, so appending is the only safe add.</para></summary>
    ReduceOutgoingDamage,

    /// <summary>Nothing stops the holder for <see cref="SkillEffect.seconds"/>. It turns away, by name: the FLINCH a
    /// landing hit causes, STUN, FREEZE, SILENCE, KNOCKBACK, PULL, and a hit CANCELLING a cast in progress. It does not
    /// touch damage, poison, bleed, burn, slow or blind — this is about being stopped, not about being hurt.
    ///
    /// <para>A plain window, not charges: it is never spent by what it blocks, so one stun does not use it up. Refused
    /// at each door rather than suppressed after the fact, so nothing lingers to resume when the window closes.</para>
    ///
    /// <para>Appending is the only safe add — Unity serializes the kind by int.</para></summary>
    ImmuneToDisables,

    /// <summary>Timed DROP in one sub-stat — the exact mirror of <see cref="SubStatBuff"/>, same stat list, same
    /// pile, same window. The authored number is stated as a positive amount and applied as a subtraction, so an
    /// author never has to type a minus sign to make something worse.
    ///
    /// <para>A separate kind rather than "a buff with a negative number" for ONE reason: what an effect is FOR should
    /// be readable off the effect. A row that says "Sub Stat Debuff · Evasion · 0.3" says what it does; a buff row
    /// with a hidden minus in a number field does not.</para>
    ///
    /// <para>MUST stay last in the enum — Unity serializes the kind by int, so appending is the only safe add.</para></summary>
    SubStatDebuff,
}

/// <summary>WHICH sub-stat a <see cref="SkillEffectKind.SubStatBuff"/> / <see cref="SkillEffectKind.SubStatDebuff"/>
/// moves. Every timed change to a sub-stat is authored here — there is no second effect kind for any one of them, so
/// an author never has to know that move speed used to be its own verb and regen another.
///
/// <para>Serialized by int, so order is LOAD-BEARING: the first three keep the positions the old three-item stat buff
/// had, and everything new is APPENDED. Never reorder, never insert in the middle.</para>
///
/// <para>Named for the sub-stat itself, never for a flavour word — "MovementSpeed", not "Haste". A reader must be able
/// to find the number on the character sheet by the name on the effect.</para></summary>
public enum SubStat
{
    Damage, AttackSpeed, CastSpeed,          // 0,1,2 — the original three; positions frozen for saved assets
    MovementSpeed,
    HPRegen, MPRegen,
    ManaLeech, Reflect, LifeSteal,
    Attack, MagicAttack,
    Defense, MagicDefense,
    Accuracy, Evasion,
    CritChance, CritDamage,
    MaxHP, MaxMP,
    // One axis for how hard incoming damage lands: positive CUTS it (what Guard used to be), negative makes it hurt
    // more (what Vulnerability used to be). They were always the same number pointed two ways.
    DamageReduction,
}

/// <summary>THE sub-stat table — which pile each sub-stat lives in, and what its authored number MEANS. Every reader
/// (the applier, the state store's recompute, the composer, the tooltip) asks here, so a new sub-stat is one row and
/// cannot be half-added.</summary>
public static class SubStats
{
    /// <summary>Every sub-stat, in authoring order. Cached so a per-frame or per-hit reader never allocates one.</summary>
    public static readonly SubStat[] All = (SubStat[])System.Enum.GetValues(typeof(SubStat));

    /// <summary>The pile this sub-stat's bonus is held in. One pile per stat, so "which pile" can name it.</summary>
    public static StateId PileFor(SubStat s)
    {
        switch (s)
        {
            case SubStat.Damage:        return StateId.DamageBuff;
            case SubStat.AttackSpeed:   return StateId.AttackSpeedBuff;
            case SubStat.CastSpeed:     return StateId.CastSpeedBuff;
            case SubStat.MovementSpeed: return StateId.MovementSpeed;
            case SubStat.HPRegen:       return StateId.HPRegen;
            case SubStat.MPRegen:       return StateId.MPRegen;
            case SubStat.ManaLeech:     return StateId.ManaLeech;
            case SubStat.Reflect:       return StateId.Reflect;
            case SubStat.LifeSteal:     return StateId.LifeSteal;
            case SubStat.Attack:        return StateId.Attack;
            case SubStat.MagicAttack:   return StateId.MagicAttack;
            case SubStat.Defense:       return StateId.Defense;
            case SubStat.MagicDefense:  return StateId.MagicDefense;
            case SubStat.Accuracy:      return StateId.Accuracy;
            case SubStat.Evasion:       return StateId.Evasion;
            case SubStat.CritChance:    return StateId.CritChance;
            case SubStat.CritDamage:    return StateId.CritDamage;
            case SubStat.MaxHP:         return StateId.MaxHP;
            case SubStat.DamageReduction: return StateId.DamageReduction;
            default:                    return StateId.MaxMP;
        }
    }

    // The reverse of PileFor, built ONCE from PileFor itself so the two can never disagree: slot = (int)StateId,
    // value = the sub-stat + 1, leaving 0 for "this pile is not a sub-stat". An array rather than a switch because
    // the state store asks this for every live pile inside its recompute.
    static readonly int[] statForPile = BuildStatForPile();
    static int[] BuildStatForPile()
    {
        var ids = (StateId[])System.Enum.GetValues(typeof(StateId));
        int max = 0;
        for (int i = 0; i < ids.Length; i++) if ((int)ids[i] > max) max = (int)ids[i];
        var map = new int[max + 1];
        for (int i = 0; i < All.Length; i++) map[(int)PileFor(All[i])] = (int)All[i] + 1;
        return map;
    }

    /// <summary>Which sub-stat this pile holds, if it holds one at all. False for every pile that is not a sub-stat
    /// (Guard, Shield, Parry, the ailments…), so a caller can walk every live state and pick out only these.</summary>
    public static bool TryStatFor(StateId pile, out SubStat stat)
    {
        int i = (int)pile;
        int v = (i >= 0 && i < statForPile.Length) ? statForPile[i] : 0;
        stat = (SubStat)(v - 1);
        return v > 0;
    }

    /// <summary>True when the authored number is a FRACTION OF THE STAT (0.3 = +30% of what you already have); false
    /// when it is added straight on in the stat's own units.
    ///
    /// <para>The split is not a style choice: a percentage of a stat that sits at zero for most builds — regen, leech,
    /// reflect, life steal, crit chance — is always zero, so a percentage knob there would be a knob that does
    /// nothing. Those stats take the number as-is instead.</para></summary>
    public static bool IsPercent(SubStat s)
    {
        switch (s)
        {
            case SubStat.HPRegen:
            case SubStat.MPRegen:
            case SubStat.ManaLeech:
            case SubStat.Reflect:
            case SubStat.LifeSteal:
            case SubStat.CritChance:
            case SubStat.CritDamage: return false;
            default:                 return true;
        }
    }

    /// <summary>What the authored number means, for the composer field and the tooltip — so an author is never left
    /// guessing whether they are typing a percentage or an amount.</summary>
    public static string UnitLabel(SubStat s)
    {
        if (IsPercent(s)) return "0.3 = +30%";
        switch (s)
        {
            case SubStat.HPRegen: return "HP per second";
            case SubStat.MPRegen: return "MP per second";
            case SubStat.CritDamage: return "0.5 = +50% crit damage";
            default: return "0.3 = 30%";
        }
    }
}

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

    [Tooltip("ON = this effect's numbers may answer to the pile this cast is cashing. Each number then carries its own " +
             "operation and reading, right beside it — a damage multiplier can build on the pile while a duration " +
             "ignores it. OFF = every number is a plain number, which is what an effect is by default.")]
    public bool useStacks = false;

    [Header("Damage")]
    [Tooltip("ON = this strike IS an ordinary attack: the caster swings by its own rules — its crit, its accuracy " +
             "against the target's dodge (so it CAN miss), its weapon's element — and a landed strike announces a " +
             "basic attack, so it feeds Momentum and every on-basic passive. The ATK multiplier still scales it (1 = " +
             "one normal swing, 2 = twice one). Damage type and element are the weapon's then, not this effect's.")]
    public bool resolveAsBasicAttack = false;
    [Tooltip("How the hit resolves: Physical (ATK vs armour) / Magic (MATK, unmissable, no crit) / Crit (physical " +
             "that pierces armour & always crits). Default Magic — matches every pre-migration bool-magic skill.")]
    public DamageType damageType = DamageType.Magic;
    [Tooltip("ATK/MATK MULTIPLIER — how many of the caster's own hits this strike is worth (1 = one hit; a heavy nuke " +
             "is higher). The roll it multiplies is a physical ATK roll or a magic MATK roll, chosen by the damage " +
             "type above. Affinity picks WHICH stats scale it; this sets HOW BIG. No skill levels.")]
    public ScaledNumber damageMult = new ScaledNumber(1f);
    [Tooltip("Attack element (RO property), checked against the target's armor element.")]
    public Element element = Element.Neutral;

    [Header("Heal")]
    [Tooltip("Flat HP restored (Heal kind).")]
    public ScaledNumber amount = new ScaledNumber(10f);

    [Header("Status / duration / magnitude")]
    [Tooltip("Which ailment to inflict (Status kind).")]
    public Ailment ailment = Ailment.Burn;
    [Tooltip("Duration in seconds — 0 for an instant/action effect. Only durational STATE kinds use it (the ailments, " +
             "Sub Stat Buff / Debuff, Shield, Parry, Brace, the outgoing-damage charges, Immune To Disables); the " +
             "composer keeps it 0 for everything else. The buff tray / nameplate shows an icon " +
             "iff this is > 0 — i.e. only for ongoing CONDITIONS, never for movement actions.")]
    public ScaledNumber seconds = new ScaledNumber(0f);
    [Tooltip("Knockback / Pull ONLY: how long the shove MOVEMENT lasts (seconds). This is movement time, NOT a state " +
             "duration — a shove isn't a tracked condition, so it never shows a buff/nameplate icon.")]
    public ScaledNumber shoveSeconds = new ScaledNumber(0.25f);
    [Tooltip("What this number MEANS depends on the kind:\n\n" +
             "• Poison / Bleed / Burn: damage per tick. Stun / Freeze / Silence use none — being stopped is binary, " +
             "so the whole knob is how long it lasts.\n" +
             "• Sub Stat Buff / Debuff: how far the chosen sub-stat moves. The Sub stat field below says whether that " +
             "reads as a fraction (0.3 = +30%) or a plain amount (HP per second) — a debuff is authored POSITIVE and " +
             "negated on the way in, so nobody types a minus sign to make something worse.\n" +
             "• Boost / Reduce Outgoing Damage: the bonus or the cut ONE charge is worth.\n" +
             "• Execute: execute power — the bonus is roll × this × the target's missing HP fraction.\n" +
             "• Knockback / Pull: shove speed.")]
    public ScaledNumber magnitude = new ScaledNumber(3f);

    [Header("Stacking (STATE effects only)")]
    [Tooltip("OFF = one instance; re-applying REPLACES it (newest wins) — today's behaviour. ON = each application ADDS " +
             "stacks, every stack carrying its own duration and its own magnitude; the total is their SUM.\n\n" +
             "IMPORTANT: a stacking effect's magnitude is the PER-STACK amount. On a Sub Stat Buff / Debuff it is the " +
             "BONUS, never a multiplier — stacking or not: author 0.03 for '+3% per stack', so 10 stacks read as +30%.")]
    public bool stacking = false;
    [Tooltip("Max stacks held at once. Past it, the OLDEST stack drops. Hard-capped by the store's safety ceiling.")]
    public ScaledNumber stackLimit = new ScaledNumber(5f);
    [Tooltip("How many stacks ONE application grants — this is the 'charges' knob. 3 = a cast hands out 3 charges that " +
             "share one window and are spent one at a time.")]
    public ScaledNumber stacksPerApply = new ScaledNumber(1f);

    [Tooltip("ON = this effect's stack count is NOT authored — it is worked out at cast time from how many times the " +
             "caster would swing during this effect's own duration (duration / seconds-per-swing). Faster attack " +
             "speed therefore means more stacks in the SAME window, never a longer one. Max stacks and stacks per " +
             "use no longer apply: the count is whatever the caster's speed earns.")]
    public bool stacksFromAttackSpeed = false;
    [Tooltip("This effect's SHARE of that count. 1 = the full count; 0.5 = half of it, rounded up. Two effects on one " +
             "skill sharing a duration stay in exact proportion for free — half is always half, at any attack speed, " +
             "with nothing to re-author when the duration changes.")]
    public ScaledNumber stackShare = new ScaledNumber(1f);

    [Header("Dash")]
    [Tooltip("Dash travel speed as a multiple of the caster's CURRENT run speed (8 = 8× run speed) — the visible fast " +
             "glide to the aimed point. Higher = snappier gap-close. Only the Dash kind uses this.")]
    public ScaledNumber dashSpeedMult = new ScaledNumber(8f);
    [Tooltip("Dash: ease IN — accelerate from a slow start up to full speed over the ease-window seconds. Off = full speed instantly.")]
    public bool dashEaseIn = false;
    [Tooltip("Dash: ease OUT — decelerate to a gentle stop over the ease-window seconds before arrival. Off = full speed " +
             "until arrival. (Both on = slow-fast-slow.)")]
    public bool dashEaseOut = false;
    [Tooltip("Dash: how long (seconds) each ease ramp lasts — the accelerate / decelerate window. TIME-based, so the " +
             "ramp reads the same at any distance; on a dash shorter than the window the ramps fill the whole trip. " +
             "Only used when ease in / out is on.")]
    public ScaledNumber dashEaseSeconds = new ScaledNumber(0.3f);

    [Tooltip("Dash / Blink: leave a FROZEN COPY of the mover where it stood, fading out behind it — so a fast chain " +
             "of hops reads as several of you at once. 0 = off.")]
    public ScaledNumber afterimageSeconds = new ScaledNumber(0f);
    [Tooltip("Colour of that frozen copy. Alpha = how solid it starts before fading to nothing.")]
    public Color afterimageTint = new Color(0.6f, 0.85f, 1f, 0.5f);
    [Tooltip("Dash: how see-through the MOVER's own body is WHILE IT TRAVELS — solid again the instant it lands, so " +
             "the fade is always exactly one hop long. 1 = solid (no fade). Lower = more see-through, so a hop reads " +
             "as a blur rather than a person sliding. Blink arrives instantly, so it has no travel to fade.")]
    public ScaledNumber casterOpacity = new ScaledNumber(1f);

    [Tooltip("Dash / Blink: how many CORNERS the repeated landings trace. Each hop crosses to the far side of the " +
             "target and steps on, so this many hops in a row draw a star THROUGH it rather than walking a ring " +
             "around it. 5 = the classic five-pointed star. Must be ODD or the figure never closes — an even value " +
             "is nudged up by one.")]
    public ScaledNumber starCorners = new ScaledNumber(5f);

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
    public ScaledNumber modifierSeconds = new ScaledNumber(1f);
    [Tooltip("How many stacks are added to that pile, each a copy of its newest. Add stacks/charges only.")]
    public ScaledNumber modifierStacks = new ScaledNumber(1f);

    [Header("Multi-hit (Damage kind)")]
    [Tooltip("How many times a Damage effect strikes per landing. Each strike is the TRIGGER POINT for the binding's " +
             "other effects — every state and proc rolls its chance once per hit. 1 = single hit.")]
    public ScaledNumber hitCount = new ScaledNumber(1f);

    [Header("Proc")]
    [Tooltip("Chance this effect happens at all (0..1), rolled PER HIT of the binding's multi-hit Damage. 1 = certain, " +
             "which is what every effect is unless its author says otherwise; 0.25 = a gamble taken on each hit. " +
             "0 means the effect can NEVER happen — the skill-data check reports it rather than letting it rot.\n\n" +
             "The roll belongs to the EFFECT, so it happens the same way whatever moment started the skill — a press, " +
             "a kill, a parry. A caster-side effect is applied once per landing, so it rolls once.")]
    public ScaledNumber chance = new ScaledNumber(1f);

    /// <summary>WHICH state entry this effect creates, or false when it creates none. THE one answer to "what pile
    /// does this effect leave behind", so a consumer's authoring list can never offer a pile that will not exist.
    ///
    /// <para>It must agree with <c>SkillEffectApplier.ApplyOne</c>, which is where each kind actually lands — every
    /// line below names the site it mirrors. Kinds absent from the switch make no state at all: the instant ones
    /// (Damage, Heal, Dash…), and <c>Knockback</c> / <c>Pull</c>, which are instant shoves with no window to hold.</para></summary>
    public static bool TryStateFor(SkillEffect e, out StateId id)
    {
        id = default;
        if (e == null) return false;
        switch (e.kind)
        {
            // Ailment i ↔ StateId i, matching enum order (StatusController.Apply).
            case SkillEffectKind.Status:        id = (StateId)(int)e.ailment; return true;
            // One pile per sub-stat, buff and debuff alike — SubStats.PileFor is the single owner of that mapping.
            case SkillEffectKind.SubStatBuff:
            case SkillEffectKind.SubStatDebuff: id = SubStats.PileFor(e.subStat); return true;
            case SkillEffectKind.Parry:         id = StateId.Parry;         return true;
            case SkillEffectKind.BoostOutgoingDamage: id = StateId.OutgoingDamageBoost; return true;
            case SkillEffectKind.ReduceOutgoingDamage: id = StateId.OutgoingDamagePenalty; return true;
            case SkillEffectKind.ImmuneToDisables: id = StateId.DisableImmune; return true;
            // A taunt holds a real window on whoever it lands on (UnitStates.BeginTaunt) — the lock the skill door and
            // both brains read.
            case SkillEffectKind.Taunt:         id = StateId.Taunt;         return true;
            // The ARMED charges. Its debt (StateId.DeferredDamage) is created by incoming hits, not by this apply,
            // so it is not something this effect hands out.
            case SkillEffectKind.Brace:         id = StateId.Brace;         return true;
            case SkillEffectKind.Shield:        id = StateId.Shield;        return true;
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

    [Header("Who it lands on")]
    [Tooltip("WHO this effect is applied to. Caster = always you. Target = always whoever the tick reaches, so a " +
             "shield, a regen or a stat buff can be placed on ANY unit — including a mob. Every effect answers this " +
             "outright; there is no 'whatever this kind usually does', because that is what once turned an authored " +
             "enemy debuff into a self-inflicted one.")]
    // The composer stamps this the moment an effect is created and again whenever its kind changes (StampSide), so the
    // field initializer is only ever seen by an effect built in code. Caster is the conservative one: a stray self-buff
    // is a wasted cast, a stray enemy-side effect would be aimed at somebody.
    public EffectSide side = EffectSide.Caster;

    [Header("Sub stat (Sub Stat Buff / Debuff kinds)")]
    [Tooltip("Which sub-stat this moves for its window. magnitude = how far (a fraction of the stat for most, an " +
             "amount for the ones whose base is normally zero — the field says which); seconds = duration " +
             "(this DOES scale off sustain stats via durationAffinity). The magnitude itself is FLAT — support value " +
             "comes from uptime, not a bigger number.")]
    public SubStat subStat = SubStat.Damage;

    // A buff that GREW while it was up (a delay, a step size, an interval, a ceiling — four knobs on one effect) was
    // removed 2026-08-17: no shipped skill ever set them, and a buff whose value depends on how long it has been
    // running can't be read off the screen or balanced against a flat one. A buff is worth what it says it is worth;
    // uptime is the support's contribution. Don't reintroduce it as an effect — it would be a standing change with a
    // clock, which the Modifiers rule already forbids.

    [Header("Affinity (ring-investment scaling)")]
    [Tooltip("Scales this effect's main OUTPUT by ring investment — its damage / heal amount / DoT damage / buff " +
             "amount / slow amount. Weights are relative; empty = no scaling (×1).")]
    public List<StatWeight> affinity = new List<StatWeight>();
    [Tooltip("Scales this effect's CHANCE by ring investment, so investment makes it land more often. Its own " +
             "channel, never the output one: a gamble can grow in TWO directions (how often, how big) and one list " +
             "can't say which. Empty = the authored chance, unscaled (×1). Nothing to scale on a certain effect.")]
    public List<StatWeight> chanceAffinity = new List<StatWeight>();
    [Tooltip("Scales this effect's DURATION (its seconds) by ring investment. Empty = duration unscaled (×1).")]
    public List<StatWeight> durationAffinity = new List<StatWeight>();
    [Tooltip("Damage kind only: scales the number of STRIKES (hitCount) by ring investment — more investment = more " +
             "hits in the flurry. hits = round(hitCount × mult). Empty = hit count unscaled (×1).")]
    public List<StatWeight> hitCountAffinity = new List<StatWeight>();

    /// <summary>WHO an authored effect lands on. The author's choice, not a property of the effect kind — every kind
    /// works on every unit now (see <see cref="SkillEffectApplier"/>'s single apply switch), so "a shield can only be
    /// cast on yourself" is a DESIGN decision made per skill, not a limit of the engine.
    ///
    /// <para>There are exactly TWO answers, and every effect carries one. There is deliberately no "Auto" / "whatever
    /// this kind traditionally does": a default that resolves differently per kind is unreadable off the asset, and it
    /// shipped a broken skill — an authored "cut THEIR attack speed" resolved to the CASTER, because sub-stat changes
    /// were traditionally self, and it cut the player's own speed while every checker passed (Offbeat, 2026-08-24).
    /// The old Auto was removed and all 46 authored sides were rewritten to what they resolved to that day, so nothing
    /// changed behaviour and nothing can hide behind a default again. Serialized by int — never reorder.</para></summary>
    public enum EffectSide
    {
        /// <summary>Always the acting caster, whatever the tick is aimed at.</summary>
        Caster,
        /// <summary>Always whoever the tick reaches — including a mob, for kinds that used to be caster-only.</summary>
        Target,
    }

    /// <summary>Resolve where this effect actually lands. THE predicate the runtime partitions on and the composer
    /// groups by, so what an author sees can't drift from what runs. Now a plain read of the authored answer.</summary>
    public static bool LandsOnCaster(SkillEffect e)
        => e != null && e.side == EffectSide.Caster;

    /// <summary>Does this kind ask <see cref="chance"/> whether it happens? Every STATE does (a buff, an ailment, a
    /// charge pile, a cleanse) plus Execute — one list, read by the runtime roll, the composer's slider and the
    /// skill-data check, so a kind can never be offered a chance the game then ignores.
    ///
    /// <para>Damage is deliberately absent: how many times it lands is <see cref="hitCount"/>, and a second knob for
    /// the same question would be two doors to one room. So are the movement verbs and the modifiers — a dash that
    /// sometimes doesn't happen, or a cooldown cut that sometimes isn't cut, is a bug wearing a feature's clothes.</para></summary>
    public static bool RollsChance(SkillEffectKind k) =>
        k != SkillEffectKind.Damage && k != SkillEffectKind.Heal && !IsModifier(k) && !NeedsPoint(k) &&
        k != SkillEffectKind.Knockback && k != SkillEffectKind.Pull;

    // IsSelfVerb — the list of kinds that were "traditionally self" — is GONE (2026-08-24). It existed only to resolve
    // EffectSide.Auto, and Auto is gone: every effect names its own side now, so a kind no longer has a side at all.
    // Do not reintroduce it. A per-kind default is exactly what let an authored enemy debuff land on the caster.

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
