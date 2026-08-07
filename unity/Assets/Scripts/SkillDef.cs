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
    /// <summary>A directional projectile: the reticle is a line-arrow (aim a DIRECTION, not a landing spot) and the
    /// skill fans <see cref="SkillDef.projectileCount"/> copies. The flying VFX (a Carrier with spread ticks) raises
    /// ticks along its path, each landing as a small AoE (<see cref="SkillTickBinding.radius"/> = hit width) at the
    /// projectile's live position. This — NOT the VFX's Carrier step — is the single owner of "this skill is a
    /// projectile" (see <see cref="SkillDef.IsProjectileAim"/>).</summary>
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
/// this tier — it fires from a dedicated slot, gated by a charge meter that fills in combat. Zero new architecture.</summary>
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
public enum SkillTrigger
{
    OnCast = 0,
    OnSkillCastComplete,   // parameterized by triggerSkillId: that active finished casting (not interrupted)
    OnBasicAttackLanded,   // natural — a basic attack connected
    OnKill,                // natural — a mob died to the player
    OnLowHP,               // natural — the player dropped below the low-HP threshold
    OnHit,                 // natural — the player landed a damaging hit
    OnParry,               // natural — a successful block (fired by the parry system, NOT on cast)
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
    [Tooltip("Only when THIS tick's VFX step is a LOOPING Play At / Attach: how long that loop stays before it ends " +
             "(and, for an End tick, when it lands). Per-step, so a VFX with two looping steps can give each its own " +
             "length. 0 = default 3s. The VFX owns the loop rule; this skill owns how long THIS use of it lasts.")]
    public float stayDuration = 0f;

    [Tooltip("What each tick does — applied through the shared SkillEffectApplier (the one damage path).")]
    public List<SkillEffect> effects = new List<SkillEffect>();

    // ---- LEGACY (pre-consolidation) — read ONLY by MigrateLegacy to seed the fields above; never by runtime now. ----
    [HideInInspector] public SkillTargets targets = SkillTargets.Mobs;
    [HideInInspector] public SkillScope scope = SkillScope.Area;
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
public class SkillDef : ScriptableObject, ISerializationCallbackReceiver
{
    // Set once the ticks carry the consolidated targeting fields. Old assets (no such field) load as false → auto-
    // migrate on load (idempotent); saving from the new composer persists true, so migration never runs again.
    [HideInInspector] public bool targetingMigrated = false;

    [Header("Identity")]
    [Tooltip("Stable kebab id, e.g. \"skill.meteor\". Keyed by SkillCatalog; don't rename once shipped.")]
    public string id;
    public string displayName = "New Skill";
    [Tooltip("Hotbar icon (Skill Icon pack). Null → the bar falls back to the short label.")]
    public Sprite icon;

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

    [Header("Trigger (OnCast = a normal active; anything else = a passive)")]
    [Tooltip("OnCast = a normal active the player presses (takes a hotbar slot + the cost/cadence below). Anything " +
             "else = a PASSIVE: no slot, it runs its bindings when that moment fires (dispatched via PassiveEvents). " +
             "For a passive, cost / cadence / aim below are IGNORED.")]
    public SkillTrigger trigger = SkillTrigger.OnCast;
    [Tooltip("Only for OnSkillCastComplete: the id of the ACTIVE skill whose successful (uninterrupted) cast fires " +
             "this passive. Set it via the Skill Composer's trigger dropdown, not by hand.")]
    public string triggerSkillId = "";

    [Header("Requires a state (the combo gate)")]
    [Tooltip("ON = this skill only runs while the caster carries the required state. It gates the WHOLE skill: when " +
             "unmet nothing happens at all — no VFX, no cast, no cost. Works for a pressed active and a passive alike.")]
    public bool requiresState = false;
    [Tooltip("The state the caster must be carrying. NOTE: this is SERIALIZED, so the StateId list is append-only from " +
             "here on — inserting a value mid-list would silently repoint every skill saved after it.")]
    public StateId requireState = StateId.Haste;
    [Tooltip("How many stacks must be present to fire ('detonate at 3'). 1 = just needs the state at all.")]
    [Min(1)] public int requireMinStacks = 1;
    [Tooltip("Spend ALL stacks on use (Deathblow cashing a Momentum pile). Overrides the per-use count below.")]
    public bool consumeAllStacks = false;
    [Tooltip("How many stacks this use spends. 0 = spends nothing (a pure condition, e.g. 'while Enraged'). " +
             "Consumption ALWAYS happens after the skill's effects have computed, so the damage sees the live stacks.")]
    [Min(0)] public int consumeStacks = 0;

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
    [Tooltip("Base = a normal hotbar skill (keys 1–9). Ultimate = fires from the dedicated ULT slot, gated by the " +
             "shared ult-charge meter instead of a plain cooldown.")]
    public SkillTier tier = SkillTier.Base;
    [Tooltip("Ult-charge required to cast (Ultimate tier only). Spent from Character's shared meter, which fills " +
             "during combat (dealing/taking damage, casting).")]
    [Min(0f)] public float chargeCost = 100f;
    /// <summary>True when this skill occupies the dedicated ULT slot and spends charge rather than riding keys 1–9.</summary>
    public bool IsUltimate => tier == SkillTier.Ultimate;

    /// <summary>True when this skill runs on a <see cref="SkillTrigger"/> instead of a hotbar press — it takes no slot,
    /// never casts, and its bindings fire through <see cref="PassiveEvents"/> when its trigger moment happens.</summary>
    public bool IsPassive => trigger != SkillTrigger.OnCast;

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

    [Header("Projectile fan (compose ONE projectile in the Vfx Composer; multiply here, like repeat)")]
    [Tooltip("Fire this many copies of the projectile at once, fanned into a cone around the aim direction. 1 = single. " +
             "Only does anything when the VFX launches a projectile (has a Carrier step).")]
    [Min(1)] public int projectileCount = 1;
    [Tooltip("Total cone angle (degrees) the fan spreads across. 0 = all straight at the aim point.")]
    [Min(0f)] public float projectileSpread = 0f;

    [Header("Skill type (release animation)")]
    [Tooltip("Play a skill-release stance when this skill FIRES (after any cast wind-up). false = no release pose.")]
    public bool hasReleaseStance = false;
    [Tooltip("Which release cue to play — the caster's chosen 'skill type'. Only the SkillReleaseStances subset is " +
             "valid; assign each cue a clip in the Stance Composer. Ignored when hasReleaseStance is false.")]
    public StanceId releaseStance = StanceId.AoeSpell1;

    [Header("Gameplay — one binding per tickId you want to matter")]
    public List<SkillTickBinding> bindings = new List<SkillTickBinding>();

    [Header("Hotbar")]
    public string shortLabel = "Skill";
    public string keyLabel = "";
    public int slotOrder = 10;
    public Color tint = Color.white;

    public void OnBeforeSerialize() { }

    /// <summary>One-time migration of the pre-consolidation targeting (skill <c>aim</c> + per-tick <c>scope</c>/
    /// <c>targets</c>) into the new per-tick <see cref="TargetMode"/> model. Runs on load for un-migrated assets; the
    /// new composer saves <see cref="targetingMigrated"/> = true so it never runs on already-migrated data.</summary>
    public void OnAfterDeserialize()
    {
        if (targetingMigrated || bindings == null) return;
        for (int i = 0; i < bindings.Count; i++) MigrateBindingFromLegacy(bindings[i]);
        targetingMigrated = true;
    }

    void MigrateBindingFromLegacy(SkillTickBinding b)
    {
        if (b == null) return;
        b.affects = b.targets == SkillTargets.None ? SkillTargets.Mobs : b.targets;

        bool anyPointSelf = false, allSelf = true;   // any Dash/Blink present? are ALL effects self-only?
        if (b.effects != null && b.effects.Count > 0)
            for (int i = 0; i < b.effects.Count; i++)
            {
                var e = b.effects[i];
                if (e == null) continue;
                if (SkillEffect.IsSelfPointVerb(e.kind)) anyPointSelf = true;
                if (!SkillEffect.IsSelfVerb(e.kind)) allSelf = false;
            }

        // Honor the LEGACY aim first (it was the authoritative cast gesture), then refine with scope/effects.
        if (aim == SkillAim.Self)
        {
            b.targetMode = TargetMode.Self; b.affects = SkillTargets.Player;
        }
        else if (aim == SkillAim.Target || b.scope == SkillScope.Single)
        {
            b.targetMode = TargetMode.SingleTarget;
        }
        else   // aim == Ground
        {
            b.targetMode = TargetMode.Ground;
            if (anyPointSelf || allSelf) { b.groundDelivery = GroundDelivery.Point; b.affects = SkillTargets.Player; }
            else                          { b.groundDelivery = GroundDelivery.Aoe; }
        }
    }

    /// <summary>The binding for <paramref name="tickId"/>, or null if this tick is purely visual (unbound).</summary>
    public SkillTickBinding BindingFor(string tickId)
    {
        if (string.IsNullOrEmpty(tickId) || bindings == null) return null;
        for (int i = 0; i < bindings.Count; i++)
            if (bindings[i] != null && bindings[i].tickId == tickId) return bindings[i];
        return null;
    }

    /// <summary>The per-cast reticle, DERIVED from the ticks (no authored aim). Any Single-target tick ⇒ Unit pick;
    /// else any Ground tick ⇒ Ground reticle; else Instant. This is the single source of truth the runner reads.</summary>
    public CastReticle Reticle
    {
        get
        {
            bool anyGround = false;
            if (bindings != null)
                for (int i = 0; i < bindings.Count; i++)
                {
                    var b = bindings[i];
                    if (b == null) continue;
                    if (b.targetMode == TargetMode.SingleTarget) return CastReticle.Unit;
                    if (b.targetMode == TargetMode.Ground) anyGround = true;
                }
            return anyGround ? CastReticle.Ground : CastReticle.Instant;
        }
    }

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

    /// <summary>True if any ground tick is authored as <see cref="GroundDelivery.Projectile"/> — so the skill aims a
    /// DIRECTION: the ground reticle shows a line-arrow toward the aim, and <see cref="projectileCount"/> fans copies
    /// of it. SINGLE OWNER of "this skill is a projectile" — derived from the authored delivery, NOT from whether the
    /// VFX happens to have a Carrier step (a Carrier that drops from ABOVE is a fall-in AoE, e.g. a meteor).</summary>
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
