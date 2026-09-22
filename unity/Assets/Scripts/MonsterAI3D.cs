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

    [Header("Combat")]
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
    [Tooltip("Ailment magnitude — damage per tick for Poison / Bleed / Burn. Stun / Freeze / Silence use none: being " +
             "stopped is binary, so only the duration matters.")]
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

    [Header("Stun indicator (FX/Magic/Spirit)")]
    [Tooltip("Spinning swirl shown over the mob's head while stunned. Null = stun still freezes it (no swirl).")]
    public Texture2D stunSheet;
    public int stunFrameSize = 32;
    public int stunFrames = 5;

    static readonly Color StunColor     = new Color(1f, 0.9f, 0.3f);
    static readonly Color LevelUpColor  = new Color(1f, 0.85f, 0.35f);   // mob level-up toast

    // AN ARROW THIS CREATURE HAS IN THE AIR: who it was shot at, and the moment it arrives. ONE slot, because the
    // swing cadence paces every shot and a creature can therefore only ever have one in flight. Two plain fields
    // rather than a list, so a field of archers allocates nothing (PERFORMANCE.md).
    ICombatUnit arrowAt;
    float arrowLandsAt;

    // The next moment this creature's auto-pilot AI is allowed to WEIGH its skills. Its own clock, separate from the
    // bite's, so a creature that has nothing ready does not re-weigh them every frame while it walks toward you.
    float nextCastTry;

    PathAgent3D agent;
    AttackLunge lunge;
    Transform player;   // whoever this mob is after, as a body to walk at — always Prey's, refreshed with it

    /// <summary>WHO this creature is currently after. Chosen from everybody actually present rather than fixed at
    /// birth: a creature born on a server has nobody in front of it yet, and a creature in a town has several.
    ///
    /// <para>Null when there is nobody to hunt, which is the normal state of a quiet field.</para></summary>
    public ICombatUnit Prey { get; private set; }

    /// <summary>The BODY this creature is after, or null when it is after nobody — the same transform it walks at.
    /// Read by its visual so a creature biting somebody turns to face THEM. Null on a watched copy, which chooses
    /// nothing and so has nobody to turn to.</summary>
    public Transform PreyBody => player;

    // How often a creature looks up and reconsiders whom it is after. Once every half second: fast enough that
    // walking up to a monster gets its attention immediately in human terms, slow enough that a field of them is
    // not re-deciding every frame. It holds whoever it picked in between (see PickPrey).
    const float PreyCheckInterval = 0.5f;
    float nextPreyCheck;
    Vector3 home;
    float nextWander;

    // The world's floor, asked for once and shared. LayerMask.GetMask is a string lookup; a creature must not pay
    // for it every time it decides where to stroll.
    static int wanderGroundMask = -1;
    static int WanderGroundMask
    {
        get
        {
            if (wanderGroundMask < 0) wanderGroundMask = LayerMask.GetMask("Ground");
            return wanderGroundMask;
        }
    }
    bool chasing;
    float nextTouch;
    float stunnedUntil;   // hit by a stagger (e.g. the Swordsman's Bash) — frozen until this time
    float mobXp;          // XP toward this mob's next level (earned from damage dealt to the player)
    StatusFX stunFx;      // looping swirl shown over the head while stunned (created on first stun)
    StatusController status;   // poison/bleed/burn/blind host (Stun/Slow still delegate to the fields above)

    /// <summary>This mob's <see cref="Health"/>, cached once. The cursor pick and auto-pilot target
    /// scans read this instead of calling <c>GetComponent&lt;Health&gt;()</c> per mob, every frame.</summary>
    public Health Health { get; private set; }

    // ---- the basic swing, this mob's half ------------------------------------------------------------------------
    // WHAT one ordinary attack of ours is made of. Its own attack site above reads these too, so a swing this mob makes
    // by walking up to the player and a swing a skill asks it for are the same numbers, from one place. HOW a swing
    // lands (the crit → accuracy → magnitude order, and the announcement) is BasicSwing's, shared with the player.

    /// <summary>Our accuracy for a swing, Blind already taken off. Every creature's ordinary attack is physical, like
    /// the player's, so it can always be dodged.</summary>
    public int BasicHit => Mathf.Max(0, hit);   // any accuracy debuff is already folded into `hit` by UnitStatBlock

    /// <summary>The element our attack carries: the held weapon's, exactly as a player's is, or this creature's own
    /// natural element while its hands are empty.</summary>
    public Element BasicElement
    {
        get
        {
            var w = Equipment != null ? Equipment.Get(EquipSlotType.Weapon) : null;
            return w != null && w.def != null ? w.def.element : attackElement;
        }
    }

    // Derived by UnitStatBlock from the primaries, gear and the ring through the player's own formulas — never authored.
    [System.NonSerialized] public float critChance;
    [System.NonSerialized] public float critMultiplier = UnitStatMath.BaseCritMultiplier;
    [System.NonSerialized] public int magicAttack;
    [System.NonSerialized] public float magicDamageSpread;

    /// <summary>Whether one swing at <paramref name="victim"/> crits — the player's rule: our crit chance less the
    /// victim's LUK shield.</summary>
    public bool RollBasicCrit(ICombatUnit victim) => RollCrit(victim != null ? victim.CritShieldLuk : 0);

    bool RollCrit(int targetLuk) => CombatRng.Chance(UnitStatMath.EffectiveCritChance(critChance, targetLuk));

    /// <summary>One skill strike's base damage from OUR numbers — the mob's half of the rule the player's roll follows:
    /// magic attack with its INT-steadied spread for a magic strike (never a crit), otherwise our physical attack,
    /// critting by the shared rule (always, for a Crit strike).</summary>
    public int RollSkillDamage(DamageType type, int targetLuk, out bool crit)
    {
        if (type == DamageType.Magic)
        {
            crit = false;
            return UnitOffense.Dealt(MobStates, magicAttack * (1f - CombatRng.Range(0f, magicDamageSpread)));
        }
        crit = type == DamageType.Crit || RollCrit(targetLuk);
        return RollBasicDamage(crit);
    }

    /// <summary>Seconds between our swings at our current attack speed — the same interval the attack site waits.</summary>
    public float SwingTime => UnitOffense.AttackInterval(touchInterval,
        attackSpeedMult > 0f ? attackSpeedMult : (MobStates != null ? MobStates.AttackSpeedBuffMult : 1f));

    /// <summary>Our swing speed, stamped by <see cref="UnitStatBlock"/> through the player's own formula — AGI, DEX, worn
    /// attack speed, the ring and any live attack-speed buff. Below zero until stamped (a watching machine's copy, a
    /// training dummy), which falls back to the buff alone.</summary>
    [System.NonSerialized] public float attackSpeedMult = -1f;

    /// <summary>RO-style spread, then our own damage buffs/debuffs — the SAME call the player's roll makes, so "cut
    /// this mob's damage" lands on a mob exactly as it would on the player. A crit hits for full attack × our crit
    /// multiplier and skips the spread, as the player's does.</summary>
    public int RollBasicDamage(bool crit)
        => crit ? UnitOffense.Dealt(MobStates, attack * critMultiplier)
                : UnitOffense.Dealt(MobStates, attack * (1f - CombatRng.Range(0f, damageSpread)));

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

    /// <summary>WHICH WAY THIS CREATURE IS POINTED, in world space, decided by the brain rather than by whatever is
    /// drawing it — the exact mirror of <see cref="Player3DController.WorldFacing"/>, so a creature and the person
    /// fighting it turn by ONE rule.
    ///
    /// <para>Zero means "no opinion, use the line I am travelling" — which is what a wandering or fleeing creature
    /// wants, and what every body already did before this existed.</para>
    ///
    /// <para>It is set to the target for exactly as long as this creature is standing and fighting, which is the
    /// player's own rule (<c>Player3DController.PursueAttackTarget</c> faces its target in the same breath as it
    /// stops). Before this, a creature faced the line it was walking along even while biting, so it bit you side-on
    /// and never turned to look at you.</para></summary>
    public Vector3 WorldFacing => facing.World;

    // Held in the SHARED holder every character uses, so "point at this" and "I have no opinion" cannot mean two
    // different things on a creature and on the person fighting it.
    CharacterBrain.Facing facing;

    void FaceToward(Vector3 dir) => facing.TowardOrNothing(dir);
    void FaceNothing() => facing.AtNothing();

    // "Am I close enough to stand and fight?" — the SHARED answer, the identical one the person at the keyboard
    // asks, so a creature can never decide it by a different rule from the one hitting it.
    UnitOffense.EngageLatch engagement;

    /// <summary>False on a mob that is only a COPY of one the server owns: it is watched, not simulated, so it
    /// must not chase, attack or decide anything for itself.
    ///
    /// <para>Checked inside <see cref="Update"/> rather than by switching this component off, for exactly the
    /// reason the LOD freeze is: disabling it drops the mob out of <see cref="Active"/>, and that list is how
    /// the player finds something to attack, how area effects find their targets, and how the minimap draws its
    /// blips. Switching the component off would silently make every networked mob impossible to click, hit or
    /// see on the map — a mob that is there but cannot be touched.</para></summary>
    public bool ThinksForItself { get; set; } = true;

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

    /// <summary>Follow <paramref name="summoner"/> for this life: wander around it, fight whoever it fights, and never
    /// stray further than <see cref="CreatureKinds.SummonFollowRange"/> from it — past that, drop the target and walk
    /// back. Set by <see cref="CreatureSummons"/> when it calls this creature up.</summary>
    public void Follow(MonsterAI3D summoner) => leader = summoner;

    MonsterAI3D leader;
    float nextFollowStep;

    // A SUMMON'S TURN, before its own choice of prey. True when it is walking back to its summoner and should do nothing
    // else this frame. Only steps its route twice a second, so a summon catching up does not re-plan every frame.
    bool FollowLeader(Vector3 pos)
    {
        if (leader == null || !leader.isActiveAndEnabled || leader.Health == null || leader.Health.IsDead) return false;
        Vector3 at = leader.transform.position;
        home = at;
        float range = CreatureKinds.SummonFollowRange;
        if (PlanarSqr(pos, at) > range * range)
        {
            if (Prey != null) SetPrey(null);
            chasing = false;
            if (Time.time >= nextFollowStep)
            {
                nextFollowStep = Time.time + 0.5f;
                agent.MoveTo(at);
            }
            return true;
        }
        var theirs = leader.Prey;
        if (theirs != null && !theirs.IsDead && Prey != theirs) { SetPrey(theirs); chasing = true; }
        return false;
    }

    void Awake()
    {
        agent = GetComponent<PathAgent3D>();
        lunge = GetComponent<AttackLunge>();
        Health = GetComponent<Health>();
        status = Status.Of(gameObject);   // poison/bleed/burn ticks + silence/blind flags
        home = transform.position;
        UnitCrowd.Register(gameObject);   // pass through other units (no stacking / climbing on heads)

        // Floating HP bar + name under the sprite. Auto-added here so EVERY mob gets one (even those baked
        // before the nameplate existed — no re-bake), the training dummy included: a test target that reads
        // differently from a real mob tests the wrong thing. The boss's name + always-visible bar are configured by
        // BossBanner. DisallowMultipleComponent + this guard avoid a duplicate when one is already present.
        if (!TryGetComponent<MobNameplate>(out _)) gameObject.AddComponent<MobNameplate>();

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

    void OnEnable()
    {
        if (!Active.Contains(this)) Active.Add(this);
        // A NEW LIFE HAS NOTHING IN THE AIR. A recycled body would otherwise land the shot its last life loosed.
        arrowAt = null;
    }
    void OnDisable() => Active.Remove(this);

    void Start()
    {
        agent.moveSpeed = moveSpeed * UnitScale.ForMob;

        // NOBODY IS PICKED HERE, on purpose. This used to look the world over for "the" player and hold whatever it
        // found for life — which on a server is nothing, because a server has no avatar of its own, so every creature
        // it ever spawned went through life with no one to chase. Prey is chosen while it lives instead (PickPrey).

        // Crowd avoidance: spread out from other mobs (they no longer physically block each other),
        // but NOT from whoever we're chasing — so melee can still close in. The exception follows the prey.
        agent.avoidMask = UnitCrowd.Mask;

        // The kill pays WHOEVER LANDED IT, which is a question only the damage door can answer and only once
        // several people can be swinging at the same creature. It used to pay the one player at this keyboard,
        // which on a server is nobody at all.
        if (Health != null)
            Health.onDeath.AddListener(() =>
            {
                // The kill's experience also trains the killer's weapon mastery — Character.AddXP pays both.
                Rewards.PayForKill(Rewards.WhoEarned(Health), xpReward, Health);
            });

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
        DamagePopup.SpawnText(transform, "Level Up!", LevelUpColor);
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

    // This creature's live-3D animation driver, when it has one. Resolved ONCE and the miss remembered too: a flat
    // sprite creature has none and never will, and a plain null-cache would search it out again on every bite.
    Agent3DVisual visual;
    bool lookedForVisual;
    Agent3DVisual Visual
    {
        get
        {
            if (!lookedForVisual) { lookedForVisual = true; visual = GetComponent<Agent3DVisual>(); }
            return visual;
        }
    }

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
        if (!wasStunned) DamagePopup.SpawnText(transform, "Stun!", StunColor);
    }

    /// <summary>The brain's half of the taunt verb: engage the player NOW rather than waiting to be provoked, and take
    /// the store the window was just stamped on. The WINDOW itself is not kept here — <see cref="UnitStates.BeginTaunt"/>
    /// holds it for every taunted unit alike, so the mob brain, the player rig and the skill door read one answer
    /// instead of timing three.
    ///
    /// <para>The store arrives by hand rather than by lookup so the per-frame read below is a plain field: a mob that
    /// has never been taunted never pays a <c>GetComponent</c>, and none is created just to be asked.</para></summary>
    public void LockOnTaunter(UnitStates window)
    {
        taunts = window;
        chasing = true;
    }

    UnitStates taunts;    // handed over by the taunt that armed the window; null until this mob has ever been taunted
    bool Taunted => taunts != null && taunts.Taunted;

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
        var ps = Prey != null ? Prey.StatusHost : null;
        if (ps != null && CombatRng.Chance(onHitAilmentChance))
            ps.Apply(onHitAilment, onHitAilmentSeconds, onHitAilmentMagnitude, Health);
    }

    void Update()
    {
        // Frozen because the player is far away (a live-3D mob attaches a SimulationLod; billboards have none and
        // run always). Checked here rather than by disabling this component so the mob stays in Active for target
        // scans. Its mover + animator are the pieces the switch actually disables.
        if (Lod != null && !Lod.Simulating) return;

        // A copy of a mob the server owns: it is moved by what the server says, not by its own decisions.
        // Same treatment as the freeze above, and for the same reason — it stays in Active so it can still be
        // targeted, hit and drawn on the map.
        if (!ThinksForItself) return;

        // AN ARROW THIS CREATURE LOOSED, ARRIVING. Read before anything that could turn this frame around — a
        // stagger, a root, a flinch — because a shot already in the air is not the shooter's to take back.
        if (arrowAt != null && Time.time >= arrowLandsAt)
        {
            var shotAt = arrowAt;
            arrowAt = null;
            LandOneSwing(shotAt, announceIntent: false);
        }

        if (Time.time < stunnedUntil) { if (agent != null) agent.Halt(); return; }   // staggered — frozen

        // ROOTED BY ITS OWN SKILL — a wind-up, an aftercast, a run that holds it. The rule a person plays by: no walking
        // and no attacking until the skill lets go.
        if (status != null && status.SkillRooted) { if (agent != null) agent.Halt(); return; }

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
        if (FollowLeader(pos)) return;
        PickPrey(pos);
        // A dead or departed target is no target: leash off and resume wandering until somebody else turns up.
        bool playerAlive = player != null && Prey != null && !Prey.IsDead;
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

        // AM I STANDING AND FIGHTING? Asked ONCE, here, BEFORE the blow — which is the order the person at the
        // keyboard has always used: they ask whether they are close enough, and that single answer both stops their
        // feet and lets the swing happen. This creature used to answer it twice, in two places, with two different
        // numbers: the blow fired the moment the gap was inside its reach, while the feet went on closing to a
        // shorter authored hold. So it bit you while still walking, and faced its own path instead of you.
        //
        // Sticky through the shared rule, so somebody hovering at the edge of reach cannot flip it on alternate
        // frames and set the feet, the steps and the whole body flickering.
        //
        // MEASURED ON THE TRUE DISTANCE, not the flat one, because that is what the blow is judged on. They used to
        // be different: the creature decided it had ARRIVED on a measurement that ignores height and whether it could
        // REACH on the real one, so a creature 0.54 away flat and 0.79 away in truth held still against a reach of
        // 0.59 and never landed anything.
        float hold = HoldDistance();
        bool standing = Prey != null && player != null && engagement.Ask(player, touch3DSqr, hold * hold);

        bool swingMoment = player != null && standing && touch3DSqr <= touch * touch && Time.time >= nextTouch && Prey != null;

        // A SKILL INSTEAD OF THE SWING — and it is NOT judged by biting reach. A creature used to have to be close
        // enough to bite you before it was even asked whether it had a skill ready, so every long-range skill it
        // owned was unreachable: a Lunge that a person fires from eight units away could only ever be fired by a
        // creature from under one. Each skill is judged by ITS OWN cast range instead (see Reaches), which is the
        // same rule the person's own aim obeys. The swing's cadence still paces it, so a creature never casts
        // faster than it bites, and a creature that has not engaged you casts nothing at all.
        // PACED BY ITS OWN CLOCK, and that clock moves whether or not anything was cast. Without it a creature
        // walking toward you with nothing ready would weigh every skill it holds on EVERY frame, and a field of them
        // would do it every frame each — the per-frame cost PERFORMANCE.md bans. One look per swing interval instead,
        // which is the same cadence a cast is allowed at anyway.
        bool castMoment = chasing && playerAlive && Prey != null && Time.time >= nextCastTry;
        if (castMoment)
        {
            nextCastTry = Time.time + SwingTime;
            if (TryCastAtPrey()) { nextTouch = Time.time + SwingTime; swingMoment = false; }
        }

        if (swingMoment)
        {
            var held = Equipment != null ? Equipment.WeaponType : WeaponType.None;
            if (WeaponTypes.IsRanged(held))
            {
                // A BOW LOOSES AN ARROW, and the blow lands when it arrives. The flight is timed from the one speed
                // every arrow in the game flies at, and every machine that can see this creature is told to draw it —
                // the identical treatment a person's bow gets, from the identical numbers.
                Vector3 from = UnitAnchor.Center(transform);
                Vector3 to = UnitAnchor.Center(player);
                arrowAt = Prey;
                arrowLandsAt = Time.time + Arrow.FlightSeconds(from, to);
                GameNetwork.AnnounceArrow(transform, player, MapManager.InMap(to, Health.Map), Health.Map);
                // ABOUT TO ATTACK is the RELEASE for a bow, not the arrival — the shot is the gesture, and a passive
                // that answers it must fire when the string is let go.
                PassiveEvents.Fire(Health, SkillTrigger.OnBasicAttackStart, null, player);
            }
            else LandOneSwing(Prey, announceIntent: true);

            // Attack-speed buffs shorten a mob's cadence by the same rule they shorten the player's swing.
            float beat = SwingTime;
            nextTouch = Time.time + beat;
            LastAttackTime = Time.time;         // the general "it attacked" beat every visual reads
            if (lunge != null) lunge.Punch();   // squash-stretch "chomp" tell (billboard mobs only)

            // THE SWING ITSELF, through ONE door on every machine alike. The swing POSE is chosen here because the
            // weapon that decides it is this machine's fact, not a watcher's guess; the beat is how long it has to
            // finish inside, and the art still owns how long the clip takes.
            //
            // Said out loud as well, because a watching machine's copy of this creature has no brain to reach this
            // line: without it a creature bites in silence and the damage numbers climb over a body that never
            // moved. Both halves carry the SAME action and the SAME beat, from here, so a swing can never look one
            // length on the machine that owns the creature and another on the machines watching it.
            StanceId swing = WeaponStances.AttackFor(Equipment != null ? Equipment.WeaponType : WeaponType.None);
            if (Visual != null) Visual.TellAction(swing, Mirror.NetworkTime.time, beat);
            GameNetwork.AnnounceActionOnMob(gameObject, swing, beat);
        }

        // FLINCHED: a hit that cost HP pins this mob's feet for as long as its hurt reaction plays — but not its bite,
        // which is why this sits BELOW the touch attack and only skips the steering. Same rule the player plays by, so
        // chaining hits on a mob keeps it off you exactly as a pack keeps you pinned.
        if (status != null && status.MovementFlinched) { agent.Halt(); return; }

        if (chasing)
        {
            Pursue(pos, toPlayerSqr, standing);
        }
        else
        {
            // NOBODY TO LOOK AT. A wandering creature faces the line it is walking, which is what every body did
            // before a creature had an opinion at all — and leaving the last target's direction standing here would
            // have it strolling around still staring at somebody it gave up on.
            FaceNothing();
            if (was) { agent.Halt(); nextWander = Time.time; }
            if (agent.Arrived && Time.time >= nextWander)
            {
                nextWander = Time.time + Random.Range(wanderPauseMin, wanderPauseMax);

                // BACK ON THE FLOOR, IF IT IS SOMEHOW UNDER IT. A creature that leaves the ground falls, and the
                // distance switch can stop its mover mid-fall — after which nothing moves it again and it hangs
                // there for the rest of its life. MEASURED: two creatures standing 31 metres below a field whose
                // floor was solid directly above them, motionless for minutes. Whatever put them there — an edge,
                // a shove, a gap — this is the answer to all of it, so it is written once here rather than at each
                // of the ways down. The ray starts well above so it can find a floor the creature is BENEATH.
                // perf-ok: once per wander pause per creature, and only while it is idle and being simulated.
                if (GroundSnap.TryGroundY(pos, WanderGroundMask, 60f, 200f, out float floorY) && pos.y < floorY - 1f)
                {
                    transform.position = new Vector3(pos.x, floorY, pos.z);
                    pos = transform.position;
                }

                // AND ONLY WANDER TO A SPOT THAT HAS A FLOOR. The target used to be any point in a circle around
                // home, so a creature living near an edge would eventually be told to walk over it.
                // perf-ok: same beat as above.
                Vector2 r = Random.insideUnitCircle * wanderRadius;
                Vector3 target = home + new Vector3(r.x, 0f, r.y);
                if (GroundSnap.TryGroundY(target, WanderGroundMask, 2f, 40f, out _)) agent.MoveTo(target);
            }
        }
    }

    /// <summary>ONE ordinary attack from this creature onto <paramref name="victim"/> — through the SAME
    /// implementation a person's swing goes through, so the order of the rolls, the charge it spends, and the two
    /// moments a swing raises are written once for every unit in the game.
    ///
    /// <para>It used to roll its own crit, accuracy and damage inline here. Everything matched except the part that
    /// cannot be seen: neither "about to swing" nor "a swing landed" was ever announced, so every passive a creature
    /// could hold that answers either moment was dead on every creature in the world.</para></summary>
    void LandOneSwing(ICombatUnit victim, bool announceIntent)
    {
        if (Health == null || Health.IsDead || PlayerUnits.Gone(victim) || victim.IsDead) return;
        int dealt = BasicSwing.Land(Health, victim, 1f, overkill: false, announceIntent: announceIntent);
        if (dealt > 0)
        {
            TryInflictOnHit();
            GainCombatXp(dealt);
        }
    }

    // Reused: the ready skills a creature is choosing between at one swing moment. Cleared after every choice.
    static readonly System.Collections.Generic.List<SkillRunner> readySkills = new System.Collections.Generic.List<SkillRunner>(4);

    // Reused: the per-skill verdicts one swing moment writes to the cast log. Only filled while that log is on.
    static readonly System.Text.StringBuilder castNote = new System.Text.StringBuilder(256);

    /// <summary>THE AUTO-PILOT AI'S CHOICE OF SKILL — the same idea by the same name as the one that fights for a
    /// person when they press T, here steering a creature. Cast one of this creature's own skills — its weapon's kit or its extra skills — at its prey
    /// if any is ready: off cooldown, affordable, not refused by silence, a taunt or a missing pile, and inside that
    /// skill's OWN cast range. False when nothing was cast, so the ordinary swing happens instead.
    ///
    /// <para><b>The order it picks in</b> (decided by the user, 2026-09-17 and 2026-09-22): the SUMMON first whenever it
    /// is ready and not everything it may have out is out, then the ULTIMATE whenever it is ready and affordable, and
    /// otherwise one of the ready ordinary actives at random. It does not save mana for a bigger
    /// skill and it does not reach for the most expensive one it can pay for. Deliberate combinations — a named
    /// sequence of skills the auto-pilot AI plays in order — are a later piece of work and are not this.</para>
    ///
    /// <para>WITH THE CAST LOG ON, a swing moment writes one line: each active and ultimate it holds, and why each was
    /// or was not ready, then what it picked. A skipped skill used to leave no trace at all, so "it only ever casts two
    /// skills" could not be told apart from "it only ever holds two". The cast that follows writes its own line, or its
    /// refusal with the reason.</para>
    ///
    /// <para>NOT EVERY SWING. A creature waiting on mana asks again every swing and gets the same answer each time, and
    /// a field of them would bury the log. So a line is written when it PICKS a skill (at most once per cooldown), and a
    /// "none ready" line only when the reasons changed since the last one it wrote — a skill coming off cooldown, mana
    /// becoming the blocker, the prey stepping out of reach. The countdown numbers inside a reason do not count as a
    /// change.</para></summary>
    bool TryCastAtPrey()
    {
        var skills = Health != null ? Health.Skills : null;
        if (skills == null || player == null) return false;
        var pressed = skills.SlotsFor(SkillTrigger.OnCast);
        if (pressed.Length == 0) return false;

        bool logging = GameLog.On(LogCat.Cast);
        if (logging) castNote.Clear();

        readySkills.Clear();
        SkillRunner ultimate = null, summon = null;
        // WHICH reason each skill had, as one number — what "the reasons changed" is measured against. Kinds only.
        int reasons = 17;
        for (int s = 0; s < pressed.Length; s++)
        {
            var def = skills.DefAt(pressed[s]);
            if (def == null || def.IsPassive) continue;
            if (!Reaches(def))
            {
                reasons = reasons * 31 + OutOfReachReason;
                if (logging) NoteSkill(def, OutOfReach(def));
                continue;
            }
            var runner = skills.RunnerAt(pressed[s]);
            if (runner == null) continue;
            var block = runner.BlockedBy;
            reasons = reasons * 31 + (int)block;
            if (logging) NoteSkill(def, runner.DescribeBlock(block));
            if (block != SkillRunner.CastBlock.None) continue;
            // THE SUMMON FIRST, every time it is ready — unless everything this creature may have out is already out.
            if (def.id == CreatureKinds.SummonSkillId) { if (!SummonsAtMax) summon = runner; continue; }
            if (def.IsUltimate) { if (ultimate == null) ultimate = runner; }
            else readySkills.Add(runner);
        }

        var pick = summon != null ? summon
                 : ultimate != null ? ultimate
                 : readySkills.Count > 0 ? readySkills[CombatRng.Range(0, readySkills.Count)] : null;
        readySkills.Clear();

        if (logging && (pick != null || reasons != lastNoneReadyReasons))
            GameLog.Info(LogCat.Cast, string.Concat(SpawnIds.MapOf(Health), " ", SpawnIds.Label(Health), " weighed its skills: ",
                castNote.ToString(), pick != null ? " — picked " + pick.def.id : " — none ready, swinging"));
        // A pick clears the memory, so the next wait is written once even if it looks like the last one.
        // …and so does the log being off, so switching it on writes the wait that is already under way.
        lastNoneReadyReasons = !logging || pick != null ? int.MinValue : reasons;

        return pick != null && pick.CastFromRequest(player, player.position, out _);
    }

    // What this creature has summoned, looked up once. A creature that does not summon never has one, and says so.
    CreatureSummons summons;
    bool summonsLooked;
    bool SummonsAtMax
    {
        get
        {
            if (!summonsLooked) { summonsLooked = true; summons = GetComponent<CreatureSummons>(); }
            return summons == null || summons.AtMax;
        }
    }

    // The reasons behind the last "none ready" line this creature wrote. Out-of-reach sits past every CastBlock value.
    int lastNoneReadyReasons = int.MinValue;
    const int OutOfReachReason = 100;

    void NoteSkill(SkillDef def, string verdict)
    {
        if (castNote.Length > 0) castNote.Append("; ");
        castNote.Append(def.id).Append(' ').Append(verdict);
    }

    string OutOfReach(SkillDef def)
    {
        Vector3 gap = player.position - transform.position; gap.y = 0f;
        return string.Concat("out of reach — ", gap.magnitude.ToString("0.##"), " units away, cast range ", def.castRange.ToString("0.##"));
    }

    // Can this skill land on the prey from here? A skill that aims at nothing (a self buff) always can, and so can one
    // with no authored range; otherwise the flat distance to the prey must be inside its cast range.
    bool Reaches(SkillDef def)
    {
        if (!def.NeedsUnitPick && !def.NeedsAimPoint) return true;
        if (def.castRange <= 0f) return true;
        return PlanarSqr(transform.position, player.position) <= def.castRange * def.castRange;
    }

    // The chase brain, split by behavior style. All branches reuse the per-frame distance already computed
    // in Update — no extra Find/GetComponent/alloc (PERFORMANCE.md: nothing new in the per-mob hot path).
    // `standing` is the ONE stand-and-fight answer, decided in Update before the blow, so the feet and the blow can
    // never be judged by two different measurements.
    void Pursue(Vector3 pos, float toPlayerSqr, bool standing)
    {
        // Skittish: wounded enough → break off and run from the player (unless taunted, which pins it in the fight).
        if (!Taunted && fleeHpFraction > 0f && Health != null && Health.Current <= Health.maxHealth * fleeHpFraction)
        {
            agent.MoveToward(AwayPoint(pos));
            // Running away is travel like any other: it faces where it is going, as a person fleeing does.
            FaceNothing();
            return;
        }

        // Ranged/caster: hold the pocket at preferredRange — kite back if too close, close in if too far,
        // otherwise stand and let the touch-attack above fire (touchRange is set ≥ preferredRange). preferredRange
        // is a deliberate standoff (their "long range"), so it is NOT rescaled by unit size — ranged stays ranged.
        //
        // A TAUNT takes the pocket away: it is the whole point of the verb that the target keeps coming and keeps
        // swinging, so a taunted ranged mob falls through to the melee close-and-bite below rather than backing off.
        // Its swing RATE is untouched — the taunt removes the gaps between attacks, it never shortens the interval.
        if (preferredRange > 0f && !Taunted)
        {
            float near = preferredRange * 0.8f;
            if (toPlayerSqr < near * near)            { agent.MoveToward(AwayPoint(pos)); FaceToward(player.position - pos); }
            else if (toPlayerSqr > preferredRange * preferredRange) { agent.MoveToward(player.position); FaceNothing(); }
            // HOLDING ITS GROUND AND SHOOTING IS STANDING AND FIGHTING, so it looks at what it is shooting. Backing
            // off keeps looking at them too — a caster gives ground without turning its back, which is the one place
            // a creature's facing is deliberately NOT its travel line.
            else                                       { agent.Halt(); FaceToward(player.position - pos); }
            return;
        }

        // Melee: close and bite. STANDING AND FIGHTING WAS ALREADY DECIDED THIS FRAME, before the blow, which is the
        // whole point — the person at the keyboard stops and swings on ONE answer, and so does this.
        if (standing)
        {
            agent.Halt();
            // AND LOOKS AT THEM WHILE DOING IT — the same line the player runs in the same breath as standing still.
            FaceToward(player.position - pos);
        }
        else
        {
            agent.MoveToward(player.position);
            // Walking in, the line travelled IS the facing, exactly as it is for a person walking to their target.
            FaceNothing();
        }
    }

    /// <summary>HOW CLOSE THIS CREATURE COMES BEFORE IT STANDS AND FIGHTS. One answer, read by the blow and by the
    /// feet, so the two can never disagree — which is exactly what they used to do.
    ///
    /// <para>The authored hold distance keeps its FRACTION of this creature's reach, so a creature holding a long
    /// weapon stands off at the same relative spot a fist-fighter does instead of walking to punching distance with a
    /// spear. Two limits then bound it, and the second outranks the first:</para>
    ///
    /// <para><b>Never closer than it needs to be to strike.</b> The blow lands the moment the gap is inside its reach,
    /// and this used to go on closing to a fraction of that reach afterwards — so a creature bit while still walking,
    /// side-on, which is nothing a person at a keyboard has ever done.</para>
    ///
    /// <para><b>Never farther than the person can reach back — at THEIR ACTUAL REACH, not a fraction of it.</b> An
    /// authored hold can exceed the person's own reach (0.7 against 0.5), and a creature holding out there bites
    /// freely while every swing at it whiffs. It used to stop at nine tenths of that reach, which was a number
    /// nobody authored quietly redefining how far the attacker could hit; the reach itself is the one owner of that,
    /// and it is what is read now. Read from the TARGET as a unit rather than off a component bolted to its body: a
    /// person the server is holding has stats but no rig, and this is the identical rule that component was
    /// applying.</para></summary>
    float HoldDistance()
    {
        // A standoff shooter holds its pocket instead; the pocket IS its hold distance. A taunt takes the pocket
        // away — being unable to back off is the whole point of the verb — so a taunted one falls through to melee.
        if (preferredRange > 0f && !Taunted) return preferredRange;

        float reachFraction = touchRange > 1e-4f ? stopDistance / touchRange : 1f;
        float hold = Mathf.Max(Reach * reachFraction, Reach);

        // A WEAPON THAT SETS ITS OWN RANGE IS RESPECTED. A bow reaches as far as a bow reaches, for a creature exactly as
        // for the person at the keyboard, so a creature holding one stands off at that range instead of walking into
        // arm's length. The limit below is for an AUTHORED reach — bare hands, or a weapon that sets none.
        var held = WeaponReach.WeaponOf(Equipment);
        bool weaponSetsReach = held != null && held.attackRange > 0f;
        if (!weaponSetsReach && Prey != null && Health != null)
        {
            float preyReach = Prey.MeleeReachFullSize * UnitScale.MeleeBlend(Prey.MeleeScale, Health.MeleeScale);
            if (preyReach > 0f) hold = Mathf.Min(hold, preyReach);
        }
        return hold;
    }

    /// <summary>Look up and decide who this creature is after.
    ///
    /// <para><b>The rule, in one sentence:</b> the nearest living person it can see, kept until they die, leave, or
    /// something takes the choice away. It does NOT re-pick every frame — a creature that switched to whoever edged a
    /// hair closer would spin on the spot between two people standing together — and it does not re-pick at all while
    /// a taunt holds it, because being unable to look away is the whole point of a taunt.</para>
    ///
    /// <para>A creature keeps whoever it has while they are still worth keeping, so walking past a monster that is
    /// already fighting your friend does not steal it off them.</para></summary>
    void PickPrey(Vector3 pos)
    {
        // Whoever it has stops counting the moment they die, their body leaves the world, or they leave it
        // themselves — the last of which does NOT look like null from here (see PlayerUnits.Gone).
        if (Prey != null && (PlayerUnits.Gone(Prey) || Prey.IsDead || Prey.Body == null)) SetPrey(null);

        if (Taunted) return;                       // pinned: the taunt already chose
        if (Time.time < nextPreyCheck) return;
        nextPreyCheck = Time.time + PreyCheckInterval;

        // Already busy with somebody who is still in reach of the chase — leave it be.
        if (Prey != null && chasing && PlanarSqr(pos, Prey.Body.position) <= leashRadius * leashRadius) return;

        // ONLY PEOPLE IN THIS CREATURE'S OWN MAP. The machine that owns the world holds every map at once in one set
        // of coordinates, so "nearest by distance" alone picks people who are not here at all.
        SetPrey(PlayerUnits.NearestTo(pos, gameObject.scene.name));
    }

    void SetPrey(ICombatUnit unit)
    {
        if (Prey == unit) return;
        Prey = unit;
        player = unit != null ? unit.Body : null;
        // Losing somebody takes the opinion about which way to look with it.
        if (player == null) FaceNothing();
        // Do not shoulder past the one you are hunting — the same exception the chase always had, moved with it.
        if (agent != null) agent.avoidIgnore = player;
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
