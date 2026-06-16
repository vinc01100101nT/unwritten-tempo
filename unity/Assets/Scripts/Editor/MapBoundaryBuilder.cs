using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// One-click invisible wall around the map so the player can't walk off the edge.
/// It reads the world bounds of the "Ground" tilemap (or the selected tilemap) and
/// builds a frame of four thin <see cref="BoxCollider2D"/> walls (sitting just OUTSIDE
/// the painted area, inner face flush with the edge) on a "MapBoundary" object.
///
/// Why four boxes and NOT one EdgeCollider2D loop: Unity's built-in EdgeCollider2D EDIT
/// tool caches the collider's point list whenever the selection changes, and if that
/// collider is destroyed underneath it — a re-run here, or a script-recompile domain
/// reload while it happens to be selected — it logs a harmless-but-annoying
/// "MissingReferenceException: EdgeCollider2D … has been destroyed". BoxCollider2D is
/// edited by a different tool that doesn't path-cache, so that false alarm cannot occur:
/// the project no longer contains an EdgeCollider2D for that tool to touch.
///
/// Re-running rebuilds it, so it's safe to run again after you paint a bigger field.
/// </summary>
public static class MapBoundaryBuilder
{
    const float Thickness = 1f;   // wall thickness in world units (1 tile at PPU 16)

    [MenuItem("Tools/unwritten/Build Map Boundary from Ground")]
    static void BuildBoundary()
    {
        // Prefer a tilemap named "Ground"; otherwise use the current selection.
        Tilemap ground = null;
        foreach (var tm in Object.FindObjectsByType<Tilemap>(FindObjectsSortMode.None))
            if (tm.name == "Ground") { ground = tm; break; }
        if (ground == null && Selection.activeGameObject != null)
            ground = Selection.activeGameObject.GetComponent<Tilemap>();

        if (ground == null)
        {
            EditorUtility.DisplayDialog("Build Map Boundary",
                "Couldn't find a tilemap named \"Ground\". Select your ground tilemap in the Hierarchy and run this again.",
                "OK");
            return;
        }

        // World-space rectangle the painted ground covers.
        Bounds b = ground.localBounds;
        Vector3 min = ground.transform.TransformPoint(b.min);
        Vector3 max = ground.transform.TransformPoint(b.max);

        var old = GameObject.Find("MapBoundary");
        if (old != null)
        {
            // Belt-and-braces: never destroy it while it's the active selection, so no collider
            // edit tool is ever caching it at the moment it disappears.
            if (Selection.activeGameObject == old) Selection.activeGameObject = null;
            Undo.DestroyObjectImmediate(old);
        }

        var go = new GameObject("MapBoundary");   // sits at world origin, so offset == world position

        float w = max.x - min.x, h = max.y - min.y;
        float cx = (min.x + max.x) * 0.5f, cy = (min.y + max.y) * 0.5f;
        float t = Thickness, half = t * 0.5f;

        // Four thin walls just OUTSIDE the painted area (inner face flush with the edge), so the
        // whole map stays walkable and the player stops right at the boundary. Top/bottom span the
        // corners (w + 2t) so there are no gaps where the walls meet.
        AddWall(go, new Vector2(cx,           max.y + half), new Vector2(w + 2f * t, t));  // top
        AddWall(go, new Vector2(cx,           min.y - half), new Vector2(w + 2f * t, t));  // bottom
        AddWall(go, new Vector2(min.x - half, cy),           new Vector2(t, h));           // left
        AddWall(go, new Vector2(max.x + half, cy),           new Vector2(t, h));           // right

        Undo.RegisterCreatedObjectUndo(go, "Build Map Boundary");
        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        Debug.Log($"[unwritten] Built MapBoundary around '{ground.name}' " +
                  $"({min.x:0.#},{min.y:0.#}) → ({max.x:0.#},{max.y:0.#}) — 4 box walls (no EdgeCollider2D).");
    }

    static void AddWall(GameObject host, Vector2 center, Vector2 size)
    {
        var box = host.AddComponent<BoxCollider2D>();
        box.offset = center;
        box.size = size;
    }
}
