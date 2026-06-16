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

    [Tooltip("Incoming attack is reduced by this; damage dealt is always at least 1.")]
    public int defense = 0;

    [Header("Hit feedback")]
    public Color flashColor = Color.white;
    public float flashTime = 0.08f;

    [Tooltip("Invoked once, just before the GameObject is removed.")]
    public UnityEngine.Events.UnityEvent onDeath;

    public int Current { get; private set; }
    public bool IsDead => Current <= 0;

    SpriteRenderer sr;
    Color baseColor;

    void Awake()
    {
        Current = maxHealth;
        sr = GetComponent<SpriteRenderer>();
        baseColor = sr.color;
    }

    /// <summary>Apply a hit; final damage = max(1, attackerAttack − defense). Returns the damage dealt.</summary>
    public int TakeDamage(int attackerAttack)
    {
        if (IsDead) return 0;
        int dmg = Mathf.Max(1, attackerAttack - defense);
        Current = Mathf.Max(0, Current - dmg);

        DamagePopup.Spawn(transform.position, dmg, Color.white);
        if (isActiveAndEnabled) StartCoroutine(Flash());

        if (IsDead) Die();
        return dmg;
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
        yield return new WaitForSeconds(flashTime);
        if (sr != null) sr.color = baseColor;
    }

    void Die()
    {
        onDeath?.Invoke();
        // Minimal for Phase D: just remove it. (Loot / death FX / XP come later.)
        Destroy(gameObject);
    }
}
