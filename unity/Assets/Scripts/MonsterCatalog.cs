using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The live registry of mobs. Loads every <see cref="MonsterDef"/> under <c>Resources/Monsters</c> ONCE
/// and caches them, so quests, spawners and the map generator can resolve a mob by its stable
/// <see cref="MonsterDef.id"/> (or display name) without a hardcoded list. This replaces direct use of the
/// legacy <c>MobRoster</c> array as the source of truth.
///
/// Loading is lazy + cached (no per-frame work); call <see cref="Reload"/> after creating/editing assets in
/// the editor. If no assets exist yet (migration not run), <see cref="Count"/> is 0 and callers fall back to
/// <c>MobRoster</c>.
/// </summary>
public static class MonsterCatalog
{
    const string ResourceDir = "Monsters";

    static MonsterDef[] _all;
    static Dictionary<string, MonsterDef> _byId;

    static void EnsureLoaded()
    {
        if (_all != null) return;
        _all = Resources.LoadAll<MonsterDef>(ResourceDir) ?? System.Array.Empty<MonsterDef>();
        _byId = new Dictionary<string, MonsterDef>(_all.Length, System.StringComparer.OrdinalIgnoreCase);
        foreach (var d in _all)
        {
            if (d == null || string.IsNullOrWhiteSpace(d.id)) continue;
            _byId[d.id] = d;
        }
    }

    /// <summary>Drop the cache so the next access reloads from Resources (use after creating/editing assets).</summary>
    public static void Reload() { _all = null; _byId = null; }

    public static IReadOnlyList<MonsterDef> All { get { EnsureLoaded(); return _all; } }

    /// <summary>How many mob assets are loaded. 0 = migration not run yet → callers should fall back to MobRoster.</summary>
    public static int Count { get { EnsureLoaded(); return _all.Length; } }

    /// <summary>Resolve a mob by stable id first, then by display name (both case-insensitive). Null if unknown.</summary>
    public static MonsterDef Get(string idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName)) return null;
        EnsureLoaded();
        if (_byId.TryGetValue(idOrName, out var d)) return d;
        foreach (var m in _all)
            if (m != null && (string.Equals(m.displayName, idOrName, System.StringComparison.OrdinalIgnoreCase)
                              || string.Equals(m.name, idOrName, System.StringComparison.OrdinalIgnoreCase)))
                return m;
        return null;
    }

    /// <summary>The first boss-flagged mob (field MVP), or null if none.</summary>
    public static MonsterDef Boss
    {
        get
        {
            EnsureLoaded();
            foreach (var m in _all) if (m != null && m.isBoss) return m;
            return null;
        }
    }

    /// <summary>A RANDOM boss-flagged mob (uniform among all bosses), or null if the catalog has none.
    /// Unlike <see cref="Boss"/> (always the first) this varies the pick, so /summon boss isn't always the
    /// same MVP.</summary>
    public static MonsterDef RandomBoss()
    {
        EnsureLoaded();
        int total = 0;
        foreach (var m in _all) if (m != null && m.isBoss) total++;
        if (total == 0) return null;

        int roll = Random.Range(0, total);
        foreach (var m in _all)
            if (m != null && m.isBoss && roll-- == 0) return m;
        return null;
    }

    /// <summary>Pick a field-spawnable mob (non-boss, weight &gt; 0) weighted by <see cref="MonsterDef.weight"/>,
    /// or null if none are loaded.</summary>
    public static MonsterDef WeightedPick()
    {
        EnsureLoaded();
        int total = 0;
        foreach (var m in _all) if (m != null && !m.isBoss) total += Mathf.Max(0, m.weight);
        if (total <= 0) return null;

        int roll = Random.Range(0, total);
        foreach (var m in _all)
        {
            if (m == null || m.isBoss) continue;
            roll -= Mathf.Max(0, m.weight);
            if (roll < 0) return m;
        }
        return null;
    }

    /// <summary>Is this mob a field-spawnable candidate for a given biome + tier band? Biome-agnostic
    /// commons (<see cref="Biome.None"/>) match any biome (when <paramref name="includeCommons"/>);
    /// everyone else must match <paramref name="biome"/> exactly. Bosses and zero-weight mobs are never
    /// field candidates.</summary>
    static bool Eligible(MonsterDef m, Biome biome, int minTier, int maxTier, bool includeCommons)
    {
        if (m == null || m.isBoss || m.weight <= 0) return false;
        if (m.tier < minTier || m.tier > maxTier) return false;
        return m.biome == Biome.None ? includeCommons : m.biome == biome;
    }

    /// <summary>Weighted pick among the non-boss mobs that match <paramref name="biome"/> within the
    /// [<paramref name="minTier"/>, <paramref name="maxTier"/>] band (commons included when asked). This is
    /// what the map generator uses to populate a biome's maps. Falls back to the unfiltered
    /// <see cref="WeightedPick()"/> when nothing matches, so a map never spawns empty.</summary>
    public static MonsterDef WeightedPick(Biome biome, int minTier, int maxTier, bool includeCommons = true)
    {
        EnsureLoaded();
        int total = 0;
        foreach (var m in _all) if (Eligible(m, biome, minTier, maxTier, includeCommons)) total += m.weight;
        if (total <= 0) return WeightedPick();

        int roll = Random.Range(0, total);
        foreach (var m in _all)
        {
            if (!Eligible(m, biome, minTier, maxTier, includeCommons)) continue;
            roll -= m.weight;
            if (roll < 0) return m;
        }
        return null;
    }
}
