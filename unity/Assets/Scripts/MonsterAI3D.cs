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
    // NO SPEED, REACH OR SWING TIMING IS TYPED HERE. A creature travels on the character gears, reaches bare-handed
    // or by its weapon, and swings on the cadence every character shares — all read from CharacterSpec.

    [Header("Senses (world units)")]
    public float aggroRadius = 6f;
    public float leashRadius = 10f;

    [Header("Wander")]
    public float wanderRadius = 3f;
    public float wanderPauseMin = 1f;
    public float wanderPauseMax = 3f;

    /// <summary>Physical attack — written by <see cref="UnitStatBlock"/> from level, primaries, gear and ring.</summary>
    [System.NonSerialized] public int attack = 3;
    // Whether its ordinary attack is ranged — the held weapon and, for a shuriken, its higher of STR and DEX. Written by
    // UnitStatBlock with the attack number, from the same totals.
    [System.NonSerialized] public bool swingsRanged;

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

    /// <summary>Outgoing damage swings down to (1−spread)×attack each hit — written by <see cref="UnitStatBlock"/>
    /// from DEX.</summary>
    [System.NonSerialized] public float damageSpread = 0.25f;
    /// <summary>HIT (accuracy), rolled against the target's FLEE on each bite — written by <see cref="UnitStatBlock"/>.</summary>
    [System.NonSerialized] public int hit = 8;
    /// <summary>Whether killing this creature pays experience at all — what it pays is its level's and its kind's
    /// (<see cref="CreatureKinds.KillExperience"/>). Off for the training dummy and for a saved character that is not
    /// a rebel. Set in code each time a body is given its part, so it is never saved.</summary>
    [System.NonSerialized] public bool paysExperience = true;

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

    [Header("Behavior style")]
    [Tooltip("0 = melee (close in and bite). >0 = HOLD this far from the player and attack from range " +
             "(a caster/archer kites). Give it a weapon whose range reaches this far, or it can never land a hit.")]
    public float preferredRange = 0f;
    [Tooltip("0 = never flee. Otherwise, once HP drops below this fraction of max while aggroed, the mob turns " +
             "and runs from the player — a skittish 'wounded animal' that breaks off when hurt.")]
    [Range(0f, 1f)] public float fleeHpFraction = 0f;

    // The next moment this creature's auto-pilot AI is allowed to WEIGH its skills. Its own clock, separate from the
    // bite's, so a creature that has nothing ready does not re-weigh them every frame while it walks toward you.
    float nextCastTry;

    PathAgent3D agent;
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

    /// <summary>The element our ordinary attack carries: a weapon converter's while one runs, otherwise an ELEMENTAL held
    /// weapon's, otherwise this creature's own element — the same one it defends as. A creature attacks and defends with
    /// its nature; there is no separate attack element to author.
    ///
    /// <para>A weapon with NO element (Neutral) does not override that nature — decided 2026-09-23. Every weapon in the
    /// game is Neutral today and every shadow holds one, so otherwise a Fire shadow would swing as nothing at all.</para></summary>
    public Element BasicElement
    {
        get
        {
            var w = Equipment != null ? Equipment.Get(EquipSlotType.Weapon) : null;
            Element natural = w != null && w.def != null && w.def.element != Element.Neutral ? w.def.element
                            : Health != null ? Health.element : Element.Neutral;
            var states = MobStates;
            return states != null ? states.AttackElementOr(natural) : natural;
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
            // Magic crits only in a sniper's hands — the same rule, and the same crit, a person's has.
            crit = WeaponTypes.MagicCanCrit(Equipment != null ? Equipment.WeaponType : WeaponType.None)
                   && RollCrit(targetLuk);
            return crit ? UnitOffense.Dealt(MobStates, magicAttack * critMultiplier)
                        : UnitOffense.Dealt(MobStates, magicAttack * (1f - CombatRng.Range(0f, magicDamageSpread)));
        }
        crit = type == DamageType.Crit || RollCrit(targetLuk);
        return RollBasicDamage(crit);
    }

    /// <summary>Seconds between our swings at our current attack speed — the same interval the attack site waits.</summary>
    public float SwingTime => UnitOffense.AttackInterval(CharacterSpec.Active.swingInterval,
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
    /// moment. Watching machines draw the swing from the beat announced below — a flat creature's squash
    /// (<see cref="AttackLunge"/>) included — never from this brain, which draws nothing.</summary>
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

    [Tooltip("This creature's body size — a MIRROR of its catalog entry, which owns it. Written by the mob baker and " +
             "again as each creature is made (MobBody.Fit), so a prefab baked before a size edit corrects itself.")]
    public float size = MobSizes.Character;

    /// <summary>Live registry of active mobs, so cursor target-magnetism can scan candidates without a
    /// per-frame <c>FindObjectsByType</c>. Maintained on enable/disable (so it respects MobRespawn pooling).</summary>
    public static readonly System.Collections.Generic.List<MonsterAI3D> Active = new System.Collections.Generic.List<MonsterAI3D>();

    // THE SAME CREATURES, FILED BY THE MAP THEY ARE IN — so a search for enemies near a point (a skill's area, a
    // bouncing blade) walks one map's creatures instead of every creature the server holds in every map. Filed when a
    // creature switches on, and FILED AGAIN when it is put into its map (MapManager.PutInMap): a creature built while
    // the game runs lands in whichever map loaded last until then. Removed from the bucket it was filed under, never
    // from whatever its scene says at that moment.
    static readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<MonsterAI3D>> activeByMap =
        new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<MonsterAI3D>>();
    static readonly System.Collections.Generic.List<MonsterAI3D> noCreatures = new System.Collections.Generic.List<MonsterAI3D>();
    string filedUnder;

    /// <summary>The active creatures in <paramref name="map"/>. Never null; the shared empty list when there are none.
    /// Read-only to callers — it is the registry itself.</summary>
    public static System.Collections.Generic.List<MonsterAI3D> ActiveIn(string map)
        => !string.IsNullOrEmpty(map) && activeByMap.TryGetValue(map, out var here) ? here : noCreatures;

    /// <summary>File this creature under the map it is in NOW — called when it is moved into its map.</summary>
    public void Refile()
    {
        if (!Active.Contains(this)) return;
        Unfile();
        File();
    }

    void File()
    {
        filedUnder = gameObject.scene.name;
        if (string.IsNullOrEmpty(filedUnder)) return;
        if (!activeByMap.TryGetValue(filedUnder, out var list))
            activeByMap[filedUnder] = list = new System.Collections.Generic.List<MonsterAI3D>();
        list.Add(this);
    }

    void Unfile()
    {
        if (!string.IsNullOrEmpty(filedUnder) && activeByMap.TryGetValue(filedUnder, out var list)) list.Remove(this);
        filedUnder = null;
    }

    /// <summary>Move this mob's wander anchor to <paramref name="p"/>. Used when a pooled mob is reused at a
    /// new spot (<see cref="FieldMobSpawner"/>) so it wanders around where it reappeared, not its first spawn.</summary>
    public void SetHome(Vector3 p) => home = p;

    /// <summary>Where this creature wanders from — the spot it was last set down at. Where a return-home effect (a
    /// Butterfly Wing's) sends it.</summary>
    public Vector3 Home => home;

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
        Health = GetComponent<Health>();
        status = Status.Of(gameObject);   // poison/bleed/burn ticks + silence/blind flags
        home = transform.position;
        UnitCrowd.Register(gameObject);   // pass through other units (no stacking / climbing on heads)

        // The nameplate holds the creature's display name for every readout. Auto-added here so EVERY mob gets one
        // (even those baked before the nameplate existed — no re-bake), the training dummy included. It draws nothing:
        // every unit's bar and name read through the one HoverPlate.
        // DisallowMultipleComponent + this guard avoid a duplicate when one is already present.
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
        // The mover and the sprite animator are the per-frame costs worth stopping. The depth sorter stays live:
        // freezing it would strand the sprite at a stale draw order the moment it thaws.
        lod.Freeze(agent, GetComponent<BillboardAnimator3D>());
        Lod = lod;
    }

    void OnEnable()
    {
        if (!Active.Contains(this)) { Active.Add(this); File(); }
        // A NEW LIFE HAS NOTHING IN THE AIR: a blade or arrow its last life threw is the flight store's to drop, and it
        // does, because it remembers which life threw it (Flights).
    }
    // A swing still on its way in dies with the life that began it — a body switched off for death or the pool must not
    // land it when it comes back.
    void OnDisable() { Active.Remove(this); Unfile(); swingAt = null; offSwingAt = null; }

    void Start()
    {
        agent.moveSpeed = CharacterSpec.Active.WalkGearSpeed;

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
                // The kill's experience also trains the killer's weapon mastery — Character.AddXP pays both. Read
                // at the moment of death, so a creature that levelled up in the fight pays its new level.
                if (paysExperience)
                {
                    // The gear that pays is its FILE's; a creature with no file (a hand-placed one) pays as ungeared.
                    var file = MonsterCatalog.ById(Health.creatureId);
                    int level = Health.Level;
                    Rewards.PayKill(Health, CreatureKinds.KillExperience(Health.kind, level, file != null ? file.level : level,
                                                                         file != null ? file.gearQuality : Quality.Raw,
                                                                         file != null ? file.gearFolds : 0));
                }
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
    /// <para>Reach comes from the EQUIPPED WEAPON (<c>Item.attackRange</c>), falling back to the shared bare-handed
    /// reach (<see cref="CharacterSpec.unarmedReach"/>) — the identical rule the player uses, through the shared
    /// <see cref="WeaponReach"/>. A MELEE reach is at full unit size and rescaled to the mob's actual size, so a small
    /// mob bites at its body edge instead of across a gap that dwarfs its sprite. A RANGED mob
    /// (<see cref="preferredRange"/> &gt; 0) keeps its range unscaled: that standoff is a deliberate distance, not
    /// body contact.</para>
    ///
    /// <para>Written once here because it was briefly computed in three places with two different rules — the ranged
    /// exception was missing from one of them.</para></summary>
    public float Reach
    {
        get
        {
            // The body's own answer — gear plus any reach a skill is lending — so a reach bonus lengthens this
            // creature's bite exactly as it lengthens a person's swing.
            float baseReach = Health != null ? Health.MeleeReachFullSize
                                             : WeaponReach.For(Equipment, CharacterSpec.Active.unarmedReach, swingsRanged);
            // Blend THIS mob's size with its opponent's. Its opponent is the player, which is always built to the
            // shared character body — so the pair is (my size, character size), never a factor guessed in advance.
            return preferredRange > 0f ? baseReach
                                       : baseReach * UnitScale.MeleeBlend(MeleeScale, UnitScale.CharacterScale);
        }
    }

    /// <summary>How big this mob's BODY is, for melee-contact maths — its size's multiple of a character, read from the
    /// same number its height is fitted from, so how big it looks and how far it reaches can never disagree. A
    /// shadow is always 1, which is exactly the character size. Reported rather than baked into a reach, so
    /// whoever measures against this mob blends the real pair.</summary>
    public float MeleeScale => MobSizes.MeleeScale(size, UnitScale.CharacterScale);

    /// <summary>Briefly freeze this mob (chase + touch-attack) — a reusable stagger/crowd-control hook for
    /// skills like the Swordsman's Bash. Stacks by taking the later end time, not by adding.</summary>
    public void Stagger(float seconds)
    {
        if (seconds > 0f) stunnedUntil = Mathf.Max(stunnedUntil, Time.time + seconds);
    }

    /// <summary>Stun this mob for <paramref name="seconds"/> — freezes it (reusing the <see cref="Stagger"/>
    /// freeze). A reusable status hook for per-hit procs (e.g. the Mage's multi-hit Meteor). Re-stunning while already
    /// stunned just refreshes the duration.</summary>
    public void Stun(float seconds)
    {
        if (seconds <= 0f) return;
        Stagger(seconds);   // freeze chase + attack (shares stunnedUntil)
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

    /// <summary>Roll this mob's configured on-hit ailment onto <paramref name="victim"/> — the unit the blow actually
    /// landed on, which for a bouncing blade is not always the one it is chasing. Called only when a hit LANDED (so a
    /// MISS / Immune doesn't proc it).</summary>
    public void TryInflictOnHit(ICombatUnit victim)
    {
        if (onHitAilmentChance <= 0f || PlayerUnits.Gone(victim)) return;
        var ps = victim.StatusHost;
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

        // A swing already begun connects at its contact moment whatever else this frame holds — a stagger or a root
        // landing during the swing does not take back a blow already on its way, the same as for a person.
        LandSwingIfDue();

        if (Time.time < stunnedUntil) { if (agent != null) agent.Halt(); return; }   // staggered — frozen

        // ROOTED BY ITS OWN SKILL — a wind-up, an aftercast, a run that holds it. The rule a person plays by: no walking
        // and no attacking until the skill lets go.
        if (status != null && status.SkillRooted) { if (agent != null) agent.Halt(); return; }

        // Move-speed states (Slow × Haste) come from the unified store's MoveMult — the SINGLE source, same as the
        // player reads. A Slow makes it <1, a Haste >1; 1 when neither is active. Every creature travels on the
        // character gears a person does — chasing on the run gear, wandering on the walk gear — so its speed is the
        // shared one, never a number typed onto it, and its size does not change it.
        float travel = chasing ? CharacterSpec.Active.RunGearSpeed : CharacterSpec.Active.WalkGearSpeed;
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
        else if (!chasing && toPlayerSqr <= NoticeSqr()) chasing = true;

        // THE TIME IT SPENDS AFTER SOMEBODY counts for them when it dies (KillLedger), from the moment it gives chase:
        // holding a creature's attention is work, whether or not it ever lands a blow.
        if (Health != null && !Health.IsWatchedCopy)
        {
            if (chasing && playerAlive) Health.LedgerForWriting().NoteHeld(Prey, Time.deltaTime);
            else if (was && !chasing && Health.Ledger != null) Health.Ledger.StoppedChasing();
        }

        // ITS WEAPON OUT WHILE IT HUNTS, AWAY WHILE IT DOES NOT — the rule a person plays by, decided here on the
        // server and drawn on every screen. A swing and a weapon skill wait for the weapon to be in hand.
        // Asked only while the weapon is not where the hunt wants it, so a settled creature costs one comparison.
        if (status != null)
        {
            var ph = status.Draw.Phase;
            if (chasing && (ph == DrawPhase.Sheathed || ph == DrawPhase.Sheathing)) status.RequestDraw();
            else if (!chasing && (ph == DrawPhase.Drawn || ph == DrawPhase.Drawing)) status.RequestSheathe();
        }
        bool armed = status == null || status.Draw.IsDrawn;

        // Touch damage (Phase F parity). No-op in the spike: Character.Instance is null without Systems.
        // Use TRUE 3D distance (not planar) so a mob that has fallen off a cliff/edge below the terrain —
        // same XZ as the player but far below — can't keep biting an invisible, "there are no mobs" hit.
        float touch3DSqr = playerAlive ? (player.position - pos).sqrMagnitude : Mathf.Infinity;
        // MELEE contact range is at full unit size; rescale it to this mob's size (Reach) so a small mob bites at
        // its actual body edge rather than across a fixed gap that dwarfs its sprite. RANGED mobs (preferredRange > 0)
        // keep their reach unscaled — a deliberate standoff, not a body-contact distance.
        // Reach comes from the EQUIPPED WEAPON (Item.attackRange), falling back to the shared bare-handed reach —
        // the identical rule the player uses, via the shared WeaponReach.
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
        //
        // AND NEVER STANDING WHERE ITS OWN BLOW CANNOT LAND. A creature that walks in stops inside the nearer of its
        // hold and its reach; one holding a shooting distance is steered by that distance and only asks whether it may
        // shoot.
        float hold = HoldDistance();
        bool standing = Prey != null && player != null && (HoldsAPocket
            ? engagement.AskHolding(player, touch3DSqr, hold * hold)
            : engagement.Ask(player, touch3DSqr, Mathf.Min(hold * hold, touch * touch)));

        bool swingMoment = armed && player != null && standing && touch3DSqr <= touch * touch && Time.time >= nextTouch && Prey != null;

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
            // Attack-speed buffs shorten a mob's cadence by the same rule they shorten the player's swing.
            float beat = SwingTime;
            float contactAfter = 0f;
            if (swingsRanged)
            {
                // A RANGED ATTACK FLIES — an arrow, or a thrown shuriken that bounces and comes back — and lands when
                // it arrives. The ONE flight store a person's ranged attack goes through as well, so a creature's
                // throw is the identical thing from the identical numbers.
                Flights.ThrowBasic(Health, Prey, -1);
                // ABOUT TO ATTACK is the RELEASE for a ranged attack, not the arrival — the throw is the gesture, and
                // a passive that answers it must fire when it is let go.
                PassiveEvents.Fire(Health, SkillTrigger.OnBasicAttackStart, null, player);
            }
            else
            {
                // A MELEE SWING BEGINS HERE AND CONNECTS PART WAY THROUGH — the way a person's does. ABOUT TO ATTACK is
                // raised now, at the start of the swing; the blow itself, its damage and everything it sets off, lands
                // at the contact moment (LandSwingIfDue), which is also the moment every screen draws the bite slamming in.
                PassiveEvents.Fire(Health, SkillTrigger.OnBasicAttackStart, null, player);
                // A PAIR WEAPON LANDS TWICE, by the same rule a person's does (SwingContacts).
                var spec = CharacterSpec.Active;
                swingWeapon = Equipment != null ? Equipment.WeaponType : WeaponType.None;
                int contacts = SwingContacts.Of(swingWeapon, spec.hitTiming, spec.offHandHitTiming, spec.offHandPower,
                                                out float firstAt, out float secondAt, out float secondPower);
                contactAfter = beat * firstAt;
                swingAt = Prey;
                swingLandsAt = Time.time + contactAfter;
                offSwingAt = contacts == 2 ? Prey : null;
                offSwingLandsAt = Time.time + beat * secondAt;
                offSwingPower = secondPower;
            }
            nextTouch = Time.time + beat;
            LastAttackTime = Time.time;         // the general "it attacked" beat every visual reads

            // THE SWING ITSELF, said out loud — the only way any screen learns of it, since a watching machine's copy of
            // this creature has no brain to reach this line. The swing POSE is chosen here because the weapon that
            // decides it is this machine's fact, not a watcher's guess; the beat is how long it has to finish inside,
            // and the art still owns how long the clip takes. This machine draws none of it.
            StanceId swing = WeaponStances.AttackFor(Equipment != null ? Equipment.WeaponType : WeaponType.None, swingsRanged);
            GameNetwork.AnnounceActionOnMob(gameObject, swing, beat, Prey != null ? Prey.Body : null, contactAfter);
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
    // A MELEE SWING ON ITS WAY IN: whom it was aimed at, and the moment it connects (Time.time). Null = none.
    ICombatUnit swingAt;
    float swingLandsAt;
    // A pair weapon's second hit from the other hand: whom, when, and at what share of a full swing. Null = none.
    ICombatUnit offSwingAt;
    float offSwingLandsAt, offSwingPower;
    // The weapon the swing began with — a different one at the contact drops the hit, as it does for a person.
    WeaponType swingWeapon;

    // THE CONTACT MOMENT, on this machine's clock. Everything is re-asked rather than remembered from when the swing
    // began, by the rules a person's swing lands by: the swinger and its target must both still be alive, still
    // enemies, and the target still within reach — so one that walked out during the swing is missed.
    void LandSwingIfDue()
    {
        if (swingAt != null && Time.time >= swingLandsAt) { var first = swingAt; swingAt = null; LandContact(first, 1f); }
        // The other hand's hit only after the first hand's has landed, so the two always connect in order.
        if (offSwingAt != null && swingAt == null && Time.time >= offSwingLandsAt) { var second = offSwingAt; offSwingAt = null; LandContact(second, offSwingPower); }
    }

    void LandContact(ICombatUnit victim, float power)
    {
        if (Health == null || Health.IsDead || PlayerUnits.Gone(victim) || victim.IsDead || victim.Body == null) return;
        if ((Equipment != null ? Equipment.WeaponType : WeaponType.None) != swingWeapon) return;
        if (!Sides.Hostile(Health, victim)) return;
        if ((victim.Body.position - transform.position).sqrMagnitude > Reach * Reach) return;
        LandSwing(victim, announceIntent: false, power);
    }

    /// <summary>One ordinary attack scaled by <paramref name="power"/> — what a thrown blade's hop lands (each jump is
    /// worth less than the last). The creature's own half of a landed hit, its on-hit ailment and its combat
    /// experience, comes with it on every hop, onto the unit the hop actually struck.</summary>
    public int LandSwing(ICombatUnit victim, bool announceIntent, float power)
    {
        if (Health == null || Health.IsDead || PlayerUnits.Gone(victim) || victim.IsDead) return 0;
        int dealt = BasicSwing.Land(Health, victim, power, overkill: false, announceIntent: announceIntent);
        if (dealt > 0)
        {
            TryInflictOnHit(victim);
            GainCombatXp(dealt);
        }
        return dealt;
    }

    // Reused: the ready skills a creature is choosing between at one swing moment. Cleared after every choice.
    static readonly System.Collections.Generic.List<SkillRunner> readySkills = new System.Collections.Generic.List<SkillRunner>(4);

    // Reused: the per-skill verdicts one swing moment writes to the cast log. Only filled while that log is on.
    static readonly System.Text.StringBuilder castNote = new System.Text.StringBuilder(256);

    /// <summary>THE AUTO-PILOT AI'S CHOICE OF SKILL — the same idea by the same name as the one that fights for a
    /// person when they press T, here steering a creature. Cast one of this creature's own skills — the ones it has bought (see Health.Skills) — at its prey
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
            // A weapon skill needs the weapon in hand, as it does for a person.
            if (def.weaponType != WeaponType.None && status != null && !status.Draw.IsDrawn) continue;
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
        // otherwise stand and let the touch-attack above fire (its weapon reaches that far). preferredRange
        // is a deliberate standoff (their "long range"), so it is NOT rescaled by unit size — ranged stays ranged.
        //
        // A TAUNT takes the pocket away: it is the whole point of the verb that the target keeps coming and keeps
        // swinging, so a taunted ranged mob falls through to the melee close-and-bite below rather than backing off.
        // Its swing RATE is untouched — the taunt removes the gaps between attacks, it never shortens the interval.
        if (HoldsAPocket)
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

    /// <summary>Does this creature hold a shooting distance rather than walk in? One answer, read by its feet, by how
    /// close it comes, and by whether it may strike. A taunt takes the distance away.</summary>
    bool HoldsAPocket => preferredRange > 0f && !Taunted;

    /// <summary>HOW CLOSE THIS CREATURE COMES BEFORE IT STANDS AND FIGHTS. One answer, read by the blow and by the
    /// feet, so the two can never disagree — which is exactly what they used to do.
    ///
    /// <para>It stands at its own reach. Two limits bound it, and the second outranks the first:</para>
    ///
    /// <para><b>Never closer than it needs to be to strike.</b> The blow lands the moment the gap is inside its reach,
    /// and this used to go on closing to a fraction of that reach afterwards — so a creature bit while still walking,
    /// side-on, which is nothing a person at a keyboard has ever done.</para>
    ///
    /// <para><b>Never farther than the person can reach back — at THEIR ACTUAL REACH, not a fraction of it.</b> A
    /// creature's reach can exceed the person's own, and a creature holding out there bites freely while every swing
    /// at it whiffs. It used to stop at nine tenths of that reach, which was a number
    /// nobody authored quietly redefining how far the attacker could hit; the reach itself is the one owner of that,
    /// and it is what is read now. Read from the TARGET as a unit rather than off a component bolted to its body: a
    /// person the server is holding has stats but no rig, and this is the identical rule that component was
    /// applying.</para></summary>
    float HoldDistance()
    {
        // A standoff shooter holds its pocket instead; the pocket IS its hold distance. A taunt takes the pocket
        // away — being unable to back off is the whole point of the verb — so a taunted one falls through to melee.
        if (HoldsAPocket) return preferredRange;

        float hold = Reach;

        // A WEAPON THAT SETS ITS OWN RANGE IS RESPECTED. A bow reaches as far as a bow reaches, for a creature exactly as
        // for the person at the keyboard, so a creature holding one stands off at that range instead of walking into
        // arm's length. The limit below is for bare hands, or a weapon that sets no range.
        bool weaponSetsReach = WeaponReach.SetsReach(WeaponReach.WeaponOf(Equipment), swingsRanged);
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
    /// <summary>The squared distance this creature notices its prey from: <see cref="aggroRadius"/>, halved for prey at
    /// peace (<see cref="WeaponDrawMath.NoticeRadius"/>).</summary>
    float NoticeSqr()
    {
        var host = Prey != null ? Prey.StatusHost : null;
        float r = WeaponDrawMath.NoticeRadius(aggroRadius, host != null && host.PeaceBonusesOn);
        return r * r;
    }

    void PickPrey(Vector3 pos)
    {
        // Whoever it has stops counting the moment they die, their body leaves the world, or they leave it
        // themselves — the last of which does NOT look like null from here (see PlayerUnits.Gone).
        if (Prey != null && (PlayerUnits.Gone(Prey) || Prey.IsDead || Prey.Body == null)) SetPrey(null);
        // …and so does one that stopped being an enemy — a saved character that said goodbye stands with nobody.
        if (Prey != null && Health != null && !Sides.Hostile(Health, Prey)) SetPrey(null);

        if (Taunted) return;                       // pinned: the taunt already chose
        if (Time.time < nextPreyCheck) return;
        nextPreyCheck = Time.time + PreyCheckInterval;

        // Already busy with somebody who is still in reach of the chase — leave it be.
        if (Prey != null && chasing && PlanarSqr(pos, Prey.Body.position) <= leashRadius * leashRadius) return;

        // ONLY ITS ENEMIES, AND ONLY IN THIS CREATURE'S OWN MAP. The machine that owns the world holds every map at once
        // in one set of coordinates, so "nearest by distance" alone picks units that are not here at all. Who its
        // enemies are is Sides' answer: people and the creatures fighting for them, for a creature; the creatures, for
        // one fighting for the players; nobody, for one that fights nobody.
        SetPrey(NearestEnemy(pos, gameObject.scene.name));
    }

    ICombatUnit NearestEnemy(Vector3 pos, string map)
    {
        var mySide = Health != null ? Health.side : Side.Creatures;
        if (mySide == Side.None) return null;
        if (mySide == Side.Players) return Sides.NearestCreatureSideCreature(pos, map);

        var person = PlayerUnits.NearestTo(pos, map);
        var ally = Sides.NearestPlayerSideCreature(pos, map, out float allySqr);
        if (ally == null) return person;
        if (person == null || person.Body == null) return ally;
        var host = person.StatusHost;
        float personSqr = WeaponDrawMath.NoticedSqr(PlanarSqr(pos, person.Body.position), host != null && host.PeaceBonusesOn);
        return personSqr <= allySqr ? person : ally;
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
