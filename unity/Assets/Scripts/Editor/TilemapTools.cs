using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Cleanup helper: wipe every painted cell off a Tilemap. Handy after deleting
/// the Tile assets / Palette a map was painted with — the orphaned cells render
/// as magenta "missing tile" placeholders until they're cleared.
///
/// Select one or more Tilemap GameObjects to clear just those; select nothing
/// (Tilemap-wise) to clear ALL Tilemaps in the open scene. It only erases tiles —
/// the Tilemap GameObjects, their colliders, and your Grid are left intact — and
/// it's undoable with Ctrl+Z.
///
/// Menu: Tools ▸ unwritten ▸ Clear Tilemaps.
/// </summary>
public static class TilemapTools
{
    [MenuItem("Tools/unwritten/Clear Tilemaps")]
    static void ClearTilemaps()
    {
        // Prefer Tilemaps among the current selection; otherwise target them all.
        var selected = Selection.gameObjects
            .Select(g => g.GetComponent<Tilemap>())
            .Where(t => t != null)
            .Distinct()
            .ToArray();

        var targets = selected.Length > 0
            ? selected
            : Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None);

        if (targets.Length == 0)
        {
            EditorUtility.DisplayDialog("Clear Tilemaps",
                "No Tilemaps found in the open scene.", "OK");
            return;
        }

        string scope = selected.Length > 0 ? "the selected" : "ALL";
        string names = string.Join("\n• ", targets.Select(t => t.name));
        bool ok = EditorUtility.DisplayDialog("Clear Tilemaps",
            $"Erase every painted tile from {scope} Tilemap(s)?\n\n• {names}\n\n" +
            "(Tiles only — GameObjects/colliders are kept. Undo with Ctrl+Z.)",
            "Clear", "Cancel");
        if (!ok) return;

        foreach (var t in targets)
        {
            Undo.RegisterCompleteObjectUndo(t, "Clear Tilemaps");
            t.ClearAllTiles();
            EditorUtility.SetDirty(t);
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log($"[unwritten] Cleared {targets.Length} Tilemap(s): " +
                  string.Join(", ", targets.Select(t => t.name)) + ".");
    }
}
