using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The live registry of particle effects. Loads the single <see cref="EffectLibrary"/> asset from
/// <c>Resources/EffectLibrary.asset</c> ONCE and caches it, so gameplay code can resolve an effect by its
/// stable <see cref="EffectDef.id"/> without a hardcoded prefab reference. Mirrors <see cref="MonsterCatalog"/>.
///
/// Spawn effects through <see cref="Vfx"/> (pooled) — this class only resolves ids. Loading is lazy + cached
/// (no per-frame work); call <see cref="Reload"/> after rebuilding the library asset in the editor.
/// </summary>
public static class EffectCatalog
{
    const string ResourcePath = "EffectLibrary";

    static EffectLibrary _lib;
    static Dictionary<string, EffectDef> _byId;

    static void EnsureLoaded()
    {
        if (_byId != null) return;
        _lib = Resources.Load<EffectLibrary>(ResourcePath);
        _byId = new Dictionary<string, EffectDef>(
            _lib != null ? _lib.effects.Count : 0, System.StringComparer.OrdinalIgnoreCase);
        if (_lib == null) return;
        foreach (var e in _lib.effects)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.id) || e.prefab == null) continue;
            _byId[e.id] = e;
        }
    }

    /// <summary>Drop the cache so the next access reloads from Resources (use after rebuilding the library).</summary>
    public static void Reload() { _lib = null; _byId = null; }

    /// <summary>How many effects are registered. 0 = library missing/empty (run Rebuild Effect Library).</summary>
    public static int Count { get { EnsureLoaded(); return _byId.Count; } }

    /// <summary>All registered effect defs (for tools/tuners that enumerate — not a hot path). Empty if the
    /// library is missing.</summary>
    public static IReadOnlyList<EffectDef> All
    {
        get { EnsureLoaded(); return _lib != null ? _lib.effects : (IReadOnlyList<EffectDef>)System.Array.Empty<EffectDef>(); }
    }

    /// <summary>Resolve an effect by stable id (case-insensitive). Null if unknown.</summary>
    public static EffectDef Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        EnsureLoaded();
        return _byId.TryGetValue(id, out var d) ? d : null;
    }

    /// <summary>Does an effect with this id exist?</summary>
    public static bool Has(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        EnsureLoaded();
        return _byId.ContainsKey(id);
    }
}
