using System.Collections;
using UnityEngine;

/// <summary>
/// Hit points + hit feedback + death, for anything that can be hurt. Phase D uses
/// it on monsters; the player can get one later for Phase F. Call
/// <see cref="TakeDamage"/> to damage it — it flashes, and when HP hits zero it
/// raises <see cref="onDeath"/> then removes the GameObject.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class Health : MonoBehaviour
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

    /// <summary>As above, but with the attacker's RO <paramref name="attackerHit"/> (≥ 0) rolled against
    /// our <see cref="flee"/> first — a non-crit miss pops a RED MISS and deals no damage. Pass −1 to skip
    /// the accuracy roll (always lands). Red here = player-dealt; monster-dealt misses are white (Character).</summary>
    public int TakeDamage(int attackerAttack, bool crit, int attackerHit, Element attackElement = Element.Neutral)
    {
        if (IsDead) return 0;

        // Accuracy (physical only; a crit can't be evaded). The hit came from the player, so a whiff
        // shows a RED MISS — monster-dealt misses on the player are white (see Character.TakeDamage).
        if (!crit && attackerHit >= 0 && !Combat.Lands(attackerHit, flee))
        {
            DamagePopup.SpawnText(transform.position, "MISS", MissColor);
            CombatSfx.Miss(transform.position, PlayerRanged());   // whiff FROM the mob (spatial), melee/ranged by weapon
            return 0;
        }

        // Element property: the attack's element vs this unit's armor element scales the damage (RO chart).
        // Neutral-vs-Neutral is 1.0, so default callers are unaffected.
        float mod = Elements.Modifier(attackElement, element);
        if (mod <= 0f) { SpawnElementNumber(0, mod, Color.white); return 0; }

        // Pre-renewal DEF split: hard-DEF % then flat soft DEF; a CRIT ignores both (and FLEE, above).
        int dmg = Mathf.Max(1, Mathf.RoundToInt(Combat.AfterDefense(attackerAttack, hardDefPercent, defense, crit) * mod));
        Current = Mathf.Max(0, Current - dmg);

        SpawnElementNumber(dmg, mod, crit ? CritColor : Color.white);
        // Impact FROM the mob (spatial). A basic attack (rolled accuracy, attackerHit ≥ 0) → hit/crit;
        // a skill strike (always-lands, attackerHit −1) → the unified skill impact.
        if (attackerHit >= 0) CombatSfx.Hit(transform.position, crit);
        else { CombatSfx.SkillHit(transform.position); VfxCue.Fire(VfxCueId.OnHitBySkill, transform); }
        if (isActiveAndEnabled) StartCoroutine(Flash());

        ApplyLeech(dmg);
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

        int dmg = Mathf.Max(1, Mathf.RoundToInt(Combat.AfterDefense(attackerMagic, hardMagicDefPercent, magicDefense, crit) * mod));
        Current = Mathf.Max(0, Current - dmg);

        SpawnElementNumber(dmg, mod, crit ? CritColor : MagicColor);
        CombatSfx.SkillHit(transform.position);   // magic = skill impact, spatial from the mob
        VfxCue.Fire(VfxCueId.OnHitBySkill, transform);
        if (isActiveAndEnabled) StartCoroutine(Flash());

        ApplyLeech(dmg);
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

        int dmg = Mathf.Max(1, Mathf.RoundToInt(Combat.AfterDefense(attackerMagic, hardMagicDefPercent, magicDefense, crit) * mod));
        bool wasAlive = !IsDead;
        Current = Mathf.Max(0, Current - dmg);
        SpawnElementNumber(dmg, mod, crit ? CritColor : MagicColor);
        CombatSfx.SkillHit(transform.position);   // overkill beats still sound (throttled per frame)
        if (wasAlive)
        {
            VfxCue.Fire(VfxCueId.OnHitBySkill, transform);
            if (isActiveAndEnabled) StartCoroutine(Flash());
            ApplyLeech(dmg);
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
        DamagePopup.Spawn(transform.position, dmg, color);
        if (isActiveAndEnabled) StartCoroutine(Flash());
        if (IsDead) Die();
        return dmg;
    }

    // Element feedback IS the damage number's opacity now (no word cue): a resisted hit dims to 70%, an
    // immune hit to 30% (shown as a faded "0" since it deals none); strong/normal stay fully opaque.
    void SpawnElementNumber(int dmg, float mod, Color baseCol)
    {
        baseCol.a *= Elements.DamageAlpha(mod);
        if (dmg > 0) DamagePopup.Spawn(transform.position, dmg, baseCol);
        else DamagePopup.SpawnText(transform.position, "0", baseCol);
    }

    // Lifesteal: a fraction of damage dealt to this mob heals the player (RO leech, the mechanical core of
    // a lifesteal card). Silent heal — a green "+N" per hit would spam during multi-hit; the HP bar is the cue.
    static void ApplyLeech(int dmgDealt)
    {
        var ch = Character.Instance;
        if (ch == null || dmgDealt <= 0 || ch.LifeStealPct <= 0f) return;
        ch.LeechHeal(Mathf.RoundToInt(dmgDealt * ch.LifeStealPct));
    }

    // A Health MISS is always the player attacking a mob (a mob missing the PLAYER pops a white MISS in
    // Character, not here), so read the player's equipped weapon to pick the ranged vs melee whiff.
    static bool PlayerRanged()
    {
        var c = Character.Instance;
        if (c == null) return false;
        var w = c.GetEquipped(EquipSlotType.Weapon);
        return w != null && (w.weaponType == WeaponType.Bow || w.weaponType == WeaponType.Firearm);
    }

    /// <summary>Reset to full HP and base color (used by <see cref="MobRespawn"/> to bring a mob back).</summary>
    public void Revive()
    {
        Current = maxHealth;
        if (sr != null) sr.color = baseColor;
    }

    /// <summary>Restore HP (capped at max) and pop a green "+N" over the unit.</summary>
    public void Heal(int amount)
    {
        if (amount <= 0 || IsDead) return;
        int before = Current;
        Current = Mathf.Min(maxHealth, Current + amount);
        int healed = Current - before;
        if (healed > 0) DamagePopup.SpawnText(transform.position, "+" + healed, new Color(0.45f, 1f, 0.45f));
    }

    IEnumerator Flash()
    {
        sr.color = flashColor;
        yield return flashWait;
        if (sr != null) sr.color = baseColor;
    }

    void Die()
    {
        onDeath?.Invoke();
        // Play the "poof" death animation if one is attached, and once it finishes either RECYCLE the mob
        // (if it has a MobRespawn) or remove it (the original Phase D behavior). With no FX, do the same
        // immediately.
        var respawn = GetComponent<MobRespawn>();
        System.Action done;
        if (respawn != null) done = respawn.OnFadedOut;
        else done = () => Destroy(gameObject);
        var fx = GetComponent<DeathDissolve>();
        if (fx != null && gameObject.activeInHierarchy) fx.Play(done);
        else done();
    }
}
