using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Load-once cache of every <see cref="SkillDef"/> under <c>Resources/Skills/</c>, keyed by stable
/// <see cref="SkillDef.id"/> — the game's front door to data-authored skills (mirrors <c>MonsterCatalog</c>).
/// The Skill Composer writes the assets; the game reads them here so nothing hardcodes a skill list.
/// </summary>
public static class SkillCatalog
{
    const string Dir = "Skills";   // Resources/Skills
    static Dictionary<string, SkillDef> byId;
    static SkillDef[] all;
    static string[] orderedIds;

    static void EnsureLoaded()
    {
        if (byId != null) return;
        all = Resources.LoadAll<SkillDef>(Dir);
        byId = new Dictionary<string, SkillDef>(all.Length);
        foreach (var s in all)
        {
            if (s == null || string.IsNullOrEmpty(s.id)) continue;
            byId[s.id] = s;   // last wins on a duplicate id (author error; harmless)
        }
    }

    /// <summary>The skill with this stable id, or null if none is authored.</summary>
    public static SkillDef Get(string id)
    {
        EnsureLoaded();
        return id != null && byId.TryGetValue(id, out var s) ? s : null;
    }

    /// <summary>True if <paramref name="id"/> is a known composed skill (exists in the catalog). Replaces the old
    /// SkillRoster.IsKnownId now that skills live on weapons, not a ring-owned roster.</summary>
    public static bool IsKnown(string id) => Get(id) != null;

    /// <summary>All authored skills (unordered). Never null.</summary>
    public static IReadOnlyList<SkillDef> All { get { EnsureLoaded(); return all; } }

    /// <summary>Every composed skill's stable id, ordered by <see cref="SkillDef.slotOrder"/> then id — the
    /// canonical menu/roster order; composed skills ARE the skill pool.
    /// Cached; rebuilt on <see cref="Reload"/>. Never null (empty when nothing is authored).</summary>
    public static string[] OrderedIds()
    {
        EnsureLoaded();
        if (orderedIds != null) return orderedIds;
        var list = new List<SkillDef>(all.Length);
        foreach (var s in all)
            if (s != null && !string.IsNullOrEmpty(s.id)) list.Add(s);
        list.Sort((a, b) => a.slotOrder != b.slotOrder ? a.slotOrder.CompareTo(b.slotOrder)
                                                       : string.CompareOrdinal(a.id, b.id));
        orderedIds = new string[list.Count];
        for (int i = 0; i < list.Count; i++) orderedIds[i] = list[i].id;
        return orderedIds;
    }

    /// <summary>Drop the cache so a freshly built/edited asset is picked up (editor tools call this).</summary>
    public static void Reload() { byId = null; all = null; orderedIds = null; }
}
