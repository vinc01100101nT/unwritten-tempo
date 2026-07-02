using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player's ACTIVE-SKILL ownership — the class-agnostic progression layer. There is ONE shared pool of
/// active skills (<see cref="AllIds"/>); you UNLOCK one of your own choosing for every 10 points ALLOCATED into
/// the ring (not per character level), until you own them all (9 skills → all owned at 90 ring points). Ownership
/// is the gate for which skills appear in the skill menu and can be bound to the hotbar (the slot→skill binding
/// itself lives in <see cref="HotbarBindings"/>).
///
/// Pure data, owned by the ring vessel (<see cref="RingProgress.Skills"/>), persisted via
/// <see cref="Capture"/>/<see cref="Restore"/>. The number of unlock PICKS earned is DERIVED from points
/// allocated (<see cref="EarnedPicks"/>), so it needs no separate save field and is robust to older saves:
/// pending = earned − owned.
/// </summary>
public sealed class SkillRoster
{
    /// <summary>Every active skill in the shared pool, by stable id — now the COMPOSED skills authored in the
    /// Skill Composer (<see cref="SkillCatalog.OrderedIds"/>), which replaced the hand-written legacy skill
    /// classes. This is the menu/display order — NOT a forced unlock order; the player picks which to take each
    /// milestone. Derived live from the catalog, so authoring a new skill grows the pool with no code change.</summary>
    public static string[] AllIds => SkillCatalog.OrderedIds();

    /// <summary>One active-skill pick is earned for every this-many points ALLOCATED into the ring.</summary>
    public const int PointsPerUnlock = 10;

    readonly HashSet<string> owned = new HashSet<string>();

    public bool Owns(string id) => id != null && owned.Contains(id);
    public int OwnedCount => owned.Count;
    public IReadOnlyCollection<string> Owned => owned;

    public static bool IsKnownId(string id)
    {
        if (id == null) return false;
        for (int i = 0; i < AllIds.Length; i++) if (AllIds[i] == id) return true;
        return false;
    }

    /// <summary>Unlock picks EARNED for <paramref name="pointsAllocated"/> ring points — one per
    /// <see cref="PointsPerUnlock"/>, capped at the pool size.</summary>
    public static int EarnedPicks(int pointsAllocated) => Mathf.Clamp(pointsAllocated / PointsPerUnlock, 0, AllIds.Length);

    /// <summary>Unspent unlock picks = earned (from points allocated) − already owned (never negative).</summary>
    public int PendingPicks(int pointsAllocated) => Mathf.Max(0, EarnedPicks(pointsAllocated) - owned.Count);

    /// <summary>True when a fresh skill choice is available to spend at this allocation.</summary>
    public bool HasPendingPick(int pointsAllocated) => PendingPicks(pointsAllocated) > 0;

    /// <summary>Ring points still needed before the NEXT active-skill pick unlocks (0 when one is already
    /// pending or every skill is owned). Drives the "N/10 to next skill" readout.</summary>
    public int PointsToNextPick(int pointsAllocated)
    {
        if (owned.Count >= AllIds.Length) return 0;          // all owned — no more picks
        if (PendingPicks(pointsAllocated) > 0) return 0;     // a pick is already waiting
        return PointsPerUnlock - (pointsAllocated % PointsPerUnlock);
    }

    /// <summary>Take ownership of a still-locked skill (the caller gates on <see cref="HasPendingPick"/> —
    /// the UI only offers a choice when a pick is pending). Returns false if the id is unknown or already owned.</summary>
    public bool Unlock(string id)
    {
        if (!IsKnownId(id) || owned.Contains(id)) return false;
        owned.Add(id);
        return true;
    }

    /// <summary>Fill <paramref name="buffer"/> with the ids NOT yet owned (the choices offered at a milestone),
    /// in <see cref="AllIds"/> order. Zero-alloc against a caller-owned list.</summary>
    public void LockedIds(List<string> buffer)
    {
        buffer.Clear();
        for (int i = 0; i < AllIds.Length; i++) if (!owned.Contains(AllIds[i])) buffer.Add(AllIds[i]);
    }

    /// <summary>Fill <paramref name="buffer"/> with the owned ids, in <see cref="AllIds"/> order.</summary>
    public void OwnedIds(List<string> buffer)
    {
        buffer.Clear();
        for (int i = 0; i < AllIds.Length; i++) if (owned.Contains(AllIds[i])) buffer.Add(AllIds[i]);
    }

    // ---- save / load ----------------------------------------------------------
    public string[] Capture()
    {
        var arr = new string[owned.Count];
        owned.CopyTo(arr);
        return arr;
    }

    public void Restore(string[] ids)
    {
        owned.Clear();
        if (ids == null) return;
        for (int i = 0; i < ids.Length; i++) if (IsKnownId(ids[i])) owned.Add(ids[i]);
    }
}
