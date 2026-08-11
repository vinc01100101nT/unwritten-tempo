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
    /// <para>MUST stay last in the enum — Unity serializes the kind by int, so appending is the only safe add.</para></summary>
    Parry,
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
    [Tooltip("Status: DoT damage-per-tick, Slow factor (0..1), or Blind HIT loss. Haste/Buff: multiplier (>1). " +
             "Guard: damage-cut fraction (0..1). Regen: HP per tick. Vulnerability: extra-damage fraction (0.3 = +30%). " +
             "Knockback/Pull: shove speed. ManaLeech/Reflect: fraction of damage dealt/taken (0..1).")]
    public float magnitude = 3f;

    [Header("Stacking (STATE effects only)")]
    [Tooltip("OFF = one instance; re-applying REPLACES it (newest wins) — today's behaviour. ON = each application ADDS " +
             "stacks, every stack carrying its own duration and its own magnitude; the total is their SUM.\n\n" +
             "IMPORTANT: a stacking effect's magnitude is the PER-STACK amount, and for multiplier-style channels " +
             "(Haste / Stat Buff) it is the BONUS, not the multiplier — author 0.03 for '+3% per stack', so 10 stacks " +
             "read as +30%. A non-stacking effect keeps using the absolute value (1.5 = x1.5).")]
    public bool stacking = false;
    [Tooltip("Max stacks held at once. Past it, the OLDEST stack drops. Hard-capped by the store's safety ceiling.")]
    [Min(1)] public int stackLimit = 5;
    [Tooltip("How many stacks ONE application grants — this is the 'charges' knob. 3 = a cast hands out 3 charges that " +
             "share one window and are spent one at a time.")]
    [Min(1)] public int stacksPerApply = 1;

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

    [Header("Multi-hit (Damage kind)")]
    [Tooltip("How many times a Damage effect strikes per landing. Each strike is the TRIGGER POINT for the binding's " +
             "PROC effects (a Status or Execute with a chance) — they roll once per hit. 1 = single hit.")]
    [Min(1)] public int hitCount = 1;

    [Header("Proc")]
    [Tooltip("PROC chance (0..1), rolled PER HIT of the binding's multi-hit Damage. Used by Status (e.g. a stun proc) " +
             "and Execute. 0 = NOT a proc — the effect applies ONCE per landing instead.")]
    [Range(0f, 1f)] public float chance = 0f;

    [Header("Buff (Buff kind)")]
    [Tooltip("Which self-stat the Buff kind lifts for its window. magnitude = the FLAT multiplier (AMPLIFY buffs " +
             "are NOT affinity-scaled — support value comes from uptime, not a bigger number); seconds = duration " +
             "(this DOES scale off sustain stats via durationAffinity).")]
    public BuffStat buffStat = BuffStat.Damage;

    [Header("Buff RAMP (Damage buff only — EXPERIMENTAL, design 'Support Scaling')")]
    [Tooltip("Seconds before the ramp begins. 0 (with rampPerStep 0) = a plain flat buff, no ramp. Only the Damage " +
             "buffStat ramps for now (War Banner prototype).")]
    [Min(0f)] public float rampDelay = 0f;
    [Tooltip("Multiplier ADDED each interval once ramping (0.05 = +5% Atk per step). 0 = no ramp.")]
    [Min(0f)] public float rampPerStep = 0f;
    [Tooltip("Seconds between ramp steps.")]
    [Min(0.1f)] public float rampInterval = 5f;
    [Tooltip("Ceiling for the ramped multiplier (1.5 = caps at +50%). 0 = uncapped. Longer duration (more MP) reaches " +
             "a higher point on the ramp before it ends — that's the support's contribution.")]
    [Min(0f)] public float rampCap = 0f;

    [Header("Affinity (ring-investment scaling)")]
    [Tooltip("Scales this effect's main OUTPUT by ring investment — its damage / heal amount / DoT damage / buff " +
             "amount / slow amount, or (for a proc) its CHANCE. Weights are relative; empty = no scaling (×1).")]
    public List<StatWeight> affinity = new List<StatWeight>();
    [Tooltip("Scales this effect's DURATION (its seconds) by ring investment. Empty = duration unscaled (×1).")]
    public List<StatWeight> durationAffinity = new List<StatWeight>();
    [Tooltip("Damage kind only: scales the number of STRIKES (hitCount) by ring investment — more investment = more " +
             "hits in the flurry. hits = round(hitCount × mult). Empty = hit count unscaled (×1).")]
    public List<StatWeight> hitCountAffinity = new List<StatWeight>();

    /// <summary>A "self verb" ALWAYS acts on the caster, never on a gathered entity. Canonical list (runtime + editor
    /// share it — no duplicate). Heal is intentionally NOT here anymore: it can target others, so its landing is
    /// decided by the tick's <see cref="TargetMode"/> like any normal effect.
    ///
    /// <para>This is the PARTITION <see cref="SkillEffectApplier"/> resolves by: a self verb runs in the caster pass
    /// (once per tick, whatever the targeting says — "who does this tick hit" was never the question it answers) and
    /// is excluded from the target pass; everything else is target-side and runs only on gathered entities. So a
    /// Single-target tick aimed at a mob still dashes the caster, and a Self-targeted tick can't dash them twice.
    /// Adding a kind? Classify it here, then implement it in exactly ONE switch of the applier —
    /// <c>ApplyOneToCaster</c>, or <c>ApplyOneToMob</c>/<c>ApplyOneToPlayer</c>.</para>
    ///
    /// <para>ROADMAP: the goal is for EVERY kind to work on both sides (an authored effect chooses its side by which
    /// composer section it sits in), which needs the mob-side sinks the player already has — see the caster/target
    /// section work. Until those land, this list is what the caster pass owns.</para></summary>
    public static bool IsSelfVerb(SkillEffectKind k) =>
        k == SkillEffectKind.Guard || k == SkillEffectKind.Buff || k == SkillEffectKind.Regen ||
        k == SkillEffectKind.Cleanse || k == SkillEffectKind.ManaLeech || k == SkillEffectKind.Reflect ||
        k == SkillEffectKind.Shield || k == SkillEffectKind.Parry || NeedsPoint(k);

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
