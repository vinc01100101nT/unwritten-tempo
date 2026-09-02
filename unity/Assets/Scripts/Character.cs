using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>The six RO-style primary stats the player allocates points into.</summary>
public enum PrimaryStat { STR, AGI, VIT, INT, DEX, LUK }

/// <summary>
/// The player's character data — the hub for Phase F's numbers. It lives in the persistent
/// <c>Systems</c> scene (which is never unloaded), so the player avatar, monsters, and the UI
/// all just reference <see cref="Instance"/> and it stays alive across map swaps with no
/// <c>DontDestroyOnLoad</c>. Holds level/XP, base stats + per-level growth, current HP, and
/// equipped items; effective stats = base + growth + equipment bonuses.
///
/// (No save-to-disk yet — a fresh session starts at level 1.)
/// </summary>
public class Character : MonoBehaviour, ICombatUnit
{
    public static Character Instance { get; private set; }

    [Header("Base stats (level 1)")]
    public int baseMaxHP = 20;
    public int baseAttack = 4;
    public int baseDefense = 1;

    [Header("Magic base stats (level 1)")]
    public int baseMaxMP = 10;
    public int baseMagicAttack = 3;
    public int baseMagicDefense = 1;
    [Tooltip("Mana regenerated per second.")]
    public float mpRegenPerSecond = UnitStatMath.BaseMpRegenPerSecond;

    [Header("Element & cards (RO)")]
    [Tooltip("The player's armor PROPERTY. Incoming attacks are scaled by their element vs this (Elements chart).")]
    public Element armorElement = Element.Neutral;
    [Range(0f, 1f)]
    [Tooltip("Lifesteal: fraction of damage you DEAL that silently heals you. The mechanical core of a leech card.")]
    public float lifeStealPct = 0f;
    [Range(0f, 1f)]
    [Tooltip("Damage reflect: fraction of damage you TAKE returned to the attacker. A thorns card.")]
    public float reflectPct = 0f;

    /// <summary>Lifesteal fraction (0..1), read by <see cref="Health"/> when the player deals damage — the permanent
    /// card value plus whatever a timed sub-stat window is adding. Floored at 0 so a debuff cannot turn dealing damage
    /// into taking it.</summary>
    public float LifeStealPct => Mathf.Max(0f, lifeStealPct + SubBonus(SubStat.LifeSteal));

    [Header("Growth per level")]
    public int hpPerLevel = 6;
    public int attackPerLevel = 1;
    public int defensePerLevel = 1;
    public int mpPerLevel = 3;
    public int magicAttackPerLevel = 1;
    public int magicDefensePerLevel = 1;

    [Header("Stat distribution (pre-renewal RO)")]
    [Tooltip("Status points available at the start (base level 1) — the classic pre-renewal grant.")]
    public int startingStatPoints = 48;
    // Pre-renewal economy (see the StatCost / StatusPointsForLevel helpers below the stat region):
    //   • COST to raise a stat from V to V+1 = ⌊V/10⌋ + 2  (each block of 10 gets pricier — the soft cap).
    //   • Points GAINED on reaching base level L = ⌊L/5⌋ + 3 (grows every 5 levels to fund that curve).
    // The old flat `statPointsPerLevel` is retired (the per-level award is now the formula above).

    [Header("Progression")]
    [SerializeField, Min(1)]
    [Tooltip("Character level. Tunable in the inspector (even at runtime) for testing — the per-level " +
             "stat growth scales with it, and the skill % layer + allocated stat-point bonuses still " +
             "apply on top, so a build stays valid at any level you dial in.")]
    int level = 1;
    public int Level => level;

    public int XP { get; private set; }
    public int CurrentHP { get; private set; }
    public int CurrentMP { get; private set; }

    // The six primary stats (indexed by PrimaryStat), each starts at 1. Runtime-only (no save yet).
    readonly int[] primary = { 1, 1, 1, 1, 1, 1 };
    public int StatPoints { get; private set; }

    // The per-point contributions, caps and curve constants all live in UnitStatMath — the ONE copy players and
    // mobs share. Aliased here only where this class's own logic (crit shield, stat economy) reads them.
    const float CritShieldPerLuk = UnitStatMath.CritShieldPerLuk;

    public int GetStat(PrimaryStat s) => primary[(int)s];
    // EFFECTIVE primary stat = allocated points + equipment/card bonuses. Every derived/combat
    // getter below reads P(), so gear that grants +STR now flows into ATK/HP/etc. (The raw
    // allocation economy — costs, caps, GetStat — uses primary[] directly and is unaffected.)
    int P(PrimaryStat s) => primary[(int)s] + equipBonuses.Stat(s);

    // ---- Pre-renewal stat economy -------------------------------------------------------------------
    /// <summary>Pre-renewal base-stat ceiling — a primary stat can't be raised past this with points.</summary>
    public const int MaxStatValue = UnitStatMath.MaxStatValue;

    /// <summary>Points to raise a stat from its current value <paramref name="currentValue"/> to +1:
    /// <c>⌊V/10⌋ + 2</c> (1→2 costs 2, 10→11 costs 3, 20→21 costs 4 …). The pre-renewal soft cap — each
    /// block of ten costs one more, so the last points into a stat are the dearest.</summary>
    public static int StatCost(int currentValue) => UnitStatMath.StatCost(currentValue);

    /// <summary>Status points GAINED on reaching base level <paramref name="level"/> — the shared economy every
    /// unit uses (see <see cref="UnitStatMath.PointsGainedAtLevel"/>).</summary>
    static int StatusPointsForLevel(int level) => UnitStatMath.PointsGainedAtLevel(level);

    /// <summary>Total status points a character who legitimately reached base level <paramref name="lvl"/>
    /// has earned — the starting grant plus every level-up award from 2..lvl.</summary>
    int TotalStatPointsEarned(int lvl) => UnitStatMath.PointsEarnedByLevel(lvl, startingStatPoints);

    /// <summary>Total points it cost to have raised a stat from 1 up to <paramref name="value"/> — the
    /// running integral of <see cref="StatCost"/>, used to reconcile the pool against an allocation.</summary>
    static int StatPointsSpentRaising(int value) => UnitStatMath.PointsToRaise(value);

    /// <summary>Points needed to raise <paramref name="s"/> by one, or -1 if it's already at the cap.</summary>
    public int CostToRaise(PrimaryStat s) => primary[(int)s] >= MaxStatValue ? -1 : StatCost(primary[(int)s]);

    /// <summary>True when <paramref name="s"/> is below the cap AND the next point is affordable.</summary>
    public bool CanRaise(PrimaryStat s)
    {
        int v = primary[(int)s];
        return v < MaxStatValue && StatPoints >= StatCost(v);
    }

    /// <summary>Grant unspent status points (dev/testing — e.g. the DevSettings panel).</summary>
    public void GrantStatPoints(int n) { if (n <= 0) return; StatPoints += n; OnStatsChanged?.Invoke(); }

    /// <summary>Re-clamp current HP/MP to the (possibly changed) max and refire the UI events. Call when
    /// something that changes <see cref="MaxHP"/>/<see cref="MaxMP"/> outside the normal getters happens —
    /// notably a CLASS switch, since vitals are now per-class.</summary>
    public void ReconcileVitals()
    {
        CurrentHP = Mathf.Min(CurrentHP, MaxHP);
        CurrentMP = Mathf.Min(CurrentMP, MaxMP);
        OnStatsChanged?.Invoke();
        OnHealthChanged?.Invoke();
        OnManaChanged?.Invoke();
    }

    /// <summary>Refund every allocated stat point (all stats back to 1) and restore the full pre-renewal
    /// budget for the current level. Also the clean way to migrate a character saved under the old flat
    /// economy onto the new cost curve — or just to re-roll a build. Wired to the DevSettings respec button.</summary>
    public void RespecStats()
    {
        for (int i = 0; i < primary.Length; i++) primary[i] = 1;
        StatPoints = TotalStatPointsEarned(level);
        CurrentHP = Mathf.Min(CurrentHP, MaxHP);
        CurrentMP = Mathf.Min(CurrentMP, MaxMP);
        OnStatsChanged?.Invoke();
        OnHealthChanged?.Invoke();
        OnManaChanged?.Invoke();
    }

    /// <summary>Spend points to raise <paramref name="s"/> by 1 using the pre-renewal cost curve. No-op if
    /// the stat is capped or there aren't enough points for the next (escalating) point.</summary>
    /// <summary>The six primaries and the unspent points, for handing to the machine that shows them. The
    /// server spends points now, so its answer has to reach the person who asked.</summary>
    public int[] CaptureStatsForOwner() => (int[])primary.Clone();

    /// <summary>Take the server's word for level, unspent points and the six primaries.</summary>
    public void RestoreStatsFromServer(int newLevel, int pointsLeft, int[] primaries)
    {
        level = Mathf.Max(1, newLevel);
        StatPoints = Mathf.Max(0, pointsLeft);
        if (primaries != null)
            for (int i = 0; i < primary.Length && i < primaries.Length; i++)
                primary[i] = Mathf.Clamp(primaries[i], 1, MaxStatValue);
        OnStatsChanged?.Invoke();
    }

    public bool AddStat(PrimaryStat s)
    {
        int v = primary[(int)s];
        if (v >= MaxStatValue) return false;
        int cost = StatCost(v);
        if (StatPoints < cost) return false;
        StatPoints -= cost;
        primary[(int)s] = v + 1;
        CurrentHP = Mathf.Min(CurrentHP, MaxHP);   // clamp in case a max dropped (it won't here, but be safe)
        CurrentMP = Mathf.Min(CurrentMP, MaxMP);
        OnStatsChanged?.Invoke();
        OnHealthChanged?.Invoke();
        OnManaChanged?.Invoke();
        return true;
    }

    // The worn set lives in the SHARED holder every unit uses (UnitEquipment) — the player does not get its own
    // equipment system. Each slot holds a unique EquipmentInstance (def + refine + cards), not a shared Item, so a
    // "+7 Iron Sword with 2 cards" is its own thing; combat callers still get the Item via GetEquipped().
    // Lazily attached so no scene or prefab needs editing to gain it.
    UnitEquipment gear;
    /// <summary>What the player is wearing — the same component a mob wears.</summary>
    public UnitEquipment Gear =>
        gear != null ? gear : (gear = GetComponent<UnitEquipment>() ?? gameObject.AddComponent<UnitEquipment>());

    // Non-null ONLY inside DeriveWith, where a hypothetical loadout is previewed. Every stat getter reads
    // `equipBonuses`, so overriding it here makes the whole snapshot reflect the preview without mutating any gear.
    EquipBonuses? previewBonuses;

    /// <summary>Equipment's stat contribution. Cached inside <see cref="UnitEquipment"/> (recomputed only when gear
    /// changes), so the hot combat getters don't re-sum the worn set per hit.</summary>
    EquipBonuses equipBonuses => previewBonuses ?? Gear.Bonuses;

    /// <summary>The Ring of Progression — the VESSEL that carries the player's life's-work: its own
    /// level/XP plus the Trio-Ring passive draft (<see cref="RingProgress.Passives"/>) and the active-skill
    /// ownership (<see cref="RingProgress.Skills"/>). Levels in parallel with the character (same kills feed
    /// both — see <see cref="AddXP"/>). Its effect is GATED by <see cref="RingEquipped"/>: take the ring off
    /// and the passive % stops applying and actives can't fire ("diminished, not erased").</summary>
    public RingProgress Ring { get; } = new RingProgress();

    /// <summary>The Phase G passive skill layer (Trio-Ring draft), living on the ring vessel. Its
    /// <see cref="PassiveRanks.Percent"/> is the % multiplier folded into the effective-stat getters below
    /// — but ONLY while <see cref="RingEquipped"/> (see <see cref="PassivePercent"/>).</summary>
    public PassiveRanks Passives => Ring.Passives;

    /// <summary>True while a ring item occupies the <see cref="EquipSlotType.Ring"/> slot. The gate for the
    /// ring's passive % bonuses (suppressed when no ring is worn). Active skills are NOT gated by the ring —
    /// they come from the equipped weapon now.</summary>
    public bool RingEquipped => GetEquipped(EquipSlotType.Ring) != null;

    /// <summary>The passive % multiplier for a stat — but only while a ring is equipped (0 otherwise).
    /// Every effective-stat getter folds passives in through HERE so unequipping the ring truly diminishes.</summary>
    float PassivePercent(StatId stat) => RingEquipped ? Ring.Passives.Percent(stat) : 0f;

    /// <summary>The gated ring % invested in <paramref name="stat"/> (0 when no ring is worn) — the single value
    /// skill AFFINITY scales off, so an effect's ring-investment bonus diminishes with the ring exactly like the
    /// passive layer does. Reads the same choke as every effective-stat getter.</summary>
    public float RingBonus(StatId stat) => PassivePercent(stat);

    /// <summary>The live signed bonus a timed sub-stat buff or debuff is putting on <paramref name="stat"/>, 0 when
    /// none. Neutral while the baseline snapshot is being taken, so the "before" column of a level-up preview shows
    /// the character without their windows rather than a moment frozen mid-fight.</summary>
    float SubBonus(SubStat stat) => ignoreStates ? 0f : States.SubStatBonus(stat);

    /// <summary>The whole percentage lift on one stat: ring investment plus any live sub-stat window. The single
    /// place the two are added, so no formula has to know that states exist and none can be forgotten.</summary>
    float Lift(StatId ringNode, SubStat stat) => PassivePercent(ringNode) + SubBonus(stat);

    /// <summary>The hotbar layout — which unlocked skill is bound to each of the nine slots (keys 1–9).
    /// Set by drag/click in the Skills (K) menu; read by the bar + the central key router. Persisted.</summary>
    public HotbarBindings Hotbar { get; } = new HotbarBindings();

    /// <summary>Which PASSIVE skills are live — ticked on in the Grimoire (J) or rolled onto the equipped weapon.
    /// The single owner of that answer; <see cref="PassiveEvents"/> gates every dispatch on it. Persisted.
    /// (Not to be confused with <see cref="Passives"/>, which is the ring's stat walk.)</summary>
    public PassiveLoadout PassiveSkills { get; } = new PassiveLoadout();

    Transform avatar;   // the in-scene player, located lazily

    public event Action OnStatsChanged;    // level / equipment changed
    public event Action OnHealthChanged;   // current or max HP changed
    public event Action OnManaChanged;     // current or max MP changed
    public event Action<int> OnLeveledUp;  // passes the new level
    public event Action<int> OnDamaged;    // took a hit — passes the damage (drives the hurt flash)
    public event Action OnBlocked;         // an off-hand blocked a hit (no damage taken)
    public event Action OnEvaded;          // Evasion dodged a hit entirely (no damage taken)
    public event Action OnDied;            // HP hit 0 — the death screen takes over (Phase F)
    public event Action OnRevived;         // came back to life (respawn)
    public event Action OnAppearanceChanged; // cosmetic dye changed (or loaded) — CharacterDye3D re-bakes, save marks dirty

    // Per-cloth-part dye (persisted): parallel authored-sRGB hex arrays, source part-color → chosen color.
    // Character is the single owner; CharacterDye3D is a computed view that reads these and writes back via SetDye.
    public string[] DyeSources { get; private set; }
    public string[] DyeTargets { get; private set; }

    // Signature of the model the dye was authored for — CharacterDye3D drops the dye when this stops matching
    // the worn model, so a color-keyed dye can't bleed onto a different character sharing a base color.
    public string DyeModel { get; private set; }

    /// <summary>Replace the outfit dye map (from the dye UI / CharacterDye3D) and notify listeners.
    /// <paramref name="model"/> stamps which model the map belongs to.</summary>
    public void SetDye(string[] sources, string[] targets, string model)
    {
        DyeSources = sources;
        DyeTargets = targets;
        DyeModel   = model;
        OnAppearanceChanged?.Invoke();
    }

    [Header("Audio")]
    [Tooltip("Default block sound when the equipped off-hand has none of its own.")]
    public SfxSet blockSfx;
    [Tooltip("Played on level-up.")]
    public SfxSet levelUpSfx;
    [Tooltip("Player hurt grunt — played when an unblocked hit lands.")]
    public SfxSet hurtSfx;

    static readonly Color GuardColor = new Color(0.78f, 0.85f, 1f);
    static readonly Color MonsterMissColor = Color.white;   // a monster's swing whiffed the player → white MISS
    /// <summary>The digits of a hit that LANDED on a player — red for a bite, violet for a spell. Public because the
    /// copy of somebody else standing in front of you shows the same two colours (<see cref="NetPlayer.ShowBlow"/>),
    /// and one blow must not be one colour on the screen of the person it happened to and another on everybody
    /// else's.</summary>
    public static readonly Color MonsterHitColor   = new Color(1f, 0.35f, 0.35f);
    public static readonly Color MonsterMagicColor = new Color(0.7f, 0.5f, 1f);
    static readonly Color ReflectColor = new Color(0.85f, 0.85f, 0.95f);   // thorns damage bounced back at a mob

    // Effective stat = flat layer (base + level growth + PRIMARY-STAT contribution + equipment) × (1 + passive%).
    // The passive % layer is deliberately separate so a +% never goes obsolete the way a flat bonus does (doc §7).
    // The purely-derived stats further below (Crit/Evasion/AttackSpeed/CastSpeed) have no flat base.
    // Pre-renewal vitals: the level-driven base curve is scaled by VIT/INT as a PERCENT multiplier (the game
    // is class-agnostic, so there is no per-class HP/SP factor — your stat investment shapes bulk vs mana).
    // Equipment HP/MP add flat on top.
    // Every derived stat below is computed by UnitStatMath — the SHARED formulas mobs run too (design doc
    // "Unified Unit Stats"). This class supplies the three inputs and owns nothing about the arithmetic:
    // its level+primaries+curve (Inputs), its aggregated gear (equipBonuses), and its ring % per channel.
    UnitStatInputs Inputs => new UnitStatInputs
    {
        level = Level,
        str = P(PrimaryStat.STR), agi = P(PrimaryStat.AGI), vit = P(PrimaryStat.VIT),
        intel = P(PrimaryStat.INT), dex = P(PrimaryStat.DEX), luk = P(PrimaryStat.LUK),
        baseMaxHP = baseMaxHP, hpPerLevel = hpPerLevel,
        baseAttack = baseAttack, attackPerLevel = attackPerLevel,
        baseDefense = baseDefense, defensePerLevel = defensePerLevel,
        baseMaxMP = baseMaxMP, mpPerLevel = mpPerLevel,
        baseMagicAttack = baseMagicAttack, magicAttackPerLevel = magicAttackPerLevel,
        baseMagicDefense = baseMagicDefense, magicDefensePerLevel = magicDefensePerLevel,
    };

    // Each stat's lift is ONE number handed to the shared formula: what the ring invested, plus whatever a timed
    // sub-stat buff or debuff is adding right now. Folding the two together here — rather than teaching every formula
    // about states — is what lets a new sub-stat work everywhere by touching one line.
    public int MaxHP   => UnitStatMath.MaxHP(Inputs, equipBonuses, Lift(StatId.HP, SubStat.MaxHP));
    public float HPRegenPerSecond => UnitStatMath.HPRegenPerSecond(Inputs, MaxHP);
    public int Attack  => UnitStatMath.Attack(Inputs, equipBonuses, Lift(StatId.Atk, SubStat.Attack));
    public int Defense => UnitStatMath.Defense(Inputs, equipBonuses, Lift(StatId.Def, SubStat.Defense));

    public int MaxMP        => UnitStatMath.MaxMP(Inputs, equipBonuses, Lift(StatId.MP, SubStat.MaxMP));
    public float MPRegenPerSecond => mpRegenPerSecond + UnitStatMath.MPRegenPerSecond(Inputs, MaxMP);
    public int MagicAttack  => UnitStatMath.MagicAttack(Inputs, equipBonuses, Lift(StatId.MAtk, SubStat.MagicAttack));
    // The ring has no separate M.DEF node — the Def node is dual, so MagicDefense reads the Def passive %.
    public int MagicDefense => UnitStatMath.MagicDefense(Inputs, equipBonuses, Lift(StatId.Def, SubStat.MagicDefense));

    // Pre-renewal DEF split (player side): incoming damage is cut by HARD DEF (a % reduction, from armor)
    // FIRST, then by SOFT DEF (the flat Defense/MagicDefense above, mostly VIT/INT). Hard DEF has no item
    // source yet so it defaults to 0 (damage taken is unchanged) — armor hard-DEF plugs into these later.
    [Tooltip("Pre-renewal HARD DEF %: percent reduction on incoming PHYSICAL damage (0–100), applied before soft Defense.")]
    public int baseHardDefPercent = 0;
    [Tooltip("Pre-renewal HARD MDEF %: percent reduction on incoming MAGIC damage (0–100), applied before soft MagicDefense.")]
    public int baseHardMagicDefPercent = 0;
    /// <summary>Player physical hard-DEF % (a flat percent reduction applied before soft <see cref="Defense"/>).</summary>
    public int HardDefPercent => Mathf.Clamp(baseHardDefPercent, 0, 100);
    /// <summary>Player magic hard-DEF % (a flat percent reduction applied before soft <see cref="MagicDefense"/>).</summary>
    public int HardMagicDefPercent => Mathf.Clamp(baseHardMagicDefPercent, 0, 100);

    // The damage window each hit rolls within. MAX = the full Attack/MagicAttack above; MIN is a
    // fraction of it raised by DEX (physical) / INT (magic). See MinDmgFloorFraction / MinFractionPerStat.
    public int MaxAttack      => Attack;
    public int MinAttack      => UnitStatMath.MinAttack(Attack, P(PrimaryStat.DEX));
    public int MaxMagicAttack => MagicAttack;
    public int MinMagicAttack => UnitStatMath.MinMagicAttack(MagicAttack, P(PrimaryStat.INT));

    public int XPToNext => 5 + Level * 5;
    public bool IsDead => CurrentHP <= 0;

    // IDamageable seam (network-readiness): same one apply-entry a mob's Health exposes, so a future net layer targets
    // player and mob through ONE type. Routes into the existing player sinks (mobs don't crit the player, so isCrit is
    // ignored here) — no behavior change; consumed by the Step D authority seam.
    public Vector3 DamagePosition { get { var a = Avatar(); return a != null ? a.position : transform.position; } }
    public int ApplyDamage(in DamageInfo info)
        => info.isMagic ? TakeMagicDamage(info.amount, info.element, info.source)
                        : TakeDamage(info.amount, info.attackerHit, info.element, info.source, info.missed);
    public int ApplyTrueDamage(int amount, Color color) => TakeTrueDamage(amount, color);

    // ICombatUnit seam: the rest of the shared vocabulary an effect needs, so a skill effect targets the player through
    // the SAME handle it targets a mob through. Body is the spawned AVATAR, not this component's own object — the
    // player's stats live on a persistent data object while the body is a separate spawned rig.
    /// <para>NULL until the avatar is spawned — callers must handle that rather than fall back to this component's own
    /// object, which is the persistent stats holder and must never collect body-level components.</para>
    public Transform Body => Avatar();

    /// <summary>How far this character reaches with nothing in their hands. The weapon's own range wins whenever one
    /// is equipped (<see cref="MeleeReachFullSize"/>); this is the bare-fisted distance.</summary>
    [Header("Reach & cadence")]
    [Tooltip("Bare-handed reach, in world units. A weapon's own attackRange replaces it while one is equipped.")]
    public float unarmedReach = 0.5f;

    [Tooltip("Seconds between ordinary swings before attack-speed is applied.")]
    public float swingInterval = 0.6f;

    /// <summary>The player's FULL-SIZE melee reach — the equipped weapon's range, or their bare-handed reach —
    /// unscaled so the caller can blend it against whatever body it is measuring to.
    ///
    /// <para><b>Read from the GEAR, not from the body.</b> It used to be read off the avatar's attacker, which meant
    /// the answer only existed while a body was standing in the scene — so a server, which holds a character with no
    /// body of its own, got 0 and refused every blow as out of reach. Reach is a fact about what somebody is holding,
    /// and what somebody is holding lives here.</para></summary>
    public float MeleeReachFullSize => WeaponReach.Of(GetEquipped(EquipSlotType.Weapon), unarmedReach);

    /// <summary>SECONDS BETWEEN SWINGS at this unit's current attack speed — the live cadence, buffs included.
    ///
    /// <para>This is the honest way to read attack speed as a COUNT: the speed stat itself is a multiplier around 1
    /// (capped at 3), so it can only become "how many times would I have swung" by way of the interval.</para>
    ///
    /// <para>Owned HERE for the same reason reach is (see <see cref="MeleeReachFullSize"/>): a cadence is a fact about
    /// the character, and it has to have an answer on a machine where that character has no body standing in a
    /// scene.</para></summary>
    public float SwingTime => UnitOffense.AttackInterval(swingInterval, AttackSpeedMult);

    /// <summary>The player is built to the shared character body, so that is its size for melee-contact maths.</summary>
    public float MeleeScale => UnitScale.CharacterScale;

    // Movement + aggro: the player's half of the shared unit verbs. Each forwards to the rig that owns the primitive
    // — the mob half forwards to its pathing agent — so one effect drives either unit through one name. Resolved from
    // the avatar (never this component's own object, which is the persistent stats holder, not a body).
    Player3DController Rig() { var b = Avatar(); return b != null ? b.GetComponent<Player3DController>() : null; }
    // A CHARACTER THE SERVER HOLDS HAS NO LEGS OF ITS OWN. Walking is still the owner's machine to do, so a skill
    // that hauls them somewhere is decided here and performed there — otherwise a dash resolved on a server moved
    // nothing at all, silently, and the person stood where they were while the skill reported success.
    public void TeleportTo(Vector3 dest)
    {
        if (GameNetwork.TellOwnerToMove(this, new NetPlayer.Motion
            { kind = NetPlayer.Motion.Kind.Teleport, destination = dest })) return;
        var r = Rig(); if (r != null) r.Teleport(dest);
    }

    public void GlideTo(Vector3 dest, float speedMult, bool easeIn, bool easeOut, float easeSeconds, Vector3? faceAt)
    {
        if (GameNetwork.TellOwnerToMove(this, new NetPlayer.Motion
            { kind = NetPlayer.Motion.Kind.Glide, destination = dest, speed = speedMult,
              easeIn = easeIn, easeOut = easeOut, seconds = easeSeconds,
              facing = faceAt.HasValue, faceAt = faceAt ?? Vector3.zero })) return;
        var r = Rig(); if (r != null) r.BeginGlideTo(dest, speedMult, easeIn, easeOut, easeSeconds, faceAt);
    }
    public bool IsRelocating { get { var r = Rig(); return r != null && r.SkillGliding; } }
    public void Shove(Vector3 worldFrom, float speed, float seconds, float stagger)
    {
        if (States != null && States.DisablesBlocked) return;   // a shove is a disable: the window turns it away
        if (GameNetwork.TellOwnerToMove(this, new NetPlayer.Motion
            { kind = NetPlayer.Motion.Kind.Shove, destination = worldFrom, speed = speed,
              seconds = seconds, stagger = stagger })) return;
        var r = Rig(); if (r != null) r.Shove(worldFrom, speed, seconds, stagger);
    }
    public void ForceAggro(ICombatUnit source, float seconds)
    {
        if (seconds <= 0f) return;
        States.BeginTaunt(seconds);   // the WINDOW — the same one the mob side stamps, and the one the skill door reads
        // The player engages a MOB, so the taunter has to be one. A mob-vs-mob aggro lock needs the mob brain to hold
        // an arbitrary chase target (it tracks the player today) — that is the mob-vs-mob combat step, not this one.
        var r = Rig();
        if (r != null && source is Health mob) r.Taunt(mob, seconds);
    }

    // Derived passive combat stats (Phase G). These have no flat "base" — they read the
    // passive % layer directly, so a stat with 0 ranks is simply neutral.
    public const float BaseCritMultiplier = UnitStatMath.BaseCritMultiplier;          // pre-renewal: a crit deals 140%…
    // The single Critical ring node drives BOTH crit rate and crit damage — both read PassivePercent(StatId.Critical).
    public float CritMultiplier  => UnitStatMath.CritMultiplier(Inputs, Lift(StatId.Critical, SubStat.CritDamage));   // …+ LUK + the Critical node's % on top
    // These combine the primary-stat contribution (LUK/AGI/DEX) with the passive % layer.
    public float CritChance01    => UnitStatMath.CritChance01(Inputs, equipBonuses, Lift(StatId.Critical, SubStat.CritChance));
    // RO accuracy/evasion. HIT (DEX + level) is rolled against a target's FLEE to decide a physical hit;
    // FLEE (AGI + level, scaled by the Evasion passive) is how often incoming physical attacks whiff on us.
    // Both feed the shared HIT-vs-FLEE roll in Combat; magic ignores them. See Combat for the formula.
    public int Hit  => UnitStatMath.Hit(Inputs, Lift(StatId.Accuracy, SubStat.Accuracy));
    public int Flee => UnitStatMath.Flee(Inputs, Lift(StatId.Evasion, SubStat.Evasion));
    // Pre-renewal ASPD: AGI is the primary driver, DEX contributes a fraction (DexAspdShare). Kept as a
    // SPEED MULTIPLIER (the game's attack model) rather than the RO 0–200 scale. >1 = swing faster.
    public float AttackSpeedMult => UnitStatMath.AttackSpeedMult(Inputs, equipBonuses, PassivePercent(StatId.AttackSpeed), AspdBuffMult);

    /// <summary>The ONE cast-speed number: the multiplier applied to a spell's base wind-up, so a spell's real cast
    /// time is <c>baseCast × CastTimeMult</c>. 1 at 0 DEX, 0 (instant) at DEX 150; the CastSpeed ring node and the
    /// cast-speed buff shorten it further. Lower is faster.</summary>
    public float CastTimeMult => UnitStatMath.CastTimeMult(Inputs, PassivePercent(StatId.CastSpeed), CastBuffMult);

    // ---- Derived-stat snapshots (the pure keystone for the HUD windows) -----------------------
    /// <summary>A readout of every derived stat for the CURRENT loadout (what the Character /
    /// Inventory windows display live).</summary>
    public DerivedSnapshot CurrentDerived => SnapshotNow();

    // ---- states ON vs OFF: the two views the Character window diffs to show "(+N)" ---------------
    // Buffs/debuffs land in DIFFERENT places at runtime — some inside a getter (attack/cast speed), some at swing
    // time (the damage buff scales the roll; Blind subtracts HIT). So a plain snapshot mixes "some states included,
    // some not". These two views make it explicit and uniform: BaseDerived is the character with NO states, and
    // DerivedWithStates is what they're actually fighting at. Diff them for the per-line bonus.
    bool ignoreStates;   // while set, the buff multipliers read neutral — the SAME formulas, minus the states

    /// <summary>Derived stats with every buff/debuff EXCLUDED — the character's own baseline.</summary>
    public DerivedSnapshot BaseDerived
    {
        get { ignoreStates = true; try { return SnapshotNow(); } finally { ignoreStates = false; } }
    }

    /// <summary>Derived stats with the live states folded in — what the player is actually fighting at right now.
    /// Applies each state where it really lands at runtime, so the window can't disagree with combat:
    /// the damage buff scales the attack rolls (as <see cref="RollDamage"/> does), Blind subtracts HIT (as the
    /// attacker does), Slow/Haste scale move speed; attack/cast speed already carry their buffs in the getters.</summary>
    public DerivedSnapshot DerivedWithStates
    {
        get
        {
            var d = SnapshotNow();               // aspd + cast speed already include their buffs
            float b = DamageBuffMult;            // the same multiplier the damage rolls apply
            if (b != 1f)
            {
                d.atkMin = Mathf.RoundToInt(d.atkMin * b);   d.atkMax = Mathf.RoundToInt(d.atkMax * b);
                d.matkMin = Mathf.RoundToInt(d.matkMin * b); d.matkMax = Mathf.RoundToInt(d.matkMax * b);
            }
            d.moveSpeed = Mathf.RoundToInt(d.moveSpeed * States.MoveMult);    // the move-speed sub-stat (NOT the cast/aftercast
                                                                             // root — that's a momentary lock, not a stat)
            return d;
        }
    }

    // Build a snapshot from the live getters. Because every getter routes through P()+equipBonuses,
    // swapping equipBonuses (DeriveWith) makes this reflect any hypothetical loadout — no formula
    // is ever duplicated.
    DerivedSnapshot SnapshotNow() => new DerivedSnapshot
    {
        atkMin = MinAttack,       atkMax = MaxAttack,
        matkMin = MinMagicAttack, matkMax = MaxMagicAttack,
        def = Defense,            mdef = MagicDefense,
        hit = Hit,                flee = Flee,
        maxHP = MaxHP,            maxMP = MaxMP,
        moveSpeed = 100,
        critChance = CritChance01, critMult = CritMultiplier,
        aspdMult = AttackSpeedMult, castTimeMult = CastTimeMult,
        hardDefPct = HardDefPercent, hardMdefPct = HardMagicDefPercent,
        guardCutPct = DamageCutNow, lifeStealPct = LifeStealPct,
        reflectPct = ReflectPctNow, manaLeechPct = ManaLeechPctNow, barrierHP = BarrierNow,
    };

    // State-sourced readouts, neutral while BaseDerived is snapshotting — so each reads 0 on the baseline and shows its
    // real value in the live view, giving the window a clean "0 (+25)" bonus line instead of a silently-mixed number.
    // What the damage-reduction sub-stat is cutting right now, as a positive fraction — the readout the window shows.
    float DamageCutNow    => ignoreStates ? 0f : 1f - States.IncomingMult;
    float ManaLeechPctNow => ignoreStates ? 0f : States.ManaLeechPct;
    int   BarrierNow      => ignoreStates ? 0  : States.ShieldPool;
    // Reflect has BOTH a permanent source (the thorns card) and a timed one — the baseline keeps the permanent part.
    float ReflectPctNow   => reflectPct + (ignoreStates ? 0f : States.ReflectBonus);

    /// <summary>The pure preview: derived stats AS IF <paramref name="hypothetical"/> were equipped
    /// in <paramref name="slot"/> (pass null to preview UN-equipping it), WITHOUT mutating the
    /// character. The hover "ATK 220 → 248 (+28)" preview diffs this against <see cref="CurrentDerived"/>;
    /// equip-compare diffs two such calls. Synchronous + side-effect-free (the cached bonus field is
    /// swapped and restored; no events fire, the equip dictionary is untouched).</summary>
    public DerivedSnapshot DeriveWith(EquipSlotType slot, EquipmentInstance hypothetical)
    {
        // Aggregate the hypothetical set = live gear with `slot` replaced by `hypothetical`. Pure — the worn set is
        // never touched; only this snapshot sees the swap.
        previewBonuses = Gear.BonusesWith(slot, hypothetical);
        try { return SnapshotNow(); }
        finally { previewBonuses = null; }
    }

    /// <summary>Roll the player's outgoing hit: a random value in [<see cref="MinAttack"/>,
    /// <see cref="MaxAttack"/>], or — on a crit (probability <see cref="CritChance01"/>) — the max
    /// ×<see cref="CritMultiplier"/> (crits ignore the spread, RO-style).</summary>
    public int RollDamage(out bool crit) => RollDamage(out crit, 0);

    /// <summary>As above, but the <paramref name="targetLuk"/> applies the pre-renewal crit shield — the
    /// defender's LUK lowers our crit chance (<see cref="CritShieldPerLuk"/> each). Pass 0 for no shield.</summary>
    public int RollDamage(out bool crit, int targetLuk) => RollDamage(out crit, targetLuk, false);

    /// <summary>As above, but <paramref name="forceCrit"/> guarantees a crit (the GuaranteedCrit skill
    /// modifier) — using the real crit math (<see cref="Attack"/> × <see cref="CritMultiplier"/>), not a
    /// cosmetic flag, so the bonus damage and DEF-bypass both apply.</summary>
    public int RollDamage(out bool crit, int targetLuk, bool forceCrit)
    {
        crit = RollCrit(targetLuk, forceCrit);
        return RollDamageMagnitude(crit);
    }

    /// <summary>Roll ONLY whether this attack crits. Split out from <see cref="RollDamage"/> because the accuracy roll
    /// needs the answer first — a crit can't be evaded — and because knowing crit + hit/miss BEFORE the magnitude lets
    /// a whiff skip the damage roll entirely. Order is: crit → accuracy → magnitude.</summary>
    public bool RollCrit(int targetLuk = 0, bool forceCrit = false)
        => forceCrit || CombatRng.Chance(EffectiveCritChance(targetLuk));

    // ---- the basic swing, this unit's half (ICombatUnit) ---------------------------------------------------------
    // WHAT one ordinary swing of ours is made of. HOW a swing is landed — the crit → accuracy → magnitude order and
    // the announcement — belongs to BasicSwing, shared with every other unit, so it is deliberately absent here.

    /// <summary>Our accuracy for a swing. Any accuracy debuff is already inside <see cref="Hit"/> — it lifts the same
    /// one number ring investment does, so there is nothing left to subtract here.</summary>
    public int BasicHit => Mathf.Max(0, Hit);

    /// <summary>The equipped weapon's element (Neutral unarmed) — the weapon is the single owner of it, read at
    /// use-time so there is no per-equip copy to keep in sync.</summary>
    public Element BasicElement
    {
        get { var w = GetEquipped(EquipSlotType.Weapon); return w != null ? w.element : Element.Neutral; }
    }

    /// <summary>A player's ordinary attack is physical — a spell is a cast, never a swing.</summary>
    public bool BasicIsMagic => false;

    public bool RollBasicCrit(ICombatUnit victim) => RollCrit(victim != null ? victim.CritShieldLuk : 0);

    public int RollBasicDamage(bool crit) => RollDamageMagnitude(crit);

    /// <summary>The player has no LUK stat, so nothing shields against a crit on us — and nothing reads this today,
    /// because mobs never crit.</summary>
    public int CritShieldLuk => 0;

    /// <summary>Roll the damage NUMBER for an attack whose crit is already decided. Only called once the strike is
    /// known to connect. UnitOffense.Dealt folds in the damage buffs — the same call a mob's swing makes, so both
    /// scale identically.</summary>
    public int RollDamageMagnitude(bool crit)
        => crit ? UnitOffense.Dealt(States, Attack * CritMultiplier)
                : UnitOffense.Dealt(States, CombatRng.Range(MinAttack, Attack + 1));

    /// <summary>As <see cref="RollDamage"/> but for a spell — rolls in [<see cref="MinMagicAttack"/>,
    /// <see cref="MaxMagicAttack"/>]. Magic NEVER crits (RPS triangle — crit is the Finesse armour-piercer).</summary>
    public int RollMagicDamage(out bool crit) => RollMagicDamage(out crit, 0);

    /// <summary>Spell roll. <paramref name="targetLuk"/> is ignored — magic can't crit, so no crit shield applies;
    /// the param is kept only for call-site signature compatibility.</summary>
    public int RollMagicDamage(out bool crit, int targetLuk)
    {
        // Magic NEVER crits: crit is the Finesse armour-piercer; a critting spell would pierce armour too and
        // collapse the Might>Arcane>Finesse triangle (design "Combat Ontology").
        crit = false;
        return UnitOffense.Dealt(States, CombatRng.Range(MinMagicAttack, MagicAttack + 1));
    }

    /// <summary>Our crit chance against a defender with <paramref name="targetLuk"/> LUK — base
    /// <see cref="CritChance01"/> minus the crit shield, clamped to [0, 0.95].</summary>
    float EffectiveCritChance(int targetLuk) => Mathf.Clamp(CritChance01 - targetLuk * CritShieldPerLuk, 0f, 0.95f);

    /// <summary>Try to spend <paramref name="amount"/> mana; returns false (and spends nothing)
    /// if there isn't enough. Used by the caster before firing a spell.</summary>
    public bool SpendMana(int amount)
    {
        if (amount <= 0) return true;
        if (CurrentMP < amount) return false;
        // A WATCHED COPY DOES NOT PAY ITS OWN COSTS, for the same reason it does not take its own damage: the pool
        // belongs to the character the server holds, and a machine subtracting its own copy would drift out of step
        // with it and start refusing casts the server would have allowed. It still answers "yes, that was
        // affordable" so the picture of the cast goes ahead — the server decides whether it really did.
        if (IsWatchedCopy) return true;
        CurrentMP -= amount;
        OnManaChanged?.Invoke();
        return true;
    }

    /// <summary>Restore <paramref name="amount"/> MP (clamped to max). Used by weapon Mana-Leech (on damage
    /// dealt) and Refund (part of a skill's cost back on cast). No-op for ≤0.</summary>
    public void GainMana(int amount)
    {
        if (amount <= 0 || CurrentMP >= MaxMP) return;
        if (IsWatchedCopy) return;
        CurrentMP = Mathf.Min(MaxMP, CurrentMP + amount);
        OnManaChanged?.Invoke();
    }

    /// <summary>The mana the server says this character has — the twin of <see cref="SetHealthFromServer"/>, arriving
    /// on the same beat. Mana had to become the server's word the moment skills did: the server refuses a cast
    /// nobody can pay for, and two machines counting the same pool separately disagree about what that is.</summary>
    public void SetManaFromServer(int current, int max)
    {
        CurrentMP = Mathf.Clamp(current, 0, Mathf.Max(1, max));
        OnManaChanged?.Invoke();
    }

    // ── Stamina (sprint pool) ─────────────────────────────────────────────────
    // The resource V-sprint drains and every non-sprint stance recharges. This is the SINGLE owner of the value +
    // its max; Player3DController owns only the drain/recharge RATES and drives the changes (it knows the stance).
    [Tooltip("Max stamina — the sprint pool size. Player3DController drains this while V-sprinting and refills it by stance.")]
    public float maxStamina = 100f;
    /// <summary>Current stamina (0..<see cref="maxStamina"/>). Starts full.</summary>
    public float Stamina { get; private set; }
    /// <summary>Stamina as a 0..1 fill for the HUD stamina bar.</summary>
    public float Stamina01 => maxStamina > 0f ? Mathf.Clamp01(Stamina / maxStamina) : 0f;
    /// <summary>Fired whenever stamina changes — drives the HUD stamina bar.</summary>
    public event Action OnStaminaChanged;

    /// <summary>Set stamina (clamped to 0..max). Fires <see cref="OnStaminaChanged"/> only on a real change. The
    /// sole mutation point — Player3DController computes the per-frame drain/recharge and pushes the result here.</summary>
    public void SetStamina(float value)
    {
        value = Mathf.Clamp(value, 0f, maxStamina);
        if (value == Stamina) return;
        Stamina = value;
        OnStaminaChanged?.Invoke();
    }

    void Awake()
    {
        // "There is exactly one player" is true of a SCREEN, not of a world. On a server there are as many
        // players as there are people signed in, so destroying the second one — which is what this used to do
        // — would delete a real person's character the moment they arrived. The singleton keeps its original
        // meaning on a client (the one player at this keyboard) and simply does not apply on a server, where
        // nobody is at the keyboard and every player is somebody else.
        if (!GameServer.IsServer)
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            // AND THE MONEY ON SCREEN FOLLOWS THIS ONE. The counter and the bag footer are built with the
            // heads-up display and outlive any particular character, so they ask Wallet once and it follows
            // whoever is current — rather than each of them having to notice a character being swapped.
            Wallet.ListenTo(this);

            // The one person at this keyboard is a player in this world, and everything that asks "who is here?"
            // — a creature choosing whom to chase, most of all — asks that question of the roll, not of this
            // handle. A SERVER's characters do not do this here: the copy sitting in its persistent scene is
            // nobody, and only the fighters built for real connections join the roll (see PlayerFighter).
            PlayerUnits.Enter(this);
        }
        Stamina = maxStamina;   // sprint pool starts full

        // Spending a skill point changes both the displayed stats and (for HP) the max —
        // re-fire the existing UI events so the HUD/Character panel/Skills panel all refresh.
        Passives.OnChanged += () =>
        {
            CurrentHP = Mathf.Min(CurrentHP, MaxHP);
            CurrentMP = Mathf.Min(CurrentMP, MaxMP);
            OnStatsChanged?.Invoke();
            OnHealthChanged?.Invoke();
            OnManaChanged?.Invoke();
        };

        StatPoints = startingStatPoints;
        EnsureRingEquipped();        // start wearing your ring so the passive/active layer is live by default
        CurrentHP = MaxHP;
        CurrentMP = MaxMP;
    }

    /// <summary>Put a ring on if the <see cref="EquipSlotType.Ring"/> slot is empty — so a fresh (or loaded)
    /// session always boots with the vessel worn. Prefers an authored Ring item from Resources/Items; falls
    /// back to a runtime-built default so the feature works even before the asset is created.</summary>
    public void EnsureRingEquipped()
    {
        if (GetEquipped(EquipSlotType.Ring) != null) return;
        var def = LoadRingDef();
        if (def != null) SetEquipped(EquipSlotType.Ring, new EquipmentInstance(def));
    }

    static Item LoadRingDef()
    {
        var all = Resources.LoadAll<Item>("Items");
        for (int i = 0; i < all.Length; i++)
            if (all[i] != null && all[i].slot == EquipSlotType.Ring) return all[i];

        // Runtime fallback (no authored asset yet): a plain vessel so the slot can be filled.
        var ring = ScriptableObject.CreateInstance<Item>();
        ring.id = "item.ring-of-beginnings";
        ring.displayName = "Ring of Beginnings";
        ring.slot = EquipSlotType.Ring;
        ring.category = ItemCategory.Equip;
        ring.rarity = Rarity.Rare;
        ring.description = "Your life's-work vessel. Its power grows as you walk the ring.";
        return ring;
    }

    // Tuning the `level` field in the inspector (at runtime) recomputes every derived stat — MaxHP,
    // Attack, MaxMP, … all read Level — so refill HP/MP to the new max and re-fire the UI events. The
    // skill % layer (Passives) and allocated stat points are separate layers that still apply on top,
    // so the displayed numbers stay consistent for the dialed-in level. No-op outside Play / before Awake.
    void OnValidate()
    {
        if (level < 1) level = 1;
        if (!Application.isPlaying || Instance != this) return;
        ReconcilePointsToLevel();
        CurrentHP = MaxHP;
        CurrentMP = MaxMP;
        OnStatsChanged?.Invoke();
        OnHealthChanged?.Invoke();
        OnManaChanged?.Invoke();
    }

    /// <summary>Top up (or trim) the allocatable pools so they match what a character that actually
    /// LEVELED to <see cref="level"/> would have. Points already spent stay spent, so this is
    /// idempotent whether the inspector level is dragged up or down. (Dragging far below your spent
    /// total simply leaves the pools at 0 — the allocations you made remain.)</summary>
    void ReconcilePointsToLevel()
    {
        // Stat points: total EARNED for this level (start grant + the pre-renewal per-level awards) minus
        // the total SPENT on the current allocation (the cost-curve integral). Idempotent up or down.
        int spentStat = 0;
        for (int i = 0; i < primary.Length; i++) spentStat += StatPointsSpentRaising(primary[i]);
        StatPoints = Mathf.Max(0, TotalStatPointsEarned(level) - spentStat);

        // Passive draft points are NO LONGER tied to character level — they come from RING level-ups
        // (see RingProgress.AddXP). Dialing the character level here only reconciles the stat-point pool.
    }

    /// <summary>Dev/testing: set the character level at runtime — reconciles the stat/skill point pools
    /// and refills HP/MP, exactly like dragging the inspector <c>level</c> field (see <see cref="OnValidate"/>).
    /// Used by the DevSettings (P) panel's "Level" knob.</summary>
    public void SetLevel(int newLevel)
    {
        level = Mathf.Max(1, newLevel);
        ReconcilePointsToLevel();
        CurrentHP = MaxHP;
        CurrentMP = MaxMP;
        OnStatsChanged?.Invoke();
        OnHealthChanged?.Invoke();
        OnManaChanged?.Invoke();
    }

    public Item GetEquipped(EquipSlotType slot)
    {
        var inst = Gear.Get(slot);
        return inst != null ? inst.def : null;
    }

    /// <summary>The full instance (def + refine + cards) in a slot, or null if empty.</summary>
    public EquipmentInstance GetEquippedInstance(EquipSlotType slot) => Gear.Get(slot);

    /// <summary>Equip a bare <see cref="Item"/>, wrapped as a +0 / no-card instance. Kept for the
    /// existing drag-drop equip UI; the instance overload below carries refine + cards.</summary>
    public void SetEquipped(EquipSlotType slot, Item item)
        => SetEquipped(slot, item != null ? new EquipmentInstance(item) : null);

    // Guards against an equip-event feedback loop: a UI refresh handler that writes equipment back
    // into a slot (whose Changed event re-enters SetEquipped) would otherwise recurse until the stack
    // overflows. While we're already inside SetEquipped notifying listeners, a re-entrant call still
    // applies the equip but does NOT re-fire the events — so the loop can't run away.
    bool notifyingEquip;

    /// <summary>Equip (or, with a null instance, clear) a slot and refresh derived stats.</summary>
    public void SetEquipped(EquipSlotType slot, EquipmentInstance instance)
    {
        if (slot == EquipSlotType.None) return;
        Gear.Equip(slot, instance != null && instance.def != null ? instance : null);
        CurrentHP = Mathf.Min(CurrentHP, MaxHP);   // clamp if max dropped

        if (notifyingEquip) return;                // re-entered from a listener — apply, but don't re-notify
        notifyingEquip = true;
        try
        {
            OnStatsChanged?.Invoke();
            OnHealthChanged?.Invoke();
        }
        finally { notifyingEquip = false; }

        // V2: the weapon owns its skill kit — equipping (or removing) a weapon replaces the numbered hotbar with its
        // granted skills. Only fires on a real equip action, not on load (RestoreEquipment writes the dict directly).
        if (slot == EquipSlotType.Weapon)
        {
            var kit = (instance != null && instance.def != null) ? instance.skillIds : null;
            if (Hotbar.SetWeaponKit(kit)) HotbarPanel.RebuildRequested?.Invoke();
        }
    }

    // ---- Invulnerability (i-frames) ---------------------------------------------------------------
    /// <summary>True while the player has invincibility frames — set by the controller for the duration of the
    /// dodge-dash. Incoming physical AND magic hits are fully negated and pop a "Dodge" cue. Runtime-only.</summary>
    public bool Invulnerable { get; set; }

    /// <summary>Persistent GM invincibility (the <c>/invuln</c> command). Unlike <see cref="Invulnerable"/>
    /// (transient dash i-frames the controller re-arms each frame), this stays until toggled off, and negates
    /// hits SILENTLY (no "Dodge" spam). Folded into the one <see cref="DodgedByInvuln"/> choke so both the
    /// physical and magic damage paths honor it. Runtime-only.</summary>
    public bool Invincible { get; set; }

    static readonly Color DodgeColor = new Color(0.55f, 0.9f, 1f);
    // The soaked-by-barrier number (Shield skill) — a pale cyan so an absorbed hit reads distinct from HP loss.
    static readonly Color ShieldAbsorbColor = new Color(0.5f, 0.8f, 1f);

    // Negate an incoming hit because we're dashing through it; pop a small "Dodge" cue over the avatar.
    bool DodgedByInvuln()
    {
        if (Invincible) return true;   // GM invuln: silently eat the hit
        if (!Invulnerable) return false;
        var a = Avatar();
        if (a != null) DamagePopup.SpawnText(UnitAnchor.Overhead(a), "Dodge", DodgeColor);
        OnEvaded?.Invoke();
        return true;
    }

    // The unified per-unit state store (buffs/debuffs) — single source for gameplay queries AND the buff-tray icons.
    // Lazily created on the player's GameObject; cached after first touch (no per-hit GetComponent).
    UnitStates states;
    public UnitStates States => states != null ? states : (states = GetComponent<UnitStates>() ?? gameObject.AddComponent<UnitStates>());

    /// <summary>Who last cost this unit health — see <see cref="ICombatUnit.LastAttacker"/>.</summary>
    public ICombatUnit LastAttacker { get; private set; }
    public void NoteAttacker(ICombatUnit attacker) { if (attacker != null) LastAttacker = attacker; }

    /// <summary>TRUE on a copy of a character the SERVER owns — the one on this player's own screen while a server
    /// holds the real one. It shows every blow (the number, the flash, the stagger) but its health is told to it
    /// rather than worked out, so two machines can never disagree about how hurt somebody is.</summary>
    public bool IsWatchedCopy { get; private set; }

    /// <summary>Say that this character's health is the server's to decide. One-way and permanent for this life —
    /// a copy never becomes an authority.</summary>
    public void MarkAsWatchedCopy() => IsWatchedCopy = true;

    /// <summary>The ONE place health comes off, so the watched-copy rule is written once and cannot be forgotten by
    /// whoever adds the next way to be hurt.</summary>
    void SubtractHealth(int dmg)
    {
        if (IsWatchedCopy) return;
        CurrentHP = Mathf.Max(0, CurrentHP - dmg);
    }

    /// <summary>The health the server says this character has. The authoritative figure, arriving on the machine of
    /// the person it belongs to; it corrects whatever the local presentation guessed within a tenth of a second.</summary>
    public void SetHealthFromServer(int current, int max)
    {
        bool wasAlive = !IsDead;
        CurrentHP = Mathf.Clamp(current, 0, Mathf.Max(1, max));
        OnHealthChanged?.Invoke();

        // DEATH IS THE SERVER'S WORD TOO. It arrives as health reaching zero, which is the same shape the offline
        // game always used — so the death screen, the cue and everything listening run through their usual door.
        if (wasAlive && CurrentHP <= 0)
        {
            OnDied?.Invoke();
            var a = Avatar();
            if (a != null) VfxCue.Fire(VfxCueId.OnDeath, a);
        }

        // AND SO IS COMING BACK. Standing up arrives the same way dying did — as health — and everything that has
        // always happened on a revive has to happen here too, or the person is walking around with the buffs they
        // died holding and nothing has written their new life down. Asking the server to revive is the request;
        // THIS is the answer, and only the answer may act.
        if (!wasAlive && CurrentHP > 0) FinishReviving();
    }

    // The half of a revive that is not about health: nothing timed survives a death, and the moment is announced.
    // Shared by the revive that happens here and the one the server's own copy performs, so the two cannot drift.
    void FinishReviving()
    {
        // The mana pool refills HERE rather than beside the health, because health is the server's to grant and mana
        // is not — so the two halves of standing back up happen at different moments on a player's own machine, and
        // only this one is guaranteed to run on both.
        CurrentMP = MaxMP;
        ((ICombatUnit)this).BeginNewLife();
        OnManaChanged?.Invoke();
        OnRevived?.Invoke();
    }

    // The player's host is a static handle the controller already publishes — nothing to look up, ever. It lives on the
    // spawned avatar, not on this persistent stats object, so it is null until the body exists.
    //
    // A TOLD HOST WINS, for the same reason a told body does: "the player's status host" is one screen's answer, and
    // a server holding several people has one per person.
    public StatusController StatusHost => boundStatusHost != null ? boundStatusHost : StatusController.Player;

    /// <summary>This character's own skills, in the fixed order the passive dispatch walks them in. Handed over by
    /// whoever built them — the avatar on a screen, the fighter host on a server — so both sides answer the question
    /// the same way and neither has to search the scene for them.</summary>
    public SkillRunner[] SkillRunners { get; private set; }

    /// <summary>Say which skills are this character's. Called again whenever that list is rebuilt (a fresh avatar
    /// after a door, a newly composed skill), because a passive's place in it IS its identity to the brake.</summary>
    public void BindSkillRunners(SkillRunner[] runners) { SkillRunners = runners; }

    StatusController boundStatusHost;

    /// <summary>Tell this character which status host is theirs, instead of reading the one-per-screen handle.</summary>
    public void BindStatusHost(StatusController host) { boundStatusHost = host; }

    // ---- Damage reduction (a timed defensive stance) ---------------------------------------------
    // Guard used to be its own state and its own effect kind. It only ever moved one number — how hard an incoming hit
    // lands — so it is the DamageReduction sub-stat now, and the exposure debuff that used to be Vulnerability is the
    // same pile carrying a negative. Both intakes hand their raw post-defence figure to UnitMitigation.Landed.

    /// <summary>True while a defensive window is cutting incoming damage.</summary>
    public bool GuardActive => States.SubStatBonus(SubStat.DamageReduction) > 0f;
    /// <summary>Seconds left on the damage-reduction window (0 when none).</summary>
    public float GuardRemaining => States.Remaining(StateId.DamageReduction);

    // ---- Timed offensive self-buffs + regen (skill-driven; runtime-only, cleared on revive) -------
    // Each buff is a multiplier live until its expiry; the getters/rolls below fold in the current value (1 when
    // expired). Latest call replaces the current buff on that channel (no stacking) — matches the Haste model.

    /// <summary>The live outgoing-damage buff multiplier (1 = none) — folded into every damage roll, so it lifts
    /// basic attacks and skills alike (War Banner's damage-up aura). Reads the store like its two siblings below:
    /// the store sums EVERY damage-buff pile (they are keyed per granting skill).</summary>
    public float DamageBuffMult => ignoreStates ? 1f : States.DamageBuffMult;        // unified state store (neutral for BaseDerived)
    float AspdBuffMult          => ignoreStates ? 1f : States.AttackSpeedBuffMult;   // unified state store (neutral for BaseDerived)
    float CastBuffMult          => ignoreStates ? 1f : States.CastSpeedBuffMult;     // unified state store (neutral for BaseDerived)

    // A timed change to a sub-stat is written ONE way, through UnitStates.ApplySubStat, for a player and a mob alike.
    // The per-stat wrappers that used to sit here (damage, attack speed, cast speed, regen, leech, reflect) were a
    // second door onto the same piles with nothing but a nicer name to recommend them, and nothing called them.

    // Timed skill windows (V3: the old weapon Mana-Leech / Reflect affixes, rehomed as skill effects) — now stored in
    // the unified state store. The combat sinks read the fraction via States and see 0 once the window lapses.

    /// <summary>The live mana-leech fraction (0 when no window active) — read where the player's damage is dealt
    /// (<see cref="Health"/>), converting that fraction of damage into MP.</summary>
    public float ActiveManaLeechPct => States.ManaLeechPct;
    /// <summary>The live extra damage-reflect fraction from a skill window (0 when inactive), added on top of the
    /// permanent thorns-card <c>reflectPct</c> where incoming damage is bounced back.</summary>
    public float ActiveReflectBonus => States.ReflectBonus;

    // Absorb pool (Shield skill): a timed barrier that eats incoming HIT damage before it reaches CurrentHP. Bypassed by
    // DoT true damage, exactly like Guard (see TakeTrueDamage). Stored in the unified state store (intAmount = pool).

    /// <summary>Remaining barrier HP from an active Shield window (0 when none/expired) — for a HUD shield pip.</summary>
    public int ShieldRemaining => States.ShieldPool;

    /// <summary>Raise a damage-absorbing barrier of <paramref name="amount"/> HP for <paramref name="seconds"/> (Aegis /
    /// Barrier skills). It soaks incoming hits until drained or the window lapses. Latest call replaces (no stacking).</summary>
    public void BeginShield(int amount, float seconds) => States.BeginShield(amount, seconds);

    /// <summary>Hold parry charges for <paramref name="seconds"/> (Parry skills): the next incoming hit is negated
    /// outright and spends one charge. HOW MANY charges comes from the state store's apply context (the effect's
    /// stacks-per-apply), so "blocks the next hit" is just the default single stack — no second charge knob here.</summary>
    public void BeginParry(float seconds) => States.BeginParry(seconds);

    /// <summary>Remaining parry charges (0 when none) — for a HUD pip / the tooltip readout.</summary>
    public int ParryCharges => States.ParryCharges;

    // A parry cancels the hit BEFORE defence/element math, exactly like the off-hand block below it. The spend + the
    // "PARRY" word are the shared unit primitive (mobs get the identical read); only the OnParry dispatch is here,
    // because passives belong to the player. `source` rides along so a counter-passive can aim at who was parried.
    bool ParriedHit(MonsterAI3D source)
    {
        var av = Avatar();
        if (!UnitMitigation.TryParry(States, av != null ? UnitAnchor.Overhead(av) : transform.position)) return false;
        PassiveEvents.Fire(this, SkillTrigger.OnParry, null, source != null ? source.transform : null);
        return true;
    }

    // Drain the barrier first, then HP. Pops a pale-cyan number for the soaked part so the absorb is visible. No alloc
    // (int math + the pooled DamagePopup). The two hit intakes route through here; DoT keeps its own line (shield bypass).
    // Fraction of MaxHP at/under which the player counts as "low HP" — the OnLowHP falling-edge threshold.
    const float LowHpFrac = 0.3f;

    /// <summary>Run the last two mitigations (Shield absorb, then Brace defer) and subtract what's left from HP.
    /// RETURNS what actually landed, which is NOT what the caller passed in — a barrier or a brace can reduce it, to
    /// zero. Every caller must use the RETURN for anything downstream (the number shown, thorns, what it
    /// reports back); the figure it passed in is only the hit's size BEFORE these two, and using that is the bug this
    /// signature exists to prevent.</summary>
    int ApplyHitToHP(int dmg, MonsterAI3D source = null)
    {
        if (dmg <= 0) return 0;
        var av = Avatar();   // barrier soaks first, via the shared unit primitive (same rule mobs get)
        Vector3 popAt = av != null ? UnitAnchor.Overhead(av) : transform.position;
        dmg = UnitMitigation.AfterShield(States, dmg, popAt);
        dmg = UnitMitigation.Deferred(States, dmg, popAt);   // Brace holds the hit back; 0 here means the bill is banked
        if (dmg > 0)
        {
            int before = CurrentHP;
            SubtractHealth(dmg);
            // A hit got THROUGH — the exact mirror of OnDamageDealt, whose fire-site (Health.ApplyLeech) likewise bails
            // on damage <= 0 after the victim's own mitigation. Both sides therefore mean one damage instance that
            // really landed, and a parried / fully shielded / braced hit reaches neither. The attacker travels as the
            // trigger's entity, so a counter-attack passive aims back at whoever hit you.
            PassiveEvents.Fire(this, SkillTrigger.OnDamageReceived, null, source != null ? source.transform : null);
            // Falling-edge into low HP: this hit crossed the threshold and left the player alive → fire OnLowHP (Last
            // Stand). Computed locally (before vs after), so it re-arms after any heal with no persisted flag. DoT-only
            // crossings bypass this line and aren't covered — fine until OnLowHP becomes a queried state.
            int thr = Mathf.CeilToInt(MaxHP * LowHpFrac);
            if (before > thr && CurrentHP > 0 && CurrentHP <= thr)
                PassiveEvents.Fire(this, SkillTrigger.OnLowHP);
        }
        return dmg;
    }

    public int TakeDamage(int attackerAttack) => TakeDamage(attackerAttack, -1);

    /// <summary>Apply an incoming PHYSICAL hit: damage = max(1, attackerAttack − Defense), then scaled by the
    /// attacker's <paramref name="attackElement"/> vs our <see cref="armorElement"/>. When
    /// <paramref name="attackerHit"/> ≥ 0, the attacker's HIT is first rolled against our <see cref="Flee"/>
    /// (RO accuracy); a miss negates the hit. Pass −1 to skip the roll (always lands). <paramref name="source"/>
    /// (the attacking mob) is used for damage-reflect. Returns damage dealt.</summary>
    public int TakeDamage(int attackerAttack, int attackerHit, Element attackElement = Element.Neutral,
                          MonsterAI3D source = null, bool missed = false)
    {
        if (IsDead) return 0;
        if (DodgedByInvuln()) return 0;   // i-frames (dodge-dash): the hit passes harmlessly through

        // Accuracy was already rolled by the attacking MOB (Combat.Missed) — we only PRESENT it, never re-roll, since
        // a second random roll could disagree. A miss shows a WHITE MISS over the player (player-dealt = red, Health).
        if (missed)
        {
            var dav = Avatar();
            if (dav != null) DamagePopup.SpawnText(UnitAnchor.Overhead(dav), "MISS", MonsterMissColor);
            OnEvaded?.Invoke();
            return 0;
        }

        // A WATCHED COPY IS TOLD WHAT LANDED — it does not work it out, exactly as its health is told to it rather
        // than counted. The server has already taken the block, the parry, the armour's element and DEF off this
        // figure, so weighing them a second time here would draw the person a smaller number than the blow they
        // actually took, or swallow the blow entirely on a coin this machine had no business tossing.
        if (IsWatchedCopy) return LandOnMe(attackerAttack, source, MonsterHitColor);

        // Off-hand block: a shield can negate the hit (and clang) before any damage applies.
        var off = GetEquipped(EquipSlotType.Shield);
        if (off != null && off.blockChance > 0f && CombatRng.Chance(off.blockChance))
        {
            Sfx.Play(off.guardSfx != null && !off.guardSfx.Empty ? off.guardSfx : blockSfx);
            var av = Avatar();
            if (av != null) DamagePopup.SpawnText(UnitAnchor.Overhead(av), "Guard", GuardColor);
            OnBlocked?.Invoke();
            return 0;
        }

        // Element property: the incoming attack's element vs our armor element scales the damage (RO chart).
        float mod = Elements.Modifier(attackElement, armorElement);
        if (mod <= 0f) return 0;   // our armor fully resists this element
        if (ParriedHit(source)) return 0;   // read LAST of the negations, so a charge is only ever spent on a hit that
                                            // would really have landed (a miss / block / immunity keeps it in hand)

        // Pre-renewal DEF split: hard-DEF % then flat soft DEF. Mobs don't crit the player → crit:false.
        int dmg = UnitMitigation.Landed(States, Combat.AfterDefense(attackerAttack, HardDefPercent, Defense, false) * mod);
        // Everything below reads what LANDED, never the pre-barrier figure: a hit a Shield ate or a Brace holds costs
        // no HP, so it must also not flash, not bounce thorns, and must report 0 to the attacker
        // (whose own "did I connect" check pops the damage number and grants its XP).
        return LandOnMe(dmg, source, MonsterHitColor);
    }

    /// <summary>Where every hit on the player ends: what a Shield or a Brace leaves of it comes off HP, the DIGITS
    /// appear over the body, the hurt grunt and flash run, thorns bounce back, and a death is raised once.
    ///
    /// <para>The number is drawn HERE, on the side it happened to, for the same reason a creature's is drawn inside
    /// its own <see cref="Health"/>: whoever swung is no longer on this machine. A monster's bite is decided on the
    /// server, so a number the monster itself drew appeared on a screen nobody is sitting at.</para></summary>
    int LandOnMe(int dmg, MonsterAI3D source, Color numberColor)
    {
        dmg = ApplyHitToHP(dmg, source);
        OnHealthChanged?.Invoke();
        if (dmg > 0)
        {
            var av = Avatar();
            if (av != null) DamagePopup.Spawn(UnitAnchor.Overhead(av), dmg, numberColor);
            // player hurt grunt
            Sfx.Play(hurtSfx);
            // drives the player hurt flash
            OnDamaged?.Invoke(dmg);
            // thorns: bounce a fraction back at the attacker
            Reflect(dmg, source);
        }
        // Phase F: DeathScreen handles the death → respawn flow
        if (CurrentHP == 0) RaiseDeath();
        return dmg;
    }

    // One choke point for the player's death: the respawn flow (OnDied) + the death VFX cue. All three damage
    // entry points route here so the cue can't be forgotten at one site.
    void RaiseDeath()
    {
        // A WATCHED COPY DOES NOT GET TO DECIDE IT DIED. The server owns that fact and sends it like every other
        // fact about this character; deciding it here as well would raise a death screen on a machine whose
        // arithmetic happened to reach zero first, over a person the server still has standing.
        if (IsWatchedCopy) return;
        OnDied?.Invoke();
        var a = Avatar();
        if (a != null) VfxCue.Fire(VfxCueId.OnDeath, a);
    }

    /// <summary>Apply an incoming MAGIC hit: reduced by <see cref="MagicDefense"/> (not <see cref="Defense"/>).
    /// Magic can't be fully dodged, but our <see cref="Flee"/> makes it GLANCE (a partial reduction via
    /// <see cref="Combat.MagicGlanceMult"/>) — the Arcane→Finesse edge of the triangle. Returns the damage dealt.</summary>
    public int TakeMagicDamage(int attackerMagic, Element attackElement = Element.Neutral, MonsterAI3D source = null)
    {
        if (IsDead) return 0;
        if (DodgedByInvuln()) return 0;   // i-frames (dodge-dash) dodge magic too

        // Told, not worked out — the same rule the physical intake above explains.
        if (IsWatchedCopy) return LandOnMe(attackerMagic, source, MonsterMagicColor);

        float mod = Elements.Modifier(attackElement, armorElement);
        if (mod <= 0f) return 0;          // armour already nullified it — nothing to parry, so no charge is spent
        if (ParriedHit(source)) return 0;   // the read catches magic too: Parry is a timing skill, not a physical block

        int dmg = UnitMitigation.Landed(States, Combat.AfterDefense(attackerMagic, HardMagicDefPercent, MagicDefense, false) * mod * Combat.MagicGlanceMult(Flee));
        return LandOnMe(dmg, source, MonsterMagicColor);
    }

    /// <summary>Apply raw over-time damage (e.g. a <see cref="StatusController"/> Poison/Bleed/Burn tick) that
    /// bypasses Defense, element, and Guard. Pops a <paramref name="color"/> number over the avatar, drives the
    /// hurt flash, and triggers death once.</summary>
    public int TakeTrueDamage(int amount, Color color)
    {
        if (IsDead || amount <= 0) return 0;
        int dmg = Mathf.Max(1, amount);
        SubtractHealth(dmg);
        OnHealthChanged?.Invoke();
        var a = Avatar();
        if (a != null) DamagePopup.Spawn(UnitAnchor.Overhead(a), dmg, color);
        OnDamaged?.Invoke(dmg);
        if (CurrentHP == 0) RaiseDeath();
        return dmg;
    }

    // Damage reflect (thorns card): bounce a fraction of damage taken back at the attacking mob as true
    // damage (bypasses its defense, can't itself be reflected — TakeTrueDamage never calls back here).
    void Reflect(int dmgTaken, MonsterAI3D source)
    {
        float pct = reflectPct + ActiveReflectBonus;   // permanent thorns card + a timed skill reflect window
        if (pct <= 0f || dmgTaken <= 0 || source == null) return;
        var h = source.Health;
        if (h == null || h.IsDead) return;
        int r = Mathf.RoundToInt(dmgTaken * pct);
        if (r > 0) CombatAuthority.ApplyTrue(h, r, ReflectColor);
    }

    /// <summary>Heal from lifesteal — like <see cref="Heal"/> but SILENT (no green popup), so multi-hit leech
    /// doesn't spam numbers. Clamped to <see cref="MaxHP"/>.</summary>
    public void LeechHeal(int amount)
    {
        if (amount <= 0 || IsDead) return;
        int before = CurrentHP;
        CurrentHP = Mathf.Min(MaxHP, CurrentHP + amount);
        if (CurrentHP != before) OnHealthChanged?.Invoke();
    }

    // Neither pool is refilled here any more. StatusController owns both tickers for EVERY unit, so a mob's mana
    // refills by the same rule the player's does — a second copy here would double-pay the player and still leave
    // mobs with a pool nothing ever filled.

    /// <summary>Bring the player back to full HP after a death. The <see cref="DeathScreen"/>
    /// calls this on respawn (and teleports the avatar to a SpawnPoint separately).</summary>
    public void Revive()
    {
        // COMING BACK IS THE SERVER'S TO GRANT, for the same reason dying was its to declare. A machine that stood
        // its own character back up would be alive on its own screen and dead in the world — and would go on being
        // dead the moment the server said so again. Sent, then it happens here when the answer arrives.
        if (GameNetwork.SendReviveToServer()) return;

        CurrentHP = MaxHP;
        OnHealthChanged?.Invoke();
        FinishReviving();   // the shared half — nothing timed carries across a death, for any unit
    }

    public void Heal(int amount)
    {
        if (amount <= 0 || IsDead) return;

        // On a machine whose health belongs to the server, the gain is SENT rather than made. The green digits still
        // pop here and now, because the heal really did happen and a potion that looked like it did nothing for a
        // tenth of a second reads as a broken potion.
        if (GameNetwork.SendHealToServer(amount)) { CombatAuthority.NoteHPAdded(this, amount); return; }

        int before = CurrentHP;
        CurrentHP = Mathf.Min(MaxHP, CurrentHP + amount);
        if (CurrentHP != before) OnHealthChanged?.Invoke();
        CombatAuthority.NoteHPAdded(this, amount);
    }

    // The in-scene player avatar (re-found after scene changes destroy the old one). Without it,
    // MISS/Guard/heal popups silently vanish (Avatar() returns null, so the `if (a != null)` guards
    // skip them).
    //
    // A SERVER-HELD FIGHTER IS TOLD ITS BODY INSTEAD OF LOOKING FOR ONE. Searching the scene for "the" player
    // means "the one person at this keyboard", which is a fact about a screen and false on a server holding
    // several people: every fighter there would find the same body, or none at all. A told body wins; the
    // search is left exactly as it was for the machine that really does have one player.
    Transform Avatar()
    {
        if (boundBody != null) return boundBody;
        if (avatar == null)
        {
            var pc3 = FindAnyObjectByType<Player3DController>();
            avatar = pc3 != null ? pc3.transform : null;
        }
        return avatar;
    }

    Transform boundBody;

    /// <summary>Tell this character which body in the world is theirs, instead of letting it search. Used by the
    /// server, which holds one of these per connected player and cannot tell them apart by looking.</summary>
    public void BindBody(Transform body) { boundBody = body; }

    string boundMap;

    /// <summary>Tell this character which map its person is standing in. The server's half of <see cref="Map"/>:
    /// a person there has no body in any map of its own, so the only machine that knows is theirs, and it says so
    /// (see <see cref="NetPlayer"/>). Same shape as <see cref="BindBody"/> on purpose.</summary>
    public void BindMap(string map) { boundMap = map; }

    /// <summary>Which map this person is in. On their own machine that is simply the map they are standing in; on a
    /// server it is whatever they last reported. Never a guess: a server that read its own active scene would hand
    /// every person the same map and put every creature back to hunting across the whole world.</summary>
    public string Map => !string.IsNullOrEmpty(boundMap) ? boundMap : MapManager.CurrentMap;

    void OnDestroy()
    {
        PlayerUnits.Leave(this);
        Wallet.StopListeningTo(this);
        if (Instance == this) Instance = null;
    }

    public void AddXP(int amount)
    {
        if (amount <= 0) return;

        // The SAME kills feed both tracks: the character (flat stats) and the ring (its own level → passive
        // draft points). Ring XP only accrues while the ring is actually worn — you can't level a ring you took off.
        if (RingEquipped) Ring.AddXP(amount);

        XP += amount;
        while (XP >= XPToNext)
        {
            XP -= XPToNext;
            level++;
            StatPoints += StatusPointsForLevel(level);   // pre-renewal: ⌊L/5⌋+3 status points this level
            CurrentHP = MaxHP;                // level-up fully heals
            CurrentMP = MaxMP;                // …and refills mana
            Sfx.Play(levelUpSfx);        // triumphant sting
            OnLeveledUp?.Invoke(Level);  // drives the level-up toast
            var lav = Avatar();
            if (lav != null) VfxCue.Fire(VfxCueId.OnLevelUp, lav);
        }
        OnStatsChanged?.Invoke();
        OnHealthChanged?.Invoke();
    }

    /// <summary>Dev/testing: feed the ring XP directly (DevSettings "ring XP" action), regardless of whether
    /// it's currently worn — so the ring track can be exercised without grinding.</summary>
    public void AddRingXP(int amount) => Ring.AddXP(amount);

    // ---- What this character is carrying --------------------------------------------------------------
    //
    // THE BAG AND THE PURSE BELONG TO THE CHARACTER, for the same reason their worn gear already does: they are
    // facts about a person, not about a screen. Both used to live somewhere else and both were wrong for it.
    //
    // The bag lived on a component attached to the inventory WINDOW, which does not exist until somebody opens
    // it — so a character's belongings had no home at all until they looked at them, and the code that saved
    // them had to guess what to write when they had not. The purse was worse: one global figure for the whole
    // program, which is fine on a machine with one person at it and is a single shared wallet the moment a
    // server holds several people.
    //
    // Neither is a hot path — a bag changes when somebody picks something up.

    readonly System.Collections.Generic.List<BagEntry> bag = new System.Collections.Generic.List<BagEntry>();

    /// <summary>What this character is carrying. The live list — the bag window reads it and edits it through
    /// <see cref="InventoryStore"/>, which is the one door for changing it.</summary>
    public System.Collections.Generic.List<BagEntry> Bag => bag;

    /// <summary>How much money this character has.</summary>
    public int Gold { get; private set; }

    /// <summary>Fires when this character's money changes. Arg = the new total.</summary>
    public event Action<int> OnGoldChanged;

    /// <summary>Add money, or take it away with a negative amount. Never goes below nothing.</summary>
    public void AddGold(int amount)
    {
        if (amount == 0) return;
        SetGold(Gold + amount);
    }

    /// <summary>Set the balance outright — loading a save, or a GM setting it.</summary>
    public void SetGold(int amount)
    {
        int next = Mathf.Max(0, amount);
        if (next == Gold) return;
        Gold = next;
        OnGoldChanged?.Invoke(Gold);
    }

    // Told once after a load even when the figure did not move, because the things that display it read it
    // before the save arrives and would otherwise sit on the pre-load number forever.
    void AnnounceGold() => OnGoldChanged?.Invoke(Gold);

    // ---- Save / load ----------------------------------------------------------
    // The progression spine — level, XP, HP/MP, the six primary stats + unspent points, the Trio-Ring
    // passive walk, (v6+) equipped gear, and what the character is carrying. These methods are the only
    // place that touches the private fields, so the storage layer stays free of Character internals.

    /// <summary>Snapshot the character's progression into a serializable <see cref="SaveData"/>.</summary>
    public SaveData CaptureSave()
    {
        var d = new SaveData
        {
            version    = SaveData.CurrentVersion,
            level      = level,
            xp         = XP,
            currentHP  = CurrentHP,
            currentMP  = CurrentMP,
            statPoints = StatPoints,
            primary    = (int[])primary.Clone(),
        };

        var map = Passives.RankMap;
        d.rankStats  = new StatId[map.Count];
        d.rankValues = new int[map.Count];
        int i = 0;
        foreach (var kv in map) { d.rankStats[i] = kv.Key; d.rankValues[i] = kv.Value; i++; }
        d.bankedPoints = Passives.BankedPoints;
        d.leader       = Passives.Leader;
        d.hasLeader    = Passives.HasLeader;
        Ring.Capture(out d.ringLevel, out d.ringXp);
        d.gold         = Gold;
        d.hotbarBindings = Hotbar.Capture();
        d.activePassives = PassiveSkills.Capture();
        CaptureEquipment(d);
        CaptureBag(d);

        // The look, written into the save with everything else it changes alongside. One home, one write.
        d.modelName  = DyeModel;
        d.dyeSources = DyeSources;
        d.dyeTargets = DyeTargets;

        d.stamina = Stamina;
        return d;
    }

    // ---- the bag, written and read exactly as it was before it moved here ------------------------------
    //
    // The arrays and their meanings are UNCHANGED from when the inventory window owned this, deliberately:
    // every save already written has to keep loading, so the storage shape is not what is being fixed here —
    // only who owns the thing being stored.

    void CaptureBag(SaveData d)
    {
        var ids = new List<string>();
        var counts = new List<int>();
        var refines = new List<int>();
        var cards = new List<string>();
        var rarities = new List<int>();
        var kits = new List<string>();
        foreach (var e in bag)
        {
            if (e == null || e.def == null) continue;
            // The Ring vessel is auto-worn on load and is never a real bag item — writing a copy of it here
            // would restore one INTO the bag and equip a second, which is one ring becoming two.
            if (e.def.slot == EquipSlotType.Ring) continue;
            ids.Add(e.def.id);
            counts.Add(Mathf.Max(1, e.count));
            if (e.IsEquip && e.instance != null)
            {
                refines.Add(e.instance.upgrade);
                cards.Add(ItemCatalog.EncodeCards(e.instance.cards));
                rarities.Add((int)e.instance.rarity);
                kits.Add(e.instance.EncodeSkills());
            }
            else { refines.Add(0); cards.Add(""); rarities.Add((int)e.def.rarity); kits.Add(""); }
        }
        d.bagItemIds = ids.ToArray();
        d.bagCounts = counts.ToArray();
        d.bagRefines = refines.ToArray();
        d.bagCards = cards.ToArray();
        d.bagRarities = rarities.ToArray();
        d.bagSkills = kits.ToArray();
    }

    // Unknown item ids are dropped, so an asset removed from the game cannot stop a character loading.
    void RestoreBag(SaveData d)
    {
        bag.Clear();

        // NO BAG IN THE SAVE AT ALL means this character has never been played, so they are given the starter
        // kit. A save that carries an EMPTY bag is a different thing entirely — somebody who spent everything —
        // and must not be handed a fresh set of goods every time they log in.
        if (d.bagItemIds == null)
        {
            SeedBag();
            return;
        }

        for (int i = 0; i < d.bagItemIds.Length; i++)
        {
            var def = ItemCatalog.Resolve(d.bagItemIds[i]);
            if (def == null) continue;
            int count = (d.bagCounts != null && i < d.bagCounts.Length) ? d.bagCounts[i] : 1;
            var entry = new BagEntry { def = def, count = Mathf.Max(1, count) };
            if (def.slot != EquipSlotType.None)
            {
                int refine = (d.bagRefines != null && i < d.bagRefines.Length) ? d.bagRefines[i] : 0;
                entry.count = 1;
                entry.instance = new EquipmentInstance(def, refine);
                if (d.bagCards != null && i < d.bagCards.Length)
                    ItemCatalog.DecodeCards(d.bagCards[i], entry.instance.cards);
                // v7+: the copy's rolled rarity + kit ride the save; a pre-v7 entry keeps the def's rarity and
                // an empty kit rather than silently re-rolling into something the player never earned.
                if (d.bagRarities != null && i < d.bagRarities.Length) entry.instance.rarity = (Rarity)d.bagRarities[i];
                if (d.bagSkills != null && i < d.bagSkills.Length) entry.instance.DecodeSkills(d.bagSkills[i]);
            }
            bag.Add(entry);
        }
    }

    /// <summary>The goods a character starts life with. Public so the bag window can hand them to somebody who
    /// arrived before their save did — it is the same set either way, decided in one place.</summary>
    public void SeedBag()
    {
        bag.Clear();
        foreach (var def in Resources.LoadAll<Item>("Items"))
        {
            if (def == null) continue;
            // The Ring of Progression vessel is auto-equipped (one per player); don't also seed a duplicate
            // into the bag. It still returns to the bag normally if you take it off.
            if (def.slot == EquipSlotType.Ring) continue;
            // WEAPONS are never seeded: a weapon's identity is its ROLLED kit, so it may only enter the world
            // through a roll (a drop, or /weapon). A hand-placed starter weapon would have no kit at all.
            if (def.slot == EquipSlotType.Weapon) continue;
            if (def.slot != EquipSlotType.None)
            {
                // Showcase the +N enhancement channel: rarer gear arrives pre-refined.
                int up = def.rarity >= Rarity.Legendary ? 9 : def.rarity >= Rarity.Rare ? 7 : 0;
                bag.Add(new BagEntry { def = def, count = 1, instance = new EquipmentInstance(def, up) });
            }
            else
                bag.Add(new BagEntry { def = def, count = def.IsUsable ? 5 : 1 });
        }
    }

    // Every occupied slot EXCEPT the Ring vessel (auto-worn on load by EnsureRingEquipped, and never a
    // normal bag item). Allocates a few small lists — fine, this runs only on save, not a hot path.
    /// <summary>What this character is WEARING, in the same arrays the save uses. Public so the wire can carry
    /// it beside the bag — equipping moves an item between the two in one decision, and sending only one half
    /// leaves the item apparently nowhere. The Ring vessel is excluded, as it is from the save.</summary>
    public void CaptureWornInto(out int[] slots, out string[] ids, out int[] refines,
                                out string[] cards, out int[] rarities, out string[] kits)
    {
        var d = new SaveData();
        CaptureEquipment(d);
        slots = d.equipSlots; ids = d.equipItemIds; refines = d.equipRefines;
        cards = d.equipCards; rarities = d.equipRarities; kits = d.equipSkills;
    }

    /// <summary>Wear exactly this, replacing whatever was worn. The Ring is put back on afterwards because it
    /// is never in what arrives.</summary>
    public void RestoreWornFrom(int[] slots, string[] ids, int[] refines,
                                string[] cards, int[] rarities, string[] kits)
    {
        if (slots == null || ids == null) return;
        RestoreEquipment(new SaveData
        {
            equipSlots = slots, equipItemIds = ids, equipRefines = refines,
            equipCards = cards, equipRarities = rarities, equipSkills = kits,
        });
        EnsureRingEquipped();
    }

    void CaptureEquipment(SaveData d)
    {
        var slots = new List<int>();
        var ids = new List<string>();
        var refines = new List<int>();
        var cards = new List<string>();
        var rarities = new List<int>();
        var kits = new List<string>();
        foreach (var kv in Gear.Worn)
        {
            if (kv.Key == EquipSlotType.Ring) continue;
            var inst = kv.Value;
            slots.Add((int)kv.Key);
            ids.Add(inst.def.id);
            refines.Add(inst.upgrade);
            cards.Add(ItemCatalog.EncodeCards(inst.cards));
            rarities.Add((int)inst.rarity);
            kits.Add(inst.EncodeSkills());
        }
        d.equipSlots   = slots.ToArray();
        d.equipItemIds = ids.ToArray();
        d.equipRefines = refines.ToArray();
        d.equipCards   = cards.ToArray();
        d.equipRarities = rarities.ToArray();
        d.equipSkills   = kits.ToArray();
    }

    // Rebuild the equip dictionary from the saved arrays (Ring excluded — EnsureRingEquipped re-wears it).
    // Unknown item/card ids are dropped so a removed asset can't break a load.
    void RestoreEquipment(SaveData d)
    {
        Gear.Clear();
        if (d.equipSlots != null && d.equipItemIds != null)
        {
            for (int i = 0; i < d.equipSlots.Length && i < d.equipItemIds.Length; i++)
            {
                var slot = (EquipSlotType)d.equipSlots[i];
                if (slot == EquipSlotType.Ring) continue;
                var def = ItemCatalog.Resolve(d.equipItemIds[i]);
                if (def == null) continue;
                int refine = (d.equipRefines != null && i < d.equipRefines.Length) ? d.equipRefines[i] : 0;
                var inst = new EquipmentInstance(def, refine);
                if (d.equipCards != null && i < d.equipCards.Length)
                    ItemCatalog.DecodeCards(d.equipCards[i], inst.cards);
                // v7+: the copy's own rolled rarity + kit. A pre-v7 save has neither, so the piece keeps the def's
                // rarity and an empty kit — it re-rolls nothing, it simply has no rolled identity yet.
                if (d.equipRarities != null && i < d.equipRarities.Length) inst.rarity = (Rarity)d.equipRarities[i];
                if (d.equipSkills != null && i < d.equipSkills.Length) inst.DecodeSkills(d.equipSkills[i]);
                Gear.Equip(slot, inst);
            }
        }
    }

    /// <summary>Apply a loaded <see cref="SaveData"/>, overwriting the current runtime progression.
    /// Stats are set first so HP/MP clamp against the restored max; a save written while dead
    /// (HP 0) revives to full rather than loading you into the death screen.</summary>
    public void RestoreSave(SaveData d)
    {
        if (d == null) return;

        level      = Mathf.Max(1, d.level);
        XP         = Mathf.Max(0, d.xp);
        StatPoints = Mathf.Max(0, d.statPoints);
        if (d.primary != null)
            for (int i = 0; i < primary.Length && i < d.primary.Length; i++)
                primary[i] = Mathf.Clamp(d.primary[i], 1, MaxStatValue);

        Passives.LoadState(d.rankStats, d.rankValues, d.leader, d.hasLeader, d.bankedPoints);
        // Ring level/XP. Pre-v4 saves have no ring fields → derive the level from the points ever granted
        // (allocated + banked), since one point == one ring level, so the walk stays consistent.
        Ring.Restore(d.ringLevel > 0 ? d.ringLevel : Passives.TotalRanks() + Passives.BankedPoints, d.ringXp);
        SetGold(d.gold);
        AnnounceGold();              // even if the figure did not move — see AnnounceGold
        Hotbar.Restore(d.hotbarBindings);
        PassiveSkills.Restore(d.activePassives);
        RestoreEquipment(d);         // v6+: rebuild worn gear from the save (Ring excluded)
        RestoreBag(d);               // what they were carrying, or the starter kit on a first-ever run
        EnsureRingEquipped();        // re-wear the Ring vessel (auto-managed, never serialized)

        // v8+: the look comes back out of the save. A save written before v8 carries no body name — leave
        // whatever the character already wears rather than stripping it to nothing.
        if (!string.IsNullOrEmpty(d.modelName)) SetDye(d.dyeSources, d.dyeTargets, d.modelName);

        // HP/MP last, against the now-restored maxima. Never load into a dead state.
        CurrentHP = Mathf.Clamp(d.currentHP <= 0 ? MaxHP : d.currentHP, 1, MaxHP);
        CurrentMP = Mathf.Clamp(d.currentMP, 0, MaxMP);
        // Below zero means a save older than v8, which never recorded it — keep the full pool it starts with.
        SetStamina(d.stamina < 0f ? maxStamina : d.stamina);

        OnStatsChanged?.Invoke();
        OnHealthChanged?.Invoke();
        OnManaChanged?.Invoke();
        OnAppearanceChanged?.Invoke();   // apply the loaded outfit dye
    }
}
