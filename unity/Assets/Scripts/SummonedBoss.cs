using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SERVER ONLY. A boss somebody summoned at an Elite Summoner: it holds its summoner's spot for as long as it exists, and
/// leaves after <see cref="SummonMath.IdleSecondsBeforeLeaving"/> with nobody hitting it. Destroyed on death by the
/// creature's own death path (it has no respawner), which frees the spot.
/// </summary>
public class SummonedBoss : MonoBehaviour
{
    // Each summoner's spot (keyed by the summoner's instance id): pending while a body loads, then the live boss.
    static readonly HashSet<int> taken = new HashSet<int>();

    /// <summary>True while the summoner <paramref name="spotKey"/> has a boss out, or one on its way.</summary>
    public static bool Taken(int spotKey) => taken.Contains(spotKey);

    /// <summary>Hold the spot before anything is paid, so two presses can never both find it free.</summary>
    public static void Hold(int spotKey) => taken.Add(spotKey);

    /// <summary>Give the spot back — the body never arrived, or the boss is gone.</summary>
    public static void Free(int spotKey) => taken.Remove(spotKey);

    int spotKey;
    float appearedAt, lastHitAt = float.NegativeInfinity, nextCheck;
    int lastHealth;
    Health health;

    public void Begin(int key)
    {
        spotKey = key;
        appearedAt = Time.time;
        health = GetComponent<Health>();
        lastHealth = health != null ? health.Current : 0;
    }

    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 1f;
        if (health == null) return;
        // a drop in health is somebody hitting it
        if (health.Current < lastHealth) lastHitAt = Time.time;
        lastHealth = health.Current;
        if (!health.IsDead && SummonMath.ShouldLeave(appearedAt, lastHitAt, Time.time)) Destroy(gameObject);
    }

    void OnDestroy() => Free(spotKey);
}
