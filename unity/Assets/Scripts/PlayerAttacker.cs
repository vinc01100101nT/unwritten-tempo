using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Melee swing EXECUTOR. It does not pick targets any more — <see cref="PlayerCommander"/>
/// owns the order/target and calls <see cref="TryAttack"/> each frame while a target is
/// in range. This class only knows how to swing: it gates on <see cref="cooldown"/>,
/// plays the attack pose, and — crucially — applies damage only at the END of a short
/// <see cref="windupTime"/>. That windup is what makes attacks cancellable: pressing S
/// (<see cref="PlayerCommander.Stop"/>) calls <see cref="Cancel"/>, so a hit that hasn't
/// landed yet deals no damage.
/// </summary>
[RequireComponent(typeof(PlayerController2D))]
public class PlayerAttacker : MonoBehaviour
{
    [Tooltip("How close (world units) you must be to land a hit on your target.")]
    public float range = 1.4f;
    [Tooltip("Seconds between auto-attacks on your target.")]
    public float cooldown = 0.6f;
    [Tooltip("Fallback damage if there's no Character yet; normally uses Character.Attack.")]
    public int damage = 1;

    [Header("Attack feedback (assigned by Run All / Setup Global Systems)")]
    [Tooltip("Slash FX strip (e.g. FX/Attack/SlashCurved/SpriteSheet.png, 32px frames).")]
    public Texture2D slashSheet;
    public int slashFrameSize = 32;
    public int slashFrames = 4;
    public float slashFps = 22f;
    public float slashScale = 1.1f;
    [Tooltip("How long the character holds its attack pose per swing.")]
    public float attackPoseTime = 0.18f;

    [Tooltip("Delay between a swing STARTING and the hit landing. Damage is applied only " +
             "at the end of this window, so Stop (S) can cancel an in-flight swing.")]
    public float windupTime = 0.18f;

    CharacterAnimator2D anim;
    float nextSwing;
    Coroutine swing;

    /// <summary>True while a swing's windup is in flight (the hit hasn't landed yet).</summary>
    public bool Swinging => swing != null;

    void Awake()
    {
        anim = GetComponent<CharacterAnimator2D>();
    }

    /// <summary>Is <paramref name="t"/> a living target within melee range right now?</summary>
    public bool InRange(Health t)
        => t != null && !t.IsDead &&
           Vector2.Distance(transform.position, t.transform.position) <= range;

    /// <summary>Start a swing at <paramref name="t"/> if the cooldown is ready, no swing is
    /// already in flight, and it's in range. The hit (slash FX + damage) lands after
    /// <see cref="windupTime"/> and can be aborted by <see cref="Cancel"/>.</summary>
    public void TryAttack(Health t)
    {
        if (Time.time < nextSwing || swing != null) return;
        if (!InRange(t)) return;

        nextSwing = Time.time + cooldown;

        Vector2 dir = (Vector2)t.transform.position - (Vector2)transform.position;
        if (anim != null) anim.PlayAttack(dir, attackPoseTime);   // swing pose toward the target

        swing = StartCoroutine(SwingThenHit(t));
    }

    IEnumerator SwingThenHit(Health t)
    {
        yield return new WaitForSeconds(windupTime);
        swing = null;

        // The hit lands only if the target is still valid and in range at this instant.
        if (!InRange(t)) yield break;

        Vector3 tpos = t.transform.position;                       // capture before the hit may destroy it
        SlashEffect.Spawn(slashSheet, slashFrameSize, slashFrames, slashFps, tpos, slashScale);
        int power = Character.Instance != null ? Character.Instance.Attack : damage;
        t.TakeDamage(power);
    }

    /// <summary>Abort an in-progress swing so its damage never lands (called by Stop / S).</summary>
    public void Cancel()
    {
        if (swing != null) { StopCoroutine(swing); swing = null; }
    }

    // ---- shared mouse helpers (also used by PlayerCommander / GameCursor) ------

    public static bool PointerOverUI()
        => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    /// <summary>The closest living monster under the mouse cursor, or null.</summary>
    public static Health MonsterUnderCursor(Camera cam)
    {
        if (cam == null) return null;
        Vector2 w = cam.ScreenToWorldPoint(Input.mousePosition);
        Health best = null;
        float bestDist = float.MaxValue;
        foreach (var c in Physics2D.OverlapCircleAll(w, 0.35f))
        {
            var h = c.GetComponentInParent<Health>();
            if (h == null || h.IsDead || h.GetComponent<MonsterAI>() == null) continue;
            float d = Vector2.SqrMagnitude((Vector2)h.transform.position - w);
            if (d < bestDist) { bestDist = d; best = h; }
        }
        return best;
    }

    /// <summary>The closest friendly unit (ally / another player) under the cursor, or null.
    /// Used for right-click-to-follow.</summary>
    public static FriendlyUnit FriendlyUnitUnderCursor(Camera cam)
    {
        if (cam == null) return null;
        Vector2 w = cam.ScreenToWorldPoint(Input.mousePosition);
        FriendlyUnit best = null;
        float bestDist = float.MaxValue;
        foreach (var c in Physics2D.OverlapCircleAll(w, 0.35f))
        {
            var f = c.GetComponentInParent<FriendlyUnit>();
            if (f == null) continue;
            float d = Vector2.SqrMagnitude((Vector2)f.transform.position - w);
            if (d < bestDist) { bestDist = d; best = f; }
        }
        return best;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, range);
    }
}
