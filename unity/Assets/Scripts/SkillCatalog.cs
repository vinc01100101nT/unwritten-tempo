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

    /// <summary>All authored skills (unordered). Never null.</summary>
    public static IReadOnlyList<SkillDef> All { get { EnsureLoaded(); return all; } }

    /// <summary>Drop the cache so a freshly built/edited asset is picked up (editor tools call this).</summary>
    public static void Reload() { byId = null; all = null; }
}
