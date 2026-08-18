using System.Collections.Generic;
using UnityEngine;

/// <summary>The percentage arithmetic behind an affinity channel, with no UI in it — the Skill Composer draws the rows
/// and calls in here to move the numbers.
///
/// <para>Affinity is a RATIO ("which stats scale this, in what mix"): the runtime normalizes by the sum, so only
/// proportions matter. It is authored as a CONSERVED-TOTAL allocator — a channel always sums to exactly 100 BY
/// CONSTRUCTION, so the total can never be a stale or wrong number. Raising one stat pulls the budget out of the other
/// stats proportionally; a LOCKED stat holds its value and is skipped by that pull.</para></summary>
public static class SkillAffinityWeights
{
    // Per-channel lock state (editor-only, session-scoped) — deliberately NOT a field on StatWeight, so the serialized
    // affinity data stays pure runtime data with no dead 'locked' knob the game would ignore. Keyed by the list's
    // identity + the stat id, so it survives repaints/row reorder; it resets when the asset unloads (fine to author).
    static readonly HashSet<(int, int)> locks = new HashSet<(int, int)>();
    static int RtHash(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);

    public static bool IsLocked(List<StatWeight> list, StatId stat) => locks.Contains((RtHash(list), (int)stat));

    public static void SetLocked(List<StatWeight> list, StatId stat, bool locked)
    {
        var key = (RtHash(list), (int)stat);
        if (locked) locks.Add(key); else locks.Remove(key);
    }

    /// <summary>Conserved-total allocator, lock-aware: set channel[changed] = newValue, hold every LOCKED stat fixed,
    /// and rescale the OTHER unlocked stats so the whole list sums to exactly 100 again. The changed stat is clamped to
    /// the budget the locks leave free (100 − lockedSum), so it can never push an unlocked stat below 0. Integer output
    /// (largest-remainder) so stored and displayed always match.</summary>
    public static void Redistribute(List<StatWeight> list, int changed, float newValue)
    {
        int n = list.Count;
        if (n == 0) return;

        int lockedSum = 0, otherUnlocked = 0;
        float otherOld = 0f;
        for (int j = 0; j < n; j++)
        {
            if (j == changed) continue;
            if (IsLocked(list, list[j].stat)) lockedSum += Mathf.RoundToInt(Mathf.Max(0f, list[j].weight));
            else { otherUnlocked++; otherOld += Mathf.Max(0f, list[j].weight); }
        }
        int budget = Mathf.Clamp(100 - lockedSum, 0, 100);       // all the changed + other-unlocked stats can hold
        int v = Mathf.Clamp(Mathf.RoundToInt(newValue), 0, budget);
        if (otherUnlocked == 0) v = budget;                       // nothing free to hold a remainder → changed takes it all
        int othersBudget = budget - v;

        var targets = new float[n];
        var pinned = new bool[n];
        for (int j = 0; j < n; j++)
        {
            if (j == changed) { targets[j] = v; pinned[j] = true; }
            else if (IsLocked(list, list[j].stat)) { targets[j] = Mathf.RoundToInt(Mathf.Max(0f, list[j].weight)); pinned[j] = true; }
            else if (otherOld > 0f) targets[j] = Mathf.Max(0f, list[j].weight) / otherOld * othersBudget;
            else targets[j] = otherUnlocked > 0 ? othersBudget / (float)otherUnlocked : 0f;
        }
        int[] rounded = RoundSharesPinned(targets, pinned, othersBudget);
        for (int j = 0; j < n; j++) { var w = list[j]; w.weight = rounded[j]; list[j] = w; }
    }

    /// <summary>Fill the UNLOCKED stats so the channel sums to 100 again while holding every locked stat fixed. Used
    /// after a stat is removed. If nothing is unlocked, falls back to a plain rescale-to-100.</summary>
    public static void RebalanceUnlocked(List<StatWeight> list)
    {
        int n = list.Count;
        if (n == 0) return;
        int lockedSum = 0, unlocked = 0;
        float unlockedOld = 0f;
        for (int j = 0; j < n; j++)
        {
            if (IsLocked(list, list[j].stat)) lockedSum += Mathf.RoundToInt(Mathf.Max(0f, list[j].weight));
            else { unlocked++; unlockedOld += Mathf.Max(0f, list[j].weight); }
        }
        if (unlocked == 0) { NormalizeTo100(list); return; }
        int budget = Mathf.Clamp(100 - lockedSum, 0, 100);
        var targets = new float[n];
        var pinned = new bool[n];
        for (int j = 0; j < n; j++)
        {
            if (IsLocked(list, list[j].stat)) { targets[j] = Mathf.RoundToInt(Mathf.Max(0f, list[j].weight)); pinned[j] = true; }
            else targets[j] = unlockedOld > 0f ? Mathf.Max(0f, list[j].weight) / unlockedOld * budget : budget / (float)unlocked;
        }
        int[] rounded = RoundSharesPinned(targets, pinned, budget);
        for (int j = 0; j < n; j++) { var w = list[j]; w.weight = rounded[j]; list[j] = w; }
    }

    /// <summary>Rescale a channel's weights so they sum to exactly 100 (integers, largest-remainder). Used on entry for
    /// legacy assets, after a stat is removed, and on Save. All-zero channels get an even split.</summary>
    public static void NormalizeTo100(List<StatWeight> list)
    {
        if (list == null || list.Count == 0) return;
        int n = list.Count;
        float sum = 0f;
        for (int i = 0; i < n; i++) sum += Mathf.Max(0f, list[i].weight);
        var targets = new float[n];
        for (int i = 0; i < n; i++)
            targets[i] = sum > 0f ? Mathf.Max(0f, list[i].weight) / sum * 100f : 100f / n;
        // No pinned index here — largest-remainder over all n to hit 100 exactly.
        int[] rounded = RoundSharesAll(targets, 100);
        for (int i = 0; i < n; i++) { var w = list[i]; w.weight = rounded[i]; list[i] = w; }
    }

    /// <summary>Normalize every affinity channel on the skill (called on Save) so assets always store clean
    /// percentages.</summary>
    public static void NormalizeAll(SkillDef def)
    {
        if (def == null || def.bindings == null) return;
        foreach (var b in def.bindings)
        {
            if (b?.effects == null) continue;
            foreach (var e in b.effects)
            {
                if (e == null) continue;
                NormalizeTo100(e.affinity);
                NormalizeTo100(e.durationAffinity);
                NormalizeTo100(e.hitCountAffinity);
            }
        }
    }

    // Largest-remainder rounding: entries flagged `pinned` keep their exact (already-integer) target; the rest are
    // rounded so the UNPINNED entries sum to exactly `unpinnedTotal` (leftover units go to the largest fractions).
    static int[] RoundSharesPinned(float[] targets, bool[] pinned, int unpinnedTotal)
    {
        int n = targets.Length;
        var result = new int[n];
        var frac = new float[n];
        int floored = 0;
        for (int j = 0; j < n; j++)
        {
            if (pinned[j]) { result[j] = Mathf.RoundToInt(targets[j]); continue; }
            int f = Mathf.FloorToInt(targets[j]);
            result[j] = f; frac[j] = targets[j] - f; floored += f;
        }
        int remainder = unpinnedTotal - floored;
        for (int r = 0; r < remainder; r++)
        {
            int best = -1; float bestFrac = -1f;
            for (int j = 0; j < n; j++)
            {
                if (pinned[j]) continue;
                if (frac[j] > bestFrac) { bestFrac = frac[j]; best = j; }
            }
            if (best < 0) break;
            result[best]++; frac[best] = -1f;
        }
        return result;
    }

    // Largest-remainder rounding over ALL entries to sum exactly `total` (no pinned index).
    static int[] RoundSharesAll(float[] targets, int total)
    {
        int n = targets.Length;
        var result = new int[n];
        var frac = new float[n];
        int floored = 0;
        for (int j = 0; j < n; j++) { int f = Mathf.FloorToInt(targets[j]); result[j] = f; frac[j] = targets[j] - f; floored += f; }
        int remainder = total - floored;
        for (int r = 0; r < remainder; r++)
        {
            int best = -1; float bestFrac = -1f;
            for (int j = 0; j < n; j++) if (frac[j] > bestFrac) { bestFrac = frac[j]; best = j; }
            if (best < 0) break;
            result[best]++; frac[best] = -1f;
        }
        return result;
    }
}
