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

    [Header("Slash FX (sliced off a horizontal sheet at runtime)")]
    public Texture2D slashSheet;
    public int slashFrameSize = 32;
    public int slashFrames = 4;
    public float slashFps = 22f;
    public float slashScale = 1.1f;

    [Header("Audio (used when the equipped weapon has none of its own)")]
    public SfxSet defaultAttackSfx;   // swing
    public SfxSet defaultHitSfx;      // impact

    float nextSwing;
    Coroutine swing;
    public bool Swinging => swing != null;

    /// <summary>
    /// Time-scale of the attack MOTION — 1 / attack-speed, so a faster ASPD shrinks every part of
    /// the swing (windup, slash FX, the held pose) the same way it shrinks the cooldown. &lt;1 means
    /// the animation plays quicker. The pose hold in <see cref="Player3DController"/> reads this too,
    /// so the whole attack stays in sync with the swing rate.
    /// </summary>
    public static float MotionScale =>
        Character.Instance != null ? 1f / Mathf.Max(0.01f, Character.Instance.AttackSpeedMult) : 1f;

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
        // AttackSpeed shortens the gap between swings AND the motion itself (faster = higher mult),
        // so the visible swing keeps pace with the cadence instead of lagging at high ASPD.
        nextSwing = Time.time + cooldown * MotionScale;
        Sfx.Play(WeaponSfx(w => w.attackSfx, defaultAttackSfx));   // swing
        swing = StartCoroutine(SwingThenHit(t));
    }

    // The equipped weapon's set if it has clips, else the player default.
    static SfxSet WeaponSfx(System.Func<Item, SfxSet> pick, SfxSet fallback)
    {
        var w = Character.Instance != null ? Character.Instance.GetEquipped(EquipSlotType.Weapon) : null;
        var s = w != null ? pick(w) : null;
        return s != null && !s.Empty ? s : fallback;
    }

    IEnumerator SwingThenHit(Health t)
    {
        // Windup scales with attack speed too — the hit lands proportionally sooner at high ASPD.
        float scale = MotionScale;
        yield return new WaitForSeconds(windupTime * scale);
        swing = null;
        if (!InRange(t)) yield break;   // target moved out / died during the windup
        // Roll the hit through Character so CritChance/CritDamage apply (spike has no Character → flat damage).
        bool crit = false;
        int power = Character.Instance != null ? Character.Instance.RollDamage(out crit) : damage;

        // Slash FX at the target (a bit above its feet), billboarded by SlashEffect so it faces the camera.
        Vector3 fxPos = t.transform.position + Vector3.up * 0.4f;
        SlashEffect.Spawn(slashSheet, slashFrameSize, slashFrames, slashFps / Mathf.Max(0.01f, scale), fxPos, slashScale);

        // Pass our HIT so the target rolls it against its FLEE (RO accuracy); −1 in the Character-less spike.
        int hit = Character.Instance != null ? Character.Instance.Hit : -1;
        int dealt = t.TakeDamage(power, crit, hit);
        if (dealt > 0) Sfx.Play(WeaponSfx(w => w.hitSfx, defaultHitSfx));   // impact — only on a landed hit, not a MISS
    }

    public void Cancel() { if (swing != null) { StopCoroutine(swing); swing = null; } }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, range);
    }
}
