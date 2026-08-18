using UnityEngine;

/// <summary>
/// Pure brain for a billboard mob. Wanders near its spawn,
/// CHASES the <see cref="Player3DController"/> once within <see cref="aggroRadius"/>, and gives up
/// past <see cref="leashRadius"/>. Movement is delegated to a <see cref="PathAgent3D"/> sibling
/// (routes around props via the grid A*); <see cref="BillboardAnimator3D"/> animates from that
/// motion. Reuses <see cref="Health"/> for HP/death and the existing <see cref="Character"/> for
/// touch-damage + XP (no-ops in the standalone spike, which has no Character yet).
/// </summary>
[RequireComponent(typeof(PathAgent3D))]
public class MonsterAI3D : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 2.5f;

    [Header("Senses (world units)")]
    public float aggroRadius = 6f;
    public float leashRadius = 10f;
    [Tooltip("Hold this far from the player. Must be < touchRange so it can actually land hits.")]
    public float stopDistance = 0.7f;

    [Header("Wander")]
    public float wanderRadius = 3f;
    public float wanderPauseMin = 1f;
    public float wanderPauseMax = 3f;

    public enum AttackType { Physical, Magic }

    [Header("Combat")]
    [Tooltip("Physical hits the player's Defense; Magic hits MagicDefense (and ignores Evasion).")]
    public AttackType attackType = AttackType.Physical;
    public int attack = 3;
    [Tooltip("This mob's attack ELEMENT (RO property), checked against the player's armor element. The mob's " +
             "OWN armor element (what the player's spells hit) lives on its Health component.")]
    public Element attackElement = Element.Neutral;

    [Header("On-hit status (RO — inflicted on the player when this mob lands a hit)")]
    [Tooltip("The ailment applied on a landed hit. Only used when the chance below is > 0.")]
    public Ailment onHitAilment = Ailment.Poison;
    [Range(0f, 1f)]
    [Tooltip("Chance per LANDED hit to inflict the ailment (0 = this mob inflicts no status).")]
    public float onHitAilmentChance = 0f;
    [Tooltip("Ailment duration in seconds.")]
    public float onHitAilmentSeconds = 4f;
    [Tooltip("Ailment magnitude — DoT damage/tick, Slow factor (0..1), or Blind HIT loss.")]
    public float onHitAilmentMagnitude = 3f;

    [Tooltip("Outgoing damage swings down to (1−spread)×attack each hit, RO-style (0 = always full attack).")]
    [Range(0f, 1f)] public float damageSpread = 0.25f;
    [Tooltip("HIT (RO accuracy). Rolled against the player's FLEE on each bite; lose and the attack MISSes (white).")]
    public int hit = 8;
    public int xpReward = 5;
    [Tooltip("UNARMED reach (world units, full size). A mob holding a weapon uses that weapon's Attack Range " +
             "instead — the same rule the player uses, so arming a mob extends its reach exactly as it would yours.")]
    public float touchRange = 0.9f;
    public float touchInterval = 1f;

    [Header("Leveling (mob progression — mirrors the player)")]
    [Min(1)]
    [Tooltip("Current mob level. Grows in combat; each level-up scales attack + max HP and fires the OnLevelUp cue.")]
    public int level = 1;
    [Tooltip("XP this mob earns per point of damage it deals to the player (the mob's mirror of the player earning " +
             "XP from combat). 0 = this mob never levels.")]
    public float xpPerDamageDealt = 1f;
    [Tooltip("XP for the FIRST level-up; the bar for each subsequent level is this × the current level.")]
    public int xpToFirstLevel = 20;
    // (Removed 2026-08-10: perLevelGrowth. A mob's STATS are RE-DERIVED at each level through the same formulas the
    //  player uses — there is no mob-only stat growth rate to drift.)

    /// <summary>How much more XP this mob is worth per level it gains (0.15 = +15%). A payout curve, NOT a stat
    /// multiplier — the unit model has none. Kept as one named constant so raising the reward curve is a single edit.</summary>
    public const float XpRewardGrowthPerLevel = 0.15f;

    [Header("Behavior style")]
    [Tooltip("0 = melee (close in and bite). >0 = HOLD this far from the player and attack from range " +
             "(a caster/archer kites). For ranged mobs set touchRange ≥ this so they can land hits while holding.")]
    public float preferredRange = 0f;
    [Tooltip("0 = never flee. Otherwise, once HP drops below this fraction of max while aggroed, the mob turns " +
             "and runs from the player — a skittish 'wounded animal' that breaks off when hurt.")]
    [Range(0f, 1f)] public float fleeHpFraction = 0f;

    [Header("Ranged projectile (magic mobs only)")]
    [Tooltip("Visible projectile lobbed by a Magic attacker; the hit lands on arrival. Null = instant magic touch.")]
    public Texture2D projectileSheet;
    public int projFrameSize = 16;
    public int projFrames = 4;
    public float projFps = 16f;
    public float projSpeed = 9f;
    public float projScale = 1.1f;

    [Header("Stun indicator (FX/Magic/Spirit)")]
    [Tooltip("Spinning swirl shown over the mob's head while stunned. Null = stun still freezes it (no swirl).")]
    public Texture2D stunSheet;
    public int stunFrameSize = 32;
    public int stunFrames = 5;

    static readonly Color MagicHitColor = new Color(0.7f, 0.5f, 1f);
    static readonly Color PhysHitColor  = new Color(1f, 0.35f, 0.35f);
    static readonly Color StunColor     = new Color(1f, 0.9f, 0.3f);
    static readonly Color LevelUpColor  = new Color(1f, 0.85f, 0.35f);   // mob level-up toast

    // Cached on-arrive for the ranged-caster projectile — allocated once, shared by every shot from every
    // mob. The shot's damage/element/source travel in the HitPayload; `homing` is the player transform
    // (guarded: the projectile only lands on a live Character, same as the old closure).
    static readonly System.Action<Projectile3D.HitPayload> LandRangedHit = p =>
    {
        if (Character.Instance == null || Character.Instance.IsDead) return;
        var mob = (MonsterAI3D)p.source;
        int d = CombatAuthority.Apply(Character.Instance, new DamageInfo(p.amount, p.element, isMagic: true, source: mob));
        if (d > 0)
        {
            DamagePopup.Spawn(p.homing != null ? UnitAnchor.Overhead(p.homing) : p.at, d, MagicHitColor);
            mob.TryInflictOnHit();
            mob.GainCombatXp(d);
        }
    };

    PathAgent3D agent;
    Player3DAttacker playerAtk;   // cached once (Start) so the melee hold-distance can clamp to the player's reach without a per-frame GetComponent
    AttackLunge lunge;
    Transform player;
    Vector3 home;
    float nextWander;
    bool chasing;
    float nextTouch;
    float stunnedUntil;   // hit by a stagger (e.g. the Swordsman's Bash) — frozen until this time
    float tauntUntil;     // taunted (e.g. Bulwark) — forced to chase the player and can't leash/flee until this time
    float mobXp;          // XP toward this mob's next level (earned from damage dealt to the player)
    StatusFX stunFx;      // looping swirl shown over the head while stunned (created on first stun)
    StatusController status;   // poison/bleed/burn/blind host (Stun/Slow still delegate to the fields above)

    /// <summary>This mob's <see cref="Health"/>, cached once. The cursor pick and auto-combat target
    /// scans read this instead of calling <c>GetComponent&lt;Health&gt;()</c> per mob, every frame.</summary>
    public Health Health { get; private set; }

    // ---- the basic swing, this mob's half ------------------------------------------------------------------------
    // WHAT one ordinary attack of ours is made of. Its own attack site above reads these too, so a swing this mob makes
    // by walking up to the player and a swing a skill asks it for are the same numbers, from one place. HOW a swing
    // lands (the crit → accuracy → magnitude order, and the announcement) is BasicSwing's, shared with the player.

    /// <summary>Our accuracy for a swing, Blind already taken off. A MAGIC mob answers −1: its attack can't be dodged,
    /// it only glances.</summary>
    public int BasicHit => attackType == AttackType.Magic
        ? -1
        : Mathf.Max(0, hit - (status != null ? status.HitPenalty : 0));

    /// <summary>The element our attack carries.</summary>
    public Element BasicElement => attackElement;

    /// <summary>True for a caster mob, whose ordinary attack IS its spell.</summary>
    public bool BasicIsMagic => attackType == AttackType.Magic;

    /// <summary>RO-style spread, then our own damage buffs/debuffs — the SAME call the player's roll makes, so "cut
    /// this mob's damage" lands on a mob exactly as it would on the player. <paramref name="crit"/> is ignored: mobs
    /// never crit.</summary>
    public int RollBasicDamage(bool crit)
        => UnitOffense.Dealt(MobStates, attack * (1f - CombatRng.Range(0f, damageSpread)));

    /// <summary>This mob's shared effect store — the SAME <see cref="UnitStates"/> the player carries, so buffs and
    /// debuffs read identically on either. Null only in the standalone spike, which has no Health.</summary>
    public UnitStates MobStates => Health != null ? Health.States : null;

    /// <summary><see cref="Time.time"/> of this mob's last landed touch-attack — the one general "it just attacked"
    /// moment, readable by ANY visual. The billboard's squash tell (<see cref="AttackLunge"/>) is driven straight
    /// from the same beat below; a live-3D rig (<see cref="MobShadow3DVisual"/>) reads this to play a swing clip.</summary>
    public float LastAttackTime { get; private set; }

    /// <summary>The simulate-near / freeze-far switch gating this brain, when the unit has one. Assigned by whatever
    /// builds the unit (a live-3D shadow attaches one); null on a billboard mob, which is cheap enough to run always.
    /// Pushed in rather than looked up so the per-frame path never calls GetComponent (PERFORMANCE.md).</summary>
    public SimulationLod Lod { get; set; }

    /// <summary>When true, this unit travels at the shared CHARACTER gear speeds (<see cref="CharacterSpec"/>) instead
    /// of the mob dial — chasing uses the run gear, wandering the walk gear. A 3D shadow sets this because it IS a
    /// character-sized body, and a character-sized body moving at mob scale (0.30×) visibly shuffles. False (the
    /// default) = ordinary mob speed, so every billboard mob is untouched. Was a reference to the live
    /// <c>Player3DController</c>; the gear speeds now have their own owner, so a mob no longer reads a player at all.</summary>
    public bool UsesCharacterGears { get; set; }

    /// <summary>Live registry of active mobs, so cursor target-magnetism can scan candidates without a
    /// per-frame <c>FindObjectsByType</c>. Maintained on enable/disable (so it respects MobRespawn pooling).</summary>
    public static readonly System.Collections.Generic.List<MonsterAI3D> Active = new System.Collections.Generic.List<MonsterAI3D>();

    /// <summary>Move this mob's wander anchor to <paramref name="p"/>. Used when a pooled mob is reused at a
    /// new spot (<see cref="FieldMobSpawner"/>) so it wanders around where it reappeared, not its first spawn.</summary>
    public void SetHome(Vector3 p) => home = p;

    void Awake()
    {
        agent = GetComponent<PathAgent3D>();
        lunge = GetComponent<AttackLunge>();
        Health = GetComponent<Health>();
        status = Status.Of(gameObject);   // poison/bleed/burn ticks + silence/blind flags
        home = transform.position;
        UnitCrowd.Register(gameObject);   // pass through other units (no stacking / climbing on heads)

        // Floating HP bar + name under the sprite. Auto-added here so EVERY mob gets one (even those baked
        // before the nameplate existed — no re-bake). The training dummy keeps its own HP readout; the
        // boss's bigger plate is configured by BossBanner. DisallowMultipleComponent + this guard avoid a
        // duplicate when one is already present.
        if (GetComponent<TrainingDummy>() == null && !TryGetComponent<MobNameplate>(out _))
            gameObject.AddComponent<MobNameplate>();

        EnsureSimulationLod();
    }

    [Header("Simulate near / freeze far")]
    [Tooltip("Beyond this distance from the player this mob's movement + animation freeze. Must stay comfortably " +
             "WIDER than the camera view so a mob chasing you from just off-screen keeps coming.")]
    public float simulateRadius = 28f;

    /// <summary>Give EVERY mob the simulate-near / freeze-far switch, sprite mobs included.
    ///
    /// <para>The switch was written to be the one shared rule for any unit that costs real per-frame work, and its own
    /// notes say it was extracted precisely because "mobs — the units there are most of — never got it". In practice it
    /// only ever reached the townsfolk and the handful of live-3D shadows: it sat on ZERO of the mob prefabs, so every
    /// ordinary sprite mob kept running its brain, its mover, its status ticking and its animator at any distance.
    /// Attaching it here — the one place every mob passes through — is what actually closes that gap.</para>
    ///
    /// <para>The BRAIN is deliberately not frozen: it early-outs on <see cref="Lod"/> itself so the mob stays in
    /// <see cref="Active"/> and can still be found by target scans. A shadow builds its own switch with its own
    /// radius and component list, so this leaves an already-configured one alone.</para></summary>
    void EnsureSimulationLod()
    {
        if (Lod != null || TryGetComponent<SimulationLod>(out _)) return;

        var lod = gameObject.AddComponent<SimulationLod>();
        lod.simulateRadius = simulateRadius;
        // The mover and the sprite animator are the per-frame costs worth stopping. The nameplate and depth sorter
        // stay live: an off-screen unit's plate is already invisible, and freezing the sorter would strand its sprite
        // at a stale draw order the moment it thaws.
        lod.Freeze(agent, GetComponent<BillboardAnimator3D>());
        Lod = lod;
    }

    void OnEnable()  { if (!Active.Contains(this)) Active.Add(this); }
    void OnDisable() => Active.Remove(this);

    void Start()
    {
        agent.moveSpeed = moveSpeed * UnitScale.ForMob;

        var pc = FindAnyObjectByType<Player3DController>();
        if (pc != null) { player = pc.transform; playerAtk = pc.GetComponent<Player3DAttacker>(); }

        // Crowd avoidance: spread out from other mobs (they no longer physically block each other),
        // but NOT from the player we're chasing — so melee can still close in.
        agent.avoidMask = UnitCrowd.Mask;
        agent.avoidIgnore = player;

        if (Health != null)
            Health.onDeath.AddListener(() => { if (Character.Instance != null) Character.Instance.AddXP(xpReward); });

        nextWander = Time.time + Random.value;
    }

    // XP needed for the next level-up — the first bar (xpToFirstLevel) scaled by the current level.
    int XpToNext => Mathf.Max(1, xpToFirstLevel * level);

    /// <summary>Award combat XP for <paramref name="damageDealt"/> the mob just landed on the player, and level
    /// up (possibly several times) once the bar is reached — the mob's mirror of the player's progression.
    /// Called per-hit (not per-frame); alloc-free.</summary>
    public void GainCombatXp(int damageDealt)
    {
        if (damageDealt <= 0 || xpPerDamageDealt <= 0f) return;
        mobXp += damageDealt * xpPerDamageDealt;
        while (mobXp >= XpToNext)
        {
            mobXp -= XpToNext;
            LevelUp();
        }
    }

    // One choke point for a mob level-up: RE-DERIVE stats from the shared formulas, fully heal, announce, cue.
    // There is deliberately NO growth multiplier here — a mob's level-20 numbers are whatever level 20 produces
    // through UnitStatMath, exactly as they are for the player. (Before 2026-08-10 this scaled attack/HP by a flat
    // 1.15×, a mob-only curve that drifted away from the player's the longer a mob lived.)
    void LevelUp()
    {
        var block = Stats;
        if (block != null) block.LevelUp();      // level++ then re-derive; writes maxHealth/attack/hit/etc. back here
        else
        {
            // No stat block (a hand-placed legacy mob): still level, but nothing to re-derive from.
            level++;
            if (Health != null) Health.RefillToMax();
        }
        // The one thing that DOES still grow by a rate — and legitimately so: xpReward is a REWARD, not a stat. It
        // says what killing this mob is worth to the player, which is a payout curve, not part of the unit model the
        // player and mobs share. Named so it can't be mistaken for a surviving stat multiplier.
        xpReward = Mathf.Max(xpReward + 1, Mathf.RoundToInt(xpReward * (1f + XpRewardGrowthPerLevel)));
        DamagePopup.SpawnText(UnitAnchor.Overhead(transform), "Level Up!", LevelUpColor);
        VfxCue.Fire(VfxCueId.OnLevelUp, transform);
    }

    /// <summary>Clear the progress toward this mob's next level. Part of "a respawn is a fresh creature": without it a
    /// recycled object keeps last life's banked XP and can level up on its first hit back. Paired with
    /// <c>UnitStatBlock.ResetToSpawnLevel</c> from the one revive choke (<c>MobRespawn.Reactivate</c>).</summary>
    public void ResetCombatXp() => mobXp = 0f;

    UnitStatBlock statBlock;
    /// <summary>This mob's live stat block (level + primaries → every derived number). Cached; may be null on a
    /// legacy hand-placed mob that predates the block.</summary>
    public UnitStatBlock Stats => statBlock != null ? statBlock : (statBlock = GetComponent<UnitStatBlock>());

    UnitEquipment gear;
    /// <summary>This mob's worn gear — the owner of its weapon, and so of its REACH (see <see cref="WeaponReach"/>).
    /// Cached on first use, never looked up per frame.</summary>
    public UnitEquipment Equipment => gear != null ? gear : (gear = GetComponent<UnitEquipment>());

    /// <summary>How far this mob can reach, in world units — THE one answer, used by its bite, by the distance it
    /// holds while chasing, and by anything outside asking (a relocation working out where to land beside it).
    ///
    /// <para>Reach comes from the EQUIPPED WEAPON (<c>Item.attackRange</c>), falling back to this mob's unarmed
    /// <see cref="touchRange"/> — the identical rule the player uses, through the shared <see cref="WeaponReach"/>.
    /// A MELEE reach is authored at full unit size and rescaled to the mob's actual size, so a shrunk mob bites at its
    /// body edge instead of across a gap that dwarfs its sprite. A RANGED mob (<see cref="preferredRange"/> &gt; 0)
    /// keeps its authored range untouched: that standoff is a deliberate distance, not body contact.</para>
    ///
    /// <para>Written once here because it was briefly computed in three places with two different rules — the ranged
    /// exception was missing from one of them.</para></summary>
    public float Reach
    {
        get
        {
            float baseReach = WeaponReach.For(Equipment, touchRange);
            // Blend THIS mob's size with its opponent's. Its opponent is the player, which is always built to the
            // shared character body — so the pair is (my size, character size), never a factor guessed in advance.
            return preferredRange > 0f ? baseReach
                                       : baseReach * UnitScale.MeleeBlend(MeleeScale, UnitScale.CharacterScale);
        }
    }

    /// <summary>How big this mob's BODY is, for melee-contact maths — the shared character size when it is a live-3D
    /// shadow (which is built to the same body as the player), the shrunken mob size otherwise. Reported rather than
    /// baked into a reach, so whoever measures against this mob blends the real pair.</summary>
    public float MeleeScale => UsesCharacterGears ? UnitScale.CharacterScale : UnitScale.MobScale;

    /// <summary>Briefly freeze this mob (chase + touch-attack) — a reusable stagger/crowd-control hook for
    /// skills like the Swordsman's Bash. Stacks by taking the later end time, not by adding.</summary>
    public void Stagger(float seconds)
    {
        if (seconds > 0f) stunnedUntil = Mathf.Max(stunnedUntil, Time.time + seconds);
    }

    /// <summary>Stun this mob for <paramref name="seconds"/> — freezes it (reusing the <see cref="Stagger"/>
    /// freeze) and shows a spinning swirl over its head. A reusable status hook for per-hit procs (e.g. the
    /// Mage's multi-hit Meteor). Re-stunning while already stunned just refreshes the duration (no popup spam).</summary>
    public void Stun(float seconds)
    {
        if (seconds <= 0f) return;
        bool wasStunned = Time.time < stunnedUntil;
        Stagger(seconds);   // freeze chase + attack (shares stunnedUntil)
        if (stunSheet != null)
        {
            if (stunFx == null) stunFx = StatusFX.Create(transform, stunSheet, stunFrameSize, stunFrames, 12f, 0.7f, 1.15f);
            stunFx.Show(seconds);
        }
        if (!wasStunned) DamagePopup.SpawnText(UnitAnchor.Overhead(transform), "Stun!", StunColor);
    }

    /// <summary>Force this mob to fixate on the player for <paramref name="seconds"/> — it chases and can't leash
    /// off or flee while taunted (a reusable aggro-lock for guard skills like Bulwark). Stacks by later end time.</summary>
    public void Taunt(float seconds)
    {
        if (seconds <= 0f) return;
        tauntUntil = Mathf.Max(tauntUntil, Time.time + seconds);
        chasing = true;
    }

    bool Taunted => Time.time < tauntUntil;

    /// <summary>Shove this mob directly away from <paramref name="worldFrom"/> at <paramref name="speed"/>
    /// for <paramref name="time"/>s and stagger it for <paramref name="stagger"/>s (so its AI doesn't fight
    /// the slide). Used by AoE skills like the Swordsman's Warcry. Reuses the cached <see cref="agent"/> —
    /// no per-call GetComponent.</summary>
    public void Knockback(Vector3 worldFrom, float speed, float time, float stagger)
    {
        Vector3 dir = transform.position - worldFrom; dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) dir = transform.forward;   // standing on the source → any direction
        if (agent != null) agent.Knockback(dir, speed, time);
        Stagger(Mathf.Max(stagger, time));
    }

    /// <summary>Roll this mob's configured on-hit ailment onto the player — called only when a hit actually
    /// LANDED (so a MISS / Immune doesn't proc it). Applies straight to the player's status host.</summary>
    public void TryInflictOnHit()
    {
        if (onHitAilmentChance <= 0f) return;
        var ps = StatusController.Player;
        if (ps != null && CombatRng.Chance(onHitAilmentChance))
            ps.Apply(onHitAilment, onHitAilmentSeconds, onHitAilmentMagnitude);
    }

    void Update()
    {
        // Frozen because the player is far away (a live-3D mob attaches a SimulationLod; billboards have none and
        // run always). Checked here rather than by disabling this component so the mob stays in Active for target
        // scans. Its mover + animator are the pieces the switch actually disables.
        if (Lod != null && !Lod.Simulating) return;

        if (Time.time < stunnedUntil) { if (agent != null) agent.Halt(); return; }   // staggered — frozen

        // Move-speed states (Slow × Haste) come from the unified store's MoveMult — the SINGLE source, same as the
        // player reads. A Slow makes it <1, a Haste >1; 1 when neither is active. UnitScale.ForMob couples speed to
        // size (smaller mob = slower) so it keeps the same body-lengths/sec at any size. A SpeedSource unit (a 3D
        // shadow) instead reads the player's own gear speeds — same body, same stride — chasing on the run gear and
        // wandering on the walk gear, exactly the two gears every character has.
        float travel = UsesCharacterGears
            ? (chasing ? CharacterSpec.Active.RunGearSpeed : CharacterSpec.Active.WalkGearSpeed)
            : moveSpeed * UnitScale.ForMob;
        agent.moveSpeed = (Health != null ? Health.States.MoveMult : 1f) * travel;

        Vector3 pos = transform.position;
        // A dead player is no target: leash off and resume wandering until they respawn.
        bool playerAlive = player != null && Character.Instance != null && !Character.Instance.IsDead;
        // Compare SQUARED distances against squared radii — avoids a sqrt per check, per mob, per frame.
        float toPlayerSqr = playerAlive ? PlanarSqr(pos, player.position) : Mathf.Infinity;

        bool was = chasing;
        if (Taunted && playerAlive) chasing = true;   // taunt overrides leash — locked onto the player
        else if (chasing && toPlayerSqr > leashRadius * leashRadius) chasing = false;
        else if (!chasing && toPlayerSqr <= aggroRadius * aggroRadius) chasing = true;

        // Touch damage (Phase F parity). No-op in the spike: Character.Instance is null without Systems.
        // Use TRUE 3D distance (not planar) so a mob that has fallen off a cliff/edge below the terrain —
        // same XZ as the player but far below — can't keep biting an invisible, "there are no mobs" hit.
        float touch3DSqr = playerAlive ? (player.position - pos).sqrMagnitude : Mathf.Infinity;
        // MELEE contact range is authored at full unit size; rescale it to the current size (UnitScale) so a shrunk
        // mob bites at its actual body edge rather than across a fixed gap that dwarfs its sprite. RANGED mobs
        // (preferredRange > 0) keep their authored touchRange untouched — their reach is a deliberate standoff, not
        // a body-contact distance, so it must NOT shrink with unit size.
        // Reach comes from the EQUIPPED WEAPON (Item.attackRange), falling back to this mob's unarmed touchRange —
        // the identical rule Player3DAttacker.BaseRange uses, via the shared WeaponReach. Arming a mob now extends
        // its reach exactly as arming the player does; before, a mob's weapon changed its damage but not its reach.
        float touch = Reach;
        if (player != null && touch3DSqr <= touch * touch && Time.time >= nextTouch && Character.Instance != null)
        {
            // Magic mobs hit MagicDefense (and can't be dodged); physical mobs hit Defense/Evasion.
            bool isMagic = attackType == AttackType.Magic;
            int effHit = BasicHit;   // blind already taken off; −1 for a magic mob, which can't be dodged
            // ORDER: accuracy FIRST, damage number second — a whiff never pays for a damage roll, and the roll happens
            // exactly once here rather than again on the player. Mobs don't crit, so crit is false.
            bool missed = Combat.Missed(false, effHit, Character.Instance.Flee);
            int atk = missed ? 0 : RollBasicDamage(false);
            atk = UnitOffense.Boosted(MobStates, atk);   // one attack, one charge — spent on the whiff too
            // A magic mob's attack IS its cast — fire the cast cue at the caster (player-parity OnCastStart).
            if (isMagic) VfxCue.Fire(VfxCueId.OnCastStart, transform);
            if (isMagic && projectileSheet != null)
            {
                // Ranged caster: lob a VISIBLE projectile at the player; the magic damage lands on arrival.
                // The hit state (damage/element/this) rides the projectile's HitPayload into the CACHED
                // LandRangedHit handler — no per-shot closure (PERFORMANCE.md: no `new` per combat event).
                // cast from the mob's mid-body (feet-pivoted)
                Vector3 from = UnitAnchor.Center(transform);
                Projectile3D.Spawn(projectileSheet, projFrameSize, projFrames, projFps, projScale,
                    from, player, UnitAnchor.Center(player), projSpeed, 0.45f, 3f, LandRangedHit,
                    this, null, atk, attackElement);
            }
            else
            {
                int dealt = CombatAuthority.Apply(Character.Instance, isMagic
                    ? new DamageInfo(atk, attackElement, isMagic: true, source: this)                              // magic: MATK vs MDEF + element
                    : new DamageInfo(atk, attackElement, attackerHit: effHit, source: this, missed: missed));      // physical: the HIT vs FLEE result, rolled above
                if (dealt > 0)
                {
                    DamagePopup.Spawn(UnitAnchor.Overhead(player), dealt,
                        attackType == AttackType.Magic ? MagicHitColor : PhysHitColor);
                    TryInflictOnHit();
                    GainCombatXp(dealt);
                }
            }
            // Attack-speed buffs shorten a mob's cadence by the same rule they shorten the player's swing.
            nextTouch = Time.time + UnitOffense.AttackInterval(touchInterval, MobStates != null ? MobStates.AttackSpeedBuffMult : 1f);
            LastAttackTime = Time.time;         // the general "it attacked" beat every visual reads
            if (lunge != null) lunge.Punch();   // squash-stretch "chomp" tell (billboard mobs only)
        }

        if (chasing)
        {
            Pursue(pos, toPlayerSqr);
        }
        else
        {
            if (was) { agent.Halt(); nextWander = Time.time; }
            if (agent.Arrived && Time.time >= nextWander)
            {
                Vector2 r = Random.insideUnitCircle * wanderRadius;
                agent.MoveTo(home + new Vector3(r.x, 0f, r.y));
                nextWander = Time.time + Random.Range(wanderPauseMin, wanderPauseMax);
            }
        }
    }

    // The chase brain, split by behavior style. All branches reuse the per-frame distance already computed
    // in Update — no extra Find/GetComponent/alloc (PERFORMANCE.md: nothing new in the per-mob hot path).
    void Pursue(Vector3 pos, float toPlayerSqr)
    {
        // Skittish: wounded enough → break off and run from the player (unless taunted, which pins it in the fight).
        if (!Taunted && fleeHpFraction > 0f && Health != null && Health.Current <= Health.maxHealth * fleeHpFraction)
        {
            agent.MoveToward(AwayPoint(pos));
            return;
        }

        // Ranged/caster: hold the pocket at preferredRange — kite back if too close, close in if too far,
        // otherwise stand and let the touch-attack above fire (touchRange is set ≥ preferredRange). preferredRange
        // is a deliberate standoff (their "long range"), so it is NOT rescaled by unit size — ranged stays ranged.
        if (preferredRange > 0f)
        {
            float near = preferredRange * 0.8f;
            if (toPlayerSqr < near * near)            agent.MoveToward(AwayPoint(pos));
            else if (toPlayerSqr > preferredRange * preferredRange) agent.MoveToward(player.position);
            else                                       agent.Halt();
            return;
        }

        // Melee: close and bite. The hold distance keeps its AUTHORED FRACTION of this mob's reach (stopDistance ÷
        // touchRange), so a mob holding a long weapon stands off at the same relative spot a fist-fighter does
        // instead of walking to punching distance with a spear. Then rescale to the current unit size, matching the
        // touch range scaled in Update.
        float reachFraction = touchRange > 1e-4f ? stopDistance / touchRange : 1f;
        float stop = Reach * reachFraction;   // same reach the bite uses — one rule, so the two can't disagree
        // INVARIANT: a melee mob must never hold farther than the PLAYER can reach, or the player whiffs every swing
        // while the mob bites freely (authored stopDistance can exceed the player's range, e.g. 0.7 vs 0.5). Clamp the
        // hold distance to just inside the player's effective melee reach so a halted mob is always in trading range.
        if (playerAtk != null) stop = Mathf.Min(stop, playerAtk.RangeAgainst(Health) * 0.9f);
        if (toPlayerSqr <= stop * stop) agent.Halt();
        else agent.MoveToward(player.position);
    }

    // A point ~2 units from the player, directly opposite this mob — the target for kiting / fleeing.
    // Planar; locals only (no allocation).
    Vector3 AwayPoint(Vector3 pos)
    {
        Vector3 away = pos - player.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = transform.forward;   // on top of the player → pick any direction
        away.Normalize();
        return pos + away * 2f;
    }

    static float PlanarSqr(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return (a - b).sqrMagnitude;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, aggroRadius);
        Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, leashRadius);
    }
}
