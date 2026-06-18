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
public class Character : MonoBehaviour
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

    [Header("Growth per level")]
    public int hpPerLevel = 6;
    public int attackPerLevel = 1;
    public int defensePerLevel = 1;
    public int mpPerLevel = 3;
    public int magicAttackPerLevel = 1;
    public int magicDefensePerLevel = 1;

    [Header("Stat distribution (RO-style)")]
    [Tooltip("Points available to spend at the start (level 1).")]
    public int startingStatPoints = 9;
    [Tooltip("Stat points granted each level-up.")]
    public int statPointsPerLevel = 3;

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
    const int   HpPerVit = 8, MpPerInt = 5, AtkPerStr = 1, MatkPerInt = 1;
    const int   DefPerVit = 3, MdefPerInt = 3, AtkPerDex = 2, AtkPerLuk = 4, MatkPerLuk = 4;  // these are DIVISORS
    const float AspdPerAgi = 0.005f, CritPerLuk = 0.004f, CastPerDex = 0.006f;
    // RO HIT/FLEE: accuracy = level + DEX, evasion = level + AGI (the latter scaled by the Evasion passive).
    const int   HitPerDex = 1, FleePerAgi = 1;

    // RO-style damage spread: a hit rolls Min…Max rather than a fixed number. Min is a fraction of Max
    // that DEX (physical) / INT (magic) raise toward 1.0 — so the relevant stat both LIFTS and STABILISES
    // damage (high DEX = bigger, more consistent melee). A crit ignores the spread and always lands max.
    const float MinDmgFloorFraction = 0.55f;   // 0 DEX/INT → a swing can drop to 55% of max
    const float MinFractionPerStat  = 0.012f;  // each point closes the min→max gap (~38 → min == max)

    public int GetStat(PrimaryStat s) => primary[(int)s];
    int P(PrimaryStat s) => primary[(int)s];

    /// <summary>Spend one stat point to raise <paramref name="s"/> by 1. No-op if no points remain.</summary>
    public bool AddStat(PrimaryStat s)
    {
        if (StatPoints <= 0) return false;
        StatPoints--;
        primary[(int)s]++;
        CurrentHP = Mathf.Min(CurrentHP, MaxHP);   // clamp in case a max dropped (it won't here, but be safe)
        CurrentMP = Mathf.Min(CurrentMP, MaxMP);
        OnStatsChanged?.Invoke();
        OnHealthChanged?.Invoke();
        OnManaChanged?.Invoke();
        return true;
    }

    readonly Dictionary<EquipSlotType, Item> equipped = new Dictionary<EquipSlotType, Item>();

    /// <summary>The Phase G passive skill layer (Trio-Ring draft). Owns the player's ranks +
    /// banked skill points; its <see cref="PassiveRanks.Percent"/> is the % multiplier folded
    /// into the effective-stat getters below. Runtime-only (no save yet).</summary>
    public PassiveRanks Passives { get; } = new PassiveRanks();

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

    [Header("Audio")]
    [Tooltip("Default block sound when the equipped off-hand has none of its own.")]
    public SfxSet blockSfx;
    [Tooltip("Played on level-up.")]
    public SfxSet levelUpSfx;
    [Tooltip("Player hurt grunt — played when an unblocked hit lands.")]
    public SfxSet hurtSfx;

    static readonly Color GuardColor = new Color(0.78f, 0.85f, 1f);
    static readonly Color MonsterMissColor = Color.white;   // a monster's swing whiffed the player → white MISS

    // Effective stat = flat layer (base + level growth + PRIMARY-STAT contribution + equipment) × (1 + passive%).
    // The passive % layer is deliberately separate so a +% never goes obsolete the way a flat bonus does (doc §7).
    // The purely-derived stats further below (Crit/Evasion/AttackSpeed/CastSpeed) have no flat base.
    public int MaxHP   => Scale(baseMaxHP   + hpPerLevel      * (Level - 1) + P(PrimaryStat.VIT) * HpPerVit + Bonus(i => i.bonusHP),      StatId.HP);
    public int Attack  => Scale(baseAttack  + attackPerLevel  * (Level - 1) + P(PrimaryStat.STR) * AtkPerStr
                                + P(PrimaryStat.DEX) / AtkPerDex + P(PrimaryStat.LUK) / AtkPerLuk + Bonus(i => i.bonusAttack),  StatId.Atk);
    public int Defense => Scale(baseDefense + defensePerLevel * (Level - 1) + P(PrimaryStat.VIT) / DefPerVit + Bonus(i => i.bonusDefense), StatId.Def);

    public int MaxMP        => Scale(baseMaxMP        + mpPerLevel           * (Level - 1) + P(PrimaryStat.INT) * MpPerInt + Bonus(i => i.bonusMP),   StatId.MP);
    public int MagicAttack  => Scale(baseMagicAttack  + magicAttackPerLevel  * (Level - 1) + P(PrimaryStat.INT) * MatkPerInt
                                + P(PrimaryStat.LUK) / MatkPerLuk + Bonus(i => i.bonusMAtk), StatId.MAtk);
    public int MagicDefense => Scale(baseMagicDefense + magicDefensePerLevel * (Level - 1) + P(PrimaryStat.INT) / MdefPerInt + Bonus(i => i.bonusMDef), StatId.MDef);

    // The damage window each hit rolls within. MAX = the full Attack/MagicAttack above; MIN is a
    // fraction of it raised by DEX (physical) / INT (magic). See MinDmgFloorFraction / MinFractionPerStat.
    public int MaxAttack      => Attack;
    public int MinAttack      => Mathf.Clamp(Mathf.RoundToInt(Attack      * MinFraction(PrimaryStat.DEX)), 1, Attack);
    public int MaxMagicAttack => MagicAttack;
    public int MinMagicAttack => Mathf.Clamp(Mathf.RoundToInt(MagicAttack * MinFraction(PrimaryStat.INT)), 1, MagicAttack);
    float MinFraction(PrimaryStat s) => Mathf.Clamp01(MinDmgFloorFraction + P(s) * MinFractionPerStat);

    public int XPToNext => 5 + Level * 5;
    public bool IsDead => CurrentHP <= 0;

    // Derived passive combat stats (Phase G). These have no flat "base" — they read the
    // passive % layer directly, so a stat with 0 ranks is simply neutral.
    public const float BaseCritMultiplier = 1.5f;                                     // a crit deals 150%…
    public float CritMultiplier  => BaseCritMultiplier + Passives.Percent(StatId.CritDamage); // …+ CritDamage on top
    // These combine the primary-stat contribution (LUK/AGI/DEX) with the passive % layer.
    public float CritChance01    => Mathf.Min(0.95f, P(PrimaryStat.LUK) * CritPerLuk + Passives.Percent(StatId.CritChance));
    // RO accuracy/evasion. HIT (DEX + level) is rolled against a target's FLEE to decide a physical hit;
    // FLEE (AGI + level, scaled by the Evasion passive) is how often incoming physical attacks whiff on us.
    // Both feed the shared HIT-vs-FLEE roll in Combat; magic ignores them. See Combat for the formula.
    public int Hit  => Level + P(PrimaryStat.DEX) * HitPerDex;
    public int Flee => Mathf.RoundToInt((Level + P(PrimaryStat.AGI) * FleePerAgi) * (1f + Passives.Percent(StatId.Evasion)));
    public float AttackSpeedMult => 1f + P(PrimaryStat.AGI) * AspdPerAgi + Passives.Percent(StatId.AttackSpeed);  // >1 = swing faster
    public float CastSpeedMult   => 1f + P(PrimaryStat.DEX) * CastPerDex;             // >1 = cast faster (shortens caster cooldown)

    int Scale(int flat, StatId stat) => Mathf.RoundToInt(flat * (1f + Passives.Percent(stat)));

    /// <summary>Roll the player's outgoing hit: a random value in [<see cref="MinAttack"/>,
    /// <see cref="MaxAttack"/>], or — on a crit (probability <see cref="CritChance01"/>) — the max
    /// ×<see cref="CritMultiplier"/> (crits ignore the spread, RO-style).</summary>
    public int RollDamage(out bool crit)
    {
        crit = UnityEngine.Random.value < CritChance01;
        if (crit) return Mathf.RoundToInt(Attack * CritMultiplier);
        return UnityEngine.Random.Range(MinAttack, Attack + 1);   // Range(int,int) is max-EXCLUSIVE
    }

    /// <summary>As <see cref="RollDamage"/> but for a spell — rolls in [<see cref="MinMagicAttack"/>,
    /// <see cref="MaxMagicAttack"/>]. (Crit stats apply to magic too, so a crit build helps casters.)</summary>
    public int RollMagicDamage(out bool crit)
    {
        crit = UnityEngine.Random.value < CritChance01;
        if (crit) return Mathf.RoundToInt(MagicAttack * CritMultiplier);
        return UnityEngine.Random.Range(MinMagicAttack, MagicAttack + 1);
    }

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

    int Bonus(Func<Item, int> select)
    {
        int total = 0;
        foreach (var item in equipped.Values) if (item != null) total += select(item);
        return total;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

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
        CurrentHP = MaxHP;
        CurrentMP = MaxMP;
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
        // Stat points: a starting grant at level 1, then statPointsPerLevel for every level after.
        int spentStat = 0;
        for (int i = 0; i < primary.Length; i++) spentStat += primary[i] - 1;   // each stat starts at 1
        StatPoints = Mathf.Max(0, startingStatPoints + statPointsPerLevel * (level - 1) - spentStat);

        // Skill points: 1 free start at level 1, +1 per level after = `level` granted in total.
        Passives.SetBankedPoints(level - Passives.TotalRanks());
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
        => equipped.TryGetValue(slot, out var item) ? item : null;

    public void SetEquipped(EquipSlotType slot, Item item)
    {
        if (slot == EquipSlotType.None) return;
        equipped[slot] = item;
        CurrentHP = Mathf.Min(CurrentHP, MaxHP);   // clamp if max dropped
        OnStatsChanged?.Invoke();
        OnHealthChanged?.Invoke();
    }

    public int TakeDamage(int attackerAttack) => TakeDamage(attackerAttack, -1);

    /// <summary>Apply an incoming PHYSICAL hit: damage = max(1, attackerAttack − Defense). When
    /// <paramref name="attackerHit"/> ≥ 0, the attacker's HIT is first rolled against our <see cref="Flee"/>
    /// (RO accuracy); a miss negates the hit. Pass −1 to skip the roll (always lands). Returns damage dealt.</summary>
    public int TakeDamage(int attackerAttack, int attackerHit)
    {
        if (IsDead) return 0;

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
        var off = GetEquipped(EquipSlotType.OffHand);
        if (off != null && off.blockChance > 0f && UnityEngine.Random.value < off.blockChance)
        {
            Sfx.Play(off.guardSfx != null && !off.guardSfx.Empty ? off.guardSfx : blockSfx);
            var av = Avatar();
            if (av != null) DamagePopup.SpawnText(av.position, "Guard", GuardColor);
            OnBlocked?.Invoke();
            return 0;
        }

        int dmg = Mathf.Max(1, attackerAttack - Defense);
        CurrentHP = Mathf.Max(0, CurrentHP - dmg);
        OnHealthChanged?.Invoke();
        Sfx.Play(hurtSfx);                       // player hurt grunt
        OnDamaged?.Invoke(dmg);                  // drives the player hurt flash
        if (CurrentHP == 0) OnDied?.Invoke();   // Phase F: DeathScreen handles the death → respawn flow
        return dmg;
    }

    /// <summary>Apply an incoming MAGIC hit: reduced by <see cref="MagicDefense"/> (not
    /// <see cref="Defense"/>), and NOT avoidable by Evasion (dodge is physical only). Returns the
    /// damage dealt.</summary>
    public int TakeMagicDamage(int attackerMagic)
    {
        if (IsDead) return 0;

        int dmg = Mathf.Max(1, attackerMagic - MagicDefense);
        CurrentHP = Mathf.Max(0, CurrentHP - dmg);
        OnHealthChanged?.Invoke();
        Sfx.Play(hurtSfx);
        OnDamaged?.Invoke(dmg);
        if (CurrentHP == 0) OnDied?.Invoke();
        return dmg;
    }

    // Mana regenerates over time toward MaxMP (whole points; the fraction is carried in mpAccum).
    void Update()
    {
        if (IsDead || mpRegenPerSecond <= 0f) return;
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
        OnHealthChanged?.Invoke();
        OnManaChanged?.Invoke();
        OnRevived?.Invoke();
    }

    public void Heal(int amount)
    {
        if (amount <= 0 || IsDead) return;
        int before = CurrentHP;
        CurrentHP = Mathf.Min(MaxHP, CurrentHP + amount);
        int healed = CurrentHP - before;
        OnHealthChanged?.Invoke();
        if (healed > 0)
        {
            var a = Avatar();
            if (a != null) DamagePopup.SpawnText(a.position, "+" + healed, HealColor);  // green = heal
        }
    }

    // The in-scene player avatar (re-found after scene changes destroy the old one). Looks for BOTH the
    // 2D player and the 2.5D billboard player — without the latter, MISS/Guard/heal popups silently
    // vanished in the 3D game (Avatar() returned null, so the `if (a != null)` guards skipped them).
    Transform Avatar()
    {
        if (avatar == null)
        {
            var pc2 = FindFirstObjectByType<PlayerController2D>();
            if (pc2 != null) avatar = pc2.transform;
            else
            {
                var pc3 = FindFirstObjectByType<Player3DController>();
                avatar = pc3 != null ? pc3.transform : null;
            }
        }
        return avatar;
    }

    public void AddXP(int amount)
    {
        if (amount <= 0) return;
        XP += amount;
        while (XP >= XPToNext)
        {
            XP -= XPToNext;
            level++;
            Passives.AddPoint();              // Phase G: bank a skill point to spend in the Skills (K) panel
            StatPoints += statPointsPerLevel; // …and stat points to distribute (STR/AGI/…)
            CurrentHP = MaxHP;                // level-up fully heals
            CurrentMP = MaxMP;                // …and refills mana
            Sfx.Play(levelUpSfx);        // triumphant sting
            OnLeveledUp?.Invoke(Level);  // drives the level-up toast / draft prompt
        }
        OnStatsChanged?.Invoke();
        OnHealthChanged?.Invoke();
    }

    // ---- Save / load (M1) -----------------------------------------------------
    // The progression spine — level, XP, HP/MP, the six primary stats + unspent points, and the
    // Trio-Ring passive walk. Equipment/bag persistence is deliberately out of scope here (it lives
    // in the UI layer and lands with the inventory milestone, M3). These two methods are the only
    // place that touches the private fields, so SaveSystem stays free of Character internals.

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
        return d;
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
                primary[i] = Mathf.Max(1, d.primary[i]);

        Passives.LoadState(d.rankStats, d.rankValues, d.leader, d.hasLeader, d.bankedPoints);

        // HP/MP last, against the now-restored maxima. Never load into a dead state.
        CurrentHP = Mathf.Clamp(d.currentHP <= 0 ? MaxHP : d.currentHP, 1, MaxHP);
        CurrentMP = Mathf.Clamp(d.currentMP, 0, MaxMP);

        OnStatsChanged?.Invoke();
        OnHealthChanged?.Invoke();
        OnManaChanged?.Invoke();
    }
}
