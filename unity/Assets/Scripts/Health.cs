using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Hit points + hit feedback + death, for anything that can be hurt. Phase D uses
/// it on monsters; the player can get one later for Phase F. Call
/// <see cref="TakeDamage"/> to damage it — it flashes, and when HP hits zero it
/// raises <see cref="onDeath"/> then removes the GameObject.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class Health : MonoBehaviour, ICombatUnit
{
    [Tooltip("Starting / maximum hit points.")]
    public int maxHealth = 3;

    [Tooltip("SOFT DEF (pre-renewal): a FLAT subtraction from incoming physical damage. Damage dealt is always ≥ 1.")]
    public int defense = 0;

    [Tooltip("SOFT MDEF (pre-renewal): a FLAT subtraction from incoming magic damage. Damage dealt is always ≥ 1.")]
    public int magicDefense = 0;

    [Tooltip("HARD DEF % (pre-renewal): a PERCENT reduction (0–100) on incoming physical damage, applied BEFORE the " +
             "flat soft 'defense'. A CRIT ignores it (and soft DEF, and FLEE). This is a tanky mob's main mitigation.")]
    [Range(0, 100)] public int hardDefPercent = 0;

    [Tooltip("HARD MDEF % (pre-renewal): a PERCENT reduction (0–100) on incoming magic damage, before the flat 'magicDefense'.")]
    [Range(0, 100)] public int hardMagicDefPercent = 0;

    [Tooltip("LUK — pre-renewal CRIT SHIELD: each point lowers an attacker's crit chance against this unit (0.3%/pt).")]
    public int luk = 0;

    [Tooltip("Health restored per second with no skill running — derived from VIT by UnitStatBlock, like every other " +
             "stat here. Authored directly only on a mob that carries no stat block.")]
    public float hpRegenPerSecond = 0f;
    public float HPRegenPerSecond => hpRegenPerSecond;

    [Tooltip("FLEE (RO evasion). A physical attacker's HIT is rolled against this; when it loses, the hit " +
             "MISSes (red) and deals no damage. Magic can't be evaded. Higher = dodges more. See Combat.")]
    public int flee = 0;

    [Tooltip("Armor PROPERTY (RO element). An attack's element is checked against this via Elements.Modifier — " +
             "so a Fire attack on an Earth mob does extra, on a Water mob is resisted, etc. Neutral = no interaction.")]
    public Element element = Element.Neutral;

    /// <summary>How strongly <see cref="element"/> resists and is weak, 1 to 4 — copied from the creature data by
    /// <see cref="MobSpawner"/>, the one door every creature is made through, exactly like <see cref="kind"/>.</summary>
    [System.NonSerialized] public int elementLevel = 1;

    [Tooltip("Invoked once, just before the GameObject is removed.")]
    public UnityEngine.Events.UnityEvent onDeath;

    /// <summary>Which kind of creature this is — copied from its creature data by <see cref="MobSpawner"/>, the one door
    /// every creature is made through, so it can never disagree with that data. Sets its extra skill slots, whether it
    /// summons, what it wears and what it drops (<see cref="CreatureKinds"/>).</summary>
    [System.NonSerialized] public CreatureKind kind = CreatureKind.Normal;

    /// <summary>Called up by another creature's summon skill, for this life. A summoned creature never summons, pays no
    /// experience and drops nothing, and it goes when its summoner goes (see <see cref="SummonedCreature"/>).</summary>
    public bool IsSummoned => summonedBy != null;
    [System.NonSerialized] SummonedCreature summonedBy;

    /// <summary>Mark this creature as <paramref name="by"/>'s summon for this life. Its skills are rebuilt, because a
    /// summoned creature carries no summon skill of its own.</summary>
    public void SetSummonedBy(SummonedCreature by)
    {
        summonedBy = by;
        extraVersion++;
    }

    /// <summary>Which side of the fight this creature stands on (see <see cref="Sides"/>). The creatures' side unless a
    /// saved character is put elsewhere; a watched copy is told by the server.</summary>
    [System.NonSerialized] public Side side = Side.Creatures;

    /// <summary>The character this creature turned back into, for this life — null for an ordinary creature.</summary>
    [System.NonSerialized] public SavedCharacter savedAs;

    /// <summary>Who this creature fights for, for this life: the player who saved it, when it is an ally. Its kills are
    /// that player's (see <see cref="Rewards.WhoEarned"/>).</summary>
    [System.NonSerialized] public ICombatUnit owner;

    public bool IsSavedCharacter => savedAs != null;

    /// <summary>Stand on <paramref name="now"/>. The one way a creature changes side, so the side registry never drifts.</summary>
    public void SetSide(Side now)
    {
        side = now;
        Sides.Filed(this, now);
    }

    /// <summary>Make this creature, for this life, the character <paramref name="character"/> — standing on
    /// <paramref name="standsOn"/> and fighting for <paramref name="fightsFor"/>. Its skills are rebuilt, because a saved
    /// character carries no summon skill of its own.</summary>
    public void BecomeSaved(SavedCharacter character, Side standsOn, ICombatUnit fightsFor)
    {
        savedAs = character;
        owner = fightsFor;
        SetSide(standsOn);
        extraVersion++;
    }

    void OnDestroy() => Sides.Filed(this, Side.None);

    public int Current { get; private set; }
    public bool IsDead => Current <= 0;

    // IDamageable seam (network-readiness): one type + one apply-entry a future net layer targets for any damageable
    // entity. Routes into the existing concrete sinks — no behavior change; consumed by the Step D authority seam.
    public Vector3 DamagePosition => transform.position;
    public int ApplyDamage(in DamageInfo info)
    {
        // A swing that missed is a miss wherever it came from — a basic-attack strike inside a flurry included — so it
        // shows MISS and deals nothing, instead of falling into the flurry sink, which has no miss.
        if (info.missed)
            return TakeDamage(info.amount, info.isCrit, info.attackerHit, info.element, info.missed, info.attacker);
        // ANY BLOW ON A CREATURE THAT IS ALREADY DEAD still shows — a flurry beat, a swing or an arrow that was on its way.
        // Presentation only: the digits and the hit cue, nothing else. (Damage over time comes through TakeTrueDamage,
        // which shows nothing on a dead creature.)
        if (IsDead) return ShowOverkillBeat(info);
        if (info.overkill)   // multi-hit flurry: keep numbers/sound climbing past the kill (RO cascade)
            return info.isMagic ? TakeMagicDamageOverkill(info.amount, info.isCrit, info.element, info.attacker)
                                : TakeDamageOverkill(info.amount, info.isCrit, info.attackerHit, info.element, info.attacker);
        return info.isMagic ? TakeMagicDamage(info.amount, info.isCrit, info.element, info.attacker)
                            : TakeDamage(info.amount, info.isCrit, info.attackerHit, info.element, info.missed,
                                         info.attacker);
    }
    public int ApplyTrueDamage(int amount, Color color) => TakeTrueDamage(amount, color);

    // ICombatUnit seam: the rest of the shared vocabulary an effect needs, so a skill effect targets a mob through the
    // SAME handle it targets the player through. Pure aliases onto this class's own fields — no second source of truth.
    public Transform Body => transform;
    public int CurrentHP => Current;
    public int MaxHP => maxHealth;

    /// <summary>The map this creature is in — simply the scene its body lives in, which is true on the machine that
    /// owns it and on every machine watching it, with nothing to keep in step.</summary>
    public string Map => gameObject.scene.name;

    /// <summary>This body's spawn_id — see <see cref="ICombatUnit.Spawn"/>. Kept through death and respawn, because
    /// a body that comes back is the same creature as far as the log is concerned.</summary>
    public SpawnTag Spawn { get; set; }

    /// <summary>How big this unit's BODY is, for melee-contact maths — read from its brain, which knows its size. A
    /// body with no brain counts as <see cref="UnitScale.MobScale"/>. REPORTED rather than folded into a reach, so whoever measures
    /// against this unit blends the real pair of bodies instead of assuming what the other one is.</summary>
    public float MeleeScale { get { var b = Brain; return b != null ? b.MeleeScale : UnitScale.MobScale; } }

    /// <summary>This mob's FULL-SIZE melee reach — its worn weapon's range, or its unarmed reach. Unscaled on purpose:
    /// the caller blends it against the other body's size. Read through the shared WeaponReach rule, the same one the
    /// player uses, so arming a mob extends its reach exactly as arming the player does.</summary>
    public float MeleeReachFullSize
    {
        get
        {
            // AND WHATEVER A SKILL IS LENDING, exactly as the player's does — a mob gets every skill a player gets, so
            // a reach bonus has to reach it here or the same buff would mean nothing on half the units in the world.
            float worn = EquippedReachFullSize;
            var st = States;
            return st != null ? Mathf.Max(0f, worn + st.SubStatBonus(SubStat.BasicAttackRange)) : worn;
        }
    }

    /// <summary>Gear alone, with nothing a skill is lending — see <see cref="ICombatUnit.EquippedReachFullSize"/>.</summary>
    public float EquippedReachFullSize
    {
        get
        {
            var b = Brain;
            return b != null ? WeaponReach.For(b.Equipment, b.touchRange) : 0.5f;   // no brain (the spike): sane default
        }
    }

    // The basic swing: forwarded to the brain, which owns this mob's attack numbers and already reads the same answers
    // at its own attack site — so a swing it walks up and makes and a swing a skill asks of it come from one place.
    public int BasicHit { get { var b = Brain; return b != null ? b.BasicHit : 0; } }
    public Element BasicElement { get { var b = Brain; return b != null ? b.BasicElement : Element.Neutral; } }
    public int RollBasicDamage(bool crit) { var b = Brain; return b != null ? b.RollBasicDamage(crit) : 0; }

    /// <summary>Whether this mob's swing crits — forwarded to the brain, which rolls it by the player's own rule.</summary>
    public bool RollBasicCrit(ICombatUnit victim) { var b = Brain; return b != null && b.RollBasicCrit(victim); }

    /// <summary>Our dodge, rolled against an attacker's accuracy.</summary>
    public int Flee => flee;

    /// <summary>Our LUK, which lowers an attacker's crit chance against us.</summary>
    public int CritShieldLuk => luk + States.WornCritShieldLuk;

    // A skill in this mob's hands: forwarded to the pieces that own each number, exactly as the swing above is.
    // Looked up once, found or not: the training dummy removes its stat block on purpose, and a miss must not turn
    // into a search on every skill hit.
    UnitStatBlock statBlock;
    bool statBlockLooked;
    UnitStatBlock StatBlock
    {
        get
        {
            if (!statBlockLooked) { statBlock = GetComponent<UnitStatBlock>(); statBlockLooked = true; }
            return statBlock;
        }
    }

    public float RingBonus(StatId stat) { var s = StatBlock; return s != null ? s.RingBonus(stat) : 0f; }

    /// <summary>What this creature's own practice adds to a skill's output. A creature presses nothing on purpose,
    /// so every skill it runs rides its WEAPON mastery — the same rule a player's passives follow, held to the same
    /// cap by the grade of what it is holding, and only for a skill that weapon actually owns.</summary>
    public float SkillOutputLift(string skillId)
    {
        var s = StatBlock;
        if (s == null) return 0f;
        var def = SkillCatalog.Get(skillId);
        float mastery = def != null && def.RidesWeaponMastery(HeldWeapon) ? MasteryMath.PassiveLift(s.UsableMastery) : 0f;
        return mastery + TrademarkLift(skillId);
    }

    /// <summary>What the weapon this creature holds adds to <paramref name="skillId"/> through its trademark — the
    /// same rule a player's weapon follows, so a creature carrying a trademark weapon is dangerous the same way.</summary>
    public float TrademarkLift(string skillId) => Trademarks.LiftOn(HeldWeaponCopy, skillId);

    /// <summary>The weapon this creature holds, as a forged copy — null when it holds none.</summary>
    public EquipmentInstance HeldWeaponCopy
    {
        get
        {
            var b = Brain;
            var gear = b != null ? b.Equipment : null;
            return gear != null ? gear.Get(EquipSlotType.Weapon) : null;
        }
    }

    public float SwingTime { get { var b = Brain; return b != null ? b.SwingTime : 0f; } }

    public int RollSkillDamage(DamageType type, int targetLuk, out bool crit)
    {
        var b = Brain;
        if (b == null) { crit = false; return 0; }
        return b.RollSkillDamage(type, targetLuk, out crit);
    }

    // Movement + aggro: the mob's half of the shared unit verbs, forwarded to the rig that owns each primitive (the
    // pathing agent moves, the brain holds aggro). Cached once — these run per cast, never per frame.
    PathAgent3D agent;
    MonsterAI3D brain;
    PathAgent3D Agent => agent != null ? agent : (agent = GetComponent<PathAgent3D>());
    MonsterAI3D Brain => brain != null ? brain : (brain = GetComponent<MonsterAI3D>());
    public void TeleportTo(Vector3 dest) { var a = Agent; if (a != null) a.Teleport(dest); else transform.position = dest; }
    public void GlideTo(Vector3 dest, float speedMult, bool easeIn, bool easeOut, float easeSeconds, Vector3? faceAt)
    {
        var a = Agent;
        if (a == null) return;
        // speedMult is a multiple of the unit's OWN run speed on both sides, so the same authored dash reads the same
        // relative to whoever performs it. ≤ 0 = the effect authored none → travel at plain run speed.
        a.GlideTo(dest, (speedMult > 0f ? speedMult : 1f) * a.moveSpeed, easeIn, easeOut, easeSeconds, faceAt);
    }
    public bool IsRelocating { get { var a = Agent; return a != null && a.Gliding; } }
    public void Shove(Vector3 worldFrom, float speed, float seconds, float stagger)
    {
        if (States != null && States.DisablesBlocked) return;   // a shove is a disable: the window turns it away
        var b = Brain; if (b != null) b.Knockback(worldFrom, speed, seconds, stagger);
    }
    public void ForceAggro(ICombatUnit source, float seconds)
    {
        if (seconds <= 0f) return;
        States.BeginTaunt(seconds, source);   // the WINDOW — shared with the player side, read by the skill door
        // …and the brain's own half: engage now rather than waiting to be provoked, reading that same window.
        var b = Brain; if (b != null) b.LockOnTaunter(States);
    }

    // The unified per-unit state store (buffs/debuffs) — single source for gameplay queries AND the nameplate icons.
    // Lazily created so every mob gets one without editing prefabs; cached after first touch (no per-hit GetComponent).
    UnitStates states;
    public UnitStates States => states != null ? states : (states = GetComponent<UnitStates>() ?? gameObject.AddComponent<UnitStates>());

    /// <summary>Who last cost this unit health — see <see cref="ICombatUnit.LastAttacker"/>.</summary>
    public ICombatUnit LastAttacker { get; private set; }
    public void NoteAttacker(ICombatUnit attacker) => LastAttacker = attacker;

    /// <summary>This creature's level — what decides how challenging a fight with it was.</summary>
    public int Level { get { var s = StatBlock; return s != null ? s.Level : 1; } }

    /// <summary>The grade of what this creature is holding — every piece a creature wears is Raw today, and it
    /// caps what its mastery is worth exactly as a player's weapon caps theirs.</summary>
    public Quality HeldQuality
    {
        get
        {
            var b = Brain;
            var gear = b != null ? b.Equipment : null;
            var weapon = gear != null ? gear.Get(EquipSlotType.Weapon) : null;
            return weapon != null ? weapon.quality : Quality.Raw;
        }
    }

    /// <summary>What this creature is holding.</summary>
    public WeaponType HeldWeapon
    {
        get
        {
            var b = Brain;
            var gear = b != null ? b.Equipment : null;
            return gear != null ? gear.WeaponType : WeaponType.None;
        }
    }

    // Same shape and same reason as the store above: looked up once, then held.
    StatusController statusHost;
    public StatusController StatusHost => statusHost != null ? statusHost : (statusHost = Status.Of(gameObject));

    /// <summary>The skills this creature casts — everything its WEAPON MASTERY has opened for the weapon in its
    /// hands, plus its extra skills (see <see cref="ExtraSkillIds"/>). The same rule a player follows: a skill
    /// belongs to a weapon and is known once that weapon is practised to its level. Rebuilt the moment either
    /// changes, which is every new life. Null on a watched copy, which decides nothing, and on anything that is not a creature the server thinks
    /// for.</summary>
    public UnitSkills Skills
    {
        get
        {
            var brain = Brain;
            if (brain == null || IsWatchedCopy || GameNetwork.TheWorldIsElsewhere || !Application.isPlaying) return null;
            var gear = brain.Equipment;
            if (gear == null) return null;
            if (!extraRolled) RollExtraSkillsForNewLife();
            int worn = gear.Version;
            if (worn != skillsBuiltFor || extraVersion != extraBuiltFor)
            {
                skillsBuiltFor = worn;
                extraBuiltFor = extraVersion;
                skills ??= new UnitSkills(this);
                skills.Bind(gameObject, KitOf(gear, extraSkillIds));
                // A NEW WEAPON IS A NEW LIFE, so nothing from the last one is still cooling down. Only a respawn
                // re-seats what a creature wears, and a respawn is a fresh creature.
                var runners = skills.Runners;
                for (int i = 0; i < runners.Length; i++)
                    if (runners[i] != null && runners[i]) runners[i].ForgetCooldown();
            }
            return skills;
        }
    }
    UnitSkills skills;
    int skillsBuiltFor = -1;

    /// <summary>This creature's EXTRA skills — slots of their own, apart from its weapon's kit, as many as its kind has
    /// (<see cref="CreatureKinds.ExtraSkillSlots"/>). Empty on a watched copy, which rolls nothing.</summary>
    public IReadOnlyList<string> ExtraSkillIds => extraSkillIds;
    readonly List<string> extraSkillIds = new List<string>(CreatureKinds.MostExtraSkillSlots);
    // A skill somebody named for this creature (a summon). Kept through every later life instead of a random roll.
    string pinnedExtraSkillId;
    bool extraRolled;
    int extraVersion;
    int extraBuiltFor = -1;

    /// <summary>Decide this life's extra skills: the named one if this creature was given one, otherwise a fresh
    /// random roll. Called by <c>MobRespawn.Reactivate</c> after the new weapon is seated, so the roll can skip what
    /// that weapon already carries; the first life rolls on its first look at <see cref="Skills"/>.</summary>
    public void RollExtraSkillsForNewLife()
    {
        if (IsWatchedCopy || GameNetwork.TheWorldIsElsewhere || !Application.isPlaying) return;
        extraRolled = true;
        // A training dummy is an instrument that never fights back, so it carries nothing extra — the same exclusion
        // its random gear has. perf-ok: once per life.
        if (GetComponent<TrainingDummy>() != null) extraSkillIds.Clear();
        else if (!string.IsNullOrEmpty(pinnedExtraSkillId))
        {
            extraSkillIds.Clear();
            extraSkillIds.Add(pinnedExtraSkillId);
        }
        else
        {
            ExtraSkillRoll.Roll(extraSkillIds, CreatureKinds.ExtraSkillSlots(kind));
        }
        extraVersion++;
    }

    /// <summary>Give this creature <paramref name="skillId"/> as its extra skill, for this life and every later one.
    /// Any skill in the catalog, published or not — the caller checks it exists.</summary>
    public void PinExtraSkill(string skillId)
    {
        pinnedExtraSkillId = skillId;
        RollExtraSkillsForNewLife();
    }

    // Reused: filled only when what the creature wears changes.
    static readonly System.Collections.Generic.List<SkillDef> kitScratch = new System.Collections.Generic.List<SkillDef>(9);

    // What its held weapon's mastery has opened, plus its extra skills, IN THE CATALOG'S ORDER — a skill's
    // position is its identity to the passive brake, so the list must not depend on where each came from. The same
    // rule a person's list keeps, and the same unlock table: a creature practised to level 30 with a sword knows
    // every sword skill up to 30, and one that has never picked a sword up knows none of them.
    System.Collections.Generic.List<SkillDef> KitOf(UnitEquipment gear, List<string> extra)
    {
        kitScratch.Clear();
        var held = gear != null ? gear.WeaponType : WeaponType.None;
        var block = StatBlock;
        int mastery = block != null ? block.UsableMastery : 0;
        var all = SkillCatalog.All;
        for (int i = 0; i < all.Count; i++)
        {
            var def = all[i];
            if (def == null || string.IsNullOrEmpty(def.id)) continue;
            bool byMastery = held != WeaponType.None && def.weaponType == held
                             && Mathf.Max(1, def.masteryLevel) <= mastery;
            // THE SUMMON SKILL, in a slot of its own on top of the extra ones — for a kind that summons, and never for a
            // creature that was itself summoned, or that turned back into a saved character.
            bool summon = def.id == CreatureKinds.SummonSkillId && CreatureKinds.Summons(kind) && !IsSummoned && savedAs == null;
            if (byMastery || summon || extra.Contains(def.id)) kitScratch.Add(def);
        }
        return kitScratch;
    }

    /// <summary>A creature's list is its weapon's kit plus its extra skills and nothing else, so every passive on it is
    /// live — the same rule that makes a person's weapon-granted passives always on.</summary>
    public bool PassiveIsOn(SkillDef def) => def != null;

    public float CastTimeMult => Block != null ? Block.CastTimeMult : 1f;

    public bool SpendMana(int amount) => Block != null ? Block.SpendMana(amount) : amount <= 0;

    // Mana lives on the stat block (it is derived from INT, like every other pool), so this side just forwards. A mob
    // with no stat block reports an empty pool, which is what stops its regen rather than a special case for it.
    UnitStatBlock block;
    UnitStatBlock Block => block != null ? block : (block = GetComponent<UnitStatBlock>());
    public int CurrentMP => Block != null ? Block.CurrentMP : 0;
    public int MaxMP => Block != null ? Block.MaxMP : 0;
    public float MPRegenPerSecond => Block != null ? Block.MPRegenPerSecond : 0f;
    public void GainMana(int amount) { var b = Block; if (b != null) b.GainMana(amount); }

    // Vulnerability mark (Hex Field): while active, every incoming hit is multiplied up. Now read from the unified
    // state store (V3 unified-state migration) — was a local vulnUntil/vulnMult timer.

    // Guard cut, then Shield absorb — the mitigation EVERY unit shares (see UnitMitigation), applied between the
    // defence roll and the HP subtraction. The states were always stored on a mob (Health.States is the same
    // UnitStates store the player uses); only this read was missing, so a Guard or Shield cast on a mob did nothing.
    // HIT-only, exactly like the player: the true-damage/DoT path below deliberately bypasses both.
    // One choke point so all four intakes (physical/magic × normal/overkill) can't drift apart.
    // Raw post-defence figure -> the damage that lands. UnitMitigation.Landed owns the vulnerability amplify, the
    // rounding and the guard cut (ONE place, so no intake can forget the debuff); the shield drain is layered after.
    // Brace is the LAST link (UnitMitigation.Deferred): what it banks is what would really have cost HP. A fully
    // deferred hit returns 0, the same "nothing got through" every intake already handles for a full Shield soak.
    // WHAT THE BARRIER AND THE BRACE TOOK is dropped here, because nothing yet carries a CREATURE's mitigation to a
    // watching machine — so a creature wearing a barrier still shows no number for what it soaked. The player's half
    // of the same gap is closed (PlayerHitResult.mitigated); this half needs the creature's announcement to carry the
    // same thing, which is a change to what crosses the wire rather than a line here.
    int Mitigate(float rawAfterDefence)
    {
        int landed = UnitMitigation.AfterShield(States, UnitMitigation.Landed(States, rawAfterDefence), out _);
        int dealt = UnitMitigation.Deferred(States, landed, out int banked);
        if (dealt <= 0) CombatAuthority.NoDamage(banked > 0 ? NoDamageReason.Braced : NoDamageReason.Soaked);
        return dealt;
    }

    // Parry — the SAME read the player gets, and now the same MOMENT too: the shared primitive spends the charge and
    // raises it, so a creature holding a counter-passive answers its own parry. Kept OUT of Mitigate on purpose:
    // Mitigate is math on a hit that lands, while a parry cancels the landing, so each intake bails before its damage
    // number and hit spark. Checked after the miss/immunity gates so a charge is only spent on a hit that would land.
    bool Parried(ICombatUnit attacker) => UnitMitigation.TryParry(this, attacker);

    /// <summary>Mark this unit to TAKE +<paramref name="extraFraction"/> more damage (0.3 = +30%) for
    /// <paramref name="seconds"/>. Refreshes to the latest mark (newest-wins). Reusable armor-fray for skills like Hex Field.</summary>

    static readonly Color MissColor = new Color(1f, 0.3f, 0.3f);   // a player-dealt hit whiffed → red MISS
    static readonly Color CritColor = new Color(1f, 0.85f, 0.2f);  // a crit pops a gold number
    static readonly Color MagicColor = new Color(0.7f, 0.5f, 1f);  // magic damage is purple


    void Awake()
    {
        Current = maxHealth;
    }

    /// <summary>Write in the health the SERVER says this creature has. Only ever called on a mob that is a
    /// watched copy of one the server owns — it does not take damage, it is told the answer.
    ///
    /// <para>Needed because everything that shows a mob's condition — its nameplate, its bar — reads
    /// <see cref="Current"/>. Without this the copy keeps the health it spawned with, so a mob being killed in
    /// front of you would sit at full health until it vanished.</para></summary>
    public void SetHealthFromServer(int current, int max)
    {
        if (max > 0) maxHealth = max;
        bool wasAlive = !IsDead;
        Current = Mathf.Clamp(current, 0, maxHealth);

        // THE ONLY THING THAT KILLS A WATCHED COPY. Writing the health straight in never ran the death, so a
        // creature dying in front of you simply blinked out of existence the instant the server let go of it —
        // no poof, no fade, nothing to tell you it had been killed rather than despawned.
        if (wasAlive && IsDead) PlayDeathVisuals();
    }

    /// <summary>True on a body that is only a WATCHED COPY of a creature the server owns. Such a copy still shows
    /// every blow in full — the number and the impact — but it never decides that the creature DIED. That
    /// answer arrives from the server, like every other fact about it.</summary>
    public bool IsWatchedCopy { get; private set; }

    /// <summary>Mark this body as a watched copy of one the server owns. Said once, when the copy is built.</summary>
    public void MarkAsWatchedCopy() => IsWatchedCopy = true;

    /// <summary>Take hit points off — the ONE place a blow lowers this unit's health, so the rule about who is
    /// allowed to do that is written once instead of at five separate sinks.
    ///
    /// <para><b>A watched copy does not do this at all.</b> It still shows the whole blow — the number and
    /// the impact — but its health is the server's to state, and it arrives already correct. Letting the copy
    /// subtract as well means two answers to one question: the copy could reach zero a moment early, count itself
    /// dead, then be corrected back to alive, and the real death — when it came — would find a body that already
    /// thought it was gone and so would play no death at all.</para></summary>
    void SubtractHealth(int dmg)
    {
        if (IsWatchedCopy) return;
        Current = Mathf.Max(0, Current - dmg);
    }

    /// <summary>Play what a death LOOKS like, with none of what a death MEANS. A watched copy must not announce a
    /// kill — the server already did that, once, on behalf of everybody — and must not recycle or bank itself,
    /// because the shell that speaks for it is what removes it when the server says it is gone.</summary>
    void PlayDeathVisuals()
    {
        VfxCue.Fire(VfxCueId.OnDeath, transform);
        var fx = GetComponent<DeathDissolve>();
        if (fx != null && gameObject.activeInHierarchy) fx.Play(() => { });
    }

    // What this unit wears, looked up once on the first blow and never again — found or not — because the lookup would
    // otherwise run on every hit this unit takes.
    UnitEquipment wornGear;
    bool wornGearLooked;

    /// <summary>A worn shield stopping this physical blow outright — the same roll a player's shield makes
    /// (<see cref="Combat.Blocked"/>), on the same blows. A watched copy tosses no coin: the server already did.</summary>
    bool BlockedByShield()
    {
        if (IsWatchedCopy) return false;
        if (!wornGearLooked) { wornGear = GetComponent<UnitEquipment>(); wornGearLooked = true; }
        var shield = wornGear != null ? wornGear.Get(EquipSlotType.Shield) : null;
        if (shield == null || !Combat.Blocked(shield.def, States.WornBlockChance)) return false;
        if (shield.def.guardSfx != null && !shield.def.guardSfx.Empty) Sfx.Play(shield.def.guardSfx);
        return true;
    }

    /// <summary>Apply a hit; final damage = max(1, attackerAttack − defense). Returns the damage dealt.</summary>
    public int TakeDamage(int attackerAttack) => TakeDamage(attackerAttack, false, -1);

    /// <summary>As <see cref="TakeDamage(int)"/>, but a <paramref name="crit"/> hit pops a distinct
    /// gold number so the player can see the crit land.</summary>
    public int TakeDamage(int attackerAttack, bool crit) => TakeDamage(attackerAttack, crit, -1);

    /// <summary>As above, but carrying the attack's already-resolved accuracy: <paramref name="missed"/> true pops a
    /// RED MISS and deals nothing. The attacker owns that roll (<see cref="Combat.Missed"/>) so it happens once and
    /// so a whiff never pays for a damage roll. Red here = player-dealt; monster-dealt misses are white (Character).</summary>
    public int TakeDamage(int attackerAttack, bool crit, int attackerHit, Element attackElement = Element.Neutral,
                          bool missed = false, ICombatUnit attacker = null)
    {
        if (IsDead) return CombatAuthority.NoDamage(NoDamageReason.AlreadyDead);

        // Accuracy was already rolled by the ATTACKER (Combat.Missed) — we only PRESENT the result, never re-roll it:
        // the roll is random, so a second one could disagree with the number the attacker already acted on. The hit
        // came from the player, so a whiff shows a RED MISS — monster-dealt misses on the player are white
        // (see Character.TakeDamage).
        if (missed)
        {
            Show(ShownBlow.Miss(), attacker);
            return CombatAuthority.NoDamage(NoDamageReason.Missed);
        }
        if (BlockedByShield()) return CombatAuthority.NoDamage(NoDamageReason.Guarded);

        // Element property: the attack's element vs this unit's armor element scales the damage (RO chart).
        // Neutral-vs-Neutral is 1.0, so default callers are unaffected.
        float mod = ElementModifier(attackElement);
        if (mod < 0f)
            return HealedByElement(Combat.AfterDefense(attackerAttack, hardDefPercent, defense, crit), mod, false, attacker);
        if (mod == 0f)
        {
            Show(ShownBlow.Immune(mod, crit: false, magic: false), attacker);
            return CombatAuthority.NoDamage(NoDamageReason.Immune);
        }
        if (Parried(attacker)) return CombatAuthority.NoDamage(NoDamageReason.Parried);

        // Pre-renewal DEF split: hard-DEF % then flat soft DEF; a CRIT ignores both (and FLEE, above).
        int dmg = Mitigate(Combat.AfterDefense(attackerAttack, hardDefPercent, defense, crit) * mod);
        int before = Current;
        SubtractHealth(dmg);
        PassiveEvents.HitLanded(this, before, attacker);

        // Impact FROM the mob (spatial). A basic attack (rolled accuracy, attackerHit ≥ 0) → hit/crit;
        // a skill strike (always-lands, attackerHit −1) → the unified skill impact.
        Show(new ShownBlow(dmg, mod, crit, magic: false, flash: true), attacker);
        VfxCue.Fire(VfxCue.HitCue(false, crit, attackerHit), transform);

        ApplyLeech(this, dmg, attacker);
        if (IsDead) Die();
        return dmg;
    }

    /// <summary>Apply a MAGIC hit, reduced by <see cref="magicDefense"/> instead of <see cref="defense"/>.
    /// Pops a purple number (gold on a crit). Returns the damage dealt.</summary>
    public int TakeMagicDamage(int attackerMagic, bool crit, Element attackElement = Element.Neutral,
                               ICombatUnit attacker = null)
    {
        if (IsDead) return CombatAuthority.NoDamage(NoDamageReason.AlreadyDead);

        float mod = ElementModifier(attackElement);
        if (mod < 0f)
            return HealedByElement(Combat.AfterDefense(attackerMagic, hardMagicDefPercent, magicDefense, crit), mod, true, attacker);
        if (mod == 0f)
        {
            Show(ShownBlow.Immune(mod, crit: false, magic: true), attacker);
            return CombatAuthority.NoDamage(NoDamageReason.Immune);
        }
        if (Parried(attacker)) return CombatAuthority.NoDamage(NoDamageReason.Parried);

        int dmg = Mitigate(Combat.AfterDefense(attackerMagic, hardMagicDefPercent, magicDefense, crit) * mod);
        int before = Current;
        SubtractHealth(dmg);
        PassiveEvents.HitLanded(this, before, attacker);

        // magic = skill impact, spatial from the mob
        Show(new ShownBlow(dmg, mod, crit, magic: true, flash: true), attacker);
        VfxCue.Fire(VfxCueId.OnHitBySkill, transform);

        ApplyLeech(this, dmg, attacker);
        if (IsDead) Die();
        return dmg;
    }

    /// <summary>Like <see cref="TakeMagicDamage"/> but ALWAYS registers a damage number — even when already
    /// dead. A multi-hit sequence keeps showing its remaining ticks (overkill) over a mob that died partway,
    /// the RO way. HP floors at 0; death fires exactly once (on the alive→dead transition). Returns the hit's
    /// value, so a combo total can keep accumulating past the kill.</summary>
    public int TakeMagicDamageOverkill(int attackerMagic, bool crit, Element attackElement = Element.Neutral,
                                       ICombatUnit attacker = null)
    {
        float mod = ElementModifier(attackElement);
        if (mod < 0f)
            return HealedByElement(Combat.AfterDefense(attackerMagic, hardMagicDefPercent, magicDefense, crit), mod, true, attacker);
        if (mod == 0f)
        {
            Show(ShownBlow.Immune(mod, crit: false, magic: true), attacker);
            return CombatAuthority.NoDamage(NoDamageReason.Immune);
        }
        if (Parried(attacker)) return CombatAuthority.NoDamage(NoDamageReason.Parried);

        int dmg = Mitigate(Combat.AfterDefense(attackerMagic, hardMagicDefPercent, magicDefense, crit) * mod);
        bool wasAlive = !IsDead;
        int before = Current;
        SubtractHealth(dmg);
        if (wasAlive) PassiveEvents.HitLanded(this, before, attacker);
        // Hit spark + impact SOUND fire on EVERY strike, including overkill beats past death — the per-hit
        // multi-hit SFX must run to the end of the flurry, not cut off the instant the mob's HP hits 0.
        Show(new ShownBlow(dmg, mod, crit, magic: true, flash: wasAlive), attacker);
        VfxCue.Fire(VfxCueId.OnHitBySkill, transform);
        if (wasAlive)
        {
            ApplyLeech(this, dmg, attacker);
            if (IsDead) Die();
        }
        return dmg;
    }

    /// <summary>Like <see cref="TakeMagicDamageOverkill"/> but for a PHYSICAL skill strike: always registers the
    /// number + hit spark/sound even past death, so a physical multi-hit flurry keeps sounding to the end of the
    /// count instead of cutting off the instant the mob's HP hits 0. Always-lands (skill hit, no FLEE roll). HP
    /// floors at 0; death fires exactly once. Returns the hit's value so a combo total keeps climbing past the kill.</summary>
    public int TakeDamageOverkill(int attackerAttack, bool crit, int attackerHit, Element attackElement = Element.Neutral,
                                  ICombatUnit attacker = null)
    {
        if (BlockedByShield()) return CombatAuthority.NoDamage(NoDamageReason.Guarded);
        float mod = ElementModifier(attackElement);
        if (mod < 0f)
            return HealedByElement(Combat.AfterDefense(attackerAttack, hardDefPercent, defense, crit), mod, false, attacker);
        if (mod == 0f)
        {
            Show(ShownBlow.Immune(mod, crit: false, magic: false), attacker);
            return CombatAuthority.NoDamage(NoDamageReason.Immune);
        }
        if (Parried(attacker)) return CombatAuthority.NoDamage(NoDamageReason.Parried);

        int dmg = Mitigate(Combat.AfterDefense(attackerAttack, hardDefPercent, defense, crit) * mod);
        bool wasAlive = !IsDead;
        int before = Current;
        SubtractHealth(dmg);
        if (wasAlive) PassiveEvents.HitLanded(this, before, attacker);
        // Always-lands skill strike → the unified skill impact. Hit spark + impact SOUND on EVERY strike, incl. overkill
        // beats past death — same as the magic path. The cue is chosen by the shared rule, so a basic-attack strike
        // inside a flurry shows the normal or crit cue a player would.
        Show(new ShownBlow(dmg, mod, crit, magic: false, flash: wasAlive), attacker);
        VfxCue.Fire(VfxCue.HitCue(false, crit, attackerHit), transform);
        if (wasAlive)
        {
            ApplyLeech(this, dmg, attacker);
            if (IsDead) Die();
        }
        return dmg;
    }

    /// <summary>The element chart's answer for a hit of <paramref name="attack"/> on this creature as it defends right
    /// now — an armour converter's element while one runs, its own element and level otherwise.</summary>
    float ElementModifier(Element attack)
    {
        Element defending = States.DefendingElementOr(element, elementLevel, out int level);
        return Elements.Warded(Elements.Modifier(attack, defending, level), States.WardAgainst(attack));
    }

    /// <summary>A hit this creature's element turns into a heal: it gains what the hit would have dealt after defence,
    /// and NOTHING a landed hit sets off happens — no reflect, no leech, no passive, no flinch, no damage number. A
    /// creature already dead has no health to gain, so it shows the faded "0" an immune hit shows.</summary>
    int HealedByElement(float afterDefence, float mod, bool magic, ICombatUnit attacker)
    {
        if (IsDead)
        {
            Show(ShownBlow.Immune(0f, crit: false, magic: magic), attacker);
            return CombatAuthority.NoDamage(NoDamageReason.Immune);
        }
        Heal(Elements.ElementHeal(afterDefence, mod));
        return CombatAuthority.NoDamage(NoDamageReason.HealedByElement);
    }

    /// <summary>A flurry beat on a creature that is already dead: the number that beat would have been, and the hit
    /// cue — and NOTHING else. No health lost, no parry or barrier spent, no leech, no passive, no death.</summary>
    int ShowOverkillBeat(in DamageInfo info)
    {
        // Immune AND would-heal both draw the faded "0" here: a creature already dead has no health to gain.
        float mod = ElementModifier(info.element);
        if (mod <= 0f)
        {
            Show(ShownBlow.Immune(mod, info.isCrit, info.isMagic), info.attacker);
            return CombatAuthority.NoDamage(NoDamageReason.Immune);
        }
        float afterDefence = info.isMagic
            ? Combat.AfterDefense(info.amount, hardMagicDefPercent, magicDefense, info.isCrit)
            : Combat.AfterDefense(info.amount, hardDefPercent, defense, info.isCrit);
        int shown = Mathf.Max(1, Mathf.RoundToInt(afterDefence * mod));
        // The same impact sound and cue a live beat plays, so a flurry sounds to the end of its count.
        Show(new ShownBlow(shown, mod, info.isCrit, info.isMagic, flash: false), info.attacker);
        VfxCue.Fire(VfxCue.HitCue(info.isMagic, info.isCrit, info.attackerHit), transform);
        return shown;
    }

    /// <summary>Apply raw over-time / reflected damage that bypasses Defense and element (used by
    /// <see cref="StatusController"/> DoTs and damage-reflect). Pops a <paramref name="color"/>-tinted
    /// number and dies exactly once. Does NOT leech (it isn't a player swing).</summary>
    public int TakeTrueDamage(int amount, Color color)
    {
        if (IsDead || amount <= 0) return 0;
        int dmg = Mathf.Max(1, amount);
        SubtractHealth(dmg);
        DamagePopup.Spawn(transform, dmg, color);
        if (IsDead) Die();
        return dmg;
    }

    // EVERY BLOW THIS UNIT SHOWS PASSES HERE — a hit, an immunity, a whiff, a flurry beat past death — on the machine
    // that decided it. It draws the blow and, on the server, tells every watching machine exactly what was drawn, AT THE
    // MOMENT it was drawn: announced any later, a reaction to this hit (a passive, a leech) could land a second blow on
    // the same unit in between, and the two announcements would describe each other's numbers.
    void Show(in ShownBlow blow, ICombatUnit attacker)
    {
        DrawShownBlow(blow);
        // Every skill strike's number is shown HERE, whichever path rolled it — so this is also the one place a run that
        // shows its own total can hear about the hit, with nothing per-path to keep in step. Null unless a run asked for
        // the total, so the normal case costs one reference check.
        if (!blow.missed && RepeatsDamageTotal.Current != null) RepeatsDamageTotal.Current.Report(transform, blow.number);
        GameNetwork.AnnounceBlowOnMob(this, attacker, blow);
    }

    /// <summary>Put a blow on screen exactly as it was decided — the digits and their colour — with no sums: every
    /// number in <paramref name="blow"/> is already final. The server's own intake and every watching machine draw
    /// through this one function, so one blow cannot be shown two ways.
    ///
    /// <para>Element feedback IS the number's opacity (no word cue): a resisted hit dims, an immune hit is a faded "0",
    /// strong and normal hits stay fully opaque.</para></summary>
    public void DrawShownBlow(in ShownBlow blow)
    {
        if (blow.missed)
        {
            DamagePopup.SpawnText(transform, "MISS", MissColor);
            return;
        }

        Color colour = blow.crit ? CritColor : blow.magic ? MagicColor : Color.white;
        colour.a *= Elements.DamageAlpha(blow.elementModifier);
        if (blow.number > 0) DamagePopup.Spawn(transform, blow.number, colour);
        else DamagePopup.SpawnText(transform, "0", colour);
    }

    // WHAT ONE LANDED BLOW PAYS THE ATTACKER, and what it costs them from the victim's own windows: the moment
    // "I dealt damage", life steal, mana leech, and the victim's reflect.
    //
    // ASKED OF THE UNIT, NEVER OF A PERSON. This whole block used to begin by casting the attacker to Character and
    // giving up when it was not one — so a creature that dealt damage raised no moment, drank no life steal and
    // leeched no mana, and every passive a creature could hold that answers a landed blow was dead on every creature
    // in the world. Nothing here needs to know which kind of thing swung.
    static void ApplyLeech(Health victim, int dmgDealt, ICombatUnit attacker)
    {
        if (PlayerUnits.Gone(attacker) || dmgDealt <= 0) return;
        // The ONE dealt-damage choke (basics and skills both funnel here) → also the OnDamageDealt fire-site
        // (victim = whatever was just hit).
        PassiveEvents.Fire(attacker, SkillTrigger.OnDamageDealt, null, victim != null ? victim.transform : null);
        // ManaLeech skill window (V3)
        float manaLeech = attacker.States != null ? attacker.States.ManaLeechPct : 0f;
        if (manaLeech > 0f) attacker.GainMana(Mathf.RoundToInt(dmgDealt * manaLeech));

        // The VICTIM's own timed windows fire here too, so a Reflect or ManaLeech landed ON this creature actually
        // does something. Reflect bounces TRUE damage (bypasses defence and can't itself be reflected); ManaLeech
        // feeds the victim's own MP pool.
        if (victim != null)
        {
            var vs = victim.States;
            if (vs.ReflectBonus > 0f)
            {
                int r = Mathf.RoundToInt(dmgDealt * vs.ReflectBonus);
                if (r > 0) CombatAuthority.ApplyTrue(attacker, r, ReflectColor, "reflect");
            }
            if (vs.ManaLeechPct > 0f) victim.GainMana(Mathf.RoundToInt(dmgDealt * vs.ManaLeechPct));
        }

        float steal = attacker.LifeStealPct;
        if (steal <= 0f) return;
        attacker.LeechHeal(Mathf.RoundToInt(dmgDealt * steal));
    }

    static readonly Color ReflectColor = new Color(1f, 0.6f, 0.9f);   // damage bounced back at the attacker

    /// <summary>Refill to full HP silently — no popup, no color reset (used on a mob LEVEL-UP, which fully
    /// heals like the player's, but shouldn't spam a giant green heal number).</summary>
    public void RefillToMax() => Current = maxHealth;

    /// <summary>Reset to full HP (used by <see cref="MobRespawn"/> to bring a mob back).</summary>
    public void Revive()
    {
        Current = maxHealth;

        // Coming back to life hands this unit's mover + animator back to the simulate-near switch, which stood down
        // while it was a corpse. Done HERE, at the one revive choke, rather than only at the respawn call site: several
        // paths revive a unit (the mob respawn, the training dummy's regen tick, the GM command), and enumerating them
        // is exactly how one gets missed and a unit is left permanently unmanaged. (MobRespawn ALSO resyncs after it
        // re-enables everything — a different beat: this lifts the stand-down, that re-asserts over the wholesale
        // enable. Both are needed; don't collapse them.)
        var lod = GetComponent<SimulationLod>(); if (lod != null) lod.Resync();
        ((ICombatUnit)this).BeginNewLife();   // the shared half — nothing timed carries across a death, for any unit
    }

    /// <summary>Restore HP (capped at max) and announce the gain.</summary>
    public void Heal(int amount)
    {
        if (amount <= 0 || IsDead) return;
        // A watched copy's health is the server's to state, gains included — the same rule as SubtractHealth.
        if (IsWatchedCopy) return;
        Current = Mathf.Min(maxHealth, Current + amount);
        CombatAuthority.NoteHPAdded(this, amount);
    }

    /// <summary>This creature's life steal — whatever a skill window is lending it. It has no gear affix for one, so
    /// with no window running this is zero; the READ is here so a creature that is given one is paid exactly as a
    /// person is.</summary>
    public float LifeStealPct => Mathf.Max(0f, States.SubStatBonus(SubStat.LifeSteal));

    /// <summary>Health returned by life steal — silent, and refused on a watched copy for the same reason every other
    /// gain is: this creature's health is the server's to state.</summary>
    public void LeechHeal(int amount)
    {
        if (amount <= 0 || IsDead || IsWatchedCopy) return;
        Current = Mathf.Min(maxHealth, Current + amount);
        CombatAuthority.LogHeal(this, amount, "life steal");
    }

    void Die()
    {
        // A WATCHED COPY never gets to decide this. Its death is played from the health the server sends
        // (see SetHealthFromServer). Deciding it here as well would announce a kill on every machine that happened
        // to be looking, and dismantle a body the shell speaking for it still owns.
        if (IsWatchedCopy) return;
        CombatAuthority.LogDeath(this);

        // A mob died (mobs only die to the player here) — announce OnKill with the dying mob as victim NOW, before it's
        // pooled/destroyed below (FireAsPassive reads victim.position synchronously). The killing SKILL rides along
        // (empty for a basic attack or an ailment tick), so a passive can answer one particular skill's kills.
        PassiveEvents.Fire(LastAttacker, SkillTrigger.OnKill, PassiveEvents.CurrentSkillId, transform);
        VfxCue.Fire(VfxCueId.OnDeath, transform);   // receiver-side death cue (mirrors Character.RaiseDeath)
        onDeath?.Invoke();
        // Play the "poof" death animation if one is attached, and once it finishes either RECYCLE the mob
        // (if it has a MobRespawn) or remove it (the original Phase D behavior). With no FX, do the same
        // immediately.
        // A pooled mob (owned by a FieldMobSpawner) returns to its pool instead of self-recycling in place
        // (MobRespawn) or being destroyed. Checked first so a spawner-owned mob always takes the pool path.
        // A SUMMON goes back to its summoner, first of all — it never respawns by itself.
        var pooled = GetComponent<PooledMob>();   // perf-ok: once, at the moment a unit dies
        var respawn = GetComponent<MobRespawn>();   // perf-ok: once, at the moment a unit dies
        System.Action done;
        if (summonedBy != null) done = summonedBy.OnFadedOut;
        else if (pooled != null) done = pooled.OnFadedOut;
        else if (respawn != null) done = respawn.OnFadedOut;
        else done = () => Destroy(gameObject);
        var fx = GetComponent<DeathDissolve>();   // perf-ok: once, at the moment a unit dies
        if (fx != null && gameObject.activeInHierarchy) fx.Play(done);
        else done();
    }
}

/// <summary>
/// A blow on a creature EXACTLY AS IT WAS PUT ON SCREEN, with every number already final — what the server's own intake
/// draws, and what it tells every watching machine to draw. Nothing in it is an input to a sum: a watching machine that
/// holds one of these has nothing left to work out, which is the whole reason it exists.
/// </summary>
public readonly struct ShownBlow
{
    /// <summary>The digits shown. 0 is the faded "0" of an immune hit, or of a hit a barrier ate whole.</summary>
    public readonly int number;
    /// <summary>The element chart's answer for this blow. Only its opacity is read from it.</summary>
    public readonly float elementModifier;
    /// <summary>Coloured as a critical hit.</summary>
    public readonly bool crit;
    /// <summary>Coloured as magic.</summary>
    public readonly bool magic;
    /// <summary>A whiff: "MISS" instead of a number.</summary>
    public readonly bool missed;
    /// <summary>The creature was alive to be hit, so it staggers — a flurry beat past its death does not.</summary>
    public readonly bool flash;

    public ShownBlow(int number, float elementModifier, bool crit, bool magic, bool flash, bool missed = false)
    {
        this.number = number;
        this.elementModifier = elementModifier;
        this.crit = crit;
        this.magic = magic;
        this.flash = flash;
        this.missed = missed;
    }

    public static ShownBlow Miss() =>
        new ShownBlow(0, 1f, false, false, false, missed: true);

    public static ShownBlow Immune(float elementModifier, bool crit, bool magic) =>
        new ShownBlow(0, elementModifier, crit, magic, false);
}
