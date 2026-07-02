using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime lookup from a stable skill id to the live <see cref="IHotbarSkill"/> component on the player.
/// The class-agnostic active pool all lives on the <see cref="Player3DController"/> (see its self-bootstrap),
/// so this maps "skill.meteor" → the <see cref="SkillRunner"/> playing that composed <see cref="SkillDef"/>:
/// the hotbar, the key router, and the skill menu fetch a skill's icon/label/tint and fire it without knowing
/// concrete types. The id is the composed skill's <see cref="SkillDef.id"/> (a legacy IModifiableSkill still
/// resolves by its <see cref="IModifiableSkill.SkillModId"/> if present). Cached; <see cref="Refresh"/> on
/// player rebuild.
/// </summary>
public static class PlayerSkills
{
    static Player3DController player;
    static readonly Dictionary<string, IHotbarSkill> byId = new Dictionary<string, IHotbarSkill>();

    /// <summary>Drop the cache (call when the player is (re)built so the next lookup re-resolves).</summary>
    public static void Refresh() { player = null; byId.Clear(); }

    static void EnsureCache()
    {
        if (player == null) byId.Clear();
        if (player == null) player = Object.FindFirstObjectByType<Player3DController>();
        if (player == null || byId.Count > 0) return;

        var comps = player.GetComponents<IHotbarSkill>();
        for (int i = 0; i < comps.Length; i++)
        {
            // Composed skills (the data-driven pool) key off their SkillDef id; the legacy IModifiableSkill
            // path is kept so any still-present hand-written active resolves too (harmless once unwired).
            if (comps[i] is SkillRunner sr)
            {
                if (sr.def != null && !string.IsNullOrEmpty(sr.def.id)) byId[sr.def.id] = comps[i];
            }
            else if (comps[i] is IModifiableSkill m && !string.IsNullOrEmpty(m.SkillModId))
                byId[m.SkillModId] = comps[i];
        }
    }

    /// <summary>The live skill component for <paramref name="id"/>, or null. Re-resolves if the cached one
    /// was destroyed (e.g. a scene rebuilt the player).</summary>
    public static IHotbarSkill ById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        EnsureCache();
        if (byId.TryGetValue(id, out var s))
        {
            if (s is Object o && o != null) return s;
            Refresh();                       // cached component is dead → rebuild and retry once
            EnsureCache();
            return byId.TryGetValue(id, out var s2) && s2 is Object o2 && o2 != null ? s2 : null;
        }
        return null;
    }

    /// <summary>True once the player + its skill components are resolvable.</summary>
    public static bool Ready { get { EnsureCache(); return byId.Count > 0; } }
}
