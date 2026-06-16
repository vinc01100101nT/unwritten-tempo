using System.Collections;
using UnityEngine;

/// <summary>
/// 3D melee swing executor — a lean port of <see cref="PlayerAttacker"/> (minus the 2D mouse
/// helpers, which <see cref="Player3DController"/> handles via a camera raycast). The controller
/// owns the target; this only knows how to swing: it gates on <see cref="cooldown"/> and applies
/// damage at the END of a short <see cref="windupTime"/>, so a swing can be cancelled before it
/// lands. Uses <see cref="Character"/>'s attack if present, else <see cref="damage"/> (the spike
/// has no Character, so it falls back).
/// </summary>
public class Player3DAttacker : MonoBehaviour
{
    [Tooltip("How close (world units, XZ) you must be to land a hit.")]
    public float range = 1.4f;
    [Tooltip("Seconds between auto-attacks.")]
    public float cooldown = 0.6f;
    [Tooltip("Delay between a swing starting and the hit landing (cancellable window).")]
    public float windupTime = 0.18f;
    [Tooltip("Fallback damage when there's no Character (e.g. the standalone spike).")]
    public int damage = 3;

    float nextSwing;
    Coroutine swing;
    public bool Swinging => swing != null;

    public bool InRange(Health t)
    {
        if (t == null || t.IsDead) return false;
        Vector3 a = transform.position, b = t.transform.position;
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b) <= range;
    }

    public void TryAttack(Health t)
    {
        if (Time.time < nextSwing || swing != null || !InRange(t)) return;
        nextSwing = Time.time + cooldown;
        swing = StartCoroutine(SwingThenHit(t));
    }

    IEnumerator SwingThenHit(Health t)
    {
        yield return new WaitForSeconds(windupTime);
        swing = null;
        if (!InRange(t)) yield break;   // target moved out / died during the windup
        int power = Character.Instance != null ? Character.Instance.Attack : damage;
        t.TakeDamage(power);
    }

    public void Cancel() { if (swing != null) { StopCoroutine(swing); swing = null; } }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, range);
    }
}
