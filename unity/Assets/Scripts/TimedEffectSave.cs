using System;
using UnityEngine;

/// <summary>
/// A timed effect written into a character's save and read back — THE ONE place its clock is paused and resumed.
/// Plain functions over plain numbers: the moment "now" is handed in, never read here, so the pause can be checked by
/// calling a function rather than by standing the game up.
///
/// <para>Decided 2026-09-23: timed effects end only on DEATH. Logging out or crossing to a map another server holds
/// no longer clears them, and their clock STOPS while the character is away — a converter with 3:12 left still has
/// 3:12 when its owner comes back. So a record holds SECONDS LEFT, never a moment on a clock.</para>
/// </summary>
public static class TimedEffectSave
{
    /// <summary>The pile as the save holds it, at <paramref name="now"/> on the saving machine's game clock. Null when
    /// nothing of it is left to keep. Every stack is kept on its own — each has its own end, strength and amount, and
    /// the pile's totals are rebuilt from them (<see cref="UnitStates.State.Refold"/>), so saving the totals too would
    /// be a second copy of the same numbers.</summary>
    public static SaveData.TimedEffect Capture(UnitStates.State s, float now)
    {
        if (s == null || s.stacks.Count == 0) return null;
        bool debt = s.id == StateId.DeferredDamage;
        int kept = 0;
        for (int i = 0; i < s.stacks.Count; i++)
            if (debt || s.stacks[i].endTime > now) kept++;
        if (kept == 0) return null;

        var rec = new SaveData.TimedEffect
        {
            kind = s.id.ToString(),
            sourceSkillId = s.sourceSkillId ?? "",
            stacking = s.stacking,
            description = s.description ?? "",
            secondsRunning = Mathf.Max(0f, now - s.startTime),
            secondsToNextTick = s.nextTick > now ? s.nextTick - now : 0f,
            stackSecondsLeft = new float[kept],
            stackMagnitude = new float[kept],
            stackAmount = new int[kept],
        };
        int k = 0;
        for (int i = 0; i < s.stacks.Count; i++)
        {
            var st = s.stacks[i];
            // A delayed hit whose time has already run out is a bill still owed: it is kept, due the moment it is read
            // back. Anything else with no time left is simply over.
            if (!debt && st.endTime <= now) continue;
            rec.stackSecondsLeft[k] = Mathf.Max(0f, st.endTime - now);
            rec.stackMagnitude[k] = st.magnitude;
            rec.stackAmount[k] = st.intAmount;
            k++;
        }
        return rec;
    }

    /// <summary>A record for delayed damage that has already come due but was not yet dealt when the save was written
    /// — owed at once when it is read back, so logging out can never erase a debt.</summary>
    public static SaveData.TimedEffect DebtDueNow(int owed) => new SaveData.TimedEffect
    {
        kind = StateId.DeferredDamage.ToString(),
        sourceSkillId = "",
        stacking = true,
        description = "",
        stackSecondsLeft = new[] { 0f },
        stackMagnitude = new[] { 0f },
        stackAmount = new[] { owed },
    };

    /// <summary>Which state a record is. Saved BY NAME, not by number: members have been removed from that list
    /// before, and a removal shifts every number after it. False for a name the game no longer has.</summary>
    public static bool TryKind(SaveData.TimedEffect rec, out StateId id)
    {
        id = default;
        return rec != null && !string.IsNullOrEmpty(rec.kind) && Enum.TryParse(rec.kind, out id)
               && Enum.IsDefined(typeof(StateId), id);
    }

    /// <summary>Fill <paramref name="into"/> from <paramref name="rec"/>, resuming its clock at <paramref name="now"/>.
    /// Writes only the timing and the stacks — who it is and what it looks like are the caller's. False when the
    /// record holds nothing to put back.</summary>
    public static bool Restore(SaveData.TimedEffect rec, float now, UnitStates.State into)
    {
        if (rec == null || into == null || rec.stackSecondsLeft == null) return false;
        int n = rec.stackSecondsLeft.Length;
        if (rec.stackMagnitude == null || rec.stackMagnitude.Length < n) return false;
        if (rec.stackAmount == null || rec.stackAmount.Length < n) return false;
        if (n == 0) return false;

        into.stacking = rec.stacking;
        into.description = rec.description ?? "";
        into.stacks.Clear();
        for (int i = 0; i < n; i++)
            into.stacks.Add(new UnitStates.State.Stack
            {
                endTime = now + Mathf.Max(0f, rec.stackSecondsLeft[i]),
                magnitude = rec.stackMagnitude[i],
                intAmount = rec.stackAmount[i],
            });
        into.Refold();
        into.startTime = now - Mathf.Max(0f, rec.secondsRunning);
        into.nextTick = rec.secondsToNextTick > 0f ? now + rec.secondsToNextTick : 0f;
        return true;
    }
}
