using System.Collections;
using UnityEngine;

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

    [Tooltip("FLEE (RO evasion). A physical attacker's HIT is rolled against this; when it loses, the hit " +
             "MISSes (red) and deals no damage. Magic can't be evaded. Higher = dodges more. See Combat.")]
    public int flee = 0;

    [Tooltip("Armor PROPERTY (RO element). An attack's element is checked against this via Elements.Modifier — " +
             "so a Fire attack on an Earth mob does extra, on a Water mob is resisted, etc. Neutral = no interaction.")]
    public Element element = Element.Neutral;

    [Header("Hit feedback")]
    public Color flashColor = Color.white;
    public float flashTime = 0.08f;

    [Tooltip("Invoked once, just before the GameObject is removed.")]
    public UnityEngine.Events.UnityEvent onDeath;

    public int Current { get; private set; }
    public bool IsDead => Current <= 0;

    // IDamageable seam (network-readiness): one type + one apply-entry a future net layer targets for any damageable
    // entity. Routes into the existing concrete sinks — no behavior change; consumed by the Step D authority seam.
    public Vector3 DamagePosition => transform.position;
    public int ApplyDamage(in DamageInfo info)
    {
        if (info.overkill)   // multi-hit flurry: keep numbers/sound climbing past the kill (RO cascade)
            return info.isMagic ? TakeMagicDamageOverkill(info.amount, info.isCrit, info.element)
                                : TakeDamageOverkill(info.amount, info.isCrit, info.element);
        return info.isMagic ? TakeMagicDamage(info.amount, info.isCrit, info.element)
                            : TakeDamage(info.amount, info.isCrit, info.attackerHit, info.element, info.missed);
    }
    public int ApplyTrueDamage(int amount, Color color) => TakeTrueDamage(amount, color);

    // ICombatUnit seam: the rest of the shared vocabulary an effect needs, so a skill effect targets a mob through the
    // SAME handle it targets the player through. Pure aliases onto this class's own fields — no second source of truth.
    public Transform Body => transform;
    public int CurrentHP => Current;
    public int MaxHP => maxHealth;

    /// <summary>How big this unit's BODY is, for melee-contact maths — read from its brain, which knows whether it is a
    /// character-sized shadow or a shrunken sprite mob. REPORTED rather than folded into a reach, so whoever measures
    /// against this unit blends the real pair of bodies instead of assuming what the other one is.</summary>
    public float MeleeScale { get { var b = Brain; return b != null ? b.MeleeScale : UnitScale.MobScale; } }

    /// <summary>This mob's FULL-SIZE melee reach — its worn weapon's range, or its unarmed reach. Unscaled on purpose:
    /// the caller blends it against the other body's size. Read through the shared WeaponReach rule, the same one the
    /// player uses, so arming a mob extends its reach exactly as arming the player does.</summary>
    public float MeleeReachFullSize
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
    public bool BasicIsMagic { get { var b = Brain; return b != null && b.BasicIsMagic; } }
    public int RollBasicDamage(bool crit) { var b = Brain; return b != null ? b.RollBasicDamage(crit) : 0; }

    /// <summary>Mobs never crit — crit is the Finesse identity, and the whole armour-piercing rule hangs off it.</summary>
    public bool RollBasicCrit(ICombatUnit victim) => false;

    /// <summary>Our dodge, rolled against an attacker's accuracy.</summary>
    public int Flee => flee;

    /// <summary>Our LUK, which lowers an attacker's crit chance against us.</summary>
    public int CritShieldLuk => luk;

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
    { var b = Brain; if (b != null) b.Taunt(seconds); }

    // The unified per-unit state store (buffs/debuffs) — single source for gameplay queries AND the nameplate icons.
    // Lazily created so every mob gets one without editing prefabs; cached after first touch (no per-hit GetComponent).
    UnitStates states;
    public UnitStates States => states != null ? states : (states = GetComponent<UnitStates>() ?? gameObject.AddComponent<UnitStates>());

    // Same shape and same reason as the store above: looked up once, then held.
    StatusController statusHost;
    public StatusController StatusHost => statusHost != null ? statusHost : (statusHost = Status.Of(gameObject));

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
    int Mitigate(float rawAfterDefence)
    {
        Vector3 popAt = UnitAnchor.Overhead(transform);
        return UnitMitigation.Deferred(States, UnitMitigation.AfterShield(States, UnitMitigation.Landed(States, rawAfterDefence), popAt), popAt);
    }

    // Parry — the SAME read the player gets (one primitive, no mob copy to hand-sync). Kept OUT of Mitigate on purpose:
    // Mitigate is math on a hit that lands, while a parry cancels the landing, so each intake bails before its damage
    // number and hit spark. Checked after the miss/immunity gates so a charge is only spent on a hit that would land.
    bool Parried() => UnitMitigation.TryParry(States, UnitAnchor.Overhead(transform));

    /// <summary>Mark this unit to TAKE +<paramref name="extraFraction"/> more damage (0.3 = +30%) for
    /// <paramref name="seconds"/>. Refreshes to the latest mark (newest-wins). Reusable armor-fray for skills like Hex Field.</summary>

    static readonly Color MissColor = new Color(1f, 0.3f, 0.3f);   // a player-dealt hit whiffed → red MISS
    static readonly Color CritColor = new Color(1f, 0.85f, 0.2f);  // a crit pops a gold number
    static readonly Color MagicColor = new Color(0.7f, 0.5f, 1f);  // magic damage is purple

    SpriteRenderer sr;
    Color baseColor;
    WaitForSeconds flashWait;   // cached so each hit's Flash() doesn't allocate a new yield instruction

    void Awake()
    {
        Current = maxHealth;
        sr = GetComponent<SpriteRenderer>();
        baseColor = sr.color;
        flashWait = new WaitForSeconds(flashTime);
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
                          bool missed = false)
    {
        if (IsDead) return 0;

        // Accuracy was already rolled by the ATTACKER (Combat.Missed) — we only PRESENT the result, never re-roll it:
        // the roll is random, so a second one could disagree with the number the attacker already acted on. The hit
        // came from the player, so a whiff shows a RED MISS — monster-dealt misses on the player are white
        // (see Character.TakeDamage).
        if (missed)
        {
            DamagePopup.SpawnText(UnitAnchor.Overhead(transform), "MISS", MissColor);
            CombatSfx.Miss(transform.position, PlayerRanged());   // whiff FROM the mob (spatial), melee/ranged by weapon
            return 0;
        }

        // Element property: the attack's element vs this unit's armor element scales the damage (RO chart).
        // Neutral-vs-Neutral is 1.0, so default callers are unaffected.
        float mod = Elements.Modifier(attackElement, element);
        if (mod <= 0f) { SpawnElementNumber(0, mod, Color.white); return 0; }
        if (Parried()) return 0;

        // Pre-renewal DEF split: hard-DEF % then flat soft DEF; a CRIT ignores both (and FLEE, above).
        int dmg = Mitigate(Combat.AfterDefense(attackerAttack, hardDefPercent, defense, crit) * mod);
        Current = Mathf.Max(0, Current - dmg);

        SpawnElementNumber(dmg, mod, crit ? CritColor : Color.white);
        // Impact FROM the mob (spatial). A basic attack (rolled accuracy, attackerHit ≥ 0) → hit/crit;
        // a skill strike (always-lands, attackerHit −1) → the unified skill impact.
        if (attackerHit >= 0)
        {
            CombatSfx.Hit(transform.position, crit);
            VfxCue.Fire(crit ? VfxCueId.OnHitByCriticalAttack : VfxCueId.OnHitByNormalAttack, transform);
        }
        else { CombatSfx.SkillHit(transform.position); VfxCue.Fire(VfxCueId.OnHitBySkill, transform); }
        if (isActiveAndEnabled) StartCoroutine(Flash());

        ApplyLeech(this, dmg);
        if (IsDead) Die();
        return dmg;
    }

    /// <summary>Apply a MAGIC hit, reduced by <see cref="magicDefense"/> instead of <see cref="defense"/>.
    /// Pops a purple number (gold on a crit). Returns the damage dealt.</summary>
    public int TakeMagicDamage(int attackerMagic, bool crit, Element attackElement = Element.Neutral)
    {
        if (IsDead) return 0;

        float mod = Elements.Modifier(attackElement, element);
        if (mod <= 0f) { SpawnElementNumber(0, mod, MagicColor); return 0; }
        if (Parried()) return 0;

        int dmg = Mitigate(Combat.AfterDefense(attackerMagic, hardMagicDefPercent, magicDefense, crit) * mod * Combat.MagicGlanceMult(flee));
        Current = Mathf.Max(0, Current - dmg);

        SpawnElementNumber(dmg, mod, crit ? CritColor : MagicColor);
        CombatSfx.SkillHit(transform.position);   // magic = skill impact, spatial from the mob
        VfxCue.Fire(VfxCueId.OnHitBySkill, transform);
        if (isActiveAndEnabled) StartCoroutine(Flash());

        ApplyLeech(this, dmg);
        if (IsDead) Die();
        return dmg;
    }

    /// <summary>Like <see cref="TakeMagicDamage"/> but ALWAYS registers a damage number — even when already
    /// dead. A multi-hit sequence keeps showing its remaining ticks (overkill) over a mob that died partway,
    /// the RO way. HP floors at 0; death fires exactly once (on the alive→dead transition). Returns the hit's
    /// value, so a combo total can keep accumulating past the kill.</summary>
    public int TakeMagicDamageOverkill(int attackerMagic, bool crit, Element attackElement = Element.Neutral)
    {
        float mod = Elements.Modifier(attackElement, element);
        if (mod <= 0f) { SpawnElementNumber(0, mod, MagicColor); return 0; }
        if (Parried()) return 0;

        int dmg = Mitigate(Combat.AfterDefense(attackerMagic, hardMagicDefPercent, magicDefense, crit) * mod * Combat.MagicGlanceMult(flee));
        bool wasAlive = !IsDead;
        Current = Mathf.Max(0, Current - dmg);
        SpawnElementNumber(dmg, mod, crit ? CritColor : MagicColor);
        CombatSfx.SkillHit(transform.position);   // overkill beats still sound (throttled per frame)
        // Hit spark + impact SOUND fire on EVERY strike, including overkill beats past death — the per-hit
        // multi-hit SFX must run to the end of the flurry, not cut off the instant the mob's HP hits 0.
        VfxCue.Fire(VfxCueId.OnHitBySkill, transform);
        if (wasAlive)
        {
            if (isActiveAndEnabled) StartCoroutine(Flash());
            ApplyLeech(this, dmg);
            if (IsDead) Die();
        }
        return dmg;
    }

    /// <summary>Like <see cref="TakeMagicDamageOverkill"/> but for a PHYSICAL skill strike: always registers the
    /// number + hit spark/sound even past death, so a physical multi-hit flurry keeps sounding to the end of the
    /// count instead of cutting off the instant the mob's HP hits 0. Always-lands (skill hit, no FLEE roll). HP
    /// floors at 0; death fires exactly once. Returns the hit's value so a combo total keeps climbing past the kill.</summary>
    public int TakeDamageOverkill(int attackerAttack, bool crit, Element attackElement = Element.Neutral)
    {
        float mod = Elements.Modifier(attackElement, element);
        if (mod <= 0f) { SpawnElementNumber(0, mod, Color.white); return 0; }
        if (Parried()) return 0;

        int dmg = Mitigate(Combat.AfterDefense(attackerAttack, hardDefPercent, defense, crit) * mod);
        bool wasAlive = !IsDead;
        Current = Mathf.Max(0, Current - dmg);
        SpawnElementNumber(dmg, mod, crit ? CritColor : Color.white);
        CombatSfx.SkillHit(transform.position);   // always-lands skill strike → the unified skill impact
        // Hit spark + impact SOUND on EVERY strike, incl. overkill beats past death — same as the magic path.
        VfxCue.Fire(VfxCueId.OnHitBySkill, transform);
        if (wasAlive)
        {
            if (isActiveAndEnabled) StartCoroutine(Flash());
            ApplyLeech(this, dmg);
            if (IsDead) Die();
        }
        return dmg;
    }

    /// <summary>Apply raw over-time / reflected damage that bypasses Defense and element (used by
    /// <see cref="StatusController"/> DoTs and damage-reflect). Pops a <paramref name="color"/>-tinted
    /// number, flashes, and dies exactly once. Does NOT leech (it isn't a player swing).</summary>
    public int TakeTrueDamage(int amount, Color color)
    {
        if (IsDead || amount <= 0) return 0;
        int dmg = Mathf.Max(1, amount);
        Current = Mathf.Max(0, Current - dmg);
        DamagePopup.Spawn(UnitAnchor.Overhead(transform), dmg, color);
        if (isActiveAndEnabled) StartCoroutine(Flash());
        if (IsDead) Die();
        return dmg;
    }

    // Element feedback IS the damage number's opacity now (no word cue): a resisted hit dims to 70%, an
    // immune hit to 30% (shown as a faded "0" since it deals none); strong/normal stay fully opaque.
    void SpawnElementNumber(int dmg, float mod, Color baseCol)
    {
        baseCol.a *= Elements.DamageAlpha(mod);
        if (dmg > 0) DamagePopup.Spawn(UnitAnchor.Overhead(transform), dmg, baseCol);
        else DamagePopup.SpawnText(UnitAnchor.Overhead(transform), "0", baseCol);
        // Every skill strike's white number is spawned HERE, whichever path rolled it — so this is also the one place a
        // run that shows its own total can hear about the hit, with nothing per-path to keep in step. Null unless a run
        // asked for the total, so the normal case costs one reference check.
        if (RepeatsDamageTotal.Current != null) RepeatsDamageTotal.Current.Report(transform, dmg);
    }

    // Lifesteal: a fraction of damage dealt to this mob heals the player (RO leech, the mechanical core of
    // a lifesteal card). Silent heal — a green "+N" per hit would spam during multi-hit; the HP bar is the cue.
    static void ApplyLeech(Health victim, int dmgDealt)
    {
        var ch = Character.Instance;
        if (ch == null || dmgDealt <= 0) return;
        // The ONE player-dealt-damage choke (basics + skills both funnel here) → also the OnDamageDealt fire-site (victim = the mob just hit).
        PassiveEvents.Fire(SkillTrigger.OnDamageDealt, null, victim != null ? victim.transform : null);
        if (ch.ActiveManaLeechPct > 0f) ch.GainMana(Mathf.RoundToInt(dmgDealt * ch.ActiveManaLeechPct));   // ManaLeech skill window (V3)

        // The VICTIM's own timed windows fire here too — the mob side of the rules the player already plays by, so a
        // Reflect or ManaLeech landed ON a mob actually does something. Reflect bounces TRUE damage (bypasses defense
        // and can't itself be reflected, exactly like Character.Reflect); ManaLeech feeds the mob's own MP pool.
        if (victim != null)
        {
            var vs = victim.States;
            if (vs.ReflectBonus > 0f)
            {
                int r = Mathf.RoundToInt(dmgDealt * vs.ReflectBonus);
                if (r > 0) CombatAuthority.ApplyTrue(ch, r, ReflectColor);
            }
            if (vs.ManaLeechPct > 0f)
            {
                var block = victim.GetComponent<UnitStatBlock>();
                if (block != null) block.GainMana(Mathf.RoundToInt(dmgDealt * vs.ManaLeechPct));
            }
        }

        if (ch.LifeStealPct <= 0f) return;
        ch.LeechHeal(Mathf.RoundToInt(dmgDealt * ch.LifeStealPct));
    }

    static readonly Color ReflectColor = new Color(1f, 0.6f, 0.9f);   // damage bounced back at the attacker

    // A Health MISS is always the player attacking a mob (a mob missing the PLAYER pops a white MISS in
    // Character, not here), so read the player's equipped weapon to pick the ranged vs melee whiff.
    static bool PlayerRanged()
    {
        var c = Character.Instance;
        if (c == null) return false;
        var w = c.GetEquipped(EquipSlotType.Weapon);
        return w != null && (w.weaponType == WeaponType.Bow || w.weaponType == WeaponType.Firearm);
    }

    /// <summary>Refill to full HP silently — no popup, no color reset (used on a mob LEVEL-UP, which fully
    /// heals like the player's, but shouldn't spam a giant green heal number or clear a hit flash).</summary>
    public void RefillToMax() => Current = maxHealth;

    /// <summary>Reset to full HP and base color (used by <see cref="MobRespawn"/> to bring a mob back).</summary>
    public void Revive()
    {
        Current = maxHealth;
        if (sr != null) sr.color = baseColor;

        // Coming back to life hands this unit's mover + animator back to the simulate-near switch, which stood down
        // while it was a corpse. Done HERE, at the one revive choke, rather than only at the respawn call site: several
        // paths revive a unit (the mob respawn, the training dummy's regen tick, the GM command), and enumerating them
        // is exactly how one gets missed and a unit is left permanently unmanaged. (MobRespawn ALSO resyncs after it
        // re-enables everything — a different beat: this lifts the stand-down, that re-asserts over the wholesale
        // enable. Both are needed; don't collapse them.)
        var lod = GetComponent<SimulationLod>(); if (lod != null) lod.Resync();
        StatusHost.ClearFlinch();   // a new life is not staggered, and does not inherit the last one's footing meter
        // A pooled mob comes back as the SAME object, so this is what tells anyone counting a run of hits that the
        // thing they were hitting is gone and this is somebody new.
        if (States != null) States.NoteNewLife();
    }

    /// <summary>Restore HP (capped at max) and pop a green "+N" over the unit.</summary>
    public void Heal(int amount)
    {
        if (amount <= 0 || IsDead) return;
        Current = Mathf.Min(maxHealth, Current + amount);
        // Show the green digits for the FULL intended amount, even at full HP — the heal still "landed" (player parity).
        DamagePopup.SpawnText(UnitAnchor.Overhead(transform), "+" + amount, new Color(0.45f, 1f, 0.45f));
        VfxCue.Fire(VfxCueId.OnReceiveHeal, transform);
    }

    IEnumerator Flash()
    {
        sr.color = flashColor;
        yield return flashWait;
        if (sr != null) sr.color = baseColor;
    }

    void Die()
    {
        // A mob died (mobs only die to the player here) — announce OnKill with the dying mob as victim NOW, before it's
        // pooled/destroyed below (FireAsPassive reads victim.position synchronously). The killing SKILL rides along
        // (empty for a basic attack or an ailment tick), so a passive can answer one particular skill's kills.
        PassiveEvents.Fire(SkillTrigger.OnKill, PassiveEvents.CurrentSkillId, transform);
        VfxCue.Fire(VfxCueId.OnDeath, transform);   // receiver-side death cue (mirrors Character.RaiseDeath)
        onDeath?.Invoke();
        // Play the "poof" death animation if one is attached, and once it finishes either RECYCLE the mob
        // (if it has a MobRespawn) or remove it (the original Phase D behavior). With no FX, do the same
        // immediately.
        // A pooled mob (owned by a FieldMobSpawner) returns to its pool instead of self-recycling in place
        // (MobRespawn) or being destroyed. Checked first so a spawner-owned mob always takes the pool path.
        var pooled = GetComponent<PooledMob>();
        var respawn = GetComponent<MobRespawn>();
        System.Action done;
        if (pooled != null) done = pooled.OnFadedOut;
        else if (respawn != null) done = respawn.OnFadedOut;
        else done = () => Destroy(gameObject);
        var fx = GetComponent<DeathDissolve>();
        if (fx != null && gameObject.activeInHierarchy) fx.Play(done);
        else done();
    }
}
