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

    /// <summary>Every point of experience this character has ever been given, the part already spent on levels
    /// included. <see cref="XP"/> only holds what is standing inside the CURRENT level and is emptied on each
    /// level-up, so it can never say who has earned the most — this can. Saved (v10+) rather than worked out
    /// from the level, so a change to the curve never rewrites what somebody actually earned.</summary>
    public long LifetimeXp { get; private set; }
    public int CurrentHP { get; private set; }
    public int CurrentMP { get; private set; }

    // The six primary stats (indexed by PrimaryStat), each starts at 1. Runtime-only (no save yet).
    readonly int[] primary = { 1, 1, 1, 1, 1, 1 };
    public int StatPoints { get; private set; }

    // The per-point contributions, caps and curve constants all live in UnitStatMath — the ONE copy players and
    // mobs share. Aliased here only where this class's own logic (crit shield, stat economy) reads them.
    const float CritShieldPerLuk = UnitStatMath.CritShieldPerLuk;

    public int GetStat(PrimaryStat s) => primary[(int)s];
    /// <summary>What the formulas use for this stat: the points bought plus what gear and cards add.</summary>
    // ---- the character sheet, as the SERVER worked it out ------------------------------------------------------
    // A player's machine works out none of the numbers a character's stats produce — not attack, not defence, not a
    // point's cost, not the next jump. The server derives them from the character it holds and sends the finished
    // sheet (NetPlayer.PushMySheet); on that machine every getter below answers from it.
    //
    // THE ONE PLACE THAT STILL DERIVES HERE is the equip-hover preview (CurrentDerived / DeriveWith), which asks "what
    // if I wore this" of a loadout the server has never seen. It sets computeHere for exactly as long as it snapshots.
    bool computeHere;
    bool Told => !computeHere && (IsWatchedCopy || GameNetwork.TheWorldIsElsewhere);

    // Until the server's first sheet arrives, every told value below is still its placeholder zero.
    bool sheetArrived;

    /// <summary>False only while this character is waiting for the server's first sheet — a screen draws no numbers
    /// from it until then, or it shows "0-0" and "next +0 at 0" for the half second before the sheet lands.</summary>
    public bool SheetReady => !Told || sheetArrived;

    DerivedSnapshot toldCurrent = DerivedSnapshot.Neutral;
    DerivedSnapshot toldBaseline = DerivedSnapshot.Neutral;
    DerivedSnapshot toldWithStates = DerivedSnapshot.Neutral;
    int toldMaxHP = 1, toldMaxMP = 1;
    // -1 = "cannot raise", until the server's first sheet says otherwise — a 0 would read as a free point at the cap.
    readonly int[] toldRaiseCost = { -1, -1, -1, -1, -1, -1 };
    readonly int[] toldJumpAt = new int[6];
    readonly int[] toldJumpGain = new int[6];
    readonly int[] toldGearStat = new int[6];

    /// <summary>The sheet the server derived for this character — the three views, and per stat its next point's cost
    /// (-1 at the cap), its next jump and what worn gear adds to it. Arrays are indexed by <see cref="PrimaryStat"/>.</summary>
    public void SetSheetFromServer(in DerivedSnapshot current, in DerivedSnapshot baseline, in DerivedSnapshot withStates,
                                   int[] raiseCost, int[] jumpAt, int[] jumpGain, int[] gearStat)
    {
        sheetArrived = true;
        toldCurrent = current;
        toldBaseline = baseline;
        toldWithStates = withStates;
        for (int i = 0; i < 6; i++)
        {
            toldRaiseCost[i] = raiseCost != null && i < raiseCost.Length ? raiseCost[i] : -1;
            toldJumpAt[i] = jumpAt != null && i < jumpAt.Length ? jumpAt[i] : 0;
            toldJumpGain[i] = jumpGain != null && i < jumpGain.Length ? jumpGain[i] : 0;
            toldGearStat[i] = gearStat != null && i < gearStat.Length ? gearStat[i] : 0;
        }
    }

    /// <summary>The value at which <paramref name="s"/> next jumps, counting gear.</summary>
    public int NextJumpAt(PrimaryStat s) => Told ? toldJumpAt[(int)s] : UnitStatMath.NextJumpAt(P(s));

    /// <summary>How much <paramref name="s"/>'s next jump gives.</summary>
    public int JumpGainAt(PrimaryStat s) => Told ? toldJumpGain[(int)s] : UnitStatMath.JumpGainAt(NextJumpAt(s));

    public int EffectiveStat(PrimaryStat s) => P(s);

    /// <summary>What worn gear adds to <paramref name="s"/> — told by the server on a player's machine, which adds nothing up.</summary>
    public int GearStat(PrimaryStat s) => Told ? toldGearStat[(int)s] : equipBonuses.Stat(s);
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
    public int CostToRaise(PrimaryStat s) =>
        Told ? toldRaiseCost[(int)s] : primary[(int)s] >= MaxStatValue ? -1 : StatCost(primary[(int)s]);

    /// <summary>True when <paramref name="s"/> is below the cap AND the next point is affordable.</summary>
    public bool CanRaise(PrimaryStat s)
    {
        int cost = CostToRaise(s);
        return cost >= 0 && StatPoints >= cost;
    }

    /// <summary>Grant unspent status points (dev/testing — the <c>/statpts</c> command).</summary>
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
    /// economy onto the new cost curve — or just to re-roll a build.</summary>
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
        // A point is real power, spent on the server (NetPlayer.AskToSpendStatPoint); a player's machine spends none.
        if (Told) return false;
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
    WeaponType? previewWeaponType;
    Quality? previewQuality;

    /// <summary>Equipment's stat contribution. Cached inside <see cref="UnitEquipment"/> (recomputed only when gear
    /// changes), so the hot combat getters don't re-sum the worn set per hit.</summary>
    EquipBonuses equipBonuses => previewBonuses ?? Gear.Bonuses;

    /// <summary>The Ring of Progression — PART OF THE CHARACTER, never an item: its own level/XP plus the
    /// Trio-Ring passive draft (<see cref="RingProgress.Passives"/>). Levels in parallel with the character
    /// (the same kills feed both — see <see cref="AddXP"/>), and what it has earned always counts.</summary>
    public RingProgress Ring { get; } = new RingProgress();

    /// <summary>The Phase G passive skill layer (Trio-Ring draft). Its <see cref="PassiveRanks.Percent"/> is the
    /// % multiplier folded into the effective-stat getters below (see <see cref="PassivePercent"/>).</summary>
    public PassiveRanks Passives => Ring.Passives;

    /// <summary>WHAT THIS CHARACTER HAS PRACTISED — a mastery per weapon type and per skill, and the skills those
    /// have opened. Part of the character, like the ring, and permanent; the weapon in hand only caps what may be
    /// used and earned.</summary>
    public MasteryProgress Mastery { get; } = new MasteryProgress();

    /// <summary>The grade of the weapon in hand — what caps both masteries. Raw when nothing is held, which is
    /// the lowest cap there is.</summary>
    public Quality HeldQuality
    {
        get
        {
            // While DeriveWith previews a different weapon, that weapon's grade — or the preview would cap the
            // mastery by the blade you are wearing and understate what the one you are hovering is worth.
            if (previewQuality.HasValue) return previewQuality.Value;
            var w = GetEquippedInstance(EquipSlotType.Weapon);
            return w != null ? w.quality : Quality.Raw;
        }
    }

    /// <summary>The weapon mastery level in use right now: what was earned with the weapon in hand, held to what
    /// that weapon's grade allows.</summary>
    public int WeaponMasteryLevel => Mastery.UsableLevel(HeldWeaponType, HeldQuality);

    /// <summary>MAY THIS CHARACTER RUN THIS SKILL — the one question, asked by the server before any cast and by
    /// every window that offers one. A skill must be LEARNED, and the weapon it belongs to must be in hand. A
    /// skill that belongs to no weapon (what a consumable runs) is nobody's to learn and always passes.</summary>
    public bool MayUse(string skillId)
    {
        if (string.IsNullOrEmpty(skillId)) return false;
        var def = SkillCatalog.Get(skillId);
        if (def == null) return false;
        if (def.weaponType == WeaponType.None) return true;
        return Mastery.HasLearned(skillId) && HeldWeaponType == def.weaponType;
    }

    /// <summary>MAY THIS SKILL GO ON THE BAR, or be ticked on as a passive. The same question as
    /// <see cref="MayUse"/> WITHOUT the weapon in hand, because a bar is arranged for a weapon you are about to
    /// hold as often as for the one you hold now — what stops it firing is the cast's own test.</summary>
    public bool MayBind(string skillId)
    {
        if (string.IsNullOrEmpty(skillId)) return false;
        var def = SkillCatalog.Get(skillId);
        if (def == null) return false;
        return def.weaponType == WeaponType.None || Mastery.HasLearned(skillId);
    }

    /// <summary>Blank, in place, every id in <paramref name="ids"/> this character may not bind
    /// (<see cref="MayBind"/>). The one rule a bar and a ticked passive list are held to wherever they arrive
    /// from — a bind command, or a save arranged under an older rule.</summary>
    public void KeepOnlyBindable(string[] ids)
    {
        if (ids == null) return;
        for (int i = 0; i < ids.Length; i++)
            if (!string.IsNullOrEmpty(ids[i]) && !MayBind(ids[i])) ids[i] = "";
    }

    /// <summary>Why <paramref name="skillId"/> may not be used right now, as one sentence for the player, or null
    /// when it may. The same two tests as <see cref="MayUse"/>, asked so a refused press can be explained.</summary>
    public string WhyNotUsable(string skillId)
    {
        if (string.IsNullOrEmpty(skillId)) return null;
        var def = SkillCatalog.Get(skillId);
        if (def == null || def.weaponType == WeaponType.None) return null;
        string name = string.IsNullOrEmpty(def.displayName) ? def.id : def.displayName;
        if (!Mastery.HasLearned(skillId))
            return name + " is not learned yet — " + def.weaponType + " Mastery " + Mathf.Max(1, def.masteryLevel)
                   + " opens it (yours is " + Mastery.EarnedLevel(def.weaponType) + ").";
        if (HeldWeaponType != def.weaponType)
            return name + " needs a " + def.weaponType + " in hand.";
        return null;
    }

    /// <summary>The passive % multiplier for a stat. The ring is part of the character, not an item, so what it
    /// has earned always counts — every effective-stat getter folds passives in through HERE.</summary>
    float PassivePercent(StatId stat) => Ring.Passives.Percent(stat);

    /// <summary>The ring % invested in <paramref name="stat"/> — the single value skill AFFINITY scales off.
    /// Reads the same choke as every effective-stat getter.</summary>
    public float RingBonus(StatId stat) => PassivePercent(stat);

    /// <summary>The lift this character's own practice puts on <paramref name="skillId"/>'s OUTPUT, added to the
    /// ring's term. An ACTIVE rides its own skill mastery; a PASSIVE cannot be pressed, so it earns none of its
    /// own and rides the weapon's mastery instead. Both stop at +30%.
    ///
    /// <para>Resolved ONCE per cast by the applier, never per hit.</para></summary>
    public float SkillOutputLift(string skillId)
    {
        if (string.IsNullOrEmpty(skillId)) return 0f;
        var def = SkillCatalog.Get(skillId);
        if (def != null && def.IsPassive) return MasteryMath.PassiveLift(WeaponMasteryLevel);
        return MasteryMath.SkillLift(Mastery.UsableSkillLevel(skillId, HeldQuality));
    }

    /// <summary>The live signed bonus a timed sub-stat buff or debuff is putting on <paramref name="stat"/>, 0 when
    /// none. Neutral while the baseline snapshot is being taken, so the "before" column of a level-up preview shows
    /// the character without their windows rather than a moment frozen mid-fight.</summary>
    float SubBonus(SubStat stat) => ignoreStates ? 0f : States.SubStatBonus(stat);

    /// <summary>The whole percentage lift on one stat: ring investment, weapon mastery, and any live sub-stat
    /// window. The single place the three are added, so no formula has to know they exist and none can be
    /// forgotten. Mastery is ADDED to the ring's term, never multiplied onto it.</summary>
    float Lift(StatId ringNode, SubStat stat) => PassivePercent(ringNode) + MasteryPercent(ringNode) + SubBonus(stat);

    /// <summary>What the weapon in hand's mastery adds to one ring stat — 0 unless that weapon's own three stats
    /// include it. Reads a fixed table and a derived level, so it is a lookup, not a search.</summary>
    float MasteryPercent(StatId stat)
    {
        var held = HeldWeaponType;
        if (held == WeaponType.None || !WeaponMasteryStats.Raises(held, stat)) return 0f;
        return MasteryMath.WeaponPercent(Mastery.UsableLevel(held, HeldQuality));
    }

    /// <summary>The hotbar layout — which unlocked skill is bound to each of the nine slots (keys 1–9).
    /// Set by drag/click in the Skills (K) menu; read by the bar + the central key router. Persisted.</summary>
    public HotbarBindings Hotbar { get; } = new HotbarBindings();

    /// <summary>Which PASSIVE skills are live — the ones ticked on in the Grimoire (J), of those learned.
    /// The single owner of that answer; <see cref="PassiveEvents"/> gates every dispatch on it. Persisted.
    /// (Not to be confused with <see cref="Passives"/>, which is the ring's stat walk.)</summary>
    public PassiveLoadout PassiveSkills => passiveSkills ??= new PassiveLoadout(this);
    PassiveLoadout passiveSkills;

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

    static readonly Color MonsterMissColor = Color.white;   // a monster's swing whiffed the player → white MISS
    /// <summary>The digits of a hit that LANDED on a player — red for a bite, violet for a spell. Public because the
    /// copy of somebody else standing in front of you shows the same two colours (<see cref="NetPlayer.ShowBlow"/>),
    /// and one blow must not be one colour on the screen of the person it happened to and another on everybody
    /// else's.</summary>
    public static readonly Color MonsterHitColor   = new Color(1f, 0.35f, 0.35f);
    public static readonly Color MonsterMagicColor = new Color(0.7f, 0.5f, 1f);

    /// <summary>THE colour a blow on a person is drawn in, on their own machine and on everyone watching: a tick in the
    /// colour the server gave it, otherwise magic violet or hit red.</summary>
    public static Color ColourOfBlow(in DamageInfo info, bool tick, Color tickColor) =>
        tick && tickColor.a > 0f ? tickColor : info.isMagic ? MonsterMagicColor : MonsterHitColor;
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
    public int MaxHP   => Told ? toldMaxHP : UnitStatMath.MaxHP(Inputs, equipBonuses, Lift(StatId.HP, SubStat.MaxHP));
    public float HPRegenPerSecond => UnitStatMath.HPRegenPerSecond(Inputs, MaxHP);
    public int MeleeAttack  => Told ? toldCurrent.meleeMax : UnitStatMath.MeleeAttack(Inputs, equipBonuses, Lift(StatId.Atk, SubStat.Attack));
    public int RangedAttack => Told ? toldCurrent.rangedMax : UnitStatMath.RangedAttack(Inputs, equipBonuses, Lift(StatId.Atk, SubStat.Attack));
    /// <summary>The physical attack this character swings with: ranged attack while holding a ranged weapon, melee
    /// attack otherwise. Every physical roll reads this, so the held weapon decides for normal attacks and physical
    /// skills alike.</summary>
    public int Attack  => Told ? toldCurrent.atkMax : UnitStatMath.PhysicalAttack(HeldWeaponType, Inputs, equipBonuses, Lift(StatId.Atk, SubStat.Attack));

    // The weapon type in hand — or, while DeriveWith previews a different weapon, that one, so the hover preview
    // of a bow shows ranged attack rather than the sword's melee attack.
    WeaponType HeldWeaponType
    {
        get
        {
            if (previewWeaponType.HasValue) return previewWeaponType.Value;
            var w = GetEquipped(EquipSlotType.Weapon);
            return w != null ? w.weaponType : WeaponType.None;
        }
    }
    public int Defense => Told ? toldCurrent.def : UnitStatMath.Defense(Inputs, equipBonuses, Lift(StatId.Def, SubStat.Defense));

    public int MaxMP        => Told ? toldMaxMP : UnitStatMath.MaxMP(Inputs, equipBonuses, Lift(StatId.MP, SubStat.MaxMP));
    public float MPRegenPerSecond => mpRegenPerSecond + UnitStatMath.MPRegenPerSecond(Inputs, MaxMP);
    public int MagicAttack  => Told ? toldCurrent.matkMax : UnitStatMath.MagicAttack(Inputs, equipBonuses, Lift(StatId.MAtk, SubStat.MagicAttack));
    // The ring has no separate M.DEF node — the Def node is dual, so MagicDefense reads the Def passive %.
    public int MagicDefense => Told ? toldCurrent.mdef : UnitStatMath.MagicDefense(Inputs, equipBonuses, Lift(StatId.Def, SubStat.MagicDefense));

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
    public int MinAttack      => Told ? toldCurrent.atkMin : UnitStatMath.MinAttack(Attack, P(PrimaryStat.DEX));
    public int MaxMagicAttack => MagicAttack;
    public int MinMagicAttack => Told ? toldCurrent.matkMin : UnitStatMath.MinMagicAttack(MagicAttack, P(PrimaryStat.INT));

    public int XPToNext => UnitStatMath.XpToNextFrom(Level);
    public bool IsDead => CurrentHP <= 0;

    // IDamageable seam (network-readiness): same one apply-entry a mob's Health exposes, so a future net layer targets
    // player and mob through ONE type. Routes into the existing player sinks, carrying a mob's crit so it ignores our
    // defence exactly as the player's crit ignores a mob's; consumed by the Step D authority seam.
    public Vector3 DamagePosition { get { var a = Avatar(); return a != null ? a.position : transform.position; } }
    public int ApplyDamage(in DamageInfo info)
        => info.isMagic ? TakeMagicDamage(info.amount, info.element, info.attacker)
                        : TakeDamage(info.amount, info.attackerHit, info.element, info.attacker, info.missed, info.isCrit);
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
    /// <para>PLUS whatever a skill is lending right now. A basic-attack range bonus is added here, at the one place
    /// reach is answered, so the walk that stops when you are close enough, the swing that decides it may land, and
    /// the server that checks it really did all read the same longer number and cannot disagree.</para>
    public float MeleeReachFullSize
        => Mathf.Max(0f, EquippedReachFullSize + States.SubStatBonus(SubStat.BasicAttackRange));

    /// <summary>Gear alone, with nothing a skill is lending — see <see cref="ICombatUnit.EquippedReachFullSize"/>.</summary>
    public float EquippedReachFullSize => WeaponReach.Of(GetEquipped(EquipSlotType.Weapon), unarmedReach);

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

    /// <summary>Walk this character to <paramref name="target"/> and run <paramref name="onArrive"/> the moment the
    /// gap is <paramref name="range"/> or less — the walk a skill does when it was pressed from too far away. FALSE
    /// when there are no legs to do it with, which is every character a server holds: walking is the owner's machine
    /// to do, so the wait belongs there and nowhere else.</summary>
    public bool WalkIntoRange(Transform target, float range, System.Action onArrive)
    {
        var r = Rig();
        if (r == null) return false;
        r.BeginMoveIntoRange(target, range, onArrive);
        return true;
    }

    /// <summary>Call off a walk started by <see cref="WalkIntoRange"/> without running what it was holding.</summary>
    public void StopWalkingIntoRange() { var r = Rig(); if (r != null) r.DropTheWait(); }
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
    public float CritMultiplier  => Told ? toldCurrent.critMult : UnitStatMath.CritMultiplier(Inputs, equipBonuses, Lift(StatId.Critical, SubStat.CritDamage));   // …+ LUK + the Critical node's % + what Ink wrote
    // These combine the primary-stat contribution (LUK/AGI/DEX) with the passive % layer.
    public float CritChance01    => Told ? toldCurrent.critChance : UnitStatMath.CritChance01(Inputs, equipBonuses, Lift(StatId.Critical, SubStat.CritChance));
    // RO accuracy/evasion. HIT (DEX + level) is rolled against a target's FLEE to decide a physical hit;
    // FLEE (AGI + level, scaled by the Evasion passive) is how often incoming physical attacks whiff on us.
    // Both feed the shared HIT-vs-FLEE roll in Combat; magic ignores them. See Combat for the formula.
    public int Hit  => Told ? toldCurrent.hit : UnitStatMath.Hit(Inputs, Lift(StatId.Accuracy, SubStat.Accuracy));
    public int Flee => Told ? toldCurrent.flee : UnitStatMath.Flee(Inputs, Lift(StatId.Evasion, SubStat.Evasion));
    // Pre-renewal ASPD: AGI is the primary driver, DEX contributes a fraction (DexAspdShare). Kept as a
    // SPEED MULTIPLIER (the game's attack model) rather than the RO 0–200 scale. >1 = swing faster.
    public float AttackSpeedMult => Told ? toldCurrent.aspdMult : UnitStatMath.AttackSpeedMult(Inputs, equipBonuses, PassivePercent(StatId.AttackSpeed), AspdBuffMult);

    /// <summary>The ONE cast-speed number: the multiplier applied to a spell's base wind-up, so a spell's real cast
    /// time is <c>baseCast × CastTimeMult</c>. 1 at 0 DEX, 0 (instant) at DEX 150; the CastSpeed ring node and the
    /// cast-speed buff shorten it further. Lower is faster.</summary>
    public float CastTimeMult => Told ? toldCurrent.castTimeMult : UnitStatMath.CastTimeMult(Inputs, PassivePercent(StatId.CastSpeed), CastBuffMult);

    // ---- Derived-stat snapshots (the pure keystone for the HUD windows) -----------------------
    /// <summary>A readout of every derived stat for the CURRENT loadout (what the Character /
    /// Inventory windows display live).</summary>
    public DerivedSnapshot CurrentDerived
    {
        get { computeHere = true; try { return SnapshotNow(); } finally { computeHere = false; } }
    }

    // ---- states ON vs OFF: the two views the Character window diffs to show "(+N)" ---------------
    // Buffs/debuffs land in DIFFERENT places at runtime — some inside a getter (attack/cast speed), some at swing
    // time (the damage buff scales the roll; Blind subtracts HIT). So a plain snapshot mixes "some states included,
    // some not". These two views make it explicit and uniform: BaseDerived is the character with NO states, and
    // DerivedWithStates is what they're actually fighting at. Diff them for the per-line bonus.
    bool ignoreStates;   // while set, the buff multipliers read neutral — the SAME formulas, minus the states

    /// <summary>Derived stats with every buff/debuff EXCLUDED — the character's own baseline.</summary>
    public DerivedSnapshot BaseDerived
    {
        get
        {
            if (Told) return toldBaseline;
            ignoreStates = true; try { return SnapshotNow(); } finally { ignoreStates = false; }
        }
    }

    /// <summary>Derived stats with the live states folded in — what the player is actually fighting at right now.
    /// Applies each state where it really lands at runtime, so the window can't disagree with combat:
    /// the damage buff scales the attack rolls (as <see cref="RollDamage"/> does), Blind subtracts HIT (as the
    /// attacker does), Slow/Haste scale move speed; attack/cast speed already carry their buffs in the getters.</summary>
    public DerivedSnapshot DerivedWithStates
    {
        get
        {
            if (Told) return toldWithStates;
            var d = SnapshotNow();               // aspd + cast speed already include their buffs
            float b = DamageBuffMult;            // the same multiplier the damage rolls apply
            if (b != 1f)
            {
                d.atkMin = Mathf.RoundToInt(d.atkMin * b);   d.atkMax = Mathf.RoundToInt(d.atkMax * b);
                d.meleeMin = Mathf.RoundToInt(d.meleeMin * b);   d.meleeMax = Mathf.RoundToInt(d.meleeMax * b);
                d.rangedMin = Mathf.RoundToInt(d.rangedMin * b); d.rangedMax = Mathf.RoundToInt(d.rangedMax * b);
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
        meleeMin = UnitStatMath.MinAttack(MeleeAttack, P(PrimaryStat.DEX)),   meleeMax = MeleeAttack,
        rangedMin = UnitStatMath.MinAttack(RangedAttack, P(PrimaryStat.DEX)), rangedMax = RangedAttack,
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
        basicAttackRange = ReachNow,
    };

    // Reach for the sheet, on the same baseline switch every other line uses: gear alone when the window is
    // snapshotting what you are without your buffs, gear plus whatever a skill is lending in the live view.
    float ReachNow => ignoreStates ? EquippedReachFullSize : MeleeReachFullSize;

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
        if (slot == EquipSlotType.Weapon)
        {
            previewWeaponType = hypothetical != null && hypothetical.def != null ? hypothetical.def.weaponType : WeaponType.None;
            previewQuality = hypothetical != null ? hypothetical.quality : Quality.Raw;
        }
        computeHere = true;
        try { return SnapshotNow(); }
        finally { previewBonuses = null; previewWeaponType = null; previewQuality = null; computeHere = false; }
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

    public bool RollBasicCrit(ICombatUnit victim) => RollCrit(victim != null ? victim.CritShieldLuk : 0);

    public int RollBasicDamage(bool crit) => RollDamageMagnitude(crit);

    public int RollSkillDamage(DamageType type, int targetLuk, out bool crit)
        => type == DamageType.Magic ? RollMagicDamage(out crit)
                                    : RollDamage(out crit, targetLuk, type == DamageType.Crit);

    /// <summary>Our LUK, which lowers an attacker's crit chance against us — the same shield a mob's LUK gives it.</summary>
    public int CritShieldLuk => P(PrimaryStat.LUK);

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
    float EffectiveCritChance(int targetLuk) => UnitStatMath.EffectiveCritChance(CritChance01, targetLuk);

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
        toldMaxMP = Mathf.Max(1, max);
        CurrentMP = Mathf.Clamp(current, 0, toldMaxMP);
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
        CurrentHP = MaxHP;
        CurrentMP = MaxMP;
    }

    // Tuning the `level` field in the inspector (at runtime) recomputes every derived stat — MaxHP,
    // Attack, MaxMP, … all read Level — so refill HP/MP to the new max and re-fire the UI events. The
    // skill % layer (Passives) and allocated stat points are separate layers that still apply on top,
    // so the displayed numbers stay consistent for the dialed-in level. No-op outside Play / before Awake.
    void OnValidate()
    {
        // THE GUARD COMES FIRST. Clamping the level above it wrote a saved value while nobody was playing, so a
        // component holding an invalid number quietly marked the scene changed on every recompile.
        if (!Application.isPlaying || Instance != this) return;
        if (level < 1) level = 1;
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
    /// Used by the <c>/lvl</c> command.</summary>
    public void SetLevel(int newLevel)
    {
        level = Mathf.Max(1, newLevel);
        // Dialing a level in hands out no experience, so the lifetime figure would still read as whatever was
        // actually earned — 0 on a character levelled straight to 80, which reads as broken on a board that
        // ranks by it. Raised to the least a climb to this level would have cost, and never lowered, so a
        // dialed-DOWN level cannot erase experience somebody really gathered.
        LifetimeXp = System.Math.Max(LifetimeXp, UnitStatMath.XpEarnedByLevel(level) + XP);
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
            // AND THE SKILLS THEMSELVES, not just the bar. A character carries only what it can use, so changing
            // weapons changes the list — without this a player would keep the old weapon's skills and have none
            // of the new one's, and the bar would show slots nothing answered.
            RefreshUsableSkills();
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
        if (a != null) DamagePopup.SpawnText(a, "Dodge", DodgeColor);
        OnEvaded?.Invoke();
        return true;
    }

    // The unified per-unit state store (buffs/debuffs) — single source for gameplay queries AND the buff-tray icons.
    // Lazily created on the player's GameObject; cached after first touch (no per-hit GetComponent).
    UnitStates states;
    public UnitStates States => states != null ? states : (states = GetComponent<UnitStates>() ?? gameObject.AddComponent<UnitStates>());

    /// <summary>Who last cost this unit health — see <see cref="ICombatUnit.LastAttacker"/>.</summary>
    public ICombatUnit LastAttacker { get; private set; }
    public void NoteAttacker(ICombatUnit attacker) => LastAttacker = attacker;

    /// <summary>The weapon in this character's hands — what a mastery is earned in.</summary>
    public WeaponType HeldWeapon => HeldWeaponType;

    /// <summary>Nothing is recorded on a PERSON: practice is paid for killing creatures, and a character is not
    /// one. The door still calls this on whoever was hurt, so it answers rather than being special-cased there.</summary>
    public void NoteDamageFor(ICombatUnit attacker, WeaponType weapon, int dealt) { }

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
        toldMaxHP = Mathf.Max(1, max);
        CurrentHP = Mathf.Clamp(current, 0, toldMaxHP);
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
        SpawnIds.PlayerGotUp(this);
    }

    // The player's host is a static handle the controller already publishes — nothing to look up, ever. It lives on the
    // spawned avatar, not on this persistent stats object, so it is null until the body exists.
    //
    // A TOLD HOST WINS, for the same reason a told body does: "the player's status host" is one screen's answer, and
    // a server holding several people has one per person.
    public StatusController StatusHost => boundStatusHost != null ? boundStatusHost : StatusController.Player;

    /// <summary>This character's own skills, in the fixed order the passive dispatch walks them in — the shared list
    /// every unit that casts holds (see <see cref="UnitSkills"/>). The order is the whole catalog's, identical on every
    /// machine, because a skill's position IS its identity to the no-self-feeding brake.</summary>
    public UnitSkills Skills => skills ??= new UnitSkills(this);
    UnitSkills skills;

    /// <summary>A person's list holds everything they can use — their bar, their ticked passives, their bag — so a
    /// passive in it runs only when their loadout says it is switched on.</summary>
    public bool PassiveIsOn(SkillDef def) => PassiveSkills.IsActive(def);

    // The object a skill component gets attached to when one is finally needed. A character and the body it drives
    // are two different objects here, so this has to be told, not looked up.
    GameObject skillHost;

    /// <summary>True once this character has been told what its skills are.</summary>
    public bool HasSkills => Skills.HasSkills;

    /// <summary>Rebuild what this character carries from what it can use RIGHT NOW. Anything that changes the
    /// answer calls this: a weapon changing, a skill being put on the bar, a passive being ticked.
    ///
    /// <para>Without it, a skill dropped onto the bar is recorded in the bar and has no component behind it, so
    /// the slot draws empty and the skill cannot be cast — which looks exactly like the drop having failed.</para></summary>
    /// <summary>Rebuild what this character can use, and the positions behind it.
    ///
    /// <para><b>THE RULE, and it binds every caller: change any of the THREE sources below and call this in the same
    /// breath.</b> The three are the hotbar (its slots and its ult), the ticked passives, and the consumables in the
    /// bag — see <see cref="UsableSkills"/>. A weapon grants nothing: what it changes is which of them may FIRE. A skill has nothing to run it until this has
    /// run, so a source changed without it leaves the character agreeing the skill is theirs and holding nothing to
    /// cast it with: the press is refused and, before it said so, refused in total silence.</para>
    ///
    /// <para>This was a real bug on the SERVER for every one of them: only a weapon swap and the moment somebody
    /// joined ever rebuilt, so a skill dragged onto the bar from the Grimoire, a passive ticked on, and a potion
    /// picked up all did nothing until the next sign-in. The player's own machine had always rebuilt on the same
    /// changes, which is why it looked like it should work right up until it was pressed.</para></summary>
    public void RefreshUsableSkills()
    {
        if (skillHost != null) BindSkills(skillHost, UsableSkills());
    }

    /// <summary>THE skills this character can actually use — everything on their bar, everything their weapon
    /// grants them, everything they ticked on, and whatever the consumables in their bag run. Nothing else.
    ///
    /// <para>A player used to be handed the WHOLE authored catalog, so every person the server held carried a
    /// position for every skill in the game whether or not they could ever cast it. What they can use comes from
    /// four places and this is the one that puts them together, in the catalog's own order so the list is the
    /// same however it was assembled.</para>
    ///
    /// <para>It reads THIS character's gear rather than asking the passive loadout whether a weapon granted a
    /// skill: that question is answered against whoever is playing at this keyboard, which is nobody on a server
    /// holding twenty people.</para></summary>
    public System.Collections.Generic.List<SkillDef> UsableSkills()
    {
        usableIds.Clear();

        for (int i = 0; i < HotbarBindings.SlotCount; i++)
        {
            var id = Hotbar.Get(i);
            if (!string.IsNullOrEmpty(id)) usableIds.Add(id);
        }
        if (!string.IsNullOrEmpty(Hotbar.Ult)) usableIds.Add(Hotbar.Ult);

        var ticked = PassiveSkills.Capture();
        if (ticked != null)
            for (int i = 0; i < ticked.Length; i++)
                if (!string.IsNullOrEmpty(ticked[i])) usableIds.Add(ticked[i]);

        for (int i = 0; i < bag.Count; i++)
        {
            var def = bag[i] != null ? bag[i].def : null;
            if (def != null && def.IsUsable) usableIds.Add(def.useSkillId);
        }

        // IN THE CATALOG'S ORDER, always. A skill's position is what the no-self-feeding brake identifies it by
        // within this process, so the list must not depend on which pocket each id happened to come out of.
        usable.Clear();
        var all = SkillCatalog.All;
        for (int i = 0; i < all.Count; i++)
        {
            var def = all[i];
            if (def != null && !string.IsNullOrEmpty(def.id) && usableIds.Contains(def.id)) usable.Add(def);
        }
        return usable;
    }

    // Reused across calls: this runs on a weapon swap and at every join, and a server does it once per person.
    readonly System.Collections.Generic.HashSet<string> usableIds =
        new System.Collections.Generic.HashSet<string>();
    readonly System.Collections.Generic.List<SkillDef> usable =
        new System.Collections.Generic.List<SkillDef>(16);

    /// <summary>Say which skills are this character's, and which object their components live on. Called again
    /// whenever that list is rebuilt (a fresh avatar after a door, a newly composed skill).
    ///
    /// <para>NOTHING IS BUILT HERE. Every skill gets a position and nothing else; the component that plays it is
    /// made the first moment it is genuinely needed — a press, or a passive of theirs that actually fires. A server
    /// holds one of these per person signed in, so building the whole catalog for everybody the instant they joined
    /// was the largest thing a person cost simply by being present.</para>
    ///
    /// <para>A component already sitting on the host (one an author-time tool attached) is ADOPTED into its own
    /// position rather than duplicated.</para></summary>
    public void BindSkills(GameObject host, System.Collections.Generic.IReadOnlyList<SkillDef> defs)
    {
        skillHost = host;
        Skills.Bind(host, defs);

        // AND THE BAR IS REDRAWN, HERE, because this is the one moment the list it draws from is rebuilt. A cell
        // finds its skill by asking this character for the component that plays it, so a cell built while the list
        // was still empty draws as an open slot and stays that way until something unrelated happens to redraw it.
        // That is exactly what a sign-in looks like: the bar is built the moment the HUD exists, and the saved
        // bindings arrive over the network a second or two later, on a body that may itself be a second one built
        // after the map finished loading. Saying it at each of the six places that rebind is how one of them was
        // forgotten; saying it once, where they all pass, cannot be.
        // ONLY FOR THE PERSON AT THIS KEYBOARD. A server rebinds this list for everybody signed in, and none of
        // them has a bar on this screen.
        if (ReferenceEquals(this, Instance)) HotbarPanel.RebuildRequested?.Invoke();
    }

    /// <summary>The component this character casts <paramref name="skillId"/> with, built on first use, or null if
    /// that is not one of their skills. One lookup, whatever the size of the skill catalog.</summary>
    public SkillRunner RunnerById(string skillId) => Skills.RunnerById(skillId);

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

    // A parry cancels the hit BEFORE defence/element math, exactly like the off-hand block below it. The spend, the
    // "PARRY" word AND the moment it raises are all the shared unit primitive now, so a creature's parry announces
    // itself exactly as this one does. `attacker` rides along so a counter-passive can aim at who was parried.
    bool ParriedHit(ICombatUnit attacker)
    {
        if (!UnitMitigation.TryParry(this, attacker)) return false;
        lastMitigation.parried = true;
        return true;
    }

    /// <summary>What the mitigations did to the blow this character is taking right now — recorded here rather than
    /// drawn, because the machine that decides a blow is the server and a server has no screen. Read once, by the
    /// door that announces the blow to the person it happened to, and cleared by that read so a later blow that
    /// mitigated nothing can never inherit an older one's word.</summary>
    UnitMitigation.Outcome lastMitigation;

    /// <summary>Hand over what the mitigations did to the last blow, and forget it. Read-and-clear, the same shape
    /// <see cref="UnitStates.TakeExpiredDebt"/> uses, and for the same reason: a total nobody collects must not be
    /// counted twice.</summary>
    public UnitMitigation.Outcome TakeMitigation()
    {
        var o = lastMitigation;
        lastMitigation = default;
        return o;
    }

    // Drain the barrier first, then HP. Pops a pale-cyan number for the soaked part so the absorb is visible. No alloc
    // (int math + the pooled DamagePopup). The two hit intakes route through here; DoT keeps its own line (shield bypass).

    /// <summary>Run the last two mitigations (Shield absorb, then Brace defer) and subtract what's left from HP.
    /// RETURNS what actually landed, which is NOT what the caller passed in — a barrier or a brace can reduce it, to
    /// zero. Every caller must use the RETURN for anything downstream (the number shown, thorns, what it
    /// reports back); the figure it passed in is only the hit's size BEFORE these two, and using that is the bug this
    /// signature exists to prevent.</summary>
    int ApplyHitToHP(int dmg, ICombatUnit attacker = null)
    {
        if (dmg <= 0) return 0;
        // A WATCHED COPY WEIGHS NOTHING. The figure it was handed is what already got through on the server, with the
        // barrier and the brace taken out of it there — so running them again here took the same protection off a
        // second time: the number on screen read smaller than the blow really was, or vanished entirely, and the
        // mirrored charges were spent on a decision this machine had no business making. What the mitigations DID
        // arrives alongside the blow and is drawn by NetPlayer.ShowBlow.
        if (!IsWatchedCopy)
        {
            dmg = UnitMitigation.AfterShield(States, dmg, out lastMitigation.soaked);
            dmg = UnitMitigation.Deferred(States, dmg, out lastMitigation.banked);
            if (dmg <= 0)
                CombatAuthority.NoDamage(lastMitigation.banked > 0 ? NoDamageReason.Braced : NoDamageReason.Soaked);
        }
        if (dmg > 0)
        {
            int before = CurrentHP;
            SubtractHealth(dmg);
            // A hit got THROUGH — the two moments that answer it are raised by the SHARED dispatch, the same call a
            // creature's damage door makes, so "damage landed on me" and "that dropped me under a third of my health"
            // cannot mean one thing for a person and another for a creature.
            PassiveEvents.HitLanded(this, before, attacker);
        }
        return dmg;
    }

    public int TakeDamage(int attackerAttack) => TakeDamage(attackerAttack, -1);

    /// <summary>Apply an incoming PHYSICAL hit: damage = max(1, attackerAttack − Defense), then scaled by the
    /// attacker's <paramref name="attackElement"/> vs our <see cref="armorElement"/>. When
    /// <paramref name="attackerHit"/> ≥ 0, the attacker's HIT is first rolled against our <see cref="Flee"/>
    /// (RO accuracy); a miss negates the hit. Pass −1 to skip the roll (always lands). <paramref name="attacker"/>
    /// — whoever swung, person or creature — is what thorns bounce back at and what the moments this blow raises aim
    /// at. Returns damage dealt.</summary>
    public int TakeDamage(int attackerAttack, int attackerHit, Element attackElement = Element.Neutral,
                          ICombatUnit attacker = null, bool missed = false, bool crit = false)
    {
        if (IsDead) return CombatAuthority.NoDamage(NoDamageReason.AlreadyDead);
        // i-frames (dodge-dash): the hit passes harmlessly through
        if (DodgedByInvuln()) return CombatAuthority.NoDamage(Invincible ? NoDamageReason.Invincible : NoDamageReason.Dodged);

        // Accuracy was already rolled by the attacking MOB (Combat.Missed) — we only PRESENT it, never re-roll, since
        // a second random roll could disagree. A miss shows a WHITE MISS over the player (player-dealt = red, Health).
        if (missed)
        {
            var dav = Avatar();
            if (dav != null) DamagePopup.SpawnText(dav, "MISS", MonsterMissColor);
            OnEvaded?.Invoke();
            return CombatAuthority.NoDamage(NoDamageReason.Missed);
        }

        // A WATCHED COPY IS TOLD WHAT LANDED — it does not work it out, exactly as its health is told to it rather
        // than counted. The server has already taken the block, the parry, the armour's element and DEF off this
        // figure, so weighing them a second time here would draw the person a smaller number than the blow they
        // actually took, or swallow the blow entirely on a coin this machine had no business tossing.
        if (IsWatchedCopy) return LandOnMe(attackerAttack, attacker, MonsterHitColor, false, crit, attackerHit);

        // Off-hand block: a shield can negate the hit (and clang) before any damage applies.
        var off = GetEquipped(EquipSlotType.Shield);
        if (Combat.Blocked(off))
        {
            Sfx.Play(off.guardSfx != null && !off.guardSfx.Empty ? off.guardSfx : blockSfx);
            lastMitigation.blocked = true;
            OnBlocked?.Invoke();
            return CombatAuthority.NoDamage(NoDamageReason.Guarded);
        }

        // Element property: the incoming attack's element vs our armor element scales the damage (RO chart).
        float mod = Elements.Modifier(attackElement, armorElement);
        // our armor fully resists this element
        if (mod <= 0f) return CombatAuthority.NoDamage(NoDamageReason.Immune);
        // read LAST of the negations, so a charge is only ever spent on a hit that would really have landed (a miss /
        // block / immunity keeps it in hand)
        if (ParriedHit(attacker)) return CombatAuthority.NoDamage(NoDamageReason.Parried);

        // Pre-renewal DEF split: hard-DEF % then flat soft DEF; a CRIT ignores both, exactly as it does on a mob.
        int dmg = UnitMitigation.Landed(States, Combat.AfterDefense(attackerAttack, HardDefPercent, Defense, crit) * mod);
        // Everything below reads what LANDED, never the pre-barrier figure: a hit a Shield ate or a Brace holds costs
        // no HP, so it must also not flash, not bounce thorns, and must report 0 to the attacker
        // (whose own "did I connect" check pops the damage number and grants its XP).
        return LandOnMe(dmg, attacker, MonsterHitColor, false, crit, attackerHit);
    }

    /// <summary>Show a blow the SERVER decided landed on this person, on their own machine — exactly as decided, with
    /// nothing worked out: no dodge, no block, no armour, no barrier, no thorns, no low-health check. Every one of those
    /// already happened on the server, which also owns the health this blow took; that arrives on its own beat.
    ///
    /// <para>The one thing this starts is the hurt stagger, fed the server's final number: a stagger stops the legs,
    /// and the legs are this machine's to drive.</para></summary>
    public void ShowBlowFromServer(in DamageInfo info, bool tick = false, Color tickColor = default)
    {
        // THE BLOW THAT KILLED YOU STILL SHOWS, whichever reached this machine first — it or the health reading zero.
        // Only a damage-over-time tick is dropped once you are dead.
        if (tick && IsDead) return;
        var av = Avatar();
        if (info.missed)
        {
            if (av != null) DamagePopup.SpawnText(av, "MISS", MonsterMissColor);
            OnEvaded?.Invoke();
            return;
        }
        if (info.amount <= 0) return;

        if (av != null)
            DamagePopup.Spawn(av, info.amount, ColourOfBlow(info, tick, tickColor));
        // The hurt grunt, and the event that drives the hurt flash.
        Sfx.Play(hurtSfx);
        OnDamaged?.Invoke(info.amount);

        // A tick staggers nobody — the same rule the server's damage door keeps. And nothing staggers the dead.
        if (tick || IsDead) return;
        var status = StatusHost;
        if (status != null) status.TookHit(info.amount);
    }

    /// <summary>Where every hit on the player ends: what a Shield or a Brace leaves of it comes off HP, the DIGITS
    /// appear over the body, the hurt grunt and flash run, thorns bounce back, and a death is raised once.
    ///
    /// <para>The number is drawn HERE, on the side it happened to, for the same reason a creature's is drawn inside
    /// its own <see cref="Health"/>: whoever swung is no longer on this machine. A monster's bite is decided on the
    /// server, so a number the monster itself drew appeared on a screen nobody is sitting at.</para></summary>
    int LandOnMe(int dmg, ICombatUnit attacker, Color numberColor, bool isMagic, bool crit, int attackerHit)
    {
        dmg = ApplyHitToHP(dmg, attacker);
        OnHealthChanged?.Invoke();
        var av = Avatar();
        // The hit cue, chosen by the same rule a creature's is and played whenever the blow got this far — a miss, a
        // block, a parry or an immunity has already returned — exactly as a creature plays it whatever its barrier
        // left. The digits keep our own colour. Silent on a joined machine: the server's cue reaches every screen.
        if (av != null) VfxCue.Fire(VfxCue.HitCue(isMagic, crit, attackerHit), av);
        if (dmg > 0)
        {
            if (av != null) DamagePopup.Spawn(av, dmg, numberColor);
            // player hurt grunt
            Sfx.Play(hurtSfx);
            // drives the player hurt flash
            OnDamaged?.Invoke(dmg);
            // thorns: bounce a fraction back at the attacker
            Reflect(dmg, attacker);
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
        CombatAuthority.LogDeath(this);
        OnDied?.Invoke();
        var a = Avatar();
        if (a != null) VfxCue.Fire(VfxCueId.OnDeath, a);
    }

    /// <summary>Apply an incoming MAGIC hit: reduced by <see cref="MagicDefense"/> (not <see cref="Defense"/>). Magic
    /// can't be dodged, and evasion does nothing to it. Returns the damage dealt.</summary>
    public int TakeMagicDamage(int attackerMagic, Element attackElement = Element.Neutral, ICombatUnit attacker = null)
    {
        if (IsDead) return CombatAuthority.NoDamage(NoDamageReason.AlreadyDead);
        // i-frames (dodge-dash) dodge magic too
        if (DodgedByInvuln()) return CombatAuthority.NoDamage(Invincible ? NoDamageReason.Invincible : NoDamageReason.Dodged);

        // Told, not worked out — the same rule the physical intake above explains.
        if (IsWatchedCopy) return LandOnMe(attackerMagic, attacker, MonsterMagicColor, true, false, -1);

        float mod = Elements.Modifier(attackElement, armorElement);
        // armour already nullified it — nothing to parry, so no charge is spent
        if (mod <= 0f) return CombatAuthority.NoDamage(NoDamageReason.Immune);
        // the read catches magic too: Parry is a timing skill, not a physical block
        if (ParriedHit(attacker)) return CombatAuthority.NoDamage(NoDamageReason.Parried);

        int dmg = UnitMitigation.Landed(States, Combat.AfterDefense(attackerMagic, HardMagicDefPercent, MagicDefense, false) * mod);
        return LandOnMe(dmg, attacker, MonsterMagicColor, true, false, -1);
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
        if (a != null) DamagePopup.Spawn(a, dmg, color);
        OnDamaged?.Invoke(dmg);
        if (CurrentHP == 0) RaiseDeath();
        return dmg;
    }

    // Damage reflect (thorns card): bounce a fraction of damage taken back at the attacking mob as true
    // damage (bypasses its defense, can't itself be reflected — TakeTrueDamage never calls back here).
    void Reflect(int dmgTaken, ICombatUnit attacker)
    {
        // permanent thorns card + a timed skill reflect window
        float pct = reflectPct + ActiveReflectBonus;
        if (pct <= 0f || dmgTaken <= 0 || PlayerUnits.Gone(attacker) || attacker.IsDead) return;
        int r = Mathf.RoundToInt(dmgTaken * pct);
        if (r > 0) CombatAuthority.ApplyTrue(attacker, r, ReflectColor, "reflect");
    }

    /// <summary>Heal from lifesteal — like <see cref="Heal"/> but SILENT (no green popup), so multi-hit leech
    /// doesn't spam numbers. Clamped to <see cref="MaxHP"/>.</summary>
    public void LeechHeal(int amount)
    {
        if (amount <= 0 || IsDead) return;
        // The same rule as Heal: health is the server's, so a player's machine never adds to it.
        if (IsWatchedCopy || GameNetwork.TheWorldIsElsewhere) return;
        int before = CurrentHP;
        CurrentHP = Mathf.Min(MaxHP, CurrentHP + amount);
        if (CurrentHP != before) OnHealthChanged?.Invoke();
        CombatAuthority.LogHeal(this, amount, "life steal");
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

        // A PLAYER'S MACHINE HEALS NOBODY. Health is the server's, and so is every gain: the server heals the copy it
        // holds and tells every screen what it healed, green digits included (CombatAuthority.NoteHPAdded).
        if (IsWatchedCopy || GameNetwork.TheWorldIsElsewhere) return;

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

    /// <summary>SERVER ONLY: the shared identity of the person this fighter belongs to, bound once when it is built —
    /// so every blow and heal announced for them reads a field instead of walking up the hierarchy.</summary>
    public Mirror.NetworkIdentity ServerIdentity { get; private set; }
    public void BindServerIdentity(Mirror.NetworkIdentity identity) { ServerIdentity = identity; }

    string boundMap;

    /// <summary>Tell this character which map its person is standing in. The server's half of <see cref="Map"/>:
    /// a person there has no body in any map of its own, so the only machine that knows is theirs, and it says so
    /// (see <see cref="NetPlayer"/>). Same shape as <see cref="BindBody"/> on purpose.</summary>
    public void BindMap(string map)
    {
        if (boundMap == map) return;
        boundMap = map;
        // AND THE ROLL IS TOLD. Creatures look for somebody to chase by map, so a person who walked through a door
        // and was never re-filed would be hunted by the map they left and invisible to the one they are in.
        PlayerUnits.Refile(this);
        SpawnIds.PlayerEnteredMap(this, map);
    }

    /// <summary>This person's spawn_id in the map they are in — see <see cref="ICombatUnit.Spawn"/>.</summary>
    public SpawnTag Spawn { get; set; }

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

    /// <summary>SERVER-SIDE ONLY. Pay experience and run the level-up. Returns how many character levels that
    /// gained, and says through <paramref name="ringLevelsGained"/> how many ring levels — the server hands both
    /// counts to the owner's machine with the finished numbers (<see cref="GameNetwork.TellOwnerTheyEarned"/>),
    /// and that machine takes them through <see cref="TakeXpFromServer"/>. It used to run this same loop itself
    /// from the raw amount, which is the player's machine deciding a level, a stat point and a full heal.</summary>
    public int AddXP(int amount) => AddXP(amount, out _);

    public int AddXP(int amount, out int ringLevelsGained)
    {
        ringLevelsGained = 0;
        // A machine whose world is elsewhere never runs this: it is told the finished answer instead.
        if (GameNetwork.TheWorldIsElsewhere) return 0;
        if (amount <= 0) return 0;

        // The SAME kills feed both tracks: the character (flat stats) and the ring (its own level → passive
        // draft points). The ring is part of the character, so nothing gates this.
        ringLevelsGained = Ring.AddXP(amount);

        int gained = 0;
        XP += amount;
        LifetimeXp += amount;
        while (XP >= XPToNext)
        {
            XP -= XPToNext;
            level++;
            gained++;
            StatPoints += StatusPointsForLevel(level);   // pre-renewal: ⌊L/5⌋+3 status points this level
            CurrentHP = MaxHP;                // level-up fully heals
            CurrentMP = MaxMP;                // …and refills mana
            PlayLevelUp(level);
        }
        OnStatsChanged?.Invoke();
        OnHealthChanged?.Invoke();
        return gained;
    }

    /// <summary>Take the server's finished answer after it paid this character: level, experience, points and
    /// the ring as they now stand, plus how many levels each track gained so the moment plays the right number
    /// of times. HP and MP are not here — the server's health report carries the level-up's full heal within a
    /// tenth of a second, through the door every other health change uses.</summary>
    public void TakeXpFromServer(int newLevel, int xp, long lifetimeXp, int statPoints, int levelsGained,
                                 int ringLevel, int ringXp, int ringBankedPoints, int ringLevelsGained)
    {
        Ring.TakeFromServer(ringLevel, ringXp, ringBankedPoints, ringLevelsGained);

        level = Mathf.Max(1, newLevel);
        XP = Mathf.Max(0, xp);
        LifetimeXp = System.Math.Max(0, lifetimeXp);
        StatPoints = Mathf.Max(0, statPoints);
        for (int reached = level - levelsGained + 1; reached <= level; reached++)
            if (reached > 1) PlayLevelUp(reached);
        OnStatsChanged?.Invoke();
    }

    /// <summary>The moment of reaching <paramref name="reached"/>: the sting, the toast and the burst. Shared by
    /// the server's loop and the owner's machine so the two can never celebrate differently.</summary>
    void PlayLevelUp(int reached)
    {
        Sfx.Play(levelUpSfx);          // triumphant sting
        OnLeveledUp?.Invoke(reached);  // drives the level-up toast
        var lav = Avatar();
        if (lav != null) VfxCue.Fire(VfxCueId.OnLevelUp, lav);
    }

    /// <summary>Dev/testing: feed the ring XP directly (the <c>/ringxp</c> command), so the ring track can be
    /// exercised without grinding. Returns ring levels gained.</summary>
    public int AddRingXP(int amount) => Ring.AddXP(amount);

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

    /// <summary>Put <paramref name="count"/> of <paramref name="def"/> into this character's bag, and hand back the
    /// stack it landed in. Anything wearable adds one entry per copy, each rolling its own upgrade and cards,
    /// because two copies cannot share a stack without losing one of them; everything else merges into a stack
    /// already there.
    ///
    /// <para>THE one place a thing ARRIVES in a bag, and the reason the chat can say so without knowing what
    /// brought it. A server does this for whoever earned it, on the copy of them it holds; a game with no world
    /// does it through <see cref="InventoryStore.Give"/>, which calls this and then repaints the window.</para>
    ///
    /// <para>The only code that may add to <see cref="Bag"/> by hand is code moving a thing you ALREADY OWN — a
    /// piece coming off your body, a whole bag arriving from the server, a save being read. Those are not
    /// arrivals and must not be announced as any; every one of them says so where it does it.</para></summary>
    public BagEntry TakeIntoBag(Item def, int count)
    {
        if (def == null || count <= 0) return null;

        if (def.slot != EquipSlotType.None)
        {
            BagEntry last = null;
            for (int i = 0; i < count; i++)
                last = TakeIntoBag(EquipmentInstance.CreateFresh(def));
            return last;
        }

        var stack = bag.Find(x => x.def == def && !x.IsEquip);
        if (stack != null) stack.count += count;
        else bag.Add(stack = new BagEntry { def = def, count = count, isNew = true });
        Arrived(def, count, null);
        return stack;
    }

    /// <summary>Put a copy that has ALREADY been rolled into this character's bag — a thing picked up off the
    /// floor, a weapon a command built, a gift, a trade. Same door as the roll above, so an item that arrives
    /// this way is announced exactly like one that arrives any other way.
    ///
    /// <para>It exists because the callers that hold a finished copy used to add it to the list themselves, and
    /// every one of them was a way into the bag that nothing could see. There is one way in now.</para></summary>
    public BagEntry TakeIntoBag(EquipmentInstance ready)
    {
        if (ready == null || ready.def == null) return null;
        var entry = new BagEntry { def = ready.def, count = 1, isNew = true, instance = ready };
        bag.Add(entry);
        Arrived(ready.def, 1, ready);
        return entry;
    }

    /// <summary>SOMETHING JUST ARRIVED — say so, once, wherever it came from. Called from the two doors above and
    /// nowhere else, which is what makes the chat line source-agnostic: a way of getting an item that is written
    /// next year is announced without anybody remembering to announce it.
    ///
    /// <para>In a world the person this happened to is sitting at another machine, so the server asks theirs to
    /// say it. With no world there is one character and their chat is right here.</para>
    ///
    /// <para><b>Deliberately not called for a thing MOVING within what you already own</b> — taking a piece off
    /// your body, the piece a new one displaces, a bag arriving from the server, or a save being loaded. None of
    /// those is a thing arriving, and a line for each would fire on every equip and fill the log on every login.
    /// Those paths add to the list directly, and say why where they do it.</para></summary>
    void Arrived(Item def, int count, EquipmentInstance inst)
    {
        if (def == null || count <= 0) return;

        int folds = inst != null ? inst.upgrade : 0;
        if (GameNetwork.TellOwnerTheyGot(this, def.id, count, folds)) return;

        // A server holds several people and has no chat of its own; one that could not reach the owner above has
        // nobody to tell.
        if (GameServer.IsServer || this != Instance) return;
        ChatController.ItemArrived(def, count, folds);
    }

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
            lifetimeXp = LifetimeXp,
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
        Mastery.Capture(out d.masteryWeapons, out d.masteryPoints,
                        out d.masterySkillIds, out d.masterySkillUses, out d.learnedSkillIds);
        CaptureEquipment(d);
        CaptureBag(d);

        // The look, written into the save with everything else it changes alongside. One home, one write.
        d.modelName  = DyeModel;
        d.dyeSources = DyeSources;
        d.dyeTargets = DyeTargets;

        d.stamina = Stamina;
        CaptureCooldowns(d);
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
        var rolls = new List<string>();
        foreach (var e in bag)
        {
            if (e == null || e.def == null) continue;
            ids.Add(e.def.id);
            counts.Add(Mathf.Max(1, e.count));
            if (e.IsEquip && e.instance != null)
            {
                refines.Add(e.instance.upgrade);
                cards.Add(ItemCatalog.EncodeCards(e.instance.cards));
                rolls.Add(e.instance.EncodeRoll());
            }
            else { refines.Add(0); cards.Add(""); rolls.Add(""); }
        }
        d.bagItemIds = ids.ToArray();
        d.bagCounts = counts.ToArray();
        d.bagRefines = refines.ToArray();
        d.bagCards = cards.ToArray();
        d.bagRolls = rolls.ToArray();
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
                // FOLDS, NOT REFINES. A save written before crafting carries +N bought under the old rule, which
                // would leave a Raw piece folded past what a forge could ever fold it to — so it starts at none.
                int folds = d.version >= 13 && d.bagRefines != null && i < d.bagRefines.Length ? d.bagRefines[i] : 0;
                entry.count = 1;
                entry.instance = new EquipmentInstance(def, folds);
                if (d.bagCards != null && i < d.bagCards.Length)
                    ItemCatalog.DecodeCards(d.bagCards[i], entry.instance.cards);
                // v12+: the copy's grade and the stats written on it. A save older than that carries the old
                // rolled-armour text, which does not read as a grade — so the piece loads Raw with nothing
                // written, which is what a piece that has never been forged is.
                if (d.bagRolls != null && i < d.bagRolls.Length) entry.instance.DecodeRoll(d.bagRolls[i]);
                else entry.instance.Recompute();
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
            // NOTHING WEARABLE IS SEEDED. Gear is made, never handed out: a character's first weapon and first
            // armour are forged at a station, so a starter piece would be a second way to come by one.
            if (def.slot != EquipSlotType.None) continue;
            // AND NO MATERIAL EITHER. Materials are earned from creatures, band by band; the moment they became
            // items this line handed every new character one of each, Adamant and Sharp Ink included.
            if (def.materialKind != MaterialKind.None) continue;
            bag.Add(new BagEntry { def = def, count = def.IsUsable ? 5 : 1 });
        }
    }

    // Allocates a few small lists — fine, this runs only on save, not a hot path.
    /// <summary>What this character is WEARING, in the same arrays the save uses. Public so the wire can carry
    /// it beside the bag — equipping moves an item between the two in one decision, and sending only one half
    /// leaves the item apparently nowhere.</summary>
    public void CaptureWornInto(out int[] slots, out string[] ids, out int[] refines,
                                out string[] cards, out string[] rolls)
    {
        var d = new SaveData();
        CaptureEquipment(d);
        slots = d.equipSlots; ids = d.equipItemIds; refines = d.equipRefines;
        cards = d.equipCards; rolls = d.equipRolls;
    }

    /// <summary>Wear exactly this, replacing whatever was worn.</summary>
    public void RestoreWornFrom(int[] slots, string[] ids, int[] refines, string[] cards, string[] rolls)
    {
        if (slots == null || ids == null) return;
        // STAMPED CURRENT, because the restore reads the fold count only from a save of the current shape — and
        // this is the wire, which is always current. Left at zero, every worn piece arrived on the screen as +0.
        RestoreEquipment(new SaveData
        {
            version = SaveData.CurrentVersion,
            equipSlots = slots, equipItemIds = ids, equipRefines = refines,
            equipCards = cards, equipRolls = rolls,
        });
    }

    void CaptureEquipment(SaveData d)
    {
        var slots = new List<int>();
        var ids = new List<string>();
        var refines = new List<int>();
        var cards = new List<string>();
        var rolls = new List<string>();
        foreach (var kv in Gear.Worn)
        {
            var inst = kv.Value;
            slots.Add((int)kv.Key);
            ids.Add(inst.def.id);
            refines.Add(inst.upgrade);
            cards.Add(ItemCatalog.EncodeCards(inst.cards));
            rolls.Add(inst.EncodeRoll());
        }
        d.equipSlots   = slots.ToArray();
        d.equipItemIds = ids.ToArray();
        d.equipRefines = refines.ToArray();
        d.equipCards   = cards.ToArray();
        d.equipRolls    = rolls.ToArray();
    }

    // Rebuild the equip dictionary from the saved arrays.
    // Unknown item/card ids are dropped so a removed asset can't break a load.
    void RestoreEquipment(SaveData d)
    {
        Gear.Clear();
        if (d.equipSlots != null && d.equipItemIds != null)
        {
            for (int i = 0; i < d.equipSlots.Length && i < d.equipItemIds.Length; i++)
            {
                var slot = (EquipSlotType)d.equipSlots[i];
                var def = ItemCatalog.Resolve(d.equipItemIds[i]);
                if (def == null) continue;
                // The same rule the bag follows: a pre-crafting refine is not a fold.
                int folds = d.version >= 13 && d.equipRefines != null && i < d.equipRefines.Length ? d.equipRefines[i] : 0;
                var inst = new EquipmentInstance(def, folds);
                if (d.equipCards != null && i < d.equipCards.Length)
                    ItemCatalog.DecodeCards(d.equipCards[i], inst.cards);
                // v12+: the copy's grade and written stats. A save older than that carries the old rolled-armour
                // text, which does not read as a grade, so the piece loads Raw with nothing written on it.
                if (d.equipRolls != null && i < d.equipRolls.Length) inst.DecodeRoll(d.equipRolls[i]);
                else inst.Recompute();
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
        // v10+ carries the lifetime figure. Anything older never recorded one and 0 is a real answer, so the
        // VERSION decides rather than the value: below 10, it is worked out from the level curve — exactly what
        // a character who climbed there honestly would have been given.
        LifetimeXp = d.version >= 10
            ? System.Math.Max(0L, d.lifetimeXp)
            : UnitStatMath.XpEarnedByLevel(level) + XP;
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
        // MASTERY BEFORE THE BAR. Nothing here re-checks the bar against what was learned — a save's bar was
        // already checked when it was arranged — but what the character can USE is built from both, and reading
        // them in this order is what makes the first rebuild right.
        Mastery.Restore(d.masteryWeapons, d.masteryPoints, d.masterySkillIds, d.masterySkillUses, d.learnedSkillIds);
        // AND WHAT THE MASTERY NOW OPENS. A skill added to the game since this character last played, or one whose
        // level was retuned below where they already stand, is learned here rather than being unreachable.
        Mastery.LearnWhatIsOpen();
        // AND THE BAR IS HELD TO WHAT WAS LEARNED. A save arranged under an older rule — the weapon kit, which put
        // four skills on the bar that nobody had to learn — kept those keys on screen while the server refused
        // every press of them, and said nothing. The same test a bind command passes.
        KeepOnlyBindable(d.hotbarBindings);
        KeepOnlyBindable(d.activePassives);
        Hotbar.Restore(d.hotbarBindings);
        PassiveSkills.Restore(d.activePassives);
        RestoreEquipment(d);         // v6+: rebuild worn gear from the save
        RestoreBag(d);               // what they were carrying, or the starter kit on a first-ever run

        // v8+: the look comes back out of the save. A save written before v8 carries no body name — leave
        // whatever the character already wears rather than stripping it to nothing.
        if (!string.IsNullOrEmpty(d.modelName)) SetDye(d.dyeSources, d.dyeTargets, d.modelName);

        // HP/MP last, against the now-restored maxima. Never load into a dead state.
        CurrentHP = Mathf.Clamp(d.currentHP <= 0 ? MaxHP : d.currentHP, 1, MaxHP);
        CurrentMP = Mathf.Clamp(d.currentMP, 0, MaxMP);
        // Below zero means a save older than v8, which never recorded it — keep the full pool it starts with.
        SetStamina(d.stamina < 0f ? maxStamina : d.stamina);

        RestoreCooldowns(d);

        OnStatsChanged?.Invoke();
        OnHealthChanged?.Invoke();
        OnManaChanged?.Invoke();
        OnAppearanceChanged?.Invoke();   // apply the loaded outfit dye
    }

    // ---- cooldowns that outlive the process ------------------------------------------------------------

    // A skill this character has NOT cast has no runner at all, so it cannot be on cooldown and contributes
    // nothing. That is why the walk below skips nulls rather than building anything: RunnerAt makes a runner on
    // first use only, and forcing one here for every skill would undo the whole reason it is lazy.
    void CaptureCooldowns(SaveData d)
    {
        var runners = Skills.Runners;

        System.Collections.Generic.List<string> ids = null;
        System.Collections.Generic.List<double> due = null;
        for (int i = 0; i < runners.Length; i++)
        {
            var r = runners[i];
            if (r == null || !r) continue;
            if (r.def == null || string.IsNullOrEmpty(r.def.id)) continue;
            if (WorldClock.SecondsUntil(r.DueAtUtc) <= 0f) continue;

            (ids ??= new System.Collections.Generic.List<string>()).Add(r.def.id);
            (due ??= new System.Collections.Generic.List<double>()).Add(WorldClock.ToEpochSeconds(r.DueAtUtc));
        }

        d.cooldownSkillIds = ids?.ToArray();
        d.cooldownDueAt = due?.ToArray();
    }

    // HELD, NOT APPLIED. At the moment a save is restored the runners do not exist yet — they are built on first
    // use, and on a freshly built fighter there is nothing to write onto. So the moments wait here, and the one
    // place BOTH runner-creation paths pass through takes each one as it builds that skill.
    readonly System.Collections.Generic.Dictionary<string, System.DateTime> pendingCooldowns =
        new System.Collections.Generic.Dictionary<string, System.DateTime>();

    void RestoreCooldowns(SaveData d)
    {
        pendingCooldowns.Clear();
        if (d.cooldownSkillIds == null || d.cooldownDueAt == null) return;

        int n = Mathf.Min(d.cooldownSkillIds.Length, d.cooldownDueAt.Length);
        for (int i = 0; i < n; i++)
        {
            string id = d.cooldownSkillIds[i];
            if (string.IsNullOrEmpty(id)) continue;
            pendingCooldowns[id] = WorldClock.FromEpochSeconds(d.cooldownDueAt[i]);
        }
    }

    /// <summary>The moment this skill was still cooling down to when the character was written down, handed over
    /// once and then forgotten. Called only as a runner is built.</summary>
    public bool TakePendingCooldown(string skillId, out System.DateTime dueAtUtc)
    {
        dueAtUtc = default;
        if (string.IsNullOrEmpty(skillId) || pendingCooldowns.Count == 0) return false;
        if (!pendingCooldowns.TryGetValue(skillId, out dueAtUtc)) return false;
        pendingCooldowns.Remove(skillId);
        return true;
    }
}
