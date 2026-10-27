using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A SEAMLESS WORLD WITH ALL ITS GROUND IN, for editor work that needs the whole world at once — drawing its minimap,
/// or finding solid ground across it. The game builds a world's squares only near people (<see cref="SeamlessWorld"/>);
/// this builds every square into a hidden copy of the scene, so the scene open in the editor is never touched and
/// nothing is saved.
/// </summary>
public static class SeamlessWorldPreview
{
    /// <summary>Every chunk of the <see cref="SeamlessWorld"/> in <paramref name="scene"/> (a hidden copy), built where
    /// its prefab stands. False when the scene holds no seamless world. The colliders are synced, so the copy's own
    /// physics scene can be asked for the ground straight away.</summary>
    public static bool Fill(Scene scene, out SeamlessWorld world)
    {
        world = null;
        foreach (var root in scene.GetRootGameObjects())
            if ((world = root.GetComponentInChildren<SeamlessWorld>(true)) != null) break;
        if (world == null) return false;

        var paths = AddressPaths();
        int missing = 0;
        foreach (var chunk in world.chunks)
        {
            if (!paths.TryGetValue(chunk.address, out var path)) { missing++; continue; }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) { missing++; continue; }
            // editor-write-ok: built inside the hidden copy only; a preview scene is never saved.
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            go.hideFlags = HideFlags.DontSave;
        }
        if (missing > 0) Debug.LogWarning($"[seamless world] {missing} of {world.chunks.Count} chunks have no prefab behind their address.");
        Physics.SyncTransforms();
        return true;
    }

    // Address → prefab path, for every entry the project's address list holds.
    static Dictionary<string, string> AddressPaths()
    {
        var map = new Dictionary<string, string>();
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return map;
        foreach (var group in settings.groups)
        {
            if (group == null) continue;
            foreach (var e in group.entries) map[e.address] = e.AssetPath;
        }
        return map;
    }
}
