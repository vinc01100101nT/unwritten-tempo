using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Central prop-prefab resolver: looks up a prop prefab BY NAME anywhere under <c>Assets/1_Authored/Prefabs/Props</c>
/// — recursively, across subfolders like <c>Trees/</c>, <c>Plants/</c>, <c>Indoor/</c>, <c>Outdoor/</c> —
/// instead of assuming it sits at the flat top level (<c>Props/&lt;name&gt;.prefab</c>).
///
/// WHY this exists: many author-time tools (recipe generator, map-painter import, brush-palette footprint
/// export, prop lighting, ground blend) load a prop from just its name. Each used to hardcode the flat path,
/// so the moment a prefab was organized into a subfolder that lookup silently returned null. This is the ONE
/// choke point they all go through now, so props can be organized into any folder structure without breaking
/// name-based loading. (Scene/recipe references use asset ids and were never affected.)
/// </summary>
public static class PropResolver
{
    public const string PropsDir = "Assets/1_Authored/Prefabs/Props";

    static Dictionary<string, string> _pathByName;   // prop name -> asset path (cached; Invalidate() after moves)

    /// <summary>Resolve a prop NAME to its asset path (recursive under Props). Null if not found.</summary>
    public static string PathFor(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return Map().TryGetValue(name, out var p) ? p : null;
    }

    /// <summary>Load a prop prefab BY NAME from anywhere under Props. Null if not found.</summary>
    public static GameObject Load(string name)
    {
        var path = PathFor(name);
        var go = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        // Cache can go stale if a prefab was moved this session — rebuild once and retry before giving up.
        if (go == null && _pathByName != null)
        {
            Invalidate();
            path = PathFor(name);
            go = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        return go;
    }

    /// <summary>Drop the cached name→path map (call after moving or adding prop prefabs).</summary>
    public static void Invalidate() => _pathByName = null;

    static Dictionary<string, string> Map()
    {
        if (_pathByName != null) return _pathByName;
        var map = new Dictionary<string, string>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PropsDir }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var n = Path.GetFileNameWithoutExtension(path);
            if (!map.ContainsKey(n)) map[n] = path;   // first found wins if a name is duplicated across folders
        }
        _pathByName = map;
        return map;
    }
}
