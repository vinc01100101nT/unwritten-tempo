using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Rebuilds each tree's collision so ONLY the bark (trunk + branches) is solid and the leaves are
/// jump/walk-through. The Pxltgr trees are a single mesh split into two sub-parts by material —
/// sub-part 0 is bark, sub-part 1 is leaves — so unlike the fused Trellis trees (see
/// <see cref="TreeColliderBaker"/>, which has to guess bark-vs-leaf by texture colour) we can just
/// keep the bark sub-part's triangles and drop the rest. No eyedropping, no tuning dial.
///
/// It bakes a <c>&lt;mesh&gt;_bark.mesh</c> collision mesh next to the tree prefabs and points each
/// tree's MeshCollider at it. Idempotent — re-run any time (it overwrites the baked mesh in place).
/// </summary>
public static class TreeBarkColliderBaker
{
    const string TreesFolder = "Assets/Prefabs/Props/Trees";
    const int BarkSubMesh = 0;   // material slot 0 = bark, slot 1 = leaves (consistent across the Pxltgr set)

    [MenuItem("Tools/unwritten/Props/Bake Bark-Only Tree Colliders", priority = 508)]
    static void BakeAll()
    {
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { TreesFolder });
        int done = 0, skipped = 0;
        try
        {
            foreach (var guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (BakeOne(root)) done++; else skipped++;
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
        }
        finally { AssetDatabase.Refresh(); }

        Debug.Log($"[unwritten] Bark-only tree colliders: baked {done}, skipped {skipped} (no 2-part mesh/collider) of {guids.Length} prefab(s) in {TreesFolder}.");
    }

    // Returns true if this prefab had a tree collider rebuilt.
    static bool BakeOne(GameObject root)
    {
        bool any = false;
        foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
        {
            var mf = mc.GetComponent<MeshFilter>();
            var src = mf != null ? mf.sharedMesh : null;
            if (src == null || src.subMeshCount < 2) continue;   // need a leaf sub-part to strip

            var barkMesh = BuildBarkMesh(src);
            string assetPath = $"{TreesFolder}/Colliders/{src.name}_bark.mesh";
            Directory.CreateDirectory($"{TreesFolder}/Colliders");

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existing != null) { existing.Clear(); EditorUtility.CopySerialized(barkMesh, existing); barkMesh = existing; }
            else AssetDatabase.CreateAsset(barkMesh, assetPath);

            mc.sharedMesh = barkMesh;
            mc.convex = false;
            any = true;
        }
        return any;
    }

    // Keep the full vertex array (cheap; unused verts don't cook into the collider) and only the bark triangles.
    static Mesh BuildBarkMesh(Mesh src)
    {
        var m = new Mesh { name = src.name + "_bark", indexFormat = src.indexFormat };
        m.vertices = src.vertices;
        m.SetTriangles(src.GetTriangles(BarkSubMesh), 0);
        m.RecalculateBounds();
        return m;
    }
}
