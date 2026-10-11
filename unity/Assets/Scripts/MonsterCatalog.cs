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
    public static void Reload() { _all = null; _byId = null; MobInfo.ForgetLines(); }

    /// <summary>The creature with exactly this id (either case), or null — never a search by name.</summary>
    public static MonsterDef ById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        EnsureLoaded();
        return _byId.TryGetValue(id, out var d) ? d : null;
    }

    public static IReadOnlyList<MonsterDef> All { get { EnsureLoaded(); return _all; } }

    /// <summary>The highest level any creature in the catalog has — the top of the shadow ladder. 1 when the catalog
    /// is empty.</summary>
    public static int HighestLevel
    {
        get
        {
            EnsureLoaded();
            int top = 1;
            foreach (var d in _all) if (d != null && d.level > top) top = d.level;
            return top;
        }
    }

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

    /// <summary>A RANDOM creature of <paramref name="kind"/> (uniform among them), or null if the catalog has none.</summary>
    public static MonsterDef RandomOfKind(CreatureKind kind)
    {
        EnsureLoaded();
        int total = 0;
        foreach (var m in _all) if (m != null && m.kind == kind) total++;
        if (total == 0) return null;

        int roll = Random.Range(0, total);
        foreach (var m in _all)
            if (m != null && m.kind == kind && roll-- == 0) return m;
        return null;
    }

    /// <summary>Pick a field-spawnable mob (a Normal, weight &gt; 0) weighted by <see cref="MonsterDef.weight"/>,
    /// or null if none are loaded.</summary>
    public static MonsterDef WeightedPick()
    {
        EnsureLoaded();
        int total = 0;
        foreach (var m in _all) if (m != null && m.kind == CreatureKind.Normal) total += Mathf.Max(0, m.weight);
        if (total <= 0) return null;

        int roll = Random.Range(0, total);
        foreach (var m in _all)
        {
            if (m == null || m.kind != CreatureKind.Normal) continue;
            roll -= Mathf.Max(0, m.weight);
            if (roll < 0) return m;
        }
        return null;
    }

    /// <summary>Is this mob a field-spawnable candidate for a given biome + LEVEL band? Difficulty is level —
    /// there is no separate tier. Biome-agnostic commons (<see cref="Biome.None"/>) match any biome (when
    /// <paramref name="includeCommons"/>); everyone else must match <paramref name="biome"/> exactly. Only Normals are
    /// ever drawn into a mix: an elite or a shadow is placed by naming it.</summary>
    static bool Eligible(MonsterDef m, Biome biome, int minLevel, int maxLevel, bool includeCommons)
    {
        if (m == null || m.kind != CreatureKind.Normal || m.weight <= 0) return false;
        if (m.level < minLevel || m.level > maxLevel) return false;
        return m.biome == Biome.None ? includeCommons : m.biome == biome;
    }

    /// <summary>EVERY CREATURE A MIX CAN DRAW — the Normals that match <paramref name="biome"/> within the
    /// [<paramref name="minLevel"/>, <paramref name="maxLevel"/>] band (commons included when asked), or, when nothing
    /// matches, every Normal with a weight, so a map never spawns empty. The one rule for a mix: the server's pick
    /// (<see cref="WeightedPick(Biome,int,int,bool)"/>) and the map window's list of who lives where both read it.
    /// Fills <paramref name="into"/> in catalog order.</summary>
    public static void MixOf(Biome biome, int minLevel, int maxLevel, bool includeCommons, List<MonsterDef> into)
    {
        into.Clear();
        EnsureLoaded();
        foreach (var m in _all) if (Eligible(m, biome, minLevel, maxLevel, includeCommons)) into.Add(m);
        if (into.Count > 0) return;
        foreach (var m in _all) if (m != null && m.kind == CreatureKind.Normal && m.weight > 0) into.Add(m);
    }

    static readonly List<MonsterDef> mixScratch = new List<MonsterDef>();

    /// <summary>Weighted pick among <see cref="MixOf"/>. This is what a spawn area that draws a mix fills a slot
    /// with.</summary>
    public static MonsterDef WeightedPick(Biome biome, int minLevel, int maxLevel, bool includeCommons = true)
    {
        MixOf(biome, minLevel, maxLevel, includeCommons, mixScratch);
        int total = 0;
        foreach (var m in mixScratch) total += m.weight;
        if (total <= 0) return null;

        int roll = Random.Range(0, total);
        foreach (var m in mixScratch)
        {
            roll -= m.weight;
            if (roll < 0) return m;
        }
        return null;
    }
}
