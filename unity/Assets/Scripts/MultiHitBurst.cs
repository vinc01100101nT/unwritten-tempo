using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reusable RO-style multi-hit for AoE skills. Snapshots the mobs in the blast, then deals
/// <paramref name="ticks"/> × <paramref name="hitsPerTick"/> hits over time — each hit is a SEPARATE roll
/// (the caller's <c>onHit</c> rolls its own damage + status proc and RETURNS the damage). Each <b>tick</b>
/// pops its OWN big yellow <b>combo total</b> per mob: it appears on that tick's first hit, accumulates
/// across the tick's hits, then rises + fades when the tick ends — so a 3-tick skill shows 3 separate
/// rising totals per mob (one per set of hits), exactly like RO's per-flurry combo number.
///
/// Faithful to RO in two ways: the per-hit numbers keep showing even after a mob dies (overkill — the
/// counter doesn't stop mid-sequence; the caller uses an overkill-safe hit like
/// <see cref="Health.TakeMagicDamageOverkill"/>), and the running total accumulates the full rolled damage,
/// not just HP removed. Runs on its own GameObject so it survives the caster being disabled (class switch).
/// </summary>
public class MultiHitBurst : MonoBehaviour
{
    static readonly Color TotalColor = new Color(1f, 0.92f, 0.15f);   // bright yellow combo total

    /// <summary>Fire a multi-hit burst at <paramref name="center"/>. <paramref name="onHit"/>(target, hitIndex)
    /// applies one hit and returns its damage; <paramref name="onTick"/>(center) (optional) fires once at the
    /// start of each tick (e.g. an impact-FX pulse); <paramref name="onHitBeat"/> (optional) fires once per HIT
    /// (per timed pulse, NOT once per target) when ≥1 mob was hit — e.g. a per-hit sound, so a pack doesn't
    /// stack N copies of the same clip into mush.</summary>
    public static void Run(Vector3 center, float radius, int ticks, int hitsPerTick,
                           float hitInterval, float tickInterval,
                           System.Func<MonsterAI3D, int, int> onHit,
                           System.Action<Vector3> onTick = null,
                           System.Action onHitBeat = null)
    {
        if (onHit == null) return;
        var go = new GameObject("MultiHitBurst") { hideFlags = HideFlags.DontSaveInEditor };
        go.AddComponent<MultiHitBurst>().StartCoroutine(Routine(go, center, radius,
            Mathf.Max(1, ticks), Mathf.Max(1, hitsPerTick),
            Mathf.Max(0.01f, hitInterval), Mathf.Max(0.01f, tickInterval), onHit, onTick, onHitBeat));
    }

    static IEnumerator Routine(GameObject self, Vector3 center, float radius, int ticks, int hitsPerTick,
                               float hitInterval, float tickInterval,
                               System.Func<MonsterAI3D, int, int> onHit, System.Action<Vector3> onTick,
                               System.Action onHitBeat)
    {
        // Snapshot the mobs in the blast ONCE (its own list — deaths can't reshuffle it, like the AoE fix).
        var targets = new List<MonsterAI3D>();
        float r2 = radius * radius;
        var list = MonsterAI3D.Active;
        for (int i = 0; i < list.Count; i++)
        {
            var ai = list[i];
            if (ai == null) continue;
            var h = ai.Health;
            if (h == null || h.IsDead) continue;
            Vector3 d = ai.transform.position - center; d.y = 0f;
            if (d.sqrMagnitude <= r2) targets.Add(ai);
        }

        // Per-mob running total + its live popup — these are reset EACH TICK so every tick shows its own
        // rising yellow combo number (RO pops one total per flurry, not one across the whole skill).
        var totals = new int[targets.Count];
        var totalPopups = new DamagePopup[targets.Count];

        var hitWait = new WaitForSeconds(hitInterval);
        var tickWait = new WaitForSeconds(tickInterval);

        for (int tick = 0; tick < ticks; tick++)
        {
            onTick?.Invoke(center);                                   // per-tick FX pulse (e.g. explosion)
            for (int i = 0; i < totals.Length; i++) { totals[i] = 0; totalPopups[i] = null; }  // fresh total for THIS tick

            for (int hit = 0; hit < hitsPerTick; hit++)
            {
                bool anyHit = false;
                for (int i = 0; i < targets.Count; i++)
                {
                    var ai = targets[i];
                    if (ai == null || ai.Health == null) continue;     // mob fully destroyed → can't show more
                    // NOTE: no IsDead skip — overkill ticks still show (the onHit is overkill-safe).
                    int dealt = onHit(ai, tick * hitsPerTick + hit);
                    anyHit = true;
                    totals[i] += dealt;

                    Vector3 tp = UnitAnchor.Center(ai.transform);   // live total over the mob (feet-pivoted)
                    // First hit of THIS tick spawns the yellow total; later hits just bump it up.
                    if (totalPopups[i] == null) totalPopups[i] = DamagePopup.SpawnHeld(tp, totals[i], TotalColor, 1.7f);
                    else totalPopups[i].SetHeld(totals[i], tp);
                }
                if (anyHit) onHitBeat?.Invoke();                       // ONE per-hit sound for the whole pulse (not per mob)
                if (hit < hitsPerTick - 1) yield return hitWait;       // between hits within a tick
            }

            // Tick over: release THIS tick's totals so they rise + fade; the next tick spawns brand-new ones.
            for (int i = 0; i < totalPopups.Length; i++)
                if (totalPopups[i] != null) { totalPopups[i].Release(); totalPopups[i] = null; }

            if (tick < ticks - 1) yield return tickWait;               // between ticks
        }

        Object.Destroy(self);
    }
}
