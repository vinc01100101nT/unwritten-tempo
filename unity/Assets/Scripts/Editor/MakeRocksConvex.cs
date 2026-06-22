using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// QUICKFIX 2026-06-21 — flips already-baked rock/stone prefab MeshColliders to convex (the concave hulls
// let the player capsule wedge & jitter). FbxPropExtractor now bakes rocks convex on its own, so this is
// only for prefabs baked BEFORE that change; delete after the Props folder has been re-split or verified.
public static class MakeRocksConvex
{
    [MenuItem("Tools/unwritten/Quick Fixes/Make Rocks & Stones Convex", priority = 9001)]
    static void Run()
    {
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Props" });
        int prefabs = 0, colliders = 0;

        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string name = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();

            // Rock/stone/boulder obstacles only — skip flat ground_* floor tiles.
            if (name.StartsWith("ground_")) continue;
            if (!(name.Contains("rock") || name.Contains("stone") || name.Contains("boulder"))) continue;

            var root = PrefabUtility.LoadPrefabContents(path);
            bool changed = false;
            foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
                if (!mc.convex) { mc.convex = true; colliders++; changed = true; }

            if (changed) { PrefabUtility.SaveAsPrefabAsset(root, path); prefabs++; }
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[unwritten] Made rocks convex: {colliders} colliders across {prefabs} prefabs.");
        EditorUtility.DisplayDialog("Make Rocks & Stones Convex",
            $"Converted {colliders} MeshCollider(s) to convex across {prefabs} prefab(s).\n\n" +
            "Placed instances update automatically (prefab links).", "OK");
    }
}
