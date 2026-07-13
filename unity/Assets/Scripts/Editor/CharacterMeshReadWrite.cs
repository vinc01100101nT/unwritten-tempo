using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Enables Read/Write on the rigged character FBX meshes so the in-game wardrobe can read each model's UV
/// footprint at runtime (<see cref="Mesh.uv"/> requires Read/Write). Without it, the wardrobe falls back to
/// scanning the whole atlas and can offer colors the mesh never actually wears. Idempotent — safe to re-run
/// after importing new models (skips ones already enabled).
/// </summary>
public static class CharacterMeshReadWrite
{
    [MenuItem("Tools/unwritten/Rigs/Enable Read-Write on Character Models", priority = 624)]
    public static void Enable()
    {
        int changed = 0, total = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { Player3DVisualBuilder.RiggedDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) continue;
            total++;
            if (!(AssetImporter.GetAtPath(path) is ModelImporter imp) || imp.isReadable) continue;
            imp.isReadable = true;
            imp.SaveAndReimport();
            changed++;
        }
        Debug.Log($"[unwritten] Read/Write enabled on {changed} of {total} character model(s) in " +
                  $"{Player3DVisualBuilder.RiggedDir} (the wardrobe now reads each model's real UV footprint).");
    }
}
