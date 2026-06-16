using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Editor helpers for the layered depth system (band layout: <see cref="DepthSortRuntime"/>).
/// Obstacle tilemaps are baked into per-object props automatically at Play by DepthSortRuntime —
/// there's no "convert" step to run. These two tools are just authoring conveniences:
///
///   • <b>Create Decor + Overhead Layers</b> — adds two NON-colliding tilemaps under the Grid:
///     "Decor" (under-foot, behind characters) and "Overhead" (walk-under, in front).
///   • <b>Refresh Depth Sorting</b> — re-bands tilemaps by name + gives in-scene units / monster
///     prefabs a YDepthSorter, so the editor Scene-view preview matches Play.
/// </summary>
public static class DepthSortTools
{
    [MenuItem("Tools/unwritten/Create Decor + Overhead Layers")]
    static void CreateDecorLayers()
    {
        var grid = Object.FindFirstObjectByType<Grid>();
        if (grid == null)
        {
            EditorUtility.DisplayDialog("Create Decor + Overhead Layers",
                "No Grid in the open scene. Open the scene with your tilemaps (e.g. Field), then run again.", "OK");
            return;
        }

        var decor = EnsureTilemap(grid, "Decor", DepthSortRuntime.DecorOrder);
        EnsureTilemap(grid, "Overhead", DepthSortRuntime.OverheadOrder);

        EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        Selection.activeGameObject = decor.gameObject;
        Debug.Log("[unwritten] Created/updated 'Decor' (under-foot, behind characters) and 'Overhead' " +
                  "(walk-under, in front) tilemaps — NO colliders, so painted tiles never block.");
    }

    static Tilemap EnsureTilemap(Grid grid, string name, int order)
    {
        foreach (var existing in grid.GetComponentsInChildren<Tilemap>(true))
            if (existing.name == name) { BandNoCollider(existing, order); return existing; }

        var go = new GameObject(name, typeof(Tilemap), typeof(TilemapRenderer));
        go.transform.SetParent(grid.transform, false);
        Undo.RegisterCreatedObjectUndo(go, "Create Decor + Overhead Layers");
        BandNoCollider(go.GetComponent<Tilemap>(), order);
        return go.GetComponent<Tilemap>();
    }

    static void BandNoCollider(Tilemap tm, int order)
    {
        var tr = tm.GetComponent<TilemapRenderer>();
        tr.sortingOrder = order;
        tr.mode = TilemapRenderer.Mode.Chunk;
        EditorUtility.SetDirty(tr);
        // Intentionally NO TilemapCollider2D — decoration only.
    }

    [MenuItem("Tools/unwritten/Refresh Depth Sorting")]
    static void Refresh()
    {
        int maps = 0, units = 0, prefabs = 0;

        foreach (var tm in Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
        {
            var tr = tm.GetComponent<TilemapRenderer>();
            if (tr == null) continue;
            Undo.RecordObject(tr, "Refresh Depth Sorting");
            DepthSortRuntime.BandTilemap(tm);
            EditorUtility.SetDirty(tr);
            maps++;
        }

        foreach (var sr in Object.FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
            if (DepthSortRuntime.IsUnit(sr) && !sr.TryGetComponent<YDepthSorter>(out _))
            {
                Undo.AddComponent<YDepthSorter>(sr.gameObject);
                units++;
            }

        if (AssetDatabase.IsValidFolder("Assets/Prefabs"))
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (go == null || go.GetComponentInChildren<MonsterAI>() == null) continue;
                var sr = go.GetComponentInChildren<SpriteRenderer>();
                if (sr == null || sr.GetComponent<YDepthSorter>() != null) continue;
                sr.gameObject.AddComponent<YDepthSorter>();
                EditorUtility.SetDirty(go);
                prefabs++;
            }
            AssetDatabase.SaveAssets();
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[unwritten] Refresh Depth Sorting ✓ — banded {maps} tilemap(s), sorters on {units} unit(s) + " +
                  $"{prefabs} prefab(s).");
    }
}
