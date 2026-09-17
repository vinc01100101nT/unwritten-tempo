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

        var hitWait = Wait(hitInterval);
        var tickWait = Wait(tickInterval);

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

                    // THE RUNNING TOTAL IS BUILT FROM WHAT CAME BACK, and on a machine that no longer decides
                    // damage nothing comes back through this return — the server answers separately, and each
                    // individual number still appears. Adding those zeros up and showing the sum would put a large
                    // confident 0 over the mob, which is worse than showing no total at all. Feeding this from the
                    // numbers that actually arrive is the follow-up; drawing a lie is not an option in between.
                    if (GameNetwork.DamageDecidedElsewhere) continue;

                    Vector3 tp = UnitAnchor.Overhead(ai.transform);   // live total over the mob
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

    /// <summary>Single-flurry variant driven by an EXPLICIT target snapshot (the skill-composer path already scanned
    /// the mobs). Deals <paramref name="hits"/> hits over time to each mob; <paramref name="onHit"/>(mob, hitIndex)
    /// applies one hit and returns its damage; a per-mob yellow combo total accumulates then rises + fades at the end.
    /// Copies the list so the caller can reuse its buffer immediately.
    ///
    /// <para><paramref name="showTotal"/> false hands the yellow number to whoever asked for it instead — a run showing
    /// ONE total across all its repeats (<see cref="RepeatsDamageTotal"/>). Exactly one of the two ever draws it, so the
    /// flurry stops drawing its own rather than stacking a second number on the same mob. The hits themselves are
    /// untouched either way: this decides only who displays their sum.</para></summary>
    public static void Run(List<MonsterAI3D> targets, int hits, float hitInterval,
                           System.Func<MonsterAI3D, int, int> onHit, System.Action onHitBeat = null,
                           bool showTotal = true)
    {
        if (onHit == null || targets == null || targets.Count == 0) return;
        var go = new GameObject("MultiHitBurst") { hideFlags = HideFlags.DontSaveInEditor };
        go.AddComponent<MultiHitBurst>().StartCoroutine(RoutineList(go, new List<MonsterAI3D>(targets),
            Mathf.Max(1, hits), Mathf.Max(0.01f, hitInterval), onHit, onHitBeat, showTotal));
    }

    /// <summary>The same flurry on PEOPLE — a creature's multi-hit skill landing on whoever it caught, one hit every
    /// <paramref name="hitInterval"/> seconds, exactly as the creature version spaces them. Draws no total of its own:
    /// every blow on a person is announced at the damage door, which is where their screen learns of it.</summary>
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
                                   System.Func<MonsterAI3D, int, int> onHit, System.Action onHitBeat, bool showTotal)
    {
        int n = targets.Count;
        var totals = new int[n];
        var popups = new DamagePopup[n];
        var hitWait = Wait(hitInterval);

        for (int hit = 0; hit < hits; hit++)
        {
            bool anyHit = false;
            for (int i = 0; i < n; i++)
            {
                var ai = targets[i];
                if (ai == null || ai.Health == null) continue;   // fully destroyed → can't show more (no IsDead skip: overkill shows)
                int dealt = onHit(ai, hit);
                anyHit = true;
                totals[i] += dealt;

                if (!showTotal) continue;              // the run is drawing one total across all its repeats instead
                if (GameNetwork.DamageDecidedElsewhere) continue;   // see the note on the other total above
                Vector3 tp = UnitAnchor.Overhead(ai.transform);
                if (popups[i] == null) popups[i] = DamagePopup.SpawnHeld(tp, totals[i], TotalColor, 1.7f);
                else popups[i].SetHeld(totals[i], tp);
            }
            if (anyHit) onHitBeat?.Invoke();
            if (hit < hits - 1) yield return hitWait;
        }

        for (int i = 0; i < n; i++)
            if (popups[i] != null) popups[i].Release();   // release → each total rises + fades

        Object.Destroy(self);
    }
}
