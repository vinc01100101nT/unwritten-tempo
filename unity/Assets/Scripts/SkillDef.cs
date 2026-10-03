using System.Collections.Generic;
using UnityEngine;

/// <summary>How a skill picks WHERE it plays — decides how the <see cref="SkillRunner"/> builds the
/// <see cref="VfxContext"/> before playing the composed VFX. LEGACY: superseded by the per-tick
/// <see cref="SkillTickBinding.targetMode"/>; kept only so old assets migrate (the cast reticle is now DERIVED
/// from the ticks — see <see cref="SkillDef.Reticle"/>).</summary>
public enum SkillAim
{
    Self,
    Target,
    Ground,
}

/// <summary>The ONE targeting decision, per tick — the single knob that replaces the old skill-level <c>aim</c> +
/// per-tick <c>scope</c>. Each option means exactly one thing; the composer reveals sub-options beneath it.</summary>
public enum TargetMode
{
    /// <summary>No aiming — the effects land on the caster. (Self buffs / heal-self / self-dash-in-place.)</summary>
    Self,
    /// <summary>Hover-pick ONE entity, validated against <see cref="SkillTickBinding.affects"/> (must be a matching
    /// kind). The effects land on that entity. (A single-target nuke, a targeted heal.)</summary>
    SingleTarget,
    /// <summary>Aim a point on the GROUND (no entity validation — aiming over a unit just takes the ground under it).
    /// How the point is used is the <see cref="SkillTickBinding.groundDelivery"/> sub-option.</summary>
    Ground,
}

/// <summary>For a <see cref="TargetMode.Ground"/> tick: how the aimed point is delivered.</summary>
public enum GroundDelivery
{
    /// <summary>Exactly the clicked spot — no entity gather. The caster/effect resolves AT the point (Dash / Blink go
    /// there; a placed hazard spawns there).</summary>
    Point,
    /// <summary>Splash: hit every <see cref="SkillTickBinding.affects"/>-matching entity within <see
    /// cref="SkillTickBinding.radius"/> of the aimed spot (the cast-circle reticle). Effect lands AT the spot the
    /// player picks.</summary>
    Aoe,
    /// <summary>A directional projectile: the reticle is a line-arrow (aim a DIRECTION, not a landing spot) and this
    /// tick fans <see cref="SkillTickBinding.projectileCount"/> copies of ITS OWN carrier step. The flying VFX (a
    /// Carrier with spread ticks) raises ticks along its path, each landing as a small AoE
    /// (<see cref="SkillTickBinding.radius"/> = hit width) at the projectile's live position. This — NOT the VFX's
    /// Carrier step — is the single owner of "this tick is a projectile" (see <see cref="SkillDef.IsProjectileAim"/>
    /// for the cast-level aim it derives).</summary>
    Projectile,
}

/// <summary>PvP faction filter, used when <see cref="SkillTargets.Player"/> is an affected kind. Enemy-player tagging
/// is future work — for now every player reads as <see cref="Ally"/>; the tag is authored NOW so PvP can switch it on
/// later without touching authored skills.</summary>
public enum PlayerFaction { Ally, Enemy }

/// <summary>The per-cast reticle the player uses — DERIVED from the skill's ticks, never authored. Any Single-target
/// tick ⇒ pick a Unit; else any Ground tick ⇒ a Ground reticle; else Instant (self / no aim).</summary>
public enum CastReticle { Instant, Ground, Unit }

/// <summary>The SCALE of a skill (orthogonal to its category/role). An Ultimate is a normal skill stamped with
/// this tier — it sits in any hotbar slot like every other skill, and every ultimate a unit has shares one cooldown
/// after any of them is cast (<see cref="SharedCooldown"/>). Zero new architecture.</summary>
public enum SkillTier { Base, Ultimate }

/// <summary>The battlefield-role archetype (the skill's TAG) — the coherence family from the design doc. Drives the
/// tooltip chip + color; elements are deferred, so this is the identity that matters.</summary>
public enum SkillArchetype { Burst, Tempo, Fortress, Buff, Curse }

/// <summary>The attribute FAMILY a skill belongs to — the RPS-triangle cluster the player steers into (design doc
/// "Combat Ontology"). Orthogonal to <see cref="SkillArchetype"/> (a verb can live in any family). Also the DEFAULT
/// source for a damage effect's <see cref="DamageType"/> (Might→Physical, Arcane→Magic, Finesse→Crit) — but any
/// effect may override that per-effect.</summary>
public enum SkillFamily { Might, Arcane, Finesse }

/// <summary>The spatial DELIVERY of a skill (design doc: single / area / aura / self / dash). Duration and aim are
/// separate properties, NOT shapes (AoE-vs-zone was merged into <see cref="Area"/>).</summary>
public enum SkillShape { Single, Area, Aura, Self, Dash }

/// <summary>Designer-facing production metadata (NOT gameplay, never shown to players) — lets "give the early player
/// another simple Burst" be a searchable query when the library grows to ~80 skills.</summary>
public enum SkillComplexity { Low, Medium, High }

/// <summary>Designer-facing organisation metadata (NOT gameplay) — keeps early and late skills from intermixing in
/// the authoring lists.</summary>
public enum AcquisitionTier { Early, Mid, Late }

/// <summary>What makes a skill RUN. <see cref="OnCast"/> = a normal active the player presses (default; it takes a
/// hotbar slot and uses the cost/cadence below). Anything else = a PASSIVE: no slot, it runs its bindings when that
/// combat moment fires, dispatched through the ONE <see cref="PassiveEvents"/> choke point.
///
/// <para><b>Two shapes of trigger, on purpose:</b> <see cref="OnSkillCastComplete"/> is PARAMETERIZED by
/// <see cref="SkillDef.triggerSkillId"/> — it fires when THAT specific active finishes casting uninterrupted (this
/// one value replaces a per-skill event like "OnLunge", because the skill set is open-ended). The remaining values are
/// the NATURAL game events — a small, fixed, closed set — so they're listed flat, no parameter.</para>
///
/// <para><b>Serialized by int</b>: <see cref="OnCast"/> MUST stay 0 (old assets have no <c>trigger</c> field → they
/// deserialize as 0 = OnCast), and new values MUST be appended, never inserted.</para></summary>
/// <summary>HOW an effect MEASURES the pile its cast is cashing — separate from what that measurement BUYS, which is
/// each authored NUMBER's own business. Two independent questions: this one is "how
/// much is there?", and every effect that pulls from the pile answers it for itself.
///
/// <para>Serialized by int: <see cref="Count"/> MUST stay 0 (assets authored before this existed deserialize as 0 and
/// keep their exact behaviour), and new values are APPENDED, never inserted.</para></summary>
public enum StackReading
{
    /// <summary>HOW MANY stacks are in the pile. The tally reading — a pile of ten identical marks is worth ten.
    /// (Momentum → Deathblow.)</summary>
    Count = 0,
    /// <summary>The SUM OF WHAT THE STACKS CARRY. The amount reading — for a pile whose stacks each hold a number
    /// rather than just existing. (Brace's delayed damage → Retribution.)</summary>
    CarriedTotal,
}

/// <summary>HOW a number meets the pile's reading. The four ordinary operations, nothing else — a knob an author can
/// read off the row without being taught a rule.
///
/// <para>Serialized by int: <see cref="Add"/> MUST stay 0 and new values are APPENDED, never inserted.</para></summary>
public enum StackOp
{
    Add = 0,
    Subtract,
    Multiply,
    Divide,
}

/// <summary>ONE authored number, anywhere on any effect — a damage multiplier, a duration, a heal's HP, a proc chance,
/// a shove's speed. It is a plain number by default and stands entirely on its own; ticking
/// <see cref="useStacks"/> lets it ALSO answer to the pile the cast is cashing, through an operation and a reading it
/// carries itself.
///
/// <para>That is the whole point of this type existing: the pile is not a mode an EFFECT is in, it is an option each
/// NUMBER may take, so every number on every kind gets the same treatment for free and no kind can drift from
/// another. A number with the box unticked behaves exactly as a bare field always did.</para></summary>
[System.Serializable]
public struct ScaledNumber
{
    [Tooltip("The number itself. It stands on its own — an effect never needs the pile to work.")]
    public float value;
    [Tooltip("ON = this number ALSO answers to the pile this cast is cashing, through the operation and reading beside it.")]
    public bool useStacks;
    [Tooltip("How this number meets the pile's reading.")]
    public StackOp op;
    [Tooltip("Count = how many stacks the pile holds. Carried total = the sum of what those stacks carry.")]
    public StackReading read;
    [Tooltip("What the reading is multiplied by before it meets this number. A stack count and a banked total are raw " +
             "tallies, so this is what turns them into the units this number is measured in.")]
    public float scaler;

    public ScaledNumber(float v)
    {
        value = v; useStacks = false; op = StackOp.Add; read = StackReading.Count; scaler = 1f;
    }

    /// <summary>An ordinary number where no authoring is involved — a code-side constant handed to the same call.</summary>
    public static ScaledNumber Of(float v) => new ScaledNumber(v);

    /// <summary>THE resolve, and the only one — every number on every effect comes through here, so there is exactly
    /// one place where what a number MEANS is decided:
    ///
    /// <para><c>(this number × basis)  op  (reading × scaler)  × ring</c></para>
    ///
    /// <para><paramref name="basis"/> is what this number is applied TO, and it is the reason the two sides of the
    /// operation are in the same units. A damage multiplier's basis is the caster's rolled hit, so its left side is
    /// real damage and the pile's side — a raw count or banked total, converted by the scaler — is real damage too.
    /// Every other number stands alone, so its basis is 1 and the left side is simply the number.</para>
    ///
    /// <para>Divide guards its own zero: dividing by an empty pile would be an error the author can neither see nor
    /// predict, so a zero reading leaves the number untouched instead.</para></summary>
    public float Resolve(in StackVars vars, float ringMult = 1f, float basis = 1f)
    {
        float own = value * basis;
        if (!useStacks) return own * ringMult;
        float r = vars.Read(read) * scaler;
        float outv;
        switch (op)
        {
            case StackOp.Subtract: outv = own - r; break;
            case StackOp.Multiply: outv = own * r; break;
            case StackOp.Divide:   outv = r == 0f ? own : own / r; break;
            default:               outv = own + r; break;
        }
        return outv * ringMult;
    }

    // "one per three" typed as 0.3333 lands at 0.9999, and cutting that down gives ZERO — a rate that reads as broken
    // for a reason no author could ever see. A nudge a ten-thousandth wide fixes exactly that and is far too small to
    // change any number anybody actually means.
    const float FloorNudge = 0.001f;

    /// <summary>The whole-number form. A pile-driven answer is cut DOWN, so a number that has not yet earned a whole
    /// unit does not get one; with no pile there is no rate to protect, so it rounds the way a bare field always did.</summary>
    public int ResolveInt(in StackVars vars, float ringMult = 1f, float basis = 1f) =>
        useStacks ? Mathf.Max(0, Mathf.FloorToInt(Resolve(vars, ringMult, basis) + FloorNudge))
                  : Mathf.Max(0, Mathf.RoundToInt(value * basis * ringMult));

    /// <summary>The number as authored, with no pile and no ring — for previews, tooltips and any reader that wants
    /// what the designer typed rather than what a live cast would produce.</summary>
    public float Raw => value;

    public override string ToString() => value.ToString("0.####");
}

/// <summary>The cast's stack readings, measured ONCE when it starts and handed to every effect it runs — so any
/// number of effects on one skill can each pull from the same pile without re-reading it (and without the first one
/// spending it out from under the rest). Retribution is the case that needs this: its damage and its lifesteal are
/// two effects reading ONE banked total.
///
/// <para>A readonly struct passed by value — no allocation on the combat path, and nothing downstream can alter what
/// the cast measured.</para></summary>
public readonly struct StackVars
{
    /// <summary>How many stacks the cashed pile held. <see cref="StackReading.Count"/>.</summary>
    public readonly int Count;
    /// <summary>The summed value those stacks carried, already in plain units — a percentage pile reads as its
    /// decimal (+30% ⇒ 0.3). <see cref="StackReading.CarriedTotal"/>.</summary>
    public readonly float Carried;

    public StackVars(int count, float carried) { Count = count; Carried = carried; }

    /// <summary>Read the variable an effect asked for. The ONE place the choice is resolved, so an effect can never
    /// read one reading while its label claims the other.</summary>
    public float Read(StackReading reading) => reading == StackReading.CarriedTotal ? Carried : Count;

    public static readonly StackVars None = new StackVars(0, 0f);
}

/// <summary>Every moment a skill can start from. Each member below documents its EXACT fire-site and the precise
/// condition that site checks, read from that code — an author picking a trigger must never have to guess whether a
/// whiffed swing, an absorbed hit or a parried blow counts. The composer shows the same text per selection.
///
/// <para><b>Naming is load-bearing.</b> <c>…Dealt</c> = it happened outward, from you. <c>…Received</c> = it happened
/// to you. A <c>Damage…</c> name means the moment REQUIRES damage to have actually reached HP; a name without it
/// (<see cref="OnBasicAttackStart"/>) does not.</para>
///
/// <para><b>NEVER ADD AN "OnRepeat" TRIGGER — a beat of a repeating cast is NOT a moment.</b> Every member here is
/// something that happened in the fight; a repeat is the SAME cast still running, which is why the repeat spends its
/// charge directly (<see cref="SkillDef.repeatCostsCharge"/>) instead of announcing anything. Give a beat a trigger
/// and a skill can answer its own beat, which re-enters the skill, which beats again — an unbounded loop that also
/// pays no charge, because the pile is only spent by the run that owns it. This is the same trap the
/// <c>OnSkillCastComplete</c> fire-site avoids by announcing only on a PLAYER press: it is what stops a granter and a
/// consumer refilling each other forever. A repeating cast reads its pile; it must never raise a moment.</para></summary>
public enum SkillTrigger
{
    /// <summary>THE PLAYER PRESSED IT. The only trigger that takes a hotbar slot and the only one that pays the
    /// skill's MP + cooldown; every other moment fires the skill free, because the moment IS the gesture.</summary>
    OnCast = 0,

    /// <summary>PARAMETERIZED by <see cref="SkillDef.triggerSkillId"/> — the ONE named active finished its wind-up
    /// without being interrupted. Fires at <c>SkillRunner.cs:312</c>, gated on <c>pressed</c>, so it means "THE PLAYER
    /// CAST THAT SKILL": a run of the same skill started by one of its own non-OnCast triggers does NOT announce.
    /// That gate is what stops a granter/consumer pair (Lock Blades ⇄ Lunge) from refilling itself forever.
    /// No entity is passed — nothing was necessarily hit.</summary>
    OnSkillCastComplete,

    /// <summary>A BASIC ATTACK YOU SWUNG DEALT DAMAGE. Fires at <c>Player3DAttacker.cs:255</c>, inside
    /// <c>if (dealt &gt; 0)</c> — so a MISS fires nothing, and skill strikes never fire it (they aren't basics).
    /// Entity = the mob you hit. This is Momentum's moment: it paces off attack speed, which is why attack speed is
    /// the lever on any stack pile built from it.</summary>
    OnBasicAttackDealt,

    /// <summary>A MOB DIED. Fires at <c>Health.cs:390</c> in <c>Die()</c>, before the corpse is pooled, with the dying
    /// mob as the entity (read synchronously — it may not exist a frame later).</summary>
    OnKill,

    /// <summary>YOUR HP CROSSED THE LOW THRESHOLD — <c>LowHpFrac</c> = <b>0.3</b> (<c>Character.cs:875</c>). Fires at
    /// <c>Character.cs:898</c> only on the FALLING EDGE (above the line before this hit, at-or-below after) and only
    /// if the hit LEFT YOU ALIVE, so it re-arms after any heal with no flag to reset. Damage-over-time crossings do
    /// NOT fire it — they never reach that line. No entity is passed.</summary>
    OnLowHP,

    /// <summary>YOU DEALT DAMAGE THAT LANDED. Fires at <c>Health.cs:312</c> inside <c>ApplyLeech</c>, which bails on
    /// <c>dmgDealt &lt;= 0</c> (<c>Health.cs:309</c>) — the number checked is AFTER the victim's full mitigation, so a
    /// miss, a fully-absorbed hit or a braced hit fires NOTHING. Basics AND skills both funnel here, once per damage
    /// instance. Entity = the victim.</summary>
    OnDamageDealt,

    /// <summary>YOU PARRIED — a charge was spent erasing an incoming hit. Fires at <c>Character.cs:868</c>, only after
    /// the parry actually consumed a charge. Entity = the attacker you parried, so a counter can aim back. A parried
    /// hit produces no damage number at all, so it fires NEITHER damage moment — this is its own moment precisely
    /// because nothing was taken.</summary>
    OnParry,

    /// <summary>YOU ARE ABOUT TO SWING — the intent, not the outcome. Fires at <c>Player3DAttacker.cs:165</c>
    /// (<c>AnnounceAttackIntent</c>) BEFORE reach is judged, so a gap-closer moves you in and the same swing still
    /// lands. Gated to once per swing interval. Entity = the intended target. The ONLY moment here that does not
    /// require damage — it fires even if the swing goes on to miss.</summary>
    OnBasicAttackStart,

    /// <summary>DAMAGE LANDED ON YOU. The exact mirror of <see cref="OnDamageDealt"/>: fires at
    /// <c>Character.cs:892</c> inside <c>if (dmg &gt; 0)</c>, after EVERY mitigation, so it means one damage instance
    /// that truly cost HP. A parried hit (see <see cref="OnParry"/>), a hit a Shield fully absorbed, and a hit held by
    /// a Brace window all fire NOTHING — same rule the dealt side uses, so the two can never drift apart. Ailment
    /// ticks (poison / bleed / burn) bypass this path and fire neither side. Entity = the attacker.</summary>
    OnDamageReceived,

    /// <summary>AN ITEM WAS USED, and that item names THIS skill (<see cref="Item.useSkillId"/>) — a potion drunk, a
    /// food eaten. Fired straight from <see cref="ItemUse.Use"/>, never through <c>PassiveEvents</c>, because the item
    /// already says which skill runs: the moment picks no winner, the item does. No entity is passed, so an item's
    /// skill is always self-cast.
    ///
    /// <para>Unparameterized ON PURPOSE, unlike <see cref="OnSkillCastComplete"/>: the pointer lives on the ITEM, so
    /// naming the item here as well would be the same fact stored in two places. A skill carrying only this trigger
    /// has no pressed form — it never reaches the hotbar and never announces itself — which is exactly what a
    /// consumable's effect should be.</para></summary>
    OnItemUsed,
}

/// <summary>ONE way a skill can start — a moment, plus the state gate that moment must satisfy. A skill owns a LIST of
/// these (<see cref="SkillDef.triggers"/>), so a single authored skill can answer OnCast AND another trigger
/// without a second asset duplicating its payload.
///
/// <para><b>The invariant that makes this safe: every trigger runs the SAME skill body.</b> An entry chooses only
/// WHEN the skill fires and what that firing costs in stacks — never WHAT it does. The moment two triggers want
/// different effects, that's two skills wearing one name, and the whole reason this exists (one copy of the payload,
/// so it can't drift) is gone. Split them into two skills instead.</para>
///
/// <para>The gate lives HERE and not on the skill because the two halves of a dual-trigger skill need different
/// answers: pressed Lunge must NOT require a charge, while the same Lunge fired off your next basic attack must
/// require and spend one.</para></summary>
[System.Serializable]
public class SkillTriggerEntry
{
    [Tooltip("The moment that starts the skill. OnCast = the player presses it (and pays its MP + cooldown); anything " +
             "other trigger fires the skill on its own, free — the trigger IS the gesture.")]
    public SkillTrigger trigger = SkillTrigger.OnCast;
    [Tooltip("Only for OnSkillCastComplete: the id of the ACTIVE skill whose successful (uninterrupted) PLAYER cast " +
             "fires this. Set it via the Skill Composer's trigger dropdown, not by hand.")]
    public string triggerSkillId = "";

    [Tooltip("ON = firing through THIS trigger reads a caster state — it may require stacks, spend them, and be paid " +
             "off by them. Each trigger gates independently — OnCast and OnBasicAttackStart can require different things.")]
    public bool requiresState = false;
    [Tooltip("WHOSE stacks this reads — the id of the skill that grants them (Deathblow reads Momentum's). Stacks are " +
             "identified by the skill that created them, NOT by what they do, so two skills that both buff attack keep " +
             "separate piles and a finisher can only ever cash its own partner's.")]
    public string requireSkillId = "";
    [Tooltip("WHICH of that skill's piles. A skill can grant more than one stacking state (charges to spend AND parry " +
             "charges to survive on), and those must never be counted together or spent for each other — so the gate " +
             "names both halves: whose pile, and which pile. Picked from what the granter actually grants.")]
    public StateId requireStateId = StateId.Poison;
    // Every StateId is a legal value, so the pile field alone cannot say "nobody picked one" — and the first member
    // (Poison) is what un-authored data reads as, which is a pile no gate would ever mean. This flag is that missing
    // answer, and it is the ONLY thing that may decide a gate is unauthored: an editor must not guess a pile for it,
    // and the runtime must not act as if one was chosen. Assets written before the pile field existed have it false,
    // which is exactly right — they never named a pile.
    [HideInInspector] public bool requireStateIdSet = false;
    [Tooltip("How many stacks must be present to fire ('detonate at 3'). 1 = just needs the state at all. " +
             "0 = NO GATE: it fires with none and simply cashes whatever happens to be there (Deathblow, which must " +
             "stay usable on a Sword that never rolled Momentum).")]
    [Min(0)] public int requireMinStacks = 1;
    [Tooltip("Spend ALL stacks on use (Deathblow cashing a Momentum pile). Overrides the per-use count below.")]
    public bool consumeAllStacks = false;
    [Tooltip("How many stacks this use spends. 0 = spends nothing (a pure condition, e.g. 'while Enraged'). " +
             "Consumption ALWAYS happens after the effects have computed, so the damage sees the live stacks.")]
    [Min(0)] public int consumeStacks = 0;

    // WHAT the cashed pile is worth is deliberately NOT here: a trigger says only WHEN the skill fires and what that
    // firing COSTS in stacks. The pile's payoff is authored on each NUMBER of the effect that spends it,
    // so it composes with that effect's own power instead of being a second damage channel at the skill level.
}

/// <summary>Binds ONE exposed <see cref="VfxStep.tickId"/> to the gameplay it should do. The tickId matches a
/// step in the skill's <see cref="SkillDef.vfx"/>; every time that component ticks, the runner applies these
/// effects to the targets in <see cref="radius"/> at the tick point.</summary>
[System.Serializable]
public class SkillTickBinding
{
    [Tooltip("MUST match a VfxStep.tickId in the referenced VfxSequence (the Skill Composer lists them for you).")]
    public string tickId;

    [Header("Targeting (the ONE targeting knob)")]
    [Tooltip("Self = lands on the caster. Single target = pick one matching entity. Ground = aim a spot on the floor.")]
    public TargetMode targetMode = TargetMode.Ground;
    [Tooltip("Ground only: Point = the exact spot (Dash/placed). AoE = splash in a radius. Projectile = fire toward the spot.")]
    public GroundDelivery groundDelivery = GroundDelivery.Aoe;
    [Tooltip("WHICH entity kinds this tick affects (Single = the pick must be one of these; AoE/Projectile = who it hits). " +
             "Mobs and/or Players; NPC reserved.")]
    public SkillTargets affects = SkillTargets.Mobs;
    [Tooltip("When Players are affected: Ally (default) or Enemy — the PvP faction filter. Enemy tagging is wired later.")]
    public PlayerFaction playerFaction = PlayerFaction.Ally;
    [Tooltip("AoE radius (world units) around the point. Used by Ground → AoE (and a projectile's per-tick hit width).")]
    public float radius = 1.5f;
    [Tooltip("Projectile delivery only: how many copies of THIS tick's carrier launch at once, fanned into a cone " +
             "around the aim direction. 1 = a single shot. Per tick, because the carrier belongs to the step this " +
             "tick names — a sequence with two carriers gives each its own volley.")]
    [Min(1)] public int projectileCount = 1;
    [Tooltip("Projectile delivery only: total cone angle (degrees) this tick's volley spreads across. 0 = every copy " +
             "flies straight at the aim.")]
    [Min(0f)] public float projectileSpread = 0f;
    [Tooltip("Only when THIS tick's VFX step is a LOOPING Play At / Attach: how long that loop stays before it ends " +
             "(and, for an End tick, when it lands). Per-step, so a VFX with two looping steps can give each its own " +
             "length. 0 = default 3s. The VFX owns the loop rule; this skill owns how long THIS use of it lasts.")]
    public float stayDuration = 0f;

    [Tooltip("What each tick does — applied through the shared SkillEffectApplier (the one damage path).")]
    public List<SkillEffect> effects = new List<SkillEffect>();

    /// <summary>True if any effect here lands on GATHERED TARGETS (Damage / Status / Knockback / …) — i.e. the
    /// targeting knobs above actually decide something. False ⇒ this tick carries only CASTER effects (Dash / Guard /
    /// Buff / …), which ignore targeting entirely (see <see cref="SkillEffect.LandsOnCaster"/>). Drives whether the cast
    /// asks for an entity pick (<see cref="SkillDef.NeedsUnitPick"/>) and how the composer's target section reads.</summary>
    public bool HasTargetEffects => AnyEffectSide(false);
    /// <summary>True if any effect here acts on the CASTER — those always apply, whatever the targeting says.</summary>
    public bool HasCasterEffects => AnyEffectSide(true);

    /// <summary>True if an effect here RELOCATES THE CASTER (Dash / Teleport authored on the caster side). Whether the
    /// cast has to ASK where to send them depends on what else it already asked — with an entity picked, that entity
    /// is the destination (see <see cref="SkillDef.NeedsAimPoint"/>).</summary>
    public bool HasCasterPointEffects => AnyPointEffectSide(true);
    /// <summary>True if an effect here RELOCATES WHOEVER IT CATCHES (Dash / Teleport authored on the target side) —
    /// "throw that one over there". Nothing but an aimed point can answer where "there" is, so this always asks.</summary>
    public bool HasTargetPointEffects => AnyPointEffectSide(false);

    // The two questions a relocation raises are NOT the same question, which is why the sides are counted apart: moving
    // yourself TO something you already pointed at needs no destination, moving something else needs one.
    bool AnyPointEffectSide(bool casterSide)
    {
        if (effects == null) return false;
        for (int i = 0; i < effects.Count; i++)
        {
            var e = effects[i];
            if (e != null && SkillEffect.NeedsPoint(e.kind) && SkillEffect.LandsOnCaster(e) == casterSide) return true;
        }
        return false;
    }

    bool AnyEffectSide(bool casterSide)
    {
        if (effects == null) return false;
        for (int i = 0; i < effects.Count; i++)
            if (effects[i] != null && SkillEffect.LandsOnCaster(effects[i]) == casterSide) return true;
        return false;
    }

    // The pre-consolidation targeting pair (targets + scope) was DELETED on 2026-08-23 with the conversion that read
    // it: every saved skill now carries the per-tick targeting directly. Same rule as the trigger fields — a format
    // change finishes on disk, so nothing is left needing a conversion.
}

/// <summary>
/// A data-authored SKILL — the output of the Skill Composer. It references a composed <see cref="VfxSequence"/>
/// (the look, authored in the Vfx Composer) and binds each of its exposed <b>ticks</b> to gameplay
/// (<see cref="SkillTickBinding"/>: damage/heal/status, element, multiplier, radius). The FINAL numbers are still
/// owned by code — the runner rolls them off the live caster stats through <see cref="SkillEffectApplier"/> — so
/// this asset only stores the design intent, never a baked damage value.
///
/// <para>Create via <c>Assets ▸ Create ▸ unwritten ▸ Skill</c> or the <b>Skill Composer</b> window; drop it under
/// <c>Resources/Skills/</c> so the game can load it by <see cref="id"/> via <see cref="SkillCatalog"/>.</para>
/// </summary>
[CreateAssetMenu(menuName = "unwritten/Skill", fileName = "Skill")]
public class SkillDef : ScriptableObject
{
    // Set once the ticks carry the consolidated targeting fields. Old assets (no such field) load as false → auto-
    // migrate on load (idempotent); saving from the new composer persists true, so migration never runs again.

    [Header("Identity")]
    [Tooltip("Stable kebab id, e.g. \"skill.meteor\". Keyed by SkillCatalog; don't rename once shipped.")]
    public string id;
    public string displayName = "New Skill";
    [Tooltip("Two letters that stand for this skill on a tile, the way Au stands for gold: \"Committed Strike\" " +
             "becomes \"Cs\". Blank = the tile shows the skill's whole name instead.")]
    public string symbol = "";
    [Tooltip("WHICH weapon this skill belongs to. It is learned with that weapon's mastery points and may only be " +
             "used while holding one. Also the middle segment of the id: skill.sword.<name>. None = belongs to no " +
             "weapon, so nobody can learn it.")]
    public WeaponType weaponType = WeaponType.None;
    [Tooltip("The mastery level in that weapon from which a mastery point can be spent on this skill — where it " +
             "sits on the Skills window's timeline. It learns nothing by itself. 0 means the same as 1.")]
    public int masteryLevel = 1;
    [Tooltip("A skill that must be learned FIRST (at least one point in it), by id — ONLY when this skill literally " +
             "uses it: spends its stacks, adds to its pile, fires on its cast or on a moment it causes (Crossed " +
             "Branches needs Piercing Gust). Never because it pairs well. Empty = nothing comes first.")]
    public string needsSkillId = "";
    [Tooltip("PUBLISHED = a forge may roll this as a weapon's trademark, and a creature may draw it as its extra " +
             "skill. Unpublished or not, it is learned with its weapon's mastery points.")]
    public bool published = false;
    [Tooltip("The highest level mastery points can raise this skill to. Every level after the first adds the same " +
             "+5% to each number the fight raises. 10 when what grows is mostly damage; 5 when it is a defensive or " +
             "utility percentage or a charge count; 1 when nothing on the skill grows. 0 = never set, which the " +
             "skill-data check refuses on any skill that belongs to a weapon.")]
    [Min(0)] public int maxLevel = 0;

    [Header("Codex (hover tooltip)")]
    [TextArea(2, 4)]
    [Tooltip("Flavor + mechanics blurb shown in the hover card.")]
    public string description = "";
    [Tooltip("Battlefield-role VERB (the skill's TAG) — drives the tooltip chip + color. One of the 5 verbs.")]
    public SkillArchetype archetype = SkillArchetype.Burst;
    [Tooltip("Attribute FAMILY (Might/Arcane/Finesse) — the RPS-triangle cluster; also the default source of a damage " +
             "effect's damage type. Orthogonal to the verb above.")]
    public SkillFamily family = SkillFamily.Might;
    [Tooltip("Spatial DELIVERY (single / area / aura / self / dash).")]
    public SkillShape shape = SkillShape.Single;
    [Tooltip("Designer metadata (not gameplay): authoring complexity, for library search.")]
    public SkillComplexity complexity = SkillComplexity.Low;
    [Tooltip("Designer metadata (not gameplay): when in progression this skill is meant to surface.")]
    public AcquisitionTier acquisitionTier = AcquisitionTier.Early;

    [Header("Triggers — every way this skill can start (all run the SAME body)")]
    [Tooltip("The moments that can start this skill, each with its own state gate. One OnCast entry = a normal " +
             "pressed active. No OnCast entry = a pure passive (no hotbar slot). BOTH = a skill you can press AND " +
             "that also fires itself when another trigger's gate is met — one asset, one payload, no duplicate.")]
    public List<SkillTriggerEntry> triggers = new List<SkillTriggerEntry>();

    // A skill with no authored entries reads as a plain OnCast skill. Shared and never mutated, so an un-authored
    // skill costs no allocation and still answers every question below.
    static readonly SkillTriggerEntry CastDefault = new SkillTriggerEntry();

    /// <summary>The skill's OnCast entry, or null when it has none (it can only ever fire from its other triggers).</summary>
    public SkillTriggerEntry CastEntry
    {
        get
        {
            if (triggers == null || triggers.Count == 0) return CastDefault;
            for (int i = 0; i < triggers.Count; i++)
                if (triggers[i] != null && triggers[i].trigger == SkillTrigger.OnCast) return triggers[i];
            return null;
        }
    }

    /// <summary>The entry an ITEM runs this skill through, or null when no item can. Separate from
    /// <see cref="CastEntry"/> because the two are different moments: a press pays mana and cooldown, an item was
    /// already paid for by being consumed.</summary>
    public SkillTriggerEntry ItemUseEntry
    {
        get
        {
            if (triggers == null) return null;
            for (int i = 0; i < triggers.Count; i++)
                if (triggers[i] != null && triggers[i].trigger == SkillTrigger.OnItemUsed) return triggers[i];
            return null;
        }
    }

    /// <summary>The entry that <paramref name="trigger"/> starts, or null if this skill doesn't answer that moment.
    /// <paramref name="skillId"/> only matters for <see cref="SkillTrigger.OnSkillCastComplete"/>, which is
    /// parameterized by WHICH skill completed. First match wins — a duplicate moment is an authoring mistake, not a
    /// second behavior (all entries run the same body, so a second copy could only differ in its gate).</summary>
    public SkillTriggerEntry EntryFor(SkillTrigger trigger, string skillId)
    {
        if (triggers == null || triggers.Count == 0)
            return trigger == SkillTrigger.OnCast ? CastDefault : null;
        for (int i = 0; i < triggers.Count; i++)
        {
            var e = triggers[i];
            if (e == null || e.trigger != trigger) continue;
            if (trigger == SkillTrigger.OnSkillCastComplete && e.triggerSkillId != skillId) continue;
            // A kill may name the skill that landed it. Left blank the entry answers ANY kill (the original meaning, so
            // every existing OnKill passive is untouched); filled, only kills by that skill — which is what lets one
            // passive belong to one skill's kills instead of every kill in the fight.
            if (trigger == SkillTrigger.OnKill && !string.IsNullOrEmpty(e.triggerSkillId)
                && e.triggerSkillId != skillId) continue;
            return e;
        }
        return null;
    }

    // The pre-multi-trigger fields (one trigger + one gate per skill) and the load-time conversion that seeded
    // `triggers` from them were DELETED on 2026-08-23, after every saved skill was rewritten carrying its own list.
    //
    // ⛔ THE RULE THAT REPLACED THEM: A FORMAT CHANGE FINISHES ON DISK, NOT IN MEMORY. Converting on load and leaving
    // the files alone is what caused the only bug this has ever had: the conversion is invisible, so a file nobody
    // happened to re-save sat in the old form for months while the game silently papered over it — and the day the
    // old field was tidied away, that file would have lost the value with no error, because the zero it fell back to
    // is a legal value. Seven of twenty-five skills were in exactly that state.
    //
    // So when the skill format changes: rewrite EVERY skill asset in the new form as part of the same change, then
    // delete the old fields. Never ship a conversion that has to be remembered. `tools/check-skill-data.py` fails on
    // any saved skill that still needs converting, so the rule is enforced rather than merely written down.

    [Header("Cost / cadence")]
    public int manaCost = 10;
    public float cooldown = 5f;
    [Tooltip("Base cast wind-up in seconds BEFORE the skill fires (the cast bar fills over this). 0 = instant. " +
             "The caster's DEX shortens it at runtime via Character.CastTimeMult (instacast at 150 DEX).")]
    [Min(0f)] public float castTime = 0f;
    [Tooltip("How the skill picks where it lands.")]
    public SkillAim aim = SkillAim.Ground;
    [Tooltip("Ground/Target aim: max distance from the caster the impact point can be.")]
    public float castRange = 11f;

    [Header("Ultimate (tier, not a category)")]
    [Tooltip("Base = a normal skill. Ultimate = sits in any hotbar slot like any other skill, pays mana and rides its " +
             "own cooldown — and casting it also starts the 40-second shared cooldown every ultimate waits on.")]
    public SkillTier tier = SkillTier.Base;
    /// <summary>True when this skill is an ultimate — held by the shared cooldown (<see cref="SharedCooldown"/>).</summary>
    public bool IsUltimate => tier == SkillTier.Ultimate;

    /// <summary>True when this skill has NO OnCast trigger — it is never pressed (on the hotbar it is only a tile to
    /// watch) and only ever runs through <see cref="PassiveEvents"/> when one of its other triggers fires. A skill that
    /// HAS an OnCast entry is an active even if it also answers other triggers: it is pressed from a slot, pays MP, and
    /// the drop roll still files it as an active.</summary>
    public bool IsPassive => CastEntry == null;

    /// <summary>True when Weapon Mastery may raise this skill's main numbers while <paramref name="held"/> is in hand:
    /// only a skill that belongs to that weapon. A potion's skill, or any skill belonging to no weapon, never rides
    /// Weapon Mastery. One rule for a player's passives and a creature's skills alike.</summary>
    public bool RidesWeaponMastery(WeaponType held) => weaponType != WeaponType.None && weaponType == held;

    [Header("Cast circle (shown ONLY during the wind-up, needs castTime > 0)")]
    [Tooltip("EffectLibrary id of the ground casting circle shown WHILE this skill winds up. Empty = no per-skill " +
             "circle (the global OnCastStart cue plays instead). It's a wind-up visual, separate from the release " +
             "vfx below. Author a LOOPING circle prefab; add packs via Art ▸ Rebuild Effect Library.")]
    public string castCircleId = "";
    [Tooltip("Tint for the casting circle (white = the prefab's own colours). Recolour per element without a new prefab.")]
    public Color castCircleTint = Color.white;
    [Header("Presentation (referenced, never copied)")]
    [Tooltip("The composed look from the Vfx Composer. Its exposed ticks are what the bindings below map to.")]
    public VfxSequence vfx;

    [Tooltip("How many times to replay the WHOLE composed cycle per cast (e.g. 5 = a 5-meteor shower). Each cycle " +
             "is an independent play at the same cast point — its own carrier falls and multi-hits via the VFX's " +
             "per-component tick rule. 1 = play once. Repetition of the whole effect lives HERE (the gameplay layer), " +
             "not in the Vfx Composer — the composer stays WYSIWYG (one cycle = one meteor).")]
    [Min(1)] public int repeatCount = 1;
    [Tooltip("Seconds between the start of each cycle when repeatCount > 1. 0 = fall back to the VFX sequence's " +
             "RestartInterval (its loop-end marker). Cycles overlap freely; a still-playing cycle keeps rendering.")]
    [Min(0f)] public float repeatInterval = 0f;

    [Tooltip("ON = the beat follows the caster's SWING SPEED instead of a fixed number of seconds, so a faster caster " +
             "beats faster. Overrides the repeat interval entirely. Read live each beat, so a speed buff landing " +
             "mid-run quickens the rest of it.")]
    public bool repeatScaleWithAttackSpeed = false;
    [Tooltip("How many beats per swing when the beat follows swing speed. 1 = one beat per swing (your normal rhythm); " +
             "2 = twice as fast as you swing; 3 = three times. Whole numbers only — a beat is either on your rhythm " +
             "or a clean multiple of it.")]
    [Min(1)] public int repeatBeatsPerSwing = 1;

    [Tooltip("ON = each repeat SPENDS one charge of the state this skill's trigger requires, and the repeats run " +
             "until that pile is empty instead of stopping at a fixed count (repeat count is then ignored). Because " +
             "the loop reads the LIVE pile, a passive that grants more charges mid-run genuinely lengthens the run.")]
    public bool repeatCostsCharge = false;

    [Tooltip("ON = the yellow total belongs to the WHOLE run: each mob shows ONE number that climbs with every repeat " +
             "that strikes it, closing a second after the run last touched it. OFF = today's behaviour, where a " +
             "multi-hit flurry owns the total, so every repeat pops its own (a meteor shower shows one per meteor). " +
             "Purely what you SEE — it changes no damage, no timing and no state.")]
    public bool showRepeatsTotalDamage = false;

    [Tooltip("ON = each repeat RE-AIMS at a random living mob within the range below, measured from the CURRENT " +
             "target — so the cast walks from one target to the next. OFF = every repeat replays at the spot the " +
             "cast was originally aimed (a meteor shower stays where you aimed it).")]
    public bool repeatReaim = false;
    [Tooltip("Re-aim search range in world units, measured from the CURRENT target's position — not from the caster " +
             "and not from where the cast started. Nothing living in range ENDS the run.")]
    [Min(0f)] public float repeatReaimRange = 6f;

    [Tooltip("ON = the caster is HELD for the whole run: from the first beat until the last one ends it can't move, " +
             "attack, start another skill or launch a combo — the run is the only thing it is doing. The hold is " +
             "re-taken each beat and lapses on its own when the run stops, however it stopped (last charge spent, " +
             "nothing left in reach, caster down). OFF = only the release animation's own aftercast roots the caster, " +
             "so it is free again between beats (a meteor shower rains while you keep fighting).")]
    public bool holdCasterForRun = false;

    [Header("Skill type (release animation)")]
    [Tooltip("Play a skill-release stance when this skill FIRES (after any cast wind-up). false = no release pose.")]
    public bool hasReleaseStance = false;
    [Tooltip("Which release cue to play — the caster's chosen 'skill type'. Only the SkillReleaseStances subset is " +
             "valid; assign each cue a clip in the Stance Composer. Ignored when hasReleaseStance is false.")]
    public StanceId releaseStance = StanceId.AoeSpell1;
    [Tooltip("Play the release animation again on EVERY repeat, restarted from its first frame and fit to that " +
             "repeat's interval — so a repeating skill shows one WHOLE animation per beat instead of one animation " +
             "stretched across the run. Off = it plays once, when the skill fires. Does nothing if the skill never repeats.")]
    public bool releaseStanceReplayPerRepeat = false;

    [Header("Gameplay — one binding per tickId you want to matter")]
    public List<SkillTickBinding> bindings = new List<SkillTickBinding>();

    [Header("Hotbar")]
    [Tooltip("A SHORTENED name for the hotbar cell, for when the real name won't fit (\"Committed Strike\" → " +
             "\"Commit\"). Leave it alone and the cell shows the skill's own name — so a skill is never labelled with " +
             "this field's factory value instead of what it's called.")]
    public string shortLabel = DefaultShortLabel;
    public string keyLabel = "";
    public int slotOrder = 10;
    public Color tint = Color.white;

    /// <summary>What the field holds until somebody shortens a name — the value that means "nobody chose one".</summary>
    public const string DefaultShortLabel = "Skill";

    /// <summary>What a hotbar cell's long form reads: the shortened name if one was authored, else the skill's OWN name.
    /// THE one answer to "what is this slot called", so no reader can show the placeholder instead.
    ///
    /// <para>Untouched, the field still says <see cref="DefaultShortLabel"/> — the literal word every skill starts with
    /// — and a cell showing that tells the player nothing about which skill it is. Treating it as unauthored is what
    /// makes naming a skill enough, with no second place to fill in.</para></summary>
    public string HotbarLabel =>
        string.IsNullOrWhiteSpace(shortLabel) || shortLabel == DefaultShortLabel ? displayName : shortLabel;

    /// <summary>What a tile reads for this skill: its two-letter <see cref="symbol"/> if one was authored, else the
    /// skill's whole name. THE one answer, so every tile falls back the same way.</summary>
    public string Symbol => string.IsNullOrWhiteSpace(symbol) ? displayName : symbol;

    /// <summary>How long a LOOPING step keyed to <paramref name="tickId"/> stays before it ends. 0 = unauthored, and
    /// the runtime falls back to its own default.
    ///
    /// <para>Here, not on whoever happens to be playing the sequence, because two different things now play it: the
    /// machine that DECIDES the cast and every machine that only DRAWS it. A second copy of this answer would have
    /// them disagree about how long the same effect lasts.</para></summary>
    public float StayForTick(string tickId)
    {
        var b = BindingFor(tickId);
        return b != null ? b.stayDuration : 0f;
    }

    /// <summary>How many copies of the carrier on <paramref name="tickId"/> fly, and how wide they fan. One shot
    /// unless the tick is authored as a projectile delivery — so a fall-in meteor is never multiplied by a count
    /// meant for a bolt.
    ///
    /// <para>Same reason as <see cref="StayForTick"/>: a watcher drawing one bolt where the server fired five is a
    /// picture of a different skill.</para></summary>
    public ProjectileFan FanForTick(string tickId)
    {
        var b = BindingFor(tickId);
        return b != null && b.targetMode == TargetMode.Ground && b.groundDelivery == GroundDelivery.Projectile
            ? new ProjectileFan(b.projectileCount, b.projectileSpread)
            : ProjectileFan.Single;
    }

    /// <summary>The binding for <paramref name="tickId"/>, or null if this tick is purely visual (unbound).</summary>
    public SkillTickBinding BindingFor(string tickId)
    {
        if (string.IsNullOrEmpty(tickId) || bindings == null) return null;
        for (int i = 0; i < bindings.Count; i++)
            if (bindings[i] != null && bindings[i].tickId == tickId) return bindings[i];
        return null;
    }

    // ---- the aim SEQUENCE, derived from the ticks (never authored) ----------------------------------------------
    // A cast asks at most two questions, in this order, and each is skipped when the effects don't pose it:
    //   1. WHICH ENTITY  (NeedsUnitPick) — only when a Single-target tick has effects that land on a target. The
    //      caster side never asks: the entity is the caster by definition.
    //   2. WHICH POINT   (NeedsAimPoint) — when a ground tick needs its spot, OR when a relocation still has to be told
    //      where to send what it caught (Dash / Teleport, see SkillEffect.NeedsPoint).
    // "Dash the mob I clicked over there" and "dash myself over there" are the same rule with step 1 present or absent.

    /// <summary>Stage 1: this cast must pick an entity first.</summary>
    public bool NeedsUnitPick
    {
        get
        {
            if (bindings == null) return false;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b != null && b.targetMode == TargetMode.SingleTarget && b.HasTargetEffects) return true;
            }
            return false;
        }
    }

    /// <summary>Stage 2: this cast must pick a ground point — either a ground tick lands there, or a relocation still
    /// has no destination once stage 1 is done.
    ///
    /// <para><b>A question already answered is not asked again.</b> Moving the CASTER to an entity the cast just picked
    /// has its destination: the landing is worked out from that entity and the mover's reach, and a clicked point would
    /// be thrown away — so a strike-and-hop skill costs ONE click, not two. Move the caster with nobody picked (a plain
    /// dash) and the point is the only aim there is, so it is still asked. Moving whoever the tick CATCHES always asks:
    /// the caught unit is the thing being thrown, so it can't also be the place it's thrown to.</para></summary>
    public bool NeedsAimPoint
    {
        get
        {
            if (bindings == null) return false;
            bool picksUnit = NeedsUnitPick;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b == null) continue;
                if (b.targetMode == TargetMode.Ground) return true;
                if (b.HasTargetPointEffects) return true;
                if (b.HasCasterPointEffects && !picksUnit) return true;
            }
            return false;
        }
    }

    /// <summary>The MOST DEMANDING single gesture of the sequence above — what a combo pre-resolves and what the
    /// tooltip calls "has a range". Derived from the two stages so it can never disagree with them.</summary>
    public CastReticle Reticle =>
        NeedsUnitPick ? CastReticle.Unit : NeedsAimPoint ? CastReticle.Ground : CastReticle.Instant;

    /// <summary>The largest Ground→AoE binding radius — the skill's real ground footprint. SINGLE OWNER of that value:
    /// the ground reticle (<see cref="SkillRunner.PreviewRadius"/>) and the cast circle both READ it. 1.5 fallback.</summary>
    public float LargestAreaRadius
    {
        get
        {
            float r = 0f;
            if (bindings != null)
                for (int i = 0; i < bindings.Count; i++)
                {
                    var b = bindings[i];
                    if (b != null && b.targetMode == TargetMode.Ground && b.groundDelivery == GroundDelivery.Aoe)
                        r = Mathf.Max(r, b.radius);
                }
            return r > 0f ? r : 1.5f;
        }
    }

    /// <summary>True if the VFX step carrying <paramref name="tickId"/> is a LOOPING Play At / Attach — the one case
    /// where a binding's <see cref="SkillTickBinding.stayDuration"/> matters (it caps how long that loop stays). A
    /// Carrier never loops (it ends on arrival).</summary>
    public bool TickStepLoops(string tickId)
    {
        if (vfx == null || vfx.steps == null || string.IsNullOrEmpty(tickId)) return false;
        for (int i = 0; i < vfx.steps.Count; i++)
        {
            var s = vfx.steps[i];
            if (s != null && s.tickId == tickId && s.looping && s.CanLoop) return true;
        }
        return false;
    }

    /// <summary>True if any ground tick is authored as <see cref="GroundDelivery.Projectile"/> — so the CAST aims a
    /// DIRECTION: the ground reticle shows a line-arrow toward the aim, and the aim is pushed out to the travel range.
    /// That is a whole-cast question (there is one reticle), which is why it asks "any tick"; how many copies actually
    /// fly is each tick's own <see cref="SkillTickBinding.projectileCount"/>. SINGLE OWNER of "this skill aims like a
    /// projectile" — derived from the authored delivery, NOT from whether the VFX happens to have a Carrier step (a
    /// Carrier that drops from ABOVE is a fall-in AoE, e.g. a meteor).</summary>
    public bool IsProjectileAim
    {
        get
        {
            if (bindings == null) return false;
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (b != null && b.targetMode == TargetMode.Ground && b.groundDelivery == GroundDelivery.Projectile)
                    return true;
            }
            return false;
        }
    }

    /// <summary>True if ANY tick is Single-target — the cast must land on one chosen unit (the Unit reticle).</summary>
    public bool IsSingleTarget
    {
        get
        {
            if (bindings == null) return false;
            for (int i = 0; i < bindings.Count; i++)
                if (bindings[i] != null && bindings[i].targetMode == TargetMode.SingleTarget) return true;
            return false;
        }
    }

    /// <summary>The union of every Single-target tick's <see cref="SkillTickBinding.affects"/> — the entity KINDS a
    /// single-target cast may legally land on. The Unit reticle validates the hovered unit against this.</summary>
    public SkillTargets SingleTargetMask
    {
        get
        {
            SkillTargets mask = SkillTargets.None;
            if (bindings != null)
                for (int i = 0; i < bindings.Count; i++)
                    if (bindings[i] != null && bindings[i].targetMode == TargetMode.SingleTarget) mask |= bindings[i].affects;
            return mask;
        }
    }
}
