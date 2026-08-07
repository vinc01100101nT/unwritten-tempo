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
public class Character : MonoBehaviour, IDamageable
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
    public float mpRegenPerSecond = 1.5f;

    [Header("Element & cards (RO)")]
    [Tooltip("The player's armor PROPERTY. Incoming attacks are scaled by their element vs this (Elements chart).")]
    public Element armorElement = Element.Neutral;
    [Range(0f, 1f)]
    [Tooltip("Lifesteal: fraction of damage you DEAL that silently heals you. The mechanical core of a leech card.")]
    public float lifeStealPct = 0f;
    [Range(0f, 1f)]
    [Tooltip("Damage reflect: fraction of damage you TAKE returned to the attacker. A thorns card.")]
    public float reflectPct = 0f;

    /// <summary>Lifesteal fraction (0..1), read by <see cref="Health"/> when the player deals damage.</summary>
    public float LifeStealPct => lifeStealPct;

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
    float mpAccum;   // fractional mana regen carried between frames

    // The six primary stats (indexed by PrimaryStat), each starts at 1. Runtime-only (no save yet).
    readonly int[] primary = { 1, 1, 1, 1, 1, 1 };
    public int StatPoints { get; private set; }

    // Per-point contributions of each primary stat to the flat / derived layers (tunable).
    const int   AtkPerStr = 1, MatkPerInt = 1;
    const int   DefPerVit = 3, MdefPerInt = 3, AtkPerDex = 2, AtkPerLuk = 4, MatkPerLuk = 4;  // these are DIVISORS
    const float AspdPerAgi = 0.005f, CritPerLuk = 0.003f, CastPerDex = 0.006f;   // pre-renewal CRIT = LUK×0.3%
    // Pre-renewal vitals/ASPD: VIT and INT scale HP/SP as a PERCENT per point (they multiply the level-driven
    // base, not add flat); DEX raises ASPD a fraction of what AGI does.
    const float VitHpPercent = 0.02f, IntSpPercent = 0.015f, DexAspdShare = 0.25f;
    // Hard ceiling on the attack-speed multiplier. Sword's Momentum settles at ~(attack rate × stack duration),
    // so an uncapped aspd would turn into uncapped Momentum (and Deathblow) scaling. Bounds the equilibrium.
    const float MaxAttackSpeedMult = 3f;
    const float CritShieldPerLuk = 0.003f;   // pre-renewal "crit shield": the TARGET's LUK lowers our crit chance 0.3%/pt
    // Pre-renewal RO cast time: a spell's wind-up shrinks LINEARLY with DEX and reaches ZERO (instant cast)
    // at this DEX. castTime = baseCast × (1 − DEX / InstantCastDex), clamped ≥ 0 — one stat, the classic
    // "build toward instacast" goal. (Distinct from CastSpeedMult below, which shortens the BETWEEN-casts
    // cooldown; cast time is the wind-up BEFORE the spell fires.)
    const int   InstantCastDex = 150;
    // Pre-renewal HIT/FLEE: HIT = BaseLvl + DEX + ⌊LUK/3⌋, FLEE = BaseLvl + AGI + ⌊LUK/5⌋ (FLEE then scaled
    // by the Evasion passive). LUK secondarily feeds both — the classic "a little LUK helps everything".
    const int   HitPerDex = 1, FleePerAgi = 1;
    const int   LukPerHit = 3, LukPerFlee = 5;   // DIVISORS: LUK/3 → HIT, LUK/5 → FLEE

    // RO-style damage spread: a hit rolls Min…Max rather than a fixed number. Min is a fraction of Max
    // that DEX (physical) / INT (magic) raise toward 1.0 — so the relevant stat both LIFTS and STABILISES
    // damage (high DEX = bigger, more consistent melee). A crit ignores the spread and always lands max.
    const float MinDmgFloorFraction = 0.55f;   // 0 DEX/INT → a swing can drop to 55% of max
    const float MinFractionPerStat  = 0.012f;  // each point closes the min→max gap (~38 → min == max)

    public int GetStat(PrimaryStat s) => primary[(int)s];
    // EFFECTIVE primary stat = allocated points + equipment/card bonuses. Every derived/combat
    // getter below reads P(), so gear that grants +STR now flows into ATK/HP/etc. (The raw
    // allocation economy — costs, caps, GetStat — uses primary[] directly and is unaffected.)
    int P(PrimaryStat s) => primary[(int)s] + equipBonuses.Stat(s);

    // ---- Pre-renewal stat economy -------------------------------------------------------------------
    /// <summary>Pre-renewal base-stat ceiling — a primary stat can't be raised past this with points.</summary>
    public const int MaxStatValue = 99;

    /// <summary>Points to raise a stat from its current value <paramref name="currentValue"/> to +1:
    /// <c>⌊V/10⌋ + 2</c> (1→2 costs 2, 10→11 costs 3, 20→21 costs 4 …). The pre-renewal soft cap — each
    /// block of ten costs one more, so the last points into a stat are the dearest.</summary>
    public static int StatCost(int currentValue) => currentValue / 10 + 2;

    /// <summary>Status points GAINED on reaching base level <paramref name="level"/>: <c>⌊L/5⌋ + 3</c>
    /// (3 early, growing every 5 levels to keep pace with the rising <see cref="StatCost"/>).</summary>
    static int StatusPointsForLevel(int level) => level / 5 + 3;

    /// <summary>Total status points a character who legitimately reached base level <paramref name="lvl"/>
    /// has earned — the starting grant plus every level-up award from 2..lvl.</summary>
    int TotalStatPointsEarned(int lvl)
    {
        int total = startingStatPoints;
        for (int L = 2; L <= lvl; L++) total += StatusPointsForLevel(L);
        return total;
    }

    /// <summary>Total points it cost to have raised a stat from 1 up to <paramref name="value"/> — the
    /// running integral of <see cref="StatCost"/>, used to reconcile the pool against an allocation.</summary>
    static int StatPointsSpentRaising(int value)
    {
        int total = 0;
        for (int v = 1; v < value; v++) total += StatCost(v);
        return total;
    }

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

    // Each slot holds a unique EquipmentInstance (def + refine + cards), not a shared Item — so
    // a "+7 Iron Sword with 2 cards" is its own thing. Combat callers still get the Item via
    // GetEquipped(); the instance layer adds refine/card bonuses through equipBonuses below.
    readonly Dictionary<EquipSlotType, EquipmentInstance> equipped = new Dictionary<EquipSlotType, EquipmentInstance>();

    /// <summary>Cached equipment stat contribution — recomputed ONLY when gear changes, so the hot
    /// combat getters read a struct field instead of re-summing the dictionary per hit. The hover
    /// preview temporarily swaps this for a hypothetical loadout; see <see cref="DeriveWith"/>.</summary>
    EquipBonuses equipBonuses;

    void RecomputeEquip()
    {
        equipBonuses = EquipBonuses.Aggregate(equipped.Values);
    }

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

    /// <summary>The hotbar layout — which unlocked skill is bound to each of the nine slots (keys 1–9).
    /// Set by drag/click in the Skills (K) menu; read by the bar + the central key router. Persisted.</summary>
    public HotbarBindings Hotbar { get; } = new HotbarBindings();

    /// <summary>The player's combo chains (4 skills each, one hotkey each) — the single owner of which skill is
    /// committed to which chain. Edited in the Combo Composer; read by the combo bar + <see cref="ComboRunner"/>.
    /// Persisted alongside the hotbar.</summary>
    public ComboBook Combos { get; } = new ComboBook();

    static readonly Color HealColor = new Color(0.45f, 1f, 0.45f);
    Transform avatar;   // the in-scene player, located lazily for heal popups

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
    static readonly Color ReflectColor = new Color(0.85f, 0.85f, 0.95f);   // thorns damage bounced back at a mob

    // Effective stat = flat layer (base + level growth + PRIMARY-STAT contribution + equipment) × (1 + passive%).
    // The passive % layer is deliberately separate so a +% never goes obsolete the way a flat bonus does (doc §7).
    // The purely-derived stats further below (Crit/Evasion/AttackSpeed/CastSpeed) have no flat base.
    // Pre-renewal vitals: the level-driven base curve is scaled by VIT/INT as a PERCENT multiplier (the game
    // is class-agnostic, so there is no per-class HP/SP factor — your stat investment shapes bulk vs mana).
    // Equipment HP/MP add flat on top.
    public int MaxHP   => Scale(Mathf.RoundToInt((baseMaxHP + hpPerLevel * (Level - 1))
                                * (1f + P(PrimaryStat.VIT) * VitHpPercent))
                                + equipBonuses.hp, StatId.HP);
    public int Attack  => Scale(baseAttack  + attackPerLevel  * (Level - 1) + P(PrimaryStat.STR) * AtkPerStr
                                + StatSquareBonus(P(PrimaryStat.STR))   // pre-renewal status-ATK bonus: +⌊STR/10⌋²
                                + P(PrimaryStat.DEX) / AtkPerDex + P(PrimaryStat.LUK) / AtkPerLuk + equipBonuses.atk,  StatId.Atk);
    public int Defense => Scale(baseDefense + defensePerLevel * (Level - 1) + P(PrimaryStat.VIT) / DefPerVit + equipBonuses.def, StatId.Def);

    public int MaxMP        => Scale(Mathf.RoundToInt((baseMaxMP + mpPerLevel * (Level - 1))
                                * (1f + P(PrimaryStat.INT) * IntSpPercent))
                                + equipBonuses.mp, StatId.MP);
    public int MagicAttack  => Scale(baseMagicAttack  + magicAttackPerLevel  * (Level - 1) + P(PrimaryStat.INT) * MatkPerInt
                                + StatSquareBonus(P(PrimaryStat.INT))   // pre-renewal status-MATK bonus: +⌊INT/10⌋²
                                + P(PrimaryStat.LUK) / MatkPerLuk + equipBonuses.matk, StatId.MAtk);
    // The ring has no separate M.DEF node — the Def node is dual, so MagicDefense reads the Def passive %.
    public int MagicDefense => Scale(baseMagicDefense + magicDefensePerLevel * (Level - 1) + P(PrimaryStat.INT) / MdefPerInt + equipBonuses.mdef, StatId.Def);

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
    public int MinAttack      => Mathf.Clamp(Mathf.RoundToInt(Attack      * MinFraction(PrimaryStat.DEX)), 1, Attack);
    public int MaxMagicAttack => MagicAttack;
    public int MinMagicAttack => Mathf.Clamp(Mathf.RoundToInt(MagicAttack * MinFraction(PrimaryStat.INT)), 1, MagicAttack);
    float MinFraction(PrimaryStat s) => Mathf.Clamp01(MinDmgFloorFraction + P(s) * MinFractionPerStat);

    /// <summary>Pre-renewal status-point damage bonus: a stat adds <c>+⌊stat/10⌋²</c> to its damage stat
    /// (STR→ATK, INT→MATK). Tiny early, large with heavy investment (50 → +25, 99 → +81) — the reward for
    /// dumping points into one offensive stat.</summary>
    static int StatSquareBonus(int stat) { int tens = stat / 10; return tens * tens; }

    public int XPToNext => 5 + Level * 5;
    public bool IsDead => CurrentHP <= 0;

    // IDamageable seam (network-readiness): same one apply-entry a mob's Health exposes, so a future net layer targets
    // player and mob through ONE type. Routes into the existing player sinks (mobs don't crit the player, so isCrit is
    // ignored here) — no behavior change; consumed by the Step D authority seam.
    public Vector3 DamagePosition { get { var a = Avatar(); return a != null ? a.position : transform.position; } }
    public int ApplyDamage(in DamageInfo info)
        => info.isMagic ? TakeMagicDamage(info.amount, info.element, info.source)
                        : TakeDamage(info.amount, info.attackerHit, info.element, info.source);
    public int ApplyTrueDamage(int amount, Color color) => TakeTrueDamage(amount, color);

    // Derived passive combat stats (Phase G). These have no flat "base" — they read the
    // passive % layer directly, so a stat with 0 ranks is simply neutral.
    public const float BaseCritMultiplier = 1.4f;                                     // pre-renewal: a crit deals 140%…
    // The single Critical ring node drives BOTH crit rate and crit damage — both read PassivePercent(StatId.Critical).
    public float CritMultiplier  => BaseCritMultiplier + PassivePercent(StatId.Critical);   // …+ the Critical node's % on top
    // These combine the primary-stat contribution (LUK/AGI/DEX) with the passive % layer.
    public float CritChance01    => Mathf.Min(0.95f, P(PrimaryStat.LUK) * CritPerLuk + PassivePercent(StatId.Critical) + equipBonuses.critPercent / 100f);
    // RO accuracy/evasion. HIT (DEX + level) is rolled against a target's FLEE to decide a physical hit;
    // FLEE (AGI + level, scaled by the Evasion passive) is how often incoming physical attacks whiff on us.
    // Both feed the shared HIT-vs-FLEE roll in Combat; magic ignores them. See Combat for the formula.
    public int Hit  => Scale(Level + P(PrimaryStat.DEX) * HitPerDex + P(PrimaryStat.LUK) / LukPerHit, StatId.Accuracy);
    public int Flee => Mathf.RoundToInt((Level + P(PrimaryStat.AGI) * FleePerAgi + P(PrimaryStat.LUK) / LukPerFlee)
                                        * (1f + PassivePercent(StatId.Evasion)));
    // Pre-renewal ASPD: AGI is the primary driver, DEX contributes a fraction (DexAspdShare). Kept as a
    // SPEED MULTIPLIER (the game's attack model) rather than the RO 0–200 scale. >1 = swing faster.
    public float AttackSpeedMult => Mathf.Min(MaxAttackSpeedMult,
                                    (1f + (P(PrimaryStat.AGI) + P(PrimaryStat.DEX) * DexAspdShare) * AspdPerAgi
                                       + PassivePercent(StatId.AttackSpeed) + equipBonuses.aspdPercent / 100f) * AspdBuffMult);
    public float CastSpeedMult   => (1f + P(PrimaryStat.DEX) * CastPerDex + PassivePercent(StatId.CastSpeed)) * CastBuffMult;   // >1 = cast faster; CastSpeed ring node + Blink haste lift it

    /// <summary>Pre-renewal cast-time multiplier applied to a spell's base wind-up: <c>1 − DEX/150</c>,
    /// clamped to [0,1]. 1 at 0 DEX, 0 (instant cast) at DEX 150. A spell's real cast time is
    /// <c>baseCast × CastTimeMult</c>.</summary>
    public float CastTimeMult => Mathf.Clamp01(1f - P(PrimaryStat.DEX) / (float)InstantCastDex);

    int Scale(int flat, StatId stat) => Mathf.RoundToInt(flat * (1f + PassivePercent(stat)));

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
            d.hit = Mathf.Max(0, d.hit - States.HitPenalty);                  // Blind
            d.moveSpeed = Mathf.RoundToInt(d.moveSpeed * States.MoveMult);    // Slow x Haste (NOT the cast/aftercast
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
        aspdMult = AttackSpeedMult, castSpeedMult = CastSpeedMult, castTimeMult = CastTimeMult,
        hardDefPct = HardDefPercent, hardMdefPct = HardMagicDefPercent,
        guardCutPct = GuardCutNow, lifeStealPct = LifeStealPct,
        reflectPct = ReflectPctNow, manaLeechPct = ManaLeechPctNow, barrierHP = BarrierNow,
    };

    // State-sourced readouts, neutral while BaseDerived is snapshotting — so each reads 0 on the baseline and shows its
    // real value in the live view, giving the window a clean "0 (+25)" bonus line instead of a silently-mixed number.
    float GuardCutNow     => ignoreStates ? 0f : States.GuardCut;
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
        // Aggregate the hypothetical set = live gear with `slot` replaced by `hypothetical`.
        var hypo = default(EquipBonuses);
        bool slotSeen = false;
        foreach (var kv in equipped)
        {
            var inst = kv.Key == slot ? hypothetical : kv.Value;
            if (kv.Key == slot) slotSeen = true;
            inst?.AccumulateInto(ref hypo);
        }
        if (!slotSeen) hypothetical?.AccumulateInto(ref hypo);   // equipping into an empty slot

        var saved = equipBonuses;
        try { equipBonuses = hypo; return SnapshotNow(); }
        finally { equipBonuses = saved; }
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
        crit = forceCrit || CombatRng.Chance(EffectiveCritChance(targetLuk));
        float b = DamageBuffMult;   // timed damage buff (War Banner) lifts every roll
        if (crit) return Mathf.RoundToInt(Attack * CritMultiplier * b);
        return Mathf.RoundToInt(CombatRng.Range(MinAttack, Attack + 1) * b);
    }

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
        float b = DamageBuffMult;   // timed damage buff (War Banner) still lifts magic rolls
        return Mathf.RoundToInt(CombatRng.Range(MinMagicAttack, MagicAttack + 1) * b);
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
        CurrentMP -= amount;
        OnManaChanged?.Invoke();
        return true;
    }

    /// <summary>Restore <paramref name="amount"/> MP (clamped to max). Used by weapon Mana-Leech (on damage
    /// dealt) and Refund (part of a skill's cost back on cast). No-op for ≤0.</summary>
    public void GainMana(int amount)
    {
        if (amount <= 0 || CurrentMP >= MaxMP) return;
        CurrentMP = Mathf.Min(MaxMP, CurrentMP + amount);
        OnManaChanged?.Invoke();
    }

    // ---- Ultimate charge (shared meter) -----------------------------------------------------------
    // ONE meter feeding the dedicated ult slot. Fills in combat — dealing damage (Health.ApplyLeech), taking
    // damage (the TakeDamage sinks), and casting any skill (SkillRunner.Spend). An Ultimate spends it instead of
    // riding a plain cooldown, so it's a earned MOMENT, not a rotation button.
    [Header("Ultimate charge")]
    [Tooltip("Charge-accumulation ceiling used ONLY while no ultimate is equipped. When an ult IS equipped the meter caps at THAT ult's chargeCost (see UltMeterCap), so any cost — 300, 1000, a million — always fills to READY.")]
    public float ultChargeMax = 200f;
    /// <summary>Charge added per point of damage DEALT / TAKEN, and a flat amount per non-ult cast — the combat fill rate.</summary>
    public const float UltChargePerDamageDealt = 0.2f, UltChargePerDamageTaken = 0.3f, UltChargePerCast = 6f;

    /// <summary>Current ult charge (0..<see cref="UltMeterCap"/>).</summary>
    public float UltCharge { get; private set; }

    /// <summary>Charge needed to FIRE the equipped ultimate (0 = none equipped). THE single source of truth for
    /// "full": the meter cap and every HUD fill derive from it, so there's no fixed max to keep in sync — raise
    /// an ult's chargeCost to any value and the meter still reaches it.</summary>
    public float EquippedUltCost => PlayerSkills.ById(Hotbar.Ult) is SkillRunner sr && sr.def != null ? sr.def.chargeCost : 0f;

    /// <summary>The live meter cap: the equipped ult's cost, or (nothing equipped) the fallback accumulation ceiling.</summary>
    float UltMeterCap { get { float c = EquippedUltCost; return c > 0f ? c : ultChargeMax; } }

    /// <summary>Ult charge as a 0..1 fill for the HUD (progress toward firing the equipped ult).</summary>
    public float UltCharge01 { get { float c = UltMeterCap; return c > 0f ? Mathf.Clamp01(UltCharge / c) : 0f; } }
    /// <summary>True when the meter holds at least <paramref name="cost"/>.</summary>
    public bool UltReady(float cost) => UltCharge >= cost;
    /// <summary>Fired whenever the meter changes — drives the ult slot's fill.</summary>
    public event Action OnUltChargeChanged;

    /// <summary>Add to the ult meter (clamped to max). No-op while dead. Called by the combat fill sources.</summary>
    public void AddUltCharge(float amount)
    {
        if (amount <= 0f || IsDead) return;
        float before = UltCharge;
        UltCharge = Mathf.Min(UltMeterCap, UltCharge + amount);
        if (UltCharge != before) OnUltChargeChanged?.Invoke();
    }

    /// <summary>Spend <paramref name="cost"/> from the meter; false (and spends nothing) if not charged.</summary>
    public bool SpendUltCharge(float cost)
    {
        if (UltCharge < cost) return false;
        UltCharge -= Mathf.Max(0f, cost);
        OnUltChargeChanged?.Invoke();
        return true;
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
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
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
        => equipped.TryGetValue(slot, out var inst) && inst != null ? inst.def : null;

    /// <summary>The full instance (def + refine + cards) in a slot, or null if empty.</summary>
    public EquipmentInstance GetEquippedInstance(EquipSlotType slot)
        => equipped.TryGetValue(slot, out var inst) ? inst : null;

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
        if (instance == null || instance.def == null) equipped.Remove(slot);
        else equipped[slot] = instance;
        RecomputeEquip();
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
            var kit = (instance != null && instance.def != null) ? instance.def.grantedSkillIds : null;
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
        if (a != null) DamagePopup.SpawnText(a.position, "Dodge", DodgeColor);
        OnEvaded?.Invoke();
        return true;
    }

    // The unified per-unit state store (buffs/debuffs) — single source for gameplay queries AND the buff-tray icons.
    // Lazily created on the player's GameObject; cached after first touch (no per-hit GetComponent).
    UnitStates states;
    public UnitStates States => states != null ? states : (states = GetComponent<UnitStates>() ?? gameObject.AddComponent<UnitStates>());

    // ---- Guard (a timed defensive stance; driven by a guard-type active) --------------------------
    // A timed damage-reduction stance: while active, incoming physical AND magic damage is cut by the Guard state's
    // magnitude. Stored in the unified state store (V3 unified-state migration); runtime-only, cleared on revive.

    /// <summary>True while the Guard stance is mitigating incoming damage.</summary>
    public bool GuardActive => States.GuardCut > 0f;
    /// <summary>Seconds left on the Guard stance (0 when inactive).</summary>
    public float GuardRemaining => States.Remaining(StateId.Guard);

    /// <summary>Begin a damage-reduction stance for <paramref name="seconds"/>, preventing
    /// <paramref name="reduction"/> (0..1) of each incoming hit. Called by a guard-type active skill.</summary>
    public void BeginGuard(float seconds, float reduction)
    {
        if (seconds <= 0f) return;
        States.Apply(StateId.Guard, seconds, Mathf.Clamp01(reduction));
    }

    // Apply the Guard reduction to a post-defense damage figure (min 1 so a hit still registers).
    int Guarded(int dmg) { float cut = States.GuardCut; return cut > 0f ? Mathf.Max(1, Mathf.RoundToInt(dmg * (1f - cut))) : dmg; }

    // ---- Timed offensive self-buffs + regen (skill-driven; runtime-only, cleared on revive) -------
    // Each buff is a multiplier live until its expiry; the getters/rolls below fold in the current value (1 when
    // expired). Latest call replaces the current buff on that channel (no stacking) — matches the Haste model.
    // War Banner RAMP (experimental): the DamageBuff can GROW over its life (ramp params live on the DamageBuff state
    // entry). Refreshing a still-live buff KEEPS the ramp progress; letting it fully lapse RESETS to base — that parity
    // comes free from UnitStates.Apply (keeps startTime on a refresh, resets it on a lapsed re-cast). Stored in the store.
    const float RegenInterval = 0.8f;

    /// <summary>The live outgoing-damage buff multiplier (1 = none) — folded into every damage roll, so it lifts
    /// basic attacks and skills alike (War Banner's damage-up aura).</summary>
    public float DamageBuffMult
    {
        get
        {
            if (ignoreStates) return 1f;   // BaseDerived: the pre-buff baseline, through this same formula
            var s = States.Get(StateId.DamageBuff);
            if (s == null) return 1f;
            float m = s.Mult;   // State.Mult owns the stacking encoding (per-stack bonuses vs an outright multiplier)
            if (s.rampPerStep > 0f)   // ramping buff: base + perStep for each interval elapsed past the delay
            {
                float el = Time.time - s.startTime - s.rampDelay;
                if (el > 0f)
                {
                    m += s.rampPerStep * Mathf.FloorToInt(el / s.rampInterval);
                    if (s.rampCap > 0f) m = Mathf.Min(m, s.rampCap);
                }
            }
            return m;
        }
    }
    float AspdBuffMult          => ignoreStates ? 1f : States.AttackSpeedBuffMult;   // unified state store (neutral for BaseDerived)
    float CastBuffMult          => ignoreStates ? 1f : States.CastSpeedBuffMult;     // unified state store (neutral for BaseDerived)

    /// <summary>Timed outgoing-damage buff ×<paramref name="mult"/> for <paramref name="seconds"/>. Optional RAMP
    /// (<paramref name="rampPerStep"/> &gt; 0) grows the multiplier by that much every <paramref name="rampInterval"/>s
    /// after <paramref name="rampDelay"/>s, capped at <paramref name="rampCap"/> (0 = uncapped). Refreshing a live buff
    /// keeps the ramp's progress; a lapsed one restarts from base (the "keep it up to reach the ceiling" support loop).</summary>
    public void BuffDamage(float mult, float seconds, float rampDelay = 0f, float rampPerStep = 0f,
                           float rampInterval = 5f, float rampCap = 0f)
    {
        if (seconds <= 0f) return;
        // Apply keeps startTime on a refresh of a live buff (ramp progress preserved) and resets it on a lapsed re-cast.
        var s = States.Apply(StateId.DamageBuff, seconds, Mathf.Max(0.01f, mult));
        if (s == null) return;
        s.rampDelay = Mathf.Max(0f, rampDelay);
        s.rampPerStep = Mathf.Max(0f, rampPerStep);
        s.rampInterval = Mathf.Max(0.1f, rampInterval);
        s.rampCap = rampCap;
    }
    /// <summary>Timed attack-speed buff ×<paramref name="mult"/> for <paramref name="seconds"/> (Blink Step haste).</summary>
    public void BuffAttackSpeed(float mult, float seconds) { if (seconds <= 0f) return; States.Apply(StateId.AttackSpeedBuff, seconds, Mathf.Max(0.01f, mult)); }
    /// <summary>Timed cast-speed buff ×<paramref name="mult"/> for <paramref name="seconds"/> (Blink Step haste).</summary>
    public void BuffCastSpeed(float mult, float seconds)   { if (seconds <= 0f) return; States.Apply(StateId.CastSpeedBuff, seconds, Mathf.Max(0.01f, mult)); }

    /// <summary>Start (or refresh) a heal-over-time: <paramref name="perTick"/> HP every <see cref="RegenInterval"/>s
    /// for <paramref name="seconds"/> (Mending Tide). Ticked in <see cref="Update"/>.</summary>
    public void BeginRegen(int perTick, float seconds)
    {
        if (perTick <= 0 || seconds <= 0f) return;
        var s = States.Apply(StateId.Regen, seconds, 0f, perTick);   // intAmount = HP per tick
        if (s != null) s.nextTick = Time.time + RegenInterval;
    }

    // Timed skill windows (V3: the old weapon Mana-Leech / Reflect affixes, rehomed as skill effects) — now stored in
    // the unified state store. The combat sinks read the fraction via States and see 0 once the window lapses.

    /// <summary>The live mana-leech fraction (0 when no window active) — read where the player's damage is dealt
    /// (<see cref="Health"/>), converting that fraction of damage into MP.</summary>
    public float ActiveManaLeechPct => States.ManaLeechPct;
    /// <summary>The live extra damage-reflect fraction from a skill window (0 when inactive), added on top of the
    /// permanent thorns-card <c>reflectPct</c> where incoming damage is bounced back.</summary>
    public float ActiveReflectBonus => States.ReflectBonus;

    /// <summary>Open a mana-leech window: for <paramref name="seconds"/>, <paramref name="fraction"/> (0..1) of the
    /// damage you deal returns as MP.</summary>
    public void BeginManaLeech(float seconds, float fraction)
    {
        if (seconds <= 0f || fraction <= 0f) return;
        States.Apply(StateId.ManaLeech, seconds, Mathf.Clamp01(fraction));
    }

    /// <summary>Open a damage-reflect window: for <paramref name="seconds"/>, reflect an extra <paramref name="fraction"/>
    /// (0..1) of the damage you take back at the attacker (adds to the thorns card's permanent reflect).</summary>
    public void BeginReflect(float seconds, float fraction)
    {
        if (seconds <= 0f || fraction <= 0f) return;
        States.Apply(StateId.Reflect, seconds, Mathf.Clamp01(fraction));
    }

    // Absorb pool (Shield skill): a timed barrier that eats incoming HIT damage before it reaches CurrentHP. Bypassed by
    // DoT true damage, exactly like Guard (see TakeTrueDamage). Stored in the unified state store (intAmount = pool).

    /// <summary>Remaining barrier HP from an active Shield window (0 when none/expired) — for a HUD shield pip.</summary>
    public int ShieldRemaining => States.ShieldPool;

    /// <summary>Raise a damage-absorbing barrier of <paramref name="amount"/> HP for <paramref name="seconds"/> (Aegis /
    /// Barrier skills). It soaks incoming hits until drained or the window lapses. Latest call replaces (no stacking).</summary>
    public void BeginShield(int amount, float seconds)
    {
        if (amount <= 0 || seconds <= 0f) return;
        States.Apply(StateId.Shield, seconds, 0f, amount);   // intAmount = barrier HP pool
    }

    // Drain the barrier first, then HP. Pops a pale-cyan number for the soaked part so the absorb is visible. No alloc
    // (int math + the pooled DamagePopup). The two hit intakes route through here; DoT keeps its own line (shield bypass).
    // Fraction of MaxHP at/under which the player counts as "low HP" — the OnLowHP falling-edge threshold.
    const float LowHpFrac = 0.3f;

    void ApplyHitToHP(int dmg)
    {
        if (dmg <= 0) return;
        int soaked = States.DrainShield(dmg);   // barrier soaks first (drops + clears its icon when it breaks)
        if (soaked > 0)
        {
            dmg -= soaked;
            var a = Avatar();
            if (a != null) DamagePopup.Spawn(a.position, soaked, ShieldAbsorbColor);
        }
        if (dmg > 0)
        {
            int before = CurrentHP;
            CurrentHP = Mathf.Max(0, CurrentHP - dmg);
            // Falling-edge into low HP: this hit crossed the threshold and left the player alive → fire OnLowHP (Last
            // Stand). Computed locally (before vs after), so it re-arms after any heal with no persisted flag. DoT-only
            // crossings bypass this line and aren't covered — fine until OnLowHP becomes a queried state.
            int thr = Mathf.CeilToInt(MaxHP * LowHpFrac);
            if (before > thr && CurrentHP > 0 && CurrentHP <= thr)
                PassiveEvents.Fire(SkillTrigger.OnLowHP);
        }
    }

    public int TakeDamage(int attackerAttack) => TakeDamage(attackerAttack, -1);

    /// <summary>Apply an incoming PHYSICAL hit: damage = max(1, attackerAttack − Defense), then scaled by the
    /// attacker's <paramref name="attackElement"/> vs our <see cref="armorElement"/>. When
    /// <paramref name="attackerHit"/> ≥ 0, the attacker's HIT is first rolled against our <see cref="Flee"/>
    /// (RO accuracy); a miss negates the hit. Pass −1 to skip the roll (always lands). <paramref name="source"/>
    /// (the attacking mob) is used for damage-reflect. Returns damage dealt.</summary>
    public int TakeDamage(int attackerAttack, int attackerHit, Element attackElement = Element.Neutral, MonsterAI3D source = null)
    {
        if (IsDead) return 0;
        if (DodgedByInvuln()) return 0;   // i-frames (dodge-dash): the hit passes harmlessly through

        // FLEE: roll the attacker's HIT against our FLEE. A miss negates the hit — the monster's attack
        // "missed", so show a WHITE MISS over the player (player-dealt misses are red, see Health).
        if (attackerHit >= 0 && !Combat.Lands(attackerHit, Flee))
        {
            var dav = Avatar();
            if (dav != null) DamagePopup.SpawnText(dav.position, "MISS", MonsterMissColor);
            OnEvaded?.Invoke();
            return 0;
        }

        // Off-hand block: a shield can negate the hit (and clang) before any damage applies.
        var off = GetEquipped(EquipSlotType.Shield);
        if (off != null && off.blockChance > 0f && CombatRng.Chance(off.blockChance))
        {
            Sfx.Play(off.guardSfx != null && !off.guardSfx.Empty ? off.guardSfx : blockSfx);
            var av = Avatar();
            if (av != null) DamagePopup.SpawnText(av.position, "Guard", GuardColor);
            OnBlocked?.Invoke();
            return 0;
        }

        // Element property: the incoming attack's element vs our armor element scales the damage (RO chart).
        float mod = Elements.Modifier(attackElement, armorElement);
        if (mod <= 0f) return 0;   // our armor fully resists this element

        // Pre-renewal DEF split: hard-DEF % then flat soft DEF. Mobs don't crit the player → crit:false.
        int dmg = Guarded(Mathf.Max(1, Mathf.RoundToInt(Combat.AfterDefense(attackerAttack, HardDefPercent, Defense, false) * mod)));
        ApplyHitToHP(dmg);
        OnHealthChanged?.Invoke();
        Sfx.Play(hurtSfx);                       // player hurt grunt
        OnDamaged?.Invoke(dmg);                  // drives the player hurt flash
        Reflect(dmg, source);                    // thorns: bounce a fraction back at the attacker
        AddUltCharge(dmg * UltChargePerDamageTaken);   // taking hits builds ult charge
        if (CurrentHP == 0) RaiseDeath();   // Phase F: DeathScreen handles the death → respawn flow
        return dmg;
    }

    // One choke point for the player's death: the respawn flow (OnDied) + the death VFX cue. All three damage
    // entry points route here so the cue can't be forgotten at one site.
    void RaiseDeath()
    {
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

        float mod = Elements.Modifier(attackElement, armorElement);
        if (mod <= 0f) return 0;

        int dmg = Guarded(Mathf.Max(1, Mathf.RoundToInt(Combat.AfterDefense(attackerMagic, HardMagicDefPercent, MagicDefense, false) * mod * Combat.MagicGlanceMult(Flee))));
        ApplyHitToHP(dmg);
        OnHealthChanged?.Invoke();
        Sfx.Play(hurtSfx);
        OnDamaged?.Invoke(dmg);
        Reflect(dmg, source);
        AddUltCharge(dmg * UltChargePerDamageTaken);   // taking hits builds ult charge
        if (CurrentHP == 0) RaiseDeath();
        return dmg;
    }

    /// <summary>Apply raw over-time damage (e.g. a <see cref="StatusController"/> Poison/Bleed/Burn tick) that
    /// bypasses Defense, element, and Guard. Pops a <paramref name="color"/> number over the avatar, drives the
    /// hurt flash, and triggers death once.</summary>
    public int TakeTrueDamage(int amount, Color color)
    {
        if (IsDead || amount <= 0) return 0;
        int dmg = Mathf.Max(1, amount);
        CurrentHP = Mathf.Max(0, CurrentHP - dmg);
        OnHealthChanged?.Invoke();
        var a = Avatar();
        if (a != null) DamagePopup.Spawn(a.position, dmg, color);
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

    // Mana regenerates over time toward MaxMP (whole points; the fraction is carried in mpAccum).
    void Update()
    {
        if (IsDead) return;

        // Skill heal-over-time (Mending Tide): tick fixed HP on its own cadence while active. Reads the Regen state
        // entry (null once it lapses → no tick). No alloc.
        var rg = States.Get(StateId.Regen);
        if (rg != null && Time.time >= rg.nextTick)
        {
            rg.nextTick = Time.time + RegenInterval;
            Heal(rg.intAmount);
        }

        if (mpRegenPerSecond <= 0f) return;
        if (CurrentMP >= MaxMP) { mpAccum = 0f; return; }
        mpAccum += mpRegenPerSecond * Time.deltaTime;
        if (mpAccum < 1f) return;
        int add = Mathf.FloorToInt(mpAccum);
        mpAccum -= add;
        CurrentMP = Mathf.Min(MaxMP, CurrentMP + add);
        OnManaChanged?.Invoke();
    }

    /// <summary>Bring the player back to full HP after a death. The <see cref="DeathScreen"/>
    /// calls this on respawn (and teleports the avatar to a SpawnPoint separately).</summary>
    public void Revive()
    {
        CurrentHP = MaxHP;
        CurrentMP = MaxMP;
        States.Clear();                           // buffs/debuffs/barriers don't carry across death — the tray reads States, so this clears it too
        OnHealthChanged?.Invoke();
        OnManaChanged?.Invoke();
        OnRevived?.Invoke();
    }

    public void Heal(int amount)
    {
        if (amount <= 0 || IsDead) return;
        int before = CurrentHP;
        CurrentHP = Mathf.Min(MaxHP, CurrentHP + amount);
        if (CurrentHP != before) OnHealthChanged?.Invoke();
        // Show the green digits for the FULL intended amount, even at full HP (healed==0) — the heal still "landed".
        var a = Avatar();
        if (a != null)
        {
            DamagePopup.SpawnText(a.position, "+" + amount, HealColor);  // green = heal
            VfxCue.Fire(VfxCueId.OnReceiveHeal, a);
        }
    }

    // The in-scene player avatar (re-found after scene changes destroy the old one). Without it,
    // MISS/Guard/heal popups silently vanish (Avatar() returns null, so the `if (a != null)` guards
    // skip them).
    Transform Avatar()
    {
        if (avatar == null)
        {
            var pc3 = FindFirstObjectByType<Player3DController>();
            avatar = pc3 != null ? pc3.transform : null;
        }
        return avatar;
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

    // ---- Save / load ----------------------------------------------------------
    // The progression spine — level, XP, HP/MP, the six primary stats + unspent points, the Trio-Ring
    // passive walk, and (v6+) equipped gear. The BAG is owned by InventoryStore, not here; SaveManager
    // stitches it into the same SaveData. These methods are the only place that touches the private
    // fields, so SaveSystem stays free of Character internals.

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
        d.gold         = Wallet.Capture();
        d.hotbarBindings = Hotbar.Capture();
        d.comboChains = Combos.Capture();
        CaptureEquipment(d);
        d.outfitDyeSources = DyeSources;
        d.outfitDyeTargets = DyeTargets;
        d.outfitDyeModel   = DyeModel;
        return d;
    }

    // Every occupied slot EXCEPT the Ring vessel (auto-worn on load by EnsureRingEquipped, and never a
    // normal bag item). Allocates a few small lists — fine, this runs only on save, not a hot path.
    void CaptureEquipment(SaveData d)
    {
        var slots = new List<int>();
        var ids = new List<string>();
        var refines = new List<int>();
        var cards = new List<string>();
        foreach (var kv in equipped)
        {
            if (kv.Key == EquipSlotType.Ring) continue;
            var inst = kv.Value;
            if (inst == null || inst.def == null) continue;
            slots.Add((int)kv.Key);
            ids.Add(inst.def.id);
            refines.Add(inst.upgrade);
            cards.Add(ItemCatalog.EncodeCards(inst.cards));
        }
        d.equipSlots   = slots.ToArray();
        d.equipItemIds = ids.ToArray();
        d.equipRefines = refines.ToArray();
        d.equipCards   = cards.ToArray();
    }

    // Rebuild the equip dictionary from the saved arrays (Ring excluded — EnsureRingEquipped re-wears it).
    // Unknown item/card ids are dropped so a removed asset can't break a load.
    void RestoreEquipment(SaveData d)
    {
        equipped.Clear();
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
                equipped[slot] = inst;
            }
        }
        RecomputeEquip();
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
        Wallet.Restore(d.gold);
        Hotbar.Restore(d.hotbarBindings);
        Combos.Restore(d.comboChains);
        RestoreEquipment(d);         // v6+: rebuild worn gear from the save (Ring excluded)
        EnsureRingEquipped();        // re-wear the Ring vessel (auto-managed, never serialized)
        DyeSources = d.outfitDyeSources;   // v6+: outfit dye (CharacterDye3D re-bakes off OnAppearanceChanged below)
        DyeTargets = d.outfitDyeTargets;
        DyeModel   = d.outfitDyeModel;     // CharacterDye3D drops the dye if this doesn't match the worn model

        // HP/MP last, against the now-restored maxima. Never load into a dead state.
        CurrentHP = Mathf.Clamp(d.currentHP <= 0 ? MaxHP : d.currentHP, 1, MaxHP);
        CurrentMP = Mathf.Clamp(d.currentMP, 0, MaxMP);

        OnStatsChanged?.Invoke();
        OnHealthChanged?.Invoke();
        OnManaChanged?.Invoke();
        OnAppearanceChanged?.Invoke();   // apply the loaded outfit dye
    }
}
