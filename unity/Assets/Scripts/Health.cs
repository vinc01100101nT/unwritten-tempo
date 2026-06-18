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

    [Tooltip("Incoming PHYSICAL attack is reduced by this; damage dealt is always at least 1.")]
    public int defense = 0;

    [Tooltip("Incoming MAGIC attack is reduced by this; damage dealt is always at least 1.")]
    public int magicDefense = 0;

    [Tooltip("FLEE (RO evasion). A physical attacker's HIT is rolled against this; when it loses, the hit " +
             "MISSes (red) and deals no damage. Magic can't be evaded. Higher = dodges more. See Combat.")]
    public int flee = 0;

    [Header("Hit feedback")]
    public Color flashColor = Color.white;
    public float flashTime = 0.08f;

    [Tooltip("Invoked once, just before the GameObject is removed.")]
    public UnityEngine.Events.UnityEvent onDeath;

    public int Current { get; private set; }
    public bool IsDead => Current <= 0;

    static readonly Color MissColor = new Color(1f, 0.3f, 0.3f);   // a player-dealt hit whiffed → red MISS

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
    public int TakeDamage(int attackerAttack, bool crit, int attackerHit)
    {
        if (IsDead) return 0;

        // Accuracy (physical only; a crit can't be evaded). The hit came from the player, so a whiff
        // shows a RED MISS — monster-dealt misses on the player are white (see Character.TakeDamage).
        if (!crit && attackerHit >= 0 && !Combat.Lands(attackerHit, flee))
        {
            DamagePopup.SpawnText(transform.position, "MISS", MissColor);
            return 0;
        }

        int dmg = Mathf.Max(1, attackerAttack - defense);
        Current = Mathf.Max(0, Current - dmg);

        DamagePopup.Spawn(transform.position, dmg, crit ? new Color(1f, 0.85f, 0.2f) : Color.white);
        if (isActiveAndEnabled) StartCoroutine(Flash());

        if (IsDead) Die();
        return dmg;
    }

    /// <summary>Apply a MAGIC hit, reduced by <see cref="magicDefense"/> instead of <see cref="defense"/>.
    /// Pops a purple number (gold on a crit). Returns the damage dealt.</summary>
    public int TakeMagicDamage(int attackerMagic, bool crit)
    {
        if (IsDead) return 0;
        int dmg = Mathf.Max(1, attackerMagic - magicDefense);
        Current = Mathf.Max(0, Current - dmg);

        DamagePopup.Spawn(transform.position, dmg, crit ? new Color(1f, 0.85f, 0.2f) : new Color(0.7f, 0.5f, 1f));
        if (isActiveAndEnabled) StartCoroutine(Flash());

        if (IsDead) Die();
        return dmg;
    }

    /// <summary>Like <see cref="TakeMagicDamage"/> but ALWAYS registers a damage number — even when already
    /// dead. A multi-hit sequence keeps showing its remaining ticks (overkill) over a mob that died partway,
    /// the RO way. HP floors at 0; death fires exactly once (on the alive→dead transition). Returns the hit's
    /// value, so a combo total can keep accumulating past the kill.</summary>
    public int TakeMagicDamageOverkill(int attackerMagic, bool crit)
    {
        int dmg = Mathf.Max(1, attackerMagic - magicDefense);
        bool wasAlive = !IsDead;
        Current = Mathf.Max(0, Current - dmg);
        DamagePopup.Spawn(transform.position, dmg, crit ? new Color(1f, 0.85f, 0.2f) : new Color(0.7f, 0.5f, 1f));
        if (wasAlive)
        {
            if (isActiveAndEnabled) StartCoroutine(Flash());
            if (IsDead) Die();
        }
        return dmg;
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
