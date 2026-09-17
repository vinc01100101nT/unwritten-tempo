using UnityEngine;

/// <summary>
/// A unit's LIVE stat block — its level, its six primaries, and the derived combat numbers that fall out of them
/// through <see cref="UnitStatMath"/>. This is the mob-side equivalent of what <see cref="Character"/> is for the
/// player: same inputs, same formulas, same results. A mob and a player of equal level and primaries wearing equal
/// gear have identical stats, because both roads lead to the same functions.
///
/// <para><b>Why this exists:</b> a mob's derived numbers used to be stamped onto <see cref="Health"/> and
/// <see cref="MonsterAI3D"/> once at spawn and then hand-scaled by a flat multiplier on level-up — a second growth
/// curve that drifted away from the player's. Now level-up just raises <see cref="Level"/> and RE-DERIVES; nothing
/// is ever scaled by a mob-only factor.</para>
///
/// <para>Attached by the mob builder alongside <see cref="Health"/>. Zero per-frame cost: derivation runs only when
/// something that feeds it actually changes — spawn, level-up, a respawn back to its spawn level, its spawn stats being
/// replaced, a buff or debuff on it starting or ending, and the dev settings. A mob's gear changes only when it comes
/// back for a new life, when it is re-seated as fresh rolled copies and this re-derives (<c>MobRespawn.Reactivate</c>).</para>
/// </summary>
[RequireComponent(typeof(Health))]
public class UnitStatBlock : MonoBehaviour
{
    [Tooltip("The authored inputs: level + six primaries + the base/growth curve. Set by the mob builder from MonsterDef.")]
    public UnitStatInputs inputs = UnitStatInputs.Default(1);

    Health health;
    MonsterAI3D ai;
    UnitEquipment gear;

    /// <summary>The unit's worn set (may be absent — an unarmed mob aggregates to zero bonuses).</summary>
    public UnitEquipment Gear => gear != null ? gear : (gear = GetComponent<UnitEquipment>());

    EquipBonuses Bonuses => Gear != null ? Gear.Bonuses : default;

    [Header("Ring — the percentage passive layer")]
    [Tooltip("Stats this unit's ring has invested in (parallel to Ring Rank Values). Serialized so the spawned mob " +
             "carries them; the % comes from the SAME soft-cap curve the player's ring uses.")]
    public StatId[] ringRankStats = System.Array.Empty<StatId>();
    public int[] ringRankValues = System.Array.Empty<int>();

    PassiveRanks ranks;
    /// <summary>This unit's ring ranks, built once from the authored arrays. Same type the player's ring uses, so the
    /// soft-cap curve, the per-stat percentages and any future rebalance are shared rather than mirrored.</summary>
    PassiveRanks Ranks
    {
        get
        {
            if (ranks != null) return ranks;
            ranks = new PassiveRanks();
            if (ringRankStats != null && ringRankValues != null && ringRankStats.Length > 0)
                ranks.LoadState(ringRankStats, ringRankValues, ringRankStats[0], true, 0);
            return ranks;
        }
    }

    // The percentage layer is GATED ON ACTUALLY WEARING A RING — the same rule the player plays by
    // (Character.RingEquipped). Authored ranks with no ring item in the slot do nothing, so the thing that grants the
    // power is the thing that can drop.
    float RingPercent(StatId stat) =>
        Gear != null && Gear.RingEquipped ? Ranks.Percent(stat) : 0f;

    /// <summary>The ring's percentage on one stat — what a skill in this unit's hands scales off.</summary>
    public float RingBonus(StatId stat) => RingPercent(stat);

    /// <summary>The whole percentage lift on one stat: ring investment plus any live sub-stat buff or debuff — the
    /// mob-side twin of <c>Character.Lift</c>, so a window that raises a player's evasion raises a mob's by the same
    /// rule. Without this a sub-stat effect would work on the player and quietly do nothing to a mob.</summary>
    float Lift(StatId ringNode, SubStat stat)
    {
        float sub = health != null && health.States != null ? health.States.SubStatBonus(stat) : 0f;
        return RingPercent(ringNode) + sub;
    }

    // A mob's derived numbers are STAMPED onto its components rather than read live the way the player's are, so a
    // timed sub-stat window only reaches them if something re-stamps when the window opens and again when it lapses.
    // The store already announces both, so listening to it is the whole fix — and it costs nothing at rest, because
    // a mob with no states never fires.
    void OnEnable()
    {
        if (health == null) health = GetComponent<Health>();
        if (health != null && health.States != null) health.States.OnChanged += OnStatesChanged;
    }

    void OnDisable()
    {
        if (health != null && health.States != null) health.States.OnChanged -= OnStatesChanged;
    }

    void OnStatesChanged() => Rederive();

    public int Level => inputs.level;

    // The level this unit was AUTHORED at — captured once, before any in-combat level-up. A pooled/respawned mob
    // returns to this rather than keeping the levels it earned last life; without it, farming one spot would ratchet
    // a zone permanently upward as recycled objects accumulate levels they never lose. -1 = not captured yet.
    int spawnLevel = -1;

    /// <summary>Put this unit back to the level it spawned at and re-derive — the mob half of "a respawn is a fresh
    /// creature, not the same one carrying its old career". Called from <c>MobRespawn.Reactivate</c>, the one choke
    /// point for reviving a mob, so the in-place recycle and the pooled spawner can't disagree.</summary>
    public void ResetToSpawnLevel()
    {
        if (spawnLevel > 0 && inputs.level != spawnLevel)
        {
            inputs.level = spawnLevel;
            Rederive(healToFull: true);
        }
        // A fresh creature comes back with a full pool, like its HP — on the server, which owns both.
        if (!GameNetwork.TheWorldIsElsewhere) CurrentMP = MaxMP;
    }

    /// <summary>Replace this unit's level + primaries after it has spawned and re-derive from them. Re-baselines the
    /// respawn level too, so a recycled mob comes back as what it was RE-STATTED to rather than snapping back to what
    /// its prefab was baked at.</summary>
    public void SetSpawnInputs(in UnitStatInputs newInputs)
    {
        inputs = newInputs;
        spawnLevel = Mathf.Max(1, inputs.level);
        Rederive(healToFull: true);   // refills HP and MP to the new maxima
    }

    // MP, on the same footing as the player's. Mobs don't spend it on anything yet (they have no cost-bearing
    // skills), but it EXISTS so an MP-touching effect — ManaLeech landing on a mob — has a real pool to move,
    // instead of being a knob that silently does nothing on half the units in the game.
    public int MaxMP => GameNetwork.TheWorldIsElsewhere
        ? toldMaxMP
        : UnitStatMath.MaxMP(Effective, Bonuses, Lift(StatId.MP, SubStat.MaxMP));

    // A watching machine's copy is told its pool rather than working it out (NetMob pushes both numbers).
    int toldMaxMP;

    /// <summary>The mana the server says this creature has — the twin of <c>Health.SetHealthFromServer</c>.</summary>
    public void SetManaFromServer(int current, int max)
    {
        toldMaxMP = Mathf.Max(0, max);
        CurrentMP = Mathf.Clamp(current, 0, toldMaxMP);
    }

    /// <summary>What this unit bought plus the primaries its gear adds — what every formula is fed, so a creature's
    /// worn STR raises its attack exactly as a player's does.</summary>
    UnitStatInputs Effective => inputs.WithGear(Bonuses);
    public int CurrentMP { get; private set; }

    /// <summary>Mana refilled per second with no skill running — the mob's half of the same INT rule the player uses,
    /// on top of the shared flat base every unit gets.</summary>
    public float MPRegenPerSecond =>
        UnitStatMath.BaseMpRegenPerSecond + UnitStatMath.MPRegenPerSecond(Effective, MaxMP);

    public void GainMana(int amount)
    {
        if (amount <= 0) return;
        CurrentMP = Mathf.Min(MaxMP, CurrentMP + amount);
    }

    /// <summary>What this creature's cast wind-ups are multiplied by — the player's own formula fed this creature's DEX,
    /// its ring's cast speed and any cast speed window on it.</summary>
    public float CastTimeMult
    {
        get
        {
            float buff = health != null && health.States != null ? health.States.CastSpeedBuffMult : 1f;
            return UnitStatMath.CastTimeMult(Effective, RingPercent(StatId.CastSpeed), buff);
        }
    }

    /// <summary>Spend MP if there's enough; returns false and spends nothing when short.</summary>
    public bool SpendMana(int amount)
    {
        if (amount <= 0) return true;
        if (CurrentMP < amount) return false;
        CurrentMP -= amount;
        return true;
    }

    void Awake()
    {
        health = GetComponent<Health>();
        ai = GetComponent<MonsterAI3D>();
        if (spawnLevel < 0) spawnLevel = Mathf.Max(1, inputs.level);   // remember the authored level, for respawns
        Rederive(healToFull: true);
        // Mana is worked out from the formulas, so a watching machine's copy is not given any.
        if (!GameNetwork.TheWorldIsElsewhere) CurrentMP = MaxMP;
    }

    /// <summary>Raise the level by one and re-derive everything from the shared formulas — the mob's mirror of a
    /// player levelling. No growth multiplier: the new numbers are simply what this level's inputs produce.</summary>
    public void LevelUp()
    {
        inputs.level++;
        Rederive(healToFull: true);
    }

    /// <summary>Recompute every derived stat and write it onto the components that actually read them. Call after
    /// anything that feeds the formulas changes (level, primaries, gear).</summary>
    public void Rederive(bool healToFull = false)
    {
        if (health == null) health = GetComponent<Health>();
        if (ai == null) ai = GetComponent<MonsterAI3D>();

        // A WATCHING MACHINE DERIVES NOTHING. Its copy of a creature is drawn from what the server says — health
        // included — so no number is worked out for it here. The level is authored, not derived, and is stamped as-is.
        if (GameNetwork.TheWorldIsElsewhere)
        {
            if (ai != null) ai.level = inputs.level;
            return;
        }

        var eb = Bonuses;
        var u = inputs.WithGear(eb);

        health.maxHealth    = UnitStatMath.MaxHP(u, eb, Lift(StatId.HP, SubStat.MaxHP));
        health.defense      = UnitStatMath.Defense(u, eb, Lift(StatId.Def, SubStat.Defense));
        health.magicDefense = UnitStatMath.MagicDefense(u, eb, Lift(StatId.Def, SubStat.MagicDefense));
        health.flee         = UnitStatMath.Flee(u, Lift(StatId.Evasion, SubStat.Evasion));
        health.luk          = u.luk;   // pre-renewal crit shield — the attacker reads this off the target
        health.hpRegenPerSecond = UnitStatMath.HPRegenPerSecond(u, health.maxHealth);

        if (ai != null)
        {
            ai.level        = inputs.level;
            var held        = Gear != null ? Gear.WeaponType : WeaponType.None;
            ai.attack       = UnitStatMath.PhysicalAttack(held, u, eb, Lift(StatId.Atk, SubStat.Attack));
            ai.hit          = UnitStatMath.Hit(u, Lift(StatId.Accuracy, SubStat.Accuracy));
            ai.critChance     = UnitStatMath.CritChance01(u, eb, Lift(StatId.Critical, SubStat.CritChance));
            ai.critMultiplier = UnitStatMath.CritMultiplier(u, Lift(StatId.Critical, SubStat.CritDamage));
            // Swing speed from AGI, DEX, worn attack speed, the ring and any live attack-speed buff — the player's own
            // formula, cap included, so a creature's gear and agility speed its swings exactly as a player's do.
            ai.attackSpeedMult = UnitStatMath.AttackSpeedMult(u, eb, RingPercent(StatId.AttackSpeed),
                                                              health.States != null ? health.States.AttackSpeedBuffMult : 1f);
            ai.magicAttack       = UnitStatMath.MagicAttack(u, eb, Lift(StatId.MAtk, SubStat.MagicAttack));
            ai.magicDamageSpread = 1f - UnitStatMath.MinFraction(u.intel);
            // Spread is DEX-driven for everyone: high DEX both lifts and STABILISES damage.
            ai.damageSpread = 1f - UnitStatMath.MinFraction(u.dex);
        }

        if (healToFull) { health.RefillToMax(); CurrentMP = MaxMP; }
        else CurrentMP = Mathf.Min(CurrentMP, MaxMP);   // a gear change that lowers MaxMP must not leave MP above it
    }
}
