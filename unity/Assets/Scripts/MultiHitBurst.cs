using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reusable RO-style multi-hit: deals a flurry of hits over time to a snapshot of targets — each hit is a SEPARATE
/// roll (the caller's <c>onHit</c> rolls its own damage + status proc). Per-hit numbers keep landing even after a mob
/// dies (overkill — the counter doesn't stop mid-sequence; the caller uses an overkill-safe hit). Runs on its own
/// GameObject so it survives the caster being disabled (class switch).
///
/// <para>Server only, and it draws nothing: every hit is announced at the damage door, which is where each screen
/// learns of it and draws it.</para>
/// </summary>
public class MultiHitBurst : MonoBehaviour
{
    // A wait object is immutable, so ONE per duration serves every burst ever cast. Skills author a
    // handful of intervals between them, so this holds a handful of entries for the life of the run —
    // instead of two fresh ones every time somebody swings.
    static readonly System.Collections.Generic.Dictionary<float, WaitForSeconds> waits =
        new System.Collections.Generic.Dictionary<float, WaitForSeconds>();

    static WaitForSeconds Wait(float seconds)
    {
        if (!waits.TryGetValue(seconds, out var w)) { w = new WaitForSeconds(seconds); waits[seconds] = w; }
        return w;
    }

    /// <summary>Single-flurry burst driven by an EXPLICIT target snapshot (the skill-composer path already scanned
    /// the mobs). Deals <paramref name="hits"/> hits over time to each mob; <paramref name="onHit"/>(mob, hitIndex)
    /// applies one hit. Copies the list so the caller can reuse its buffer immediately.</summary>
    public static void Run(List<MonsterAI3D> targets, int hits, float hitInterval,
                           System.Func<MonsterAI3D, int, int> onHit)
    {
        if (onHit == null || targets == null || targets.Count == 0) return;
        var go = new GameObject("MultiHitBurst") { hideFlags = HideFlags.DontSaveInEditor };
        go.AddComponent<MultiHitBurst>().StartCoroutine(RoutineList(go, new List<MonsterAI3D>(targets),
            Mathf.Max(1, hits), Mathf.Max(0.01f, hitInterval), onHit));
    }

    /// <summary>The same flurry on PEOPLE — a creature's multi-hit skill landing on whoever it caught, one hit every
    /// <paramref name="hitInterval"/> seconds, exactly as the creature version spaces them.</summary>
    public static void RunOnUnits(List<ICombatUnit> targets, int hits, float hitInterval,
                                  System.Func<ICombatUnit, int, int> onHit)
    {
        if (onHit == null || targets == null || targets.Count == 0) return;
        var go = new GameObject("MultiHitBurst") { hideFlags = HideFlags.DontSaveInEditor };
        go.AddComponent<MultiHitBurst>().StartCoroutine(RoutineUnits(go, new List<ICombatUnit>(targets),
            Mathf.Max(1, hits), Mathf.Max(0.01f, hitInterval), onHit));
    }

    static IEnumerator RoutineUnits(GameObject self, List<ICombatUnit> targets, int hits, float hitInterval,
                                    System.Func<ICombatUnit, int, int> onHit)
    {
        var hitWait = Wait(hitInterval);
        for (int hit = 0; hit < hits; hit++)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                var unit = targets[i];
                if (PlayerUnits.Gone(unit)) continue;
                onHit(unit, hit);
            }
            if (hit < hits - 1) yield return hitWait;
        }
        Object.Destroy(self);
    }

    static IEnumerator RoutineList(GameObject self, List<MonsterAI3D> targets, int hits, float hitInterval,
                                   System.Func<MonsterAI3D, int, int> onHit)
    {
        var hitWait = Wait(hitInterval);
        for (int hit = 0; hit < hits; hit++)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                var ai = targets[i];
                if (ai == null || ai.Health == null) continue;   // fully destroyed → can't land more (no IsDead skip: overkill lands)
                onHit(ai, hit);
            }
            if (hit < hits - 1) yield return hitWait;
        }
        Object.Destroy(self);
    }
}
